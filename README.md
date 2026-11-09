# Roblox Mod Manager

A Windows launcher for Roblox in the style of Fishstrap and Bloxstrap. It installs and updates Roblox itself, applies your mods and fast flags every time you play, and puts Roblox's settings in one window.

## Build it

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (x64) on Windows.

- **Easiest:** double-click `build.bat`. The app lands in `publish\RobloxModManager.exe`.
- **Visual Studio 2022:** open `RobloxModManager.sln` and press F5.
- **Command line:**
  ```
  dotnet publish src\RobloxModManager\RobloxModManager.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
  ```
- **No SDK?** Push this folder to a GitHub repository. The included workflow (`.github/workflows/build.yml`) builds the exe on every push. Download it from the run's **Artifacts** section.

The exe needs the .NET 8 Desktop Runtime to run. Windows offers to download it the first time if it's missing.

To make a version that runs without the runtime installed, change `--self-contained false` to `--self-contained true` (the exe grows to roughly 150 MB).

## Using it

1. Run `RobloxModManager.exe`. The installer asks where to put it (default `%LocalAppData%\RobloxModManager`) and which shortcuts to add.
2. From then on, joining a game on roblox.com starts it automatically. It downloads or updates Roblox, applies mods and flags, and starts the game.
3. Open **Configure settings** from the Start menu or desktop shortcut to change anything.

To uninstall, use Windows **Settings > Apps**, or **Settings > About > Uninstall** inside the app. Roblox links go back to the official player if it's installed.

## Features you won't find elsewhere

| Feature | What it does |
| --- | --- |
| Smart mod drop | Drag sounds, images, fonts or a whole `.zip` mod pack onto the Mods page. Each file is matched by name to the Roblox file it replaces and put in the right folder. Zip packs with an extra wrapper folder are handled, and a font Roblox doesn't have can become the custom font. |
| Game profiles | Give one game its own fast flags, or turn mods off only for it. The profile switches on by itself when you join that game from the website. Your normal flags are never edited. |
| Play history | Reads Roblox's own logs to record time per game, including teleports. Shows the last 7 days, all-time totals, a day streak and each game's share. Names come from Roblox's public API and are cached. |
| Break reminders | An optional corner notice after 30 minutes to 3 hours of play. It doesn't take focus or pause the game, and has a "remind me later" option. |
| Safe-mode launch | Hold **Shift** while Roblox starts to launch once without any mods or flags. A fast way to check whether a mod is breaking a game. |

## What's in it

| Area | What it does |
| --- | --- |
| Installer | Picks the folder, takes over `roblox:` and `roblox-player:` links, adds shortcuts and an Apps & features entry. Running a newer exe updates an existing install in place. |
| Launch window | Three styles: Fluent, Classic and Terminal (live log). Shows progress on the taskbar, and Cancel works at any point. |
| Updating Roblox | Picks the fastest of Roblox's CDN mirrors and downloads packages 4 at a time. Each file is checked against its MD5 hash and retried if damaged. Unchanged packages are reused from the cache or from Roblox's own downloads. Old versions are removed. |
| Mods | Everything in `Mods\` is copied over Roblox on each launch. The original file is backed up first, so deleting a mod restores it. Includes a custom font option and a list of your mod files. |
| Fast flags | Presets for rendering mode, MSAA, display scaling, exclusive fullscreen, fixed quality level and mesh detail. A full editor adds flags by hand or imports a JSON paste, with name checks and overwrite warnings. |
| Engine settings | Edits Roblox's `GlobalBasicSettings_13.xml`: graphics quality, FPS cap, UI transparency, reduced motion, text size, mouse sensitivity and VR. Can lock the file so Roblox doesn't overwrite it. |
| Integrations | Starts your own programs with Roblox, and optionally closes them when Roblox exits. |
| Behaviour | Asks before opening a second game. A cleaner deletes old logs and cache. |
| Deployment | Shows the installed version, sets the Roblox channel (with a check button) and can force a reinstall. |
| Appearance | Light, dark or follow-Windows theme. Launch window style (with preview) and title. |

## Compared with Fishstrap

This covers Fishstrap's core: installing and updating, mods, fast flags, global settings, integrations, launch window styles, shortcuts and the uninstaller. Not built yet:

- Discord Rich Presence and activity tracking (server location, server history)
- BetterMatchmaking, and access to your Roblox account
- Roblox Studio support
- The custom launch window theme editor
- Bundled mod presets (old cursors, old death sound, emoji style)
- Self-updating from GitHub releases
- Translations

## Project layout

```
src/RobloxModManager/
  Core/            Windows-independent logic: deployment, packages, mods, flags, settings
  UI/              WPF windows and pages
    Bootstrapper/  launch window
    Installer/     first-run setup
    Settings/      settings window and its pages
    Themes/        light and dark colours, control styles
  App.xaml(.cs)    startup: decides between installer, menu, settings and launching
tests/CoreTests/   end-to-end tests for Core against a mock Roblox CDN
```

Rename the app by changing `ProjectName` and `DisplayName` in `Core/AppInfo.cs` (and `AssemblyName` in the `.csproj` to match).

## Tests

```
dotnet run --project tests/CoreTests
```

The 130 checks cover a fresh install, updates, forced reinstall, corrupt downloads, cancelling mid-download, two launches at once, applying and restoring mods, the custom font, fast flags, global settings, the cleaner, place IDs in launch links, log parsing (joins, teleports, leaves), play history, game profiles, safe mode, smart mod placement, and rejection of zip files that try to write outside their folder. They use a local fake of Roblox's servers, so they run anywhere without internet.

## Credits

Based on the design of [Fishstrap](https://github.com/fishstrap/fishstrap) and [Bloxstrap](https://github.com/bloxstraplabs/bloxstrap), with package handling following [Roblox Studio Mod Manager](https://github.com/MaximumADHD/Roblox-Studio-Mod-Manager). All are MIT licensed; see `LICENSE`. Not affiliated with Roblox Corporation.
