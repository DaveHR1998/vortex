using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Reflection;
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
[assembly: AssemblyVersion("2.4.0.0")]
[assembly: AssemblyFileVersion("2.4.0.0")]

namespace Vortex.Setup
{
    public class SetupForm : Form
    {
        private int _step = 0; // 0 = Options, 1 = Installing, 2 = Finished
        private Point _dragStart;
        private bool _dragging;

        // Custom Components
        private PictureBox _picLogo;
        private Label _lblHeaderTag;
        private Label _lblTitle;
        private Label _lblSubtitle;

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
            InitializeComponent();
            SetStep(0);
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            this.ClientSize = new Size(620, 520);
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
                using (Pen linePen = new Pen(Color.FromArgb(56, 108, 109, 147), 1)) // #386C6D93
                {
                    e.Graphics.DrawLine(linePen, 0, topBar.Height - 1, topBar.Width, topBar.Height - 1);
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
                ForeColor = Color.FromArgb(159, 160, 195), // #9FA0C3
                AutoSize = true,
                Location = new Point(18, 14),
                UseMnemonic = false
            };
            topBar.Controls.Add(topBrand);

            // Verified Publisher Badge
            Label badgePublisher = new Label
            {
                Text = "🛡️ v2.4.0 • DAVE_HR",
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(16, 185, 129), // #10B981
                BackColor = Color.FromArgb(24, 44, 37),
                AutoSize = true,
                Location = new Point(230, 13),
                Padding = new Padding(6, 2, 6, 2),
                UseMnemonic = false
            };
            topBar.Controls.Add(badgePublisher);

            // Close Button
            Button btnX = new Button
            {
                Text = "✕",
                Size = new Size(38, 32),
                Location = new Point(620 - 46, 7),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10f),
                ForeColor = Color.FromArgb(159, 160, 195),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            btnX.FlatAppearance.BorderSize = 0;
            btnX.FlatAppearance.MouseOverBackColor = Color.FromArgb(239, 68, 68); // #EF4444
            btnX.FlatAppearance.MouseDownBackColor = Color.FromArgb(185, 28, 28);
            btnX.Click += delegate(object s, EventArgs e) { Application.Exit(); };
            topBar.Controls.Add(btnX);

            this.Controls.Add(topBar);

            // ==================== HERO HEADER ====================
            _picLogo = new PictureBox
            {
                Size = new Size(52, 52),
                Location = new Point(32, 64),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                foreach (string res in asm.GetManifestResourceNames())
                {
                    if (res.EndsWith("vortex.png", StringComparison.OrdinalIgnoreCase))
                    {
                        using (Stream s = asm.GetManifestResourceStream(res))
                        {
                            if (s != null) _picLogo.Image = Image.FromStream(s);
                        }
                        break;
                    }
                }
            }
            catch { }
            this.Controls.Add(_picLogo);

            _lblHeaderTag = new Label
            {
                Text = "SYSTEM OPTIMIZER & ZERO-TRUST VAULT",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(129, 140, 248), // AccentGlow #818CF8
                Location = new Point(96, 62),
                AutoSize = true,
                UseMnemonic = false
            };
            this.Controls.Add(_lblHeaderTag);

            _lblTitle = new Label
            {
                Text = "Install Vortex Guardian",
                Font = new Font("Segoe UI", 16f, FontStyle.Bold),
                ForeColor = Color.FromArgb(227, 227, 232), // TextPrimary #E3E3E8
                Location = new Point(94, 80),
                AutoSize = true,
                UseMnemonic = false
            };
            this.Controls.Add(_lblTitle);

            _lblSubtitle = new Label
            {
                Text = "Autonomous RAM boost, process heuristics, zero-trust encryption & telemetry defense.",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(159, 160, 195), // TextSecondary #9FA0C3
                Location = new Point(96, 114),
                Size = new Size(490, 22),
                UseMnemonic = false
            };
            this.Controls.Add(_lblSubtitle);

            // ==================== PANEL 0: OPTIONS CARD ====================
            _optionsCard = new ModernCardPanel
            {
                Location = new Point(32, 148),
                Size = new Size(556, 276)
            };

            Label lblDest = new Label
            {
                Text = "INSTALLATION LOCATION",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(159, 160, 195),
                Location = new Point(18, 16),
                AutoSize = true,
                BackColor = Color.Transparent,
                UseMnemonic = false
            };
            _optionsCard.Controls.Add(lblDest);

            Panel txtContainer = new Panel
            {
                Location = new Point(18, 38),
                Size = new Size(414, 34),
                BackColor = Color.FromArgb(22, 22, 29) // #16161D
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
                BackColor = Color.Transparent,
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
                BackColor = Color.Transparent,
                Location = new Point(18, 128),
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
                BackColor = Color.Transparent,
                Location = new Point(18, 158),
                Size = new Size(480, 24),
                UseMnemonic = false
            };
            _optionsCard.Controls.Add(_chkLaunch);

            // Self-Contained Callout Banner
            Panel callout = new Panel
            {
                Location = new Point(18, 198),
                Size = new Size(520, 58),
                BackColor = Color.FromArgb(26, 37, 54) // #1A2536
            };
            callout.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Color.FromArgb(32, 75, 114), 1))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, callout.Width - 1, callout.Height - 1);
                }
            };

            Label lblCallout = new Label
            {
                Text = "🛡️ 100% Self-Contained Deployment:\nIncludes isolated .NET runtime engine & native binaries. Works fully offline with zero external prerequisites.",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(129, 140, 248),
                BackColor = Color.Transparent,
                Location = new Point(12, 10),
                Size = new Size(496, 38),
                UseMnemonic = false
            };
            callout.Controls.Add(lblCallout);
            _optionsCard.Controls.Add(callout);

            this.Controls.Add(_optionsCard);

            // ==================== PANEL 1: INSTALLING ====================
            _installPanel = new Panel
            {
                Location = new Point(32, 170),
                Size = new Size(556, 240),
                BackColor = Color.Transparent,
                Visible = false
            };

            _lblInstallStatus = new Label
            {
                Text = "Preparing installation package...",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(227, 227, 232),
                Location = new Point(0, 20),
                Size = new Size(556, 30),
                UseMnemonic = false
            };
            _installPanel.Controls.Add(_lblInstallStatus);

            _progressBar = new ModernProgressBar
            {
                Location = new Point(0, 64),
                Size = new Size(556, 12),
                Value = 0
            };
            _installPanel.Controls.Add(_progressBar);

            _lblInstallPercent = new Label
            {
                Text = "0%",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(129, 140, 248), // #818CF8
                Location = new Point(0, 84),
                Size = new Size(556, 26),
                TextAlign = ContentAlignment.MiddleRight,
                UseMnemonic = false
            };
            _installPanel.Controls.Add(_lblInstallPercent);

            _lblInstallDetail = new Label
            {
                Text = "Unpacking self-contained runtime dependencies and native modules into destination folder...",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(159, 160, 195),
                Location = new Point(0, 120),
                Size = new Size(556, 40),
                UseMnemonic = false
            };
            _installPanel.Controls.Add(_lblInstallDetail);

            this.Controls.Add(_installPanel);

            // ==================== PANEL 2: FINISHED ====================
            _finishedPanel = new Panel
            {
                Location = new Point(32, 150),
                Size = new Size(556, 260),
                BackColor = Color.Transparent,
                Visible = false
            };

            _picSuccess = new PictureBox
            {
                Size = new Size(68, 68),
                Location = new Point(244, 10),
                BackColor = Color.Transparent
            };
            _picSuccess.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(16, 185, 129))) // Emerald #10B981
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
                Location = new Point(0, 90),
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
                Location = new Point(0, 130),
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
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                BackColor = Color.Transparent,
                Location = new Point(176, 172),
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
                Location = new Point(32, 444),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            _btnCancel.Click += delegate(object s, EventArgs e) { Application.Exit(); };
            this.Controls.Add(_btnCancel);

            _btnInstall = new ModernButton
            {
                Text = "⚡ Install Vortex Now",
                Size = new Size(190, 42),
                Location = new Point(398, 444),
                IsPrimary = true,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold)
            };
            _btnInstall.Click += delegate(object s, EventArgs e) { StartInstallation(); };
            this.Controls.Add(_btnInstall);

            _btnFinish = new ModernButton
            {
                Text = "🚀 Finish & Launch",
                Size = new Size(190, 42),
                Location = new Point(398, 444),
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
                _lblTitle.Text = "Install Vortex Guardian";
                _lblSubtitle.Visible = true;
                _optionsCard.Visible = true;
                _installPanel.Visible = false;
                _finishedPanel.Visible = false;
                _btnCancel.Visible = true;
                _btnInstall.Visible = true;
                _btnFinish.Visible = false;
            }
            else if (_step == 1) // Installing
            {
                _lblTitle.Text = "Installing Vortex Guardian...";
                _lblSubtitle.Text = "Please wait while files, self-contained runtime, and security policies are configured.";
                _lblSubtitle.Visible = true;
                _optionsCard.Visible = false;
                _installPanel.Visible = true;
                _finishedPanel.Visible = false;
                _btnCancel.Visible = false;
                _btnInstall.Visible = false;
                _btnFinish.Visible = false;
            }
            else if (_step == 2) // Finished
            {
                _lblTitle.Visible = false;
                _lblSubtitle.Visible = false;
                _lblHeaderTag.Visible = false;
                _picLogo.Visible = false;
                _optionsCard.Visible = false;
                _installPanel.Visible = false;
                _finishedPanel.Visible = true;
                _btnCancel.Visible = false;
                _btnInstall.Visible = false;
                _btnFinish.Visible = true;
            }
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
            UpdateProgress("Registering Windows installation...", 96);
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
            _progressBar.Value = Math.Max(0, Math.Min(100, percent));
            _lblInstallPercent.Text = percent.ToString() + "%";
        }

        private static void CreateShortcutOnDesktop(string name, string targetPath)
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string lnkPath = Path.Combine(desktop, name + ".lnk");
                CreateLnk(lnkPath, targetPath, "Vortex - Windows System Guardian & RAM Optimizer");

                // OneDrive Desktop
                string oneDrive = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive", "Desktop");
                if (Directory.Exists(oneDrive) && oneDrive != desktop)
                {
                    CreateLnk(Path.Combine(oneDrive, name + ".lnk"), targetPath, "Vortex - Windows System Guardian & RAM Optimizer");
                }
            }
            catch { }
        }

        private static void CreateStartMenuShortcuts(string targetDir, string appExe, string uninstallExe)
        {
            try
            {
                string commonPrograms = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
                string startDir = Path.Combine(commonPrograms, "Vortex");
                Directory.CreateDirectory(startDir);

                CreateLnk(Path.Combine(startDir, "Vortex Guardian.lnk"), appExe, "Vortex - Windows System Guardian");
                if (File.Exists(uninstallExe))
                {
                    CreateLnk(Path.Combine(startDir, "Uninstall Vortex.lnk"), uninstallExe, "Uninstall Vortex Guardian");
                }
            }
            catch { }
        }

        private static void CreateLnk(string lnkPath, string targetPath, string description)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;

                dynamic shell = Activator.CreateInstance(shellType);
                dynamic shortcut = shell.CreateShortcut(lnkPath);
                shortcut.TargetPath = targetPath;
                shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
                shortcut.Description = description;
                shortcut.IconLocation = targetPath + ",0";
                shortcut.Save();
            }
            catch { }
        }

        private static void RegisterUninstall(string targetDir, string appExe, string uninstallExe)
        {
            try
            {
                using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey unKey = hklm.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Vortex"))
                {
                    if (unKey != null)
                    {
                        unKey.SetValue("DisplayName", "Vortex System Guardian");
                        unKey.SetValue("DisplayVersion", "2.4.0");
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
                        unKey.SetValue("DisplayVersion", "2.4.0");
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

            // Top atmospheric gradient aura
            Rectangle auraRect = new Rectangle(0, 0, this.Width, 180);
            using (LinearGradientBrush auraBrush = new LinearGradientBrush(auraRect,
                Color.FromArgb(42, 38, 73), Color.FromArgb(22, 22, 29), 90f)) // #2A2649 to #16161D
            {
                e.Graphics.FillRectangle(auraBrush, auraRect);
            }

            // Outer sleek 1px border
            using (Pen borderPen = new Pen(Color.FromArgb(56, 108, 109, 147), 1)) // #386C6D93
            {
                e.Graphics.DrawRectangle(borderPen, 0, 0, this.Width - 1, this.Height - 1);
            }
        }

        [STAThread]
        public static void Main(string[] args)
        {
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

                string targetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Vortex");
                Directory.CreateDirectory(targetDir);

                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream stream = asm.GetManifestResourceStream("package.zip"))
                {
                    if (stream != null)
                    {
                        using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read))
                        {
                            foreach (ZipArchiveEntry entry in archive.Entries)
                            {
                                string destPath = Path.Combine(targetDir, entry.FullName);
                                if (string.IsNullOrEmpty(entry.Name))
                                {
                                    Directory.CreateDirectory(destPath);
                                    continue;
                                }
                                string dir = Path.GetDirectoryName(destPath);
                                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                                entry.ExtractToFile(destPath, true);
                            }
                        }
                    }
                }

                string exePath = Path.Combine(targetDir, "Vortex.exe");
                string uninstallPath = Path.Combine(targetDir, "Uninstall.exe");
                CreateShortcutOnDesktop("Vortex Guardian", exePath);
                CreateStartMenuShortcuts(targetDir, exePath, uninstallPath);
                RegisterUninstall(targetDir, exePath, uninstallPath);
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
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            this.Cursor = Cursors.Hand;
            this.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
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
                bg = _isPressed ? Color.FromArgb(67, 56, 202) : (_isHovered ? Color.FromArgb(79, 70, 229) : Color.FromArgb(99, 102, 241)); // #6366F1
                border = bg;
                text = Color.White;
            }
            else
            {
                bg = _isPressed ? Color.FromArgb(57, 58, 86) : (_isHovered ? Color.FromArgb(45, 46, 68) : Color.FromArgb(37, 37, 55)); // #252537
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
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
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
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            // Draw rounded track
            Rectangle trackRect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            using (GraphicsPath trackPath = SetupGraphicsHelper.CreateRoundedRectangle(trackRect, 6))
            {
                using (SolidBrush trackBrush = new SolidBrush(Color.FromArgb(53, 54, 82))) // #353652
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
                        Color.FromArgb(99, 102, 241), Color.FromArgb(129, 140, 248), 0f)) // #6366F1 to #818CF8
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
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            this.BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            using (GraphicsPath path = SetupGraphicsHelper.CreateRoundedRectangle(rect, 12))
            {
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(37, 37, 55))) // #252537
                {
                    e.Graphics.FillPath(brush, path);
                }
                using (Pen pen = new Pen(Color.FromArgb(75, 76, 105), 1.2f)) // #4B4C69
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
