namespace WallhavenService.Models;

public sealed class WallpaperCache
{
    public required WallpaperItem Wallpaper { get; init; }
    public required string ImageFileName { get; init; }
}
