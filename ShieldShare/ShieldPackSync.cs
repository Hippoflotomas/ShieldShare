using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace ShieldShare
{
    /// <summary>
    ///     Copies shield packs from the player-facing drop folder (Documents\Valheim Custom Shields)
    ///     into the mod's working folder (BepInEx\config\ShieldShare\Shields).
    ///
    ///     Pack layout is fixed, the same as BannerShare:
    ///       MyShields.zip
    ///         ShieldID/            one folder per shield, directly inside the zip
    ///           shield.json        required
    ///           Pattern1.png ...
    ///         AnotherShieldID/
    ///           ...
    ///     Files at the top of the zip, folders nested deeper, and folders without shield.json are
    ///     ignored (with a warning in the log). Loose folders in the drop folder are ignored too -
    ///     only .zip files are read.
    ///
    ///     Every folder we create is tagged with a marker file. On the next launch, tagged folders whose
    ///     zip has gone are removed; untagged folders (added by hand, or the built-in "Missing"
    ///     fallback) are never touched.
    /// </summary>
    internal static class ShieldPackSync
    {
        public const string MarkerFileName = ".shieldshare-source";
        public const string TemplatesFolderName = "_Templates";

        public static void Sync(string dropFolder, string shieldsFolder)
        {
            var produced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool anyFailures = false;

            foreach (var zipPath in Directory.GetFiles(dropFolder, "*.zip").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string zipName = Path.GetFileName(zipPath);
                try
                {
                    int count = SyncZip(zipPath, shieldsFolder, produced);
                    if (count == 0)
                        Jotunn.Logger.LogWarning($"[ShieldShare] '{zipName}' contains no shields. Each shield must be a folder inside the zip, with {ShieldPack.JsonFileName} and its images in that folder.");
                    else
                        Jotunn.Logger.LogInfo($"[ShieldShare] Synced {count} shield(s) from '{zipName}'.");
                }
                catch (Exception ex)
                {
                    anyFailures = true;
                    Jotunn.Logger.LogError($"[ShieldShare] Failed to read '{zipName}': {ex.Message}");
                }
            }

            foreach (var dir in Directory.GetDirectories(dropFolder))
            {
                string name = Path.GetFileName(dir);
                if (!name.StartsWith("_") && !name.StartsWith("."))
                    Jotunn.Logger.LogWarning($"[ShieldShare] The folder '{name}' in the drop folder is ignored - zip it first (the shield folder goes inside the zip).");
            }

            if (anyFailures)
            {
                Jotunn.Logger.LogWarning("[ShieldShare] Skipping clean-up this launch because a zip failed to read.");
                return;
            }

            foreach (var existing in Directory.GetDirectories(shieldsFolder))
            {
                string name = Path.GetFileName(existing);
                if (produced.Contains(name) || !File.Exists(Path.Combine(existing, MarkerFileName)))
                    continue; // still wanted, or not ours to remove

                try
                {
                    Directory.Delete(existing, true);
                    Jotunn.Logger.LogInfo($"[ShieldShare] Removed '{name}' - no zip in the drop folder contains it any more.");
                }
                catch (Exception ex)
                {
                    Jotunn.Logger.LogError($"[ShieldShare] Failed to remove '{name}': {ex.Message}");
                }
            }
        }

        private static int SyncZip(string zipPath, string shieldsFolder, HashSet<string> produced)
        {
            string zipName = Path.GetFileName(zipPath);
            int count = 0;

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                // Group entries by their top-level folder. Some zip tools write '\' separators, so normalise.
                var byFolder = new Dictionary<string, List<ZipArchiveEntry>>(StringComparer.OrdinalIgnoreCase);
                var ignoredRootFiles = new List<string>();
                var ignoredNested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var entry in archive.Entries)
                {
                    string full = entry.FullName.Replace('\\', '/').TrimStart('/');
                    if (full.EndsWith("/") || full.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase))
                        continue; // folder entries, macOS junk

                    string[] parts = full.Split('/');
                    if (parts[parts.Length - 1].StartsWith("."))
                        continue; // hidden files (.DS_Store etc.)

                    if (parts.Length == 1)
                    {
                        ignoredRootFiles.Add(full);
                        continue;
                    }
                    if (parts.Length > 2)
                    {
                        ignoredNested.Add(parts[0] + "/" + parts[1]);
                        continue;
                    }

                    List<ZipArchiveEntry> list;
                    if (!byFolder.TryGetValue(parts[0], out list))
                        byFolder[parts[0]] = list = new List<ZipArchiveEntry>();
                    list.Add(entry);
                }

                if (ignoredRootFiles.Count > 0)
                    Jotunn.Logger.LogWarning($"[ShieldShare] '{zipName}': files at the top of the zip are ignored ({string.Join(", ", ignoredRootFiles.Take(5))}" +
                                             $"{(ignoredRootFiles.Count > 5 ? ", ..." : "")}). Put them in a folder named after the shield.");
                foreach (var nested in ignoredNested)
                    Jotunn.Logger.LogWarning($"[ShieldShare] '{zipName}': '{nested}/' is ignored - shield folders must sit directly inside the zip, not inside another folder.");

                foreach (var kv in byFolder)
                {
                    string folderName = kv.Key;

                    if (!kv.Value.Any(e => ShieldPack.IsJson(FileNameOf(e))))
                    {
                        Jotunn.Logger.LogWarning($"[ShieldShare] '{zipName}': '{folderName}/' is skipped - it has no {ShieldPack.JsonFileName}.");
                        continue;
                    }

                    string name = SafeFolderName(folderName);
                    if (name != folderName)
                        Jotunn.Logger.LogInfo($"[ShieldShare] '{zipName}': '{folderName}' is used as '{name}' (only letters, digits, '-' and '_' are kept).");
                    if (!produced.Add(name))
                    {
                        Jotunn.Logger.LogWarning($"[ShieldShare] Two shields are called '{name}' - skipping the one in '{zipName}'. Shield folder names must be unique.");
                        continue;
                    }

                    string target = PrepareTarget(shieldsFolder, name, zipName);
                    foreach (var entry in kv.Value)
                        entry.ExtractToFile(Path.Combine(target, FileNameOf(entry)), true);
                    count++;
                }
            }
            return count;
        }

        private static string FileNameOf(ZipArchiveEntry entry)
        {
            string full = entry.FullName.Replace('\\', '/');
            return full.Substring(full.LastIndexOf('/') + 1);
        }

        private static string PrepareTarget(string shieldsFolder, string name, string source)
        {
            string target = Path.Combine(shieldsFolder, name);
            if (Directory.Exists(target))
                Directory.Delete(target, true);
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, MarkerFileName), source);
            return target;
        }

        /// <summary>Folder names become prefab names (the shield's ID), so keep them to letters, digits, '_' and '-'.</summary>
        public static string SafeFolderName(string name)
        {
            var chars = name.Trim().Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray();
            string safe = new string(chars).Trim('_');
            return safe.Length == 0 ? "Shield" : safe;
        }
    }
}
