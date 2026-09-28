using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ShieldShare
{
    /// <summary>
    ///     The built-in magenta/black "missing" patterns, one per vanilla shield, compiled into the DLL from
    ///     the solution's Assets folder (see the EmbeddedResource entry in ShieldShare.csproj).
    ///     Resource names look like "ShieldShare.Fallback.ShieldWood - pattern guide.png"; everything before
    ///     " - " is the base prefab name, so "ShieldWood.png" works as well.
    /// </summary>
    internal static class FallbackPatterns
    {
        private const string ResourcePrefix = "ShieldShare.Fallback.";
        private static Dictionary<string, string> resourceByBase;

        /// <summary>Base prefabs that have a built-in fallback pattern.</summary>
        public static IEnumerable<string> Bases
        {
            get
            {
                Index();
                return resourceByBase.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray();
            }
        }

        private static void Index()
        {
            if (resourceByBase != null)
                return;
            resourceByBase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in Assembly.GetExecutingAssembly().GetManifestResourceNames())
            {
                if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal) || !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    continue;
                string file = name.Substring(ResourcePrefix.Length, name.Length - ResourcePrefix.Length - 4);
                int dash = file.IndexOf(" - ", StringComparison.Ordinal);
                string baseName = (dash >= 0 ? file.Substring(0, dash) : file).Trim();
                if (baseName.Length > 0 && !resourceByBase.ContainsKey(baseName))
                    resourceByBase[baseName] = name;
            }
        }

        /// <summary>
        ///     The fallback pattern for a base shield: its own if built in, otherwise the wood shield's,
        ///     otherwise a generated magenta/black checkerboard. Never returns null.
        /// </summary>
        public static Texture2D Load(string baseName)
        {
            Index();
            string resource;
            if (resourceByBase.TryGetValue(baseName ?? "", out resource) || resourceByBase.TryGetValue(ShieldPack.DefaultBasePrefab, out resource))
            {
                try
                {
                    using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
                    using (var memory = new MemoryStream())
                    {
                        stream.CopyTo(memory);
                        var tex = TextureIO.LoadBytes(memory.ToArray(), "Fallback_" + baseName, mipmaps: false);
                        if (tex != null)
                            return tex;
                    }
                }
                catch (Exception ex)
                {
                    Jotunn.Logger.LogWarning($"[ShieldShare] Built-in pattern '{resource}' could not be read: {ex.Message}");
                }
            }
            return Checkerboard();
        }

        private static Texture2D Checkerboard()
        {
            const int size = 256, squares = 4;
            var px = new Color32[size * size];
            var magenta = new Color32(255, 0, 255, 255);
            var black = new Color32(0, 0, 0, 255);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = ((x * squares / size) + (y * squares / size)) % 2 == 0 ? magenta : black;
            return TextureIO.FromPixels(px, size, size, false);
        }
    }
}
