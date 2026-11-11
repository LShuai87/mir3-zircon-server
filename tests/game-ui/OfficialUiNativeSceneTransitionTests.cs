using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Client.Controls;
using Client.Envir;
using Client.Models;
using Client.Scenes;
using Client.Scenes.Views;
using Library;
using Library.Network;
using Library.SystemModels;
using G = Library.Network.GeneralPackets;
using S = Library.Network.ServerPackets;

// Runs the actual socket receive/packet dispatch and native scene/modal callbacks.
// All packets, account data and world data are synthetic and loopback-only.
internal static partial class OfficialUiFullSceneTests
{
    private static readonly List<string> transitions = new List<string>();
    private static int socketPackets, nativeEntries;
    private static List<DXControl> NativeChildren(DXControl control) => (List<DXControl>)typeof(DXControl).GetProperty("Controls", Instance).GetValue(control);
    private static int TopologyListeners() => ((Delegate)typeof(ChatTab).GetField("GameUiTopologyChanged", Static).GetValue(null))?.GetInvocationList().Length ?? 0;

    private sealed class Loopback : IDisposable
    {
        private readonly TcpListener listener;
        private readonly TcpClient client, peer;
        internal readonly CConnection Connection;
        internal Loopback()
        {
            listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            client = new TcpClient(); client.Connect((IPEndPoint)listener.LocalEndpoint);
            peer = listener.AcceptTcpClient(); peer.NoDelay = true;
            Connection = new CConnection(client); CEnvir.Connection = Connection;
        }
        internal void Send(Packet packet)
        {
            byte[] bytes = packet.GetPacketBytes();
            peer.GetStream().Write(bytes, 0, bytes.Length);
            var timer = Stopwatch.StartNew();
            while (Connection.ReceiveList != null && Connection.ReceiveList.IsEmpty && timer.ElapsedMilliseconds < 5000) Thread.Sleep(1);
            Check(Connection.ReceiveList != null && !Connection.ReceiveList.IsEmpty, "Loopback packet did not reach native receive queue: " + packet.GetType().Name);
            Check(Connection.ReceiveList.TryPeek(out var decoded) && decoded.GetType() == packet.GetType() && !ReferenceEquals(decoded, packet), "Native receive did not deserialize expected packet");
            Connection.Process(); socketPackets++;
            Check(Connection.ReceiveList.IsEmpty, "Native dispatch left an unexpected packet");
        }
        internal void ClosePeerAndProcess()
        {
            peer.Close();
            var timer = Stopwatch.StartNew();
            while (!Connection.Disconnecting && timer.ElapsedMilliseconds < 5000) Thread.Sleep(1);
            Check(Connection.Disconnecting, "Native receive did not observe socket EOF");
            Connection.TimeOutTime = DateTime.MinValue;
            Connection.Process();
            Check(!Connection.Connected && CEnvir.Connection == null, "Socket EOF did not reach native disconnect path");
        }
        public void Dispose()
        {
            if (ReferenceEquals(CEnvir.Connection, Connection)) CEnvir.Connection = null;
            Connection.Disconnect(); peer.Dispose(); client.Dispose(); listener.Stop();
        }
    }
    private static List<SelectInfo> Characters() => new List<SelectInfo> {
        new SelectInfo { CharacterIndex = 1, CharacterName = "离线协议角色", Level = 35, Class = MirClass.Warrior, Gender = MirGender.Male, LastLogin = DateTime.UtcNow }
    };
    private static StartInformation NativeStart(MapInfo map) => new StartInformation {
        Index = 1, ObjectID = 1, Name = "离线协议角色", GuildName = "", GuildRank = "", NameColour = Color.White,
        Class = MirClass.Warrior, Gender = MirGender.Male, Location = new Point(64,64), Direction = MirDirection.Down,
        MapIndex = map.Index, Level = 35, CurrentHP = 650, CurrentMP = 240, Gold = 123456, Experience = 12345,
        DayTime = 1, HairColour = Color.Brown, ArmourColour = Color.White, InSafeZone = true, StorageSize = 50,
        Items = new List<ClientUserItem>(), BeltLinks = new List<ClientBeltLink>(), AutoPotionLinks = new List<ClientAutoPotionLink>(),
        Magics = new List<ClientUserMagic>(), Buffs = new List<ClientBuffInfo>(), Quests = new List<ClientUserQuest>(),
        CompanionUnlocks = new List<int>(), Companions = new List<ClientUserCompanion>()
    };
    private static void LoginThroughPacket(Loopback wire, GameUiStartupSession owner, int listeners)
    {
        var login = DXControl.ActiveScene as LoginScene;
        Check(login != null && !login.IsDisposed, "Native login scene missing");
        Check(Field<GameScene>(owner, "attachedScene") == null && TopologyListeners() == listeners, "Login retained old scene/listener");
        wire.Send(new S.Login { Result = LoginResult.Success, Characters = Characters(), Items = new List<ClientUserItem>(), BlockList = new List<ClientBlockInfo>(), Address = "" });
        Check(login.IsDisposed && DXControl.ActiveScene is SelectScene && GameScene.Game == null, "Login success did not dispose login / enter select scene");
        transitions.Add("serialized-login-success|native LoginScene -> SelectScene");
    }
    private static GameScene EnterThroughPacket(Loopback wire, GameUiStartupSession owner, MapInfo map, int listeners, bool official)
    {
        var select = (SelectScene)DXControl.ActiveScene;
        wire.Send(new S.StartGame { Result = StartGameResult.Success, StartInformation = NativeStart(map) });
        var scene = DXControl.ActiveScene as GameScene;
        Check(select.IsDisposed && scene != null && ReferenceEquals(scene, GameScene.Game) && scene.User != null, "StartGame success failed normal scene/user construction");
        Check(scene.MapControl.MapInfo == map && scene.MapControl.Cells != null, "Native StartGame did not load map");
        Check(scene.HasOfficialWindowOwnerForTest() == official, "Native entry installed incorrect style");
        Check(TopologyListeners() == listeners + (official ? 1 : 0), "Native entry duplicated chat topology subscription");
        Check(Field<GameScene>(owner, "attachedScene") == (official ? scene : null), "Startup session has wrong borrower");
        scene.User.Stats[Stat.Health] = 1000; scene.User.Stats[Stat.Mana] = 400; scene.UserChanged();
        Check(string.IsNullOrEmpty(scene.ChatTextBox.TextBox.TextBox.Text), "Native reentry retained the prior scene editor draft");
        scene.ChatTextBox.TextBox.TextBox.Text = "此草稿只属于当前场景";
        scene.ChatTextBox.TextBox.SetFocus();
        scene.CharacterBox.Visible = scene.InventoryBox.Visible = true;
        Render(scene, null); nativeEntries++; scenes++;
        Check(!owner.RequiresRestart && !owner.IsDisposed, "Native entry faulted application session");
        transitions.Add((official ? "official" : "legacy") + "-serialized-start-game|normal constructor, native UserObject, map, frame, one listener");
        return scene;
    }
    // Test-only extension avoids exposing production lifecycle internals.
    private static bool HasOfficialWindowOwnerForTest(this GameScene scene) => (bool)typeof(GameScene).GetProperty("HasOfficialWindowOwner", Instance).GetValue(scene);

    private static System.Windows.Forms.FormClosingEventArgs RequestWindowClose(System.Windows.Forms.CloseReason reason = System.Windows.Forms.CloseReason.UserClosing)
    {
        var args = new System.Windows.Forms.FormClosingEventArgs(reason, false);
        Call(typeof(Client.TargetForm), CEnvir.Target, "OnFormClosing", args);
        return args;
    }

    private static void ExerciseSelectCloseConfirmation()
    {
        var select = DXControl.ActiveScene;
        Check(RequestWindowClose().Cancel, "Select scene window close was not cancelled for confirmation");
        var box = NativeChildren(select).OfType<DXMessageBox>().Single(b => b.IsVisible);
        Check(box.Modal && box.YesButton.Label.Text == "退出游戏" && box.NoButton.Label.Text == "取消", "Select close lost explicit confirmation actions");
        Check(RequestWindowClose().Cancel && NativeChildren(select).OfType<DXMessageBox>().Count(b => b.IsVisible) == 1, "Repeated select close duplicated modal");
        box.CloseButton.InvokeMouseClick();
        Check(!box.Visible && !select.IsDisposed, "Select close cancellation disposed scene");
        Check(RequestWindowClose().Cancel && box.IsVisible, "Window close did not reopen confirmation after modal X cancellation");
        box.NoButton.InvokeMouseClick();
        Check(box.IsDisposed && !select.IsDisposed, "Select No cancellation failed");
    }

    private static void ExerciseExitConfirmation(GameScene scene, Loopback wire, string evidence, bool official)
    {
        var dialog = scene.ExitBox;
        var editor = scene.ChatTextBox.TextBox;
        var combat = scene.User.CombatTime;
        int queued = wire.Connection.SendList.Count;
        int closes = 0;
        bool confirmed = false;
        System.Windows.Forms.FormClosingEventHandler stopExit = (sender, args) =>
        {
            closes++;
            confirmed = Field<bool>(CEnvir.Target, "closeConfirmed");
            args.Cancel = true; // Observe the real Close() callback without ending the harness.
        };
        CEnvir.Target.FormClosing += stopExit;
        try
        {
            editor.SetFocus();
            DXTextBox expectedFocus = editor;
            scene.MapControl.MapButtons = System.Windows.Forms.MouseButtons.Left;
            if (official)
            {
                var button = scene.MainPanel.GameUiExitButton;
                Check(button != null && button.Parent == scene.MainPanel.GameUiHudRoots["commands"] && button.IsVisible, "Official HUD exit entry is missing");
                Check(button.Library == scene.MainPanel.GameUiHudRoots["commands"].Library && button.ImageStates.Normal == GameUiOfficialV1Catalog.Buttons["hud-exit-icon"].Normal, "Official exit icon does not use verified package artwork");
                Check(new Rectangle(Point.Empty, button.Parent.Size).Contains(new Rectangle(button.Location, button.Size)), "Exit entry is outside HUD root");
                foreach (var region in GameUiHudLayout.OfficialV1.Regions.Values.Where(r => r.Owner == "commands"))
                    Check(!region.Bounds.IntersectsWith(new Rectangle(button.Location, button.Size)), "Exit entry overlaps an existing command");
                var point = new Point(button.DisplayArea.X + button.Size.Width / 2, button.DisplayArea.Y + button.Size.Height / 2);
                DXControl.FocusControl = null;
                scene.OnMouseMove(new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.None, 0, point.X, point.Y, 0));
                Check(DXControl.MouseControl == button, "Exit icon did not receive pointer through native scene routing");
                var click = new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 1, point.X, point.Y, 0);
                scene.ClickTime = DateTime.MinValue;
                // WinForms raises MouseClick before MouseUp. MouseDown may release native chat focus.
                scene.OnMouseDown(click); expectedFocus = DXTextBox.ActiveTextBox;
                scene.OnMouseClick(click); scene.OnMouseUp(click);
            }
            else dialog.ShowConfirmation();
            Check(dialog.IsVisible && dialog.Modal && dialog.Parent == scene && DXTextBox.ActiveTextBox == null, "Exit confirmation did not open modal / suspend chat focus");
            Check(dialog.Location == new Point((scene.Size.Width - dialog.Size.Width) / 2, (scene.Size.Height - dialog.Size.Height) / 2), "Exit confirmation is not centred");
            Check(scene.MapControl.MapButtons == System.Windows.Forms.MouseButtons.None && wire.Connection.SendList.Count == queued, "Opening exit confirmation acted on gameplay / network");
            scene.OnMouseMove(new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.None, 0, 10, 10, 0));
            Check(DXControl.MouseControl == dialog, "Exit modal leaked background pointer to gameplay controls");
            var key = new System.Windows.Forms.KeyEventArgs(System.Windows.Forms.Keys.W);
            dialog.OnKeyDown(key); Check(key.Handled, "Exit modal leaked keyboard shortcuts");
            dialog.ShowConfirmation();
            dialog.CloseButton.InvokeMouseClick();
            Check(!dialog.Visible && DXTextBox.ActiveTextBox == expectedFocus && wire.Connection.SendList.Count == queued, "Exit X cancellation lost editor focus / sent logout");

            // Remove our observer while synthesizing close events: its Cancel would bypass the override.
            CEnvir.Target.FormClosing -= stopExit;
            editor.SetFocus();
            CEnvir.Target.Close();
            Check(!CEnvir.Target.IsDisposed && dialog.IsVisible, "Window X / Alt+F4 did not cancel real Close() and open exit confirmation");
            Check(RequestWindowClose().Cancel && NativeChildren(scene).OfType<ExitDialog>().Count() == 1, "Repeated window close duplicated exit dialog");
            dialog.OnKeyDown(new System.Windows.Forms.KeyEventArgs(System.Windows.Forms.Keys.Escape));
            Check(!dialog.Visible && DXTextBox.ActiveTextBox == editor, "Escape did not cancel exit / restore editor");
            DXControl.FocusControl = scene.MapControl; DXControl.MouseControl = scene.MapControl;
            scene.MapControl.MapButtons = System.Windows.Forms.MouseButtons.Left;
            CEnvir.Target.Close();
            Check(dialog.IsVisible && DXControl.FocusControl == dialog && DXControl.MouseControl == dialog && scene.MapControl.MapButtons == System.Windows.Forms.MouseButtons.None,
                "Window close during held map input did not transfer capture to exit modal");
            scene.OnMouseMove(new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.None, 0, 10, 10, 0));
            Check(DXControl.MouseControl == dialog, "Held map input bypassed modal pointer routing");
            dialog.CloseButton.InvokeMouseClick(); editor.SetFocus();
            Check(!RequestWindowClose(System.Windows.Forms.CloseReason.WindowsShutDown).Cancel && !dialog.Visible, "OS shutdown was trapped by game confirmation");
            CEnvir.Target.FormClosing += stopExit;

            scene.User.CombatTime = CEnvir.Now;
            dialog.ShowConfirmation();
            dialog.ToSelectButton.InvokeMouseClick(); dialog.ExitButton.InvokeMouseClick();
            Check(dialog.Visible && closes == 0 && wire.Connection.SendList.Count == queued, "Recent combat allowed exit or logout");
            if (official && !noScreenshots) Render(scene, Path.Combine(evidence, "official-exit-confirmation.png"));

            scene.User.CombatTime = CEnvir.Now.AddSeconds(-10);
            dialog.ExitButton.InvokeMouseClick();
            Check(closes == 1 && confirmed && !Field<bool>(CEnvir.Target, "closeConfirmed") && !CEnvir.Target.IsDisposed, "Confirmed exit did not use one-shot bypass / clear it after cancelled close");
            Check(wire.Connection.SendList.Count == queued, "Full exit incorrectly sent character logout packet");

            dialog.ToSelectButton.InvokeMouseClick();
            Check(!dialog.Visible && wire.Connection.SendList.Count == queued + 1 && wire.Connection.SendList.Last() is Library.Network.ClientPackets.Logout,
                "Return to character selection did not enqueue native Logout exactly once");
            transitions.Add((official ? "official" : "legacy") + "-exit-confirmation|HUD/window entry, modal focus, cancel/Escape, combat block, exact 10s boundary, one-shot confirmed close, native Logout");
        }
        finally
        {
            CEnvir.Target.FormClosing -= stopExit;
            scene.User.CombatTime = combat;
            dialog.Visible = false;
        }
    }

    private static void ExerciseNativeModalFocus(GameScene scene)
    {
        var editor = scene.ChatTextBox.TextBox;
        var native = editor.TextBox;
        native.Text = "弹窗焦点草稿"; native.Select(1, 3); editor.SetFocus(); native.Select(1,3);
        var hwnd = native.Handle;
        var box = DXMessageBox.Show("离线焦点回归", "原有弹窗");
        Check(ReferenceEquals(box.HiddenBox, editor) && DXTextBox.ActiveTextBox == null, "Modal did not capture native editor focus");
        box.OKButton.InvokeMouseClick();
        Check(box.IsDisposed && box.HiddenBox == null && DXTextBox.ActiveTextBox == editor && native.Handle == hwnd && native.Text == "弹窗焦点草稿" && native.SelectionStart == 1 && native.SelectionLength == 3,
            "Normal modal OK did not restore original editor/HWND/draft/selection");
        box = DXMessageBox.Show("隐藏后释放", "原有弹窗");
        box.Visible = false;
        Check(box.HiddenBox == null && DXTextBox.ActiveTextBox == editor, "Hiding modal retained stale focus owner");
        using (var alternate = new DXTextBox { Parent = scene, Size = new Size(100,20), Location = new Point(10,10) })
        {
            alternate.SetFocus(); box.Dispose();
            Check(DXTextBox.ActiveTextBox == alternate, "Disposing already hidden modal stole newer focus");
        }
        editor.SetFocus();
        box = DXMessageBox.Show("关闭按钮", "原有弹窗");
        box.CloseButton.InvokeMouseClick();
        Check(!box.Visible && box.HiddenBox == null && DXTextBox.ActiveTextBox == editor, "Normal close button lost focus restoration");
        box.Dispose();
        var branch = new DXControl { Parent = scene, Size = new Size(200,100) };
        var previous = new DXTextBox { Parent = branch, Size = new Size(100,20) };
        previous.SetFocus();
        box = DXMessageBox.Show("编辑框先释放", "原有弹窗");
        Check(box.HiddenBox == previous, "Disposed-editor fixture did not capture focus");
        previous.Dispose(); box.Dispose();
        Check(DXTextBox.ActiveTextBox == null && box.HiddenBox == null, "Modal restored disposed editor");
        previous = new DXTextBox { Parent = branch, Size = new Size(100,20) }; previous.SetFocus();
        box = DXMessageBox.Show("父控件先开始释放", "原有弹窗"); box.Parent = branch;
        NativeChildren(branch).Remove(box); NativeChildren(branch).Insert(0,box);
        bool liveEditorDuringModalDispose = false;
        box.Disposing += (sender, args) => liveEditorDuringModalDispose = branch.IsDisposed && !previous.IsDisposed;
        branch.Dispose();
        Check(liveEditorDuringModalDispose && box.IsDisposed && previous.IsDisposed && DXTextBox.ActiveTextBox == null, "Modal restored editor while ancestor was disposing");
        transitions.Add("native-modal-focus|OK/close/hide restore live editor; dispose does not steal newer focus or restore disposed editor/ancestor");
        editor.SetFocus();
    }
    private sealed class RetiredScene
    {
        private readonly GameScene scene;
        private readonly DXControl[] controls;
        private readonly System.Windows.Forms.TextBox editor;
        private readonly ChatTab[] tabs;
        private readonly GameUiOfficialV1WindowSet windows;
        private readonly GameUiOfficialV1ChatSet chat;
        internal RetiredScene(GameScene scene)
        {
            this.scene = scene; controls = NativeChildren(scene).ToArray(); tabs = ChatTab.Tabs.ToArray();
            editor = scene.ChatTextBox.TextBox.TextBox;
            windows = Field<GameUiOfficialV1WindowSet>(scene, "officialWindowSet");
            chat = windows == null ? null : Field<GameUiOfficialV1ChatSet>(windows, "chat");
            Check(!editor.IsDisposed && tabs.Length > 0, "Lifecycle fixture lacks native editor/tabs");
        }
        internal void AssertReleased(GameUiStartupSession owner, int listeners, string context)
        {
            Check(scene.IsDisposed && controls.All(c => c.IsDisposed) && tabs.All(t => t.IsDisposed), context + ": native controls retained");
            Check(editor.IsDisposed && DXTextBox.ActiveTextBox?.IsDisposed != true, context + ": old editor/focus retained");
            Check(GameScene.Game == null && ChatTab.Tabs.Count == 0 && !DXWindow.Windows.Any(w => controls.Contains(w)), context + ": static native window/chat references retained");
            Check(!scene.HasOfficialWindowOwnerForTest() && Field<GameScene>(owner, "attachedScene") == null, context + ": borrower retained");
            if (windows != null) Check(Field<bool>(windows, "disposed") && Field<bool>(chat, "disposed") && chat.BoundGroupCount == 0, context + ": Official adapters retained");
            Check(TopologyListeners() == listeners && !owner.RequiresRestart && !owner.IsDisposed, context + ": listener/owner lifecycle mismatch");
            if (owner.EffectiveStyle == GameUiStyle.OfficialV1) Check(owner.Package != null && !owner.Package.IsDisposed, context + ": application package released before shutdown");
            transitions.Add(context + "|old controls/editor/tabs/adapters released, static listener removed, application owner retained");
        }
    }
    private static void AssertRestoredChat(GameScene scene, string name)
    {
        var tab = ChatTab.Tabs.SingleOrDefault(t => t.TabButton.Label.Text == name);
        Check(tab != null && tab.Panel.SystemCheckBox.Checked, "Native reentry did not restore saved tab name/filter");
        Check(tab.History.All(label => !label.Text.Contains("仅旧场景")), "Reentry retained old scene history");
        int before = tab.History.Count;
        scene.ReceiveChat("单次路由回归", MessageType.System);
        Check(tab.History.Count == before + 1, "Reentry duplicated or lost native chat delivery");
        transitions.Add("saved-chat-restored|saved name/filter, fresh history/editor, single message delivery");
    }
    private static void ExerciseNativeTransitions(GameUiStartupSession owner, MapInfo map, string evidence, bool official)
    {
        int listeners = TopologyListeners();
        Config.GameSize = new Size(1024,768); Reset(Config.GameSize);
        DXControl.ActiveScene = new LoginScene(Config.IntroSceneSize);
        try
        {
            using (var wire = new Loopback())
            {
                LoginThroughPacket(wire, owner, listeners);
                ExerciseSelectCloseConfirmation();
                var scene = EnterThroughPacket(wire, owner, map, listeners, official);
                ExerciseNativeModalFocus(scene);
                ExerciseExitConfirmation(scene, wire, evidence, official);
                string name = "生命周期持久化标签";
                if (official)
                {
                    var tab = ChatTab.Tabs.First(); tab.Panel.Text = name;
                    tab.Panel.SystemCheckBox.Checked = true;
                    scene.ReceiveChat("仅旧场景历史", MessageType.System);
                    scene.ChatTextBox.TextBox.TextBox.Text = "仅旧场景草稿";
                    Check((bool)Call(typeof(GameScene), scene, "SaveChatTabs"), "Native chat save failed before logout");
                }
                var old = new RetiredScene(scene);
                wire.Send(new S.GameLogout { Characters = Characters() });
                Check(DXControl.ActiveScene is SelectScene && ReferenceEquals(CEnvir.Connection, wire.Connection), "GameLogout did not retain connection / enter select");
                old.AssertReleased(owner, listeners, "game-logout");
                scene = EnterThroughPacket(wire, owner, map, listeners, official);
                if (official) AssertRestoredChat(scene, name);
                old = new RetiredScene(scene);
                CEnvir.ReturnToLogin(); old.AssertReleased(owner, listeners, "direct-return-login");
                var login = DXControl.ActiveScene;
                CEnvir.ReturnToLogin();
                Check(ReferenceEquals(login, DXControl.ActiveScene), "Repeated ReturnToLogin replaced live login scene");
                LoginThroughPacket(wire, owner, listeners);
                wire.Send(new S.SelectLogout());
                Check(DXControl.ActiveScene is LoginScene back && back.LoginBox.Visible, "SelectLogout did not expose native login dialog");
                LoginThroughPacket(wire, owner, listeners);
                scene = EnterThroughPacket(wire, owner, map, listeners, official);
                if (official) AssertRestoredChat(scene, name);
                old = new RetiredScene(scene);
                wire.ClosePeerAndProcess();
                Check(ReferenceEquals(DXControl.ActiveScene, scene) && !scene.IsDisposed, "Socket EOF disposed scene before native acknowledgement");
                var box = NativeChildren(scene).OfType<DXMessageBox>().Single();
                Check(box.WindowSkin == null && box.Modal && box.OKButton != null, "Disconnect message lost native popup behavior/style");
                box.OKButton.InvokeMouseClick();
                old.AssertReleased(owner, listeners, "socket-eof-confirm-ok");
                Check(box.IsDisposed && DXControl.ActiveScene is LoginScene, "Native disconnect acknowledgement did not reach login");
            }
            foreach (var reason in Enum.GetValues(typeof(DisconnectReason)).Cast<DisconnectReason>().Where(reason => reason != DisconnectReason.WrongVersion))
            {
                using (var wire = new Loopback())
                {
                    LoginThroughPacket(wire, owner, listeners);
                    var scene = EnterThroughPacket(wire, owner, map, listeners, official);
                    if (official) AssertRestoredChat(scene, "生命周期持久化标签");
                    var old = new RetiredScene(scene);
                    wire.Send(new G.Disconnect { Reason = reason });
                    Check(CEnvir.Connection == null && wire.Connection.Disconnecting && !scene.IsDisposed, "Server disconnect did not wait for modal acknowledgement");
                    var box = NativeChildren(scene).OfType<DXMessageBox>().Single();
                    if (reason == DisconnectReason.ServerClosing) box.CloseButton.InvokeMouseClick(); else box.OKButton.InvokeMouseClick();
                    old.AssertReleased(owner, listeners, "server-disconnect-" + reason);
                    Check(DXControl.ActiveScene is LoginScene && box.IsDisposed, "Server disconnect did not return to fresh login scene");
                }
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Native transition body failure: " + error);
            File.WriteAllLines(Path.Combine(evidence, "native-transition-failed-progress.txt"), transitions);
            throw;
        }
        finally
        {
            CEnvir.Connection = null;
            DXControl.ActiveScene?.Dispose(); DXControl.ActiveScene = null;
        }
        Check(GameScene.Game == null && ChatTab.Tabs.Count == 0 && TopologyListeners() == listeners && Field<GameScene>(owner, "attachedScene") == null, "Native transition suite cleanup leaked resources");
        File.WriteAllLines(Path.Combine(evidence, "native-transition-results.txt"), transitions);
        Console.WriteLine("Native scene transitions: " + nativeEntries + " normal game entries; " + socketPackets + " serialized loopback packets; " + transitions.Count + " evidence rows. Native login/start/logout, EOF disconnect and eight in-game server reasons; synthetic account/world only.");
    }
}
