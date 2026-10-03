using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Vortex.Core.Privacy
{
    /// <summary>
    /// Read-only scan for locally stored identity data: Windows Credentials,
    /// Wi-Fi profiles, browser autofill databases, SSH keys, Git config,
    /// environment variable tokens, and registry identity entries.
    /// Never deletes anything — opens the right native tool via ActionTarget.
    /// </summary>
    public class IdentityScanner
    {
        public List<IdentityFinding> ScanAll()
        {
            var findings = new List<IdentityFinding>();
            findings.AddRange(ScanCredentialManager());
            findings.AddRange(ScanWifiProfiles());
            findings.AddRange(ScanBrowserAutofill());
            findings.AddRange(ScanSshKeys());
            findings.AddRange(ScanGitConfig());
            findings.AddRange(ScanEnvironmentVariables());
            findings.AddRange(ScanRegistryIdentity());
            return findings;
        }

        // ─── Credential Manager ───────────────────────────────────────────────

        private static List<IdentityFinding> ScanCredentialManager()
        {
            var list = new List<IdentityFinding>();
            try
            {
                // Use cmdkey /list to enumerate credentials (no P/Invoke complexity)
                var output = RunCommand("cmdkey", "/list");
                var lines  = output.Split('\n');
                string? current = null;
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("Target:"))
                    {
                        current = trimmed.Replace("Target:", "").Trim();
                    }
                    else if (trimmed.StartsWith("User:") && current != null)
                    {
                        var user = trimmed.Replace("User:", "").Trim();
                        list.Add(new IdentityFinding
                        {
                            Category     = "Saved Credential",
                            Name         = current,
                            Detail       = $"Username: {user}",
                            Location     = "Windows Credential Manager",
                            Risk         = PrivacyRiskLevel.Critical,
                            ActionLabel  = "Open Credential Manager",
                            ActionTarget = "control.exe keymgr.dll",
                        });
                        current = null;
                    }
                }
            }
            catch { }
            return list;
        }

        // ─── Wi-Fi Profiles ───────────────────────────────────────────────────

        private static List<IdentityFinding> ScanWifiProfiles()
        {
            var list = new List<IdentityFinding>();
            try
            {
                var profilesOutput = RunCommand("netsh", "wlan show profiles");
                foreach (var line in profilesOutput.Split('\n'))
                {
                    if (!line.Contains(":")) continue;
                    var idx = line.IndexOf(':', StringComparison.Ordinal);
                    if (idx < 0) continue;
                    var ssid = line[(idx + 1)..].Trim();
                    if (string.IsNullOrWhiteSpace(ssid)) continue;

                    // Check if key content is accessible (exposed)
                    var detail    = RunCommand("netsh", $"wlan show profile name=\"{ssid}\" key=clear");
                    bool hasKey   = detail.Contains("Key Content");
                    bool isOpen   = detail.Contains("Authentication      : Open");

                    list.Add(new IdentityFinding
                    {
                        Category     = "Wi-Fi Profile",
                        Name         = ssid,
                        Detail       = hasKey ? "⚠️ Password stored in plaintext" : isOpen ? "Open network (no password)" : "Password stored (encrypted)",
                        Location     = "Windows WLAN Profiles",
                        Risk         = hasKey ? PrivacyRiskLevel.High : PrivacyRiskLevel.Low,
                        ActionLabel  = "Manage Wi-Fi",
                        ActionTarget = "ms-settings:network-wifi",
                    });
                }
            }
            catch { }
            return list;
        }

        // ─── Browser Autofill Databases ───────────────────────────────────────

        private static List<IdentityFinding> ScanBrowserAutofill()
        {
            var list = new List<IdentityFinding>();
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appData  = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            var targets = new (string Browser, string Path, string Type)[]
            {
                ("Google Chrome",          Path.Combine(localApp, @"Google\Chrome\User Data\Default\Login Data"),         "Saved Logins"),
                ("Google Chrome",          Path.Combine(localApp, @"Google\Chrome\User Data\Default\Web Data"),           "Autofill Data"),
                ("Microsoft Edge",         Path.Combine(localApp, @"Microsoft\Edge\User Data\Default\Login Data"),        "Saved Logins"),
                ("Microsoft Edge",         Path.Combine(localApp, @"Microsoft\Edge\User Data\Default\Web Data"),          "Autofill Data"),
                ("Mozilla Firefox",        Path.Combine(appData,  @"Mozilla\Firefox\Profiles"),                           "Firefox Profiles"),
                ("Brave",                  Path.Combine(localApp, @"BraveSoftware\Brave-Browser\User Data\Default\Login Data"), "Saved Logins"),
            };

            foreach (var (browser, path, type) in targets)
            {
                bool exists = File.Exists(path) || Directory.Exists(path);
                if (!exists) continue;
                list.Add(new IdentityFinding
                {
                    Category     = "Browser Data",
                    Name         = $"{browser} — {type}",
                    Detail       = "Login credentials / autofill data stored locally in SQLite database",
                    Location     = path,
                    Risk         = type == "Saved Logins" ? PrivacyRiskLevel.Critical : PrivacyRiskLevel.High,
                    ActionLabel  = "Open Folder",
                    ActionTarget = Path.GetDirectoryName(path) ?? path,
                });
            }
            return list;
        }

        // ─── SSH Keys ─────────────────────────────────────────────────────────

        private static List<IdentityFinding> ScanSshKeys()
        {
            var list  = new List<IdentityFinding>();
            var home  = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var sshDir = Path.Combine(home, ".ssh");
            if (!Directory.Exists(sshDir)) return list;

            var privatePatterns = new[] { "id_rsa", "id_ed25519", "id_ecdsa", "id_dsa" };
            foreach (var file in Directory.GetFiles(sshDir))
            {
                var fname = Path.GetFileName(file);
                bool isPrivate = privatePatterns.Any(p => fname.Equals(p, StringComparison.OrdinalIgnoreCase));
                if (!isPrivate && !fname.StartsWith("id_")) continue;

                // Check if the key has a passphrase (heuristic: look for ENCRYPTED in first 3 lines)
                bool encrypted = false;
                try
                {
                    var firstLines = File.ReadLines(file).Take(3);
                    encrypted = firstLines.Any(l => l.Contains("ENCRYPTED"));
                }
                catch { }

                list.Add(new IdentityFinding
                {
                    Category     = "SSH Private Key",
                    Name         = fname,
                    Detail       = encrypted ? "Passphrase protected" : "⚠️ No passphrase — unprotected private key",
                    Location     = file,
                    Risk         = encrypted ? PrivacyRiskLevel.Medium : PrivacyRiskLevel.Critical,
                    ActionLabel  = "Open .ssh Folder",
                    ActionTarget = sshDir,
                });
            }
            return list;
        }

        // ─── Git Config ───────────────────────────────────────────────────────

        private static List<IdentityFinding> ScanGitConfig()
        {
            var list   = new List<IdentityFinding>();
            var home   = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var config = Path.Combine(home, ".gitconfig");
            if (!File.Exists(config)) return list;

            string? email = null, name = null, credHelper = null;
            try
            {
                foreach (var line in File.ReadAllLines(config))
                {
                    var t = line.Trim();
                    if (t.StartsWith("email"))        email      = t.Split('=').LastOrDefault()?.Trim();
                    if (t.StartsWith("name"))         name       = t.Split('=').LastOrDefault()?.Trim();
                    if (t.StartsWith("helper"))       credHelper = t.Split('=').LastOrDefault()?.Trim();
                }
            }
            catch { }

            if (!string.IsNullOrEmpty(email) || !string.IsNullOrEmpty(name))
                list.Add(new IdentityFinding
                {
                    Category     = "Git Identity",
                    Name         = ".gitconfig",
                    Detail       = $"Name: {name ?? "?"} | Email: {email ?? "?"}",
                    Location     = config,
                    Risk         = PrivacyRiskLevel.Medium,
                    ActionLabel  = "Open .gitconfig",
                    ActionTarget = config,
                });

            if (!string.IsNullOrEmpty(credHelper))
                list.Add(new IdentityFinding
                {
                    Category     = "Git Credential Store",
                    Name         = "Credential Helper",
                    Detail       = $"Helper: {credHelper} — Git may store tokens/passwords",
                    Location     = config,
                    Risk         = PrivacyRiskLevel.High,
                    ActionLabel  = "Open .gitconfig",
                    ActionTarget = config,
                });

            return list;
        }

        // ─── Environment Variables ────────────────────────────────────────────

        private static readonly string[] _tokenKeywords = { "KEY", "TOKEN", "SECRET", "PASSWORD", "PASS", "CREDENTIAL", "AUTH", "API" };

        private static List<IdentityFinding> ScanEnvironmentVariables()
        {
            var list = new List<IdentityFinding>();
            try
            {
                var envVars = System.Environment.GetEnvironmentVariables(EnvironmentVariableTarget.User);
                foreach (System.Collections.DictionaryEntry entry in envVars)
                {
                    var key = entry.Key?.ToString() ?? string.Empty;
                    var val = entry.Value?.ToString() ?? string.Empty;
                    if (_tokenKeywords.Any(k => key.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    {
                        list.Add(new IdentityFinding
                        {
                            Category     = "Environment Variable",
                            Name         = key,
                            Detail       = $"Value length: {val.Length} chars — likely contains sensitive credential",
                            Location     = "User Environment Variables",
                            Risk         = PrivacyRiskLevel.Critical,
                            ActionLabel  = "Open Env Variables",
                            ActionTarget = "SystemPropertiesAdvanced.exe",
                        });
                    }
                }
            }
            catch { }
            return list;
        }

        // ─── Registry Identity ────────────────────────────────────────────────

        private static List<IdentityFinding> ScanRegistryIdentity()
        {
            var list = new List<IdentityFinding>();
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                if (key != null)
                {
                    var owner = key.GetValue("RegisteredOwner")?.ToString();
                    var org   = key.GetValue("RegisteredOrganization")?.ToString();
                    if (!string.IsNullOrWhiteSpace(owner))
                        list.Add(new IdentityFinding
                        {
                            Category     = "Registry Identity",
                            Name         = "Registered Owner",
                            Detail       = owner,
                            Location     = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
                            Risk         = PrivacyRiskLevel.Low,
                            ActionLabel  = "Open in RegEdit",
                            ActionTarget = "regedit.exe",
                        });
                    if (!string.IsNullOrWhiteSpace(org))
                        list.Add(new IdentityFinding
                        {
                            Category     = "Registry Identity",
                            Name         = "Registered Organization",
                            Detail       = org,
                            Location     = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
                            Risk         = PrivacyRiskLevel.Low,
                            ActionLabel  = "Open in RegEdit",
                            ActionTarget = "regedit.exe",
                        });
                }
            }
            catch { }
            return list;
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        private static string RunCommand(string exe, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    RedirectStandardOutput = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    StandardOutputEncoding = Encoding.UTF8,
                };
                using var p = Process.Start(psi);
                if (p == null) return string.Empty;
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(8000);
                return output;
            }
            catch { return string.Empty; }
        }
    }
}
