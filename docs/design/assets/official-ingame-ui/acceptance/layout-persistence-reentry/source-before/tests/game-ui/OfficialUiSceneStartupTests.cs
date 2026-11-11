using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Windows.Forms;
using Client;
using Client.Controls;
using Client.Envir;
using Client.Scenes;
using Library.Network;
using SlimDX.Direct3D9;
using C = Library.Network.ClientPackets;
using G = Library.Network.GeneralPackets;

internal static partial class OfficialUiWindowSetTests
{
    private static readonly PropertyInfo SessionProperty = typeof(CEnvir).GetProperty("GameUiSession", BindingFlags.Static | BindingFlags.NonPublic);
    private static void Invoke(object target, Type type, string method, params object[] args)
    {
        try { type.GetMethod(method, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(target, args); }
        catch (TargetInvocationException e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
    private static GameUiStartupSession StartupOwner(string directory)
    {
        return (GameUiStartupSession)typeof(GameUiStartupSession).GetMethod("Prepare", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { "OfficialV1", directory, true });
    }
    private static void CompleteStartup(GameScene scene, GameUiStartupSession owner, string directory)
        => Invoke(scene, typeof(GameScene), "CompleteStartupUi", owner, directory);
    private static bool HasOwner(GameScene scene)
        => (bool)typeof(GameScene).GetProperty("HasOfficialWindowOwner", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(scene);
    private static void WithStartupOwner(string package, string directory, Action<GameUiStartupSession> action)
    {
        string installed = Path.Combine(directory, GameUiOfficialV1Catalog.PackageRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(installed)); File.Copy(package, installed, true);
        using (var owner = StartupOwner(directory))
        {
            Check(owner.EffectiveStyle == GameUiStyle.OfficialV1, "Isolated startup preflight failed: " + owner.Diagnostic);
            SessionProperty.SetValue(null, owner);
            try { action(owner); }
            finally { SessionProperty.SetValue(null, null); }
        }
        using (var lease = new FileStream(installed, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Check(lease.Length > 0, "Startup package lease was not released after all scenes detached");
    }
    private static void ExerciseStartupSessions(string package, string directory)
    {
        Config.GameSize = new Size(1024, 768);
        WithStartupOwner(package, Path.Combine(directory, "startup-reentry"), owner =>
        {
            var borrowed = owner.Package;
            Check((string)typeof(GameUiStartupSession).GetProperty("ConfigurationDirectory", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner) == Environment.CurrentDirectory, "Layout configuration base did not follow Zircon.ini working directory");
            object initialStore = null;
            for (int entry = 0; entry < 2; entry++)
            {
                var scene = SceneFixture(); scene.Observer = entry != 0;
                try
                {
                    var originalCell = scene.BeltBox.Grid.Grid[9]; var links = scene.BeltBox.Links;
                    var text = scene.ChatTextBox.TextBox.TextBox; text.Text = "进入场景保留草稿"; text.Select(1, 3); var hwnd = text.Handle;
                    CompleteStartup(scene, owner, directory);
                    var store = Field(typeof(GameUiStartupSession), "layoutStore").GetValue(owner);
                    if (entry == 0) initialStore = store; else Check(ReferenceEquals(initialStore, store), "Relogin replaced the application layout store");
                    Check(HasOwner(scene) && scene.MainPanel.GameUiHudLayout != null && scene.ChatTextBox.GameUiInputLayout != null, "Startup did not install the complete window set");
                    Check(ReferenceEquals(originalCell, scene.BeltBox.Grid.Grid[9]) && ReferenceEquals(links, scene.BeltBox.Links), "Startup replaced a belt cell or its ten links");
                    Check(text.Handle == hwnd && text.Text == "进入场景保留草稿" && text.SelectionStart == 1 && text.SelectionLength == 3, "Startup replaced/reset native input");
                    Reject<InvalidOperationException>(() => owner.Dispose(), "Package disposal while scene borrows it");
                    Reject<InvalidOperationException>(() => CompleteStartup(scene, owner, directory), "Installing the same startup session twice");
                    Check(!owner.IsDisposed && !borrowed.IsDisposed, "Rejected package disposal released resources");
                    Config.GameUiStyle = "Legacy";
                    Check(owner.EffectiveStyle == GameUiStyle.OfficialV1 && !owner.RequiresRestart, "Config edit hot-switched an attached scene");
                }
                finally { scene.Dispose(); }
                Check(!HasOwner(scene) && !borrowed.IsDisposed, "Logout disposed shared package or retained scene owner");
            }
        });
        WithStartupOwner(package, Path.Combine(directory, "startup-clean-fallback"), owner =>
        {
            var scene = SceneFixture();
            try
            {
                bool fail = true; EventHandler<EventArgs> observer = (o, e) => { if (fail) { fail = false; throw new InvalidOperationException("startup late install"); } };
                scene.MiniMapBox.SizeChanged += observer;
                CompleteStartup(scene, owner, directory); scene.MiniMapBox.SizeChanged -= observer;
                Check(!fail && owner.EffectiveStyle == GameUiStyle.Legacy && !owner.RequiresRestart && !HasOwner(scene), "Clean startup failure did not choose whole Legacy");
                Check(scene.CharacterBox.WindowSkin == null && scene.InventoryBox.WindowSkin == null && scene.MainPanel.GameUiHudLayout == null && scene.ChatTextBox.GameUiInputLayout == null, "Fallback retained mixed UI");
                Check(scene.BeltBox.Grid.Grid.Length < 10 && scene.BeltBox.Links.Length == 10, "Legacy fallback did not restore original minimal belt sizing/links");
                CompleteStartup(scene, owner, directory);
                Check(!HasOwner(scene), "Clean fallback retried installation in the same process");
            }
            finally { scene.Dispose(); }
            var next = SceneFixture();
            try { CompleteStartup(next, owner, directory); Check(!HasOwner(next), "Relogin retried a failed startup selection"); }
            finally { next.Dispose(); }
        });
        WithStartupOwner(package, Path.Combine(directory, "startup-dirty-rollback"), owner =>
        {
            var scene = SceneFixture();
            bool fail = true;
            EventHandler<EventArgs> size = (o, e) => { if (fail) { fail = false; Set(scene.MiniMapBox, "_Location", new Point(11, 17)); throw new InvalidOperationException("late failure requiring rollback"); } };
            EventHandler<EventArgs> location = (o, e) => { if (scene.MiniMapBox.WindowSkin == null) { Set(scene.MiniMapBox, "_Location", new Point(11, 17)); throw new InvalidOperationException("rollback blocked by observer"); } };
            scene.MiniMapBox.SizeChanged += size; scene.MiniMapBox.LocationChanged += location;
            try
            {
                CompleteStartup(scene, owner, directory);
                Check(owner.RequiresRestart && HasOwner(scene) && !owner.Package.IsDisposed, "Unfinished rollback lost its owner/package or allowed continued use");
                Reject<InvalidOperationException>(() => owner.Dispose(), "Package release before unfinished rollback");
                Reject<AggregateException>(() => scene.Dispose(), "Scene teardown while rollback remains blocked");
                Check(!scene.IsDisposed && HasOwner(scene) && !owner.IsDisposed, "Failed teardown made rollback non-retryable");
            }
            finally { scene.MiniMapBox.SizeChanged -= size; scene.MiniMapBox.LocationChanged -= location; scene.Dispose(); }
            Check(owner.RequiresRestart && !HasOwner(scene), "Disposal cleared restart requirement or retained detached scene");
        });
        ClearNativeFailureNotice();
        WithStartupOwner(package, Path.Combine(directory, "startup-runtime-fault"), owner => ExerciseRuntimeFault(owner, directory));
        foreach (var result in new[] { ResultCode.DeviceLost, ResultCode.DeviceNotReset, ResultCode.InvalidCall })
        {
            ClearNativeFailureNotice();
            WithStartupOwner(package, Path.Combine(directory, "startup-d3d-" + result.Code), owner => ExerciseD3DFault(owner, directory, result));
        }
        Config.GameUiStyle = "Legacy";
    }
    private static void ClearNativeFailureNotice()
    {
        // Isolate fault owners without destroying the D3D device HWND.
        var notice = (Panel)Field(typeof(TargetForm), "gameUiFailureNotice").GetValue(CEnvir.Target);
        if (notice != null) { CEnvir.Target.Controls.Remove(notice); notice.Dispose(); Set(CEnvir.Target, "gameUiFailureNotice", null); }
    }
    private static void ExerciseD3DFault(GameUiStartupSession owner, string directory, SlimDX.Result result)
    {
        var scene = SceneFixture();
        try
        {
            CompleteStartup(scene, owner, directory); DXManager.DeviceLost = false;
            scene.BeforeDraw += (o, e) => { throw new Direct3D9Exception(result); };
            Invoke(null, typeof(CEnvir), "RenderGame");
            bool expectedLoss = result.Code == ResultCode.DeviceLost.Code || result.Code == ResultCode.DeviceNotReset.Code;
            Check(owner.RequiresRestart != expectedLoss && DXManager.DeviceLost == expectedLoss, "Device loss and permanent D3D fault were not distinguished: " + result);
            Check(!owner.Package.IsDisposed && HasOwner(scene), "D3D error released live scene/package");
        }
        finally { DXManager.AttemptRecovery(); DXManager.DeviceLost = false; scene.Dispose(); }
    }
    private static void ExerciseRuntimeFault(GameUiStartupSession owner, string directory)
    {
        var scene = SceneFixture();
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            using (var socket = new TcpClient())
            {
                socket.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
                using (var peer = listener.AcceptTcpClient())
                {
                    var connection = new CConnection(socket); CEnvir.Connection = connection;
                    try
                    {
                        CompleteStartup(scene, owner, directory);
                        var pending = new C.Chat { Text = "already queued before fault" };
                        CEnvir.Enqueue(pending); Check(connection.SendList.Count == 1, "Normal gameplay enqueue was changed");
                        var editor = scene.ChatTextBox.TextBox.TextBox; editor.Text = "故障后保留草稿"; editor.Select(1, 2); var hwnd = editor.Handle;
                        int clicks = 0, draws = 0, keyEvents = 0, textEvents = 0;
                        scene.MainPanel.MouseClick += (o, e) => clicks++;
                        editor.KeyDown += (o, e) => keyEvents++; editor.KeyPress += (o, e) => textEvents++;
                        scene.MapControl.MapButtons = MouseButtons.Left | MouseButtons.Right; Set(scene, "_AutoRun", true); scene.CanRun = true;
                        CEnvir.Shift = CEnvir.Ctrl = CEnvir.Alt = true;
                        scene.BeforeDraw += (o, e) => { draws++; throw new InvalidOperationException("injected official render failure"); };
                        int errors = CEnvir.ErrorCount;
                        Invoke(null, typeof(CEnvir), "RenderGame");
                        Check(draws == 1 && owner.RequiresRestart && CEnvir.ErrorCount == errors + 1, "Render failure was not captured/logged exactly once");
                        Check(scene.MapControl.MapButtons == MouseButtons.None && !scene.AutoRun && !scene.CanRun && !CEnvir.Shift && !CEnvir.Alt && !CEnvir.Ctrl, "Fault retained held movement/modifier state");
                        Check(CEnvir.Target.Controls.OfType<Panel>().Count() == 1 && CEnvir.Target.Text.Contains("重启"), "Native restart notice was not shown");
                        scene.MainPanel.InvokeMouseClick();
                        Invoke(CEnvir.Target, typeof(TargetForm), "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 300, 300, 0));
                        Invoke(CEnvir.Target, typeof(TargetForm), "OnKeyDown", new KeyEventArgs(Keys.Enter));
                        Invoke(editor, typeof(DXTextBox.MirTextBox), "OnKeyDown", new KeyEventArgs(Keys.Enter));
                        Invoke(editor, typeof(DXTextBox.MirTextBox), "OnKeyPress", new KeyPressEventArgs('\r'));
                        foreach (int msg in new[] { 0x100, 0x102, 0x10F, 0x286, 0x201, 0x302, 0x303, 0x304 })
                        {
                            var native = Message.Create(hwnd, msg, new IntPtr(13), IntPtr.Zero);
                            Invoke(editor, typeof(DXTextBox.MirTextBox), "WndProc", native);
                        }
                        Check(clicks == 0 && keyEvents == 0 && textEvents == 0, "Fault forwarded UI events to native subscribers");
                        Check(editor.Handle == hwnd && editor.Text == "故障后保留草稿" && editor.SelectionStart == 1 && editor.SelectionLength == 2, "Blocked input cleared/recreated the native editor");
                        CEnvir.Enqueue(new C.Chat { Text = "must not be sent" });
                        Check(connection.SendList.Count == 1, "Fault queued another gameplay operation or removed an existing one");
                        byte[] extra;
                        connection.ReceiveList.Enqueue(Packet.ReceivePacket(new G.PingResponse { Ping = 321 }.GetPacketBytes(), out extra));
                        connection.ReceiveList.Enqueue(Packet.ReceivePacket(new G.Ping().GetPacketBytes(), out extra));
                        for (int frame = 0; frame < 3; frame++) CEnvir.GameLoop();
                        Check(connection.Ping == 321 && connection.ReceiveList.IsEmpty, "Fault stopped incoming network responses");
                        int expected = pending.GetPacketBytes().Length + new G.Ping().GetPacketBytes().Length;
                        Check(connection.TotalBytesSent == expected && connection.SendList.IsEmpty, "Existing queue/heartbeat was lost, replayed or duplicated");
                        peer.ReceiveTimeout = 3000; var bytes = new byte[expected]; int received = 0;
                        while (received < expected) { int count = peer.GetStream().Read(bytes, received, expected - received); if (count == 0) throw new Exception("Loopback closed early"); received += count; }
                        byte[] remaining; var first = Packet.ReceivePacket(bytes, out remaining); var second = Packet.ReceivePacket(remaining, out remaining);
                        Check(first is C.Chat && ((C.Chat)first).Text == pending.Text && second is G.Ping && remaining.Length == 0, "Loopback observed unexpected packet bytes");
                        Check(draws == 1 && CEnvir.ErrorCount == errors + 1 && CEnvir.Target.Controls.OfType<Panel>().Count() == 1, "Fault repeated render/log/notice on subsequent frames");
                    }
                    finally { CEnvir.Connection = null; connection.Disconnect(); }
                }
            }
        }
        finally { listener.Stop(); scene.Dispose(); }
    }
}
