# Builds and runs the Alt + click probe against the running KeyBank. The probe only injects into its own window.
$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$root = Split-Path $PSScriptRoot
$out = Join-Path $root 'bin\AltClickProbe.exe'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
& $csc /nologo /target:exe "/out:$out" /r:System.Windows.Forms.dll /r:System.Drawing.dll "$PSScriptRoot\AltClickProbe.cs"
if ($LASTEXITCODE -ne 0) { throw "csc failed ($LASTEXITCODE)" }
& $out
exit $LASTEXITCODE
