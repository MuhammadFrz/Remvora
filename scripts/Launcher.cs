using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace Remvora.Launcher
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string targetExe = Path.Combine(baseDir, "app", "Remvora.App.exe");
            if (!File.Exists(targetExe))
            {
                targetExe = Path.Combine(baseDir, "Remvora.App.exe");
            }

            if (!File.Exists(targetExe))
            {
                MessageBox.Show(
                    "Remvora application files could not be found in the 'app' directory.\nExpected: " + targetExe,
                    "Remvora Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 1;
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = targetExe,
                WorkingDirectory = Path.GetDirectoryName(targetExe),
                Arguments = string.Join(" ", args),
                UseShellExecute = false
            };

            try
            {
                Process.Start(startInfo);
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Failed to launch Remvora: " + ex.Message,
                    "Remvora Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
