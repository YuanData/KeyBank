$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$out = Join-Path $PSScriptRoot 'bin\KeyBank.exe'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
$sources = Get-ChildItem (Join-Path $PSScriptRoot 'src') -Recurse -Filter *.cs | ForEach-Object FullName
& $csc /nologo /target:winexe /optimize+ "/out:$out" "/win32manifest:$PSScriptRoot\app.manifest" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll $sources
if ($LASTEXITCODE -ne 0) { throw "csc failed ($LASTEXITCODE)" }
