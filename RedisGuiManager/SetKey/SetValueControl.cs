using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using RedisGuiManager.Properties;
using Newtonsoft.Json;

namespace RedisGuiManager
{
    public partial class SetValueControl : UserControl
    {
        private DataGridViewRow selectRow = null;
        private string stringKeyName = string.Empty;
        private RedisClient redisClient = null;
        private StackExchange.Redis.IDatabase database;
        private readonly PageNavigator pages = new PageNavigator();

        // Server-side SSCAN cursors, one per page boundary, so deep paging does not restart the scan.
        private readonly CursorStack<string> setCursors = new CursorStack<string>();

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

        public SetValueControl()
        {
            InitializeComponent();
            foreach (Control control in Controls)
                if ((control.Anchor & AnchorStyles.Bottom) != 0) { if (control.Height > 72) control.Height -= 36; else control.Top -= 36; }
            Controls.Add(pages);
            pages.CanNavigate = valueControl.ConfirmDiscard;
            pages.PageChanged += () => RefreshKeyAsync();
            valueControl.ProtectSelection(dataGridView_set);
            GridUi.LimitCellText(dataGridView_set);
            GridUi.AttachRowValueMenu(dataGridView_set,
                () => dataGridView_set.SelectedRows.Count > 0 ? dataGridView_set.SelectedRows[0].Cells[1].Value?.ToString() ?? "" : "");

            if (Config.darkmode > 0)
            {
                Utils.DarkThemeControl(this);
            }
        }

        private void SetValueControl_Load(object sender, EventArgs e)
        {
            keyOperateControl.LoadValue.Click += LoadValue_Click;
            dataGridView_set.SelectionChanged += dataGridView_set_SelectionChanged;
            dataGridView_set.SizeChanged += dataGridView_set_SizeChanged;
            dataGridView_set_SizeChanged(null, null);
        }

        private void dataGridView_set_SizeChanged(object sender, EventArgs e)
        {
            dataGridView_set.Columns[1].Width = dataGridView_set.Width - 104;
        }

        private async void LoadValue_Click(object sender, EventArgs e)
        {
            await RefreshKeyAsync();
        }

        private async Task RefreshKeyAsync()
        {
            if (!valueControl.ConfirmDiscard()) return;
            if (redisClient == null || database == null)
            {
                MessageBox.Show(UiText.RedisConnectionError);
                return;
            }

            // SSCAN keeps its server-side cursor per page, so deep paging does not replay the scan
            // from the start, and the request runs off the UI thread.
            int offset = pages.Offset;
            int pageIndex = offset / PageNavigator.PageSize;
            if (setCursors.HasCursor(pageIndex) == false)
            {
                setCursors.Reset("0");
            }

            var cursor = setCursors.Get(pageIndex);
            var client = redisClient;
            var key = stringKeyName;
            var page = await Task.Run(() => client.SetScanPage(key, cursor, PageNavigator.PageSize));

            // The user may have switched keys while the request was in flight.
            if (IsDisposed || Disposing || key != stringKeyName) return;

            bool more = page.NextCursor != "0";
            setCursors.Set(pageIndex + 1, page.NextCursor);
            setCursors.TrimTo(pageIndex + 2);

            pages.UpdatePage(more);
            var read = page.Members.Take(PageNavigator.PageSize).ToArray();

            int size = 0;
            for (int i = 0; i < read.Length; i++)
            {
                size += Encoding.UTF8.GetBytes(read[i].ToString()).Length;
            }

            label_size.Text = UiText.SizePrefix + Utils.GetSizeDescription(size);
            label_length_val.Text = string.Format(UiText.RowsOnThisPage, read.Length);

            Utils.ControlDataGridViewRow(dataGridView_set, read.Length);
            for (int i = 0; i < read.Length; i++)
            {
                dataGridView_set.Rows[i].Cells[0].Value = offset + i;
                dataGridView_set.Rows[i].Cells[1].Value = read[i];
            }
        }

        private void dataGridView_set_SelectionChanged(object sender, EventArgs e)
        {
            if (dataGridView_set.SelectedRows.Count > 0)
            {
                if (dataGridView_set.SelectedRows[0].Cells[1].Value != null)
                {
                    selectRow = dataGridView_set.SelectedRows[0];
                    object value = selectRow.Cells[1].Value;

                    valueControl.SetValue(value);
                    button_save.Enabled = redisClient != null && !redisClient.Settings.read_only;
                }
            }
            else
            {
                valueControl.SetValue(string.Empty);
                button_save.Enabled = false;
            }
        }

        public async Task SetNewKey(RedisClient redisClient, string key)
        {
            if (key != stringKeyName || redisClient != this.redisClient)
            {
                pages.Reset();
                // A different key invalidates every retained SSCAN cursor.
                setCursors.Reset("0");
            }
            if (key != stringKeyName)
            {
                valueControl.SetValue(string.Empty);
            }

            this.redisClient = redisClient;
            database = redisClient.Redis;
            stringKeyName = key;

            dataGridView_set.SelectionChanged -= dataGridView_set_SelectionChanged;

            keyOperateControl.SetRedisClient(redisClient, key);
            await RefreshKeyAsync();

            // Reset derived state so a stale selection/value cannot leak into the new key, matching
            // the Hash and List editors.
            valueControl.SetValue(string.Empty);
            selectRow = null;
            dataGridView_set.ClearSelection();

            dataGridView_set.SelectionChanged += dataGridView_set_SelectionChanged;
        }

        private async void button_insert_row_Click(object sender, EventArgs e)
        {
            if (redisClient == null || !redisClient.CanWrite()) return;
            using (SetValueInsertForm form = new SetValueInsertForm(redisClient, stringKeyName, database: database))
            {
                form.ShowDialog();
                await RefreshKeyAsync();
            }
        }

        private async void button_delete_row_Click(object sender, EventArgs e)
        {
            if (redisClient == null || !redisClient.CanWrite()) return;
            if (dataGridView_set.SelectedRows.Count > 0)
            {
                if (dataGridView_set.SelectedRows[0].Cells[1].Value != null)
                {
                    if (MessageBox.Show(string.Format(UiText.DeleteKeyPrompt, stringKeyName), UiText.Delete, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    {
                        bool removed = await database.SetRemoveAsync(stringKeyName, (StackExchange.Redis.RedisValue)dataGridView_set.SelectedRows[0].Cells[1].Value);
                        if (removed)
                        {
                            MessageBox.Show(UiText.DeleteRowSuccess);
                            await RefreshKeyAsync();
                        }
                        else
                        {
                            MessageBox.Show(UiText.DeleteRowFailed);
                        }
                    }
                }
            }
        }

        private async void button_refresh_Click(object sender, EventArgs e)
        {
            await RefreshKeyAsync();
        }

        private async void button_save_Click(object sender, EventArgs e)
		{
            if (!valueControl.CanEditText || ValueControl.GetDisplayType() == ValueControl.DisplayType.Hex) { MessageBox.Show(UiText.ReadOnlyBinaryValue); return; }
            if (redisClient == null || !redisClient.CanWrite()) return;
            try
            {
                if (dataGridView_set.SelectedRows.Count <= 0)
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

                await database.ScriptEvaluateAsync(
                    "if redis.call('SISMEMBER',KEYS[1],ARGV[1]) == 0 then return redis.error_reply('Member changed; refresh first') end; redis.call('SADD',KEYS[1],ARGV[2]); if ARGV[1] ~= ARGV[2] then redis.call('SREM',KEYS[1],ARGV[1]) end; return 1",
                    new StackExchange.Redis.RedisKey[] { stringKeyName },
                    new StackExchange.Redis.RedisValue[] { selectRow.Cells[1].Value.ToString(), save_text });

                valueControl.AcceptChanges();
            await RefreshKeyAsync();

            }
            catch (StackExchange.Redis.RedisException ex)
            {
                MessageBox.Show(ex.Message, UiText.OperationFailedRefresh);
            }
        }

		private void textBox_search_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            if (dataGridView_set.Rows.Count <= 0)
            {
                return;
            }

            if (checkBox_search_grep.Checked)
            {
                GridUi.FilterRows(dataGridView_set, textBox_search, firstColumnOnly: false);
            }
            else
            {
                GridUi.FindNextMatch(dataGridView_set, textBox_search, searchState, firstColumnOnly: false);
            }
        }
	}
}
