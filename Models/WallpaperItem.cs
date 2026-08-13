namespace WallhavenService.Models;

public sealed class WallpaperItem
{
    public required string Id { get; init; }
    public required string ImageUrl { get; init; }
    public string? SourceUrl { get; init; }
}
