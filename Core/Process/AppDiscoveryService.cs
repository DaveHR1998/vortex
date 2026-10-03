using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using Vortex.Core.Database;

namespace Vortex.Core.ProcessManagement
{
    public class AppDiscoveryService
    {
        private static readonly HashSet<string> IgnoredSystemProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "system", "system idle process", "smss", "csrss", "wininit", "services",
            "lsass", "winlogon", "fontdrvhost", "dwm", "svchost", "registry",
            "memory compression", "sihost", "ctfmon", "taskhostw", "conhost", "runtimebroker",
            "shellexperiencehost", "startmenuexperiencehost", "searchhost", "searchindexer",
            "vortex", "vortexsetup", "uninstall"
        };

        public static List<DiscoveredAppModel> DiscoverAllApplications(DatabaseService? db, ProcessBlockService? blockService)
        {
            var appsDict = new Dictionary<string, DiscoveredAppModel>(StringComparer.OrdinalIgnoreCase);

            // 1. Scan currently active running processes
            try
            {
                var procs = Process.GetProcesses();
                foreach (var p in procs)
                {
                    try
                    {
                        string name = p.ProcessName;
                        if (IgnoredSystemProcesses.Contains(name)) continue;

                        string cleanExe = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
                        string path = string.Empty;
                        try { path = p.MainModule?.FileName ?? string.Empty; } catch { }
                        string title = string.Empty;
                        try { title = p.MainWindowTitle; } catch { }

                        string disp = !string.IsNullOrEmpty(title) && title.Length < 40 
                            ? $"{name} - {title}" 
                            : FormatDisplayName(name);

                        var app = new DiscoveredAppModel
                        {
                            ProcessName = cleanExe,
                            DisplayName = disp,
                            ExecutablePath = path,
                            IconGlyph = DetermineGlyph(name, path),
                            Category = DetermineCategory(name, path),
                            IsRunning = true
                        };

                        appsDict[cleanExe] = app;
                    }
                    catch { }
                    finally
                    {
                        p.Dispose();
                    }
                }
            }
            catch { }

            // 2. Discover Known User App Locations (Antigravity, LocalAppData, AppData)
            try
            {
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

                // Check Google Antigravity in ~/.gemini
                string geminiDir = Path.Combine(userProfile, ".gemini");
                if (Directory.Exists(geminiDir))
                {
                    ScanDirectoryForExecutables(geminiDir, "Google Antigravity IDE", "AI & Dev Tool", "🤖", appsDict, maxDepth: 4);
                }

                // Check LocalAppData Programs
                string progDir = Path.Combine(localAppData, "Programs");
                if (Directory.Exists(progDir))
                {
                    ScanDirectoryForExecutables(progDir, "", "Developer & User Tool", "⚡", appsDict, maxDepth: 3);
                }

                // Check LocalAppData Discord / Spotify
                string discordDir = Path.Combine(localAppData, "Discord");
                if (Directory.Exists(discordDir))
                {
                    ScanDirectoryForExecutables(discordDir, "Discord", "Communication", "💬", appsDict, maxDepth: 3);
                }
                string spotifyDir = Path.Combine(appData, "Spotify");
                if (Directory.Exists(spotifyDir))
                {
                    ScanDirectoryForExecutables(spotifyDir, "Spotify", "Media & Audio", "🎵", appsDict, maxDepth: 2);
                }
            }
            catch { }

            // 3. Scan Installed Programs from Windows Registry
            ScanRegistryUninstall(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", appsDict);
            ScanRegistryUninstall(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", appsDict);
            ScanRegistryUninstall(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", appsDict);

            // 4. Merge with user-configured access rules and blocked rules
            var userRules = db?.GetAllAppAccessRules() ?? new List<AppAccessRule>();
            var blockedItems = blockService?.GetBlockedItems() ?? new List<ProcessBlockService.BlockedItemModel>();

            // Apply database rules
            foreach (var rule in userRules)
            {
                string cleanExe = rule.ProcessName.ToLowerInvariant();
                if (appsDict.TryGetValue(cleanExe, out var existing))
                {
                    existing.AccessStatus = rule.AccessStatus;
                    existing.MuteNotifications = rule.MuteNotifications;
                    if (!string.IsNullOrEmpty(rule.DisplayName)) existing.DisplayName = rule.DisplayName;
                    if (!string.IsNullOrEmpty(rule.ExecutablePath) && string.IsNullOrEmpty(existing.ExecutablePath))
                        existing.ExecutablePath = rule.ExecutablePath;
                }
                else
                {
                    appsDict[cleanExe] = new DiscoveredAppModel
                    {
                        ProcessName = cleanExe,
                        DisplayName = string.IsNullOrEmpty(rule.DisplayName) ? Path.GetFileNameWithoutExtension(cleanExe) : rule.DisplayName,
                        ExecutablePath = rule.ExecutablePath,
                        IconGlyph = DetermineGlyph(cleanExe, rule.ExecutablePath),
                        Category = rule.Category,
                        IsRunning = false,
                        AccessStatus = rule.AccessStatus,
                        MuteNotifications = rule.MuteNotifications
                    };
                }
            }

            // Apply blocked items
            foreach (var block in blockedItems)
            {
                string cleanExe = block.ProcessName.ToLowerInvariant();
                if (!cleanExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) cleanExe += ".exe";

                if (appsDict.TryGetValue(cleanExe, out var existing))
                {
                    existing.AccessStatus = block.BlockType == "FIREWALL" ? "BLOCKED_FIREWALL" : "BLOCKED_EXECUTION";
                    if (!string.IsNullOrEmpty(block.ExecutablePath) && string.IsNullOrEmpty(existing.ExecutablePath))
                        existing.ExecutablePath = block.ExecutablePath;
                }
                else
                {
                    appsDict[cleanExe] = new DiscoveredAppModel
                    {
                        ProcessName = cleanExe,
                        DisplayName = Path.GetFileNameWithoutExtension(cleanExe),
                        ExecutablePath = block.ExecutablePath,
                        IconGlyph = "🚫",
                        Category = "Blocked Binary",
                        IsRunning = false,
                        AccessStatus = block.BlockType == "FIREWALL" ? "BLOCKED_FIREWALL" : "BLOCKED_EXECUTION",
                        MuteNotifications = true
                    };
                }
            }

            // Filter out system components and return sorted list: Configured / Running first, then Alphabetical
            return appsDict.Values
                .Where(a => !IgnoredSystemProcesses.Contains(Path.GetFileNameWithoutExtension(a.ProcessName)))
                .OrderByDescending(a => a.IsAllowed || a.IsBlocked)
                .ThenByDescending(a => a.IsRunning)
                .ThenBy(a => a.DisplayName)
                .ToList();
        }

        private static void ScanRegistryUninstall(RegistryKey root, string keyPath, Dictionary<string, DiscoveredAppModel> appsDict)
        {
            try
            {
                using var uninstKey = root.OpenSubKey(keyPath);
                if (uninstKey == null) return;

                foreach (var subKeyName in uninstKey.GetSubKeyNames())
                {
                    try
                    {
                        using var subKey = uninstKey.OpenSubKey(subKeyName);
                        if (subKey == null) continue;

                        string? dispName = subKey.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(dispName) || dispName.Length < 2) continue;

                        // Check SystemComponent
                        var sysComp = subKey.GetValue("SystemComponent");
                        if (sysComp is int sc && sc == 1) continue;

                        // Check ParentKeyName (e.g. updates)
                        if (subKey.GetValue("ParentKeyName") != null) continue;

                        string? installLoc = subKey.GetValue("InstallLocation") as string;
                        string? dispIcon = subKey.GetValue("DisplayIcon") as string;

                        string exePath = string.Empty;
                        string exeName = string.Empty;

                        if (!string.IsNullOrEmpty(dispIcon))
                        {
                            string rawPath = dispIcon.Split(',')[0].Trim('"', ' ');
                            if (rawPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(rawPath))
                            {
                                exePath = rawPath;
                                exeName = Path.GetFileName(rawPath);
                            }
                        }

                        if (string.IsNullOrEmpty(exePath) && !string.IsNullOrEmpty(installLoc) && Directory.Exists(installLoc))
                        {
                            var files = Directory.GetFiles(installLoc, "*.exe", SearchOption.TopDirectoryOnly);
                            if (files.Length > 0)
                            {
                                // Prefer executable matching dispName or first non-uninstaller
                                var candidate = files.FirstOrDefault(f => !Path.GetFileName(f).ToLowerInvariant().Contains("unins")) ?? files[0];
                                exePath = candidate;
                                exeName = Path.GetFileName(candidate);
                            }
                        }

                        if (!string.IsNullOrEmpty(exeName))
                        {
                            string cleanExe = exeName.ToLowerInvariant();
                            if (!appsDict.ContainsKey(cleanExe))
                            {
                                appsDict[cleanExe] = new DiscoveredAppModel
                                {
                                    ProcessName = cleanExe,
                                    DisplayName = dispName,
                                    ExecutablePath = exePath,
                                    IconGlyph = DetermineGlyph(cleanExe, exePath),
                                    Category = DetermineCategory(dispName, exePath),
                                    IsRunning = false
                                };
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static void ScanDirectoryForExecutables(string dir, string defaultName, string category, string glyph, Dictionary<string, DiscoveredAppModel> appsDict, int maxDepth = 2)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                var exeFiles = Directory.GetFiles(dir, "*.exe", SearchOption.AllDirectories);
                foreach (var file in exeFiles)
                {
                    try
                    {
                        string fileName = Path.GetFileName(file);
                        string lower = fileName.ToLowerInvariant();
                        if (lower.Contains("unins") || lower.Contains("crashpad") || lower.Contains("update") || lower.Contains("elevate"))
                            continue;

                        if (!appsDict.ContainsKey(lower))
                        {
                            string disp = !string.IsNullOrEmpty(defaultName) ? defaultName : FormatDisplayName(Path.GetFileNameWithoutExtension(fileName));
                            appsDict[lower] = new DiscoveredAppModel
                            {
                                ProcessName = lower,
                                DisplayName = disp,
                                ExecutablePath = file,
                                IconGlyph = glyph,
                                Category = category,
                                IsRunning = false
                            };
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static string FormatDisplayName(string rawName)
        {
            if (string.IsNullOrEmpty(rawName)) return "Application";
            string name = Path.GetFileNameWithoutExtension(rawName);
            if (name.Equals("antigravity", StringComparison.OrdinalIgnoreCase) || name.Equals("antigravity-ide", StringComparison.OrdinalIgnoreCase))
                return "Google Antigravity IDE";
            if (name.Equals("code", StringComparison.OrdinalIgnoreCase)) return "Visual Studio Code";
            if (name.Equals("cursor", StringComparison.OrdinalIgnoreCase)) return "Cursor AI Code Editor";
            if (name.Equals("chrome", StringComparison.OrdinalIgnoreCase)) return "Google Chrome";
            if (name.Equals("msedge", StringComparison.OrdinalIgnoreCase)) return "Microsoft Edge";
            if (name.Equals("firefox", StringComparison.OrdinalIgnoreCase)) return "Mozilla Firefox";
            if (name.Equals("node", StringComparison.OrdinalIgnoreCase)) return "Node.js JavaScript Runtime";
            if (name.Equals("python", StringComparison.OrdinalIgnoreCase)) return "Python Runtime";
            if (name.Equals("git", StringComparison.OrdinalIgnoreCase)) return "Git Version Control";
            if (name.Equals("pwsh", StringComparison.OrdinalIgnoreCase)) return "PowerShell 7 (Core)";
            if (name.Equals("powershell", StringComparison.OrdinalIgnoreCase)) return "Windows PowerShell";
            if (name.Equals("windowsterminal", StringComparison.OrdinalIgnoreCase)) return "Windows Terminal";
            if (name.Equals("discord", StringComparison.OrdinalIgnoreCase)) return "Discord";
            if (name.Equals("spotify", StringComparison.OrdinalIgnoreCase)) return "Spotify";
            if (name.Equals("steam", StringComparison.OrdinalIgnoreCase)) return "Steam Gaming Client";

            return char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        private static string DetermineGlyph(string name, string path)
        {
            string n = name.ToLowerInvariant();
            string p = path.ToLowerInvariant();

            if (n.Contains("antigravity") || p.Contains("antigravity")) return "🤖";
            if (n.Contains("code") || n.Contains("cursor") || n.Contains("devenv") || n.Contains("node") || n.Contains("python") || n.Contains("git")) return "⚡";
            if (n.Contains("chrome") || n.Contains("edge") || n.Contains("firefox") || n.Contains("brave") || n.Contains("opera")) return "🌐";
            if (n.Contains("discord") || n.Contains("slack") || n.Contains("teams") || n.Contains("telegram")) return "💬";
            if (n.Contains("spotify") || n.Contains("vlc") || n.Contains("media")) return "🎵";
            if (n.Contains("steam") || n.Contains("game") || n.Contains("epic") || n.Contains("riot")) return "🎮";
            if (n.Contains("wireguard") || n.Contains("vpn") || n.Contains("tailscale")) return "🛡️";

            return "💻";
        }

        private static string DetermineCategory(string name, string path)
        {
            string n = name.ToLowerInvariant();
            string p = path.ToLowerInvariant();

            if (n.Contains("antigravity") || p.Contains("antigravity")) return "AI & Agent Platform";
            if (n.Contains("code") || n.Contains("cursor") || n.Contains("node") || n.Contains("python") || n.Contains("git") || n.Contains("terminal")) return "Developer Tool";
            if (n.Contains("chrome") || n.Contains("edge") || n.Contains("firefox") || n.Contains("brave")) return "Web Browser";
            if (n.Contains("discord") || n.Contains("slack") || n.Contains("teams")) return "Communication";
            if (n.Contains("spotify") || n.Contains("vlc")) return "Media & Entertainment";
            if (n.Contains("steam") || n.Contains("game")) return "Gaming & Launcher";

            return "Installed Application";
        }
    }
}
