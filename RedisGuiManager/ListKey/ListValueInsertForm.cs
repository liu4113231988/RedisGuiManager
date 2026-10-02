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
    public partial class ListValueInsertForm : Form
    {
        private RedisClient redisClient = null;
        private StackExchange.Redis.IDatabase database;
        private string key = string.Empty;

        public ListValueInsertForm(RedisClient client, string key, StackExchange.Redis.IDatabase database = null)
        {
            InitializeComponent();

            if (Config.darkmode > 0)
            {
                Utils.DarkThemeForm(this);
            }

            this.redisClient = client;
            this.database = database ?? client.Redis;
            this.key = key;
        }

        private void ListValueInsertForm_Load(object sender, EventArgs e)
        {
            textBox_server.Text = string.Format("{0} [{1}:{2}]", redisClient.Settings.name, redisClient.Settings.host, redisClient.Settings.port);
            textBox_key.Text = this.key;
            textBox_db_num.Text = this.database.Database.ToString();

            Icon = Icon.FromHandle(Properties.Resources.brackets_Square_16xMD.GetHicon());
            ListValueInsertForm_SizeChanged(null, null);
        }

        private void button_save_Click(object sender, EventArgs e)
        {
            try
            {
            if (redisClient == null || !redisClient.CanWrite()) return;
            if (radioButton_order_default.Checked)
            {
                if (database.ListLeftPush(this.key, textBox_value.Text) > 0)
                {
                    Close();
                    return;
                }
                else
                {
                    MessageBox.Show("Save fail");
                }
            }
            else
            {
                if (database.ListRightPush(this.key, textBox_value.Text) > 0)
                {
                    Close();
                    return;
                }
                else
                {
                    MessageBox.Show("Save fail");
                }
            }

            }
            catch (Exception ex) when (ex is StackExchange.Redis.RedisException || ex is ObjectDisposedException)
            {
                MessageBox.Show(this, ex.Message, "Write failed; input preserved");
            }
        }

        private void ListValueInsertForm_Shown(object sender, EventArgs e)
        {
            textBox_value.Focus();
        }

        private void ListValueInsertForm_SizeChanged(object sender, EventArgs e)
        {
            button_save.Location = new Point(this.Width / 2 - button_save.Width / 2, this.Height - 100);
        }
	}
}
