# Builds and runs the separator probe against the running KeyBank. The probe only types into its own window.
$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$root = Split-Path $PSScriptRoot
$out = Join-Path $root 'bin\SeparatorProbe.exe'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
& $csc /nologo /target:exe "/out:$out" /r:System.Windows.Forms.dll /r:System.Drawing.dll "$PSScriptRoot\SeparatorProbe.cs"
if ($LASTEXITCODE -ne 0) { throw "csc failed ($LASTEXITCODE)" }
& $out
exit $LASTEXITCODE
