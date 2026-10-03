# Vortex System Guardian — Technical Architecture & Engineering Manual

> **Software Specification & Architectural Reference Document**  
> **Target Platform:** Windows 10 / Windows 11 (x64)  
> **Framework:** .NET 10.0 (WPF, C# 13) with Win32 / NT Kernel Native Interop  
> **Build Target:** Standalone Win-x64 Release Executable & Native Embedded Installer  

---

## 1. Executive Summary & Architectural Overview

**Vortex System Guardian** is a high-performance Windows systems optimization, security diagnostics, and hardware telematics workstation designed for Windows 10 and 11. Unlike conventional consumer utilities that introduce heavy background services, telemetry bloat, and fragile electron runtimes, Vortex is engineered as a lightweight, zero-overhead native application with direct Windows API bindings.

### Core Architectural Pillars
```
+---------------------------------------------------------------------------------------+
|                                    Vortex Desktop UI                                   |
|               (WPF, XAML, Deep Space Cyberpunk Theme, Hardware Acceleration)           |
+---------------------------------------------------------------------------------------+
                                           |
                                           v
+---------------------------------------------------------------------------------------+
|                              Core Application Manager                                 |
|          Single-Instance Mutex | Tray Notification Service | UAC Elevation             |
+---------------------------------------------------------------------------------------+
     |                  |                   |                   |                  |
     v                  v                   v                   v                  v
+-----------+    +---------------+    +------------+    +---------------+    +--------------+
| Hardware  |    | Software      |    | Privacy &  |    | Storage &     |    | Process &    |
| Telemetry |    | Updater       |    | Identity   |    | Registry      |    | Memory       |
| Engine    |    | Engine        |    | Shield     |    | Guard         |    | Optimizer    |
+-----------+    +---------------+    +------------+    +---------------+    +--------------+
     |                  |                   |                   |                  |
     v                  v                   v                   v                  v
LibreHardware     WinGet CLI +         DPAPI /          Folder Provenance    NT WorkingSet
Monitor WMI       WinINet Live Poller  Credential Vault & Reg Hive Parser    & Win32 IP Helper
```

---

## 2. Requirements & Toolchains

### 2.1 Developer & Build Tools
* **.NET 10.0 SDK** (`net10.0-windows`): Modern C# 13 runtime compiler supporting implicit usings, nullable reference types, and high-performance I/O streams.
* **Microsoft C# Compiler (Roslyn / .NET 4.8 `csc.exe`)**: Used by `build_setup.ps1` to compile the native standalone bootstrapping installer (`VortexSetup.exe`) and uninstaller (`Uninstall.exe`) without requiring the host system to have .NET 10 pre-installed.
* **PowerShell 7.x (pwsh) / Windows PowerShell 5.1**: Automation engine for the build pipeline.
* **Windows Package Manager (`winget.exe` v1.29+)**: Backend engine for package discovery, dependency resolution, and silent software updates.

### 2.2 System & Runtime Requirements
* **Operating System**: Windows 10 64-bit (Build 19041+) or Windows 11 64-bit (Build 22000+).
* **Architecture**: `win-x64` (Native AMD64/Intel 64).
* **Privileges**: Administrator privileges (enforced via `app.manifest` with `requireAdministrator`).
* **Display**: Minimum 1280x720 (Optimized for 1080p, 1440p, and 4K with per-monitor DPI awareness).

### 2.3 NuGet Package Dependencies
| Package | Version | Purpose |
| :--- | :--- | :--- |
| **`LibreHardwareMonitorLib`** | `0.9.4` | Direct ring-0/ring-3 hardware sensor extraction (CPU, GPU, Motherboard, RAM, Storage thermals, voltages, and load clocks). |
| **`Microsoft.Data.Sqlite`** | `9.0.2` | Embedded high-speed SQLite database engine for audit history and process knowledge base. |
| **`System.Management`** | `9.0.2` | WMI (Windows Management Instrumentation) queries for hardware specs, BIOS, and motherboard details. |

---

## 3. Detailed Subsystem Architecture

### 3.1 Single-Instance Mutex & Process Lifecycle Guardian
* **Source:** `App.xaml.cs`
* **Mutex Name:** `Global\Vortex_SingleInstance_Mutex_Guardian`
* **Mechanism:**
  * Uses a global named `Mutex` with cross-session visibility.
  * When a second instance of Vortex is launched, it checks the mutex. If the mutex is already owned, the secondary instance invokes the Win32 API (`FindWindow`, `SetForegroundWindow`, `ShowWindowAsync(SW_RESTORE)`) to bring the running primary window to the foreground, and immediately terminates itself with code 0.
  * Ensures that hibernating, sleeping, or background tray modes never cause duplicate or competing processes.

### 3.2 System Tray Service & Power/Hibernate State Manager
* **Source:** `Core/System/TrayIconService.cs` & `Core/System/PowerAndIdleManager.cs`
* **Features:**
  * Uses Windows Forms `NotifyIcon` bridged into WPF without external tray libraries.
  * **System Events Monitoring:** Subscribes to `SystemEvents.PowerModeChanged`. When the system resumes from Sleep or Hibernate (`PowerModes.Resume`), it gracefully restores the tray icon, recreates the taskbar handle if `TaskbarCreated` is broadcast by Explorer, and re-validates sensor polling loops.
  * **Minimize-to-Tray:** Clicking the window minimize or close button sends Vortex to the system notification area with a smooth tooltip confirmation. Double-clicking the tray icon restores the window instantly.

### 3.3 Hardware Telemetry & Overheat Alert Subsystem
* **Source:** `Core/System/HardwareMonitor.cs`
* **Sensor Capabilities:**
  * Initialized via `Computer` with CPU, GPU, RAM, Motherboard, and Storage sensors enabled.
  * Reads real-time core temperatures, core clocks, package wattage, and GPU VRAM usage.
  * **Built-in Overheat Alert:** Monitors CPU and GPU temperatures every 1000ms. If temperatures exceed user-configurable safety thresholds (default: 85°C for CPU, 82°C for GPU), Vortex fires an audio warning and pushes a system notification to prevent thermal throttling or hardware damage.

### 3.4 Hardware Specs & Multi-Format Reporting Engine
* **Source:** `Core/SystemInfo/SystemInfoModels.cs` & `MainWindow.xaml.cs`
* **Data Points Extracted:**
  * **CPU:** Model name, core/thread count, base clock, current clock, socket type, L2/L3 cache sizes.
  * **GPU:** Name, Driver version, Dedicated Video Memory (VRAM), resolution, refresh rate.
  * **Motherboard:** Manufacturer, product model, BIOS version, serial number.
  * **RAM:** Total capacity, available capacity, speed (MHz), form factor, slot count.
  * **Disks:** Drive models, partitions, bus types (NVMe/SATA), total and free storage space.
* **Export Formats:**
  * **Plain Text (`.txt`)**: Clean ASCII tabular report suitable for support tickets and system logs.
  * **HTML Report (`.html`)**: Polished, responsive HTML5 document styled with Vortex dark-mode cyberpunk CSS, complete with status badges and sensor cards.

### 3.5 Software Updater Subsystem (WinGet Engine)
* **Source:** `Core/System/WingetService.cs`
* **Downloader Acceleration:**
  * Dynamically ensures `%LOCALAPPDATA%\Packages\Microsoft.DesktopAppInstaller_8wekyb3d8bbwe\LocalState\settings.json` is configured with `"network": { "downloader": "wininet" }`. This bypasses Windows Delivery Optimization (`DoSvc`) stalls.
* **Non-Blocking Pipe Communication:**
  * Launches `winget upgrade` with `--source winget` and `--disable-interactivity --force`.
  * Redirects both `StandardOutput` and `StandardError`, reading both concurrently via separate asynchronous tasks to prevent 4KB pipe buffer deadlocks.
* **Real-Time Download Tracking (`MonitorDownloadProgressAsync`):**
  * When WinGet initiates a download, Vortex captures the installer URL via regex and runs a background HTTP `HEAD` request to determine `Content-Length`.
  * A background monitor polls the package-specific download directory in `%LOCALAPPDATA%\Temp\WinGet` every 300ms using `FileShare.ReadWrite | FileShare.Delete` file stream handles.
  * Computes downloaded MB, percentage, and live transfer speed (e.g. `45.2 MB / 168.5 MB (27%) • 5.2 MB/s`) and updates the progress bar and terminal log in real time.
* **Dual-Tier Cache Cleanup:**
  * **Automatic:** Pre-cleans previous stale file locks before each package update, and automatically purges the downloaded installer binary from disk as soon as installation completes.
  * **Manual:** Features a dedicated **"🧹 Clear Cache"** toolbar button that wipes all temporary installer residues across `%TEMP%\WinGet` and reports exact megabytes reclaimed.

### 3.6 Privacy Guard & Trace-Free Zero-Remanence Eraser
* **Source:** `Core/Privacy/PrivacyEraser.cs` & `Core/Privacy/PrivacyModels.cs`
* **Coverage:**
  * **Browser History & Caches:** Google Chrome, Microsoft Edge, Mozilla Firefox, Brave, Opera (Web Cache, Cookies, Session Storage, IndexedDB, Crashpad).
  * **Windows OS Telemetry & Traces:** Recent Docs MRU, Run History (`RunMRU`), UserAssist ROT13 telemetry keys, Thumbcache files, JumpLists (`AutomaticDestinations`), Prefetch records (`C:\Windows\Prefetch`), Explorer search history.
  * **Office & Media Traces:** VLC Player recent items, Windows Media Player history, Microsoft Office Recent Files list.
* **Deletion Security:** Overwrites file metadata before deletion to thwart forensic file recovery.

### 3.7 Digital Identity Shield & Local Credential Scanner
* **Source:** `Core/Privacy/IdentityScanner.cs`
* **Exposed Data Auditing:**
  * **Windows Credential Manager:** Scans generic, domain, and web credentials via native Win32 `CredEnumerate` APIs.
  * **SSH Private Keys & Configs:** Inspects `%USERPROFILE%\.ssh` for unencrypted private key files (`id_rsa`, `id_ed25519`, `known_hosts`).
  * **Wi-Fi Profiles & Passwords:** Uses native `netsh wlan show profiles` commands to discover saved networks and security keys.
  * **Cloud & Developer Secrets:** Scans for `.aws/credentials`, `.azure/`, `.docker/config.json`, and `.git-credentials`.
* **Action Routing:** Each finding includes a direct native launch action (e.g., launching `control.exe keymgr.dll` for Credentials, or opening Network Settings for Wi-Fi).

### 3.8 Storage & Registry Cleaners
* **Source:** `Core/Storage/SystemCleaner.cs`, `Core/Storage/RegistryGuard.cs`, `Core/Storage/FolderProvenanceEngine.cs`
* **Junk Cleaner:** Cleans Windows Temp, User Temp, Windows Update Cache (`SoftwareDistribution\Download`), crash dumps (`*.dmp`), log files, and delivery optimization caches.
* **Registry Guard:** Scans `HKCU` and `HKLM` software trees for orphaned uninstall strings, missing shared DLLs, obsolete COM/ActiveX CLSIDs, and broken file associations. Always prompts/creates a System Restore Point before modification.
* **Folder Provenance:** Classifies unorganized desktop/downloads folders by age, size, and origin, enabling safe quarantine or permanent deletion.

### 3.9 Process Manager, Network Monitor & Intrusion Threat Shield
* **Source:** `Core/Process/ProcessManager.cs`, `Core/Process/MemoryOptimizer.cs`, `Core/Process/NetworkMonitor.cs`
* **TurboBoost:** Calls Win32 API `EmptyWorkingSet` across non-essential processes, releasing megabytes of dormant committed physical memory back to the Windows memory pool without killing active applications.
* **Process Manager:** Real-time PID list with memory usage, thread count, CPU utilization, and integrated knowledge-base risk ratings.
* **Network & Intrusion Shield Subsystem:**
  * **Dual Protocol Kernel Probing**: Calls both `GetExtendedTcpTable` (`TCP_TABLE_OWNER_PID_ALL`) and `GetExtendedUdpTable` (`UDP_TABLE_OWNER_PID`) from `iphlpapi.dll` to capture every TCP and UDP socket.
  * **Process Identity & Attribution**: Maps raw PIDs to real executables, window titles, full paths on disk, and application category icon glyphs (🌐 Browsers, 💬 Chat, 🎮 Gaming, ⚙️ Services).
  * **Live Bandwidth Speedometer**: Polls network adapter statistics (`GetIPv4Statistics`) every 1000ms to calculate live download and upload transfer rates (`MB/s` / `KB/s`) and session transfer totals.
  * **VPN Tunnel & Interface Detection**: Detects virtual VPN network adapters (WireGuard, OpenVPN, Wintun, TAP, Nord, Tailscale, Cloudflare WARP) and flags whether sockets route through encrypted tunnels (`10.x`, `100.x`) or local Wi-Fi/Ethernet adapters.
  * **Reverse DNS & Protocol Intelligence**: Performs asynchronous non-blocking reverse DNS lookups, resolving remote IPs to domains (e.g., `google.com`, `discord.com`), while tagging standard ports (HTTPS, DNS, SSH, RDP, SMB, Steam, WireGuard).
  * **Active Intrusion Threat Heuristics**: Automatically flags and pushes desktop toast notifications (`TrayIconService.ShowBalloon`) with audio alarms when detecting:
    1. *Process Masquerading*: Executables named `svchost.exe`, `lsass.exe`, `csrss.exe` running outside `System32`.
    2. *Rogue Inbound Servers*: Apps in `AppData`, `Temp`, or `Downloads` opening listening server ports.
    3. *Untrusted Outbound Connections*: Executables in temporary folders contacting foreign external IPs.
    4. *Exposed Dangerous Ports*: Inbound external connections established to SMB (445) or RDP (3389).
    5. *Connection Flood / Port Scan*: Single PIDs generating spikes of >40 concurrent outbound connections.
  * **Contextual Management & Controls**: 2-second auto-refresh engine with pause toggle, search filtering, and right-click actions (`Terminate Process`, `Open File Location`, `Copy Remote Endpoint`, `Inspect in Process Manager`).

### 3.10 SQLite Knowledge Base & Audit Logging
* **Source:** `Core/Database/DatabaseService.cs` & `Core/Database/Models.cs`
* **Location:** `%LOCALAPPDATA%\Vortex\vortex_data.db`
* **Tables:**
  * `ProcessKnowledgeBase`: Curated ratings for over 50 common Windows background processes.
  * `AuditLog`: Immutable chronological log of every optimization, deletion, software update, and boost action performed.

---

## 4. UI/UX Design System: Deep Space Cyberpunk

* **Resource Dictionary:** `Styles/DeepSpaceTheme.xaml`
* **Color Palette:**
  * `SurfaceBackground`: `#0E0E14` (Deep void obsidian)
  * `SurfaceDark`: `#14141E` (Panel substrate)
  * `SurfaceRaised`: `#1A1A28` (Card & header highlight)
  * `AccentPrimary`: `#7C3AED` (Electrifying neon purple)
  * `AccentPrimaryHover`: `#9055FF`
  * `AccentSuccess`: `#00E599` (Vibrant emerald terminal green)
  * `AccentDanger`: `#EF4444` (Crimson hazard alert)
  * `TextPrimary`: `#F3F4F6` (High-contrast pure white)
  * `TextSecondary`: `#9CA3AF` (Refined slate gray)
* **Custom Controls:**
  * `VortexPrimaryButton`, `VortexSecondaryButton`, `VortexDangerButton`: Rounded corner buttons with smooth hover animations.
  * Custom styled `TabControl` with vertical navigation sidebar and SVG iconography.
  * Dark-themed `ListView` with customized `GridViewColumnHeader`, auto-scrollbars, and rounded selection borders.

---

## 5. Build, Packaging & Distribution Pipeline

The deployment architecture is completely automated via [`build_setup.ps1`](file:///c:/Users/daveH/Desktop/xfix/build_setup.ps1).

```
+-------------------------------------------------------------------------------+
|                             build_setup.ps1 Pipeline                          |
+-------------------------------------------------------------------------------+
  [Step 1] Clean staging/app and staging artifacts
  [Step 2] dotnet publish Vortex.csproj -c Release -r win-x64 (Win-x64 Payload)
  [Step 3] csc.exe compiles native Win32 Uninstall.exe into payload
  [Step 4] System.IO.Compression packs payload into package.zip
  [Step 5] csc.exe compiles single-file self-extracting VortexSetup.exe
           (embeds package.zip, vortex.png, and vortex.ico as binary resources)
  [Step 6] Finalize, unblock mark-of-the-web, copy to Release_Installer/
+-------------------------------------------------------------------------------+
```

### Key Installer Characteristics
1. **Self-Extracting Native Binary**: `VortexSetup.exe` is compiled with .NET Framework 4.8 `csc.exe`, guaranteeing it executes on any Windows 10/11 machine without prompting to install prerequisites.
2. **Embedded Payload**: Contains the complete compiled `Vortex.exe` and native libraries inside an embedded resource `package.zip`.
3. **Automated Setup Steps**:
   * Installs to `%LOCALAPPDATA%\Programs\Vortex` (or `%PROGRAMFILES%\Vortex`).
   * Creates Start Menu shortcuts and Desktop icon.
   * Registers native uninstallation in Windows Registry (`Software\Microsoft\Windows\CurrentVersion\Uninstall\Vortex`).
   * Bundles a dedicated `Uninstall.exe` that cleans files, shortcuts, and registry keys upon removal.

---

## 6. Security, Safety & Elevation Protocol

* **UAC Elevation Manifest (`app.manifest`)**:
  ```xml
  <requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
  ```
  Guarantees that low-level hardware sensor drivers (LibreHardwareMonitor), WMI telemetry, system service management, and registry cleaning operations succeed with complete system authority.
* **Failsafe System Restore Point Creation**:
  * Before performing registry cleans or deep driver alterations, Vortex automatically interfaces with the Windows System Restore API (`srclient.dll` / WMI `SystemRestore`) to create a recovery checkpoint.
* **Process Protection Whitelist**:
  * Critical Windows subsystems (`csrss.exe`, `lsass.exe`, `smss.exe`, `explorer.exe`, `services.exe`) are hardcoded as protected and cannot be terminated by the Process Manager or TurboBoost routines.

---

## 7. Verification & Testing History

* **Software Updater Testing**: Verified update discovery, downloading, and installation across packages including Git (`Git.Git`), EnterpriseDB PostgreSQL (`PostgreSQL.PostgreSQL.18`), Box (`Box.Box`), and Google Chrome (`Google.Chrome.EXE`).
* **Sensor Benchmarking**: Confirmed active sensor reading on multi-core Intel/AMD CPUs, NVIDIA/AMD/Intel GPUs, and NVMe drives.
* **Sleep/Hibernate Cycle**: Verified that system resume events correctly re-initialize the tray icon and maintain single-instance process constraints without leaking handles.

---
*Document Version: 1.2.0 | Vortex Engineering Team | October 2026*
