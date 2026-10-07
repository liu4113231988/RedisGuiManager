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
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly Label status = new Label { Dock = DockStyle.Fill, Padding = new Padding(12), AutoEllipsis = true };

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
                    if (!dialog.IsDisposed && dialog.IsHandleCreated) dialog.status.Text = message;
                });

                if (showProgressWindow)
                {
                    // Show() rather than ShowDialog(): the caller keeps running and the user can
                    // keep working while the operation proceeds.
                    dialog.Show(Control.FromHandle(owner?.Handle ?? IntPtr.Zero));
                }

                value = await Task.Run(() => action(dialog.cancellation.Token, progress));
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                if (!dialog.IsDisposed) dialog.Close();
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