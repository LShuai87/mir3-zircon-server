using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;
using Client;
using Client.Controls;
using Client.Envir;
using Client.Scenes;
using Client.Scenes.Views;
using Library;
using Library.Network;
using Library.SystemModels;
using C = Library.Network.ClientPackets;
using S = Library.Network.ServerPackets;

internal static class OfficialUiNpcEntry
{
    [STAThread] private static int Main(string[] args) => OfficialUiFullSceneTests.RunNpc(args);
}

// Actual GameScene, TargetForm mouse routing, NPC parser and packet handlers.
// No sockets are created; the outgoing queue and server replies are isolated fixtures.
internal static partial class OfficialUiFullSceneTests
{
    private static readonly List<string> npcAssertions = new List<string>();
    private static readonly List<string> npcHits = new List<string> { "style|target|x|y" };
    private static string npcStyle;
    private static void NpcCheck(bool condition, string message)
    {
        Check(condition, npcStyle + ": " + message);
        npcAssertions.Add(npcStyle + "|" + message);
    }
    private static void NpcMove(Point point)
    {
        Call(typeof(TargetForm), CEnvir.Target, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, point.X, point.Y, 0));
    }
    private static void NpcClick(GameScene scene, DXControl control, DateTime clock)
    {
        NpcCheck(control.IsVisible && control.IsEnabled, "target visible and enabled: " + control.GetType().Name);
        var area = control.DisplayArea;
        var point = new Point(area.Left + area.Width / 2, area.Top + area.Height / 2);
        NpcCheck(scene.DisplayArea.Contains(point), "target within viewport");
        NpcMove(point);
        NpcCheck(DXControl.MouseControl == control, "real coordinate hits exact NPC target: " + control.GetType().Name);
        npcHits.Add(npcStyle + "|" + control.GetType().Name + "|" + point.X + "|" + point.Y);
        scene.ClickTime = DateTime.MinValue; CEnvir.Now = clock;
        var mouse = new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0);
        Call(typeof(TargetForm), CEnvir.Target, "OnMouseDown", mouse);
        Call(typeof(TargetForm), CEnvir.Target, "OnMouseClick", mouse);
        Call(typeof(TargetForm), CEnvir.Target, "OnMouseUp", mouse);
        NpcCheck(!(bool)typeof(CEnvir).GetProperty("GameUiOperationsBlocked", Static).GetValue(null), "mouse routing did not fault style session");
    }
    private static T NpcPacket<T>(CConnection connection, List<string> trace) where T : Packet
    {
        Packet packet;
        NpcCheck(connection.SendList.TryDequeue(out packet) && packet is T, "exact packet type " + typeof(T).Name);
        NpcCheck(connection.SendList.IsEmpty, "one packet only " + typeof(T).Name);
        var button = packet as C.NPCButton;
        trace.Add("packet|" + typeof(T).Name + (button == null ? "" : "|" + button.ButtonID));
        return (T)packet;
    }
    private static NPCPage NpcPage(string text, NPCDialogType type = NPCDialogType.None)
    {
        var page = Globals.NPCPageList.CreateNewObject(); page.Say = text; page.DialogType = type; return page;
    }
    private static void NpcReply(GameScene scene, CConnection connection, NPCPage page)
    {
        // Exercise the normal decoded-packet completion lookup, then its real handler.
        var reply = new S.NPCResponse { ObjectID = 9001, Index = page.Index };
        reply.Complete();
        NpcCheck(ReferenceEquals(reply.Page, page), "server page index resolves exact database record");
        connection.Process(reply);
        NpcCheck(scene.NPCBox.Visible && scene.NPCBox.Opened && scene.NPCID == 9001 && ReferenceEquals(scene.NPCBox.Page, page), "response opens original card and assigns page/NPC identity");
        NpcCheck(connection.SendList.IsEmpty, "server page response sends no speculative request");
    }
    private static SortedDictionary<string, DXWindow> NpcServices(GameScene scene)
    {
        var result = new SortedDictionary<string, DXWindow>(StringComparer.Ordinal);
        foreach (var field in typeof(GameScene).GetFields(Instance).Where(f => f.Name.StartsWith("NPC", StringComparison.Ordinal) && f.Name.EndsWith("Box", StringComparison.Ordinal) && typeof(DXWindow).IsAssignableFrom(f.FieldType)))
            if (field.Name != "NPCBox") result.Add(field.Name, (DXWindow)field.GetValue(scene));
        NpcCheck(result.Count == 16, "all original sixteen dependent NPC windows enumerated");
        return result;
    }
    private static string NpcStyleContract(NPCDialog card)
    {
        return string.Join("|", new object[] { card.Size, card.ClientArea, card.HasTitle, card.HasFooter, card.Movable,
            card.WindowSkin == null, card.PageText.Location, card.PageText.Size, card.PageText.Font.ToString(),
            card.PageText.ForeColour.ToArgb(), card.PageText.Outline, card.PageText.DrawFormat,
            card.CloseButton.Location, card.CloseButton.Size, card.CloseButton.LibraryFile, card.CloseButton.Index,
            card.CloseButton.ImageStates == null });
    }
    private static List<string> ExerciseNpc(GameUiStartupSession owner, MapInfo map, string evidence, string style)
    {
        npcStyle = style;
        var trace = new List<string>();
        using (var scene = new GameScene(Config.GameSize))
        {
            scenes++; Populate(scene, map);
            var connection = (CConnection)FormatterServices.GetUninitializedObject(typeof(CConnection));
            connection.Connected = true; connection.SendList = new ConcurrentQueue<Packet>(); CEnvir.Connection = connection;
            try
            {
                scene.ChatTextBox.TextBox.KeepFocus = false; DXTextBox.ActiveTextBox = null;
                bool official = owner.EffectiveStyle == GameUiStyle.OfficialV1;
                NpcCheck((scene.MainPanel.GameUiHudLayout != null) == official && (scene.CharacterBox.WindowSkin != null) == official && (scene.ChatTextBox.GameUiInputLayout != null) == official, "actual scene installed requested HUD/character/chat style");
                // Keep the initial reference focused; service cases below use original visibility rules.
                scene.StorageBox.Visible = false; scene.InventoryBox.Visible = false;
                var card = scene.NPCBox; var originalText = card.PageText; var originalClose = card.CloseButton;
                var services = NpcServices(scene);
                NpcCheck(card.WindowSkin == null && card.CloseButton.ImageStates == null, "original NPC frame and close images remain unskinned");
                NpcCheck(!card.Movable && !card.HasTitle && !card.HasFooter, "original NPC window presentation retained");
                scene.CharacterBox.Location = Point.Empty; scene.CharacterBox.Visible = true;
                var first = NpcPage("离线测试 NPC，正文继续使用原样式。\n[查看装备:17]   [询问任务:29]\n[离开:0]");
                NpcReply(scene, connection, first);
                NpcCheck(scene.CharacterBox.Location == new Point(card.Size.Width, 0), "opening NPC moves overlapping character panel using original rule");
                NpcCheck(scene.StorageBox.Location == (official ? new Point(scene.Size.Width - 804, 86) : new Point(scene.Size.Width - scene.StorageBox.Size.Width, scene.InventoryBox.Size.Height)), "opening NPC respects active warehouse layout ownership");
                scene.CharacterBox.Visible = false;
                NpcCheck(card.PageText.Text == "离线测试 NPC，正文继续使用原样式。\n查看装备   询问任务\n离开", "option markup stripped while all body content remains");
                NpcCheck(card.Buttons.Count == 3, "three original clickable option labels");
                NpcCheck(card.Buttons.All(b => b.Parent == originalText && b.Font.Underline && !b.Outline && b.ForeColour == Color.Yellow && b.Sound == SoundIndex.ButtonC), "original option parent/font/underline/color/sound retained");
                trace.Add("style|" + NpcStyleContract(card));
                NpcMove(new Point(800, 350));
                string initial = Path.Combine(evidence, style + "-card.png"); Render(scene, initial);
                if (style != "legacy") SamePixels(Path.Combine(evidence, "legacy-card.png"), initial, card.DisplayArea, "NPC body, frame, options and close");
                var clock = DateTime.UtcNow;
                NpcClick(scene, card.Buttons[0], clock);
                NpcCheck(card.Buttons[0].ForeColour == Color.Red, "option hover uses original red");
                NpcCheck(NpcPacket<C.NPCButton>(connection, trace).ButtonID == 17, "exact first option ID");
                NpcClick(scene, card.Buttons[0], clock.AddMilliseconds(999));
                NpcCheck(connection.SendList.IsEmpty, "same option throttled before one-second boundary");
                NpcClick(scene, card.Buttons[1], clock.AddMilliseconds(999));
                NpcCheck(NpcPacket<C.NPCButton>(connection, trace).ButtonID == 29, "distinct option has independent throttle");
                NpcClick(scene, card.Buttons[0], clock.AddSeconds(1));
                NpcCheck(NpcPacket<C.NPCButton>(connection, trace).ButtonID == 17, "exact one-second boundary accepts click");
                NpcMove(new Point(800, 350));
                NpcCheck(card.Buttons.All(b => b.ForeColour == Color.Yellow), "leaving options restores original yellow");
                scene.Observer = true; scene.UserChanged(); scene.Process();
                NpcCheck(!card.CloseButton.Enabled, "observer close button follows original disabled rule");
                NpcClick(scene, card.Buttons[0], clock.AddSeconds(3));
                NpcClick(scene, card.Buttons[2], clock.AddSeconds(3));
                NpcCheck(connection.SendList.IsEmpty && card.Opened && card.Visible, "observer option/leave clicks send nothing and keep card open");
                scene.Observer = false; scene.UserChanged(); scene.Process();
                NpcCheck(card.CloseButton.Enabled, "leaving observer restores close button");
                var oldLabels = card.Buttons.ToArray();
                // A single long Chinese option spans multiple actual hit rectangles.
                string longOption = string.Join(" ", Enumerable.Repeat("跨行选项文字", 22));
                var multiline = NpcPage("新的正文\n[" + longOption + ":2147483647]\n[离开:0]");
                NpcReply(scene, connection, multiline);
                NpcCheck(oldLabels.All(b => b.IsDisposed) && !card.Buttons.Intersect(oldLabels).Any(), "page refresh disposes every stale clickable label");
                NpcMove(new Point(800, 350));
                Render(scene, Path.Combine(evidence, style + "-wrapped-option.png"));
                var wrapped = card.Buttons.Where(b => b.Text != "离开").ToArray();
                NpcCheck(wrapped.Length >= 2 && string.Concat(wrapped.Select(b => b.Text)).Replace(" ", "") == longOption.Replace(" ", ""), "wrapped option covers all original non-space text without loss or duplication");
                NpcMove(new Point(wrapped[0].DisplayArea.Left + 3, wrapped[0].DisplayArea.Top + 3));
                NpcCheck(wrapped.All(b => b.ForeColour == Color.Red), "all rectangles of one wrapped option hover together");
                NpcClick(scene, wrapped[0], clock.AddSeconds(5));
                NpcCheck(NpcPacket<C.NPCButton>(connection, trace).ButtonID == int.MaxValue, "multi-line option keeps exact 32-bit ID");
                NpcClick(scene, wrapped[wrapped.Length - 1], clock.AddSeconds(5).AddMilliseconds(999));
                NpcCheck(connection.SendList.IsEmpty, "wrapped rectangles share the same throttle");
                NpcClick(scene, wrapped[wrapped.Length - 1], clock.AddSeconds(6));
                NpcCheck(NpcPacket<C.NPCButton>(connection, trace).ButtonID == int.MaxValue, "wrapped option accepts boundary on another rectangle");
                NpcClick(scene, card.Buttons.Last(), clock.AddSeconds(6));
                NpcPacket<C.NPCClose>(connection, trace);
                NpcCheck(!card.Visible && !card.Opened && scene.NPCID == 0, "leave option clears open flag and NPC identity");
                NpcCheck(services.Values.All(w => !w.Visible), "leave option hides every dependent service");

                var expected = new Dictionary<NPCDialogType, string[]> {
                    { NPCDialogType.None, new string[0] }, { NPCDialogType.BuySell, new[] { "NPCGoodsBox", "NPCSellBox" } },
                    { NPCDialogType.Repair, new[] { "NPCRepairBox" } }, { NPCDialogType.Refine, new[] { "NPCRefineBox" } },
                    { NPCDialogType.RefineRetrieve, new[] { "NPCRefineRetrieveBox" } },
                    { NPCDialogType.CompanionManage, new[] { "NPCAdoptCompanionBox", "NPCCompanionStorageBox" } },
                    { NPCDialogType.WeddingRing, new[] { "NPCWeddingRingBox" } }, { NPCDialogType.RefinementStone, new[] { "NPCRefinementStoneBox" } },
                    { NPCDialogType.MasterRefine, new[] { "NPCMasterRefineBox" } }, { NPCDialogType.WeaponReset, new string[0] },
                    { NPCDialogType.ItemFragment, new[] { "NPCItemFragmentBox" } }, { NPCDialogType.AccessoryRefineUpgrade, new[] { "NPCAccessoryUpgradeBox" } },
                    { NPCDialogType.AccessoryRefineLevel, new[] { "NPCAccessoryLevelBox" } }, { NPCDialogType.AccessoryReset, new[] { "NPCAccessoryResetBox" } },
                    { NPCDialogType.WeaponCraft, new[] { "NPCWeaponCraftBox" } }
                };
                NpcCheck(expected.Count == Enum.GetValues(typeof(NPCDialogType)).Length, "every existing dialog discriminator covered, including original WeaponReset no-op");
                foreach (NPCDialogType type in Enum.GetValues(typeof(NPCDialogType)))
                {
                    var page = NpcPage("原有服务：" + type + "\n[离开:0]", type);
                    if (type == NPCDialogType.BuySell)
                    {
                        var item = Globals.ItemInfoList.CreateNewObject(); item.ItemName = "离线商品"; item.ItemType = ItemType.Consumable; item.Price = 10; item.StackSize = 1;
                        var good = CEnvir.Session.GetCollection<NPCGood>().CreateNewObject(); good.Item = item; good.Page = page;
                        var saleType = CEnvir.Session.GetCollection<NPCType>().CreateNewObject(); saleType.ItemType = ItemType.Consumable; saleType.Page = page;
                    }
                    var placementWrites = services.Values.ToDictionary(w => w, w => 0);
                    foreach (var window in services.Values) window.Location = new Point(61, 73);
                    EventHandler<EventArgs> recordPlacement = (sender, args) =>
                    {
                        placementWrites[(DXWindow)sender]++;
                        var ready = services.Where(p => p.Value.Visible).Select(p => p.Key).OrderBy(n => n, StringComparer.Ordinal);
                        NpcCheck(ready.SequenceEqual(expected[type].OrderBy(n => n, StringComparer.Ordinal)), "placement follows all service visibility updates: " + type);
                        if (type == NPCDialogType.BuySell)
                            NpcCheck(scene.NPCGoodsBox.Cells.Count == page.Goods.Count, "placement follows goods content update");
                    };
                    foreach (var window in services.Values) window.LocationChanged += recordPlacement;
                    try { NpcReply(scene, connection, page); }
                    finally { foreach (var window in services.Values) window.LocationChanged -= recordPlacement; }
                    foreach (var pair in services)
                    {
                        NpcCheck(placementWrites[pair.Value] == (pair.Value.Visible ? 1 : 0), "one final placement per visible response service, none for hidden: " + type + "/" + pair.Key);
                        if (!pair.Value.Visible) NpcCheck(pair.Value.Location == new Point(61, 73), "hidden service position retained: " + pair.Key);
                    }
                    var visible = services.Where(p => p.Value.Visible).Select(p => p.Key).OrderBy(n => n, StringComparer.Ordinal).ToArray();
                    NpcCheck(visible.SequenceEqual(expected[type].OrderBy(n => n, StringComparer.Ordinal)), "original dependent windows for " + type);
                    foreach (var pair in services)
                    {
                        NpcCheck((pair.Value.WindowSkin != null) == (official && pair.Key == "NPCGoodsBox"), "only the agreed shop main view receives a skin; secondary services retain their native style: " + pair.Key);
                        if (pair.Value.Visible && pair.Key != "NPCGoodsBox")
                            trace.Add("service|" + type + "|" + pair.Key + "|" + pair.Value.Location + "|" +
                                pair.Value.Size + "|" + pair.Value.ClientArea + "|" + pair.Value.HasTitle + "|" +
                                pair.Value.HasFooter + "|" + pair.Value.CloseButton.Location + "|" +
                                pair.Value.CloseButton.Size + "|" + pair.Value.CloseButton.Index + "|" +
                                (pair.Value.CloseButton.ImageStates == null));
                    }
                    if (official)
                    {
                        var selectedPage = card.Page;
                        var selectedButtons = card.Buttons.ToArray();
                        var shapes = services.ToDictionary(p => p.Key, p => p.Value.Size);
                        foreach (var size in new[] { new Size(1366,768), new Size(1280,800), new Size(1440,900), new Size(1600,900), new Size(1920,1080), new Size(1024,768) })
                        {
                            foreach (var window in services.Values) window.Location = new Point(61, 73);
                            // Geometry pass; actual device reset and rendered pixel checks follow below.
                            Config.GameSize = size; scene.Size = size;
                            NpcCheck(ReferenceEquals(card.Page, selectedPage) && card.Buttons.SequenceEqual(selectedButtons), "resize does not rebuild the NPC page or handlers: " + type + "/" + size);
                            NpcCheck(connection.SendList.IsEmpty, "service reflow sends no gameplay packets: " + type + "/" + size);
                            foreach (var pair in services)
                            {
                                NpcCheck(pair.Value.Size == shapes[pair.Key], "service reflow retains native window size: " + pair.Key);
                                NpcCheck(pair.Value.Visible == expected[type].Contains(pair.Key), "service reflow cannot open or close business windows: " + pair.Key);
                                NpcCheck(pair.Value.Visible ? pair.Value.Location.Y == card.Size.Height : pair.Value.Location == new Point(61, 73), "resize routes visible services and preserves hidden coordinates: " + pair.Key);
                                if (pair.Value.Visible)
                                {
                                    bool leftColumn = new[] { "NPCGoodsBox", "NPCRepairBox", "NPCRefineRetrieveBox", "NPCCompanionStorageBox" }.Contains(pair.Key) || (pair.Key == "NPCSellBox" && !scene.NPCGoodsBox.Visible);
                                    int expectedX = leftColumn ? 0 : card.Size.Width - (pair.Key == "NPCMasterRefineBox" ? scene.NPCRefineBox.Size.Width : pair.Value.Size.Width);
                                    NpcCheck(pair.Value.Location.X == expectedX, "resize reads current card and service widths: " + pair.Key);
                                }
                            }
                        }
                    }
                    NpcClick(scene, card.CloseButton, DateTime.UtcNow);
                    NpcPacket<C.NPCClose>(connection, trace);
                    NpcCheck(services.Values.All(w => !w.Visible) && !card.Visible && !card.Opened && scene.NPCID == 0, "close button clears original dependent state: " + type);
                    card.Visible = false;
                    NpcCheck(connection.SendList.IsEmpty, "repeated close sends no duplicate: " + type);
                }
                // BuySell independently selects the original buy and sell surfaces.
                for (int mask = 0; mask < 4; mask++)
                {
                    var page = NpcPage("买卖组合 " + mask + "\n[离开:0]", NPCDialogType.BuySell);
                    if ((mask & 1) != 0)
                    {
                        var item = Globals.ItemInfoList.CreateNewObject(); item.ItemName = "离线商品"; item.ItemType = ItemType.Consumable; item.Price = 10; item.StackSize = 1;
                        var good = CEnvir.Session.GetCollection<NPCGood>().CreateNewObject(); good.Item = item; good.Page = page;
                    }
                    if ((mask & 2) != 0)
                    {
                        var saleType = CEnvir.Session.GetCollection<NPCType>().CreateNewObject(); saleType.ItemType = ItemType.Consumable; saleType.Page = page;
                    }
                    Point hiddenSellPosition = scene.NPCSellBox.Location;
                    NpcReply(scene, connection, page);
                    NpcCheck(scene.NPCGoodsBox.Visible == ((mask & 1) != 0) && scene.NPCSellBox.Visible == ((mask & 2) != 0), "independent buy/sell flags " + mask);
                    NpcCheck(scene.NPCGoodsBox.Cells.Count == ((mask & 1) != 0 ? 1 : 0), "goods replacement retains exact original collection " + mask);
                    trace.Add("buy-sell|" + mask + "|" + scene.NPCGoodsBox.Visible + "|" + scene.NPCSellBox.Visible);
                    NpcCheck(scene.NPCSellBox.Visible ? scene.NPCSellBox.Location.Y == scene.NPCBox.Size.Height : scene.NPCSellBox.Location == hiddenSellPosition, "visible sell remains below card; hidden sell retains its own position");
                    if (mask == 3) NpcCheck(scene.NPCSellBox.Location.X == scene.NPCBox.Size.Width - scene.NPCSellBox.Size.Width, "buy/sell uses original card-relative placement with current shell width");
                    NpcClick(scene, card.CloseButton, DateTime.UtcNow); NpcPacket<C.NPCClose>(connection, trace);
                }
                NpcReply(scene, connection, NpcPage("修理\n[离开:0]", NPCDialogType.Repair));
                NpcReply(scene, connection, NpcPage("精炼\n[离开:0]", NPCDialogType.Refine));
                NpcCheck(!scene.NPCRepairBox.Visible && scene.NPCRefineBox.Visible, "in-place service switch hides previous repair window");
                NpcReply(scene, connection, NpcPage("普通正文\n[离开:0]"));
                NpcCheck(services.Values.All(w => !w.Visible), "in-place switch to plain page hides original services");
                // Quest cards are opened by their own packet flow, outside NPCDialogType.
                scene.NPCQuestBox.Visible = true;
                NpcClick(scene, card.CloseButton, DateTime.UtcNow); NpcPacket<C.NPCClose>(connection, trace);
                NpcCheck(!scene.NPCQuestBox.Visible, "NPC close also hides independently opened quest card");
                // A response while already open must refresh in place, without a close packet.
                NpcReply(scene, connection, first);
                var replacement = NpcPage("更新正文\n[新的选项:61]\n[离开:0]");
                oldLabels = card.Buttons.ToArray();
                NpcReply(scene, connection, replacement);
                NpcCheck(oldLabels.All(b => b.IsDisposed) && card.Buttons.Count == 2, "visible page replacement releases stale hit regions");
                NpcClick(scene, card.Buttons[0], DateTime.UtcNow);
                NpcCheck(NpcPacket<C.NPCButton>(connection, trace).ButtonID == 61, "updated page sends updated option ID only");
                connection.Process(new S.NPCClose());
                NpcPacket<C.NPCClose>(connection, trace);
                NpcCheck(!card.Visible && !card.Opened && scene.NPCID == 0, "server close preserves original close acknowledgement");
                connection.Process(new S.NPCClose());
                NpcCheck(connection.SendList.IsEmpty, "duplicate server close is idempotent");
                NpcReply(scene, connection, first);
                NpcMove(new Point(800, 350));
                foreach (var size in new[] { new Size(1024,768), new Size(1366,768), new Size(1280,800), new Size(1440,900), new Size(1600,900), new Size(1920,1080), new Size(1024,768) })
                {
                    Reset(size); scene.Size = size;
                    string image = Path.Combine(evidence, style + "-reset-" + size.Width + "x" + size.Height + ".png"); Render(scene, image);
                    NpcCheck(ReferenceEquals(card, scene.NPCBox) && card.PageText == originalText && card.CloseButton == originalClose, "resize/reset retains original NPC control identities");
                    NpcCheck(card.WindowSkin == null && card.Opened && card.Page == first && scene.NPCID == 9001, "resize/reset retains original page/state without skin");
                    SamePixels(initial, image, card.DisplayArea, "NPC original card after reset " + size);
                    NpcCheck(connection.SendList.IsEmpty, "reset sends no NPC packets");
                }
                NpcClick(scene, card.Buttons[1], DateTime.UtcNow.AddSeconds(20));
                NpcCheck(NpcPacket<C.NPCButton>(connection, trace).ButtonID == 29, "original option hit remains valid after all resets");
                NpcClick(scene, card.CloseButton, DateTime.UtcNow.AddSeconds(20)); NpcPacket<C.NPCClose>(connection, trace);
                ExerciseNpcGoods(scene, connection, official, evidence, style, trace);
                ExerciseNpcItemServices(scene, connection, official, evidence, style, trace);
                ExerciseNpcRefineServices(scene, connection, official, evidence, style, trace);
                ExerciseNpcQuestServices(scene, connection, official, evidence, style, trace);
                File.WriteAllLines(Path.Combine(evidence, style + "-trace.txt"), trace);
                NpcCheck(!owner.RequiresRestart && !owner.IsDisposed, "NPC lifetime does not fault/dispose UI owner");
            }
            finally { CEnvir.Connection = null; DXTextBox.ActiveTextBox = null; }
        }
        DXControl.ActiveScene = null;
        NpcCheck(GameScene.Game == null && ChatTab.Tabs.Count == 0, "scene exit releases original dialogs and chat tabs");
        return trace;
    }
    internal static int RunNpc(string[] args)
    {
        try
        {
            string package = args[0], data = args[1], evidence = args[2]; Directory.CreateDirectory(evidence);
            Config.FullScreen = false; Config.GameSize = new Size(1024,768); Config.LogChat = false;
            Config.SystemVolume = Config.MusicVolume = Config.PlayerVolume = Config.MonsterVolume = Config.MagicVolume = 0;
            CEnvir.Now = DateTime.UtcNow; Directory.CreateDirectory("Data"); InitializeDatabase();
            var map = BlankMap(Path.GetFullPath("Map")); LoadLibraries(data, evidence);
            string installed = Path.Combine(Environment.CurrentDirectory, GameUiOfficialV1Catalog.PackageRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(installed)); File.Copy(package, installed);
            using (var target = new TargetForm { ClientSize = Config.GameSize })
            {
                CEnvir.Target = target; DXManager.Create();
                try
                {
                    List<string> baseline;
                    using (var legacy = GameUiStartupSession.Prepare("Legacy", Environment.CurrentDirectory))
                    { Publish(legacy); baseline = ExerciseNpc(legacy, map, evidence, "legacy"); Publish(null); }
                    using (var official = (GameUiStartupSession)typeof(GameUiStartupSession).GetMethod("Prepare", Static, null, new[] { typeof(string), typeof(string), typeof(bool) }, null).Invoke(null, new object[] { "OfficialV1", Environment.CurrentDirectory, true }))
                    {
                        NpcCheck(official.EffectiveStyle == GameUiStyle.OfficialV1, "candidate accepted through test-only gate"); Publish(official);
                        NpcCheck(baseline.SequenceEqual(ExerciseNpc(official, map, evidence, "official")), "Legacy/Official ordered packets/service geometry/style contract identical");
                        NpcCheck(baseline.SequenceEqual(ExerciseNpc(official, map, evidence, "official-reentry")), "Legacy/reentry ordered packets/service geometry/style contract identical"); Publish(null);
                    }
                }
                finally { CEnvir.Connection = null; Publish(null); DXManager.Unload(); }
            }
            NpcCheck(!GameUiOfficialV1Catalog.RuntimeReady, "production readiness stays false");
            File.WriteAllLines(Path.Combine(evidence, "assertions.txt"), npcAssertions);
            File.WriteAllLines(Path.Combine(evidence, "coordinate-hits.txt"), npcHits);
            File.WriteAllText(Path.Combine(evidence, "metrics.json"), "{\"checks\":" + checks + ",\"scenes\":" + scenes + ",\"presentedFrames\":" + renders + ",\"deviceResets\":" + resets + ",\"coordinateHits\":" + (npcHits.Count-1) + ",\"comparedPixels\":" + comparedPixels + ",\"dialogTypes\":15,\"dependentWindows\":16,\"serverConnected\":false,\"releaseAccepted\":false}");
            Console.WriteLine("NPC original-card regression: " + checks + " checks; " + scenes + " actual scenes; " + (npcHits.Count-1) + " coordinate hits; " + renders + " presented frames; " + resets + " resets; " + comparedPixels + " identical card pixels. Legacy/Official/reentry ordered traces identical. Socket-free synthetic replies only; no live-server acceptance.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { CEnvir.Connection = null; foreach (var library in CEnvir.LibraryList.Values) library.Dispose(); CEnvir.LibraryList.Clear(); CEnvir.Target = null; }
    }
}
