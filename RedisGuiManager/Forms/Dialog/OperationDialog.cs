using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RedisGuiManager
{
    public sealed class OperationDialog : Form
    {
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly Label status = new Label { Dock = DockStyle.Fill, Padding = new Padding(12), AutoEllipsis = true };
        private bool running = true;

        private OperationDialog(string title)
        {
            Text = title;
            ClientSize = new Size(460, 110);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false;
            var cancel = new Button { Text = "Cancel", Dock = DockStyle.Bottom, Height = 30 };
            cancel.Click += (s, e) => { cancellation.Cancel(); cancel.Enabled = false; status.Text = "Canceling at the next safe boundary…"; };
            Controls.Add(status);
            Controls.Add(cancel);
            FormClosing += (s, e) => { if (running) { cancellation.Cancel(); e.Cancel = true; } };
        }

        public static bool TryRun<T>(IWin32Window owner, string title, Func<CancellationToken, IProgress<string>, T> action, out T result)
        {
            using var dialog = new OperationDialog(title);
            T value = default;
            Exception error = null;
            dialog.Shown += async (s, e) =>
            {
                var progress = new Progress<string>(message => { if (!dialog.IsDisposed) dialog.status.Text = message; });
                try
                {
                    value = await Task.Run(() => action(dialog.cancellation.Token, progress));

                }
                catch (Exception ex) { error = ex; }
                finally { dialog.running = false; dialog.Close(); }
            };
            dialog.ShowDialog(owner);
            result = value;
            if (error is OperationCanceledException) return false;
            if (error != null) { MessageBox.Show(owner, error.Message, title + " failed"); return false; }
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) cancellation.Dispose();
            base.Dispose(disposing);
        }
    }
}
