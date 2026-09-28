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
    ///     Contents of shield.json. The FILE is required (a folder without it is skipped), but every field
    ///     in it has a default. Property names are matched case-insensitively ("displayName" = "DisplayName").
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
    ///     One shield folder on disk. Layout (same idea as BannerShare):
    ///       ShieldID/
    ///         shield.json      required
    ///         Pattern1.png     one per style, numbered from 1 (Pattern2.png, ...)
    ///         Icon1.png        optional, per style - generated from the pattern if missing
    ///         MainTex.png ...  optional texture layers
    ///     File names are fixed but not case-sensitive. PNG only.
    /// </summary>
    internal sealed class ShieldPack
    {
        public const int MaxStyles = 16; // the style atlas is a fixed 4x4 grid
        public const string DefaultBasePrefab = "ShieldWood";
        public const string DefaultCraftingStation = "piece_workbench";

        public const string JsonFileName = "shield.json";
        public const string ImageExtension = ".png";

        // Pattern1 ... Pattern16 / Icon1 ... Icon16 (any capitalisation)
        private static readonly Regex PatternName = new Regex(@"^pattern(\d+)$", RegexOptions.IgnoreCase);
        private static readonly Regex IconName = new Regex(@"^icon(\d+)$", RegexOptions.IgnoreCase);

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
        /// <summary>
        ///     Why this folder can't be loaded (no shield.json, or it can't be read), or null if it's fine.
        ///     Folders with a problem are skipped.
        /// </summary>
        public string Problem { get; private set; }

        /// <summary>Pattern image paths in style order (style 0 = lowest number).</summary>
        public List<string> PatternPaths { get; } = new List<string>();
        /// <summary>Icon path per style, or null where the icon should be generated from the pattern.</summary>
        public List<string> IconPaths { get; } = new List<string>();
        /// <summary>Icon for a shield with no patterns (Icon1.png), or null.</summary>
        public string SingleIconPath { get; private set; }
        public List<string> Warnings { get; } = new List<string>();

        private readonly Dictionary<string, string> imagesByStem = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string FindImage(string stem)
        {
            string path;
            return imagesByStem.TryGetValue(stem, out path) ? path : null;
        }

        public static bool IsImage(string path) =>
            string.Equals(Path.GetExtension(path), ImageExtension, StringComparison.OrdinalIgnoreCase);

        public static bool IsJson(string fileName) =>
            string.Equals(fileName, JsonFileName, StringComparison.OrdinalIgnoreCase);

        public static ShieldPack Load(string folder)
        {
            var pack = new ShieldPack { Folder = folder, Name = Path.GetFileName(folder) };

            string jsonPath = null;
            foreach (var file in Directory.GetFiles(folder))
            {
                string fileName = Path.GetFileName(file);
                if (IsJson(fileName))
                    jsonPath = file;
                else if (IsImage(file))
                {
                    string stem = Path.GetFileNameWithoutExtension(file);
                    if (pack.imagesByStem.ContainsKey(stem))
                        pack.Warnings.Add($"Both '{Path.GetFileName(pack.imagesByStem[stem])}' and '{fileName}' exist - using the first.");
                    else
                        pack.imagesByStem[stem] = file;
                }
                else if (Path.GetExtension(file).Length > 0 && !fileName.StartsWith(".")
                         && !string.Equals(Path.GetExtension(file), ".txt", StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(Path.GetExtension(file), ".md", StringComparison.OrdinalIgnoreCase))
                {
                    pack.Warnings.Add($"'{fileName}' is ignored - images must be .png.");
                }
            }

            if (jsonPath == null)
            {
                pack.Problem = $"no {JsonFileName} in the folder";
                return pack;
            }

            pack.Definition = ReadDefinition(jsonPath, pack);
            if (pack.Problem != null)
                return pack;
            pack.ApplyDefaults();
            pack.CollectStyles();
            return pack;
        }

        private static ShieldDefinition ReadDefinition(string jsonPath, ShieldPack pack)
        {
            try
            {
                return JsonConvert.DeserializeObject<ShieldDefinition>(File.ReadAllText(jsonPath)) ?? new ShieldDefinition();
            }
            catch (Exception ex)
            {
                pack.Problem = $"{JsonFileName} can't be read: {ex.Message}";
                return null;
            }
        }

        private void ApplyDefaults()
        {
            var d = Definition;
            if (string.IsNullOrWhiteSpace(d.DisplayName))
                d.DisplayName = Name.Replace('_', ' ').Replace('-', ' ');
            if (d.Description == null)
                d.Description = "";
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
                    int n = int.Parse(m.Groups[1].Value);
                    if (patternsByNumber.ContainsKey(n))
                        Warnings.Add($"Two pattern files for style {n} ('{Path.GetFileName(patternsByNumber[n])}' and '{Path.GetFileName(kv.Value)}') - using the first.");
                    else
                        patternsByNumber[n] = kv.Value;
                    continue;
                }

                m = IconName.Match(kv.Key);
                if (m.Success)
                {
                    int n = int.Parse(m.Groups[1].Value);
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
