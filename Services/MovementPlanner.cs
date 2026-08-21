using MousePilot.Models;

namespace MousePilot.Services;

/// <summary>移動目標計算（純函式，無 Win32、無隨機源——randomIndex 由呼叫端提供）。</summary>
public static class MovementPlanner
{
    // 順序：上、下、左、右、左上、右上、左下、右下（規格 §4 隨機模式）
    private static readonly (int Dx, int Dy)[] RandomDirections =
    {
        (0, -1), (0, 1), (-1, 0), (1, 0), (-1, -1), (1, -1), (-1, 1), (1, 1),
    };

    public static (int Dx, int Dy) NextOffset(MovementMode mode, int pixels, bool togglePositive, int randomIndex)
        => mode switch
        {
            MovementMode.Horizontal => (togglePositive ? pixels : -pixels, 0),
            MovementMode.Vertical => (0, togglePositive ? pixels : -pixels),
            _ => (RandomDirections[randomIndex & 7].Dx * pixels, RandomDirections[randomIndex & 7].Dy * pixels),
        };

    /// <summary>套用位移並確保結果在虛擬螢幕內：越界軸自動反向（規格 §23），反向仍越界則夾在邊界。</summary>
    public static (int X, int Y) ApplyWithinBounds((int X, int Y) pos, (int Dx, int Dy) offset, ScreenBounds bounds)
        => (ReflectAxis(pos.X, offset.Dx, bounds.Left, bounds.Right),
            ReflectAxis(pos.Y, offset.Dy, bounds.Top, bounds.Bottom));

    private static int ReflectAxis(int value, int delta, int min, int max)
    {
        var target = value + delta;
        if (target >= min && target <= max)
        {
            return target;
        }

        return Math.Clamp(value - delta, min, max);
    }
}
