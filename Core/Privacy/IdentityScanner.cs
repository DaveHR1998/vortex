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
    /// Active Identity Shield: Scans locally stored identity data (Windows Credentials,
    /// Wi-Fi profiles, Cloud & Dev secrets, SSH keys, Environment variables, LSA Protection)
    /// and provides direct 1-click in-app remediation (Revoke, Forget, Shred, Purge, Enable LSA).
    /// </summary>
    public class IdentityScanner
    {
        public List<IdentityFinding> ScanAll()
        {
            var findings = new List<IdentityFinding>();
            findings.AddRange(ScanLsaProtection());
            findings.AddRange(ScanCloudAndDevSecrets());
            findings.AddRange(ScanCredentialManager());
            findings.AddRange(ScanWifiProfiles());
            findings.AddRange(ScanSshKeys());
            findings.AddRange(ScanEnvironmentVariables());
            findings.AddRange(ScanBrowserAutofill());
            findings.AddRange(ScanGitConfig());
            findings.AddRange(ScanRegistryIdentity());
            return findings;
        }

        // ─── Windows LSA Protection & Credential Guard ─────────────────────────

        private static List<IdentityFinding> ScanLsaProtection()
        {
            var list = new List<IdentityFinding>();
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Lsa");
                var val = key?.GetValue("RunAsPPL");
                bool isProtected = val != null && Convert.ToInt32(val) >= 1;

                if (!isProtected)
                {
                    list.Add(new IdentityFinding
                    {
                        Category               = "OS Memory Guard",
                        Name                   = "Windows LSA Protection",
                        Detail                 = "⚠️ Disabled — Memory scrapers (e.g. Mimikatz) can dump NTLM & Kerberos hashes from LSASS",
                        Location               = @"HKLM\SYSTEM\CurrentControlSet\Control\Lsa\RunAsPPL",
                        Risk                   = PrivacyRiskLevel.Critical,
                        ActionType             = "EnableLsaGuard",
                        ActionLabel            = "🛡️ Enable LSA Guard",
                        ActionData             = "1",
                        SecondaryActionLabel   = "",
                    });
                }
                else
                {
                    list.Add(new IdentityFinding
                    {
                        Category               = "OS Memory Guard",
                        Name                   = "Windows LSA Protection",
                        Detail                 = "🟢 Active — LSASS runs as Protected Process Light (PPL), blocking unauthorized memory dumping",
                        Location               = @"HKLM\SYSTEM\CurrentControlSet\Control\Lsa\RunAsPPL",
                        Risk                   = PrivacyRiskLevel.Low,
                        ActionType             = "OpenTarget",
                        ActionLabel            = "Protected",
                        ActionData             = "",
                    });
                }
            }
            catch { }
            return list;
        }

        // ─── Cloud & Developer Plaintext Secrets ───────────────────────────────

        private static List<IdentityFinding> ScanCloudAndDevSecrets()
        {
            var list = new List<IdentityFinding>();
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // 1. AWS Credentials
            var awsCreds = Path.Combine(home, ".aws", "credentials");
            if (File.Exists(awsCreds))
            {
                list.Add(new IdentityFinding
                {
                    Category               = "Cloud Secrets",
                    Name                   = "AWS CLI Credentials",
                    Detail                 = "⚠️ Plaintext IAM Access Key ID & Secret Access Key stored on disk",
                    Location               = awsCreds,
                    Risk                   = PrivacyRiskLevel.Critical,
                    ActionType             = "ShredFile",
                    ActionLabel            = "🗑️ Shred File",
                    ActionData             = awsCreds,
                    SecondaryActionLabel   = "📂 Open Folder",
                    SecondaryActionTarget  = Path.GetDirectoryName(awsCreds) ?? home,
                });
            }

            // 2. Docker Auth
            var dockerConfig = Path.Combine(home, ".docker", "config.json");
            if (File.Exists(dockerConfig))
            {
                list.Add(new IdentityFinding
                {
                    Category               = "Developer Secrets",
                    Name                   = "Docker Registry Auth",
                    Detail                 = "Plaintext base64 tokens for Docker Hub & container registries",
                    Location               = dockerConfig,
                    Risk                   = PrivacyRiskLevel.High,
                    ActionType             = "ShredFile",
                    ActionLabel            = "🗑️ Shred File",
                    ActionData             = dockerConfig,
                    SecondaryActionLabel   = "📂 Open Folder",
                    SecondaryActionTarget  = Path.GetDirectoryName(dockerConfig) ?? home,
                });
            }

            // 3. NPM Auth Token
            var npmrc = Path.Combine(home, ".npmrc");
            if (File.Exists(npmrc))
            {
                try
                {
                    var text = File.ReadAllText(npmrc);
                    if (text.Contains("_authToken") || text.Contains("//registry.npmjs.org/"))
                    {
                        list.Add(new IdentityFinding
                        {
                            Category               = "Package Tokens",
                            Name                   = "NPM Auth Token (.npmrc)",
                            Detail                 = "⚠️ Plaintext publish/install auth token granting full NPM account access",
                            Location               = npmrc,
                            Risk                   = PrivacyRiskLevel.Critical,
                            ActionType             = "ShredFile",
                            ActionLabel            = "🗑️ Shred File",
                            ActionData             = npmrc,
                            SecondaryActionLabel   = "📂 Open Folder",
                            SecondaryActionTarget  = home,
                        });
                    }
                }
                catch { }
            }

            // 4. Kubernetes Config
            var kubeConfig = Path.Combine(home, ".kube", "config");
            if (File.Exists(kubeConfig))
            {
                list.Add(new IdentityFinding
                {
                    Category               = "Developer Secrets",
                    Name                   = "Kubernetes Cluster Config",
                    Detail                 = "Plaintext client certificates & Bearer tokens for cloud clusters",
                    Location               = kubeConfig,
                    Risk                   = PrivacyRiskLevel.High,
                    ActionType             = "ShredFile",
                    ActionLabel            = "🗑️ Shred File",
                    ActionData             = kubeConfig,
                    SecondaryActionLabel   = "📂 Open Folder",
                    SecondaryActionTarget  = Path.GetDirectoryName(kubeConfig) ?? home,
                });
            }

            // 5. Azure CLI Access Tokens
            var azureTokens = Path.Combine(home, ".azure", "accessTokens.json");
            if (File.Exists(azureTokens))
            {
                list.Add(new IdentityFinding
                {
                    Category               = "Cloud Secrets",
                    Name                   = "Azure CLI Access Tokens",
                    Detail                 = "Cached plaintext OAuth tokens granting active Azure subscription access",
                    Location               = azureTokens,
                    Risk                   = PrivacyRiskLevel.Critical,
                    ActionType             = "ShredFile",
                    ActionLabel            = "🗑️ Shred File",
                    ActionData             = azureTokens,
                    SecondaryActionLabel   = "📂 Open Folder",
                    SecondaryActionTarget  = Path.GetDirectoryName(azureTokens) ?? home,
                });
            }

            return list;
        }

        // ─── Credential Manager ───────────────────────────────────────────────

        private static List<IdentityFinding> ScanCredentialManager()
        {
            var list = new List<IdentityFinding>();
            try
            {
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
                            Category               = "Saved Credential",
                            Name                   = current,
                            Detail                 = $"Username: {user}",
                            Location               = "Windows Credential Manager",
                            Risk                   = PrivacyRiskLevel.High,
                            ActionType             = "RevokeCredential",
                            ActionLabel            = "🗑️ Revoke",
                            ActionData             = current,
                            SecondaryActionLabel   = "Control Panel",
                            SecondaryActionTarget  = "control.exe keymgr.dll",
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
                var profilesOutput = RunCommand("netsh", "wlan", "show", "profiles");
                foreach (var line in profilesOutput.Split('\n'))
                {
                    if (!line.Contains(":")) continue;
                    var idx = line.IndexOf(':', StringComparison.Ordinal);
                    if (idx < 0) continue;
                    var ssid = line[(idx + 1)..].Trim();
                    if (string.IsNullOrWhiteSpace(ssid)) continue;

                    var detail  = RunCommand("netsh", "wlan", "show", "profile", $"name={ssid}", "key=clear");
                    bool hasKey = detail.Contains("Key Content");
                    bool isOpen = detail.Contains("Authentication      : Open");

                    list.Add(new IdentityFinding
                    {
                        Category               = "Wi-Fi Profile",
                        Name                   = ssid,
                        Detail                 = hasKey ? "⚠️ Password stored in plaintext" : isOpen ? "Open network (no password)" : "Password stored (encrypted)",
                        Location               = "Windows WLAN Profiles",
                        Risk                   = hasKey ? PrivacyRiskLevel.High : PrivacyRiskLevel.Low,
                        ActionType             = "ForgetWifi",
                        ActionLabel            = "🗑️ Forget",
                        ActionData             = ssid,
                        SecondaryActionLabel   = "Settings",
                        SecondaryActionTarget  = "ms-settings:network-wifi",
                    });
                }
            }
            catch { }
            return list;
        }

        // ─── SSH Keys ─────────────────────────────────────────────────────────

        private static List<IdentityFinding> ScanSshKeys()
        {
            var list   = new List<IdentityFinding>();
            var home   = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var sshDir = Path.Combine(home, ".ssh");
            if (!Directory.Exists(sshDir)) return list;

            var privatePatterns = new[] { "id_rsa", "id_ed25519", "id_ecdsa", "id_dsa" };
            foreach (var file in Directory.GetFiles(sshDir))
            {
                var fname = Path.GetFileName(file);
                bool isPrivate = privatePatterns.Any(p => fname.Equals(p, StringComparison.OrdinalIgnoreCase));
                if (!isPrivate && !fname.StartsWith("id_")) continue;

                bool encrypted = false;
                try
                {
                    var firstLines = File.ReadLines(file).Take(3);
                    encrypted = firstLines.Any(l => l.Contains("ENCRYPTED"));
                }
                catch { }

                list.Add(new IdentityFinding
                {
                    Category               = "SSH Private Key",
                    Name                   = fname,
                    Detail                 = encrypted ? "🟢 Passphrase protected" : "⚠️ No passphrase — unprotected private key vulnerable to extraction",
                    Location               = file,
                    Risk                   = encrypted ? PrivacyRiskLevel.Low : PrivacyRiskLevel.Critical,
                    ActionType             = encrypted ? "OpenTarget" : "ShredFile",
                    ActionLabel            = encrypted ? "View" : "🗑️ Shred Key",
                    ActionData             = file,
                    ActionTarget           = sshDir,
                    SecondaryActionLabel   = "📂 Open .ssh",
                    SecondaryActionTarget  = sshDir,
                });
            }
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
                            Category               = "Environment Variable",
                            Name                   = key,
                            Detail                 = $"Length: {val.Length} chars — plaintext credential exposed to all user processes",
                            Location               = "User Environment Variables",
                            Risk                   = PrivacyRiskLevel.Critical,
                            ActionType             = "PurgeEnvVar",
                            ActionLabel            = "🗑️ Purge",
                            ActionData             = key,
                            SecondaryActionLabel   = "System Properties",
                            SecondaryActionTarget  = "SystemPropertiesAdvanced.exe",
                        });
                    }
                }
            }
            catch { }
            return list;
        }

        // ─── Browser Autofill Databases ───────────────────────────────────────

        private static List<IdentityFinding> ScanBrowserAutofill()
        {
            var list = new List<IdentityFinding>();
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
                    Category               = "Browser Credential Store",
                    Name                   = $"{browser} — {type}",
                    Detail                 = "Local database file containing saved credentials and autofill data",
                    Location               = path,
                    Risk                   = type == "Saved Logins" ? PrivacyRiskLevel.High : PrivacyRiskLevel.Medium,
                    ActionType             = "OpenTarget",
                    ActionLabel            = "📂 Open Folder",
                    ActionTarget           = Path.GetDirectoryName(path) ?? path,
                    SecondaryActionLabel   = "",
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
                    if (t.StartsWith("email"))  email      = t.Split('=').LastOrDefault()?.Trim();
                    if (t.StartsWith("name"))   name       = t.Split('=').LastOrDefault()?.Trim();
                    if (t.StartsWith("helper")) credHelper = t.Split('=').LastOrDefault()?.Trim();
                }
            }
            catch { }

            if (!string.IsNullOrEmpty(email) || !string.IsNullOrEmpty(name))
            {
                list.Add(new IdentityFinding
                {
                    Category               = "Git Identity",
                    Name                   = ".gitconfig",
                    Detail                 = $"Name: {name ?? "?"} | Email: {email ?? "?"}",
                    Location               = config,
                    Risk                   = PrivacyRiskLevel.Low,
                    ActionType             = "OpenTarget",
                    ActionLabel            = "View",
                    ActionTarget           = config,
                });
            }

            if (!string.IsNullOrEmpty(credHelper))
            {
                list.Add(new IdentityFinding
                {
                    Category               = "Git Credential Store",
                    Name                   = "Git Credential Helper",
                    Detail                 = $"Helper: '{credHelper}' — Git caches plaintext/DPAPI authentication tokens",
                    Location               = config,
                    Risk                   = PrivacyRiskLevel.High,
                    ActionType             = "UnsetGitHelper",
                    ActionLabel            = "🗑️ Clear Helper",
                    ActionData             = "",
                    SecondaryActionLabel   = "Open .gitconfig",
                    SecondaryActionTarget  = config,
                });
            }

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
                            ActionType   = "OpenTarget",
                            ActionLabel  = "View",
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
                            ActionType   = "OpenTarget",
                            ActionLabel  = "View",
                            ActionTarget = "regedit.exe",
                        });
                }
            }
            catch { }
            return list;
        }

        // ─── In-App Remediation Dispatcher ────────────────────────────────────

        public (bool success, string message) Remediate(string actionType, string actionData)
        {
            try
            {
                switch (actionType)
                {
                    case "RevokeCredential":
                        return RevokeCredential(actionData);
                    case "ForgetWifi":
                        return ForgetWifi(actionData);
                    case "PurgeEnvVar":
                        return PurgeEnvVar(actionData);
                    case "ShredFile":
                        return ShredFile(actionData);
                    case "EnableLsaGuard":
                        return EnableLsaProtection();
                    case "UnsetGitHelper":
                        return UnsetGitHelper();
                    default:
                        return (false, $"Unsupported remediation action: {actionType}");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Remediation error: {ex.Message}");
            }
        }

        public static (bool success, string message) RevokeCredential(string target)
        {
            if (string.IsNullOrWhiteSpace(target)) return (false, "Target cannot be empty.");
            var output = RunCommand("cmdkey", $"/delete:{target}");
            return (true, $"Revoked credential '{target}' from Windows Credential Manager.");
        }

        public static (bool success, string message) ForgetWifi(string ssid)
        {
            if (string.IsNullOrWhiteSpace(ssid)) return (false, "SSID cannot be empty.");
            var output = RunCommand("netsh", "wlan", "delete", "profile", $"name={ssid}");
            return (true, $"Forgot Wi-Fi network profile '{ssid}'.");
        }

        public static (bool success, string message) PurgeEnvVar(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return (false, "Environment variable key cannot be empty.");
            Environment.SetEnvironmentVariable(key, null, EnvironmentVariableTarget.User);
            Environment.SetEnvironmentVariable(key, null, EnvironmentVariableTarget.Process);
            return (true, $"Purged exposed environment variable '{key}'.");
        }

        public static (bool success, string message) ShredFile(string path)
        {
            if (!File.Exists(path)) return (false, $"File does not exist: {path}");
            try
            {
                long len = new FileInfo(path).Length;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write))
                {
                    byte[] zeroes = new byte[Math.Min(len, 65536)];
                    long written = 0;
                    while (written < len)
                    {
                        int toWrite = (int)Math.Min(zeroes.Length, len - written);
                        fs.Write(zeroes, 0, toWrite);
                        written += toWrite;
                    }
                    fs.Flush();
                }
                File.Delete(path);
                return (true, $"Securely shredded sensitive file: {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to shred file: {ex.Message}");
            }
        }

        public static (bool success, string message) EnableLsaProtection()
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Lsa");
                if (key != null)
                {
                    key.SetValue("RunAsPPL", 1, RegistryValueKind.DWord);
                    return (true, "Enabled Windows LSA Protection (RunAsPPL = 1). LSASS process memory is now shielded against dumping tools (active on next reboot).");
                }
                return (false, "Failed to open LSA registry key.");
            }
            catch (Exception ex)
            {
                return (false, $"Registry error: {ex.Message}");
            }
        }

        public static (bool success, string message) UnsetGitHelper()
        {
            RunCommand("git", "config", "--global", "--unset", "credential.helper");
            return (true, "Cleared global Git credential.helper configuration.");
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        private static string RunCommand(string exe, params string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo(exe)
                {
                    RedirectStandardOutput = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    StandardOutputEncoding = Encoding.UTF8,
                };
                foreach (var arg in args)
                {
                    psi.ArgumentList.Add(arg);
                }
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
