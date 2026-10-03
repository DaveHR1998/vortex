using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Vortex
{
    public partial class App : System.Windows.Application
    {
        private static System.Threading.Mutex? _singleInstanceMutex;
        private const string MutexName = "Global\\Vortex_SystemOptimizer_SingleInstance_Mutex";
        private const int HWND_BROADCAST = 0xffff;

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern int RegisterWindowMessage(string lpString);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            // Handle CLI Test Suite Execution
            if (e.Args != null && e.Args.Length > 0 &&
                (e.Args[0].Equals("--test", StringComparison.OrdinalIgnoreCase) ||
                 e.Args[0].Equals("/test", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = Vortex.Tests.TestSuiteExecutive.RunAll();
                Environment.Exit(exitCode);
                return;
            }

            bool createdNew = false;
            try
            {
                _singleInstanceMutex = new System.Threading.Mutex(true, MutexName, out createdNew);
            }
            catch (System.Threading.AbandonedMutexException)
            {
                // Previous instance terminated unexpectedly; take ownership
                createdNew = true;
            }
            catch
            {
                createdNew = true;
            }

            if (!createdNew)
            {
                // Verify if an actual alive Vortex process exists
                try
                {
                    var currentProc = System.Diagnostics.Process.GetCurrentProcess();
                    var runningProcs = System.Diagnostics.Process.GetProcessesByName("Vortex")
                        .Where(p => p.Id != currentProc.Id)
                        .ToList();

                    if (runningProcs.Count == 0)
                    {
                        // No active Vortex process found; stale mutex or ghost handle. Proceed!
                        createdNew = true;
                    }
                    else
                    {
                        // Another instance of Vortex is already running!
                        // Wake up and restore the existing window to the foreground
                        int msg = RegisterWindowMessage("VORTEX_RESTORE_MAIN_WINDOW");
                        PostMessage((IntPtr)HWND_BROADCAST, msg, IntPtr.Zero, IntPtr.Zero);

                        // Exit this duplicate process immediately
                        Shutdown();
                        return;
                    }
                }
                catch
                {
                    // Fallback to launching
                }
            }

            base.OnStartup(e);

            try
            {
                var mainWindow = new MainWindow();
                MainWindow = mainWindow;
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                LogCrash("MainWindow_Startup_Exception", ex);
                MessageBox.Show(
                    $"Vortex Guardian failed to initialize user interface:\n\n{ex.Message}\n\nType: {ex.GetType().Name}\nInner: {ex.InnerException?.Message}\n\n{ex.StackTrace}",
                    "Vortex Guardian - Initialization Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _singleInstanceMutex?.ReleaseMutex();
                _singleInstanceMutex?.Dispose();
            }
            catch { }
            base.OnExit(e);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogCrash("DispatcherUnhandledException", e.Exception);
            // CRUCIAL: Mark handled so the application never abruptly terminates!
            e.Handled = true;
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogCrash("DomainUnhandledException", ex);
            }
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            LogCrash("UnobservedTaskException", e.Exception);
            e.SetObserved();
        }

        private static void LogCrash(string context, Exception ex)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vortex");
                Directory.CreateDirectory(dir);
                string logFile = Path.Combine(dir, "vortex_error.log");
                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{context}] {ex.GetType().Name}: {ex.Message}\nStack: {ex.StackTrace}\nInner: {ex.InnerException?.Message}\n\n";
                File.AppendAllText(logFile, logEntry);
            }
            catch { }
        }
    }
}
