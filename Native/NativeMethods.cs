using System.Runtime.InteropServices;

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

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    /// <summary>最後一次輸入的 tick（毫秒）。失敗時回傳目前 tick——視為「剛有輸入」的保守值，確保不誤觸發移動（規格 §40-1）。</summary>
    internal static uint GetLastInputTick()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref info) ? info.dwTime : (uint)Environment.TickCount;
    }

    internal static (int X, int Y) GetCursorPosition()
        => GetCursorPos(out var p) ? (p.X, p.Y) : (0, 0);
}
