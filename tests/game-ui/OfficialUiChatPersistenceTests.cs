using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using Client.Controls;
using Client.Envir;
using Client.Scenes;
using Client.Scenes.Views;
using Library;

internal static partial class OfficialUiWindowSetTests
{
    private static readonly Size[] ChatResolutions = { new Size(1024, 768), new Size(1366, 768), new Size(1280, 800),
        new Size(1440, 900), new Size(1600, 900), new Size(1920, 1080) };
    private static ChatTab[] SceneChat(GameScene scene) => ChatTab.Tabs.Where(t => !t.IsDisposed && t.CurrentTabControl?.Parent == scene).ToArray();
    private static ChatTab[] HeaderPages(DXTabControl group) => group.TabButtons.Select(b => Children(group).OfType<ChatTab>().Single(t => t.TabButton == b)).ToArray();
    private static DXCheckBox[] ChatFilters(ChatTab tab) => new[] { tab.Panel.LocalCheckBox, tab.Panel.WhisperCheckBox,
        tab.Panel.GroupCheckBox, tab.Panel.GuildCheckBox, tab.Panel.ShoutCheckBox, tab.Panel.GlobalCheckBox,
        tab.Panel.ObserverCheckBox, tab.Panel.HintCheckBox, tab.Panel.SystemCheckBox, tab.Panel.GainsCheckBox,
        tab.Panel.TransparentCheckBox, tab.Panel.AlertCheckBox };
    private static int ChatFlags(ChatTab tab) => ChatFilters(tab).Select((c, i) => c.Checked ? 1 << i : 0).Sum();
    private static void SetChatPage(ChatTab tab, string name, int flags)
    {
        tab.Panel.NameTextBox.TextBox.Text = name;
        var filters = ChatFilters(tab);
        for (int i = 0; i < filters.Length; i++) filters[i].Checked = (flags & (1 << i)) != 0;
    }
    private static GameScene ChatScene(Size size)
    {
        Config.GameSize = size;
        var scene = SceneFixture();
        scene.ConfigBox = new DXConfigWindow { Parent = scene, Visible = false };
        scene.ChatOptionsBox = new ChatOptionsDialog { Parent = scene, Visible = false };
        scene.ChatOptionsBox.CreateDefaultWindows();
        return scene;
    }
    private static void ChatClick(GameScene scene, string label, bool? accept = null)
    {
        Children(scene.ChatOptionsBox).OfType<DXButton>().Single(b => b.Label.Text == label).InvokeMouseClick();
        if (!accept.HasValue) return;
        var box = Children(scene).OfType<DXMessageBox>().Single(b => !b.IsDisposed);
        Check(box.WindowSkin == null, "Persistence confirmation must retain native skin");
        (accept.Value ? box.YesButton : box.NoButton).InvokeMouseClick();
        Check(box.IsDisposed, "Persistence confirmation leaked");
    }
    private static GameUiChatStore.Snapshot ReadChat(string directory, Size resolution)
    {
        var store = new GameUiChatStore(directory); GameUiChatStore.Snapshot value;
        Check(store.TryGet(resolution, out value), "Missing saved chat entry: " + resolution + " / " + store.LastDiagnostic);
        return value;
    }
    private static void CheckChatSnapshot(GameScene scene, GameUiChatStore.Snapshot saved)
    {
        var groups = SceneChat(scene).Select(t => t.CurrentTabControl).Distinct().ToArray();
        Check(groups.Length == saved.groups.Length, "Restored group count mismatch");
        Check(Children(scene).OfType<DXTabControl>().Count() == groups.Length, "Persistence retained an empty temporary group");
        foreach (var group in saved.groups)
        {
            var live = groups.Single(g => HeaderPages(g).Select(t => t.Panel.NameTextBox.TextBox.Text).SequenceEqual(group.tabs.Select(t => t.name)));
            Check(new Rectangle(live.Location, live.Size) == new Rectangle(group.x, group.y, group.width, group.height), "Restored geometry mismatch");
            var pages = HeaderPages(live);
            Check(pages.Select(ChatFlags).SequenceEqual(group.tabs.Select(t => t.flags)), "Restored native flags mismatch");
            Check((group.selected < 0 ? null : pages[group.selected]) == live.SelectedTab, "Restored selection mismatch");
            Check(pages.All(t => t.GameUiViewportLayout != null), "Restored pages lost geometry owner");
        }
    }
    private static void ExerciseChatPersistence(string package, string directory)
    {
        var oldSession = CEnvir.Session;
        int initialTabs = ChatTab.Tabs.Count, initialListeners = ChatListenerCount();
        string config = Path.Combine(directory, "chat-routing-config"); Directory.CreateDirectory(config);
        string data = Path.Combine(config, "Data") + Path.DirectorySeparatorChar; Directory.CreateDirectory(data);
        var rows = new List<string>();
        try
        {
            var session = new MirDB.Session(MirDB.SessionMode.Users, data) { BackUp = false };
            var collections = (Dictionary<Type, Library.MirDB.ADBCollection>)typeof(MirDB.Session).GetField("Collections", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session);
            collections[typeof(Client.UserModels.ChatTabControlSetting)] = new MirDB.DBCollection<Client.UserModels.ChatTabControlSetting>(session);
            collections[typeof(Client.UserModels.ChatTabPageSetting)] = new MirDB.DBCollection<Client.UserModels.ChatTabPageSetting>(session);
            Invoke(session, typeof(MirDB.Session), "InitializeUsers"); CEnvir.Session = session;
            var saved = new Dictionary<Size, GameUiChatStore.Snapshot>();
            string path = Path.Combine(config, "GameUiChat.OfficialV1.json");
            byte[] legacyBytes = null;
            WithStartupOwner(package, Path.Combine(config, "app"), owner =>
            {
                var scene = ChatScene(ChatResolutions[0]);
                byte[] fullDisk = null;
                try
                {
                    var first = SceneChat(scene).Single(); SetChatPage(first, "原生数据库", 4095);
                    Check(scene.SaveChatTabs(), "Legacy save failed"); session.Save(true);
                    legacyBytes = File.ReadAllBytes(Path.Combine(data, "Users.db"));
                    Check(!File.Exists(path), "Legacy created Official sidecar");
                    CompleteStartup(scene, owner, config);
                    Check(HasOwner(scene) && !owner.RequiresRestart, "Persistence scene installation failed");
                    var primary = first.CurrentTabControl;
                    var second = scene.ChatOptionsBox.AddNewTab(); second.Parent = primary;
                    var third = scene.ChatOptionsBox.AddNewTab(); var floating = third.CurrentTabControl;
                    SetChatPage(first, "本地/组队", 0x555); SetChatPage(second, "世界 中文", 0xaaa); SetChatPage(third, "独立频道", 0xfff);
                    primary.TabButtons.Reverse(); primary.SelectedTab = first; RefreshChatFrame(primary);
                    var input = scene.ChatTextBox; var editor = input.TextBox.TextBox;
                    var handle = editor.Handle; editor.Text = "输入法草稿保留"; editor.Select(2, 3);
                    foreach (var size in ChatResolutions)
                    {
                        Config.GameSize = size; scene.Size = size;
                        int index = Array.IndexOf(ChatResolutions, size);
                        primary.Size = new Size(350 + index * 10, 170 + index * 5); primary.Location = new Point(40 + index * 15, 50 + index * 9);
                        floating.Size = new Size(240 + index * 7, 130 + index * 3); floating.Location = new Point(430 + index * 11, 120 + index * 7);
                        ChatClick(scene, "保存所有"); saved[size] = ReadChat(config, size);
                        Check(saved[size].groups.Length == 2, "Save lost a live group");
                        var main = saved[size].groups.Single(g => g.primary);
                        Check(main.floating && main.selected == 1 && main.tabs.Select(t => t.name).SequenceEqual(new[] { "世界 中文", "本地/组队" }), "Header order or primary selection not captured");
                        Check(saved[size].groups.SelectMany(g => g.tabs).Select(t => t.flags).OrderBy(v => v).SequenceEqual(new[] { 0x555, 0xaaa, 0xfff }), "Save lost filter bits");
                        Check(saved[size].groups.Select(g => g.id).SequenceEqual(saved[ChatResolutions[0]].groups.Select(g => g.id)), "Resolution save changed group identity");
                        rows.Add("save|" + size.Width + "x" + size.Height + "|header-order|12-flags|two-groups");
                    }
                    File.Copy(path, Path.Combine(directory, "chat-persistence-saved.json"));
                    var tabs = SceneChat(scene); var histories = tabs.Select(t => t.History.ToArray()).ToArray();
                    primary.SelectedTab = second; SetChatPage(first, "本地/组队", 7);
                    foreach (var size in ChatResolutions.Reverse())
                    {
                        Config.GameSize = size; scene.Size = size;
                        var main = saved[size].groups.Single(g => g.primary); var other = saved[size].groups.Single(g => !g.primary);
                        Check(new Rectangle(primary.Location, primary.Size) == new Rectangle(main.x, main.y, main.width, main.height), "Resize did not recover saved primary position");
                        Check(new Rectangle(floating.Location, floating.Size) == new Rectangle(other.x, other.y, other.width, other.height), "Resize did not recover saved secondary position");
                        Check(ReferenceEquals(input, scene.ChatTextBox) && editor.Handle == handle && editor.Text == "输入法草稿保留" && editor.SelectionStart == 2 && editor.SelectionLength == 3, "Resize replaced native editor or draft");
                        Check(tabs.SequenceEqual(SceneChat(scene)) && tabs.Select((t, i) => t.History.SequenceEqual(histories[i])).All(v => v), "Resize reconstructed tabs/history");
                        Check(primary.SelectedTab == second && ChatFlags(first) == 7, "Resize replayed selection/filters");
                        rows.Add("resize|" + size.Width + "x" + size.Height + "|identity|history|editor|live-filters");
                    }
                    // Renames invalidate the saved topology for resize; live geometry must survive.
                    SetChatPage(first, "临时改名", 7); primary.Location = new Point(13, 17);
                    Config.GameSize = ChatResolutions[1]; scene.Size = Config.GameSize;
                    Check(primary.Location == new Point(13, 17), "Resize applied stale geometry after rename");
                    SetChatPage(first, "本地/组队", 7);
                    primary.TabButtons.Reverse(); primary.Location = new Point(23, 27);
                    Config.GameSize = ChatResolutions[2]; scene.Size = Config.GameSize;
                    Check(primary.Location == new Point(23, 27), "Resize applied stale geometry after reorder");
                    primary.TabButtons.Reverse();
                    var extra = scene.ChatOptionsBox.AddNewTab(); extra.Parent = primary;
                    primary.Location = new Point(33, 37);
                    Config.GameSize = ChatResolutions[3]; scene.Size = Config.GameSize;
                    Check(primary.Location == new Point(33, 37), "Resize applied stale geometry after page addition");
                    extra.Panel.RemoveButton.InvokeMouseClick();
                    byte[] beforeBusySave = File.ReadAllBytes(path); first.Updating = true;
                    try { Check(!scene.SaveChatTabs(), "Save during native header update succeeded"); }
                    finally { first.Updating = false; }
                    Check(beforeBusySave.SequenceEqual(File.ReadAllBytes(path)), "Busy save changed preferences");
                    rows.Add("stale-topology|rename|header-reorder|page-count|busy-save-preserves-file");
                    Config.GameSize = ChatResolutions[0]; scene.Size = Config.GameSize;
                    ChatClick(scene, "重新加载所有", false);
                    Check(tabs.SequenceEqual(SceneChat(scene)), "Cancelled reload rebuilt pages");
                    ChatClick(scene, "重新加载所有", true);
                    CheckChatSnapshot(scene, saved[Config.GameSize]);
                    Check(tabs.All(t => t.IsDisposed) && ReferenceEquals(input, scene.ChatTextBox) && editor.Handle == handle && editor.Text == "输入法草稿保留", "Explicit reload lifetime mismatch");
                    Check(ChatListenerCount() == initialListeners + 1, "Explicit reload duplicated chat listener");
                    rows.Add("reload|real-button|native-confirmation|topology|flags|selection|input-preserved");
                    byte[] disk = File.ReadAllBytes(path); fullDisk = disk;
                    var current = SceneChat(scene); current[0].CurrentTabControl.Location = new Point(12, 12);
                    using (var lease = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        Check(!scene.SaveChatTabs(), "Locked sidecar save reported success");
                        ChatClick(scene, "保存所有");
                    }
                    Check(disk.SequenceEqual(File.ReadAllBytes(path)), "Failed Official save changed previous bytes");
                    Check(SceneChat(scene).Any(t => t.History.Any(l => l.Text.Contains("聊天布局保存失败"))), "Save button did not report failure");
                    rows.Add("write-lock|false-result|failure-announcement|previous-file-preserved");
                    File.WriteAllText(path, "{corrupt");
                    ChatClick(scene, "重新加载所有", true);
                    Check(File.ReadAllText(path) == "{corrupt" && !owner.RequiresRestart, "Invalid sidecar was rewritten or faulted owner");
                    Check(SceneChat(scene).Length == 1 && SceneChat(scene)[0].Panel.NameTextBox.TextBox.Text == "原生数据库", "Invalid sidecar did not fall back to native configuration");
                    File.WriteAllBytes(path, disk);
                    ChatClick(scene, "重新加载所有", true); CheckChatSnapshot(scene, saved[Config.GameSize]);
                    rows.Add("corrupt|native-fallback|no-file-write|repair-reload");
                    var noSelection = ReadChat(config, Config.GameSize);
                    noSelection.groups.Single(g => g.primary).selected = -1;
                    Check(new GameUiChatStore(config).TrySave(noSelection), "No-selection fixture failed");
                    ChatClick(scene, "重新加载所有", true); CheckChatSnapshot(scene, noSelection);
                    foreach (var group in SceneChat(scene).Select(t => t.CurrentTabControl).Distinct()) RefreshChatFrame(group);
                    CheckChatSnapshot(scene, noSelection);
                    Check(scene.SaveChatTabs() && ReadChat(config, Config.GameSize).groups.Single(g => g.primary).selected == -1, "No-selection state did not survive capture/refresh");
                    var deselectedGroup = SceneChat(scene).Select(t => t.CurrentTabControl).Distinct().Single(g => g.SelectedTab == null);
                    var page = HeaderPages(deselectedGroup)[0]; page.TabButton.InvokeMouseClick();
                    Check(deselectedGroup.SelectedTab == page, "Native header could not select restored page");
                    rows.Add("no-selection|reload|frame-refresh|save|native-header-click");
                    File.WriteAllBytes(path, disk); ChatClick(scene, "重新加载所有", true);
                    ChatClick(scene, "重置所有", true);
                    Check(SceneChat(scene).Length == 1, "Reset failed");
                    ChatClick(scene, "重新加载所有", true); CheckChatSnapshot(scene, saved[Config.GameSize]);
                    foreach (var tab in SceneChat(scene)) tab.Panel.RemoveButton.InvokeMouseClick();
                    Check(SceneChat(scene).Length == 0 && Children(scene).OfType<DXTabControl>().Count() == 0, "Remove-all retained empty chat groups");
                    Check(scene.SaveChatTabs() && ReadChat(config, Config.GameSize).groups.Length == 0, "Empty topology did not save");
                    ChatClick(scene, "重置所有", true); Check(SceneChat(scene).Length == 1, "Reset after empty save failed");
                    ChatClick(scene, "重新加载所有", true); Check(SceneChat(scene).Length == 0, "Empty saved topology recreated defaults");
                    Check(Children(scene).OfType<DXTabControl>().Count() == 0, "Empty reload leaked groups");
                    rows.Add("empty|remove-all|save|reset|reload");
                    session.Save(true);
                    Check(legacyBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(data, "Users.db"))), "Official operations changed native DB");
                }
                catch (Exception error) { Console.Error.WriteLine("Chat persistence body: " + error); throw; }
                finally { try { scene.Dispose(); } catch (Exception error) { Console.Error.WriteLine("Chat persistence disposal: " + error); throw; } }
                Check(ChatTab.Tabs.Count == initialTabs && ChatListenerCount() == initialListeners, "Persistence scene disposal leaked tabs/listeners");
                var emptyScene = ChatScene(ChatResolutions[0]);
                try
                {
                    CompleteStartup(emptyScene, owner, config);
                    Check(!owner.RequiresRestart && SceneChat(emptyScene).Length == 0 && Children(emptyScene).OfType<DXTabControl>().Count() == 0, "Empty startup recreated default groups");
                    rows.Add("empty|same-owner-startup|no-default-groups");
                }
                finally { emptyScene.Dispose(); }
                File.WriteAllBytes(path, fullDisk);
                foreach (var size in ChatResolutions)
                {
                    byte[] disk = File.ReadAllBytes(path); var next = ChatScene(size);
                    try
                    {
                        var previousTabs = SceneChat(next); CompleteStartup(next, owner, config);
                        Check(!owner.RequiresRestart && previousTabs.All(t => t.IsDisposed), "Scene reentry did not reconstruct saved pages");
                        CheckChatSnapshot(next, saved[size]);
                        Check(disk.SequenceEqual(File.ReadAllBytes(path)), "Scene reentry rewrote preferences");
                        rows.Add("same-owner-reentry|" + size.Width + "x" + size.Height);
                    }
                    finally { next.Dispose(); }
                }
            });
            foreach (var size in ChatResolutions)
            {
                WithStartupOwner(package, Path.Combine(config, "fresh-owner"), owner =>
                {
                    byte[] disk = File.ReadAllBytes(path); var scene = ChatScene(size);
                    try
                    {
                        CompleteStartup(scene, owner, config);
                        Check(!owner.RequiresRestart, "Fresh owner restore failed: " + owner.Diagnostic);
                        CheckChatSnapshot(scene, saved[size]);
                        Check(disk.SequenceEqual(File.ReadAllBytes(path)), "Fresh owner startup changed preferences");
                        rows.Add("new-owner-disk|" + size.Width + "x" + size.Height);
                    }
                    finally { scene.Dispose(); }
                });
                Check(ChatTab.Tabs.Count == initialTabs && ChatListenerCount() == initialListeners, "Fresh owner leaked chat");
            }
            string edgeConfig = Path.Combine(directory, "chat-routing-docked");
            var docked = ReadChat(config, ChatResolutions[0]); var dockedMain = docked.groups.Single(g => g.primary);
            dockedMain.floating = false; dockedMain.width = 574; dockedMain.height = 106;
            dockedMain.x = (docked.width - 574) / 2; dockedMain.y = docked.height - 138;
            Check(new GameUiChatStore(edgeConfig).TrySave(docked), "Docked fixture failed");
            WithStartupOwner(package, Path.Combine(edgeConfig, "app"), owner =>
            {
                var scene = ChatScene(ChatResolutions[0]);
                try
                {
                    CompleteStartup(scene, owner, edgeConfig); CheckChatSnapshot(scene, docked);
                    var main = SceneChat(scene).Select(t => t.CurrentTabControl).Distinct().Single(g => g.Size == new Size(574, 106));
                    var tabs = SceneChat(scene); byte[] disk = File.ReadAllBytes(Path.Combine(edgeConfig, GameUiChatStore.FileName));
                    foreach (var size in ChatResolutions.Skip(1))
                    {
                        Config.GameSize = size; scene.Size = size;
                        Check(main.Size == new Size(574, 106) && main.Location == new Point((size.Width - 574) / 2, size.Height - 138), "Unsaved resolution lost native dock state");
                        Check(tabs.SequenceEqual(SceneChat(scene)), "Unsaved resolution rebuilt tabs");
                    }
                    ChatClick(scene, "重新加载所有", true);
                    Check(SceneChat(scene).Length == 1 && !owner.RequiresRestart, "Missing matching entry did not use native defaults");
                    Check(disk.SequenceEqual(File.ReadAllBytes(Path.Combine(edgeConfig, GameUiChatStore.FileName))), "Missing entry fallback rewrote saved resolution");
                    Config.GameSize = ChatResolutions[0]; scene.Size = Config.GameSize;
                    ChatClick(scene, "重新加载所有", true); CheckChatSnapshot(scene, docked);
                    rows.Add("docked-primary|six-resolutions|missing-entry-native-fallback|original-entry-retained");
                }
                finally { scene.Dispose(); }
            });
            string emptyConfig = Path.Combine(directory, "chat-routing-empty-disk");
            Check(new GameUiChatStore(emptyConfig).TrySave(new GameUiChatStore.Snapshot { width = 1024, height = 768, groups = new GameUiChatStore.Group[0] }), "Empty disk fixture failed");
            WithStartupOwner(package, Path.Combine(emptyConfig, "app"), owner =>
            {
                var scene = ChatScene(ChatResolutions[0]);
                try
                {
                    CompleteStartup(scene, owner, emptyConfig);
                    Check(!owner.RequiresRestart && SceneChat(scene).Length == 0 && Children(scene).OfType<DXTabControl>().Count() == 0, "New owner did not restore empty disk topology");
                    rows.Add("empty|new-owner-disk|no-default-groups");
                }
                finally { scene.Dispose(); }
            });
            WithStartupOwner(package, Path.Combine(config, "fault-owner"), owner =>
            {
                var scene = ChatScene(ChatResolutions[0]);
                EventHandler failure = (sender, args) =>
                {
                    var tab = (ChatTab)sender;
                    if (!tab.IsDisposed && tab.Parent != null) throw new InvalidOperationException("injected saved topology reconstruction failure");
                };
                try
                {
                    ChatTab.GameUiTopologyChanged += failure;
                    try { CompleteStartup(scene, owner, config); }
                    finally { ChatTab.GameUiTopologyChanged -= failure; }
                    Check(owner.RequiresRestart && HasOwner(scene) && !owner.Package.IsDisposed, "Saved reconstruction failure released owner/package");
                    Check(scene.ChatTextBox.GameUiInputLayout == null && ChatListenerCount() == initialListeners, "Saved reconstruction fault retained adapter/listener");
                    Reject<InvalidOperationException>(() => owner.Dispose(), "Package release before failed saved chat scene disposal");
                    rows.Add("saved-reconstruction-fault|requires-restart|package-retained|adapters-detached");
                }
                finally { ChatTab.GameUiTopologyChanged -= failure; scene.Dispose(); }
            });
            ClearNativeFailureNotice();
            Check(ChatTab.Tabs.Count == initialTabs && ChatListenerCount() == initialListeners, "Saved reconstruction failure cleanup leaked");
            session.Save(true); Check(legacyBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(data, "Users.db"))), "Disposal/reentry changed native DB");
            File.Copy(Path.Combine(data, "Users.db"), Path.Combine(directory, "chat-persistence-legacy.db"));
            rows.Add("native-users-db|legacy-seed|official-save-reset-reload-reentry-disposal|bytes-unchanged");
            File.WriteAllLines(Path.Combine(directory, "chat-persistence-results.txt"), rows);
        }
        finally { CEnvir.Session = oldSession; Config.GameSize = ChatResolutions[0]; }
    }
}
