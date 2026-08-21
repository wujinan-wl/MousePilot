# Changelog

本專案遵循 [Semantic Versioning](https://semver.org/lang/zh-TW/)。版本號唯一來源為 `VERSION` 檔。

## [Unreleased]

### 新增
- 專案文件：CLAUDE.md、需求規格、總體計畫、Phase 0+1 細部計畫。
- Git 基礎設定：remote（github.com/wujinan-wl/MousePilot）、.gitignore、Release workflow（推送 v* tag 自動建置並發佈附 MousePilot.exe 的 GitHub Release）。
- Spike A/B：SendInput 對 GetLastInputInfo 影響、SetSystemCursor 恢復機制實測結論（docs/superpowers/research/）。
- WPF 專案骨架（.NET 8 / MVVM / CommunityToolkit.Mvvm），可 build 並 publish 為單一 EXE。
- AppSettings 模型與範圍夾制、SettingsService（settings.json 載入/保存/損毀備份回復）。
- Dashboard 卡片版面殼：狀態區、自動移動設定、游標佔位、系統設定。
- xUnit 測試專案與 Phase 1 單元測試。
- 閒置偵測（Phase 2）：GetLastInputInfo + 500ms 輪詢狀態機、五種狀態與顏色點、閒置/倒數/座標/觸發次數即時顯示、啟動/暫停與「啟動後自動開始監控」生效。
- 模擬輸入抑制窗 API（Suppress）：依 Spike A 結論，供 Phase 3 滑鼠移動時避免誤判為使用者操作。
- 滑鼠自動微移（Phase 3）：左右/上下/隨機三模式、移動像素、回原位置（300ms）、螢幕邊界反向與多螢幕負座標支援；「立即執行一次」按鈕。
- 使用者輸入立即取消：真實輸入取消進行中移動；返回前雙重輸入檢查（鍵盤 lastInput 基準 + 游標位置）。
- 系統匣（Phase 4）：tray icon 與右鍵選單（開啟/啟動/暫停/立即執行一次/游標佔位三項/設定/結束）、雙擊開 Dashboard、關閉視窗縮到系統匣（可設定）、啟動後最小化到系統匣（預設）、安全結束流程。
- 開機自動啟動（Phase 5）：checkbox 寫入/移除 HKCU Run key（引號包覆 EXE 路徑）、啟動時與 Registry 實際狀態同步並自我修復移動後的路徑、寫入失敗不 crash（提示 + checkbox 還原）。
