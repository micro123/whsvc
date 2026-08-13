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
- `PeriodicTimer` scheduler with manual trigger and duplicate-run protection
- Wallhaven API search and image download
- Windows desktop wallpaper API integration
- Local JSON settings under `%AppData%\WallhavenService\settings.json`
- Balloon notifications for tray-visible status

One keyword is selected randomly for each run. The Wallhaven API key is optional for public content; NSFW is disabled when no API key is configured. The app uses the API endpoint rather than scraping the HTML page.

The current wallpaper is stored as `%TEMP%\WallhavenService\current-wallpaper.<format>` and overwritten on each run. Its Wallhaven ID is kept in memory. Use **保存当前图片** from the tray menu to copy it to `Pictures\wallhaven-<Wallhaven ID>.<format>`. If that exact file already exists, it is left unchanged. Random searches include a new six-character alphanumeric `seed` on every run.
