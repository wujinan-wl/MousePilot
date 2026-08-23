namespace MousePilot.Models;

public enum CursorSourceKind
{
    Preset,
    ImageFile,
    CursorFile,
}

/// <summary>游標編輯器的來源項（Id 格式沿用收藏：preset:&lt;GalleryId&gt; / file:&lt;檔名&gt;，規格補八）。</summary>
public sealed record CursorSource(
    string Id,
    string DisplayName,
    CursorSourceKind Kind,
    string? FilePath,
    bool HotspotTopLeft,
    int DefaultSize);
