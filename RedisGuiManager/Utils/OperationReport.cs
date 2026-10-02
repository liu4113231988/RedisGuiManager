using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace RedisGuiManager
{
    public sealed class OperationReport
    {
        public int Success { get; set; }
        public int Skipped { get; set; }
        public bool Canceled { get; set; }
        public List<string> Errors { get; } = new List<string>();

        public void Show(IWin32Window owner, string title)
        {
            using var form = new Form { Text = title, Width = 760, Height = 420, StartPosition = FormStartPosition.CenterParent };
            var summary = new Label { Dock = DockStyle.Top, Height = 42, Text = $"{(Canceled ? "Canceled; completed changes remain" : "Completed")} · Success: {Success} · Skipped: {Skipped} · Failed: {Errors.Count}" };
            var details = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true };
            details.Items.AddRange(Errors.Cast<object>().ToArray());
            var save = new Button { Text = "Save full report…", Dock = DockStyle.Bottom, Height = 32 };
            save.Click += (s, e) =>
            {
                using var dialog = new SaveFileDialog { Filter = "Text files (*.txt)|*.txt", FileName = "redis_operation_report.txt" };
                if (dialog.ShowDialog(form) != DialogResult.OK) return;
                try { File.WriteAllLines(dialog.FileName, new[] { summary.Text }.Concat(Errors)); }
                catch (Exception ex) { MessageBox.Show(form, ex.Message, "Report save failed"); }
            };
            form.Controls.Add(details);
            form.Controls.Add(summary);
            form.Controls.Add(save);
            form.ShowDialog(owner);
        }
    }
}
