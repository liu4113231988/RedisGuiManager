using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RedisGuiManager
{
    /// <summary>
    /// Result of a non-modal background operation. Distinguishes success, user cancellation
    /// and failure without resorting to exceptions.
    /// </summary>
    public readonly struct OperationOutcome<T>
    {
        public bool IsSuccess { get; }
        public bool IsCanceled { get; }
        public string Error { get; }
        public T Value { get; }

        private OperationOutcome(bool success, bool canceled, string error, T value)
        {
            IsSuccess = success;
            IsCanceled = canceled;
            Error = error;
            Value = value;
        }

        public static OperationOutcome<T> Succeeded(T value) => new OperationOutcome<T>(true, false, null, value);
        public static OperationOutcome<T> Canceled(T value) => new OperationOutcome<T>(false, true, null, value);
        public static OperationOutcome<T> Failed(string error) => new OperationOutcome<T>(false, false, error, default);
    }

    /// <summary>
    /// Progress window for long-running Redis operations. It is shown non-modally, so the main
    /// window stays interactive (scrolling, switching nodes) while work runs in the background.
    /// </summary>
    public sealed class OperationDialog : Form
    {
        /// <summary>
        /// How long an operation may run before the progress window appears. Showing it
        /// immediately made every quick tree selection flash an empty dialog on screen.
        /// </summary>
        private const int ShowDelayMilliseconds = 250;

        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly Label status = new Label { Dock = DockStyle.Fill, Padding = new Padding(12), AutoEllipsis = true };

        /// <summary>Last progress text, buffered so a window revealed late still starts with content.</summary>
        private string pendingMessage;

        private OperationDialog(string title)
        {
            Text = title;
            ClientSize = new Size(460, 110);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false;
            ShowInTaskbar = false;
            var cancel = new Button { Text = "Cancel", Dock = DockStyle.Bottom, Height = 30 };
            cancel.Click += (s, e) => { cancellation.Cancel(); cancel.Enabled = false; status.Text = "Canceling at the next safe boundary…"; };
            Controls.Add(status);
            Controls.Add(cancel);
        }

        protected override bool ShowWithoutActivation => true;

        /// <summary>
        /// Runs <paramref name="action"/> on a background thread behind a non-modal progress
        /// window. Must be awaited from the UI thread.
        /// </summary>
        public static async Task<OperationOutcome<T>> RunAsync<T>(
            IWin32Window owner,
            string title,
            Func<CancellationToken, IProgress<string>, T> action,
            bool showProgressWindow = true)
        {
            var dialog = new OperationDialog(title);
            T value = default;
            Exception error = null;

            try
            {
                var progress = new Progress<string>(message =>
                {
                    // IsDisposed stays false after the user closes the window with X, so check the
                    // handle instead: writing to a destroyed control would throw.
                    if (dialog.IsDisposed) return;

                    // Before the window exists the text is buffered, otherwise a dialog revealed
                    // after the first report would open empty.
                    if (dialog.IsHandleCreated) dialog.status.Text = message;
                    else dialog.pendingMessage = message;
                });

                System.Windows.Forms.Timer reveal = null;
                if (showProgressWindow)
                {
                    Control parent = null;
                    try { parent = Control.FromHandle(owner?.Handle ?? IntPtr.Zero); }
                    catch { /* a destroyed owner simply means the dialog runs unowned */ }

                    // Show() rather than ShowDialog(): the caller keeps running and the user can keep
                    // working while the operation proceeds. The reveal is delayed and is cancelled
                    // when the work finishes first, so quick operations never flash a window.
                    reveal = new System.Windows.Forms.Timer { Interval = ShowDelayMilliseconds };
                    reveal.Tick += (s, e) =>
                    {
                        ((System.Windows.Forms.Timer)s).Stop();
                        if (dialog.IsDisposed || dialog.IsHandleCreated) return;

                        if (string.IsNullOrEmpty(dialog.pendingMessage) == false)
                        {
                            dialog.status.Text = dialog.pendingMessage;
                        }

                        try { dialog.Show(parent); }
                        catch { /* the owner disappeared; the operation still runs to completion */ }
                    };
                    reveal.Start();
                }

                try
                {
                    value = await Task.Run(() => action(dialog.cancellation.Token, progress));
                }
                finally
                {
                    if (reveal != null)
                    {
                        reveal.Stop();
                        reveal.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                // A fast operation never revealed the window, so only close a form that was actually
                // shown; Dispose() alone is enough for the rest.
                if (!dialog.IsDisposed && dialog.IsHandleCreated) dialog.Close();
                dialog.Dispose();
            }

            if (error is OperationCanceledException) return OperationOutcome<T>.Canceled(value);
            if (error != null)
            {
                // The owner may already be closing; MessageBox.Show would then throw.
                if (owner is Control alive && alive.IsDisposed == false && alive.IsHandleCreated)
                {
                    MessageBox.Show(alive, error.Message, title + " failed");
                }
                return OperationOutcome<T>.Failed(error.Message);
            }

            return OperationOutcome<T>.Succeeded(value);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                cancellation.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}