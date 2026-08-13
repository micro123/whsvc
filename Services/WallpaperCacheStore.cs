using System.IO;
using System.Text.Json;
using WallhavenService.Models;

namespace WallhavenService.Services;

public sealed class WallpaperCacheStore
{
    private const string MetadataFileName = "current-wallpaper.json";
    private readonly string _cacheDirectory = Path.Combine(Path.GetTempPath(), "WallhavenService");

    public string CacheDirectory => _cacheDirectory;

    public (WallpaperItem Wallpaper, string ImagePath, DateTime LastWriteTimeUtc)? Load()
    {
        try
        {
            var metadataPath = Path.Combine(_cacheDirectory, MetadataFileName);
            if (!File.Exists(metadataPath))
                return null;

            var cache = JsonSerializer.Deserialize<WallpaperCache>(File.ReadAllText(metadataPath));
            if (cache is null || !IsValidImageFileName(cache.ImageFileName))
                return null;

            var imagePath = Path.Combine(_cacheDirectory, cache.ImageFileName);
            if (!File.Exists(imagePath))
                return null;

            return (cache.Wallpaper, imagePath, File.GetLastWriteTimeUtc(imagePath));
        }
        catch
        {
            return null;
        }
    }

    public void Save(WallpaperItem wallpaper, string imagePath)
    {
        Directory.CreateDirectory(_cacheDirectory);
        var fullImagePath = Path.GetFullPath(imagePath);
        var expectedDirectory = Path.GetFullPath(_cacheDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullImagePath.StartsWith(expectedDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("临时壁纸文件不在应用缓存目录中。");

        var imageFileName = Path.GetFileName(fullImagePath);
        if (!IsValidImageFileName(imageFileName))
            throw new InvalidOperationException("临时壁纸文件名无效。");

        var cache = new WallpaperCache
        {
            Wallpaper = wallpaper,
            ImageFileName = imageFileName
        };
        var metadataPath = Path.Combine(_cacheDirectory, MetadataFileName);
        var temporaryPath = $"{metadataPath}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, metadataPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static bool IsValidImageFileName(string fileName)
    {
        if (!string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
            return false;

        var extension = Path.GetExtension(fileName);
        return fileName.StartsWith("current-wallpaper", StringComparison.OrdinalIgnoreCase) &&
               extension is ".jpg" or ".jpeg" or ".png";
    }
}
