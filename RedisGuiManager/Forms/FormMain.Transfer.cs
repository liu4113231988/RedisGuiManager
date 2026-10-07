using System;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;
using RedisGuiManager.Properties;
using StackExchange.Redis;

namespace RedisGuiManager
{
    public partial class FormMain
    {
        // Key copy and migration between databases and servers.

        private bool copy_key_in_same_machine(IDatabase src_db, IDatabase dst_db, string src_key, string dst_key)
		{
            // A dropped connection nulls out the database handles, and the callers are async void, so
            // a missing handle must be reported rather than dereferenced.
            if (src_db == null || dst_db == null)
            {
                MessageBox.Show(UiText.RedisConnectionError, UiText.CopyFailed);
                return false;
            }

            if (src_key == "")
			{
                MessageBox.Show(UiText.SourceKeyEmpty, UiText.CopyFailed);
                return false;
			}

            if (dst_key == "")
			{
                MessageBox.Show(UiText.DestinationKeyEmpty, UiText.CopyFailed);
                return false;
			}

            if (src_db.KeyExists(src_key) == false)
			{
                MessageBox.Show(string.Format(UiText.SourceKeyNotExist, src_key), UiText.CopyFailed);
                return false;
			}

            try
            {
                var snapshot = (RedisResult[])src_db.ScriptEvaluate(
                    "local v=redis.call('DUMP',KEYS[1]); if not v then return redis.error_reply('Source key disappeared') end; return {v,redis.call('PTTL',KEYS[1])}",
                    new StackExchange.Redis.RedisKey[] { src_key });
                long ttl = (long)snapshot[1];

                // RESTORE is issued without REPLACE on purpose: if the destination key appeared after
                // the existence check above, the server rejects the write with BUSYKEY instead of
                // silently overwriting the newer value.
                dst_db.Execute("RESTORE", dst_key, ttl < 0 ? 0L : ttl, (byte[])snapshot[0]);

                return true;
            }
            catch (RedisServerException ex) when (ex.Message.StartsWith("BUSYKEY", StringComparison.OrdinalIgnoreCase))
            {
                // The destination key appeared after the pre-check; report it the same way.
                MessageBox.Show(string.Format(UiText.DestinationKeyExists, dst_key), UiText.CopyFailed);
                return false;
            }
            catch (RedisException ex)
            {
                MessageBox.Show(ex.Message, UiText.CopyFailed);
                return false;
            }

		}

		private async void copy_key_ToolStripMenuItem_Click(object sender, EventArgs e)
		{
            if (!CanWriteSelected()) return;
			TreeNode select = treeView_server.SelectedNode;
			if (select == null)
			{
				return;
			}

			if (select.Tag is RedisKey)
			{
				using (FormInputString formInput = new FormInputString())
				{
					formInput.TextInfo = "Copy key";
					formInput.InputValue = select.Text;

					if (formInput.ShowDialog() == DialogResult.OK)
					{
						TreeNode dbNode = GetDbNode(select);
						TreeNode redisNode = GetRedisNode(select);
                        var dbSettings = dbNode.Tag as DbSettings;
                        var redisClient = redisNode.Tag as RedisClient;
                        var db = redisClient.GetDB(dbSettings.DBNumber);

                        string src_key = select.Text;
                        string dst_key = formInput.InputValue;

                        if (copy_key_in_same_machine(db, db, src_key, dst_key))
						{
                            await RefreshDbKeysAsync(dbNode, true);
                        }
                    }
				}
			}
		}

		private async void copy_key_to_db_ToolStripMenuItem_Click(object sender, EventArgs e)
		{
            if (!CanWriteSelected()) return;
			TreeNode select = treeView_server.SelectedNode;
			if (select == null)
			{
				return;
			}

			if (select.Tag is RedisKey)
			{
				using (FormTwoInputString formInput = new FormTwoInputString())
				{
					TreeNode dbNode = GetDbNode(select);
					TreeNode redisNode = GetRedisNode(select);
					var dbSettings = dbNode.Tag as DbSettings;
					var redisClient = redisNode.Tag as RedisClient;

					formInput.TextInfo = "Copy key to other db";
                    formInput.InputValue1 = dbSettings.DBNumber.ToString();
					formInput.InputValue2 = select.Text;

					if (formInput.ShowDialog() == DialogResult.OK)
					{
                        int dst_db_num = -1;
                        if (int.TryParse(formInput.InputValue1, out dst_db_num) == false)
						{
                            MessageBox.Show(UiText.InsertFirstAsNumber, UiText.CopyFailed);
                            return;
						}

                        if (dst_db_num < 0)
						{
                            MessageBox.Show(UiText.DbNumberMustBePositive, UiText.CopyFailed);
                            return;
						}

                        if (dst_db_num >= redisClient.RedisServer.DatabaseCount)
						{
                            MessageBox.Show(string.Format(UiText.DbNumberNotExist, dst_db_num), UiText.CopyFailed);
                            return;
						}

						var src_db = redisClient.GetDB(dbSettings.DBNumber);
                        var dst_db = redisClient.GetDB(dst_db_num);

						string src_key = select.Text;
						string dst_key = formInput.InputValue2;

						if (copy_key_in_same_machine(src_db, dst_db, src_key, dst_key))
						{
                            var dst_db_node = GetDbNodeFromRedisNode(redisNode, dst_db_num);
                            if (dst_db_node != null)
							{
                                await RefreshDbKeysAsync(dst_db_node, true);
                            }
                        }
					}
				}
			}
		}

		private void copy_key_to_machine_ToolStripMenuItem_Click(object sender, EventArgs e)
		{
            if (!CanWriteSelected()) return;
			TreeNode select = treeView_server.SelectedNode;
			if (select == null)
			{
				return;
			}

			if (select.Tag is RedisKey)
			{
				using (FormMigrateKey formInput = new FormMigrateKey())
				{
					TreeNode dbNode = GetDbNode(select);
					TreeNode redisNode = GetRedisNode(select);
					var dbSettings = dbNode.Tag as DbSettings;
					var redisClient = redisNode.Tag as RedisClient;

					if (formInput.ShowDialog() == DialogResult.OK)
					{
						var db = redisClient.GetDB(dbSettings.DBNumber);
						string src_key = select.Text;

                        // MIGRATE needs a literal endpoint, so a hostname must be rejected with a
                        // message instead of letting IPAddress.Parse throw and kill the UI.
                        if (IPAddress.TryParse(formInput.Host, out IPAddress targetAddress) == false)
                        {
                            MessageBox.Show(UiText.MigrationNeedsIpAddress);
                            return;
                        }

                        IPEndPoint end_point = new IPEndPoint(targetAddress, formInput.Port);

                        try
                        {
                            db.KeyMigrate(src_key, end_point, formInput.DBNumber, migrateOptions: MigrateOptions.Copy);

                            MessageBox.Show(string.Format(UiText.MigrateComplete, src_key, $"{formInput.Host}:{formInput.Port}", formInput.DBNumber));
                        }
                        catch (RedisCommandException ex)
						{
                            MessageBox.Show(ex.Message);
						}
                        catch (RedisTimeoutException ex)
						{
                            MessageBox.Show(ex.Message);
                        }
                    }
				}
			}
		}

		private async void migrate_keys_ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!CanWriteSelected()) return;
            var selected = treeView_server.SelectedNode;
            if (selected?.Tag is not DbSettings db) return;
            var client = (RedisClient)GetRedisNode(selected).Tag;
            using var input = new FormMigrateKey { UsePattern = true };
            if (input.ShowDialog(this) != DialogResult.OK) return;
            if (!IPAddress.TryParse(input.Host, out var address)) { MessageBox.Show(UiText.MigrationNeedsIpAddress); return; }
            var target = new IPEndPoint(address, input.Port);
            int targetDb = input.DBNumber;
            await RunBatchAsync(client, new[] { db.DBNumber }, input.KeyPattern, "Copy keys to server", $"Target: {target}, DB {targetDb}", (database, key) =>
            {
                database.KeyMigrate(key, target, targetDb, migrateOptions: MigrateOptions.Copy);
                return true;
            });
        }

    }
}
