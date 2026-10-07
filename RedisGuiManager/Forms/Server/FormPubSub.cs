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

namespace RedisGuiManager
{
    public partial class FormPubSub : Form
    {
        private RedisClient redisClient;
        private ISubscriber subscriber;
        private Dictionary<string, ChannelMessageQueue> subscriptions = new Dictionary<string, ChannelMessageQueue>();
        private ChannelMessageQueue keyspaceSubscription;

        // A long-running subscription would otherwise grow the grid without bound and eventually
        // lock up the UI. Oldest rows are dropped once the cap is reached.
        private const int MaxMessageRows = 5000;
        private long droppedMessages;

        public FormPubSub(RedisClient client)
        {
            InitializeComponent();

            // Keyspace notifications ride on pattern subscription, so they fit naturally here.
            var keyspaceButton = new Button
            {
                Text = "Watch keyspace events",
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Size = new Size(170, textBox_channel.Height),
                UseVisualStyleBackColor = true
            };
            keyspaceButton.Location = new Point(Width - keyspaceButton.Width - 20, textBox_channel.Top);
            keyspaceButton.Click += (s, e) => SubscribeKeyspaceNotifications();
            Resize += (s, e) => keyspaceButton.Left = ClientSize.Width - keyspaceButton.Width - 20;
            Controls.Add(keyspaceButton);
            keyspaceButton.BringToFront();

            if (Config.darkmode > 0)
            {
                Utils.DarkThemeForm(this);
            }

            redisClient = client;
        }

        private void FormPubSub_Load(object sender, EventArgs e)
        {
            Icon = Icon.FromHandle(Properties.Resources.console.GetHicon());
            Text = $"Pub/Sub - {redisClient.Settings.name} [{redisClient.Settings.host}:{redisClient.Settings.port}]";

            try
            {
                // RedisServer only resolves a single endpoint (the first one for clusters),
                // so take the multiplexer directly to cover every node.
                subscriber = redisClient.Multiplexer?.GetSubscriber();
                if (subscriber == null)
                {
                    MessageBox.Show(UiText.SubscriberUnavailable,
                        "Pub/Sub", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(UiText.GetSubscriberFailed + ex.Message, UiText.ErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void button_subscribe_Click(object sender, EventArgs e)
        {
            if (subscriber == null)
            {
                MessageBox.Show(UiText.SubscriberNotAvailable);
                return;
            }

            string channel = textBox_channel.Text.Trim();
            if (string.IsNullOrEmpty(channel))
            {
                MessageBox.Show(UiText.ChannelNameRequired);
                return;
            }

            if (subscriptions.ContainsKey(channel))
            {
                MessageBox.Show($"Already subscribed to [{channel}]");
                return;
            }

            try
            {
                // A trailing '*' subscribes by pattern; everything else is a literal channel.
                bool isPattern = channel.Contains('*') || channel.Contains('?') || channel.Contains('[');
                var redisChannel = isPattern ? RedisChannel.Pattern(channel) : RedisChannel.Literal(channel);
                var queue = subscriber.Subscribe(redisChannel);
                queue.OnMessage(msg =>
                {
                    this.BeginInvoke((Action)(() =>
                    {
                        AppendMessage(msg.Channel.ToString(), msg.Message.ToString());
                    }));
                });

                subscriptions[channel] = queue;

                listBox_channels.Items.Add(isPattern ? $"{channel}  (pattern)" : channel);
                textBox_channel.Clear();
                label_sub_count.Text = $"Subscriptions: {subscriptions.Count}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(UiText.SubscribeFailed + ex.Message, UiText.ErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void button_unsubscribe_Click(object sender, EventArgs e)
        {
            if (listBox_channels.SelectedIndex < 0)
            {
                MessageBox.Show(UiText.SelectChannelToUnsubscribe);
                return;
            }

            string channel = listBox_channels.SelectedItem.ToString();

            if (subscriptions.TryGetValue(channel, out var queue))
            {
                try
                {
                    queue.Unsubscribe();
                    subscriptions.Remove(channel);
                    listBox_channels.Items.Remove(channel);
                    label_sub_count.Text = $"Subscriptions: {subscriptions.Count}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show(UiText.UnsubscribeFailed + ex.Message, UiText.ErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void button_publish_Click(object sender, EventArgs e)
        {
            if (!redisClient.CanWrite()) return;
            if (subscriber == null)
            {
                MessageBox.Show(UiText.SubscriberNotAvailable);
                return;
            }

            string channel = textBox_pub_channel.Text.Trim();
            string message = textBox_pub_message.Text;

            if (string.IsNullOrEmpty(channel))
            {
                MessageBox.Show(UiText.ChannelNameRequired);
                return;
            }

            try
            {
                long receivers = subscriber.Publish(RedisChannel.Literal(channel), message);
                toolStripStatusLabel1.Text = $"Published to {channel}, {receivers} receiver(s)";
            }
            catch (Exception ex)
            {
                MessageBox.Show(UiText.PublishFailed + ex.Message, UiText.ErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Subscribes to Redis keyspace notifications for every DB, which is how a client observes
        /// expiries, evictions and invalidations caused by other clients. Requires the server to be
        /// configured with notify-keyspace-events (otherwise the subscription simply stays silent).
        /// </summary>
        private void SubscribeKeyspaceNotifications()
        {
            if (subscriber == null)
            {
                MessageBox.Show(UiText.SubscriberNotAvailable);
                return;
            }

            if (keyspaceSubscription != null)
            {
                MessageBox.Show(UiText.WatchAlreadyActive);
                return;
            }

            try
            {
                // Reports DEL, EXPIRE, EXPIRED, RENAMED and similar for every key in every DB.
                // Equivalent to RedisChannel.KeySpacePattern("*"), written out to avoid that
                // helper's awkward ref parameter.
                var channel = RedisChannel.Pattern("__keyspace@*__:*");
                var queue = subscriber.Subscribe(channel);

                queue.OnMessage(msg =>
                {
                    this.BeginInvoke((Action)(() =>
                    {
                        // Messages look like: "__keyspace@0__:mykey" -> "expired"
                        string payload = msg.Message.ToString();
                        string source = msg.Channel.ToString();
                        int separator = source.IndexOf("__:");
                        string key = separator >= 0 && source.Length > separator + 3
                            ? source.Substring(separator + 3)
                            : source;
                        AppendMessage($"{key}  (db{ExtractDatabase(source)})", payload);
                    }));
                });

                keyspaceSubscription = queue;
                listBox_channels.Items.Add("__keyspace@*__:key  (keyspace notifications)");
                label_sub_count.Text = $"Subscriptions: {subscriptions.Count + 1}";

                MessageBox.Show(this,
                    "Watching keyspace notifications for every database.\r\n\r\n" +
                    "You will only see events if the server has notify-keyspace-events enabled " +
                    "(for example: CONFIG SET notify-keyspace-events \"KEA\").",
                    "Keyspace notifications", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(UiText.WatchFailedPrefix + ex.Message, UiText.ErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string ExtractDatabase(string channel)
        {
            int at = channel.IndexOf('@');
            int separator = channel.IndexOf("__:", at < 0 ? 0 : at);
            return at >= 0 && separator > at + 1 ? channel.Substring(at + 1, separator - at - 1) : "?";
        }

        private void AppendMessage(string channel, string message)
        {
            if (IsDisposed || Disposing) return;

            // Drop the oldest rows instead of letting the grid grow without limit.
            int overflow = dataGridView_messages.Rows.Count - MaxMessageRows + 1;
            if (overflow > 0)
            {
                for (int i = 0; i < overflow && dataGridView_messages.Rows.Count > 0; i++)
                {
                    droppedMessages++;
                    dataGridView_messages.Rows.RemoveAt(0);
                }
            }

            const int MaxMessageLength = 10_000;
            if (message != null && message.Length > MaxMessageLength)
            {
                message = message.Substring(0, MaxMessageLength) +
                          $"... [{message.Length - MaxMessageLength:N0} more characters truncated]";
            }

            int row = dataGridView_messages.Rows.Add(
                DateTime.Now.ToString("HH:mm:ss.fff"),
                channel,
                message
            );

            // Auto-scroll only while the user is already at the bottom, so reading older
            // messages is not interrupted by incoming traffic.
            bool atBottom = dataGridView_messages.RowCount <= 1 ||
                            dataGridView_messages.FirstDisplayedScrollingRowIndex >=
                            dataGridView_messages.RowCount - 1 - dataGridView_messages.DisplayedRowCount(false);
            if (atBottom)
            {
                dataGridView_messages.FirstDisplayedScrollingRowIndex = row;
            }

            UpdateDroppedLabel();
        }

        private void UpdateDroppedLabel()
        {
            if (toolStripStatusLabel1 == null) return;

            if (droppedMessages > 0)
            {
                toolStripStatusLabel1.Text = $"{dataGridView_messages.Rows.Count} shown · {droppedMessages:N0} older messages dropped";
            }
        }

        private void button_clear_Click(object sender, EventArgs e)
        {
            dataGridView_messages.Rows.Clear();
            droppedMessages = 0;
            UpdateDroppedLabel();
        }

        private void FormPubSub_FormClosing(object sender, FormClosingEventArgs e)
        {
            foreach (var kv in subscriptions)
            {
                try { kv.Value.Unsubscribe(); } catch { }
            }
            subscriptions.Clear();

            if (keyspaceSubscription != null)
            {
                try { keyspaceSubscription.Unsubscribe(); } catch { }
                keyspaceSubscription = null;
            }
        }
    }
}
