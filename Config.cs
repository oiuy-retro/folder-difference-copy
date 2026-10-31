using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FolderDiffCopy
{
    // Reads and updates configuration.conf, the plain text settings file that
    // sits next to the exe. The layout is shared with the other oiuy-retro
    // Windows apps: [tools] for outside programs, [folders] for the last
    // folder each picker used, and a section per app for anything else.
    // Lines starting with # are comments. When the app saves a value it only
    // changes that one line, so anything the user wrote by hand stays put.
    internal sealed class Config
    {
        public const string FileName = "configuration.conf";
        const string AppDataFolder = "Folder Difference Copy";

        const string Template =
            "# Folder Difference Copy settings. Lines starting with # are comments.\r\n" +
            "# Relative paths start from this file's folder, and %VARS% such as\r\n" +
            "# %USERPROFILE% are expanded.\r\n" +
            "\r\n" +
            "[tools]\r\n" +
            "# Folder Difference Copy doesn't use any outside programs.\r\n" +
            "\r\n" +
            "[folders]\r\n" +
            "# The folder each Browse button opens at. The app updates these\r\n" +
            "# lines itself after you pick a folder.\r\n" +
            "a =\r\n" +
            "b =\r\n";

        readonly string mainPath;
        readonly string fallbackPath;
        readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public Config(string mainPath, string fallbackPath)
        {
            this.mainPath = mainPath;
            this.fallbackPath = fallbackPath;
        }

        public static Config ForApp()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string appData = null;
            try
            {
                string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (!string.IsNullOrEmpty(roaming))
                    appData = Path.Combine(Path.Combine(roaming, AppDataFolder), FileName);
            }
            catch
            {
            }
            var config = new Config(Path.Combine(exeDir, FileName), appData);
            config.Load();
            return config;
        }

        public string MainPath { get { return mainPath; } }

        // Loads the file next to the exe, writing the commented template first
        // if it is missing. Values saved to the AppData copy (used when the exe
        // folder can't be written to) win over the same keys next to the exe.
        // Never throws: a missing or unreadable file just means defaults.
        public void Load()
        {
            values.Clear();
            if (!File.Exists(mainPath))
                TryWrite(mainPath, Template);
            ReadInto(mainPath);
            if (fallbackPath != null)
                ReadInto(fallbackPath);
        }

        public string Get(string section, string key)
        {
            string value;
            return values.TryGetValue(section + "\n" + key, out value) ? value : null;
        }

        // Returns the value with %VARS% expanded and a relative path turned into
        // one that starts from the config file's folder. Empty means not set.
        public string GetPath(string section, string key)
        {
            string raw = Get(section, key);
            if (string.IsNullOrEmpty(raw)) return null;
            string expanded = Environment.ExpandEnvironmentVariables(raw);
            if (expanded.StartsWith("::", StringComparison.Ordinal) || expanded.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                return expanded;
            try
            {
                if (!Path.IsPathRooted(expanded))
                    expanded = Path.Combine(Path.GetDirectoryName(mainPath), expanded);
                return Path.GetFullPath(expanded);
            }
            catch
            {
                return expanded;
            }
        }

        // Saves one value. Tries the file next to the exe first and falls back
        // to AppData if that folder is read-only (for example Program Files).
        // If both fail the value is kept for this session only.
        public void Set(string section, string key, string value)
        {
            value = value ?? "";
            values[section + "\n" + key] = value;
            if (UpdateFile(mainPath, section, key, value)) return;
            if (fallbackPath == null) return;
            try
            {
                string dir = Path.GetDirectoryName(fallbackPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            catch
            {
                return;
            }
            UpdateFile(fallbackPath, section, key, value);
        }

        void ReadInto(string path)
        {
            string[] lines;
            try
            {
                if (!File.Exists(path)) return;
                lines = File.ReadAllLines(path, Encoding.UTF8);
            }
            catch
            {
                return;
            }
            string section = "";
            foreach (string line in lines)
            {
                string name;
                if (IsSection(line, out name))
                {
                    section = name;
                    continue;
                }
                string key, value;
                if (IsKey(line, out key, out value))
                    values[section + "\n" + key] = value;
            }
        }

        internal static bool IsSection(string line, out string name)
        {
            name = null;
            string t = line.Trim();
            if (t.Length < 2 || t[0] != '[' || t[t.Length - 1] != ']') return false;
            name = t.Substring(1, t.Length - 2).Trim();
            return true;
        }

        internal static bool IsKey(string line, out string key, out string value)
        {
            key = null;
            value = null;
            string t = line.Trim();
            if (t.Length == 0 || t[0] == '#' || t[0] == ';' || t[0] == '[') return false;
            int eq = t.IndexOf('=');
            if (eq <= 0) return false;
            key = t.Substring(0, eq).Trim();
            value = t.Substring(eq + 1).Trim();
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                value = value.Substring(1, value.Length - 2);
            return key.Length > 0;
        }

        // Rewrites only the line for this key, adding the key (and section if
        // needed) when it isn't there yet. Everything else is copied as is.
        internal static string UpdateText(string text, string section, string key, string value)
        {
            string newline = text.Contains("\r\n") || text.Length == 0 ? "\r\n" : "\n";
            var lines = new List<string>(text.Replace("\r\n", "\n").Split('\n'));
            bool endsWithNewline = lines.Count > 0 && lines[lines.Count - 1].Length == 0;
            if (endsWithNewline) lines.RemoveAt(lines.Count - 1);

            string newLine = key + " = " + value;
            int sectionStart = -1;
            int insertAt = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                string name;
                if (IsSection(lines[i], out name))
                {
                    if (sectionStart >= 0) break;
                    if (string.Equals(name, section, StringComparison.OrdinalIgnoreCase))
                    {
                        sectionStart = i;
                        insertAt = i + 1;
                    }
                    continue;
                }
                if (sectionStart < 0) continue;
                string k, v;
                if (IsKey(lines[i], out k, out v))
                {
                    if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = newLine;
                        return Join(lines, newline);
                    }
                    insertAt = i + 1;
                }
                else if (lines[i].Trim().Length > 0)
                {
                    insertAt = i + 1;
                }
            }
            if (sectionStart >= 0)
            {
                lines.Insert(insertAt, newLine);
            }
            else
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0) lines.Add("");
                lines.Add("[" + section + "]");
                lines.Add(newLine);
            }
            return Join(lines, newline);
        }

        static string Join(List<string> lines, string newline)
        {
            return string.Join(newline, lines.ToArray()) + newline;
        }

        static bool UpdateFile(string path, string section, string key, string value)
        {
            try
            {
                string text = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : Template;
                return TryWrite(path, UpdateText(text, section, key, value));
            }
            catch
            {
                return false;
            }
        }

        static bool TryWrite(string path, string text)
        {
            string temp = path + ".tmp";
            try
            {
                File.WriteAllText(temp, text, new UTF8Encoding(false));
                if (File.Exists(path))
                    File.Replace(temp, path, null);
                else
                    File.Move(temp, path);
                return true;
            }
            catch
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                return false;
            }
        }

        // The nearest folder that still exists, walking up from the saved one.
        // Returns null when nothing on that drive is left (an unplugged drive).
        public static string NearestExisting(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                string dir = path;
                while (!string.IsNullOrEmpty(dir))
                {
                    if (Directory.Exists(dir)) return dir;
                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch
            {
            }
            return null;
        }
    }
}
