using BepInEx;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ShieldShare
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    internal class ShieldShare : BaseUnityPlugin
    {
        public const string PluginGUID = "com.jotunn.ShieldShare";
        public const string PluginName = "ShieldShare";
        public const string PluginVersion = "0.0.2";
        private const string ItemPrefabPrefix = "ShieldShare_";
        private const string DropFolderName = "Valheim Custom Shields";

        // Use this class to add your own localization to the game
        // https://valheim-modding.github.io/Jotunn/tutorials/localization.html
        public static CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();

        /// <summary>Optional per-pack images for the base material, keyed by file name (no extension).</summary>
        private static readonly Dictionary<string, string> LayerFileToShaderProperty = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "MainTex", "_MainTex" },
            { "BumpMap", "_BumpMap" },
            { "EmissionMap", "_EmissionMap" },
            { "MetallicGlossMap", "_MetallicGlossMap" },
        };

        /// <summary>Layers that hold data rather than colour and must be loaded as linear textures.</summary>
        private static readonly HashSet<string> LinearLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BumpMap", "MetallicGlossMap" };

        private const int MinCellSize = 256;
        private const int MaxCellSize = 1024;
        private const int IconSize = 128;
        private static readonly Color32 IconWood = new Color32(128, 92, 58, 255);
        private static readonly Color32 IconEdge = new Color32(48, 34, 22, 255);

        /// <summary>Mesh analysis per base prefab name - every pack on the same base shares it.</summary>
        private readonly Dictionary<string, FrontProjection> projections = new Dictionary<string, FrontProjection>(StringComparer.OrdinalIgnoreCase);

        private void Awake()
        {
            // Jotunn comes with its own Logger class to provide a consistent Log style for all mods using it
            Jotunn.Logger.LogInfo("ShieldShare has landed");
            PrefabManager.OnVanillaPrefabsAvailable += LoadAndRegisterShields;
        }

        private static string GetShieldFolderPath()
        {
            string path = Path.Combine(BepInEx.Paths.ConfigPath, PluginName, "Shields");
            Directory.CreateDirectory(path);
            return path;
        }

        private static string GetShieldDropFolderPath()
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string dropFolder = Path.Combine(documents, DropFolderName);
            Directory.CreateDirectory(dropFolder);
            return dropFolder;
        }

        private void LoadAndRegisterShields()
        {
            // The event is raised from ObjectDB.CopyOtherDB, which can run more than once per session.
            // Items must only be registered once.
            PrefabManager.OnVanillaPrefabsAvailable -= LoadAndRegisterShields;

            string shieldsFolder = GetShieldFolderPath();
            string dropFolder = GetShieldDropFolderPath();

            TryWriteAuthorGuide(dropFolder);

            try
            {
                ShieldPackSync.Sync(dropFolder, shieldsFolder);
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogError($"[ShieldShare] Syncing from '{dropFolder}' failed: {ex}");
            }

            var shieldFolders = Directory.GetDirectories(shieldsFolder).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
            Jotunn.Logger.LogInfo($"[ShieldShare] Found {shieldFolders.Length} shield folder(s) in {shieldsFolder}");

            foreach (var folder in shieldFolders)
            {
                try
                {
                    RegisterShield(ShieldPack.Load(folder));
                }
                catch (Exception ex)
                {
                    // One broken pack must never stop the others from loading.
                    Jotunn.Logger.LogError($"[ShieldShare] Shield '{Path.GetFileName(folder)}' failed to load: {ex}");
                }
            }

            TryWriteTemplates(dropFolder);
        }

        private void RegisterShield(ShieldPack pack)
        {
            foreach (var warning in pack.Warnings)
                Jotunn.Logger.LogWarning($"[ShieldShare] '{pack.Name}': {warning}");
            if (pack.Problem != null)
            {
                Jotunn.Logger.LogError($"[ShieldShare] Skipping '{pack.Name}': {pack.Problem}.");
                return;
            }
            var def = pack.Definition;

            string prefabName = ItemPrefabPrefix + ShieldPackSync.SafeFolderName(pack.Name);
            if (PrefabManager.Instance.GetPrefab(prefabName) != null)
            {
                Jotunn.Logger.LogWarning($"[ShieldShare] A shield called '{prefabName}' is already registered - skipping '{pack.Folder}'.");
                return;
            }

            var shieldPrefab = PrefabManager.Instance.CreateClonedPrefab(prefabName, def.BasePrefab);
            if (shieldPrefab == null)
            {
                Jotunn.Logger.LogError($"[ShieldShare] '{pack.Name}': there is no vanilla item called '{def.BasePrefab}' (check \"basePrefab\" in shield.json).");
                return;
            }

            var itemDrop = shieldPrefab.GetComponent<ItemDrop>();
            if (itemDrop == null)
            {
                Jotunn.Logger.LogError($"[ShieldShare] '{pack.Name}': '{def.BasePrefab}' is not an item, so it can't be used as a shield base.");
                UnityEngine.Object.Destroy(shieldPrefab);
                return;
            }

            // The clone still shares the vanilla materials. Copy them before changing anything, or we'd
            // repaint every vanilla shield in the game too.
            CopyMaterials(shieldPrefab);
            ApplyTextureLayers(pack, shieldPrefab);

            var model = FindModel(shieldPrefab);
            FrontProjection projection = model != null ? GetProjection(def.BasePrefab, model) : null;

            Texture2D styleTex = null;
            Sprite[] icons = null;

            if (pack.PatternPaths.Count > 0)
            {
                if (projection == null || projection.Front.Count == 0)
                    Jotunn.Logger.LogError($"[ShieldShare] '{pack.Name}': can't work out the front face of '{def.BasePrefab}', so its patterns can't be applied.");
                else
                    styleTex = BuildStyles(pack, def.BasePrefab, projection, out icons);
            }

            if (styleTex == null)
            {
                // No styles: switch the base shield's own paint styles off and show one icon.
                DisableStyles(shieldPrefab, itemDrop);
                icons = null;
                var single = TextureIO.Load(pack.SingleIconPath, mipmaps: false);
                if (single != null)
                    icons = new[] { TextureIO.ToSprite(single) };
                // otherwise keep the base shield's own icon
            }

            var itemConfig = new ItemConfig
            {
                Name = def.DisplayName,
                Description = def.Description,
                CraftingStation = def.CraftingStation,
                MinStationLevel = def.MinStationLevel,
                Icons = icons,
                StyleTex = styleTex,
                Enabled = !def.Hidden,
                Requirements = def.Requirements
                    .Select(r => new RequirementConfig(r.Item, r.Amount, r.AmountPerLevel, true))
                    .ToArray()
            };

            ItemManager.Instance.AddItem(new CustomItem(shieldPrefab, false, itemConfig));

            var shared = itemDrop.m_itemData.m_shared;
            if (styleTex != null && icons != null)
                shared.m_variants = icons.Length; // Jötunn's FixVariants sets this too, later; set it now for consistency

            Jotunn.Logger.LogInfo($"[ShieldShare] Registered '{def.DisplayName}' from '{pack.Name}' " +
                                  $"(base {def.BasePrefab}, {(styleTex != null ? icons.Length : 0)} style(s)).");
        }

        // ------------------------------------------------------------------------------------------
        // Styles: bake each flat PatternN image into the base shield's own UV layout
        // ------------------------------------------------------------------------------------------

        /// <summary>
        ///     Builds the 4x4 style atlas and one icon per style.
        ///     The shader looks each style up through the mesh's ORIGINAL UVs, and the back, rim and
        ///     strap share that UV space with the face. So instead of moving UVs we paint each pattern
        ///     only where the face's UVs point, and leave the rest of the cell transparent - exactly
        ///     how the vanilla style atlas is made.
        /// </summary>
        private Texture2D BuildStyles(ShieldPack pack, string baseName, FrontProjection projection, out Sprite[] icons)
        {
            int count = pack.PatternPaths.Count;
            var iconList = new Sprite[count];
            bool[] iconMask = null;

            string overridePath = pack.FindImage("StyleTex");
            Texture2D handBuilt = overridePath != null ? TextureIO.Load(overridePath) : null;
            if (handBuilt != null)
                Jotunn.Logger.LogInfo($"[ShieldShare] '{pack.Name}': using hand-made StyleTex image instead of baking the patterns.");

            int cellSize = 0;
            Color32[] atlas = null;
            int atlasSize = 0;
            byte[] paintMask = null;
            int maskBefore = 0, maskAfter = 0;

            for (int i = 0; i < count; i++)
            {
                var pattern = TextureIO.Load(pack.PatternPaths[i], mipmaps: false);
                if (pattern == null)
                {
                    Jotunn.Logger.LogWarning($"[ShieldShare] '{pack.Name}': '{Path.GetFileName(pack.PatternPaths[i])}' could not be loaded - style {i + 1} will be blank.");
                    iconList[i] = LoadOrNull(pack.IconPaths[i]);
                    continue;
                }

                var patternPixels = pattern.GetPixels32();

                if (handBuilt == null)
                {
                    if (atlas == null)
                    {
                        cellSize = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(pattern.width, pattern.height)), MinCellSize, MaxCellSize);
                        atlasSize = cellSize * 4;
                        atlas = new Color32[atlasSize * atlasSize]; // all transparent
                        paintMask = GetVanillaPaintMask(baseName, atlasSize);
                    }

                    int col = i % 4, row = i / 4; // row 0 = bottom, matches the in-game style order
                    StyleBaker.BakeCell(projection, patternPixels, pattern.width, pattern.height,
                        atlas, atlasSize, col * cellSize, row * cellSize, cellSize);
                    if (paintMask != null)
                    {
                        var counts = StyleBaker.ApplyMask(atlas, atlasSize, col * cellSize, row * cellSize, cellSize, paintMask);
                        maskBefore += counts.Key;
                        maskAfter += counts.Value;
                    }
                }

                iconList[i] = LoadOrNull(pack.IconPaths[i]);
                if (iconList[i] == null)
                {
                    if (iconMask == null)
                        iconMask = StyleBaker.FrontMask(projection, IconSize);
                    iconList[i] = MakeIcon(patternPixels, pattern.width, pattern.height, iconMask);
                }

                UnityEngine.Object.Destroy(pattern);
            }

            // Every style needs an icon, or the style picker breaks.
            for (int i = 0; i < count; i++)
            {
                if (iconList[i] == null)
                {
                    if (iconMask == null)
                        iconMask = StyleBaker.FrontMask(projection, IconSize);
                    iconList[i] = MakeIcon(new[] { new Color32(0, 0, 0, 0) }, 1, 1, iconMask);
                }
            }
            icons = iconList;

            if (handBuilt != null)
            {
                handBuilt.wrapMode = TextureWrapMode.Clamp;
                return handBuilt;
            }
            if (atlas == null)
                return null;

            if (paintMask != null && maskBefore > 0)
                Jotunn.Logger.LogInfo($"[ShieldShare] '{pack.Name}': masked to the vanilla paint area - {Mathf.RoundToInt(100f * maskAfter / maskBefore)}% of the face is paintable on '{baseName}'.");

            var tex = new Texture2D(atlasSize, atlasSize, TextureFormat.RGBA32, true)
            {
                name = "ShieldShare_" + pack.Name + "_StyleTex",
                wrapMode = TextureWrapMode.Clamp, // avoid bleeding across cell edges
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
            };
            tex.SetPixels32(atlas);
            tex.Apply(true, false);
            tex.Compress(true);        // RGBA32 2048x2048 is 16 MB; DXT5 is 4 MB
            tex.Apply(false, true);    // drop the CPU copy - nothing reads it back
            return tex;
        }

        private static Sprite LoadOrNull(string path)
        {
            var tex = TextureIO.Load(path, mipmaps: false);
            return tex != null ? TextureIO.ToSprite(tex) : null;
        }

        /// <summary>A shield-shaped icon: the pattern over plain wood, clipped to the face outline.</summary>
        private static Sprite MakeIcon(Color32[] pattern, int width, int height, bool[] mask)
        {
            var px = new Color32[IconSize * IconSize];
            for (int y = 0; y < IconSize; y++)
            {
                for (int x = 0; x < IconSize; x++)
                {
                    int i = y * IconSize + x;
                    if (!mask[i])
                        continue; // transparent
                    bool edge = x == 0 || y == 0 || x == IconSize - 1 || y == IconSize - 1
                                || !mask[i - 1] || !mask[i + 1] || !mask[i - IconSize] || !mask[i + IconSize];
                    if (edge)
                    {
                        px[i] = IconEdge;
                        continue;
                    }
                    var c = StyleBaker.SampleBilinear(pattern, width, height, (x + 0.5f) / IconSize, (y + 0.5f) / IconSize);
                    px[i] = StyleBaker.Over(c, IconWood);
                }
            }
            return TextureIO.ToSprite(TextureIO.FromPixels(px, IconSize, IconSize, false));
        }

        /// <summary>Per-base-shield settings established by testing in game.</summary>
        private sealed class BaseSettings
        {
            /// <summary>-1 = the outside faces local -Z, +1 = local +Z.</summary>
            public int FaceSign;
            /// <summary>Turn patterns upside down (model built the other way up).</summary>
            public bool FlipVertical;
            /// <summary>Only paint where the vanilla styles paint (keeps patterns off metal parts).</summary>
            public bool MaskToVanillaPaint;

            public BaseSettings(int faceSign, bool flipVertical, bool mask)
            {
                FaceSign = faceSign;
                FlipVertical = flipVertical;
                MaskToVanillaPaint = mask;
            }
        }

        /// <summary>
        ///     What testing showed for each vanilla shield. Shields not listed: the face is worked out from the
        ///     vanilla paint styles (DetectFaceSign), not flipped, and masked to the vanilla paint area.
        ///     If a pattern comes out upside down, set FlipVertical; if it covers metal parts, set the mask.
        /// </summary>
        private static readonly Dictionary<string, BaseSettings> KnownBases =
            new Dictionary<string, BaseSettings>(StringComparer.OrdinalIgnoreCase)
            {
                //                                          face  flipV  mask
                { "ShieldWood",            new BaseSettings(-1, false, false) }, // verified in game
                { "ShieldBanded",          new BaseSettings(-1, false, false) }, // verified in game
                { "ShieldWoodTower",       new BaseSettings(+1, false, false) }, // verified in game
                { "ShieldSilver",          new BaseSettings(+1, true,  false) }, // verified in game
                { "ShieldBlackmetal",      new BaseSettings(+1, true,  true)  }, // side + mask verified; flip from test 4
                { "ShieldBlackmetalTower", new BaseSettings(+1, true,  true)  }, // side + mask verified; flip from test 4
                { "ShieldIronTower",       new BaseSettings(+1, true,  true)  }, // side + mask verified; flip from test 4
                { "ShieldFlametal",        new BaseSettings(+1, false, true)  }, // verified in game
                { "ShieldFlametalTower",   new BaseSettings(-1, false, true)  }, // verified in game
            };

        /// <summary>Vanilla style atlas of each base, for the paint-area mask.</summary>
        private readonly Dictionary<string, Texture> vanillaStyleTex = new Dictionary<string, Texture>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> useVanillaMask = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, byte[]> maskCache = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        private FrontProjection GetProjection(string baseName, MeshFilter model)
        {
            FrontProjection projection;
            if (projections.TryGetValue(baseName, out projection))
                return projection;

            var mesh = model.sharedMesh;
            if (mesh == null)
                return null;

            MeshSnapshot snap;
            try
            {
                snap = MeshReader.Read(mesh);
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogError($"[ShieldShare] The mesh of '{baseName}' could not be read{(mesh.isReadable ? "" : " back from the GPU")}, " +
                                       $"so patterns can't be baked for it: {ex.Message}");
                projections[baseName] = null;
                return null;
            }

            var renderer = model.GetComponent<MeshRenderer>();
            string partsNote;
            int[] triangles = StyledTriangles(snap, renderer, out partsNote);

            // Which side is the outside? Use the tested value if we have one; otherwise (and for the log,
            // always) check which side the vanilla paint styles cover.
            string detectNote;
            Texture vanilla;
            int detected = DetectFaceSign(snap, triangles, renderer, out detectNote, out vanilla);
            BaseSettings known;
            int faceSign;
            bool flipV = false, mask = true;
            if (KnownBases.TryGetValue(baseName, out known))
            {
                faceSign = known.FaceSign;
                flipV = known.FlipVertical;
                mask = known.MaskToVanillaPaint;
                detectNote = $"tested: {(faceSign < 0 ? "-Z" : "+Z")}{(flipV ? ", flipped" : "")}; paint check: {detectNote}";
            }
            else
            {
                faceSign = detected != 0 ? detected : -1;
                detectNote = detected != 0 ? $"paint check: {detectNote}" : $"paint check inconclusive ({detectNote}), assuming -Z";
            }
            vanillaStyleTex[baseName] = vanilla;
            useVanillaMask[baseName] = mask && vanilla != null;
            if (mask && vanilla != null)
                detectNote += "; masked to vanilla paint area";

            projection = FrontProjection.Build(snap.Vertices, snap.Normals, snap.Uvs, triangles, faceSign, flipV);
            projections[baseName] = projection;

            Jotunn.Logger.LogInfo($"[ShieldShare] Base '{baseName}': face = {(faceSign < 0 ? "-Z" : "+Z")} ({detectNote}); " +
                                  $"{projection.Front.Count} of {projection.TotalTriangles} triangles form the face " +
                                  $"(aspect {projection.AspectRatio:F2}, bounds X[{projection.MinX:F3},{projection.MaxX:F3}] " +
                                  $"Y[{projection.MinY:F3},{projection.MaxY:F3}]){partsNote}{(snap.FromGpu ? ", mesh read from GPU" : "")}.");
            if (projection.FrontTrianglesOutsideUnitUv > 0)
                Jotunn.Logger.LogWarning($"[ShieldShare] Base '{baseName}': {projection.FrontTrianglesOutsideUnitUv} face triangle(s) have UVs outside 0..1 " +
                                         "and will be partly unpainted.");
            return projection;
        }

        /// <summary>
        ///     Returns -1 or +1 for the side (local Z) whose triangles the vanilla style atlas paints most,
        ///     or 0 if that can't be told (no vanilla styles, or both sides equal).
        /// </summary>
        private static int DetectFaceSign(MeshSnapshot snap, int[] triangles, MeshRenderer renderer, out string note, out Texture vanilla)
        {
            const int size = 256;
            vanilla = null;
            if (renderer != null)
                foreach (var mat in renderer.sharedMaterials)
                    if (mat != null && mat.HasProperty("_StyleTex") && mat.GetTexture("_StyleTex") != null)
                    {
                        vanilla = mat.GetTexture("_StyleTex");
                        break;
                    }

            if (vanilla == null)
            {
                note = "no vanilla styles to compare";
                return 0;
            }

            Color32[] atlas;
            try
            {
                atlas = ReadTexturePixels(vanilla, size);
            }
            catch (Exception ex)
            {
                note = "could not read vanilla styles: " + ex.Message;
                return 0;
            }

            float neg = FrontProjection.PaintedFraction(snap.Vertices, snap.Normals, snap.Uvs, triangles, -1, atlas, size);
            float pos = FrontProjection.PaintedFraction(snap.Vertices, snap.Normals, snap.Uvs, triangles, +1, atlas, size);
            note = $"-Z {Pct(neg)} painted, +Z {Pct(pos)} painted";
            // Metal shields only have a small paintable panel, so the numbers can be small (6% vs 0%).
            // In testing the larger side was the outside on every shield; require a clear ratio, not a big gap.
            float hi = Mathf.Max(neg, pos), lo = Mathf.Max(Mathf.Min(neg, pos), 0f);
            if (hi < 0.02f || hi < lo * 1.5f)
                return 0;
            return neg > pos ? -1 : 1;
        }

        private static string Pct(float f) => f < 0 ? "n/a" : Mathf.RoundToInt(f * 100f) + "%";

        /// <summary>The vanilla paintable area of a base at one atlas size, or null if that base isn't masked.</summary>
        private byte[] GetVanillaPaintMask(string baseName, int atlasSize)
        {
            bool use;
            Texture vanilla;
            if (!useVanillaMask.TryGetValue(baseName, out use) || !use || !vanillaStyleTex.TryGetValue(baseName, out vanilla) || vanilla == null)
                return null;

            string key = baseName + "@" + atlasSize;
            byte[] mask;
            if (maskCache.TryGetValue(key, out mask))
                return mask;

            try
            {
                mask = StyleBaker.PaintableMask(ReadTexturePixels(vanilla, atlasSize), atlasSize);
                int painted = mask.Count(a => a > StyleBaker.MaskLow);
                if (painted == 0)
                {
                    Jotunn.Logger.LogWarning($"[ShieldShare] Base '{baseName}': the vanilla styles paint nothing, so no paint-area mask is used.");
                    mask = null;
                }
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"[ShieldShare] Base '{baseName}': could not read the vanilla styles for the paint-area mask: {ex.Message}");
                mask = null;
            }
            maskCache[key] = mask;
            return mask;
        }

        /// <summary>Copies any texture (even a compressed, non-readable one) into a readable pixel array via the GPU.</summary>
        private static Color32[] ReadTexturePixels(Texture source, int size)
        {
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            Texture2D copy = null;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                copy = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
                copy.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                copy.Apply();
                return copy.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                if (copy != null)
                    UnityEngine.Object.Destroy(copy);
            }
        }

        /// <summary>
        ///     Triangles of the mesh parts (sub-meshes) whose material actually uses the style texture.
        ///     Metal shields can have extra parts (a boss, a trim) with their own material; baking those
        ///     would paint pattern into places the styled material never looks.
        /// </summary>
        private static int[] StyledTriangles(MeshSnapshot snap, MeshRenderer renderer, out string note)
        {
            note = "";
            var all = snap.SubMeshTriangles.SelectMany(t => t).ToArray();
            if (renderer == null || snap.SubMeshTriangles.Count <= 1)
                return all;

            var mats = renderer.sharedMaterials;
            var tris = new List<int>();
            var used = new List<string>();
            for (int i = 0; i < snap.SubMeshTriangles.Count && i < mats.Length; i++)
            {
                if (mats[i] != null && mats[i].HasProperty("_StyleTex"))
                {
                    tris.AddRange(snap.SubMeshTriangles[i]);
                    used.Add(i + ":" + mats[i].name);
                }
            }

            if (tris.Count == 0)
                return all; // nothing flagged - fall back to everything

            note = $", using {used.Count} of {snap.SubMeshTriangles.Count} mesh parts [{string.Join(", ", used.ToArray())}]";
            return tris.ToArray();
        }

        /// <summary>The shield's visible model: the mesh whose material supports styles, else the biggest mesh.</summary>
        private static MeshFilter FindModel(GameObject prefab)
        {
            MeshFilter best = null;
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null)
                    continue;
                var rend = mf.GetComponent<MeshRenderer>();
                if (rend != null && rend.sharedMaterials.Any(m => m != null && m.HasProperty("_StyleTex")))
                    return mf;
                if (best == null || mf.sharedMesh.vertexCount > best.sharedMesh.vertexCount)
                    best = mf;
            }
            return best;
        }

        private static IEnumerable<Renderer> ModelRenderers(GameObject prefab)
        {
            return prefab.GetComponentsInChildren<Renderer>(true)
                .Where(r => r is MeshRenderer || r is SkinnedMeshRenderer);
        }

        private static void CopyMaterials(GameObject prefab)
        {
            foreach (var rend in ModelRenderers(prefab))
            {
                var mats = rend.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null)
                        mats[i] = new Material(mats[i]) { name = mats[i].name + "_" + prefab.name };
                rend.sharedMaterials = mats;
            }
        }

        private static void DisableStyles(GameObject prefab, ItemDrop itemDrop)
        {
            foreach (var rend in ModelRenderers(prefab))
            {
                foreach (var mat in rend.sharedMaterials)
                {
                    if (mat == null || !mat.HasProperty("_StyleTex"))
                        continue;
                    mat.DisableKeyword("_USESTYLES_ON");
                    if (mat.HasProperty("_UseStyles"))
                        mat.SetFloat("_UseStyles", 0f);
                }
            }
            itemDrop.m_itemData.m_shared.m_variants = 0;
        }

        private static void ApplyTextureLayers(ShieldPack pack, GameObject prefab)
        {
            var renderers = ModelRenderers(prefab).ToArray();
            foreach (var layer in LayerFileToShaderProperty)
            {
                string path = pack.FindImage(layer.Key);
                if (path == null)
                    continue;

                var texture = TextureIO.Load(path, linear: LinearLayers.Contains(layer.Key));
                if (texture == null)
                    continue;

                int applied = 0;
                foreach (var rend in renderers)
                {
                    foreach (var mat in rend.sharedMaterials)
                    {
                        if (mat != null && mat.HasProperty(layer.Value))
                        {
                            mat.SetTexture(layer.Value, texture);
                            applied++;
                        }
                    }
                }

                if (applied > 0)
                    Jotunn.Logger.LogInfo($"[ShieldShare] '{pack.Name}': applied {Path.GetFileName(path)} to {layer.Value}.");
                else
                    Jotunn.Logger.LogWarning($"[ShieldShare] '{pack.Name}': {Path.GetFileName(path)} was ignored - the base shield's material has no {layer.Value}.");
            }
        }

        // ------------------------------------------------------------------------------------------
        // Help for pack authors, written into the drop folder
        // ------------------------------------------------------------------------------------------

        private static void TryWriteAuthorGuide(string dropFolder)
        {
            try
            {
                File.WriteAllText(Path.Combine(dropFolder, AuthorGuide.FileName), AuthorGuide.Text);
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"[ShieldShare] Could not write the pack author guide: {ex.Message}");
            }
        }

        private void TryWriteTemplates(string dropFolder)
        {
            try
            {
                if (!projections.ContainsKey(ShieldPack.DefaultBasePrefab))
                {
                    var vanilla = PrefabManager.Instance.GetPrefab(ShieldPack.DefaultBasePrefab);
                    var model = vanilla != null ? FindModel(vanilla) : null;
                    if (model != null)
                        GetProjection(ShieldPack.DefaultBasePrefab, model);
                }

                string folder = Path.Combine(dropFolder, ShieldPackSync.TemplatesFolderName);
                Directory.CreateDirectory(folder);

                foreach (var kv in projections)
                {
                    if (kv.Value == null || kv.Value.Front.Count == 0)
                        continue;
                    WritePatternGuide(kv.Value, Path.Combine(folder, kv.Key + " - pattern guide.png"));
                    WriteUvLayout(kv.Value, Path.Combine(folder, kv.Key + " - UV layout.png"));
                }
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"[ShieldShare] Could not write the pattern templates: {ex.Message}");
            }
        }

        /// <summary>The face outline at the right proportions - paint your pattern over this.</summary>
        private static void WritePatternGuide(FrontProjection projection, string path)
        {
            const int width = 512;
            int height = Mathf.Clamp(Mathf.RoundToInt(width / Mathf.Max(projection.AspectRatio, 0.01f)), 64, 2048);

            var px = new Color32[width * height];
            foreach (var t in projection.Front)
            {
                StyleBaker.RasterizeTriangle(t.PA, t.PB, t.PC, width, height, 0.5f,
                    (x, y, wa, wb, wc) => px[y * width + x] = new Color32(205, 205, 205, 255));
            }

            // outline + centre cross so authors can line artwork up
            var outline = new Color32[px.Length];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    if (px[i].a == 0) continue;
                    bool edge = false;
                    for (int dy = -2; dy <= 2 && !edge; dy++)
                        for (int dx = -2; dx <= 2 && !edge; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            edge = nx < 0 || ny < 0 || nx >= width || ny >= height || px[ny * width + nx].a == 0;
                        }
                    outline[i] = edge ? new Color32(40, 40, 40, 255)
                        : (x == width / 2 || y == height / 2) ? new Color32(160, 160, 160, 255) : px[i];
                }
            }
            TextureIO.SavePng(outline, width, height, path);
        }

        /// <summary>The base shield's UV layout: face in orange, the rest in grey. For MainTex/BumpMap/StyleTex authors.</summary>
        private static void WriteUvLayout(FrontProjection projection, string path)
        {
            const int size = 1024;
            var px = new Color32[size * size];
            var other = new Color32(140, 140, 140, 170);
            var face = new Color32(235, 160, 50, 230);
            var wire = new Color32(20, 20, 20, 255);

            foreach (var t in projection.OtherUvTriangles)
                StyleBaker.RasterizeTriangle(t[0], t[1], t[2], size, size, 0f, (x, y, wa, wb, wc) => px[y * size + x] = other);
            foreach (var t in projection.Front)
                StyleBaker.RasterizeTriangle(t.UvA, t.UvB, t.UvC, size, size, 0f, (x, y, wa, wb, wc) => px[y * size + x] = face);

            foreach (var t in projection.OtherUvTriangles)
            {
                StyleBaker.DrawLine(px, size, size, t[0], t[1], wire);
                StyleBaker.DrawLine(px, size, size, t[1], t[2], wire);
                StyleBaker.DrawLine(px, size, size, t[2], t[0], wire);
            }
            foreach (var t in projection.Front)
            {
                StyleBaker.DrawLine(px, size, size, t.UvA, t.UvB, wire);
                StyleBaker.DrawLine(px, size, size, t.UvB, t.UvC, wire);
                StyleBaker.DrawLine(px, size, size, t.UvC, t.UvA, wire);
            }
            TextureIO.SavePng(px, size, size, path);
        }
    }
}
