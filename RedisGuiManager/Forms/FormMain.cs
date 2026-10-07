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
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StackExchange.Redis;
using System.Net;

namespace RedisGuiManager
{
    public partial class FormMain : Form
    {
        private List<RedisGroup> redis_group = new List<RedisGroup>();
        private List<RedisSettings> redis_settings = new List<RedisSettings>();
        private UserControl userControl = null;
        private bool is_shown = false;
        private const string ConnectionsDir = "connections";
        private const string DefaultConnectionsFile = "connections/connections.json";
        private Dictionary<RedisSettings, string> _settingFileMap = new Dictionary<RedisSettings, string>();
        private Dictionary<RedisGroup, string> _groupFileMap = new Dictionary<RedisGroup, string>();
        private HashSet<string> _knownFiles = new HashSet<string>();
        private HashSet<string> _failedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Application.DoEvents() runs a nested message loop and is therefore re-entrant: it can
        // dispatch another selection change (or a tree edit) while the first one is still loading,
        // which corrupts the node being populated. This wrapper keeps the loading icon responsive
        // but never lets two pumps overlap.
        private bool pumpingUi;

        private void PumpUi()
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            if (pumpingUi) return;

            pumpingUi = true;
            try
            {
                Application.DoEvents();
            }
            catch (Exception)
            {
                // Never let a nested-pump failure break node loading.
            }
            finally
            {
                pumpingUi = false;
            }
        }

        // Application-level shortcuts. ProcessCmdKey is consulted before the focused control, so
        // this needs no KeyPreview; the handlers behind the menu items already no-op when the
        // selected node is not the right kind.

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F5:
                    // Let text inputs keep F5 for their own use.
                    if (IsEditingText(ActiveControl)) break;
                    reload_keys_ToolStripMenuItem.PerformClick();
                    if (treeView_server.SelectedNode?.Tag is RedisClient)
                    {
                        reload_server_ToolStripMenuItem.PerformClick();
                    }
                    return true;

                case Keys.Control | Keys.R:
                    reload_server_ToolStripMenuItem.PerformClick();
                    return true;

                case Keys.Control | Keys.F:
                    if (IsEditingText(ActiveControl)) break;
                    filter_key_ToolStripMenuItem.PerformClick();
                    return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private static bool IsEditingText(Control control)
        {
            return control is TextBoxBase || control is SyntaxRichTextBox;
        }

        public FormMain()
        {
            InitializeComponent();

            contextMenuStrip_redis.Items.Remove(remove_keys_from_registered_dbs_ToolStripMenuItem);

            treeView_server.BeforeSelect += (s, e) => { e.Cancel = !ValueControl.ConfirmAll(panel1); };
            Config.Load();

            if (Config.darkmode > 0)
            {
                Utils.DarkThemeForm(this);
            }
            // 同步暗黑模式复选框到左侧面板（替代原 FormLoadServerList 中的设置）
            try
            {
                checkBox_darkmode.Checked = Config.darkmode > 0;
            }
            catch { }
        }

        private void checkBox_darkmode_CheckedChanged(object sender, EventArgs e)
        {
            Config.darkmode = checkBox_darkmode.Checked ? 1 : 0;
            Config.Save();
        }

        private void FormMain_Load(object sender, EventArgs e)
        {
            CreateRedisShowTagControl<StartControl>();

            LoadAllConnections();
        }

        private void FormMain_Shown(object sender, EventArgs e)
        {
            int x = Config.mainform_pos_x;
            int y = Config.mainform_pos_y;
            int wt = Screen.FromPoint(this.Location).WorkingArea.Top;
            int wr = Screen.FromPoint(this.Location).WorkingArea.Right;
            int wb = Screen.FromPoint(this.Location).WorkingArea.Bottom;
            int wl = Screen.FromPoint(this.Location).WorkingArea.Left;

            foreach (Screen screen in Screen.AllScreens)
            {
                wl = screen.WorkingArea.Left < wl ? screen.WorkingArea.Left : wl;
                wr = screen.WorkingArea.Right > wr ? screen.WorkingArea.Right : wr;
                wt = screen.WorkingArea.Top < wt ? screen.WorkingArea.Top : wt;
                wb = screen.WorkingArea.Bottom > wb ? screen.WorkingArea.Bottom : wb;
            }

            if (x < wl)
            {
                x = wl;
            }
            else if (x > wr - this.Width)
            {
                x = wr - this.Width;
            }

            if (y < wt)
            {
                y = wt;
            }
            else if (y > wb - this.Height)
            {
                y = wb - this.Height;
            }

            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(x, y);
            this.Width = Config.mainform_width;
            this.Height = Config.mainform_height;
            if (Config.mainform_is_maximized > 0)
            {
                this.WindowState = FormWindowState.Maximized;
            }
            is_shown = true;

            treeView_server.SelectedNode = null;

            treeView_server.AfterSelect += treeView_server_AfterSelect;
            treeView_server.MouseDown += treeView_server_MouseDown;
            treeView_server.MouseUp += treeView_server_MouseUp;
        }

        private void FormMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!ValueControl.ConfirmAll(panel1)) { e.Cancel = true; return; }
            Config.mainform_is_maximized = (WindowState == FormWindowState.Maximized) ? 1 : 0;

            if (WindowState == FormWindowState.Normal)
            {
                Config.mainform_pos_x = Location.X;
                Config.mainform_pos_y = Location.Y;
                Config.mainform_width = Width;
                Config.mainform_height = Height;
            }

            Config.Save();

            ClearAll();
        }

        private bool CanWriteSelected()
        {
            var node = treeView_server.SelectedNode;
            var client = node == null ? null : GetRedisNode(node)?.Tag as RedisClient;
            return client != null && client.CanWrite();
        }

        /// <summary>
        /// Builds one export record.
        ///
        /// The DUMP payload is authoritative and is what ImportEntry restores. "type" and "value"
        /// are added so the file is readable and greppable, as the documentation promises, but
        /// they are explicitly best effort: the value is capped per key, and is omitted entirely
        /// when the payload is binary or the collection exceeds the cap. Because import always
        /// prefers the dump, a capped or missing value can never corrupt a restore.
        /// </summary>

        private void CreateRedisShowTagControl<T>() where T : UserControl, new()
        {
            if (userControl != null && !ValueControl.ConfirmAll(userControl)) return;
            T control = new T();
            panel1.Controls.Add(control);
            control.Location = new Point(0, 0);
            control.Size = new Size(panel1.Width - 1, panel1.Height - 1);
            control.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            if (userControl != null)
            {
                panel1.Controls.Remove(userControl);
            }

            if (userControl != null)
            {
                userControl.Dispose();
            }

            userControl = control;
        }

        // Key-type dispatch for the preview panel lives in FormMain.KeyEditors.cs.

        // Load / Save / Add servers

        private void FormMain_Move(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Normal && is_shown)
            {
                Config.mainform_pos_x = Location.X;
                Config.mainform_pos_y = Location.Y;
            }
        }

        private void FormMain_Resize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Normal && is_shown)
            {
                Config.mainform_pos_x = Location.X;
                Config.mainform_pos_y = Location.Y;
                Config.mainform_width = Width;
                Config.mainform_height = Height;
            }
        }

        public void RefreshRenamedKey(TreeNode node, string oldName, string newName)
        {
            var dbNode = GetDbNode(node);
            var settings = (DbSettings)dbNode.Tag;
            settings.Keys.Remove(oldName);
            settings.Keys.Add(newName);
            node.Text = newName;
            CreateRedisShowTagControl<StartControl>();
            treeView_server_AfterSelect(treeView_server, new TreeViewEventArgs(node));
        }

        public void delete_key_operate(TreeNode select, IDatabase database)
        {
            if (!CanWriteSelected()) return;
            if (MessageBox.Show(string.Format(UiText.DeleteKeyConfirm, select.Text), UiText.DeleteKeyTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                if (database.KeyDelete(select.Text))
                {
                    var db_setting_node = get_db_setting_node(select);
                    if (db_setting_node != null &&
                        db_setting_node.Tag is DbSettings dbSettings)
                    {
                        dbSettings.Keys.Remove(select.Text);
						FilterKeys(db_setting_node);
                        db_setting_node.ExpandAll();
					}
                    else
                    {
                        select.Text += "  (Deleted)";
                    }
                }
                else
                {
                    MessageBox.Show(UiText.DeleteKeyFailed);
                }
            }

            if (userControl == null || (userControl as StartControl) == null)
            {
                CreateRedisShowTagControl<StartControl>();
            }
        }

        private static string FormatTTL(TimeSpan ttl)
        {
            if (ttl.TotalSeconds < 0) return "permanent (no TTL)";

            int days = (int)ttl.TotalDays;
            int hours = ttl.Hours;
            int minutes = ttl.Minutes;
            int seconds = ttl.Seconds;

            if (days > 0) return $"{days}d {hours}h {minutes}m {seconds}s ({(long)ttl.TotalSeconds}s)";
            if (hours > 0) return $"{hours}h {minutes}m {seconds}s ({(long)ttl.TotalSeconds}s)";
            if (minutes > 0) return $"{minutes}m {seconds}s ({(long)ttl.TotalSeconds}s)";
            return $"{seconds}s ({(long)ttl.TotalSeconds}s)";
        }

        private const int ExportValueLimit = 500;
    }
}
