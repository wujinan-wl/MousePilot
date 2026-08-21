using System.Runtime.InteropServices;
using MousePilot.Models;

namespace MousePilot.Native;

internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    /// <summary>最後一次輸入的 tick（毫秒）。失敗時回傳目前 tick——視為「剛有輸入」的保守值，確保不誤觸發移動（規格 §40-1）。</summary>
    internal static uint GetLastInputTick()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref info) ? info.dwTime : (uint)Environment.TickCount;
    }

    /// <summary>目前游標座標；失敗回傳 null（(0,0) 在負座標桌面是合法座標，不可作 fallback——Phase 2 移交約束 5）。</summary>
    internal static (int X, int Y)? GetCursorPosition()
        => GetCursorPos(out var p) ? (p.X, p.Y) : null;

    /// <summary>虛擬螢幕範圍；GetSystemMetrics 失敗（回 0）時用保守預設 1920x1080。</summary>
    internal static ScreenBounds GetVirtualScreenBounds()
    {
        var w = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var h = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if (w <= 0 || h <= 0)
        {
            return new ScreenBounds(0, 0, 1920, 1080);
        }

        return new ScreenBounds(GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN), w, h);
    }

    /// <summary>以絕對座標送出一次滑鼠移動。會產生真實輸入事件（防閒置的核心；SetCursorPos 不會）。</summary>
    internal static bool SendMouseMoveAbsolute(int x, int y, ScreenBounds bounds)
    {
        var (nx, ny) = bounds.ToAbsolute(x, y);
        var inputs = new[]
        {
            new INPUT
            {
                type = INPUT_MOUSE,
                mi = new MOUSEINPUT { dx = nx, dy = ny, dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK },
            },
        };
        return SendInput(1, inputs, Marshal.SizeOf<INPUT>()) == 1;
    }

    /// <summary>精準設定游標位置（不產生輸入事件，僅供 ±1px 校正）。</summary>
    internal static bool SetCursorPosition(int x, int y) => SetCursorPos(x, y);
}
