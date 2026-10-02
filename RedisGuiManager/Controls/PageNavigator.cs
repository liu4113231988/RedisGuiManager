using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace RedisGuiManager
{
    public sealed class PageNavigator : FlowLayoutPanel
    {
        public const int PageSize = 500;
        public int Offset { get; private set; }
        private readonly Button previous = new Button { Text = "Previous", AutoSize = true };
        private readonly Button next = new Button { Text = "Next", AutoSize = true };
        private readonly Label page = new Label { AutoSize = true, Padding = new Padding(8) };
        private bool committed;
        public event Action PageChanged;
        public Func<bool> CanNavigate { get; set; }

        public PageNavigator()
        {
            Dock = DockStyle.Bottom;
            Height = 36;
            Controls.AddRange(new Control[] { previous, next, page });
            previous.Click += (s, e) => Navigate(Math.Max(0, Offset - PageSize));
            next.Click += (s, e) => Navigate(Offset + PageSize);
            UpdatePage(false);
        }

        private void Navigate(int offset)
        {
            if (CanNavigate != null && !CanNavigate()) return;
            int old = Offset;
            Offset = offset;
            committed = false;
            try { PageChanged?.Invoke(); if (!committed) Offset = old; }
            catch { Offset = old; throw; }
        }

        public void Reset() { Offset = 0; UpdatePage(false); }
        public void UpdatePage(bool more)
        {
            committed = true;
            previous.Enabled = Offset > 0;
            next.Enabled = more;
            page.Text = $"Page {Offset / PageSize + 1} · search applies to this page";
        }

        public T[] Read<T>(IEnumerable<T> source, CancellationToken token)
        {
            // ponytail: scans replay earlier pages; use retained SCAN cursors if deep paging becomes common.
            var values = new List<T>();
            int skipped = 0;
            foreach (var item in source)
            {
                token.ThrowIfCancellationRequested();
                if (skipped++ < Offset) continue;
                values.Add(item);
                if (values.Count > PageSize) break;
            }
            return values.ToArray();
        }
    }
}
