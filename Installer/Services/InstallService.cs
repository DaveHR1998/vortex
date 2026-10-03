using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace VortexInstaller.Services
{
    public class InstallProgressEventArgs : EventArgs
    {
        public int Percent { get; }
        public string StatusMessage { get; }

        public InstallProgressEventArgs(int percent, string statusMessage)
        {
            Percent = percent;
            StatusMessage = statusMessage;
        }
    }

    public class InstallOptions
    {
        public string TargetDirectory { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Vortex"
        );

        public bool CreateDesktopShortcut { get; set; } = true;
        public bool CreateStartMenuShortcut { get; set; } = true;
        public bool RunOnStartup { get; set; } = true;
        public bool LaunchAfterInstall { get; set; } = true;
    }

    public class InstallResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class InstallService
    {
        public event EventHandler<InstallProgressEventArgs>? ProgressChanged;

        public async Task<InstallResult> InstallAsync(InstallOptions options)
        {
            try
            {
                Report(5, "Preparing target installation directory...");
                await Task.Delay(200);

                // Ensure target directory has \Vortex suffix
                string targetDir = options.TargetDirectory.Trim();
                if (string.IsNullOrWhiteSpace(targetDir))
                {
                    targetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Vortex");
                }
                if (!targetDir.EndsWith("Vortex", StringComparison.OrdinalIgnoreCase))
                {
                    targetDir = Path.Combine(targetDir, "Vortex");
                }
                options.TargetDirectory = targetDir;

                // 1. Terminate any running instances so files are not locked
                try
                {
                    var existingProcesses = Process.GetProcessesByName("Vortex");
                    foreach (var proc in existingProcesses)
                    {
                        try
                        {
                            proc.Kill();
                            proc.WaitForExit(2000);
                        }
                        catch { }
                    }
                }
                catch { }

                // 2. Ensure target directory exists
                if (!Directory.Exists(options.TargetDirectory))
                {
                    Directory.CreateDirectory(options.TargetDirectory);
                }

                Report(15, "Unpacking core application payload...");
                await Task.Delay(200);

                // 3. Extract embedded app.zip
                var assembly = Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream("VortexInstaller.Payload.app.zip"))
                {
                    if (stream == null)
                    {
                        return new InstallResult { Success = false, ErrorMessage = "Embedded installation payload not found inside installer package." };
                    }

                    using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                    {
                        int totalEntries = archive.Entries.Count;
                        int extracted = 0;

                        foreach (var entry in archive.Entries)
                        {
                            if (string.IsNullOrEmpty(entry.Name)) continue;

                            string destinationPath = Path.Combine(options.TargetDirectory, entry.FullName);
                            string? parentDir = Path.GetDirectoryName(destinationPath);
                            if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                            {
                                Directory.CreateDirectory(parentDir);
                            }

                            // Delete destination file if exists to prevent lock errors
                            if (File.Exists(destinationPath))
                            {
                                try { File.Delete(destinationPath); } catch { }
                            }

                            entry.ExtractToFile(destinationPath, overwrite: true);

                            extracted++;
                            int p = 15 + (int)((extracted / (double)totalEntries) * 50);
                            Report(p, $"Deploying: {entry.Name}...");
                            await Task.Delay(30);
                        }
                    }
                }

                Report(68, "Configuring shell shortcuts and desktop integration...");
                await Task.Delay(200);

                string exePath = Path.Combine(options.TargetDirectory, "Vortex.exe");

                // 4. Desktop Shortcut
                if (options.CreateDesktopShortcut)
                {
                    try
                    {
                        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                        if (!string.IsNullOrEmpty(desktopPath) && Directory.Exists(desktopPath))
                        {
                            string shortcutPath = Path.Combine(desktopPath, "Vortex Guardian.lnk");
                            CreateShortcut(shortcutPath, exePath, options.TargetDirectory);
                        }

                        string commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
                        if (!string.IsNullOrEmpty(commonDesktop) && Directory.Exists(commonDesktop))
                        {
                            string commonShortcut = Path.Combine(commonDesktop, "Vortex Guardian.lnk");
                            CreateShortcut(commonShortcut, exePath, options.TargetDirectory);
                        }

                        // Also check for OneDrive Desktop
                        string oneDriveDesktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive", "Desktop");
                        if (Directory.Exists(oneDriveDesktop) && oneDriveDesktop != desktopPath)
                        {
                            string oneDriveShortcut = Path.Combine(oneDriveDesktop, "Vortex Guardian.lnk");
                            CreateShortcut(oneDriveShortcut, exePath, options.TargetDirectory);
                        }
                    }
                    catch { }
                }

                // 5. Start Menu Shortcut
                if (options.CreateStartMenuShortcut)
                {
                    try
                    {
                        string startMenuCommon = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Vortex");
                        if (!Directory.Exists(startMenuCommon)) Directory.CreateDirectory(startMenuCommon);
                        CreateShortcut(Path.Combine(startMenuCommon, "Vortex Guardian.lnk"), exePath, options.TargetDirectory);

                        string uninstallExe = Path.Combine(options.TargetDirectory, "Uninstall.exe");
                        if (File.Exists(uninstallExe))
                        {
                            CreateShortcut(Path.Combine(startMenuCommon, "Uninstall Vortex.lnk"), uninstallExe, options.TargetDirectory);
                        }
                    }
                    catch { }
                }

                Report(82, "Configuring Windows startup preferences...");
                await Task.Delay(150);

                if (options.RunOnStartup)
                {
                    try
                    {
                        SetStartupRunKey(exePath);
                    }
                    catch { }
                }

                Report(92, "Registering application in Windows Programs list...");
                await Task.Delay(200);

                try
                {
                    RegisterUninstaller(options.TargetDirectory, exePath);
                }
                catch { }

                Report(100, "Installation complete!");
                await Task.Delay(300);

                return new InstallResult { Success = true };
            }
            catch (Exception ex)
            {
                Report(0, $"Error: {ex.Message}");
                return new InstallResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        private void Report(int percent, string message)
        {
            ProgressChanged?.Invoke(this, new InstallProgressEventArgs(percent, message));
        }

        private static void CreateShortcut(string shortcutPath, string targetPath, string workingDir)
        {
            try
            {
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null)
                {
                    dynamic shell = Activator.CreateInstance(shellType)!;
                    dynamic shortcut = shell.CreateShortcut(shortcutPath);
                    shortcut.TargetPath = targetPath;
                    shortcut.WorkingDirectory = workingDir;
                    shortcut.Description = "Vortex - Windows System Guardian & RAM Optimizer";
                    shortcut.IconLocation = targetPath + ",0";
                    shortcut.Save();
                }
            }
            catch { }
        }

        private static void SetStartupRunKey(string exePath)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                key?.SetValue("Vortex", $"\"{exePath}\"");
            }
            catch { }
        }

        private static void RegisterUninstaller(string installDir, string exePath)
        {
            string uninstallExe = Path.Combine(installDir, "Uninstall.exe");

            // Register in HKLM (System-wide for all users)
            try
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var unKey = hklm.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Vortex");
                if (unKey != null)
                {
                    unKey.SetValue("DisplayName", "Vortex System Guardian");
                    unKey.SetValue("DisplayVersion", "1.0.0");
                    unKey.SetValue("Publisher", "Vortex Technologies");
                    unKey.SetValue("DisplayIcon", $"{exePath},0");
                    unKey.SetValue("InstallLocation", installDir);
                    unKey.SetValue("UninstallString", $"\"{uninstallExe}\"");
                    unKey.SetValue("QuietUninstallString", $"\"{uninstallExe}\" --quiet");
                    unKey.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    unKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    unKey.SetValue("EstimatedSize", 3500, RegistryValueKind.DWord);
                }
            }
            catch { }

            // Register in HKCU (Per-user fallback)
            try
            {
                using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
                using var unKey = hkcu.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Vortex");
                if (unKey != null)
                {
                    unKey.SetValue("DisplayName", "Vortex System Guardian");
                    unKey.SetValue("DisplayVersion", "1.0.0");
                    unKey.SetValue("Publisher", "Vortex Technologies");
                    unKey.SetValue("DisplayIcon", $"{exePath},0");
                    unKey.SetValue("InstallLocation", installDir);
                    unKey.SetValue("UninstallString", $"\"{uninstallExe}\"");
                    unKey.SetValue("QuietUninstallString", $"\"{uninstallExe}\" --quiet");
                    unKey.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    unKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    unKey.SetValue("EstimatedSize", 3500, RegistryValueKind.DWord);
                }
            }
            catch { }
        }
    }
}
