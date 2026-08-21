namespace MousePilot.Models;

/// <summary>虛擬螢幕範圍（可含負座標，規格 §22）。端點含邊界。</summary>
public readonly record struct ScreenBounds(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width - 1;

    public int Bottom => Top + Height - 1;

    public bool Contains(int x, int y) => x >= Left && x <= Right && y >= Top && y <= Bottom;

    /// <summary>把虛擬螢幕座標正規化為 SendInput 絕對座標（0~65535，搭配 MOUSEEVENTF_VIRTUALDESK）。</summary>
    public (int Nx, int Ny) ToAbsolute(int x, int y) => (
        (int)Math.Round((x - Left) * 65535.0 / Math.Max(1, Width - 1)),
        (int)Math.Round((y - Top) * 65535.0 / Math.Max(1, Height - 1)));
}
