using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using RedisGuiManager.Properties;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StackExchange.Redis;

namespace RedisGuiManager
{
    public partial class FormMain
    {
        // Connection storage: files, groups and settings persistence.

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
                MessageBox.Show(UiText.LoadConnectionsFailed + ex.Message);
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
                        MessageBox.Show(string.Format(UiText.SaveFailedFor, kv.Key, ex.Message));
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(UiText.SaveFailedDetail + ex.Message);
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
                    MessageBox.Show(string.Format(UiText.DuplicateConnectionName, newName),
                        UiText.DuplicateNameTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

    }
}
