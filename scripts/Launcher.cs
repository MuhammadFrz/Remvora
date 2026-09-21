using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Remvora")]
[assembly: AssemblyProduct("Remvora")]
[assembly: AssemblyCompany("Remvora")]
[assembly: AssemblyCopyright("Copyright © 2026 Remvora")]
[assembly:AssemblyVersion("1.2.1.0")]
[assembly: AssemblyFileVersion("1.2.1.0")]
[assembly: AssemblyInformationalVersion("1.2.1")]

namespace Remvora.Launcher
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

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

            // Check for atomic update command
            foreach (string arg in args)
            {
                if (arg.Equals("--apply-update", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("/apply-update", StringComparison.OrdinalIgnoreCase))
                {
                    return ApplyStagedUpdate(baseDir, localAppData, targetExe);
                }
            }

            bool hasNoInstallFlag = false;
            bool forceSetup = false;
            foreach (string arg in args)
            {
                if (arg.Equals("--no-install", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("--portable", StringComparison.OrdinalIgnoreCase))
                {
                    hasNoInstallFlag = true;
                }
                else if (arg.Equals("--setup", StringComparison.OrdinalIgnoreCase) ||
                         arg.Equals("/setup", StringComparison.OrdinalIgnoreCase))
                {
                    forceSetup = true;
                }
            }

            // If already installed and setup is not forced, launch app directly
            if (isInstalledLocation && !forceSetup)
            {
                if (!File.Exists(targetExe))
                {
                    MessageBox.Show(
                        "Remvora application binary could not be found.\nExpected: " + targetExe,
                        "Remvora Launch Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return 1;
                }

                return LaunchProcess(targetExe, args);
            }

            // If user explicitly asked for portable / no-install mode
            if (hasNoInstallFlag)
            {
                if (!File.Exists(targetExe))
                {
                    MessageBox.Show(
                        "Remvora application binary could not be found in 'app' directory.\nExpected: " + targetExe,
                        "Remvora Launch Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return 1;
                }

                return LaunchProcess(targetExe, args);
            }

            // Show Modern Minimal Setup Wizard
            using (SetupWizardForm wizard = new SetupWizardForm(baseDir, standardInstallDir, targetExe, args))
            {
                DialogResult result = wizard.ShowDialog();
                if (result == DialogResult.OK && wizard.ShouldLaunchAfterExit)
                {
                    string launchedExe = wizard.InstalledExePath;
                    if (string.IsNullOrEmpty(launchedExe) || !File.Exists(launchedExe))
                    {
                        launchedExe = targetExe;
                    }

                    if (File.Exists(launchedExe))
                    {
                        return LaunchProcess(launchedExe, args);
                    }
                }
            }

            return 0;
        }

        private static int LaunchProcess(string exePath, string[] args)
        {
            System.Collections.Generic.List<string> appArgs = new System.Collections.Generic.List<string>();
            foreach (string arg in args)
            {
                if (!arg.Equals("--no-install", StringComparison.OrdinalIgnoreCase) &&
                    !arg.Equals("--portable", StringComparison.OrdinalIgnoreCase) &&
                    !arg.Equals("--setup", StringComparison.OrdinalIgnoreCase) &&
                    !arg.Equals("/setup", StringComparison.OrdinalIgnoreCase))
                {
                    appArgs.Add(arg);
                }
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = Path.GetDirectoryName(exePath),
                Arguments = string.Join(" ", appArgs.ToArray()),
                UseShellExecute = true
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

        private static int ApplyStagedUpdate(string baseDir, string localAppData, string targetExe)
        {
            try
            {
                string stagingDir = Path.Combine(baseDir, "updates", "staging");
                if (!Directory.Exists(stagingDir))
                {
                    stagingDir = Path.Combine(localAppData, "Remvora", "updates", "staging");
                }

                if (!Directory.Exists(stagingDir))
                {
                    if (File.Exists(targetExe))
                    {
                        return LaunchProcess(targetExe, new string[0]);
                    }
                    return 0;
                }

                // Wait up to 6 seconds for running Remvora.App to exit so files are unlocked
                WaitForProcessExit("Remvora.App", 6000);

                string appDir = Path.Combine(baseDir, "app");
                if (!Directory.Exists(appDir))
                {
                    appDir = baseDir;
                }

                // Copy all staged files into target app directory
                string[] stagedFiles = Directory.GetFiles(stagingDir, "*.*", SearchOption.AllDirectories);
                foreach (string file in stagedFiles)
                {
                    string fileName = Path.GetFileName(file);
                    if (fileName.Equals("update.pending", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string relativePath = file.Substring(stagingDir.Length).TrimStart('\\', '/');
                    string destPath = Path.Combine(appDir, relativePath);
                    string destDir = Path.GetDirectoryName(destPath);
                    if (!Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    File.Copy(file, destPath, true);
                }

                // Clean up staging
                try
                {
                    Directory.Delete(stagingDir, true);
                }
                catch
                {
                    // Ignored
                }

                // Launch updated application
                if (File.Exists(targetExe))
                {
                    return LaunchProcess(targetExe, new string[0]);
                }

                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Failed to apply update: " + ex.Message,
                    "Remvora Update Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 1;
            }
        }

        private static void WaitForProcessExit(string processName, int timeoutMs)
        {
            int elapsed = 0;
            while (elapsed < timeoutMs)
            {
                Process[] procs = Process.GetProcessesByName(processName);
                if (procs == null || procs.Length == 0)
                {
                    return;
                }

                foreach (Process p in procs)
                {
                    try { p.Dispose(); } catch { }
                }

                System.Threading.Thread.Sleep(200);
                elapsed += 200;
            }
        }

        public const string FallbackVersion = "1.2.1";

        public static string GetAppVersion(string targetExe = null)
        {
            try
            {
                if (!string.IsNullOrEmpty(targetExe) && File.Exists(targetExe))
                {
                    FileVersionInfo fv = FileVersionInfo.GetVersionInfo(targetExe);
                    if (!string.IsNullOrEmpty(fv.ProductVersion))
                    {
                        string v = fv.ProductVersion;
                        int plus = v.IndexOf('+');
                        return plus > 0 ? v.Substring(0, plus) : v;
                    }
                }
            }
            catch { }

            try
            {
                Version asmVer = Assembly.GetExecutingAssembly().GetName().Version;
                if (asmVer != null && asmVer.Major > 0)
                {
                    return asmVer.Major + "." + asmVer.Minor + "." + asmVer.Build;
                }
            }
            catch { }

            return FallbackVersion;
        }
    }

    /// <summary>
    /// Modern, minimal, Windows 11 Fluent-inspired Setup Wizard.
    /// Free of AI slop, dated 90s Inno look, and bloated assets.
    /// </summary>
    internal class SetupWizardForm : Form
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private readonly string _sourceDir;
        private readonly string _defaultInstallDir;
        private readonly string _sourceExe;
        private readonly string[] _launchArgs;

        // Colors
        private readonly Color _bg = Color.FromArgb(18, 19, 24);
        private readonly Color _cardBg = Color.FromArgb(24, 26, 34);
        private readonly Color _cardBorder = Color.FromArgb(40, 44, 56);
        private readonly Color _accent = Color.FromArgb(99, 102, 241); // Indigo
        private readonly Color _accentHover = Color.FromArgb(129, 140, 248);
        private readonly Color _textPrimary = Color.FromArgb(250, 250, 250);
        private readonly Color _textSecondary = Color.FromArgb(156, 163, 175);
        private readonly Color _textMuted = Color.FromArgb(107, 114, 128);

        // State Panels
        private Panel _panelWelcome;
        private Panel _panelInstalling;
        private Panel _panelComplete;

        // Step 1 Controls
        private TextBox _txtInstallDir;
        private CheckBox _chkDesktop;
        private CheckBox _chkStartMenu;
        private CheckBox _chkRegisterApps;

        // Step 2 Controls
        private Label _lblInstallStatus;
        private Label _lblInstallFile;
        private ModernProgressBar _progressBar;

        // Step 3 Controls
        private Label _lblVersion;
        private CheckBox _chkLaunch;

        public bool ShouldLaunchAfterExit { get; private set; }
        public string InstalledExePath { get; private set; }

        public SetupWizardForm(string sourceDir, string defaultInstallDir, string sourceExe, string[] launchArgs)
        {
            _sourceDir = sourceDir;
            _defaultInstallDir = defaultInstallDir;
            _sourceExe = sourceExe;
            _launchArgs = launchArgs;
            ShouldLaunchAfterExit = true;

            InitializeUI();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                // Windows 11 Immersive Dark Mode
                int darkMode = 1;
                DwmSetWindowAttribute(this.Handle, 20, ref darkMode, sizeof(int));
                DwmSetWindowAttribute(this.Handle, 19, ref darkMode, sizeof(int));

                // Windows 11 Rounded Window Corners
                int cornerPreference = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(this.Handle, 33, ref cornerPreference, sizeof(int));

                // Windows 11 Dark Titlebar color
                int captionColor = 0x00181312;
                DwmSetWindowAttribute(this.Handle, 35, ref captionColor, sizeof(int));
            }
            catch { }
        }

        private void InitializeUI()
        {
            this.Text = "Remvora Setup";
            this.ClientSize = new Size(540, 500);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = true;
            this.BackColor = _bg;
            this.ForeColor = _textPrimary;
            this.DoubleBuffered = true;
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular);

            BuildWelcomeView();
            BuildInstallingView();
            BuildCompleteView();

            ShowView(1);
        }

        private void ShowView(int view)
        {
            _panelWelcome.Visible = (view == 1);
            _panelInstalling.Visible = (view == 2);
            _panelComplete.Visible = (view == 3);
        }

        #region Step 1: Welcome & Preferences View
        private void BuildWelcomeView()
        {
            _panelWelcome = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = _bg
            };

            // App Icon & Header
            PictureBox iconBox = new PictureBox
            {
                Location = new Point(238, 28),
                Size = new Size(64, 64),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            iconBox.Paint += DrawAppIcon;
            _panelWelcome.Controls.Add(iconBox);

            Label lblTitle = new Label
            {
                Text = "Install Remvora",
                Location = new Point(20, 100),
                Size = new Size(500, 32),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = _textPrimary
            };
            _panelWelcome.Controls.Add(lblTitle);

            Label lblSub = new Label
            {
                Text = "Windows Uninstaller & Deep System Cleanup Utility",
                Location = new Point(20, 134),
                Size = new Size(500, 20),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
                ForeColor = _textSecondary
            };
            _panelWelcome.Controls.Add(lblSub);

            // Card Container
            RoundedPanel card = new RoundedPanel
            {
                Location = new Point(24, 168),
                Size = new Size(492, 224),
                BackColor = _cardBg,
                BorderColor = _cardBorder,
                BorderRadius = 8
            };

            Label lblPathTag = new Label
            {
                Text = "INSTALLATION LOCATION",
                Location = new Point(18, 14),
                Size = new Size(300, 16),
                Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
                ForeColor = _textMuted
            };
            card.Controls.Add(lblPathTag);

            _txtInstallDir = new TextBox
            {
                Location = new Point(18, 34),
                Size = new Size(360, 26),
                Text = _defaultInstallDir,
                BackColor = Color.FromArgb(32, 34, 44),
                ForeColor = _textPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9F)
            };
            card.Controls.Add(_txtInstallDir);

            ModernButton btnBrowse = new ModernButton
            {
                Text = "Browse...",
                Location = new Point(386, 33),
                Size = new Size(88, 27),
                BackColor = Color.FromArgb(32, 34, 44),
                BorderColor = _cardBorder,
                HoverColor = Color.FromArgb(42, 45, 58),
                TextColor = _textSecondary,
                Font = new Font("Segoe UI", 8.5F)
            };
            btnBrowse.Click += (s, e) =>
            {
                using (FolderBrowserDialog fbd = new FolderBrowserDialog())
                {
                    fbd.Description = "Select Remvora Installation Folder";
                    fbd.SelectedPath = _txtInstallDir.Text;
                    if (fbd.ShowDialog() == DialogResult.OK)
                    {
                        _txtInstallDir.Text = fbd.SelectedPath;
                    }
                }
            };
            card.Controls.Add(btnBrowse);

            // Divider Line
            Panel divider = new Panel
            {
                Location = new Point(18, 74),
                Size = new Size(456, 1),
                BackColor = _cardBorder
            };
            card.Controls.Add(divider);

            Label lblOptionsTag = new Label
            {
                Text = "INTEGRATION PREFERENCES",
                Location = new Point(18, 86),
                Size = new Size(300, 16),
                Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
                ForeColor = _textMuted
            };
            card.Controls.Add(lblOptionsTag);

            _chkDesktop = new CheckBox
            {
                Text = "Create Desktop shortcut",
                Location = new Point(18, 108),
                Size = new Size(400, 24),
                Checked = true,
                ForeColor = _textPrimary,
                Font = new Font("Segoe UI", 9F)
            };
            card.Controls.Add(_chkDesktop);

            _chkStartMenu = new CheckBox
            {
                Text = "Add Remvora to Windows Start Menu",
                Location = new Point(18, 136),
                Size = new Size(400, 24),
                Checked = true,
                ForeColor = _textPrimary,
                Font = new Font("Segoe UI", 9F)
            };
            card.Controls.Add(_chkStartMenu);

            _chkRegisterApps = new CheckBox
            {
                Text = "Register in Windows Installed Apps (Settings)",
                Location = new Point(18, 164),
                Size = new Size(400, 24),
                Checked = true,
                ForeColor = _textPrimary,
                Font = new Font("Segoe UI", 9F)
            };
            card.Controls.Add(_chkRegisterApps);

            Label lblNoUac = new Label
            {
                Text = "• Per-user installation (requires no administrator rights or system disruption)",
                Location = new Point(18, 196),
                Size = new Size(456, 18),
                Font = new Font("Segoe UI", 8F),
                ForeColor = _textMuted
            };
            card.Controls.Add(lblNoUac);

            _panelWelcome.Controls.Add(card);

            // Action Footer
            ModernButton btnRunDirect = new ModernButton
            {
                Text = "Run without installing",
                Location = new Point(24, 420),
                Size = new Size(165, 38),
                BackColor = Color.FromArgb(26, 28, 38),
                BorderColor = _cardBorder,
                HoverColor = Color.FromArgb(36, 39, 52),
                TextColor = _textSecondary,
                Font = new Font("Segoe UI", 8.5F)
            };
            btnRunDirect.Click += (s, e) =>
            {
                this.InstalledExePath = _sourceExe;
                this.ShouldLaunchAfterExit = true;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };
            _panelWelcome.Controls.Add(btnRunDirect);

            ModernButton btnInstall = new ModernButton
            {
                Text = "Install Remvora",
                Location = new Point(366, 420),
                Size = new Size(150, 38),
                BackColor = _accent,
                BorderColor = _accent,
                HoverColor = _accentHover,
                TextColor = Color.White,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };
            btnInstall.Click += (s, e) => StartInstallation();
            _panelWelcome.Controls.Add(btnInstall);

            this.Controls.Add(_panelWelcome);
        }
        #endregion

        #region Step 2: Installing View
        private void BuildInstallingView()
        {
            _panelInstalling = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = _bg,
                Visible = false
            };

            PictureBox iconBox = new PictureBox
            {
                Location = new Point(244, 44),
                Size = new Size(52, 52),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            iconBox.Paint += DrawAppIcon;
            _panelInstalling.Controls.Add(iconBox);

            Label lblTitle = new Label
            {
                Text = "Installing Remvora...",
                Location = new Point(20, 110),
                Size = new Size(500, 30),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = _textPrimary
            };
            _panelInstalling.Controls.Add(lblTitle);

            Label lblSub = new Label
            {
                Text = "Configuring application files and system integrations",
                Location = new Point(20, 142),
                Size = new Size(500, 20),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = _textSecondary
            };
            _panelInstalling.Controls.Add(lblSub);

            RoundedPanel card = new RoundedPanel
            {
                Location = new Point(24, 186),
                Size = new Size(492, 140),
                BackColor = _cardBg,
                BorderColor = _cardBorder,
                BorderRadius = 8
            };

            _lblInstallStatus = new Label
            {
                Text = "Preparing installation...",
                Location = new Point(20, 22),
                Size = new Size(452, 22),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = _textPrimary
            };
            card.Controls.Add(_lblInstallStatus);

            _progressBar = new ModernProgressBar
            {
                Location = new Point(20, 52),
                Size = new Size(452, 12),
                BackColor = Color.FromArgb(32, 35, 48),
                BarColor = _accent
            };
            card.Controls.Add(_progressBar);

            _lblInstallFile = new Label
            {
                Text = "",
                Location = new Point(20, 74),
                Size = new Size(452, 20),
                Font = new Font("Segoe UI", 8F),
                ForeColor = _textMuted
            };
            card.Controls.Add(_lblInstallFile);

            _panelInstalling.Controls.Add(card);

            this.Controls.Add(_panelInstalling);
        }
        #endregion

        #region Step 3: Complete View
        private void BuildCompleteView()
        {
            _panelComplete = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = _bg,
                Visible = false
            };

            // Success Badge
            PictureBox badgeBox = new PictureBox
            {
                Location = new Point(244, 40),
                Size = new Size(52, 52),
                BackColor = Color.Transparent
            };
            badgeBox.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(16, 185, 129))) // Emerald Green
                {
                    e.Graphics.FillEllipse(brush, 4, 4, 44, 44);
                }
                using (Pen pen = new Pen(Color.White, 3.5f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    Point[] check = new Point[] {
                        new Point(16, 26),
                        new Point(24, 34),
                        new Point(36, 18)
                    };
                    e.Graphics.DrawLines(pen, check);
                }
            };
            _panelComplete.Controls.Add(badgeBox);

            Label lblTitle = new Label
            {
                Text = "Remvora is Ready",
                Location = new Point(20, 104),
                Size = new Size(500, 30),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 17F, FontStyle.Bold),
                ForeColor = _textPrimary
            };
            _panelComplete.Controls.Add(lblTitle);

            Label lblSub = new Label
            {
                Text = "Remvora has been installed and configured on this machine.",
                Location = new Point(20, 138),
                Size = new Size(500, 20),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = _textSecondary
            };
            _panelComplete.Controls.Add(lblSub);

            RoundedPanel card = new RoundedPanel
            {
                Location = new Point(24, 180),
                Size = new Size(492, 170),
                BackColor = _cardBg,
                BorderColor = _cardBorder,
                BorderRadius = 8
            };

            Label lblDetailsTag = new Label
            {
                Text = "INSTALLATION SUMMARY",
                Location = new Point(18, 16),
                Size = new Size(300, 16),
                Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
                ForeColor = _textMuted
            };
            card.Controls.Add(lblDetailsTag);

            Label lblPath = new Label
            {
                Text = "• Destination: " + _defaultInstallDir,
                Location = new Point(18, 38),
                Size = new Size(456, 20),
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = _textSecondary
            };
            card.Controls.Add(lblPath);

            _lblVersion = new Label
            {
                Text = "• Version: " + Program.GetAppVersion(_sourceExe) + " (Windows 11 Native)",
                Location = new Point(18, 62),
                Size = new Size(456, 20),
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = _textSecondary
            };
            card.Controls.Add(_lblVersion);

            Panel divider = new Panel
            {
                Location = new Point(18, 92),
                Size = new Size(456, 1),
                BackColor = _cardBorder
            };
            card.Controls.Add(divider);

            _chkLaunch = new CheckBox
            {
                Text = "Launch Remvora now",
                Location = new Point(18, 112),
                Size = new Size(400, 26),
                Checked = true,
                ForeColor = _textPrimary,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };
            card.Controls.Add(_chkLaunch);

            _panelComplete.Controls.Add(card);

            // Finish Button
            ModernButton btnFinish = new ModernButton
            {
                Text = "Finish",
                Location = new Point(382, 420),
                Size = new Size(134, 38),
                BackColor = _accent,
                BorderColor = _accent,
                HoverColor = _accentHover,
                TextColor = Color.White,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };
            btnFinish.Click += (s, e) =>
            {
                this.ShouldLaunchAfterExit = _chkLaunch.Checked;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };
            _panelComplete.Controls.Add(btnFinish);

            this.Controls.Add(_panelComplete);
        }
        #endregion

        #region Installation Logic
        private void StartInstallation()
        {
            string installDir = _txtInstallDir.Text.Trim();
            if (string.IsNullOrEmpty(installDir))
            {
                MessageBox.Show("Please specify a valid installation directory.", "Remvora Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ShowView(2);

            BackgroundWorker worker = new BackgroundWorker();
            worker.WorkerReportsProgress = true;
            worker.DoWork += (s, e) =>
            {
                PerformInstall(installDir, worker);
            };
            worker.ProgressChanged += (s, e) =>
            {
                _progressBar.Value = Math.Min(100, Math.Max(0, e.ProgressPercentage));
                string status = e.UserState as string;
                if (status != null)
                {
                    if (status.Contains("|"))
                    {
                        string[] parts = status.Split('|');
                        _lblInstallStatus.Text = parts[0];
                        _lblInstallFile.Text = parts.Length > 1 ? parts[1] : "";
                    }
                    else
                    {
                        _lblInstallStatus.Text = status;
                        _lblInstallFile.Text = "";
                    }
                }
            };
            worker.RunWorkerCompleted += (s, e) =>
            {
                if (e.Error != null)
                {
                    MessageBox.Show(
                        "Installation encountered an error:\n" + e.Error.Message,
                        "Remvora Setup Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    ShowView(1);
                }
                else
                {
                    if (_lblVersion != null)
                    {
                        _lblVersion.Text = "• Version: " + Program.GetAppVersion(this.InstalledExePath) + " (Windows 11 Native)";
                    }
                    ShowView(3);
                }
            };
            worker.RunWorkerAsync();
        }

        private void PerformInstall(string installDir, BackgroundWorker worker)
        {
            string appInstallDir = Path.Combine(installDir, "app");
            Directory.CreateDirectory(installDir);
            Directory.CreateDirectory(appInstallDir);

            worker.ReportProgress(5, "Analyzing installation package|Initializing self-extracting payload...");
            System.Threading.Thread.Sleep(200);

            Stream payloadStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RemvoraPayload");
            if (payloadStream != null)
            {
                using (payloadStream)
                using (ZipArchive archive = new ZipArchive(payloadStream, ZipArchiveMode.Read))
                {
                    int totalEntries = archive.Entries.Count;
                    int extracted = 0;

                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            continue; // Directory entry
                        }

                        string cleanEntry = entry.FullName.Replace('/', '\\').TrimStart('\\');
                        string destPath;

                        if (cleanEntry.StartsWith("app\\", StringComparison.OrdinalIgnoreCase))
                        {
                            destPath = Path.Combine(installDir, cleanEntry);
                        }
                        else if (cleanEntry.Contains("\\"))
                        {
                            destPath = Path.Combine(appInstallDir, cleanEntry);
                        }
                        else
                        {
                            destPath = Path.Combine(installDir, cleanEntry);
                        }

                        string destFolder = Path.GetDirectoryName(destPath);
                        if (!Directory.Exists(destFolder))
                        {
                            Directory.CreateDirectory(destFolder);
                        }

                        using (Stream entryStream = entry.Open())
                        using (FileStream fileStream = File.Create(destPath))
                        {
                            entryStream.CopyTo(fileStream);
                        }
                        extracted++;

                        int pct = 10 + (int)((float)extracted / totalEntries * 65);
                        if (extracted % 4 == 0 || extracted == totalEntries)
                        {
                            worker.ReportProgress(pct, "Installing Remvora components (" + pct + "%)|Extracting " + entry.Name + " (" + extracted + "/" + totalEntries + ")");
                        }
                    }
                }
            }
            else
            {
                // Fallback: Copy app folder from local directory
                string sourceApp = Path.Combine(_sourceDir, "app");
                if (!Directory.Exists(sourceApp))
                {
                    sourceApp = _sourceDir;
                }

                string[] files = Directory.GetFiles(sourceApp, "*.*", SearchOption.AllDirectories);
                int totalFiles = files.Length;
                int copied = 0;

                foreach (string file in files)
                {
                    string rel = file.Substring(sourceApp.Length).TrimStart('\\', '/');
                    string dest = Path.Combine(appInstallDir, rel);
                    string destFolder = Path.GetDirectoryName(dest);
                    if (!Directory.Exists(destFolder))
                    {
                        Directory.CreateDirectory(destFolder);
                    }

                    File.Copy(file, dest, true);
                    copied++;

                    int pct = 10 + (int)((float)copied / totalFiles * 65);
                    if (copied % 4 == 0 || copied == totalFiles)
                    {
                        worker.ReportProgress(pct, "Installing Remvora components (" + pct + "%)|Copying " + Path.GetFileName(file) + " (" + copied + "/" + totalFiles + ")");
                    }
                }
            }

            // Ensure root launcher Remvora.exe exists
            worker.ReportProgress(78, "Setting up launcher|Configuring Remvora executable entry point...");
            string targetLauncher = Path.Combine(installDir, "Remvora.exe");
            if (!File.Exists(targetLauncher))
            {
                string sourceLauncher = Path.Combine(_sourceDir, "Remvora.exe");
                if (File.Exists(sourceLauncher))
                {
                    File.Copy(sourceLauncher, targetLauncher, true);
                }
                else
                {
                    try
                    {
                        File.Copy(Application.ExecutablePath, targetLauncher, true);
                    }
                    catch { }
                }
            }

            // Ensure uninstall.ps1 exists
            worker.ReportProgress(82, "Configuring uninstaller|Writing uninstall script...");
            string targetUninstall = Path.Combine(installDir, "uninstall.ps1");
            if (!File.Exists(targetUninstall))
            {
                string sourceUninstall = Path.Combine(_sourceDir, "uninstall.ps1");
                if (File.Exists(sourceUninstall))
                {
                    File.Copy(sourceUninstall, targetUninstall, true);
                }
                else
                {
                    try
                    {
                        string defaultUninstallScript =
                            "# Remvora Uninstaller Script\r\n" +
                            "Stop-Process -Name \"Remvora.App\" -Force -ErrorAction SilentlyContinue\r\n" +
                            "Stop-Process -Name \"Remvora\" -Force -ErrorAction SilentlyContinue\r\n" +
                            "Start-Sleep -Seconds 1\r\n" +
                            "$installDir = Split-Path -Parent $MyInvocation.MyCommand.Path\r\n" +
                            "Remove-Item -Path \"$env:APPDATA\\Microsoft\\Windows\\Start Menu\\Programs\\Remvora.lnk\" -Force -ErrorAction SilentlyContinue\r\n" +
                            "Remove-Item -Path \"$env:USERPROFILE\\Desktop\\Remvora.lnk\" -Force -ErrorAction SilentlyContinue\r\n" +
                            "Remove-Item -Path \"HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Remvora\" -Recurse -Force -ErrorAction SilentlyContinue\r\n" +
                            "Start-Process cmd.exe -ArgumentList \"/c timeout /t 2 & rmdir /s /q `\"$installDir`\"\" -WindowStyle Hidden\r\n";
                        File.WriteAllText(targetUninstall, defaultUninstallScript, System.Text.Encoding.UTF8);
                    }
                    catch { }
                }
            }

            this.InstalledExePath = File.Exists(targetLauncher) ? targetLauncher : Path.Combine(appInstallDir, "Remvora.App.exe");

            // Shortcuts
            worker.ReportProgress(88, "Registering shortcuts|Adding to Start Menu and Desktop...");

            if (_chkStartMenu.Checked)
            {
                try
                {
                    string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    string startMenuPrograms = Path.Combine(appData, "Microsoft", "Windows", "Start Menu", "Programs");
                    string shortcutPath = Path.Combine(startMenuPrograms, "Remvora.lnk");
                    CreateShortcut(shortcutPath, this.InstalledExePath, installDir);
                }
                catch { }
            }

            if (_chkDesktop.Checked)
            {
                try
                {
                    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    string shortcutPath = Path.Combine(desktop, "Remvora.lnk");
                    CreateShortcut(shortcutPath, this.InstalledExePath, installDir);
                }
                catch { }
            }

            // Register in Windows Installed Apps
            if (_chkRegisterApps.Checked)
            {
                worker.ReportProgress(95, "Registering in Windows Installed Apps...");
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Remvora"))
                    {
                        if (key != null)
                        {
                            key.SetValue("DisplayName", "Remvora");
                            key.SetValue("DisplayVersion", Program.GetAppVersion(this.InstalledExePath));
                            key.SetValue("Publisher", "Remvora");
                            key.SetValue("InstallLocation", installDir);
                            key.SetValue("DisplayIcon", this.InstalledExePath);
                            key.SetValue("UninstallString", "powershell.exe -ExecutionPolicy Bypass -File \"" + targetUninstall + "\"");
                            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                        }
                    }
                }
                catch { }
            }

            worker.ReportProgress(100, "Installation complete!");
            System.Threading.Thread.Sleep(300);
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
            catch { }
        }
        #endregion

        #region Drawing Helpers
        private void DrawAppIcon(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            PictureBox pb = sender as PictureBox;
            int size = (pb != null) ? pb.Width : 48;

            // Try to load icon from file or draw sleek vector shield/geometric logo
            string iconFile = Path.Combine(_sourceDir, "app", "Assets", "icon.png");
            if (!File.Exists(iconFile))
            {
                iconFile = Path.Combine(_sourceDir, "Assets", "icon.png");
            }

            if (File.Exists(iconFile))
            {
                try
                {
                    using (Image img = Image.FromFile(iconFile))
                    {
                        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        e.Graphics.DrawImage(img, new Rectangle(0, 0, size, size));
                        return;
                    }
                }
                catch { }
            }

            // High-aesthetic modern geometric vector logo fallback
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddArc(2, 2, size - 4, size - 4, 0, 360);
                using (LinearGradientBrush brush = new LinearGradientBrush(new Point(0, 0), new Point(size, size), _accent, Color.FromArgb(168, 85, 247)))
                {
                    e.Graphics.FillPath(brush, path);
                }
            }

            using (Font f = new Font("Segoe UI", size * 0.45f, FontStyle.Bold))
            using (SolidBrush textBrush = new SolidBrush(Color.White))
            {
                StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                e.Graphics.DrawString("R", f, textBrush, new RectangleF(0, 0, size, size), sf);
            }
        }
        #endregion
    }

    #region Custom Modern Controls
    internal class RoundedPanel : Panel
    {
        public Color BorderColor { get; set; }
        public int BorderRadius { get; set; }

        public RoundedPanel()
        {
            BorderColor = Color.FromArgb(40, 44, 56);
            BorderRadius = 8;
            this.DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            if (this.Parent != null)
            {
                using (SolidBrush pb = new SolidBrush(this.Parent.BackColor))
                {
                    e.Graphics.FillRectangle(pb, this.ClientRectangle);
                }
            }

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            using (GraphicsPath path = GetRoundedPath(rect, BorderRadius))
            {
                using (SolidBrush brush = new SolidBrush(this.BackColor))
                {
                    e.Graphics.FillPath(brush, path);
                }

                if (BorderColor != Color.Transparent)
                {
                    using (Pen pen = new Pen(BorderColor, 1f))
                    {
                        pen.Alignment = PenAlignment.Inset;
                        e.Graphics.DrawPath(pen, path);
                    }
                }
            }
        }

        private static GraphicsPath GetRoundedPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal class ModernButton : Button
    {
        public Color BorderColor { get; set; }
        public Color HoverColor { get; set; }
        public Color TextColor { get; set; }
        private bool _isHovered;
        private bool _isPressed;

        public ModernButton()
        {
            this.FlatStyle = FlatStyle.Flat;
            this.FlatAppearance.BorderSize = 0;
            this.Cursor = Cursors.Hand;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BorderColor = Color.Transparent;
            HoverColor = Color.FromArgb(129, 140, 248);
            TextColor = Color.White;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _isHovered = true;
            this.Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _isHovered = false;
            this.Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            _isPressed = true;
            this.Invalidate();
            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            _isPressed = false;
            this.Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            pevent.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            pevent.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // Clear parent background to eliminate jagged redraw residue
            Color parentBg = this.Parent != null ? this.Parent.BackColor : Color.FromArgb(18, 19, 24);
            using (SolidBrush parentBrush = new SolidBrush(parentBg))
            {
                pevent.Graphics.FillRectangle(parentBrush, this.ClientRectangle);
            }

            Color fill = _isPressed ? Color.FromArgb(79, 70, 229) : (_isHovered ? HoverColor : this.BackColor);
            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);

            using (GraphicsPath path = new GraphicsPath())
            {
                int r = 5;
                int d = r * 2;
                path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                path.CloseFigure();

                using (SolidBrush brush = new SolidBrush(fill))
                {
                    pevent.Graphics.FillPath(brush, path);
                }

                if (BorderColor != Color.Transparent)
                {
                    using (Pen pen = new Pen(BorderColor, 1f))
                    {
                        pen.Alignment = PenAlignment.Inset;
                        pevent.Graphics.DrawPath(pen, path);
                    }
                }
            }

            TextRenderer.DrawText(
                pevent.Graphics,
                this.Text,
                this.Font,
                this.ClientRectangle,
                TextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    internal class ModernProgressBar : Control
    {
        private int _value;
        public Color BarColor { get; set; }

        public int Value
        {
            get { return _value; }
            set
            {
                _value = Math.Min(100, Math.Max(0, value));
                this.Invalidate();
            }
        }

        public ModernProgressBar()
        {
            this.DoubleBuffered = true;
            this.BackColor = Color.FromArgb(32, 35, 48);
            BarColor = Color.FromArgb(99, 102, 241);
            _value = 0;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            int r = Math.Min(rect.Height / 2, 4);
            int d = r * 2;

            // Track background
            using (GraphicsPath trackPath = new GraphicsPath())
            {
                trackPath.AddArc(rect.X, rect.Y, d, d, 180, 90);
                trackPath.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                trackPath.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                trackPath.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                trackPath.CloseFigure();

                using (SolidBrush trackBrush = new SolidBrush(this.BackColor))
                {
                    e.Graphics.FillPath(trackBrush, trackPath);
                }
            }

            // Fill Bar
            if (_value > 0)
            {
                int fillWidth = Math.Max(d, (int)((float)rect.Width * ((float)_value / 100f)));
                Rectangle fillRect = new Rectangle(rect.X, rect.Y, fillWidth, rect.Height);

                using (GraphicsPath fillPath = new GraphicsPath())
                {
                    fillPath.AddArc(fillRect.X, fillRect.Y, d, d, 180, 90);
                    fillPath.AddArc(fillRect.Right - d, fillRect.Y, d, d, 270, 90);
                    fillPath.AddArc(fillRect.Right - d, fillRect.Bottom - d, d, d, 0, 90);
                    fillPath.AddArc(fillRect.X, fillRect.Bottom - d, d, d, 90, 90);
                    fillPath.CloseFigure();

                    using (LinearGradientBrush fillBrush = new LinearGradientBrush(new Point(fillRect.X, 0), new Point(fillRect.Right, 0), BarColor, Color.FromArgb(168, 85, 247)))
                    {
                        e.Graphics.FillPath(fillBrush, fillPath);
                    }
                }
            }
        }
    }
    #endregion
}
