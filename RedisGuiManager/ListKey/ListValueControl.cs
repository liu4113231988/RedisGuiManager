using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using RedisGuiManager.Properties;
using Newtonsoft.Json;

namespace RedisGuiManager
{
    public partial class ListValueControl : UserControl
    {
        private DataGridViewRow selectRow = null;
        private string stringKeyName = string.Empty;
        private RedisClient redisClient = null;
        private StackExchange.Redis.IDatabase database;
        private readonly PageNavigator pages = new PageNavigator();

        private readonly GridUi.SearchState searchState = new GridUi.SearchState();

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

        public ListValueControl()
        {
            InitializeComponent();
            foreach (Control control in Controls)
                if ((control.Anchor & AnchorStyles.Bottom) != 0) { if (control.Height > 72) control.Height -= 36; else control.Top -= 36; }
            Controls.Add(pages);
            pages.CanNavigate = valueControl.ConfirmDiscard;
            pages.PageChanged += () => RefreshKeyAsync();
            valueControl.ProtectSelection(dataGridView_list);
            GridUi.LimitCellText(dataGridView_list);
            GridUi.AttachRowValueMenu(dataGridView_list,
                () => dataGridView_list.SelectedRows.Count > 0 ? dataGridView_list.SelectedRows[0].Cells[1].Value?.ToString() ?? "" : "");

            if (Config.darkmode > 0)
            {
                Utils.DarkThemeControl(this);
            }
        }

        private void ListValueControl_Load(object sender, EventArgs e)
        {
            keyOperateControl.LoadValue.Click += LoadValue_Click;
            dataGridView_list.SelectionChanged += dataGridView_list_SelectionChanged;
            dataGridView_list.SizeChanged += dataGridView_list_SizeChanged;
            dataGridView_list_SizeChanged(null, null);
        }

        private void dataGridView_list_SizeChanged(object sender, EventArgs e)
        {
            dataGridView_list.Columns[1].Width = dataGridView_list.Width - 104;
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
                MessageBox.Show(UiText.RedisConnectionError);
                return;
            }

            // LRANGE seeks by index, so paging is already O(log N); only the round trip needs to
            // move off the UI thread.
            int offset = pages.Offset;
            var db = database;
            var key = stringKeyName;
            var batch = await db.ListRangeAsync(key, offset, offset + PageNavigator.PageSize);

            if (key != stringKeyName) return;

            pages.UpdatePage(batch.Length > PageNavigator.PageSize);
            var read = batch.Take(PageNavigator.PageSize).ToArray();

            int size = 0;
            for (int i = 0; i < read.Length; i++)
            {
                size += Encoding.UTF8.GetBytes(read[i].ToString()).Length;
            }

            label_size.Text = UiText.SizePrefix + Utils.GetSizeDescription(size);
            label_length_val.Text = string.Format(UiText.RowsOnThisPage, read.Length);

            Utils.ControlDataGridViewRow(dataGridView_list, read.Length);
            for (int i = 0; i < read.Length; i++)
            {
                dataGridView_list.Rows[i].Cells[0].Value = pages.Offset + i;
                dataGridView_list.Rows[i].Cells[1].Value = read[i];
            }
        }

        private void dataGridView_list_SelectionChanged(object sender, EventArgs e)
        {
            if (dataGridView_list.SelectedRows.Count > 0)
            {
                if (dataGridView_list.SelectedRows[0].Cells[1].Value != null)
                {
                    selectRow = dataGridView_list.SelectedRows[0];
                    object value = selectRow.Cells[1].Value;

                    valueControl.SetValue(value);

                    button_save.Enabled = redisClient != null && !redisClient.Settings.read_only;
                }
            }
            else
            {
                valueControl.SetValue(string.Empty);
                selectRow = null;
                button_save.Enabled = false;
            }
        }

        public async Task SetNewKey(RedisClient redisClient, string key)
        {
            if (key != stringKeyName || redisClient != this.redisClient) pages.Reset();
            this.redisClient = redisClient;
            database = redisClient.Redis;
            stringKeyName = key;

            dataGridView_list.SelectionChanged -= dataGridView_list_SelectionChanged;

            keyOperateControl.SetRedisClient(redisClient, key);
            await RefreshKeyAsync();
            valueControl.SetValue(string.Empty);
            dataGridView_list.ClearSelection();

            dataGridView_list.SelectionChanged += dataGridView_list_SelectionChanged;
        }

        private async void button_delete_row_Click(object sender, EventArgs e)
        {
            if (redisClient == null || !redisClient.CanWrite()) return;
            try
            {
                if (selectRow != null)
                {
                    int selectIndex = int.Parse(selectRow.Cells[0].Value.ToString());
                    if (MessageBox.Show(string.Format(UiText.DeleteKeyIndexPrompt, stringKeyName, selectIndex), UiText.Delete, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    {
                        string randomValue = "Remove:" + Guid.NewGuid().ToString();
                        if ((long)await database.ScriptEvaluateAsync(
                            "if redis.call('LINDEX',KEYS[1],ARGV[1]) ~= ARGV[2] then return redis.error_reply('List changed; refresh first') end; redis.call('LSET',KEYS[1],ARGV[1],ARGV[3]); return redis.call('LREM',KEYS[1],1,ARGV[3])",
                            new StackExchange.Redis.RedisKey[] { stringKeyName },
                            new StackExchange.Redis.RedisValue[] { selectIndex, (StackExchange.Redis.RedisValue)selectRow.Cells[1].Value, randomValue }) > 0)
                        {
                            MessageBox.Show(string.Format(UiText.DeleteIndexSuccess, selectIndex));
                            await RefreshKeyAsync();
                        }
                        else
                        {
                            MessageBox.Show(string.Format(UiText.DeleteIndexFailed, selectIndex));
                        }
                    }
                }

            }
            catch (StackExchange.Redis.RedisException ex)
            {
                MessageBox.Show(ex.Message, UiText.OperationFailedRefresh);
            }
        }

        private async void button_insert_row_Click(object sender, EventArgs e)
        {
            if (redisClient == null || !redisClient.CanWrite()) return;
            using (ListValueInsertForm form = new ListValueInsertForm(redisClient, stringKeyName, database: database))
            {
                form.ShowDialog();
                await RefreshKeyAsync();
            }
        }

        private async void button_refresh_Click(object sender, EventArgs e)
        {
            if (this.selectRow != null)
            {
                int selectIndex = int.Parse(selectRow.Cells[0].Value.ToString());
                var read = await database.ListGetByIndexAsync(stringKeyName, selectIndex);
                selectRow.Cells[1].Value = read.ToString();
                valueControl.SetValue(read.ToString());
            }
        }

        private async void button_save_Click(object sender, EventArgs e)
        {
            if (!valueControl.CanEditText || ValueControl.GetDisplayType() == ValueControl.DisplayType.Hex) { MessageBox.Show(UiText.ReadOnlyBinaryValue); return; }
            if (redisClient == null || !redisClient.CanWrite()) return;
            if (dataGridView_list.SelectedRows.Count <= 0)
            {
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

            int index = int.Parse(selectRow.Cells[0].Value.ToString());
            try
            {
                await database.ScriptEvaluateAsync(
                    "if redis.call('LINDEX',KEYS[1],ARGV[1]) ~= ARGV[2] then return redis.error_reply('List changed; refresh before saving') end; redis.call('LSET',KEYS[1],ARGV[1],ARGV[3]); return 1",
                    new StackExchange.Redis.RedisKey[] { stringKeyName },
                    new StackExchange.Redis.RedisValue[] { index, valueControl.OriginalValue, save_text });
            }
            catch (StackExchange.Redis.RedisException ex) { MessageBox.Show(ex.Message, "Save failed"); return; }
            valueControl.AcceptChanges();
            await RefreshKeyAsync();
        }

        private void textBox_search_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            if (dataGridView_list.Rows.Count <= 0)
            {
                return;
            }

            if (checkBox_search_grep.Checked)
            {
                GridUi.FilterRows(dataGridView_list, textBox_search, firstColumnOnly: false);
            }
            else
            {
                GridUi.FindNextMatch(dataGridView_list, textBox_search, searchState, firstColumnOnly: false);
            }
        }
    }
}
