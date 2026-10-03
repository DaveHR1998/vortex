using System;
using System.Diagnostics;

namespace Vortex.Tests
{
    public static class TestRunner
    {
        public static int PassCount { get; private set; }
        public static int FailCount { get; private set; }

        public static void Pass(string testName)
        {
            PassCount++;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("  [PASS] ");
            Console.ResetColor();
            Console.WriteLine(testName);
        }

        public static void Fail(string testName, Exception ex)
        {
            FailCount++;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("  [FAIL] ");
            Console.ResetColor();
            Console.WriteLine(testName);
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"         Error: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"         Inner: {ex.InnerException.Message}");
            }
            Console.ResetColor();
        }

        public static void ExecuteGroup(string groupName, Action testAction)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n--- {groupName} ---");
            Console.ResetColor();

            var sw = Stopwatch.StartNew();
            try
            {
                testAction();
            }
            catch (Exception ex)
            {
                Fail(groupName, ex);
            }
            finally
            {
                sw.Stop();
            }
        }
    }
}
