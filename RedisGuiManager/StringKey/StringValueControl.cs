using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace RedisGuiManager
{
    public partial class StringValueControl : UserControl
    {
        private string stringKeyName = string.Empty;
        private RedisClient redisClient = null;
        private StackExchange.Redis.IDatabase database;

        [Browsable(false)]
        public FormMain MainForm
        {
            set
            {
                keyOperateControl.MainForm = value;
            }
        }

        [Browsable(false)]
        public TreeNode TargetNode
        {
            set
            {
                keyOperateControl.TargetNode = value;
            }
        }

        public StringValueControl()
        {
            InitializeComponent();

            if (Config.darkmode > 0)
            {
                Utils.DarkThemeControl(this);
            }
        }

        private void StringValueControl_Load(object sender, EventArgs e)
        {
            keyOperateControl.LoadValue.Click += LoadValue_Click;
        }

        public async Task SetNewKey(RedisClient client, string key)
        {
            redisClient = client;
            database = client.Redis;
            stringKeyName = key;

            keyOperateControl.SetRedisClient(redisClient, key);
            await RefreshKeyAsync();
        }

        private async void LoadValue_Click(object sender, EventArgs e)
        {
            await RefreshKeyAsync();
        }

        private async Task RefreshKeyAsync()
        {
            if (!valueControl.ConfirmDiscard()) return;
            if (redisClient == null)
            {
                MessageBox.Show("Redis connection error");
                return;
            }

            if (database == null)
            {
                MessageBox.Show("Redis connection error");
                return;
            }

            var key = stringKeyName;
            var read = await database.StringGetAsync(key);

            // The user may have switched keys while the request was in flight.
            if (key != stringKeyName) return;

            valueControl.SetValue(read);
        }

        private async void button_save_Click(object sender, EventArgs e)
        {
            if (redisClient == null || !redisClient.CanWrite()) return;
            try
            {
                if (valueControl.OriginalValue.IsNull) { MessageBox.Show("Key is missing; refresh or create it explicitly"); return; }
                if (!valueControl.CanEditText || ValueControl.GetDisplayType() == ValueControl.DisplayType.Hex)
                {
                    MessageBox.Show("Binary/Hex values are read-only. Switch to text for text values.");
                    return;
                }
                string save_text = valueControl.EditedValue();
                if (ValueControl.GetDisplayType() == ValueControl.DisplayType.Json)
                {
                    try
                    {
                        save_text = JsonConvert.SerializeObject(JsonConvert.DeserializeObject(save_text), Formatting.None);
                    }
                    catch (JsonReaderException)
                    {
                    }
                }

                await database.ScriptEvaluateAsync(
                    "if redis.call('GET',KEYS[1]) ~= ARGV[1] then return redis.error_reply('Value changed; refresh before saving') end; local ttl=redis.call('PTTL',KEYS[1]); redis.call('SET',KEYS[1],ARGV[2]); if ttl >= 0 then redis.call('PEXPIRE',KEYS[1],ttl) end; return 1",
                    new StackExchange.Redis.RedisKey[] { stringKeyName },
                    new StackExchange.Redis.RedisValue[] { valueControl.OriginalValue, save_text });
                valueControl.AcceptChanges();
                await RefreshKeyAsync();
            }
            catch (StackExchange.Redis.RedisException ex)
            {
                MessageBox.Show(ex.Message, "Operation failed; refresh before retrying");
            }
        }
    }
}
