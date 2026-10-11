# Install + logon task (elevated). Run as Administrator.
#   .\install.ps1                                  # -> C:\Program Files\FaceUnlockAutoConfirm
#   .\install.ps1 -InstallDir 'D:\Tools\FaceUnlockAutoConfirm'
#   .\install.ps1 -User 'PC\someone'               # default: current user
param(
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'FaceUnlockAutoConfirm'),
    [string]$User = [Security.Principal.WindowsIdentity]::GetCurrent().Name
)
$ErrorActionPreference = 'Stop'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw 'Run as Administrator.' }

# Source: local build or release exe next to this script
$src = @((Join-Path $PSScriptRoot 'bin\FaceUnlockAutoConfirm.exe'), (Join-Path $PSScriptRoot 'FaceUnlockAutoConfirm.exe')) |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $src) { throw 'FaceUnlockAutoConfirm.exe not found. Run .\build.ps1 or place the exe next to this script.' }

$taskName = 'FaceUnlockAutoConfirm'
if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) { Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue }
Get-Process FaceUnlockAutoConfirm -ErrorAction SilentlyContinue | Stop-Process -Force

# Install dir: write = Administrators/SYSTEM only (exe runs elevated)
New-Item -ItemType Directory -Force $InstallDir | Out-Null
$acl = New-Object System.Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true, $false)
$inherit = [System.Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
$none = [System.Security.AccessControl.PropagationFlags]::None
foreach ($rule in @(
    @('S-1-5-18',     'FullControl'),      # SYSTEM
    @('S-1-5-32-544', 'FullControl'),      # Administrators
    @('S-1-5-32-545', 'ReadAndExecute'))) { # Users
    $sid = New-Object System.Security.Principal.SecurityIdentifier($rule[0])
    $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($sid, $rule[1], $inherit, $none, 'Allow')))
}
$acl.SetOwner((New-Object System.Security.Principal.SecurityIdentifier('S-1-5-32-544')))
Set-Acl -Path $InstallDir -AclObject $acl

$exe = Join-Path $InstallDir 'FaceUnlockAutoConfirm.exe'
Copy-Item $src $exe -Force
Unblock-File $exe -ErrorAction SilentlyContinue

$action    = New-ScheduledTaskAction -Execute $exe -WorkingDirectory $InstallDir
$trigger   = New-ScheduledTaskTrigger -AtLogOn -User $User
$principal = New-ScheduledTaskPrincipal -UserId $User -LogonType Interactive -RunLevel Highest
$settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings `
    -Description 'FaceUnlockAutoConfirm: Windows Hello prompt to front + auto-confirm after face recognition' -Force | Out-Null
Start-ScheduledTask -TaskName $taskName

Write-Host "Installed: $exe"
Write-Host "Task: $taskName ($User, logon, elevated) started."
Write-Host "Log: $env:ProgramData\FaceUnlockAutoConfirm\FaceUnlockAutoConfirm.log"
