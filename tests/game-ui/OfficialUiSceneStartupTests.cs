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
        return GameUiStartupSession.PrepareForLaunch("OfficialV1", directory, null);
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
            Check(owner.EffectiveStyle == GameUiStyle.OfficialV1 && !owner.IsPreview, "Ordinary startup preflight failed: " + owner.Diagnostic);
            SessionProperty.SetValue(null, owner);
            try { action(owner); }
            finally { SessionProperty.SetValue(null, null); }
        }
        using (var lease = new FileStream(installed, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Check(lease.Length > 0, "Startup package lease was not released after all scenes detached");
    }
    // Real native windows and atomic JSON roundtrips; socket/world setup stays in SceneFixture.
    private static void ExercisePersistenceSessions(string package, string directory)
    {
        string previousDirectory = Environment.CurrentDirectory;
        bool previousLoaded = CEnvir.Loaded;
        var previousSettings = CEnvir.WindowSettings;
        string config = Path.Combine(directory, "persistence-config");
        Directory.CreateDirectory(config);
        var resolutions = new[] { new Size(1024, 768), new Size(1366, 768), new Size(1280, 800),
            new Size(1440, 900), new Size(1600, 900), new Size(1920, 1080) };
        var saved = new System.Collections.Generic.Dictionary<Size, System.Collections.Generic.Dictionary<Client.UserModels.WindowType, GameUiWindowLayout>>();
        var rows = new System.Collections.Generic.List<string>();
        try
        {
            Environment.CurrentDirectory = config;
            string legacyDirectory = Path.Combine(config, "legacy", "Data") + Path.DirectorySeparatorChar;
            var legacy = new MirDB.Session(MirDB.SessionMode.Users, legacyDirectory) { BackUp = false };
            var collections = (System.Collections.Generic.Dictionary<Type, Library.MirDB.ADBCollection>)typeof(MirDB.Session)
                .GetField("Collections", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(legacy);
            collections[typeof(Client.UserModels.WindowSetting)] = new MirDB.DBCollection<Client.UserModels.WindowSetting>(legacy);
            Invoke(legacy, typeof(MirDB.Session), "InitializeUsers");
            CEnvir.WindowSettings = legacy.GetCollection<Client.UserModels.WindowSetting>();
            CEnvir.Loaded = true;
            foreach (var type in new[] { Client.UserModels.WindowType.StorageBox, Client.UserModels.WindowType.CharacterBox,
                Client.UserModels.WindowType.InventoryBox, Client.UserModels.WindowType.BeltBox, Client.UserModels.WindowType.MiniMapBox })
            {
                var setting = CEnvir.WindowSettings.CreateNewObject();
                setting.Window = type; setting.Resolution = resolutions[0]; setting.Location = new Point(321, 234);
                setting.Size = new Size(180, 160); setting.Visible = false;
            }
            legacy.Save(true);
            string legacyPath = Path.Combine(legacyDirectory, "Users.db");
            byte[] legacyBefore = File.ReadAllBytes(legacyPath);
            string layoutPath = Path.Combine(config, GameUiLayoutStore.FileName);
            for (int ownerIndex = 0; ownerIndex < 2; ownerIndex++)
            {
                int generation = ownerIndex;
                WithStartupOwner(package, Path.Combine(config, "app"), owner =>
                {
                    for (int entry = 0; entry < (generation == 0 ? 7 : 6); entry++)
                    {
                        Size initialResolution = resolutions[generation == 0 ? Math.Max(0, entry - 1) : entry];
                        Config.GameSize = initialResolution;
                        var scene = SceneFixture();
                        try
                        {
                            var windows = new DXWindow[] { scene.StorageBox, scene.CharacterBox, scene.InventoryBox, scene.BeltBox, scene.MiniMapBox };
                            var original = windows.ToDictionary(w => w.Type, w => CEnvir.WindowSettings.Binding.Single(s => s.Window == w.Type));
                            foreach (var window in windows) window.Settings = original[window.Type];
                            byte[] beforeAttach = File.Exists(layoutPath) ? File.ReadAllBytes(layoutPath) : null;
                            CompleteStartup(scene, owner, config);
                            Check(HasOwner(scene), "Persistence scene failed startup");
                            var store = (GameUiLayoutStore)Field(typeof(GameUiStartupSession), "layoutStore").GetValue(owner);
                            Check(store.FilePath == layoutPath, "Persistence store not beside Zircon.ini");
                            Check(File.Exists(layoutPath) == (beforeAttach != null) && (beforeAttach == null || beforeAttach.SequenceEqual(File.ReadAllBytes(layoutPath))), "Startup wrote persisted preferences");
                            foreach (var window in windows)
                                Check(window.Settings == null && ReferenceEquals(Field(typeof(DXWindow), "_OfficialLayout").GetValue(window), store), "Native window retained Legacy layout routing");
                            Check(scene.NPCBox.WindowSkin == null && Field(typeof(DXWindow), "_OfficialLayout").GetValue(scene.NPCBox) == null,
                                "Temporary native NPC card acquired style or persistence");
                            bool seed = generation == 0 && entry == 0;
                            bool[] initialVisibility = windows.Select(w => w.Visible).ToArray();
                            if (!seed) foreach (var window in windows)
                            {
                                var expected = saved[initialResolution][window.Type];
                                Check(window.Location == expected.Location && window.Size == expected.Size && window.Visible == (expected.Visible ?? true),
                                    "Scene entry lost matching-resolution geometry/visibility: " + window.Type + "/" + initialResolution);
                            }
                            for (int index = 0; index < resolutions.Length; index++)
                            {
                                Size resolution = resolutions[index];
                                byte[] beforeResize = File.Exists(layoutPath) ? File.ReadAllBytes(layoutPath) : null;
                                Config.GameSize = resolution; scene.Size = resolution;
                                Check(File.Exists(layoutPath) == (beforeResize != null) && (beforeResize == null || beforeResize.SequenceEqual(File.ReadAllBytes(layoutPath))), "Resolution pass changed persisted preferences");
                                if (seed)
                                {
                                    // Real window setters drive production save handlers; every resolution gets its own geometry/visibility.
                                    foreach (var window in windows)
                                    {
                                        window.IsMoving = true;
                                        try { window.Location = new Point(Math.Max(0, window.Location.X - index - 2), window.Location.Y); }
                                        finally { window.IsMoving = false; }
                                        window.Visible = index % 2 == 0;
                                    }
                                    saved[resolution] = windows.ToDictionary(w => w.Type, w => new GameUiWindowLayout(w.Location, w.Size, w.AutomaticVisiblity ? (bool?)w.Visible : null));
                                }
                                else
                                {
                                    for (int w = 0; w < windows.Length; w++)
                                    {
                                        var window = windows[w]; var expected = saved[resolution][window.Type];
                                        Check(window.Location == expected.Location && window.Size == expected.Size, "Native geometry did not restore matching resolution: " + window.Type + "/" + resolution);
                                        Check(window.Visible == initialVisibility[w], "Resize replayed another resolution's visibility: " + window.Type);
                                    }
                                }
                                Check(!scene.NPCBox.Visible, "Layout reentry or resize reopened a temporary NPC card");
                                foreach (var window in windows)
                                {
                                    GameUiWindowLayout persisted;
                                    Check(store.TryGet(resolution, window.Type, out persisted), "Native user save missing: " + window.Type);
                                    var expected = saved[resolution][window.Type];
                                    Check(persisted.Location == expected.Location && persisted.Size == expected.Size && persisted.Visible == expected.Visible,
                                        "Resolution layout record changed or crossed keys: " + window.Type);
                                    rows.Add(generation + "|" + entry + "|" + resolution.Width + "x" + resolution.Height + "|" + window.Type + "|visible=" + window.Visible + "|storedVisible=" + persisted.Visible + "|" + window.Location.X + "," + window.Location.Y + "," + window.Size.Width + "," + window.Size.Height);
                                }
                            }
                            byte[] beforeDispose = File.ReadAllBytes(layoutPath);
                            scene.Dispose();
                            Check(beforeDispose.SequenceEqual(File.ReadAllBytes(layoutPath)), "Scene disposal overwrote user preferences");
                            foreach (var window in windows) Check(window.IsDisposed, "Persistence scene retained native window");
                        }
                        finally { if (!scene.IsDisposed) scene.Dispose(); }
                        legacy.Save(true);
                        Check(legacyBefore.SequenceEqual(File.ReadAllBytes(legacyPath)), "Official layout changed Legacy Users.db");
                    }
                });
            }
            Check(saved.Count == 6 && saved.Values.All(v => v.Count == 5), "Persistence matrix incomplete");
            Check(!Directory.GetFiles(config, "*.tmp").Any(), "Atomic layout save leaked temporary file");
            File.WriteAllLines(Path.Combine(directory, "layout-persistence-results.txt"), rows);
            File.Copy(layoutPath, Path.Combine(directory, "layout-persistence-saved.json"));
            File.Copy(legacyPath, Path.Combine(directory, "layout-persistence-legacy.db"));
        }
        finally { Environment.CurrentDirectory = previousDirectory; CEnvir.Loaded = previousLoaded; CEnvir.WindowSettings = previousSettings; Config.GameSize = new Size(1024, 768); }
    }

    // Explicit native reset/reload may rebuild tabs; ordinary resize must retain them.
    private static void ExerciseChatReconstruction(string package, string directory)
    {
        var oldSession = CEnvir.Session;
        int baselineTabs = Client.Scenes.Views.ChatTab.Tabs.Count, baselineListeners = ChatListenerCount();
        var rows = new System.Collections.Generic.List<string>();
        var resolutions = new[] { new Size(1024, 768), new Size(1366, 768), new Size(1280, 800),
            new Size(1440, 900), new Size(1600, 900), new Size(1920, 1080) };
        try
        {
            foreach (bool official in new[] { false, true })
            {
                string mode = official ? "OfficialV1" : "Legacy";
                string data = Path.Combine(directory, "chat-rebuild-" + mode, "Data") + Path.DirectorySeparatorChar;
                Directory.CreateDirectory(data);
                var session = new MirDB.Session(MirDB.SessionMode.Users, data) { BackUp = false };
                var collections = (System.Collections.Generic.Dictionary<Type, Library.MirDB.ADBCollection>)typeof(MirDB.Session)
                    .GetField("Collections", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session);
                collections[typeof(Client.UserModels.ChatTabControlSetting)] = new MirDB.DBCollection<Client.UserModels.ChatTabControlSetting>(session);
                collections[typeof(Client.UserModels.ChatTabPageSetting)] = new MirDB.DBCollection<Client.UserModels.ChatTabPageSetting>(session);
                Invoke(session, typeof(MirDB.Session), "InitializeUsers");
                CEnvir.Session = session;
                Action<GameUiStartupSession> exercise = owner =>
                {
                    Config.GameSize = resolutions[0];
                    var scene = SceneFixture();
                    try
                    {
                        scene.ConfigBox = new DXConfigWindow { Parent = scene, Visible = false };
                        scene.ChatOptionsBox = new Client.Scenes.Views.ChatOptionsDialog { Parent = scene, Visible = false };
                        scene.ChatOptionsBox.CreateDefaultWindows();
                        var tab = Client.Scenes.Views.ChatTab.Tabs.Last();
                        tab.Panel.Text = "恢复频道";
                        tab.Panel.GlobalCheckBox.Checked = false;
                        tab.Panel.TransparentCheckBox.Checked = true;
                        tab.Panel.AlertCheckBox.Checked = false;
                        var second = scene.ChatOptionsBox.AddNewTab();
                        second.Parent = tab.Parent; second.Panel.Text = "第二页";
                        tab.CurrentTabControl.SelectedTab = second;
                        scene.SaveChatTabs(); session.Save(true);
                        byte[] saved = File.ReadAllBytes(Path.Combine(data, "Users.db"));
                        if (owner != null) CompleteStartup(scene, owner, directory);
                        int listeners = ChatListenerCount();
                        Check(listeners == baselineListeners + (official ? 1 : 0), "Chat rebuild listener setup mismatch");
                        Action<string, bool> click = (label, accept) =>
                        {
                            Children(scene.ChatOptionsBox).OfType<DXButton>().Single(b => b.Label.Text == label).InvokeMouseClick();
                            var box = Children(scene).OfType<DXMessageBox>().Single(b => !b.IsDisposed);
                            Check(box.WindowSkin == null, "Chat confirmation was restyled");
                            (accept ? box.YesButton : box.NoButton).InvokeMouseClick();
                            Check(box.IsDisposed, "Chat confirmation retained modal");
                        };
                        var originalInput = scene.ChatTextBox;
                        var originalTabs = Client.Scenes.Views.ChatTab.Tabs.ToArray();
                        click("重置所有", false); click("重新加载所有", false);
                        Check(ReferenceEquals(scene.ChatTextBox, originalInput) && originalTabs.SequenceEqual(Client.Scenes.Views.ChatTab.Tabs), "Cancel changed native chat");
                        for (int cycle = 0; cycle < 3; cycle++)
                        {
                            foreach (string operation in new[] { "重置所有", "重新加载所有", "空配置加载" })
                            {
                                Config.GameSize = resolutions[0];
                                if (scene.Size != Config.GameSize) scene.Size = Config.GameSize;
                                // Empty configuration is selected by resolution, without mutating saved records.
                                if (operation == "空配置加载") Config.GameSize = new Size(1280, 800);
                                var previousInput = scene.ChatTextBox;
                                var previousTabs = Client.Scenes.Views.ChatTab.Tabs.ToArray();
                                click(operation == "重置所有" ? "重置所有" : "重新加载所有", true);
                                bool replacesInput = operation != "重新加载所有";
                                Check(previousInput.IsDisposed == replacesInput, "Native input replacement semantics changed: " + operation);
                                Check(replacesInput != ReferenceEquals(previousInput, scene.ChatTextBox), "Native input identity mismatch");
                                Check(previousTabs.All(t => t.IsDisposed), "Explicit rebuild retained removed tabs");
                                Check(scene.ChatOptionsBox.WindowSkin == null && scene.ConfigBox.WindowSkin == null, "Native options dialog was restyled");
                                Check((scene.ChatTextBox.GameUiInputLayout != null) == official, "Rebuilt native input lost Official adapter: " + operation);
                                Check(ChatListenerCount() == listeners, "Explicit rebuild duplicated chat listeners");
                                var currentTabs = Client.Scenes.Views.ChatTab.Tabs.ToArray();
                                Check(currentTabs.Length == (operation == "重新加载所有" ? 2 : 1), "Native rebuilt tab count mismatch");
                                if (operation == "重新加载所有")
                                {
                                    Check(currentTabs[0].Panel.Text == "恢复频道" && !currentTabs[0].Panel.GlobalCheckBox.Checked && currentTabs[0].Panel.TransparentCheckBox.Checked && !currentTabs[0].Panel.AlertCheckBox.Checked,
                                        "Reload lost native tab filters/transparency/alert");
                                    Check(currentTabs[0].CurrentTabControl.SelectedTab == currentTabs[1], "Reload lost selected page");
                                }
                                // Legacy resize intentionally reloads by native design; retention applies to Official only.
                                if (official)
                                {
                                    var input = scene.ChatTextBox; var editor = input.TextBox.TextBox;
                                    editor.Text = "重建后保留草稿"; editor.Select(2, 3); var handle = editor.Handle;
                                    scene.ReceiveChat("重建后的保留消息", Library.MessageType.System);
                                    var histories = currentTabs.Select(t => t.History.ToArray()).ToArray();
                                    Check(histories.All(h => h.Length > 0), "History retention probe had no messages");
                                    foreach (var resolution in resolutions)
                                    {
                                        Config.GameSize = resolution; scene.Size = resolution;
                                        Check(ReferenceEquals(input, scene.ChatTextBox) && !input.IsDisposed && currentTabs.SequenceEqual(Client.Scenes.Views.ChatTab.Tabs), "Resize rebuilt live chat");
                                        Check(editor.Handle == handle && editor.Text == "重建后保留草稿" && editor.SelectionStart == 2 && editor.SelectionLength == 3, "Resize lost rebuilt native editor state");
                                        Check(input.Location == new Point((resolution.Width - 574) / 2, resolution.Height - 30) && input.Size == new Size(574, 22), "Rebuilt input dock mismatch");
                                        Check(editor.Bounds == input.TextBox.DisplayArea, "Rebuilt input WinForms bounds stale");
                                        var primary = currentTabs[0].CurrentTabControl;
                                        Check(primary.Location == new Point((resolution.Width - 574) / 2, resolution.Height - 138) && primary.Size == new Size(574, 106), "Rebuilt primary history dock mismatch");
                                        Check(Children(scene).OfType<DXTabControl>().Count() == 1, "Rebuild retained an empty temporary group");
                                        Check(currentTabs.Select((t, i) => t.History.SequenceEqual(histories[i])).All(v => v), "Resize cleared rebuilt chat history");
                                        Check(currentTabs.All(t => t.GameUiViewportLayout != null) && ChatListenerCount() == listeners, "Rebuilt tabs lost adapter or duplicated listener");
                                        rows.Add(mode + "|" + cycle + "|" + operation + "|" + resolution.Width + "x" + resolution.Height);
                                    }
                                }
                                else rows.Add(mode + "|" + cycle + "|" + operation);
                                session.Save(true);
                                Check(saved.SequenceEqual(File.ReadAllBytes(Path.Combine(data, "Users.db"))), "Reset/reload changed saved native configuration");
                            }
                        }
                    }
                    finally { scene.Dispose(); }
                    Check(ChatListenerCount() == baselineListeners && Client.Scenes.Views.ChatTab.Tabs.Count == baselineTabs, "Scene disposal leaked rebuilt chat");
                };
                if (official) WithStartupOwner(package, Path.Combine(directory, "chat-rebuild-owner"), exercise);
                else exercise(null);
            }
            foreach (bool reentrant in new[] { false, true })
            {
                WithStartupOwner(package, Path.Combine(directory, "chat-rebuild-fault-" + reentrant), owner =>
                {
                    var scene = SceneFixture();
                    try
                    {
                        scene.ChatOptionsBox = new Client.Scenes.Views.ChatOptionsDialog { Parent = scene, Visible = false };
                        scene.ChatOptionsBox.CreateDefaultWindows();
                        CompleteStartup(scene, owner, directory);
                        var borrowed = owner.Package;
                        Action rebuild = () =>
                        {
                            if (reentrant) scene.Size = new Size(1366, 768);
                            else
                            {
                                for (int i = Client.Scenes.Views.ChatTab.Tabs.Count - 1; i >= 0; i--)
                                    Client.Scenes.Views.ChatTab.Tabs[i].Panel.RemoveButton.InvokeMouseClick();
                                scene.ChatOptionsBox.CreateDefaultWindows();
                                throw new InvalidOperationException("injected native reconstruction failure");
                            }
                        };
                        Reject<InvalidOperationException>(() => Invoke(scene, typeof(GameScene), "RebuildChatTabs", rebuild), "Chat reconstruction failure");
                        Check(owner.RequiresRestart && HasOwner(scene) && !borrowed.IsDisposed, "Rebuild failure lost containment/package ownership");
                        Check(scene.ChatTextBox.GameUiInputLayout == null && ChatListenerCount() == baselineListeners, "Failed rebuild retained stale adapter/listener");
                        Reject<InvalidOperationException>(() => owner.Dispose(), "Releasing package before failed chat scene disposal");
                        rows.Add("fault|" + (reentrant ? "resize-during-rebuild" : "native-action"));
                    }
                    finally { scene.Dispose(); }
                    Check(!HasOwner(scene) && ChatListenerCount() == baselineListeners && Client.Scenes.Views.ChatTab.Tabs.Count == baselineTabs,
                        "Failed chat rebuild disposal leaked owner/listener/tab");
                });
                ClearNativeFailureNotice();
            }
            File.WriteAllLines(Path.Combine(directory, "chat-reconstruction-results.txt"), rows);
        }
        finally { CEnvir.Session = oldSession; Config.GameSize = new Size(1024, 768); }
    }

    private static void ExerciseStartupSessions(string package, string directory)
    {
        Config.GameSize = new Size(1024, 768);
        ExerciseChatReconstruction(package, directory);
        ExerciseChatPersistence(package, directory);
        ExercisePersistenceSessions(package, directory);
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
