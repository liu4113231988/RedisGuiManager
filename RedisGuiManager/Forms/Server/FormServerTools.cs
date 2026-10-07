using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RedisGuiManager
{
    /// <summary>
    /// Server diagnostics and administration: runtime configuration, connected clients, memory
    /// usage, persistence controls, cluster topology, and per-key OBJECT / SENTINEL information.
    ///
    /// The layout is built in code (like FormQueryWindow and FormRedisAdd) so the tab wiring stays
    /// next to the handlers that populate it.
    /// </summary>
    public partial class FormServerTools : Form
    {
        private readonly RedisClient redisClient;
        private TabControl tabs;

        private TextBox configPattern;
        private TextBox configSetName;
        private TextBox configSetValue;
        private DataGridView configGrid;
        private DataGridView clientsGrid;
        private Label clientsCount;
        private long ownClientId = -1;
        private TextBox memoryKey;
        private DataGridView memoryGrid;
        private TextBox memoryDoctorOutput;
        private DataGridView persistenceGrid;
        private DataGridView clusterGrid;
        private TextBox clusterInfo;
        private TextBox diagKey;
        private DataGridView diagGrid;
        private ComboBox sentinelCombo;
        private DataGridView sentinelGrid;

        public FormServerTools(RedisClient client)
        {
            redisClient = client;
            Text = $"Server tools - {client.Settings.name} [{client.Settings.host}:{client.Settings.port}]";
            Icon = Icon.FromHandle(Properties.Resources.console.GetHicon());
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1000, 560);
            MinimumSize = new Size(880, 480);

            BuildLayout();

            if (Config.darkmode > 0)
            {
                Utils.DarkThemeForm(this);
            }
        }

        private void BuildLayout()
        {
            tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 6) };
            tabs.TabPages.Add(BuildConfigTab());
            tabs.TabPages.Add(BuildClientsTab());
            tabs.TabPages.Add(BuildMemoryTab());
            tabs.TabPages.Add(BuildPersistenceTab());
            tabs.TabPages.Add(BuildClusterTab());
            tabs.TabPages.Add(BuildDiagnosticsTab());

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 40 };
            var refreshAll = MakeButton("Refresh all", async (s, e) => await RefreshActiveTabAsync(), 110);
            refreshAll.Location = new Point(12, 7);
            var close = MakeButton("Close", (s, e) => Close(), 90);
            close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            close.Location = new Point(bottom.Width - 102, 7);
            bottom.Controls.Add(refreshAll);
            bottom.Controls.Add(close);
            bottom.Resize += (s, e) => close.Left = bottom.ClientSize.Width - close.Width - 12;

            Controls.Add(tabs);
            Controls.Add(bottom);
        }

        private IServer Server => redisClient.RedisServer;

        /// <summary>
        /// Runs a diagnostics call on the UI thread, reporting failures as a message rather than
        /// letting them escape into the WinForms unhandled-exception path. The Redis calls
        /// themselves are pushed to the thread pool; only the grid updates run inline.
        /// </summary>
        private async Task RunAsync(string title, Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, title + " failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            tabs.SelectedIndex = 0;
            _ = RefreshActiveTabAsync();
        }

        private async Task RefreshActiveTabAsync()
        {
            switch (tabs.SelectedIndex)
            {
                case 0: await LoadConfigAsync(); break;
                case 1: await LoadClientsAsync(); break;
                case 2: await LoadMemoryStatsAsync(); break;
                case 3: await LoadPersistenceAsync(); break;
                case 4: await LoadClusterAsync(); break;
                case 5: await LoadObjectInfoAsync(); break;
            }
        }

        private async Task LoadConfigAsync()
        {
            string pattern = configPattern.Text;
            await RunAsync("Configuration", async () =>
            {
                var rows = await Task.Run(() => RedisOps.ConfigGet(Server, pattern));
                ReplaceGrid(configGrid, rows.Select(row => (object)row.Key + "\t" + row.Value));
            });
        }

        private async Task ApplyConfigSetAsync(string name, string value)
        {
            if (!redisClient.CanWrite()) return;
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(this, "Setting name is required.", "CONFIG SET", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (MessageBox.Show(this,
                    $"Apply this change to the running server?\r\n\r\n{name} = {value}\r\n\r\nThere is no undo, and CONFIG REWRITE is not issued.",
                    "Confirm CONFIG SET", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            await RunAsync("CONFIG SET", async () =>
            {
                await Task.Run(() => RedisOps.ConfigSet(Server, name, value));
                await LoadConfigAsync();
            });
        }

        private async Task LoadClientsAsync()
        {
            await RunAsync("Clients", async () =>
            {
                var clients = await Task.Run(() => RedisOps.ClientList(Server));
                ownClientId = RedisOps.OwnClientId(Server) ?? -1;

                ReplaceGrid(clientsGrid, clients
                    .OrderByDescending(client => client.Id == ownClientId)
                    .ThenByDescending(client => client.Id)
                    .Select(client => (object)string.Join("\t",
                        client.Id,
                        client.Address?.ToString() ?? "",
                        client.Name ?? "",
                        client.Database,
                        client.LastCommand ?? "",
                        FormatAge(client.IdleSeconds),
                        FormatAge(client.AgeSeconds),
                        client.SubscriptionCount,
                        client.FlagsRaw ?? "",
                        DescribeLibrary(client))));

                clientsCount.Text = clients.Length == 1
                    ? "1 connected client" + (clients[0].Id == ownClientId ? " (this application)" : "")
                    : $"{clients.Length} connected clients";
            });
        }

        private async Task KillSelectedClientsAsync()
        {
            if (!redisClient.CanWrite()) return;
            if (clientsGrid.SelectedRows.Count == 0)
            {
                MessageBox.Show(this, "Select at least one client.", "CLIENT KILL", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var ids = new List<long>();
            foreach (DataGridViewRow row in clientsGrid.SelectedRows)
            {
                if (long.TryParse(row.Cells[0].Value?.ToString(), out long id)) ids.Add(id);
            }

            if (ids.Count == 0) return;

            if (ids.Contains(ownClientId))
            {
                if (MessageBox.Show(this,
                        "The selection includes this application's own connection. Killing it will drop the GUI's link.\r\n\r\nContinue?",
                        "Confirm CLIENT KILL", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                {
                    return;
                }
            }
            else if (MessageBox.Show(this,
                    $"Disconnect {ids.Count} client(s)?\r\n\r\n{string.Join(", ", ids)}",
                    "Confirm CLIENT KILL", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            await RunAsync("CLIENT KILL", async () =>
            {
                foreach (long id in ids)
                {
                    await Task.Run(() => RedisOps.ClientKill(Server, id));
                }

                await LoadClientsAsync();
            });
        }

        private async Task LoadMemoryUsageAsync()
        {
            string key = memoryKey.Text.Trim();
            if (key.Length == 0)
            {
                MessageBox.Show(this, "Enter a key first.", "MEMORY USAGE", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            await RunAsync("MEMORY USAGE", async () =>
            {
                var usage = await Task.Run(() => RedisOps.MemoryUsage(redisClient.GetDB(0), (StackExchange.Redis.RedisKey)key));
                ReplaceGrid(memoryGrid, new[]
                {
                    (object)("MEMORY USAGE " + key),
                    usage.HasValue ? RedisOps.FormatBytes(usage.Value) : "key does not exist"
                });
            });
        }

        private async Task LoadMemoryStatsAsync()
        {
            await RunAsync("MEMORY STATS", async () =>
            {
                var stats = await Task.Run(() => RedisOps.MemoryStats(Server));
                ReplaceGrid(memoryGrid, stats
                    .OrderByDescending(pair => IsHeadlineMetric(pair.Key))
                    .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pair => (object)pair.Key + "\t" + pair.Value));
            });
        }

        private async Task LoadMemoryDoctorAsync()
        {
            await RunAsync("MEMORY DOCTOR", async () =>
            {
                string report = await Task.Run(() => RedisOps.MemoryDoctor(Server));
                memoryDoctorOutput.Text = report ?? "";
            });
        }

        private async Task LoadPersistenceAsync()
        {
            await RunAsync("Persistence", async () =>
            {
                var rows = new List<string>();

                DateTime lastSave = await Task.Run(() => RedisOps.LastSave(Server));
                rows.Add("LASTSAVE\t" + (lastSave == DateTime.MinValue
                    ? "never"
                    : lastSave.ToString("yyyy-MM-dd HH:mm:ss") + $" ({(DateTime.Now - lastSave).TotalSeconds:F0}s ago)"));

                var info = await Task.Run(() => Server.Info("persistence"));

                // INFO returns groups keyed by section, each holding the entries for that section.
                foreach (var section in info)
                {
                    foreach (KeyValuePair<string, string> pair in section)
                    {
                        string key = pair.Key;
                        if (key.StartsWith("rdb_", StringComparison.OrdinalIgnoreCase)
                            || key.StartsWith("aof_", StringComparison.OrdinalIgnoreCase)
                            || key.StartsWith("loading:", StringComparison.OrdinalIgnoreCase))
                        {
                            rows.Add(key + "\t" + pair.Value);
                        }
                    }
                }

                ReplaceGrid(persistenceGrid, rows);
            });
        }

        private async Task RunSaveAsync(bool background)
        {
            if (!redisClient.CanWrite()) return;

            string verb = background ? "BGSAVE" : "SAVE";
            string warning = background
                ? "BGSAVE forks the server to write an RDB snapshot. On a large dataset this can briefly double memory usage."
                : "SAVE blocks the server until the snapshot is written. On a large dataset the server will be unresponsive.";

            if (MessageBox.Show(this, verb + "\r\n\r\n" + warning + "\r\n\r\nContinue?", "Confirm " + verb,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }

            await RunAsync(verb, async () =>
            {
                string reply = await Task.Run(() => RedisOps.Save(redisClient.GetDB(0), background));
                MessageBox.Show(this, verb + " returned:\r\n\r\n" + reply, verb, MessageBoxButtons.OK, MessageBoxIcon.Information);
                await LoadPersistenceAsync();
            });
        }

        private async Task RunRewriteAofAsync()
        {
            if (!redisClient.CanWrite()) return;

            if (MessageBox.Show(this,
                    "BGREWRITEAOF rewrites the append-only file from the current dataset.\r\n\r\nThis is an I/O-heavy operation. Continue?",
                    "Confirm BGREWRITEAOF", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }

            await RunAsync("BGREWRITEAOF", async () =>
            {
                string reply = await Task.Run(() => RedisOps.RewriteAppendOnlyFile(redisClient.GetDB(0), disableAppendFsync: false));
                MessageBox.Show(this, "BGREWRITEAOF returned:\r\n\r\n" + reply, "BGREWRITEAOF",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                await LoadPersistenceAsync();
            });
        }

        private async Task LoadClusterAsync()
        {
            await RunAsync("Cluster", async () =>
            {
                if (redisClient.Settings.use_cluster == false)
                {
                    clusterInfo.Text =
                        "This connection is not in cluster mode.\r\n\r\n" +
                        "Enable \"Use cluster\" on the connection to browse topology here.";
                    ReplaceGrid(clusterGrid, Array.Empty<string>());
                    return;
                }

                var configuration = await Task.Run(() => RedisOps.ClusterConfiguration(Server));
                var rows = RedisOps.ClusterNodeRows(configuration);

                ReplaceGrid(clusterGrid, rows.Select(node => (object)string.Join("\t",
                    node.ShortNodeId,
                    node.Endpoint,
                    node.Role,
                    node.LinkState,
                    node.SlotCount,
                    node.FailFlag)));

                clusterInfo.Text = rows.Count == 0
                    ? "CLUSTER NODES returned no nodes."
                    : $"{rows.Count} node(s): " +
                      $"{rows.Count(node => node.Role == "master")} master(s), " +
                      $"{rows.Count(node => node.Role == "replica")} replica(s), " +
                      $"{rows.Count(node => node.LinkState != "connected")} not connected.";
            });
        }

        private async Task LoadObjectInfoAsync()
        {
            string key = diagKey.Text.Trim();
            if (key.Length == 0)
            {
                MessageBox.Show(this, "Enter a key first.", "OBJECT", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            await RunAsync("OBJECT", async () =>
            {
                var database = redisClient.GetDB(0);
                var rows = new List<string>();

                long? usage = await Task.Run(() => RedisOps.MemoryUsage(database, (StackExchange.Redis.RedisKey)key));
                rows.Add("MEMORY USAGE\t" + (usage.HasValue ? RedisOps.FormatBytes(usage.Value) : "-"));

                var info = await Task.Run(() => RedisOps.ObjectInfo(database, (StackExchange.Redis.RedisKey)key));
                if (info.Count == 0)
                {
                    rows.Add("OBJECT\tkey does not exist, or OBJECT is unavailable on this server");
                }
                else
                {
                    rows.AddRange(info.Select(pair => pair.Key + "\t" + pair.Value));
                }

                ReplaceGrid(diagGrid, rows);
            });
        }

        private async Task LoadSentinelMastersAsync()
        {
            await RunAsync("Sentinel", async () =>
            {
                string[] masters;
                try
                {
                    masters = await Task.Run(() => RedisOps.SentinelMasters(Server));
                }
                catch (RedisServerException ex)
                {
                    // Pointing an ordinary connection at SENTINEL is the normal failure case.
                    sentinelCombo.Items.Clear();
                    ReplaceGrid(sentinelGrid, new[] { (object)("SENTINEL MASTERS\t" + ex.Message) });
                    return;
                }

                sentinelCombo.Items.Clear();
                foreach (string master in masters)
                {
                    var pairs = RedisOps.ToPairs(RedisResult.Create((RedisValue)master));
                    string name = pairs
                        .FirstOrDefault(pair => pair.Key.Equals("name", StringComparison.OrdinalIgnoreCase))
                        .Value;

                    if (name != null) sentinelCombo.Items.Add(name);
                }

                if (sentinelCombo.Items.Count > 0)
                {
                    sentinelCombo.SelectedIndex = 0;
                }
                else
                {
                    ReplaceGrid(sentinelGrid, new[] { (object)"SENTINEL MASTERS\treturned no masters" });
                }
            });
        }

        private async Task LoadSentinelMasterAsync()
        {
            if (sentinelCombo.SelectedItem == null) return;
            string master = sentinelCombo.SelectedItem.ToString();

            await RunAsync("Sentinel", async () =>
            {
                var pairs = await Task.Run(() => RedisOps.SentinelMaster(Server, master));
                ReplaceGrid(sentinelGrid, pairs.Select(pair => (object)pair.Key + "\t" + pair.Value));
            });
        }

        private static Label MakeLabel(string text, int width) =>
            new Label { Text = text, Width = width, Height = 20 };

        private static Button MakeButton(string text, EventHandler handler, int width)
        {
            var button = new Button { Text = text, Width = width, Height = 26, UseVisualStyleBackColor = true };
            button.Click += handler;
            return button;
        }

        private static DataGridView MakeGrid(params (string Header, int Width)[] columns)
        {
            var grid = new DataGridView
            {
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                BackgroundColor = Color.White,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };

            foreach (var column in columns)
            {
                grid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    HeaderText = column.Header,
                    Width = column.Width,
                    ReadOnly = true
                });
            }

            return grid;
        }

        /// <summary>
        /// Fills a grid from "a\tb" rows. Accepts any row sequence so callers can pass projected
        /// strings or object arrays without converting first.
        /// </summary>
        private static void ReplaceGrid(DataGridView grid, System.Collections.IEnumerable rows)
        {
            grid.Rows.Clear();
            foreach (object row in rows)
            {
                string[] cells = (row as string ?? row?.ToString() ?? "").Split('\t');
                int index = grid.Rows.Add(cells);
                for (int i = 0; i < cells.Length && i < grid.Columns.Count; i++)
                {
                    grid.Rows[index].Cells[i].Value = cells[i];
                }
            }
        }

        private static bool IsHeadlineMetric(string name)
        {
            return name.Equals("used_memory", StringComparison.OrdinalIgnoreCase)
                || name.Equals("used_memory_human", StringComparison.OrdinalIgnoreCase)
                || name.Equals("used_memory_rss", StringComparison.OrdinalIgnoreCase)
                || name.Equals("used_memory_peak", StringComparison.OrdinalIgnoreCase)
                || name.Equals("allocator_allocated", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatAge(int seconds) => seconds < 60 ? $"{seconds}s" : $"{seconds / 60}m {seconds % 60}s";

        private static string DescribeLibrary(ClientInfo client)
        {
            if (string.IsNullOrWhiteSpace(client.LibraryName)) return "";
            return string.IsNullOrWhiteSpace(client.LibraryVersion)
                ? client.LibraryName
                : $"{client.LibraryName} {client.LibraryVersion}";
        }
    }
}