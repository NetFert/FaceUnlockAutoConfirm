# Build bin\FaceUnlockAutoConfirm.exe with in-box tools only:
#   csc.exe (.NET Framework 4.x) + UIA3 interop generated from System32\UIAutomationCore.dll (embedded).

# TypeLibConverter: .NET Framework only -> Windows PowerShell 5.1.
if ($PSVersionTable.PSEdition -eq 'Core') {
    & "$env:windir\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath @args
    exit $LASTEXITCODE
}

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$obj = Join-Path $root 'obj'
$bin = Join-Path $root 'bin'
New-Item -ItemType Directory -Force $obj, $bin | Out-Null

$csc = Join-Path ([Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe not found: $csc" }

# 1. Interop.UIAutomationClient.dll
$interop = Join-Path $obj 'Interop.UIAutomationClient.dll'
if (-not (Test-Path $interop)) {
    Add-Type -TypeDefinition @'
using System;
using System.Reflection;
using System.Runtime.InteropServices;
public class HfTlbSink : ITypeLibImporterNotifySink {
    public void ReportEvent(ImporterEventKind k, int c, string m) { }
    public Assembly ResolveRef(object t) { return null; }
}
public static class HfTlbLoader {
    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    public static extern void LoadTypeLibEx(string file, int regKind, [MarshalAs(UnmanagedType.Interface)] out object lib);
}
'@
    $lib = $null
    [HfTlbLoader]::LoadTypeLibEx("$env:windir\System32\UIAutomationCore.dll", 2, [ref]$lib)
    $conv = New-Object System.Runtime.InteropServices.TypeLibConverter
    # Save() uses the .NET current directory
    $prevCwd = [Environment]::CurrentDirectory
    [Environment]::CurrentDirectory = $obj
    try {
        $asm = $conv.ConvertTypeLibToAssembly($lib, 'Interop.UIAutomationClient.dll', 0, (New-Object HfTlbSink), $null, $null, 'Interop.UIAutomationClient', $null)
        $asm.Save('Interop.UIAutomationClient.dll')
    } finally { [Environment]::CurrentDirectory = $prevCwd }
    Write-Host "Generated $interop"
}

# 2. Compile
$out = Join-Path $bin 'FaceUnlockAutoConfirm.exe'
& $csc /nologo /target:winexe /optimize+ /codepage:65001 "/link:$interop" "/out:$out" (Join-Path $root 'src\FaceUnlockAutoConfirm.cs')
if ($LASTEXITCODE -ne 0) { throw "Compilation failed" }
Write-Host "Built $out"
