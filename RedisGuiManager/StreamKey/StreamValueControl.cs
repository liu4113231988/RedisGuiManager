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
using StackExchange.Redis;

namespace RedisGuiManager
{
    public partial class StreamValueControl : UserControl
    {
        private string stringKeyName = string.Empty;
        private RedisClient redisClient = null;
        private StackExchange.Redis.IDatabase database;
        private readonly PageNavigator pages = new PageNavigator();

        private int lastSearchIndex = -1;
        private string searchCondition = string.Empty;

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
            pages.PageChanged += RefreshKey;
            valueControl.ProtectSelection(dataGridView_stream);

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

        private void LoadValue_Click(object sender, EventArgs e)
        {
            RefreshKey();
        }

        private void RefreshKey()
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

            if (!OperationDialog.TryRun(this, "Load page", (token, progress) => { var rows = pages.Read(redisClient.ScanStream(database, stringKeyName), token); token.ThrowIfCancellationRequested(); return rows; }, out var batch)) return;
            pages.UpdatePage(batch.Length > PageNavigator.PageSize);
            var entries = batch.Take(PageNavigator.PageSize).ToArray();

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

            label_size.Text = "Size : " + Utils.GetSizeDescription(size);
            label_length_val.Text = $"{entries.Length} on this page";

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

        public void SetNewKey(RedisClient redisClient, string key)
        {
            if (key != stringKeyName || redisClient != this.redisClient) pages.Reset();
            this.redisClient = redisClient;
            database = redisClient.Redis;
            this.stringKeyName = key;

            dataGridView_stream.SelectionChanged -= dataGridView_stream_SelectionChanged;

            keyOperateControl.SetRedisClient(redisClient, key);
            RefreshKey();
            valueControl.SetValue(string.Empty);
            dataGridView_stream.ClearSelection();

            dataGridView_stream.SelectionChanged += dataGridView_stream_SelectionChanged;
        }

        private void button_delete_row_Click(object sender, EventArgs e)
        {
            if (redisClient == null || !redisClient.CanWrite()) return;
            if (dataGridView_stream.SelectedRows.Count <= 0)
            {
                MessageBox.Show("Please select a row");
                return;
            }

            string entryId = dataGridView_stream.SelectedRows[0].Cells[0].Value.ToString();

            if (MessageBox.Show($"Delete entry [{entryId}] from stream [{stringKeyName}]?", "Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                var deleted = database.StreamDelete(stringKeyName, new RedisValue[] { entryId });
                if (deleted > 0)
                {
                    RefreshKey();
                }
                else
                {
                    MessageBox.Show("Delete entry failed");
                }
            }
        }

        private void button_insert_row_Click(object sender, EventArgs e)
        {
            if (redisClient == null || !redisClient.CanWrite()) return;
            using StreamValueInsertForm form = new StreamValueInsertForm(redisClient, stringKeyName, database: database);
            form.ShowDialog();
            RefreshKey();
        }

        private void button_refresh_Click(object sender, EventArgs e)
        {
            RefreshKey();
        }

        private void dataGridView_stream_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.Value != null)
            {
                if (e.Value.ToString().Length > 10000)
                {
                    e.Value = e.Value.ToString().Substring(0, 10000);
                }
            }
        }

        private void dataGridView_stream_CellMouseUp(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.ColumnIndex == -1 || e.RowIndex == -1)
                return;

            if (e.Button == MouseButtons.Right)
            {
                dataGridView_stream.Rows[e.RowIndex].Selected = true;

                Rectangle cellRect = dataGridView_stream.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, true);
                Point ptLoc = new Point(cellRect.Left + e.Location.X, cellRect.Top + e.Location.Y);

                show_context_menu(dataGridView_stream, ptLoc);
            }
        }

        private void show_context_menu(Control c, Point p)
        {
            ContextMenuStrip contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Json viewer", null, new EventHandler(this.CM_json_viewer));
            contextMenu.Show(c, p);
        }

        private void CM_json_viewer(object o, EventArgs e)
        {
            if (dataGridView_stream.SelectedRows.Count <= 0) return;

            FormJsonViewer fjv = new FormJsonViewer();
            fjv.Show();
            fjv.JsonText = dataGridView_stream.SelectedRows[0].Cells[1].Value.ToString();
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

            string search_text = textBox_search.Text;
            if (string.IsNullOrEmpty(search_text))
            {
                return;
            }

            if (string.IsNullOrEmpty(searchCondition))
            {
                searchCondition = search_text;
            }
            else
            {
                if (searchCondition != search_text)
                {
                    lastSearchIndex = -1;
                    searchCondition = search_text;
                }
            }

            for (int i = lastSearchIndex + 1; i < dataGridView_stream.Rows.Count; ++i)
            {
                foreach (DataGridViewCell cell in dataGridView_stream.Rows[i].Cells)
                {
                    if (cell.Value != null && cell.Value.ToString().ToLower().Contains(search_text.ToLower()))
                    {
                        dataGridView_stream.Rows[i].Selected = true;
                        dataGridView_stream.CurrentCell = dataGridView_stream.Rows[i].Cells[0];
                        lastSearchIndex = i;
                        return;
                    }
                }
            }

            for (int i = 0; i <= lastSearchIndex && i < dataGridView_stream.Rows.Count; ++i)
            {
                foreach (DataGridViewCell cell in dataGridView_stream.Rows[i].Cells)
                {
                    if (cell.Value != null && cell.Value.ToString().ToLower().Contains(search_text.ToLower()))
                    {
                        dataGridView_stream.Rows[i].Selected = true;
                        dataGridView_stream.CurrentCell = dataGridView_stream.Rows[i].Cells[0];
                        lastSearchIndex = i;
                        return;
                    }
                }
            }
        }
    }
}
