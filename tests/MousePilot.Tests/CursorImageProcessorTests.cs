using System.Drawing;
using MousePilot.Services;

namespace MousePilot.Tests;

public class CursorImageProcessorTests
{
    private static Bitmap MakeBitmap(int w, int h, Action<Bitmap>? paint = null)
    {
        var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        paint?.Invoke(bmp);
        return bmp;
    }

    [Fact]
    public void 裁切至非透明外接矩形()
    {
        using var src = MakeBitmap(10, 10, b =>
        {
            b.SetPixel(3, 4, Color.Red);
            b.SetPixel(6, 7, Color.Blue);
        });
        using var trimmed = CursorImageProcessor.TrimTransparent(src);
        Assert.Equal(4, trimmed.Width);   // x: 3..6
        Assert.Equal(4, trimmed.Height);  // y: 4..7
        Assert.Equal(Color.Red.ToArgb(), trimmed.GetPixel(0, 0).ToArgb());
        Assert.Equal(Color.Blue.ToArgb(), trimmed.GetPixel(3, 3).ToArgb());
    }

    [Fact]
    public void 全透明圖回傳1x1()
    {
        using var src = MakeBitmap(8, 8);
        using var trimmed = CursorImageProcessor.TrimTransparent(src);
        Assert.Equal(1, trimmed.Width);
        Assert.Equal(1, trimmed.Height);
    }

    [Fact]
    public void 去背以容差判定並保留其他像素()
    {
        using var src = MakeBitmap(3, 1, b =>
        {
            b.SetPixel(0, 0, Color.FromArgb(255, 250, 250, 250)); // 接近白（容差內）
            b.SetPixel(1, 0, Color.FromArgb(255, 200, 200, 200)); // 容差外
            b.SetPixel(2, 0, Color.FromArgb(255, 255, 0, 0));     // 紅
        });
        using var result = CursorImageProcessor.RemoveBackground(src, Color.White, 10);
        Assert.Equal(0, result.GetPixel(0, 0).A);
        Assert.Equal(255, result.GetPixel(1, 0).A);
        Assert.Equal(Color.FromArgb(255, 255, 0, 0).ToArgb(), result.GetPixel(2, 0).ToArgb());
    }

    [Fact]
    public void 去背容差為Chebyshev距離()
    {
        using var src = MakeBitmap(2, 1, b =>
        {
            b.SetPixel(0, 0, Color.FromArgb(255, 245, 255, 255)); // maxΔ=10 → 透明
            b.SetPixel(1, 0, Color.FromArgb(255, 244, 255, 255)); // maxΔ=11 → 保留
        });
        using var result = CursorImageProcessor.RemoveBackground(src, Color.White, 10);
        Assert.Equal(0, result.GetPixel(0, 0).A);
        Assert.Equal(255, result.GetPixel(1, 0).A);
    }

    [Fact]
    public void 等比縮放寬圖置中不拉伸()
    {
        using var src = MakeBitmap(64, 32, b =>
        {
            using var g = Graphics.FromImage(b);
            g.Clear(Color.Lime);
        });
        using var scaled = CursorImageProcessor.ScaleProportional(src, 32);
        Assert.Equal(32, scaled.Width);
        Assert.Equal(32, scaled.Height);
        Assert.Equal(0, scaled.GetPixel(16, 2).A);    // 上邊帶透明（比例 2:1 → 高 16，置中 y=8..23）
        Assert.NotEqual(0, scaled.GetPixel(16, 16).A); // 中心有內容
        Assert.Equal(0, scaled.GetPixel(16, 29).A);    // 下邊帶透明
    }

    [Fact]
    public void 等比縮放高圖置中()
    {
        using var src = MakeBitmap(16, 64, b =>
        {
            using var g = Graphics.FromImage(b);
            g.Clear(Color.Red);
        });
        using var scaled = CursorImageProcessor.ScaleProportional(src, 64);
        Assert.Equal(64, scaled.Width);
        Assert.Equal(0, scaled.GetPixel(2, 32).A);     // 左邊帶透明（寬 16，置中 x=24..39）
        Assert.NotEqual(0, scaled.GetPixel(32, 32).A);
    }

    [Fact]
    public void 半透明像素經裁切位元組無損()
    {
        using var src = new Bitmap(3, 3, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var data = src.LockBits(new Rectangle(0, 0, 3, 3),
            System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var pixel = new byte[] { 50, 100, 200, 128 }; // BGRA @ (1,1)
            System.Runtime.InteropServices.Marshal.Copy(pixel, 0, data.Scan0 + data.Stride + 4, 4);
        }
        finally
        {
            src.UnlockBits(data);
        }

        using var trimmed = CursorImageProcessor.TrimTransparent(src);

        Assert.Equal(1, trimmed.Width);
        Assert.Equal(1, trimmed.Height);
        var outData = trimmed.LockBits(new Rectangle(0, 0, 1, 1),
            System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var outPixel = new byte[4];
            System.Runtime.InteropServices.Marshal.Copy(outData.Scan0, outPixel, 0, 4);
            Assert.Equal(new byte[] { 50, 100, 200, 128 }, outPixel);
        }
        finally
        {
            trimmed.UnlockBits(outData);
        }
    }

    [Fact]
    public void 縮放結果為32bppArgb()
    {
        using var src = MakeBitmap(10, 10);
        using var scaled = CursorImageProcessor.ScaleProportional(src, 16);
        Assert.Equal(System.Drawing.Imaging.PixelFormat.Format32bppArgb, scaled.PixelFormat);
    }
}
