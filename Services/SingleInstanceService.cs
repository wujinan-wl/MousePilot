using System.IO;
using System.Threading;

namespace MousePilot.Services;

/// <summary>
/// 單一實例（規格 §20）：named Mutex 判定 + named EventWaitHandle 跨程序喚醒。
/// 喚醒不依賴視窗 handle——Tray 隱藏狀態也能喚醒；WakeRequested 在 threadpool thread 觸發，UI 端需自行轉 Dispatcher。
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    private readonly string _baseName;
    private readonly EventWaitHandle _wakeEvent;
    private readonly ManualResetEventSlim _releaseSignal = new(initialState: false);
    private RegisteredWaitHandle? _waitRegistration;
    private Thread? _ownerThread;
    private bool _owned;

    public SingleInstanceService(string? name = null)
    {
        _baseName = name ?? "MousePilot-SingleInstance";
        _wakeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, _baseName + "-wake");
    }

    public event Action? WakeRequested;

    /// <summary>
    /// true = 本程序為第一實例（取得所有權；喚醒監聽由訂閱 WakeRequested 後呼叫 StartListening 啟動）。
    /// 注意：Win32 named Mutex 的擁有權以「執行緒」為單位（同執行緒對同一 named mutex 可重入取得，
    /// 即使透過不同的 Mutex handle）。因此判定與持有動作固定綁在專屬背景執行緒上執行，
    /// 該執行緒直到 Dispose() 才釋放 mutex 並結束——避免呼叫端執行緒重入造成誤判「已取得」。
    /// </summary>
    public bool TryAcquire()
    {
        if (_ownerThread is not null)
        {
            throw new InvalidOperationException("TryAcquire 只能呼叫一次（重複呼叫會令第一持有緒永久等待）。");
        }

        using var acquiredSignal = new ManualResetEventSlim(false);
        var acquired = false;

        _ownerThread = new Thread(() =>
        {
            try
            {
                using var mutex = new Mutex(initiallyOwned: false, _baseName);
                try
                {
                    acquired = mutex.WaitOne(0);
                }
                catch (AbandonedMutexException)
                {
                    acquired = true; // 前實例 crash 未釋放——接手（計畫決策 3）
                }

                acquiredSignal.Set();

                if (acquired)
                {
                    _releaseSignal.Wait();
                    try
                    {
                        mutex.ReleaseMutex();
                    }
                    catch (ApplicationException)
                    {
                        // 防禦性——重設計後取得與釋放同緒，理論上不會發生
                    }
                }
            }
            catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException or IOException)
            {
                // kernel object 名稱被其他型別占用/ACL 拒絕：fail-open——寧可失去單一實例保證也不可啟動即 crash（規格 §21）
                acquired = true;
                acquiredSignal.Set();
                _releaseSignal.Wait();
            }
        })
        {
            IsBackground = true,
            Name = "SingleInstanceService-Owner",
        };
        _ownerThread.Start();
        acquiredSignal.Wait();

        _owned = acquired;

        return _owned;
    }

    /// <summary>
    /// 訂閱 WakeRequested 之後呼叫：開始監聽喚醒訊號。
    /// AutoReset event 具 latch 語意——呼叫前抵達的 Signal 會在註冊瞬間補觸發，訂閱後才監聽即可完全關閉喚醒丟失窗。
    /// </summary>
    public void StartListening()
    {
        if (!_owned || _waitRegistration is not null)
        {
            return;
        }

        _waitRegistration = ThreadPool.RegisterWaitForSingleObject(
            _wakeEvent, (_, _) => WakeRequested?.Invoke(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>第二實例呼叫：通知第一實例開啟 Dashboard。</summary>
    public void SignalFirstInstance() => _wakeEvent.Set();

    public void Dispose()
    {
        _waitRegistration?.Unregister(_wakeEvent);
        _waitRegistration = null;

        if (_owned)
        {
            _releaseSignal.Set();
            _ownerThread?.Join();
            _owned = false;
        }

        _wakeEvent.Dispose();
        _releaseSignal.Dispose();
    }
}
