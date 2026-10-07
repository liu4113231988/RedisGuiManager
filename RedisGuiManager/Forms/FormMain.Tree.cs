using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using StackExchange.Redis;
using RedisGuiManager.Properties;

namespace RedisGuiManager
{
    public partial class FormMain
    {
        // Server tree: node lookup, key scanning, paging and folder building.

        private void treeView_server_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                // Right-click only moves the selection. Loading is a double-click action now, so
                // there is no handler to detach while the selection changes.
                TreeNode node = treeView_server.GetNodeAt(e.Location);
                if (treeView_server.SelectedNode != node)
                {
                    treeView_server.SelectedNode = node;
                }

                TreeNode select = treeView_server.SelectedNode;
                if (select == null)
                {
                    return;
                }

                if (select.Tag is DbSettings)
                {
                    contextMenuStrip_db.Show(treeView_server, e.Location);
                }
                else if (select.Tag is RedisClient)
                {
                    contextMenuStrip_redis.Show(treeView_server, e.Location);
                }
                else if (select.Tag is RedisFolder)
                {
                    contextMenuStrip_class.Show(treeView_server, e.Location);
                }
                else if (select.Tag is RedisKey)
                {
                    if (select.Text.Contains("  (Deleted)") == false)
                    {
                        contextMenuStrip_key.Show(treeView_server, e.Location);
                    }
                }
            }
        }

        /// <summary>
        /// Loads a node: a connection, database or folder materialises its children and a key opens
        /// its value editor. A single click only moves the selection, so clicking through a wide tree
        /// no longer starts a scan per node.
        /// </summary>
        private void LoadTreeNode(TreeNode node)
        {
            if (node == null) return;

            RunGuardedAsync(async () =>
            {
                bool hadChildren = node.Nodes.Count > 0;
                await SelectNodeAsync(node);

                // A node that only just received its children has no expander yet, so open it here.
                // An already populated node is left to the TreeView's own double-click behaviour.
                if (hadChildren == false && node.Nodes.Count > 0) node.Expand();
            });
        }

        /// <summary>
        /// Loads the value editor for <paramref name="select"/>. Callers that are not event handlers
        /// await this instead of firing an async void, so a failure can still be observed.
        /// </summary>
        private async Task SelectNodeAsync(TreeNode select)
        {
            if (select == null) return;
            // Tag 可能为 null（新创建的临时节点），直接返回
            if (select.Tag == null) return;

            if (select.Tag is Action loadMore) { loadMore(); return; }
            if (select.Tag is RedisGroup)
            {
                return;
            }

            // Only a node that really talks to Redis gets the loading icon. Groups, folders whose
            // children are already built and databases with cached keys are filled from memory, and
            // flashing "loading" for them made every click blink. The tree is invalidated but never
            // pumped, so the icon is painted at the first real await and a quick load skips it.
            bool showLoading = SelectionNeedsServer(select);
            string old_imagekey = select.ImageKey;
            if (showLoading)
            {
                select.ImageKey = "loading";
                select.SelectedImageKey = "loading";
                treeView_server.Invalidate();
            }

            try
            {
            if (select.Tag is RedisClient)
            {
                CreateRedisShowTagControl<StartControl>();
                await RefreshRedisKeyAsync(select);
            }
            else if (select.Tag is DbSettings)
            {
                CreateRedisShowTagControl<StartControl>();
                await RefreshDbKeysAsync(select, false);
            }
            else if (select.Tag is RedisFolder)
            {
                BuildTreeNode_Folder(select);
            }
            else if (select.Tag is RedisKey)
            {
                if (select.Text.Contains("  (Deleted)") == false)
                {
                    TreeNode dbNode = GetDbNode(select);
                    TreeNode redisNode = GetRedisNode(select);

                    if (redisNode.Tag is RedisClient redisClient)
                    {
                        if (dbNode.Tag is DbSettings dbSettings)
                        {
                            if (redisClient.DBBlock != dbSettings.DBNumber)
                            {
                                OperateResult change = redisClient.SelectDB(dbSettings.DBNumber);
                                if (change.IsSuccess == false)
                                {
                                    select.ImageKey = old_imagekey;
                                    select.SelectedImageKey = old_imagekey;
                                    MessageBox.Show(string.Format(UiText.SelectDbFailedFormat, redisClient.Settings.ToString(), dbSettings.DBNumber) + Environment.NewLine + change.Message);

                                    return;
                                }

                                redisClient.DBBlock = dbSettings.DBNumber;
                            }

                            toolStripStatusLabel1.Text = string.Format(UiText.KeyStatusFormat,
                                redisClient.Settings.name,
                                dbSettings.DBNumber,
                                redisClient.IsConnected ? UiText.ConnectedStatus : UiText.DisconnectedStatus,
                                redisClient.Settings.read_only ? UiText.ReadOnlyStatusSuffix : "");
                            // Redis is null once a connection was closed or never established.
                            if (redisClient.Redis == null) { select.ImageKey = old_imagekey; select.SelectedImageKey = old_imagekey; return; }
                            var type = redisClient.Redis.KeyType(select.Text);
                            switch (type)
                            {
                                case StackExchange.Redis.RedisType.String:
                                {
                                    await StringKeySelect(redisClient, select);
                                }
                                break;
                                case StackExchange.Redis.RedisType.List:
                                {
                                    await ListKeySelect(redisClient, select);
                                }
                                break;
                                case StackExchange.Redis.RedisType.Hash:
                                {
                                    await HashKeySelect(redisClient, select);
                                }
                                break;
                                case StackExchange.Redis.RedisType.Set:
                                {
                                    await SetKeySelect(redisClient, select);
                                }
                                break;
                                case StackExchange.Redis.RedisType.SortedSet:
                                {
                                    await ZSetKeySelect(redisClient, select);
                                }
                                break;
                                case StackExchange.Redis.RedisType.Stream:
                                {
                                    await StreamKeySelect(redisClient, select);
                                }
                                break;
                                default:
                                {
                                    MessageBox.Show(string.Format(UiText.KeyNotExistFormat, select.Text));
                                }
                                break;
                            }
                        }
                    }
                }
                else
                {
                    if (userControl == null || (userControl as StartControl) == null)
                    {
                        CreateRedisShowTagControl<StartControl>();
                    }
                }
            }

            }
            catch (RedisException ex)
            {
                MessageBox.Show(this, ex.Message + Environment.NewLine + UiText.RedisReloadHint, UiText.RedisRequestFailedTitle);
            }
            catch (Exception ex)
            {
                // A dropped connection nulls out RedisClient.Redis, so selecting a node can also
                // fail with NullReferenceException or ObjectDisposedException. Those must not
                // escape an async void handler.
                ReportBackgroundFailure(ex);
            }
            finally
            {
                if (showLoading && IsDisposed == false && Disposing == false)
                {
                    select.ImageKey = old_imagekey;
                    select.SelectedImageKey = old_imagekey;
                }
            }
        }

        /// <summary>
        /// True when selecting this node will actually hit the server. Groups and folders are
        /// populated from memory, and a database whose keys are already cached needs no round trip,
        /// so none of them should show a loading indicator.
        /// </summary>
        private static bool SelectionNeedsServer(TreeNode select)
        {
            switch (select.Tag)
            {
                case RedisKey _:
                    return true;

                case RedisClient client:
                    // A fresh connection has to connect, and an empty node has to ask for its db list.
                    return client.Redis == null || select.Nodes.Count == 0;

                case DbSettings db:
                    return db.KeyScan == null && db.Keys == null;

                default:
                    return false;
            }
        }

        private TreeNode GetRedisNode(TreeNode treeNode)
        {
            if (treeNode == null) return null;
            if (treeNode.Tag is RedisClient)
            {
                return treeNode;
            }
            if (treeNode.Parent == null) return null;
            return GetRedisNode(treeNode.Parent);
        }

        private TreeNode GetDbNode(TreeNode treeNode)
        {
            if (treeNode == null) return null;
            if (treeNode.Tag is DbSettings)
            {
                return treeNode;
            }
            if (treeNode.Parent == null) return null;
            return GetDbNode(treeNode.Parent);
        }

        private TreeNode GetDbNodeFromRedisNode(TreeNode redisNode, int db_num)
		{
            if (redisNode.Tag is not RedisClient)
			{
                return null;
			}

            foreach (TreeNode node in redisNode.Nodes)
			{
                if (node.Tag is DbSettings dbSettings)
				{
                    if (dbSettings.DBNumber == db_num)
					{
                        return node;
					}
				}
			}

            return null;
		}

        private string GetFullpathFolder(TreeNode treeNode, string path)
        {
            if (treeNode == null) return path.Length > 0 ? path.Substring(1) : path;
            if (treeNode.Tag is DbSettings)
            {
                return path.Length > 0 ? path.Substring(1) : path;
            }
            else if (treeNode.Tag is RedisFolder redis_folder)
            {
                path = ":" + redis_folder.path_name + path;
            }
            if (treeNode.Parent == null) return path.Length > 0 ? path.Substring(1) : path;

            return GetFullpathFolder(treeNode.Parent, path);
        }

        private void FilterKeys(TreeNode select)
        {
            if (select.Tag is DbSettings dbSettings)
            {
                select.Nodes.Clear();

                var filter_list_keys = (dbSettings.Keys ?? new List<string>()).ToList();

                if (dbSettings.Filter != "" &&
                    dbSettings.Filter != "*")
                {
                    filter_list_keys = filter_list_keys.Where(s => Utils.RedisGlobMatch(dbSettings.Filter, s)).ToList();
                    select.Text = string.Format("db{0} ({1}) [{2}]", dbSettings.DBNumber, filter_list_keys.Count(), dbSettings.Filter);
                }
                else
                {
                    select.Text = string.Format("db{0} ({1})", dbSettings.DBNumber, filter_list_keys.Count());
                }

                BuildTreeNode_DB(select, filter_list_keys);
                if (dbSettings.HasMoreKeys)
                    select.Nodes.Add(new TreeNode(UiText.LoadNextKeysLabel) { Tag = (Action)(() => RunGuardedAsync(() => LoadDbKeyPageAsync(select))) });
            }
        }

        /// <summary>
        /// Runs a fire-and-forget task with a catch-all: these are invoked from synchronous contexts
        /// (menu items, tree node tags), so an escaping exception would reach
        /// Application.ThreadException and close the application.
        /// </summary>
        private async void RunGuardedAsync(Func<Task> work)
        {
            try
            {
                await work();
            }
            catch (Exception ex)
            {
                ReportBackgroundFailure(ex);
            }
        }

        private void ReportBackgroundFailure(Exception ex)
        {
            if (ex == null || IsDisposed || Disposing || shuttingDown) return;
            try
            {
                MessageBox.Show(this, ex.Message, UiText.OperationFailedRefresh, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch
            {
                // The form may be closing while the message box is requested.
            }
        }

        // Refresh key

        private async Task RefreshRedisKeyAsync(TreeNode select, bool reload = false)
        {
            if (select.Tag is RedisClient redisClient)
            {
                if (reload)
                {
                    if (!ValueControl.ConfirmAll(panel1)) return;
                    CreateRedisShowTagControl<StartControl>();
                    DisposeKeyScans(select);
                    select.Nodes.Clear();
                }

                if (reload && !redisClient.IsConnected) redisClient.Close();
                if (redisClient.Redis == null)
                {
                    var connectOutcome = await OperationDialog.RunAsync(this, UiText.ConnectTitle, (token, progress) => redisClient.Connect());
                    if (!connectOutcome.IsSuccess) return;
                    var connect = connectOutcome.Value;
                    if (connect.IsSuccess == false)
                    {
                        MessageBox.Show(string.Format(UiText.ConnectFailedDetailFormat, redisClient.Settings.name, redisClient.Settings.host, redisClient.Settings.port) + Environment.NewLine + connect.Message);

                        return;
                    }
                }

                if (select.Nodes.Count == 0 || reload)
                {
                    if (redisClient.Settings.use_cluster)
                    {
                        // Cluster mode only supports db 0
                        AddTreeNode_DB(redisClient, 0, select);
                    }
                    else
                    {
                        if (redisClient.Settings.hide_default_dbs == false)
                        {
                            for (int i = 0; i < redisClient.RedisServer.DatabaseCount; i++)
                            {
                                AddTreeNode_DB(redisClient, i, select);
                            }
                        }

                        if (redisClient.Settings.additional_dbs != null)
                        {
                            redisClient.Settings.additional_dbs.Sort();
                            foreach (int dbNum in redisClient.Settings.additional_dbs)
                            {
                                AddTreeNode_DB(redisClient, dbNum, select);
                            }
                        }
                    }

                    if (redisClient.SelectDB(0).IsSuccess)
                    {
                        redisClient.DBBlock = 0;
                    }
                }
            }
        }

        private void DisposeKeyScans(TreeNode node)
        {
            if (node.Tag is DbSettings db) db.KeyScan?.Dispose();
            foreach (TreeNode child in node.Nodes) DisposeKeyScans(child);
        }

        private async Task RefreshDbKeysAsync(TreeNode select, bool reload = false)
        {
            if (select.Tag is not DbSettings db) return;
            if (!ValueControl.ConfirmAll(panel1)) return;
            if (reload || db.KeyScan == null && db.Keys == null)
            {
                db.KeyScan?.Dispose();
                var client = (RedisClient)select.Parent.Tag;
                db.KeyScan = client.ScanKeys(db.DBNumber, db.Filter, Config.scan_page_count).GetEnumerator();
                db.Keys = new List<string>();
                db.HasMoreKeys = true;
                await LoadDbKeyPageAsync(select);
            }
        }

        private async Task LoadDbKeyPageAsync(TreeNode node)
        {
            var db = (DbSettings)node.Tag;
            var outcome = await OperationDialog.RunAsync(this, UiText.LoadKeysTitle, (token, progress) =>
            {
                var page = new List<string>();
                bool more = true;
                string error = null;
                try
                {
                    while (page.Count < PageNavigator.PageSize && !token.IsCancellationRequested)
                    {
                        if (!db.KeyScan.MoveNext()) { more = false; break; }
                        page.Add(db.KeyScan.Current.ToString());
                    }
                }
                catch (RedisException ex) { error = ex.Message; more = false; }
                return (Keys: page, More: more, Error: error, Canceled: token.IsCancellationRequested);
            });
            if (!outcome.IsSuccess) return;

            var result = outcome.Value;
            var existing = db.Keys.ToHashSet(StringComparer.Ordinal);
            db.Keys.AddRange(result.Keys.Where(existing.Add));
            db.HasMoreKeys = result.More;
            if (!db.HasMoreKeys) { db.KeyScan.Dispose(); db.KeyScan = null; }
            FilterKeys(node);
            node.Expand();
            if (result.Error != null) MessageBox.Show(this, string.Format(UiText.KeyScanFailedDetailFormat, result.Error), UiText.KeyScanFailedTitle);
            if (result.Canceled) toolStripStatusLabel1.Text = UiText.KeyScanPausedStatus;
        }

        private void BuildTreeNode_Folder(TreeNode parent)
        {
            if (parent.Tag is RedisFolder redis_folder)
            {
                if (parent.Nodes.Count > 0)
                {
                    return;
                }

                var qry = redis_folder.sub_items.
                Select(c => new
                {
                    SplitArray = c.Key.Split(':'),
                    Value = c
                }).
                Select(c => new
                {
                    Prefix = c.SplitArray.Length <= 1 ? "" : c.SplitArray.First(),
                    Postfix = c.Value.Key == "" ? "" :
                        (c.Value.Key.Replace(c.SplitArray.First(), "").Length > 0 && c.Value.Key.Replace(c.SplitArray.First(), "")[0] == ':') ?
                        c.Value.Key.Replace(c.SplitArray.First() + ":", "") :
                        c.Value.Key.Replace(c.SplitArray.First(), ""),
                    Value = c
                }).
                GroupBy(r => r.Prefix).
                Select(grp => new
                {
                    FolderName = grp.Key,
                    SubItems = grp.Count() > 1 ? grp.Select(t => new KeyValuePair<string, string>(t.Postfix, t.Value.Value.Value.ToString())) : null,
                    Value = grp.First().Value
                });

                List<TreeNode> list_folder_node = new List<TreeNode>();
                List<TreeNode> list_item_node = new List<TreeNode>();
                foreach (var item in qry)
                {
                    if (item.SubItems != null)
                    {
                        List<KeyValuePair<string, string>> items = new List<KeyValuePair<string, string>>();
                        foreach (var sub in item.SubItems)
						{
                            if (sub.Key == "")
							{
								TreeNode key_node = new TreeNode(sub.Value.ToString());
                                key_node.ImageKey = "keyword";
                                key_node.SelectedImageKey = "keyword";
                                key_node.Tag = new RedisKey();
								list_item_node.Add(key_node);
							}
                            else
							{
                                items.Add(sub);
							}
						}

                        // 폴더
                        int item_count = items.Count();
                        if (item_count > 0)
                        {
                            string node_name = $"{item.FolderName} ({item_count})";

                            RedisFolder folder = new RedisFolder();
                            folder.path_name = item.FolderName;
                            folder.sub_items = items;
                            folder.display_name = node_name;

                            TreeNode node = new TreeNode(node_name);
                            node.Tag = folder;
                            node.ImageKey = "Class_489";
                            node.SelectedImageKey = "Class_489";
                            list_folder_node.Add(node);
                        }
                    }
                    else
                    {
                        // 키
                        TreeNode node = new TreeNode(item.Value.Value.Value.ToString());
                        node.ImageKey = "keyword";
                        node.SelectedImageKey = "keyword";
                        node.Tag = new RedisKey();
                        list_item_node.Add(node);
                    }
                }

                list_folder_node.Sort((TreeNode x, TreeNode y) => x.Text.CompareTo(y.Text));
                list_item_node.Sort((TreeNode x, TreeNode y) => x.Text.CompareTo(y.Text));

                parent.Nodes.AddRange(list_folder_node.ToArray());
                parent.Nodes.AddRange(list_item_node.ToArray());
            }
        }

        private TreeNode GetTreeNode_DB(int dbNum, TreeNode select)
		{
			foreach (TreeNode node in select.Nodes)
			{
				if (node.Text.Contains($"db{dbNum} "))
				{
					return node;
				}
			}

			return null;
		}

        private bool HasTreeNode_DB(int dbNum, TreeNode select)
		{
            return GetTreeNode_DB(dbNum, select) != null;
		}

        private bool AddTreeNode_DB(RedisClient redisClient, int dbNum, TreeNode select, bool msg_box = true)
		{
            if (HasTreeNode_DB(dbNum, select))
			{
                return false;
			}

            try
            {
                if (redisClient.TryDatabaseSize(dbNum, out long keyCount) == false)
                {
                    if (msg_box)
                    {
                        MessageBox.Show(string.Format(UiText.KeyCountFailedFormat, dbNum));
                    }

                    return false;
                }

                TreeNode dbTree = new TreeNode(string.Format("db{0} ({1})", dbNum, keyCount));
                dbTree.ImageKey = "redis_db";
                dbTree.SelectedImageKey = "redis_db";
                dbTree.Tag = new DbSettings() { DBNumber = dbNum, Keys = null };

                select.Nodes.Add(dbTree);

                // Load keys only when the database is selected.
            }
            catch (RedisCommandException ex)
			{
                if (msg_box)
				{
                    MessageBox.Show(ex.Message);
                }

                return false;
			}

            return true;
		}

        private void BuildTreeNode_DB(TreeNode parent, List<string> keys)
        {
            if (parent.Nodes.Count > 0)
            {
                return;
            }

            var qry = keys.
            Select(c => new
            {
                SplitArray = c.Split(':'),
                Value = c
            }).
            Select(c => new
            {
                Prefix = c.SplitArray.Length <= 1 ? "" : c.SplitArray.First(),
                Postfix = c.Value == "" ? "" :
                    (c.Value.Replace(c.SplitArray.First(), "").Length > 0 && c.Value.Replace(c.SplitArray.First(), "")[0] == ':') ?
                    c.Value.Replace(c.SplitArray.First() + ":", "") :
                    c.Value.Replace(c.SplitArray.First(), ""),
                Value = c
            }).
            GroupBy(r => r.Prefix).
            Select(grp => new
            {
                FolderName = grp.Key,
                SubItems = grp.Count() > 1 ? grp.Select(t => new KeyValuePair<string, string>(t.Postfix, t.Value.Value.ToString())) : null,
                Value = grp.First().Value
            });

            List<TreeNode> list_folder_node = new List<TreeNode>();
            List<TreeNode> list_item_node = new List<TreeNode>();
            foreach (var item in qry)
            {
                if (item.SubItems != null)
                {
					List<KeyValuePair<string, string>> items = new List<KeyValuePair<string, string>>();
					foreach (var sub in item.SubItems)
					{
						if (sub.Key == "")
						{
							TreeNode key_node = new TreeNode(sub.Value.ToString());
							key_node.ImageKey = "keyword";
							key_node.SelectedImageKey = "keyword";
							key_node.Tag = new RedisKey();
							list_item_node.Add(key_node);
						}
						else
						{
							items.Add(sub);
						}
					}

					// 폴더
					int item_count = items.Count();
                    if (item_count > 0)
                    {
                        string node_name = $"{item.FolderName} ({item_count})";

                        RedisFolder folder = new RedisFolder();
                        folder.path_name = item.FolderName;
                        folder.sub_items = items;
                        folder.display_name = node_name;

                        TreeNode node = new TreeNode(node_name);
                        node.Tag = folder;
                        node.ImageKey = "Class_489";
                        node.SelectedImageKey = "Class_489";
                        list_folder_node.Add(node);
                    }
                }
                else
                {
                    // 키
                    TreeNode node = new TreeNode(item.Value.Value.ToString());
                    node.ImageKey = "keyword";
                    node.SelectedImageKey = "keyword";
                    node.Tag = new RedisKey();
                    list_item_node.Add(node);
                }
            }

            list_folder_node.Sort((TreeNode x, TreeNode y) => x.Text.CompareTo(y.Text));
            list_item_node.Sort((TreeNode x, TreeNode y) => x.Text.CompareTo(y.Text));

            parent.Nodes.AddRange(list_folder_node.ToArray());
            parent.Nodes.AddRange(list_item_node.ToArray());
        }

        // Show value

        private TreeNode get_db_setting_node(TreeNode select)
		{
            if (select.Parent == null)
			{
                return null;
			}

            if (select.Parent.Tag is DbSettings dbSettings)
			{
                return select.Parent;
			}

            return get_db_setting_node(select.Parent);
		}

    }
}
