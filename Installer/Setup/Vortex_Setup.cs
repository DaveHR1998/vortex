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

namespace Vortex.Setup
{
    public class SetupForm : Form
    {
        private int _step = 0; // 0 = Options, 1 = Installing, 2 = Finished
        private Point _dragStart;
        private bool _dragging;

        // Controls
        private Label _lblTitle;
        private Label _lblSubtitle;
        private Panel _optionsPanel;
        private TextBox _txtPath;
        private Button _btnBrowse;
        private CheckBox _chkDesktop;
        private CheckBox _chkStartMenu;
        private CheckBox _chkLaunch;

        private Panel _installPanel;
        private Label _lblStatus;
        private ProgressBar _progressBar;
        private Label _lblPercent;

        private Panel _finishedPanel;
        private PictureBox _picSuccess;
        private Label _lblFinishedTitle;
        private Label _lblFinishedSubtitle;
        private CheckBox _chkLaunchFinished;

        private Button _btnCancel;
        private Button _btnInstall;
        private Button _btnFinish;
        private PictureBox _picLogo;

        public SetupForm()
        {
            InitializeComponent();
            SetStep(0);
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            this.ClientSize = new Size(580, 480);
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(11, 15, 25);
            this.ForeColor = Color.FromArgb(241, 245, 249);
            this.DoubleBuffered = true;
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            // Drag Support
            this.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { _dragging = true; _dragStart = e.Location; } };
            this.MouseMove += (s, e) => { if (_dragging) { Point diff = new Point(e.X - _dragStart.X, e.Y - _dragStart.Y); this.Location = new Point(this.Location.X + diff.X, this.Location.Y + diff.Y); } };
            this.MouseUp += (s, e) => { _dragging = false; };

            // Title Bar
            Panel topBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = Color.FromArgb(17, 24, 39)
            };
            topBar.Paint += (s, e) =>
            {
                using (Pen linePen = new Pen(Color.FromArgb(42, 55, 78), 1))
                {
                    e.Graphics.DrawLine(linePen, 0, topBar.Height - 1, topBar.Width, topBar.Height - 1);
                }
            };
            topBar.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { _dragging = true; _dragStart = e.Location; } };
            topBar.MouseMove += (s, e) => { if (_dragging) { Point diff = new Point(e.X - _dragStart.X, e.Y - _dragStart.Y); this.Location = new Point(this.Location.X + diff.X, this.Location.Y + diff.Y); } };
            topBar.MouseUp += (s, e) => { _dragging = false; };

            Label topTitle = new Label
            {
                Text = "Vortex Guardian Setup Wizard • v1.0.0",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(241, 245, 249),
                AutoSize = true,
                Location = new Point(18, 12),
                UseMnemonic = false
            };
            topBar.Controls.Add(topTitle);

            Button btnX = new Button
            {
                Text = "✕",
                Size = new Size(36, 30),
                Location = new Point(580 - 46, 7),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(148, 163, 184),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            btnX.FlatAppearance.BorderSize = 0;
            btnX.FlatAppearance.MouseOverBackColor = Color.FromArgb(220, 38, 38);
            btnX.Click += (s, e) => Application.Exit();
            topBar.Controls.Add(btnX);

            this.Controls.Add(topBar);

            // Brand Logo Header
            _picLogo = new PictureBox
            {
                Size = new Size(54, 54),
                Location = new Point(32, 60),
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

            _lblTitle = new Label
            {
                Text = "Install Vortex Guardian",
                Font = new Font("Segoe UI", 16f, FontStyle.Bold),
                ForeColor = Color.FromArgb(241, 245, 249),
                Location = new Point(94, 62),
                AutoSize = true,
                UseMnemonic = false
            };
            this.Controls.Add(_lblTitle);

            _lblSubtitle = new Label
            {
                Text = "High-performance memory optimizer, process intelligence & storage provenance.",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(96, 96),
                Size = new Size(450, 24),
                UseMnemonic = false
            };
            this.Controls.Add(_lblSubtitle);

            // ==================== PANEL 0: OPTIONS ====================
            _optionsPanel = new Panel
            {
                Location = new Point(32, 134),
                Size = new Size(516, 246),
                BackColor = Color.FromArgb(22, 31, 48)
            };
            _optionsPanel.Paint += (s, e) =>
            {
                using (Pen pen = new Pen(Color.FromArgb(42, 55, 78), 1))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, _optionsPanel.Width - 1, _optionsPanel.Height - 1);
                }
            };

            Label lblDest = new Label
            {
                Text = "Installation Directory:",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(226, 232, 240),
                Location = new Point(18, 14),
                AutoSize = true,
                UseMnemonic = false
            };
            _optionsPanel.Controls.Add(lblDest);

            _txtPath = new TextBox
            {
                Text = @"C:\Program Files\Vortex",
                Font = new Font("Segoe UI", 9.5f),
                BackColor = Color.FromArgb(11, 15, 25),
                ForeColor = Color.FromArgb(241, 245, 249),
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(18, 38),
                Size = new Size(380, 26)
            };
            _optionsPanel.Controls.Add(_txtPath);

            _btnBrowse = new Button
            {
                Text = "Browse...",
                Font = new Font("Segoe UI", 9f),
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.FromArgb(241, 245, 249),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(408, 37),
                Size = new Size(90, 27),
                Cursor = Cursors.Hand
            };
            _btnBrowse.FlatAppearance.BorderSize = 1;
            _btnBrowse.FlatAppearance.BorderColor = Color.FromArgb(51, 65, 85);
            _btnBrowse.Click += (s, e) =>
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
            _optionsPanel.Controls.Add(_btnBrowse);

            _chkDesktop = new CheckBox
            {
                Text = "Create Desktop Shortcut (with Vortex Icon)",
                Checked = true,
                ForeColor = Color.FromArgb(226, 232, 240),
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(18, 80),
                Size = new Size(400, 24),
                UseMnemonic = false
            };
            _optionsPanel.Controls.Add(_chkDesktop);

            _chkStartMenu = new CheckBox
            {
                Text = "Create Start Menu Folder & Shortcuts",
                Checked = true,
                ForeColor = Color.FromArgb(226, 232, 240),
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(18, 114),
                Size = new Size(400, 24),
                UseMnemonic = false
            };
            _optionsPanel.Controls.Add(_chkStartMenu);

            _chkLaunch = new CheckBox
            {
                Text = "Launch Vortex Guardian immediately after installation",
                Checked = true,
                ForeColor = Color.FromArgb(226, 232, 240),
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(18, 148),
                Size = new Size(450, 24),
                UseMnemonic = false
            };
            _optionsPanel.Controls.Add(_chkLaunch);

            Label lblAdminNote = new Label
            {
                Text = "🛡️ Administrative installation: Configures clean system service, startup & registry keys.",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(18, 190),
                Size = new Size(480, 20),
                UseMnemonic = false
            };
            _optionsPanel.Controls.Add(lblAdminNote);

            this.Controls.Add(_optionsPanel);

            // ==================== PANEL 1: INSTALLING ====================
            _installPanel = new Panel
            {
                Location = new Point(32, 160),
                Size = new Size(516, 200),
                BackColor = Color.Transparent,
                Visible = false
            };

            _lblStatus = new Label
            {
                Text = "Preparing installation...",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(241, 245, 249),
                Location = new Point(0, 20),
                Size = new Size(516, 28),
                UseMnemonic = false
            };
            _installPanel.Controls.Add(_lblStatus);

            _progressBar = new ProgressBar
            {
                Location = new Point(0, 58),
                Size = new Size(516, 14),
                Value = 0
            };
            _installPanel.Controls.Add(_progressBar);

            _lblPercent = new Label
            {
                Text = "0%",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 229, 153),
                Location = new Point(0, 84),
                Size = new Size(516, 24),
                TextAlign = ContentAlignment.MiddleRight,
                UseMnemonic = false
            };
            _installPanel.Controls.Add(_lblPercent);

            this.Controls.Add(_installPanel);

            // ==================== PANEL 2: FINISHED ====================
            _finishedPanel = new Panel
            {
                Location = new Point(32, 140),
                Size = new Size(516, 230),
                BackColor = Color.Transparent,
                Visible = false
            };

            _picSuccess = new PictureBox
            {
                Size = new Size(60, 60),
                Location = new Point(228, 10)
            };
            _picSuccess.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(16, 185, 129)))
                {
                    e.Graphics.FillEllipse(b, 2, 2, 56, 56);
                }
                using (Pen p = new Pen(Color.White, 4))
                {
                    p.StartCap = LineCap.Round;
                    p.EndCap = LineCap.Round;
                    e.Graphics.DrawLines(p, new Point[] { new Point(16, 30), new Point(26, 42), new Point(44, 18) });
                }
            };
            _finishedPanel.Controls.Add(_picSuccess);

            _lblFinishedTitle = new Label
            {
                Text = "Installation Complete!",
                Font = new Font("Segoe UI", 16f, FontStyle.Bold),
                ForeColor = Color.FromArgb(241, 245, 249),
                Location = new Point(0, 80),
                Size = new Size(516, 32),
                TextAlign = ContentAlignment.MiddleCenter,
                UseMnemonic = false
            };
            _finishedPanel.Controls.Add(_lblFinishedTitle);

            _lblFinishedSubtitle = new Label
            {
                Text = "Vortex System Guardian is now installed and ready to safeguard your PC.",
                Font = new Font("Segoe UI", 10f, FontStyle.Regular),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(0, 116),
                Size = new Size(516, 24),
                TextAlign = ContentAlignment.MiddleCenter,
                UseMnemonic = false
            };
            _finishedPanel.Controls.Add(_lblFinishedSubtitle);

            _chkLaunchFinished = new CheckBox
            {
                Text = "Launch Vortex Guardian now",
                Checked = true,
                ForeColor = Color.FromArgb(241, 245, 249),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Location = new Point(160, 154),
                Size = new Size(240, 28),
                UseMnemonic = false
            };
            _finishedPanel.Controls.Add(_chkLaunchFinished);

            this.Controls.Add(_finishedPanel);

            // ==================== BOTTOM ACTION BUTTONS ====================
            _btnCancel = new Button
            {
                Text = "Cancel",
                Size = new Size(110, 42),
                Location = new Point(32, 408),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                BackColor = Color.FromArgb(30, 41, 59),
                Cursor = Cursors.Hand
            };
            _btnCancel.FlatAppearance.BorderSize = 1;
            _btnCancel.FlatAppearance.BorderColor = Color.FromArgb(51, 65, 85);
            _btnCancel.Click += (s, e) => Application.Exit();
            this.Controls.Add(_btnCancel);

            _btnInstall = new Button
            {
                Text = "⚡ Install Vortex Now",
                Size = new Size(180, 42),
                Location = new Point(368, 408),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(109, 74, 255),
                Cursor = Cursors.Hand
            };
            _btnInstall.FlatAppearance.BorderSize = 0;
            _btnInstall.Click += (s, e) => StartInstallation();
            this.Controls.Add(_btnInstall);

            _btnFinish = new Button
            {
                Text = "🚀 Finish & Launch",
                Size = new Size(180, 42),
                Location = new Point(368, 408),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(16, 185, 129),
                Cursor = Cursors.Hand,
                Visible = false
            };
            _btnFinish.FlatAppearance.BorderSize = 0;
            _btnFinish.Click += (s, e) =>
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
                _optionsPanel.Visible = true;
                _installPanel.Visible = false;
                _finishedPanel.Visible = false;
                _btnCancel.Visible = true;
                _btnInstall.Visible = true;
                _btnFinish.Visible = false;
            }
            else if (_step == 1) // Installing
            {
                _lblTitle.Text = "Installing Vortex Guardian...";
                _lblSubtitle.Text = "Please wait while files and dependencies are configured on your PC.";
                _lblSubtitle.Visible = true;
                _optionsPanel.Visible = false;
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
                _optionsPanel.Visible = false;
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

            Thread worker = new Thread(() =>
            {
                try
                {
                    PerformInstall(targetDir, makeDesktopShortcut, makeStartMenuShortcut);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Installation encountered an error: " + ex.Message, "Installation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                this.Invoke(new Action(() => SetStep(2)));
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private void PerformInstall(string targetDir, bool desktopShortcut, bool startMenuShortcut)
        {
            UpdateProgress("Preparing installation directory...", 5);

            // Kill running Vortex instances so files are not locked
            try
            {
                foreach (var p in Process.GetProcessesByName("Vortex"))
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
                        int pct = 5 + (int)((double)i / count * 80);

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
            UpdateProgress("Creating application shortcuts...", 88);
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
            UpdateProgress("Registering Windows installation...", 95);
            RegisterUninstall(targetDir, exePath, uninstallPath);

            UpdateProgress("Finalizing installation...", 100);
            Thread.Sleep(500);
        }

        private void UpdateProgress(string status, int percent)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateProgress(status, percent)));
                return;
            }

            _lblStatus.Text = status;
            _progressBar.Value = Math.Max(0, Math.Min(100, percent));
            _lblPercent.Text = percent.ToString() + "%";
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
                        unKey.SetValue("DisplayVersion", "1.0.0");
                        unKey.SetValue("Publisher", "Vortex Technologies");
                        unKey.SetValue("InstallLocation", targetDir);
                        unKey.SetValue("DisplayIcon", appExe + ",0");
                        unKey.SetValue("UninstallString", string.Format("\"{0}\"", uninstallExe));
                        unKey.SetValue("QuietUninstallString", string.Format("\"{0}\"", uninstallExe));
                        unKey.SetValue("EstimatedSize", 4500, RegistryValueKind.DWord);
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
                        unKey.SetValue("DisplayVersion", "1.0.0");
                        unKey.SetValue("Publisher", "Vortex Technologies");
                        unKey.SetValue("InstallLocation", targetDir);
                        unKey.SetValue("DisplayIcon", appExe + ",0");
                        unKey.SetValue("UninstallString", string.Format("\"{0}\"", uninstallExe));
                        unKey.SetValue("QuietUninstallString", string.Format("\"{0}\"", uninstallExe));
                        unKey.SetValue("EstimatedSize", 4500, RegistryValueKind.DWord);
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
            using (Pen borderPen = new Pen(Color.FromArgb(59, 45, 113), 1))
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
}
