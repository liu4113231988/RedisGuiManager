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

namespace RedisGuiManager
{
    public partial class FormPubSub : Form
    {
        private RedisClient redisClient;
        private ISubscriber subscriber;
        private Dictionary<string, ChannelMessageQueue> subscriptions = new Dictionary<string, ChannelMessageQueue>();

        public FormPubSub(RedisClient client)
        {
            InitializeComponent();

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
                subscriber = redisClient.RedisServer.Multiplexer.GetSubscriber();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to get subscriber\r\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void button_subscribe_Click(object sender, EventArgs e)
        {
            if (subscriber == null)
            {
                MessageBox.Show("Subscriber not available");
                return;
            }

            string channel = textBox_channel.Text.Trim();
            if (string.IsNullOrEmpty(channel))
            {
                MessageBox.Show("Channel name cannot be empty");
                return;
            }

            if (subscriptions.ContainsKey(channel))
            {
                MessageBox.Show($"Already subscribed to [{channel}]");
                return;
            }

            try
            {
                var queue = subscriber.Subscribe(channel);
                queue.OnMessage(msg =>
                {
                    this.BeginInvoke((Action)(() =>
                    {
                        int row = dataGridView_messages.Rows.Add(
                            DateTime.Now.ToString("HH:mm:ss.fff"),
                            channel,
                            msg.Message.ToString()
                        );
                        dataGridView_messages.FirstDisplayedScrollingRowIndex = row;
                    }));
                });

                subscriptions[channel] = queue;

                listBox_channels.Items.Add(channel);
                textBox_channel.Clear();
                label_sub_count.Text = $"Subscriptions: {subscriptions.Count}";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Subscribe failed\r\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void button_unsubscribe_Click(object sender, EventArgs e)
        {
            if (listBox_channels.SelectedIndex < 0)
            {
                MessageBox.Show("Please select a channel to unsubscribe");
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
                    MessageBox.Show("Unsubscribe failed\r\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void button_publish_Click(object sender, EventArgs e)
        {
            if (!redisClient.CanWrite()) return;
            if (subscriber == null)
            {
                MessageBox.Show("Subscriber not available");
                return;
            }

            string channel = textBox_pub_channel.Text.Trim();
            string message = textBox_pub_message.Text;

            if (string.IsNullOrEmpty(channel))
            {
                MessageBox.Show("Channel name cannot be empty");
                return;
            }

            try
            {
                long receivers = subscriber.Publish(channel, message);
                toolStripStatusLabel1.Text = $"Published to {channel}, {receivers} receiver(s)";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Publish failed\r\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void button_clear_Click(object sender, EventArgs e)
        {
            dataGridView_messages.Rows.Clear();
        }

        private void FormPubSub_FormClosing(object sender, FormClosingEventArgs e)
        {
            foreach (var kv in subscriptions)
            {
                try { kv.Value.Unsubscribe(); } catch { }
            }
            subscriptions.Clear();
        }
    }
}
