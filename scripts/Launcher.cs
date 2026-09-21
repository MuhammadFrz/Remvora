using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Remvora.Launcher
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string standardInstallDir = Path.Combine(localAppData, "Programs", "Remvora");
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string systemInstallDir = Path.Combine(programFiles, "Remvora");

            bool isInstalledLocation = baseDir.Equals(standardInstallDir, StringComparison.OrdinalIgnoreCase) ||
                                       baseDir.Equals(systemInstallDir, StringComparison.OrdinalIgnoreCase);

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

            // If not running from installed folder and no silent flag passed, ask user if they want to install
            bool hasNoInstallFlag = false;
            foreach (string arg in args)
            {
                if (arg.Equals("--no-install", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("--portable", StringComparison.OrdinalIgnoreCase))
                {
                    hasNoInstallFlag = true;
                    break;
                }
            }

            if (!isInstalledLocation && !hasNoInstallFlag)
            {
                DialogResult choice = MessageBox.Show(
                    "Would you like to install Remvora on this computer?\n\n" +
                    "• Click 'Yes' to install Remvora (adds Start Menu shortcut and registers in Windows Apps).\n" +
                    "• Click 'No' to run directly without installing.",
                    "Remvora Setup",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);

                if (choice == DialogResult.Cancel)
                {
                    return 0;
                }

                if (choice == DialogResult.Yes)
                {
                    try
                    {
                        string installedExe = InstallToLocalAppData(baseDir);
                        if (File.Exists(installedExe))
                        {
                            targetExe = installedExe;
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            "Installation encountered an issue: " + ex.Message + "\n\nLaunching application directly...",
                            "Remvora Setup Notice",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                }
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

        private static string InstallToLocalAppData(string sourceDir)
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string installDir = Path.Combine(localAppData, "Programs", "Remvora");
            string appInstallDir = Path.Combine(installDir, "app");

            Directory.CreateDirectory(installDir);
            Directory.CreateDirectory(appInstallDir);

            // Copy app folder
            string sourceApp = Path.Combine(sourceDir, "app");
            if (Directory.Exists(sourceApp))
            {
                CopyDirectory(sourceApp, appInstallDir);
            }
            else
            {
                // Flat source fallback
                CopyDirectory(sourceDir, appInstallDir);
            }

            // Copy root launcher and scripts
            string sourceLauncher = Path.Combine(sourceDir, "Remvora.exe");
            string targetLauncher = Path.Combine(installDir, "Remvora.exe");
            if (File.Exists(sourceLauncher))
            {
                File.Copy(sourceLauncher, targetLauncher, true);
            }

            string sourceUninstall = Path.Combine(sourceDir, "uninstall.ps1");
            string targetUninstall = Path.Combine(installDir, "uninstall.ps1");
            if (File.Exists(sourceUninstall))
            {
                File.Copy(sourceUninstall, targetUninstall, true);
            }

            // Create Start Menu Shortcut
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string startMenuPrograms = Path.Combine(appData, "Microsoft", "Windows", "Start Menu", "Programs");
            string shortcutPath = Path.Combine(startMenuPrograms, "Remvora.lnk");

            string shortcutTarget = File.Exists(targetLauncher) ? targetLauncher : Path.Combine(appInstallDir, "Remvora.App.exe");
            CreateShortcut(shortcutPath, shortcutTarget, installDir);

            // Register in Windows Installed Apps
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Remvora"))
            {
                if (key != null)
                {
                    key.SetValue("DisplayName", "Remvora");
                    key.SetValue("DisplayVersion", "1.0.0");
                    key.SetValue("Publisher", "Remvora");
                    key.SetValue("InstallLocation", installDir);
                    key.SetValue("DisplayIcon", shortcutTarget);
                    key.SetValue("UninstallString", "powershell.exe -ExecutionPolicy Bypass -File \"" + targetUninstall + "\"");
                    key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                }
            }

            return shortcutTarget;
        }

        private static void CreateShortcut(string shortcutPath, string targetPath, string workingDir)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null)
                {
                    object shell = Activator.CreateInstance(shellType);
                    object shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                    Type shortcutType = shortcut.GetType();
                    shortcutType.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
                    shortcutType.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { workingDir });
                    shortcutType.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { targetPath + ",0" });
                    shortcutType.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { "Remvora - Windows Uninstaller and Cleanup Utility" });
                    shortcutType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
                }
            }
            catch
            {
                // Fallback: non-fatal if shortcut creation fails
            }
        }

        private static void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string targetFile = Path.Combine(targetDir, Path.GetFileName(file));
                File.Copy(file, targetFile, true);
            }

            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string targetSubDir = Path.Combine(targetDir, Path.GetFileName(subDir));
                CopyDirectory(subDir, targetSubDir);
            }
        }
    }
}
