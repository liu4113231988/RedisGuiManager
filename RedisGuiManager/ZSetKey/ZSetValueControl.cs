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
    public partial class ZSetValueControl : UserControl
    {
        private DataGridViewRow selectRow = null;
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

        public ZSetValueControl()
        {
            InitializeComponent();
            foreach (Control control in Controls)
                if ((control.Anchor & AnchorStyles.Bottom) != 0) { if (control.Height > 72) control.Height -= 36; else control.Top -= 36; }
            Controls.Add(pages);
            pages.CanNavigate = valueControl.ConfirmDiscard;
            pages.PageChanged += RefreshKey;
            valueControl.ProtectSelection(dataGridView_zset);
            valueControl.HasOtherChanges = () => selectRow != null && textBox_score.Text != selectRow.Cells[2].Value?.ToString();
            valueControl.DiscardOtherChanges = () => textBox_score.Text = selectRow?.Cells[2].Value?.ToString() ?? "";

            if (Config.darkmode > 0)
            {
                Utils.DarkThemeControl(this);
            }
        }

        private void ZSetValueControl_Load(object sender, EventArgs e)
        {
            keyOperateControl.LoadValue.Click += LoadValue_Click;
            dataGridView_zset.SelectionChanged += dataGridView_zset_SelectionChanged;
            dataGridView_zset.SizeChanged += dataGridView_zset_SizeChanged;
            dataGridView_zset_SizeChanged(null, null);
        }

        private void dataGridView_zset_SizeChanged(object sender, EventArgs e)
        {
            dataGridView_zset.Columns[1].Width = dataGridView_zset.Width - 254;
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

            if (!OperationDialog.TryRun(this, "Load page", (token, progress) => { var rows = database.SortedSetRangeByRankWithScores(stringKeyName, pages.Offset, pages.Offset + PageNavigator.PageSize); token.ThrowIfCancellationRequested(); return rows; }, out var batch)) return;
            pages.UpdatePage(batch.Length > PageNavigator.PageSize);
            var read = batch.Take(PageNavigator.PageSize).ToArray();

            int size = 0;
            for (int i = 0; i < read.Length; i++)
            {
                size += Encoding.UTF8.GetBytes(read[i].Element).Length;
            }

            label_size.Text = "Size : " + Utils.GetSizeDescription(size);
            label_length_val.Text = $"{read.Length} on this page";

            Utils.ControlDataGridViewRow(dataGridView_zset, read.Length);
            for (int i = 0; i < read.Length; i++)
            {
                dataGridView_zset.Rows[i].Cells[0].Value = pages.Offset + i;
                dataGridView_zset.Rows[i].Cells[1].Value = read[i].Element;
                dataGridView_zset.Rows[i].Cells[2].Value = Convert.ToDouble(read[i].Score).ToString();
            }
        }

        private void dataGridView_zset_SelectionChanged(object sender, EventArgs e)
        {
            if (dataGridView_zset.SelectedRows.Count > 0)
            {
                if (dataGridView_zset.SelectedRows[0].Cells[1].Value != null)
                {
                    selectRow = dataGridView_zset.SelectedRows[0];
                    object value = selectRow.Cells[1].Value;
                    valueControl.SetValue(value);
                    button_save.Enabled = redisClient != null && !redisClient.Settings.read_only;
                    textBox_score.Text = dataGridView_zset.SelectedRows[0].Cells[2].Value.ToString();
                }
            }
            else
            {
                valueControl.SetValue(string.Empty);
                selectRow = null;
                button_save.Enabled = false;
                textBox_score.Text = string.Empty;
            }
        }

        public void SetNewKey(RedisClient redisClient, string key)
        {
            if (key != stringKeyName || redisClient != this.redisClient) pages.Reset();
            if (key != this.stringKeyName)
            {
                valueControl.SetValue(string.Empty);
                textBox_score.Text = string.Empty;
            }

            this.redisClient = redisClient;
            database = redisClient.Redis;
            stringKeyName = key;

            dataGridView_zset.SelectionChanged -= dataGridView_zset_SelectionChanged;

            keyOperateControl.SetRedisClient(redisClient, key);
            RefreshKey();

            dataGridView_zset.SelectionChanged += dataGridView_zset_SelectionChanged;
        }

        private void button_insert_row_Click(object sender, EventArgs e)
        {
            if (redisClient == null || !redisClient.CanWrite()) return;
            using (ZSetValueInsertForm form = new ZSetValueInsertForm(redisClient, stringKeyName, database: database))
            {
                form.ShowDialog();
                RefreshKey();
            }
        }

        private void button_delete_row_Click(object sender, EventArgs e)
        {
            if (redisClient == null || !redisClient.CanWrite()) return;
            if (dataGridView_zset.SelectedRows.Count > 0)
            {
                if (dataGridView_zset.SelectedRows[0].Cells[1].Value != null)
                {
                    if (MessageBox.Show(string.Format("Delete Key:{0}", stringKeyName), "Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    {
                        if (database.SortedSetRemove(stringKeyName, (StackExchange.Redis.RedisValue)dataGridView_zset.SelectedRows[0].Cells[1].Value))
                        {
                            MessageBox.Show("Delete row success");
                            RefreshKey();
                        }
                        else
                        {
                            MessageBox.Show("Delete row fail");
                        }
                    }
                }
            }
        }

        private void button_refresh_Click(object sender, EventArgs e)
        {
            RefreshKey();
        }

        private void dataGridView_zset_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.Value != null)
            {
                if (e.Value.ToString().Length > 10000)
                {
                    e.Value = e.Value.ToString().Substring(0, 10000);
                }
            }
        }

        private void dataGridView_zset_CellMouseUp(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.ColumnIndex == -1 || e.RowIndex == -1)
                return;

            if (e.Button == MouseButtons.Right)
            {
                dataGridView_zset.Rows[e.RowIndex].Selected = true;

                Rectangle cellRect = dataGridView_zset.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, true);
                Point ptLoc = new Point(cellRect.Left + e.Location.X, cellRect.Top + e.Location.Y);

                show_context_menu(dataGridView_zset, ptLoc);
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
            fjv.JsonText = dataGridView_zset.SelectedRows[0].Cells[1].Value.ToString();
        }

		private void button_save_Click(object sender, EventArgs e)
		{
            if (!valueControl.CanEditText || ValueControl.GetDisplayType() == ValueControl.DisplayType.Hex) { MessageBox.Show("Binary/Hex values are read-only"); return; }
            if (redisClient == null || !redisClient.CanWrite()) return;
            try
            {
                if (dataGridView_zset.SelectedRows.Count <= 0)
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

                double score = 0;
                if (double.TryParse(textBox_score.Text, out score) == false || double.IsNaN(score) || double.IsInfinity(score))
                {
                    MessageBox.Show("input score as number");
                    return;
                }

                database.ScriptEvaluate(
                    "if tonumber(redis.call('ZSCORE',KEYS[1],ARGV[1])) ~= tonumber(ARGV[4]) then return redis.error_reply('Member changed; refresh first') end; redis.call('ZADD',KEYS[1],ARGV[3],ARGV[2]); if ARGV[1] ~= ARGV[2] then redis.call('ZREM',KEYS[1],ARGV[1]) end; return 1",
                    new StackExchange.Redis.RedisKey[] { stringKeyName },
                    new StackExchange.Redis.RedisValue[] { selectRow.Cells[1].Value.ToString(), save_text, score, double.Parse(selectRow.Cells[2].Value.ToString()) });

                selectRow.Cells[2].Value = score.ToString();
                textBox_score.Text = score.ToString();
                valueControl.AcceptChanges();
            RefreshKey();

            }
            catch (StackExchange.Redis.RedisException ex)
            {
                MessageBox.Show(ex.Message, "Operation failed; refresh before retrying");
            }
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

            foreach (DataGridViewRow row in dataGridView_zset.Rows)
            {
                row.Visible = true;
            }

            for (int i = lastSearchIndex + 1; i < dataGridView_zset.Rows.Count; ++i)
            {
                foreach (DataGridViewCell cell in dataGridView_zset.Rows[i].Cells)
                {
                    if (cell.Value.ToString().ToLower().Contains(search_text.ToLower()))
                    {
                        dataGridView_zset.Rows[i].Selected = true;
                        dataGridView_zset.CurrentCell = dataGridView_zset.Rows[i].Cells[0];
                        lastSearchIndex = i;

                        return;
                    }
                }
            }

            // 못 찾으면 처음 부터 한번더 돌아 봄
            for (int i = 0; i < dataGridView_zset.Rows.Count; ++i)
            {
                foreach (DataGridViewCell cell in dataGridView_zset.Rows[i].Cells)
                {
                    if (cell.Value.ToString().ToLower().Contains(search_text.ToLower()))
                    {
                        dataGridView_zset.Rows[i].Selected = true;
                        dataGridView_zset.CurrentCell = dataGridView_zset.Rows[i].Cells[0];

                        if (lastSearchIndex == i)
                        {
                            dataGridView_zset.CurrentCell = dataGridView_zset.Rows[i].Cells[1];
                            dataGridView_zset.CurrentCell = dataGridView_zset.Rows[i].Cells[0];
                        }

                        lastSearchIndex = i;

                        return;
                    }
                }
            }
        }

        private void search_field_grep()
        {
            string search_text = textBox_search.Text;

            if (string.IsNullOrEmpty(search_text))
            {
                foreach (DataGridViewRow row in dataGridView_zset.Rows)
                {
                    row.Visible = true;
                }
            }
            else
            {
                foreach (DataGridViewRow row in dataGridView_zset.Rows)
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

                dataGridView_zset.ClearSelection();
            }
        }

        private void textBox_search_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            if (dataGridView_zset.Rows.Count <= 0)
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
	}
}
