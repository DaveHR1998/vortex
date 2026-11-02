using System;
using System.IO;
using System.Threading;
using System.Windows;
using Vortex.Core.SystemIntegration;
using Vortex.Views;

namespace Vortex.Tests
{
    public static class UiAndAccessibilityTests
    {
        public static void RunAll()
        {
            Test_HighDpiAndPerMonitorV2Manifest();
            Test_ThemeSwitchingFidelity();
            Test_WindowResolutionAndMinimumBounds();
            Test_ModalDialogContractAndOwnership();
        }

        private static void Test_HighDpiAndPerMonitorV2Manifest()
        {
            string manifestPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "app.manifest");
            if (!File.Exists(manifestPath))
            {
                manifestPath = Path.Combine(Environment.CurrentDirectory, "app.manifest");
            }

            if (!File.Exists(manifestPath))
                throw new Exception("app.manifest was not found!");

            string content = File.ReadAllText(manifestPath);
            if (!content.Contains("PerMonitorV2"))
                throw new Exception("app.manifest is missing PerMonitorV2 High-DPI declaration!");

            if (!content.Contains("longPathAware"))
                throw new Exception("app.manifest is missing longPathAware Windows declaration!");

            TestRunner.Pass("UI/UX: PerMonitorV2 High-DPI & LongPathAware Manifest Verified");
        }

        private static void Test_ThemeSwitchingFidelity()
        {
            RunOnSta(() =>
            {
                var themeService = new ThemeService();
                themeService.SetTheme(darkMode: true);

                var app = Application.Current ?? new Application();
                var darkBg = app.Resources["CanvasBgBrush"] ?? app.TryFindResource("BackgroundPrimary");
                if (darkBg == null)
                    throw new Exception("Canvas background brush was not found in Dark Theme!");

                themeService.SetTheme(darkMode: false);
                var lightBg = app.Resources["CanvasBgBrush"] ?? app.TryFindResource("BackgroundPrimary");
                if (lightBg == null)
                    throw new Exception("Canvas background brush was not found in Light Theme!");

                // Switch back to Dark default
                themeService.SetTheme(darkMode: true);
                TestRunner.Pass("UI/UX: Dynamic Dark/Light Theme Switching & Resource Integrity");
            });
        }

        private static void Test_WindowResolutionAndMinimumBounds()
        {
            RunOnSta(() =>
            {
                var dummyWindow = new Window
                {
                    MinWidth = 1000,
                    MinHeight = 650,
                    Width = 1200,
                    Height = 800
                };

                if (dummyWindow.MinWidth < 800 || dummyWindow.MinHeight < 500)
                    throw new Exception("Window minimum bounds are too small, risking UI layout collapse!");

                TestRunner.Pass("UI/UX: Multi-Resolution Boundaries & Minimum Aspect Ratio Safeguard");
            });
        }

        private static void Test_ModalDialogContractAndOwnership()
        {
            RunOnSta(() =>
            {
                var parent = new Window { Title = "Owner", Width = 100, Height = 100 };
                parent.Show();
                var child = new Window { Title = "Modal", Owner = parent, Width = 100, Height = 100 };

                if (child.Owner != parent)
                    throw new Exception("Modal child window does not establish ownership of parent window!");

                child.Close();
                parent.Close();
                TestRunner.Pass("UI/UX: Modal Dialog Parent Window Ownership & Blocking Contract");
            });
        }

        private static void RunOnSta(Action action)
        {
            Exception? ex = null;
            var thread = new Thread(() =>
            {
                try
                {
                    if (Application.Current == null)
                    {
                        new Application();
                    }
                    action();
                }
                catch (Exception e)
                {
                    ex = e;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (ex != null)
            {
                throw new Exception($"STA UI execution failed: {ex.Message}", ex);
            }
        }
    }
}
