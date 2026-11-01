# ============================================================
# Triple-Detection 停止脚本
# 用法: .\stop.ps1
# 说明: 按命令行特征精确匹配 TripleDetection.App.dll 进程，
#       不会误杀其他 dotnet 进程
# ============================================================
$ErrorActionPreference = "Stop"

$procs = Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
    Where-Object { $_.CommandLine -like "*TripleDetection.App.dll*" }

if (-not $procs) {
    Write-Host "[stop] 没有正在运行的 TripleDetection 进程" -ForegroundColor Yellow
    exit 0
}

foreach ($p in $procs) {
    Stop-Process -Id $p.ProcessId -Force
    Write-Host "[stop] 已停止 PID $($p.ProcessId)" -ForegroundColor Green
}
