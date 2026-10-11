# Uninstall. Run as Administrator.
#   .\uninstall.ps1 [-InstallDir 'D:\Tools\FaceUnlockAutoConfirm'] [-KeepLog]
param(
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'FaceUnlockAutoConfirm'),
    [switch]$KeepLog
)
$ErrorActionPreference = 'Stop'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw 'Run as Administrator.' }

$taskName = 'FaceUnlockAutoConfirm'
if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
    Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    Write-Host "Removed task: $taskName"
}
Get-Process FaceUnlockAutoConfirm -ErrorAction SilentlyContinue | Stop-Process -Force

$exe = Join-Path $InstallDir 'FaceUnlockAutoConfirm.exe'
if (Test-Path $exe) {
    Remove-Item $exe -Force
    if (-not (Get-ChildItem $InstallDir -Force)) { Remove-Item $InstallDir -Force }
    Write-Host "Removed: $exe"
}

if (-not $KeepLog) {
    # Elevated log: ProgramData; non-elevated: LOCALAPPDATA
    foreach ($logDir in @((Join-Path $env:ProgramData 'FaceUnlockAutoConfirm'), (Join-Path $env:LOCALAPPDATA 'FaceUnlockAutoConfirm'))) {
        if (Test-Path $logDir) {
            Remove-Item $logDir -Recurse -Force
            Write-Host "Removed: $logDir"
        }
    }
}
