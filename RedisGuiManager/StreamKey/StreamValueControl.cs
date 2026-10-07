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
    public partial class StreamValueControl : UserControl
    {
        private string stringKeyName = string.Empty;
        private RedisClient redisClient = null;
        private StackExchange.Redis.IDatabase database;
        private readonly PageNavigator pages = new PageNavigator();

        // Last entry ID of each visited page; resuming from it makes paging O(page) instead of
        // re-reading the stream from the beginning.
        private readonly CursorStack<RedisValue?> streamCursors = new CursorStack<RedisValue?>();

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

        public StreamValueControl()
        {
            InitializeComponent();
            foreach (Control control in Controls)
                if ((control.Anchor & AnchorStyles.Bottom) != 0) { if (control.Height > 72) control.Height -= 36; else control.Top -= 36; }
            Controls.Add(pages);
            pages.CanNavigate = valueControl.ConfirmDiscard;
            pages.PageChanged += () => RefreshKeyAsync();
            valueControl.ProtectSelection(dataGridView_stream);
            GridUi.LimitCellText(dataGridView_stream);
            GridUi.AttachRowValueMenu(dataGridView_stream,
                () => dataGridView_stream.SelectedRows.Count > 0 ? dataGridView_stream.SelectedRows[0].Cells[1].Value?.ToString() ?? "" : "");

            // Consumer groups, pending entries and stream internals live in their own window.
            var groupInfo = new Button
            {
                Text = UiText.StreamGroupsButton,
                Width = 120,
                Height = 26,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            groupInfo.Click += (s, e) => OpenStreamInfo();
            groupInfo.Location = new Point(Width - groupInfo.Width - 12, 8);
            Resize += (s, e) => groupInfo.Left = ClientSize.Width - groupInfo.Width - 12;
            Controls.Add(groupInfo);
            groupInfo.BringToFront();

            if (Config.darkmode > 0)
            {
                Utils.DarkThemeControl(this);
            }
        }

        private void StreamValueControl_Load(object sender, EventArgs e)
        {
            keyOperateControl.LoadValue.Click += LoadValue_Click;
            dataGridView_stream.SelectionChanged += dataGridView_stream_SelectionChanged;
            dataGridView_stream.SizeChanged += dataGridView_stream_SizeChanged;
            dataGridView_stream_SizeChanged(null, null);
        }

        private void dataGridView_stream_SizeChanged(object sender, EventArgs e)
        {
            dataGridView_stream.Columns[1].Width = dataGridView_stream.Width - 200;
        }

        private async void LoadValue_Click(object sender, EventArgs e)
        {
            await RefreshKeyAsync();
        }

        private void OpenStreamInfo()
        {
            if (redisClient == null || database == null)
            {
                MessageBox.Show(this, UiText.RedisConnectionError, UiText.StreamTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!valueControl.ConfirmDiscard()) return;

            FormStreamInfo form = new FormStreamInfo(redisClient, stringKeyName, database);
            form.Show(this);
        }

        private async Task RefreshKeyAsync()
        {
            if (!valueControl.ConfirmDiscard()) return;
            if (redisClient == null)
            {
                MessageBox.Show(UiText.RedisConnectionError);
                return;
            }

            if (database == null)
            {
                MessageBox.Show(UiText.RedisConnectionError);
                return;
            }

            // Entries resume from the last ID of the previous page, so paging no longer re-reads
            // the whole stream; the request itself runs off the UI thread.
            int pageIndex = pages.Offset / PageNavigator.PageSize;
            if (streamCursors.HasCursor(pageIndex) == false)
            {
                streamCursors.Reset(null);
            }

            var afterId = streamCursors.Get(pageIndex);
            var db = database;
            var client = redisClient;
            var key = stringKeyName;
            var page = await Task.Run(() => client.StreamPageAfter(db, key, afterId, PageNavigator.PageSize));

            // The user may have switched keys while the request was in flight.
            if (IsDisposed || Disposing || key != stringKeyName) return;

            // Remember the boundary so the next page resumes here instead of re-reading the stream.
            streamCursors.Set(pageIndex + 1, page.Entries.Count > 0 ? page.Entries[page.Entries.Count - 1].Id : afterId);
            streamCursors.TrimTo(pageIndex + 2);

            pages.UpdatePage(page.HasMore);
            var entries = page.Entries.ToArray();

            int size = 0;
            for (int i = 0; i < entries.Length; i++)
            {
                size += Encoding.UTF8.GetBytes(entries[i].Id.ToString()).Length;
                foreach (var pair in entries[i].Values)
                {
                    size += Encoding.UTF8.GetBytes(pair.Name.ToString()).Length;
                    size += Encoding.UTF8.GetBytes(pair.Value.ToString()).Length;
                }
            }

            label_size.Text = UiText.SizePrefix + Utils.GetSizeDescription(size);
            label_length_val.Text = string.Format(UiText.RowsOnThisPage, entries.Length);

            Utils.ControlDataGridViewRow(dataGridView_stream, entries.Length);
            for (int i = 0; i < entries.Length; i++)
            {
                dataGridView_stream.Rows[i].Cells[0].Value = entries[i].Id.ToString();

                var fields = new List<string>();
                foreach (var pair in entries[i].Values)
                {
                    fields.Add($"{pair.Name}: {pair.Value}");
                }
                dataGridView_stream.Rows[i].Cells[1].Value = string.Join(", ", fields);
            }
        }

        private void dataGridView_stream_SelectionChanged(object sender, EventArgs e)
        {
            if (dataGridView_stream.SelectedRows.Count > 0)
            {
                if (dataGridView_stream.SelectedRows[0].Cells[1].Value != null)
                {
                    string value = dataGridView_stream.SelectedRows[0].Cells[1].Value.ToString();
                    valueControl.SetValue(value);
                }
            }
            else
            {
                valueControl.SetValue(string.Empty);
            }
        }

        public async Task SetNewKey(RedisClient redisClient, string key)
        {
            if (key != stringKeyName || redisClient != this.redisClient)
            {
                pages.Reset();
                // A different key invalidates every retained stream boundary.
                streamCursors.Reset(null);
            }

            this.redisClient = redisClient;
            database = redisClient.Redis;
            this.stringKeyName = key;

            dataGridView_stream.SelectionChanged -= dataGridView_stream_SelectionChanged;

            keyOperateControl.SetRedisClient(redisClient, key);
            await RefreshKeyAsync();
            valueControl.SetValue(string.Empty);
            dataGridView_stream.ClearSelection();

            dataGridView_stream.SelectionChanged += dataGridView_stream_SelectionChanged;
        }

        private async void button_delete_row_Click(object sender, EventArgs e)
        {
            if (redisClient == null || !redisClient.CanWrite()) return;
            if (dataGridView_stream.SelectedRows.Count <= 0)
            {
                MessageBox.Show(UiText.SelectRowFirst);
                return;
            }

            string entryId = dataGridView_stream.SelectedRows[0].Cells[0].Value.ToString();

            if (MessageBox.Show(string.Format(UiText.StreamDeletePrompt, entryId, stringKeyName), UiText.Delete, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                var deleted = await database.StreamDeleteAsync(stringKeyName, new RedisValue[] { entryId });
                if (deleted > 0)
                {
                    await RefreshKeyAsync();
                }
                else
                {
                    MessageBox.Show(UiText.StreamDeleteFailed);
                }
            }
        }

        private async void button_insert_row_Click(object sender, EventArgs e)
        {
            if (redisClient == null || !redisClient.CanWrite()) return;
            using StreamValueInsertForm form = new StreamValueInsertForm(redisClient, stringKeyName, database: database);
            form.ShowDialog();
            await RefreshKeyAsync();
        }

        private async void button_refresh_Click(object sender, EventArgs e)
        {
            await RefreshKeyAsync();
        }

        private void textBox_search_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            if (dataGridView_stream.Rows.Count <= 0)
            {
                return;
            }

            GridUi.FindNextMatch(dataGridView_stream, textBox_search, searchState, firstColumnOnly: false);
        }
    }
}
