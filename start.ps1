# Local simulation launcher: builds with MockDetectionEngine and starts only the WPF app.
# Usage: .\start.ps1 [-Configuration Debug|Release]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectFile = Join-Path $ProjectRoot "TripleDetection.csproj"
$OutputDir = Join-Path $ProjectRoot "bin\$Configuration\net8.0-windows"
$AppDll = Join-Path $OutputDir "TripleDetection.App.dll"

# Avoid opening a second instance of this app.
$running = Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
    Where-Object { $_.CommandLine -like "*TripleDetection.App.dll*" }
if ($running) {
    Write-Host "[start] App already running (PID: $($running.ProcessId -join ', '))." -ForegroundColor Yellow
    exit 0
}

# Always rebuild with VM_SDK disabled so this local launcher reliably uses MockDetectionEngine,
# even if VisionMaster happens to be installed on this machine or a previous build used it.
Write-Host "[start] Building local simulation ($Configuration, MockDetectionEngine) ..." -ForegroundColor Cyan
dotnet build $ProjectFile -c $Configuration -t:Rebuild --nologo -v q -p:VmSdkAvailable=false
if ($LASTEXITCODE -ne 0) {
    Write-Host "[start] Build failed; app was not started." -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $AppDll)) {
    Write-Host "[start] App output not found: $AppDll" -ForegroundColor Red
    exit 1
}

# This intentionally starts only the desktop app. No Windows Service or watchdog is installed or started.
Write-Host "[start] Starting TripleDetection.App (simulation mode) ..." -ForegroundColor Cyan
$stderrLog = Join-Path $OutputDir "startup-stderr.log"
Start-Process -FilePath "dotnet" `
    -ArgumentList "`"$AppDll`"" `
    -WorkingDirectory $OutputDir `
    -RedirectStandardError $stderrLog
Start-Sleep -Seconds 3

$proc = Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
    Where-Object { $_.CommandLine -like "*TripleDetection.App.dll*" }
if ($proc) {
    Write-Host "[start] App started (PID: $($proc.ProcessId -join ', ')); MockDetectionEngine is active." -ForegroundColor Green
} else {
    Write-Host "[start] App did not remain running. Check: $stderrLog" -ForegroundColor Red
    exit 1
}
