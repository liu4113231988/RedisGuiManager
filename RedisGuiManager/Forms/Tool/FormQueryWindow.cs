using Newtonsoft.Json.Linq;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using RedisGuiManager.Properties;
using System.Data.SQLite;
using System.IO;
using System.Threading;
using AutocompleteMenuNS;

namespace RedisGuiManager
{
    public partial class FormQueryWindow : Form
    {
        public enum RedisKeyType
        {
            String = 0,
            List,
            Set,
            Zset,
            Hash,
            Stream,
        }
        private SQLiteConnection sqlite_con;
        private string table_name = "";
        private DataSet ds;
        private RedisClient redis_client;
        private int db_num;
        private bool is_querying = false;
        private bool is_stop_query = false;
        private CancellationTokenSource queryCancellation;
        private readonly NumericUpDown keyLimit = new NumericUpDown { Minimum = 1, Maximum = 1000000, Value = 1000, Width = 95 };
        private readonly NumericUpDown entryLimit = new NumericUpDown { Minimum = 1, Maximum = 1000000, Value = 500, Width = 95 };
        private readonly PageNavigator resultPages = new PageNavigator();
        private int activeEntryLimit;
        private int activeKeyLimit;
        private readonly OperationReport queryReport = new OperationReport();
        private List<AutocompleteItem> list_auto_complete_static = new List<AutocompleteItem>();

        public FormQueryWindow()
        {
            InitializeComponent();
            var limits = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34 };
            limits.Controls.AddRange(new Control[] { new Label { Text = "Snapshot: max keys/DB", AutoSize = true }, keyLimit, new Label { Text = "rows/key", AutoSize = true }, entryLimit, new Label { Text = "SQL uses this subset; results capped at 10,000 rows", AutoSize = true } });
            foreach (Control control in Controls) if (control.Dock == DockStyle.None) { control.Top += 34; if ((control.Anchor & AnchorStyles.Bottom) != 0 && control.Height > 100) control.Height -= 70; }
            Controls.Add(limits);
            Controls.Add(resultPages);
            resultPages.PageChanged += () => { RenderResultPage(); return Task.CompletedTask; };
            setting_richtextbox();

            if (Config.darkmode > 0)
            {
                Utils.DarkThemeForm(this);
            }

            comboBox_keys_type.SelectedIndex = (int)RedisKeyType.Hash;
            dataGridView_query_result.DoubleBuffered(true);
            GridUi.LimitCellText(dataGridView_query_result);
            GridUi.AttachCellValueMenu(dataGridView_query_result,
                () => dataGridView_query_result.SelectedCells.Count > 0 ? dataGridView_query_result.SelectedCells[0].Value?.ToString() ?? "" : "",
                CM_remove_selected_keys);

            sqlite_con = new SQLiteConnection($"Data Source=:memory:;Version=3;");
            sqlite_con.Open();
            sqlite_con.EnableExtensions(true);

            // The JSON1 extension lives in SQLite.Interop.dll; load it from the app directory
            // first and fail with an actionable message instead of a raw DllNotFoundException.
            if (TryLoadJsonExtension() == false)
            {
                MessageBox.Show(
                    "The SQLite JSON1 extension could not be loaded.\r\n\r\n" +
                    $"Expected: {Path.Combine(AppContext.BaseDirectory, "SQLite.Interop.dll")}\r\n" +
                    $"Process architecture: {(IntPtr.Size == 8 ? "x64" : "x86")}\r\n\r\n" +
                    "JSON functions (json_extract, json_each, ...) are unavailable in this session.",
                    "Query window", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            table_name = "t_" + Guid.NewGuid().ToString().Replace("-", "");
            textBox_table.Text = table_name;

            richTextBox_query.Text = $"SELECT *\nFROM {table_name}\n";
            richTextBox_query.ProcessAllLines();

            list_auto_complete_static = new List<AutocompleteItem>();
            foreach (var item in Utils.Sql_keyword_list)
            {
                list_auto_complete_static.Add(new AutocompleteItem(item, 2));
            }
            foreach (var item in Utils.Sql_json_func_list)
            {
                list_auto_complete_static.Add(new AutocompleteItem(item, 1));
            }
            list_auto_complete_static.Add(new AutocompleteItem("a_key", 3));
            list_auto_complete_static.Add(new AutocompleteItem("a_index", 3));
            list_auto_complete_static.Add(new AutocompleteItem("a_value", 3));
            list_auto_complete_static.Add(new AutocompleteItem("a_score", 3));
            list_auto_complete_static.Add(new AutocompleteItem(table_name, 0));

            ImageList imageList = new ImageList();
            imageList.Images.Add("Table_748", Properties.Resources.Table_748);
            imageList.Images.Add("Method_636", Properties.Resources.Method_636);
            imageList.Images.Add("keyword", Properties.Resources.keyword);
            imageList.Images.Add("Structure_507", Properties.Resources.Structure_507);

            autocompleteMenu.SetAutocompleteItems(list_auto_complete_static);
            autocompleteMenu.AppearInterval = 200;
            autocompleteMenu.MinFragmentLength = 1;
            autocompleteMenu.ImageList = imageList;
        }

        private bool TryLoadJsonExtension()
        {
            // Preferred: resolve next to the executable, then fall back to the default probe path.
            string dllPath = Path.Combine(AppContext.BaseDirectory, "SQLite.Interop.dll");
            foreach (string candidate in new[] { dllPath, "SQLite.Interop.dll" })
            {
                try
                {
                    sqlite_con.LoadExtension(candidate, "sqlite3_json_init");
                    return true;
                }
                catch (Exception)
                {
                    // Try the next candidate; a missing or mismatched DLL must not abort startup.
                }
            }

            return false;
        }

        private void FormQueryWindow_Shown(object sender, EventArgs e)
        {
            richTextBox_query.Select(richTextBox_query.Text.Length, 0);
            richTextBox_query.Focus();
        }

        // Raw Application.DoEvents() is re-entrant: it can dispatch a nested message loop, so a
        // second DoEvents (or a close/grid edit) can run in the middle of a query and mutate the
        // SQLite connection and DataSet the query is still writing to. This wrapper keeps the UI
        // responsive while refusing to re-enter or to pump once the window is going away.
        private bool pumping;
        private bool closing;

        private void PumpUi()
        {
            if (closing || IsDisposed || Disposing || !IsHandleCreated) return;
            if (is_stop_query || pumping) return;

            pumping = true;
            try
            {
                Application.DoEvents();
            }
            catch (Exception)
            {
                // A failure inside a nested pump must not abort the query.
            }
            finally
            {
                pumping = false;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Stop pumping before any teardown so an in-flight query cannot resurrect the window.
            closing = true;
            queryCancellation?.Cancel();
            base.OnFormClosing(e);
        }

        private void setting_richtextbox()
        {
            richTextBox_query.Settings.Keywords = Utils.Sql_keyword_list;
            richTextBox_query.Settings.Comment = "--";
            richTextBox_query.Settings.KeywordColor = Color.Blue;
            richTextBox_query.Settings.CommentColor = Color.Gray;
            richTextBox_query.Settings.StringColor = Color.Green;
            richTextBox_query.Settings.IntegerColor = Color.Purple;
            richTextBox_query.Settings.EnableStrings = true;
            richTextBox_query.Settings.EnableIntegers = true;

            if (Config.darkmode > 0)
            {
                richTextBox_query.Settings.KeywordColor = Color.LightSkyBlue;
                richTextBox_query.Settings.CommentColor = Color.Gray;
                richTextBox_query.Settings.StringColor = Color.LightGreen;
                richTextBox_query.Settings.IntegerColor = Color.MediumPurple;
            }

            richTextBox_query.CompileKeywords();
        }

        public void SetServerInfo(RedisClient redis_client, int db_num)
        {
            textBox_server_info.Text = string.Format("[{0}] ({1}:{2} DB_{3})"
                , redis_client.Settings.name
                , redis_client.Settings.host
                , redis_client.Settings.port
                , db_num == -1 ? $"All [0 ~ {(redis_client.Settings.use_cluster ? 0 : redis_client.RedisServer.DatabaseCount - 1)}]" : db_num.ToString()
                );

            this.redis_client = redis_client;
            this.db_num = db_num;
        }

        public void SetRedisKeysFilter(string keys_filter)
        {
            textBox_keys_filter.Text = keys_filter;
        }
        public void SetRedisKeysType(RedisType type)
        {
            RedisKeyType my_type = RedisKeyType.Hash;
            switch(type)
            {
                case RedisType.String:
                {
                    my_type = RedisKeyType.String;
                }
                break;
                case RedisType.List:
                {
                    my_type = RedisKeyType.List;
                }
                break;
                case RedisType.Set:
                {
                    my_type = RedisKeyType.Set;
                }
                break;
                case RedisType.SortedSet:
                {
                    my_type = RedisKeyType.Zset;
                }
                break;
                case RedisType.Hash:
                {
                    my_type = RedisKeyType.Hash;
                }
                break;
                case RedisType.Stream:
                {
                    my_type = RedisKeyType.Stream;
                }
                break;
            }

            comboBox_keys_type.SelectedIndex = (int)my_type;
        }

        public void SetRedisKeysType(RedisKeyType type)
        {
            comboBox_keys_type.SelectedIndex = (int)type;
        }

        private async Task query_string()
        {
            treeView_field_names.Nodes.Add("a_db");
			treeView_field_names.Nodes.Add("a_key");
			treeView_field_names.Nodes.Add("a_value");

            string sql = $"CREATE TABLE {table_name} (a_db INTEGER, a_key TEXT, a_value TEXT)";
            SQLiteCommand command = new SQLiteCommand(sql, sqlite_con);
            int result = command.ExecuteNonQuery();

            int db_num_start = db_num == -1 ? 0 : db_num;
            int db_num_end = db_num == -1 ? (redis_client.Settings.use_cluster ? 0 : redis_client.RedisServer.DatabaseCount - 1) : db_num;

            for (int i = db_num_start; i <= db_num_end; ++i)
            {
                IDatabase redis = redis_client.GetDB(i);
				toolStripStatusLabel_status.Text = $"Getting DB_{i} keys...";
				PumpUi();

				var keys = await FetchKeys(i);

				toolStripProgressBar_status.Value = 0;
				toolStripProgressBar_status.Maximum = keys.Count();

				foreach (var key in keys)
                {
                    ++toolStripProgressBar_status.Value;
                    toolStripStatusLabel_status.Text = $"Getting DB_{i} values...({toolStripProgressBar_status.Value} / {toolStripProgressBar_status.Maximum})";
                    PumpUi();

                    try
                    {
                        var val = await redis.StringGetAsync(key);
                        if (is_stop_query)
                        {
                            return;
                        }

                        sql = $"INSERT INTO {table_name} (a_db, a_key, a_value) VALUES (@Db, @Key, @Value);";
                        command = new SQLiteCommand(sql, sqlite_con);
                        command.Parameters.AddWithValue("Db", i);
						command.Parameters.AddWithValue("Key", key.ToString());
						command.Parameters.AddWithValue("Value", val.ToString());

                        command.ExecuteNonQuery();
                    }
                    catch (RedisServerException redis_ex)
                    {
                        if (!redis_ex.Message.StartsWith("WRONGTYPE", StringComparison.OrdinalIgnoreCase)) queryReport.Errors.Add(redis_ex.Message);

                        if (is_stop_query)
                        {
                            return;
                        }
                    }
                }
            }
        }

        private async Task query_list()
        {
            treeView_field_names.Nodes.Add("a_db");
            treeView_field_names.Nodes.Add("a_key");
            treeView_field_names.Nodes.Add("a_index");
            treeView_field_names.Nodes.Add("a_value");

            string sql = $"CREATE TABLE {table_name} (a_db INTEGER, a_key TEXT, a_index INTEGER, a_value TEXT)";
            SQLiteCommand command = new SQLiteCommand(sql, sqlite_con);
            int result = command.ExecuteNonQuery();

			int db_num_start = db_num == -1 ? 0 : db_num;
			int db_num_end = db_num == -1 ? (redis_client.Settings.use_cluster ? 0 : redis_client.RedisServer.DatabaseCount - 1) : db_num;

            for (int i = db_num_start; i <= db_num_end; ++i)
            {
				IDatabase redis = redis_client.GetDB(i);
				toolStripStatusLabel_status.Text = $"Getting DB_{i} keys...";
				PumpUi();

				var keys = await FetchKeys(i);

				toolStripProgressBar_status.Value = 0;
				toolStripProgressBar_status.Maximum = keys.Count();

				foreach (var key in keys)
                {
                    ++toolStripProgressBar_status.Value;
                    toolStripStatusLabel_status.Text = $"Getting DB_{i} values...({toolStripProgressBar_status.Value} / {toolStripProgressBar_status.Maximum})";
                    PumpUi();

                    try
                    {
                        var vals = await redis.ListRangeAsync(key, 0, activeEntryLimit - 1);
                        if (is_stop_query)
                        {
                            return;
                        }

                        int index = 0;
                        foreach (var val in vals)
                        {
                            sql = $"INSERT INTO {table_name} (a_db, a_key, a_index, a_value) VALUES (@Db, @Key, @Index, @Value);";
                            command = new SQLiteCommand(sql, sqlite_con);
                            command.Parameters.AddWithValue("Db", i);
							command.Parameters.AddWithValue("Key", key.ToString());
							command.Parameters.AddWithValue("Index", index);
                            command.Parameters.AddWithValue("Value", val.ToString());

                            command.ExecuteNonQuery();
                            ++index;
                        }
                    }
                    catch (RedisServerException redis_ex)
                    {
                        if (!redis_ex.Message.StartsWith("WRONGTYPE", StringComparison.OrdinalIgnoreCase)) queryReport.Errors.Add(redis_ex.Message);

                        if (is_stop_query)
                        {
                            return;
                        }
                    }
                }
            }
        }

        private async Task query_set()
        {
            treeView_field_names.Nodes.Add("a_db");
			treeView_field_names.Nodes.Add("a_key");
			treeView_field_names.Nodes.Add("a_index");
            treeView_field_names.Nodes.Add("a_value");

            string sql = $"CREATE TABLE {table_name} (a_db INTEGER, a_key TEXT, a_index INTEGER, a_value TEXT)";
            SQLiteCommand command = new SQLiteCommand(sql, sqlite_con);
            int result = command.ExecuteNonQuery();

			int db_num_start = db_num == -1 ? 0 : db_num;
			int db_num_end = db_num == -1 ? (redis_client.Settings.use_cluster ? 0 : redis_client.RedisServer.DatabaseCount - 1) : db_num;

            for (int i = db_num_start; i <= db_num_end; ++i)
            {
				IDatabase redis = redis_client.GetDB(i);
				toolStripStatusLabel_status.Text = $"Getting DB_{i} keys...";
				PumpUi();

				var keys = await FetchKeys(i);

				toolStripProgressBar_status.Value = 0;
				toolStripProgressBar_status.Maximum = keys.Count();

				foreach (var key in keys)
                {
                    ++toolStripProgressBar_status.Value;
                    toolStripStatusLabel_status.Text = $"Getting DB_{i} values...({toolStripProgressBar_status.Value} / {toolStripProgressBar_status.Maximum})";

                    try
                    {
                        var vals = await ReadSnapshot(redis.SetScan(key), activeEntryLimit);
                        if (is_stop_query)
                        {
                            return;
                        }

                        int index = 0;
                        foreach (var val in vals)
                        {
                            sql = $"INSERT INTO {table_name} (a_db, a_key, a_index, a_value) VALUES (@Db, @Key, @Index, @Value);";
                            command = new SQLiteCommand(sql, sqlite_con);
                            command.Parameters.AddWithValue("Db", i);
                            command.Parameters.AddWithValue("Key", key.ToString());
                            command.Parameters.AddWithValue("Index", index);
                            command.Parameters.AddWithValue("Value", val.ToString());

                            command.ExecuteNonQuery();
                            ++index;
                        }
                    }
                    catch (RedisServerException redis_ex)
                    {
                        if (!redis_ex.Message.StartsWith("WRONGTYPE", StringComparison.OrdinalIgnoreCase)) queryReport.Errors.Add(redis_ex.Message);

                        if (is_stop_query)
                        {
                            return;
                        }
                    }
                }
            }
        }

        private async Task query_zset()
        {
            treeView_field_names.Nodes.Add("a_db");
			treeView_field_names.Nodes.Add("a_key");
			treeView_field_names.Nodes.Add("a_index");
            treeView_field_names.Nodes.Add("a_value");
            treeView_field_names.Nodes.Add("a_score");

            string sql = $"CREATE TABLE {table_name} (a_db INTEGER, a_key TEXT, a_index INTEGER, a_value TEXT, a_score INTEGER)";
            SQLiteCommand command = new SQLiteCommand(sql, sqlite_con);
            int result = command.ExecuteNonQuery();

			int db_num_start = db_num == -1 ? 0 : db_num;
			int db_num_end = db_num == -1 ? (redis_client.Settings.use_cluster ? 0 : redis_client.RedisServer.DatabaseCount - 1) : db_num;

            for (int i = db_num_start; i <= db_num_end; ++i)
            {
				IDatabase redis = redis_client.GetDB(i);
				toolStripStatusLabel_status.Text = $"Getting DB_{i} keys...";
				PumpUi();

				var keys = await FetchKeys(i);

				toolStripProgressBar_status.Value = 0;
				toolStripProgressBar_status.Maximum = keys.Count();

				foreach (var key in keys)
                {
                    ++toolStripProgressBar_status.Value;
                    toolStripStatusLabel_status.Text = $"Getting DB_{i} values...({toolStripProgressBar_status.Value} / {toolStripProgressBar_status.Maximum})";

                    try
                    {
                        var vals = await redis.SortedSetRangeByRankWithScoresAsync(key, 0, activeEntryLimit - 1);
                        if (is_stop_query)
                        {
                            return;
                        }

                        int index = 0;
                        foreach (var val in vals)
                        {
                            sql = $"INSERT INTO {table_name} (a_db, a_key, a_index, a_value, a_score) VALUES (@Db, @Key, @Index, @Value, @Score);";
                            command = new SQLiteCommand(sql, sqlite_con);
                            command.Parameters.AddWithValue("Db", i);
							command.Parameters.AddWithValue("Key", key.ToString());
							command.Parameters.AddWithValue("Index", index);
                            command.Parameters.AddWithValue("Value", val.ToString());
                            command.Parameters.AddWithValue("Score", Convert.ToInt64(val.Score));

                            command.ExecuteNonQuery();
                            ++index;
                        }
                    }
                    catch (RedisServerException redis_ex)
                    {
                        if (!redis_ex.Message.StartsWith("WRONGTYPE", StringComparison.OrdinalIgnoreCase)) queryReport.Errors.Add(redis_ex.Message);

                        if (is_stop_query)
                        {
                            return;
                        }
                    }
                }
            }
        }

        private async Task query_hash()
        {
            treeView_field_names.Nodes.Add("a_db");
			treeView_field_names.Nodes.Add("a_key");
			var field_tree = treeView_field_names.Nodes.Add("a_field");
            treeView_field_names.Nodes.Add("a_value");

            string sql = $"CREATE TABLE {table_name} (a_db INTEGER, a_key TEXT, a_field TEXT, a_value TEXT)";
            SQLiteCommand command = new SQLiteCommand(sql, sqlite_con);
            int result = command.ExecuteNonQuery();

			int db_num_start = db_num == -1 ? 0 : db_num;
			int db_num_end = db_num == -1 ? (redis_client.Settings.use_cluster ? 0 : redis_client.RedisServer.DatabaseCount - 1) : db_num;

            for (int i = db_num_start; i <= db_num_end; ++i)
            {
				IDatabase redis = redis_client.GetDB(i);
				toolStripStatusLabel_status.Text = $"Getting DB_{i} keys...";
				PumpUi();

				var keys = await FetchKeys(i);

				toolStripProgressBar_status.Value = 0;
				toolStripProgressBar_status.Maximum = keys.Count();

				HashSet<string> field_name_set = new HashSet<string>();
                foreach (var key in keys)
                {
                    ++toolStripProgressBar_status.Value;
                    toolStripStatusLabel_status.Text = $"Getting DB_{i} values...({toolStripProgressBar_status.Value} / {toolStripProgressBar_status.Maximum})";

                    try
                    {
                        var hashes = await ReadSnapshot(redis.HashScan(key), activeEntryLimit);
                        if (is_stop_query)
                        {
                            return;
                        }

                        foreach (var hash in hashes)
                        {
                            sql = $"INSERT INTO {table_name} (a_db, a_key, a_field, a_value) VALUES (@Db, @Key, @Field, @Value);";
                            command = new SQLiteCommand(sql, sqlite_con);
                            command.Parameters.AddWithValue("Db", i);
                            command.Parameters.AddWithValue("Key", key.ToString());
                            command.Parameters.AddWithValue("Field", hash.Name.ToString());
                            command.Parameters.AddWithValue("Value", hash.Value.ToString());

                            command.ExecuteNonQuery();

                            field_name_set.Add(hash.Name.ToString());
                        }
                    }
                    catch (RedisServerException redis_ex)
                    {
                        if (!redis_ex.Message.StartsWith("WRONGTYPE", StringComparison.OrdinalIgnoreCase)) queryReport.Errors.Add(redis_ex.Message);

                        if (is_stop_query)
                        {
                            return;
                        }
                    }

                    List<string> field_name_list = field_name_set.ToList();
                    field_name_list.Sort();
                    List<TreeNode> field_name_nodes = new List<TreeNode>();
                    var list_auto_complete_total = list_auto_complete_static.ToList();
                    foreach (var field_name in field_name_list)
                    {
                        field_name_nodes.Add(new TreeNode(field_name));
                        list_auto_complete_total.Add(new AutocompleteItem(field_name, 3));
                    }

                    autocompleteMenu.SetAutocompleteItems(list_auto_complete_total);
                    field_tree.Nodes.AddRange(field_name_nodes.ToArray());
                }
            }
        }

        private async Task query_hash_ex()
        {
            treeView_field_names.Nodes.Add("a_db");
			treeView_field_names.Nodes.Add("a_key");

			HashSet<string> field_name_set = new HashSet<string>();
            Dictionary<string, List<KeyValuePair<string, string>>> dic_data = new Dictionary<string, List<KeyValuePair<string, string>>>();

            int db_num_start = db_num == -1 ? 0 : db_num;
			int db_num_end = db_num == -1 ? (redis_client.Settings.use_cluster ? 0 : redis_client.RedisServer.DatabaseCount - 1) : db_num;
            string db_n_key = "";

            for (int i = db_num_start; i <= db_num_end; ++i)
            {
                IDatabase redis = redis_client.GetDB(i);
                toolStripStatusLabel_status.Text = $"Getting DB_{i} keys...";
                PumpUi();

                var keys = await FetchKeys(i);

                toolStripProgressBar_status.Value = 0;
                toolStripProgressBar_status.Maximum = keys.Count();

                foreach (var key in keys)
                {
                    db_n_key = $"{i} {key}";

                    ++toolStripProgressBar_status.Value;
                    toolStripStatusLabel_status.Text = $"Getting DB_{i} values...({toolStripProgressBar_status.Value} / {toolStripProgressBar_status.Maximum})";
                    PumpUi();

                    try
                    {
                        var hashes = await ReadSnapshot(redis.HashScan(key), activeEntryLimit);
                        if (is_stop_query)
                        {
                            return;
                        }


						if (dic_data.ContainsKey(db_n_key.ToString()) == false)
						{
							dic_data.Add(db_n_key, new List<KeyValuePair<string, string>>());
                        }

						foreach (var hash in hashes)
                        {
                            dic_data[db_n_key].Add(new KeyValuePair<string, string>(hash.Name.ToString(), hash.Value.ToString()));

                            field_name_set.Add(hash.Name.ToString());
                        }
                    }
                    catch (RedisServerException redis_ex)
                    {
                        if (!redis_ex.Message.StartsWith("WRONGTYPE", StringComparison.OrdinalIgnoreCase)) queryReport.Errors.Add(redis_ex.Message);

                        if (is_stop_query)
                        {
                            return;
                        }
                    }
                }
            }

            ////////////////////////////////
            // create table
            string sql_create_table = "";
            StringBuilder sb_create_table = new StringBuilder();
            foreach (var field_name in field_name_set)
            {
                sb_create_table.Append($",`{field_name.Replace("`", "``")}` TEXT DEFAULT NULL");
            }

            sql_create_table = $"CREATE TABLE {table_name} (a_db INTEGER, a_key TEXT{sb_create_table.ToString()})";

            //string sql = $"CREATE TABLE {table_name} (a_key TEXT, a_field TEXT, a_value TEXT)";
            SQLiteCommand command = new SQLiteCommand(sql_create_table, sqlite_con);
            int result = command.ExecuteNonQuery();
            //
            ////////////////////////////////

            ////////////////////////////////
            // insert data
            foreach (var row in dic_data)
            {
                StringBuilder sb_insert_key = new StringBuilder();
                StringBuilder sb_insert_val = new StringBuilder();
                int fieldIndex = 0;
                foreach (var field_n_val in row.Value)
                {
                    sb_insert_key.Append($",`{field_n_val.Key.Replace("`", "``")}`");
                    sb_insert_val.Append($",@f_{fieldIndex++}");
                }

                string sql = $"INSERT INTO {table_name} (a_db, a_key{sb_insert_key.ToString()}) VALUES (@Db, @Key{sb_insert_val.ToString()});";
                command = new SQLiteCommand(sql, sqlite_con);
				command.Parameters.AddWithValue("Db", row.Key.Split(' ')[0]);
				command.Parameters.AddWithValue("Key", row.Key.Substring(row.Key.IndexOf(' ') + 1));
				foreach (var field_n_val in row.Value)
                {
                    command.Parameters.AddWithValue(field_n_val.Key, field_n_val.Value);
                }

                command.ExecuteNonQuery();
            }
            //
            ////////////////////////////////


            List<string> field_name_list = field_name_set.ToList();
            field_name_list.Sort();
            List<TreeNode> field_name_nodes = new List<TreeNode>();
            var list_auto_complete_total = list_auto_complete_static.ToList();
            foreach (var field_name in field_name_list)
            {
                field_name_nodes.Add(new TreeNode(field_name));
                list_auto_complete_total.Add(new AutocompleteItem(field_name, 3));
            }

            autocompleteMenu.SetAutocompleteItems(list_auto_complete_total);
            treeView_field_names.Nodes.AddRange(field_name_nodes.ToArray());
        }

        private async Task query_stream()
        {
            treeView_field_names.Nodes.Add("a_db");
            treeView_field_names.Nodes.Add("a_key");
            treeView_field_names.Nodes.Add("a_id");
            treeView_field_names.Nodes.Add("a_field");
            treeView_field_names.Nodes.Add("a_value");

            string sql = $"CREATE TABLE {table_name} (a_db INTEGER, a_key TEXT, a_id TEXT, a_field TEXT, a_value TEXT)";
            SQLiteCommand command = new SQLiteCommand(sql, sqlite_con);
            int result = command.ExecuteNonQuery();

            int db_num_start = db_num == -1 ? 0 : db_num;
            int db_num_end = db_num == -1 ? (redis_client.Settings.use_cluster ? 0 : redis_client.RedisServer.DatabaseCount - 1) : db_num;

            for (int i = db_num_start; i <= db_num_end; ++i)
            {
                IDatabase redis = redis_client.GetDB(i);
                toolStripStatusLabel_status.Text = $"Getting DB_{i} keys...";
                PumpUi();

                var keys = await FetchKeys(i);

                toolStripProgressBar_status.Value = 0;
                toolStripProgressBar_status.Maximum = keys.Count();

                HashSet<string> field_name_set = new HashSet<string>();
                foreach (var key in keys)
                {
                    ++toolStripProgressBar_status.Value;
                    toolStripStatusLabel_status.Text = $"Getting DB_{i} values...({toolStripProgressBar_status.Value} / {toolStripProgressBar_status.Maximum})";

                    try
                    {
                        var entries = await redis.StreamRangeAsync(key, count: activeEntryLimit);
                        if (is_stop_query)
                        {
                            return;
                        }

                        foreach (var entry in entries)
                        {
                            foreach (var pair in entry.Values)
                            {
                                sql = $"INSERT INTO {table_name} (a_db, a_key, a_id, a_field, a_value) VALUES (@Db, @Key, @Id, @Field, @Value);";
                                command = new SQLiteCommand(sql, sqlite_con);
                                command.Parameters.AddWithValue("Db", i);
                                command.Parameters.AddWithValue("Key", key.ToString());
                                command.Parameters.AddWithValue("Id", entry.Id.ToString());
                                command.Parameters.AddWithValue("Field", pair.Name.ToString());
                                command.Parameters.AddWithValue("Value", pair.Value.ToString());

                                command.ExecuteNonQuery();

                                field_name_set.Add(pair.Name.ToString());
                            }
                        }
                    }
                    catch (RedisServerException redis_ex)
                    {
                        if (redis_ex.HResult != -2146233088)
                        {
                            // Log
                        }

                        if (is_stop_query)
                        {
                            return;
                        }
                    }

                    List<string> field_name_list = field_name_set.ToList();
                    field_name_list.Sort();
                    List<TreeNode> field_name_nodes = new List<TreeNode>();
                    var list_auto_complete_total = list_auto_complete_static.ToList();
                    foreach (var field_name in field_name_list)
                    {
                        field_name_nodes.Add(new TreeNode(field_name));
                        list_auto_complete_total.Add(new AutocompleteItem(field_name, 3));
                    }

                    autocompleteMenu.SetAutocompleteItems(list_auto_complete_total);
                    var field_tree = treeView_field_names.Nodes.Cast<TreeNode>().FirstOrDefault(n => n.Text == "a_field");
                    if (field_tree != null)
                    {
                        field_tree.Nodes.Clear();
                        field_tree.Nodes.AddRange(field_name_nodes.ToArray());
                    }
                }
            }
        }

        private Task<T[]> ReadSnapshot<T>(IEnumerable<T> source, int limit)
        {
            return Task.Run(() =>
            {
                var rows = new List<T>();
                foreach (var item in source)
                {
                    queryCancellation.Token.ThrowIfCancellationRequested();
                    rows.Add(item);
                    if (rows.Count == limit) break;
                }
                return rows.ToArray();
            });
        }

        private Task<StackExchange.Redis.RedisKey[]> FetchKeys(int database)
        {
            string filter = textBox_keys_filter.Text;
            int limit = activeKeyLimit;
            return ReadSnapshot(redis_client.ScanKeys(database, filter, Config.scan_page_count), limit);
        }

        // Reused across page renders so paging does not rebuild the table (schema + metadata) every time.
        private DataTable pageTable;
        private string pageTableSchema = string.Empty;

        private void RenderResultPage()
        {
            if (ds == null || ds.Tables.Count == 0) return;
            var source = ds.Tables[0];

            // Re-clone only when the result shape changed (new query, or different columns).
            string schema = string.Join("|", source.Columns.Cast<DataColumn>()
                .Select(c => c.ColumnName + ":" + c.DataType.FullName));
            if (pageTable == null || schema != pageTableSchema)
            {
                pageTable = source.Clone();
                pageTableSchema = schema;
            }
            else
            {
                pageTable.Rows.Clear();
            }

            foreach (DataRow row in source.Rows.Cast<DataRow>().Skip(resultPages.Offset).Take(PageNavigator.PageSize))
            {
                pageTable.ImportRow(row);
            }

            dataGridView_query_result.DataSource = pageTable;
            resultPages.UpdatePage(source.Rows.Count > resultPages.Offset + PageNavigator.PageSize);
            button_col_row_count.Text = $"{source.Rows.Count} result rows · page {resultPages.Offset / PageNavigator.PageSize + 1}";
        }

        /// <summary>
        /// Computes a column width from the header plus a bounded sample of values.
        /// Setting <see cref="DataGridViewColumn.AutoSizeMode"/> to DisplayedCells instead would force
        /// a full layout pass over every displayed cell, which visibly stalls large result sets.
        /// </summary>
        private void SizeColumnsToSample(DataTable table)
        {
            var headerFont = dataGridView_query_result.ColumnHeadersDefaultCellStyle.Font ?? Font;
            var cellFont = dataGridView_query_result.DefaultCellStyle.Font ?? Font;
            var sample = table.Rows.Cast<DataRow>().Take(200).ToArray();

            for (int i = 0; i < table.Columns.Count && i < dataGridView_query_result.Columns.Count; i++)
            {
                DataColumn column = table.Columns[i];
                int width = TextRenderer.MeasureText(column.ColumnName, headerFont).Width + 16;

                foreach (DataRow row in sample)
                {
                    string text = row[i]?.ToString() ?? string.Empty;
                    // Long values are already truncated for display; measuring them fully is pointless.
                    if (text.Length > 200) text = text.Substring(0, 200);
                    width = Math.Max(width, TextRenderer.MeasureText(text, cellFont).Width + 16);
                    if (width >= MaxColumnWidth) break;
                }

                dataGridView_query_result.Columns[i].Width = Math.Min(width, MaxColumnWidth);
            }
        }

        private const int MaxColumnWidth = 550;

        private async Task execute_query(string sql)
        {
            if (is_querying) { is_stop_query = true; queryCancellation?.Cancel(); return; }
            queryCancellation = new CancellationTokenSource();
            activeEntryLimit = (int)entryLimit.Value;
            activeKeyLimit = (int)keyLimit.Value;
            queryReport.Errors.Clear();
            keyLimit.Enabled = entryLimit.Enabled = comboBox_keys_type.Enabled = textBox_keys_filter.Enabled = false;
            is_stop_query = false;
            try { await execute_query_core(sql); }
            catch (OperationCanceledException) { toolStripStatusLabel_status.Text = "Canceled"; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, UiText.QueryFailedTitle); }
            finally
            {
                try { using var rollback = new SQLiteCommand("ROLLBACK;", sqlite_con); rollback.ExecuteNonQuery(); } catch (SQLiteException) { }
                is_querying = false;
                is_stop_query = false;
                button_query_execute.Text = "▶";
                keyLimit.Enabled = entryLimit.Enabled = comboBox_keys_type.Enabled = textBox_keys_filter.Enabled = true;
                if (queryReport.Errors.Count > 0) queryReport.Show(this, "Query read failures");
                queryCancellation.Dispose();
                queryCancellation = null;
            }
        }

        private async Task execute_query_core(string select_sql)
        {
            button_query_execute.Text = "■";
            is_querying = true;
            dataGridView_query_result.Columns.Clear();
            button_col_row_count.Text = "(0r x 0c)";

            if (ds != null)
            {
                ds.Clear();
            }

            if (checkBox_reuse_table.Checked == false)
            {
                string sql = $"DROP TABLE IF EXISTS {table_name};";
                SQLiteCommand command = new SQLiteCommand(sql, sqlite_con);
                int result = command.ExecuteNonQuery();

                if (textBox_keys_filter.Text == "")
                {
                    textBox_keys_filter.Text = "*";
                }

                toolStripProgressBar_status.Maximum = 0;
                toolStripProgressBar_status.Value = 0;

                command = new SQLiteCommand("BEGIN;", sqlite_con);
                result = command.ExecuteNonQuery();

                treeView_field_names.Nodes.Clear();
                autocompleteMenu.SetAutocompleteItems(list_auto_complete_static);

                switch ((RedisKeyType)comboBox_keys_type.SelectedIndex)
                {
                    case RedisKeyType.String:
                    {
                        await query_string();
                    }
                    break;
                    case RedisKeyType.List:
                    {
                        await query_list();
                    }
                    break;
                    case RedisKeyType.Set:
                    {
                        await query_set();
                    }
                    break;
                    case RedisKeyType.Zset:
                    {
                        await query_zset();
                    }
                    break;
                    case RedisKeyType.Hash:
                    {
                        await query_hash_ex();
                    }
                    break;
                    case RedisKeyType.Stream:
                    {
                        await query_stream();
                    }
                    break;
                }

                if (is_stop_query)
                {
                    is_querying = false;
                    is_stop_query = false;
                    button_query_execute.Text = "▶";
                    toolStripStatusLabel_status.Text = "User canceled";
                    toolStripProgressBar_status.Value = 0;
                    command = new SQLiteCommand("ROLLBACK;", sqlite_con);
                    result = command.ExecuteNonQuery();
                    return;
                }

                toolStripStatusLabel_status.Text = "Querying...";
                PumpUi();

                command = new SQLiteCommand("COMMIT;", sqlite_con);
                result = command.ExecuteNonQuery();

                checkBox_reuse_table.Checked = true;
            }

            try
            {
                toolStripStatusLabel_status.Text = "Making data grid view...";
                PumpUi();

                var adapter = new SQLiteDataAdapter(select_sql, sqlite_con);
                ds = new DataSet();
                await Task.Run(() =>
                {
                    using var cancel = queryCancellation.Token.Register(() => adapter.SelectCommand.Cancel());
                    adapter.Fill(ds, 0, 10000, "results");
                });
                queryCancellation.Token.ThrowIfCancellationRequested();

                dataGridView_query_result.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
                dataGridView_query_result.AllowUserToResizeColumns = true;
                resultPages.Reset();
                RenderResultPage();

                // Widths are derived from a bounded sample instead of DisplayedCells autosizing,
                // which would force a layout pass over every displayed cell.
                if (pageTable != null)
                {
                    SizeColumnsToSample(pageTable);
                }
            }
            catch (SQLiteException sqlite_ex)
            {
                MessageBox.Show(sqlite_ex.Message, UiText.QueryErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            button_col_row_count.Text = $"({dataGridView_query_result.Rows.Count}r x {dataGridView_query_result.Columns.Count}c)";
            toolStripStatusLabel_status.Text = "Done";

            is_querying = false;
            button_query_execute.Text = "▶";
        }

        private async void button_query_go_Click(object sender, EventArgs e)
        {
            if (is_querying)
            {
                is_stop_query = true;
                queryCancellation?.Cancel();
            }
            else
            {
                string sql = richTextBox_query.Text;
                await execute_query(sql);
            }
        }

        private void FormQueryWindow_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (is_querying) { is_stop_query = true; queryCancellation?.Cancel(); e.Cancel = true; return; }
            is_stop_query = true;
        }

        /// <summary>
        /// Releases the in-memory SQLite snapshot. The designer Dispose calls this: components alone
        /// would leak a full copy of the keyspace on every open.
        /// </summary>
        private void ReleaseSnapshot()
        {
            is_stop_query = true;
            try { queryCancellation?.Cancel(); } catch { }
            try { queryCancellation?.Dispose(); } catch { }
            queryCancellation = null;

            try
            {
                if (sqlite_con != null)
                {
                    using (SQLiteCommand command = new SQLiteCommand($"DROP TABLE IF EXISTS {table_name};", sqlite_con))
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (SQLiteException)
            {
            }

            try { sqlite_con?.Close(); } catch { }
            try { sqlite_con?.Dispose(); } catch { }
            sqlite_con = null;
        }

        private async void richTextBox_query_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.F9)
            {
                string sql = richTextBox_query.SelectedText;
                await execute_query(sql);

                return;
            }
            else if (e.KeyCode == Keys.F9)
            {
                string sql = richTextBox_query.Text;
                await execute_query(sql);

                return;
            }
        }

        private void CM_remove_selected_keys()
        {
            // Collect the targets first so the user confirms exactly what will be deleted.
            var targets = new List<(IDatabase redis, string key)>();
            if (db_num != -1)
            {
                IDatabase redis = redis_client.GetDB(db_num);
                if (redis == null) return;

                foreach (DataGridViewCell cell in dataGridView_query_result.SelectedCells)
                {
                    if (dataGridView_query_result.Columns[cell.ColumnIndex].Name == "a_key")
                    {
                        targets.Add((redis, cell.Value.ToString()));
                    }
                }
            }
            else
            {
                if (dataGridView_query_result.Columns.Contains("a_db") == false)
                {
                    MessageBox.Show(UiText.DbFieldRequired);
                    return;
                }

                foreach (DataGridViewCell cell in dataGridView_query_result.SelectedCells)
                {
                    if (dataGridView_query_result.Columns[cell.ColumnIndex].Name == "a_key")
                    {
                        // A_db comes from the result set, so a null or non-numeric cell must not
                        // throw out of the context-menu handler.
                        string raw_db = dataGridView_query_result.Rows[cell.RowIndex].Cells["a_db"].Value?.ToString();
                        if (int.TryParse(raw_db, out int a_db) == false) continue;
                        IDatabase redis = redis_client.GetDB(a_db);
                        if (redis == null) continue;
                        targets.Add((redis, cell.Value?.ToString() ?? ""));
                    }
                }
            }

            if (targets.Count == 0) return;

            // Enforce the connection's read-only flag before asking for confirmation.
            if (redis_client == null || redis_client.CanWrite() == false) return;

            var preview = string.Join("\r\n", targets.Take(20).Select(t => t.key)
                .Concat(targets.Count > 20 ? new[] { $"... and {targets.Count - 20} more" } : Array.Empty<string>()));
            DialogResult confirm = MessageBox.Show(
                $"Delete {targets.Count} key(s)? This cannot be undone.\r\n\r\n{preview}",
                "Confirm key removal", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes) return;

            int removed = 0;
            var errors = new List<string>();
            foreach (var target in targets)
            {
                try
                {
                    if (target.redis.KeyDelete(target.key))
                    {
                        removed++;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{target.key}: {ex.Message}");
                }
            }

            if (errors.Count > 0)
            {
                MessageBox.Show(
                    $"Removed {removed} of {targets.Count} key(s).\r\n\r\nFailed:\r\n{string.Join("\r\n", errors.Take(10))}",
                    "Key removal finished", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else if (removed == 0)
            {
                MessageBox.Show(UiText.NoKeysRemoved, UiText.KeyRemovalFinished, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            checkBox_reuse_table.Checked = false;
        }

        private void checkBox_reuse_table_CheckedChanged(object sender, EventArgs e)
        {
            textBox_keys_filter.Enabled = !checkBox_reuse_table.Checked;
            comboBox_keys_type.Enabled = !checkBox_reuse_table.Checked;
        }

        private void treeView_field_names_DoubleClick(object sender, EventArgs e)
        {
            try
            {
                Clipboard.SetText(treeView_field_names.SelectedNode.Text);
            }
            catch (Exception)
            {
            }
            richTextBox_query.SelectedText = treeView_field_names.SelectedNode.Text;
            richTextBox_query.Focus();
        }
    }
}
