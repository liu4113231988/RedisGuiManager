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
using StackExchange.Redis;

namespace RedisGuiManager
{
    public partial class HashValueControl : UserControl
    {
        private RedisValue selectField = RedisValue.Null;
        private string stringKeyName = string.Empty;
        private RedisClient redisClient = null;
        private StackExchange.Redis.IDatabase database;
        private readonly PageNavigator pages = new PageNavigator();

        // Server-side HSCAN cursors, one per page boundary, so deep paging does not restart the scan.
        private readonly CursorStack<string> hashCursors = new CursorStack<string>();

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

        public HashValueControl()
        {
            InitializeComponent();
            foreach (Control control in Controls)
                if ((control.Anchor & AnchorStyles.Bottom) != 0) { if (control.Height > 72) control.Height -= 36; else control.Top -= 36; }
            Controls.Add(pages);
            pages.CanNavigate = valueControl.ConfirmDiscard;
            pages.PageChanged += () => RefreshKeyAsync();
            valueControl.ProtectSelection(dataGridView_hash);
            GridUi.LimitCellText(dataGridView_hash);
            GridUi.AttachRowValueMenu(dataGridView_hash,
                () => dataGridView_hash.SelectedRows.Count > 0 ? dataGridView_hash.SelectedRows[0].Cells[1].Value?.ToString() ?? "" : "");

            if (Config.darkmode > 0)
            {
                Utils.DarkThemeControl(this);
            }
        }

        private void HashValueControl_Load(object sender, EventArgs e)
        {
            keyOperateControl.LoadValue.Click += async (s, e) => await RefreshKeyAsync();
            dataGridView_hash.SelectionChanged += dataGridView_hash_SelectionChanged;
            dataGridView_hash.SizeChanged += dataGridView_hash_SizeChanged;
            dataGridView_hash_SizeChanged(null, null);
        }

        private void dataGridView_hash_SizeChanged(object sender, EventArgs e)
        {
            dataGridView_hash.Columns[1].Width = dataGridView_hash.Width - dataGridView_hash.Columns[0].Width - 20;
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

            // HSCAN keeps its server-side cursor per page, so deep paging does not replay the scan
            // from the start, and the request runs off the UI thread.
            int pageIndex = pages.Offset / PageNavigator.PageSize;
            if (hashCursors.HasCursor(pageIndex) == false)
            {
                hashCursors.Reset("0");
            }

            var cursor = hashCursors.Get(pageIndex);
            var client = redisClient;
            var key = stringKeyName;
            var page = await Task.Run(() => client.HashScanPage(key, cursor, PageNavigator.PageSize));

            // The user may have switched keys or pages while the request was in flight.
            if (IsDisposed || Disposing || key != stringKeyName) return;

            bool more = page.NextCursor != "0";
            hashCursors.Set(pageIndex + 1, page.NextCursor);
            hashCursors.TrimTo(pageIndex + 2);

            pages.UpdatePage(more);
            var read = page.Entries.Take(PageNavigator.PageSize).ToArray();

            int size = 0;
            for (int i = 0; i < read.Length; i++)
            {
                size += (int)read[i].Name.Length();
                size += (int)read[i].Value.Length();
            }

            label_size.Text = UiText.SizePrefix + Utils.GetSizeDescription(size);
            label_length_val.Text = string.Format(UiText.RowsOnThisPage, read.Length);

            Utils.ControlDataGridViewRow(dataGridView_hash, read.Length);
            for (int i = 0; i < read.Length; i++)
            {
                dataGridView_hash.Rows[i].Cells[0].Value = read[i].Name;
                dataGridView_hash.Rows[i].Cells[1].Value = read[i].Value;
            }

            dataGridView_hash.Sort(dataGridView_hash.Columns[0], ListSortDirection.Ascending);
        }

        private void dataGridView_hash_SelectionChanged(object sender, EventArgs e)
        {
            if (dataGridView_hash.SelectedRows.Count > 0)
            {
                valueControl.SetValue(dataGridView_hash.SelectedRows[0].Cells[1].Value);

                if (dataGridView_hash.SelectedRows[0].Cells[1].Value != null)
                {
                    selectField = (RedisValue)dataGridView_hash.SelectedRows[0].Cells[0].Value;
                    textBox_field.Text = selectField.ToString();
                    button_save.Enabled = redisClient != null && !redisClient.Settings.read_only;
                }
            }
            else
            {
                textBox_field.Text = string.Empty;
                selectField = string.Empty;
                button_save.Enabled = false;
            }
        }

        public async Task SetNewKey(RedisClient redisClient, string key)
        {
            if (key != stringKeyName || redisClient != this.redisClient)
            {
                pages.Reset();
                // A different key invalidates every retained HSCAN cursor.
                hashCursors.Reset("0");
            }

            this.redisClient = redisClient;
            database = redisClient.Redis;
            this.stringKeyName = key;

            dataGridView_hash.SelectionChanged -= dataGridView_hash_SelectionChanged;

            keyOperateControl.SetRedisClient(redisClient, key);
            await RefreshKeyAsync();
            valueControl.SetValue(new RedisValue(""));
            textBox_field.Text = string.Empty;
            dataGridView_hash.ClearSelection();

            dataGridView_hash.SelectionChanged += dataGridView_hash_SelectionChanged;
        }

        private async void button_insert_row_Click(object sender, EventArgs e)
        {
            if (redisClient == null || !redisClient.CanWrite()) return;
            using (HashValueInsertForm form = new HashValueInsertForm(redisClient, this.stringKeyName, string.Empty, string.Empty, database: database))
            {
                form.ShowDialog();
                await RefreshKeyAsync();
            }
        }

        private async void button_delete_row_Click(object sender, EventArgs e)
        {
            if (redisClient == null || !redisClient.CanWrite()) return;
            if (dataGridView_hash.SelectedRows.Count > 0)
            {
                if (MessageBox.Show(string.Format(UiText.DeleteHashFieldPrompt, stringKeyName, selectField), UiText.Delete, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    bool removed = await database.HashDeleteAsync(stringKeyName, selectField);
                    if (removed)
                    {
                        await RefreshKeyAsync();
                    }
                    else
                    {
                        MessageBox.Show(UiText.DeleteRowFailed);
                    }
                }
            }
            else
            {
                MessageBox.Show(UiText.SelectRowFirst);
            }
        }

        private async void button_refresh_Click(object sender, EventArgs e)
        {
            if (dataGridView_hash.SelectedRows.Count > 0)
            {
                DataGridViewRow row = dataGridView_hash.SelectedRows[0];
                RedisValue field = (RedisValue)row.Cells[0].Value;
                var read = await database.HashGetAsync(stringKeyName, field);
                row.Cells[1].Value = read;
                valueControl.SetValue(read);
                textBox_field.Text = field.ToString();
            }
        }

        private void dataGridView_hash_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.C)
            {
                if (dataGridView_hash.SelectedRows.Count > 0)
                {
                    DataObject dataObj = dataGridView_hash.GetClipboardContent();
                    if (dataObj != null)
                    {
                        dataObj.SetText(dataGridView_hash.SelectedRows[0].Cells[0].Value.ToString());
                        Clipboard.SetDataObject(dataObj, true);
                    }
                }
            }
        }

        private void textBox_search_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            if (dataGridView_hash.Rows.Count <= 0)
            {
                return;
            }

            if (checkBox_search_grep.Checked)
            {
                GridUi.FilterRows(dataGridView_hash, textBox_search, checkBox_search_only_field.Checked);
            }
            else
            {
                GridUi.FindNextMatch(dataGridView_hash, textBox_search, searchState, checkBox_search_only_field.Checked);
            }
        }

        private async void button_save_Click(object sender, EventArgs e)
        {
            if (!valueControl.CanEditText || ValueControl.GetDisplayType() == ValueControl.DisplayType.Hex) { MessageBox.Show(UiText.ReadOnlyBinaryValue); return; }
            if (redisClient == null || !redisClient.CanWrite()) return;
            if (dataGridView_hash.SelectedRows.Count <= 0)
            {
                return;
            }

            if (valueControl.OriginalValue.IsNull) { MessageBox.Show(UiText.HashFieldMissing); return; }
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

            try
            {
                await database.ScriptEvaluateAsync(
                    "if redis.call('HGET',KEYS[1],ARGV[1]) ~= ARGV[2] then return redis.error_reply('Field changed; refresh before saving') end; return redis.call('HSET',KEYS[1],ARGV[1],ARGV[3])",
                    new StackExchange.Redis.RedisKey[] { stringKeyName },
                    new RedisValue[] { selectField, valueControl.OriginalValue, save_text });
            }
            catch (RedisException ex) { MessageBox.Show(ex.Message, "Save failed"); return; }
            valueControl.AcceptChanges();
            await RefreshKeyAsync();
        }
	}
}
