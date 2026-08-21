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
