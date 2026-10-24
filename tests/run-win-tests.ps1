$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$bin = Join-Path $root 'bin'
New-Item -ItemType Directory -Force $bin | Out-Null
$refs = '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Web.Extensions.dll'
$lib = @("$root\src\Core\*.cs", "$root\src\IEngineHost.cs", "$root\src\Win\*.cs")

& $csc /nologo /target:library /warnaserror+ "/out:$bin\KeyBank.Win.dll" @refs @lib
if ($LASTEXITCODE -ne 0) { throw "library compile failed ($LASTEXITCODE)" }

$exe = "$bin\WinTests.exe"
& $csc /nologo /target:exe /warnaserror+ "/out:$exe" @refs @lib "$root\tests\WinTests.cs"
if ($LASTEXITCODE -ne 0) { throw "test compile failed ($LASTEXITCODE)" }

# The tests install real low-level hooks for short stretches and never inject input. If this process ever
# hangs, the watchdog inside Engine unhooks and exits it; the kill below is only the last resort.
$p = Start-Process -FilePath $exe -NoNewWindow -PassThru -Wait:$false
if (-not $p.WaitForExit(120000)) { $p.Kill(); throw 'WinTests hung and was killed' }
$code = $p.ExitCode

Start-Sleep -Milliseconds 300
$left = @(Get-Process -Name WinTests -ErrorAction SilentlyContinue | Where-Object { $_.Id -eq $p.Id })
if ($left.Count -gt 0) { Write-Host 'FAIL WinTests process still alive after exit'; $code = 1 }
else { Write-Host 'no WinTests process left running' }
exit $code
