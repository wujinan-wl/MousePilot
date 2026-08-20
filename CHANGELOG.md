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
