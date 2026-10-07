using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StackExchange.Redis;
using System.Net;

namespace RedisGuiManager
{
    public partial class FormMain : Form
    {
        private List<RedisGroup> redis_group = new List<RedisGroup>();
        private List<RedisSettings> redis_settings = new List<RedisSettings>();
        private UserControl userControl = null;
        private bool is_shown = false;
        private const string ConnectionsDir = "connections";
        private const string DefaultConnectionsFile = "connections/connections.json";
        private Dictionary<RedisSettings, string> _settingFileMap = new Dictionary<RedisSettings, string>();
        private Dictionary<RedisGroup, string> _groupFileMap = new Dictionary<RedisGroup, string>();
        private HashSet<string> _knownFiles = new HashSet<string>();
        private HashSet<string> _failedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Application.DoEvents() runs a nested message loop and is therefore re-entrant: it can
        // dispatch another selection change (or a tree edit) while the first one is still loading,
        // which corrupts the node being populated. This wrapper keeps the loading icon responsive
        // but never lets two pumps overlap.
        private bool pumpingUi;

        private void PumpUi()
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            if (pumpingUi) return;

            pumpingUi = true;
            try
            {
                Application.DoEvents();
            }
            catch (Exception)
            {
                // Never let a nested-pump failure break node loading.
            }
            finally
            {
                pumpingUi = false;
            }
        }

        // Application-level shortcuts. ProcessCmdKey is consulted before the focused control, so
        // this needs no KeyPreview; the handlers behind the menu items already no-op when the
        // selected node is not the right kind.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F5:
                    // Let text inputs keep F5 for their own use.
                    if (IsEditingText(ActiveControl)) break;
                    reload_keys_ToolStripMenuItem.PerformClick();
                    if (treeView_server.SelectedNode?.Tag is RedisClient)
                    {
                        reload_server_ToolStripMenuItem.PerformClick();
                    }
                    return true;

                case Keys.Control | Keys.R:
                    reload_server_ToolStripMenuItem.PerformClick();
                    return true;

                case Keys.Control | Keys.F:
                    if (IsEditingText(ActiveControl)) break;
                    filter_key_ToolStripMenuItem.PerformClick();
                    return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private static bool IsEditingText(Control control)
        {
            return control is TextBoxBase || control is SyntaxRichTextBox;
        }

        public FormMain()
        {
            InitializeComponent();

            contextMenuStrip_redis.Items.Remove(remove_keys_from_registered_dbs_ToolStripMenuItem);

            treeView_server.BeforeSelect += (s, e) => { e.Cancel = !ValueControl.ConfirmAll(panel1); };
            Config.Load();

            if (Config.darkmode > 0)
            {
                Utils.DarkThemeForm(this);
            }
            // 同步暗黑模式复选框到左侧面板（替代原 FormLoadServerList 中的设置）
            try
            {
                checkBox_darkmode.Checked = Config.darkmode > 0;
            }
            catch { }
        }

        private void checkBox_darkmode_CheckedChanged(object sender, EventArgs e)
        {
            Config.darkmode = checkBox_darkmode.Checked ? 1 : 0;
            Config.Save();
        }

        private void FormMain_Load(object sender, EventArgs e)
        {
            CreateRedisShowTagControl<StartControl>();

            LoadAllConnections();
        }

        private void ClearAll()
        {
            foreach (TreeNode server in treeView_server.Nodes) DisposeKeyScans(server);
            // 关闭已建立的连接：包含分组内的连接与独立连接，避免 TreeView 仅含顶层节点导致泄漏
            foreach (var g in redis_group)
            {
                if (g.connections == null) continue;
                foreach (var s in g.connections)
                {
                    try { s.redis_client?.Close(); } catch { }
                }
            }
            foreach (var s in redis_settings)
            {
                try { s.redis_client?.Close(); } catch { }
            }
            // 兜底：树上仍可能存在未在列表中的节点（极端情况）
            foreach (TreeNode item in treeView_server.Nodes)
            {
                if (item?.Tag is RedisClient client)
                {
                    try { if (client.Redis != null) client.Close(); } catch { }
                    continue;
                }
                if (item?.Tag is RedisGroup grp && grp.connections != null)
                {
                    foreach (var s in grp.connections)
                        try { s.redis_client?.Close(); } catch { }
                }
                // 递归关闭子节点中的客户端（分组子节点）
                foreach (TreeNode child in item.Nodes)
                {
                    if (child?.Tag is RedisClient c2)
                        try { if (c2.Redis != null) c2.Close(); } catch { }
                }
            }

            redis_group.Clear();
            redis_settings.Clear();
            _settingFileMap.Clear();
            _groupFileMap.Clear();
            _knownFiles.Clear();
            _failedFiles.Clear();
            treeView_server.Nodes.Clear();
        }

        private string GetConnectionsDir()
        {
            return Path.Combine(Application.StartupPath, ConnectionsDir);
        }

        private string GetDefaultConnectionsFile()
        {
            return Path.Combine(GetConnectionsDir(), "connections.json");
        }

        private void LoadAllConnections()
        {
            ClearAll();

            this.Text = "Redis Gui Manager";
            _knownFiles.Clear();
            _failedFiles.Clear();
            var loadErrors = new List<string>();

            try
            {
                string connDir = GetConnectionsDir();
                string defaultFile = GetDefaultConnectionsFile();
                if (!Directory.Exists(connDir))
                {
                    Directory.CreateDirectory(connDir);
                }

                string[] files = Directory.GetFiles(connDir, "*.json");
                if (files.Length == 0)
                {
                    // create default empty file if none exists
                    if (!File.Exists(defaultFile))
                    {
                        File.WriteAllText(defaultFile, "[\n]\n", Encoding.UTF8);
                    }
                    files = new string[] { defaultFile };
                }


                foreach (string path in files)
                {
                    string fullPath = Path.GetFullPath(path);
                    _knownFiles.Add(fullPath);
                    string json_file;
                    try
                    {
                        json_file = File.ReadAllText(path, Encoding.UTF8);
                        if (string.IsNullOrWhiteSpace(json_file))
                        {
                            // 空文件视为合法空数组
                            continue;
                        }
                        JArray json_parsed;
                        try
                        {
                            json_parsed = JArray.Parse(json_file);
                        }
                        catch (JsonReaderException jex)
                        {
                            // 单对象或损坏的 JSON：尝试包一层或记录错误后跳过
                            throw new Exception($"JSON 格式错误: {jex.Message}", jex);
                        }
                        foreach (var j in json_parsed)
                        {
                            if (j == null || j.Type == JTokenType.Null) continue;
                            if (j.SelectToken("$.type") != null)
                            {
                                RedisGroup group = j.ToObject<RedisGroup>();
                                if (group == null) continue;
                                if (group.connections == null) group.connections = new List<RedisSettings>();
                                redis_group.Add(group);
                                _groupFileMap[group] = fullPath;
                            }
                            else
                            {
                                RedisSettings setting = j.ToObject<RedisSettings>();
                                if (setting == null) continue;
                                // 基础字段校验：name/host 为空的视为脏数据跳过
                                if (string.IsNullOrWhiteSpace(setting.name) && string.IsNullOrWhiteSpace(setting.host))
                                {
                                    loadErrors.Add($"{Path.GetFileName(path)}: 跳过无名无 host 的条目");
                                    continue;
                                }
                                redis_settings.Add(setting);
                                _settingFileMap[setting] = fullPath;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _failedFiles.Add(fullPath);
                        string msg = $"Load failed: {Path.GetFileName(path)} - {ex.Message}";
                        loadErrors.Add(msg);
                    }
                }
                if (loadErrors.Count > 0)
                {
                    // 暂存，后面汇总显示，避免被后续覆盖
                }

                foreach (var group in redis_group)
                {
                    if (group.connections == null) group.connections = new List<RedisSettings>();
                    foreach (var settings in group.connections)
                    {
                        settings.redis_client = ObserveClient(new RedisClient(settings));
                        if (!_settingFileMap.ContainsKey(settings))
                        {
                            // nested settings inherit group's file
                            _settingFileMap[settings] = _groupFileMap[group];
                        }
                    }
                }

                foreach (var settings in redis_settings)
                {
                    settings.redis_client = ObserveClient(new RedisClient(settings));
                }

                // ensure default file is known for new adds
                _knownFiles.Add(Path.GetFullPath(GetDefaultConnectionsFile()));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Load connections failed\r\n" + ex.Message);
            }

            LoadRedisSettings();
            string summary = $"Loaded {redis_settings.Count + redis_group.Sum(g => g.connections.Count)} connections from {_knownFiles.Count} file(s)";
            if (loadErrors.Count > 0)
                toolStripStatusLabel1.Text = summary + " | " + string.Join(" | ", loadErrors.Take(2));
            else
                toolStripStatusLabel1.Text = summary;
        }

        private void LoadRedisServerList(string path)
        {
            ClearAll();

            this.Text = string.Format("Redis Gui Manager ({0})", Path.GetFileNameWithoutExtension(path));
            string fullPath = Path.GetFullPath(path);
            _knownFiles.Add(fullPath);

            string json_file = File.ReadAllText(path, Encoding.UTF8);
            var json_parsed = JArray.Parse(json_file);
            foreach (var j in json_parsed)
            {
                if (j.SelectToken("$.type") != null)
                {
                    RedisGroup group = j.ToObject<RedisGroup>();
                    if (group.connections == null) group.connections = new List<RedisSettings>();
                    redis_group.Add(group);
                    _groupFileMap[group] = fullPath;
                }
                else
                {
                    RedisSettings setting = j.ToObject<RedisSettings>();
                    redis_settings.Add(setting);
                    _settingFileMap[setting] = fullPath;
                }
            }

            foreach (var group in redis_group)
            {
                foreach (var settings in group.connections)
                {
                    settings.redis_client = ObserveClient(new RedisClient(settings));
                    if (!_settingFileMap.ContainsKey(settings))
                        _settingFileMap[settings] = _groupFileMap[group];
                }
            }

            foreach (var settings in redis_settings)
            {
                settings.redis_client = ObserveClient(new RedisClient(settings));
            }

            LoadRedisSettings();
        }

        private void FormMain_Shown(object sender, EventArgs e)
        {
            int x = Config.mainform_pos_x;
            int y = Config.mainform_pos_y;
            int wt = Screen.FromPoint(this.Location).WorkingArea.Top;
            int wr = Screen.FromPoint(this.Location).WorkingArea.Right;
            int wb = Screen.FromPoint(this.Location).WorkingArea.Bottom;
            int wl = Screen.FromPoint(this.Location).WorkingArea.Left;

            foreach (Screen screen in Screen.AllScreens)
            {
                wl = screen.WorkingArea.Left < wl ? screen.WorkingArea.Left : wl;
                wr = screen.WorkingArea.Right > wr ? screen.WorkingArea.Right : wr;
                wt = screen.WorkingArea.Top < wt ? screen.WorkingArea.Top : wt;
                wb = screen.WorkingArea.Bottom > wb ? screen.WorkingArea.Bottom : wb;
            }

            if (x < wl)
            {
                x = wl;
            }
            else if (x > wr - this.Width)
            {
                x = wr - this.Width;
            }

            if (y < wt)
            {
                y = wt;
            }
            else if (y > wb - this.Height)
            {
                y = wb - this.Height;
            }

            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(x, y);
            this.Width = Config.mainform_width;
            this.Height = Config.mainform_height;
            if (Config.mainform_is_maximized > 0)
            {
                this.WindowState = FormWindowState.Maximized;
            }
            is_shown = true;

            treeView_server.SelectedNode = null;

            treeView_server.AfterSelect += treeView_server_AfterSelect;
            treeView_server.MouseDown += treeView_server_MouseDown;
            treeView_server.MouseUp += treeView_server_MouseUp;
        }

        private void FormMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!ValueControl.ConfirmAll(panel1)) { e.Cancel = true; return; }
            Config.mainform_is_maximized = (WindowState == FormWindowState.Maximized) ? 1 : 0;

            if (WindowState == FormWindowState.Normal)
            {
                Config.mainform_pos_x = Location.X;
                Config.mainform_pos_y = Location.Y;
                Config.mainform_width = Width;
                Config.mainform_height = Height;
            }

            Config.Save();

            ClearAll();
        }

        private void add_db_to_list_ToolStripMenuItem_Click(object sender, EventArgs e)
		{
			TreeNode select = treeView_server.SelectedNode;
			if (select == null)
			{
				return;
			}

			if (select.Tag is not RedisClient)
			{
				return;
			}

			using (FormInputString formInput = new FormInputString())
			{
				formInput.TextInfo = "Add DB";
				formInput.InputValue = "";

				if (formInput.ShowDialog() == DialogResult.OK)
				{
                    string[] seps = formInput.InputValue.Split(' ');
                    List<int> db_nums = new List<int>();

                    foreach (string s in seps)
					{
                        if (int.TryParse(s, out int dbNum))
                        {
                            db_nums.Add(dbNum);
                        }
                    }

                    if (db_nums.Count > 0)
					{
                        add_dbs_to_list(db_nums, select);
                    }
				}
			}
		}

        private void add_db_range_to_list_ToolStripMenuItem_Click(object sender, EventArgs e)
		{
			TreeNode select = treeView_server.SelectedNode;
			if (select == null)
			{
				return;
			}

			if (select.Tag is not RedisClient)
			{
				return;
			}

			using (FormTwoInputString formInput = new FormTwoInputString())
			{
				formInput.TextInfo = "Add DB Range";
				formInput.InputValue1 = "";
				formInput.InputValue2 = "";

				if (formInput.ShowDialog() == DialogResult.OK)
				{
                    int dbNumStart = 0;
                    int dbNumEnd = 0;

                    if (int.TryParse(formInput.InputValue1, out dbNumStart) == false)
					{
                        MessageBox.Show("Insert value1 number only");
                        return;
					}

					if (int.TryParse(formInput.InputValue2, out dbNumEnd) == false)
					{
						MessageBox.Show("Insert value2 number only");
						return;
					}

                    if (dbNumStart > dbNumEnd)
					{
                        int temp = dbNumStart;
                        dbNumStart = dbNumEnd;
                        dbNumEnd = temp;
					}

                    List<int> db_nums = new List<int>();
                    for (int i = dbNumStart; i <= dbNumEnd; ++i)
					{
                        db_nums.Add(i);
					}

                    add_dbs_to_list(db_nums, select);
                }
			}
		}

        private void add_dbs_to_list(List<int> db_nums, TreeNode select)
		{
			if (select.Tag is RedisClient client)
			{
				if (client.Settings.additional_dbs == null)
					client.Settings.additional_dbs = new List<int>();
				bool changed = false;
				foreach (int db_num in db_nums)
				{
					if (AddTreeNode_DB(client, db_num, select, false))
					{
						client.Settings.additional_dbs.Add(db_num);
						changed = true;
					}
				}

				if (changed)
				{
					client.Settings.additional_dbs.Sort();
					SaveRedisSettings();
				}
				else if (client.Settings.additional_dbs.Count == 0)
				{
					// 避免空列表序列化为 null，保持一致性
					client.Settings.additional_dbs = null;
				}
			}
		}

        private async void remove_db_range_from_list_ToolStripMenuItem_Click(object sender, EventArgs e)
		{
			TreeNode select = treeView_server.SelectedNode;
			if (select == null)
			{
				return;
			}

			if (select.Tag is not RedisClient)
			{
				return;
			}

			using (FormTwoInputString formInput = new FormTwoInputString())
			{
				formInput.TextInfo = "Remove DB Range";
				formInput.InputValue1 = "";
				formInput.InputValue2 = "";

				if (formInput.ShowDialog() == DialogResult.OK)
				{
					int dbNumStart = 0;
					int dbNumEnd = 0;

					if (int.TryParse(formInput.InputValue1, out dbNumStart) == false)
					{
						MessageBox.Show("Insert value1 number only");
						return;
					}

					if (int.TryParse(formInput.InputValue2, out dbNumEnd) == false)
					{
						MessageBox.Show("Insert value2 number only");
						return;
					}

					if (dbNumStart > dbNumEnd)
					{
						int temp = dbNumStart;
						dbNumStart = dbNumEnd;
						dbNumEnd = temp;
					}

					if (select.Tag is RedisClient client)
					{
						if (client.Settings.additional_dbs == null)
							client.Settings.additional_dbs = new List<int>();
						for (int i = dbNumStart; i <= dbNumEnd; ++i)
						{
							client.Settings.additional_dbs.Remove(i);
						}

						client.Settings.additional_dbs.Sort();

						SaveRedisSettings();
					}

                    await RefreshRedisKeyAsync(select, true);
                                    }
                    			}
                            }

        private void find_db_from_list_ToolStripMenuItem_Click(object sender, EventArgs e)
		{
			TreeNode select = treeView_server.SelectedNode;
			if (select == null)
			{
				return;
			}

			if (select.Tag is not RedisClient)
			{
				return;
			}

			using (FormInputString formInput = new FormInputString())
			{
				formInput.TextInfo = "Find DB";
				formInput.InputValue = "";

				if (formInput.ShowDialog() == DialogResult.OK)
				{
					if (int.TryParse(formInput.InputValue, out int dbNum))
					{
						if (select.Tag is RedisClient client)
						{
                            TreeNode node = GetTreeNode_DB(dbNum, select);
                            if (node != null)
							{
                                treeView_server.SelectedNode = node;
							}
						}
					}
					else
					{
						MessageBox.Show("Insert number only");
					}
				}
			}
		}

        private async void toggle_show_default_dbs_ToolStripMenuItem_Click(object sender, EventArgs e)
		{
			TreeNode select = treeView_server.SelectedNode;
			if (select == null)
			{
				return;
			}

            if (select.Tag is RedisClient client)
			{
                client.Settings.hide_default_dbs = !client.Settings.hide_default_dbs;

				SaveRedisSettings();

				await RefreshRedisKeyAsync(select, true);
			}
        }

        private void remove_keys_from_registered_dbs_ToolStripMenuItem_Click(object sender, EventArgs e)
		{
            if (!CanWriteSelected()) return;
            PromptBatchDelete(false);
        }

        private void remove_keys_from_whole_dbs_ToolStripMenuItem_Click(object sender, EventArgs e)
		{
            if (!CanWriteSelected()) return;
            PromptBatchDelete(true);
        }

        private async void PromptBatchDelete(bool allDatabases)
        {
            var selected = treeView_server.SelectedNode;
            var client = (RedisClient)GetRedisNode(selected).Tag;
            using var input = new FormInputString { TextInfo = "Delete keys matching pattern", InputValue = "" };
            if (input.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(input.InputValue)) return;
            var databases = selected.Tag is DbSettings db && !allDatabases
                ? new[] { db.DBNumber }
                : allDatabases ? Enumerable.Range(0, client.Settings.use_cluster ? 1 : client.RedisServer.DatabaseCount).ToArray()
                : selected.Nodes.Cast<TreeNode>().Where(n => n.Tag is DbSettings).Select(n => ((DbSettings)n.Tag).DBNumber).ToArray();
            await RunBatchAsync(client, databases, input.InputValue, "Delete keys", "", (database, key) => database.KeyDelete(key));
            if (selected.Tag is DbSettings) await RefreshDbKeysAsync(selected, true);
        }

        private async Task RunBatchAsync(RedisClient client, int[] databases, string pattern, string title, string destination, Func<IDatabase, StackExchange.Redis.RedisKey, bool> execute)
        {
            if (!client.CanWrite()) return;
            string manifest = Path.GetTempFileName();
            try
            {
                var previewOutcome = await OperationDialog.RunAsync(this, "Preview " + title, (token, progress) =>
                {
                    long count = 0;
                    var sample = new List<string>();
                    using var writer = new StreamWriter(manifest);
                    foreach (int db in databases.Distinct())
                    {
                        foreach (var key in client.ScanKeys(db, pattern, Config.scan_page_count))
                        {
                            token.ThrowIfCancellationRequested();
                            writer.WriteLine(JsonConvert.SerializeObject(new object[] { db, Convert.ToBase64String((byte[])key) }));
                            if (sample.Count < 10) sample.Add($"DB {db}: {key}");
                            if (++count % 100 == 0) progress.Report($"Matched {count} keys");
                        }
                    }
                    return (Count: count, Sample: sample);
                });
                if (!previewOutcome.IsSuccess) return;

                var preview = previewOutcome.Value;
                if (preview.Count == 0) { MessageBox.Show(this, "No keys matched.", title); return; }
                if (MessageBox.Show(this, $"Connection: {client.Settings.name} [{client.Settings.host}:{client.Settings.port}]\nDBs: {string.Join(", ", databases)}\nPattern: {pattern}\nMatched keys: {preview.Count}\n{destination}\n\n{string.Join("\n", preview.Sample)}\n\nContinue? Completed changes remain if canceled.", title + " preview", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

                var resultOutcome = await OperationDialog.RunAsync(this, title, (token, progress) =>
                {
                    var report = new OperationReport();
                    foreach (string line in File.ReadLines(manifest))
                    {
                        if (token.IsCancellationRequested) { report.Canceled = true; break; }
                        var item = JArray.Parse(line);
                        int db = item[0].Value<int>();
                        var key = (StackExchange.Redis.RedisKey)Convert.FromBase64String(item[1].Value<string>());
                        try { if (execute(client.GetDB(db), key)) report.Success++; else report.Skipped++; }
                        catch (RedisException ex) { report.Errors.Add($"DB {db} / {key}: {ex.Message}"); }
                        if ((report.Success + report.Skipped + report.Errors.Count) % 100 == 0) progress.Report($"Processed {report.Success + report.Skipped + report.Errors.Count} / {preview.Count}");
                    }
                    return report;
                });
                if (!resultOutcome.IsSuccess) return;

                resultOutcome.Value.Show(this, title + " result");
            }
            finally { File.Delete(manifest); }
        }

        private async void reload_server_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode select = treeView_server.SelectedNode;
            if (select == null)
            {
                return;
            }

            if (select.Tag is RedisClient)
            {
                await RefreshRedisKeyAsync(select, true);
            }
        }

        private async void query_window_all_server_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode select = treeView_server.SelectedNode;
            if (select == null)
            {
                return;
            }

            if (select.Tag is RedisClient client)
            {
				if (client.RedisServer == null)
				{
                    var connect = await OperationDialog.RunAsync(this, "Connect", (token, progress) => client.Connect());
                    if (!connect.IsSuccess) return;
					if (connect.Value.IsSuccess == false)
					{
						MessageBox.Show("Connection failed");
						return;
					}
				}

				FormQueryWindow fqw = new FormQueryWindow();
				fqw.SetServerInfo(client, -1);
				fqw.Show();
			}
        }

        private void open_console_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode select = treeView_server.SelectedNode;
            if (select == null)
            {
                return;
            }

            if (select.Tag is RedisClient client)
            {
                FormConsole formConsole = new FormConsole();
                formConsole.SetRedis(client);
                formConsole.Show();
            }
        }

        private void server_info_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode select = treeView_server.SelectedNode;
            if (select == null) return;

            if (select.Tag is RedisClient client)
            {
                FormServerInfo form = new FormServerInfo(client);
                form.Show();
            }
        }

        private void slowlog_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode select = treeView_server.SelectedNode;
            if (select == null) return;

            if (select.Tag is RedisClient client)
            {
                FormSlowlog form = new FormSlowlog(client);
                form.Show();
            }
        }

        private async void groups_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!ValueControl.ConfirmAll(panel1)) return;

            // Snapshot membership before editing so connections can move both ways.
            var beforeGrouped = GroupedConnectionNames(redis_group);

            // Every connection object we know about, both top level and inside a group. Needed to
            // resolve group members when saving and to find connections dropped from a group.
            var known = redis_settings
                .Concat(redis_group.SelectMany(g => g.connections ?? new List<RedisSettings>()))
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.name))
                .GroupBy(c => c.name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var editedGroups = new List<RedisGroup>();
            using (var dialog = new FormGroups(redis_group, redis_settings))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                editedGroups = dialog.BuildResult(redis_group, known.Values);
            }

            var afterGrouped = GroupedConnectionNames(editedGroups);

            // A connection is stored either inside a group or at the top level, never both,
            // otherwise the next save would write it to the file twice.
            redis_settings.RemoveAll(c => afterGrouped.Contains(c.name));

            foreach (string name in beforeGrouped)
            {
                if (afterGrouped.Contains(name)) continue;
                if (redis_settings.Any(c => string.Equals(c.name, name, StringComparison.OrdinalIgnoreCase))) continue;
                if (known.TryGetValue(name, out RedisSettings settings)) redis_settings.Add(settings);
            }

            redis_group.Clear();
            redis_group.AddRange(editedGroups);

            SaveRedisSettings();

            TreeNode selected = treeView_server.SelectedNode;
            if (selected != null)
            {
                await RefreshRedisKeyAsync(selected, true);
            }

            toolStripStatusLabel1.Text = $"Groups updated: {redis_group.Count} group(s), {redis_settings.Count} ungrouped";
        }

        private static HashSet<string> GroupedConnectionNames(IEnumerable<RedisGroup> groups) =>
            new HashSet<string>(
                (groups ?? Enumerable.Empty<RedisGroup>())
                    .Where(g => g?.connections != null)
                    .SelectMany(g => g.connections)
                    .Where(c => c != null && !string.IsNullOrWhiteSpace(c.name))
                    .Select(c => c.name),
                StringComparer.OrdinalIgnoreCase);

        private void server_tools_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode select = treeView_server.SelectedNode;
            if (select == null) return;

            if (select.Tag is RedisClient client)
            {
                if (client.Redis == null)
                {
                    MessageBox.Show(this, "Connect to the server first.", "Server tools",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                FormServerTools form = new FormServerTools(client);
                form.Show(this);
            }
        }

        private void pubsub_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode select = treeView_server.SelectedNode;
            if (select == null) return;

            if (select.Tag is RedisClient client)
            {
                FormPubSub form = new FormPubSub(client);
                form.Show();
            }
        }

        private bool CanWriteSelected()
        {
            var node = treeView_server.SelectedNode;
            var client = node == null ? null : GetRedisNode(node)?.Tag as RedisClient;
            return client != null && client.CanWrite();
        }

        private async void export_data_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var selected = treeView_server.SelectedNode;
            if (selected == null || GetDbNode(selected)?.Tag is not DbSettings dbSettings) return;
            var client = (RedisClient)GetRedisNode(selected).Tag;
            var database = client.GetDB(dbSettings.DBNumber);
            using var dialog = new SaveFileDialog { Filter = "JSON files (*.json)|*.json", FileName = $"redis_export_db{dbSettings.DBNumber}.json" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            string path = dialog.FileName;
            var outcome = await OperationDialog.RunAsync(this, "Export data", (token, progress) =>
            {
                var report = new OperationReport();
                string temporary = path + ".partial-" + Guid.NewGuid().ToString("N");
                try
                {
                    using (var writer = new JsonTextWriter(new StreamWriter(temporary)) { Formatting = Formatting.Indented })
                    {
                        var serializer = new JsonSerializer();
                        writer.WriteStartArray();
                        foreach (var key in client.ScanKeys(dbSettings.DBNumber, "*", Config.scan_page_count))
                        {
                            if (token.IsCancellationRequested) { report.Canceled = true; break; }
                            try
                            {
                                var snapshot = (RedisResult[])database.ScriptEvaluate(
                                    "local v=redis.call('DUMP',KEYS[1]); if not v then return redis.error_reply('Key disappeared') end; return {v,redis.call('PTTL',KEYS[1])}",
                                    new StackExchange.Redis.RedisKey[] { key });
                                serializer.Serialize(writer, new Dictionary<string, object> { ["key"] = key.ToString(), ["keyBytes"] = Convert.ToBase64String((byte[])key), ["dump"] = Convert.ToBase64String((byte[])snapshot[0]), ["pttl"] = (long)snapshot[1] });
                                report.Success++;
                            }
                            catch (RedisException ex) { report.Errors.Add($"{key}: {ex.Message}"); }
                            if ((report.Success + report.Errors.Count) % 100 == 0) progress.Report($"Exported {report.Success} keys; failed {report.Errors.Count}");
                        }
                        writer.WriteEndArray();
                    }
                    if (report.Canceled && report.Success == 0) return report;
                    if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                    else File.Move(temporary, path);
                    return report;
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            });
            if (!outcome.IsSuccess) return;
            var result = outcome.Value;
            result.Show(this, result.Canceled ? (result.Success > 0 ? "Partial export saved" : "Export canceled; file unchanged") : "Export result");
        }

        private async void import_data_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!CanWriteSelected()) return;
            var selected = treeView_server.SelectedNode;
            if (selected == null || GetDbNode(selected)?.Tag is not DbSettings dbSettings) return;
            var client = (RedisClient)GetRedisNode(selected).Tag;
            var database = client.GetDB(dbSettings.DBNumber);
            using var dialog = new OpenFileDialog { Filter = "JSON files (*.json)|*.json" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            string path = dialog.FileName;
            if (MessageBox.Show(this, $"Import {Path.GetFileName(path)} to {client.Settings.name} [{client.Settings.host}:{client.Settings.port}], DB {dbSettings.DBNumber}?\nExisting keys will be skipped. Completed imports remain if canceled.", "Confirm import", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            var outcome = await OperationDialog.RunAsync(this, "Import data", (token, progress) =>
            {
                var report = new OperationReport();
                using var reader = new JsonTextReader(new StreamReader(path));
                var serializer = new JsonSerializer();
                if (!reader.Read() || reader.TokenType != JsonToken.StartArray) throw new InvalidDataException("Expected a JSON array");
                int index = 0;
                try
                {
                while (reader.Read() && reader.TokenType != JsonToken.EndArray)
                {
                    if (token.IsCancellationRequested) { report.Canceled = true; break; }
                    index++;
                    // Parse a complete entry before writing; malformed JSON ends the file with a partial-result report.
                    Dictionary<string, object> entry;
                    try { entry = serializer.Deserialize<Dictionary<string, object>>(reader); }
                    catch (JsonException ex) { report.Errors.Add($"Entry {index}: {ex.Message}"); break; }
                    try { if (ImportEntry(database, entry)) report.Success++; else report.Skipped++; }
                    catch (Exception ex) { report.Errors.Add($"Entry {index} ({(entry != null && entry.TryGetValue("key", out var key) ? key : "unknown")}): {ex.Message}"); }
                    if (index % 100 == 0) progress.Report($"Processed {index}: imported {report.Success}, skipped {report.Skipped}, failed {report.Errors.Count}");
                }
                    if (!report.Canceled && reader.TokenType != JsonToken.EndArray) report.Errors.Add("Unexpected end of JSON file; only completed entries were imported");
                }
                catch (JsonException ex) { report.Errors.Add("Invalid JSON: " + ex.Message); }
                return report;
            });
            if (!outcome.IsSuccess) return;
            outcome.Value.Show(this, "Import result");
            await RefreshDbKeysAsync(GetDbNode(selected), true);
        }

        private static bool ImportEntry(IDatabase database, Dictionary<string, object> entry)
        {
            string key = entry != null && entry.TryGetValue("key", out var keyObj) ? keyObj?.ToString() : null;
            string type = entry != null && entry.TryGetValue("type", out var typeObj) ? typeObj?.ToString() : null;
            if (key == null) throw new InvalidDataException("Missing key");
            if (entry.ContainsKey("error")) throw new InvalidDataException("Export entry contains an error");
            if (entry.TryGetValue("dump", out var dump))
            {
                byte[] payload = Convert.FromBase64String(dump.ToString());
                long ttl = Convert.ToInt64(entry["pttl"]);
                if (ttl < -1) throw new InvalidDataException("Invalid TTL");
                if (ttl == 0) return false;
                var restoreKey = entry.TryGetValue("keyBytes", out var encodedKey)
                    ? (StackExchange.Redis.RedisKey)Convert.FromBase64String(encodedKey.ToString()) : (StackExchange.Redis.RedisKey)key;
                if (database.KeyExists(restoreKey)) return false;
                database.KeyRestore(restoreKey, payload, ttl < 0 ? null : TimeSpan.FromMilliseconds(ttl));
                return true;
            }
            if (database.KeyExists(key)) return false;
            var value = JToken.FromObject(entry["value"] ?? throw new InvalidDataException("Missing value"));
            var commands = new List<string[]>();
            switch (type)
            {
                case "String":
                    if (value.Type != JTokenType.String) throw new InvalidDataException("Expected text value");
                    commands.Add(new[] { "SET", key, value.ToString() });
                    break;
                case "Hash":
                    foreach (var item in (JArray)value)
                        commands.Add(new[] { "HSET", key, item["field"]?.Value<string>() ?? throw new InvalidDataException("Missing field"), item["value"]?.Value<string>() ?? throw new InvalidDataException("Missing value") });
                    break;
                case "List":
                case "Set":
                    foreach (var item in (JArray)value)
                    {
                        if (item.Type != JTokenType.String) throw new InvalidDataException("Expected text member");
                        commands.Add(new[] { type == "List" ? "RPUSH" : "SADD", key, item.ToString() });
                    }
                    break;
                case "SortedSet":
                    foreach (var item in (JArray)value)
                    {
                        double score = item["score"].Value<double>();
                        if (double.IsNaN(score) || double.IsInfinity(score)) throw new InvalidDataException("Invalid score");
                        commands.Add(new[] { "ZADD", key, score.ToString(System.Globalization.CultureInfo.InvariantCulture), item["member"]?.Value<string>() ?? throw new InvalidDataException("Missing member") });
                    }
                    break;
                case "Stream":
                    ulong previousMs = 0, previousSeq = 0;
                    foreach (var item in (JArray)value)
                    {
                        string id = item["id"]?.Value<string>() ?? throw new InvalidDataException("Missing stream ID");
                        var parts = id.Split('-');
                        if (parts.Length != 2 || !ulong.TryParse(parts[0], out ulong ms) || !ulong.TryParse(parts[1], out ulong seq) || ms < previousMs || (ms == previousMs && seq <= previousSeq))
                            throw new InvalidDataException("Stream IDs must be valid and increasing");
                        previousMs = ms; previousSeq = seq;
                        var command = new List<string> { "XADD", key, id };
                        foreach (var field in (JArray)item["fields"])
                        {
                            command.Add(field["name"]?.Value<string>() ?? throw new InvalidDataException("Missing field"));
                            command.Add(field["value"]?.Value<string>() ?? throw new InvalidDataException("Missing value"));
                        }
                        if (command.Count == 3) throw new InvalidDataException("Stream entry has no fields");
                        commands.Add(command.ToArray());
                    }
                    break;
                default: throw new InvalidDataException("Unsupported type: " + type);
            }
            if (commands.Count == 0) throw new InvalidDataException("Empty collection cannot be restored");
            long legacyTtl = -1;
            if (entry.TryGetValue("ttl", out var ttlObj) && ttlObj != null)
            {
                legacyTtl = Convert.ToInt64(ttlObj);
                if (legacyTtl < -1 || legacyTtl > long.MaxValue / 1000) throw new InvalidDataException("Invalid TTL");
                if (legacyTtl == 0) return false;
            }
            var imported = (long)database.ScriptEvaluate(
                "if redis.call('EXISTS',KEYS[1]) == 1 then return 0 end; local commands=cjson.decode(ARGV[1]); for _,cmd in ipairs(commands) do redis.call(unpack(cmd)) end; if tonumber(ARGV[2]) > 0 then redis.call('PEXPIRE',KEYS[1],ARGV[2]) end; return 1",
                new StackExchange.Redis.RedisKey[] { key },
                new RedisValue[] { JsonConvert.SerializeObject(commands), legacyTtl < 0 ? -1 : legacyTtl * 1000 });
            if (imported == 0) return false;

            return true;
        }

        private async void edit_connection_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!ValueControl.ConfirmAll(panel1)) return;
            TreeNode select = treeView_server.SelectedNode;
            if (select == null)
            {
                return;
            }

            if (select.Tag is RedisClient client)
            {
                using (FormRedisAdd form = new FormRedisAdd(client.Settings))
                {
                    if (form.ShowDialog() == DialogResult.OK)
                    {
                        if (client.Redis != null)
                        {
                            client.Close();
                            client.Redis = null;
                        }

                        client.Settings = form.Settings;

                        SaveRedisSettings();

                        await RefreshRedisKeyAsync(select, true);
                    }
                }
            }
        }

        private void disconnect_connection_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!ValueControl.ConfirmAll(panel1)) return;
            TreeNode select = treeView_server.SelectedNode;
            if (select == null)
            {
                return;
            }

            if (select.Tag is RedisClient client)
            {
                if (client.Redis != null)
                {
                    client.Close();
                }

                client.Redis = null;
                DisposeKeyScans(select);
                select.Nodes.Clear();
            }
        }

        private void delete_connection_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!ValueControl.ConfirmAll(panel1)) return;
            TreeNode select = treeView_server.SelectedNode;
            if (select == null)
            {
                return;
            }

            if (select.Tag is RedisClient client)
            {
                if (client != null && client.Redis != null)
                {
                    client.Close();
                }

                RemoveServer(client);

                SaveRedisSettings();

                treeView_server.Nodes.Remove(select);
            }
        }

        private async void new_key_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!CanWriteSelected()) return;
            TreeNode select = treeView_server.SelectedNode;
            if (select == null)
            {
                return;
            }

            RedisClient redisClient = select.Parent.Tag as RedisClient;
            if (redisClient == null)
            {
                MessageBox.Show("Invalid redis setting");

                return;
            }

            if (select.Tag is DbSettings dbSettings)
            {
                var db = redisClient.GetDB(dbSettings.DBNumber);
                using (FormRedisInput redisInput = new FormRedisInput(db))
                {
                    redisInput.ShowDialog();
                    await RefreshDbKeysAsync(select, true);
                }
            }
        }

        private async void reload_keys_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode select = treeView_server.SelectedNode;
            if (select == null)
            {
                return;
            }

            if (select.Tag is DbSettings)
            {
                await RefreshDbKeysAsync(select, true);
            }
        }

        private async void filter_key_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode select = treeView_server.SelectedNode;
            if (select == null)
            {
                return;
            }

            if (select.Tag is DbSettings dbSettings)
            {
                using (FormInputString formInput = new FormInputString())
                {
                    formInput.TextInfo = "Filter";
                    formInput.InputValue = dbSettings.Filter;

                    if (formInput.ShowDialog() == DialogResult.OK)
                    {
                        dbSettings.Filter = formInput.InputValue == "" ? "*" : formInput.InputValue;
                        await RefreshDbKeysAsync(select, true);
                        select.ExpandAll();
                    }
                }
            }
        }

        private async void unfilter_key_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode select = treeView_server.SelectedNode;
            if (select == null)
            {
                return;
            }

            if (select.Tag is DbSettings dbSettings)
            {
                dbSettings.Filter = "*";
                await RefreshDbKeysAsync(select, true);
                select.ExpandAll();
            }
        }

        private void remove_keys_ToolStripMenuItem_Click(object sender, EventArgs e)
		{
            if (!CanWriteSelected()) return;
            PromptBatchDelete(false);
        }

        private async void remove_db_ToolStripMenuItem_Click(object sender, EventArgs e)
		{
			TreeNode select = treeView_server.SelectedNode;
			if (select == null)
			{
				return;
			}

			if (select.Tag is DbSettings dbSettings)
			{
                RedisClient redisClient = select.Parent?.Tag as RedisClient;
                if (redisClient == null) return;
                if (redisClient.Settings.additional_dbs == null)
                    redisClient.Settings.additional_dbs = new List<int>();
                redisClient.Settings.additional_dbs.Remove(dbSettings.DBNumber);

				SaveRedisSettings();

				await RefreshRedisKeyAsync(select.Parent, true);
			}
        }

        private void queryWindowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode select = treeView_server.SelectedNode;
            if (select == null)
            {
                return;
            }

            if (select.Tag is DbSettings dbSettings)
            {
                RedisClient redisClient = select.Parent.Tag as RedisClient;

                FormQueryWindow fqw = new FormQueryWindow();
                fqw.SetServerInfo(redisClient, dbSettings.DBNumber);
                fqw.Show();
            }
        }

        // Tree view action
        private void treeView_server_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                treeView_server.AfterSelect -= treeView_server_AfterSelect;

                TreeNode node = treeView_server.GetNodeAt(e.Location);
                if (treeView_server.SelectedNode != node)
                {
                    treeView_server.SelectedNode = node;
                }

                treeView_server.AfterSelect += treeView_server_AfterSelect;

                TreeNode select = treeView_server.SelectedNode;
                if (select == null)
                {
                    return;
                }

                if (select.Tag is DbSettings)
                {
                    contextMenuStrip_db.Show(treeView_server, e.Location);
                }
                else if (select.Tag is RedisClient)
                {
                    contextMenuStrip_redis.Show(treeView_server, e.Location);
                }
                else if (select.Tag is RedisFolder)
                {
                    contextMenuStrip_class.Show(treeView_server, e.Location);
                }
                else if (select.Tag is RedisKey)
                {
                    if (select.Text.Contains("  (Deleted)") == false)
                    {
                        contextMenuStrip_key.Show(treeView_server, e.Location);
                    }
                }
            }
        }

        private void treeView_server_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                bool is_same_node = false;
                TreeNode node = treeView_server.GetNodeAt(e.Location);
                if (treeView_server.SelectedNode != node)
                {
                    treeView_server.SelectedNode = node;
                }
                else
                {
                    is_same_node = true;
                }

                TreeNode select = treeView_server.SelectedNode;
                if (select == null)
                {
                    return;
                }

                if (is_same_node)
                {
                    treeView_server_AfterSelect(null, null);
                }

                if (select.IsExpanded)
                {
                    select.Collapse();
                }
                else
                {
                    select.Expand();
                }
            }
        }

        private async void treeView_server_AfterSelect(object sender, TreeViewEventArgs e)
        {
            TreeNode select = treeView_server.SelectedNode;
            if (select == null) return;
            // Tag 可能为 null（新创建的临时节点），直接返回
            if (select.Tag == null) return;

            if (select.Tag is Action loadMore) { loadMore(); return; }
            if (select.Tag is RedisGroup)
            {
                return;
            }

            var old_imagekey = select.ImageKey;
            select.ImageKey = "loading";
            select.SelectedImageKey = "loading";
            treeView_server.Invalidate(true);
            PumpUi();

            try
            {
            if (select.Tag is RedisClient)
            {
                CreateRedisShowTagControl<StartControl>();
                await RefreshRedisKeyAsync(select);
            }
            else if (select.Tag is DbSettings)
            {
                CreateRedisShowTagControl<StartControl>();
                await RefreshDbKeysAsync(select, false);
            }
            else if (select.Tag is RedisFolder)
            {
                BuildTreeNode_Folder(select);
            }
            else if (select.Tag is RedisKey)
            {
                if (select.Text.Contains("  (Deleted)") == false)
                {
                    TreeNode dbNode = GetDbNode(select);
                    TreeNode redisNode = GetRedisNode(select);

                    if (redisNode.Tag is RedisClient redisClient)
                    {
                        if (dbNode.Tag is DbSettings dbSettings)
                        {
                            if (redisClient.DBBlock != dbSettings.DBNumber)
                            {
                                OperateResult change = redisClient.SelectDB(dbSettings.DBNumber);
                                if (change.IsSuccess == false)
                                {
                                    select.ImageKey = old_imagekey;
                                    select.SelectedImageKey = old_imagekey;
                                    MessageBox.Show(string.Format("Select [{0}] DB {1} fail\r\n", redisClient.Settings.ToString(), dbSettings.DBNumber) + change.Message);

                                    return;
                                }

                                redisClient.DBBlock = dbSettings.DBNumber;
                            }

                            toolStripStatusLabel1.Text = $"{redisClient.Settings.name} · DB {dbSettings.DBNumber} · {(redisClient.IsConnected ? "Connected" : "Disconnected")} {(redisClient.Settings.read_only ? "· read-only" : "")}";
                            var type = redisClient.Redis.KeyType(select.Text);
                            switch (type)
                            {
                                case StackExchange.Redis.RedisType.String:
                                {
                                    await StringKeySelect(redisClient, select);
                                }
                                break;
                                case StackExchange.Redis.RedisType.List:
                                {
                                    await ListKeySelect(redisClient, select);
                                }
                                break;
                                case StackExchange.Redis.RedisType.Hash:
                                {
                                    await HashKeySelect(redisClient, select);
                                }
                                break;
                                case StackExchange.Redis.RedisType.Set:
                                {
                                    await SetKeySelect(redisClient, select);
                                }
                                break;
                                case StackExchange.Redis.RedisType.SortedSet:
                                {
                                    await ZSetKeySelect(redisClient, select);
                                }
                                break;
                                case StackExchange.Redis.RedisType.Stream:
                                {
                                    await StreamKeySelect(redisClient, select);
                                }
                                break;
                                default:
                                {
                                    MessageBox.Show($"key {select.Text} is not exist");
                                }
                                break;
                            }
                        }
                    }
                }
                else
                {
                    if (userControl == null || (userControl as StartControl) == null)
                    {
                        CreateRedisShowTagControl<StartControl>();
                    }
                }
            }

            }
            catch (RedisException ex)
            {
                MessageBox.Show(this, ex.Message + "\nReload the connection to retry.", "Redis request failed");
            }
            finally
            {
                select.ImageKey = old_imagekey;
                select.SelectedImageKey = old_imagekey;
            }
        }

        private TreeNode GetRedisNode(TreeNode treeNode)
        {
            if (treeNode == null) return null;
            if (treeNode.Tag is RedisClient)
            {
                return treeNode;
            }
            if (treeNode.Parent == null) return null;
            return GetRedisNode(treeNode.Parent);
        }

        private TreeNode GetDbNode(TreeNode treeNode)
        {
            if (treeNode == null) return null;
            if (treeNode.Tag is DbSettings)
            {
                return treeNode;
            }
            if (treeNode.Parent == null) return null;
            return GetDbNode(treeNode.Parent);
        }

        private TreeNode GetDbNodeFromRedisNode(TreeNode redisNode, int db_num)
		{
            if (redisNode.Tag is not RedisClient)
			{
                return null;
			}

            foreach (TreeNode node in redisNode.Nodes)
			{
                if (node.Tag is DbSettings dbSettings)
				{
                    if (dbSettings.DBNumber == db_num)
					{
                        return node;
					}
				}
			}

            return null;
		}

        private string GetFullpathFolder(TreeNode treeNode, string path)
        {
            if (treeNode == null) return path.Length > 0 ? path.Substring(1) : path;
            if (treeNode.Tag is DbSettings)
            {
                return path.Length > 0 ? path.Substring(1) : path;
            }
            else if (treeNode.Tag is RedisFolder redis_folder)
            {
                path = ":" + redis_folder.path_name + path;
            }
            if (treeNode.Parent == null) return path.Length > 0 ? path.Substring(1) : path;

            return GetFullpathFolder(treeNode.Parent, path);
        }

        private void FilterKeys(TreeNode select)
        {
            if (select.Tag is DbSettings dbSettings)
            {
                select.Nodes.Clear();

                var filter_list_keys = (dbSettings.Keys ?? new List<string>()).ToList();

                if (dbSettings.Filter != "" &&
                    dbSettings.Filter != "*")
                {
                    filter_list_keys = filter_list_keys.Where(s => Utils.RedisGlobMatch(dbSettings.Filter, s)).ToList();
                    select.Text = string.Format("db{0} ({1}) [{2}]", dbSettings.DBNumber, filter_list_keys.Count(), dbSettings.Filter);
                }
                else
                {
                    select.Text = string.Format("db{0} ({1})", dbSettings.DBNumber, filter_list_keys.Count());
                }

                BuildTreeNode_DB(select, filter_list_keys);
                if (dbSettings.HasMoreKeys)
                    select.Nodes.Add(new TreeNode("Load next 500 keys…") { Tag = (Action)(async () => await LoadDbKeyPageAsync(select)) });
            }
        }

        // Refresh key
        private async Task RefreshRedisKeyAsync(TreeNode select, bool reload = false)
        {
            if (select.Tag is RedisClient redisClient)
            {
                if (reload)
                {
                    if (!ValueControl.ConfirmAll(panel1)) return;
                    CreateRedisShowTagControl<StartControl>();
                    DisposeKeyScans(select);
                    select.Nodes.Clear();
                }

                if (reload && !redisClient.IsConnected) redisClient.Close();
                if (redisClient.Redis == null)
                {
                    var connectOutcome = await OperationDialog.RunAsync(this, "Connect", (token, progress) => redisClient.Connect());
                    if (!connectOutcome.IsSuccess) return;
                    var connect = connectOutcome.Value;
                    if (connect.IsSuccess == false)
                    {
                        MessageBox.Show(string.Format("Failed to connect to redis[{0}] IpAddress:{1} Port:{2}\r\n", redisClient.Settings.name, redisClient.Settings.host, redisClient.Settings.port) + connect.Message);

                        return;
                    }
                }

                if (select.Nodes.Count == 0 || reload)
                {
                    if (redisClient.Settings.use_cluster)
                    {
                        // Cluster mode only supports db 0
                        AddTreeNode_DB(redisClient, 0, select);
                    }
                    else
                    {
                        if (redisClient.Settings.hide_default_dbs == false)
                        {
                            for (int i = 0; i < redisClient.RedisServer.DatabaseCount; i++)
                            {
                                AddTreeNode_DB(redisClient, i, select);
                            }
                        }

                        if (redisClient.Settings.additional_dbs != null)
                        {
                            redisClient.Settings.additional_dbs.Sort();
                            foreach (int dbNum in redisClient.Settings.additional_dbs)
                            {
                                AddTreeNode_DB(redisClient, dbNum, select);
                            }
                        }
                    }

                    if (redisClient.SelectDB(0).IsSuccess)
                    {
                        redisClient.DBBlock = 0;
                    }
                }
            }
        }

        private void DisposeKeyScans(TreeNode node)
        {
            if (node.Tag is DbSettings db) db.KeyScan?.Dispose();
            foreach (TreeNode child in node.Nodes) DisposeKeyScans(child);
        }

        private async Task RefreshDbKeysAsync(TreeNode select, bool reload = false)
        {
            if (select.Tag is not DbSettings db) return;
            if (!ValueControl.ConfirmAll(panel1)) return;
            if (reload || db.KeyScan == null && db.Keys == null)
            {
                db.KeyScan?.Dispose();
                var client = (RedisClient)select.Parent.Tag;
                db.KeyScan = client.ScanKeys(db.DBNumber, db.Filter, Config.scan_page_count).GetEnumerator();
                db.Keys = new List<string>();
                db.HasMoreKeys = true;
                await LoadDbKeyPageAsync(select);
            }
        }

        private async Task LoadDbKeyPageAsync(TreeNode node)
        {
            var db = (DbSettings)node.Tag;
            var outcome = await OperationDialog.RunAsync(this, "Load keys", (token, progress) =>
            {
                var page = new List<string>();
                bool more = true;
                string error = null;
                try
                {
                    while (page.Count < PageNavigator.PageSize && !token.IsCancellationRequested)
                    {
                        if (!db.KeyScan.MoveNext()) { more = false; break; }
                        page.Add(db.KeyScan.Current.ToString());
                    }
                }
                catch (RedisException ex) { error = ex.Message; more = false; }
                return (Keys: page, More: more, Error: error, Canceled: token.IsCancellationRequested);
            });
            if (!outcome.IsSuccess) return;

            var result = outcome.Value;
            var existing = db.Keys.ToHashSet(StringComparer.Ordinal);
            db.Keys.AddRange(result.Keys.Where(existing.Add));
            db.HasMoreKeys = result.More;
            if (!db.HasMoreKeys) { db.KeyScan.Dispose(); db.KeyScan = null; }
            FilterKeys(node);
            node.Expand();
            if (result.Error != null) MessageBox.Show(this, result.Error + "\nAlready loaded keys remain; reload to retry.", "Key scan failed");
            if (result.Canceled) toolStripStatusLabel1.Text = "Key scan paused; use Load next to continue";
        }

        private void BuildTreeNode_Folder(TreeNode parent)
        {
            if (parent.Tag is RedisFolder redis_folder)
            {
                if (parent.Nodes.Count > 0)
                {
                    return;
                }

                var qry = redis_folder.sub_items.
                Select(c => new
                {
                    SplitArray = c.Key.Split(':'),
                    Value = c
                }).
                Select(c => new
                {
                    Prefix = c.SplitArray.Length <= 1 ? "" : c.SplitArray.First(),
                    Postfix = c.Value.Key == "" ? "" :
                        (c.Value.Key.Replace(c.SplitArray.First(), "").Length > 0 && c.Value.Key.Replace(c.SplitArray.First(), "")[0] == ':') ?
                        c.Value.Key.Replace(c.SplitArray.First() + ":", "") :
                        c.Value.Key.Replace(c.SplitArray.First(), ""),
                    Value = c
                }).
                GroupBy(r => r.Prefix).
                Select(grp => new
                {
                    FolderName = grp.Key,
                    SubItems = grp.Count() > 1 ? grp.Select(t => new KeyValuePair<string, string>(t.Postfix, t.Value.Value.Value.ToString())) : null,
                    Value = grp.First().Value
                });

                List<TreeNode> list_folder_node = new List<TreeNode>();
                List<TreeNode> list_item_node = new List<TreeNode>();
                foreach (var item in qry)
                {
                    if (item.SubItems != null)
                    {
                        List<KeyValuePair<string, string>> items = new List<KeyValuePair<string, string>>();
                        foreach (var sub in item.SubItems)
						{
                            if (sub.Key == "")
							{
								TreeNode key_node = new TreeNode(sub.Value.ToString());
                                key_node.ImageKey = "keyword";
                                key_node.SelectedImageKey = "keyword";
                                key_node.Tag = new RedisKey();
								list_item_node.Add(key_node);
							}
                            else
							{
                                items.Add(sub);
							}
						}

                        // 폴더
                        int item_count = items.Count();
                        if (item_count > 0)
                        {
                            string node_name = $"{item.FolderName} ({item_count})";

                            RedisFolder folder = new RedisFolder();
                            folder.path_name = item.FolderName;
                            folder.sub_items = items;
                            folder.display_name = node_name;

                            TreeNode node = new TreeNode(node_name);
                            node.Tag = folder;
                            node.ImageKey = "Class_489";
                            node.SelectedImageKey = "Class_489";
                            list_folder_node.Add(node);
                        }
                    }
                    else
                    {
                        // 키
                        TreeNode node = new TreeNode(item.Value.Value.Value.ToString());
                        node.ImageKey = "keyword";
                        node.SelectedImageKey = "keyword";
                        node.Tag = new RedisKey();
                        list_item_node.Add(node);
                    }
                }

                list_folder_node.Sort((TreeNode x, TreeNode y) => x.Text.CompareTo(y.Text));
                list_item_node.Sort((TreeNode x, TreeNode y) => x.Text.CompareTo(y.Text));

                parent.Nodes.AddRange(list_folder_node.ToArray());
                parent.Nodes.AddRange(list_item_node.ToArray());
            }
        }

        private TreeNode GetTreeNode_DB(int dbNum, TreeNode select)
		{
			foreach (TreeNode node in select.Nodes)
			{
				if (node.Text.Contains($"db{dbNum} "))
				{
					return node;
				}
			}

			return null;
		}

        private bool HasTreeNode_DB(int dbNum, TreeNode select)
		{
            return GetTreeNode_DB(dbNum, select) != null;
		}

        private bool AddTreeNode_DB(RedisClient redisClient, int dbNum, TreeNode select, bool msg_box = true)
		{
            if (HasTreeNode_DB(dbNum, select))
			{
                return false;
			}

            try
            {
                if (redisClient.TryDatabaseSize(dbNum, out long keyCount) == false)
                {
                    if (msg_box)
                    {
                        MessageBox.Show($"Could not read the key count for db{dbNum}. The server may be unreachable.");
                    }

                    return false;
                }

                TreeNode dbTree = new TreeNode(string.Format("db{0} ({1})", dbNum, keyCount));
                dbTree.ImageKey = "redis_db";
                dbTree.SelectedImageKey = "redis_db";
                dbTree.Tag = new DbSettings() { DBNumber = dbNum, Keys = null };

                select.Nodes.Add(dbTree);

                // Load keys only when the database is selected.
            }
            catch (RedisCommandException ex)
			{
                if (msg_box)
				{
                    MessageBox.Show(ex.Message);
                }

                return false;
			}

            return true;
		}

        private void BuildTreeNode_DB(TreeNode parent, List<string> keys)
        {
            if (parent.Nodes.Count > 0)
            {
                return;
            }

            var qry = keys.
            Select(c => new
            {
                SplitArray = c.Split(':'),
                Value = c
            }).
            Select(c => new
            {
                Prefix = c.SplitArray.Length <= 1 ? "" : c.SplitArray.First(),
                Postfix = c.Value == "" ? "" :
                    (c.Value.Replace(c.SplitArray.First(), "").Length > 0 && c.Value.Replace(c.SplitArray.First(), "")[0] == ':') ?
                    c.Value.Replace(c.SplitArray.First() + ":", "") :
                    c.Value.Replace(c.SplitArray.First(), ""),
                Value = c
            }).
            GroupBy(r => r.Prefix).
            Select(grp => new
            {
                FolderName = grp.Key,
                SubItems = grp.Count() > 1 ? grp.Select(t => new KeyValuePair<string, string>(t.Postfix, t.Value.Value.ToString())) : null,
                Value = grp.First().Value
            });

            List<TreeNode> list_folder_node = new List<TreeNode>();
            List<TreeNode> list_item_node = new List<TreeNode>();
            foreach (var item in qry)
            {
                if (item.SubItems != null)
                {
					List<KeyValuePair<string, string>> items = new List<KeyValuePair<string, string>>();
					foreach (var sub in item.SubItems)
					{
						if (sub.Key == "")
						{
							TreeNode key_node = new TreeNode(sub.Value.ToString());
							key_node.ImageKey = "keyword";
							key_node.SelectedImageKey = "keyword";
							key_node.Tag = new RedisKey();
							list_item_node.Add(key_node);
						}
						else
						{
							items.Add(sub);
						}
					}

					// 폴더
					int item_count = items.Count();
                    if (item_count > 0)
                    {
                        string node_name = $"{item.FolderName} ({item_count})";

                        RedisFolder folder = new RedisFolder();
                        folder.path_name = item.FolderName;
                        folder.sub_items = items;
                        folder.display_name = node_name;

                        TreeNode node = new TreeNode(node_name);
                        node.Tag = folder;
                        node.ImageKey = "Class_489";
                        node.SelectedImageKey = "Class_489";
                        list_folder_node.Add(node);
                    }
                }
                else
                {
                    // 키
                    TreeNode node = new TreeNode(item.Value.Value.ToString());
                    node.ImageKey = "keyword";
                    node.SelectedImageKey = "keyword";
                    node.Tag = new RedisKey();
                    list_item_node.Add(node);
                }
            }

            list_folder_node.Sort((TreeNode x, TreeNode y) => x.Text.CompareTo(y.Text));
            list_item_node.Sort((TreeNode x, TreeNode y) => x.Text.CompareTo(y.Text));

            parent.Nodes.AddRange(list_folder_node.ToArray());
            parent.Nodes.AddRange(list_item_node.ToArray());
        }

        // Show value
        private void CreateRedisShowTagControl<T>() where T : UserControl, new()
        {
            if (userControl != null && !ValueControl.ConfirmAll(userControl)) return;
            T control = new T();
            panel1.Controls.Add(control);
            control.Location = new Point(0, 0);
            control.Size = new Size(panel1.Width - 1, panel1.Height - 1);
            control.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            if (userControl != null)
            {
                panel1.Controls.Remove(userControl);
            }

            if (userControl != null)
            {
                userControl.Dispose();
            }

            userControl = control;
        }

        // Key-type dispatch for the preview panel lives in FormMain.KeyEditors.cs.

        // Load / Save / Add servers
        private RedisClient ObserveClient(RedisClient client)
        {
            client.ConnectionStatusChanged += status =>
            {
                if (!IsHandleCreated || IsDisposed) return;
                try { BeginInvoke((Action)(() => { if (!IsDisposed) toolStripStatusLabel1.Text = $"{client.Settings.name}: {status}{(client.Settings.read_only ? " · read-only" : "")}"; })); }
                catch (InvalidOperationException) { }
            };
            return client;
        }

		private void LoadRedisSettings()
        {
            treeView_server.Nodes.Clear();

            foreach (var group in redis_group)
            {
                TreeNode node = new TreeNode(group.name);
                node.ImageKey = "ListView_687";
                node.SelectedImageKey = "ListView_687";
                node.Tag = group;
                treeView_server.Nodes.Add(node);

                foreach (var settings in group.connections)
                {
                    TreeNode node_con = new TreeNode(settings.name);
                    node_con.ImageKey = "VirtualMachine";
                    node_con.SelectedImageKey = "VirtualMachine";
                    node_con.Tag = settings.redis_client;
                    node.Nodes.Add(node_con);
                }
            }

            foreach (var settings in redis_settings)
            {
                TreeNode node = new TreeNode(settings.name);
                node.ImageKey = "VirtualMachine";
                node.SelectedImageKey = "VirtualMachine";
                node.Tag = settings.redis_client;
                treeView_server.Nodes.Add(node);
            }
        }

        private void SaveRedisSettings()
        {
            try
            {
                string connDir = GetConnectionsDir();
                string defaultFull = Path.GetFullPath(GetDefaultConnectionsFile());
                if (_failedFiles.Contains(defaultFull)) defaultFull = Path.Combine(GetConnectionsDir(), "connections.recovered.json");
                if (!Directory.Exists(connDir))
                    Directory.CreateDirectory(connDir);

                // group entries by target file
                var fileMap = new Dictionary<string, JArray>(StringComparer.OrdinalIgnoreCase);

                // ensure known files are considered
                foreach (var f in _knownFiles)
                {
                    if (!_failedFiles.Contains(f) && !fileMap.ContainsKey(f))
                        fileMap[f] = new JArray();
                }

                if (!fileMap.ContainsKey(defaultFull))
                    fileMap[defaultFull] = new JArray();

                foreach (var group in redis_group)
                {
                    string target = null;
                    if (!_groupFileMap.TryGetValue(group, out target) || string.IsNullOrEmpty(target))
                        target = defaultFull;
                    target = Path.GetFullPath(target);
                    if (!fileMap.ContainsKey(target))
                        fileMap[target] = new JArray();
                    fileMap[target].Add(JObject.FromObject(group));
                    _groupFileMap[group] = target;
                    // keep nested settings mapped to same file
                    if (group.connections != null)
                    {
                        foreach (var s in group.connections)
                            _settingFileMap[s] = target;
                    }
                    if (!_knownFiles.Contains(target))
                        _knownFiles.Add(target);
                }

                foreach (var setting in redis_settings)
                {
                    string target = null;
                    if (!_settingFileMap.TryGetValue(setting, out target) || string.IsNullOrEmpty(target))
                        target = defaultFull;
                    target = Path.GetFullPath(target);
                    if (!fileMap.ContainsKey(target))
                        fileMap[target] = new JArray();
                    fileMap[target].Add(JObject.FromObject(setting));
                    _settingFileMap[setting] = target;
                    if (!_knownFiles.Contains(target))
                        _knownFiles.Add(target);
                }

                // Replace atomically and keep the previous version as a backup.
                foreach (var kv in fileMap)
                {
                    if (_failedFiles.Contains(kv.Key)) continue;
                    try
                    {
                        string dir = Path.GetDirectoryName(kv.Key);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);
                        string tmp = kv.Key + ".tmp";
                        File.WriteAllText(tmp, kv.Value.ToString(Formatting.Indented), Encoding.UTF8);
                        if (File.Exists(kv.Key)) File.Replace(tmp, kv.Key, kv.Key + ".bak");
                        else File.Move(tmp, kv.Key);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Save failed for {kv.Key}\r\n{ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Save failed\r\n" + ex.Message);
            }
        }

        private void RemoveServer(RedisClient client)
        {
            // 安全地移除分组内连接：避免 foreach 中修改集合
            foreach (var group in redis_group)
            {
                if (group.connections == null) continue;
                for (int i = group.connections.Count - 1; i >= 0; i--)
                {
                    var settings = group.connections[i];
                    if (settings.redis_client == client)
                    {
                        _settingFileMap.Remove(settings);
                        group.connections.RemoveAt(i);
                        break;
                    }
                }
            }

            // 移除独立连接
            for (int i = redis_settings.Count - 1; i >= 0; i--)
            {
                var settings = redis_settings[i];
                if (settings.redis_client == client)
                {
                    _settingFileMap.Remove(settings);
                    redis_settings.RemoveAt(i);
                    break;
                }
            }
        }

        private void button_add_server_Click(object sender, EventArgs e)
        {
            using (FormRedisAdd form = new FormRedisAdd(null))
            {
                if (form.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                // 名称重复校验：避免树节点歧义（与现有独立连接及分组内连接比较）
                string newName = form.Settings.name?.Trim();
                bool dup = redis_settings.Any(s => string.Equals(s.name?.Trim(), newName, StringComparison.OrdinalIgnoreCase))
                    || redis_group.Any(g => g.connections != null && g.connections.Any(s => string.Equals(s.name?.Trim(), newName, StringComparison.OrdinalIgnoreCase)));
                if (dup)
                {
                    MessageBox.Show($"A connection named \"{newName}\" already exists. Choose a different name.",
                        "Duplicate name", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                redis_settings.Add(form.Settings);
                form.Settings.redis_client = ObserveClient(new RedisClient(form.Settings));
                string defaultFull = Path.GetFullPath(GetDefaultConnectionsFile());
                if (_failedFiles.Contains(defaultFull)) defaultFull = Path.Combine(GetConnectionsDir(), "connections.recovered.json");
                _settingFileMap[form.Settings] = defaultFull;
                _knownFiles.Add(defaultFull);
                SaveRedisSettings();

                TreeNode node = new TreeNode(form.Settings.name);
                node.ImageKey = "VirtualMachine";
                node.SelectedImageKey = "VirtualMachine";
                node.Tag = form.Settings.redis_client;
                treeView_server.Nodes.Add(node);
            }
        }

        private void button_open_server_Click(object sender, EventArgs e)
        {
            // 刷新所有连接：重新扫描 connections 目录并以树根节点加载
            if (!ValueControl.ConfirmAll(panel1)) return;
            LoadAllConnections();
        }

        private void FormMain_Move(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Normal && is_shown)
            {
                Config.mainform_pos_x = Location.X;
                Config.mainform_pos_y = Location.Y;
            }
        }

        private void FormMain_Resize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Normal && is_shown)
            {
                Config.mainform_pos_x = Location.X;
                Config.mainform_pos_y = Location.Y;
                Config.mainform_width = Width;
                Config.mainform_height = Height;
            }
        }

        public void RefreshRenamedKey(TreeNode node, string oldName, string newName)
        {
            var dbNode = GetDbNode(node);
            var settings = (DbSettings)dbNode.Tag;
            settings.Keys.Remove(oldName);
            settings.Keys.Add(newName);
            node.Text = newName;
            CreateRedisShowTagControl<StartControl>();
            treeView_server_AfterSelect(treeView_server, new TreeViewEventArgs(node));
        }

        public void delete_key_operate(TreeNode select, IDatabase database)
        {
            if (!CanWriteSelected()) return;
            if (MessageBox.Show(string.Format("Delete key [{0}]?", select.Text), "Delete key", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                if (database.KeyDelete(select.Text))
                {
                    var db_setting_node = get_db_setting_node(select);
                    if (db_setting_node != null &&
                        db_setting_node.Tag is DbSettings dbSettings)
                    {
                        dbSettings.Keys.Remove(select.Text);
						FilterKeys(db_setting_node);
                        db_setting_node.ExpandAll();
					}
                    else
                    {
                        select.Text += "  (Deleted)";
                    }
                }
                else
                {
                    MessageBox.Show("Delete key failed");
                }
            }

            if (userControl == null || (userControl as StartControl) == null)
            {
                CreateRedisShowTagControl<StartControl>();
            }
        }

        private TreeNode get_db_setting_node(TreeNode select)
		{
            if (select.Parent == null)
			{
                return null;
			}

            if (select.Parent.Tag is DbSettings dbSettings)
			{
                return select.Parent;
			}

            return get_db_setting_node(select.Parent);
		}

        private void delete_key_toolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode select = treeView_server.SelectedNode;
            if (select == null)
            {
                return;
            }

            if (select.Tag is not RedisKey)
            {
                return;
            }

            TreeNode dbNode = GetDbNode(select);
            TreeNode redisNode = GetRedisNode(select);

            if (redisNode.Tag is RedisClient redisClient)
            {
                if (dbNode.Tag is DbSettings dbSettings)
                {
                    var database = redisClient.GetDB(dbSettings.DBNumber);

                    delete_key_operate(select, database);
                }
            }
        }

        private void key_query_window_toolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode select = treeView_server.SelectedNode;
            if (select == null)
            {
                return;
            }

            if (select.Tag is not RedisKey)
            {
                return;
            }

            TreeNode dbNode = GetDbNode(select);
            TreeNode redisNode = GetRedisNode(select);

            if (redisNode.Tag is RedisClient redisClient)
            {
                if (dbNode.Tag is DbSettings dbSettings)
                {
                    var database = redisClient.GetDB(dbSettings.DBNumber);
                    var key_type = database.KeyType(select.Text);

                    FormQueryWindow fqw = new FormQueryWindow();
                    fqw.SetServerInfo(redisClient, dbSettings.DBNumber);
                    fqw.SetRedisKeysType(key_type);
                    fqw.SetRedisKeysFilter(select.Text);
                    fqw.Show();
                }
            }
        }

        private void view_ttl_toolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode select = treeView_server.SelectedNode;
            if (select == null || select.Tag is not RedisKey) return;

            TreeNode dbNode = GetDbNode(select);
            TreeNode redisNode = GetRedisNode(select);

            if (redisNode.Tag is RedisClient redisClient && dbNode.Tag is DbSettings dbSettings)
            {
                var database = redisClient.GetDB(dbSettings.DBNumber);
                var ttl = database.KeyTimeToLive(select.Text);

                string message;
                if (ttl == null)
                {
                    message = database.KeyExists(select.Text) ? "Key has no associated TTL (permanent)." : "Key does not exist.";
                }
                else if (ttl.Value.TotalSeconds < 0)
                {
                    message = database.KeyExists(select.Text) ? "Key has no associated TTL (permanent)." : "Key does not exist.";
                }
                else
                {
                    message = FormatTTL(ttl.Value);
                }

                MessageBox.Show(message, $"TTL of [{select.Text}]", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void set_ttl_toolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!CanWriteSelected()) return;
            TreeNode select = treeView_server.SelectedNode;
            if (select == null || select.Tag is not RedisKey) return;

            TreeNode dbNode = GetDbNode(select);
            TreeNode redisNode = GetRedisNode(select);

            if (redisNode.Tag is RedisClient redisClient && dbNode.Tag is DbSettings dbSettings)
            {
                var database = redisClient.GetDB(dbSettings.DBNumber);

                using FormInputString formInput = new FormInputString();
                formInput.TextInfo = "Set TTL (seconds), -1 = permanent, 0 = delete immediately";
                formInput.InputValue = "";

                if (formInput.ShowDialog() == DialogResult.OK)
                {
                    if (long.TryParse(formInput.InputValue, out long seconds) && seconds >= -1 && seconds <= TimeSpan.MaxValue.TotalSeconds)
                    {
                        if (seconds == -1)
                        {
                            if (database.KeyPersist(select.Text))
                            {
                                MessageBox.Show("TTL removed (key is now permanent).", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                            else
                            {
                                MessageBox.Show("Failed to remove TTL.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }
                        else if (seconds == 0)
                        {
                            if (database.KeyDelete(select.Text))
                            {
                                var db_setting_node = get_db_setting_node(select);
                                if (db_setting_node != null && db_setting_node.Tag is DbSettings dbs)
                                {
                                    dbs.Keys.Remove(select.Text);
                                    FilterKeys(db_setting_node);
                                    db_setting_node.ExpandAll();
                                }
                                MessageBox.Show("Key deleted (TTL=0).", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                            else
                            {
                                MessageBox.Show("Failed to delete key.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }
                        else
                        {
                            if (database.KeyExpire(select.Text, TimeSpan.FromSeconds(seconds)))
                            {
                                var ttl = database.KeyTimeToLive(select.Text);
                                string ttlStr = ttl != null ? FormatTTL(ttl.Value) : "permanent";
                                MessageBox.Show($"TTL set successfully.\r\n\r\nKey: {select.Text}\r\nRemaining: {ttlStr}", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                            else
                            {
                                MessageBox.Show("Failed to set TTL.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }
                    }
                    else
                    {
                        MessageBox.Show("Please enter a valid number.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void remove_ttl_toolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!CanWriteSelected()) return;
            TreeNode select = treeView_server.SelectedNode;
            if (select == null || select.Tag is not RedisKey) return;

            TreeNode dbNode = GetDbNode(select);
            TreeNode redisNode = GetRedisNode(select);

            if (redisNode.Tag is RedisClient redisClient && dbNode.Tag is DbSettings dbSettings)
            {
                var database = redisClient.GetDB(dbSettings.DBNumber);

                if (MessageBox.Show($"Remove TTL from key [{select.Text}]?\r\nThe key will become permanent.", "Remove TTL", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    if (database.KeyPersist(select.Text))
                    {
                        MessageBox.Show("TTL removed. Key is now permanent.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        MessageBox.Show("Failed to remove TTL.\r\nThe key may not exist or already has no TTL.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private static string FormatTTL(TimeSpan ttl)
        {
            if (ttl.TotalSeconds < 0) return "permanent (no TTL)";

            int days = (int)ttl.TotalDays;
            int hours = ttl.Hours;
            int minutes = ttl.Minutes;
            int seconds = ttl.Seconds;

            if (days > 0) return $"{days}d {hours}h {minutes}m {seconds}s ({(long)ttl.TotalSeconds}s)";
            if (hours > 0) return $"{hours}h {minutes}m {seconds}s ({(long)ttl.TotalSeconds}s)";
            if (minutes > 0) return $"{minutes}m {seconds}s ({(long)ttl.TotalSeconds}s)";
            return $"{seconds}s ({(long)ttl.TotalSeconds}s)";
        }

        private void folder_query_window_toolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode select = treeView_server.SelectedNode;
            if (select == null)
            {
                return;
            }

            if (select.Tag is not RedisFolder)
            {
                return;
            }

            TreeNode dbNode = GetDbNode(select);
            TreeNode redisNode = GetRedisNode(select);
            string key_filter = GetFullpathFolder(select, "") + "*";

            if (redisNode.Tag is RedisClient redisClient)
            {
                if (dbNode.Tag is DbSettings dbSettings)
                {
                    var database = redisClient.GetDB(dbSettings.DBNumber);
                    var key_type = database.KeyType(select.Text);

                    FormQueryWindow fqw = new FormQueryWindow();
                    fqw.SetServerInfo(redisClient, dbSettings.DBNumber);
                    fqw.SetRedisKeysType(key_type);
                    fqw.SetRedisKeysFilter(key_filter);
                    fqw.Show();
                }
            }
        }

        private bool copy_key_in_same_machine(IDatabase src_db, IDatabase dst_db, string src_key, string dst_key)
		{
            if (src_key == "")
			{
                MessageBox.Show("source key is empty", "Copy failed");
                return false;
			}

            if (dst_key == "")
			{
                MessageBox.Show("destination key is empty", "Copy failed");
                return false;
			}

            if (src_db.KeyExists(src_key) == false)
			{
                MessageBox.Show($"{src_key} is not exist", "Copy failed");
                return false;
			}

            if (dst_db.KeyExists(dst_key))
			{
                MessageBox.Show($"{dst_key} is already exist", "Copy failed");
                return false;
			}

            try
            {
            var snapshot = (RedisResult[])src_db.ScriptEvaluate(
                "local v=redis.call('DUMP',KEYS[1]); if not v then return redis.error_reply('Source key disappeared') end; return {v,redis.call('PTTL',KEYS[1])}",
                new StackExchange.Redis.RedisKey[] { src_key });
            long ttl = (long)snapshot[1];
            dst_db.KeyRestore(dst_key, (byte[])snapshot[0], ttl < 0 ? null : TimeSpan.FromMilliseconds(Math.Max(1, ttl)));

            return true;
            }
            catch (RedisException ex)
            {
                MessageBox.Show(ex.Message, "Copy failed");
                return false;
            }

		}

		private async void copy_key_ToolStripMenuItem_Click(object sender, EventArgs e)
		{
            if (!CanWriteSelected()) return;
			TreeNode select = treeView_server.SelectedNode;
			if (select == null)
			{
				return;
			}

			if (select.Tag is RedisKey)
			{
				using (FormInputString formInput = new FormInputString())
				{
					formInput.TextInfo = "Copy key";
					formInput.InputValue = select.Text;

					if (formInput.ShowDialog() == DialogResult.OK)
					{
						TreeNode dbNode = GetDbNode(select);
						TreeNode redisNode = GetRedisNode(select);
                        var dbSettings = dbNode.Tag as DbSettings;
                        var redisClient = redisNode.Tag as RedisClient;
                        var db = redisClient.GetDB(dbSettings.DBNumber);

                        string src_key = select.Text;
                        string dst_key = formInput.InputValue;

                        if (copy_key_in_same_machine(db, db, src_key, dst_key))
						{
                            await RefreshDbKeysAsync(dbNode, true);
                        }
                    }
				}
			}
		}

		private async void copy_key_to_db_ToolStripMenuItem_Click(object sender, EventArgs e)
		{
            if (!CanWriteSelected()) return;
			TreeNode select = treeView_server.SelectedNode;
			if (select == null)
			{
				return;
			}

			if (select.Tag is RedisKey)
			{
				using (FormTwoInputString formInput = new FormTwoInputString())
				{
					TreeNode dbNode = GetDbNode(select);
					TreeNode redisNode = GetRedisNode(select);
					var dbSettings = dbNode.Tag as DbSettings;
					var redisClient = redisNode.Tag as RedisClient;

					formInput.TextInfo = "Copy key to other db";
                    formInput.InputValue1 = dbSettings.DBNumber.ToString();
					formInput.InputValue2 = select.Text;

					if (formInput.ShowDialog() == DialogResult.OK)
					{
                        int dst_db_num = -1;
                        if (int.TryParse(formInput.InputValue1, out dst_db_num) == false)
						{
                            MessageBox.Show("insert first input as number", "Copy failed");
                            return;
						}

                        if (dst_db_num < 0)
						{
                            MessageBox.Show("db number is must greater than 0", "Copy failed");
                            return;
						}

                        if (dst_db_num >= redisClient.RedisServer.DatabaseCount)
						{
                            MessageBox.Show($"db number {dst_db_num} is not exist", "Copy failed");
                            return;
						}

						var src_db = redisClient.GetDB(dbSettings.DBNumber);
                        var dst_db = redisClient.GetDB(dst_db_num);

						string src_key = select.Text;
						string dst_key = formInput.InputValue2;

						if (copy_key_in_same_machine(src_db, dst_db, src_key, dst_key))
						{
                            var dst_db_node = GetDbNodeFromRedisNode(redisNode, dst_db_num);
                            if (dst_db_node != null)
							{
                                await RefreshDbKeysAsync(dst_db_node, true);
                            }
                        }
					}
				}
			}
		}

		private void copy_key_to_machine_ToolStripMenuItem_Click(object sender, EventArgs e)
		{
            if (!CanWriteSelected()) return;
			TreeNode select = treeView_server.SelectedNode;
			if (select == null)
			{
				return;
			}

			if (select.Tag is RedisKey)
			{
				using (FormMigrateKey formInput = new FormMigrateKey())
				{
					TreeNode dbNode = GetDbNode(select);
					TreeNode redisNode = GetRedisNode(select);
					var dbSettings = dbNode.Tag as DbSettings;
					var redisClient = redisNode.Tag as RedisClient;

					if (formInput.ShowDialog() == DialogResult.OK)
					{
						var db = redisClient.GetDB(dbSettings.DBNumber);
						string src_key = select.Text;

                        IPEndPoint end_point = new IPEndPoint(IPAddress.Parse(formInput.Host), formInput.Port);

                        try
                        {
                            db.KeyMigrate(src_key, end_point, formInput.DBNumber, migrateOptions: MigrateOptions.Copy);

                            MessageBox.Show($"Migrate key complete.\r\n\r\nkey : {src_key}\r\ntarget server : {$"{formInput.Host}:{formInput.Port}"}\r\ndb num : {formInput.DBNumber}");
                        }
                        catch (RedisCommandException ex)
						{
                            MessageBox.Show(ex.Message);
						}
                        catch (RedisTimeoutException ex)
						{
                            MessageBox.Show(ex.Message);
                        }
                    }
				}
			}
		}

		private async void migrate_keys_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!CanWriteSelected()) return;
            var selected = treeView_server.SelectedNode;
            if (selected?.Tag is not DbSettings db) return;
            var client = (RedisClient)GetRedisNode(selected).Tag;
            using var input = new FormMigrateKey { UsePattern = true };
            if (input.ShowDialog(this) != DialogResult.OK) return;
            if (!IPAddress.TryParse(input.Host, out var address)) { MessageBox.Show("Migration requires a target IP address"); return; }
            var target = new IPEndPoint(address, input.Port);
            int targetDb = input.DBNumber;
            await RunBatchAsync(client, new[] { db.DBNumber }, input.KeyPattern, "Copy keys to server", $"Target: {target}, DB {targetDb}", (database, key) =>
            {
                database.KeyMigrate(key, target, targetDb, migrateOptions: MigrateOptions.Copy);
                return true;
            });
        }

	}
}
