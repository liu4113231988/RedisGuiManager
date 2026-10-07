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
    /// Stream internals and consumer-group operations: XINFO STREAM / GROUPS / CONSUMERS plus the
    /// consumer-side commands XPENDING, XACK, XCLAIM, XAUTOCLAIM and XTRIM. Opened from the stream
    /// value editor.
    /// </summary>
    public partial class FormStreamInfo : Form
    {
        private readonly RedisClient redisClient;
        private readonly string keyName;
        private readonly IDatabase database;

        private DataGridView streamGrid;
        private DataGridView groupsGrid;
        private DataGridView consumersGrid;
        private DataGridView pendingGrid;
        private Label groupsHint;
        private Label pendingHint;
        private TextBox groupName;
        private TextBox consumerName;
        private TextBox minIdleMs;

        public FormStreamInfo(RedisClient client, string key, IDatabase database)
        {
            redisClient = client;
            keyName = key;
            this.database = database ?? client.GetDB(0);

            Text = $"Stream - {key}";
            Icon = Icon.FromHandle(Properties.Resources.console.GetHicon());
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(980, 660);
            MinimumSize = new Size(820, 560);

            BuildLayout();

            if (Config.darkmode > 0)
            {
                Utils.DarkThemeForm(this);
            }
        }

        private void BuildLayout()
        {
            var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 6) };

            var streamPage = new TabPage("Stream");
            streamGrid = MakeGrid(("Property", 340), ("Value", 560));
            streamGrid.Dock = DockStyle.Fill;
            streamPage.Controls.Add(streamGrid);
            tabs.TabPages.Add(streamPage);

            var groupsPage = new TabPage("Consumer groups");
            groupsHint = MakeLabel("Select a group to list its consumers.", 480);
            groupsHint.Dock = DockStyle.Top;
            groupsHint.Height = 24;
            groupsGrid = MakeGrid(
                ("Name", 170), ("Consumers", 90), ("Pending", 80), ("Last delivered", 160),
                ("Entries read", 100), ("Lag", 90));
            groupsGrid.Dock = DockStyle.Fill;
            groupsGrid.SelectionChanged += async (s, e) => await LoadConsumersAsync();
            groupsPage.Controls.Add(groupsGrid);
            groupsPage.Controls.Add(groupsHint);
            tabs.TabPages.Add(groupsPage);

            var consumersPage = new TabPage("Consumers");
            consumersGrid = MakeGrid(("Name", 280), ("Pending", 90), ("Idle", 120));
            consumersGrid.Dock = DockStyle.Fill;
            consumersPage.Controls.Add(consumersGrid);
            tabs.TabPages.Add(consumersPage);

            var pendingPage = new TabPage("Pending entries");
            pendingHint = MakeLabel("Pending entries are messages delivered to a consumer but not yet acknowledged.", 700);
            pendingHint.Dock = DockStyle.Top;
            pendingHint.Height = 24;
            pendingGrid = MakeGrid(
                ("Message id", 150), ("Consumer", 200), ("Deliveries", 90), ("Idle", 110));
            pendingGrid.Dock = DockStyle.Fill;
            pendingPage.Controls.Add(pendingGrid);
            pendingPage.Controls.Add(pendingHint);
            tabs.TabPages.Add(pendingPage);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 42 };
            var refresh = MakeButton("Refresh", async (s, e) => await LoadAllAsync(), 100);
            refresh.Location = new Point(12, 8);

            var groupLabel = MakeLabel("Group:", 45);
            groupLabel.Location = new Point(124, 12);
            groupName = new TextBox { Location = new Point(168, 8), Width = 130 };
            var claimConsumerLabel = MakeLabel("Consumer:", 62);
            claimConsumerLabel.Location = new System.Drawing.Point(306, 12);
            consumerName = new TextBox { Location = new System.Drawing.Point(368, 8), Width = 110 };
            var idleLabel = MakeLabel("Min idle (ms):", 78);
            idleLabel.Location = new System.Drawing.Point(486, 12);
            minIdleMs = new TextBox { Location = new System.Drawing.Point(564, 8), Width = 70, Text = "60000" };

            var claim = MakeButton("XCLAIM selected", async (s, e) => await ClaimPendingAsync(), 130);
            claim.Location = new System.Drawing.Point(642, 7);
            var ack = MakeButton("XACK selected", async (s, e) => await AckPendingAsync(), 130);
            ack.Location = new System.Drawing.Point(780, 7);
            var trim = MakeButton("XTRIM…", async (s, e) => await TrimAsync(), 95);
            trim.Location = new System.Drawing.Point(918, 7);

            bottom.Controls.Add(refresh);
            bottom.Controls.Add(groupLabel);
            bottom.Controls.Add(groupName);
            bottom.Controls.Add(claimConsumerLabel);
            bottom.Controls.Add(consumerName);
            bottom.Controls.Add(idleLabel);
            bottom.Controls.Add(minIdleMs);
            bottom.Controls.Add(claim);
            bottom.Controls.Add(ack);
            bottom.Controls.Add(trim);

            Controls.Add(tabs);
            Controls.Add(bottom);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _ = LoadAllAsync();
        }

        private async Task LoadAllAsync()
        {
            await LoadStreamInfoAsync();
            await LoadGroupsAsync();

            // Selecting the first group also drives the pending list, but only once the user has
            // picked a group explicitly (or the stream genuinely has one).
            string group = SelectedGroup();
            if (group != null)
            {
                groupName.Text = group;
                await LoadPendingAsync();
            }
        }

        private async Task LoadStreamInfoAsync()
        {
            await RunAsync("XINFO STREAM", async () =>
            {
                StreamInfo info;
                try
                {
                    info = await Task.Run(() => database.StreamInfo(keyName));
                }
                catch (RedisServerException ex)
                {
                    ReplaceGrid(streamGrid, new[] { (object)("XINFO STREAM\t" + ex.Message) });
                    return;
                }

                var rows = new List<string>
                {
                    "Length\t" + info.Length,
                    "Radix tree keys\t" + info.RadixTreeKeys,
                    "Radix tree nodes\t" + info.RadixTreeNodes,
                    "Consumer groups\t" + info.ConsumerGroupCount,
                    "Entries added\t" + info.EntriesAdded,
                    "Last generated ID\t" + info.LastGeneratedId,
                    "Max deleted entry ID\t" + info.MaxDeletedEntryId,
                    "First entry\t" + DescribeEntry(info.FirstEntry),
                    "Last entry\t" + DescribeEntry(info.LastEntry)
                };

                ReplaceGrid(streamGrid, rows);
            });
        }

        private async Task LoadGroupsAsync()
        {
            await RunAsync("XINFO GROUPS", async () =>
            {
                StreamGroupInfo[] groups;
                try
                {
                    groups = await Task.Run(() => database.StreamGroupInfo(keyName));
                }
                catch (RedisServerException ex)
                {
                    // Having no consumer groups is the normal case for a plain XADD stream.
                    ReplaceGrid(groupsGrid, new[] { (object)("XINFO GROUPS\t" + ex.Message) });
                    groupsHint.Text = "This stream has no consumer groups.";
                    return;
                }

                ReplaceGrid(groupsGrid, groups.Select(group => (object)string.Join("\t",
                    group.Name,
                    group.ConsumerCount,
                    group.PendingMessageCount,
                    group.LastDeliveredId,
                    group.EntriesRead.HasValue ? group.EntriesRead.Value.ToString() : "-",
                    group.Lag.HasValue ? group.Lag.Value.ToString() : "-")));

                groupsHint.Text = groups.Length == 0
                    ? "This stream has no consumer groups."
                    : $"{groups.Length} consumer group(s). Select one to list its consumers.";

                if (groupsGrid.Rows.Count > 0)
                {
                    groupsGrid.Rows[0].Selected = true;
                }
            });
        }

        private async Task LoadConsumersAsync()
        {
            string group = SelectedGroup();
            if (group == null) return;

            await RunAsync("XINFO CONSUMERS", async () =>
            {
                try
                {
                    var consumers = await Task.Run(() => database.StreamConsumerInfo(keyName, group));
                    ReplaceGrid(consumersGrid, consumers.Select(consumer => (object)string.Join("\t",
                        consumer.Name,
                        consumer.PendingMessageCount,
                        consumer.IdleTimeInMilliseconds + " ms")));
                }
                catch (RedisServerException ex)
                {
                    ReplaceGrid(consumersGrid, new[] { (object)("XINFO CONSUMERS\t" + ex.Message) });
                }
            });
        }

        private async Task LoadPendingAsync()
        {
            string group = SelectedGroup();
            if (group == null)
            {
                MessageBox.Show(this, "Select a consumer group first.", "XPENDING",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            await RunAsync("XPENDING", async () =>
            {
                try
                {
                    var pending = await Task.Run(() => database.StreamPending(keyName, group));
                    // No overload has optional parameters: consumerName is a non-nullable RedisValue, the id bounds are nullable.
                    var messages = await Task.Run(() => database.StreamPendingMessages(
                        keyName, group, 100, RedisValue.Null, null, null));

                    ReplaceGrid(pendingGrid, messages.Select(message => (object)string.Join("\t",
                        message.MessageId.ToString(),
                        message.ConsumerName.IsNull ? "(none)" : message.ConsumerName.ToString(),
                        message.DeliveryCount,
                        (message.IdleTimeInMilliseconds) + " ms")));

                    pendingHint.Text =
                        $"{pending.PendingMessageCount} pending message(s); " +
                        $"oldest {pending.LowestPendingMessageId}, newest {pending.HighestPendingMessageId}.";
                }
                catch (RedisServerException ex)
                {
                    ReplaceGrid(pendingGrid, new[] { (object)("XPENDING\t" + ex.Message) });
                    pendingHint.Text = "Could not read the pending list.";
                }
            });
        }

        /// <summary>The group name typed in the box, or the selected row, whichever is available.</summary>
        private string SelectedGroup()
        {
            if (!string.IsNullOrWhiteSpace(groupName.Text)) return groupName.Text.Trim();
            if (groupsGrid.SelectedRows.Count > 0)
            {
                string selected = groupsGrid.SelectedRows[0].Cells[0].Value?.ToString();
                if (!string.IsNullOrEmpty(selected)) return selected;
            }

            return null;
        }

        private async Task AckPendingAsync()
        {
            if (!redisClient.CanWrite()) return;

            string group = SelectedGroup();
            if (group == null)
            {
                MessageBox.Show(this, "Select a consumer group first.", "XACK", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var ids = SelectedPendingIds();
            if (ids.Count == 0)
            {
                MessageBox.Show(this, "Select one or more pending entries first.", "XACK",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show(this,
                    $"Acknowledge {ids.Count} message(s) in group [{group}]?\r\n\r\nAcknowledging removes them from the pending list; the messages themselves stay in the stream.",
                    "Confirm XACK", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            await RunAsync("XACK", async () =>
            {
                long acknowledged = await Task.Run(() => database.StreamAcknowledge(keyName, group, ids.ToArray()));
                MessageBox.Show(this, $"Acknowledged {acknowledged} message(s).", "XACK",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                await LoadPendingAsync();
                await LoadGroupsAsync();
            });
        }

        private async Task ClaimPendingAsync()
        {
            if (!redisClient.CanWrite()) return;

            string group = SelectedGroup();
            if (group == null)
            {
                MessageBox.Show(this, "Select a consumer group first.", "XCLAIM", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(consumerName.Text))
            {
                MessageBox.Show(this, "Enter the consumer that should take over the messages.", "XCLAIM",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var ids = SelectedPendingIds();
            if (ids.Count == 0)
            {
                MessageBox.Show(this, "Select one or more pending entries first.", "XCLAIM",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!long.TryParse(minIdleMs.Text, out long minIdle) || minIdle < 0)
            {
                MessageBox.Show(this, "Minimum idle time must be a non-negative number of milliseconds.", "XCLAIM",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string consumer = consumerName.Text.Trim();
            if (MessageBox.Show(this,
                    $"Hand {ids.Count} message(s) to [{consumer}] in group [{group}]?\r\n\r\nOnly messages idle for at least {minIdle} ms will be claimed; anything younger stays put.",
                    "Confirm XCLAIM", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            await RunAsync("XCLAIM", async () =>
            {
                var claimed = await Task.Run(() => database.StreamClaim(keyName, group, consumer,
                    minIdleTimeInMs: minIdle, messageIds: ids.ToArray()));
                MessageBox.Show(this, $"Claimed {claimed.Length} message(s) for [{consumer}].", "XCLAIM",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                await LoadPendingAsync();
                await LoadGroupsAsync();
            });
        }

        private async Task TrimAsync()
        {
            if (!redisClient.CanWrite()) return;

            string input = Microsoft.VisualBasic.Interaction.InputBox(
                "Maximum stream length (trim oldest entries above it):", "XTRIM", "1000");
            if (!long.TryParse(input, out long maxLength) || maxLength < 0)
            {
                return;
            }

            await RunAsync("XTRIM", async () =>
            {
                long removed = await Task.Run(() => database.StreamTrim(keyName, maxLength, useApproximateMaxLength: false));
                MessageBox.Show(this, $"Removed {removed} entr(y/ies).", "XTRIM",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                await LoadStreamInfoAsync();
            });
        }

        private List<RedisValue> SelectedPendingIds()
        {
            var ids = new List<RedisValue>();
            foreach (DataGridViewRow row in pendingGrid.SelectedRows)
            {
                string text = row.Cells[0].Value?.ToString();
                if (!string.IsNullOrEmpty(text)) ids.Add(text);
            }

            return ids;
        }

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

        private static string DescribeEntry(StreamEntry entry)
        {
            // StreamEntry is a struct, so an empty entry is detected via IsNull rather than null.
            if (entry.IsNull) return "-";
            return entry.Id + " (" + entry.Values.Length + " field(s))";
        }

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
                MultiSelect = true,
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
    }
}
