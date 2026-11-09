using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using RobloxModManager.Core;

static class T
{
    public static int Passed, Failed;
    public static void Check(bool ok, string name)
    {
        if (ok) { Passed++; Console.WriteLine("  PASS  " + name); }
        else { Failed++; Console.WriteLine("  FAIL  " + name); }
    }
    public static void Section(string s) => Console.WriteLine($"\n== {s}");
}

sealed class FakeUi : IBootstrapperUi
{
    public List<string> Statuses = new();
    public List<int> Progress = new();
    public void SetStatus(string m) { lock (Statuses) Statuses.Add(m); }
    public void SetProgress(int p) { lock (Progress) Progress.Add(p); }
    public void SetCancelEnabled(bool e) { }
    public bool Confirm(string m) => false;
}

sealed class MockCdn : IDisposable
{
    readonly HttpListener _listener = new();
    public string Url { get; }
    public string VersionGuid = "version-aaaa1111bbbb2222";
    public string Version = "0.600.0.1";
    public Dictionary<string, byte[]> Packages = new();
    public HashSet<string> CorruptOnce = new();
    public int PackageRequests;
    public int PackageDelayMs;

    public MockCdn()
    {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();
        Url = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add(Url + "/");
        _listener.Start();
        _ = Task.Run(async () =>
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); } catch { break; }
                _ = Task.Run(() => Handle(ctx));
            }
        });
    }

    public static string Md5(byte[] b) => Convert.ToHexString(MD5.HashData(b)).ToLowerInvariant();

    public static byte[] Zip(params (string Name, string Content)[] files)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var f in files)
            {
                var e = zip.CreateEntry(f.Name);
                using var w = new StreamWriter(e.Open());
                w.Write(f.Content);
            }
        return ms.ToArray();
    }

    byte[] Manifest()
    {
        var sb = new StringBuilder("v0\r\n");
        foreach (var (name, bytes) in Packages)
            sb.Append(name).Append("\r\n").Append(Md5(bytes)).Append("\r\n").Append(bytes.Length).Append("\r\n").Append(bytes.Length * 2).Append("\r\n");
        sb.Append("RobloxPlayerLauncher.exe\r\n").Append(new string('a', 32)).Append("\r\n1000\r\n2000\r\n");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    async Task Handle(HttpListenerContext ctx)
    {
        try
        {
            string path = ctx.Request.Url!.AbsolutePath;
            byte[]? body = null;

            if (path == "/versionStudio") body = Encoding.ASCII.GetBytes("version-012732894899482c");
            else if (path == "/universes/v1/places/606849621/universe") body = Encoding.UTF8.GetBytes("{\"universeId\":994732206}");
            else if (path == "/v1/games" && ctx.Request.QueryString["universeIds"] == "994732206")
                body = Encoding.UTF8.GetBytes("{\"data\":[{\"id\":994732206,\"name\":\"Jailbreak\"}]}");
            else if (path == "/v2/client-version/WindowsPlayer")
                body = Encoding.UTF8.GetBytes($"{{\"version\":\"{Version}\",\"clientVersionUpload\":\"{VersionGuid}\",\"bootstrapperVersion\":\"1, 0, 0, 1\"}}");
            else if (path == $"/{VersionGuid}-rbxPkgManifest.txt") body = Manifest();
            else if (path.StartsWith($"/{VersionGuid}-"))
            {
                string name = path[(VersionGuid.Length + 2)..];
                if (Packages.TryGetValue(name, out var bytes))
                {
                    Interlocked.Increment(ref PackageRequests);
                    if (PackageDelayMs > 0) await Task.Delay(PackageDelayMs);
                    if (CorruptOnce.Remove(name)) { bytes = (byte[])bytes.Clone(); bytes[^1] ^= 0xFF; }
                    body = bytes;
                }
            }

            ctx.Response.StatusCode = body is null ? 404 : 200;
            body ??= Array.Empty<byte>();
            ctx.Response.ContentLength64 = body.Length;
            await ctx.Response.OutputStream.WriteAsync(body);
            ctx.Response.Close();
        }
        catch { try { ctx.Response.Abort(); } catch { } }
    }

    public void Dispose() { try { _listener.Stop(); _listener.Close(); } catch { } }
}

static class Program
{
    static string Read(string p) => File.ReadAllText(p);

    static Dictionary<string, byte[]> PackagesFor(string tag) => new()
    {
        ["RobloxApp.zip"] = MockCdn.Zip(("RobloxPlayerBeta.exe", $"fake exe {tag}"), ("notes.txt", "hello")),
        ["content-sounds.zip"] = MockCdn.Zip(("ouch.ogg", $"ORIGINAL SOUND {tag}"), ("sub/dir/a.ogg", "a")),
        ["shaders.zip"] = MockCdn.Zip(("a.hlsl", "shader")),
        ["content-future.zip"] = MockCdn.Zip(("f.txt", "unknown package heuristic")),
        ["WebView2RuntimeInstaller.zip"] = MockCdn.Zip(("MicrosoftEdgeWebview2Setup.exe", "x")),
    };

    static RobloxBootstrapper NewRun(MockCdn cdn, AppSettings? settings = null, FakeUi? ui = null, GameProfile? profile = null)
    {
        settings ??= new AppSettings();
        var state = AppState.Load(); // mimic a fresh process: always reload from disk
        var dep = new Deployment(settings.Channel, new Dictionary<string, int> { [cdn.Url] = 0 }, new[] { cdn.Url });
        return new RobloxBootstrapper(settings, state, dep, ui ?? new FakeUi(), profile);
    }

    static async Task<int> Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "rmm-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        Paths.Initialize(Path.Combine(root, "base"));
        Paths.Roblox = Path.Combine(root, "roblox");
        Directory.CreateDirectory(Paths.Base);
        Logger.Initialize(Paths.Logs);

        // ------------------------------------------------------------------
        T.Section("PackageManifest / PackageMaps");
        var m = new PackageManifest("v0\r\nRobloxApp.zip\r\nABCDEF0123456789ABCDEF0123456789\r\n100\r\n250\r\nshaders.zip\r\nffffffffffffffffffffffffffffffff\r\n5\r\n9\r\nRobloxPlayerLauncher.exe\r\naaaa\r\n1\r\n2\r\n");
        T.Check(m.Count == 2, "launcher entry is ignored");
        T.Check(m[0].Signature == "abcdef0123456789abcdef0123456789", "signature is lower-cased");
        T.Check(m[0].PackedSize == 100 && m[0].Size == 250, "sizes parsed");
        bool threw = false; try { _ = new PackageManifest("v1\r\n"); } catch (NotSupportedException) { threw = true; }
        T.Check(threw, "unsupported manifest version rejected");
        T.Check(PackageMaps.Resolve("content-sounds.zip", out var k1) == @"content\sounds\" && k1, "known package maps");
        T.Check(PackageMaps.Resolve("content-newthing.zip", out var k2) == @"content\newthing\" && !k2, "unknown content-* follows convention");
        T.Check(PackageMaps.Resolve("extracontent-xyz.zip", out _) == @"ExtraContent\xyz\", "unknown extracontent-* follows convention");
        T.Check(PackageMaps.Resolve("Weird.zip", out _) == "", "other unknown packages go to root");

        // ------------------------------------------------------------------
        T.Section("LaunchArguments");
        var la = LaunchArguments.Parse(new[] { "-player", "roblox-player:1+launchmode:play+gameinfo:abc+launchtime:123" });
        T.Check(la.Mode == LaunchMode.Player && la.RobloxArguments.StartsWith("roblox-player:1+") && !la.RejectedLink, "protocol link accepted");
        T.Check(LaunchArguments.RefreshLaunchTime("x+launchtime:5+y").StartsWith("x+launchtime:17"), "launch time refreshed");
        var bad = LaunchArguments.Parse(new[] { "roblox-player:1+x --evil \"quoted\"" });
        T.Check(bad.RejectedLink && bad.RobloxArguments == "--app", "link with spaces/quotes rejected");
        T.Check(LaunchArguments.Parse(new[] { "-settings" }).Mode == LaunchMode.Settings, "-settings");
        T.Check(LaunchArguments.Parse(new[] { "-uninstall" }).Mode == LaunchMode.Uninstall, "-uninstall");
        T.Check(LaunchArguments.Parse(Array.Empty<string>()).Mode == LaunchMode.Menu, "no args -> menu");
        T.Check(LaunchArguments.Parse(new[] { "-player" }).RobloxArguments == "--app", "-player alone -> --app");

        // ------------------------------------------------------------------
        T.Section("FastFlagManager");
        T.Check(FastFlagManager.TryParse("{\"FFlagA\": true, \"DFIntB\": 60, \"FStringC\": \"x\", \"Bad\": null, \"Arr\": [1]}", out var parsed, out _)
                && parsed["FFlagA"] == "True" && parsed["DFIntB"] == "60" && parsed["FStringC"] == "x" && parsed.Count == 3, "mixed JSON types normalised to strings");
        T.Check(!FastFlagManager.TryParse("[1,2]", out _, out var err) && err != null, "non-object rejected");
        T.Check(!FastFlagManager.TryParse("{oops", out _, out _), "invalid JSON rejected");
        var ff = new FastFlagManager();
        ff.Set("DFIntTaskSchedulerTargetFps", "144"); ff.SetPreset("Rendering.Mode.Vulkan", "True"); ff.Save();
        var ff2 = new FastFlagManager(); ff2.Load();
        T.Check(ff2.Get("DFIntTaskSchedulerTargetFps") == "144" && ff2.GetPreset("Rendering.Mode.Vulkan") == "True", "save/load round trip");
        T.Check(Read(Paths.FlagsFile).Contains("\"DFIntTaskSchedulerTargetFps\": \"144\""), "values are written as strings");
        ff.Set("DFIntTaskSchedulerTargetFps", null); ff.SetPreset("Rendering.Mode.Vulkan", null); ff.Save();
        T.Check(!File.Exists(Paths.FlagsFile), "file removed when no flags remain");

        // ------------------------------------------------------------------
        T.Section("ModManager (unit)");
        string vdir = Path.Combine(root, "mm-version"), mods = Path.Combine(root, "mm-mods"), back = Path.Combine(root, "mm-back");
        Directory.CreateDirectory(Path.Combine(vdir, "content", "sounds")); Directory.CreateDirectory(Path.Combine(mods, "content", "sounds"));
        File.WriteAllText(Path.Combine(vdir, "content", "sounds", "a.ogg"), "orig");
        File.WriteAllText(Path.Combine(mods, "content", "sounds", "a.ogg"), "modded");
        File.WriteAllText(Path.Combine(mods, "content", "sounds", "new.ogg"), "brand new");
        File.WriteAllText(Path.Combine(mods, "README.txt"), "ignore me");
        Directory.CreateDirectory(Path.Combine(mods, "content", "avatar", "heads")); File.WriteAllText(Path.Combine(mods, "content", "avatar", "heads", "head.mesh"), "mesh");
        File.WriteAllText(Path.Combine(mods, "x.lock"), "lock");
        var r1 = ModManager.Apply(vdir, mods, back, new List<string>(), null, CancellationToken.None);
        T.Check(Read(Path.Combine(vdir, "content", "sounds", "a.ogg")) == "modded" && File.Exists(Path.Combine(vdir, "content", "sounds", "new.ogg")), "mods copied");
        T.Check(!File.Exists(Path.Combine(vdir, "README.txt")) && !File.Exists(Path.Combine(vdir, "x.lock")), "README and .lock ignored");
        T.Check(!File.Exists(Path.Combine(vdir, "content", "avatar", "heads", "head.mesh")), "avatar mesh replacement blocked");
        T.Check(r1.Applied.Count == 2 && r1.Success, "manifest has two applied files");
        File.WriteAllText(Path.Combine(mods, "content", "sounds", "a.ogg"), "modded v2");
        var r1b = ModManager.Apply(vdir, mods, back, r1.Applied, null, CancellationToken.None);
        T.Check(Read(Path.Combine(back, "content", "sounds", "a.ogg")) == "orig", "editing a mod keeps the first original backup");
        File.Delete(Path.Combine(mods, "content", "sounds", "a.ogg")); File.Delete(Path.Combine(mods, "content", "sounds", "new.ogg"));
        var r2 = ModManager.Apply(vdir, mods, back, r1b.Applied, null, CancellationToken.None);
        T.Check(Read(Path.Combine(vdir, "content", "sounds", "a.ogg")) == "orig", "original restored after mod removed");
        T.Check(!File.Exists(Path.Combine(vdir, "content", "sounds", "new.ogg")), "mod-only file deleted after removal");
        T.Check(r2.Applied.Count == 0, "manifest empty after removal");

        // ------------------------------------------------------------------
        T.Section("GlobalSettingsManager");
        var gbs = new GlobalSettingsManager();
        T.Check(!gbs.Load(), "no file -> not loaded");
        T.Check(gbs.CreateTemplate() && gbs.Loaded, "template created");
        T.Check(gbs.Get("Rendering.FramerateCap") == "60", "reads existing value");
        gbs.Set("Rendering.FramerateCap", "240"); gbs.Set("Rendering.SavedQualityLevel", "5");
        gbs.Save(readOnly: true);
        T.Check(gbs.IsReadOnly, "read-only applied");
        var gbs2 = new GlobalSettingsManager(); gbs2.Load();
        T.Check(gbs2.Get("Rendering.FramerateCap") == "240" && gbs2.Get("Rendering.SavedQualityLevel") == "5", "values persisted");
        gbs2.Set("Rendering.FramerateCap", "75"); gbs2.Save(readOnly: false);
        T.Check(!gbs2.IsReadOnly && new GlobalSettingsManager().Load(), "read-only cleared, file still valid");

        // ------------------------------------------------------------------
        T.Section("Deployment");
        using var cdn = new MockCdn();
        cdn.Packages = PackagesFor("v1");
        var dep0 = new Deployment("production", new Dictionary<string, int> { [cdn.Url] = 0 }, new[] { cdn.Url });
        await dep0.InitializeConnectivityAsync();
        T.Check(dep0.CdnUrl == cdn.Url, "connectivity picks the mirror");
        T.Check(dep0.GetLocation("/x") == cdn.Url + "/x", "production location has no channel path");
        var depC = new Deployment("ZChannel", new Dictionary<string, int> { [cdn.Url] = 0 }, new[] { cdn.Url });
        await depC.InitializeConnectivityAsync();
        T.Check(depC.GetLocation("/x") == cdn.Url + "/channel/common/x", "other channels use /channel/common");
        bool chanErr = false; try { await depC.GetInfoAsync(); } catch (ChannelNotFoundException) { chanErr = true; }
        T.Check(chanErr, "unknown channel -> ChannelNotFoundException");
        var depDead = new Deployment("production", new Dictionary<string, int> { ["http://127.0.0.1:1"] = 0 }, new[] { "http://127.0.0.1:1" });
        bool dead = false; try { await depDead.InitializeConnectivityAsync(); } catch (HttpRequestException) { dead = true; }
        T.Check(dead, "unreachable mirror -> HttpRequestException");
        var info = await dep0.GetInfoAsync();
        T.Check(info.VersionGuid == cdn.VersionGuid && info.Version == "0.600.0.1", "client version parsed");

        // ------------------------------------------------------------------
        T.Section("Bootstrapper: fresh install");
        var fl = new FastFlagManager(); fl.Set("DFIntTaskSchedulerTargetFps", "144"); fl.Save();
        string v1Dir = Paths.VersionDirectory(cdn.VersionGuid);
        var ui1 = new FakeUi();
        string exe = await NewRun(cdn, ui: ui1).PrepareAsync();
        T.Check(exe == Path.Combine(v1Dir, "RobloxPlayerBeta.exe") && Read(exe) == "fake exe v1", "player exe extracted to version folder");
        T.Check(Read(Path.Combine(v1Dir, "content", "sounds", "ouch.ogg")) == "ORIGINAL SOUND v1", "content-sounds -> content\\sounds");
        T.Check(File.Exists(Path.Combine(v1Dir, "content", "sounds", "sub", "dir", "a.ogg")), "nested zip folders extracted");
        T.Check(File.Exists(Path.Combine(v1Dir, "shaders", "a.hlsl")), "shaders -> shaders folder");
        T.Check(File.Exists(Path.Combine(v1Dir, "content", "future", "f.txt")), "unknown content-* package placed by convention");
        T.Check(!Directory.Exists(Path.Combine(v1Dir, "WebView2RuntimeInstaller")), "WebView2 installer not extracted unless needed");
        T.Check(Read(Path.Combine(v1Dir, "AppSettings.xml")).Contains("<ContentFolder>content</ContentFolder>"), "AppSettings.xml written");
        T.Check(Read(Path.Combine(v1Dir, "ClientSettings", "ClientAppSettings.json")).Contains("\"DFIntTaskSchedulerTargetFps\": \"144\""), "fast flags applied");
        var state1 = AppState.Load();
        T.Check(state1.VersionGuid == cdn.VersionGuid && state1.PackageHashes.Count == 5 && !state1.ForceReinstall, "state saved");
        T.Check(Directory.GetFiles(Paths.Downloads).Length == 5 && !Directory.GetFiles(Paths.Downloads, "*.part").Any(), "downloads cached by hash, no .part left");
        T.Check(ui1.Progress.Max() == 100 && ui1.Progress.Contains(0), "progress reached 100");
        T.Check(!ui1.Progress.Where(p => p >= 0).Zip(ui1.Progress.Where(p => p >= 0).Skip(1)).Any(x => x.Second < x.First - 0), "progress never goes backwards on a clean install");
        T.Check(File.Exists(Path.Combine(Paths.Mods, "README.txt")), "mods README created");
        int requestsAfterInstall = cdn.PackageRequests;
        T.Check(requestsAfterInstall == 5, "each package downloaded once");

        // ------------------------------------------------------------------
        T.Section("Bootstrapper: up to date, mods apply and restore");
        await NewRun(cdn).PrepareAsync();
        T.Check(cdn.PackageRequests == requestsAfterInstall, "no downloads when already up to date");

        Directory.CreateDirectory(Path.Combine(Paths.Mods, "content", "sounds"));
        string modFile = Path.Combine(Paths.Mods, "content", "sounds", "ouch.ogg");
        File.WriteAllText(modFile, "MOD SOUND");
        await NewRun(cdn).PrepareAsync();
        T.Check(Read(Path.Combine(v1Dir, "content", "sounds", "ouch.ogg")) == "MOD SOUND", "mod replaces original file");
        T.Check(AppState.Load().ModManifest.Contains("content/sounds/ouch.ogg"), "mod recorded in state");
        File.Delete(modFile);
        await NewRun(cdn).PrepareAsync();
        T.Check(Read(Path.Combine(v1Dir, "content", "sounds", "ouch.ogg")) == "ORIGINAL SOUND v1", "original restored after mod deleted");

        File.WriteAllText(modFile, "MOD SOUND");
        await NewRun(cdn).PrepareAsync();
        T.Check(Read(Path.Combine(v1Dir, "content", "sounds", "ouch.ogg")) == "MOD SOUND", "mod applied again");
        await NewRun(cdn, new AppSettings { ApplyMods = false }).PrepareAsync();
        T.Check(Read(Path.Combine(v1Dir, "content", "sounds", "ouch.ogg")) == "ORIGINAL SOUND v1", "disabling mods restores originals");
        T.Check(File.Exists(Path.Combine(v1Dir, "ClientSettings", "ClientAppSettings.json")), "fast flags still applied with mods off");
        await NewRun(cdn, new AppSettings { UseFastFlagManager = false }).PrepareAsync();
        T.Check(!File.Exists(Path.Combine(v1Dir, "ClientSettings", "ClientAppSettings.json")), "disabling flag manager removes flags from Roblox");
        T.Check(Read(Path.Combine(v1Dir, "content", "sounds", "ouch.ogg")) == "MOD SOUND", "mods re-applied when mods on again");

        // ------------------------------------------------------------------
        T.Section("Custom font");
        string famSrc = Path.Combine(v1Dir, "content", "fonts", "families");
        Directory.CreateDirectory(famSrc);
        File.WriteAllText(Path.Combine(famSrc, "Arial.json"), "{\"name\":\"Arial\",\"faces\":[{\"name\":\"Regular\",\"weight\":400,\"assetId\":\"rbxasset://fonts/arial.ttf\"},{\"name\":\"Bold\",\"weight\":700,\"assetId\":\"rbxasset://fonts/arialbd.ttf\"}]}");
        File.WriteAllText(Path.Combine(famSrc, "Broken.json"), "{not json");
        string ttf = Path.Combine(root, "MyFont.ttf"); File.WriteAllText(ttf, "TTFDATA");
        bool badExt = false; try { CustomFont.Apply(Path.Combine(root, "x.woff")); } catch (InvalidDataException) { badExt = true; }
        T.Check(badExt, "non-ttf font rejected");
        CustomFont.Apply(ttf);
        await NewRun(cdn).PrepareAsync();
        string arial = Read(Path.Combine(v1Dir, "content", "fonts", "families", "Arial.json"));
        T.Check(arial.Contains("rbxasset://fonts/CustomFont.ttf") && !arial.Contains("arialbd"), "every face points at the custom font");
        T.Check(Read(Path.Combine(v1Dir, "content", "fonts", "CustomFont.ttf")) == "TTFDATA", "font file copied into Roblox");
        T.Check(Read(Path.Combine(famSrc, "Broken.json")) == "{not json", "unreadable family left alone");
        CustomFont.Remove();
        await NewRun(cdn).PrepareAsync();
        T.Check(Read(Path.Combine(v1Dir, "content", "fonts", "families", "Arial.json")).Contains("arialbd.ttf"), "original family restored after removing font");
        T.Check(!File.Exists(Path.Combine(v1Dir, "content", "fonts", "CustomFont.ttf")), "font file removed from Roblox");

        // ------------------------------------------------------------------
        T.Section("Bootstrapper: update to a new version");
        cdn.VersionGuid = "version-bbbb3333cccc4444"; cdn.Version = "0.601.0.1"; cdn.Packages = PackagesFor("v2");
        string v2Dir = Paths.VersionDirectory(cdn.VersionGuid);
        await NewRun(cdn).PrepareAsync();
        T.Check(Read(Path.Combine(v2Dir, "RobloxPlayerBeta.exe")) == "fake exe v2", "new version installed");
        T.Check(!Directory.Exists(v1Dir), "old version folder removed");
        T.Check(Read(Path.Combine(v2Dir, "content", "sounds", "ouch.ogg")) == "MOD SOUND", "mods re-applied to the new version");
        T.Check(AppState.Load().VersionGuid == cdn.VersionGuid, "state points at the new version");
        T.Check(Directory.GetFiles(Paths.Downloads).Length == 5, "stale cached packages cleaned");

        T.Section("Bootstrapper: forced reinstall");
        var forced = AppState.Load(); forced.ForceReinstall = true; forced.Save();
        int before = cdn.PackageRequests;
        await NewRun(cdn).PrepareAsync();
        T.Check(!AppState.Load().ForceReinstall && File.Exists(Path.Combine(v2Dir, "RobloxPlayerBeta.exe")), "reinstall completes and clears the flag");
        T.Check(cdn.PackageRequests == before, "reinstall reuses the verified download cache");
        T.Check(Read(Path.Combine(v2Dir, "content", "sounds", "ouch.ogg")) == "MOD SOUND", "mods re-applied after reinstall");

        // ------------------------------------------------------------------
        T.Section("Bootstrapper: corrupt download is retried");
        cdn.VersionGuid = "version-cccc5555dddd6666"; cdn.Version = "0.602.0.1"; cdn.Packages = PackagesFor("v3");
        cdn.CorruptOnce.Add("RobloxApp.zip"); int reqBefore = cdn.PackageRequests;
        await NewRun(cdn).PrepareAsync();
        T.Check(Read(Path.Combine(Paths.VersionDirectory(cdn.VersionGuid), "RobloxPlayerBeta.exe")) == "fake exe v3", "install succeeds after checksum failure");
        // v3 changes only RobloxApp.zip and content-sounds.zip; the other 3 packages are byte-identical and come from the cache.
        T.Check(cdn.PackageRequests - reqBefore == 3, "corrupt package retried once; unchanged packages served from cache");

        // ------------------------------------------------------------------
        T.Section("Bootstrapper: cancel");
        cdn.VersionGuid = "version-dddd7777eeee8888"; cdn.Version = "0.603.0.1"; cdn.Packages = PackagesFor("v4"); cdn.PackageDelayMs = 6000;
        var b9 = NewRun(cdn);
        var prep = b9.PrepareAsync();
        await Task.Delay(1200); b9.Cancel();
        bool cancelled = false; var cs = Stopwatch.StartNew();
        try { await prep; } catch (OperationCanceledException) { cancelled = true; }
        T.Check(cancelled && cs.Elapsed < TimeSpan.FromSeconds(4), "cancel stops quickly with OperationCanceledException");
        T.Check(AppState.Load().VersionGuid == "version-cccc5555dddd6666", "state untouched by cancelled install");
        T.Check(!Directory.GetFiles(Paths.Downloads, "*.part").Any(), "no partial downloads left behind");
        cdn.PackageDelayMs = 0;
        await NewRun(cdn).PrepareAsync();
        T.Check(Read(Path.Combine(Paths.VersionDirectory(cdn.VersionGuid), "RobloxPlayerBeta.exe")) == "fake exe v4", "next run recovers cleanly");

        // ------------------------------------------------------------------
        T.Section("Bootstrapper: instance lock");
        var lockStream = new FileStream(Paths.BootstrapperLock, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        _ = Task.Run(async () => { await Task.Delay(1500); lockStream.Dispose(); });
        var ls = Stopwatch.StartNew();
        var ui11 = new FakeUi();
        await NewRun(cdn, ui: ui11).PrepareAsync();
        T.Check(ls.Elapsed > TimeSpan.FromMilliseconds(1200), "second instance waits for the first");
        T.Check(ui11.Statuses.Any(s => s.StartsWith("Waiting")), "user sees a waiting message");

        // ------------------------------------------------------------------
        T.Section("Security: zip-slip");
        byte[] evil = MockCdn.Zip(("good.txt", "ok"), ("../../evil.txt", "pwned"));
        string sig = MockCdn.Md5(evil); Directory.CreateDirectory(Paths.Downloads);
        File.WriteAllBytes(Path.Combine(Paths.Downloads, sig), evil);
        string target = Path.Combine(root, "slip-target"); Directory.CreateDirectory(target);
        bool blocked = false;
        try { RobloxBootstrapper.ExtractPackage(new Package { Name = "shaders.zip", Signature = sig }, target, CancellationToken.None); }
        catch (InvalidDataException) { blocked = true; }
        T.Check(blocked, "path traversal entry rejected");
        T.Check(!File.Exists(Path.Combine(root, "evil.txt")) && !File.Exists(Path.Combine(target, "evil.txt")), "nothing written outside the target folder");

        // ------------------------------------------------------------------
        T.Section("Cleaner");
        Directory.CreateDirectory(Paths.RobloxLogs);
        string oldLog = Path.Combine(Paths.RobloxLogs, "old.log"), newLog = Path.Combine(Paths.RobloxLogs, "new.log");
        File.WriteAllText(oldLog, "x"); File.WriteAllText(newLog, "x"); File.SetLastWriteTimeUtc(oldLog, DateTime.UtcNow.AddDays(-40));
        Cleaner.Run(new AppSettings { CleanerAge = CleanerAge.OneMonth, CleanRobloxLogs = true, CleanAppLogs = false });
        T.Check(!File.Exists(oldLog) && File.Exists(newLog), "only logs older than the limit are deleted");
        Cleaner.Run(new AppSettings { CleanerAge = CleanerAge.Never });
        T.Check(File.Exists(newLog), "'Never' deletes nothing");


        // ------------------------------------------------------------------
        T.Section("Place IDs from launch links");
        string webLink = "roblox-player:1+launchmode:play+gameinfo:AbC123+launchtime:1700000000000+placelauncherurl:https%3A%2F%2Fassetgame.roblox.com%2Fgame%2FPlaceLauncher.ashx%3Frequest%3DRequestGame%26browserTrackerId%3D123%26placeId%3D606849621%26isPlayTogetherGame%3Dfalse+browsertrackerid:123+robloxLocale:en_us+gameLocale:en_us";
        T.Check(LaunchArguments.ExtractPlaceId(webLink) == 606849621, "website link (URL-encoded) -> place ID");
        T.Check(LaunchArguments.ExtractPlaceId("roblox://placeId=920587237") == 920587237, "roblox://placeId= link");
        T.Check(LaunchArguments.ExtractPlaceId("roblox://experiences/start?placeId=123&launchData=x") == 123, "experiences/start link");
        T.Check(LaunchArguments.ExtractPlaceId("--app") is null, "no place ID in --app");
        T.Check(RobloxGames.ParsePlaceInput("https://www.roblox.com/games/920587237/Adopt-Me") == 920587237, "game page URL");
        T.Check(RobloxGames.ParsePlaceInput("https://www.roblox.com/es/games/5/x") == 5, "localised game page URL");
        T.Check(RobloxGames.ParsePlaceInput(" 1818 ") == 1818, "plain number");
        T.Check(RobloxGames.ParsePlaceInput("hello") is null, "junk -> null");

        // ------------------------------------------------------------------
        T.Section("Roblox log watcher");
        string Line(string msg) => "2026-11-09T07:00:00.000Z,10.5,1a2b,6 " + msg;
        string job1 = "11111111-2222-3333-4444-555555555555", job2 = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
        var events = new RobloxLogWatcher().ParseLines(new[]
        {
            Line("[FLog::Output] Something unrelated"),
            Line($"[FLog::Output] ! Joining game '{job1}' place 100 at 10.0.0.1"),
            Line("[FLog::Network] serverId: 10.0.0.1|5000"),
            Line($"[FLog::Output] ! Joining game '{job2}' place 200 at 10.0.0.2"),   // teleport
            Line("[FLog::Network] serverId: 10.0.0.2|5001"),
            Line("[FLog::Network] Time to disconnect replication data: 0.01"),
            Line("[FLog::SingleSurfaceApp] leaveUGCGameInternal"),
            "continuation line without a timestamp",
        }).ToList();
        string seq = string.Join(",", events.Select(e => $"{e.Kind}:{e.PlaceId}"));
        T.Check(seq == "Joining:100,Joined:100,Left:100,Joined:200,Left:200", $"join, teleport, leave parsed in order ({seq})");
        T.Check(events[1].JobId == job1 && events[3].JobId == job2, "job IDs kept");
        var failed = new RobloxLogWatcher().ParseLines(new[] { Line($"[FLog::Output] ! Joining game '{job1}' place 100 at 1.1.1.1"), Line("[FLog::SingleSurfaceApp] leaveUGCGameInternal") }).ToList();
        T.Check(failed.Count == 1 && failed[0].Kind == LogEventKind.Joining, "cancelled join produces no session");

        // live tailing of a growing log file
        Directory.CreateDirectory(Paths.RobloxLogs);
        foreach (var f in Directory.GetFiles(Paths.RobloxLogs)) File.Delete(f);
        var launched = DateTime.UtcNow;
        var seen = new List<LogEvent>();
        using (var watchCts = new CancellationTokenSource())
        {
            var watchTask = new RobloxLogWatcher().WatchAsync(Paths.RobloxLogs, launched, e => { lock (seen) seen.Add(e); }, watchCts.Token);
            await Task.Delay(1200);
            string logPath = Path.Combine(Paths.RobloxLogs, "0.600.0.1_20261109T070000Z_Player_ABC12_last.log");
            using (var w = new StreamWriter(new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true })
            {
                w.WriteLine(Line($"[FLog::Output] ! Joining game '{job1}' place 100 at 10.0.0.1"));
                await Task.Delay(1500);
                w.WriteLine(Line("[FLog::Network] serverId: 10.0.0.1|5000"));
                await Task.Delay(1500);
                w.WriteLine(Line("[FLog::Network] Time to disconnect replication data: 0.01"));
                await Task.Delay(2500);
            }
            watchCts.Cancel();
            try { await watchTask; } catch (OperationCanceledException) { }
        }
        T.Check(string.Join(",", seen.Select(e => e.Kind)) == "Joining,Joined,Left", "follows a log file as Roblox writes it");
        T.Check(RobloxLogWatcher.FindLog(Paths.RobloxLogs, DateTime.UtcNow.AddHours(1)) is null, "older logs are not picked up");

        // ------------------------------------------------------------------
        T.Section("Play history");
        var t0 = DateTime.UtcNow.AddDays(-1);
        PlayHistory.Append(new PlaySession { PlaceId = 100, StartUtc = t0, EndUtc = t0.AddMinutes(90) });
        PlayHistory.Append(new PlaySession { PlaceId = 100, StartUtc = t0.AddHours(3), EndUtc = t0.AddHours(3).AddMinutes(30) });
        PlayHistory.Append(new PlaySession { PlaceId = 200, StartUtc = t0, EndUtc = t0.AddMinutes(20) });
        PlayHistory.Append(new PlaySession { PlaceId = 300, StartUtc = t0, EndUtc = t0.AddSeconds(10) }); // too short
        var hist = PlayHistory.Load();
        var per = hist.PerGame();
        T.Check(hist.Sessions.Count == 3, "sessions under 30 seconds are dropped");
        T.Check(per[0].PlaceId == 100 && per[0].Total == TimeSpan.FromMinutes(120) && per[0].Sessions == 2, "totals per game, most played first");
        T.Check(per[1].Name == "Place 200", "unknown names fall back to the place ID");
        T.Check(hist.TotalSince(t0.AddMinutes(60)) == TimeSpan.FromMinutes(60), "time since a date counts partial sessions");
        T.Check(hist.DayStreak(DateTime.Now) >= 1, "day streak counts recent play");
        T.Check(PlayHistory.Format(TimeSpan.FromMinutes(135)) == "2 h 15 min" && PlayHistory.Format(TimeSpan.FromSeconds(20)) == "under a minute", "durations formatted");
        string? gameName = await RobloxGames.GetNameAsync(606849621, default, cdn.Url, cdn.Url);
        T.Check(gameName == "Jailbreak", "game name looked up from place ID");
        T.Check(await RobloxGames.GetNameAsync(42, default, cdn.Url, cdn.Url) is null, "unknown place -> null, no crash");

        // ------------------------------------------------------------------
        T.Section("Game profiles");
        string vNow = Paths.VersionDirectory(cdn.VersionGuid);
        string vFlags = Path.Combine(vNow, "ClientSettings", "ClientAppSettings.json");
        var baseFlags = new FastFlagManager(); baseFlags.Load();
        baseFlags.Set("DFIntTaskSchedulerTargetFps", "144"); baseFlags.Set("FFlagShared", "False"); baseFlags.Save();
        var profile = new GameProfile { PlaceId = 100, Name = "Test game", Flags = new() { ["FFlagShared"] = "True", ["FIntProfileOnly"] = "7" } };
        await NewRun(cdn, profile: profile).PrepareAsync();
        string pf = Read(vFlags);
        T.Check(pf.Contains("\"FIntProfileOnly\": \"7\"") && pf.Contains("\"FFlagShared\": \"True\"") && pf.Contains("\"DFIntTaskSchedulerTargetFps\": \"144\""), "profile flags merged over normal flags");
        T.Check(!Read(Paths.FlagsFile).Contains("FIntProfileOnly"), "your saved flags are not changed by a profile");
        await NewRun(cdn).PrepareAsync();
        string nf = Read(vFlags);
        T.Check(!nf.Contains("FIntProfileOnly") && nf.Contains("\"FFlagShared\": \"False\""), "next normal launch goes back to normal flags");
        File.WriteAllText(modFile, "MOD SOUND");
        await NewRun(cdn).PrepareAsync();
        string ouch = Path.Combine(vNow, "content", "sounds", "ouch.ogg");
        T.Check(Read(ouch) == "MOD SOUND", "mod in place before no-mods profile");
        await NewRun(cdn, profile: new GameProfile { PlaceId = 5, ApplyMods = false }).PrepareAsync();
        T.Check(Read(ouch).StartsWith("ORIGINAL SOUND"), "profile with mods off restores originals");
        T.Check(File.Exists(vFlags), "flags still applied in a no-mods profile");
        await NewRun(cdn).PrepareAsync();
        T.Check(Read(ouch) == "MOD SOUND", "mods come back on the next normal launch");
        var original = new AppSettings { ApplyMods = true };
        var safe = original.Clone(); safe.ApplyMods = false; safe.UseFastFlagManager = false;
        T.Check(original.ApplyMods && original.UseFastFlagManager, "safe-mode copy doesn't change saved settings");
        await NewRun(cdn, safe).PrepareAsync();
        T.Check(Read(ouch).StartsWith("ORIGINAL SOUND") && !File.Exists(vFlags), "safe mode launches without mods and flags");
        await NewRun(cdn).PrepareAsync();
        T.Check(Read(ouch) == "MOD SOUND" && File.Exists(vFlags), "everything returns after safe mode");

        // ------------------------------------------------------------------
        T.Section("Smart mod drop");
        var index = ModPlacer.IndexVersion(vNow);
        string dropMods = Path.Combine(root, "drop-mods"); Directory.CreateDirectory(dropMods);
        string dropDir = Path.Combine(root, "drop-src"); Directory.CreateDirectory(dropDir);
        string dropped = Path.Combine(dropDir, "ouch.ogg"); File.WriteAllText(dropped, "MY OUCH");
        var p1 = ModPlacer.PlaceFile(dropped, index, dropMods);
        T.Check(p1.Outcome == PlacementOutcome.Placed && p1.RelativePath == "content/sounds/ouch.ogg" && Read(Path.Combine(dropMods, "content", "sounds", "ouch.ogg")) == "MY OUCH", "single file placed where Roblox keeps it");
        Directory.CreateDirectory(Path.Combine(vNow, "ExtraContent", "x")); File.WriteAllText(Path.Combine(vNow, "ExtraContent", "x", "a.ogg"), "dupe");
        index = ModPlacer.IndexVersion(vNow);
        string droppedA = Path.Combine(dropDir, "a.ogg"); File.WriteAllText(droppedA, "A");
        var p2 = ModPlacer.PlaceFile(droppedA, index, dropMods);
        T.Check(p2.Outcome == PlacementOutcome.Ambiguous && p2.Candidates[0] == "content/sounds/sub/dir/a.ogg", "duplicate names reported, content folder ranked first");
        string droppedX = Path.Combine(dropDir, "nothing.png"); File.WriteAllText(droppedX, "x");
        T.Check(ModPlacer.PlaceFile(droppedX, index, dropMods).Outcome == PlacementOutcome.NotFound, "unknown file names are reported, not guessed");
        T.Check(!index.ContainsKey("RobloxPlayerBeta.exe"), "the game exe is never offered as a target");
        string pack = Path.Combine(dropDir, "pack.zip");
        File.WriteAllBytes(pack, MockCdn.Zip(
            ("MyPack/content/textures/Cursor.png", "CURSOR"),
            ("MyPack/README.txt", "readme"),
            ("MyPack/ouch.ogg", "LOOSE OUCH"),
            ("__MACOSX/MyPack/._ouch.ogg", "junk"),
            ("MyPack/content/../../../escape.txt", "evil")));
        var packResults = ModPlacer.PlaceZip(pack, index, dropMods);
        T.Check(Read(Path.Combine(dropMods, "content", "textures", "Cursor.png")) == "CURSOR", "zip wrapper folder detected, layout kept");
        T.Check(Read(Path.Combine(dropMods, "content", "sounds", "ouch.ogg")) == "LOOSE OUCH", "loose file in a zip placed by name");
        T.Check(!File.Exists(Path.Combine(dropMods, "README.txt")) && !packResults.Any(r => r.Source.Contains("__MACOSX")), "readme and Mac junk skipped");
        T.Check(!File.Exists(Path.Combine(root, "escape.txt")) && packResults.Any(r => r.Outcome == PlacementOutcome.Failed), "zip entries escaping the mods folder are refused");
        T.Check(ModPlacer.FindRoot(new[] { "a/b/content/x.png", "a/content/y.png" }) == "a/", "shallowest Roblox-looking root wins");

        Console.WriteLine($"\n{T.Passed} passed, {T.Failed} failed");
        try { Directory.Delete(root, true); } catch { }
        return T.Failed == 0 ? 0 : 1;
    }
}
