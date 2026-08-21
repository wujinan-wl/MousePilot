using System.Drawing;
using System.IO;
using MousePilot.Services;

namespace MousePilot.Tests;

public class CurFileFormatTests
{
    private static Bitmap MakeTestImage()
    {
        var bmp = new Bitmap(8, 8, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        bmp.SetPixel(0, 0, Color.FromArgb(255, 255, 0, 0));   // 左上紅
        bmp.SetPixel(7, 7, Color.FromArgb(255, 0, 0, 255));   // 右下藍
        bmp.SetPixel(3, 3, Color.FromArgb(128, 0, 255, 0));   // 半透明綠
        return bmp;
    }

    [Fact]
    public void 寫入後可讀回尺寸與Hotspot()
    {
        using var img = MakeTestImage();
        var bytes = CurFileFormat.Write(img, 2, 5);

        var read = CurFileFormat.TryReadFirstImage(bytes);

        Assert.NotNull(read);
        Assert.Equal(new CurImage(8, 8, 2, 5), read!.Value.Info);
        read.Value.Image.Dispose();
    }

    [Fact]
    public void 寫入後像素往返一致()
    {
        using var img = MakeTestImage();
        var bytes = CurFileFormat.Write(img, 0, 0);

        var read = CurFileFormat.TryReadFirstImage(bytes);
        using var round = read!.Value.Image;

        Assert.Equal(Color.FromArgb(255, 255, 0, 0).ToArgb(), round.GetPixel(0, 0).ToArgb());
        Assert.Equal(Color.FromArgb(255, 0, 0, 255).ToArgb(), round.GetPixel(7, 7).ToArgb());
        Assert.Equal(Color.FromArgb(128, 0, 255, 0).ToArgb(), round.GetPixel(3, 3).ToArgb());
        Assert.Equal(0, round.GetPixel(5, 5).A); // 未設定像素維持透明
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3, 4 })]
    public void 損毀資料回傳null(byte[] data)
    {
        Assert.Null(CurFileFormat.TryReadFirstImage(data));
        Assert.Null(CurFileFormat.TryReadAniFirstFrame(data));
    }

    [Fact]
    public void 截斷的cur回傳null()
    {
        using var img = MakeTestImage();
        var bytes = CurFileFormat.Write(img, 0, 0);
        Assert.Null(CurFileFormat.TryReadFirstImage(bytes[..30]));
    }

    [Fact]
    public void ANI首格可解析()
    {
        using var img = MakeTestImage();
        var cur = CurFileFormat.Write(img, 1, 2);
        var ani = BuildAni(cur);

        var read = CurFileFormat.TryReadAniFirstFrame(ani);

        Assert.NotNull(read);
        Assert.Equal(new CurImage(8, 8, 1, 2), read!.Value.Info);
        read.Value.Image.Dispose();
    }

    [Fact]
    public void 非ACON的RIFF回傳null()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8);
        w.Write(4);
        w.Write("WAVE"u8);
        Assert.Null(CurFileFormat.TryReadAniFirstFrame(ms.ToArray()));
    }

    [Fact]
    public void 惡意深巢狀LIST回傳null不當機()
    {
        // 100 層巢狀 LIST/fram，無 icon chunk——深度上限應回 null 而非 StackOverflow
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        const int depth = 100;
        var innermost = 4;                        // 最內層只有 'fram'
        var sizes = new int[depth];
        sizes[depth - 1] = innermost;
        for (var i = depth - 2; i >= 0; i--)
        {
            sizes[i] = 4 + 8 + sizes[i + 1];      // 'fram' + 內層 LIST chunk
        }

        w.Write("RIFF"u8);
        w.Write(4 + 8 + sizes[0]);
        w.Write("ACON"u8);
        for (var i = 0; i < depth; i++)
        {
            w.Write("LIST"u8);
            w.Write(sizes[i]);
            w.Write("fram"u8);
        }

        Assert.Null(CurFileFormat.TryReadAniFirstFrame(ms.ToArray()));
    }

    [Fact]
    public void PNG條目的cur可讀取且影像獨立於來源()
    {
        using var bmp = MakeTestImage();
        byte[] png;
        using (var ms = new MemoryStream())
        {
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            png = ms.ToArray();
        }

        var cur = BuildPngCur(png, 8, 8, hotspotX: 3, hotspotY: 4);
        var read = CurFileFormat.TryReadFirstImage(cur);

        Assert.NotNull(read);
        Assert.Equal(new CurImage(8, 8, 3, 4), read!.Value.Info);
        using var image = read.Value.Image;
        Assert.Equal(System.Drawing.Imaging.PixelFormat.Format32bppArgb, image.PixelFormat);
        Assert.Equal(Color.FromArgb(255, 255, 0, 0).ToArgb(), image.GetPixel(0, 0).ToArgb());
    }

    private static byte[] BuildPngCur(byte[] png, int width, int height, int hotspotX, int hotspotY)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((ushort)0);
        w.Write((ushort)2);
        w.Write((ushort)1);
        w.Write((byte)width);
        w.Write((byte)height);
        w.Write((byte)0);
        w.Write((byte)0);
        w.Write((ushort)hotspotX);
        w.Write((ushort)hotspotY);
        w.Write((uint)png.Length);
        w.Write((uint)22);
        w.Write(png);
        return ms.ToArray();
    }

    private static byte[] BuildAni(byte[] cur)
    {
        var pad = cur.Length % 2;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var iconChunkTotal = 8 + cur.Length + pad;  // 'icon' + size + data + padding
        var listSize = 4 + iconChunkTotal;          // 'fram' + icon chunk
        var riffSize = 4 + 8 + listSize;            // 'ACON' + LIST header + LIST content
        w.Write("RIFF"u8);
        w.Write(riffSize);
        w.Write("ACON"u8);
        w.Write("LIST"u8);
        w.Write(listSize);
        w.Write("fram"u8);
        w.Write("icon"u8);
        w.Write(cur.Length);
        w.Write(cur);
        if (pad == 1)
        {
            w.Write((byte)0);
        }

        return ms.ToArray();
    }
}
