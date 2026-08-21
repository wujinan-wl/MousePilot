# MousePilot 總體實作計畫（Master Plan）

**Spec:** `docs/spec/mousepilot-spec.md`
**日期:** 2026-08-20
**目標:** 完成 MousePilot — Windows 防閒置滑鼠微移工具 + 自訂游標功能，最終產出 Portable 單一 `MousePilot.exe`（win-x64、self-contained）。

**架構:** C# / .NET 8 / WPF / MVVM（CommunityToolkit.Mvvm）。核心邏輯全部放在 Services（每職責一個 Service），PInvoke 集中於 `Native/NativeMethods.cs`，View 只做綁定。可測邏輯（設定、移動計算、邊界處理、log rotate）用 xUnit 覆蓋；依賴真實 Windows 環境的行為（SendInput、SetSystemCursor、Tray、Hotkey）以規格 §34 測試案例做手動驗證清單。

**開發紀律（依使用者偏好）:**
- 每個 Phase 有獨立細部計畫文件（本檔只是總表 + 範圍摘要），範圍確認後才動工。
- 每個 Phase 結束條件：`dotnet build -c Release` 成功 + 該 Phase 測試項目通過 + 本表更新狀態。
- 版本：`VERSION` 檔為唯一來源；`CHANGELOG.md` 維護 `[Unreleased]`，每個 Phase 完成時寫入。
- Git：只做本地 commit（繁中訊息 + 前綴），不 push、不建 tag。

---

## 進度總表

| Phase | 名稱 | 狀態 | 細部計畫文件 |
|-------|------|------|--------------|
| 0 | 高風險技術驗證（Spike） | ✅ 完成 | `2026-08-20-phase0-phase1.md` |
| 1 | 專案骨架：WPF + MVVM + Settings + Dashboard 殼 | ✅ 完成 | `2026-08-20-phase0-phase1.md` |
| 2 | Idle Detection | ✅ 完成 | `2026-08-20-phase2-idle-detection.md` |
| 3 | Mouse Movement | ✅ 完成 | `2026-08-20-phase3-mouse-movement.md` |
| 4 | System Tray | ✅ 完成 | `2026-08-21-phase4-system-tray.md` |
| 5 | Startup Registry | ✅ 完成 | `2026-08-21-phase5-startup-registry.md` |
| 6 | Global Hotkey | ✅ 完成 | `2026-08-21-phase6-global-hotkey.md` |
| 7 | Cursor Import（含內建圖案庫） | ⬜ 未開始 | 〃 |
| 8 | Hotspot Editor + Preview | ⬜ 未開始 | 〃 |
| 9 | Global Cursor 套用/恢復 | ⬜ 未開始 | 〃 |
| 10 | Single Instance | ⬜ 未開始 | 〃 |
| 11 | Exception Handling / Log | ⬜ 未開始 | 〃 |
| 12 | Publish Single EXE + 文件交付 | ⬜ 未開始 | 〃 |

狀態：⬜ 未開始 / 🟡 進行中 / ✅ 完成 / ⛔ 阻塞

---

## Phase 0：高風險技術驗證（Spike）

- **目標：** 在寫正式程式前回答規格 §36 的兩個問題，把結論寫成研究筆記，供 Phase 2/9 設計引用。
- **本階段範圍：**
  - Spike A：驗證 `SendInput` 模擬滑鼠移動是否重置 `GetLastInputInfo`；驗證候選隔離方案（模擬時間窗 + 預期座標比對）可行性。
  - Spike B：驗證 `SetSystemCursor` 替換游標後，`SystemParametersInfo(SPI_SETCURSORS)` 能完整恢復使用者 Cursor Scheme（含非預設 scheme、強制結束程序後手動恢復的可行性）。
- **不在本階段範圍：** 任何正式專案程式碼；spike 程式碼放 `spikes/`，不進 MousePilot 專案。
- **預計新增檔案：** `spikes/IdleSpike/`、`spikes/CursorSpike/`（console 專案）、`docs/superpowers/research/2026-08-20-spike-findings.md`。
- **測試項目：** 手動執行 spike，記錄實測輸出（不得臆測結論）。
- **風險：** SetSystemCursor 會改動目前 Windows session 的游標——spike 必須先實作恢復再實作替換；在自己機器上測試前先確認恢復路徑可用。

## Phase 1：專案骨架

- **目標：** 可 build、可執行的 WPF 專案：MVVM 架構、AppSettings + SettingsService（含損毀處理）、Dashboard 靜態殼（卡片版面、狀態區、設定綁定）。
- **本階段範圍：** solution + csproj（含發布屬性）、xUnit 測試專案、AppSettings（含範圍夾制）、SettingsService（load/save/損毀備份/預設值）、MainViewModel、MainWindow Dashboard 殼、App 啟動/關閉時載入/保存設定、git init + .gitignore、VERSION/CHANGELOG。
- **不在本階段範圍：** 閒置偵測、滑鼠移動、Tray、Hotkey、Cursor 任何功能——Dashboard 上對應按鈕先停用（disabled），不接假邏輯。
- **預計新增檔案：** 見細部計畫。
- **測試項目：** AppSettings 預設值與夾制、SettingsService round-trip、損毀 JSON → 備份+預設值不 crash、資料夾自動建立；手動：`dotnet run` 開出 Dashboard。
- **風險：** PublishSingleFile + WPF 相容性——Phase 1 就把 publish 跑通一次，避免 Phase 12 才發現問題。

## Phase 2：Idle Detection

- **目標：** IdleDetectionService 以 `GetLastInputInfo` + 500~1000ms DispatcherTimer 偵測閒置，UI 即時顯示閒置秒數/剩餘時間/狀態（監控中、使用者活動中、等待啟動、自動移動中、已暫停）。
- **本階段範圍：** IdleDetectionService（含 Phase 0 結論的自我輸入隔離介面：`SuppressWindow(DateTime start, TimeSpan len)` 供 Phase 3 呼叫）、狀態機、ViewModel 綁定、啟動/暫停按鈕生效、狀態顏色點（綠/黃/灰/紅）。
- **不在本階段範圍：** 實際滑鼠移動（觸發點先只改狀態 + 記 log 佔位事件）。
- **測試項目：** 規格 §34 Idle 1~4（手動）；狀態機轉移與倒數計算（單元測試，時間來源抽象成 `Func<uint> tickProvider` 以便 mock）。
- **風險：** `GetLastInputInfo` 回傳的 tick 是 32-bit，49.7 天 wrap-around——計算需用 unchecked 差值。

## Phase 3：Mouse Movement

- **目標：** MouseMovementService：左右/上下/隨機三模式、1~100px、回原位置（100~500ms 後返回）、螢幕邊界反向、多螢幕負座標、CancellationToken 全程可取消；「立即執行一次」按鈕。
- **本階段範圍：** 移動目標計算（純函式，可單元測試：輸入目前座標 + Virtual Screen Bounds + 模式 + 像素 → 輸出合法目標座標）、SendInput 送出、與 IdleDetectionService 的自我輸入隔離整合、使用者重新操作立即取消（規格 §6、§24）。
- **測試項目：** 規格 §34 案例 5~12；目標計算純函式的邊界/負座標單元測試。
- **風險：** SendInput 的座標正規化（0~65535 絕對座標 vs virtual desktop flag）在多螢幕 + DPI 下容易算錯，需以 Phase 0 spike 實測為準。
- **硬性約束（Phase 2 final review / ledger 移交，計畫撰寫時必須逐條納入）：**
  1. 抑制窗為單一覆蓋語意：一次 `Suppress` 必須涵蓋整個「移動＋返回」動作；若分兩次 Suppress，需保證兩次模擬輸入之間至少發生一次 Tick，否則前窗被覆蓋會把前一個模擬輸入誤判為真實輸入。
  2. `MoveRequested`/`Ticked` 訂閱端不得拋例外（Phase 11 全域 handler 之前，例外會經 DispatcherTimer 傳播）。
  3. `AutoMoving` 狀態目前無進入路徑（`IdleStateMachine.State` private set、Tick 只產生四種狀態）——需在狀態機新增進入/離開 API。
  4. 「回原位置」的 100~500ms 等待落在兩次輪詢之間：執行返回移動前必須再讀一次 `GetLastInputInfo` 確認無真實輸入，取消不能只靠 500ms 輪詢（規格 §24 CancellationToken）。
  5. 若實作「預期座標比對」防線，先修 `NativeMethods.GetCursorPosition` 失敗回 `(0,0)` 的 fallback（(0,0) 在多螢幕負座標下是合法座標，應改 nullable 或 last-known）。
- **已知限制（Phase 3 final review 記錄，屬抑制窗架構固有的接受成本）：**
  1. 孤立的單次真實輸入若 tick 恰落在 ≤500ms 抑制窗內，不會觸發「重新計時」（GetLastInputInfo 只保留最新 tick）——使用者不受干擾（返回仍會被雙重防線放棄），但自動週期會多跑一次；持續操作則在窗結束後 ≤500ms 內被正常採納。
  2. Suppress 餘裕 200ms：若 UI thread 卡頓使返回移動晚於窗尾送出，該模擬輸入會被誤採納為真實輸入 → 週期靜默重啟（失敗方向保守：絕不多動滑鼠，只會多等）。列入實機觀察點。
  3. 「自動移動中」狀態通常在兩次輪詢之間就結束（300ms vs 500ms），UI 上稍縱即逝——非 bug。

## Phase 4：System Tray

- **目標：** Tray icon + 右鍵選單（開啟/啟動/暫停/立即執行一次/游標三項/設定/結束）、雙擊開 Dashboard、關閉視窗縮小至 Tray（可設定）、安全結束流程（規格 §30 順序）。
- **不在本階段範圍：** 游標三個選單項先停用（Phase 9 啟用）。
- **測試項目：** 規格 §34 案例 25；Explorer 重啟後 Tray icon 重建（TaskbarCreated 訊息）。
- **風險（已決策）：** 採 WinForms NotifyIcon——UseWindowsForms 屬 Desktop Runtime 內建非新 NuGet、自帶 TaskbarCreated（Explorer 重啟）重建；以 csproj `<Using Remove>` 根除與 WPF 的全域 using 型別歧義。

## Phase 5：Startup Registry

- **目標：** StartupService 讀寫 `HKCU\...\Run`，UI checkbox 同步實際 Registry 狀態；「啟動後最小化到系統匣」「啟動後自動開始監控」生效。
- **測試項目：** 規格 §34 案例 23、24；Registry 寫入失敗不 crash（顯示錯誤）。
- **風險：** Portable EXE 被移動位置後 Run value 路徑失效——寫入時以目前 EXE 路徑更新。
- **完成註記（final review 補強）：** (a) 已處理工作管理員「停用」語意（StartupApproved key）——IsEnabled 偵測停用旗標、Enable/Disable 清除旗標，「Registry 為真實來源」決策完整成立；(b) 測試層已加 RealRunKeyCanaryFixture（快照/驗證真 Run key），未來測試若遺漏注入 StartupService 會 fail loudly——原擬移交 Phase 11 的「測試層 guard」已就地解決。

## Phase 6：Global Hotkey

- **目標：** HotkeyService（RegisterHotKey）：預設 Ctrl+Alt+F9 啟停、Ctrl+Alt+F10 恢復游標；UI 可改快捷鍵；占用/無效/重複偵測與清楚錯誤。
- **測試項目：** 規格 §34 案例 26、27。
- **風險：** Hotkey 需要 HWND 訊息迴圈——掛在隱藏訊息視窗上，避免依賴 MainWindow 存在。
- **移交事項（Phase 2 ledger）：** 本階段開始有程式改設定值 → AppSettings 需 INPC 化（或 VM 包裝屬性），順帶解決「StartMonitoring 的 Clamp 改值後 UI 不刷新」問題。
- **移交事項（Phase 4 final review）：** Tray 選單的 Start/Pause 以 `Execute(null)` 直呼、未檢查 CanExecute（選單開著跨狀態轉換時可能 stale）——hotkey 啟停實作時一併在兩處呼叫點加 `CanExecute` guard。

## Phase 7：Cursor Import（含內建圖案庫）

- **目標：** CursorService 圖片處理半邊：匯入 PNG/JPG/JPEG/BMP/CUR/ANI；圖片→cursor 轉換（保留 alpha、等比縮放、尺寸 16~128、預設 32、卡通類預設 48）、透明裁切、JPG 簡易去背（背景色+容差）；內建圖案庫（基本類 8 種 + 可愛類 8 種，Embed 進 assembly，不含未授權哆啦A夢素材，以「藍色機器貓風格」替代）；匯入檔複製到 `%AppData%\MousePilot\Cursors\`；收藏功能。
- **不在本階段範圍：** 套用到 Windows 全域（Phase 9）；Hotspot 編輯 UI（Phase 8）。
- **測試項目：** 規格 §34 案例 13~17；轉換純邏輯（縮放比例、裁切框、去背容差）單元測試。
- **風險：** ANI 格式無內建解析——載入失敗要優雅降級；.cur 檔案格式需自行寫入 hotspot 欄位。

## Phase 8：Hotspot Editor + Preview

- **目標：** CursorEditorWindow：圖片 + 座標格 + 點擊設 Hotspot（十字標記 + 座標顯示 + 手動輸入）；Preview Panel（白/深色背景、按鈕、Checkbox、Hyperlink、文字區域、原始/實際尺寸與 Hotspot 顯示）；預設圖案 Grid 選擇介面。預覽絕不動全域 Cursor。
- **測試項目：** 規格 §34 案例 18、19。
- **風險：** 圖片顯示縮放與 hotspot 像素座標的換算（DPI、Stretch）要一致。

## Phase 9：Global Cursor 套用/恢復

- **目標：** CursorService 系統半邊：`SetSystemCursor` 套用、`SystemParametersInfo(SPI_SETCURSORS)` 恢復；所有退出路徑掛恢復（按鈕、Tray、程式結束、未處理例外、SessionEnding）；Tray 游標選單項啟用。依 Phase 0 Spike B 結論實作。
- **測試項目：** 規格 §34 案例 20~22；手動測正常關閉/Tray Exit/登出。
- **風險：** 本專案最高風險點——實作順序固定為「恢復路徑先寫先測，替換後寫」。

## Phase 10：Single Instance

- **目標：** SingleInstanceService：named Mutex 阻止第二實例 + 跨程序喚醒原視窗（named pipe 或自訂 window message）。
- **測試項目：** 規格 §34 案例 28。
- **風險：** 喚醒需處理視窗在 Tray 隱藏狀態的還原。

## Phase 11：Exception Handling / Log

- **目標：** LogService（`%AppData%\MousePilot\Logs\mousepilot.log`，5MB rotate 保留 3~5 份）；全域未處理例外 handler（記 log、恢復游標、不靜默吞掉）；規格 §21 各失敗情境的統一錯誤呈現（非侵入式）。回頭把前面各 Phase 的錯誤點接上 log。
- **測試項目：** 規格 §34 案例 31；rotate 單元測試。
- **風險：** 例外 handler 內再拋例外——handler 必須自身 try/catch 到底。
- **移交事項（Phase 2 ledger）：** (a) `MonitorStatus` 需擴充 `Error` 值 + XAML 紅色狀態點（規格 §28）；(b) 全域 handler 必須涵蓋 Dispatcher 例外（DispatcherTimer 事件路徑目前無防護）。
- **移交事項（Phase 3 ledger）：** (c) `MouseMovementService.ExecuteMoveAsync` 回傳 bare bool，無法區分「取消/Win32 失敗/保守放棄」——接 log 時需改 reason enum 或注入 log callback（簽章變更要規劃，不要現場發現）；(d) VM `_moving` 防重入的靜默丟棄應記 log。
- **移交事項（Phase 4 final review）：** (e) 未處理例外 crash 會跳過 OnExit → ghost tray icon + 設定未保存；預設 tray-only 模式下例外風險升高——全域 handler 必須包含 Tray.Dispose 與 SaveSettings（連同既有的恢復游標要求）。

## Phase 12：Publish + 文件交付

- **目標：** publish 產出單一 EXE 並在乾淨環境驗證；README、使用說明、FAQ、Debug 方法、測試項目文件；跑完規格 §34 全部 31 個測試案例並記錄結果。
- **驗收：** 規格 §39——EXE 複製到無 .NET 8 Runtime 的 Win10/11 x64 機器可直接執行。
- **風險：** ReadyToRun/SingleFile 與 WPF 資源、embed cursor 資源的相容性；若需調整 csproj 參數要寫明原因。

---

## 全域約束（每個 Phase 都適用）

- .NET 8 / WPF / win-x64；不用 Electron、不加大型框架；相依僅允許 CommunityToolkit.Mvvm + xUnit（測試）。
- 不需要 Administrator；Registry 只碰 HKCU。
- 執行期資料一律 `%AppData%\MousePilot\`；EXE 目錄不寫入。
- 常態 CPU 接近 0%：Timer 500~1000ms，禁止 busy loop / 高頻 hook。
- 所有 unmanaged resource 正確 Dispose。
- 一般錯誤不得讓程式退出。
- 設計衝突時依規格 §40 優先順序取捨。
- 第一版排除清單見規格 §35。
