using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace ShieldShare
{
    /// <summary>One shield ShieldShare has registered at some point.</summary>
    public class KnownShield
    {
        public string BasePrefab { get; set; }
        public string DisplayName { get; set; }
        /// <summary>Highest style count ever seen, so saved shields' style numbers always stay valid.</summary>
        public int Styles { get; set; }
        public string LastSeen { get; set; }
    }

    /// <summary>
    ///     Remembers every pack shield that has been registered (BepInEx\config\ShieldShare\known-shields.json).
    ///     When a pack's zip is removed, its shields are still registered as magenta "missing" stand-ins on the
    ///     same base with the same number of styles. Otherwise the game would silently delete every copy
    ///     players own the next time their character or a chest loads ("Failed to find item prefab").
    ///     Delete an entry from the file to forget a shield for good.
    /// </summary>
    internal sealed class KnownShields
    {
        private readonly string path;
        private Dictionary<string, KnownShield> entries = new Dictionary<string, KnownShield>(StringComparer.OrdinalIgnoreCase);

        public KnownShields(string path)
        {
            this.path = path;
        }

        public IEnumerable<KeyValuePair<string, KnownShield>> All => entries;

        public void Load()
        {
            if (!File.Exists(path))
                return;
            try
            {
                var loaded = JsonConvert.DeserializeObject<Dictionary<string, KnownShield>>(File.ReadAllText(path));
                if (loaded != null)
                    entries = new Dictionary<string, KnownShield>(loaded, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                // Keep a copy rather than overwrite it - it's the only record of removed packs.
                string backup = path + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                try { File.Copy(path, backup, true); } catch { }
                Jotunn.Logger.LogError($"[ShieldShare] '{Path.GetFileName(path)}' could not be read ({ex.Message}); saved a copy as '{Path.GetFileName(backup)}' and started a new list.");
            }
        }

        public void Remember(string id, string basePrefab, string displayName, int styles)
        {
            KnownShield entry;
            if (!entries.TryGetValue(id, out entry))
                entries[id] = entry = new KnownShield();
            entry.BasePrefab = basePrefab;
            entry.DisplayName = displayName;
            entry.Styles = Math.Max(entry.Styles, styles);
            entry.LastSeen = DateTime.Now.ToString("yyyy-MM-dd");
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(path, JsonConvert.SerializeObject(entries, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"[ShieldShare] Could not save '{Path.GetFileName(path)}': {ex.Message}");
            }
        }
    }
}
