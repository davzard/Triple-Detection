# ============================================================
# Triple-Detection 启动脚本
# 用法:
#   .\start.ps1              构建(如需要)并启动
#   .\start.ps1 -Rebuild     强制重新构建后启动
#   .\start.ps1 -Configuration Release
# 说明:
#   - 未安装 VisionMaster 4.2 SDK 时自动以 Mock 检测引擎运行
#   - 数据库/图像/日志均相对 bin\<配置>\net8.0-windows 目录
# ============================================================
param(
    [switch]$Rebuild,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectFile = Join-Path $ProjectRoot "TripleDetection.csproj"
$OutputDir   = Join-Path $ProjectRoot "bin\$Configuration\net8.0-windows"
$AppDll      = Join-Path $OutputDir "TripleDetection.App.dll"

# 1. 已在运行则提示
$running = Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
    Where-Object { $_.CommandLine -like "*TripleDetection.App.dll*" }
if ($running) {
    Write-Host "[start] 应用已在运行 (PID: $($running.ProcessId -join ', '))，如需重启请先运行 .\stop.ps1" -ForegroundColor Yellow
    exit 0
}

# 2. 构建（DLL 不存在或指定 -Rebuild 时）
if ($Rebuild -or -not (Test-Path $AppDll)) {
    Write-Host "[start] 正在构建 ($Configuration) ..." -ForegroundColor Cyan
    dotnet build $ProjectFile -c $Configuration --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[start] 构建失败，已终止" -ForegroundColor Red
        exit 1
    }
}

# 3. 启动（工作目录必须是输出目录，相对路径的 Data/Images/logs 都基于它）
Write-Host "[start] 启动 TripleDetection.App ..." -ForegroundColor Cyan
$stderrLog = Join-Path $OutputDir "startup-stderr.log"
Start-Process -FilePath "dotnet" -ArgumentList "`"$AppDll`"" -WorkingDirectory $OutputDir -RedirectStandardError $stderrLog
Start-Sleep -Seconds 3

$proc = Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
    Where-Object { $_.CommandLine -like "*TripleDetection.App.dll*" }
if ($proc) {
    Write-Host "[start] 启动成功 (PID: $($proc.ProcessId -join ', '))" -ForegroundColor Green
} else {
    Write-Host "[start] 启动失败，请查看日志: $stderrLog" -ForegroundColor Red
    exit 1
}
