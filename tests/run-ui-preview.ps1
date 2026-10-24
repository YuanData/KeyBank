$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$bin = Join-Path $root 'bin'
$out = Join-Path $bin 'UiPreview.exe'
New-Item -ItemType Directory -Force (Join-Path $bin 'preview') | Out-Null
$src = @()
$src += Get-ChildItem (Join-Path $root 'src\Core'), (Join-Path $root 'src\Ui') -Filter *.cs | ForEach-Object FullName
$src += Join-Path $root 'src\IEngineHost.cs'
$src += Join-Path $PSScriptRoot 'UiPreview.cs'
$src += Join-Path $PSScriptRoot 'UiFakeHost.cs'
& $csc /nologo /warnaserror+ /target:exe "/out:$out" "/win32manifest:$root\app.manifest" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll $src
if ($LASTEXITCODE -ne 0) { throw "csc failed ($LASTEXITCODE)" }
& $out (Join-Path $bin 'preview')
exit $LASTEXITCODE
