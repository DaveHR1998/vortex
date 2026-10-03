using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Vortex.Tests
{
    public static class TestSuiteExecutive
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        public static int RunAll()
        {
            if (!AttachConsole(-1))
            {
                AllocConsole();
            }

            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==========================================================");
            Console.WriteLine("    VORTEX SYSTEM GUARDIAN - AUTOMATED TEST SUITE        ");
            Console.WriteLine("==========================================================");
            Console.ResetColor();

            var totalSw = Stopwatch.StartNew();

            // 1. Vault Cryptographic Engine Tests
            TestRunner.ExecuteGroup("Vault AES-256-GCM Cryptographic Tests", () =>
            {
                VaultCryptoTests.RunAll();
            });

            // 2. Database Schema & Persistence Tests
            TestRunner.ExecuteGroup("SQLite Database & Settings CRUD Tests", () =>
            {
                DatabaseTests.RunAll();
            });

            // 3. Folder Guard & Blacklist Tests
            TestRunner.ExecuteGroup("Folder Guard & System Blacklist Tests", () =>
            {
                FolderLockTests.RunAll();
            });

            // 4. XAML Resource Graph Integrity Tests
            TestRunner.ExecuteGroup("XAML Resource Graph & StaticResource Integrity Tests", () =>
            {
                XamlIntegrityTests.RunAll();
            });

            // 5. WPF STA Application & View Initialization Tests
            TestRunner.ExecuteGroup("WPF STA Application & View Context Tests", () =>
            {
                WpfStartupTests.RunAll();
            });

            totalSw.Stop();

            Console.WriteLine();
            Console.WriteLine("==========================================================");
            if (TestRunner.FailCount == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  ALL TESTS PASSED: {TestRunner.PassCount} passed, 0 failed ({totalSw.ElapsedMilliseconds} ms)");
                Console.ResetColor();
                Console.WriteLine("==========================================================");
                return 0;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  TEST SUITE FAILED: {TestRunner.PassCount} passed, {TestRunner.FailCount} failed ({totalSw.ElapsedMilliseconds} ms)");
                Console.ResetColor();
                Console.WriteLine("==========================================================");
                return 1;
            }
        }
    }
}
