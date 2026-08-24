# MousePilot Phase 12：Publish + 文件交付 實作計畫

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 補齊規格 §38 交付清單 9~13（Debug 方法、README、使用說明、常見問題、測試項目）；產出 §34 全 31 案例 + 歷代 Phase 累積實機驗證的整合勾選清單；VERSION/CHANGELOG 準備 1.0.0；最終 build/test/publish 驗證。收官。

**Architecture:** 純文件 phase（僅一行 backlog 程式修正）。文件全繁中；計畫提供逐節大綱與**必載事實清單**（歷代 final review 移交的文件必載 7 條 a~g），implementer 依實際程式碼行為撰寫（文件內容不得臆測——寫之前先讀對應原始碼確認行為），reviewer 對照原始碼驗證正確性與必載完整性。

**Tech Stack:** Markdown。

**Spec:** `docs/spec/mousepilot-spec.md`（§31~§34、§38、§39）；Master Plan Phase 12 章節（文件必載 (a)~(g) + backlog）。

## 計畫決策（供使用者知悉，可否決）

1. **本 phase 不建 v1.0.0 tag**：VERSION/CHANGELOG 準備好 1.0.0，但 tag 留待使用者跑完 release checklist（§34 31 案例 + 乾淨環境驗證）回報後再建——tag 會觸發 CI Release，驗證前發布違反流程。
2. **不加 LICENSE 檔**：使用者未指定授權；README 不設授權節（要加隨時可補）。
3. **CHANGELOG**：`[Unreleased]` 內容移入 `[1.0.0] - 2026-08-24`，並保留新的空 `[Unreleased]` 節。

## Global Constraints

- **文件必載（歷代 final review 移交，缺一即 NEEDS_CHANGES）**：(a) 快捷鍵設定檔格式嚴格大小寫正規（`Ctrl+Alt+F9` 形式；手改 settings.json 小寫會判無效並停用該鍵）；(b) 單一實例：第二次啟動不多開、自動彈出 Dashboard；per-user，不同 Windows 使用者可各跑一份；(c) `--restore-cursor` 參數：緊急恢復游標，不受單一實例限制，會重載系統游標 scheme；(d) log 路徑 `%AppData%\MousePilot\Logs\mousepilot.log`、5MB 輪替、保留 3 份歸檔（mousepilot.1~3.log，1 最新）；(e) 狀態紅點 Error：滑鼠移動連續失敗 3 次進入，成功/重新啟動/暫停解除；(f) balloon 已知限制：啟動期（視窗建構中）的通知不彈 balloon（log 有記錄）、同文字通知連續發生不重彈；(g) mutex fail-open 啟動時失去單一實例保證（log 有 ERROR 佐證）。
- 文件描述的每個行為必須與程式碼一致——implementer 寫之前 Read 對應原始碼；**不得**描述不存在的功能（§35 排除清單）。
- 檔名/路徑事實：EXE 產出 `bin\Release\net8.0-windows\win-x64\publish\MousePilot.exe`；執行期資料 `%AppData%\MousePilot\`（settings.json、Logs\、Cursors\、confirmed-cursor.cur、cursor-applied.marker）。
- 版本規範：`VERSION` 檔為唯一版本來源。
- Commit 紀律同前（`$env:TEMP` + `git commit -F`、UTF8 無 BOM、繁中+前綴+`Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`、`git log -1 --format=%B` 驗證）；禁止 git 還原 docs/。現況基準 271 綠。

---

### Task 1: 使用文件三件（使用說明 / FAQ / Debug 方法）

**Files:**
- Create: `docs/user-guide.md`、`docs/faq.md`、`docs/debugging.md`

**Interfaces:** Produces：三份文件（Task 2 的 README 連結它們）。

- [ ] **Step 1: 寫作前研讀**：Read `Views/MainWindow.xaml`、`Views/CursorEditorWindow.xaml`、`ViewModels/MainViewModel.cs`、`ViewModels/CursorEditorViewModel.cs`、`App.xaml.cs`、`Models/AppSettings.cs`、`Services/TrayIconService.cs`——文件描述以實碼為準。

- [ ] **Step 2: `docs/user-guide.md`（使用說明）**——逐節大綱（每節寫實際操作與行為）：
  1. 簡介與系統需求（Win10/11 x64、免安裝、免系統管理員權限）。
  2. 第一次啟動（預設縮到系統匣；如何打開 Dashboard——匣圖示雙擊/右鍵開啟）。
  3. Dashboard 導覽（監控狀態卡：狀態點顏色含 **Error 紅點語意 (e)**、閒置秒數、倒數、游標狀態、觸發次數）。
  4. 閒置偵測設定（閒置秒數 5~86400、移動間隔 1~86400、像素 1~100、三種模式、回原位置；**輸入超界會在按「啟動」時自動夾制並回填欄位**）。
  5. 啟動/暫停/立即執行一次（按鈕、系統匣、快捷鍵三種途徑）。
  6. 系統匣（選單全項說明、關閉視窗＝縮到匣的預設行為與設定）。
  7. 開機自動啟動（HKCU Run；工作管理員停用的互動）。
  8. 全域快捷鍵（預設 Ctrl+Alt+F9/F10；UI 錄製方式；**設定檔格式嚴格大小寫 (a)**；占用/重複的錯誤提示）。
  9. 自訂游標——完整流程（匯入支援格式、編輯器：圖案庫/收藏/尺寸/Hotspot 點擊與手動輸入/JPG 去背與容差/退化警告、模擬預覽、確定 vs 套用、恢復 Windows 游標的四種途徑、重開程式自動套回）。
  10. 通知（視窗內 Notice 與 tray balloon；**已知限制兩則 (f)**）。
  11. 設定檔與資料位置（`%AppData%\MousePilot\` 各檔案用途）。

- [ ] **Step 3: `docs/faq.md`（常見問題）**——至少涵蓋：不觸發（閒置條件未滿足/已暫停/Error 狀態）、游標套用後想復原（四途徑 + **--restore-cursor (c)**）、程式 crash 後游標卡自訂（下次啟動自動補救 + --restore-cursor）、第二次開啟沒反應（**單一實例 (b)**——原視窗會跳出）、多個 Windows 使用者（(b) per-user）、快捷鍵沒作用（占用/格式 (a)）、匯入圖片變形或空白（等比縮放置中/全背景去背退化）、防毒軟體警告（SendInput 模擬輸入的用途說明、開源可稽核）、CPU/RAM 佔用（500ms 輪詢近 0%）、為什麼沒有自動更新/雲端（§35 設計取捨）。

- [ ] **Step 4: `docs/debugging.md`（Debug 方法）**——涵蓋：**log 位置/輪替/保留 (d)** 與記錄項目（§29 七項）；讀 log 的方式（等級/時間戳格式）；`--restore-cursor` 用法 (c)；settings.json 位置、手改注意（夾制/互斥/hotkey 格式 (a)）、損毀時的備份行為（.corrupt-*.bak）；cursor-applied.marker 用途（crash 補救）；**fail-open 訊息 (g)**；回報問題應附資訊（log 檔、Windows 版本、重現步驟）；從原始碼 build/debug（restore/build/test/publish 指令、測試專案位置）。

- [ ] **Step 5: Commit**：`docs: 使用說明/FAQ/Debug 方法 - 交付文件三件`

---

### Task 2: README + 測試項目清單 + backlog 一行修

**Files:**
- Create: `README.md`（repo 根）、`docs/testing/release-checklist.md`
- Modify: `App.xaml.cs`（OnExit fallback 補「程式結束」log——backlog）

**Interfaces:** Consumes：Task 1 三份文件（連結）。

- [ ] **Step 1: `README.md`**——大綱：專案一句話（Windows 防閒置滑鼠微移 + 自訂游標，Portable 單一 EXE）；功能特色（條列：閒置偵測/三種移動模式/回原位/系統匣/開機自啟/全域快捷鍵/游標匯入編輯套用/單一實例/log）；系統需求（§32）；下載與執行（GitHub Releases 取得 `MousePilot.exe` 直接執行，免安裝免 Admin；資料寫入 `%AppData%\MousePilot\`——§33）；從原始碼建置（§39 三指令 + EXE 產出位置——§38-7/8/14）；文件連結（使用說明/FAQ/Debug/CHANGELOG/release-checklist）；螢幕截圖佔位**不放**（無截圖不放假連結）。

- [ ] **Step 2: `docs/testing/release-checklist.md`**——結構：
  1. 前置：§39 驗收指令三行 + 乾淨環境驗證（複製 EXE 到無 .NET Runtime 的 Win10/11 x64 機器可直接執行）。
  2. §34 全 31 案例：每案一個勾選項 `- [ ]`，含**具體操作步驟與預期結果**（依實作行為寫，例如案例 4「Idle 期間重新操作後取消」→ 操作：設閒置 5 秒，等待觸發前 2 秒動滑鼠；預期：倒數重置、狀態回「使用者活動中」）。31 案對應章節：Idle 1~5 / Movement 6~12 / Cursor 13~22 / Integration 23~28 / Configuration 29~31。
  3. 補充驗證（歷代 phase 手動清單**去重整合**——與 §34 重複者不再列，只列 §34 未涵蓋的差異項，例如：Explorer 重啟 tray 重建、工作管理員停用開機自啟的偵測、balloon 通知、等價快捷鍵查重、數值夾制回填、強殺後 marker 補救、--restore-cursor、fail-open、Error 紅點與解除、單一實例四步、log 檔輪替觀察）。每項同樣附操作/預期。
  4. 結果記錄欄（日期/機器/版本/整體結論）。

- [ ] **Step 3: backlog 一行修**：`App.xaml.cs` 的 `OnExit` 保險路徑（`if (!_exiting)` 分支內）補 `_logService?.Info("程式結束（OnExit 保險路徑）");`（在 SaveSettings 附近、Dispose 之前）。

- [ ] **Step 4: 驗證**：`dotnet build -c Release`（0 警告）、`dotnet test tests/MousePilot.Tests`（271 綠）。

- [ ] **Step 5: Commit**：`docs: README 與發布驗證清單 - 交付文件收齊`

---

### Task 3: 版本準備與收官

**Files:**
- Modify: `VERSION`（→ `1.0.0`）、`CHANGELOG.md`（`[Unreleased]` → `[1.0.0] - 2026-08-24` + 保留空 `[Unreleased]`）、`docs/superpowers/plans/2026-08-20-mousepilot-master-plan.md`（Phase 12 列 ✅ + 細部計畫文件欄）

- [ ] **Step 1: 三檔編輯**（CHANGELOG 節搬移不得改動任何條目內文）。
- [ ] **Step 2: 最終驗證**：`dotnet restore` → `dotnet build -c Release`（0 警告）→ `dotnet test tests/MousePilot.Tests`（271 綠）→ `dotnet publish ...`（成功，回報 EXE 大小）——§39 驗收指令完整跑一輪。
- [ ] **Step 3: Commit**：`chore: 版本 1.0.0 準備與進度總表收官 - Phase 12 完成`

---

## Phase 12 完成定義

- [ ] §38 交付清單 14 項全數具備（9~13 為本 phase 產出；1~8/14 既有）。
- [ ] 文件必載 (a)~(g) 全數出現在對應文件且與程式碼一致。
- [ ] build 0 error、測試全綠（271）、publish 成功。
- [ ] **使用者後續動作（非本 phase 完成條件，但為 v1.0.0 發布前置）：**
  1. 依 `docs/testing/release-checklist.md` 逐項執行（§34 31 案例 + 補充驗證 + 乾淨環境）。
  2. 全數通過後回報——屆時建 `v1.0.0` tag 並 `git push origin main --tags` 觸發 CI Release。
