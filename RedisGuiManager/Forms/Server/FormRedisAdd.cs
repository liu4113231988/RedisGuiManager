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
using System.Net;
using Newtonsoft.Json;

namespace RedisGuiManager
{
    public partial class FormRedisAdd : Form
    {
        private RedisSettings settings = null;
        private RedisSettings originalSettings;
        private readonly TextBox usernameInput = new TextBox();
        private readonly CheckBox readOnlyInput = new CheckBox { Text = "Read-only connection", AutoSize = true };
        public RedisSettings Settings
        {
            get { return settings; }
        }

        public FormRedisAdd(RedisSettings redisSettings)
        {
            InitializeComponent();
            int y = button_finish.Top;
            MaximumSize = Size.Empty;
            MinimumSize = Size.Empty;
            button_finish.Top += 70;
            button_connection_test.Top += 70;
            Controls.Add(new Label { Text = "ACL user", AutoSize = true, Location = new Point(32, y + 3) });
            usernameInput.SetBounds(122, y, 246, 26);
            usernameInput.Font = textBox_name.Font;
            usernameInput.AccessibleName = "Redis ACL username (blank uses default user)";
            Controls.Add(usernameInput);
            readOnlyInput.Location = new Point(122, y + 34);
            Controls.Add(readOnlyInput);

            // Private-key path is long and easy to mistype, so offer a file picker next to it.
            var browseKey = new Button
            {
                Text = "...",
                AccessibleName = "Browse for the SSH private key",
                Font = textBox_tunnel_key.Font,
                Location = new Point(textBox_tunnel_key.Right + 6, textBox_tunnel_key.Top),
                Size = new Size(34, textBox_tunnel_key.Height),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            browseKey.Click += button_browse_ssh_key_Click;
            Controls.Add(browseKey);
            textBox_tunnel_key.Width = Math.Max(60, textBox_tunnel_key.Width - browseKey.Width - 6);
            ClientSize = new Size(ClientSize.Width, ClientSize.Height + 70);
            MaximumSize = MinimumSize = Size;

            if (Config.darkmode > 0)
            {
                Utils.DarkThemeForm(this);
            }

            originalSettings = redisSettings;
            settings = redisSettings == null ? new RedisSettings()
            {
                host = "127.0.0.1",
                port = 6379,
                auth = string.Empty
            } : JsonConvert.DeserializeObject<RedisSettings>(JsonConvert.SerializeObject(redisSettings));

            if (redisSettings != null)
            {
                textBox_name.ReadOnly = true;
            }
        }

        private void FormRedisAdd_Load(object sender, EventArgs e)
        {
            Icon = Icon.FromHandle(Properties.Resources.action_add_16xLG.GetHicon());

            usernameInput.Text = settings.username ?? "";
            readOnlyInput.Checked = settings.read_only;
            textBox_name.Text = settings.name;
            textBox_ip.Text = settings.host;
            textBox_port.Text = settings.port.ToString();
            textBox_password.Text = settings.auth;
            checkBox_use_tunnel.Checked = settings.use_tunnel;
            textBox_tunnel_ip.Text = settings.ssh_host;
            textBox_tunnel_port.Text = settings.ssh_port.ToString();
            textBox_tunnel_id.Text = settings.ssh_user;
            textBox_tunnel_pw.Text = settings.ssh_password;
            checkBox_use_ssh_key.Checked = settings.use_ssh_key;
            textBox_tunnel_key.Text = settings.ssh_key;
            checkBox_use_ssl.Checked = settings.use_ssl;
            checkBox_use_cluster.Checked = settings.use_cluster;
            textBox_cluster_endpoints.Text = settings.cluster_endpoints ?? "";

            groupBox_tunnel.Enabled = checkBox_use_tunnel.Checked;
            textBox_cluster_endpoints.Enabled = checkBox_use_cluster.Checked;
        }

        private void checkBox_show_password_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox_show_password.Checked)
            {
                textBox_password.PasswordChar = (char)0;
            }
            else
            {
                textBox_password.PasswordChar = '*';
            }
        }

        private bool CheckInput()
        {
            if (string.IsNullOrEmpty(textBox_name.Text))
            {
                MessageBox.Show("Server name can not be empty!");

                return false;
            }

            if (IPAddress.TryParse(textBox_ip.Text, out IPAddress address) == false)
            {
                try
                {
                    IPAddress[] addresses = Dns.GetHostAddresses(textBox_ip.Text);
                    if (addresses.Length == 0)
                    {
                        MessageBox.Show("Invalid IP address or URL");
                        return false;
                    }
                }
                catch
                {
                    MessageBox.Show("Invalid IP address or URL");
                    return false;
                }
            }

            if (int.TryParse(textBox_port.Text, out int port) == false || port < 1 || port > 65535)
            {
                MessageBox.Show("Invalid port");

                return false;
            }

            if (int.TryParse(textBox_tunnel_port.Text, out int tunnel_port) == false || tunnel_port < 1 || tunnel_port > 65535)
            {
                MessageBox.Show("Invalid tunnel port");

                return false;
            }

            if (checkBox_use_cluster.Checked && CheckClusterEndpoints() == false)
            {
                return false;
            }

            if (checkBox_use_tunnel.Checked && checkBox_use_ssh_key.Checked)
            {
                if (string.IsNullOrWhiteSpace(textBox_tunnel_key.Text))
                {
                    MessageBox.Show("SSH private key path can not be empty when a key is used.");
                    return false;
                }

                if (File.Exists(textBox_tunnel_key.Text) == false)
                {
                    MessageBox.Show($"SSH private key not found:\r\n{textBox_tunnel_key.Text}");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Validates the cluster seed list. The connection silently falls back to a single seed when
        /// nothing parses, so a typo here would otherwise surface much later as a confusing
        /// connection error.
        /// </summary>
        private bool CheckClusterEndpoints()
        {
            string raw = textBox_cluster_endpoints.Text;
            if (string.IsNullOrWhiteSpace(raw))
            {
                MessageBox.Show("Cluster mode needs at least one seed endpoint, for example 127.0.0.1:7000.");
                return false;
            }

            var problems = new List<string>();
            int accepted = 0;

            foreach (string item in raw.Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string endpoint = item.Trim();
                if (endpoint.Length == 0) continue;

                int separator = endpoint.LastIndexOf(':');
                if (separator <= 0 || int.TryParse(endpoint.Substring(separator + 1), out int endpointPort) == false
                    || endpointPort < 1 || endpointPort > 65535)
                {
                    problems.Add($"'{endpoint}' is not host:port");
                    continue;
                }

                string host = endpoint.Substring(0, separator).Trim();
                if (host.Length == 0)
                {
                    problems.Add($"'{endpoint}' has no host");
                    continue;
                }

                accepted++;
            }

            if (accepted == 0)
            {
                MessageBox.Show("No usable cluster endpoint was found. Use host:port, one per line, for example 127.0.0.1:7000.");
                return false;
            }

            if (problems.Count > 0)
            {
                DialogResult answer = MessageBox.Show(
                    "These cluster endpoints will be ignored:\r\n\r\n" + string.Join("\r\n", problems) +
                    "\r\n\r\nSave the connection anyway?",
                    "Invalid cluster endpoints", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                return answer == DialogResult.Yes;
            }

            return true;
        }

        private void button_browse_ssh_key_Click(object sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Select SSH private key",
                Filter = "Private key files (*.pem;*.ppk;*.key)|*.pem;*.ppk;*.key|All files (*.*)|*.*",
                CheckFileExists = true
            };

            if (string.IsNullOrWhiteSpace(textBox_tunnel_key.Text) == false)
            {
                string directory = Path.GetDirectoryName(textBox_tunnel_key.Text);
                if (Directory.Exists(directory)) dialog.InitialDirectory = directory;
            }

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                textBox_tunnel_key.Text = dialog.FileName;
            }
        }

        private void button_connection_test_Click(object sender, EventArgs e)
        {
            if (CheckInput() == false)
            {
                return;
            }

            settings.username = usernameInput.Text.Trim();
            settings.read_only = readOnlyInput.Checked;
            settings.name = textBox_name.Text;
            settings.host = textBox_ip.Text;
            settings.port = int.Parse(textBox_port.Text);
            settings.auth = textBox_password.Text;
            settings.use_tunnel = checkBox_use_tunnel.Checked;
            settings.ssh_host = textBox_tunnel_ip.Text;
            settings.ssh_port = int.Parse(textBox_tunnel_port.Text);
            settings.ssh_user = textBox_tunnel_id.Text;
            settings.ssh_password = textBox_tunnel_pw.Text;
            settings.use_ssh_key = checkBox_use_ssh_key.Checked;
            settings.ssh_key = textBox_tunnel_key.Text;
            settings.use_ssl = checkBox_use_ssl.Checked;
            settings.use_cluster = checkBox_use_cluster.Checked;
            settings.cluster_endpoints = textBox_cluster_endpoints.Text;

            RedisClient redis = new RedisClient(settings);
            OperateResult connect = redis.Connect();
            if (connect.IsSuccess)
            {
                MessageBox.Show("Connect Success!");
            }
            else
            {
                MessageBox.Show("Connect Failed\r\n" + connect.Message);
            }

            redis.Close();
        }

        private void button_finish_Click(object sender, EventArgs e)
        {
            if (CheckInput() == false)
            {
                return;
            }

            settings.username = usernameInput.Text.Trim();
            settings.read_only = readOnlyInput.Checked;
            settings.name = textBox_name.Text;
            settings.host = textBox_ip.Text;
            settings.port = int.Parse(textBox_port.Text);
            settings.auth = textBox_password.Text;
            settings.use_tunnel = checkBox_use_tunnel.Checked;
            settings.ssh_host = textBox_tunnel_ip.Text;
            settings.ssh_port = int.Parse(textBox_tunnel_port.Text);
            settings.ssh_user = textBox_tunnel_id.Text;
            settings.ssh_password = textBox_tunnel_pw.Text;
            settings.use_ssh_key = checkBox_use_ssh_key.Checked;
            settings.ssh_key = textBox_tunnel_key.Text;
            settings.use_ssl = checkBox_use_ssl.Checked;
            settings.use_cluster = checkBox_use_cluster.Checked;
            settings.cluster_endpoints = textBox_cluster_endpoints.Text;

            if (originalSettings != null)
            {
                JsonConvert.PopulateObject(JsonConvert.SerializeObject(settings), originalSettings, new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace });
                settings = originalSettings;
            }
            DialogResult = DialogResult.OK;
        }

        private void FormRedisAdd_Shown(object sender, EventArgs e)
        {
            textBox_name.Focus();
        }

        private void checkBox_use_tunnel_CheckedChanged(object sender, EventArgs e)
        {
            groupBox_tunnel.Enabled = checkBox_use_tunnel.Checked;
        }

        private void checkBox_use_cluster_CheckedChanged(object sender, EventArgs e)
        {
            textBox_cluster_endpoints.Enabled = checkBox_use_cluster.Checked;
            if (checkBox_use_cluster.Checked)
            {
                // Cluster mode only supports db 0; disable SSH tunnel as it conflicts
                checkBox_use_tunnel.Checked = false;
                groupBox_tunnel.Enabled = false;
            }
        }

		private void checkBox_show_ssh_password_CheckedChanged(object sender, EventArgs e)
		{
			if (checkBox_show_ssh_password.Checked)
			{
				textBox_tunnel_pw.PasswordChar = (char)0;
			}
			else
			{
                textBox_tunnel_pw.PasswordChar = '*';
			}
		}
	}
}
