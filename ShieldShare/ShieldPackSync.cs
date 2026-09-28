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
    ///     The drop folder accepts, in any mix:
    ///       - .zip files, laid out any way (files at the root, in one folder, in nested folders,
    ///         several shields per zip). A zip with files at its root becomes a shield named after the zip.
    ///       - plain folders (handy while authoring - no zipping needed to test a change).
    ///
    ///     Every folder we create is tagged with a marker file. On the next launch, tagged folders whose
    ///     source has gone are removed; untagged folders (added by hand, or the built-in "Missing"
    ///     fallback) are never touched.
    /// </summary>
    internal static class ShieldPackSync
    {
        public const string MarkerFileName = ".shieldshare-source";
        public const string TemplatesFolderName = "_Templates";
        private const int MaxFolderDepth = 4;

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
                        Jotunn.Logger.LogWarning($"[ShieldShare] '{zipName}' has no shield files in it (expected shield.json, PatternN.png, IconN.png...).");
                    else
                        Jotunn.Logger.LogInfo($"[ShieldShare] Synced {count} shield(s) from '{zipName}'.");
                }
                catch (Exception ex)
                {
                    anyFailures = true;
                    Jotunn.Logger.LogError($"[ShieldShare] Failed to read '{zipName}': {ex.Message}");
                }
            }

            foreach (var packDir in FindPackFolders(dropFolder, 0))
            {
                string name = SafeFolderName(Path.GetFileName(packDir));
                try
                {
                    if (!produced.Add(name))
                    {
                        Jotunn.Logger.LogWarning($"[ShieldShare] Two shields are called '{name}' - skipping the folder '{packDir}'. Rename one of them.");
                        continue;
                    }
                    string target = PrepareTarget(shieldsFolder, name, "folder:" + packDir);
                    foreach (var file in Directory.GetFiles(packDir))
                        if (!Path.GetFileName(file).StartsWith("."))
                            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
                    Jotunn.Logger.LogInfo($"[ShieldShare] Synced shield folder '{name}'.");
                }
                catch (Exception ex)
                {
                    anyFailures = true;
                    Jotunn.Logger.LogError($"[ShieldShare] Failed to copy shield folder '{packDir}': {ex.Message}");
                }
            }

            if (anyFailures)
            {
                Jotunn.Logger.LogWarning("[ShieldShare] Skipping clean-up this launch because something failed to read.");
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
                    Jotunn.Logger.LogInfo($"[ShieldShare] Removed '{name}' - it is no longer in the drop folder.");
                }
                catch (Exception ex)
                {
                    Jotunn.Logger.LogError($"[ShieldShare] Failed to remove '{name}': {ex.Message}");
                }
            }
        }

        private static int SyncZip(string zipPath, string shieldsFolder, HashSet<string> produced)
        {
            string zipStem = Path.GetFileNameWithoutExtension(zipPath);
            int count = 0;

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                // Group file entries by the folder they sit in inside the zip. Windows' own zip tool and
                // some others write '\' separators, so normalise them.
                var byDir = new Dictionary<string, List<ZipArchiveEntry>>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in archive.Entries)
                {
                    string full = entry.FullName.Replace('\\', '/');
                    if (full.EndsWith("/") || full.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase))
                        continue;
                    string fileName = full.Substring(full.LastIndexOf('/') + 1);
                    if (fileName.StartsWith("."))
                        continue;

                    string dir = full.Contains("/") ? full.Substring(0, full.LastIndexOf('/')) : "";
                    List<ZipArchiveEntry> list;
                    if (!byDir.TryGetValue(dir, out list))
                        byDir[dir] = list = new List<ZipArchiveEntry>();
                    list.Add(entry);
                }

                foreach (var kv in byDir)
                {
                    if (!kv.Value.Any(e => ShieldPack.IsPackFile(e.Name)))
                        continue; // e.g. a readme folder

                    string rawName = kv.Key.Length == 0 ? zipStem : kv.Key.Substring(kv.Key.LastIndexOf('/') + 1);
                    string name = SafeFolderName(rawName);
                    if (!produced.Add(name))
                    {
                        Jotunn.Logger.LogWarning($"[ShieldShare] Two shields are called '{name}' - skipping the copy inside '{Path.GetFileName(zipPath)}'. Rename one of them.");
                        continue;
                    }

                    string target = PrepareTarget(shieldsFolder, name, "zip:" + Path.GetFileName(zipPath));
                    foreach (var entry in kv.Value)
                    {
                        string fileName = entry.FullName.Replace('\\', '/');
                        fileName = fileName.Substring(fileName.LastIndexOf('/') + 1);
                        entry.ExtractToFile(Path.Combine(target, fileName), true);
                    }
                    count++;
                }
            }
            return count;
        }

        /// <summary>Folders in the drop folder (searched a few levels deep) that directly contain shield files.</summary>
        private static IEnumerable<string> FindPackFolders(string dir, int depth)
        {
            if (depth >= MaxFolderDepth)
                yield break;

            foreach (var sub in Directory.GetDirectories(dir).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileName(sub);
                if (name.StartsWith(".") || name.StartsWith("_") || string.Equals(name, "__MACOSX", StringComparison.OrdinalIgnoreCase))
                    continue; // _Templates and other helper folders

                if (Directory.GetFiles(sub).Any(f => ShieldPack.IsPackFile(Path.GetFileName(f))))
                    yield return sub;

                foreach (var nested in FindPackFolders(sub, depth + 1))
                    yield return nested;
            }
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

        /// <summary>Folder names become prefab names, so keep them to letters, digits, '_' and '-'.</summary>
        public static string SafeFolderName(string name)
        {
            var chars = name.Trim().Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray();
            string safe = new string(chars).Trim('_');
            return safe.Length == 0 ? "Shield" : safe;
        }
    }
}
