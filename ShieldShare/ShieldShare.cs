using BepInEx;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
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
        public const string PluginVersion = "0.0.1";
        private const string FallBackShieldName = "Missing";
        private const string ItemPrefabPrefix = "ShieldShare_";
        private const string VanillaShieldSource = "ShieldWood";

        // Use this class to add your own localization to the game
        // https://valheim-modding.github.io/Jotunn/tutorials/localization.html
        public static CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();

        public class ShieldRequirement
        {
            public string Item { get; set; }
            public int Amount { get; set; }
        }

        public class ShieldDefinition
        {
            public string DisplayName { get; set; }
            public string Description { get; set; }
            public string CraftingStation { get; set; }
            public string BasePrefab { get; set; }
            public int StyleCount { get; set; }
            public bool Hidden { get; set; }
            public List<ShieldRequirement> Requirements { get; set; } = new List<ShieldRequirement>();
        }

        private static readonly Dictionary<string, string> LayerFileToShaderProperty = new Dictionary<string, string>
        {
            { "MainTex", "_MainTex" },
            { "BumpMap", "_BumpMap" },
            { "EmissionMap", "_EmissionMap" },
            { "MetallicGlossMap", "_MetallicGlossMap" },
        };

        private string GetShieldFolderPath()
        {
            string path = Path.Combine(BepInEx.Paths.ConfigPath, PluginName, "Shields");
            Directory.CreateDirectory(path);
            return path;
        }

        private string GetShieldDropFolderPath()
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string dropFolder = Path.Combine(documents, "Valheim Custom Shields");
            Directory.CreateDirectory(dropFolder);
            return dropFolder;
        }

        private List<string> GetTopLevelFolderNames(string zipPath)
        {
            var names = new HashSet<string>();
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    int slash = entry.FullName.IndexOf('/');
                    if (slash > 0)
                        names.Add(entry.FullName.Substring(0, slash));
                }
            }
            return names.ToList();
        }

        private Sprite LoadShieldIcon(string shieldFolder, string styleNumber)
        {
            string iconPath = Path.Combine(shieldFolder, styleNumber);
            if (!File.Exists(iconPath))
            {
                Jotunn.Logger.LogWarning($"[ShieldShare] failed to load file {iconPath}");
                return null;
            }

            Jotunn.Logger.LogInfo($"[ShieldShare] loaded icon file {iconPath}");
            var iconTexture = AssetUtils.LoadTexture(iconPath, relativePath: false);
            return Sprite.Create(iconTexture, new Rect(0, 0, iconTexture.width, iconTexture.height), new Vector2(0.5f, 0.5f));
        }

        private void SyncShieldsFromDropFolder(string shieldsFolder)
        {
            string dropFolder = GetShieldDropFolderPath();
            var zipPaths = Directory.GetFiles(dropFolder, "*.zip");
            var shouldExist = new HashSet<string>();
            bool anyReadFailures = false;

            foreach (var zipPath in zipPaths)
            {
                string zipName = Path.GetFileName(zipPath);
                try
                {
                    var producedFolders = GetTopLevelFolderNames(zipPath);
                    foreach (var name in producedFolders)
                    {
                        string existingPath = Path.Combine(shieldsFolder, name);
                        if (Directory.Exists(existingPath))
                            Directory.Delete(existingPath, recursive: true);
                    }
                    ZipFile.ExtractToDirectory(zipPath, shieldsFolder);
                    foreach (var name in producedFolders)
                        shouldExist.Add(name);
                    Logger.LogInfo($"[ShieldShare] Synced {producedFolders.Count} shield(s) from '{zipName}'.");
                }
                catch (Exception ex)
                {
                    anyReadFailures = true;
                    Logger.LogError($"[ShieldShare] Failed to sync '{zipName}': {ex.Message}");
                }
            }

            if (anyReadFailures)
            {
                Logger.LogWarning("[ShieldShare] Skipping cleanup this time - a zip failed to parse");
                return;
            }

            foreach (var existingFolder in Directory.GetDirectories(shieldsFolder))
            {
                string name = Path.GetFileName(existingFolder);
                if (name == FallBackShieldName)
                    continue; // permanent fallback shield, do not kill

                if (!shouldExist.Contains(name))
                {
                    try
                    {
                        Directory.Delete(existingFolder, recursive: true);
                        Logger.LogInfo($"[ShieldShare] Removed '{name}' - no matching zip in the drop folder.");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"[ShieldShare] Failed to remove '{name}': {ex.Message}");
                    }
                }
            }
        }

        private void LoadAndRegisterShields()
        {
            string folder = GetShieldFolderPath();
            SyncShieldsFromDropFolder(folder);
            Logger.LogInfo($"[ShieldShare] scanning folder {folder}");

            var shieldFolders = Directory.GetDirectories(folder);
            Logger.LogInfo($"[ShieldShare] Found {shieldFolders.Length} shield folder(s).");

            foreach (var shieldFolder in shieldFolders)
            {
                string baseName = Path.GetFileName(shieldFolder);
                string jsonPath = Path.Combine(shieldFolder, "shield.json");

                if (!File.Exists(jsonPath))
                {
                    Logger.LogWarning($"[ShieldShare] Skipping '{baseName}' shield.json missing from it's folder.");
                    continue;
                }

                ShieldDefinition definition;
                try
                {
                    definition = JsonConvert.DeserializeObject<ShieldDefinition>(File.ReadAllText(jsonPath));
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[ShieldShare] The file '{baseName}/shield.json' is wrong: {ex.Message}");
                    continue;
                }

                RegisterShieldPiece(baseName, shieldFolder, definition);
            }
        }

        private void RegisterShieldPiece(string baseName, string shieldFolder, ShieldDefinition definition)
        {
            string prefabName = ItemPrefabPrefix + baseName;
            string baseSource = string.IsNullOrWhiteSpace(definition.BasePrefab) ? VanillaShieldSource : definition.BasePrefab;

            var shieldPrefab = PrefabManager.Instance.CreateClonedPrefab(prefabName, baseSource);

            foreach (var rend in shieldPrefab.GetComponentsInChildren<Renderer>(true))
            {
                Jotunn.Logger.LogInfo($"[ShieldShare] renderer '{rend.name}' ({rend.GetType().Name}), {rend.sharedMaterials.Length} material(s):");
                foreach (var mat in rend.sharedMaterials)
                {
                    Jotunn.Logger.LogInfo($"[ShieldShare]   material '{mat?.name}', shader '{mat?.shader?.name}', has _StyleTex: {mat != null && mat.HasProperty("_StyleTex")}");
                }
            }

            if (shieldPrefab == null)
            {
                Jotunn.Logger.LogError($"[ShieldShare] Could not clone '{baseSource}' for banner '{baseName}' - check the vanilla prefab name and try again.");
                return;
            }

            ApplyPlanarFrontUV(shieldPrefab);

            var meshRenderer = shieldPrefab.GetComponentInChildren<MeshRenderer>(true);
            if (meshRenderer == null)
            {
                Jotunn.Logger.LogError($"[ShieldShare] No MeshRenderer found on cloned prefab '{prefabName}'.");
            }
            else
            {
                var material = meshRenderer.material;
                ApplyShieldTextureLayers(shieldFolder, material);
            }

            List<Sprite> icons = new List<Sprite>();
            for (int n = 1; n <= definition.StyleCount; n++)
            {
                string styleNum = $"Icon{n}.png";
                icons.Add(LoadShieldIcon(shieldFolder, styleNum));
            }

            Sprite[] iconsArray = icons.ToArray();

            // StyleTex is Jötunn's own supported mechanism for the in-world pattern (not our
            // per-pack base coat - see ApplyShieldTextureLayers for that). Handing it to
            // ItemConfig lets Jötunn's CustomItem.FixVariants() do the real wiring: force the
            // Custom/Creature shader, add the ItemStyle component, enable the _USESTYLES_ON
            // keyword, and initialize _Style/_UseStyles/_StyleTex correctly. Setting _StyleTex
            // by hand on the material (what earlier diagnostics did) bypasses all of that and
            // never actually connects to variant selection.
            string styleTexPath = Path.Combine(shieldFolder, "StyleTex.png");
            Texture2D styleTexture = File.Exists(styleTexPath)
                ? AssetUtils.LoadTexture(styleTexPath, relativePath: false) //handbuilt override
                : BuildStyleAtlas(shieldFolder, definition.StyleCount); // auto pack from patternN.png files
            if (styleTexture != null)
                Jotunn.Logger.LogInfo($"[ShieldShare] Built/Loaded style tex from '{baseName}'.");

            var itemConfig = new ItemConfig
            {
                Name = definition.DisplayName,
                Description = definition.Description,
                CraftingStation = definition.CraftingStation,
                Icons = iconsArray,
                StyleTex = styleTexture,
                Enabled = !definition.Hidden,
                Requirements = definition.Requirements
                    .Select(r => new RequirementConfig { Item = r.Item, Amount = r.Amount })
                    .ToArray()
            };

            ItemManager.Instance.AddItem(new CustomItem(shieldPrefab, false, itemConfig));
            var shared = shieldPrefab.GetComponent<ItemDrop>().m_itemData.m_shared;
            // FixVariants() already sets m_variants when StyleTex is provided - this line is now
            // the fallback for packs with icons but no StyleTex.png, where that branch never runs.
            shared.m_variants = iconsArray.Length;
            Jotunn.Logger.LogInfo($"[ShieldShare] Registered '{definition.DisplayName}' from '{baseName}'.");
            Jotunn.Logger.LogInfo($"[ShieldShare] '{definition.DisplayName}': m_variants={shared.m_variants}, m_icons.Length={shared.m_icons?.Length ?? -1}");
        }

        private void ApplyPlanarFrontUV(GameObject prefab)
        {
            var meshFilter = prefab.GetComponentInChildren<MeshFilter>(true);
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                Jotunn.Logger.LogWarning($"[ShieldShare] No MeshFilter/sharedMesh found for UV remap on '{prefab.name}'.");
                return;
            }

            var source = meshFilter.sharedMesh;
            var verts = source.vertices;
            var normals = source.normals;
            var uvs = (Vector2[])source.uv.Clone();

            // Outward-facing (what the viewer actually sees) = local -Z normal.
            // Originally assumed +Z based on the dome/boss cluster, but an in-game test with a
            // single clean texture showed it landing on the INSIDE face instead - proof the
            // mesh's local axis convention is the opposite of what we assumed. The flat concave
            // cluster (normal.z ~ -0.90, including the dead-center vertex at normal=(0,0,-1))
            // is the one a viewer actually sees; the domed cluster (normal.z > 0) is the grip side.
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            var frontIndices = new List<int>();
            for (int i = 0; i < verts.Length; i++)
            {
                if (normals[i].z < 0f)
                {
                    frontIndices.Add(i);
                    if (verts[i].x < minX) minX = verts[i].x;
                    if (verts[i].x > maxX) maxX = verts[i].x;
                    if (verts[i].y < minY) minY = verts[i].y;
                    if (verts[i].y > maxY) maxY = verts[i].y;
                }
            }

            float rangeX = Mathf.Max(maxX - minX, 0.0001f);
            float rangeY = Mathf.Max(maxY - minY, 0.0001f);

            foreach (int i in frontIndices)
            {
                uvs[i] = new Vector2(
                    (verts[i].x - minX) / rangeX,
                    1f - (verts[i].y - minY) / rangeY);
            }

            var newMesh = new Mesh
            {
                name = source.name + "_ShieldSharePlanarUV",
                vertices = verts,
                triangles = source.triangles,
                normals = normals,
                uv = uvs,
                colors = source.colors,
                tangents = source.tangents,
            };
            newMesh.RecalculateBounds();

            meshFilter.mesh = newMesh;
            Jotunn.Logger.LogInfo($"[ShieldShare] Rebuilt planar front UVs for '{prefab.name}': {frontIndices.Count}/{verts.Length} verts remapped, bounds X[{minX:F3},{maxX:F3}] Y[{minY:F3},{maxY:F3}].");
        }

        private Texture2D ResizeTexture(Texture2D source, int width, int height)
        {
            RenderTexture rt = RenderTexture.GetTemporary(width, height);
            RenderTexture prev = RenderTexture.active;

            Graphics.Blit(source, rt);
            RenderTexture.active = rt;

            var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            result.Apply();

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return result;
        }

        private Texture2D BuildStyleAtlas(string shieldFolder, int styleCount)
        {
            const int cols = 4;
            const int rows = 4;
            const float artScale = 1f; // may need to be adjusted for different shields

            var patterns = new List<Texture2D>();

            for (int n = 1; n <= styleCount; n++)
            {
                string path = Path.Combine(shieldFolder, $"Pattern{n}.png");
                if (!File.Exists(path))
                {
                    Jotunn.Logger.LogWarning($"[ShieldShare] Missing 'Pattern{n}.png' - style {n - 1} will be blank.");
                    patterns.Add(null);
                    continue;
                }
                patterns.Add(AssetUtils.LoadTexture(path, relativePath: false));
            }

            if (patterns.All(p => p == null))
                return null; //no patterns at all.

             
            //int rows = Mathf.CeilToInt(patterns.Count / (float)cols);
            

            if (patterns.Count > rows * cols)
            {
                Jotunn.Logger.LogWarning(
                    $"[ShieldShare] {patterns.Count} patterns found, but only {rows * cols} style " +
                    "slots (4x4 grid) are confirmed to work - extra patterns will be skipped.");
            }

            int cellSize = patterns.First(p => p != null).width;
            int insetSize = Mathf.RoundToInt(cellSize * artScale); // may need to be adjusted for different shields
            int padding = (cellSize  - insetSize);

            var atlas = new Texture2D(cellSize * cols, cellSize * rows, TextureFormat.RGBA32, true);
            var blank = new Color32[atlas.width * atlas.height];
            atlas.SetPixels32(blank); //transparent by default - unused/partial-row cells stay empty

            for (int i = 0; i < patterns.Count && i < rows * cols; i ++)
            {
                var tex = patterns[i];
                if (tex == null) continue;

                var inset = ResizeTexture(tex, insetSize, insetSize);

                int col = i % cols;
                int row = i / cols; // row 0 = bottom, matches SetPixels' bottom left origin directly
                int x = col * cellSize + padding;
                int y = row * cellSize + padding;
                atlas.SetPixels(x, y, insetSize, insetSize, inset.GetPixels());
            }

            atlas.wrapMode = TextureWrapMode.Clamp; // avoid wrap bleed at the atlas edges
            atlas.Apply(updateMipmaps: true);
            return atlas;
        }

        private void ApplyShieldTextureLayers(string shieldFolder, Material material)
        {
            foreach (var layer in LayerFileToShaderProperty)
            {
                string layerPath = Path.Combine(shieldFolder, $"{layer.Key}.png");
                if (!File.Exists(layerPath))
                {
                    Jotunn.Logger.LogInfo($"[ShieldShare] No '{layer.Key}.png' for this pack, leaving '{layer.Value}' at its base value.");
                    continue;
                }

                var texture = AssetUtils.LoadTexture(layerPath, relativePath: false);
                material.SetTexture(layer.Value, texture);
                Jotunn.Logger.LogInfo($"[ShieldShare] Applied {layer.Key}.png to '{layer.Value}'.");
            }
        }

        private void Awake()
        {
            // Jotunn comes with its own Logger class to provide a consistent Log style for all mods using it
            Jotunn.Logger.LogInfo("ShieldShare has landed");
            PrefabManager.OnVanillaPrefabsAvailable += LoadAndRegisterShields;
            // To learn more about Jotunn's features, go to
            // https://valheim-modding.github.io/Jotunn/tutorials/overview.html
        }
    }
}