using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using RedisGuiManager.Properties;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StackExchange.Redis;

namespace RedisGuiManager
{
    public partial class FormMain
    {
        // Database node management and batch delete / migrate engine.

        private void add_db_to_list_ToolStripMenuItem_Click(object sender, EventArgs e)
		{
			TreeNode select = treeView_server.SelectedNode;
			if (select == null)
			{
				return;
			}

			if (select.Tag is not RedisClient)
			{
				return;
			}

			using (FormInputString formInput = new FormInputString())
			{
				formInput.TextInfo = "Add DB";
				formInput.InputValue = "";

				if (formInput.ShowDialog() == DialogResult.OK)
				{
                    string[] seps = formInput.InputValue.Split(' ');
                    List<int> db_nums = new List<int>();

                    foreach (string s in seps)
					{
                        if (int.TryParse(s, out int dbNum))
                        {
                            db_nums.Add(dbNum);
                        }
                    }

                    if (db_nums.Count > 0)
					{
                        add_dbs_to_list(db_nums, select);
                    }
				}
			}
		}

        private void add_db_range_to_list_ToolStripMenuItem_Click(object sender, EventArgs e)
		{
			TreeNode select = treeView_server.SelectedNode;
			if (select == null)
			{
				return;
			}

			if (select.Tag is not RedisClient)
			{
				return;
			}

			using (FormTwoInputString formInput = new FormTwoInputString())
			{
				formInput.TextInfo = "Add DB Range";
				formInput.InputValue1 = "";
				formInput.InputValue2 = "";

				if (formInput.ShowDialog() == DialogResult.OK)
				{
                    int dbNumStart = 0;
                    int dbNumEnd = 0;

                    if (int.TryParse(formInput.InputValue1, out dbNumStart) == false)
					{
                        MessageBox.Show(UiText.InsertValue1Number);
                        return;
					}

					if (int.TryParse(formInput.InputValue2, out dbNumEnd) == false)
					{
						MessageBox.Show(UiText.InsertValue2Number);
						return;
					}

                    if (dbNumStart > dbNumEnd)
					{
                        int temp = dbNumStart;
                        dbNumStart = dbNumEnd;
                        dbNumEnd = temp;
					}

                    List<int> db_nums = new List<int>();
                    for (int i = dbNumStart; i <= dbNumEnd; ++i)
					{
                        db_nums.Add(i);
					}

                    add_dbs_to_list(db_nums, select);
                }
			}
		}

        private void add_dbs_to_list(List<int> db_nums, TreeNode select)
		{
			if (select.Tag is RedisClient client)
			{
				if (client.Settings.additional_dbs == null)
					client.Settings.additional_dbs = new List<int>();
				bool changed = false;
				foreach (int db_num in db_nums)
				{
					if (AddTreeNode_DB(client, db_num, select, false))
					{
						client.Settings.additional_dbs.Add(db_num);
						changed = true;
					}
				}

				if (changed)
				{
					client.Settings.additional_dbs.Sort();
					SaveRedisSettings();
				}
				else if (client.Settings.additional_dbs.Count == 0)
				{
					// 避免空列表序列化为 null，保持一致性
					client.Settings.additional_dbs = null;
				}
			}
		}

        private async void remove_db_range_from_list_ToolStripMenuItem_Click(object sender, EventArgs e)
		{
			TreeNode select = treeView_server.SelectedNode;
			if (select == null)
			{
				return;
			}

			if (select.Tag is not RedisClient)
			{
				return;
			}

			using (FormTwoInputString formInput = new FormTwoInputString())
			{
				formInput.TextInfo = "Remove DB Range";
				formInput.InputValue1 = "";
				formInput.InputValue2 = "";

				if (formInput.ShowDialog() == DialogResult.OK)
				{
					int dbNumStart = 0;
					int dbNumEnd = 0;

					if (int.TryParse(formInput.InputValue1, out dbNumStart) == false)
					{
						MessageBox.Show(UiText.InsertValue1Number);
						return;
					}

					if (int.TryParse(formInput.InputValue2, out dbNumEnd) == false)
					{
						MessageBox.Show(UiText.InsertValue2Number);
						return;
					}

					if (dbNumStart > dbNumEnd)
					{
						int temp = dbNumStart;
						dbNumStart = dbNumEnd;
						dbNumEnd = temp;
					}

					if (select.Tag is RedisClient client)
					{
						if (client.Settings.additional_dbs == null)
							client.Settings.additional_dbs = new List<int>();
						for (int i = dbNumStart; i <= dbNumEnd; ++i)
						{
							client.Settings.additional_dbs.Remove(i);
						}

						client.Settings.additional_dbs.Sort();

						SaveRedisSettings();
					}

                    await RefreshRedisKeyAsync(select, true);
                                    }
                    			}
                            }

        private void find_db_from_list_ToolStripMenuItem_Click(object sender, EventArgs e)
		{
			TreeNode select = treeView_server.SelectedNode;
			if (select == null)
			{
				return;
			}

			if (select.Tag is not RedisClient)
			{
				return;
			}

			using (FormInputString formInput = new FormInputString())
			{
				formInput.TextInfo = "Find DB";
				formInput.InputValue = "";

				if (formInput.ShowDialog() == DialogResult.OK)
				{
					if (int.TryParse(formInput.InputValue, out int dbNum))
					{
						if (select.Tag is RedisClient client)
						{
                            TreeNode node = GetTreeNode_DB(dbNum, select);
                            if (node != null)
							{
                                treeView_server.SelectedNode = node;
							}
						}
					}
					else
					{
						MessageBox.Show(UiText.InsertNumberOnly);
					}
				}
			}
		}

        private async void toggle_show_default_dbs_ToolStripMenuItem_Click(object sender, EventArgs e)
		{
			TreeNode select = treeView_server.SelectedNode;
			if (select == null)
			{
				return;
			}

            if (select.Tag is RedisClient client)
			{
                client.Settings.hide_default_dbs = !client.Settings.hide_default_dbs;

				SaveRedisSettings();

				await RefreshRedisKeyAsync(select, true);
			}
        }

        private void remove_keys_from_registered_dbs_ToolStripMenuItem_Click(object sender, EventArgs e)
		{
            if (!CanWriteSelected()) return;
            PromptBatchDelete(false);
        }

        private void remove_keys_from_whole_dbs_ToolStripMenuItem_Click(object sender, EventArgs e)
		{
            if (!CanWriteSelected()) return;
            PromptBatchDelete(true);
        }

        private async void PromptBatchDelete(bool allDatabases)
        {
            var selected = treeView_server.SelectedNode;
            var client = (RedisClient)GetRedisNode(selected).Tag;
            using var input = new FormInputString { TextInfo = "Delete keys matching pattern", InputValue = "" };
            if (input.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(input.InputValue)) return;
            var databases = selected.Tag is DbSettings db && !allDatabases
                ? new[] { db.DBNumber }
                : allDatabases ? Enumerable.Range(0, client.Settings.use_cluster ? 1 : client.RedisServer.DatabaseCount).ToArray()
                : selected.Nodes.Cast<TreeNode>().Where(n => n.Tag is DbSettings).Select(n => ((DbSettings)n.Tag).DBNumber).ToArray();
            await RunBatchAsync(client, databases, input.InputValue, "Delete keys", "", (database, key) => database.KeyDelete(key));
            if (selected.Tag is DbSettings) await RefreshDbKeysAsync(selected, true);
        }

        private async Task RunBatchAsync(RedisClient client, int[] databases, string pattern, string title, string destination, Func<IDatabase, StackExchange.Redis.RedisKey, bool> execute)
        {
            if (!client.CanWrite()) return;
            string manifest = Path.GetTempFileName();
            try
            {
                var previewOutcome = await OperationDialog.RunAsync(this, "Preview " + title, (token, progress) =>
                {
                    long count = 0;
                    var sample = new List<string>();
                    using var writer = new StreamWriter(manifest);
                    foreach (int db in databases.Distinct())
                    {
                        foreach (var key in client.ScanKeys(db, pattern, Config.scan_page_count))
                        {
                            token.ThrowIfCancellationRequested();
                            writer.WriteLine(JsonConvert.SerializeObject(new object[] { db, Convert.ToBase64String((byte[])key) }));
                            if (sample.Count < 10) sample.Add($"DB {db}: {key}");
                            if (++count % 100 == 0) progress.Report($"Matched {count} keys");
                        }
                    }
                    return (Count: count, Sample: sample);
                });
                if (!previewOutcome.IsSuccess) return;

                var preview = previewOutcome.Value;
                if (preview.Count == 0) { MessageBox.Show(this, UiText.NoKeysMatched, title); return; }
                if (MessageBox.Show(this, string.Format(UiText.BatchPreview, client.Settings.name, $"{client.Settings.host}:{client.Settings.port}", string.Join(", ", databases), pattern, preview.Count, destination, string.Join("\n", preview.Sample)), title + UiText.BatchPreviewSuffix, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

                var resultOutcome = await OperationDialog.RunAsync(this, title, (token, progress) =>
                {
                    var report = new OperationReport();
                    foreach (string line in File.ReadLines(manifest))
                    {
                        if (token.IsCancellationRequested) { report.Canceled = true; break; }
                        var item = JArray.Parse(line);
                        int db = item[0].Value<int>();
                        var key = (StackExchange.Redis.RedisKey)Convert.FromBase64String(item[1].Value<string>());
                        try { if (execute(client.GetDB(db), key)) report.Success++; else report.Skipped++; }
                        catch (RedisException ex) { report.Errors.Add($"DB {db} / {key}: {ex.Message}"); }
                        if ((report.Success + report.Skipped + report.Errors.Count) % 100 == 0) progress.Report($"Processed {report.Success + report.Skipped + report.Errors.Count} / {preview.Count}");
                    }
                    return report;
                });
                if (!resultOutcome.IsSuccess) return;

                resultOutcome.Value.Show(this, title + " result");
            }
            finally { File.Delete(manifest); }
        }

        private async void remove_db_ToolStripMenuItem_Click(object sender, EventArgs e)
		{
			TreeNode select = treeView_server.SelectedNode;
			if (select == null)
			{
				return;
			}

			if (select.Tag is DbSettings dbSettings)
			{
                RedisClient redisClient = select.Parent?.Tag as RedisClient;
                if (redisClient == null) return;
                if (redisClient.Settings.additional_dbs == null)
                    redisClient.Settings.additional_dbs = new List<int>();
                redisClient.Settings.additional_dbs.Remove(dbSettings.DBNumber);

				SaveRedisSettings();

				await RefreshRedisKeyAsync(select.Parent, true);
			}
        }

    }
}
