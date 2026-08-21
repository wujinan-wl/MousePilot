using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace MousePilot.Services;

public enum CursorCategory
{
    Basic,
    Cute,
}

public sealed record CursorPreset(string Id, string DisplayName, CursorCategory Category, bool HotspotTopLeft, int DefaultSize);

/// <summary>
/// 內建游標圖案庫（規格補一/補九）：全部程式繪製、無二進位資產——單一 EXE 天然成立、無授權疑慮。
/// 「CuteRobotCat」為自繪藍色機器貓風格 generic 造型（規格補二：不包含哆啦A夢素材）。
/// </summary>
public static class CursorGallery
{
    public static IReadOnlyList<CursorPreset> Presets { get; } = new[]
    {
        new CursorPreset("Arrow", "標準箭頭", CursorCategory.Basic, HotspotTopLeft: true, DefaultSize: 32),
        new CursorPreset("Dot", "圓點", CursorCategory.Basic, false, 32),
        new CursorPreset("Crosshair", "十字準心", CursorCategory.Basic, false, 32),
        new CursorPreset("Hand", "手指", CursorCategory.Basic, true, 32),
        new CursorPreset("Heart", "愛心", CursorCategory.Basic, false, 32),
        new CursorPreset("Star", "星星", CursorCategory.Basic, false, 32),
        new CursorPreset("Lightning", "閃電", CursorCategory.Basic, false, 32),
        new CursorPreset("Flame", "火焰", CursorCategory.Basic, false, 32),
        new CursorPreset("Cat", "貓咪", CursorCategory.Cute, false, 48),
        new CursorPreset("Dog", "狗狗", CursorCategory.Cute, false, 48),
        new CursorPreset("Bear", "熊熊", CursorCategory.Cute, false, 48),
        new CursorPreset("Rabbit", "兔子", CursorCategory.Cute, false, 48),
        new CursorPreset("Frog", "青蛙", CursorCategory.Cute, false, 48),
        new CursorPreset("Ghost", "小幽靈", CursorCategory.Cute, false, 48),
        new CursorPreset("CuteRobotCat", "藍色機器貓", CursorCategory.Cute, false, 48),
        new CursorPreset("Robot", "卡通機器人", CursorCategory.Cute, false, 48),
    };

    private static readonly Color Outline = Color.FromArgb(45, 45, 48);

    public static Bitmap Render(string id, int size)
    {
        Action<Graphics> painter = id switch
        {
            "Arrow" => DrawArrow,
            "Dot" => DrawDot,
            "Crosshair" => DrawCrosshair,
            "Hand" => DrawHand,
            "Heart" => DrawHeart,
            "Star" => DrawStar,
            "Lightning" => DrawLightning,
            "Flame" => DrawFlame,
            "Cat" => DrawCat,
            "Dog" => DrawDog,
            "Bear" => DrawBear,
            "Rabbit" => DrawRabbit,
            "Frog" => DrawFrog,
            "Ghost" => DrawGhost,
            "CuteRobotCat" => DrawRobotCat,
            "Robot" => DrawRobot,
            _ => throw new ArgumentException($"未知的內建圖案 id：{id}", nameof(id)),
        };

        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        g.ScaleTransform(size / 32f, size / 32f);
        painter(g);
        return bmp;
    }

    private static Pen OutlinePen() => new(Outline, 1.6f) { LineJoin = LineJoin.Round };

    private static void FillAndOutline(Graphics g, Brush fill, GraphicsPath path)
    {
        g.FillPath(fill, path);
        using var pen = OutlinePen();
        g.DrawPath(pen, path);
    }

    private static void DrawArrow(Graphics g)
    {
        using var path = new GraphicsPath();
        path.AddPolygon(new PointF[]
        {
            new(2, 1), new(2, 25), new(9, 19), new(13, 29), new(17, 27), new(13, 18), new(22, 17),
        });
        path.CloseFigure();
        using var fill = new SolidBrush(Color.White);
        FillAndOutline(g, fill, path);
    }

    private static void DrawDot(Graphics g)
    {
        using var outer = new SolidBrush(Color.White);
        g.FillEllipse(outer, 8, 8, 16, 16);
        using var inner = new SolidBrush(Outline);
        g.FillEllipse(inner, 11, 11, 10, 10);
    }

    private static void DrawCrosshair(Graphics g)
    {
        using var pen = new Pen(Outline, 2.2f);
        g.DrawLine(pen, 16, 2, 16, 12);
        g.DrawLine(pen, 16, 20, 16, 30);
        g.DrawLine(pen, 2, 16, 12, 16);
        g.DrawLine(pen, 20, 16, 30, 16);
        using var dot = new SolidBrush(Color.FromArgb(220, 38, 38));
        g.FillEllipse(dot, 14, 14, 4, 4);
    }

    private static void DrawHand(Graphics g)
    {
        using var path = new GraphicsPath();
        path.AddRectangle(new RectangleF(13, 2, 5, 14));       // 食指
        path.AddEllipse(8, 11, 17, 16);                        // 手掌
        using var fill = new SolidBrush(Color.FromArgb(255, 224, 189));
        FillAndOutline(g, fill, path);
    }

    private static void DrawHeart(Graphics g)
    {
        using var path = new GraphicsPath();
        path.AddArc(5, 5, 12, 12, 135, 225);
        path.AddArc(15, 5, 12, 12, 180, 225);
        path.AddLine(26, 13, 16, 28);
        path.AddLine(16, 28, 6, 13);
        path.CloseFigure();
        using var fill = new SolidBrush(Color.FromArgb(239, 68, 68));
        FillAndOutline(g, fill, path);
    }

    private static void DrawStar(Graphics g)
    {
        var points = new PointF[10];
        for (var i = 0; i < 10; i++)
        {
            var angle = (Math.PI / 5 * i) - (Math.PI / 2);
            var radius = i % 2 == 0 ? 14.0 : 5.6;
            points[i] = new PointF(16 + (float)(radius * Math.Cos(angle)), 16 + (float)(radius * Math.Sin(angle)));
        }

        using var path = new GraphicsPath();
        path.AddPolygon(points);
        using var fill = new SolidBrush(Color.FromArgb(250, 204, 21));
        FillAndOutline(g, fill, path);
    }

    private static void DrawLightning(Graphics g)
    {
        using var path = new GraphicsPath();
        path.AddPolygon(new PointF[]
        {
            new(19, 1), new(7, 18), new(14, 18), new(11, 31), new(25, 12), new(17, 12),
        });
        using var fill = new SolidBrush(Color.FromArgb(250, 204, 21));
        FillAndOutline(g, fill, path);
    }

    private static void DrawFlame(Graphics g)
    {
        using var outer = new GraphicsPath();
        outer.AddBezier(16, 2, 8, 10, 6, 16, 8, 22);
        outer.AddArc(8, 16, 16, 14, 180, -180);
        outer.AddBezier(24, 22, 26, 14, 20, 9, 16, 2);
        outer.CloseFigure();
        using var orange = new SolidBrush(Color.FromArgb(249, 115, 22));
        FillAndOutline(g, orange, outer);
        using var yellow = new SolidBrush(Color.FromArgb(253, 224, 71));
        g.FillEllipse(yellow, 12, 16, 8, 11);
    }

    private static void DrawAnimalHead(Graphics g, Color color)
    {
        using var head = new GraphicsPath();
        head.AddEllipse(4, 8, 24, 22);
        using var fill = new SolidBrush(color);
        FillAndOutline(g, fill, head);
        using var eye = new SolidBrush(Outline);
        g.FillEllipse(eye, 11, 16, 3, 4);
        g.FillEllipse(eye, 18, 16, 3, 4);
    }

    private static void DrawCat(Graphics g)
    {
        using var ears = new GraphicsPath();
        ears.AddPolygon(new PointF[] { new(6, 12), new(8, 2), new(14, 9) });
        ears.CloseFigure();
        ears.AddPolygon(new PointF[] { new(26, 12), new(24, 2), new(18, 9) });
        using var gray = new SolidBrush(Color.FromArgb(156, 163, 175));
        FillAndOutline(g, gray, ears);
        DrawAnimalHead(g, Color.FromArgb(156, 163, 175));
        using var pen = new Pen(Outline, 1.2f);
        g.DrawLine(pen, 2, 20, 9, 21);
        g.DrawLine(pen, 2, 24, 9, 23);
        g.DrawLine(pen, 30, 20, 23, 21);
        g.DrawLine(pen, 30, 24, 23, 23);
    }

    private static void DrawDog(Graphics g)
    {
        using var ears = new GraphicsPath();
        ears.AddEllipse(1, 8, 8, 14);
        ears.AddEllipse(23, 8, 8, 14);
        using var brown = new SolidBrush(Color.FromArgb(120, 83, 44));
        FillAndOutline(g, brown, ears);
        DrawAnimalHead(g, Color.FromArgb(180, 130, 80));
        using var nose = new SolidBrush(Outline);
        g.FillEllipse(nose, 14, 21, 4, 3);
    }

    private static void DrawBear(Graphics g)
    {
        using var ears = new GraphicsPath();
        ears.AddEllipse(4, 3, 8, 8);
        ears.AddEllipse(20, 3, 8, 8);
        using var brown = new SolidBrush(Color.FromArgb(146, 100, 57));
        FillAndOutline(g, brown, ears);
        DrawAnimalHead(g, Color.FromArgb(146, 100, 57));
        using var muzzle = new SolidBrush(Color.FromArgb(222, 190, 150));
        g.FillEllipse(muzzle, 12, 20, 8, 6);
        using var nose = new SolidBrush(Outline);
        g.FillEllipse(nose, 14.5f, 21, 3, 2.4f);
    }

    private static void DrawRabbit(Graphics g)
    {
        using var ears = new GraphicsPath();
        ears.AddEllipse(8, 0, 6, 14);
        ears.AddEllipse(18, 0, 6, 14);
        using var white = new SolidBrush(Color.White);
        FillAndOutline(g, white, ears);
        using var pink = new SolidBrush(Color.FromArgb(249, 168, 212));
        g.FillEllipse(pink, 10, 3, 2, 8);
        g.FillEllipse(pink, 20, 3, 2, 8);
        DrawAnimalHead(g, Color.White);
    }

    private static void DrawFrog(Graphics g)
    {
        using var bumps = new GraphicsPath();
        bumps.AddEllipse(5, 4, 9, 9);
        bumps.AddEllipse(18, 4, 9, 9);
        using var green = new SolidBrush(Color.FromArgb(74, 222, 128));
        FillAndOutline(g, green, bumps);
        using var head = new GraphicsPath();
        head.AddEllipse(3, 10, 26, 19);
        FillAndOutline(g, green, head);
        using var white = new SolidBrush(Color.White);
        g.FillEllipse(white, 7, 6, 5, 5);
        g.FillEllipse(white, 20, 6, 5, 5);
        using var eye = new SolidBrush(Outline);
        g.FillEllipse(eye, 9, 7.5f, 2.4f, 2.4f);
        g.FillEllipse(eye, 22, 7.5f, 2.4f, 2.4f);
        using var pen = new Pen(Outline, 1.4f);
        g.DrawArc(pen, 10, 16, 12, 8, 20, 140);
    }

    private static void DrawGhost(Graphics g)
    {
        using var path = new GraphicsPath();
        path.AddArc(6, 3, 20, 20, 180, 180);
        path.AddLine(26, 13, 26, 27);
        path.AddLine(26, 27, 22.7f, 24);
        path.AddLine(22.7f, 24, 19.3f, 28);
        path.AddLine(19.3f, 28, 16, 24);
        path.AddLine(16, 24, 12.7f, 28);
        path.AddLine(12.7f, 28, 9.3f, 24);
        path.AddLine(9.3f, 24, 6, 27);
        path.CloseFigure();
        using var white = new SolidBrush(Color.White);
        FillAndOutline(g, white, path);
        using var eye = new SolidBrush(Outline);
        g.FillEllipse(eye, 11, 11, 3.4f, 4.6f);
        g.FillEllipse(eye, 18, 11, 3.4f, 4.6f);
    }

    private static void DrawRobotCat(Graphics g)
    {
        using var head = new GraphicsPath();
        head.AddEllipse(3, 4, 26, 25);
        using var blue = new SolidBrush(Color.FromArgb(59, 130, 246));
        FillAndOutline(g, blue, head);
        using var face = new SolidBrush(Color.White);
        g.FillEllipse(face, 6, 12, 20, 15);
        using var eyeWhite = new SolidBrush(Color.White);
        g.FillEllipse(eyeWhite, 10, 6, 6, 8);
        g.FillEllipse(eyeWhite, 16, 6, 6, 8);
        using var outlinePen = OutlinePen();
        g.DrawEllipse(outlinePen, 10, 6, 6, 8);
        g.DrawEllipse(outlinePen, 16, 6, 6, 8);
        using var eye = new SolidBrush(Outline);
        g.FillEllipse(eye, 12.5f, 8.5f, 2, 3);
        g.FillEllipse(eye, 17.5f, 8.5f, 2, 3);
        using var nose = new SolidBrush(Color.FromArgb(220, 38, 38));
        g.FillEllipse(nose, 14.4f, 13, 3.2f, 3.2f);
        using var pen = new Pen(Outline, 1.2f);
        g.DrawLine(pen, 16, 16.5f, 16, 24);
        g.DrawLine(pen, 4, 17, 11, 18.5f);
        g.DrawLine(pen, 4, 22, 11, 21.5f);
        g.DrawLine(pen, 28, 17, 21, 18.5f);
        g.DrawLine(pen, 28, 22, 21, 21.5f);
    }

    private static void DrawRobot(Graphics g)
    {
        using var pen = OutlinePen();
        g.DrawLine(pen, 16, 6, 16, 2);
        using var antenna = new SolidBrush(Color.FromArgb(220, 38, 38));
        g.FillEllipse(antenna, 14.4f, 0, 3.2f, 3.2f);
        using var head = new GraphicsPath();
        using var headRect = RoundedRect(5, 6, 22, 20, 4);
        head.AddPath(headRect, false);
        using var gray = new SolidBrush(Color.FromArgb(203, 213, 225));
        FillAndOutline(g, gray, head);
        using var eye = new SolidBrush(Color.FromArgb(37, 99, 235));
        g.FillRectangle(eye, 9, 12, 5, 5);
        g.FillRectangle(eye, 18, 12, 5, 5);
        using var mouthPen = new Pen(Outline, 1.4f);
        g.DrawLine(mouthPen, 10, 21.5f, 22, 21.5f);
        g.DrawLine(mouthPen, 13, 19.5f, 13, 23.5f);
        g.DrawLine(mouthPen, 16, 19.5f, 16, 23.5f);
        g.DrawLine(mouthPen, 19, 19.5f, 19, 23.5f);
    }

    private static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
    {
        var path = new GraphicsPath();
        path.AddArc(x, y, r * 2, r * 2, 180, 90);
        path.AddArc(x + w - (r * 2), y, r * 2, r * 2, 270, 90);
        path.AddArc(x + w - (r * 2), y + h - (r * 2), r * 2, r * 2, 0, 90);
        path.AddArc(x, y + h - (r * 2), r * 2, r * 2, 90, 90);
        path.CloseFigure();
        return path;
    }
}
