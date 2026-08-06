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
    public partial class FormServerInfo : Form
    {
        private RedisClient redisClient;

        public FormServerInfo(RedisClient client)
        {
            InitializeComponent();

            if (Config.darkmode > 0)
            {
                Utils.DarkThemeForm(this);
            }

            redisClient = client;
        }

        private void FormServerInfo_Load(object sender, EventArgs e)
        {
            Icon = Icon.FromHandle(Properties.Resources.redis.GetHicon());
            Text = $"Server Info - {redisClient.Settings.name} [{redisClient.Settings.host}:{redisClient.Settings.port}]";
            LoadServerInfo();
        }

        private void LoadServerInfo()
        {
            try
            {
                var info = redisClient.RedisServer.Info();
                var sections = info.ToList();

                dataGridView_info.Rows.Clear();

                foreach (var section in sections)
                {
                    dataGridView_info.Rows.Add("===== " + section.Key + " =====", "");
                    foreach (var pair in section)
                    {
                        dataGridView_info.Rows.Add("  " + pair.Key, pair.Value);
                    }
                    dataGridView_info.Rows.Add("", "");
                }

                // Summary at top
                var summary = new List<(string, string)>();

                foreach (var section in sections)
                {
                    foreach (var pair in section)
                    {
                        string key = pair.Key;
                        string val = pair.Value;

                        if (key == "redis_version" || key == "redis_mode" || key == "os" ||
                            key == "tcp_port" || key == "uptime_in_days" ||
                            key == "connected_clients" || key == "blocked_clients" ||
                            key == "used_memory_human" || key == "used_memory_peak_human" ||
                            key == "maxmemory_human" || key == "mem_fragmentation_ratio" ||
                            key == "total_connections_received" || key == "total_commands_processed" ||
                            key == "instantaneous_ops_per_sec" ||
                            key == "keyspace_hits" || key == "keyspace_misses" ||
                            key == "rejected_connections" || key == "expired_keys" ||
                            key == "evicted_keys" || key == "pubsub_channels" || key == "pubsub_patterns" ||
                            key == "role")
                        {
                            summary.Add((key, val));
                        }
                    }
                }

                // Update summary labels
                UpdateSummary(summary);

                // Calculate hit rate
                long hits = 0, misses = 0;
                foreach (var s in summary)
                {
                    if (s.Item1 == "keyspace_hits") long.TryParse(s.Item2, out hits);
                    if (s.Item1 == "keyspace_misses") long.TryParse(s.Item2, out misses);
                }
                if (hits + misses > 0)
                {
                    double hitRate = (double)hits / (hits + misses) * 100;
                    label_hitrate_val.Text = $"{hitRate:F2}%  (H:{hits} / M:{misses})";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load server info\r\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateSummary(List<(string, string)> summary)
        {
            var dict = summary.ToDictionary(x => x.Item1, x => x.Item2);

            if (dict.TryGetValue("redis_version", out string ver)) label_version_val.Text = ver;
            if (dict.TryGetValue("redis_mode", out string mode)) label_mode_val.Text = mode;
            if (dict.TryGetValue("role", out string role)) label_role_val.Text = role;
            if (dict.TryGetValue("os", out string os)) label_os_val.Text = os;
            if (dict.TryGetValue("uptime_in_days", out string uptime)) label_uptime_val.Text = uptime + " days";
            if (dict.TryGetValue("connected_clients", out string cc)) label_clients_val.Text = cc;
            if (dict.TryGetValue("used_memory_human", out string um)) label_mem_val.Text = um;
            if (dict.TryGetValue("used_memory_peak_human", out string pm)) label_peakmem_val.Text = pm;
            if (dict.TryGetValue("maxmemory_human", out string mm)) label_maxmem_val.Text = mm;
            if (dict.TryGetValue("instantaneous_ops_per_sec", out string ops)) label_ops_val.Text = ops + " /s";
            if (dict.TryGetValue("total_commands_processed", out string tcp)) label_totalcmd_val.Text = tcp;
            if (dict.TryGetValue("expired_keys", out string ek)) label_expired_val.Text = ek;
            if (dict.TryGetValue("evicted_keys", out string evk)) label_evicted_val.Text = evk;
            if (dict.TryGetValue("pubsub_channels", out string psc)) label_pubsub_val.Text = psc;
        }

        private void button_refresh_Click(object sender, EventArgs e)
        {
            LoadServerInfo();
        }

        private void button_close_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
