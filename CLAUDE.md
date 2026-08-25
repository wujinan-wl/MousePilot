# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

> 個人工作偏好（語言、分階段開發、版本、Git、文件、測試等規範）見本機的 `CLAUDE-preferences.md`（不隨 repo 公開，故不設連結），**必須一併遵守**。本檔只記錄 MousePilot 專案本身的技術規範。

## 專案概述

**MousePilot** 是純本機的 Windows 桌面工具（Portable 單一 EXE），用途：偵測使用者閒置後自動微移滑鼠，防止筆電進入螢幕保護／待機；另支援自訂 Windows 滑鼠游標（含圖片匯入、Hotspot 編輯、預覽、全域套用與安全恢復）。

- 技術棧：**C# / .NET 8 / WPF / MVVM（CommunityToolkit.Mvvm）/ Win32 PInvoke**，目標 Windows 10/11 x64。
- 不用 Electron、不用跨平台框架、不需要 Administrator、不做 Installer（第一版 Portable EXE）。
- 完整需求規格與階段計畫見 `docs/` 下的計畫文件。

## Git 與發布

- Remote：`https://github.com/wujinan-wl/MousePilot.git`（公開 repo；push 與建 tag 由使用者執行，Claude 只做本地 commit）。
- Release：推送 `v*` tag 會觸發 `.github/workflows/release.yml`，於 CI 建置單一 EXE 並自動發佈 GitHub Release（附 `MousePilot.exe`）。
- 發布流程：更新 `VERSION` 與 `CHANGELOG.md` → 本地建 tag `vX.Y.Z` → 使用者執行 `git push origin main --tags`。

## 常用指令

```powershell
dotnet restore
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

- Publish 產出位置：`bin\Release\net8.0-windows\win-x64\publish\MousePilot.exe`
- csproj 關鍵屬性：`net8.0-windows`、`UseWPF`、`RuntimeIdentifier=win-x64`、`SelfContained`、`PublishSingleFile`、`IncludeNativeLibrariesForSelfExtract`（**必須為 true**——否則 WPF Native DLL `*_cor3.dll` 不進單檔，單獨散佈 EXE 會在其他電腦 DllNotFoundException 閃退，dotnet/runtime#61279）、`PublishReadyToRun`（目前 false，排除 R2R 與 WPF 單檔相容性變因；重新啟用前必須先通過 `tools/publish-smoke-test.ps1` 獨立目錄啟動測試）。
- Release 前 CI 會跑 `tools/publish-smoke-test.ps1`：publish 目錄不得殘留 `*_cor3.dll`，且只複製 MousePilot.exe 到全新目錄須能啟動並在 log 留下「程式啟動」標記。

## 架構

```
MousePilot/
├─ Models/        AppSettings 等資料模型
├─ Services/      核心邏輯，每個職責一個 Service：
│                 IdleDetectionService, MouseMovementService, CursorService,
│                 StartupService, HotkeyService, SettingsService,
│                 TrayIconService, SingleInstanceService
├─ ViewModels/    MainViewModel（MVVM，UI 不直接呼叫 Win32）
├─ Views/         MainWindow（Dashboard）, CursorEditorWindow（Hotspot 編輯）
└─ Native/        NativeMethods.cs — 所有 PInvoke 集中在此，不散落各處
```

- 嚴禁把邏輯塞進 `MainWindow.xaml.cs`；View 只做綁定。
- 內建游標圖案 Embed 進 Assembly Resource，維持單一 EXE；使用者匯入的圖片才落地到 `%AppData%\MousePilot\Cursors\`。
- 執行期資料一律放 `%AppData%\MousePilot\`（settings.json、Logs\、Cursors\）；EXE 所在目錄不需寫入權限。

## 關鍵技術約束（違反會產生難察覺的 bug）

### 1. 閒置偵測 vs 自我產生的輸入
- 閒置偵測用 `GetLastInputInfo` + 500~1000ms Timer；**不得**用高頻 Hook 或 Polling 計算閒置。
- `SendInput` 送出的模擬滑鼠事件**會**重置 `GetLastInputInfo`。必須區分「程式自己的輸入」與「真實使用者輸入」（例如：記錄送出模擬輸入的時間窗與預期座標，該窗內符合預期的輸入變化不視為使用者活動）。任何動 idle/movement 邏輯的改動都要重新驗證這點。
- 最高優先原則：**偵測到真實使用者操作，立即取消所有進行中的自動移動（含等待返回原位的延遲）**，用 CancellationToken 貫穿。

### 2. 全域 Cursor 安全性
- `SetSystemCursor` 會破壞性覆蓋系統游標。套用自訂游標**之前**必須保存原始狀態；恢復用 `SystemParametersInfo(SPI_SETCURSORS)` 重載 Windows Cursor Scheme。
- 恢復游標必須掛在所有退出路徑：正常關閉、Tray 結束、未處理例外 handler、Windows 登出/關機（Session Ending）。**永遠不得永久覆蓋使用者原本的 Cursor 設定。**

### 3. 資源與長時間執行
- 長時間背景執行，常態 CPU 需接近 0%：禁止 busy loop、1ms polling、無限制 Timer。
- 所有 unmanaged resource（Timer/Hook/Handle/Icon/Cursor）必須正確 Dispose；Tray 結束流程依固定順序清理（停止→取消 Task→解除 Hotkey→恢復 Cursor→Dispose Tray→存設定→釋放 Mutex）。

### 4. 座標與多螢幕
- 滑鼠座標**不得**假設 `X>=0` / `Y>=0`，一律以 Virtual Screen Bounds 處理（多螢幕、負座標、DPI Scaling）。
- 移動前檢查目標座標合法性；到達螢幕邊界時自動反向，隨機模式也要驗證目的座標。

## 開發紀律

- **分階段開發**（Phase 1~12，見計畫文件）：每個 Phase 先確認 `dotnet build` 成功再進下一階段；有 compiler error 時不得堆新功能。
- 正式實作前必須先驗證兩個高風險點：(A) SendInput 對 GetLastInputInfo 的影響與隔離方案；(B) 全域 Cursor 替換／恢復機制（含 crash 與登出情境）。
- 一般錯誤（圖片損毀、Registry 寫入失敗、Hotkey 被占用、settings.json 損毀等）不得讓程式退出；settings.json 損毀時：備份→載入預設值→非侵入式提示。
- Log 寫入 `%AppData%\MousePilot\Logs\mousepilot.log`，含 rotate（5MB、保留 3~5 份）；不得對每次 mouse move 寫 log。
- 開機自啟只用 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，不碰 HKLM。
- 第一版明確排除：帳號、Cloud Sync、DB、Web/API Server、自動更新、遠端控制、AI 功能（見規格「三十五、第一版不需要的功能」）。

## 設計決策優先順序（需求衝突時依此取捨）

1. 不干擾使用者正常操作
2. 不破壞 Windows 原始 Cursor
3. 穩定長時間執行
4. 低 CPU / RAM
5. 不需要 Administrator
6. Portable Single EXE
7. UI 簡單
8. 程式碼容易維護
