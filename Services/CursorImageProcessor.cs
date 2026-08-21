using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace MousePilot.Services;

/// <summary>游標圖片處理（純運算；LockBits 存取避免 GetPixel 逐點的效能問題）。回傳皆為新 Bitmap，呼叫端負責 Dispose。</summary>
public static class CursorImageProcessor
{
    /// <summary>裁切至非透明內容的外接矩形（規格補六「自動裁切透明區域」）；全透明回傳 1×1 透明圖。</summary>
    public static Bitmap TrimTransparent(Bitmap source)
    {
        using var normalized = ToArgb(source);
        var bytes = ReadArgb(normalized, out var stride);
        int minX = normalized.Width, minY = normalized.Height, maxX = -1, maxY = -1;
        for (var y = 0; y < normalized.Height; y++)
        {
            for (var x = 0; x < normalized.Width; x++)
            {
                if (bytes[(y * stride) + (x * 4) + 3] > 0)
                {
                    if (x < minX) { minX = x; }
                    if (x > maxX) { maxX = x; }
                    if (y < minY) { minY = y; }
                    if (y > maxY) { maxY = y; }
                }
            }
        }

        if (maxX < 0)
        {
            return new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        }

        // 手動位元組裁切：Bitmap.Clone 內部經 premultiply 往返，半透明像素會 ±1 失真（review 修正）
        var cropped = new Bitmap(maxX - minX + 1, maxY - minY + 1, PixelFormat.Format32bppArgb);
        var cropData = cropped.LockBits(
            new Rectangle(0, 0, cropped.Width, cropped.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < cropped.Height; y++)
            {
                Marshal.Copy(
                    bytes, ((minY + y) * stride) + (minX * 4),
                    cropData.Scan0 + (y * cropData.Stride), cropped.Width * 4);
            }
        }
        finally
        {
            cropped.UnlockBits(cropData);
        }

        return cropped;
    }

    /// <summary>把與參考色 Chebyshev 距離 ≤ tolerance 的像素設為透明（規格補六 JPG 簡易去背）。</summary>
    public static Bitmap RemoveBackground(Bitmap source, Color reference, int tolerance)
    {
        var result = ToArgb(source);
        var bytes = ReadArgb(result, out var stride);
        for (var y = 0; y < result.Height; y++)
        {
            for (var x = 0; x < result.Width; x++)
            {
                var i = (y * stride) + (x * 4);
                var maxDelta = Math.Max(
                    Math.Abs(bytes[i + 2] - reference.R),
                    Math.Max(Math.Abs(bytes[i + 1] - reference.G), Math.Abs(bytes[i] - reference.B)));
                if (maxDelta <= tolerance)
                {
                    bytes[i + 3] = 0;
                }
            }
        }

        WriteArgb(result, bytes);
        return result;
    }

    /// <summary>等比縮放進 targetSize×targetSize 透明畫布並置中（規格 §8：保持比例、不強制拉伸）。</summary>
    public static Bitmap ScaleProportional(Bitmap source, int targetSize)
    {
        var result = new Bitmap(targetSize, targetSize, PixelFormat.Format32bppArgb);
        var scale = Math.Min((double)targetSize / source.Width, (double)targetSize / source.Height);
        var w = Math.Max(1, (int)Math.Round(source.Width * scale));
        var h = Math.Max(1, (int)Math.Round(source.Height * scale));
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);
        g.DrawImage(source, (targetSize - w) / 2, (targetSize - h) / 2, w, h);
        return result;
    }

    private static Bitmap ToArgb(Bitmap source)
    {
        var clone = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        if (source.PixelFormat == PixelFormat.Format32bppArgb)
        {
            // 同格式：位元組直拷，完全繞過 GDI+ 繪製管線的 premultiply 取整（review 修正）
            var srcData = source.LockBits(
                new Rectangle(0, 0, source.Width, source.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var dstData = clone.LockBits(
                    new Rectangle(0, 0, clone.Width, clone.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var row = new byte[source.Width * 4];
                    for (var y = 0; y < source.Height; y++)
                    {
                        Marshal.Copy(srcData.Scan0 + (y * srcData.Stride), row, 0, row.Length);
                        Marshal.Copy(row, 0, dstData.Scan0 + (y * dstData.Stride), row.Length);
                    }
                }
                finally
                {
                    clone.UnlockBits(dstData);
                }
            }
            finally
            {
                source.UnlockBits(srcData);
            }

            return clone;
        }

        using var g = Graphics.FromImage(clone);
        g.CompositingMode = CompositingMode.SourceCopy; // 非 32bppArgb 來源：SourceCopy 繪製轉換
        g.DrawImageUnscaled(source, 0, 0);
        return clone;
    }

    private static byte[] ReadArgb(Bitmap bmp, out int stride)
    {
        var data = bmp.LockBits(
            new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            stride = data.Stride;
            var bytes = new byte[data.Stride * bmp.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
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
            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
