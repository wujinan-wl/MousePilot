# Spike 結論：閒置偵測與全域游標（2026-08-20）

## Spike A：SendInput vs GetLastInputInfo

**問題（規格 §36A）：** MousePilot 自己的 SendInput 模擬滑鼠移動是否會重置 GetLastInputInfo？

**實測輸出：**（節錄自 `.superpowers/sdd/2026-08-20-phase0-phase1/spike-a-output.txt`，第 1-4 行）

```
放開滑鼠鍵盤。閒置滿 5 秒時會送出一次 SendInput 相對移動 3px。
idle = 8453 ms
SendInput 送出，回傳 1（1=成功，0=失敗）
idle = 516 ms（已送出模擬輸入）
```

**結論：會重置。**

SendInput 呼叫前，`GetLastInputInfo` 讀到的 idle 已累積至 `8453 ms`；SendInput 呼叫成功（回傳 `1`）；緊接著下一筆讀值僅 `516 ms`，遠低於「若未重置，理論值應延續累積至約 8453 + 500 ≈ 8953 ms（迴圈每 500ms 讀一次）」的預期，而是與「歸零後才過了一個 500ms 輪詢間隔」的預期（約 500ms 上下）高度吻合。證實 `SendInput` 會更新 `GetLastInputInfo` 的 `dwTime`，形同將系統閒置計時歸零，且程式自己送出的模擬輸入會被系統當成使用者活動看待。

**Phase 2/3 設計決定：**
- 若會重置：MouseMovementService 在每次送出模擬輸入前，先呼叫
  IdleDetectionService 標記「抑制窗」（送出時刻 ± 容許誤差）。抑制窗內
  發生的 last-input 變化視為自我輸入、不重置 idle 計時；抑制窗外的任何
  輸入變化一律視為真實使用者操作（寧可誤判為使用者，也不可吃掉真實操作，
  符合規格 §40 原則 1）。
- 額外防線：送出前記錄預期游標座標，若偵測到的座標與預期不符 → 視為真實使用者。

## Spike B：SetSystemCursor 替換與恢復

**問題（規格 §36B）：** 自訂游標替換後能否可靠恢復使用者原本的 cursor scheme？

**測試矩陣結果：**（來源：`.superpowers/sdd/2026-08-20-phase0-phase1/spike-b-output.txt`）

| 情境 | 結果 |
|------|------|
| 恢復路徑冒煙（未改動時執行 SPI_SETCURSORS） | `EXITCODE:0` / `SPI_SETCURSORS 恢復：True`（第 9-10 行） |
| 正常流程（替換 → 5 秒 → 恢復） | `SetSystemCursor：True`、`SPI_SETCURSORS 恢復：True`，`DURATION_SEC:5.0819716`（第 20-25 行） |
| 模擬 crash（不恢復即結束 → --restore-only 補救） | 強制終止前僅印出 `SetSystemCursor：True`，未印出恢復行（第 44-50 行）；補救呼叫 `REMEDIATION EXITCODE:0` / `SPI_SETCURSORS 恢復：True`（第 59-60 行） |
| 非預設 cursor scheme 恢復 | **未測試**（agent 無法安全確認或變更使用者當下的 cursor scheme，如實標記未測試，第 66-68 行） |

（測試矩陣完成後另執行一次最終安全確認：`FINAL-SAFETY-RESTORE EXITCODE:0` / `SPI_SETCURSORS 恢復：True`，第 74-75 行。）

**結論：**

1. `SetSystemCursor(OCR_NORMAL)` 替換可靠成功：正常流程與模擬 crash 情境中皆回傳 `True`。
2. `SystemParametersInfo(SPI_SETCURSORS)` 恢復可靠：冒煙測試、正常流程自動恢復、crash 後補救、最終安全確認四次呼叫全部回傳 `True`，exit code 皆為 `0`，無例外。
3. crash 後補救有效：process 於自動恢復執行前即被強制終止（`Stop-Process -Force`，等同 `TerminateProcess`）後，另一次獨立呼叫 `--restore-only` 仍可成功執行 `SPI_SETCURSORS` 完成恢復，證實這是有效的安全網。
4. 情境 4（非預設 cursor scheme）未測試，見下方 caveat。

**caveat（必須如實記載）：**
- 本次所有「游標已恢復」的結論，僅依據 `SetSystemCursor` / `SystemParametersInfo(SPI_SETCURSORS)` 的 API 回傳值皆為 `True` 以及 process 正常結束（exit code 0）來判斷；執行測試的 agent 無螢幕存取能力，未對游標外觀做任何視覺確認，正常流程情境的原始輸出中甚至留有「請目視確認箭頭游標已恢復原狀」的提示但未被履行（`spike-b-output.txt` 第 25、31-32 行）。若需要視覺層級的確認，仍待使用者端目視確認正常。
- 情境 4（非預設 cursor scheme）未測試：agent 無法安全得知或變更使用者當下實際使用的 cursor scheme，因此未建立此測試情境，如實標記為未測試而非用預設情境結果推論代替。Phase 9 實作前，應於一台使用非預設 cursor scheme 的環境補測一次，再依賴「`SPI_SETCURSORS` 保證重載使用者自訂 scheme」這個假設。

**Phase 9 設計決定：**
- SetSystemCursor 必須傳 CopyIcon 複本（API 會銷毀傳入 handle）。
- 恢復一律用 SystemParametersInfo(SPI_SETCURSORS)（從使用者 registry scheme 重載，
  不需要自行保存/還原個別 cursor handle）。
- 恢復掛在：套用失敗、停用功能、程式結束、未處理例外 handler、SessionEnding。
- 補充：本次驗證證實即使呼叫端 process 被強制終止（模擬 crash），系統全域游標資源
  不會隨 process 結束自動復原，必須有獨立於原 process 之外的補救路徑（如 `--restore-only`
  重新呼叫），此為本次 spike 驗證的核心價值之一。
- Phase 9 實作前應補測情境 4（非預設 cursor scheme），且應規劃使用者端的視覺確認步驟，
  不可僅依賴 API 回傳值作為「游標確實已恢復」的唯一證據。
