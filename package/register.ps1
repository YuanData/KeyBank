$ErrorActionPreference = 'Stop'
# 需先開啟 Windows 開發人員模式。只註冊 KeyBank.NewText 套件，不改其他系統設定。
$root = Split-Path $PSScriptRoot
$bin = Join-Path $root 'bin'
if (-not (Test-Path (Join-Path $bin 'KeyBank.exe'))) { throw '找不到 bin\KeyBank.exe，請先執行 build.ps1' }

$logo = Join-Path $PSScriptRoot 'logo.png'
if (-not (Test-Path $logo)) {
    Add-Type -AssemblyName System.Drawing
    $bmp = New-Object System.Drawing.Bitmap 150, 150
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::FromArgb(0, 90, 60))
    $g.Dispose()
    $bmp.Save($logo, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$ico = Join-Path $PSScriptRoot 'newtext.ico'
if (-not (Test-Path $ico)) {
    Add-Type -AssemblyName System.Drawing
    $bmp = New-Object System.Drawing.Bitmap 32, 32
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.FillEllipse([System.Drawing.Brushes]::OrangeRed, 1, 1, 30, 30)
    $f = New-Object System.Drawing.Font 'Arial', 18, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $g.DrawString('+', $f, [System.Drawing.Brushes]::White, 6, 5)
    $g.Dispose()
    $icon = [System.Drawing.Icon]::FromHandle($bmp.GetHicon())
    $fs = [IO.File]::Create($ico); $icon.Save($fs); $fs.Close()
}

Add-AppxPackage -Register (Join-Path $PSScriptRoot 'AppxManifest.xml') -ExternalLocation $bin
Write-Host '已註冊。請重新啟動檔案總管（工作管理員 → Windows 檔案總管 → 重新啟動）。'
