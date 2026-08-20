# MousePilot Windows 桌面工具 — 需求規格

> 本文件為使用者於 2026-08-20 提供的完整需求規格，是所有實作計畫的依據。

專案名稱：**MousePilot**

主要用途：解決部分 Windows 筆電在短時間沒有鍵盤或滑鼠操作後，就進入螢幕保護、顯示器待機或被判定為閒置的問題。

優先考量：

* 穩定性
* 低 CPU 使用率
* 低記憶體使用量
* Windows 原生整合能力
* 不需要複雜安裝
* 最終可產生單一 EXE
* 程式架構容易維護
* UI 操作直覺

---

## 一、技術方案

優先採用：

* C#
* .NET 8
* WPF
* Win32 API / PInvoke
* Windows 10
* Windows 11
* x64

不要使用 Electron。若沒有重大技術限制，不要改用其他大型跨平台框架。

最終發布方式：

* Release
* win-x64
* Self-contained
* PublishSingleFile
* 不要求使用者額外安裝 .NET Runtime

主要輸出：`MousePilot.exe`

---

## 二、核心功能

### 1. 閒置偵測

程式需要偵測：滑鼠操作、鍵盤操作。

只有當「滑鼠與鍵盤都沒有使用者操作」超過設定時間後，才進入自動模擬模式。

建議使用 Windows 原生 API：`GetLastInputInfo`。

不要使用高頻率 Keyboard Hook 或 Mouse Hook 來單純計算閒置時間，避免不必要的資源消耗。

UI 顯示：

* 目前閒置秒數
* 距離啟動剩餘時間
* 目前狀態

狀態至少包含：監控中、使用者活動中、等待啟動、自動移動中、已暫停。

---

## 三、閒置時間設定

分成兩個設定。

### 開始閒置時間

例如 `120 秒`：使用者連續 120 秒沒有鍵盤及滑鼠操作後，開始執行 MousePilot。必須可以自由設定。建議設定範圍：`5 ～ 86400 秒`。

### 後續移動間隔

例如 `30 秒`：第一次觸發後，每隔 30 秒執行一次滑鼠微移。必須可以自由設定。建議設定範圍：`1 ～ 86400 秒`。

---

## 四、滑鼠移動

支援以下模式（透過 UI 下拉選單切換）：

1. 左右移動
2. 上下移動
3. 隨機方向

### 移動像素

使用者可以設定 `1 ～ 100 px`，例如 `3 px`。

### 左右模式

目前位置 `X=500 Y=300` → 第一次 `503,300` → 下一次 `497,300`，或使用往返方式執行。

### 上下模式

相同概念：`500,303` 以及 `500,297`。

### 隨機模式

每次可以隨機選擇：上、下、左、右、左上、右上、左下、右下。移動距離仍由「移動像素」設定控制。

---

## 五、移動後回到原位置

增加 `移動後回到原位置` Checkbox。

例如設定為開啟：滑鼠原位置 `500,300` → 執行 `503,300` → 等待約 `100 ～ 500 ms` → 返回 `500,300`。

等待時間不必提供複雜設定，可以使用合理預設值。

這個功能目的是避免滑鼠長時間自動漂移到螢幕邊界。

---

## 六、使用者重新操作（非常重要）

如果 MousePilot 正在自動移動，但偵測到使用者：移動滑鼠、按鍵盤、點擊滑鼠、滾動滑鼠，則：

* 立即停止 MousePilot 當前的自動移動流程
* 重新開始計算「開始閒置時間」

不能因程式自己產生的模擬滑鼠事件，而錯誤判定為使用者重新操作。

請在架構設計時特別處理「程式產生的 Input」與「真實使用者 Input」之間的差異。

如果單純使用 GetLastInputInfo 會受到 SendInput 影響，請設計可靠的方法避免自己的自動移動造成 Idle Timer 被錯誤重置。

---

## 七、自訂滑鼠 Cursor

MousePilot 需要支援更換 Windows 滑鼠游標。

注意：不是讓滑鼠依照圖片軌跡移動，而是**把滑鼠指標本身換成自訂圖片**。

支援：PNG、JPG、JPEG、BMP、CUR、ANI。

---

## 八、圖片轉 Cursor

如果使用者匯入 PNG / JPG / BMP，MousePilot 要能將圖片轉換成 Windows 可以使用的 Cursor。

建議處理：

* 保留透明背景
* 自動縮放
* 可以選擇尺寸

建議至少提供：16×16、24×24、32×32、48×48、64×64、128×128。預設 `32×32`。

如果圖片比例不同，保持原比例，不要強制拉伸。

---

## 九、Cursor Hotspot

圖片匯入後，需要進入「游標預覽 / 編輯」介面。

顯示：圖片、Cursor 預覽、座標格、Hotspot。

使用者可以直接在圖片上**點一下**指定 Hotspot，例如 `Hotspot X=4 Y=3`。

Hotspot 代表實際滑鼠點擊位置。UI 中用明顯標記（`+` 或十字準心）表示，旁邊顯示 `Hotspot：4,3`。也允許手動輸入 X / Y。

---

## 十、Cursor 模擬預覽

在套用全域 Cursor 之前，提供模擬區域（Preview Panel），顯示：

* 自訂 Cursor
* Hotspot
* 模擬按鈕、模擬文字、模擬 Link、模擬 Checkbox

使用者可以測試 Cursor 大小、外觀、Hotspot 是否正確。

預覽期間不要修改 Windows 全域 Cursor。

---

## 十一、全域套用 Cursor

當使用者按 `套用`，MousePilot 可以暫時修改 Windows 全域滑鼠 Cursor。

必須注意安全性：不要永久破壞 Windows 原本 Cursor 設定。程式需要在啟動自訂 Cursor 前保存原始 Cursor 狀態。

以下情況必須恢復原本 Cursor：

* 使用者按「恢復預設游標」
* 關閉自訂 Cursor 功能
* 結束 MousePilot
* 正常 Shutdown
* 可處理的程式例外
* Tray 選單選擇恢復

如果使用 `SetSystemCursor`，請仔細處理其行為。必要時使用 `SystemParametersInfo` 重新載入 Windows Cursor Scheme。

不得把 Windows 使用者原本的 Cursor 設定永久覆蓋掉。

---

## 十二、Dashboard

程式採用完整 Dashboard UI。主畫面至少分成：

### 狀態區

顯示：MousePilot 執行中/暫停、Windows Idle Time、距離第一次觸發、距離下一次移動、滑鼠目前座標、Cursor 狀態。

### 自動移動設定

* 開始閒置：`[120] 秒`
* 後續移動：`[30] 秒`
* 移動像素：`[3] px`
* 移動模式：`左右 / 上下 / 隨機`
* Checkbox：`移動後回原位置`

### Cursor 設定

* `匯入 Cursor / 圖片`
* 顯示：檔案名稱、尺寸、Hotspot、Preview
* 按鈕：預覽、套用、恢復 Windows Cursor、移除圖片

### 系統設定

* Checkbox：`Windows 開機時自動啟動`
* Checkbox：`開機後最小化至系統匣`
* Checkbox：`啟動程式後自動開始監控`
* Shortcut 設定

---

## 十三、系統匣

程式必須支援縮小到 Windows Notification Area / System Tray。

關閉主視窗時，預設不要直接結束程式，而是縮小至系統匣。可以在設定中決定是否真的關閉。

Tray Icon 右鍵選單：

* 開啟 MousePilot
* 啟動
* 暫停
* 立即執行一次
* 啟用自訂游標
* 停用自訂游標
* 恢復 Windows 游標
* 設定
* 結束

雙擊 Tray Icon：開啟 Dashboard。

---

## 十四、立即執行一次

Tray Menu 與 Dashboard 都增加 `立即執行一次` 功能。

按下後不需要等待 Idle Timer，立即按照目前設定（移動模式、Pixels、回原位置）執行一次。主要用於測試設定。

---

## 十五、Windows 開機自動啟動

程式內提供設定 `Windows 開機時自動啟動`。

實作建議：Registry `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`。

不要使用 HKLM，避免要求 Administrator。

使用者關閉開機啟動：移除對應 Registry Value。

---

## 十六、開機顯示方式

提供設定 `啟動後最小化到系統匣`，支援兩種模式：

* 模式一：開機後顯示 Dashboard
* 模式二：開機後完全不顯示主視窗，直接進 System Tray

預設：**直接最小化到 System Tray**。

---

## 十七、Global Hotkey

支援 Windows Global Hotkey，建議使用 `RegisterHotKey`。

* 預設 `Ctrl + Alt + F9`：**啟動 / 暫停 MousePilot**
* 預設 `Ctrl + Alt + F10`：**恢復 Windows 原始 Cursor**

UI 要可以修改快捷鍵。需要檢查：Hotkey 是否已被其他程式占用、無效組合、重複設定。

如果註冊失敗：在 UI 顯示清楚錯誤。

---

## 十八、設定保存

設定存放：`%AppData%\MousePilot\settings.json`

```json
{
  "idleStartSeconds": 120,
  "movementIntervalSeconds": 30,
  "movementPixels": 3,
  "movementMode": "Random",
  "returnToOriginalPosition": true,
  "runAtStartup": true,
  "startMinimized": true,
  "autoStartMonitoring": true,
  "customCursorEnabled": false,
  "cursorFile": "",
  "cursorHotspotX": 0,
  "cursorHotspotY": 0,
  "toggleHotkey": "Ctrl+Alt+F9",
  "restoreCursorHotkey": "Ctrl+Alt+F10"
}
```

如果資料夾不存在：自動建立。

如果 settings.json 損毀：不要 Crash，改成：

1. 備份損壞設定
2. 載入預設值
3. 顯示非侵入式提示

---

## 十九、資源管理

此程式會長時間背景執行，因此非常重視資源消耗。

不要使用：Busy Loop、每 1ms Polling、無限制 Timer、不必要高頻 Mouse/Keyboard Hook。

建議 Idle Detection Timer：`500 ms ～ 1000 ms` 即可。

常態背景執行時 CPU 應幾乎接近 0%。避免記憶體持續增加。

所有 Timer / Handle / Hook / Icon / Cursor 等 unmanaged resource 都需要正確 Dispose。

---

## 二十、單一實例

MousePilot 不允許同時啟動多個 Instance（例如使用 `Mutex`）。

如果使用者第二次執行 `MousePilot.exe`：不要啟動第二份，而是通知原本的 MousePilot 開啟 Dashboard。

如果實作跨程序喚醒成本過高，至少需要：阻止第二個 Instance、顯示「MousePilot 已經執行」。但優先實作：第二次啟動時把原本視窗叫出來。

---

## 二十一、異常處理

需要完整處理：

* Cursor 圖片不存在 / 損毀
* CUR / ANI 載入失敗
* Registry 無法寫入
* Global Hotkey 被占用
* 設定檔損毀
* Windows API Call 失敗
* Mouse Coordinate 超過螢幕範圍
* 多螢幕環境
* DPI Scaling
* Explorer 重啟造成 Tray Icon 消失

程式不能因為一般錯誤直接退出。

---

## 二十二、多螢幕支援

需要支援：單螢幕、雙螢幕、三螢幕以上、左側負座標螢幕、上方負座標螢幕。

滑鼠移動不得假設 `X >= 0` 或 `Y >= 0`。請依照 Windows Virtual Screen Bounds 處理。

---

## 二十三、螢幕邊界

如果目前 Cursor 已經在螢幕最右側：不要繼續向右造成無效移動，例如自動選擇反方向。

隨機模式也要確認目的座標合法。

---

## 二十四、使用者輸入優先

MousePilot 的最高優先原則：**不能干擾正在使用電腦的人**。

如果偵測到真實使用者操作，立即停止：Mouse Move、Return Move、Scheduled Action，重新進入 Idle Detection。

使用 CancellationToken 或類似架構，確保正在等待返回原位置時也能立刻取消。

---

## 二十五、架構建議

避免把所有程式碼寫進 MainWindow.xaml.cs。建議拆分至少：

```text
MousePilot
│
├─ Models
│  └─ AppSettings.cs
│
├─ Services
│  ├─ IdleDetectionService.cs
│  ├─ MouseMovementService.cs
│  ├─ CursorService.cs
│  ├─ StartupService.cs
│  ├─ HotkeyService.cs
│  ├─ SettingsService.cs
│  ├─ TrayIconService.cs
│  └─ SingleInstanceService.cs
│
├─ ViewModels
│  └─ MainViewModel.cs
│
├─ Views
│  ├─ MainWindow.xaml
│  └─ CursorEditorWindow.xaml
│
├─ Native
│  └─ NativeMethods.cs
│
├─ App.xaml
├─ App.xaml.cs
└─ MousePilot.csproj
```

可依需求改善架構。

---

## 二十六、設計模式

WPF 建議採用 MVVM。可以使用 `CommunityToolkit.Mvvm`。

如果會增加太多相依，也可以自己實作輕量 ObservableObject / ICommand。

請以簡潔、好維護為優先。

---

## 二十七、UI 設計

UI 希望：Windows 11 現代化、簡潔、不浮誇、Dashboard、Card Layout、清楚的狀態顯示。

首頁最重要的是 `啟動 MousePilot` 以及 `暫停 MousePilot`，不要讓使用者需要很多步驟才能啟用。

---

## 二十八、狀態顏色

* 執行中：綠色狀態點
* 等待中：黃色
* 暫停：灰色
* 錯誤：紅色

不要過度使用動畫。

---

## 二十九、Log

建立簡單 Log。路徑：`%AppData%\MousePilot\Logs\`，例如 `mousepilot.log`。

記錄：程式啟動、程式結束、設定載入錯誤、Cursor 套用錯誤、Registry 錯誤、Global Hotkey 錯誤、未處理例外。

不要每次 Mouse Move 都大量寫 Log。避免 Log 無限增長。

加入簡單 Rotate：最大 `5 MB`、保留 `3 ～ 5 份`。

---

## 三十、安全結束

使用者從 Tray 選擇 `結束`，流程：

1. 停止 MousePilot
2. Cancel 所有 Background Task
3. 停止 Timer
4. Unregister Global Hotkey
5. 恢復 Windows 原始 Cursor
6. Dispose Tray Icon
7. 保存設定
8. Release Mutex
9. 關閉程式

---

## 三十一、發布設定

csproj：

```xml
<PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>

    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>

    <PublishReadyToRun>true</PublishReadyToRun>
</PropertyGroup>
```

若某個參數會造成 WPF / Cursor / Native DLL 執行問題，可以調整。

Publish Command：

```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

最終主要使用 `MousePilot.exe`。

---

## 三十二、Windows 版本

最低支援：Windows 10 64-bit。同時支援：Windows 11 64-bit。

目前不需要：macOS、Linux、Windows ARM。

---

## 三十三、不使用 Installer 為主要方案

第一版以 **Portable EXE** 為主。使用者取得 `MousePilot.exe` 即可執行。

程式自己的 settings / logs / cursor cache 放到 `%AppData%\MousePilot\`，因此 EXE 所在目錄不需要寫入權限。

未來可以再增加 MSIX / Setup，但目前不是必要項目。

---

## 三十四、測試案例

### Idle

1. 使用者持續操作鍵盤，不觸發。
2. 使用者持續移動滑鼠，不觸發。
3. 滑鼠鍵盤都停止後才觸發。
4. Idle 期間重新操作後取消。
5. 自動移動後等待下一次間隔。

### Mouse Movement

6. 左右模式正常。
7. 上下模式正常。
8. Random 正常。
9. Return Position 正常。
10. 螢幕邊界正常。
11. 多螢幕正常。
12. 負座標正常。

### Cursor

13. PNG 匯入。
14. JPG 匯入。
15. BMP 匯入。
16. CUR 匯入。
17. ANI 匯入。
18. Hotspot 正常。
19. Preview 正常。
20. Global Cursor 正常。
21. Restore Cursor 正常。
22. 關閉程式後 Cursor 正常恢復。

### Windows Integration

23. Registry Startup 正常。
24. 關閉 Startup 後 Registry 清除。
25. Tray 正常。
26. Global Hotkey 正常。
27. Hotkey 衝突正常處理。
28. Single Instance 正常。

### Configuration

29. 設定保存。
30. 重開程式設定保留。
31. settings.json 損毀不 Crash。

---

## 三十五、第一版不需要的功能

避免 Scope Creep。目前不要加入：

帳號系統、Cloud Sync、資料庫、Web Server、API Server、自動更新系統、遙端控制、Telegram、Discord、Web Dashboard、Electron、Docker、AI 功能。

MousePilot 是純 Windows Local Utility。

---

## 三十六、重要技術驗證

正式實作前，先驗證以下兩個高風險技術點：

### A. 使用者 Idle 與模擬 Mouse Event

確認：MousePilot 自己的模擬滑鼠輸入是否會影響 GetLastInputInfo。

如果會：請設計額外狀態或 Input Detection 機制，確保 MousePilot 自己產生的輸入不會被視為使用者操作。

### B. Windows Global Cursor

確認：自訂 Cursor 的替換、保存與恢復機制。

尤其測試：正常關閉、Tray Exit、Crash Handler、Windows 登出、Windows 關機、Cursor Scheme。

原則：**永遠優先確保能恢復使用者原本的 Windows Cursor。**

---

## 三十七、開發方式

不要一次性產生大量不可驗證的程式碼。採用階段式開發：

* Phase 1：建立專案（WPF、MVVM、Settings、Dashboard），可以 Build。
* Phase 2：Idle Detection，完成測試。
* Phase 3：Mouse Movement（Horizontal、Vertical、Random、Return Position）。
* Phase 4：Tray。
* Phase 5：Startup Registry。
* Phase 6：Global Hotkey。
* Phase 7：Cursor Import。
* Phase 8：Hotspot Editor。
* Phase 9：Global Cursor。
* Phase 10：Single Instance。
* Phase 11：Exception Handling / Log。
* Phase 12：Publish Single EXE。

每個 Phase 先確認 Build 成功。不要在前一階段有 Compiler Error 時繼續加入大量新功能。

---

## 三十八、最終交付內容

1. 完整 Source Code
2. 完整資料夾架構
3. `.csproj`
4. 所有 XAML
5. 所有 C# Class
6. Windows API PInvoke
7. Build 指令
8. Publish 指令
9. Debug 方法
10. README
11. 使用說明
12. 常見問題
13. 測試項目
14. EXE 產出位置

---

## 三十九、驗收標準

執行：

```powershell
dotnet restore
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

並在 `bin\Release\net8.0-windows\win-x64\publish\` 取得 `MousePilot.exe`。

將這個 EXE 複製到另一台沒有安裝 .NET 8 Runtime 的 Windows 10 / Windows 11 x64 電腦時，應該可以直接執行。

---

## 四十、最終核心原則

所有設計決策優先順序：

1. 不干擾使用者正常操作
2. 不破壞 Windows 原始 Cursor
3. 穩定長時間執行
4. 低 CPU / RAM
5. 不需要 Administrator
6. Portable Single EXE
7. UI 簡單
8. 程式碼容易維護

如果需求之間存在技術衝突：優先選擇安全且穩定的實作方式，並清楚說明原因。

---

# 補充需求：內建游標圖案

MousePilot 除了支援使用者自行匯入圖片外，也需要提供一組「預設游標圖案庫」，讓使用者第一次啟動後不需要另外準備圖片即可立即測試自訂游標功能。

## 補一、預設游標分類

### 基本類

Windows 標準箭頭風格、圓點、十字準心、手指、愛心、星星、閃電、火焰。

### 可愛類

貓咪、狗狗、熊熊、兔子、青蛙、小幽靈、藍色機器貓風格、卡通機器人。

「藍色機器貓風格」可作為不直接使用受版權保護角色素材時的預設替代方案。

## 補二、哆啦A夢圖片支援

MousePilot 必須可以支援哆啦A夢圖片作為自訂滑鼠游標（使用者自行匯入 PNG/JPG/JPEG/BMP/CUR/ANI，例如 `doraemon.png`）。

匯入後可以：自動裁切透明區域、自動縮放、選擇 Cursor 尺寸、設定 Hotspot、預覽、套用為 Windows 全域 Cursor。

若專案擁有合法授權的哆啦A夢圖像素材，可直接作為內建 Cursor Resource；**若沒有授權，不要從網路自動下載或將第三方哆啦A夢圖片直接打包進公開發行版本**。

## 補三、預設圖案選擇介面

Cursor 設定區新增「預設圖案」，以 Grid / Card 形式顯示。點擊後立即在右側 Preview Panel 顯示效果（不直接修改 Windows 全域 Cursor）。只有按「套用」才真正變更游標。

## 補四、圖片預覽

預覽區需要模擬：白色背景、深色背景、按鈕、Checkbox、Hyperlink、文字區域。

預覽區還要顯示：Cursor 原始尺寸、實際使用尺寸、Hotspot X、Hotspot Y。

## 補五、Cursor 尺寸

預設提供：16、24、32、48、64、96、128。預設值 **32×32**。

使用卡通人物或哆啦A夢類型圖片時，建議預設 **48×48**，避免縮得太小而看不清楚。

## 補六、圖片自動處理

PNG 優先保留 Alpha Channel / 透明背景。

JPG 沒有透明背景，提供「移除背景」選項。第一版使用簡單方式：指定背景色為透明、使用左上角像素當背景參考色、容差值設定。不需要第一版就加入 AI 去背。

## 補七、Hotspot 預設建議

* 箭頭：左上角
* 圓點 / 愛心 / 星星 / 人物卡通圖案：中心

使用者仍可在 Hotspot Editor 裡自行修改。

## 補八、收藏功能

預設圖案及使用者匯入圖案都可以加入「我的收藏」，快速切換常用 Cursor。

資料存放：`%AppData%\MousePilot\Cursors\`；設定資訊存放：`%AppData%\MousePilot\settings.json`，例如：

```json
{
  "cursorPreset": "CuteRobotCat",
  "cursorSize": 48,
  "cursorHotspotX": 24,
  "cursorHotspotY": 24
}
```

## 補九、圖片資源策略

程式內建圖片必須直接 Embed 到 MousePilot Assembly Resource，維持單一 `MousePilot.exe`。

不要因預設 Cursor 圖案而要求使用者另外攜帶 `images\`、`cursors\`、`assets\` 等資料夾。

只有使用者自行匯入的圖片才複製到 `%AppData%\MousePilot\Cursors\`。

## 補十、最終使用流程

```text
開啟 MousePilot → Cursor 設定 → 選擇預設圖案 → 選擇大小 → 調整 Hotspot → 模擬預覽 → 套用 → Windows 全域 Cursor 更換
```

或：

```text
Cursor 設定 → 自訂匯入 → 選擇 doraemon.png → 自動縮放 / 透明處理 → Hotspot 設定 → 預覽 → 套用
```

整個流程必須簡單直覺，讓一般 Windows 使用者不需要理解 CUR、Hotspot 或 Windows API 即可完成操作。
