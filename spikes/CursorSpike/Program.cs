using System.Runtime.InteropServices;

// Spike B：驗證 SetSystemCursor 替換游標後，SPI_SETCURSORS 能否完整恢復。
// 用法：
//   dotnet run --project spikes/CursorSpike                  完整測試（替換 5 秒後恢復）
//   dotnet run --project spikes/CursorSpike -- --restore-only 只執行恢復（模擬 crash 後補救）
internal static class Program
{
    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

    [DllImport("user32.dll")]
    private static extern IntPtr CopyIcon(IntPtr hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetSystemCursor(IntPtr hcur, uint id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

    private const int IDC_CROSS = 32515;
    private const uint OCR_NORMAL = 32512;
    private const uint SPI_SETCURSORS = 0x0057;

    private static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--restore-only")
        {
            var ok = SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, 0);
            Console.WriteLine($"SPI_SETCURSORS 恢復：{ok}");
            return;
        }

        Console.WriteLine("按 Enter 後：標準箭頭 → 十字游標 5 秒 → SPI_SETCURSORS 恢復。");
        Console.ReadLine();

        // SetSystemCursor 會接管並銷毀傳入的 handle，必須傳複本（CopyIcon）
        var cross = CopyIcon(LoadCursor(IntPtr.Zero, IDC_CROSS));
        var setOk = SetSystemCursor(cross, OCR_NORMAL);
        Console.WriteLine($"SetSystemCursor：{setOk}");
        Thread.Sleep(5000);

        var restoreOk = SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, 0);
        Console.WriteLine($"SPI_SETCURSORS 恢復：{restoreOk}");
        Console.WriteLine("請目視確認箭頭游標已恢復原狀。");
    }
}
