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
    public partial class StreamValueInsertForm : Form
    {
        private RedisClient redisClient = null;
        private StackExchange.Redis.IDatabase database;
        private string keyName;

        public StreamValueInsertForm(RedisClient client, string keyName, StackExchange.Redis.IDatabase database = null)
        {
            InitializeComponent();

            if (Config.darkmode > 0)
            {
                Utils.DarkThemeForm(this);
            }

            this.redisClient = client;
            this.database = database ?? client.Redis;
            this.keyName = keyName;
            textBox_key.Text = keyName;
            textBox_id.Text = "*";
        }

        private void button_save_Click(object sender, EventArgs e)
        {
            try
            {
            if (redisClient == null || !redisClient.CanWrite()) return;
            if (string.IsNullOrEmpty(textBox_field.Text))
            {
                MessageBox.Show(UiText.FieldIsEmpty);
                return;
            }

            string id = string.IsNullOrEmpty(textBox_id.Text) ? "*" : textBox_id.Text;

            try
            {
                var nameValues = new NameValueEntry[]
                {
                    new NameValueEntry(textBox_field.Text, textBox_value.Text)
                };

                var result = database.StreamAdd(keyName, nameValues, id);

                if (!result.IsNull)
                {
                    Close();
                }
                else
                {
                    MessageBox.Show(UiText.AddStreamEntryFailed);
                }
            }
            catch (RedisServerException ex)
            {
                MessageBox.Show(ex.Message, UiText.ErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            }
            catch (Exception ex) when (ex is StackExchange.Redis.RedisException || ex is ObjectDisposedException)
            {
                MessageBox.Show(this, ex.Message, UiText.WriteFailedInputPreserved);
            }
        }
    }
}
