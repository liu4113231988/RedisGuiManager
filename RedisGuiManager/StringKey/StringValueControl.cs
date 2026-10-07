using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using RedisGuiManager.Properties;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace RedisGuiManager
{
    public partial class StringValueControl : UserControl
    {
        private string stringKeyName = string.Empty;
        private RedisClient redisClient = null;
        private StackExchange.Redis.IDatabase database;

        // String-keyed structures (bitmaps, HyperLogLog, geo sets) have no distinct TYPE in Redis,
        // so they open here as plain strings. These are the commands that interpret them.
        private TextBox rangeStart;
        private TextBox rangeEnd;
        private TextBox bitValue;
        private TextBox otherKey;
        private CheckBox indexByBit;
        private ListBox toolResults;

        [Browsable(false)]
        public FormMain MainForm
        {
            set
            {
                keyOperateControl.MainForm = value;
            }
        }

        [Browsable(false)]
        public TreeNode TargetNode
        {
            set
            {
                keyOperateControl.TargetNode = value;
            }
        }

        public StringValueControl()
        {
            InitializeComponent();
            BuildStringTools();

            if (Config.darkmode > 0)
            {
                Utils.DarkThemeControl(this);
            }
        }

        /// <summary>
        /// Adds a panel for the commands that interpret string-encoded structures: bitmap
        /// (BITCOUNT / BITPOS / GETRANGE), HyperLogLog (PFCOUNT / PFMERGE) and LCS.
        /// </summary>
        private void BuildStringTools()
        {
            var group = new GroupBox
            {
                Text = "String tools (bitmap / HyperLogLog / LCS)",
                Dock = DockStyle.Bottom,
                Height = 176,
                Width = 700,
                Padding = new Padding(8)
            };

            var startLabel = new Label { Text = "Start:", Location = new Point(12, 26), Width = 40 };
            rangeStart = new TextBox { Location = new Point(52, 22), Width = 60, Text = "0" };
            var endLabel = new Label { Text = "End:", Location = new Point(120, 26), Width = 32 };
            rangeEnd = new TextBox { Location = new Point(152, 22), Width = 60, Text = "-1" };
            indexByBit = new CheckBox { Text = "Index by bit", Location = new Point(220, 24), AutoSize = true };
            var bitLabel = new Label { Text = "Bit:", Location = new Point(310, 26), Width = 26 };
            bitValue = new TextBox { Location = new Point(336, 22), Width = 40, Text = "1" };

            var otherLabel = new Label { Text = "Other key:", Location = new Point(388, 26), Width = 62 };
            otherKey = new TextBox { Location = new Point(450, 22), Width = 130 };

            group.Controls.Add(startLabel);
            group.Controls.Add(rangeStart);
            group.Controls.Add(endLabel);
            group.Controls.Add(rangeEnd);
            group.Controls.Add(indexByBit);
            group.Controls.Add(bitLabel);
            group.Controls.Add(bitValue);
            group.Controls.Add(otherLabel);
            group.Controls.Add(otherKey);

            int x = 12;
            x = AddToolButton(group, "BITCOUNT", x, 100, async (s, e) => await RunBitCountAsync());
            x = AddToolButton(group, "BITPOS", x, 90, async (s, e) => await RunBitPositionAsync());
            x = AddToolButton(group, "GETRANGE", x, 100, async (s, e) => await RunGetRangeAsync());
            x = AddToolButton(group, "PFCOUNT", x, 95, async (s, e) => await RunPfCountAsync());
            x = AddToolButton(group, "PFMERGE", x, 100, async (s, e) => await RunPfMergeAsync());
            x = AddToolButton(group, "LCS", x, 80, async (s, e) => await RunLcsAsync());

            AddToolButton(group, "Clear", x, 70, (s, e) => toolResults.Items.Clear());

            toolResults = new ListBox
            {
                Location = new Point(12, 58),
                Width = group.Width - 24,
                Height = 62,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                IntegralHeight = false,
                HorizontalScrollbar = true
            };

            group.Controls.Add(toolResults);
            Controls.Add(group);
        }

        private int AddToolButton(Control parent, string text, int x, int width, EventHandler handler)
        {
            var button = new Button
            {
                Text = text,
                Location = new Point(x, 130),
                Size = new Size(width, 26),
                UseVisualStyleBackColor = true
            };
            button.Click += handler;
            parent.Controls.Add(button);
            return x + width + 4;
        }

        private void StringValueControl_Load(object sender, EventArgs e)
        {
            keyOperateControl.LoadValue.Click += LoadValue_Click;
        }

        public async Task SetNewKey(RedisClient client, string key)
        {
            redisClient = client;
            database = client.Redis;
            stringKeyName = key;

            keyOperateControl.SetRedisClient(redisClient, key);
            await RefreshKeyAsync();
        }

        private async void LoadValue_Click(object sender, EventArgs e)
        {
            await RefreshKeyAsync();
        }

        private bool TryGetRange(out long start, out long end)
        {
            start = 0;
            end = -1;
            return long.TryParse(rangeStart.Text, out start) && long.TryParse(rangeEnd.Text, out end);
        }

        private void Report(string line)
        {
            toolResults.Items.Insert(0, $"{DateTime.Now:HH:mm:ss}  {line}");
        }

        private async Task RunToolAsync(string name, Func<Task<string>> action)
        {
            if (database == null)
            {
                MessageBox.Show(this, UiText.RedisConnectionError, name, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                Report($"{name}: {await action()}");
            }
            catch (Exception ex)
            {
                // WRONGTYPE here means the key is not the structure the command expects; say so
                // rather than showing the bare server error.
                Report($"{name}: failed - {ex.Message}");
            }
        }

        private async Task RunBitCountAsync() => await RunToolAsync("BITCOUNT", async () =>
        {
            if (TryGetRange(out long start, out long end) == false)
            {
                return "start and end must be integers";
            }

            var indexType = indexByBit.Checked ? StringIndexType.Bit : StringIndexType.Byte;
            long count = await database.StringBitCountAsync(stringKeyName, start, end, indexType);
            return $"{count} bit(s) set in [{start}..{end}] ({(indexByBit.Checked ? "bit" : "byte")} indexed)";
        });

        private async Task RunBitPositionAsync() => await RunToolAsync("BITPOS", async () =>
        {
            if (TryGetRange(out long start, out long end) == false)
            {
                return "start and end must be integers";
            }

            if (int.TryParse(bitValue.Text, out int bit) == false || bit < 0 || bit > 1)
            {
                return "bit must be 0 or 1";
            }

            var indexType = indexByBit.Checked ? StringIndexType.Bit : StringIndexType.Byte;
            long position = await database.StringBitPositionAsync(stringKeyName, bit == 1, start, end, indexType);
            return position < 0 ? $"no {bit} in [{start}..{end}]" : $"first {bit} at offset {position}";
        });

        private async Task RunGetRangeAsync() => await RunToolAsync("GETRANGE", async () =>
        {
            if (TryGetRange(out long start, out long end) == false)
            {
                return "start and end must be integers";
            }

            var slice = await database.StringGetRangeAsync(stringKeyName, start, end);
            string text = slice.ToString();
            if (text.Length > 400) text = text.Substring(0, 400) + $"... [{text.Length - 400} more chars]";
            return $"{slice.Length} byte(s): {text}";
        });

        private async Task RunPfCountAsync() => await RunToolAsync("PFCOUNT", async () =>
        {
            long cardinality = await database.HyperLogLogLengthAsync(stringKeyName);
            return $"{cardinality:N0} distinct element(s) estimated";
        });

        private async Task RunPfMergeAsync()
        {
            string other = otherKey.Text.Trim();
            if (other.Length == 0)
            {
                Report("PFMERGE: enter the other key in \"Other key\" first");
                return;
            }

            if (!redisClient.CanWrite()) return;

            if (MessageBox.Show(this,
                    $"Merge [{otherKey.Text.Trim()}] into [{stringKeyName}]?\r\n\r\nThis changes both keys and cannot be undone from here.",
                    "Confirm PFMERGE", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            await RunToolAsync("PFMERGE", async () =>
            {
                await database.HyperLogLogMergeAsync(stringKeyName, stringKeyName, other);
                long cardinality = await database.HyperLogLogLengthAsync(stringKeyName);
                return $"merged into {stringKeyName}; now {cardinality:N0} distinct element(s)";
            });

            await RefreshKeyAsync();
        }

        private async Task RunLcsAsync() => await RunToolAsync("LCS", async () =>
        {
            string other = otherKey.Text.Trim();
            if (other.Length == 0) return "enter the other key in \"Other key\" first";

            // LCS has no binding in StackExchange.Redis; it needs Redis 7.0+.
            var result = (RedisResult[])await database.ExecuteAsync(
                "LCS", stringKeyName, other, "LEN");

            return result.Length == 0 ? "no common subsequence" : $"{result[0]} character(s) in common";
        });

        private async Task RefreshKeyAsync()
        {
            if (!valueControl.ConfirmDiscard()) return;
            if (redisClient == null)
            {
                MessageBox.Show(UiText.RedisConnectionError);
                return;
            }

            if (database == null)
            {
                MessageBox.Show(UiText.RedisConnectionError);
                return;
            }

            var key = stringKeyName;
            var read = await database.StringGetAsync(key);

            // The user may have switched keys while the request was in flight.
            if (key != stringKeyName) return;

            valueControl.SetValue(read);
        }

        private async void button_save_Click(object sender, EventArgs e)
        {
            if (redisClient == null || !redisClient.CanWrite()) return;
            try
            {
                if (valueControl.OriginalValue.IsNull) { MessageBox.Show(UiText.KeyIsMissing); return; }
                if (!valueControl.CanEditText || ValueControl.GetDisplayType() == ValueControl.DisplayType.Hex)
                {
                    MessageBox.Show(UiText.BinaryHexReadOnlyHint);
                    return;
                }
                string save_text = valueControl.EditedValue();
                if (ValueControl.GetDisplayType() == ValueControl.DisplayType.Json)
                {
                    try
                    {
                        save_text = JsonConvert.SerializeObject(JsonConvert.DeserializeObject(save_text), Formatting.None);
                    }
                    catch (JsonReaderException)
                    {
                    }
                }

                await database.ScriptEvaluateAsync(
                    "if redis.call('GET',KEYS[1]) ~= ARGV[1] then return redis.error_reply('Value changed; refresh before saving') end; local ttl=redis.call('PTTL',KEYS[1]); redis.call('SET',KEYS[1],ARGV[2]); if ttl >= 0 then redis.call('PEXPIRE',KEYS[1],ttl) end; return 1",
                    new StackExchange.Redis.RedisKey[] { stringKeyName },
                    new StackExchange.Redis.RedisValue[] { valueControl.OriginalValue, save_text });
                valueControl.AcceptChanges();
                await RefreshKeyAsync();
            }
            catch (StackExchange.Redis.RedisException ex)
            {
                MessageBox.Show(ex.Message, UiText.OperationFailedRefresh);
            }
        }
    }
}
