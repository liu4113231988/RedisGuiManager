using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace RedisGuiManager
{
    public class Utils
    {
        private static string version;

        // Single source of truth is the csproj (<Version>/<AssemblyVersion>), so the UI can
        // never drift away from the shipped build the way a hard-coded string did.
        public static string Version
        {
            get
            {
                if (string.IsNullOrEmpty(version))
                {
                    version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
                }

                return version;
            }
            set => version = value;
        }
        private static List<string> sql_keyword_list;

        // Commands that never mutate the keyspace. Used to gate the raw command console when the
        // connection is marked read-only. Anything not listed here is treated as a write, so the
        // list errs on the side of refusing.
        private static readonly HashSet<string> read_only_commands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Keys / generic
            "GET", "MGET", "TYPE", "TTL", "PTTL", "EXPIRETIME", "PEXPIRETIME", "EXISTS", "STRLEN",
            "GETRANGE", "SUBSTR", "LCS", "RANDOMKEY", "DBSIZE", "SCAN", "DUMP",
            // Hash
            "HGET", "HGETALL", "HMGET", "HKEYS", "HVALS", "HLEN", "HEXISTS", "HSCAN", "HSTRLEN", "HRANDFIELD",
            // List
            "LRANGE", "LLEN", "LINDEX", "LPOS",
            // Set
            "SMEMBERS", "SISMEMBER", "SMISMEMBER", "SCARD", "SRANDMEMBER", "SSCAN", "SDIFF", "SINTER", "SUNION",
            // Sorted set
            "ZRANGE", "ZRANGEBYSCORE", "ZRANGEBYLEX", "ZREVRANGE", "ZREVRANGEBYSCORE", "ZREVRANGEBYLEX",
            "ZRANK", "ZREVRANK", "ZSCORE", "ZMSCORE", "ZCARD", "ZCOUNT", "ZLEXCOUNT", "ZSCAN", "ZRANDMEMBER",
            // Stream (consumer groups are excluded: XREADGROUP/XACK/XCLAIM all mutate)
            "XRANGE", "XREVRANGE", "XLEN", "XPENDING", "XREAD",
            // Bitmap / hyperloglog / geo
            "GETBIT", "BITCOUNT", "BITPOS", "BITFIELD_RO", "PFCOUNT", "GEODIST", "GEOHASH", "GEOPOS",
            // Server introspection
            "PING", "ECHO", "INFO", "TIME", "LOLWUT", "SLOWLOG_GET", "SLOWLOG_LEN",
            "LATENCY_HISTORY", "LATENCY_LATEST", "MEMORY_DOCTOR", "MEMORY_STATS",
            "ACL_LIST", "ACL_WHOAMI", "ACL_GETUSER", "ACL_CAT",
        };

        // Commands whose read-only subcommands must be listed explicitly, e.g. "CONFIG GET" is
        // safe but "CONFIG SET" is not. The subcommand is the token after the command name.
        private static readonly Dictionary<string, HashSet<string>> read_only_subcommands =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["CONFIG"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GET" },
                ["CLIENT"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "LIST", "INFO", "ID", "GETNAME", "NO-EVICT", "NO-TOUCH", "REPLY"
                },
                ["XINFO"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "STREAM", "GROUPS", "CONSUMERS"
                },
                ["OBJECT"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "ENCODING", "REFCOUNT", "IDLETIME", "FREQ", "HELP"
                },
                ["MEMORY"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "USAGE", "DOCTOR", "STATS", "MALLOC-STATS", "HELP"
                },
                ["COMMAND"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "DOCS", "INFO", "COUNT", "GETKEYS", "LIST"
                },
                ["ACL"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "LIST", "WHOAMI", "GETUSER", "CAT", "LOG"
                },
                ["LATENCY"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    // RESET is excluded: it mutates the server's monitoring state.
                    "HISTORY", "LATEST", "DOCTOR"
                },
                ["SLOWLOG"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "GET", "LEN"
                },
            };

        /// <summary>
        /// Decides whether a console command may run against a read-only connection.
        /// Refuses anything that is not provably non-mutating, including SUBSCRIBE/MONITOR
        /// (which hijack the connection) and EVAL/SCRIPT (which can write server-side).
        /// </summary>
        public static bool IsReadOnlyCommandAllowed(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return false;

            var parts = command.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;

            string head = parts[0];
            if (read_only_subcommands.TryGetValue(head, out var allowedSubs))
            {
                // CONFIG, CLIENT, ... are only safe when the subcommand is on the allow list.
                return parts.Length > 1 && allowedSubs.Contains(parts[1]);
            }

            return read_only_commands.Contains(head);
        }

        public static List<string> Sql_keyword_list
        {
            get
            {
                if (sql_keyword_list == null)
                {
                    sql_keyword_list = new List<string>()
                    {
                        "ADD","ALL","ALTER","AND","AS","ASC","BEFORE","BETWEEN","BOTH","BY","CALL","CASCADE","CHANGE","CHARACTER","CHECK","COLLATE","COLUMN","CONDITION","CONSTRAINT","CONTINUE","CONVERT","CREATE","CROSS","CURSOR","DATABASE","DATABASES","DAY_HOUR","DAY_MICROSECOND","DAY_MINUTE","DAY_SECOND","DECLARE","DEFAULT","DELAYED","DELETE","DESC","DESCRIBE","DETERMINISTIC","DISTINCT","DISTINCTROW","DIV","DROP","DUAL","EACH","ELSE","ELSEIF","ENCLOSED","ESCAPED","EXISTS","EXPLAIN","FALSE","FOR","FORCE","FOREIGN","FROM","FULLTEXT","GENERAL","GRANT","GROUP","HAVING","HIGH_PRIORITY","HOUR_MICROSECOND","HOUR_MINUTE","HOUR_SECOND","IGNORE","IGNORE_SERVER_IDS","IN","INDEX","INFILE","INNER","INOUT","INSERT","INTO","IS","JOIN","KEY","KEYS","KILL","LEADING","LEFT","LIKE","LIMIT","LINEAR","LINES","LOAD","LOCK","LOW_PRIORITY","MASTER_HEARTBEAT_PERIOD","MASTER_SSL_VERIFY_SERVER_CERT","MATCH","MAXVALUE","MINUTE_MICROSECOND","MINUTE_SECOND","MOD","MODIFIES","NATURAL","NOT","NO_WRITE_TO_BINLOG","NULL","ON","OPTIMIZE","OPTION","OPTIONALLY","OR","ORDER","OUT","OUTER","OUTFILE","PAGE_CHECKSUM","PARTITION","PRIMARY","PROCEDURE","PURGE","RANGE","READ","READS","REFERENCES","REGEXP","RELEASE","RENAME","REPLACE","REQUIRE","RESIGNAL","RESTRICT","RETURN","REVOKE","RLIKE","ROWS","SCHEMA","SECOND_MICROSECOND","SELECT","SEPARATOR","SET","SHOW","SIGNAL","SLOW","SPATIAL","SQL","SQLEXCEPTION","SQLSTATE","SQLWARNING","SQL_BIG_RESULT","SQL_CALC_FOUND_ROWS","SQL_SMALL_RESULT","STARTING","STATS_AUTO_RECALC","STATS_PERSISTENT","STATS_SAMPLE_PAGES","STRAIGHT_JOIN","TABLE","TERMINATED","TO","TRAILING","TRIGGER","TRUE","UNDO","UNION","UNIQUE","UNLOCK","UNSIGNED","UPDATE","USAGE","USE","USING","VALUES","WHERE","WITH","WRITE","XOR","YEAR_MONTH"
                    };
                }

                return sql_keyword_list;
            }
        }
        private static List<string> sql_json_func_list;
        public static List<string> Sql_json_func_list
        {
            get
            {
                if (sql_json_func_list == null)
                {
                    sql_json_func_list = new List<string>()
                    {
                        "JSON","JSON_ARRAY","JSON_ARRAY_LENGTH","JSON_EXTRACT","JSON_INSERT","JSON_OBJECT","JSON_PATCH","JSON_REMOVE","JSON_REPLACE","JSON_SET","JSON_TYPE","JSON_VALID","JSON_QUOTE","JSON_GROUP_ARRAY","JSON_GROUP_OBJECT","JSON_EACH","JSON_TREE"
                    };
                }

                return sql_json_func_list;
            }
        }

        public static void ControlDataGridViewRow(DataGridView dataGridView, int row)
        {
            int rowCount = dataGridView.RowCount;
            if (rowCount < row)
            {
                for (int i = 0; i < row - rowCount; i++)
                {
                    dataGridView.Rows.Add();
                }
            }
            else if (rowCount > row)
            {
                for (int i = 0; i < rowCount - row; i++)
                {
                    dataGridView.Rows.RemoveAt(dataGridView.RowCount - 1);
                }
            }

            foreach (DataGridViewRow viewRow in dataGridView.SelectedRows)
            {
                viewRow.Selected = false;
            }
        }

        public static string GetSizeDescription(int byte_size)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            while (byte_size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                byte_size = byte_size / 1024;
            }

            // Adjust the format string to your preferences. For example "{0:0.#}{1}" would
            // show a single decimal place, and no space.
            return string.Format("{0:0.##} {1}", byte_size, sizes[order]);
        }

		// Convert an object to a byte array
		public static byte[] ObjectToByteArray(object obj)
		{
			if (obj == null)
				return null;

			try
			{
				string json = JsonConvert.SerializeObject(obj);
				return Encoding.UTF8.GetBytes(json);
			}
			catch (Exception ex)
			{
				throw new InvalidOperationException("Serialization to byte[] failed:", ex);
			}
		}

		// Convert a byte array to an Object
		public static object ByteArrayToObject(byte[] arrBytes)
		{
			if (arrBytes == null || arrBytes.Length == 0)
				return null;

			try
				{
					string json = Encoding.UTF8.GetString(arrBytes);
					return JsonConvert.DeserializeObject(json);
				}
			catch (Exception ex)
			{
				throw new InvalidOperationException("Deserialization from byte[] failed:", ex);
			}
		}

		public static bool RedisGlobMatch(string pattern, string str)
        {
            unsafe
            {
                fixed (char* ppattern = pattern)
                {
                    fixed (char* pstr = str)
                    {
                        return RedisGlobMatch(ppattern, pattern.Length, pstr, str.Length);
                    }
                }
            }
        }

        public unsafe static bool RedisGlobMatch(char* pattern, int patternLen, char* str, int stringLen)
        {
            // An empty key name is legal in Redis and "*" must match it. The loop below never runs
            // when stringLen is 0, so handle that case up front.
            if (stringLen == 0)
            {
                while (patternLen > 0 && *pattern == '*')
                {
                    pattern++;
                    patternLen--;
                }

                return patternLen == 0;
            }

            while (patternLen > 0 && stringLen > 0)
            {
                switch (pattern[0])
                {
                    case '*':
                    {
                        while (patternLen > 0 && pattern[1] == '*')
                        {
                            pattern++;
                            patternLen--;
                        }

                        if (patternLen == 1)
                        {
                            return true; /* match */
                        }

                        while (stringLen > 0)
                        {
                            if (RedisGlobMatch(pattern + 1, patternLen - 1, str, stringLen))
                            {
                                return true; /* match */
                            }

                            str++;
                            stringLen--;
                        }

                        return false; /* no match */
                    }

                    case '?':
                    {
                        str++;
                        stringLen--;
                    }
                    break;

                    case '[':
                    {
                        bool not = false;
                        bool match = false;

                        pattern++;
                        patternLen--;
                        not = pattern[0] == '^';

                        if (not)
                        {
                            pattern++;
                            patternLen--;
                        }

                        match = false;

                        while (true)
                        {
                            if (pattern[0] == '\\' && patternLen >= 2)
                            {
                                pattern++;
                                patternLen--;

                                if (pattern[0] == str[0])
                                {
                                    match = true;
                                }
                            }
                            else if (pattern[0] == ']')
                            {
                                break;
                            }
                            else if (patternLen == 0)
                            {
                                pattern--;
                                patternLen++;

                                break;
                            }
                            else if (patternLen >= 3 && pattern[1] == '-')
                            {
                                int start = pattern[0];
                                int end = pattern[2];
                                int c = str[0];

                                if (start > end)
                                {
                                    int t = start;
                                    start = end;
                                    end = t;
                                }

                                pattern += 2;
                                patternLen -= 2;

                                if (c >= start && c <= end)
                                {
                                    match = true;
                                }
                            }
                            else
                            {
                                if (pattern[0] == str[0])
                                {
                                    match = true;
                                }
                            }

                            pattern++;
                            patternLen--;
                        }

                        if (not)
                        {
                            match = !match;
                        }

                        if (match == false)
                        {
                            return false; /* no match */
                        }

                        str++;
                        stringLen--;
                    }
                    break;

                    /* fall through */
                    default:
                    {
                        if (pattern[0] != str[0])
                        {
                            return false; /* no match */
                        }

                        str++;
                        stringLen--;
                    }
                    break;
                }

                pattern++;
                patternLen--;

                if (stringLen == 0)
                {
                    while (*pattern == '*')
                    {
                        pattern++;
                        patternLen--;
                    }
                    break;
                }
            }

            if (patternLen == 0 && stringLen == 0)
            {
                return true;
            }

            return false;
        }

        public static int FreeTcpPort()
        {
            TcpListener l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        public static void DarkThemeForm(Form form)
        {
            form.BackColor = DarkColors.GreyBackground;
            form.ForeColor = DarkColors.LightText;
            DarkThemeControls(form.Controls);
        }

        public static void DarkThemeControls(Control.ControlCollection controls)
                {
                    foreach (Control component in controls)
                    {
                        DarkThemeControl(component);
                    }
                }

                public static void DarkThemeControl(Control component)
                {
                    if (component == null) return;

                    if (component is DataGridView grid)
                    {
                        grid.EnableHeadersVisualStyles = false;
                        grid.BackgroundColor = DarkColors.GreyHighlight;
                        grid.BorderStyle = BorderStyle.None;
                        grid.GridColor = DarkColors.GreySelection;
                        grid.DefaultCellStyle.BackColor = DarkColors.GreyBackground;
                        grid.DefaultCellStyle.ForeColor = DarkColors.LightText;
                        grid.DefaultCellStyle.SelectionBackColor = DarkColors.GreySelection;
                        grid.DefaultCellStyle.SelectionForeColor = Color.White;
                        grid.AlternatingRowsDefaultCellStyle.BackColor = DarkColors.GreyBackground;
                        grid.AlternatingRowsDefaultCellStyle.ForeColor = DarkColors.LightText;
                        grid.ColumnHeadersDefaultCellStyle.BackColor = DarkColors.GreyBackground;
                        grid.ColumnHeadersDefaultCellStyle.ForeColor = DarkColors.LightText;
                        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = DarkColors.GreyBackground;
                        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = DarkColors.LightText;
                        grid.RowHeadersDefaultCellStyle.BackColor = DarkColors.GreyBackground;
                        grid.RowHeadersDefaultCellStyle.ForeColor = DarkColors.LightText;
                    }
                    else if (component is ComboBox combo)
                    {
                        combo.FlatStyle = FlatStyle.Flat;
                        combo.BackColor = DarkColors.GreyBackground;
                        combo.ForeColor = DarkColors.LightText;
                    }
                    else if (component is NumericUpDown numeric)
                                {
                                    // UpDownBase keeps its spinner in an internal child control that paints the
                                    // arrows with its own colours, so theme that child as well.
                                    numeric.BackColor = DarkColors.GreyBackground;
                                    numeric.ForeColor = DarkColors.LightText;
                                    numeric.BorderStyle = BorderStyle.FixedSingle;
                                    foreach (Control spinner in numeric.Controls)
                                    {
                                        spinner.BackColor = DarkColors.GreyBackground;
                                        spinner.ForeColor = DarkColors.LightText;
                                    }
                                }
                    else if (component is TabControl tabs)
                    {
                        tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
                        tabs.ForeColor = DarkColors.LightText;
                        DarkThemeControls(tabs.Controls);
                    }
                    else if (component is TabPage tabPage)
                    {
                        tabPage.BackColor = DarkColors.GreyBackground;
                        tabPage.ForeColor = DarkColors.LightText;
                        DarkThemeControls(tabPage.Controls);
                    }
                    else if (component is ToolStrip strip)
                                {
                                    // ContextMenuStrip derives from ToolStrip, so this covers both.
                                    strip.BackColor = DarkColors.GreyBackground;
                                    strip.ForeColor = DarkColors.LightText;
                                    DarkThemeToolStripItems(strip.Items);
                                }
                    else if (component is LinkLabel)
                    {
                        component.BackColor = DarkColors.GreyBackground;
                        component.ForeColor = Color.LightBlue;
                    }
                    else if (component is Button button)
                    {
                        button.BackColor = DarkColors.GreyBackground;
                        button.ForeColor = DarkColors.LightText;
                        button.FlatStyle = FlatStyle.Flat;
                        button.FlatAppearance.BorderColor = DarkColors.GreySelection;
                    }
                    else if (component is CheckBox checkBox)
                    {
                        checkBox.BackColor = DarkColors.GreyBackground;
                        checkBox.ForeColor = DarkColors.LightText;
                        checkBox.FlatStyle = FlatStyle.Flat;
                        checkBox.FlatAppearance.BorderColor = DarkColors.LightText;
                    }
                    else if (component is RadioButton radio)
                    {
                        radio.BackColor = DarkColors.GreyBackground;
                        radio.ForeColor = DarkColors.LightText;
                        radio.FlatStyle = FlatStyle.Flat;
                        radio.FlatAppearance.BorderColor = DarkColors.LightText;
                    }
                    else if (component is ProgressBar progress)
                    {
                        progress.BackColor = DarkColors.GreyBackground;
                        progress.ForeColor = DarkColors.LightText;
                    }
                    else if (component is ListView listView)
                    {
                        listView.BackColor = DarkColors.GreyBackground;
                        listView.ForeColor = DarkColors.LightText;
                    }

                    // Containers and everything not handled above still need the base colours,
                    // otherwise labels/groups keep the light default background in dark mode.
                    component.BackColor = DarkColors.GreyBackground;
                    component.ForeColor = DarkColors.LightText;

                    if (component is SplitContainer split)
                    {
                        DarkThemeControl(split.Panel1);
                        DarkThemeControl(split.Panel2);
                    }
                    else
                    {
                        DarkThemeControls(component.Controls);
                    }
                }

                private static void DarkThemeToolStripItems(ToolStripItemCollection items)
                        {
                            foreach (ToolStripItem item in items)
                            {
                                item.BackColor = DarkColors.GreyBackground;
                                item.ForeColor = DarkColors.LightText;

                                // ToolStripDropDownItem is the ToolStripItem subtype that owns a submenu.
                                if (item is ToolStripDropDownItem dropDown)
                                {
                                    dropDown.BackColor = DarkColors.GreyBackground;
                                    dropDown.ForeColor = DarkColors.LightText;
                                    DarkThemeToolStripItems(dropDown.DropDownItems);
                                }
                            }
                        }
    }

    public static class ExtensionMethods
    {
        public static void DoubleBuffered(this DataGridView dgv, bool setting)
        {
            Type dgvType = dgv.GetType();

            PropertyInfo pi = dgvType.GetProperty("DoubleBuffered",
                BindingFlags.Instance | BindingFlags.NonPublic);

            pi.SetValue(dgv, setting, null);
        }
    }
}
