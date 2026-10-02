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
    public partial class HashValueControl : UserControl
    {
        private RedisValue selectField = RedisValue.Null;
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

        public HashValueControl()
        {
            InitializeComponent();
            foreach (Control control in Controls)
                if ((control.Anchor & AnchorStyles.Bottom) != 0) { if (control.Height > 72) control.Height -= 36; else control.Top -= 36; }
            Controls.Add(pages);
            pages.CanNavigate = valueControl.ConfirmDiscard;
            pages.PageChanged += RefreshKey;
            valueControl.ProtectSelection(dataGridView_hash);

            if (Config.darkmode > 0)
            {
                Utils.DarkThemeControl(this);
            }
        }

        private void HashValueControl_Load(object sender, EventArgs e)
        {
            keyOperateControl.LoadValue.Click += LoadValue_Click;
            dataGridView_hash.SelectionChanged += dataGridView_hash_SelectionChanged;
            dataGridView_hash.SizeChanged += dataGridView_hash_SizeChanged;
            dataGridView_hash_SizeChanged(null, null);
        }

        private void dataGridView_hash_SizeChanged(object sender, EventArgs e)
        {
            dataGridView_hash.Columns[1].Width = dataGridView_hash.Width - dataGridView_hash.Columns[0].Width - 20;
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

            if (!OperationDialog.TryRun(this, "Load page", (token, progress) => { var rows = pages.Read(database.HashScan(stringKeyName, pageSize: PageNavigator.PageSize), token); token.ThrowIfCancellationRequested(); return rows; }, out var batch)) return;
            pages.UpdatePage(batch.Length > PageNavigator.PageSize);
            var read = batch.Take(PageNavigator.PageSize).ToArray();

            int size = 0;
            for (int i = 0; i < read.Length; i++)
            {
                size += (int)read[i].Name.Length();
                size += (int)read[i].Value.Length();
            }

            label_size.Text = "Size : " + Utils.GetSizeDescription(size);
            label_length_val.Text = $"{read.Length} on this page";

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

        public void SetNewKey(RedisClient redisClient, string key)
        {
            if (key != stringKeyName || redisClient != this.redisClient) pages.Reset();
            this.redisClient = redisClient;
            database = redisClient.Redis;
            this.stringKeyName = key;

            dataGridView_hash.SelectionChanged -= dataGridView_hash_SelectionChanged;

            keyOperateControl.SetRedisClient(redisClient, key);
            RefreshKey();
            valueControl.SetValue(new RedisValue(""));
            textBox_field.Text = string.Empty;
            dataGridView_hash.ClearSelection();

            dataGridView_hash.SelectionChanged += dataGridView_hash_SelectionChanged;
        }

        private void button_insert_row_Click(object sender, EventArgs e)
        {
            if (redisClient == null || !redisClient.CanWrite()) return;
            using (HashValueInsertForm form = new HashValueInsertForm(redisClient, this.stringKeyName, string.Empty, string.Empty, database: database))
            {
                form.ShowDialog();
                RefreshKey();
            }
        }

        private void button_delete_row_Click(object sender, EventArgs e)
        {
            if (redisClient == null || !redisClient.CanWrite()) return;
            if (dataGridView_hash.SelectedRows.Count > 0)
            {
                if (MessageBox.Show(string.Format("Delete Key:{0} Hash key:{1}", stringKeyName, selectField), "Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    if (database.HashDelete(stringKeyName, selectField))
                    {
                        RefreshKey();
                    }
                    else
                    {
                        MessageBox.Show("Delete row fail");
                    }
                }
            }
            else
            {
                MessageBox.Show("Please select row");
            }
        }

        private void button_refresh_Click(object sender, EventArgs e)
        {
            if (dataGridView_hash.SelectedRows.Count > 0)
            {
                DataGridViewRow row = dataGridView_hash.SelectedRows[0];
                RedisValue field = (RedisValue)row.Cells[0].Value;
                var read = database.HashGet(stringKeyName, field);
                row.Cells[1].Value = read;
                valueControl.SetValue(read);
                textBox_field.Text = field.ToString();
            }
        }

        private void dataGridView_hash_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.Value != null)
            {
                if (e.Value.ToString().Length > 10000)
                {
                    e.Value = e.Value.ToString().Substring(0, 10000);
                }
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

        private void dataGridView_hash_CellMouseUp(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.ColumnIndex == -1 || e.RowIndex == -1)
                return;

            if (e.Button == MouseButtons.Right)
            {
                dataGridView_hash.Rows[e.RowIndex].Selected = true;

                Rectangle cellRect = dataGridView_hash.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, true);
                Point ptLoc = new Point(cellRect.Left + e.Location.X, cellRect.Top + e.Location.Y);

                show_context_menu(dataGridView_hash, ptLoc);
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
            FormJsonViewer fjv = new FormJsonViewer();
            fjv.Show();
            fjv.JsonText = dataGridView_hash.SelectedRows[0].Cells[1].Value.ToString();
        }

        private void search_field_loop()
        {
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

            foreach (DataGridViewRow row in dataGridView_hash.Rows)
            {
                row.Visible = true;
            }

            for (int i = lastSearchIndex + 1; i < dataGridView_hash.Rows.Count; ++i)
            {
                if (checkBox_search_only_field.Checked)
                {
                    if (dataGridView_hash.Rows[i].Cells[0].Value.ToString().ToLower().Contains(search_text.ToLower()))
                    {
                        dataGridView_hash.Rows[i].Selected = true;
                        dataGridView_hash.CurrentCell = dataGridView_hash.Rows[i].Cells[0];
                        lastSearchIndex = i;

                        return;
                    }
                }
                else
                {
                    foreach (DataGridViewCell cell in dataGridView_hash.Rows[i].Cells)
                    {
                        if (cell.Value.ToString().ToLower().Contains(search_text.ToLower()))
                        {
                            dataGridView_hash.Rows[i].Selected = true;
                            dataGridView_hash.CurrentCell = dataGridView_hash.Rows[i].Cells[0];
                            lastSearchIndex = i;

                            return;
                        }
                    }
                }
            }

            // 못 찾으면 처음 부터 한번더 돌아 봄
            for (int i = 0; i < dataGridView_hash.Rows.Count; ++i)
            {
                if (checkBox_search_only_field.Checked)
                {
                    if (dataGridView_hash.Rows[i].Cells[0].Value.ToString().ToLower().Contains(search_text.ToLower()))
                    {
                        dataGridView_hash.Rows[i].Selected = true;
                        dataGridView_hash.CurrentCell = dataGridView_hash.Rows[i].Cells[0];

                        if (lastSearchIndex == i)
                        {
                            dataGridView_hash.CurrentCell = dataGridView_hash.Rows[i].Cells[1];
                            dataGridView_hash.CurrentCell = dataGridView_hash.Rows[i].Cells[0];
                        }

                        lastSearchIndex = i;

                        return;
                    }
                }
                else
                {
                    foreach (DataGridViewCell cell in dataGridView_hash.Rows[i].Cells)
                    {
                        if (cell.Value.ToString().ToLower().Contains(search_text.ToLower()))
                        {
                            dataGridView_hash.Rows[i].Selected = true;
                            dataGridView_hash.CurrentCell = dataGridView_hash.Rows[i].Cells[0];

                            if (lastSearchIndex == i)
                            {
                                dataGridView_hash.CurrentCell = dataGridView_hash.Rows[i].Cells[1];
                                dataGridView_hash.CurrentCell = dataGridView_hash.Rows[i].Cells[0];
                            }

                            lastSearchIndex = i;

                            return;
                        }
                    }
                }
            }
        }

        private void search_field_grep()
        {
            string search_text = textBox_search.Text;

            if (string.IsNullOrEmpty(search_text))
            {
                foreach (DataGridViewRow row in dataGridView_hash.Rows)
                {
                    row.Visible = true;
                }
            }
            else
            {
                foreach (DataGridViewRow row in dataGridView_hash.Rows)
                {
                    if (checkBox_search_only_field.Checked)
                    {
                        if (row.Cells[0].Value.ToString().ToLower().Contains(search_text.ToLower()))
                        {
                            row.Visible = true;
                        }
                        else
                        {
                            row.Visible = false;
                        }
                    }
                    else
                    {
                        foreach (DataGridViewCell cell in row.Cells)
                        {
                            if (cell.Value.ToString().ToLower().Contains(search_text.ToLower()))
                            {
                                row.Visible = true;
                                break;
                            }
                            else
                            {
                                row.Visible = false;
                            }
                        }
                    }
                }

                dataGridView_hash.ClearSelection();
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
                search_field_grep();
            }
            else
            {
                search_field_loop();
            }
        }

        private void button_save_Click(object sender, EventArgs e)
        {
            if (!valueControl.CanEditText || ValueControl.GetDisplayType() == ValueControl.DisplayType.Hex) { MessageBox.Show("Binary/Hex values are read-only"); return; }
            if (redisClient == null || !redisClient.CanWrite()) return;
            if (dataGridView_hash.SelectedRows.Count <= 0)
            {
                return;
            }

            if (valueControl.OriginalValue.IsNull) { MessageBox.Show("Field is missing; refresh first"); return; }
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
                database.ScriptEvaluate(
                    "if redis.call('HGET',KEYS[1],ARGV[1]) ~= ARGV[2] then return redis.error_reply('Field changed; refresh before saving') end; return redis.call('HSET',KEYS[1],ARGV[1],ARGV[3])",
                    new StackExchange.Redis.RedisKey[] { stringKeyName },
                    new RedisValue[] { selectField, valueControl.OriginalValue, save_text });
            }
            catch (RedisException ex) { MessageBox.Show(ex.Message, "Save failed"); return; }
            valueControl.AcceptChanges();
            RefreshKey();
        }
	}
}
