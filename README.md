# ⚡ Vortex System Guardian

<div align="center">

![Vortex Logo](vortex.png)

**Next-Generation Windows Optimization, Privacy Hardening, Software Lifecycle & Hardware Monitoring Station**

[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%20Windows%2011-0078D6?style=flat-square&logo=windows)](https://microsoft.com/windows)
[![Architecture](https://img.shields.io/badge/arch-x64-8A2BE2?style=flat-square)](https://dotnet.microsoft.com)
[![Framework](https://img.shields.io/badge/framework-.NET%2010.0%20%28WPF%29-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com)
[![C#](https://img.shields.io/badge/language-C%23%2013-239120?style=flat-square&logo=csharp)](https://docs.microsoft.com/dotnet/csharp/)
[![Database](https://img.shields.io/badge/storage-SQLite%203-003B57?style=flat-square&logo=sqlite)](https://sqlite.org)
[![License](https://img.shields.io/badge/license-MIT-blue?style=flat-square)](LICENSE)

[**Technical Documentation**](TECHNICAL_DOCUMENTATION.md) • [**Architecture Plan**](x_fix_architecture_and_implementation_plan.md) • [**Download Setup**](Release_Installer/VortexSetup.exe)

</div>

---

## 🌟 Overview

**Vortex System Guardian** is an enterprise-grade Windows optimization and security desktop suite engineered in modern **C# 13 and .NET 10 WPF**. It replaces bloated consumer cleaning tools with a native, zero-telemetry, cyberpunk-styled workstation that gives power users complete sovereignty over their system's hardware, performance, privacy, and installed applications.

---

## 🚀 Key Features & Capabilities

### 1. 🛡️ Privacy Guard & Zero-Trace Eraser
* **Deep Trace Cleaning**: Wipes browser caches, cookies, IndexedDB, and tracking artifacts across Google Chrome, Microsoft Edge, Mozilla Firefox, Brave, and Opera.
* **Windows Forensic Trace Elimination**: Clears Recent Docs MRU, Run History, UserAssist ROT13 telemetry keys, JumpLists, Thumbnail caches, Prefetch execution traces, and Explorer search logs.
* **Secure Metadata Scrubbing**: Prevents file carving and data reconstruction.

### 2. 🔒 Digital Identity Shield
* **Credential Vault Auditing**: Enumerates stored Windows Credentials (Web, Generic, Domain) using the native Win32 `CredEnumerate` API.
* **Developer & SSH Key Security**: Discovers exposed private keys (`id_rsa`, `id_ed25519`) in `.ssh` and cloud credentials (`.aws`, `.docker`, `.git-credentials`).
* **Wi-Fi Profile Exposure**: Audits saved wireless networks and passwords.
* **Direct Action Routing**: Direct 1-click links to launch native managers (Credential Manager, Network Settings, etc.).

### 3. 🚀 Real-Time Software Updater (WinGet Engine)
* **Selective, Per-App Upgrades**: Powered by Windows Package Manager (`winget.exe`).
* **WinINet Downloader Acceleration**: Configures winget to bypass slow Delivery Optimization (`DoSvc`) stalls.
* **Live Download Progress & Speed**: Live polling of active download chunks in `%TEMP%\WinGet` displays true transferred MB, percentage, and speed (e.g. `125.4 MB / 519.4 MB (24%) • 5.2 MB/s`).
* **Source Pinning**: Bypasses Microsoft Store connection timeouts by routing through `--source winget`.
* **Automatic & Manual Cache Cleanup**: Automatically deletes downloaded installer binaries when installations finish, with a manual **🧹 Clear Cache** toolbar button.

### 4. 🌡️ Hardware Monitor & Thermal Overheat Alerts
* **Real-time Ring-0 Sensor Telemetry**: Powered by `LibreHardwareMonitorLib` to measure CPU, GPU, Motherboard, RAM, and NVMe temperatures, voltages, package wattages, and clock speeds.
* **Active Overheat Safety System**: Real-time alerts sound and notify if CPU (>85°C) or GPU (>82°C) breach critical thermal thresholds.
* **Multi-Format Export**: Generates comprehensive hardware spec audit sheets in **Plain Text (`.txt`)** or dark-themed **HTML5 (`.html`)**.

### 5. ⚡ Memory TurboBoost, Network Monitor & Intrusion Shield
* **Working Set Trimming**: Uses Win32 `EmptyWorkingSet` memory optimization to free hundreds of megabytes of stagnant RAM across idle processes without killing applications.
* **Knowledge-Base Process Ratings**: Built-in SQLite database classifies over 50 background processes with safety ratings and recommendations.
* **Dual-Protocol Sockets Probing**: Captures all active TCP and UDP sockets mapped to process IDs, executable paths, and category icon glyphs.
* **Live Bandwidth Speedometer**: Real-time download/upload speed gauges (`MB/s` / `KB/s`) and total session byte counters.
* **VPN Tunnel & Interface Detection**: Identifies virtual VPN adapters (WireGuard, OpenVPN, Wintun, TAP, Nord, Tailscale) and flags encrypted tunnel endpoints.
* **Reverse DNS & Protocol Tagging**: Resolves remote IP addresses to readable domains and tags standard services (HTTPS, DNS, SSH, RDP).
* **Automated Intrusion Threat Alerts**: Detects process masquerading, rogue servers in Temp folders, exposed SMB/RDP ports, and connection spikes with desktop notifications and audio alarms.
* **Interactive Socket Controls**: Auto-refreshing 2-second monitor with search filtering and right-click process termination.

### 6. 🧹 System Cleaner & Registry Guard
* **Junk Elimination**: Safely purges temporary directories, Windows Update cache, crash dumps, and shader caches.
* **Registry Integrity Guard**: Cleans obsolete COM/ActiveX entries, missing shared DLL references, and broken file associations with automatic **System Restore Point** generation.
* **Folder Provenance Engine**: Identifies forgotten large folders and orphaned directory trees for quarantine or deletion.

### 7. 🌙 Deep Space Cyberpunk UI & Tray Guardian
* **Rich Cyberpunk Aesthetics**: Deep void background (`#0E0E14`), neon violet accents (`#7C3AED`), emerald status indicators (`#00E599`), and micro-animated buttons.
* **Single-Instance Mutex Guardian**: Uses `Global\Vortex_SingleInstance_Mutex_Guardian` to prevent duplicate processes during system sleep or hibernate.
* **System Tray Minimized Mode**: Seamlessly docks to the notification area with full sleep/wake recovery.

---

## 🛠️ Technology Stack & Architecture

```
                                  VORTEX ARCHITECTURE
                                  
   [ Presentation Layer ]        WPF XAML (Hardware Accelerated, Custom DeepSpace Theme)
                                                            │
   [ Core Orchestrator  ]        App.xaml.cs (Single-Instance Mutex, Lifecycle, TrayIcon)
                                                            │
             ┌──────────────────────┬───────────────────────┼──────────────────────┐
             │                      │                       │                      │
             ▼                      ▼                       ▼                      ▼
    [ Hardware Telemetry ]   [ Software Updater ]   [ Privacy & Identity ]   [ System Storage ]
    LibreHardwareMonitor     WinGet CLI Driver      DPAPI & Credential API   RegistryGuard
    WMI System.Management    WinINet File Poller    Zero-Trace Eraser        SystemCleaner
             │                      │                       │                      │
             └──────────────────────┼───────────────────────┴──────────────────────┘
                                    │
   [ Persistence Layer  ]        Microsoft.Data.Sqlite (AuditLog, ProcessKnowledgeBase)
                                    │
   [ Native OS Interop  ]        Win32 API (kernel32, user32, advapi32, iphlpapi, psapi)
```

### Core Technologies
* **Framework**: .NET 10.0 (`net10.0-windows`)
* **Language**: C# 13 (Pattern matching, asynchronous streams, file share interop)
* **Libraries**:
  * `LibreHardwareMonitorLib` v0.9.4 — Hardware sensor interface
  * `Microsoft.Data.Sqlite` v9.0.2 — Local audit & process database
  * `System.Management` v9.0.2 — WMI device enumeration
* **Installer Engine**: Roslyn / .NET 4.8 `csc.exe` Native Self-Extracting Single-File Executable

---

## 📦 Directory Structure

```
c:\Users\daveH\Desktop\xfix\
├── App.xaml / App.xaml.cs            # Entry point, Single-instance Mutex, UAC check
├── MainWindow.xaml / .cs             # 12-Tab Cyberpunk Master UI & Tab Controllers
├── Vortex.csproj                     # .NET 10 WPF project definition
├── build_setup.ps1                   # Automated build & packaging script
├── TECHNICAL_DOCUMENTATION.md        # Comprehensive technical & engineering manual
├── x_fix_architecture_and_implementation_plan.md # Architectural roadmap
├── vortex.ico / vortex.png           # Application branding & vector logos
│
├── Core/
│   ├── Database/                     # SQLite persistence & audit logging
│   │   ├── DatabaseService.cs
│   │   └── Models.cs
│   ├── Modes/                        # Deep Focus Mode daemon manager
│   │   └── FocusModeManager.cs
│   ├── Privacy/                      # Privacy Guard & Identity Shield
│   │   ├── IdentityScanner.cs
│   │   ├── PrivacyEraser.cs
│   │   └── PrivacyModels.cs
│   ├── Process/                      # Process Manager, RAM TurboBoost & Network
│   │   ├── MemoryOptimizer.cs
│   │   ├── NetworkMonitor.cs
│   │   └── ProcessManager.cs
│   ├── Storage/                      # Junk Cleaner, Registry Guard, Provenance
│   │   ├── FolderProvenanceEngine.cs
│   │   ├── RegistryGuard.cs
│   │   └── SystemCleaner.cs
│   ├── System/                       # Telemetry, Tray Icon & Updater
│   │   ├── HardwareMonitor.cs
│   │   ├── PowerAndIdleManager.cs
│   │   ├── ServiceControllerService.cs
│   │   ├── SystemRestoreService.cs
│   │   ├── TrayIconService.cs
│   │   └── WingetService.cs
│   └── SystemInfo/                   # Hardware specs & report generation
│       └── SystemInfoModels.cs
│
├── Styles/
│   └── DeepSpaceTheme.xaml           # XAML dark brushes, styles, and control templates
│
├── Installer/                        # Setup bootstrapping source code
│   ├── Setup/Vortex_Setup.cs         # Native self-extracting installer
│   └── Uninstaller/Uninstaller.cs    # Native standalone uninstaller
│
└── Release_Installer/
    └── VortexSetup.exe               # 4.2 MB standalone single-file distribution installer
```

---

## 🔨 Building from Source

### Prerequisites
1. Windows 10 or Windows 11 (64-bit).
2. [.NET 10.0 SDK](https://dotnet.microsoft.com/download) installed.
3. Administrator command prompt or PowerShell.

### 1. Development Build
Compile the application directly in Debug or Release mode:
```powershell
dotnet build Vortex.csproj -c Release
```

### 2. Generate Standalone Distribution Installer
Run the automated build script:
```powershell
powershell -ExecutionPolicy Bypass -File build_setup.ps1
```
This executes the 6-stage build pipeline:
1. Cleans previous staging directories.
2. Compiles and publishes the self-contained `Vortex` application payload (`win-x64`).
3. Compiles the native Win32 `Uninstall.exe` binary.
4. Compresses all assets and dependencies into `package.zip`.
5. Compiles `VortexSetup.exe` with `csc.exe`, embedding the compressed payload and graphics.
6. Emits the final single-file installer to `Release_Installer/VortexSetup.exe` (~4.2 MB).

---

## 💻 Installation & Usage

1. Run **`VortexSetup.exe`** with Administrator privileges.
2. The setup wizard automatically installs Vortex to `%LOCALAPPDATA%\Programs\Vortex` (or `%PROGRAMFILES%\Vortex`).
3. Launch Vortex from the Desktop or Start Menu shortcut.
4. **Navigation**: Use the left sidebar to switch between Performance, Storage, Registry, Privacy Guard, Identity Shield, Software Updater, and System Information.
5. **System Tray**: Minimize or close the window at any time to keep Vortex monitoring sensors and alerts in the background.

---

## 🔒 Security & Privacy Commitments
* **Zero Telemetry**: Vortex transmits **0 bytes** of telemetry or tracking data.
* **All Processing is Local**: Sensors, audit history, identity exposures, and credentials are processed strictly on your local PC.
* **Safe Cleaners**: System restore points are created before registry modifications. Protected OS processes can never be terminated.

---

## 📄 License
This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.
