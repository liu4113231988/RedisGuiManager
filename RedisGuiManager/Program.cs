using System;
using System.Windows.Forms;

namespace RedisGuiManager
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();

            // async void event handlers (grid paging, tree selection) would otherwise end the
            // process through Application.ThreadException, losing unsaved work silently.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (sender, e) => ReportCrash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (sender, e) => ReportCrash(e.ExceptionObject as Exception);

            // A batch killed mid-run leaves its key manifest behind; sweep the stale ones on start.
            FormMain.CleanupStaleBatchManifests();

            Application.Run(new FormMain());
        }

        private static void ReportCrash(Exception error)
        {
            if (error == null) return;

            try
            {
                MessageBox.Show(
                    error.Message + Environment.NewLine + Environment.NewLine + error.StackTrace,
                    "Unexpected error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
                // The message box can fail while the UI is being torn down; never mask the cause.
            }
        }
    }
}
