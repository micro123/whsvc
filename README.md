# WallhavenService

Windows-only WPF desktop application for periodically downloading a Wallhaven wallpaper and applying it to the desktop.

## Run

```powershell
dotnet run
```

The application starts as a tray application. Closing the settings window hides it; use the tray menu to exit.

## Current skeleton

- .NET 10 WPF desktop application
- Tray icon with open, run-now, and exit commands
- Configurable keyword list, purity (SFW/Sketchy/NSFW), categories (General/Anime/People), minimum resolution, aspect ratio, and interval
- In-memory one-time keyword override used by the next manual or automatic search
- `PeriodicTimer` scheduler with manual trigger and duplicate-run protection
- Wallhaven API search and image download
- Windows desktop wallpaper API integration
- Local JSON settings under `%AppData%\WallhavenService\settings.json`
- Balloon notifications for tray-visible status
- Interactive Windows notifications before search and after download, including a thumbnail preview, result metadata, and page URL actions
- Immediate startup search when settings are valid and no fresh cached wallpaper exists
- Failure notifications and automatic rotation shutdown after five consecutive failures

One keyword is selected randomly for each run. The Wallhaven API key is optional for public content; NSFW is disabled when no API key is configured. The app uses the API endpoint rather than scraping the HTML page.

When a next-search keyword is configured, it takes priority over the keyword list. It is kept only in memory, survives failed attempts, and is cleared after the first successful search. Restarting the application discards it.

The current wallpaper is stored as `%TEMP%\WallhavenService\current-wallpaper.<format>` and overwritten on each run. Its Wallhaven metadata is persisted atomically in `%TEMP%\WallhavenService\current-wallpaper.json`. On startup, a valid cache younger than the configured rotation interval is restored and the immediate search is skipped; the next automatic search waits only for the remaining interval. Use **保存当前图片** from the tray menu to copy it to `Pictures\wallhaven-<Wallhaven ID>.<format>`. If that exact file already exists, it is left unchanged. Random searches include a new six-character alphanumeric `seed` on every run.
