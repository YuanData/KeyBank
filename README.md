# KeyBank

取代 PowerToys Keyboard Manager 的系統匣按鍵工具。單一 exe，不需安裝。

## 使用

雙擊 `bin\KeyBank.exe`，系統匣會出現灰底星號圖示。

| 操作 | 結果 |
| --- | --- |
| 左鍵或右鍵點圖示 | 開啟選單 |
| 選單「停用」 | 勾選就停用，圖示變成疊上紅色禁止符號 |
| 選單「查看目前設定」 | 開啟唯讀設定視窗（鍵位圖、組合鍵清單、最後按下的鍵） |
| 右鍵選單「分段捲動」 | PgUp、PgDn 的分段捲動開關 |
| 右鍵選單「Alt 加滑鼠變 Ctrl」 | 開關 |
| 右鍵選單「捲動微調…」 | 兩根橫桿調單次格數與整頁格數，附試捲區 |
| 右鍵選單「鍵盤設定」 | 自動辨識，或鎖定某一組設定 |
| 右鍵選單「開機自動啟動」 | 預設關閉，勾選才會寫入 HKCU Run |
| 右鍵選單「開啟設定檔」 | 編輯 `%APPDATA%\KeyBank\config.json`，存檔後自動重新讀取 |

## 功能

**Alt 手勢（Mac 的 Cmd 習慣）**
- 全域：Alt 加 C、X、V、A、Z、S、F 變成 Ctrl 加同一個鍵。Shift 照常，所以 Alt+Shift+Z 變 Ctrl+Shift+Z。
- 只在 Chrome（`chrome.exe`）：W、T、N、R、L、D、數字 1 到 9 與 0、等號鍵、減號鍵。Alt+Shift+T 重新開啟分頁、Alt+Shift+N 無痕視窗也自動生效。
- 其他 Alt 組合照常，Alt+Tab 是原本的切換視窗。
- 要加別的程式，在設定檔的 `appAltChords` 加一行，例如 `"firefox.exe": ["W", "T"]`。

**Alt 加滑鼠（實驗性，預設關閉）**
- 按住左 Alt 時，所有滑鼠按鍵和垂直滾輪都當成按住 Ctrl（例如 Alt+點擊開新分頁、Alt+滾輪縮放）。
- 預設關閉，滑鼠攔截不會安裝。要開啟，右鍵選單勾「Alt 加滑鼠變 Ctrl」，或把設定檔 `altMouse` 改成 `true`。
- 這個功能曾導致程式卡死，尚未用實體滑鼠驗證，請確認可用後再長期開啟。
- 實測：舊版開啟後 `%TEMP%\KeyBank.crash.log` 出現過 `slow mouse callback: 308 ms`，與 Windows 攔截逾時（約 300 毫秒）相符。推測是在攔截處理中送出滑鼠動作，要等逾時才返回，逾時後 Windows 會忽略「吞掉原事件」，Alt+點擊就失效。現在滑鼠的輸出改為排進佇列，由攔截執行緒在處理返回後送出，單元測試確認處理期間不再送出輸入。是否真的解決，要看換新版後記錄檔還會不會出現 `slow mouse callback`，這點尚未用實體滑鼠驗證。

**滾輪反向抑制（預設關閉）**
- 有些滑鼠（例如 Logitech MX Anywhere 2S）往一個方向捲動時，會夾雜極小的反向滾輪事件，造成畫面跳回去。這個功能把它們丟掉。
- 判定：以最近一次放行的事件建立主要方向。反向事件若在時間窗內（預設 80 毫秒）且反向位移累計沒超過門檻（預設 120，也就是一格），就丟棄。超過門檻或停頓超過時間窗，就當成真正的反向或重新開始，立刻放行。
- 只丟棄，不暫存也不重送，不改變任何事件的方向或位移量，所以沒有額外延遲，也不會產生合成事件。代價是：確認反向之前被丟掉的那幾下（至多一格）不會補回來。
- 其他程式注入的滾輪事件不處理，只處理實體滑鼠。
- 開啟：右鍵選單勾「滾輪反向抑制」，或設定檔 `wheelFilter.enabled` 改成 `true`。參數 `wheelFilter.windowMs`（10 到 500）與 `wheelFilter.thresholdDelta`（1 到 1200）只能在設定檔調整。
- 開啟時會安裝滑鼠攔截，狀態在設定視窗的「滾輪」列顯示。
- 如果停止捲動時仍偶爾反彈：反彈可能晚於時間窗才出現，或位移量大於門檻，所以沒被丟掉。用 `tests\WheelCapture.ps1` 記錄實際的間隔與位移量，再依資料調整 `windowMs` 與 `thresholdDelta`。追蹤格式為 `wheel(-120) => pass gap=35ms`，被丟棄的事件結尾會有 `wheelfilter`。

**滾輪加速（預設關閉）**
- 持續轉動滾輪時放大滾動量。兩段倍率相加：累積格數（預設第 3 格起，5 格內升到 ×2）、持續時間（預設滿 500 毫秒起，再經 1000 毫秒加到最多 +2），總倍率上限預設 5 倍。
- 兩格間隔超過 `gapMs`（預設 100 毫秒）或方向改變，就重新計算。倍率為 1 時事件原樣放行，不經過注入。
- 按住 Ctrl 或 Win（縮放、快捷鍵）、Alt 加滑鼠變 Ctrl 時不加速；其他程式注入的滾輪事件不處理。
- 與反向抑制並用時，先抑制再加速。
- 開啟：右鍵選單勾「滾輪加速」，或設定檔 `wheelAccel.enabled` 改成 `true`。參數 `gapMs`、`startTicks`、`rampTicks`、`tickBoost`、`timeStartMs`、`timeRampMs`、`timeBoost`、`maxMultiplier` 只能在設定檔調整。
- 預設值是起點，需要實機調整手感。

**滾輪記錄（常駐，預設開啟）**
- 為了追查「往下滾動停止時偶爾反彈」，KeyBank 會把每個捲動段落記成一行，寫在 `%LOCALAPPDATA%\KeyBank\logs\wheel.log`，超過 2 MB 輪替成 `wheel.log.1`。
- 只記時間、程式名稱、位移量與間隔，不記按鍵內容、視窗標題、座標。標記鍵之外的按鍵一概不記。
- 看到反彈時，在反彈後 1 秒內、下一次滑動之前，按一下 **F9**，記錄裡會多一行 `MARK`。F9 本身不會被吞掉或改變。
- 每行格式：`時間 app=chrome dir=down n=21 sum=-2520 arr=460ms hw=455ms maxHwGap=38ms tailHw=… tailArr=… clump=0 rev=0`。
  - `arr` 是 KeyBank 收到事件的時間跨度，`hw` 是滑鼠自己的事件時間跨度；`tailHw`、`tailArr` 是最後六個間隔。兩者對照，可以分辨是滑鼠自己的節奏，還是事件在途中被擠在一起（`clump` 是這種事件的數量）。
  - `rev` 是反向事件數量，每個反向事件另外列出位移量、在段落裡的位置，以及放行或丟棄。
- 設定檔 `diagnostics`：`wheelLog`（開關）、`burstGapMs`（兩段之間的靜止時間，預設 250）、`markKey`（預設 `F9`，留空就停用）。
- 開啟滾輪記錄時會安裝滑鼠攔截，只讀取、不改變任何事件。

**分段捲動**
- 按 PgUp、PgDn 一下，捲「單次格數」。連點第 3 下起（每下間隔 400 毫秒內、同方向）捲「整頁格數」。停頓或換方向就重新計算。
- 按住不放當成連點，每秒最多 8 次。
- 用滾輪格數近似，不是精確的一頁，也不會移動游標。要校準，用「捲動微調…」；要為特定程式各設一組，在設定檔 `scroll.apps` 加一行，例如 `"notepad++.exe": { "stepTicks": 6, "pageTicks": 12 }`。
- 連點參數在設定檔 `scroll.burstTaps`、`scroll.burstIntervalMs`、`scroll.holdRepeatsPerSec`。

**迷你鍵盤（廠商碼 046D）**
- 實體 PgUp 變 Home、PgDn 變 PgUp、`*` 變 End、`-` 變 PgDn。最左邊的 Home 按住 500 毫秒送出 PrintScreen，短按仍是 Home。

**鍵盤辨識**
- 依廠商碼自動辨識：3434 是完整鍵盤，046D 是迷你鍵盤，內建鍵盤和其他鍵盤用 `defaultProfile`。

## 托盤沒反應或卡住時

| 做法 | 說明 |
| --- | --- |
| 再雙擊一次 `bin\KeyBank.exe` | 叫出執行中那一份的設定視窗 |
| `bin\KeyBank.exe --quit` | 關閉執行中的那一份，托盤卡住時也有效 |
| 自動保護 | 輸入處理停擺超過約 1.5 秒，程式會先解除鍵盤與滑鼠攔截，再自己結束，原因寫在 `%TEMP%\KeyBank.crash.log` |
| 圖示在哪裡 | Windows 11 預設把新程式的圖示放進工作列右下角「^」的隱藏區 |

## 已知限制

- 迷你鍵盤的規則、Alt 加滑鼠、Chrome 專用手勢，尚未用實體鍵盤與滑鼠逐項驗證。若迷你鍵盤有問題，從「鍵盤設定」鎖定成「完整鍵盤」。
- 換鍵盤後的第一下按鍵，可能還套用前一把鍵盤的設定。
- 一般權限的 KeyBank 無法處理以系統管理員身分執行的視窗。
- 舊版設定檔（有 `halfPage` 區段）第一次啟動時會自動升級，原檔備份成 `config.json.bak`。
- 使用前要先在 PowerToys 關閉 Keyboard Manager，否則兩邊會重複對應。退回時，把它的開關打開、結束 KeyBank 即可。

## 開發

| 指令 | 用途 |
| --- | --- |
| `build.ps1` | 編譯成 `bin\KeyBank.exe` |
| `test.ps1` | 核心邏輯測試 |
| `tests\run-win-tests.ps1` | Windows 層測試，不注入按鍵 |
| `tests\run-ui-preview.ps1` | 設定視窗與微調視窗測試，預覽圖在 `bin\preview\` |
| `tests\PhysicalCheck.ps1` | 實體鍵與滑鼠驗證，90 秒追蹤，要加 `-ExecutionPolicy Bypass` 執行 |
| `tests\WheelCapture.ps1` | 滾輪反彈診斷：追蹤 60 秒，只印出滾輪事件的方向、位移量、與上一筆的間隔，以及有沒有被丟棄 |

`src\Core` 是不依賴 Windows 的決策邏輯，`src\Win` 是鍵盤與滑鼠攔截與注入，`src\Ui` 是托盤與視窗。`tests\LiveSmoke.ps1` 與 `spikes\` 是除錯過程的留存，LiveSmoke 在目前環境不可靠，不要當成驗證依據。
