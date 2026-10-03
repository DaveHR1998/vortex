using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Vortex.Uninstaller
{
    public class UninstallerForm : Form
    {
        private readonly string _targetDir;
        private int _step = 0; // 0 = Confirm, 1 = Progress, 2 = Complete

        // UI Controls
        private Label _lblTitle;
        private Label _lblSubtitle;
        private Panel _cardPanel;
        private Label _lblLocation;
        private ProgressBar _progressBar;
        private Label _lblStatus;
        private Button _btnCancel;
        private Button _btnUninstall;
        private Button _btnClose;
        private PictureBox _picIcon;
        private Point _dragStart;
        private bool _dragging;

        public UninstallerForm(string targetDir)
        {
            _targetDir = string.IsNullOrWhiteSpace(targetDir) ? @"C:\Program Files\Vortex" : targetDir;

            InitializeComponent();
            SetStep(0);
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            this.ClientSize = new Size(540, 420);
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(11, 15, 25);
            this.ForeColor = Color.FromArgb(241, 245, 249);
            this.DoubleBuffered = true;
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            // Title Bar Drag Support
            this.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { _dragging = true; _dragStart = e.Location; } };
            this.MouseMove += (s, e) => { if (_dragging) { Point diff = new Point(e.X - _dragStart.X, e.Y - _dragStart.Y); this.Location = new Point(this.Location.X + diff.X, this.Location.Y + diff.Y); } };
            this.MouseUp += (s, e) => { _dragging = false; };

            // Custom Title Bar Area
            Panel topBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = Color.FromArgb(17, 24, 39)
            };
            topBar.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { _dragging = true; _dragStart = e.Location; } };
            topBar.MouseMove += (s, e) => { if (_dragging) { Point diff = new Point(e.X - _dragStart.X, e.Y - _dragStart.Y); this.Location = new Point(this.Location.X + diff.X, this.Location.Y + diff.Y); } };
            topBar.MouseUp += (s, e) => { _dragging = false; };

            Label topTitle = new Label
            {
                Text = "Vortex Guardian Uninstaller",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(241, 245, 249),
                AutoSize = true,
                Location = new Point(16, 12)
            };
            topBar.Controls.Add(topTitle);

            Button btnX = new Button
            {
                Text = "✕",
                Size = new Size(36, 30),
                Location = new Point(540 - 46, 7),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(148, 163, 184),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            btnX.FlatAppearance.BorderSize = 0;
            btnX.FlatAppearance.MouseOverBackColor = Color.FromArgb(51, 65, 85);
            btnX.Click += (s, e) => CloseApp();
            topBar.Controls.Add(btnX);

            this.Controls.Add(topBar);

            // Main Content Container
            Panel content = new Panel
            {
                Location = new Point(28, 60),
                Size = new Size(484, 340),
                BackColor = Color.Transparent
            };
            this.Controls.Add(content);

            // Step 0 Controls
            _lblTitle = new Label
            {
                Text = "Uninstall Vortex Guardian",
                Font = new Font("Segoe UI", 16f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 113, 113),
                Location = new Point(0, 8),
                AutoSize = true
            };
            content.Controls.Add(_lblTitle);

            _lblSubtitle = new Label
            {
                Text = "Are you sure you want to completely remove Vortex from your computer?",
                Font = new Font("Segoe UI", 10f, FontStyle.Regular),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(0, 42),
                Size = new Size(480, 24)
            };
            content.Controls.Add(_lblSubtitle);

            // Details Card
            _cardPanel = new Panel
            {
                Location = new Point(0, 76),
                Size = new Size(484, 180),
                BackColor = Color.FromArgb(22, 31, 48)
            };
            _cardPanel.Paint += (s, e) =>
            {
                using (Pen pen = new Pen(Color.FromArgb(42, 55, 78), 1))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, _cardPanel.Width - 1, _cardPanel.Height - 1);
                }
            };

            Label cardTitle = new Label
            {
                Text = "The following will be completely removed:",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(226, 232, 240),
                Location = new Point(16, 14),
                AutoSize = true
            };
            _cardPanel.Controls.Add(cardTitle);

            string[] items = new string[]
            {
                "•  Vortex Application Executable & Core Runtime Libraries",
                "•  Desktop and Start Menu Shortcuts",
                "•  Windows Startup Registration & Background Service Keys",
                "•  Windows Add/Remove Programs Registry Entries"
            };

            int yOffset = 42;
            foreach (string item in items)
            {
                Label lbl = new Label
                {
                    Text = item,
                    Font = new Font("Segoe UI", 9f),
                    ForeColor = Color.FromArgb(148, 163, 184),
                    Location = new Point(16, yOffset),
                    AutoSize = true
                };
                _cardPanel.Controls.Add(lbl);
                yOffset += 24;
            }

            _lblLocation = new Label
            {
                Text = "Location: " + _targetDir,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(16, 148),
                Size = new Size(450, 20)
            };
            _cardPanel.Controls.Add(_lblLocation);

            content.Controls.Add(_cardPanel);

            // Progress Bar & Status (Step 1)
            _lblStatus = new Label
            {
                Text = "Closing running processes...",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(0, 100),
                Size = new Size(480, 28),
                Visible = false
            };
            content.Controls.Add(_lblStatus);

            _progressBar = new ProgressBar
            {
                Location = new Point(0, 140),
                Size = new Size(484, 10),
                Style = ProgressBarStyle.Marquee,
                Visible = false
            };
            content.Controls.Add(_progressBar);

            // Icon for complete state (Step 2)
            _picIcon = new PictureBox
            {
                Size = new Size(64, 64),
                Location = new Point(210, 50),
                Visible = false
            };
            _picIcon.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(16, 185, 129)))
                {
                    e.Graphics.FillEllipse(b, 4, 4, 56, 56);
                }
                using (Pen p = new Pen(Color.White, 4))
                {
                    p.StartCap = LineCap.Round;
                    p.EndCap = LineCap.Round;
                    e.Graphics.DrawLines(p, new Point[] { new Point(20, 32), new Point(28, 42), new Point(44, 22) });
                }
            };
            content.Controls.Add(_picIcon);

            // Bottom Actions
            _btnCancel = new Button
            {
                Text = "Cancel",
                Size = new Size(110, 40),
                Location = new Point(230, 280),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                BackColor = Color.FromArgb(30, 41, 59),
                Cursor = Cursors.Hand
            };
            _btnCancel.FlatAppearance.BorderSize = 1;
            _btnCancel.FlatAppearance.BorderColor = Color.FromArgb(51, 65, 85);
            _btnCancel.Click += (s, e) => CloseApp();
            content.Controls.Add(_btnCancel);

            _btnUninstall = new Button
            {
                Text = "Uninstall",
                Size = new Size(130, 40),
                Location = new Point(354, 280),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(220, 38, 38),
                Cursor = Cursors.Hand
            };
            _btnUninstall.FlatAppearance.BorderSize = 0;
            _btnUninstall.Click += (s, e) => StartUninstall();
            content.Controls.Add(_btnUninstall);

            _btnClose = new Button
            {
                Text = "Close",
                Size = new Size(130, 40),
                Location = new Point(177, 260),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(109, 74, 255),
                Cursor = Cursors.Hand,
                Visible = false
            };
            _btnClose.FlatAppearance.BorderSize = 0;
            _btnClose.Click += (s, e) => CloseApp();
            content.Controls.Add(_btnClose);

            this.ResumeLayout(false);
        }

        private void SetStep(int step)
        {
            _step = step;
            if (_step == 0) // Confirm
            {
                _lblTitle.Visible = true;
                _lblSubtitle.Visible = true;
                _cardPanel.Visible = true;
                _lblStatus.Visible = false;
                _progressBar.Visible = false;
                _picIcon.Visible = false;
                _btnCancel.Visible = true;
                _btnUninstall.Visible = true;
                _btnClose.Visible = false;
            }
            else if (_step == 1) // In Progress
            {
                _lblTitle.Text = "Uninstalling Vortex...";
                _lblTitle.ForeColor = Color.FromArgb(241, 245, 249);
                _lblSubtitle.Text = "Please wait while Vortex is removed from your system.";
                _lblSubtitle.Visible = true;
                _cardPanel.Visible = false;
                _lblStatus.Visible = true;
                _progressBar.Visible = true;
                _picIcon.Visible = false;
                _btnCancel.Visible = false;
                _btnUninstall.Visible = false;
                _btnClose.Visible = false;
            }
            else if (_step == 2) // Complete
            {
                _lblTitle.Text = "Uninstallation Complete";
                _lblTitle.ForeColor = Color.FromArgb(241, 245, 249);
                _lblTitle.Location = new Point(130, 130);
                _lblSubtitle.Text = "Vortex Guardian has been cleanly removed from your computer.";
                _lblSubtitle.Location = new Point(70, 168);
                _lblSubtitle.Visible = true;
                _cardPanel.Visible = false;
                _lblStatus.Visible = false;
                _progressBar.Visible = false;
                _picIcon.Visible = true;
                _btnCancel.Visible = false;
                _btnUninstall.Visible = false;
                _btnClose.Visible = true;
            }
        }

        private void StartUninstall()
        {
            SetStep(1);

            Thread t = new Thread(() =>
            {
                try
                {
                    UpdateStatus("Closing running Vortex instances...");
                    KillVortexProcesses();
                    Thread.Sleep(500);

                    UpdateStatus("Removing shortcuts...");
                    RemoveShortcuts();
                    Thread.Sleep(400);

                    UpdateStatus("Removing Windows registry entries...");
                    RemoveRegistryKeys();
                    Thread.Sleep(400);

                    UpdateStatus("Deleting files...");
                    RemoveInstallFiles();
                    Thread.Sleep(600);

                    UpdateStatus("Finishing up...");
                    Thread.Sleep(400);
                }
                catch { }

                this.Invoke(new Action(() => SetStep(2)));
            });
            t.IsBackground = true;
            t.Start();
        }

        private void UpdateStatus(string text)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => _lblStatus.Text = text));
            }
            else
            {
                _lblStatus.Text = text;
            }
        }

        private static void KillVortexProcesses()
        {
            string[] names = new string[] { "Vortex" };
            foreach (var name in names)
            {
                try
                {
                    foreach (var p in Process.GetProcessesByName(name))
                    {
                        try { p.Kill(); p.WaitForExit(2000); } catch { }
                    }
                }
                catch { }
            }
        }

        private static void RemoveShortcuts()
        {
            try
            {
                string[] desktopDirs = new string[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive", "Desktop")
                };

                foreach (var dir in desktopDirs)
                {
                    if (string.IsNullOrEmpty(dir)) continue;
                    string link = Path.Combine(dir, "Vortex Guardian.lnk");
                    if (File.Exists(link))
                    {
                        try { File.Delete(link); } catch { }
                    }
                }

                string[] startMenuDirs = new string[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Vortex"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Vortex"),
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
                };

                foreach (var dir in startMenuDirs)
                {
                    if (string.IsNullOrEmpty(dir)) continue;
                    string link = Path.Combine(dir, "Vortex Guardian.lnk");
                    if (File.Exists(link))
                    {
                        try { File.Delete(link); } catch { }
                    }
                }
            }
            catch { }
        }

        private static void RemoveRegistryKeys()
        {
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var unKey = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", true))
                {
                    if (unKey != null)
                    {
                        unKey.DeleteSubKeyTree("Vortex", false);
                    }
                }
            }
            catch { }

            try
            {
                using (var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default))
                using (var unKey = hkcu.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", true))
                {
                    if (unKey != null)
                    {
                        unKey.DeleteSubKeyTree("Vortex", false);
                    }
                }
            }
            catch { }

            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        key.DeleteValue("Vortex", false);
                    }
                }
            }
            catch { }
        }

        private void RemoveInstallFiles()
        {
            if (string.IsNullOrWhiteSpace(_targetDir) || !Directory.Exists(_targetDir))
                return;

            try
            {
                foreach (string file in Directory.GetFiles(_targetDir, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                        File.Delete(file);
                    }
                    catch { }
                }

                try
                {
                    Directory.Delete(_targetDir, true);
                }
                catch { }
            }
            catch { }
        }

        private void CloseApp()
        {
            Process currentProc = Process.GetCurrentProcess();
            string currentExe = (currentProc.MainModule != null) ? currentProc.MainModule.FileName : string.Empty;
            string currentDir = Path.GetDirectoryName(currentExe) ?? string.Empty;
            string tempDir = Path.Combine(Path.GetTempPath(), "Vortex_Uninstall");

            if (currentDir.StartsWith(tempDir, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = string.Format("/c timeout /t 2 /nobreak >nul & rmdir /s /q \"{0}\"", tempDir),
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    Process.Start(psi);
                }
                catch { }
            }

            Application.Exit();
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
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Process currentProc = Process.GetCurrentProcess();
            string currentExe = (currentProc.MainModule != null) ? currentProc.MainModule.FileName : string.Empty;
            string currentDir = Path.GetDirectoryName(currentExe) ?? string.Empty;
            string tempDir = Path.Combine(Path.GetTempPath(), "Vortex_Uninstall");

            bool isCleanup = false;
            string targetInstallDir = @"C:\Program Files\Vortex";

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Equals("--cleanup", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    isCleanup = true;
                    targetInstallDir = args[i + 1].Trim('\"');
                }
            }

            // Self-replicate to %TEMP% so the installed directory is completely unlocked for deletion!
            if (!isCleanup && !currentDir.StartsWith(tempDir, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Directory.CreateDirectory(tempDir);
                    string tempExe = Path.Combine(tempDir, "Uninstall.exe");
                    File.Copy(currentExe, tempExe, true);

                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = tempExe,
                        Arguments = string.Format("--cleanup \"{0}\"", currentDir),
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                    return;
                }
                catch { }
            }

            Application.Run(new UninstallerForm(targetInstallDir));
        }
    }
}
