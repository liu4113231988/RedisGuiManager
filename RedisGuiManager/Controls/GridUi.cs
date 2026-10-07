using System;
using System.Drawing;
using System.Windows.Forms;
using RedisGuiManager.Properties;

namespace RedisGuiManager
{
    /// <summary>
    /// Shared grid behaviour that was previously copy-pasted into every value editor: long-value
    /// truncation, the right-click menu, and the incremental "find next" search.
    /// </summary>
    public static class GridUi
    {
        /// <summary>
        /// Redis values are frequently megabytes long; painting them in full stalls the grid.
        /// </summary>
        public const int MaxCellLength = 10000;

        /// <summary>
        /// Truncates over-long cell text for display. Attach once per grid instead of hand-writing a
        /// CellFormatting handler in every editor.
        /// </summary>
        public static void LimitCellText(DataGridView grid)
        {
            if (grid == null) return;

            grid.CellFormatting += (sender, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

                // Only strings can exceed the limit; leaving other types alone preserves their
                // formatting (scores, indexes) instead of stringifying them.
                if (e.Value is string value && value.Length > MaxCellLength)
                {
                    e.Value = value.Substring(0, MaxCellLength) + "... [" + (value.Length - MaxCellLength) + " more chars]";
                }
            };
        }

        /// <summary>
        /// Shows a value context menu that releases itself once dismissed, so repeated right-clicks
        /// do not leak a handle each time.
        /// </summary>
        public static void ShowValueContextMenu(Control owner, Point location, Action onOpenViewer, Action onRemove = null)
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add(UiText.MenuJsonViewer, null, (s, e) => onOpenViewer?.Invoke());
            if (onRemove != null)
            {
                menu.Items.Add(UiText.MenuRemoveSelectedKeys, null, (s, e) => onRemove());
            }

            // Show() is asynchronous, so the menu is only disposed after it closes.
            menu.Closed += (s, e) => menu.Dispose();
            menu.Show(owner, location);
        }

        /// <summary>
        /// Attaches the whole "right-click a cell -> open the JSON viewer" behaviour to a grid so
        /// each value editor does not re-implement the mouse handling. Right-clicking outside the
        /// current selection first moves the selection onto the clicked row.
        /// </summary>
        public static void AttachRowValueMenu(DataGridView grid, Func<string> selectedValue, Action onRemove = null)
        {
            if (grid == null) return;

            grid.CellMouseUp += (sender, e) =>
            {
                if (e.Button != MouseButtons.Right || e.RowIndex < 0 || e.ColumnIndex < 0) return;

                grid.Rows[e.RowIndex].Selected = true;
                ShowAtCell(grid, e, () => OpenJsonViewer(selectedValue), onRemove);
            };
        }

        /// <summary>
        /// Cell-selection variant used by the query window, whose grid tracks the exact cell the user
        /// clicked rather than whole rows.
        /// </summary>
        public static void AttachCellValueMenu(DataGridView grid, Func<string> selectedValue, Action onRemove = null)
        {
            if (grid == null) return;

            grid.CellMouseUp += (sender, e) =>
            {
                if (e.Button != MouseButtons.Right || e.RowIndex < 0 || e.ColumnIndex < 0) return;

                if (grid.SelectedCells.Count != 1)
                {
                    grid.ClearSelection();
                    grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Selected = true;
                }

                ShowAtCell(grid, e, () => OpenJsonViewer(selectedValue), onRemove);
            };
        }

        private static void ShowAtCell(DataGridView grid, DataGridViewCellMouseEventArgs e, Action onOpenViewer, Action onRemove)
        {
            Rectangle cellRect = grid.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, true);
            Point location = new Point(cellRect.Left + e.Location.X, cellRect.Top + e.Location.Y);
            ShowValueContextMenu(grid, location, onOpenViewer, onRemove);
        }

        private static void OpenJsonViewer(Func<string> selectedValue)
        {
            if (selectedValue == null) return;

            FormJsonViewer viewer = new FormJsonViewer();
            viewer.Show();
            viewer.JsonText = selectedValue() ?? "";
        }

        /// <summary>
        /// Tracks the state of the incremental "Enter finds the next match" search so repeated
        /// presses walk forward through the rows and then wrap around once.
        /// </summary>
        public sealed class SearchState
        {
            private int lastIndex = -1;
            private string condition = string.Empty;

            /// <summary>
            /// Returns true when the caller should restart from the top, which happens when the
            /// query text changed or was cleared.
            /// </summary>
            public bool Accept(string searchText)
            {
                if (string.IsNullOrEmpty(searchText))
                {
                    Reset();
                    return true;
                }

                if (condition == searchText) return false;

                condition = searchText;
                lastIndex = -1;
                return true;
            }

            public void Reset()
            {
                condition = string.Empty;
                lastIndex = -1;
            }

            public int LastIndex => lastIndex;

            /// <summary>Records where the search stopped so the next Enter continues from there.</summary>
            public void SetPosition(int index) => lastIndex = index;

            /// <summary>
            /// Finds the text at or after startIndex, optionally only inspecting the first column.
            /// Returns the row index, or -1.
            /// </summary>
            public int Find(DataGridViewRowCollection rows, string searchText, int startIndex, int columnCount, bool firstColumnOnly)
            {
                if (string.IsNullOrEmpty(searchText)) return -1;
                int last = rows.Count - 1;

                for (int i = Math.Max(0, startIndex); i <= last; i++)
                {
                    if (MatchesAt(rows[i], searchText, columnCount, firstColumnOnly))
                    {
                        return i;
                    }
                }

                return -1;
            }

            private static bool MatchesAt(DataGridViewRow row, string searchText, int columnCount, bool firstColumnOnly)
            {
                int limit = firstColumnOnly ? 1 : Math.Max(1, columnCount);
                for (int c = 0; c < limit && c < row.Cells.Count; c++)
                {
                    string text = row.Cells[c].Value?.ToString();
                    if (text != null && text.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// "Enter jumps to the next match and wraps once" - the behaviour every value editor's
        /// search box had duplicated.
        /// </summary>
        public static void FindNextMatch(DataGridView grid, TextBox searchBox, SearchState state, bool firstColumnOnly)
        {
            string searchText = searchBox.Text;

            if (string.IsNullOrEmpty(searchText))
            {
                ShowAllRows(grid);
                state.Reset();
                return;
            }

            bool restart = state.Accept(searchText);
            int start = restart ? 0 : state.LastIndex + 1;

            int rowIndex = state.Find(grid.Rows, searchText, start, grid.Columns.Count, firstColumnOnly);

            // Wrap around once, then give up.
            if (rowIndex < 0 && !restart)
            {
                state.Reset();
                rowIndex = state.Find(grid.Rows, searchText, 0, grid.Columns.Count, firstColumnOnly);
            }

            if (rowIndex < 0) return;

            DataGridViewRow match = grid.Rows[rowIndex];
            match.Selected = true;
            // Assigning CurrentCell scrolls the matched row into view.
            grid.CurrentCell = match.Cells[firstColumnOnly ? 0 : FirstMatchColumn(match, searchText)];
            state.SetPosition(rowIndex);
        }

        private static int FirstMatchColumn(DataGridViewRow row, string searchText)
        {
            foreach (DataGridViewCell cell in row.Cells)
            {
                string text = cell.Value?.ToString();
                if (text != null && text.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return cell.OwningColumn.Index;
                }
            }

            return 0;
        }

        /// <summary>"Show only the rows containing the text" filter.</summary>
        public static void FilterRows(DataGridView grid, TextBox searchBox, bool firstColumnOnly)
        {
            string searchText = searchBox.Text;

            if (string.IsNullOrEmpty(searchText))
            {
                ShowAllRows(grid);
                return;
            }

            foreach (DataGridViewRow row in grid.Rows)
            {
                row.Visible = RowMatches(row, searchText, grid.Columns.Count, firstColumnOnly);
            }

            grid.ClearSelection();
        }

        private static void ShowAllRows(DataGridView grid)
        {
            foreach (DataGridViewRow row in grid.Rows) row.Visible = true;
        }

        private static bool RowMatches(DataGridViewRow row, string searchText, int columnCount, bool firstColumnOnly)
        {
            int limit = firstColumnOnly ? 1 : Math.Max(1, columnCount);
            for (int c = 0; c < limit && c < row.Cells.Count; c++)
            {
                string text = row.Cells[c].Value?.ToString();
                if (text != null && text.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}