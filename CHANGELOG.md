# Changelog

本專案遵循 [Semantic Versioning](https://semver.org/lang/zh-TW/)。版本號唯一來源為 `VERSION` 檔。

## [Unreleased]

### 文件
- 文件同步 v1.0.1 變更：debugging.md 新增 §1.1a Bootstrap Log 說明（位置/里程碑/閃退排查）與 publish 產物、smoke test 驗證說明；FAQ 與線上手冊新增「打不開/閃退」排查條目（提醒升級 v1.0.1、附 bootstrap log 回報）；user-guide 資料位置表補 `mousepilot-bootstrap.log`；README Log 條目補啟動診斷；發布驗證清單前置補 `*_cor3.dll` 檢查與獨立目錄啟動驗證步驟。

## [1.0.1] - 2026-08-25

### 修正
- 單一 EXE 在其他電腦啟動即 `DllNotFoundException` 閃退：WPF Native DLL（`*_cor3.dll`）先前未打進單檔，Release 只發佈 EXE 導致缺檔（dotnet/runtime#61279）。csproj 加入 `IncludeNativeLibrariesForSelfExtract=true`，並暫時關閉 `PublishReadyToRun` 排除相容性變因（重新啟用前須先通過獨立目錄啟動測試）。
- `LogService.Error` 只記例外型別與 Message：改記 `ex.ToString()`，完整保留堆疊與 inner exception。

### 新增
- Bootstrap log（`BootstrapLog`）：極早期開機紀錄改寫至 `%AppData%\MousePilot\Logs\mousepilot-bootstrap.log`（不可用時退回 `%TEMP%`），記錄 App 建構子／OnStartup／服務初始化／主視窗／系統匣各階段；成功啟動後於正式 log 記錄 bootstrap log 位置；寫入失敗一律靜默，超過 512KB 重寫防 crash loop 無限成長。
- Publish smoke test（`tools/publish-smoke-test.ps1`）：驗證 publish 目錄無 `*_cor3.dll` 殘留、只複製 MousePilot.exe 至全新目錄可啟動、log 出現「程式啟動」成功標記才算通過；Release workflow 於發佈前強制執行，未通過不得發佈。
- 單元測試：`LogService.Error` 完整堆疊、BootstrapLog 主/備援路徑與靜默失敗（新增 5 項，總數 271→276）。

## [1.0.0] - 2026-08-24

### 新增
- 原創 LOGO（程式繪製藍底徽章＋游標軌跡，`tools/LogoGen` 產生）：套用至 EXE 圖示、視窗標題列、系統匣圖示（載入失敗自動退回原綠圓）與 README。
- 簡易使用者手冊 `docs/manual.html`：單檔離線可開的圖文快速上手（三步開始、狀態說明、自訂游標五步、快捷鍵、疑難速查），LOGO 內嵌。
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
- 全域快捷鍵（Phase 6）：Ctrl+Alt+F9 切換啟動/暫停、Ctrl+Alt+F10 恢復游標（佔位，隨自訂游標功能啟用）；UI 點擊欄位按鍵即可修改，無效/重複/被占用皆有提示並還原；Tray 選單啟停加上狀態防護。
- 游標匯入（Phase 7）：支援 PNG/JPG/JPEG/BMP/CUR/ANI 匯入至 %AppData%\MousePilot\Cursors\（透明裁切/等比縮放/JPG 去背/.cur 讀寫/ANI 首格預覽，損毀不 crash）；16 個程式繪製內建圖案（基本 8 + 可愛 8，含藍色機器貓風格）；收藏資料層。
- 游標編輯器（Phase 8）：預設圖案 Grid（16 內建 + 匯入檔案）與我的收藏、尺寸選擇、點擊設 Hotspot（十字標記/座標格/手動輸入）、JPG 去背（左上角參考色 + 容差）、雙背景模擬預覽（面板局部游標，不動全域）；移除失敗防孤兒檔案。
- 全域游標套用/恢復（Phase 9）：SetSystemCursor 套用（僅標準箭頭）＋ SPI_SETCURSORS 恢復；恢復掛所有退出路徑（按鈕/F10/Tray/關閉/未處理例外/登出/crash 後啟動補救/--restore-cursor 參數）；「確定」落地 confirmed 游標檔（WYSIWYG）；Tray 游標選單項與主視窗 套用/恢復 按鈕啟用。
- 單一實例（Phase 10）：named Mutex 阻止多開；第二次啟動自動喚醒原實例開啟 Dashboard（Tray 隱藏狀態亦可）；crash 後 abandoned mutex 自動接手；結束流程補上釋放 Mutex（§30 步驟 8）。
- 例外處理與 Log（Phase 11）：LogService（5MB 輪替、保留 3 份歸檔、失敗靜默）；未處理例外統一 EmergencyShutdown（記 log→恢復游標→存設定→清 Tray，不吞例外）；滑鼠移動連續失敗紅色錯誤狀態；快捷鍵錯誤碼區分與等價組合查重；數值夾制後 UI 即時刷新；tray-only 狀態通知改 balloon 顯示；marker write-ahead 與單一實例 fail-open 強化。
