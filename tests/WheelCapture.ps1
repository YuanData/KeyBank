param([int]$Seconds = 60)
$ErrorActionPreference = 'Stop'
$exe = Join-Path (Split-Path $PSScriptRoot) 'bin\KeyBank.exe'
$trace = Join-Path $env:TEMP 'KeyBank.trace.log'

if (Get-Process -Name KeyBank -ErrorAction SilentlyContinue) {
    Write-Host '請先結束正在執行的 KeyBank（bin\KeyBank.exe --quit），再重跑本腳本。' -ForegroundColor Red
    exit 2
}

Write-Host "KeyBank 會以追蹤模式執行 $Seconds 秒，用你目前的設定檔（滾輪反向抑制開或關都照設定）。" -ForegroundColor Yellow
Write-Host '追蹤檔會記錄這段時間的所有按鍵，結束後只印出滾輪的行，追蹤檔立刻刪除。請不要在這段時間輸入密碼或其他內容。'
Write-Host ''
Write-Host '請在會出現反彈的程式裡（例如 Chrome 的長網頁）操作：'
Write-Host '  1. 用滾輪往下滑，滑一小段就停下，重複約 10 次，盡量重現「最後一下反彈」'
Write-Host '  2. 再用滾輪往上滑，同樣重複約 10 次'
Write-Host '  3. 每次停下來後，等一秒再做下一次'
Write-Host '  4. 看到反彈的當下，記下是哪個程式、往哪個方向'
Write-Host ''
Read-Host '準備好就按 Enter 開始計時' | Out-Null

$p = Start-Process -FilePath $exe -ArgumentList '--trace' -PassThru
Start-Sleep -Seconds $Seconds
Stop-Process -Id $p.Id -Force
Start-Sleep -Milliseconds 500

$lines = Get-Content $trace -ErrorAction SilentlyContinue | Where-Object { $_ -match 'wheel\(' }
$lines | ForEach-Object { $_ }
Remove-Item $trace -ErrorAction SilentlyContinue
Write-Host ''
Write-Host ("共 {0} 筆滾輪事件。把上面的輸出貼給 Claude，並說明哪幾筆是你看到反彈的時候。" -f @($lines).Count) -ForegroundColor Green
