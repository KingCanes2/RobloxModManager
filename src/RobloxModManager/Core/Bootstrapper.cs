using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace RobloxModManager.Core;

/// <summary>What the bootstrapper needs from a window. Implementations must be safe to call from any thread.</summary>
public interface IBootstrapperUi
{
    void SetStatus(string message);

    /// <summary>0-100 for a determinate bar, or a negative number for an indeterminate one.</summary>
    void SetProgress(int percent);

    void SetCancelEnabled(bool enabled);

    /// <summary>Asks a yes/no question and waits for the answer.</summary>
    bool Confirm(string message);
}

/// <summary>An error whose message is meant to be shown to the user as-is.</summary>
public class BootstrapperException : Exception
{
    public BootstrapperException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Installs or updates Roblox (download, verify, extract), applies mods and fast flags, then starts the player.
/// </summary>
public sealed class RobloxBootstrapper : IDisposable
{
    public const string PlayerExecutable = "RobloxPlayerBeta.exe";

    private const string Ident = "Bootstrapper";
    private const int MaxParallelDownloads = 4;
    private const string WebView2InstallerPackage = "WebView2RuntimeInstaller.zip";

    private const string AppSettingsXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n" +
        "<Settings>\r\n" +
        "\t<ContentFolder>content</ContentFolder>\r\n" +
        "\t<BaseUrl>http://www.roblox.com</BaseUrl>\r\n" +
        "</Settings>\r\n";

    private readonly AppSettings _settings;
    private readonly AppState _state;
    private readonly Deployment _deployment;
    private readonly IBootstrapperUi? _ui;
    private readonly CancellationTokenSource _cts = new();

    private long _workDone;
    private long _workTotal = 1;
    private int _lastPercent = -2;

    public RobloxBootstrapper(AppSettings settings, AppState state, Deployment deployment, IBootstrapperUi? ui = null)
    {
        _settings = settings;
        _state = state;
        _deployment = deployment;
        _ui = ui;
    }

    public string VersionDirectory { get; private set; } = "";
    public string ExecutablePath => Path.Combine(VersionDirectory, PlayerExecutable);
    public ClientVersion? LatestVersion { get; private set; }

    public void Cancel()
    {
        try { _cts.Cancel(); } catch (ObjectDisposedException) { /* already finished */ }
    }

    public void Dispose() => _cts.Dispose();

    // ------------------------------------------------------------------ public flow

    /// <summary>Makes sure the newest Roblox is installed and modded. Returns the path of the player exe.</summary>
    public async Task<string> PrepareAsync()
    {
        CancellationToken ct = _cts.Token;
        Directory.CreateDirectory(Paths.Base);

        using var installLock = await AcquireLockAsync(ct);

        SetStatus("Connecting to Roblox...");
        SetProgress(-1);

        await _deployment.InitializeConnectivityAsync(ct);

        ClientVersion info = await _deployment.GetInfoAsync(ct);
        LatestVersion = info;
        VersionDirectory = Paths.VersionDirectory(info.VersionGuid);
        Logger.WriteLine(Ident, $"Latest version is {info.Version} ({info.VersionGuid}) on channel {_deployment.Channel}");

        bool upToDate = !_state.ForceReinstall
            && string.Equals(_state.VersionGuid, info.VersionGuid, StringComparison.Ordinal)
            && File.Exists(ExecutablePath);

        if (upToDate)
            Logger.WriteLine(Ident, "Roblox is up to date");
        else
            await InstallAsync(info, ct);

        SetStatus("Applying modifications...");
        SetProgress(-1);
        await Task.Run(() => ApplyModifications(ct), ct);

        CleanupOldVersions(info.VersionGuid);

        return ExecutablePath;
    }

    public Process StartRoblox(string launchArguments)
    {
        SetStatus("Starting Roblox...");
        SetProgress(-1);
        SetCancelEnabled(false);

        Logger.WriteLine(Ident, "Launching " + ExecutablePath);

        var startInfo = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            Arguments = launchArguments,
            WorkingDirectory = VersionDirectory
        };

        return Process.Start(startInfo) ?? throw new BootstrapperException("Roblox could not be started.");
    }

    // ------------------------------------------------------------------ install

    private async Task InstallAsync(ClientVersion info, CancellationToken ct)
    {
        bool firstInstall = string.IsNullOrEmpty(_state.VersionGuid);
        SetStatus(firstInstall ? "Installing Roblox..." : "Updating Roblox...");
        SetProgress(-1);

        string manifestUrl = _deployment.GetLocation($"/{info.VersionGuid}-rbxPkgManifest.txt");
        var manifest = new PackageManifest(await GetStringWithRetryAsync(manifestUrl, ct));

        if (!manifest.Exists(p => p.Name.Equals("RobloxApp.zip", StringComparison.OrdinalIgnoreCase)))
            throw new BootstrapperException("This Roblox version does not contain the player. Check that your channel is correct.");

        Directory.CreateDirectory(Paths.Downloads);
        Directory.CreateDirectory(Paths.Versions);

        CheckDiskSpace(manifest);

        if (Directory.Exists(VersionDirectory))
        {
            if (RobloxProcess.IsRunning())
                throw new BootstrapperException("Close Roblox before reinstalling it.");

            try { Directory.Delete(VersionDirectory, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new BootstrapperException("The existing Roblox folder could not be removed. Close Roblox and try again.", ex);
            }
        }

        Directory.CreateDirectory(VersionDirectory);

        // A fresh version folder holds pristine files, so earlier mod bookkeeping no longer applies.
        _state.ModManifest = new List<string>();
        TryDeleteDirectory(Paths.ModBackups);

        var packages = manifest.OrderByDescending(p => p.PackedSize).ToList();
        _workTotal = Math.Max(1, packages.Sum(p => p.PackedSize) * 2); // every byte is downloaded, then extracted
        _workDone = 0;
        _lastPercent = -2;
        SetProgress(0);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var throttle = new SemaphoreSlim(MaxParallelDownloads);

        var tasks = packages
            .Select(p => (Task)ProcessPackageAsync(p, info.VersionGuid, throttle, linked.Token))
            .ToList();

        await WhenAllFailFastAsync(tasks, linked);

        SetProgress(-1);
        SetStatus("Configuring Roblox...");
        await EnsureWebView2Async(manifest, ct);

        _state.VersionGuid = info.VersionGuid;
        _state.Version = info.Version;
        _state.PackageHashes = new Dictionary<string, string>();
        foreach (var package in manifest)
            _state.PackageHashes[package.Name] = package.Signature;
        _state.ForceReinstall = false;
        _state.Save();

        CleanupDownloads(manifest);
        Logger.WriteLine(Ident, "Installation finished");
    }

    private void CheckDiskSpace(PackageManifest manifest)
    {
        long required = manifest.Where(p => !File.Exists(p.DownloadPath)).Sum(p => p.PackedSize) + manifest.Sum(p => p.Size);

        try
        {
            string root = Path.GetPathRoot(Path.GetFullPath(Paths.Base))!;
            var drive = new DriveInfo(root);

            if (drive.AvailableFreeSpace < required)
                throw new BootstrapperException($"Not enough disk space. Roblox needs about {FileSize.Format(required)} free on {drive.Name}.");
        }
        catch (BootstrapperException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.WriteLine(Ident, $"Could not check free disk space: {ex.Message}");
        }
    }

    private async Task ProcessPackageAsync(Package package, string versionGuid, SemaphoreSlim throttle, CancellationToken ct)
    {
        await throttle.WaitAsync(ct);
        try { await DownloadPackageAsync(package, versionGuid, ct); }
        finally { throttle.Release(); }

        // The WebView2 installer is only unpacked if the runtime turns out to be missing.
        if (!package.Name.Equals(WebView2InstallerPackage, StringComparison.OrdinalIgnoreCase))
            await Task.Run(() => ExtractPackage(package, VersionDirectory, ct), ct);

        AddWork(package.PackedSize);
    }

    private static async Task WhenAllFailFastAsync(List<Task> tasks, CancellationTokenSource cts)
    {
        var pending = new List<Task>(tasks);

        while (pending.Count > 0)
        {
            Task finished = await Task.WhenAny(pending);
            pending.Remove(finished);

            if (finished.IsFaulted || finished.IsCanceled)
            {
                cts.Cancel();

                foreach (Task other in pending)
                {
                    try { await other; } catch { /* already failing */ }
                }

                await finished; // rethrow the original failure
            }
        }
    }

    private async Task DownloadPackageAsync(Package package, string versionGuid, CancellationToken ct)
    {
        string ident = $"{Ident}::Download[{package.Name}]";
        string target = package.DownloadPath;

        if (File.Exists(target))
        {
            if (Md5Hash.FromFile(target).Equals(package.Signature, StringComparison.OrdinalIgnoreCase))
            {
                Logger.WriteLine(ident, "Already downloaded");
                AddWork(package.PackedSize);
                return;
            }

            Logger.WriteLine(ident, "Cached copy is corrupt, downloading again");
            File.Delete(target);
        }

        // Roblox's own installer keeps the same zips under the same hash; reuse them if present.
        string official = Path.Combine(Paths.Roblox, "Downloads", package.Signature);
        if (File.Exists(official) && Md5Hash.FromFile(official).Equals(package.Signature, StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(official, target, true);
            Logger.WriteLine(ident, "Copied from Roblox's own download cache");
            AddWork(package.PackedSize);
            return;
        }

        string url = _deployment.GetLocation($"/{versionGuid}-{package.Name}");
        string part = target + ".part";
        const int maxTries = 5;

        for (int attempt = 1; ; attempt++)
        {
            long counted = 0;

            try
            {
                using var response = await Http.Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

                await using (var stream = await response.Content.ReadAsStreamAsync(ct))
                await using (var file = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    var buffer = new byte[81920];
                    int read;

                    while ((read = await stream.ReadAsync(buffer, ct)) > 0)
                    {
                        await file.WriteAsync(buffer.AsMemory(0, read), ct);
                        hash.AppendData(buffer, 0, read);
                        counted += read;
                        AddWork(read);
                    }
                }

                string actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();

                if (!actual.Equals(package.Signature, StringComparison.OrdinalIgnoreCase))
                    throw new ChecksumFailedException($"Downloaded {package.Name} is corrupt (expected {package.Signature}, got {actual}).");

                File.Move(part, target, true);
                Logger.WriteLine(ident, $"Downloaded {FileSize.Format(counted)}");
                return;
            }
            catch (OperationCanceledException)
            {
                TryDeleteFile(part);
                throw;
            }
            catch (Exception ex)
            {
                TryDeleteFile(part);
                AddWork(-counted);
                Logger.WriteLine(ident, $"Attempt {attempt}/{maxTries} failed: {ex.Message}");

                if (attempt >= maxTries)
                    throw;

                await Task.Delay(TimeSpan.FromSeconds(Math.Min(attempt * 2, 8)), ct);
            }
        }
    }

    /// <summary>Extracts a package into its mapped folder, refusing any entry that would escape that folder.</summary>
    public static void ExtractPackage(Package package, string versionDirectory, CancellationToken ct)
    {
        string map = PackageMaps.Resolve(package.Name, out bool known);
        if (!known)
            Logger.WriteLine(Ident, $"WARNING: {package.Name} is not in the package map, extracting to '{(map.Length == 0 ? "(root)" : map)}'");

        string destination = Path.GetFullPath(PackageMaps.Combine(versionDirectory, map));
        string destinationPrefix = destination.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(destination);

        using var archive = ZipFile.OpenRead(package.DownloadPath);

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();

            string relative = entry.FullName.Replace('\\', '/');
            bool isDirectory = relative.EndsWith('/');
            string target = Path.GetFullPath(Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar)));

            if (!target.StartsWith(destinationPrefix, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(target.TrimEnd(Path.DirectorySeparatorChar), destination.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"{package.Name} contains an unsafe path ({entry.FullName}).");
            }

            if (isDirectory)
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            try
            {
                entry.ExtractToFile(target, true);
            }
            catch (Exception ex) when (target.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase))
            {
                // A few font files fail to extract on some machines; Roblox still runs without them.
                Logger.WriteLine(Ident, $"Could not extract {entry.FullName}: {ex.Message}");
            }
        }
    }

    private static async Task<string> GetStringWithRetryAsync(string url, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var response = await Http.Client.GetAsync(url, ct);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(ct);
            }
            catch (Exception ex) when (ex is HttpRequestException && attempt < 3)
            {
                Logger.WriteLine(Ident, $"Request failed ({attempt}/3): {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(attempt), ct);
            }
        }
    }

    private async Task EnsureWebView2Async(PackageManifest manifest, CancellationToken ct)
    {
        if (!_state.PromptWebView2Install || !OperatingSystem.IsWindows() || IsWebView2Installed())
            return;

        Package? package = manifest.Find(p => p.Name.Equals(WebView2InstallerPackage, StringComparison.OrdinalIgnoreCase));
        if (package is null)
        {
            Logger.WriteLine(Ident, "WebView2 is missing, but this Roblox version has no installer package");
            return;
        }

        bool install = _ui?.Confirm("Roblox uses the Microsoft Edge WebView2 Runtime for some in-app screens, and it is not installed on this PC. Install it now?") ?? false;
        if (!install)
        {
            _state.PromptWebView2Install = false; // do not ask again
            return;
        }

        string folder = PackageMaps.Combine(VersionDirectory, PackageMaps.Resolve(package.Name, out _));
        await Task.Run(() => ExtractPackage(package, VersionDirectory, ct), ct);

        SetStatus("Installing the WebView2 runtime...");

        using var process = Process.Start(new ProcessStartInfo
        {
            WorkingDirectory = folder,
            FileName = Path.Combine(folder, "MicrosoftEdgeWebview2Setup.exe"),
            Arguments = "/silent /install"
        });

        if (process is not null)
            await process.WaitForExitAsync(ct);

        TryDeleteDirectory(folder);
    }

    [SupportedOSPlatform("windows")]
    private static bool IsWebView2Installed()
    {
        const string clientId = "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

        using var machine = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\" + clientId);
        using var user = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\EdgeUpdate\Clients\" + clientId);

        return machine is not null || user is not null;
    }

    // ------------------------------------------------------------------ mods and flags

    private bool ShouldSkipMod(string relative)
    {
        bool isFlagsFile = relative.Equals("ClientSettings/ClientAppSettings.json", StringComparison.OrdinalIgnoreCase);
        return isFlagsFile ? !_settings.UseFastFlagManager : !_settings.ApplyMods;
    }

    private void ApplyModifications(CancellationToken ct)
    {
        Directory.CreateDirectory(Paths.Mods);
        EnsureModsReadme();

        // Roblox needs AppSettings.xml; a mod may provide its own.
        if (!File.Exists(Path.Combine(Paths.Mods, "AppSettings.xml")))
            File.WriteAllText(Path.Combine(VersionDirectory, "AppSettings.xml"), AppSettingsXml);

        if (_settings.ApplyMods)
        {
            int families = CustomFont.GenerateFamilies(VersionDirectory);
            if (families > 0)
                Logger.WriteLine(Ident, $"Custom font: generated {families} font families");
        }

        var result = ModManager.Apply(VersionDirectory, Paths.Mods, Paths.ModBackups, _state.ModManifest, ShouldSkipMod, ct);

        _state.ModManifest = result.Applied;
        _state.Save();

        Logger.WriteLine(Ident, $"Mods: {result.Copied} copied, {result.Restored} restored, {result.Failures.Count} failed");
    }

    private static void EnsureModsReadme()
    {
        string readme = Path.Combine(Paths.Mods, "README.txt");
        if (File.Exists(readme))
            return;

        try
        {
            File.WriteAllText(readme,
                "Files in this folder replace files inside Roblox every time it starts.\r\n" +
                "Keep the same folder structure as Roblox, for example:\r\n\r\n" +
                "    content\\sounds\\ouch.ogg\r\n" +
                "    content\\textures\\ArrowCursor.png\r\n\r\n" +
                "Delete a file from this folder and the original is restored on the next launch.\r\n" +
                "Fast flags are managed from the settings window (they are stored in ClientSettings\\ClientAppSettings.json).\r\n");
        }
        catch
        {
            // purely informational
        }
    }

    // ------------------------------------------------------------------ cleanup

    private static void CleanupOldVersions(string keepGuid)
    {
        if (!Directory.Exists(Paths.Versions) || RobloxProcess.IsRunning())
            return; // never delete files out from under a running game

        foreach (string directory in Directory.GetDirectories(Paths.Versions))
        {
            if (string.Equals(Path.GetFileName(directory), keepGuid, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                Directory.Delete(directory, true);
                Logger.WriteLine(Ident, $"Removed old version {Path.GetFileName(directory)}");
            }
            catch (Exception ex)
            {
                Logger.WriteLine(Ident, $"Could not remove {directory}: {ex.Message}");
            }
        }
    }

    private static void CleanupDownloads(PackageManifest manifest)
    {
        if (!Directory.Exists(Paths.Downloads))
            return;

        var keep = new HashSet<string>(manifest.Select(p => p.Signature), StringComparer.OrdinalIgnoreCase);

        foreach (string file in Directory.GetFiles(Paths.Downloads))
        {
            if (!keep.Contains(Path.GetFileName(file)))
                TryDeleteFile(file);
        }
    }

    // ------------------------------------------------------------------ helpers

    private async Task<FileStream> AcquireLockAsync(CancellationToken ct)
    {
        bool announced = false;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                return new FileStream(Paths.BootstrapperLock, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (!announced)
                {
                    SetStatus("Waiting for another installation to finish...");
                    announced = true;
                }

                await Task.Delay(500, ct);
            }
        }
    }

    private void AddWork(long delta)
    {
        long done = Interlocked.Add(ref _workDone, delta);
        int percent = (int)Math.Clamp(done * 100 / _workTotal, 0, 100);

        if (Interlocked.Exchange(ref _lastPercent, percent) != percent)
            _ui?.SetProgress(percent);
    }

    private void SetStatus(string message)
    {
        Logger.WriteLine(Ident, message);
        _ui?.SetStatus(message);
    }

    private void SetProgress(int percent) => _ui?.SetProgress(percent);

    private void SetCancelEnabled(bool enabled) => _ui?.SetCancelEnabled(enabled);

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { /* best effort */ }
    }
}
