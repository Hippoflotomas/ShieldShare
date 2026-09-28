using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace ShieldShare
{
    public class ShieldRequirement
    {
        public string Item { get; set; }
        public int Amount { get; set; } = 1;
        public int AmountPerLevel { get; set; } = 0;
    }

    /// <summary>
    ///     Contents of shield.json. EVERY field is optional - a pack with no shield.json at all still loads.
    ///     Property names are matched case-insensitively ("displayName" and "DisplayName" both work).
    /// </summary>
    public class ShieldDefinition
    {
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string CraftingStation { get; set; }
        public int MinStationLevel { get; set; } = 1;
        public string BasePrefab { get; set; }
        /// <summary>Optional cap on how many patterns to use. Normally the Pattern files are just counted.</summary>
        public int StyleCount { get; set; }
        public bool Hidden { get; set; }
        public List<ShieldRequirement> Requirements { get; set; }
    }

    /// <summary>
    ///     One shield pack folder on disk, with its files found case-insensitively and its
    ///     shield.json filled in with friendly defaults.
    /// </summary>
    internal sealed class ShieldPack
    {
        public const int MaxStyles = 16; // the style atlas is a fixed 4x4 grid
        public const string DefaultBasePrefab = "ShieldWood";
        public const string DefaultCraftingStation = "piece_workbench";

        public static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg" };

        // "Pattern1", "pattern_2", "Pattern 3", "Style4", "pattern" (= 1)
        private static readonly Regex PatternName = new Regex(@"^(pattern|style)[\s_\-]*(\d*)$", RegexOptions.IgnoreCase);
        private static readonly Regex IconName = new Regex(@"^icon[\s_\-]*(\d*)$", RegexOptions.IgnoreCase);

        private static readonly Dictionary<string, string> StationAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "workbench", "piece_workbench" },
            { "forge", "forge" },
            { "stonecutter", "piece_stonecutter" },
            { "artisan", "piece_artisanstation" },
            { "artisantable", "piece_artisanstation" },
            { "artisanstation", "piece_artisanstation" },
            { "blackforge", "blackforge" },
            { "blacksmith", "blackforge" },
            { "galdr", "piece_magetable" },
            { "galdrtable", "piece_magetable" },
            { "magetable", "piece_magetable" },
            { "cauldron", "piece_cauldron" },
            { "none", "" },
        };

        public string Name { get; private set; }
        public string Folder { get; private set; }
        public ShieldDefinition Definition { get; private set; }
        /// <summary>Set when shield.json exists but could not be read. The shield still loads with defaults.</summary>
        public string JsonError { get; private set; }

        /// <summary>Pattern image paths in style order (style 0 = lowest number).</summary>
        public List<string> PatternPaths { get; } = new List<string>();
        /// <summary>Icon path per style, or null where the icon should be generated from the pattern.</summary>
        public List<string> IconPaths { get; } = new List<string>();
        /// <summary>Icon for a shield with no patterns ("Icon.png" / "Icon1.png"), or null.</summary>
        public string SingleIconPath { get; private set; }
        public List<string> Warnings { get; } = new List<string>();

        private readonly Dictionary<string, string> imagesByStem = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string FindImage(string stem)
        {
            string path;
            return imagesByStem.TryGetValue(stem, out path) ? path : null;
        }

        public static bool IsImage(string path) =>
            ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

        /// <summary>Does this file name look like part of a shield pack? Used to spot pack folders.</summary>
        public static bool IsPackFile(string fileName)
        {
            if (string.Equals(fileName, "shield.json", StringComparison.OrdinalIgnoreCase))
                return true;
            if (!IsImage(fileName))
                return false;
            string stem = Path.GetFileNameWithoutExtension(fileName);
            return PatternName.IsMatch(stem) || IconName.IsMatch(stem)
                   || string.Equals(stem, "StyleTex", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(stem, "MainTex", StringComparison.OrdinalIgnoreCase);
        }

        public static ShieldPack Load(string folder)
        {
            var pack = new ShieldPack { Folder = folder, Name = Path.GetFileName(folder) };

            string jsonPath = null;
            foreach (var file in Directory.GetFiles(folder))
            {
                string fileName = Path.GetFileName(file);
                if (string.Equals(fileName, "shield.json", StringComparison.OrdinalIgnoreCase))
                    jsonPath = file;
                else if (IsImage(file))
                {
                    string stem = Path.GetFileNameWithoutExtension(file);
                    if (pack.imagesByStem.ContainsKey(stem))
                        pack.Warnings.Add($"Both '{Path.GetFileName(pack.imagesByStem[stem])}' and '{fileName}' exist - using the first.");
                    else
                        pack.imagesByStem[stem] = file;
                }
            }

            pack.Definition = ReadDefinition(jsonPath, pack);
            pack.ApplyDefaults();
            pack.CollectStyles();
            return pack;
        }

        private static ShieldDefinition ReadDefinition(string jsonPath, ShieldPack pack)
        {
            if (jsonPath == null)
                return new ShieldDefinition();

            try
            {
                return JsonConvert.DeserializeObject<ShieldDefinition>(File.ReadAllText(jsonPath)) ?? new ShieldDefinition();
            }
            catch (Exception ex)
            {
                pack.JsonError = ex.Message;
                return new ShieldDefinition();
            }
        }

        private void ApplyDefaults()
        {
            var d = Definition;
            if (string.IsNullOrWhiteSpace(d.DisplayName))
                d.DisplayName = Name.Replace('_', ' ').Replace('-', ' ');
            if (d.Description == null)
                d.Description = "";
            if (JsonError != null)
                d.Description = "(shield.json has an error - check the BepInEx log) " + d.Description;
            if (string.IsNullOrWhiteSpace(d.BasePrefab))
                d.BasePrefab = DefaultBasePrefab;

            if (string.IsNullOrWhiteSpace(d.CraftingStation))
                d.CraftingStation = DefaultCraftingStation;
            else
            {
                string key = d.CraftingStation.Replace(" ", "").Replace("_", "");
                string mapped;
                if (StationAliases.TryGetValue(key, out mapped))
                    d.CraftingStation = mapped;
            }
            if (d.MinStationLevel < 1)
                d.MinStationLevel = 1;

            d.Requirements = (d.Requirements ?? new List<ShieldRequirement>())
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.Item))
                .ToList();
            if (d.Requirements.Count == 0)
                d.Requirements.Add(new ShieldRequirement { Item = "Wood", Amount = 10 });
            foreach (var r in d.Requirements)
                if (r.Amount < 1) r.Amount = 1;
        }

        private void CollectStyles()
        {
            var patternsByNumber = new SortedDictionary<int, string>();
            var iconsByNumber = new Dictionary<int, string>();

            foreach (var kv in imagesByStem)
            {
                var m = PatternName.Match(kv.Key);
                if (m.Success)
                {
                    int n = m.Groups[2].Value.Length == 0 ? 1 : int.Parse(m.Groups[2].Value);
                    if (patternsByNumber.ContainsKey(n))
                        Warnings.Add($"Two pattern files for style {n} ('{Path.GetFileName(patternsByNumber[n])}' and '{Path.GetFileName(kv.Value)}') - using the first.");
                    else
                        patternsByNumber[n] = kv.Value;
                    continue;
                }

                m = IconName.Match(kv.Key);
                if (m.Success)
                {
                    int n = m.Groups[1].Value.Length == 0 ? 1 : int.Parse(m.Groups[1].Value);
                    if (!iconsByNumber.ContainsKey(n))
                        iconsByNumber[n] = kv.Value;
                }
            }

            int limit = MaxStyles;
            if (Definition.StyleCount > 0 && Definition.StyleCount < limit)
                limit = Definition.StyleCount;

            foreach (var kv in patternsByNumber)
            {
                if (PatternPaths.Count >= limit)
                {
                    Warnings.Add(Definition.StyleCount > 0 && Definition.StyleCount < MaxStyles
                        ? $"StyleCount is {Definition.StyleCount} in shield.json - extra pattern files were ignored."
                        : $"Only {MaxStyles} styles fit on one shield - extra pattern files were ignored.");
                    break;
                }
                PatternPaths.Add(kv.Value);
                string icon;
                IconPaths.Add(iconsByNumber.TryGetValue(kv.Key, out icon) ? icon : null);
            }

            if (PatternPaths.Count == 0)
            {
                string single;
                if (iconsByNumber.TryGetValue(1, out single))
                    SingleIconPath = single;
            }
        }
    }
}
