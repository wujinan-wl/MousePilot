using System.Runtime.InteropServices;

// Spike A：驗證 SendInput 模擬滑鼠移動是否會重置 GetLastInputInfo。
// 執行方式：放開滑鼠鍵盤，等待程式在閒置滿 5 秒時送出一次相對移動 3px，
// 觀察後續印出的 idle 值是否歸零（歸零 = SendInput 會重置，正式版需要隔離機制）。
internal static class Program
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

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

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private static uint IdleMs()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info))
        {
            Console.WriteLine("GetLastInputInfo 失敗");
            return 0;
        }
        // TickCount 為 32-bit，49.7 天 wrap-around；用 unchecked 差值計算
        return unchecked((uint)Environment.TickCount - info.dwTime);
    }

    private static void Main()
    {
        Console.WriteLine("放開滑鼠鍵盤。閒置滿 5 秒時會送出一次 SendInput 相對移動 3px。");
        var sent = false;
        while (true)
        {
            var idle = IdleMs();
            Console.WriteLine($"idle = {idle} ms{(sent ? "（已送出模擬輸入）" : "")}");
            if (!sent && idle > 5000)
            {
                var inputs = new[]
                {
                    new INPUT
                    {
                        type = INPUT_MOUSE,
                        mi = new MOUSEINPUT { dx = 3, dy = 0, dwFlags = MOUSEEVENTF_MOVE },
                    },
                };
                var n = SendInput(1, inputs, Marshal.SizeOf<INPUT>());
                Console.WriteLine($"SendInput 送出，回傳 {n}（1=成功，0=失敗）");
                sent = true;
            }
            Thread.Sleep(500);
        }
    }
}
