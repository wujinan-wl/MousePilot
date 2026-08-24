# MousePilot Debug 方法

本文件面向進階使用者／開發者，說明如何從 Log、設定檔與命令列參數排查 MousePilot 的問題，以及如何從原始碼建置與測試。

## 1. Log 檔

### 1.1 位置與輪替

- 目前執行中的 Log：`%AppData%\MousePilot\Logs\mousepilot.log`
- 歷史歸檔：`mousepilot.1.log` ~ `mousepilot.3.log`（最多保留 **3 份**歸檔，`.1.log` 為最新一份、數字越大越舊）。
- 輪替規則：目前 `mousepilot.log` 超過 **5 MB** 時觸發輪替——把現有歸檔依序往後遞增一個編號（超過保留數量的最舊一份會被刪除），再把目前的 `mousepilot.log` 更名為 `mousepilot.1.log`，程式繼續寫入全新的 `mousepilot.log`。
- Log 寫入本身若失敗（例如檔案被占用、權限不足）會靜默略過，不會讓程式因為記錄失敗而中斷或當掉。

### 1.2 記錄項目

依規格會記錄以下七類事件（不含逐次滑鼠移動，避免 Log 無限增長）：

1. 程式啟動 / 程式結束
2. 設定載入錯誤（`settings.json` 損毀，已改用預設值）
3. Cursor 套用錯誤（`SetSystemCursor` 失敗、寫入 confirmed 游標檔失敗等）
4. Registry 錯誤（開機自啟寫入/移除失敗）
5. Global Hotkey 錯誤（格式無效、占用、Win32 錯誤碼）
6. 未處理例外（Dispatcher / AppDomain 層級的 `EmergencyShutdown`）
7. 其他關鍵事件：滑鼠移動連續失敗達 3 次進入錯誤狀態、Session 結束（登出/關機）恢復游標、單一實例 fail-open 等

### 1.3 讀 log 的方式

每行格式固定為：

```
yyyy-MM-dd HH:mm:ss.fff [LEVEL] 訊息內容
```

例如：

```
2026-08-24 10:15:03.512 [INFO] 程式啟動
2026-08-24 10:42:11.087 [ERROR] 滑鼠移動失敗（Win32 呼叫失敗，連續 3 次）｜Win32Exception: ...
```

- **等級**只有兩種：`INFO`（一般事件，如啟動/結束/成功恢復）與 `ERROR`（失敗事件，如設定損毀、Registry 寫入失敗、快捷鍵占用、未處理例外）。
- 時間戳為本機時間，精確到毫秒，方便比對其他系統事件（例如工作管理員記錄、事件檢視器）發生的先後順序。
- 用純文字編輯器或 `Get-Content -Wait` 之類的工具即可即時觀察；由於不記錄逐次滑鼠移動，檔案成長速度很慢，一般不需要特別處理即可長期保留。

## 2. `--restore-cursor` 緊急恢復參數

用法（命令列或「執行」對話框皆可）：

```
MousePilot.exe --restore-cursor
```

行為特性：

- 立即呼叫 `SPI_SETCURSORS` 重新載入使用者的 Cursor Scheme，把 Windows 游標恢復成使用者原本的設定，然後**直接結束**，不會顯示任何視窗、不會進入正常啟動流程。
- **不受單一實例限制**：即使目前已經有一份 MousePilot 在背景執行，這個指令仍會獨立啟動、執行恢復、結束，不會被原實例攔截或喚醒原視窗，適合在正常程式卡住、UI 沒反應，或需要腳本化緊急處理時使用。
- 因為只會重載 Cursor Scheme（而不是寫回單一游標），這個動作本質上不可能把使用者原本的游標設定變得更糟，可以放心多次執行。

## 3. `settings.json`

### 3.1 位置

`%AppData%\MousePilot\settings.json`（JSON、UTF-8、camelCase 欄位命名）。

### 3.2 手動編輯注意事項

- **數值欄位一律會被夾制**：載入與保存前都會呼叫 `AppSettings.Clamp()`，超出範圍的數字（例如 `idleStartSeconds` 設成負數或超大值）會被自動夾制回合法範圍（`idleStartSeconds` 5~86400、`movementIntervalSeconds` 1~86400、`movementPixels` 1~100、`cursorSize` 需為 16/24/32/48/64/96/128 之一，否則回退為 32）。
- **游標來源互斥**：`cursorPreset` 與 `cursorFile` 為二擇一，若手動編輯讓兩者同時有值，`Clamp()` 會保留 `cursorPreset`、清空 `cursorFile`。
- **Hotspot 會依游標尺寸夾制**：`cursorHotspotX`/`cursorHotspotY` 會被夾制在 `0` 到「`cursorSize` - 1」之間。
- **快捷鍵格式嚴格區分大小寫**：`toggleHotkey`/`restoreCursorHotkey` 必須符合 `Ctrl+Alt+F9` 這種格式（修飾鍵 `Ctrl`/`Alt`/`Shift`/`Win` 字首大寫、其餘小寫，主鍵 `F1`~`F24`/`A`~`Z`/`0`~`9`，以 `+` 連接）。若手動改成小寫（如 `ctrl+alt+f9`）或其他不符合格式的字串，程式**不會**自動修正，該快捷鍵會直接視為無效並停用，啟動時 Log 會記一筆 `ERROR`，Dashboard 也會出現提示。

### 3.3 損毀時的行為

若 `settings.json` 內容無法解析（JSON 格式錯誤、反序列化失敗、讀取時發生 IO/權限例外），程式會：

1. 把損毀的原始檔案備份為 `settings.json.corrupt-yyyyMMdd-HHmmss.bak`（同目錄下，時間戳精確到秒，避免覆蓋歷史備份）。
2. 改用程式內建預設值繼續啟動（不會讓程式因此無法啟動或當掉）。
3. 在 Dashboard 顯示非侵入式提示，並在 Log 記一筆 `ERROR: 設定檔損毀，已載入預設值`。

若備份動作本身也失敗（例如權限問題），仍會繼續套用預設值啟動，只是 Dashboard 提示文字會省略備份路徑。

## 4. `cursor-applied.marker`

位置：`%AppData%\MousePilot\cursor-applied.marker`（內容為空、僅作存在性標記）。

用途：write-ahead 標記——套用自訂游標前先寫入這個檔案，成功恢復（`SPI_SETCURSORS` 重載成功）後才刪除。若程式在「已套用、尚未恢復」的狀態下異常結束（crash、強制關閉工作管理員結束程序等），這個檔案會殘留下來；下次啟動 MousePilot 時，只要偵測到這個檔案存在，會先自動執行一次恢復，補救上次未完成的清理，再繼續正常啟動流程。若需要手動排查游標卡住的問題，可以檢查這個檔案是否存在，作為「上次是否正常結束」的線索。

## 5. 單一實例 fail-open 訊息

MousePilot 使用具名 Mutex 判斷是否已有實例在跑。正常情況下這個機制可靠地保證「同一使用者工作階段只有一份在執行」。但若這個具名 kernel object 因為某些環境因素（例如名稱被其他類型物件占用、ACL 拒絕存取）而無法建立/開啟，程式的設計取捨是**寧可失去單一實例保證也要正常啟動**（fail-open），而不是直接啟動失敗。

這種情況發生時，Log 會記錄一筆：

```
[ERROR] 單一實例 mutex 建立失敗（kernel object 名稱被占用或拒絕），以 fail-open 模式啟動——本次執行失去單一實例保證
```

若懷疑環境中同時跑了多份 MousePilot（例如系統匣出現重複圖示、行為顯得不一致），先檢查 Log 中是否有這則 `ERROR`；若有，代表這次執行環境本身無法保證單一實例，需要從環境面（例如檢查是否有安全軟體或群組原則限制具名核心物件的建立）排查，而非 MousePilot 本身的邏輯錯誤。

## 6. 回報問題時應附帶的資訊

回報 Issue 或尋求協助時，建議附上：

1. `%AppData%\MousePilot\Logs\mousepilot.log`（以及若有異常時段更早的 `mousepilot.1.log` 等歸檔）。
2. Windows 版本（Windows 10 或 11、build 號碼，`winver` 可查）。
3. 具體重現步驟（操作順序、期望結果與實際結果的差異）。
4. 若與游標相關：目前使用的游標來源（內建圖案 / 匯入圖片 / `.cur`/`.ani` 檔）與尺寸設定。
5. 若與快捷鍵相關：目前 `settings.json` 中 `toggleHotkey`/`restoreCursorHotkey` 的實際內容。

## 7. 從原始碼 build / debug

### 7.1 環境需求

.NET 8 SDK（含 WPF 工作負載，Windows 上安裝 .NET SDK 預設即包含）。

### 7.2 常用指令

```powershell
dotnet restore
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

- `dotnet restore`：還原相依套件。
- `dotnet build -c Release`：Release 組態編譯，用來確認沒有 compiler error/warning。
- `dotnet publish ...`：產出單一可執行檔，路徑為：

  ```
  bin\Release\net8.0-windows\win-x64\publish\MousePilot.exe
  ```

### 7.3 測試專案

單元測試位於 `tests\MousePilot.Tests\`（xUnit），執行方式：

```powershell
dotnet test tests\MousePilot.Tests
```

測試涵蓋各 Service（`IdleDetectionService`、`MouseMovementService`、`CursorService`、`SettingsService`、`HotkeyParser`、`StartupService`、`SingleInstanceService`、`CursorImportService`、`CursorImageProcessor` 等）與 ViewModel 的行為，且皆透過建構子注入的方式偽造 Win32/檔案系統相依，不需要實機環境即可執行。

### 7.4 專案結構速查

- `Native\NativeMethods.cs`：所有 Win32 PInvoke 集中於此。
- `Services\`：每個職責一個 Service（閒置偵測、滑鼠移動、游標套用/恢復、開機自啟、全域快捷鍵、設定存取、系統匣、單一實例、Log）。
- `ViewModels\`：`MainViewModel`、`CursorEditorViewModel`（MVVM，畫面邏輯集中於此，View 只做綁定）。
- `Views\`：`MainWindow.xaml`（Dashboard）、`CursorEditorWindow.xaml`（游標編輯器）。

想在本機除錯特定行為（例如閒置狀態機、快捷鍵解析）時，優先在對應的單元測試專案中重現，比直接跑整支 EXE 除錯更快、也更容易鎖定問題範圍。
