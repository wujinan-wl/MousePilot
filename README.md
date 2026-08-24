<p align="center">
  <img src="Assets/logo.png" width="112" alt="MousePilot LOGO"/>
</p>

# MousePilot

Windows 桌面小工具：閒置一段時間後自動微移滑鼠，避免筆電進入螢幕保護／待機；同時支援自訂 Windows 滑鼠游標圖案（匯入圖片、Hotspot 編輯、預覽、全域套用與安全恢復）。純本機執行、Portable 單一 EXE，不需要安裝、不需要系統管理員權限。

## 功能特色

- **閒置偵測自動微移**：`GetLastInputInfo` 輪詢閒置時間，達到門檻後自動微移滑鼠，避免螢幕保護／待機。
- **三種移動模式**：左右移動、上下移動、隨機方向，移動像素與間隔皆可調整。
- **回到原位置**：微移後可自動移回原座標，真實使用者操作時立即取消，不會硬拉滑鼠。
- **系統匣（Tray）**：預設縮到系統匣執行，右鍵選單可控制啟動/暫停/立即執行一次/游標/結束。
- **開機自動啟動**：寫入 `HKCU\...\Run`（僅目前使用者，不需要系統管理員權限）。
- **全域快捷鍵**：預設 `Ctrl+Alt+F9`（啟動/暫停）、`Ctrl+Alt+F10`（恢復 Windows 游標），可自訂錄製。
- **自訂游標匯入與編輯**：支援 PNG/JPG/JPEG/BMP/CUR/ANI 匯入、Hotspot 點擊編輯、JPG 去背、模擬預覽、全域套用與安全恢復（`SPI_SETCURSORS`，crash/登出/關機皆會恢復）。
- **單一實例**：per-user 只允許一份執行，第二次啟動會喚醒原視窗；不同 Windows 使用者可各自執行一份。
- **Log 紀錄**：關鍵事件寫入 `%AppData%\MousePilot\Logs\mousepilot.log`，5MB 輪替、保留 3 份歸檔。

## 系統需求

- Windows 10 64-bit 或 Windows 11 64-bit。
- 免安裝、免系統管理員權限。

## 下載與執行

從 [GitHub Releases](https://github.com/wujinan-wl/MousePilot/releases) 下載 `MousePilot.exe`，直接雙擊執行即可，不需要安裝、不需要系統管理員權限。

執行期資料（設定、Log、匯入的游標圖片）一律寫入 `%AppData%\MousePilot\`，`MousePilot.exe` 所在目錄本身不需要寫入權限，可放在隨身碟等唯讀位置執行。

## 從原始碼建置

```powershell
dotnet restore
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

Publish 產出的單一可執行檔位置：

```
bin\Release\net8.0-windows\win-x64\publish\MousePilot.exe
```

需要 .NET 8 SDK。技術棧為 C# / .NET 8 / WPF / MVVM（CommunityToolkit.Mvvm）/ Win32 PInvoke。

## 文件

- [使用說明](docs/user-guide.md) — Dashboard 導覽、閒置偵測設定、自訂游標完整流程、系統匣、開機自啟、全域快捷鍵等。
- [常見問題（FAQ）](docs/faq.md)
- [Debug 方法](docs/debugging.md) — Log 位置與輪替、`--restore-cursor` 緊急恢復參數、`settings.json` 手動編輯注意事項、原始碼 build/debug。
- [CHANGELOG](CHANGELOG.md)
- [發布驗證清單](docs/testing/release-checklist.md) — §34 全 31 案例 + 歷代補充驗證項目，release 前逐項手動驗證用。
