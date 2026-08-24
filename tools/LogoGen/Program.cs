using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// LogoGen：程式繪製 MousePilot 原創 LOGO（藍底圓形徽章 + 白色游標箭頭 + 飛行軌跡點），
// 產出 Assets/app.ico（16~256 多尺寸）與 Assets/logo.png（256）。
// 用法：dotnet run --project tools/LogoGen -- <repo 根目錄>
internal static class Program
{
    private static readonly Color Badge = Color.FromArgb(255, 37, 99, 235);      // #2563EB
    private static readonly Color BadgeRim = Color.FromArgb(255, 29, 78, 216);   // #1D4ED8（外圈微暗）
    private static readonly Color ArrowOutline = Color.FromArgb(255, 30, 58, 138); // #1E3A8A

    private static void Main(string[] args)
    {
        var root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
        var assets = Path.Combine(root, "Assets");
        Directory.CreateDirectory(assets);

        using var master = Draw(256);
        master.Save(Path.Combine(assets, "logo.png"), ImageFormat.Png);

        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        var images = new Dictionary<int, Bitmap>();
        foreach (var size in sizes)
        {
            images[size] = size == 256 ? (Bitmap)master.Clone() : Downscale(master, size);
        }

        WriteIco(Path.Combine(assets, "app.ico"), sizes, images);
        foreach (var bmp in images.Values)
        {
            bmp.Dispose();
        }

        Console.WriteLine($"寫出 {Path.Combine(assets, "app.ico")} 與 logo.png");
    }

    private static Bitmap Draw(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        var s = size / 256f;

        // 藍底圓形徽章（外圈微暗營造立體感）
        using (var rim = new SolidBrush(BadgeRim))
        {
            g.FillEllipse(rim, 4 * s, 4 * s, 248 * s, 248 * s);
        }

        using (var badge = new SolidBrush(Badge))
        {
            g.FillEllipse(badge, 12 * s, 12 * s, 232 * s, 232 * s);
        }

        // 飛行軌跡點：自左上弧向游標，越靠近箭頭越大越實
        var dots = new (float X, float Y, float R, int Alpha)[]
        {
            (52, 52, 8, 120),
            (68, 64, 11, 185),
            (85, 78, 14, 255),
        };
        foreach (var (x, y, r, alpha) in dots)
        {
            using var dot = new SolidBrush(Color.FromArgb(alpha, Color.White));
            g.FillEllipse(dot, (x - r) * s, (y - r) * s, r * 2 * s, r * 2 * s);
        }

        // 白色游標箭頭（經典指標形，指向左上；深藍描邊）
        var unit = new PointF[]
        {
            new(0f, 0f), new(0f, 16.5f), new(4.6f, 12.7f), new(7.6f, 19.2f),
            new(10.4f, 18.0f), new(7.3f, 11.6f), new(13.2f, 11.6f),
        };
        const float arrowScale = 7.2f;
        const float tipX = 100f;
        const float tipY = 96f;
        var pts = unit.Select(p => new PointF((tipX + p.X * arrowScale) * s, (tipY + p.Y * arrowScale) * s)).ToArray();
        using var path = new GraphicsPath();
        path.AddPolygon(pts);
        using (var fill = new SolidBrush(Color.White))
        {
            g.FillPath(fill, path);
        }

        using (var pen = new Pen(ArrowOutline, MathF.Max(1f, 5f * s)) { LineJoin = LineJoin.Round })
        {
            g.DrawPath(pen, path);
        }

        return bmp;
    }

    private static Bitmap Downscale(Bitmap source, int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.DrawImage(source, new Rectangle(0, 0, size, size));
        return bmp;
    }

    /// <summary>ICO（type 1）：≤128 用 32bpp DIB 條目（相容性最佳），256 用 PNG 條目（官方支援）。</summary>
    private static void WriteIco(string path, int[] sizes, Dictionary<int, Bitmap> images)
    {
        var entries = new List<byte[]>();
        foreach (var size in sizes)
        {
            entries.Add(size >= 256 ? EncodePng(images[size]) : EncodeDib(images[size]));
        }

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((ushort)0);
        w.Write((ushort)1); // type 1 = icon
        w.Write((ushort)sizes.Length);
        var offset = 6 + 16 * sizes.Length;
        for (var i = 0; i < sizes.Length; i++)
        {
            var size = sizes[i];
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((ushort)1);  // planes
            w.Write((ushort)32); // bitcount
            w.Write((uint)entries[i].Length);
            w.Write((uint)offset);
            offset += entries[i].Length;
        }

        foreach (var entry in entries)
        {
            w.Write(entry);
        }

        File.WriteAllBytes(path, ms.ToArray());
    }

    private static byte[] EncodePng(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static byte[] EncodeDib(Bitmap bmp)
    {
        var w = bmp.Width;
        var h = bmp.Height;
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var pixels = new byte[w * h * 4];
        try
        {
            for (var y = 0; y < h; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * w * 4, w * 4);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        var andStride = ((w + 31) / 32) * 4;
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(40);            // BITMAPINFOHEADER
        bw.Write(w);
        bw.Write(h * 2);         // XOR + AND
        bw.Write((ushort)1);
        bw.Write((ushort)32);
        bw.Write(0);             // BI_RGB
        bw.Write(w * h * 4 + andStride * h);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);
        for (var y = h - 1; y >= 0; y--) // bottom-up
        {
            bw.Write(pixels, y * w * 4, w * 4);
        }

        bw.Write(new byte[andStride * h]); // AND mask 全 0（alpha 主導）
        return ms.ToArray();
    }
}
