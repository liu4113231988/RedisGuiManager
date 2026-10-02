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
    public partial class FormSlowlog : Form
    {
        private RedisClient redisClient;

        public FormSlowlog(RedisClient client)
        {
            InitializeComponent();

            if (Config.darkmode > 0)
            {
                Utils.DarkThemeForm(this);
            }

            redisClient = client;
        }

        private void FormSlowlog_Load(object sender, EventArgs e)
        {
            Icon = Icon.FromHandle(Properties.Resources.console.GetHicon());
            Text = $"Slowlog - {redisClient.Settings.name} [{redisClient.Settings.host}:{redisClient.Settings.port}]";
            LoadSlowlog();
        }

        private void LoadSlowlog()
        {
            try
            {
                int count = (int)numericUpDown_count.Value;
                var entries = redisClient.RedisServer.SlowlogGet(count);

                dataGridView_slowlog.Rows.Clear();

                foreach (var entry in entries)
                {
                    string args = string.Join(" ", entry.Arguments.Select(a => a.ToString()));

                    dataGridView_slowlog.Rows.Add(
                        "-",
                        entry.Time.ToString("yyyy-MM-dd HH:mm:ss"),
                        FormatDuration(entry.Duration),
                        args,
                        "-",
                        "-"
                    );
                }

                label_count_val.Text = entries.Length.ToString();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load slowlog\r\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string FormatDuration(TimeSpan duration)
        {
            double microseconds = duration.TotalMilliseconds * 1000;
            if (microseconds < 1000) return $"{microseconds:F0} us";
            if (microseconds < 1000000) return $"{duration.TotalMilliseconds:F1} ms";
            return $"{duration.TotalSeconds:F2} s";
        }

        private void button_refresh_Click(object sender, EventArgs e)
        {
            LoadSlowlog();
        }

        private void button_clear_Click(object sender, EventArgs e)
        {
            if (!redisClient.CanWrite()) return;
            if (MessageBox.Show("Clear all slowlog entries?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                try
                {
                    redisClient.RedisServer.SlowlogReset();
                    LoadSlowlog();
                    MessageBox.Show("Slowlog cleared.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to clear slowlog\r\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void button_close_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void dataGridView_slowlog_CellMouseUp(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.ColumnIndex == -1 || e.RowIndex == -1)
                return;

            if (e.Button == MouseButtons.Right)
            {
                dataGridView_slowlog.Rows[e.RowIndex].Selected = true;
                ContextMenuStrip menu = new ContextMenuStrip();
                menu.Items.Add("Copy", null, (s, ev) =>
                {
                    if (dataGridView_slowlog.SelectedRows.Count > 0)
                    {
                        Clipboard.SetText(dataGridView_slowlog.SelectedRows[0].Cells[3].Value?.ToString() ?? "");
                    }
                });
                menu.Show(dataGridView_slowlog, e.Location);
            }
        }
    }
}
