using System;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using Client;
using Client.Envir;
using SlimDX.Direct3D9;

internal static class OfficialUiStartupTests
{
    private static int checks;
    private static string directory, installedPath;
    private static byte[] original;
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }
    private static void Reject<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { checks++; return; }
        throw new Exception("Accepted invalid operation: " + message);
    }
    private static GameUiStartupSession Prepare(string value, bool gate)
    {
        var method = typeof(GameUiStartupSession).GetMethod("Prepare", BindingFlags.Static | BindingFlags.NonPublic,
            null, new[] { typeof(string), typeof(string), typeof(bool) }, null);
        try { return (GameUiStartupSession)method.Invoke(null, new object[] { value, directory, gate }); }
        catch (TargetInvocationException error)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static void Legacy(string value, bool gate, string reason)
    {
        using (var owner = Prepare(value, gate))
        {
            Check(owner.EffectiveStyle == GameUiStyle.Legacy && owner.Package == null, "Fallback published a package: " + value);
            Check(reason == null ? owner.Diagnostic == null : owner.Diagnostic != null && owner.Diagnostic.Contains(reason),
                "Missing fallback reason: " + value + ": " + owner.Diagnostic);
        }
    }
    private static void Writable()
    {
        using (var file = new FileStream(installedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Check(file.Length == original.Length, "Lease was not released");
    }
    private static void SetDevice(object value)
    { typeof(DXManager).GetProperty("Device").SetValue(null, value, null); }
    private static void ConfigRoundTrip()
    {
        string ini = Path.Combine(directory, "startup.ini");
        Directory.CreateDirectory(directory);
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var reader = typeof(Library.ConfigReader).GetMethod("ReadConfig", flags);
        var writer = typeof(Library.ConfigReader).GetMethod("SaveConfig", flags);
        try
        {
            File.WriteAllText(ini, "[Graphics]\r\nGameUiStyle=OfficialV1\r\nFullScreen=False\r\n");
            reader.Invoke(null, new object[] { typeof(Config), ini, null });
            Check(Config.GameUiStyle == "OfficialV1" && !Config.FullScreen, "Graphics config did not load style and existing settings");
            using (var frozen = Prepare(Config.GameUiStyle, false))
            {
                File.WriteAllText(ini, "[Graphics]\r\nGameUiStyle=unknown-style\r\nFullScreen=False\r\n");
                reader.Invoke(null, new object[] { typeof(Config), ini, null });
                Check(Config.GameUiStyle == "unknown-style", "String config discarded invalid value before policy could diagnose it");
                Check(frozen.EffectiveStyle == GameUiStyle.Legacy && frozen.Diagnostic.Contains("acceptance gate"), "Config reload changed captured startup decision");
                using (var next = Prepare(Config.GameUiStyle, true))
                    Check(next.EffectiveStyle == GameUiStyle.Legacy && next.Diagnostic.Contains("Invalid GameUiStyle"), "Restart decision did not reject invalid value");
            }
            Config.GameUiStyle = "Legacy";
            writer.Invoke(null, new object[] { typeof(Config), ini, null });
            string saved = File.ReadAllText(ini);
            Check(saved.Contains("[Graphics]\r\nGameUiStyle=Legacy\r\nFullScreen=False"), "Saved style is not under Graphics or changed existing settings");
        }
        finally { Config.GameUiStyle = "Legacy"; Config.FullScreen = true; if (File.Exists(ini)) File.Delete(ini); }
    }
    private static void Cpu()
    {
        Check(Config.GameUiStyle == "Legacy", "Development default changed");
        foreach (var value in new[] { "Legacy", "legacy", " LEGACY " }) Legacy(value, false, null);
        foreach (var value in new[] { null, "", " ", "0", "1", "OfficialV2", "Legacy,OfficialV1" })
            Legacy(value, true, "Invalid GameUiStyle");
        foreach (var value in new[] { "OfficialV1", "officialv1", " OfficialV1 " }) Legacy(value, false, "acceptance gate");
        using (var gated = GameUiStartupSession.Prepare("OfficialV1", directory))
            Check(gated.EffectiveStyle == GameUiStyle.Legacy && gated.Diagnostic.Contains("acceptance gate"), "Public API bypassed compiled gate");
        Legacy("OfficialV1", true, "DirectoryNotFoundException");
        Directory.CreateDirectory(Path.GetDirectoryName(installedPath));
        Legacy("OfficialV1", true, "FileNotFoundException");
        File.WriteAllBytes(installedPath, original);
        Legacy("OfficialV1", true, "device is not ready");
        Check(DXManager.TextureList.Count == 0, "No-device fallback leaked textures");
        Writable();
        var bad = (byte[])original.Clone(); bad[bad.Length - 1] ^= 1;
        File.WriteAllBytes(installedPath, bad);
        Legacy("OfficialV1", true, "SHA256 mismatch");
        File.WriteAllBytes(installedPath, original);
        Writable();
        var owner = Prepare("Legacy", false);
        Config.GameUiStyle = "OfficialV1";
        Check(owner.EffectiveStyle == GameUiStyle.Legacy && owner.Package == null, "Config edit hot-switched session");
        Config.GameUiStyle = "Legacy";
        Exception otherThread = null;
        var worker = new Thread(() => { try { owner.Dispose(); } catch (Exception error) { otherThread = error; } });
        worker.Start(); worker.Join();
        Check(otherThread is InvalidOperationException && !owner.IsDisposed, "Wrong-thread disposal altered session");
        owner.Dispose(); owner.Dispose();
        Check(owner.IsDisposed, "Dispose not idempotent");
        Reject<ObjectDisposedException>(() => { var unused = owner.Package; }, "disposed package access");
    }
    private static void Gpu()
    {
        using (var target = new TargetForm()) using (var d3d = new Direct3D())
        {
            CEnvir.Target = target;
            var parameters = new PresentParameters { Windowed = true, SwapEffect = SwapEffect.Discard,
                BackBufferFormat = Format.X8R8G8B8, BackBufferWidth = 1024, BackBufferHeight = 768 };
            using (var device = new Device(d3d, 0, DeviceType.Hardware, target.Handle, CreateFlags.SoftwareVertexProcessing, parameters))
            {
                SetDevice(device);
                try
                {
                    for (int cycle = 0; cycle < 3; cycle++)
                    {
                        GameUiVerifiedPackage borrowed;
                        using (var owner = Prepare("OfficialV1", true))
                        {
                            Check(owner.EffectiveStyle == GameUiStyle.OfficialV1 && owner.Diagnostic == null, "Valid preflight failed: " + owner.Diagnostic);
                            borrowed = owner.Package;
                            Check(borrowed != null && borrowed.Manifest.Frames.Count == 93, "Compiled package contract changed");
                            Check(DXManager.TextureList.Count == 93, "Did not upload all required frames");
                            var library = borrowed.Library;
                            foreach (var frame in borrowed.Manifest.Frames.Values)
                            {
                                var image = library.GetImage(frame.Index);
                                Check(image != null && image.Image != null && !image.Image.Disposed, "Required frame unreadable: " + frame.Id);
                            }
                            Reject<IOException>(() => File.Open(installedPath, FileMode.Open, FileAccess.Write, FileShare.Read).Dispose(), "write to borrowed package");
                            borrowed.UploadAllFrames();
                            Check(ReferenceEquals(library, owner.Package.Library) && DXManager.TextureList.Count == 93, "Repeated preflight duplicated textures");
                            Config.GameUiStyle = "Legacy";
                            Check(owner.EffectiveStyle == GameUiStyle.OfficialV1 && ReferenceEquals(borrowed, owner.Package), "Config edit replaced active package");
                        }
                        Check(borrowed.IsDisposed && DXManager.TextureList.Count == 0, "Application owner leaked texture or file lease");
                        Check(!device.Disposed, "Package owner disposed shared device");
                        Writable();
                    }
                }
                finally { SetDevice(null); CEnvir.Target = null; }
            }
        }
    }
    [STAThread]
    public static int Main(string[] args)
    {
        directory = Path.Combine(Path.GetTempPath(), "ZirconStartupData-" + Guid.NewGuid().ToString("N"));
        installedPath = Path.Combine(directory, GameUiOfficialV1Catalog.PackageRelativePath);
        try
        {
            original = File.ReadAllBytes(args[0]);
            Cpu(); ConfigRoundTrip(); Gpu();
            Console.WriteLine("Startup selection checks passed: " + checks + "; 3 isolated GPU lifetimes; all 93 frames uploaded per lifetime; release gates retained.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            if (File.Exists(installedPath)) File.Delete(installedPath);
            string leaf = Path.GetDirectoryName(installedPath);
            if (Directory.Exists(leaf)) Directory.Delete(leaf, false);
            string data = Path.GetDirectoryName(leaf);
            if (Directory.Exists(data)) Directory.Delete(data, false);
            if (Directory.Exists(directory)) Directory.Delete(directory, false);
        }
    }
}
