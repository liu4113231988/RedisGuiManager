using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using RedisGuiManager.Properties;
using StackExchange.Redis;

namespace RedisGuiManager
{
    public partial class FormMain
    {
        // Server-level tools: console, info, slowlog, pub/sub and reload.

        private async void reload_server_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode select = treeView_server.SelectedNode;
            if (select == null)
            {
                return;
            }

            if (select.Tag is RedisClient)
            {
                await RefreshRedisKeyAsync(select, true);
            }
        }

        private async void query_window_all_server_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode select = treeView_server.SelectedNode;
            if (select == null)
            {
                return;
            }

            if (select.Tag is RedisClient client)
            {
				if (client.RedisServer == null)
				{
                    var connect = await OperationDialog.RunAsync(this, "Connect", (token, progress) => client.Connect());
                    if (!connect.IsSuccess) return;
					if (connect.Value.IsSuccess == false)
					{
						MessageBox.Show(UiText.ConnectionFailed);
						return;
					}
				}

				FormQueryWindow fqw = new FormQueryWindow();
				fqw.SetServerInfo(client, -1);
				fqw.Show();
			}
        }

        private void open_console_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode select = treeView_server.SelectedNode;
            if (select == null)
            {
                return;
            }

            if (select.Tag is RedisClient client)
            {
                FormConsole formConsole = new FormConsole();
                formConsole.SetRedis(client);
                formConsole.Show();
            }
        }

        private void server_info_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode select = treeView_server.SelectedNode;
            if (select == null) return;

            if (select.Tag is RedisClient client)
            {
                FormServerInfo form = new FormServerInfo(client);
                form.Show();
            }
        }

        private void slowlog_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode select = treeView_server.SelectedNode;
            if (select == null) return;

            if (select.Tag is RedisClient client)
            {
                FormSlowlog form = new FormSlowlog(client);
                form.Show();
            }
        }

        private void server_tools_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode select = treeView_server.SelectedNode;
            if (select == null) return;

            if (select.Tag is RedisClient client)
            {
                if (client.Redis == null)
                {
                    MessageBox.Show(this, UiText.ConnectToServerFirst, UiText.ServerToolsTitle,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                FormServerTools form = new FormServerTools(client);
                form.Show(this);
            }
        }

        private void pubsub_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode select = treeView_server.SelectedNode;
            if (select == null) return;

            if (select.Tag is RedisClient client)
            {
                FormPubSub form = new FormPubSub(client);
                form.Show();
            }
        }

    }
}
