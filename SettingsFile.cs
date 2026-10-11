using System.Text;

namespace DealerSelfSupply
{
    // Makes sure UserData/DealerSelfSupply.cfg is a valid file with the settings in ONE table
    // before MelonLoader reads it. MelonLoader refuses a file with the same [table] twice (it logs a
    // TOML error and ignores the whole file), and that is what happens when two versions of the
    // mod - one that still called the table [DealerSupplyRun] - have both written to it.
    // Nothing is renamed in place: every line under either name is collected, the latest value of
    // each current setting wins, and the file is rewritten once (the old one kept as .bak).
    internal static class SettingsFile
    {
        private const string Table = "DealerSelfSupply";
        private static readonly string[] OldTables = { "DealerSupplyRun" };

        // Returns what to say in the log, or null when the file was fine (or missing).
        public static string Repair(string path, ICollection<string> knownKeys)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string text = File.ReadAllText(path);
                var values = new Dictionary<string, string>();
                var tablesSeen = new List<string>();
                bool other = false; // anything that isn't ours (another table, stray text)
                int keyLines = 0;
                string current = null;
                foreach (string raw in text.Split('\n'))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    if (line.StartsWith("["))
                    {
                        current = line.Trim('[', ']', ' ');
                        tablesSeen.Add(current);
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    if (eq <= 0 || current == null) { other = true; continue; }
                    if (current != Table && !OldTables.Contains(current)) { other = true; continue; }
                    keyLines++;
                    string key = line.Substring(0, eq).Trim();
                    if (knownKeys.Contains(key)) values[key] = line.Substring(eq + 1).Trim();
                }

                // Fine as it is: one table of ours, nothing else, every line a current setting once.
                bool clean = tablesSeen.Count == 1 && tablesSeen[0] == Table && !other && keyLines == values.Count;
                if (clean) return null;

                File.Copy(path, path + ".bak", true);
                var sb = new StringBuilder("[" + Table + "]\n");
                foreach (var kv in values) sb.Append(kv.Key).Append(" = ").Append(kv.Value).Append('\n');
                File.WriteAllText(path, sb.ToString());
                return $"Repaired the settings file ({tablesSeen.Count} section(s) merged into one, {values.Count} setting(s) kept; the old file is {Path.GetFileName(path)}.bak).";
            }
            catch (Exception e)
            {
                // Unreadable: start from defaults rather than have MelonLoader trip over it.
                try { File.Move(path, path + ".bad", true); } catch { }
                return "The settings file couldn't be read (" + e.Message + ") - started from the defaults.";
            }
        }
    }
}
