using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MousePilot.Models;
using MousePilot.Services;
using MousePilot.Views;

namespace MousePilot.ViewModels;

public partial class CursorSourceItem : ObservableObject
{
    public CursorSourceItem(CursorSource source, ImageSource? thumbnail, bool isFavorite)
    {
        Source = source;
        Thumbnail = thumbnail;
        _isFavorite = isFavorite;
    }

    public CursorSource Source { get; }

    public ImageSource? Thumbnail { get; }

    [ObservableProperty]
    private bool _isFavorite;

    public string DisplayName => Source.DisplayName;
}

/// <summary>
/// 游標編輯器（規格 §9/§10/補三~補八）。全邏輯可測：影像來源、WPF Cursor 建立、檔案列舉皆注入。
/// 管線順序固定：去背（左上角參考色）→ 裁切 → 縮放 → Write（Phase 7 移交約束）。預覽絕不動全域游標。
/// </summary>
public partial class CursorEditorViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly Func<string, Bitmap?> _imageLoader;
    private readonly Func<byte[], System.Windows.Input.Cursor?> _cursorFactory;
    private bool _applyingDefaults;

    public event Action? CloseRequested;

    public CursorEditorViewModel(
        AppSettings settings,
        Func<string, Bitmap?>? imageLoader = null,
        Func<IReadOnlyList<string>>? storedFilesProvider = null,
        Func<byte[], System.Windows.Input.Cursor?>? cursorFactory = null)
    {
        _settings = settings;
        _imageLoader = imageLoader ?? LoadBitmap;
        _cursorFactory = cursorFactory ?? CreateCursor;
        var storedFiles = (storedFilesProvider ?? DefaultStoredFiles)();

        Sources = new ObservableCollection<CursorSourceItem>(BuildSources(storedFiles));
        FavoriteSources = new ObservableCollection<CursorSourceItem>(Sources.Where(s => s.IsFavorite));
    }

    public ObservableCollection<CursorSourceItem> Sources { get; }

    public ObservableCollection<CursorSourceItem> FavoriteSources { get; }

    public IReadOnlyList<int> AllowedSizes => AppSettings.AllowedCursorSizes;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private CursorSourceItem? _selectedSource;

    [ObservableProperty]
    private int _selectedSize = 32;

    [ObservableProperty]
    private int _hotspotX;

    [ObservableProperty]
    private int _hotspotY;

    [ObservableProperty]
    private bool _removeBackgroundEnabled;

    [ObservableProperty]
    private int _tolerance = 30;

    [ObservableProperty]
    private ImageSource? _previewImage;

    [ObservableProperty]
    private System.Windows.Input.Cursor? _previewCursor;

    [ObservableProperty]
    private byte[]? _currentCurBytes;

    [ObservableProperty]
    private string _sourceSizeText = "—";

    [ObservableProperty]
    private string _warning = "";

    public bool CanEditProcessing => SelectedSource?.Source.Kind != CursorSourceKind.CursorFile && SelectedSource is not null;

    public bool CanRemoveBackground => SelectedSource?.Source.Kind == CursorSourceKind.ImageFile;

    public bool Confirmed { get; private set; }

    public void SetHotspot(int x, int y)
    {
        HotspotX = Math.Clamp(x, 0, SelectedSize - 1);
        HotspotY = Math.Clamp(y, 0, SelectedSize - 1);
    }

    partial void OnSelectedSourceChanged(CursorSourceItem? value)
    {
        if (value is null)
        {
            return;
        }

        _applyingDefaults = true;
        SelectedSize = value.Source.DefaultSize;
        RemoveBackgroundEnabled = false;
        var center = SelectedSize / 2;
        HotspotX = value.Source.HotspotTopLeft ? 0 : center;
        HotspotY = value.Source.HotspotTopLeft ? 0 : center;
        _applyingDefaults = false;
        OnPropertyChanged(nameof(CanEditProcessing));
        OnPropertyChanged(nameof(CanRemoveBackground));
        Rebuild();
    }

    partial void OnSelectedSizeChanged(int value)
    {
        if (_applyingDefaults)
        {
            return;
        }

        SetHotspot(HotspotX, HotspotY); // 重新夾制
        Rebuild();
    }

    partial void OnHotspotXChanged(int value)
    {
        if (!_applyingDefaults)
        {
            Rebuild();
        }
    }

    partial void OnHotspotYChanged(int value)
    {
        if (!_applyingDefaults)
        {
            Rebuild();
        }
    }

    partial void OnRemoveBackgroundEnabledChanged(bool value)
    {
        if (!_applyingDefaults)
        {
            Rebuild();
        }
    }

    partial void OnToleranceChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 255);
        if (clamped != value)
        {
            Tolerance = clamped; // 夾制後重新觸發本 handler 一次（等值防重入）
            return;
        }

        if (!_applyingDefaults)
        {
            Rebuild();
        }
    }

    [RelayCommand]
    private void ToggleFavorite()
    {
        if (SelectedSource is not { } item)
        {
            return;
        }

        item.IsFavorite = !item.IsFavorite;
        if (item.IsFavorite)
        {
            _settings.FavoriteCursors.Add(item.Source.Id);
            FavoriteSources.Add(item);
        }
        else
        {
            _settings.FavoriteCursors.Remove(item.Source.Id);
            FavoriteSources.Remove(item);
        }
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        var source = SelectedSource!.Source;
        if (source.Kind == CursorSourceKind.Preset)
        {
            _settings.CursorPreset = source.Id["preset:".Length..];
            _settings.CursorFile = ""; // 互斥：二擇一
        }
        else
        {
            _settings.CursorFile = source.FilePath!;
            _settings.CursorPreset = "";
        }

        _settings.CursorSize = SelectedSize;
        _settings.CursorHotspotX = HotspotX;
        _settings.CursorHotspotY = HotspotY;
        Confirmed = true;
        CloseRequested?.Invoke();
    }

    private bool CanConfirm() => SelectedSource is not null;

    private void Rebuild()
    {
        Warning = "";
        CurrentCurBytes = null;
        PreviewCursor = null;
        PreviewImage = null;
        SourceSizeText = "—";
        if (SelectedSource is not { } item)
        {
            return;
        }

        try
        {
            switch (item.Source.Kind)
            {
                case CursorSourceKind.Preset:
                {
                    using var rendered = CursorGallery.Render(item.Source.Id["preset:".Length..], SelectedSize);
                    FinishBuild(rendered);
                    break;
                }

                case CursorSourceKind.ImageFile:
                    RebuildFromImageFile(item.Source.FilePath!);
                    break;

                case CursorSourceKind.CursorFile:
                    RebuildFromCursorFile(item.Source.FilePath!);
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or OutOfMemoryException)
        {
            Warning = $"預覽建立失敗：{ex.Message}"; // 規格 §21：不 crash
        }
    }

    private void RebuildFromImageFile(string path)
    {
        // 不 Dispose：imageLoader 可能回傳共用/快取的 Bitmap（所有權留給呼叫端），VM 只 Dispose 自己產生的中介影像。
        var source = _imageLoader(path);
        if (source is null)
        {
            Warning = "圖片載入失敗（檔案可能已被移除或損毀）。";
            return;
        }

        SourceSizeText = $"{source.Width} x {source.Height}";
        Bitmap working = source;
        Bitmap? removed = null;
        try
        {
            if (RemoveBackgroundEnabled)
            {
                // 補六：預設參考色 = 原圖左上角像素（任何處理前取得）
                removed = CursorImageProcessor.RemoveBackground(source, source.GetPixel(0, 0), Tolerance);
                working = removed;
            }

            using var trimmed = CursorImageProcessor.TrimTransparent(working);
            // TrimTransparent 對「全透明」與「僅剩一個實心像素」都會收斂成 1x1，需再檢查該像素是否透明才能分辨（brief 原判斷式誤判合法單像素內容）。
            if (trimmed.Width == 1 && trimmed.Height == 1 && (source.Width > 1 || source.Height > 1) && trimmed.GetPixel(0, 0).A == 0)
            {
                Warning = "去背後沒有可見內容——請降低容差或關閉去背。"; // 退化防護（Phase 7 移交 (d) 前半）
                return;
            }

            // 1x1 內容直接實心填滿：GDI+ HighQualityBicubic 對 1x1 來源放大時無法達到完全不透明（環境實測 alpha 上限約 190/255），
            // ScaleProportional（既有元件，非本 task 範圍）本身即有此限制，故此處繞過內插改直接填色。
            using var scaled = trimmed.Width == 1 && trimmed.Height == 1
                ? FillSolid(trimmed.GetPixel(0, 0), SelectedSize)
                : CursorImageProcessor.ScaleProportional(trimmed, SelectedSize);
            FinishBuild(scaled);
        }
        finally
        {
            removed?.Dispose();
        }
    }

    private static Bitmap FillSolid(System.Drawing.Color color, int size)
    {
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(color);
        return bmp;
    }

    private void RebuildFromCursorFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var read = Path.GetExtension(path).ToLowerInvariant() == ".ani"
            ? CurFileFormat.TryReadAniFirstFrame(bytes)
            : CurFileFormat.TryReadFirstImage(bytes);
        if (read is { } ok)
        {
            SourceSizeText = $"{ok.Info.Width} x {ok.Info.Height}";
            _applyingDefaults = true;
            HotspotX = ok.Info.HotspotX; // 顯示檔內 hotspot（唯讀語意，控制項停用）
            HotspotY = ok.Info.HotspotY;
            _applyingDefaults = false;
            using var img = ok.Image;
            PreviewImage = BitmapInterop.ToBitmapSource(img);
        }
        else
        {
            Warning = "無法解析游標檔預覽（Windows 可能仍可套用）。";
        }

        CurrentCurBytes = bytes; // 原樣使用（Phase 9 走 LoadCursorFromFile）
        PreviewCursor = _cursorFactory(bytes);
    }

    private void FinishBuild(Bitmap finalBitmap)
    {
        CurrentCurBytes = CurFileFormat.Write(finalBitmap, HotspotX, HotspotY);
        PreviewImage = BitmapInterop.ToBitmapSource(finalBitmap);
        PreviewCursor = _cursorFactory(CurrentCurBytes);
    }

    private IEnumerable<CursorSourceItem> BuildSources(IReadOnlyList<string> storedFiles)
    {
        foreach (var preset in CursorGallery.Presets)
        {
            var id = $"preset:{preset.Id}";
            using var thumb = CursorGallery.Render(preset.Id, 32);
            yield return new CursorSourceItem(
                new CursorSource(id, preset.DisplayName, CursorSourceKind.Preset, null, preset.HotspotTopLeft, preset.DefaultSize),
                BitmapInterop.ToBitmapSource(thumb),
                _settings.FavoriteCursors.Contains(id));
        }

        foreach (var path in storedFiles)
        {
            var name = Path.GetFileName(path);
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var kind = ext is ".cur" or ".ani" ? CursorSourceKind.CursorFile : CursorSourceKind.ImageFile;
            var id = $"file:{name}";
            yield return new CursorSourceItem(
                new CursorSource(id, name, kind, path, HotspotTopLeft: false, DefaultSize: 32),
                thumbnail: null,
                _settings.FavoriteCursors.Contains(id));
        }
    }

    private static Bitmap? LoadBitmap(string path)
    {
        try
        {
            return new Bitmap(path);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or OutOfMemoryException
            or System.Runtime.InteropServices.ExternalException)
        {
            return null;
        }
    }

    private static System.Windows.Input.Cursor? CreateCursor(byte[] curBytes)
    {
        try
        {
            using var ms = new MemoryStream(curBytes);
            return new System.Windows.Input.Cursor(ms, scaleWithDpi: true);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or FormatException)
        {
            return null; // 預覽游標建立失敗 → 僅無游標預覽，不影響其他預覽
        }
    }

    private static IReadOnlyList<string> DefaultStoredFiles() => new CursorImportService().ListStored();
}
