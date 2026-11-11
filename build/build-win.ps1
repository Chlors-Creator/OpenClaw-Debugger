param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/OpenClawDebugger.Shell.Windows/OpenClawDebugger.Shell.Windows.csproj'

Write-Host "发布 OpenClaw Debugger（$Configuration / $Runtime）…"
dotnet publish $project -c $Configuration -r $Runtime --self-contained false
Write-Host '完成。产物位于 src/OpenClawDebugger.Shell.Windows/bin/ 下的 publish 目录。'
