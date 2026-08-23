namespace MousePilot.Views;

/// <summary>Hotspot 顯示座標 ↔ 游標像素座標換算（純函式；display 為正方形顯示區邊長）。</summary>
public static class HotspotMath
{
    public static (int X, int Y) DisplayToPixel(double clickX, double clickY, double displaySize, int cursorSize)
        => ((int)Math.Clamp(Math.Floor(clickX / displaySize * cursorSize), 0, cursorSize - 1),
            (int)Math.Clamp(Math.Floor(clickY / displaySize * cursorSize), 0, cursorSize - 1));

    public static double PixelToDisplayCenter(int pixel, double displaySize, int cursorSize)
        => (pixel + 0.5) / cursorSize * displaySize;
}
