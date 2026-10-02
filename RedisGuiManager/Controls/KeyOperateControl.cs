using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RedisGuiManager
{
    public partial class KeyOperateControl : UserControl
    {
        private string keyType = string.Empty;                          // Current key type
        private string keyName = string.Empty;                          // Current key name
        private RedisClient redisClient = null;
        private StackExchange.Redis.IDatabase database;

        [Browsable(true)]
        [Description("Get or set the type expression of the current key value")]
        [DefaultValue("String")]
        public string KeyType
        {
            get { return label_type.Text; }
            set { label_type.Text = value; }
        }

        [Browsable(false)]
        public Button LoadValue
        {
            get { return button_reload; }
        }

        [Browsable(false)]
        public FormMain MainForm
        {
            set; get;
        }

        [Browsable(false)]
        public TreeNode TargetNode
        {
            set; get;
        }

        public KeyOperateControl()
        {
            InitializeComponent();

            if (Config.darkmode > 0)
            {
                Utils.DarkThemeControl(this);
            }
        }

        private void button_delete_Click(object sender, EventArgs e)
        {
            if (redisClient == null || !redisClient.CanWrite()) return;
            MainForm.delete_key_operate(TargetNode, database);
        }

        private void button_ttl_Click(object sender, EventArgs e)
        {
            if (redisClient == null || !redisClient.CanWrite()) return;
            try
            {
                if (redisClient != null)
                {
                    using (FormInputString formInput = new FormInputString())
                    {
                        formInput.TextInfo = "New ttl(seconds), -1 means permanent";
                        if (formInput.ShowDialog() == DialogResult.OK)
                        {
                            if (formInput.InputValue == "-1")
                            {
                                if (database.KeyPersist(keyName))
                                {
                                    MessageBox.Show("Set ttl success");
                                }
                                else
                                {
                                    MessageBox.Show("Set ttl failed");
                                }
                            }
                            else
                            {
                                if (int.TryParse(formInput.InputValue, out int seconds) && seconds >= 0)
                                {
                                    if (database.KeyExpire(keyName, new TimeSpan(0, 0, seconds)))
                                    {
                                        MessageBox.Show("Set ttl success");
                                    }
                                    else
                                    {
                                        MessageBox.Show("Set ttl failed");
                                    }
                                }
                                else
                                {
                                    MessageBox.Show("Please enter the number. not text");
                                }
                            }
                        }
                    }
                }
            }
            catch (StackExchange.Redis.RedisException ex)
            {
                MessageBox.Show(ex.Message, "Operation failed; refresh before retrying");
            }
        }

        private void button_rename_Click(object sender, EventArgs e)
        {
            if (Parent != null && !ValueControl.ConfirmAll(Parent)) return;
            if (redisClient == null || !redisClient.CanWrite()) return;
            try
            {
                if (redisClient != null)
                {
                    using (FormInputString formInput = new FormInputString())
                    {
                        formInput.TextInfo = string.Format("Rename key [{0}]", keyName);
                        if (formInput.ShowDialog() == DialogResult.OK)
                        {
                            if (!string.IsNullOrEmpty(formInput.InputValue) && database.KeyRename(keyName, formInput.InputValue, StackExchange.Redis.When.NotExists))
                            {
                                MainForm.RefreshRenamedKey(TargetNode, keyName, formInput.InputValue);
                            }
                            else
                            {
                                MessageBox.Show("Rename failed");
                            }
                        }
                    }
                }
            }
            catch (StackExchange.Redis.RedisException ex)
            {
                MessageBox.Show(ex.Message, "Operation failed; refresh before retrying");
            }
        }


        private static string FormatTTLShort(long seconds)
        {
            if (seconds == -2) return "missing";
            if (seconds < 0) return "∞";
            if (seconds < 60) return $"{seconds}s";
            if (seconds < 3600) return $"{seconds / 60}m{seconds % 60}s";
            if (seconds < 86400) return $"{seconds / 3600}h{(seconds % 3600) / 60}m";
            return $"{seconds / 86400}d{(seconds % 86400) / 3600}h";
        }

        public void SetRedisClient(RedisClient client, string keyName)
        {
            this.redisClient = client;
            database = client?.Redis;
            if (client != null && Parent != null) client.ApplyReadOnly(Parent);
            this.textBox_key.Text = keyName;
            this.keyName = keyName;

            if (client != null)
            {
                var ttl_raw = database.Execute("TTL", keyName);
                long ttl_seconds = (long)ttl_raw;
                button_ttl.Text = "TTL:" + FormatTTLShort(ttl_seconds);
            }
        }
    }
}
