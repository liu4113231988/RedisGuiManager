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
    public partial class SetValueInsertForm : Form
    {
        private RedisClient redisClient = null;
        private StackExchange.Redis.IDatabase database;
        private string key = string.Empty;

        public SetValueInsertForm(RedisClient client, string key, StackExchange.Redis.IDatabase database = null)
        {
            InitializeComponent();

            if (Config.darkmode > 0)
            {
                Utils.DarkThemeForm(this);
            }

            redisClient = client;
            this.database = database ?? client.Redis;
            this.key = key;
        }

        private void SetValueInsertForm_Load(object sender, EventArgs e)
        {
            textBox_server.Text = string.Format("{0}[{1}:{2}]", redisClient.Settings.name, redisClient.Settings.host, redisClient.Settings.port);
            textBox_key.Text = key;
            textBox_db_num.Text = database.Database.ToString();

            Icon = Icon.FromHandle(Properties.Resources.docview_xaml_on_16x16.GetHicon());
        }

        private void button_save_Click(object sender, EventArgs e)
        {
            try
            {
            if (redisClient == null || !redisClient.CanWrite()) return;
            if (database.SetAdd(key, textBox_value.Text))
            {
                Close();
                return;
            }
            else
            {
                MessageBox.Show($"{textBox_value.Text} is already exist");
            }

            }
            catch (Exception ex) when (ex is StackExchange.Redis.RedisException || ex is ObjectDisposedException)
            {
                MessageBox.Show(this, ex.Message, "Write failed; input preserved");
            }
        }

        private void SetValueInsertForm_Shown(object sender, EventArgs e)
        {
            textBox_value.Focus();
        }
	}
}
