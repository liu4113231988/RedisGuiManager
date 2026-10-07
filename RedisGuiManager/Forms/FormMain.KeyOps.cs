using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using RedisGuiManager.Properties;
using StackExchange.Redis;

namespace RedisGuiManager
{
    public partial class FormMain
    {
        // Single-key operations: create, refresh, filter, TTL and query window.

        private async void new_key_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!CanWriteSelected()) return;
            TreeNode select = treeView_server.SelectedNode;
            if (select == null)
            {
                return;
            }

            RedisClient redisClient = select.Parent.Tag as RedisClient;
            if (redisClient == null)
            {
                MessageBox.Show(UiText.InvalidRedisSetting);

                return;
            }

            if (select.Tag is DbSettings dbSettings)
            {
                var db = redisClient.GetDB(dbSettings.DBNumber);
                using (FormRedisInput redisInput = new FormRedisInput(db))
                {
                    redisInput.ShowDialog();
                    await RefreshDbKeysAsync(select, true);
                }
            }
        }

        private async void reload_keys_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode select = treeView_server.SelectedNode;
            if (select == null)
            {
                return;
            }

            if (select.Tag is DbSettings)
            {
                await RefreshDbKeysAsync(select, true);
            }
        }

        private async void filter_key_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode select = treeView_server.SelectedNode;
            if (select == null)
            {
                return;
            }

            if (select.Tag is DbSettings dbSettings)
            {
                using (FormInputString formInput = new FormInputString())
                {
                    formInput.TextInfo = "Filter";
                    formInput.InputValue = dbSettings.Filter;

                    if (formInput.ShowDialog() == DialogResult.OK)
                    {
                        dbSettings.Filter = formInput.InputValue == "" ? "*" : formInput.InputValue;
                        await RefreshDbKeysAsync(select, true);
                        select.ExpandAll();
                    }
                }
            }
        }

        private async void unfilter_key_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode select = treeView_server.SelectedNode;
            if (select == null)
            {
                return;
            }

            if (select.Tag is DbSettings dbSettings)
            {
                dbSettings.Filter = "*";
                await RefreshDbKeysAsync(select, true);
                select.ExpandAll();
            }
        }

        private void remove_keys_ToolStripMenuItem_Click(object sender, EventArgs e)
		{
            if (!CanWriteSelected()) return;
            PromptBatchDelete(false);
        }

        private void queryWindowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode select = treeView_server.SelectedNode;
            if (select == null)
            {
                return;
            }

            if (select.Tag is DbSettings dbSettings)
            {
                RedisClient redisClient = select.Parent.Tag as RedisClient;

                FormQueryWindow fqw = new FormQueryWindow();
                fqw.SetServerInfo(redisClient, dbSettings.DBNumber);
                fqw.Show();
            }
        }

        // Tree view action

        private void delete_key_toolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode select = treeView_server.SelectedNode;
            if (select == null)
            {
                return;
            }

            if (select.Tag is not RedisKey)
            {
                return;
            }

            TreeNode dbNode = GetDbNode(select);
            TreeNode redisNode = GetRedisNode(select);

            if (redisNode.Tag is RedisClient redisClient)
            {
                if (dbNode.Tag is DbSettings dbSettings)
                {
                    var database = redisClient.GetDB(dbSettings.DBNumber);

                    delete_key_operate(select, database);
                }
            }
        }

        private void key_query_window_toolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode select = treeView_server.SelectedNode;
            if (select == null)
            {
                return;
            }

            if (select.Tag is not RedisKey)
            {
                return;
            }

            TreeNode dbNode = GetDbNode(select);
            TreeNode redisNode = GetRedisNode(select);

            if (redisNode.Tag is RedisClient redisClient)
            {
                if (dbNode.Tag is DbSettings dbSettings)
                {
                    var database = redisClient.GetDB(dbSettings.DBNumber);
                    var key_type = database.KeyType(select.Text);

                    FormQueryWindow fqw = new FormQueryWindow();
                    fqw.SetServerInfo(redisClient, dbSettings.DBNumber);
                    fqw.SetRedisKeysType(key_type);
                    fqw.SetRedisKeysFilter(select.Text);
                    fqw.Show();
                }
            }
        }

        private void view_ttl_toolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode select = treeView_server.SelectedNode;
            if (select == null || select.Tag is not RedisKey) return;

            TreeNode dbNode = GetDbNode(select);
            TreeNode redisNode = GetRedisNode(select);

            if (redisNode.Tag is RedisClient redisClient && dbNode.Tag is DbSettings dbSettings)
            {
                var database = redisClient.GetDB(dbSettings.DBNumber);
                var ttl = database.KeyTimeToLive(select.Text);

                string message;
                if (ttl == null)
                {
                    message = database.KeyExists(select.Text) ? "Key has no associated TTL (permanent)." : "Key does not exist.";
                }
                else if (ttl.Value.TotalSeconds < 0)
                {
                    message = database.KeyExists(select.Text) ? "Key has no associated TTL (permanent)." : "Key does not exist.";
                }
                else
                {
                    message = FormatTTL(ttl.Value);
                }

                MessageBox.Show(message, string.Format(UiText.TtlOf, select.Text), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void set_ttl_toolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!CanWriteSelected()) return;
            TreeNode select = treeView_server.SelectedNode;
            if (select == null || select.Tag is not RedisKey) return;

            TreeNode dbNode = GetDbNode(select);
            TreeNode redisNode = GetRedisNode(select);

            if (redisNode.Tag is RedisClient redisClient && dbNode.Tag is DbSettings dbSettings)
            {
                var database = redisClient.GetDB(dbSettings.DBNumber);

                using FormInputString formInput = new FormInputString();
                formInput.TextInfo = "Set TTL (seconds), -1 = permanent, 0 = delete immediately";
                formInput.InputValue = "";

                if (formInput.ShowDialog() == DialogResult.OK)
                {
                    if (long.TryParse(formInput.InputValue, out long seconds) && seconds >= -1 && seconds <= TimeSpan.MaxValue.TotalSeconds)
                    {
                        if (seconds == -1)
                        {
                            if (database.KeyPersist(select.Text))
                            {
                                MessageBox.Show(UiText.TtlRemovedPermanent, UiText.SuccessTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                            else
                            {
                                MessageBox.Show(UiText.FailedToRemoveTtl, UiText.ErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }
                        else if (seconds == 0)
                        {
                            if (database.KeyDelete(select.Text))
                            {
                                var db_setting_node = get_db_setting_node(select);
                                if (db_setting_node != null && db_setting_node.Tag is DbSettings dbs)
                                {
                                    dbs.Keys.Remove(select.Text);
                                    FilterKeys(db_setting_node);
                                    db_setting_node.ExpandAll();
                                }
                                MessageBox.Show(UiText.KeyDeletedTtlZero, UiText.SuccessTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                            else
                            {
                                MessageBox.Show(UiText.FailedToDeleteKey, UiText.ErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }
                        else
                        {
                            if (database.KeyExpire(select.Text, TimeSpan.FromSeconds(seconds)))
                            {
                                var ttl = database.KeyTimeToLive(select.Text);
                                string ttlStr = ttl != null ? FormatTTL(ttl.Value) : "permanent";
                                MessageBox.Show(string.Format(UiText.TtlSetSuccess, select.Text, ttlStr), UiText.SuccessTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                            else
                            {
                                MessageBox.Show(UiText.FailedToSetTtl, UiText.ErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }
                    }
                    else
                    {
                        MessageBox.Show(UiText.EnterValidNumber, UiText.ErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void remove_ttl_toolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!CanWriteSelected()) return;
            TreeNode select = treeView_server.SelectedNode;
            if (select == null || select.Tag is not RedisKey) return;

            TreeNode dbNode = GetDbNode(select);
            TreeNode redisNode = GetRedisNode(select);

            if (redisNode.Tag is RedisClient redisClient && dbNode.Tag is DbSettings dbSettings)
            {
                var database = redisClient.GetDB(dbSettings.DBNumber);

                if (MessageBox.Show(string.Format(UiText.RemoveTtlPrompt, select.Text), UiText.RemoveTtlTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    if (database.KeyPersist(select.Text))
                    {
                        MessageBox.Show(UiText.TtlRemovedKeyPermanent, UiText.SuccessTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        MessageBox.Show(UiText.FailedToRemoveTtlDetail, UiText.ErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void folder_query_window_toolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode select = treeView_server.SelectedNode;
            if (select == null)
            {
                return;
            }

            if (select.Tag is not RedisFolder)
            {
                return;
            }

            TreeNode dbNode = GetDbNode(select);
            TreeNode redisNode = GetRedisNode(select);
            string key_filter = GetFullpathFolder(select, "") + "*";

            if (redisNode.Tag is RedisClient redisClient)
            {
                if (dbNode.Tag is DbSettings dbSettings)
                {
                    var database = redisClient.GetDB(dbSettings.DBNumber);
                    var key_type = database.KeyType(select.Text);

                    FormQueryWindow fqw = new FormQueryWindow();
                    fqw.SetServerInfo(redisClient, dbSettings.DBNumber);
                    fqw.SetRedisKeysType(key_type);
                    fqw.SetRedisKeysFilter(key_filter);
                    fqw.Show();
                }
            }
        }

    }
}
