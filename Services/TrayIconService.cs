using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using MousePilot.Models;
using MousePilot.Native;

namespace MousePilot.Services;

/// <summary>
/// 系統匣圖示與右鍵選單（規格 §13）。薄包裝：不含業務邏輯、只轉發事件，
/// 讓選單結構與狀態切換可單元測試。游標三項選單由 Phase 9 啟用。
/// WinForms NotifyIcon 內建 TaskbarCreated（Explorer 重啟）重建行為（規格 §21）。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    public event Action? OpenRequested;
    public event Action? StartRequested;
    public event Action? PauseRequested;
    public event Action? MoveOnceRequested;
    public event Action? ExitRequested;

    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _startItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly Icon _icon;

    public TrayIconService(bool visible = true)
    {
        _menu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("開啟 MousePilot");
        openItem.Click += (_, _) => OpenRequested?.Invoke();
        _startItem = new ToolStripMenuItem("啟動");
        _startItem.Click += (_, _) => StartRequested?.Invoke();
        _pauseItem = new ToolStripMenuItem("暫停");
        _pauseItem.Click += (_, _) => PauseRequested?.Invoke();
        var moveOnceItem = new ToolStripMenuItem("立即執行一次");
        moveOnceItem.Click += (_, _) => MoveOnceRequested?.Invoke();
        var enableCursorItem = new ToolStripMenuItem("啟用自訂游標") { Enabled = false };   // Phase 9
        var disableCursorItem = new ToolStripMenuItem("停用自訂游標") { Enabled = false };  // Phase 9
        var restoreCursorItem = new ToolStripMenuItem("恢復 Windows 游標") { Enabled = false }; // Phase 9
        var settingsItem = new ToolStripMenuItem("設定");
        settingsItem.Click += (_, _) => OpenRequested?.Invoke(); // 設定就在 Dashboard 上
        var exitItem = new ToolStripMenuItem("結束");
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        _menu.Items.AddRange(new ToolStripItem[]
        {
            openItem, new ToolStripSeparator(),
            _startItem, _pauseItem, moveOnceItem, new ToolStripSeparator(),
            enableCursorItem, disableCursorItem, restoreCursorItem, new ToolStripSeparator(),
            settingsItem, exitItem,
        });

        _icon = CreateIcon();
        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            ContextMenuStrip = _menu,
            Text = "MousePilot",
            Visible = visible,
        };
        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    public string TooltipText => _notifyIcon.Text;

    /// <summary>依監控狀態更新選單可用性與 tooltip（NotifyIcon.Text 上限 63 字元，超長會擲例外，先截斷）。</summary>
    public void UpdateStatus(MonitorStatus status, string statusText)
    {
        _startItem.Enabled = status == MonitorStatus.Paused;
        _pauseItem.Enabled = status != MonitorStatus.Paused;
        var text = $"MousePilot - {statusText}";
        _notifyIcon.Text = text.Length <= 63 ? text : text[..63];
    }

    public ToolStripMenuItem? FindMenuItem(string text)
        => _menu.Items.OfType<ToolStripMenuItem>().FirstOrDefault(i => i.Text == text);

    public IReadOnlyList<string> MenuTexts
        => _menu.Items.OfType<ToolStripItem>()
            .Select(i => i is ToolStripSeparator ? "-" : i.Text ?? "")
            .ToList();

    private static Icon CreateIcon()
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        using (var brand = new SolidBrush(Color.FromArgb(22, 163, 74)))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            g.FillEllipse(brand, 0, 0, 15, 15);        // 品牌綠圓（同 StatusRunningBrush #16A34A）
            g.FillEllipse(Brushes.White, 6, 3, 4, 7);  // 滑鼠滾輪意象
        }

        var hIcon = bmp.GetHicon();
        try
        {
            using var fromHandle = Icon.FromHandle(hIcon);
            return (Icon)fromHandle.Clone(); // Clone 擁有獨立資源，原 handle 可安全銷毀
        }
        finally
        {
            NativeMethods.DestroyIcon(hIcon);
        }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false; // 先隱藏避免系統匣殘影
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon.Dispose();
    }
}
