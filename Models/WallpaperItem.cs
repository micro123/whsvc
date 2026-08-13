namespace WallhavenService.Models;

public sealed class WallpaperItem
{
    public required string Id { get; init; }
    public required string ImageUrl { get; init; }
    public required string SourceUrl { get; init; }
    public required string ThumbnailUrl { get; init; }
    public required string SearchTag { get; init; }
    public required string Resolution { get; init; }
    public required string Purity { get; init; }
}
