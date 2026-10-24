param([int]$Seconds = 90)
$ErrorActionPreference = 'Stop'
$exe = Join-Path (Split-Path $PSScriptRoot) 'bin\KeyBank.exe'
$trace = Join-Path $env:TEMP 'KeyBank.trace.log'

if (Get-Process -Name KeyBank -ErrorAction SilentlyContinue) {
    Write-Host '請先從系統匣結束正在執行的 KeyBank，再重跑本腳本。' -ForegroundColor Red
    exit 2
}
if (Get-Process -Name 'PowerToys.KeyboardManagerEngine' -ErrorAction SilentlyContinue) {
    Write-Host 'PowerToys 的 Keyboard Manager 還在跑，請先在 PowerToys 關閉它。' -ForegroundColor Red
    exit 2
}

Write-Host "KeyBank 會以追蹤模式執行 $Seconds 秒。這段時間內的所有按鍵都會被記錄，請只按下面列的測試鍵。" -ForegroundColor Yellow
Write-Host '結束後只會顯示測試相關的行，追蹤檔會立刻刪除。'
Write-Host ''
Write-Host '請在 Chrome 開一個普通網頁（頁面要夠長），依序操作：'
Write-Host '  1. PgDn 慢慢按 1 下，等 1 秒。預期畫面捲動單次格數'
Write-Host '  2. PgDn 快速連按 4 下（每下間隔不到 0.4 秒）。預期第 3 下起捲整頁'
Write-Host '  3. 按住 PgDn 約 2 秒再放開。預期連續捲動，速度有限制'
Write-Host '  4. 按 PgUp 1 下。預期往上捲'
Write-Host '  5. 按住 Alt 再按 T（新分頁），再按住 Alt 按 W（關掉它）'
Write-Host '  6. 按住 Alt 再用滑鼠左鍵點一個連結（預期在新分頁開啟）'
Write-Host '  7. 按住 Alt 再滾動滾輪（預期頁面縮放）'
Write-Host '  8. 在文字區選一些字，按住 Alt 再按 C'
Write-Host '  9. 換另一把鍵盤（迷你鍵盤），重複 1 到 4，再按住最左邊的 Home 約 1 秒再放開，按一下 * 和 -'
Write-Host ''
Read-Host '準備好就按 Enter 開始計時' | Out-Null

$p = Start-Process -FilePath $exe -ArgumentList '--trace' -PassThru
Start-Sleep -Seconds $Seconds
Stop-Process -Id $p.Id -Force
Start-Sleep -Milliseconds 500

$lines = Get-Content $trace -ErrorAction SilentlyContinue
$keep = 'PageUp|PageDown|Multiply|Subtract|Home|End|LeftAlt|=> .*\+(C|T|W)\b|Btn|wheel|tick =>'
$lines | Where-Object { $_ -match $keep } | ForEach-Object { $_ }
Remove-Item $trace -ErrorAction SilentlyContinue
Write-Host ''
Write-Host '完成。追蹤檔已刪除。把上面的輸出貼給 Claude，並說明畫面上的實際結果。' -ForegroundColor Green
