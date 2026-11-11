using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using Library.MirDB;
using MirDB;
using Client.Controls;
using Client.Envir;
using Client.UserModels;

internal static class OfficialUiLayoutTests
{
    private static int checks;
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    private static readonly Size Resolution = new Size(1024, 768);
    private sealed class Window : DXWindow
    {
        public bool PersistVisibility = true;
        public WindowType Identity = WindowType.CharacterBox;
        public override void OnIsMovingChanged(bool oldValue, bool newValue) { }
        public override WindowType Type => Identity;
        public override bool CustomSize => true;
        public override bool AutomaticVisiblity => PersistVisibility;
    }
    private static GameUiWindowLayout Layout(int x = 10, bool? visible = true) => new GameUiWindowLayout(new Point(x, 20), new Size(100, 80), visible);
    private static void Tests(string root)
    {
        Config.GameSize = Resolution;
        // Session discovers its caller assembly, so register the unchanged Client model explicitly.
        string legacyDirectory = Path.Combine(root, "legacy", "Data") + Path.DirectorySeparatorChar;
        var legacySession = new Session(SessionMode.Users, legacyDirectory) { BackUp = false };
        var collections = (Dictionary<Type, ADBCollection>)typeof(Session).GetField("Collections", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(legacySession);
        collections[typeof(WindowSetting)] = new DBCollection<WindowSetting>(legacySession);
        typeof(Session).GetMethod("InitializeUsers", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(legacySession, null);
        CEnvir.WindowSettings = legacySession.GetCollection<WindowSetting>();
        CEnvir.Loaded = true;
        var oldSetting = CEnvir.WindowSettings.CreateNewObject();
        oldSetting.Window = WindowType.CharacterBox; oldSetting.Resolution = Resolution;
        oldSetting.Location = new Point(321, 234); oldSetting.Size = new Size(180, 160); oldSetting.Visible = false;
        legacySession.Save(true);
        string legacyPath = Path.Combine(legacyDirectory, "Users.db");
        byte[] legacyBefore = File.ReadAllBytes(legacyPath);
        string directory = Path.Combine(root, "config");
        var store = new GameUiLayoutStore(directory);
        GameUiWindowLayout layout;
        Check(!store.TryGet(Resolution, WindowType.CharacterBox, out layout) && !File.Exists(store.FilePath), "Missing file creates state");
        Check(store.TrySave(Resolution, WindowType.CharacterBox, Layout()), "Initial save failed");
        string valid = File.ReadAllText(store.FilePath);
        Check(new GameUiLayoutStore(directory).TryGet(Resolution, WindowType.CharacterBox, out layout) && layout.Location.X == 10 && layout.Visible == true, "Save/reload mismatch");
        Size second = new Size(1920, 1080);
        Check(store.TrySave(second, WindowType.CharacterBox, Layout(90)), "Second resolution save");
        Check(store.TryGet(Resolution, WindowType.CharacterBox, out layout) && layout.Location.X == 10, "Resolution keys overlap");
        Check(store.TryGet(second, WindowType.CharacterBox, out layout) && layout.Location.X == 90, "Resolution layout lost");
        Check(!store.TrySave(Resolution, WindowType.None, Layout()), "None window persisted");
        Check(!store.TrySave(Resolution, (WindowType)999, Layout()), "Unknown enum persisted");
        Check(!store.TrySave(new Size(800, 600), WindowType.CharacterBox, Layout()), "Unknown resolution persisted");
        Check(!store.TrySave(Resolution, WindowType.CharacterBox, Layout(-1)), "Negative position persisted");
        Check(!store.TrySave(Resolution, WindowType.CharacterBox, Layout(int.MaxValue)), "Coordinate overflow persisted");
        Check(!store.TrySave(Resolution, WindowType.CharacterBox, new GameUiWindowLayout(Point.Empty, Size.Empty, null)), "Empty size persisted");
        Check(store.TrySave(Resolution, WindowType.InventoryBox, new GameUiWindowLayout(new Point(924, 688), new Size(100, 80), null)), "Exact screen edge rejected");
        string lastGood = File.ReadAllText(store.FilePath);
        using (var locked = new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            Check(!store.TrySave(Resolution, WindowType.CharacterBox, Layout(50)) && store.LastDiagnostic != null, "Failed write reported success");
        Check(File.ReadAllText(store.FilePath) == lastGood && !Directory.GetFiles(directory, "*.tmp").Any(), "Failed write damaged existing layout or leaked temp file");
        Check(store.TryGet(Resolution, WindowType.CharacterBox, out layout) && layout.Location.X == 10, "Failed write changed memory snapshot");
        foreach (string invalid in new[]
        {
            "{", valid.Replace("\"schemaVersion\":1", "\"schemaVersion\":2"),
            valid.Replace("OfficialV1", "Legacy"), valid.Replace("\"x\":10", "\"x\":-1"),
            valid.Replace("\"x\":10", "\"x\":10.5"), valid.Replace("\"visible\":true", "\"visible\":\"true\""),
            valid.Replace("\"schemaVersion\":1", "\"businessState\":1,\"schemaVersion\":1"),
            valid.Replace("\"windows\":[", "\"windows\":[null,")
        })
        {
            File.WriteAllText(store.FilePath, invalid);
            var broken = new GameUiLayoutStore(directory);
            Check(!broken.TryGet(Resolution, WindowType.CharacterBox, out layout) && broken.LastDiagnostic != null, "Invalid JSON/version/geometry did not retain defaults");
        }
        File.WriteAllText(store.FilePath, valid);
        store.Reload();
        using (var window = new Window { Size = new Size(120, 100), Location = new Point(40, 50) })
        {
            window.UseOfficialLayout(store);
            window.Visible = false;
            Check(File.ReadAllText(store.FilePath) == valid, "Initialization wrote layout");
            window.LoadSettings();
            Check(window.Settings == null && window.Location == new Point(10, 20) && window.Size == new Size(100, 80) && window.Visible, "Official restore or Legacy isolation failed");
            using (window.SuppressLayoutWrites()) using (window.SuppressLayoutWrites())
            {
                window.Visible = false;
                window.IsMoving = true; window.Location = new Point(30, 40); window.IsMoving = false;
                Check(File.ReadAllText(store.FilePath) == valid, "Nested layout suppression failed");
            }
            window.IsMoving = true; window.Location = new Point(50, 60); window.IsMoving = false;
            Check(store.TryGet(Resolution, WindowType.CharacterBox, out layout) && layout.Location == new Point(50, 60) && layout.Visible == false, "Move did not persist permitted fields");
            window.Visible = true;
            Check(store.TryGet(Resolution, WindowType.CharacterBox, out layout) && layout.Visible == true, "Automatic visibility not persisted");
        }
        using (var npc = new Window { PersistVisibility = false, Size = new Size(90, 70), Visible = false })
        {
            npc.UseOfficialLayout(store); npc.LoadSettings();
            Check(!npc.Visible && npc.Settings == null, "NPC reopened from stored visibility");
            npc.IsMoving = true; npc.Location = new Point(70, 80); npc.IsMoving = false;
            Check(store.TryGet(Resolution, WindowType.CharacterBox, out layout) && layout.Visible == null, "Service visibility persisted");
            string before = File.ReadAllText(store.FilePath);
            npc.Identity = WindowType.None; npc.LoadSettings(); npc.Visible = true;
            npc.IsMoving = true; npc.Location = new Point(80, 90); npc.IsMoving = false;
            Check(File.ReadAllText(store.FilePath) == before, "None NPC changed layout store");
        }
        string blocker = Path.Combine(root, "not-directory"); File.WriteAllText(blocker, "block");
        Check(!new GameUiLayoutStore(blocker).TrySave(Resolution, WindowType.CharacterBox, Layout()), "Directory failure reported success");
        Check(CEnvir.WindowSettings.Count == 1 && oldSetting.Location == new Point(321, 234) && oldSetting.Size == new Size(180, 160) && !oldSetting.Visible, "Official operations mutated Legacy settings in memory");
        legacySession.Save(true);
        byte[] legacyAfter = File.ReadAllBytes(legacyPath);
        Check(legacyBefore.SequenceEqual(legacyAfter), "Official operations changed Legacy Users.db");
        using (var hash = SHA256.Create())
            Console.WriteLine("Legacy Users.db SHA256 before/after: " + BitConverter.ToString(hash.ComputeHash(legacyAfter)).Replace("-", "").ToLowerInvariant());
    }
    [STAThread]
    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "ZirconOfficialUiLayout-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try { Tests(root); Console.WriteLine("Official UI layout: " + checks + " checks passed."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { CEnvir.WindowSettings = null; CEnvir.Loaded = false; Directory.Delete(root, true); }
    }
}
