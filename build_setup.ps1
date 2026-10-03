param(
    [switch]$KeepStaging = $false
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "       Vortex System Guardian Installer Builder           " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 0. Kill any running app instances
Stop-Process -Name Vortex, VortexSetup -Force -ErrorAction SilentlyContinue

$rootDir = "c:\Users\daveH\Desktop\xfix"
$installerDir = Join-Path $rootDir "Installer"
$stagingDir = Join-Path $rootDir "staging"
$appStaging = Join-Path $stagingDir "app"
$packageZip = Join-Path $installerDir "Setup\package.zip"
$finalInstaller = Join-Path $rootDir "VortexSetup.exe"
$distInstaller = Join-Path $rootDir "Release_Installer\VortexSetup.exe"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$iconPath = Join-Path $rootDir "vortex.ico"
$pngPath = Join-Path $rootDir "vortex.png"

# 1. Clean Staging
Write-Host "`n[1/6] Cleaning staging directories..." -ForegroundColor Yellow
if (Test-Path $stagingDir) { Remove-Item $stagingDir -Recurse -Force }
if (Test-Path $packageZip) { Remove-Item $packageZip -Force }
New-Item -ItemType Directory -Path $appStaging -Force | Out-Null

# 2. Publish Vortex Application (win-x64 Release with all native dependencies)
Write-Host "[2/6] Publishing Vortex Application (win-x64 Release)..." -ForegroundColor Yellow
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1"
$env:DOTNET_CLI_HOME=Join-Path $rootDir ".cli"
$env:APPDATA=Join-Path $rootDir ".cli\AppData\Roaming"

& dotnet publish "$rootDir\Vortex.csproj" -c Release -r win-x64 --self-contained false -o "$appStaging" -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

# 3. Compile Standalone Native Uninstall.exe
Write-Host "[3/6] Compiling native standalone Uninstall.exe..." -ForegroundColor Yellow
$uninstallerCs = Join-Path $installerDir "Uninstaller\Uninstaller.cs"
$uninstallerManifest = Join-Path $installerDir "Uninstaller\app.manifest"
$uninstallOut = Join-Path $appStaging "Uninstall.exe"

$cscArgs = @(
    "/target:winexe",
    "/out:`"$uninstallOut`"",
    "/win32manifest:`"$uninstallerManifest`"",
    "/win32icon:`"$iconPath`"",
    "/r:System.Windows.Forms.dll",
    "/r:System.Drawing.dll",
    "/r:System.dll",
    "`"$uninstallerCs`""
)
& $csc $cscArgs
if ($LASTEXITCODE -ne 0) {
    throw "Compiling Uninstall.exe failed with exit code $LASTEXITCODE"
}

# Copy icon and logo files into app payload
Copy-Item $iconPath -Destination (Join-Path $appStaging "vortex.ico") -Force
Copy-Item $pngPath -Destination (Join-Path $appStaging "vortex.png") -Force

# 4. Compress Payload into package.zip
Write-Host "[4/6] Compressing payload archive into package.zip..." -ForegroundColor Yellow
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($appStaging, $packageZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
$zipSizeMb = [math]::Round((Get-Item $packageZip).Length / 1MB, 2)
Write-Host "       Archive created: $zipSizeMb MB" -ForegroundColor Green

# 5. Compile Final Single-File Installer VortexSetup.exe
Write-Host "[5/6] Compiling single-file native VortexSetup.exe with embedded payload..." -ForegroundColor Yellow
$setupCs = Join-Path $installerDir "Setup\Vortex_Setup.cs"
$setupManifest = Join-Path $installerDir "Setup\app.manifest"

$setupCscArgs = @(
    "/target:winexe",
    "/out:`"$finalInstaller`"",
    "/win32manifest:`"$setupManifest`"",
    "/win32icon:`"$iconPath`"",
    "/resource:`"$packageZip`",package.zip",
    "/resource:`"$pngPath`",vortex.png",
    "/r:System.Windows.Forms.dll",
    "/r:System.Drawing.dll",
    "/r:System.dll",
    "/r:System.IO.Compression.dll",
    "/r:System.IO.Compression.FileSystem.dll",
    "/r:`"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\Microsoft.CSharp.dll`"",
    "`"$setupCs`""
)
& $csc $setupCscArgs
if ($LASTEXITCODE -ne 0) {
    throw "Compiling VortexSetup.exe failed with exit code $LASTEXITCODE"
}

# 6. Copy to Release_Installer and Unblock
Write-Host "[6/6] Finalizing distribution and unblocking mark-of-the-web..." -ForegroundColor Yellow
New-Item -ItemType Directory -Path (Split-Path $distInstaller) -Force | Out-Null
Copy-Item $finalInstaller $distInstaller -Force
Unblock-File $finalInstaller -ErrorAction SilentlyContinue
Unblock-File $distInstaller -ErrorAction SilentlyContinue

# Clean temporary files unless KeepStaging is specified
if (-not $KeepStaging) {
    Remove-Item $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $packageZip -Force -ErrorAction SilentlyContinue
}

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host "   SUCCESS: Standalone Installer Generated Successfully! " -ForegroundColor Green
Write-Host "   Path: $finalInstaller" -ForegroundColor Green
Write-Host "   Distribution: $distInstaller" -ForegroundColor Green
Write-Host "   Size: $([math]::Round((Get-Item $finalInstaller).Length / 1MB, 2)) MB" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
