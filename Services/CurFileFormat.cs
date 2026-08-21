using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace MousePilot.Services;

public readonly record struct CurImage(int Width, int Height, int HotspotX, int HotspotY);

/// <summary>
/// .cur 檔讀寫（規格 §8/§9：hotspot 存於 ICONDIRENTRY）與 ANI 首格解析（規格 §21：失敗優雅降級 → null）。
/// 寫入採 32bpp DIB 條目（全 Windows 版本通用）；讀取支援 DIB 與 PNG 壓縮條目。
/// </summary>
public static class CurFileFormat
{
    /// <summary>把 32bppArgb 圖寫成單條目 .cur。寬高上限 256（.cur 格式限制，寫入前應先縮放）。</summary>
    public static byte[] Write(Bitmap image, int hotspotX, int hotspotY)
    {
        var w = image.Width;
        var h = image.Height;
        var xorStride = w * 4;
        var andStride = ((w + 31) / 32) * 4; // 1bpp，每列補齊 32 位元
        var imageSize = 40 + (xorStride * h) + (andStride * h);

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((ushort)0);              // reserved
        bw.Write((ushort)2);              // type 2 = cursor
        bw.Write((ushort)1);              // count
        bw.Write((byte)(w == 256 ? 0 : w));
        bw.Write((byte)(h == 256 ? 0 : h));
        bw.Write((byte)0);                // color count
        bw.Write((byte)0);                // reserved
        bw.Write((ushort)hotspotX);
        bw.Write((ushort)hotspotY);
        bw.Write((uint)imageSize);
        bw.Write((uint)22);               // offset = 6 + 16

        bw.Write(40);                     // BITMAPINFOHEADER
        bw.Write(w);
        bw.Write(h * 2);                  // XOR + AND
        bw.Write((ushort)1);
        bw.Write((ushort)32);
        bw.Write(0);                      // BI_RGB
        bw.Write((xorStride * h) + (andStride * h));
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);

        var pixels = ReadArgb(image);
        for (var y = h - 1; y >= 0; y--) // DIB 由下而上
        {
            bw.Write(pixels, y * xorStride, xorStride);
        }

        var andRow = new byte[andStride]; // alpha 承擔透明，AND mask 全 0
        for (var y = 0; y < h; y++)
        {
            bw.Write(andRow);
        }

        return ms.ToArray();
    }

    /// <summary>讀取 .cur/.ico 首條目；任何格式異常回傳 null（規格 §21）。</summary>
    public static (CurImage Info, Bitmap Image)? TryReadFirstImage(byte[] data)
    {
        try
        {
            if (data.Length < 22)
            {
                return null;
            }

            var type = BitConverter.ToUInt16(data, 2);
            var count = BitConverter.ToUInt16(data, 4);
            if (BitConverter.ToUInt16(data, 0) != 0 || (type != 1 && type != 2) || count < 1)
            {
                return null;
            }

            int width = data[6] == 0 ? 256 : data[6];
            int height = data[7] == 0 ? 256 : data[7];
            var hotspotX = type == 2 ? BitConverter.ToUInt16(data, 10) : 0;
            var hotspotY = type == 2 ? BitConverter.ToUInt16(data, 12) : 0;
            var size = BitConverter.ToInt32(data, 14);
            var offset = BitConverter.ToInt32(data, 18);
            if (offset < 22 || size < 4 || offset + size > data.Length)
            {
                return null;
            }

            Bitmap image;
            if (data[offset] == 0x89 && data[offset + 1] == 0x50) // PNG 條目
            {
                using var pngStream = new MemoryStream(data, offset, size);
                image = new Bitmap(pngStream);
            }
            else
            {
                var dib = TryDecodeDib32(data, offset, size, width, height);
                if (dib is null)
                {
                    return null;
                }

                image = dib;
            }

            return (new CurImage(image.Width, image.Height, hotspotX, hotspotY), image);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>解析 ANI（RIFF/ACON）的第一個 icon chunk；任何異常回傳 null。</summary>
    public static (CurImage Info, Bitmap Image)? TryReadAniFirstFrame(byte[] data)
    {
        try
        {
            if (data.Length < 12
                || data[0] != 'R' || data[1] != 'I' || data[2] != 'F' || data[3] != 'F'
                || data[8] != 'A' || data[9] != 'C' || data[10] != 'O' || data[11] != 'N')
            {
                return null;
            }

            return FindIconChunk(data, 12, data.Length);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    private static (CurImage Info, Bitmap Image)? FindIconChunk(byte[] data, int start, int end)
    {
        var pos = start;
        while (pos + 8 <= end)
        {
            var id = System.Text.Encoding.ASCII.GetString(data, pos, 4);
            var size = BitConverter.ToInt32(data, pos + 4);
            if (size < 0 || pos + 8 + size > data.Length)
            {
                return null;
            }

            if (id == "icon")
            {
                return TryReadFirstImage(data[(pos + 8)..(pos + 8 + size)]);
            }

            if (id == "LIST" && size >= 4)
            {
                var inner = FindIconChunk(data, pos + 12, pos + 8 + size);
                if (inner is not null)
                {
                    return inner;
                }
            }

            pos += 8 + size + (size % 2); // chunk 補齊偶數
        }

        return null;
    }

    private static Bitmap? TryDecodeDib32(byte[] data, int offset, int size, int width, int height)
    {
        if (size < 40 || BitConverter.ToInt32(data, offset) != 40)
        {
            return null;
        }

        var bitCount = BitConverter.ToUInt16(data, offset + 14);
        if (bitCount != 32)
        {
            return null; // 只支援 32bpp（自家 Write 的格式；其他 bpp 屬罕見舊格式 → 優雅降級）
        }

        var xorStride = width * 4;
        if (offset + 40 + (xorStride * height) > data.Length)
        {
            return null;
        }

        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var pixels = new byte[xorStride * height];
        for (var y = 0; y < height; y++)
        {
            Array.Copy(data, offset + 40 + ((height - 1 - y) * xorStride), pixels, y * xorStride, xorStride);
        }

        WriteArgb(bmp, pixels);
        return bmp;
    }

    private static byte[] ReadArgb(Bitmap bmp)
    {
        var data = bmp.LockBits(
            new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[bmp.Width * 4 * bmp.Height];
            for (var y = 0; y < bmp.Height; y++)
            {
                Marshal.Copy(data.Scan0 + (y * data.Stride), bytes, y * bmp.Width * 4, bmp.Width * 4);
            }

            return bytes;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    private static void WriteArgb(Bitmap bmp, byte[] bytes)
    {
        var data = bmp.LockBits(
            new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < bmp.Height; y++)
            {
                Marshal.Copy(bytes, y * bmp.Width * 4, data.Scan0 + (y * data.Stride), bmp.Width * 4);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
