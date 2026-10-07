using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace RedisGuiManager
{
    public class Config
    {
        public static int scan_page_count = 1000;
        public static int dp_type;
        public static int mainform_is_maximized;
        public static int mainform_pos_x;
        public static int mainform_pos_y;
        public static int mainform_width = 1159;
        public static int mainform_height = 712;
        public static int darkmode = 0;
        public Config()
        {
        }

        // Keep settings next to the executable, matching how connection files are stored.
        // A relative path made the settings file depend on the current working directory.
        public static string ConfigPath =>
            overridePath ?? Path.Combine(Application.StartupPath, "config.json");

        /// <summary>
        /// Redirects <see cref="ConfigPath"/>. Used by tests so they never touch the real user
        /// settings file; null restores the default location.
        /// </summary>
        public static string OverridePath
        {
            get => overridePath;
            set => overridePath = value;
        }

        private static string overridePath;

        public static void Load()
        {
            foreach (string path in CandidatePaths())
            {
                if (File.Exists(path) == false) continue;

                try
                {
                    string config_json = File.ReadAllText(path);
                    LoadFrom(config_json);
                    return;
                }
                catch (Exception)
                {
                    // Fall through and try the next candidate (usually the .bak written on save).
                }
            }
        }

        private static void LoadFrom(string config_json)
        {
            var json = JsonConvert.DeserializeObject<Dictionary<string, object>>(config_json);
            if (json == null)
            {
                return;
            }

            if (json.ContainsKey("dp_type"))
            {
                dp_type = Convert.ToInt32(json["dp_type"]);
            }

            if (json.ContainsKey("mainform_is_maximized"))
            {
                mainform_is_maximized = Convert.ToInt32(json["mainform_is_maximized"]);
            }
            if (json.ContainsKey("mainform_pos_x"))
            {
                mainform_pos_x = Convert.ToInt32(json["mainform_pos_x"]);
            }
            if (json.ContainsKey("mainform_pos_y"))
            {
                mainform_pos_y = Convert.ToInt32(json["mainform_pos_y"]);
            }
            if (json.ContainsKey("mainform_width"))
            {
                mainform_width = Convert.ToInt32(json["mainform_width"]);
            }
            if (json.ContainsKey("mainform_height"))
            {
                mainform_height = Convert.ToInt32(json["mainform_height"]);
            }
            if (json.ContainsKey("darkmode"))
            {
                darkmode = Convert.ToInt32(json["darkmode"]);
            }
        }

        public static void Save()
        {
            Dictionary<string, object> dic_config = new Dictionary<string, object>();
            dic_config.Add("dp_type", dp_type);
            dic_config.Add("mainform_is_maximized", mainform_is_maximized);
            dic_config.Add("mainform_pos_x", mainform_pos_x);
            dic_config.Add("mainform_pos_y", mainform_pos_y);
            dic_config.Add("mainform_width", mainform_width);
            dic_config.Add("mainform_height", mainform_height);
            dic_config.Add("darkmode", darkmode);
            string save_string = JsonConvert.SerializeObject(dic_config).ToString();

            string path = ConfigPath;
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir) == false && Directory.Exists(dir) == false)
                {
                    Directory.CreateDirectory(dir);
                }

                // Write to a temporary file first, then swap it in, so a crash mid-write
                // can never leave a truncated settings file behind.
                string tmp = path + ".tmp";
                try
                {
                    File.WriteAllText(tmp, save_string, Encoding.UTF8);
                    if (File.Exists(path))
                    {
                        File.Replace(tmp, path, path + ".bak");
                    }
                    else
                    {
                        File.Move(tmp, path);
                    }
                }
                catch
                {
                    // A leftover .tmp would shadow nothing but confuses the next attempt, and the
                    // old value stays authoritative, so clean it up before reporting.
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                    throw;
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Save failed for {path}\r\n{ex.Message}");
            }
        }

        // The current file, its backup, and the legacy working-directory-relative file.
        private static IEnumerable<string> CandidatePaths()
        {
            string path = ConfigPath;
            string bak = path + ".bak";
            string legacy = Path.GetFullPath("config.json");

            foreach (string candidate in new[] { path, bak })
            {
                if (string.Equals(candidate, legacy, StringComparison.OrdinalIgnoreCase) == false)
                {
                    yield return candidate;
                }
            }

            if (File.Exists(legacy))
            {
                yield return legacy;
            }
        }
    }
}