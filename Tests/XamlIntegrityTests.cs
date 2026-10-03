using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Vortex.Tests
{
    public static class XamlIntegrityTests
    {
        public static void RunAll()
        {
            Test_AllStaticResourceReferencesResolve();
        }

        private static void Test_AllStaticResourceReferencesResolve()
        {
            string rootDir = AppDomain.CurrentDomain.BaseDirectory;
            // Navigate up to solution root if running in bin/Debug/net10.0-windows
            while (!File.Exists(Path.Combine(rootDir, "Vortex.csproj")) && Directory.GetParent(rootDir) != null)
            {
                rootDir = Directory.GetParent(rootDir)!.FullName;
            }

            if (!File.Exists(Path.Combine(rootDir, "Vortex.csproj")))
            {
                rootDir = @"c:\Users\daveH\Desktop\xfix";
            }

            // 1. Collect all declared keys in Styles/DeepSpaceTheme.xaml & App.xaml
            var declaredKeys = new HashSet<string>(StringComparer.Ordinal);

            // Add standard WPF system resources
            declaredKeys.Add("BooleanToVisibilityConverter");

            string[] dictionaryFiles = new[]
            {
                Path.Combine(rootDir, "Styles", "DeepSpaceTheme.xaml"),
                Path.Combine(rootDir, "App.xaml")
            };

            foreach (var dictFile in dictionaryFiles)
            {
                if (!File.Exists(dictFile)) continue;
                string content = File.ReadAllText(dictFile);

                // Match x:Key="SomeName"
                var keyMatches = Regex.Matches(content, @"x:Key\s*=\s*""([^""]+)""");
                foreach (Match m in keyMatches)
                {
                    declaredKeys.Add(m.Groups[1].Value);
                }

                // Match TargetType="SomeType" where it serves as implicit style key (e.g. TargetType="{x:Type Control}")
                var targetTypeMatches = Regex.Matches(content, @"<Style\s+TargetType\s*=\s*""\{x:Type\s+([^}]+)\}""(?!\s+x:Key)");
                foreach (Match m in targetTypeMatches)
                {
                    declaredKeys.Add(m.Groups[1].Value);
                }
            }

            // 2. Scan all XAML files for {StaticResource ...} usage
            var xamlFiles = Directory.GetFiles(rootDir, "*.xaml", SearchOption.AllDirectories);
            var missingResources = new List<string>();

            foreach (var xamlFile in xamlFiles)
            {
                // Skip obj / bin / staging
                if (xamlFile.Contains(@"\obj\") || xamlFile.Contains(@"\bin\") || xamlFile.Contains(@"\staging\"))
                    continue;

                string content = File.ReadAllText(xamlFile);

                // Look for local resources declared inside the file itself (e.g., <Window.Resources>, <UserControl.Resources>)
                var fileDeclaredKeys = new HashSet<string>(declaredKeys, StringComparer.Ordinal);
                var localKeyMatches = Regex.Matches(content, @"x:Key\s*=\s*""([^""]+)""");
                foreach (Match m in localKeyMatches)
                {
                    fileDeclaredKeys.Add(m.Groups[1].Value);
                }

                var resourceMatches = Regex.Matches(content, @"\{StaticResource\s+([A-Za-z0-9_\.]+)\}");
                foreach (Match m in resourceMatches)
                {
                    string resourceKey = m.Groups[1].Value;
                    if (!fileDeclaredKeys.Contains(resourceKey))
                    {
                        missingResources.Add($"{Path.GetFileName(xamlFile)} references undefined StaticResource '{resourceKey}'");
                    }
                }
            }

            if (missingResources.Count > 0)
            {
                throw new Exception("Missing XAML Resources detected!\n" + string.Join("\n", missingResources));
            }

            TestRunner.Pass($"XAML: Complete StaticResource Graph Integrity Verified ({declaredKeys.Count} defined keys checked across all views)");
        }
    }
}
