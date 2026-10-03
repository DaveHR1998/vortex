using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Vortex.Core.SystemIntegration
{
    public class TrayIconService : IDisposable
    {
        private const int NIM_ADD = 0x00000000;
        private const int NIM_MODIFY = 0x00000001;
        private const int NIM_DELETE = 0x00000002;

        private const int NIF_MESSAGE = 0x00000001;
        private const int NIF_ICON = 0x00000002;
        private const int NIF_TIP = 0x00000004;

        private const int WM_USER = 0x0400;
        private const int WM_TRAYICON = WM_USER + 101;

        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONUP = 0x0205;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public int uVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ICONINFO
        {
            public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIconW(int dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr CreateIconIndirect(ref ICONINFO icon);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateBitmap(int nWidth, int nHeight, uint cPlanes, uint cBitsPerPel, IntPtr lpvBits);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int RegisterWindowMessage(string lpString);

        private readonly Window _owner;
        private IntPtr _hwnd = IntPtr.Zero;
        private HwndSource? _hwndSource;
        private readonly Action _openDashboard;
        private readonly Action _turboBoost;
        private readonly Action _toggleOrb;
        private readonly Action _toggleFocus;
        private readonly Action _exitApp;

        private readonly int _taskbarCreatedMsg;
        private readonly int _restoreVortexMsg;

        private NOTIFYICONDATA _nid;
        private IntPtr _hIcon = IntPtr.Zero;
        private ContextMenu? _contextMenu;
        private bool _isDisposed = false;

        public TrayIconService(
            Window ownerWindow,
            Action openDashboard,
            Action turboBoost,
            Action toggleOrb,
            Action toggleFocus,
            Action exitApp)
        {
            _owner = ownerWindow;
            _openDashboard = openDashboard;
            _turboBoost = turboBoost;
            _toggleOrb = toggleOrb;
            _toggleFocus = toggleFocus;
            _exitApp = exitApp;

            _taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");
            _restoreVortexMsg  = RegisterWindowMessage("VORTEX_RESTORE_MAIN_WINDOW");

            ownerWindow.SourceInitialized += (s, e) =>
            {
                try
                {
                    _hwnd = new WindowInteropHelper(ownerWindow).Handle;
                    _hwndSource = HwndSource.FromHwnd(_hwnd);
                    _hwndSource?.AddHook(WndProc);
                    InitIcon();
                }
                catch { }
            };

            BuildContextMenu();

            // Handle system sleep/hibernate resume to re-inject tray icon
            try
            {
                Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
            }
            catch { }
        }

        private void InitIcon()
        {
            try
            {
                BitmapSource? frame = null;
                string localFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vortex.png");
                if (File.Exists(localFile))
                {
                    frame = new BitmapImage(new Uri(localFile, UriKind.Absolute));
                }
                else
                {
                    var iconUri = new Uri("pack://application:,,,/vortex.png");
                    frame = BitmapFrame.Create(iconUri);
                }

                if (frame != null)
                {
                    _hIcon = CreateHIcon(frame);
                }
            }
            catch
            {
                _hIcon = IntPtr.Zero;
            }

            _nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA)),
                hWnd = _hwnd,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = _hIcon,
                szTip = "Vortex - System Guardian (Double-click to open)"
            };

            Shell_NotifyIconW(NIM_ADD, ref _nid);
        }

        private static IntPtr CreateHIcon(BitmapSource source)
        {
            int targetWidth = 32;
            int targetHeight = 32;
            var scale = new System.Windows.Media.ScaleTransform((double)targetWidth / source.PixelWidth, (double)targetHeight / source.PixelHeight);
            var scaledBmp = new TransformedBitmap(source, scale);
            var bgraBmp = new FormatConvertedBitmap(scaledBmp, System.Windows.Media.PixelFormats.Bgra32, null, 0);

            int stride = targetWidth * 4;
            byte[] pixelData = new byte[targetHeight * stride];
            bgraBmp.CopyPixels(pixelData, stride, 0);

            GCHandle handle = GCHandle.Alloc(pixelData, GCHandleType.Pinned);
            try
            {
                IntPtr pBits = handle.AddrOfPinnedObject();
                IntPtr hbmColor = CreateBitmap(targetWidth, targetHeight, 1, 32, pBits);
                IntPtr hbmMask = CreateBitmap(targetWidth, targetHeight, 1, 1, IntPtr.Zero);

                var iconInfo = new ICONINFO
                {
                    fIcon = true,
                    xHotspot = 0,
                    yHotspot = 0,
                    hbmMask = hbmMask,
                    hbmColor = hbmColor
                };

                IntPtr hIcon = CreateIconIndirect(ref iconInfo);
                DeleteObject(hbmColor);
                DeleteObject(hbmMask);
                return hIcon;
            }
            finally
            {
                handle.Free();
            }
        }

        private void BuildContextMenu()
        {
            _contextMenu = new ContextMenu();

            var openItem = new MenuItem { Header = "🖥️ Open Vortex Dashboard", FontWeight = FontWeights.Bold };
            openItem.Click += (s, e) => _openDashboard();
            _contextMenu.Items.Add(openItem);

            var boostItem = new MenuItem { Header = "⚡ Instant Turbo Boost" };
            boostItem.Click += (s, e) => _turboBoost();
            _contextMenu.Items.Add(boostItem);

            var orbItem = new MenuItem { Header = "🔮 Toggle Desktop Orb" };
            orbItem.Click += (s, e) => _toggleOrb();
            _contextMenu.Items.Add(orbItem);

            var focusItem = new MenuItem { Header = "🎯 Toggle Gaming / Focus Mode" };
            focusItem.Click += (s, e) => _toggleFocus();
            _contextMenu.Items.Add(focusItem);

            _contextMenu.Items.Add(new Separator());

            var exitItem = new MenuItem { Header = "❌ Exit Vortex Permanently" };
            exitItem.Click += (s, e) => _exitApp();
            _contextMenu.Items.Add(exitItem);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_TRAYICON)
            {
                int mouseMsg = lParam.ToInt32();
                if (mouseMsg == WM_LBUTTONUP || mouseMsg == WM_LBUTTONDBLCLK)
                {
                    _openDashboard();
                    handled = true;
                }
                else if (mouseMsg == WM_RBUTTONUP)
                {
                    ShowTrayMenu();
                    handled = true;
                }
            }
            else if (_taskbarCreatedMsg != 0 && msg == _taskbarCreatedMsg)
            {
                // Windows taskbar was recreated (e.g. explorer restart or waking up from sleep/hibernate)
                Shell_NotifyIconW(NIM_ADD, ref _nid);
                handled = true;
            }
            else if (_restoreVortexMsg != 0 && msg == _restoreVortexMsg)
            {
                // A duplicate process was started by the user; bring this instance to front
                _owner.Dispatcher.Invoke(() =>
                {
                    _openDashboard();
                    _owner.Topmost = true;
                    _owner.Topmost = false;
                    _owner.Focus();
                });
                Shell_NotifyIconW(NIM_ADD, ref _nid);
                handled = true;
            }

            return IntPtr.Zero;
        }

        private void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
        {
            if (e.Mode == Microsoft.Win32.PowerModes.Resume)
            {
                // System resumed from sleep or hibernate: re-register the tray icon after explorer wakes
                _owner.Dispatcher.InvokeAsync(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(1500);
                    Shell_NotifyIconW(NIM_ADD, ref _nid);
                    Shell_NotifyIconW(NIM_MODIFY, ref _nid);
                });
            }
        }

        private void ShowTrayMenu()
        {
            if (_contextMenu != null)
            {
                SetForegroundWindow(_hwnd);
                _contextMenu.IsOpen = true;
            }
        }

        public void ShowNotification(string title, string text)
        {
            try
            {
                _nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | 0x00000010; // NIF_INFO
                _nid.szInfoTitle = title;
                _nid.szInfo = text;
                _nid.dwInfoFlags = 0x00000001; // NIIF_INFO
                Shell_NotifyIconW(NIM_MODIFY, ref _nid);
            }
            catch { }
        }

        /// <summary>
        /// Show a tray balloon notification with severity: 1=Info, 2=Warning, 3=Error.
        /// </summary>
        public void ShowBalloon(string title, string text, int severity = 1)
        {
            try
            {
                _nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | 0x00000010; // NIF_INFO
                _nid.szInfoTitle = title;
                _nid.szInfo = text;
                _nid.dwInfoFlags = severity; // 1=NIIF_INFO, 2=NIIF_WARNING, 3=NIIF_ERROR
                Shell_NotifyIconW(NIM_MODIFY, ref _nid);
            }
            catch { }
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                try
                {
                    Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                }
                catch { }

                Shell_NotifyIconW(NIM_DELETE, ref _nid);
                if (_hIcon != IntPtr.Zero)
                {
                    DestroyIcon(_hIcon);
                    _hIcon = IntPtr.Zero;
                }
            }
        }
    }
}
