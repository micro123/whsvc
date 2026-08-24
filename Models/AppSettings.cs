namespace WallhavenService.Models;

public sealed class AppSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public List<string> SearchKeywords { get; set; } = [];
    public string MinimumResolution { get; set; } = "1920x1080";
    public string AspectRatio { get; set; } = "16x9";
    public bool IncludeSfw { get; set; } = true;
    public bool IncludeSketchy { get; set; }
    public bool IncludeNsfw { get; set; }
    public bool IncludeGeneral { get; set; } = true;
    public bool IncludeAnime { get; set; } = true;
    public bool IncludePeople { get; set; } = true;
    public bool ScheduleEnabled { get; set; } = true;
    public bool RotateOnStartup { get; set; }
    public bool StartMinimized { get; set; } = true;
    public int IntervalMinutes { get; set; } = 30;
}
