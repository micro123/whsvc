using System.Net.Http.Headers;
using System.Net.Http;
using System.Text.Json;
using System.IO;
using WallhavenService.Models;

namespace WallhavenService.Services;

public sealed class WallhavenClient : IDisposable
{
    private const string SeedCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    private readonly HttpClient _httpClient = new()
    {
        BaseAddress = new Uri("https://wallhaven.cc/")
    };

    public async Task<WallpaperItem> FindWallpaperAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var categories = string.Concat(
            settings.IncludeGeneral ? '1' : '0',
            settings.IncludeAnime ? '1' : '0',
            settings.IncludePeople ? '1' : '0');
        var purity = string.Concat(
            settings.IncludeSfw ? '1' : '0',
            settings.IncludeSketchy ? '1' : '0',
            settings.IncludeNsfw && !string.IsNullOrWhiteSpace(settings.ApiKey) ? '1' : '0');

        var query = new List<string>
        {
            $"categories={categories}",
            $"purity={purity}",
            "sorting=random",
            $"seed={CreateSeed()}",
            "page=1"
        };
        var keyword = settings.SearchKeywords
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .OrderBy(_ => Random.Shared.Next())
            .FirstOrDefault();
        if (keyword is not null)
            query.Add($"q={Uri.EscapeDataString(keyword)}");
        if (!string.IsNullOrWhiteSpace(settings.MinimumResolution))
            query.Add($"atleast={Uri.EscapeDataString(settings.MinimumResolution)}");
        if (!string.IsNullOrWhiteSpace(settings.AspectRatio))
            query.Add($"ratios={Uri.EscapeDataString(settings.AspectRatio)}");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/search?{string.Join('&', query)}");
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            request.Headers.Add("X-API-Key", settings.ApiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
            throw new InvalidOperationException("Wallhaven 没有返回符合条件的壁纸。");

        var item = data[Random.Shared.Next(data.GetArrayLength())];
        return new WallpaperItem
        {
            Id = item.GetProperty("id").GetString() ?? throw new InvalidOperationException("壁纸缺少 ID。"),
            ImageUrl = item.GetProperty("path").GetString() ?? throw new InvalidOperationException("壁纸缺少下载地址。"),
            SourceUrl = item.TryGetProperty("url", out var source) ? source.GetString() : null
        };
    }

    public async Task<string> DownloadCurrentAsync(WallpaperItem item, string directory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var extension = Path.GetExtension(new Uri(item.ImageUrl).AbsolutePath);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 5)
            extension = ".jpg";
        var path = Path.Combine(directory, $"current-wallpaper{extension.ToLowerInvariant()}");
        var downloadPath = $"{path}.download";

        try
        {
            using var response = await _httpClient.GetAsync(item.ImageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var target = File.Create(downloadPath))
                await source.CopyToAsync(target, cancellationToken);

            File.Move(downloadPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(downloadPath))
                File.Delete(downloadPath);
        }

        return path;
    }

    public void Dispose() => _httpClient.Dispose();

    private static string CreateSeed() => new(
        Enumerable.Range(0, 6)
            .Select(_ => SeedCharacters[Random.Shared.Next(SeedCharacters.Length)])
            .ToArray());
}
