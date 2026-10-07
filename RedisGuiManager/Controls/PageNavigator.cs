using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using RedisGuiManager.Properties;

namespace RedisGuiManager
{
    public sealed class PageNavigator : FlowLayoutPanel
    {
        public const int PageSize = 500;
        public int Offset { get; private set; }
        private readonly Button previous = new Button { Text = UiText.PagerPrevious, AutoSize = true };
        private readonly Button next = new Button { Text = UiText.PagerNext, AutoSize = true };
        private readonly Label page = new Label { AutoSize = true, Padding = new Padding(8) };
        private bool committed;
        private bool navigating;

        /// <summary>
        /// Raised when the page changes. Handlers may be asynchronous (loading a page from Redis),
        /// in which case the navigator waits for them before committing the offset.
        /// </summary>
        public event Func<Task> PageChanged;

        /// <summary>
        /// Raised when <see cref="PageChanged"/> throws. The navigator has already rolled its offset
        /// back, so the owner only has to surface the failure.
        /// </summary>
        public event Action<Exception> LoadFailed;

        public Func<bool> CanNavigate { get; set; }

        public PageNavigator()
        {
            Dock = DockStyle.Bottom;
            Height = 36;
            Controls.AddRange(new Control[] { previous, next, page });
            previous.Click += async (s, e) => await NavigateAsync(Math.Max(0, Offset - PageSize));
            next.Click += async (s, e) => await NavigateAsync(Offset + PageSize);
            UpdatePage(false);
        }

        /// <summary>
        /// Moves to <paramref name="offset"/>. If the <see cref="PageChanged"/> handler does not
        /// call <see cref="UpdatePage"/> (for example because the user cancelled the load), the
        /// offset is rolled back so the navigator stays consistent with what is on screen.
        /// </summary>
        public async Task<bool> NavigateAsync(int offset)
        {
            // Ignore extra clicks while a page load is still running, otherwise two loads would
            // interleave and paint the wrong rows.
            if (navigating) return false;
            if (CanNavigate != null && !CanNavigate()) return false;

            // Clamp defensively: a negative offset would make Read() skip nothing and page backwards.
            offset = Math.Max(0, offset);

            navigating = true;
            int old = Offset;
            Offset = offset;
            committed = false;
            try
            {
                if (PageChanged != null)
                {
                    await PageChanged();
                }
            }
            catch (Exception ex)
            {
                // The click handlers are async void, so rethrowing here would end up on
                // Application.ThreadException and take the process down. Report instead.
                Offset = old;
                LoadFailed?.Invoke(ex);
                return false;
            }
            finally
            {
                navigating = false;
            }

            if (!committed) Offset = old;
            return committed;
        }

        public void Reset() { Offset = 0; UpdatePage(false); }

        public void UpdatePage(bool more)
        {
            committed = true;
            previous.Enabled = Offset > 0;
            next.Enabled = more;
            page.Text = string.Format(UiText.PagerPageFormat, Offset / PageSize + 1);
        }

        /// <summary>
        /// Reads at most one page worth of items starting at <see cref="Offset"/>, plus one extra
        /// item so the caller can tell whether another page exists. Prefer cursor-based paging for
        /// large collections; this replays the source from the beginning on every page.
        /// </summary>
        public T[] Read<T>(IEnumerable<T> source, CancellationToken token)
        {
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