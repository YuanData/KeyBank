$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$out = Join-Path $PSScriptRoot 'bin\CoreTests.exe'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
& $csc /nologo /target:exe "/out:$out" /r:System.Web.Extensions.dll `
    "$PSScriptRoot\src\Core\*.cs" "$PSScriptRoot\tests\CoreTests.cs"
if ($LASTEXITCODE -ne 0) { throw "csc failed ($LASTEXITCODE)" }
& $out
exit $LASTEXITCODE
