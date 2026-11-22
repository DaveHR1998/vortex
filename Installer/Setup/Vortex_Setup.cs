using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Vortex System Guardian Setup")]
[assembly: AssemblyDescription("Vortex System Guardian & Encrypted Vault Installer")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("DAVE_HR")]
[assembly: AssemblyProduct("Vortex System Guardian")]
[assembly: AssemblyCopyright("Copyright © DAVE_HR 2026")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

namespace Vortex.Setup
{
    public class SetupForm : Form
    {
        private int _step = 0; // 0 = Options, 1 = Installing, 2 = Finished
        private Point _dragStart;
        private bool _dragging;
        private Image _logoImage;

        // Panel 0: Options
        private ModernCardPanel _optionsCard;
        private TextBox _txtPath;
        private ModernButton _btnBrowse;
        private CheckBox _chkDesktop;
        private CheckBox _chkStartMenu;
        private CheckBox _chkLaunch;

        // Panel 1: Installing
        private Panel _installPanel;
        private Label _lblInstallStatus;
        private ModernProgressBar _progressBar;
        private Label _lblInstallPercent;
        private Label _lblInstallDetail;

        // Panel 2: Finished
        private Panel _finishedPanel;
        private PictureBox _picSuccess;
        private Label _lblFinishedTitle;
        private Label _lblFinishedSubtitle;
        private CheckBox _chkLaunchFinished;

        // Actions
        private ModernButton _btnCancel;
        private ModernButton _btnInstall;
        private ModernButton _btnFinish;

        protected override CreateParams CreateParams
        {
            get
            {
                const int CS_DROPSHADOW = 0x20000;
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                return cp;
            }
        }

        public SetupForm()
        {
            LoadLogoImage();
            InitializeComponent();
            SetStep(0);
        }

        private void LoadLogoImage()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                foreach (string res in asm.GetManifestResourceNames())
                {
                    if (res.EndsWith("vortex.png", StringComparison.OrdinalIgnoreCase))
                    {
                        using (Stream s = asm.GetManifestResourceStream(res))
                        {
                            if (s != null)
                            {
                                _logoImage = Image.FromStream(s);
                            }
                        }
                        break;
                    }
                }
            }
            catch { }
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            this.ClientSize = new Size(620, 466);
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(22, 22, 29); // Deep space canvas #16161D
            this.ForeColor = Color.FromArgb(227, 227, 232); // TextPrimary #E3E3E8
            this.DoubleBuffered = true;
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            // Drag Support
            this.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _dragging = true; _dragStart = e.Location; } };
            this.MouseMove += delegate(object s, MouseEventArgs e) { if (_dragging) { Point diff = new Point(e.X - _dragStart.X, e.Y - _dragStart.Y); this.Location = new Point(this.Location.X + diff.X, this.Location.Y + diff.Y); } };
            this.MouseUp += delegate(object s, MouseEventArgs e) { _dragging = false; };

            // ==================== TITLE BAR ====================
            Panel topBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 46,
                BackColor = Color.FromArgb(27, 27, 39) // #1B1B27
            };
            topBar.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                // Bottom subtle border line
                using (Pen linePen = new Pen(Color.FromArgb(56, 108, 109, 147), 1))
                {
                    e.Graphics.DrawLine(linePen, 0, topBar.Height - 1, topBar.Width, topBar.Height - 1);
                }

                // Verified Publisher Badge rendered directly as a crisp rounded pill
                Rectangle badgeRect = new Rectangle(236, 11, 142, 24);
                using (GraphicsPath bp = SetupGraphicsHelper.CreateRoundedRectangle(badgeRect, 6))
                {
                    using (SolidBrush bBg = new SolidBrush(Color.FromArgb(24, 44, 37)))
                    {
                        e.Graphics.FillPath(bBg, bp);
                    }
                    using (Pen bPen = new Pen(Color.FromArgb(16, 185, 129), 1))
                    {
                        e.Graphics.DrawPath(bPen, bp);
                    }
                }
                using (Font bFont = new Font("Segoe UI", 8f, FontStyle.Bold))
                {
                    TextRenderer.DrawText(e.Graphics, "🛡️ v2.0.0 • DAVE_HR", bFont, badgeRect, Color.FromArgb(16, 185, 129),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                }
            };
            topBar.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _dragging = true; _dragStart = e.Location; } };
            topBar.MouseMove += delegate(object s, MouseEventArgs e) { if (_dragging) { Point diff = new Point(e.X - _dragStart.X, e.Y - _dragStart.Y); this.Location = new Point(this.Location.X + diff.X, this.Location.Y + diff.Y); } };
            topBar.MouseUp += delegate(object s, MouseEventArgs e) { _dragging = false; };

            // Title Logo + Name
            Label topBrand = new Label
            {
                Text = "⚡ VORTEX SYSTEM GUARDIAN",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(159, 160, 195),
                BackColor = Color.FromArgb(27, 27, 39),
                AutoSize = true,
                Location = new Point(18, 14),
                UseMnemonic = false
            };
            topBar.Controls.Add(topBrand);

            // Close Button
            Button btnX = new Button
            {
                Text = "✕",
                Size = new Size(38, 32),
                Location = new Point(620 - 46, 7),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10f),
                ForeColor = Color.FromArgb(159, 160, 195),
                BackColor = Color.FromArgb(27, 27, 39),
                Cursor = Cursors.Hand
            };
            btnX.FlatAppearance.BorderSize = 0;
            btnX.FlatAppearance.MouseOverBackColor = Color.FromArgb(239, 68, 68);
            btnX.FlatAppearance.MouseDownBackColor = Color.FromArgb(185, 28, 28);
            btnX.Click += delegate(object s, EventArgs e) { Application.Exit(); };
            topBar.Controls.Add(btnX);

            this.Controls.Add(topBar);

            // ==================== PANEL 0: OPTIONS CARD ====================
            _optionsCard = new ModernCardPanel
            {
                Location = new Point(32, 146),
                Size = new Size(556, 218)
            };

            Label lblDest = new Label
            {
                Text = "INSTALLATION LOCATION",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(159, 160, 195),
                BackColor = Color.FromArgb(37, 37, 55),
                Location = new Point(18, 16),
                AutoSize = true,
                UseMnemonic = false
            };
            _optionsCard.Controls.Add(lblDest);

            Panel txtContainer = new Panel
            {
                Location = new Point(18, 38),
                Size = new Size(414, 34),
                BackColor = Color.FromArgb(22, 22, 29)
            };
            txtContainer.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Color.FromArgb(75, 76, 105), 1))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, txtContainer.Width - 1, txtContainer.Height - 1);
                }
            };

            _txtPath = new TextBox
            {
                Text = @"C:\Program Files\Vortex",
                Font = new Font("Segoe UI", 9.5f),
                BackColor = Color.FromArgb(22, 22, 29),
                ForeColor = Color.FromArgb(227, 227, 232),
                BorderStyle = BorderStyle.None,
                Location = new Point(8, 8),
                Size = new Size(396, 20)
            };
            txtContainer.Controls.Add(_txtPath);
            _optionsCard.Controls.Add(txtContainer);

            _btnBrowse = new ModernButton
            {
                Text = "Browse...",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Location = new Point(442, 38),
                Size = new Size(96, 34)
            };
            _btnBrowse.Click += delegate(object s, EventArgs e)
            {
                using (FolderBrowserDialog fbd = new FolderBrowserDialog())
                {
                    fbd.SelectedPath = _txtPath.Text;
                    fbd.Description = "Select Destination Folder for Vortex:";
                    if (fbd.ShowDialog() == DialogResult.OK)
                    {
                        string p = fbd.SelectedPath;
                        if (!p.EndsWith("Vortex", StringComparison.OrdinalIgnoreCase))
                        {
                            p = Path.Combine(p, "Vortex");
                        }
                        _txtPath.Text = p;
                    }
                }
            };
            _optionsCard.Controls.Add(_btnBrowse);

            // Separation Line
            Panel sepLine = new Panel
            {
                Location = new Point(18, 86),
                Size = new Size(520, 1),
                BackColor = Color.FromArgb(56, 108, 109, 147)
            };
            _optionsCard.Controls.Add(sepLine);

            _chkDesktop = new CheckBox
            {
                Text = "Create Desktop Shortcut (with Vortex Icon)",
                Checked = true,
                ForeColor = Color.FromArgb(227, 227, 232),
                Font = new Font("Segoe UI", 9.5f),
                BackColor = Color.FromArgb(37, 37, 55),
                Location = new Point(18, 98),
                Size = new Size(480, 24),
                UseMnemonic = false
            };
            _optionsCard.Controls.Add(_chkDesktop);

            _chkStartMenu = new CheckBox
            {
                Text = "Create Start Menu Folder & Shortcuts",
                Checked = true,
                ForeColor = Color.FromArgb(227, 227, 232),
                Font = new Font("Segoe UI", 9.5f),
                BackColor = Color.FromArgb(37, 37, 55),
                Location = new Point(18, 130),
                Size = new Size(480, 24),
                UseMnemonic = false
            };
            _optionsCard.Controls.Add(_chkStartMenu);

            _chkLaunch = new CheckBox
            {
                Text = "Launch Vortex Guardian immediately after installation",
                Checked = true,
                ForeColor = Color.FromArgb(227, 227, 232),
                Font = new Font("Segoe UI", 9.5f),
                BackColor = Color.FromArgb(37, 37, 55),
                Location = new Point(18, 162),
                Size = new Size(480, 24),
                UseMnemonic = false
            };
            _optionsCard.Controls.Add(_chkLaunch);

            this.Controls.Add(_optionsCard);

            // ==================== PANEL 1: INSTALLING ====================
            _installPanel = new Panel
            {
                Location = new Point(32, 160),
                Size = new Size(556, 210),
                BackColor = Color.FromArgb(22, 22, 29),
                Visible = false
            };

            _lblInstallStatus = new Label
            {
                Text = "Preparing installation package...",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(227, 227, 232),
                BackColor = Color.FromArgb(22, 22, 29),
                Location = new Point(0, 16),
                Size = new Size(556, 30),
                UseMnemonic = false
            };
            _installPanel.Controls.Add(_lblInstallStatus);

            _progressBar = new ModernProgressBar
            {
                Location = new Point(0, 60),
                Size = new Size(556, 12),
                Value = 0
            };
            _installPanel.Controls.Add(_progressBar);

            _lblInstallPercent = new Label
            {
                Text = "0%",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(129, 140, 248),
                BackColor = Color.FromArgb(22, 22, 29),
                Location = new Point(0, 80),
                Size = new Size(556, 26),
                TextAlign = ContentAlignment.MiddleRight,
                UseMnemonic = false
            };
            _installPanel.Controls.Add(_lblInstallPercent);

            _lblInstallDetail = new Label
            {
                Text = "Configuring application binaries, system shields, and security modules...",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(159, 160, 195),
                BackColor = Color.FromArgb(22, 22, 29),
                Location = new Point(0, 116),
                Size = new Size(556, 40),
                UseMnemonic = false
            };
            _installPanel.Controls.Add(_lblInstallDetail);

            this.Controls.Add(_installPanel);

            // ==================== PANEL 2: FINISHED ====================
            _finishedPanel = new Panel
            {
                Location = new Point(32, 140),
                Size = new Size(556, 230),
                BackColor = Color.FromArgb(22, 22, 29),
                Visible = false
            };

            _picSuccess = new PictureBox
            {
                Size = new Size(68, 68),
                Location = new Point(244, 4),
                BackColor = Color.FromArgb(22, 22, 29)
            };
            _picSuccess.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(16, 185, 129)))
                {
                    e.Graphics.FillEllipse(b, 2, 2, 64, 64);
                }
                using (Pen p = new Pen(Color.White, 4.5f))
                {
                    p.StartCap = LineCap.Round;
                    p.EndCap = LineCap.Round;
                    e.Graphics.DrawLines(p, new Point[] { new Point(18, 35), new Point(28, 47), new Point(48, 22) });
                }
            };
            _finishedPanel.Controls.Add(_picSuccess);

            _lblFinishedTitle = new Label
            {
                Text = "Installation Complete!",
                Font = new Font("Segoe UI", 17f, FontStyle.Bold),
                ForeColor = Color.FromArgb(227, 227, 232),
                BackColor = Color.FromArgb(22, 22, 29),
                Location = new Point(0, 84),
                Size = new Size(556, 36),
                TextAlign = ContentAlignment.MiddleCenter,
                UseMnemonic = false
            };
            _finishedPanel.Controls.Add(_lblFinishedTitle);

            _lblFinishedSubtitle = new Label
            {
                Text = "Vortex System Guardian is successfully configured and ready to protect your PC.",
                Font = new Font("Segoe UI", 10f, FontStyle.Regular),
                ForeColor = Color.FromArgb(159, 160, 195),
                BackColor = Color.FromArgb(22, 22, 29),
                Location = new Point(0, 122),
                Size = new Size(556, 26),
                TextAlign = ContentAlignment.MiddleCenter,
                UseMnemonic = false
            };
            _finishedPanel.Controls.Add(_lblFinishedSubtitle);

            _chkLaunchFinished = new CheckBox
            {
                Text = "Launch Vortex Guardian now",
                Checked = true,
                ForeColor = Color.FromArgb(227, 227, 232),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                BackColor = Color.FromArgb(22, 22, 29),
                Location = new Point(176, 160),
                Size = new Size(240, 30),
                UseMnemonic = false
            };
            _finishedPanel.Controls.Add(_chkLaunchFinished);

            this.Controls.Add(_finishedPanel);

            // ==================== BOTTOM ACTION BUTTONS ====================
            _btnCancel = new ModernButton
            {
                Text = "Cancel",
                Size = new Size(110, 42),
                Location = new Point(32, 392),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            _btnCancel.Click += delegate(object s, EventArgs e) { Application.Exit(); };
            this.Controls.Add(_btnCancel);

            _btnInstall = new ModernButton
            {
                Text = "⚡ Install Vortex Now",
                Size = new Size(190, 42),
                Location = new Point(398, 392),
                IsPrimary = true,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold)
            };
            _btnInstall.Click += delegate(object s, EventArgs e) { StartInstallation(); };
            this.Controls.Add(_btnInstall);

            _btnFinish = new ModernButton
            {
                Text = "🚀 Launch Vortex",
                Size = new Size(190, 42),
                Location = new Point(398, 392),
                IsSuccess = true,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                Visible = false
            };
            _btnFinish.Click += delegate(object s, EventArgs e)
            {
                if (_chkLaunchFinished.Checked)
                {
                    string target = _txtPath.Text.Trim();
                    if (!target.EndsWith("Vortex", StringComparison.OrdinalIgnoreCase))
                    {
                        target = Path.Combine(target, "Vortex");
                    }

                    string exePath = Path.Combine(target, "Vortex.exe");
                    if (File.Exists(exePath))
                    {
                        try
                        {
                            ProcessStartInfo psi = new ProcessStartInfo
                            {
                                FileName = exePath,
                                WorkingDirectory = target,
                                UseShellExecute = true
                            };
                            Process.Start(psi);
                        }
                        catch
                        {
                            try
                            {
                                Process.Start(new ProcessStartInfo
                                {
                                    FileName = exePath,
                                    WorkingDirectory = target,
                                    UseShellExecute = false
                                });
                            }
                            catch { }
                        }
                    }
                }
                Application.Exit();
            };
            this.Controls.Add(_btnFinish);

            this.ResumeLayout(false);
        }

        private void SetStep(int step)
        {
            _step = step;
            if (_step == 0) // Options
            {
                _optionsCard.Visible = true;
                _installPanel.Visible = false;
                _finishedPanel.Visible = false;
                _btnCancel.Visible = true;
                _btnInstall.Visible = true;
                _btnFinish.Visible = false;
            }
            else if (_step == 1) // Installing
            {
                _optionsCard.Visible = false;
                _installPanel.Visible = true;
                _finishedPanel.Visible = false;
                _btnCancel.Visible = false;
                _btnInstall.Visible = false;
                _btnFinish.Visible = false;
            }
            else if (_step == 2) // Finished
            {
                _optionsCard.Visible = false;
                _installPanel.Visible = false;
                _finishedPanel.Visible = true;
                _btnCancel.Visible = false;
                _btnInstall.Visible = false;
                _btnFinish.Visible = true;
            }
            this.Invalidate();
        }

        private void StartInstallation()
        {
            string targetDir = _txtPath.Text.Trim();
            if (string.IsNullOrWhiteSpace(targetDir))
            {
                targetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Vortex");
            }
            if (!targetDir.EndsWith("Vortex", StringComparison.OrdinalIgnoreCase))
            {
                targetDir = Path.Combine(targetDir, "Vortex");
            }

            bool makeDesktopShortcut = _chkDesktop.Checked;
            bool makeStartMenuShortcut = _chkStartMenu.Checked;
            _chkLaunchFinished.Checked = _chkLaunch.Checked;

            SetStep(1);

            Thread worker = new Thread(delegate()
            {
                try
                {
                    PerformInstall(targetDir, makeDesktopShortcut, makeStartMenuShortcut);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Installation encountered an error: " + ex.Message, "Installation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                this.Invoke(new Action(delegate() { SetStep(2); }));
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private void PerformInstall(string targetDir, bool desktopShortcut, bool startMenuShortcut)
        {
            UpdateProgress("Preparing destination folder...", 5);

            // Kill running Vortex instances so files are not locked
            try
            {
                foreach (Process p in Process.GetProcessesByName("Vortex"))
                {
                    try { p.Kill(); p.WaitForExit(2000); } catch { }
                }
            }
            catch { }

            Directory.CreateDirectory(targetDir);

            // Extract embedded package.zip
            Assembly asm = Assembly.GetExecutingAssembly();
            Stream zipStream = null;

            foreach (string name in asm.GetManifestResourceNames())
            {
                if (name.EndsWith("package.zip", StringComparison.OrdinalIgnoreCase))
                {
                    zipStream = asm.GetManifestResourceStream(name);
                    break;
                }
            }

            if (zipStream != null)
            {
                using (zipStream)
                using (ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
                {
                    int count = archive.Entries.Count;
                    int i = 0;

                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        i++;
                        int pct = 5 + (int)((double)i / count * 82);

                        string destinationPath = Path.Combine(targetDir, entry.FullName);

                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(destinationPath);
                            continue;
                        }

                        string parentDir = Path.GetDirectoryName(destinationPath);
                        if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                        {
                            Directory.CreateDirectory(parentDir);
                        }

                        UpdateProgress(string.Format("Deploying: {0}...", entry.Name), pct);

                        try
                        {
                            if (File.Exists(destinationPath))
                            {
                                try { File.Delete(destinationPath); } catch { }
                            }
                            entry.ExtractToFile(destinationPath, true);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("Extraction failed for " + entry.Name + ": " + ex.Message);
                        }
                    }
                }
            }
            else
            {
                UpdateProgress("Package payload not found!", 85);
            }

            // Create Shortcuts
            UpdateProgress("Creating application shortcuts...", 90);
            string exePath = Path.Combine(targetDir, "Vortex.exe");
            string uninstallPath = Path.Combine(targetDir, "Uninstall.exe");

            if (desktopShortcut)
            {
                CreateShortcutOnDesktop("Vortex Guardian", exePath);
            }

            if (startMenuShortcut)
            {
                CreateStartMenuShortcuts(targetDir, exePath, uninstallPath);
            }

            // Register in Windows Add/Remove Programs
            UpdateProgress("Registering system components...", 96);
            RegisterUninstall(targetDir, exePath, uninstallPath);

            UpdateProgress("Finalizing installation...", 100);
            Thread.Sleep(500);
        }

        private void UpdateProgress(string status, int percent)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(delegate() { UpdateProgress(status, percent); }));
                return;
            }

            _lblInstallStatus.Text = status;
            _progressBar.Value = percent;
            _lblInstallPercent.Text = string.Format("{0}%", percent);
        }

        private void CreateShortcutOnDesktop(string linkName, string targetPath)
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string lnk = Path.Combine(desktop, linkName + ".lnk");
                CreateShellShortcut(lnk, targetPath, Path.GetDirectoryName(targetPath), "Vortex System Guardian & Encrypted Vault");
            }
            catch { }
        }

        private void CreateStartMenuShortcuts(string installDir, string exePath, string uninstallPath)
        {
            try
            {
                string programs = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
                if (string.IsNullOrEmpty(programs) || !Directory.Exists(programs))
                {
                    programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                }

                string folder = Path.Combine(programs, "Vortex Guardian");
                Directory.CreateDirectory(folder);

                CreateShellShortcut(Path.Combine(folder, "Vortex Guardian.lnk"), exePath, installDir, "Vortex System Guardian");
                CreateShellShortcut(Path.Combine(folder, "Uninstall Vortex.lnk"), uninstallPath, installDir, "Uninstall Vortex Guardian");
            }
            catch { }
        }

        private void CreateShellShortcut(string shortcutPath, string targetPath, string workingDir, string description)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;

                dynamic shell = Activator.CreateInstance(shellType);
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = targetPath;
                shortcut.WorkingDirectory = workingDir;
                shortcut.Description = description;
                shortcut.IconLocation = targetPath + ",0";
                shortcut.Save();
            }
            catch { }
        }

        private void RegisterUninstall(string targetDir, string appExe, string uninstallExe)
        {
            try
            {
                using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default))
                using (RegistryKey unKey = hklm.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Vortex"))
                {
                    if (unKey != null)
                    {
                        unKey.SetValue("DisplayName", "Vortex System Guardian");
                        unKey.SetValue("DisplayVersion", "2.0.0");
                        unKey.SetValue("Publisher", "DAVE_HR");
                        unKey.SetValue("InstallLocation", targetDir);
                        unKey.SetValue("DisplayIcon", appExe + ",0");
                        unKey.SetValue("UninstallString", string.Format("\"{0}\"", uninstallExe));
                        unKey.SetValue("QuietUninstallString", string.Format("\"{0}\"", uninstallExe));
                        unKey.SetValue("EstimatedSize", 145000, RegistryValueKind.DWord);
                        unKey.SetValue("NoModify", 1, RegistryValueKind.DWord);
                        unKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    }
                }
            }
            catch { }

            try
            {
                using (RegistryKey hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default))
                using (RegistryKey unKey = hkcu.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Vortex"))
                {
                    if (unKey != null)
                    {
                        unKey.SetValue("DisplayName", "Vortex System Guardian");
                        unKey.SetValue("DisplayVersion", "2.0.0");
                        unKey.SetValue("Publisher", "DAVE_HR");
                        unKey.SetValue("InstallLocation", targetDir);
                        unKey.SetValue("DisplayIcon", appExe + ",0");
                        unKey.SetValue("UninstallString", string.Format("\"{0}\"", uninstallExe));
                        unKey.SetValue("QuietUninstallString", string.Format("\"{0}\"", uninstallExe));
                        unKey.SetValue("EstimatedSize", 145000, RegistryValueKind.DWord);
                        unKey.SetValue("NoModify", 1, RegistryValueKind.DWord);
                        unKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    }
                }
            }
            catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // Top atmospheric gradient aura
            Rectangle auraRect = new Rectangle(0, 0, this.Width, 160);
            using (LinearGradientBrush auraBrush = new LinearGradientBrush(auraRect,
                Color.FromArgb(42, 38, 73), Color.FromArgb(22, 22, 29), 90f))
            {
                e.Graphics.FillRectangle(auraBrush, auraRect);
            }

            // Draw hero header directly onto the gradient surface for razor-sharp rendering with zero box halos
            if (_step < 2)
            {
                if (_logoImage != null)
                {
                    e.Graphics.DrawImage(_logoImage, new Rectangle(32, 60, 52, 52));
                }

                using (Font tagFont = new Font("Segoe UI", 8.5f, FontStyle.Bold))
                {
                    TextRenderer.DrawText(e.Graphics, "SYSTEM OPTIMIZER & ZERO-TRUST VAULT", tagFont,
                        new Point(96, 58), Color.FromArgb(129, 140, 248),
                        TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                }

                string title = _step == 0 ? "Install Vortex Guardian" : "Installing Vortex Guardian...";
                using (Font titleFont = new Font("Segoe UI", 16f, FontStyle.Bold))
                {
                    TextRenderer.DrawText(e.Graphics, title, titleFont,
                        new Point(94, 76), Color.FromArgb(227, 227, 232),
                        TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                }

                string subtitle = _step == 0
                    ? "Autonomous RAM boost, process heuristics, zero-trust encryption & telemetry defense."
                    : "Please wait while files, system shields, and security modules are configured.";
                using (Font subFont = new Font("Segoe UI", 9.5f, FontStyle.Regular))
                {
                    TextRenderer.DrawText(e.Graphics, subtitle, subFont,
                        new Point(96, 112), Color.FromArgb(159, 160, 195),
                        TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                }
            }

            // Outer sleek 1px border
            using (Pen borderPen = new Pen(Color.FromArgb(56, 108, 109, 147), 1))
            {
                e.Graphics.DrawRectangle(borderPen, 0, 0, this.Width - 1, this.Height - 1);
            }
        }

        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int awareness);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                // Set Per-Monitor V2 DPI awareness for native high-DPI scaling
                SetProcessDpiAwareness(2);
            }
            catch
            {
                try { SetProcessDPIAware(); } catch { }
            }

            if (args != null)
            {
                foreach (string arg in args)
                {
                    if (string.Equals(arg, "/silent", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(arg, "/s", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(arg, "-silent", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(arg, "-s", StringComparison.OrdinalIgnoreCase))
                    {
                        PerformSilentInstall();
                        return;
                    }
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }

        private static void PerformSilentInstall()
        {
            try
            {
                foreach (Process proc in Process.GetProcessesByName("Vortex"))
                {
                    try { proc.Kill(); proc.WaitForExit(3000); } catch { }
                }
            }
            catch { }

            string targetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Vortex");
            Directory.CreateDirectory(targetDir);

            Assembly asm = Assembly.GetExecutingAssembly();
            Stream zipStream = null;
            foreach (string name in asm.GetManifestResourceNames())
            {
                if (name.EndsWith("package.zip", StringComparison.OrdinalIgnoreCase))
                {
                    zipStream = asm.GetManifestResourceStream(name);
                    break;
                }
            }

            if (zipStream != null)
            {
                using (zipStream)
                using (ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string destinationPath = Path.Combine(targetDir, entry.FullName);
                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(destinationPath);
                            continue;
                        }
                        string parentDir = Path.GetDirectoryName(destinationPath);
                        if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                        {
                            Directory.CreateDirectory(parentDir);
                        }
                        entry.ExtractToFile(destinationPath, true);
                    }
                }
            }

            string exePath = Path.Combine(targetDir, "Vortex.exe");
            string uninstallPath = Path.Combine(targetDir, "Uninstall.exe");

            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null)
                {
                    dynamic shell = Activator.CreateInstance(shellType);
                    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    dynamic sc = shell.CreateShortcut(Path.Combine(desktop, "Vortex Guardian.lnk"));
                    sc.TargetPath = exePath;
                    sc.WorkingDirectory = targetDir;
                    sc.Description = "Vortex System Guardian";
                    sc.Save();
                }
            }
            catch { }

            try
            {
                using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default))
                using (RegistryKey unKey = hklm.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Vortex"))
                {
                    if (unKey != null)
                    {
                        unKey.SetValue("DisplayName", "Vortex System Guardian");
                        unKey.SetValue("DisplayVersion", "2.0.0");
                        unKey.SetValue("Publisher", "DAVE_HR");
                        unKey.SetValue("InstallLocation", targetDir);
                        unKey.SetValue("DisplayIcon", exePath + ",0");
                        unKey.SetValue("UninstallString", string.Format("\"{0}\"", uninstallPath));
                        unKey.SetValue("QuietUninstallString", string.Format("\"{0}\"", uninstallPath));
                        unKey.SetValue("EstimatedSize", 145000, RegistryValueKind.DWord);
                    }
                }
            }
            catch { }
        }
    }

    // ==================== MODERN CONTROLS & HELPERS ====================

    public class ModernButton : Button
    {
        public bool IsPrimary { get; set; }
        public bool IsSuccess { get; set; }
        private bool _isHovered;
        private bool _isPressed;

        public ModernButton()
        {
            this.SetStyle(ControlStyles.UserPaint |
                           ControlStyles.AllPaintingInWmPaint |
                           ControlStyles.OptimizedDoubleBuffer |
                           ControlStyles.ResizeRedraw |
                           ControlStyles.SupportsTransparentBackColor, true);
            this.FlatStyle = FlatStyle.Flat;
            this.FlatAppearance.BorderSize = 0;
            this.FlatAppearance.MouseDownBackColor = Color.Transparent;
            this.FlatAppearance.MouseOverBackColor = Color.Transparent;
            this.BackColor = Color.Transparent;
            this.Cursor = Cursors.Hand;
            this.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (this.Width > 0 && this.Height > 0)
            {
                Rectangle rect = new Rectangle(0, 0, this.Width, this.Height);
                using (GraphicsPath path = SetupGraphicsHelper.CreateRoundedRectangle(rect, 8))
                {
                    this.Region = new Region(path);
                }
            }
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            // Do not paint default background to eliminate square corner artifacts
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

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _isPressed = true;
            this.Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _isPressed = false;
            this.Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // Fill container surface behind anti-aliased edge curves
            Color parentBg = Color.FromArgb(22, 22, 29); // Default deep space
            if (this.Parent is ModernCardPanel)
            {
                parentBg = Color.FromArgb(37, 37, 55); // #252537 inside card
            }
            else if (this.Parent != null && this.Parent.BackColor != Color.Transparent)
            {
                parentBg = this.Parent.BackColor;
            }

            using (SolidBrush parentBrush = new SolidBrush(parentBg))
            {
                e.Graphics.FillRectangle(parentBrush, 0, 0, this.Width, this.Height);
            }

            Color bg;
            Color border;
            Color text;

            if (IsSuccess)
            {
                bg = _isPressed ? Color.FromArgb(4, 120, 87) : (_isHovered ? Color.FromArgb(5, 150, 105) : Color.FromArgb(16, 185, 129));
                border = bg;
                text = Color.White;
            }
            else if (IsPrimary)
            {
                bg = _isPressed ? Color.FromArgb(67, 56, 202) : (_isHovered ? Color.FromArgb(79, 70, 229) : Color.FromArgb(99, 102, 241));
                border = bg;
                text = Color.White;
            }
            else
            {
                bg = _isPressed ? Color.FromArgb(57, 58, 86) : (_isHovered ? Color.FromArgb(45, 46, 68) : Color.FromArgb(37, 37, 55));
                border = _isHovered ? Color.FromArgb(129, 140, 248) : Color.FromArgb(75, 76, 105);
                text = Color.FromArgb(227, 227, 232);
            }

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            using (GraphicsPath path = SetupGraphicsHelper.CreateRoundedRectangle(rect, 8))
            {
                using (SolidBrush brush = new SolidBrush(bg))
                {
                    e.Graphics.FillPath(brush, path);
                }
                using (Pen pen = new Pen(border, 1.2f))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }

            TextRenderer.DrawText(e.Graphics, this.Text, this.Font, rect, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }

    public class ModernProgressBar : Control
    {
        private int _value;
        public int Value
        {
            get { return _value; }
            set
            {
                _value = Math.Max(0, Math.Min(100, value));
                this.Invalidate();
            }
        }

        public ModernProgressBar()
        {
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            this.Height = 12;
            this.BackColor = Color.FromArgb(22, 22, 29);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // Draw rounded track
            Rectangle trackRect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            using (GraphicsPath trackPath = SetupGraphicsHelper.CreateRoundedRectangle(trackRect, 6))
            {
                using (SolidBrush trackBrush = new SolidBrush(Color.FromArgb(53, 54, 82)))
                {
                    e.Graphics.FillPath(trackBrush, trackPath);
                }
            }

            // Draw glowing progress indicator
            if (_value > 0)
            {
                int fillWidth = Math.Max(12, (int)((double)_value / 100.0 * (this.Width - 1)));
                Rectangle fillRect = new Rectangle(0, 0, fillWidth, this.Height - 1);
                using (GraphicsPath fillPath = SetupGraphicsHelper.CreateRoundedRectangle(fillRect, 6))
                {
                    using (LinearGradientBrush fillBrush = new LinearGradientBrush(fillRect,
                        Color.FromArgb(99, 102, 241), Color.FromArgb(129, 140, 248), 0f))
                    {
                        e.Graphics.FillPath(fillBrush, fillPath);
                    }
                }
            }
        }
    }

    public class ModernCardPanel : Panel
    {
        public ModernCardPanel()
        {
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.BackColor = Color.FromArgb(37, 37, 55);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (this.Width > 0 && this.Height > 0)
            {
                Rectangle rect = new Rectangle(0, 0, this.Width, this.Height);
                using (GraphicsPath path = SetupGraphicsHelper.CreateRoundedRectangle(rect, 12))
                {
                    this.Region = new Region(path);
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            using (GraphicsPath path = SetupGraphicsHelper.CreateRoundedRectangle(rect, 12))
            {
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(37, 37, 55)))
                {
                    e.Graphics.FillPath(brush, path);
                }
                using (Pen pen = new Pen(Color.FromArgb(75, 76, 105), 1.2f))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }
            base.OnPaint(e);
        }
    }

    public static class SetupGraphicsHelper
    {
        public static GraphicsPath CreateRoundedRectangle(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = radius * 2;
            Rectangle arc = new Rectangle(rect.X, rect.Y, diameter, diameter);

            path.AddArc(arc, 180, 90);
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
