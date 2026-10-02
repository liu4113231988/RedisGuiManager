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
    public partial class ZSetValueInsertForm : Form
    {
        private RedisClient redisClient = null;
        private StackExchange.Redis.IDatabase database;
        private string key = string.Empty;

        public ZSetValueInsertForm(RedisClient client, string key, StackExchange.Redis.IDatabase database = null)
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

        private void ZSetValueInsertForm_Load(object sender, EventArgs e)
        {
            textBox_server.Text = string.Format("{0}[{1}:{2}]", redisClient.Settings.name, redisClient.Settings.host, redisClient.Settings.port);
            textBox_key.Text = key;
            textBox_db_num.Text = database.Database.ToString();
            textBox_score.Text = "0";

            Icon = Icon.FromHandle(Properties.Resources.zset.GetHicon());
        }

        private void button_save_Click(object sender, EventArgs e)
        {
            try
            {
            if (redisClient == null || !redisClient.CanWrite()) return;
            if (double.TryParse(textBox_score.Text, out double sco) == false)
            {
                MessageBox.Show("Invalid score");
                return;
            }

            if (database.SortedSetAdd(key, textBox_value.Text, sco))
            {
                Close();
                return;
            }
            else
            {
                MessageBox.Show("Save fail");
            }

            }
            catch (Exception ex) when (ex is StackExchange.Redis.RedisException || ex is ObjectDisposedException)
            {
                MessageBox.Show(this, ex.Message, "Write failed; input preserved");
            }
        }

        private void ZSetValueInsertForm_Shown(object sender, EventArgs e)
        {
            textBox_value.Focus();
        }
	}
}
