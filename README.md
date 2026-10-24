# KeyBank

取代 PowerToys Keyboard Manager 的系統匣按鍵工具。單一 exe，不需安裝。

## 使用

雙擊 `bin\KeyBank.exe`，系統匣會出現圖示。藍色是啟用，灰色是停用。

| 操作 | 結果 |
| --- | --- |
| 左鍵點圖示 | 啟用或停用 |
| 雙擊圖示，或選單「查看目前設定」 | 開啟唯讀設定視窗（鍵位圖、組合鍵清單、最後按下的鍵） |
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

`src\Core` 是不依賴 Windows 的決策邏輯，`src\Win` 是鍵盤與滑鼠攔截與注入，`src\Ui` 是托盤與視窗。`tests\LiveSmoke.ps1` 與 `spikes\` 是除錯過程的留存，LiveSmoke 在目前環境不可靠，不要當成驗證依據。
