# Publish Smoke Test：驗證「只散佈 MousePilot.exe」仍能獨立啟動（防 dotnet/runtime#61279 回歸）。
# 步驟：
#   1. publish 目錄不得殘留 *_cor3.dll（WPF Native DLL 必須已打進單檔）。
#   2. 將 MousePilot.exe 單獨複製到全新暫存目錄並啟動。
#   3. 等待 N 秒：程序提前退出 → 失敗。
#   4. 正式 log 必須出現「[INFO] 程式啟動」成功標記（程序活著≠啟動成功——啟動失敗會停在阻塞的 MessageBox）。
#   5. 安全結束程序；本機執行時另跑 --restore-cursor 保險（強制結束不會走游標恢復流程）。
# 注意：本測試使用真實 %AppData%\MousePilot（程式的 log 位置無法重導向）；若本機已有 MousePilot 在執行，
#       單一實例機制會讓測試實例立即讓路，因此偵測到既有程序即中止測試。
param(
    [string]$PublishDir = "bin\Release\net8.0-windows\win-x64\publish",
    [int]$WaitSeconds = 10
)

$ErrorActionPreference = "Stop"

function Fail([string]$message) {
    Write-Host "SMOKE TEST 失敗：$message" -ForegroundColor Red
    # 失敗時把兩份 log 倒給 CI 便於診斷
    foreach ($log in @(
        (Join-Path $env:APPDATA "MousePilot\Logs\mousepilot.log"),
        (Join-Path $env:APPDATA "MousePilot\Logs\mousepilot-bootstrap.log"))) {
        if (Test-Path $log) {
            Write-Host "----- $log（最後 40 行）-----"
            Get-Content $log -Tail 40 | Write-Host
        }
    }
    exit 1
}

# 0. 前置檢查
$exe = Join-Path $PublishDir "MousePilot.exe"
if (-not (Test-Path $exe)) { Fail "找不到 $exe，請先 dotnet publish" }
if (Get-Process -Name "MousePilot" -ErrorAction SilentlyContinue) {
    Fail "偵測到 MousePilot 已在執行——單一實例機制會使測試實例立即退出，請先關閉再測"
}

# 1. publish 目錄不得殘留必須隨程式散佈的 WPF Native DLL
$leftover = Get-ChildItem $PublishDir -Filter "*_cor3.dll" -ErrorAction SilentlyContinue
if ($leftover) {
    Fail "publish 目錄殘留 Native DLL（未打進單檔）：$($leftover.Name -join ', ')。請確認 csproj 的 IncludeNativeLibrariesForSelfExtract=true"
}
Write-Host "[1/4] publish 目錄無 *_cor3.dll 殘留" -ForegroundColor Green

# 2. 只複製 EXE 到全新暫存目錄（模擬使用者只下載單一 EXE）
$standaloneDir = Join-Path ([IO.Path]::GetTempPath()) ("mousepilot-smoke-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $standaloneDir | Out-Null
Copy-Item $exe -Destination $standaloneDir
$standaloneExe = Join-Path $standaloneDir "MousePilot.exe"
Write-Host "[2/4] 已複製 EXE 至獨立目錄：$standaloneDir" -ForegroundColor Green

# 3. 啟動並等待：紀錄啟動前 log 內容，之後只認「新寫入」的成功標記
$mainLog = Join-Path $env:APPDATA "MousePilot\Logs\mousepilot.log"
$preContent = if (Test-Path $mainLog) { Get-Content $mainLog -Raw -Encoding UTF8 } else { "" }

$proc = Start-Process -FilePath $standaloneExe -WorkingDirectory $standaloneDir -PassThru
Write-Host "已啟動 PID $($proc.Id)，等待 $WaitSeconds 秒..."
Start-Sleep -Seconds $WaitSeconds

if ($proc.HasExited) {
    Fail "程序在 $WaitSeconds 秒內提前退出（ExitCode=$($proc.ExitCode)）"
}

$postContent = if (Test-Path $mainLog) { Get-Content $mainLog -Raw -Encoding UTF8 } else { "" }
$newContent = if ($postContent.Length -ge $preContent.Length -and $postContent.StartsWith($preContent)) {
    $postContent.Substring($preContent.Length)   # 只看本次啟動新增的內容
} else {
    $postContent                                  # log 已輪替：退而搜尋全文
}
if ($newContent -notmatch "\[INFO\] 程式啟動") {
    try { Stop-Process -Id $proc.Id -Force } catch {}
    Fail "程序存活但 log 未出現「程式啟動」成功標記（可能停在啟動失敗的 MessageBox）"
}
Write-Host "[3/4] 程序存活且 log 出現「程式啟動」成功標記" -ForegroundColor Green

# 4. 安全結束與清理
Stop-Process -Id $proc.Id -Force
Wait-Process -Id $proc.Id -Timeout 10 -ErrorAction SilentlyContinue
# 強制結束不會走游標恢復流程：跑 --restore-cursor 保險（該路徑在單一實例檢查前執行，會自行結束）
$restore = Start-Process -FilePath $standaloneExe -ArgumentList "--restore-cursor" -PassThru
$restore.WaitForExit(15000) | Out-Null
Remove-Item $standaloneDir -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "[4/4] 已結束程序並清理暫存目錄" -ForegroundColor Green

Write-Host "SMOKE TEST 通過：MousePilot.exe 可單獨於全新目錄啟動" -ForegroundColor Green
exit 0
