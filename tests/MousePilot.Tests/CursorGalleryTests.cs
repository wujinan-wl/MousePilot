using MousePilot.Services;

namespace MousePilot.Tests;

public class CursorGalleryTests
{
    [Fact]
    public void 共16個圖案且Id唯一()
    {
        Assert.Equal(16, CursorGallery.Presets.Count);
        Assert.Equal(16, CursorGallery.Presets.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void 基本與可愛各8個()
    {
        Assert.Equal(8, CursorGallery.Presets.Count(p => p.Category == CursorCategory.Basic));
        Assert.Equal(8, CursorGallery.Presets.Count(p => p.Category == CursorCategory.Cute));
    }

    [Fact]
    public void 預設尺寸_基本32可愛48()
    {
        Assert.All(CursorGallery.Presets.Where(p => p.Category == CursorCategory.Basic), p => Assert.Equal(32, p.DefaultSize));
        Assert.All(CursorGallery.Presets.Where(p => p.Category == CursorCategory.Cute), p => Assert.Equal(48, p.DefaultSize));
    }

    [Fact]
    public void 預設Hotspot_僅箭頭與手指為左上()
    {
        Assert.Equal(
            new[] { "Arrow", "Hand" },
            CursorGallery.Presets.Where(p => p.HotspotTopLeft).Select(p => p.Id).ToArray());
    }

    [Fact]
    public void 包含規格範例Id_CuteRobotCat()
    {
        Assert.Contains(CursorGallery.Presets, p => p.Id == "CuteRobotCat");
    }

    [Theory]
    [InlineData(16)]
    [InlineData(48)]
    public void 全部圖案可繪製且非全透明(int size)
    {
        foreach (var preset in CursorGallery.Presets)
        {
            using var bmp = CursorGallery.Render(preset.Id, size);
            Assert.Equal(size, bmp.Width);
            Assert.Equal(size, bmp.Height);
            var hasContent = false;
            for (var y = 0; y < size && !hasContent; y++)
            {
                for (var x = 0; x < size && !hasContent; x++)
                {
                    hasContent = bmp.GetPixel(x, y).A > 0;
                }
            }

            Assert.True(hasContent, $"{preset.Id} 繪製結果全透明");
        }
    }

    [Fact]
    public void 未知Id擲ArgumentException()
    {
        Assert.Throws<ArgumentException>(() => CursorGallery.Render("Nope", 32));
    }
}
