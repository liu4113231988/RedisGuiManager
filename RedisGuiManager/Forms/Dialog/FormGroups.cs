using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace RedisGuiManager
{
    /// <summary>
    /// Creates, renames and deletes connection groups, and controls which connections belong to
    /// each group. Edits a snapshot and hands the result back to the caller rather than mutating
    /// live state, so cancelling really does cancel.
    ///
    /// A connection belongs to at most one group: the persistence format nests a group's members
    /// inside the group object, so a connection cannot be serialised into two groups at once.
    /// </summary>
    public partial class FormGroups : Form
    {
        private readonly List<GroupEdit> groups = new List<GroupEdit>();
        private readonly List<ConnectionEdit> connections = new List<ConnectionEdit>();

        private DataGridView groupGrid;
        private DataGridView memberGrid;
        private Label hint;

        /// <summary>One group being edited. Members are connection names.</summary>
        private sealed class GroupEdit
        {
            public string OriginalName;
            public string Name;
            public readonly List<string> Members = new List<string>();
            public bool Removed;
        }

        private sealed class ConnectionEdit
        {
            public string Name;
            public string Endpoint;
        }

        public FormGroups(
            IEnumerable<RedisGroup> sourceGroups,
            IEnumerable<RedisSettings> topLevelConnections)
        {
            // Snapshot: connection name -> owning group name (null when top level).
            foreach (RedisSettings settings in topLevelConnections ?? Enumerable.Empty<RedisSettings>())
            {
                if (settings == null || string.IsNullOrWhiteSpace(settings.name)) continue;
                connections.Add(new ConnectionEdit { Name = settings.name, Endpoint = Endpoint(settings) });
            }

            foreach (RedisGroup group in sourceGroups ?? Enumerable.Empty<RedisGroup>())
            {
                if (group == null || string.IsNullOrWhiteSpace(group.name)) continue;

                var edit = new GroupEdit { OriginalName = group.name, Name = group.name };
                foreach (RedisSettings settings in group.connections ?? new List<RedisSettings>())
                {
                    if (settings == null || string.IsNullOrWhiteSpace(settings.name)) continue;
                    edit.Members.Add(settings.name);
                    if (connections.All(c => !string.Equals(c.Name, settings.name, StringComparison.OrdinalIgnoreCase)))
                    {
                        connections.Add(new ConnectionEdit { Name = settings.name, Endpoint = Endpoint(settings) });
                    }
                }

                groups.Add(edit);
            }

            Text = "Connection groups";
            Icon = Icon.FromHandle(Properties.Resources.action_add_16xLG.GetHicon());
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(760, 620);
            MinimumSize = new Size(680, 520);

            BuildLayout();

            if (Config.darkmode > 0)
            {
                Utils.DarkThemeForm(this);
            }

            RefreshGroups();
        }

        private static string Endpoint(RedisSettings settings) =>
            $"{settings.host}:{settings.port}";

        /// <summary>
        /// The edited group list, with renamed/new groups applied and removed ones dropped.
        /// Callers replace their group collection with this and re-save.
        /// </summary>
        /// <param name="existingGroups">
        /// The live group objects. Reused where a group survives the edit so the tree keeps
        /// pointing at the same instances.
        /// </param>
        /// <param name="allKnownConnections">
        /// Every connection object that may be referenced, both top level and currently inside a
        /// group. Members are resolved against this, so a connection that already lives in a group
        /// is not silently dropped when that group is saved.
        /// </param>
        public List<RedisGroup> BuildResult(IList<RedisGroup> existingGroups, IEnumerable<RedisSettings> allKnownConnections)
        {
            var byName = (allKnownConnections ?? Enumerable.Empty<RedisSettings>())
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.name))
                .GroupBy(c => c.name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var result = new List<RedisGroup>();

            foreach (GroupEdit edit in groups.Where(g => !g.Removed))
            {
                // Reuse the existing instance so the live tree keeps pointing at the same object.
                RedisGroup target = existingGroups.FirstOrDefault(g =>
                    string.Equals(g.name, edit.OriginalName, StringComparison.OrdinalIgnoreCase));

                if (target == null)
                {
                    target = new RedisGroup { type = "group" };
                    result.Add(target);
                }
                else
                {
                    result.Add(target);
                }

                target.name = edit.Name;
                target.connections = edit.Members
                    .Select(memberName => byName.TryGetValue(memberName, out RedisSettings settings) ? settings : null)
                    .Where(settings => settings != null)
                    .ToList();
            }

            return result;
        }

        private void BuildLayout()
        {
            hint = new Label
            {
                Dock = DockStyle.Top,
                Height = 34,
                Padding = new Padding(10, 8, 0, 0),
                Text = "A connection can belong to one group only. Clearing it here moves it back to the top level."
            };

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 210
            };

            groupGrid = MakeGrid(false, ("Group", 420), ("Connections", 110));
            groupGrid.SelectionChanged += (s, e) => RefreshMembers();
            split.Panel1.Controls.Add(groupGrid);

            var memberButtons = new Panel { Dock = DockStyle.Bottom, Height = 34 };
            memberButtons.Controls.Add(MakeButton("Add existing connection…", (s, e) => AddConnectionToGroup(), 190));

            memberGrid = MakeGrid(true, ("In group", 90), ("Connection", 300), ("Endpoint", 160));
            memberGrid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                // Commit the checkbox straight away so the click is not lost on cell change.
                if (memberGrid.IsCurrentCellDirty)
                {
                    memberGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
            memberGrid.CellValueChanged += MemberCellContentChanged;
            split.Panel2.Controls.Add(memberGrid);
            split.Panel2.Controls.Add(memberButtons);

            var groupButtons = new Panel { Dock = DockStyle.Bottom, Height = 34 };
            groupButtons.Controls.Add(MakeButton("New group", (s, e) => AddGroup(), 100));
            var rename = MakeButton("Rename", (s, e) => RenameGroup(), 100);
            rename.Location = new Point(108, 0);
            groupButtons.Controls.Add(rename);
            var remove = MakeButton("Delete", (s, e) => DeleteGroup(), 100);
            remove.Location = new Point(216, 0);
            groupButtons.Controls.Add(remove);
            split.Panel1.Controls.Add(groupButtons);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 42 };
            var ok = MakeButton("Save", (s, e) => { DialogResult = DialogResult.OK; Close(); }, 100);
            ok.Location = new Point(10, 8);
            var cancel = MakeButton("Cancel", (s, e) => Close(), 100);
            cancel.Location = new Point(120, 8);
            bottom.Controls.Add(ok);
            bottom.Controls.Add(cancel);

            Controls.Add(split);
            Controls.Add(hint);
            Controls.Add(bottom);
        }

        private void RefreshGroups()
        {
            groupGrid.Rows.Clear();
            foreach (GroupEdit edit in groups.Where(g => !g.Removed))
            {
                groupGrid.Rows.Add(edit.Name, edit.Members.Count.ToString());
            }

            if (groupGrid.Rows.Count > 0)
            {
                groupGrid.Rows[0].Selected = true;
            }
            else
            {
                RefreshMembers();
            }
        }

        private GroupEdit SelectedGroup() =>
            groupGrid.SelectedRows.Count > 0
                ? groups.FirstOrDefault(g => !g.Removed && string.Equals(g.Name, groupGrid.SelectedRows[0].Cells[0].Value?.ToString(), StringComparison.OrdinalIgnoreCase))
                : null;

        private void RefreshMembers()
        {
            memberGrid.Rows.Clear();
            GroupEdit group = SelectedGroup();
            if (group == null) return;

            foreach (ConnectionEdit connection in connections)
            {
                bool member = group.Members.Contains(connection.Name, StringComparer.OrdinalIgnoreCase);
                memberGrid.Rows.Add(
                    member,
                    connection.Name,
                    connection.Endpoint);
            }
        }

        private void AddGroup()
        {
            using var input = new FormInputString { TextInfo = "Group name", InputValue = "" };
            if (input.ShowDialog(this) != DialogResult.OK) return;

            string name = input.InputValue?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show(this, "Group name can not be empty.", "New group", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (groups.Any(g => !g.Removed && string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, $"A group named \"{name}\" already exists.", "New group", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            groups.Add(new GroupEdit { OriginalName = null, Name = name });
            RefreshGroups();

            foreach (DataGridViewRow row in groupGrid.Rows)
            {
                if (string.Equals(row.Cells[0].Value?.ToString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    row.Selected = true;
                    break;
                }
            }

            RefreshMembers();
        }

        private void RenameGroup()
        {
            GroupEdit group = SelectedGroup();
            if (group == null)
            {
                MessageBox.Show(this, "Select a group first.", "Rename group", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var input = new FormInputString { TextInfo = "Group name", InputValue = group.Name };
            if (input.ShowDialog(this) != DialogResult.OK) return;

            string name = input.InputValue?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show(this, "Group name can not be empty.", "Rename group", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (groups.Any(g => !g.Removed && !ReferenceEquals(g, group) && string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, $"A group named \"{name}\" already exists.", "Rename group", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            group.Name = name;
            RefreshGroups();
            RefreshMembers();
        }

        private void DeleteGroup()
        {
            GroupEdit group = SelectedGroup();
            if (group == null)
            {
                MessageBox.Show(this, "Select a group first.", "Delete group", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int count = group.Members.Count;
            string warning = count == 0
                ? $"Delete the group \"{group.Name}\"?\r\n\r\nIt is empty, so nothing else changes."
                : $"Delete the group \"{group.Name}\"?\r\n\r\nIts {count} connection(s) are kept and move back to the top level.";

            if (MessageBox.Show(this, warning, "Delete group", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            group.Removed = true;
            RefreshGroups();
            RefreshMembers();
        }

        private void AddConnectionToGroup()
        {
            GroupEdit group = SelectedGroup();
            if (group == null)
            {
                MessageBox.Show(this, "Select a group first.", "Add connection", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Offers only connections that are not already in this group; each connection lives in
            // exactly one group, so there is nothing to move out here.
            var candidates = connections
                .Where(c => !group.Members.Contains(c.Name, StringComparer.OrdinalIgnoreCase))
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (candidates.Count == 0)
            {
                MessageBox.Show(this, "Every connection is already in this group.", "Add connection",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var picker = new FormPickConnection(candidates))
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                string chosen = picker.SelectedName;
                if (string.IsNullOrEmpty(chosen)) return;
                if (candidates.All(c => !string.Equals(c.Name, chosen, StringComparison.OrdinalIgnoreCase))) return;

                group.Members.Add(chosen);
                RefreshGroups();
                RefreshMembers();
            }
        }

        /// <summary>Minimal single-choice list of connection names.</summary>
        private sealed class FormPickConnection : Form
        {
            private readonly ComboBox picker;

            public string SelectedName => picker.SelectedItem?.ToString();

            public FormPickConnection(IReadOnlyList<ConnectionEdit> candidates)
            {
                Text = "Add connection";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterParent;
                ClientSize = new Size(360, 150);
                MaximizeBox = false;
                MinimizeBox = false;

                var label = new Label { Text = "Connection to add", Location = new Point(12, 18), AutoSize = true };
                picker = new ComboBox
                {
                    Location = new Point(12, 42),
                    Width = 330,
                    DropDownStyle = ComboBoxStyle.DropDownList
                };
                foreach (ConnectionEdit candidate in candidates)
                {
                    picker.Items.Add(candidate.Name);
                }

                if (picker.Items.Count > 0) picker.SelectedIndex = 0;

                var ok = new Button { Text = "Add", Location = new Point(160, 108), Width = 85, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Location = new Point(255, 108), Width = 85, DialogResult = DialogResult.Cancel };
                Controls.Add(label);
                Controls.Add(picker);
                Controls.Add(ok);
                Controls.Add(cancel);
                AcceptButton = ok;
                CancelButton = cancel;
            }
        }

        private void MemberCellContentChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            GroupEdit group = SelectedGroup();
            if (group == null) return;

            string name = memberGrid.Rows[e.RowIndex].Cells[1].Value?.ToString();
            if (string.IsNullOrEmpty(name)) return;

            bool member = Convert.ToBoolean(memberGrid.Rows[e.RowIndex].Cells[0].Value ?? false);

            if (member)
            {
                if (group.Members.Contains(name, StringComparer.OrdinalIgnoreCase) == false)
                {
                    group.Members.Add(name);
                }
            }
            else
            {
                group.Members.RemoveAll(m => string.Equals(m, name, StringComparison.OrdinalIgnoreCase));
            }

            UpdateGroupCount(group);
        }

        private void UpdateGroupCount(GroupEdit group)
        {
            foreach (DataGridViewRow row in groupGrid.Rows)
            {
                if (string.Equals(row.Cells[0].Value?.ToString(), group.Name, StringComparison.OrdinalIgnoreCase))
                {
                    row.Cells[1].Value = group.Members.Count.ToString();
                    return;
                }
            }
        }

        private static Button MakeButton(string text, EventHandler handler, int width)
        {
            var button = new Button { Text = text, Width = width, Height = 26, UseVisualStyleBackColor = true };
            button.Click += handler;
            return button;
        }

        private static DataGridView MakeGrid(bool firstColumnCheckBox, params (string Header, int Width)[] columns)
        {
            var grid = new DataGridView
            {
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                Dock = DockStyle.Fill,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                BackgroundColor = Color.White,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };

            foreach (var column in columns)
            {
                if (firstColumnCheckBox && column == columns[0])
                {
                    grid.Columns.Add(new DataGridViewCheckBoxColumn
                    {
                        HeaderText = column.Header,
                        Width = column.Width,
                        ThreeState = false
                    });
                }
                else
                {
                    grid.Columns.Add(new DataGridViewTextBoxColumn
                    {
                        HeaderText = column.Header,
                        Width = column.Width,
                        ReadOnly = true
                    });
                }
            }

            return grid;
        }
    }
}
