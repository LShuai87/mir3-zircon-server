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
        DXControl[] disposedNpcControls = null;
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
                var loadedWarehousePosition = scene.StorageBox.Location;
                NpcReply(scene, connection, first);
                NpcCheck(scene.CharacterBox.Location == new Point(card.Size.Width, 0), "opening NPC moves overlapping character panel using original rule");
                NpcCheck(scene.StorageBox.Location == (official ? loadedWarehousePosition : new Point(scene.Size.Width - scene.StorageBox.Size.Width, scene.InventoryBox.Size.Height)), "opening NPC respects active warehouse layout ownership; loaded=" + loadedWarehousePosition + "; actual=" + scene.StorageBox.Location);
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
                            trace.Add("service|" + type + "|" + pair.Key + "|" +
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
                            CheckNpcGroupReachability(scene, "response-reset/" + type + "/" + size);
                            foreach (var pair in services)
                            {
                                NpcCheck(pair.Value.Size == shapes[pair.Key], "service reflow retains native window size: " + pair.Key);
                                NpcCheck(pair.Value.Visible == expected[type].Contains(pair.Key), "service reflow cannot open or close business windows: " + pair.Key);
                                NpcCheck(pair.Value.Visible || pair.Value.Location == new Point(61, 73), "resize preserves hidden service coordinates: " + pair.Key);

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
                    NpcCheck(scene.NPCSellBox.Visible ? (official || scene.NPCSellBox.Location.Y == scene.NPCBox.Size.Height) : scene.NPCSellBox.Location == hiddenSellPosition, "Legacy sell keeps original position; hidden sell retains its own position");
                    if (official) CheckNpcGroupReachability(scene, "buy-sell/" + mask);
                    if (mask == 3 && !official) NpcCheck(scene.NPCSellBox.Location.X == scene.NPCBox.Size.Width - scene.NPCSellBox.Size.Width, "buy/sell uses original card-relative placement with current shell width");
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
                // Compare the same isolated native card as the initial reference.
                // Auxiliary overlap is exercised separately by the group scenarios.
                if (official) { scene.StorageBox.Visible = false; scene.InventoryBox.Visible = false; }
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
                if (official)
                {
                    ExerciseNpcLongCard(scene, connection, evidence, style);
                    ExerciseNpcParagraphRegions(scene, connection, evidence, style);
                    ExerciseNpcGroups(scene, connection, evidence, style);
                }
                ExerciseNpcGoods(scene, connection, official, evidence, style, trace);
                ExerciseNpcItemServices(scene, connection, official, evidence, style, trace);
                ExerciseNpcRefineServices(scene, connection, official, evidence, style, trace);
                ExerciseNpcQuestServices(scene, connection, official, evidence, style, trace);
                ExerciseNpcLifecycle(scene, connection, evidence, style, trace);
                disposedNpcControls = PrepareNpcDisposal(scene, connection, evidence, style, trace);
                File.WriteAllLines(Path.Combine(evidence, style + "-trace.txt"), trace);
                NpcCheck(!owner.RequiresRestart && !owner.IsDisposed, "NPC lifetime does not fault/dispose UI owner");
            }
            finally { CEnvir.Connection = null; DXTextBox.ActiveTextBox = null; }
        }
        NpcCheck(disposedNpcControls != null && disposedNpcControls.All(c => c.IsDisposed && c.Parent == null && !c.IsVisible),
            "scene exit disposes all seventeen NPC windows, captured original children, and disconnect modal");
        NpcCheck(disposedNpcControls.OfType<DXItemCell>().All(c => c.Link == null && !c.Locked), "scene exit releases live service selection in both directions");
        NpcCheck(DXControl.MouseControl == null && DXControl.FocusControl == null && DXControl.ActiveScene == null && DXTextBox.ActiveTextBox == null,
            "scene exit releases static mouse, focus, scene and editor references");
        DXControl.ActiveScene = null;
        NpcCheck(GameScene.Game == null && ChatTab.Tabs.Count == 0, "scene exit releases original dialogs and chat tabs");
        return trace;
    }
    private static DXWindow[] CurrentNpcGroup(GameScene scene)
    {
        var members = new[] { scene.NPCBox, (DXWindow)scene.StorageBox, scene.InventoryBox }
            .Concat(NpcServices(scene).Values).Where(w => w.IsVisible).ToArray();
        var siblings = (List<DXControl>)typeof(DXControl).GetProperty("Controls", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(scene);
        return siblings.OfType<DXWindow>().Where(w => members.Contains(w)).ToArray();
    }
    private static void CheckNpcGroupReachability(GameScene scene, string scenario)
    {
        var area = new Rectangle(0, 0, scene.Size.Width, scene.Size.Height - 202);
        var windows = CurrentNpcGroup(scene);
        foreach (var window in windows)
        {
            NpcCheck(area.Contains(window.DisplayArea), "complete group window above HUD: " + scenario + "/" + window.GetType().Name);
            if (window.CloseButton.IsVisible)
            {
                var close = window.CloseButton.DisplayArea;
                NpcMove(new Point(close.Left + close.Width / 2, close.Top + close.Height / 2));
                NpcCheck(DXControl.MouseControl == window.CloseButton, "real group close remains reachable: " + scenario + "/" + window.GetType().Name);
            }
        }
        var services = NpcServices(scene).Values.Where(w => w.IsVisible && w != scene.NPCQuestBox).ToArray();
        for (int i = 0; i < services.Length; i++)
            for (int j = 0; j < i; j++)
                NpcCheck(!services[i].DisplayArea.IntersectsWith(services[j].DisplayArea), "paired service bodies do not overlap: " + scenario);
    }
    private static void ExerciseNpcGroups(GameScene scene, CConnection connection, string evidence, string style)
    {
        var log = new List<string>();
        var packets = new List<string>();
        var card = scene.NPCBox;
        var resolutions = new[] { new Size(1024,768), new Size(1366,768), new Size(1280,800),
            new Size(1440,900), new Size(1600,900), new Size(1920,1080), new Size(1024,768) };
        scene.CharacterBox.Visible = false;
        foreach (var type in Enum.GetValues(typeof(NPCDialogType)).Cast<NPCDialogType>())
        {
            var page = NpcPage(string.Join("\n", Enumerable.Range(0, 80).Select(n => "组合布局正文 " + n)) + "\n[离开:0]", type);
            if (type == NPCDialogType.BuySell)
            {
                var item = Globals.ItemInfoList.CreateNewObject(); item.ItemName = "布局商品";
                item.ItemType = ItemType.Consumable; item.Price = 10; item.StackSize = 1;
                var good = CEnvir.Session.GetCollection<NPCGood>().CreateNewObject(); good.Item = item; good.Page = page;
                var sale = CEnvir.Session.GetCollection<NPCType>().CreateNewObject(); sale.ItemType = item.ItemType; sale.Page = page;
            }
            scene.StorageBox.Visible = true; scene.InventoryBox.Visible = true;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            NpcReply(scene, connection, page); timer.Stop();
            NpcCheck(timer.ElapsedMilliseconds < 1000, "80-line response finishes within regression ceiling: " + type);
            log.Add("response-ms/" + type + "|" + timer.ElapsedMilliseconds);
            if (type == NPCDialogType.None)
            {
                timer.Restart();
                Call(typeof(NPCDialog), card, "ProcessText");
                timer.Stop(); log.Add("option-layout-ms|" + timer.ElapsedMilliseconds);
                timer.Restart();
                Call(typeof(NPCDialog), card, "ApplyTextViewport", scene.Size, false);
                timer.Stop(); log.Add("cached-viewport-ms|" + timer.ElapsedMilliseconds);
                timer.Restart();
                Call(typeof(NPCDialog), card, "ApplyServiceLocations");
                timer.Stop(); log.Add("group-layout-ms|" + timer.ElapsedMilliseconds);
            }
            var controls = card.Buttons.ToArray();
            var services = NpcServices(scene).ToDictionary(p => p.Key, p => p.Value.Visible);
            var sizes = CurrentNpcGroup(scene).ToDictionary(w => w, w => w.Size);
            foreach (var size in resolutions)
            {
                if (type == NPCDialogType.None || type == NPCDialogType.BuySell) Reset(size);
                else Config.GameSize = size;
                scene.Size = size;
                string scenario = type + "/" + size.Width + "x" + size.Height;
                CheckNpcGroupReachability(scene, scenario);
                NpcCheck(card.Buttons.SequenceEqual(controls), "group layout retains original option controls: " + scenario);
                NpcCheck(NpcServices(scene).All(p => p.Value.Visible == services[p.Key]), "group layout preserves business visibility: " + scenario);
                NpcCheck(sizes.All(p => p.Key.Size == p.Value || p.Key == card), "group layout preserves every service/grid size: " + scenario);
                NpcCheck(connection.SendList.IsEmpty, "group layout produces no gameplay requests: " + scenario);
                log.Add(scenario + "|" + string.Join(";", CurrentNpcGroup(scene).Select(w => w.GetType().Name + "=" + w.DisplayArea)));
                if (type == NPCDialogType.None || type == NPCDialogType.BuySell)
                    Render(scene, Path.Combine(evidence, style + "-group-reset-" + type + "-" + size.Width + "x" + size.Height + "-" + log.Count + ".png"));
                if (size == new Size(1024,768) && resolutions[0] == size && !log.Any(v => v.StartsWith("rendered/" + type + "|")))
                {
                    Render(scene, Path.Combine(evidence, style + "-group-" + type + ".png"));
                    log.Add("rendered/" + type + "|");
                }
            }
            // Open/close auxiliary windows after a response: this must reflow without
            // reconstructing the NPC page or starting any service.
            scene.StorageBox.Visible = false; scene.InventoryBox.Visible = false;
            CheckNpcGroupReachability(scene, type + "/aux-hidden");
            scene.InventoryBox.Visible = true; scene.StorageBox.Visible = true;
            CheckNpcGroupReachability(scene, type + "/aux-reopened");
            if (type == NPCDialogType.BuySell)
            {
                // Reflow must refresh hover even if no physical mouse event follows.
                var close = scene.NPCGoodsBox.CloseButton.DisplayArea;
                NpcMove(new Point(close.Left + close.Width / 2, close.Top + close.Height / 2));
                scene.StorageBox.Visible = false;
                var routed = DXControl.MouseControl;
                NpcMove(CEnvir.MouseLocation);
                NpcCheck(DXControl.MouseControl == routed, "group reflow refreshes hover before the next mouse event");
                scene.StorageBox.Visible = true;
                using (var modal = new DXMessageBox("组合布局确认", "原生确认"))
                {
                    scene.StorageBox.Visible = false; scene.StorageBox.Visible = true;
                    var order = ServiceChildren(scene);
                    int topService = NpcServices(scene).Values.Where(w => w.IsVisible).Max(w => order.IndexOf(w));
                    NpcCheck(order.IndexOf(modal) > topService && modal.WindowSkin == null,
                        "native modal retains foreground precedence and presentation after group reflow");
                    foreach (var overlay in new[] { scene.ItemLabel, scene.MagicLabel })
                        if (overlay != null) NpcCheck(order.IndexOf(overlay) > topService,
                            "native tooltip root retains foreground precedence after group reflow");
                }
                CheckNpcGroupReachability(scene, type + "/after-modal");
            }
            NpcCheck(card.Page == page && card.Buttons.SequenceEqual(controls) && connection.SendList.IsEmpty,
                "auxiliary visibility cannot change NPC page, option handlers or packets: " + type);
            NpcClick(scene, card.CloseButton, DateTime.UtcNow); NpcPacket<C.NPCClose>(connection, packets);
            NpcCheck(NpcServices(scene).Values.All(w => !w.Visible), "group close retains native dependent cleanup: " + type);
        }
        scene.StorageBox.Visible = false; scene.InventoryBox.Visible = false;
        File.WriteAllLines(Path.Combine(evidence, style + "-group-layouts.txt"), log);
    }
    private static void ExerciseNpcParagraphRegions(GameScene scene, CConnection connection, string evidence, string style)
    {
        var card = scene.NPCBox;
        var packets = new List<string>();
        var log = new List<string>();
        var prefixes = new[] {
            string.Empty,
            "第一段正文\n",
            string.Join("\n", Enumerable.Range(0, 80).Select(n => "短段正文 " + n)) + "\n",
            "空行之前\n\n\n包含 English 和中文的正文\r\n同段文字 ",
            string.Join(" ", Enumerable.Repeat("前段自动换行 wrapped text", 18)) + "\n\n后段文字 "
        };
        string option = string.Join(" ", Enumerable.Repeat("跨行选项 Action", 12));
        for (int i = 0; i < prefixes.Length; i++)
        {
            string prefix = prefixes[i];
            NpcReply(scene, connection, NpcPage(prefix + "[" + option + ":731]\n[离开:0]"));
            var text = card.PageText;
            var scroll = Field<DXVScrollBar>(card, "pageScroll");
            NpcCheck(text.Text.Length < 4096, "paragraph fixture covers former short-prefix slow path: " + i);
            // The public native parser receives the entire document. It does not use
            // the viewport paragraph cache or the optimized ProcessText entry point.
            var expected = NPCDialog.GetWordRegionsNew(DXManager.Graphics, text.Text, text.Font,
                text.DrawFormat, text.Size.Width, prefix.Length, option.Length);
            expected.AddRange(NPCDialog.GetWordRegionsNew(DXManager.Graphics, text.Text, text.Font,
                text.DrawFormat, text.Size.Width, prefix.Length + option.Length + 1, 2));
            NpcCheck(expected.Count == card.Buttons.Count, "paragraph-local and native full-document option counts match: " + i);
            for (int j = 0; j < expected.Count; j++)
            {
                var actual = card.Buttons[j]; var reference = expected[j];
                NpcCheck(actual.Location == reference.Region.Location && actual.Size == reference.Region.Size &&
                    actual.Text == text.Text.Substring(reference.Index, reference.Length),
                    "paragraph-local option equals native full-document geometry/text: " + i + "/" + j +
                    " actual=" + new Rectangle(actual.Location, actual.Size) + " expected=" + reference.Region +
                    " actualText=" + actual.Text + " expectedText=" + text.Text.Substring(reference.Index, reference.Length));
                log.Add(i + "/" + j + "|" + actual.Text + "|" + reference.Region);
            }
            Render(scene, Path.Combine(evidence, style + "-paragraph-regions-" + i + "-top.png"));
            CheckNpcTextTexture(card, 0, scroll.Visible ? scroll.MaxValue : text.Size.Height, evidence, style + "-paragraph-" + i + "-top");
            scroll.Value = int.MaxValue;
            Render(scene, Path.Combine(evidence, style + "-paragraph-regions-" + i + "-bottom.png"));
            CheckNpcTextTexture(card, scroll.Value, scroll.Visible ? scroll.MaxValue : text.Size.Height, evidence, style + "-paragraph-" + i + "-bottom");
            NpcClick(scene, card.CloseButton, DateTime.UtcNow);
            NpcPacket<C.NPCClose>(connection, packets);
        }
        File.WriteAllLines(Path.Combine(evidence, style + "-paragraph-native-geometry.txt"), log);
    }

    private static void CheckNpcTextTexture(NPCDialog card, int offset, int height, string evidence, string style)
    {
        var text = card.PageText;
        using (var expected = new Bitmap(text.Size.Width, text.Size.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            using (var graphics = Graphics.FromImage(expected))
            {
                DXManager.ConfigureGraphics(graphics); graphics.Clear(text.BackColour);
                TextRenderer.DrawText(graphics, text.Text, text.Font, new Rectangle(1, -offset, text.Size.Width, height), text.ForeColour, text.DrawFormat);
            }
            var locked = text.ControlTexture.LockRectangle(0, SlimDX.Direct3D9.LockFlags.ReadOnly);
            try
            {
                using (var actual = new Bitmap(text.Size.Width, text.Size.Height, locked.Pitch,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb, locked.Data.DataPointer))
                {
                    expected.Save(Path.Combine(evidence, style + "-long-text-reference.png"));
                    actual.Save(Path.Combine(evidence, style + "-long-text-texture.png"));
                    for (int y = 0; y < text.Size.Height; y++) for (int x = 0; x < text.Size.Width; x++)
                        if (actual.GetPixel(x,y).ToArgb() != expected.GetPixel(x,y).ToArgb())
                            throw new Exception("Viewport text differs from independent full-document renderer at " + x + "," + y);
                }
            }
            finally { text.ControlTexture.UnlockRectangle(0); locked.Data.Dispose(); }
        }
        NpcCheck(true, "viewport text texture equals independent full-document render pixel for pixel at offset " + offset);
    }

    private static void ExerciseNpcLongCard(GameScene scene, CConnection connection, string evidence, string style)
    {
        var card = scene.NPCBox;
        var packets = new List<string>();
        var clock = DateTime.UtcNow;
        string option = string.Join(" ", Enumerable.Repeat("末段跨行选项", 22));
        string body = "[首项:711]\n" + string.Join("\n", Enumerable.Range(1, 900).Select(i => "长正文第 " + i + " 行，保留原始文字和样式。"));
        var opening = System.Diagnostics.Stopwatch.StartNew();
        NpcReply(scene, connection, NpcPage(body + "\n[" + option + ":712]\n[末项:713]\n[离开:0]"));
        opening.Stop();
        File.WriteAllText(Path.Combine(evidence, style + "-long-card-open-ms.txt"), opening.ElapsedMilliseconds.ToString());
        NpcCheck(opening.Elapsed < TimeSpan.FromSeconds(5), "long multiline page opens without repeated whole-prefix measurement");
        var text = card.PageText; var close = card.CloseButton;
        var scroll = Field<DXVScrollBar>(card, "pageScroll");
        var labels = card.Buttons.ToArray();
        var first = labels.First(); var last = labels.Single(b => b.Text == "末项");
        var leave = labels.Single(b => b.Text == "离开");
        var wrapped = labels.Where(b => b.Text.Contains("末段跨行选项")).ToArray();
        NpcCheck(scroll.Visible && scroll.MaxValue > 10000 && scroll.Value == 0, "long body is measured beyond former 9999px option-region limit");
        int finalIndex = text.Text.IndexOf("末项", StringComparison.Ordinal);
        int lineHeight = TextRenderer.MeasureText(DXManager.Graphics, "A", text.Font, new Size(text.Size.Width, int.MaxValue), text.DrawFormat).Height;
        int finalLineY = TextRenderer.MeasureText(DXManager.Graphics, text.Text.Substring(0, finalIndex + 2), text.Font,
            new Size(text.Size.Width, int.MaxValue), text.DrawFormat).Height - lineHeight;
        NpcCheck(last.Location.Y == finalLineY, "tail hit region matches independent whole-prefix text measurement");
        NpcCheck(card.DisplayArea.Bottom <= scene.Size.Height - 202 && card.DisplayArea.Contains(close.DisplayArea), "long card and fixed close fit above HUD");
        Render(scene, Path.Combine(evidence, style + "-long-card-top.png"));
        NpcCheck(text.TextureSize == text.Size && text.TextureSize.Height < scroll.MaxValue, "long body allocates only viewport-sized GPU texture");
        NpcClick(scene, first, clock); NpcCheck(NpcPacket<C.NPCButton>(connection, packets).ButtonID == 711, "long card first option keeps original ID");
        var point = new Point(first.DisplayArea.Left + 3, first.DisplayArea.Top + 3);
        NpcMove(point);
        Call(typeof(TargetForm), CEnvir.Target, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, point.X, point.Y, -120));
        NpcCheck(scroll.Value == scroll.Change && DXControl.MouseControl != first, "real wheel over option scrolls and refreshes stale hover hit immediately");
        NpcCheck(labels.SequenceEqual(card.Buttons) && connection.SendList.IsEmpty, "wheel neither rebuilds handlers nor sends packets");
        Render(scene, Path.Combine(evidence, style + "-long-card-partial-line.png"));
        CheckNpcTextTexture(card, scroll.Value, scroll.MaxValue, evidence, style + "-partial-line");
        NpcClick(scene, scroll.DownButton, clock);
        NpcCheck(scroll.Value == 2 * scroll.Change && connection.SendList.IsEmpty, "native down arrow scrolls without business packets");
        NpcClick(scene, scroll.UpButton, clock);
        NpcCheck(scroll.Value == scroll.Change && connection.SendList.IsEmpty, "native up arrow scrolls without business packets");
        scroll.Value = 0;
        NpcClick(scene, first, clock.AddMilliseconds(999));
        NpcCheck(connection.SendList.IsEmpty, "scroll round trip preserves original one-second option throttle");
        NpcClick(scene, first, clock.AddSeconds(1)); NpcPacket<C.NPCButton>(connection, packets);
        var closeLocation = close.Location;
        scroll.Value = int.MaxValue;
        NpcCheck(scroll.Value == scroll.MaxValue - scroll.VisibleSize, "bottom scroll clamps to full document extent");
        NpcCheck(last.Location.Y > 0 && text.DisplayArea.Contains(last.DisplayArea) && text.DisplayArea.Contains(leave.DisplayArea), "last action and leave have fully reachable real hit rectangles");
        NpcCheck(close.Location == closeLocation && card.CloseButton == close && card.PageText == text, "scroll keeps native close and text identities fixed");
        NpcCheck(wrapped.Length >= 2 && string.Concat(wrapped.Select(b => b.Text)).Replace(" ", "") == option.Replace(" ", ""), "wrapped tail option retains all characters beyond 10000px");
        Render(scene, Path.Combine(evidence, style + "-long-card-bottom.png"));
        CheckNpcTextTexture(card, scroll.Value, scroll.MaxValue, evidence, style);
        NpcClick(scene, wrapped[0], clock.AddSeconds(2)); NpcCheck(NpcPacket<C.NPCButton>(connection, packets).ButtonID == 712, "wrapped tail first rectangle sends original ID");
        NpcClick(scene, wrapped.Last(), clock.AddSeconds(2).AddMilliseconds(999));
        NpcCheck(connection.SendList.IsEmpty, "wrapped tail rectangles share throttle after scrolling");
        NpcClick(scene, last, clock.AddSeconds(3)); NpcCheck(NpcPacket<C.NPCButton>(connection, packets).ButtonID == 713, "last real coordinate sends exact ID");
        NpcMove(new Point(first.DisplayArea.Left + 3, card.DisplayArea.Top + 5));
        NpcCheck(DXControl.MouseControl != first, "offscreen first option cannot capture a header coordinate");
        foreach (var size in new[] { new Size(1024,768), new Size(1366,768), new Size(1280,800), new Size(1440,900), new Size(1600,900), new Size(1920,1080), new Size(1024,768) })
        {
            Reset(size); scene.Size = size;
            NpcCheck(labels.SequenceEqual(card.Buttons), "long-card resize retains every option handler: " + size);
            NpcCheck(scroll.Value >= 0 && scroll.Value <= scroll.MaxValue - scroll.VisibleSize, "resize clamps scroll extent: " + size);
            scroll.Value = int.MaxValue;
            NpcCheck(card.DisplayArea.Bottom <= size.Height - 202 && card.DisplayArea.Contains(close.DisplayArea), "resize keeps fixed close above HUD: " + size);
            Render(scene, Path.Combine(evidence, style + "-long-card-" + size.Width + "x" + size.Height + ".png"));
            NpcCheck(text.TextureSize == text.Size && text.DisplayArea.Contains(last.DisplayArea), "reset preserves bounded texture and final action: " + size);
            NpcCheck(connection.SendList.IsEmpty, "scroll/resize/reset sends no business packets: " + size);
        }
        NpcClick(scene, last, clock.AddSeconds(3).AddMilliseconds(999));
        NpcCheck(connection.SendList.IsEmpty, "all resolution changes preserve last option throttle");
        NpcClick(scene, last, clock.AddSeconds(4)); NpcPacket<C.NPCButton>(connection, packets);
        NpcClick(scene, leave, clock.AddSeconds(5)); NpcPacket<C.NPCClose>(connection, packets);
        NpcCheck(!card.Opened && !card.Visible && scene.NPCID == 0, "scrolled ButtonID zero keeps original close cleanup");
        NpcReply(scene, connection, NpcPage("短正文\n[新选项:714]\n[离开:0]"));
        NpcCheck(!scroll.Visible && scroll.Value == 0 && labels.All(b => b.IsDisposed), "new short page clears overflow, offset and stale option labels");
        NpcCheck(card.ClientArea.Height == 180 && text.Location == new Point(card.ClientArea.X + 10, card.ClientArea.Y + 10), "short page restores native text insets and compact height");
        NpcClick(scene, card.Buttons[0], clock.AddSeconds(6)); NpcCheck(NpcPacket<C.NPCButton>(connection, packets).ButtonID == 714, "replacement page sends only replacement option ID");
        NpcClick(scene, close, clock.AddSeconds(6)); NpcPacket<C.NPCClose>(connection, packets);
    }

    private static DXItemCell[] NpcLifecycleLinks(GameScene scene, NPCDialogType type)
    {
        if (type == NPCDialogType.AccessoryReset)
            return scene.NPCAccessoryResetBox.AccessoryGrid.Grid;
        if (type == NPCDialogType.WeaponCraft)
            return new[] { scene.NPCWeaponCraftBox.TemplateCell, scene.NPCWeaponCraftBox.YellowCell,
                scene.NPCWeaponCraftBox.BlueCell, scene.NPCWeaponCraftBox.RedCell,
                scene.NPCWeaponCraftBox.PurpleCell, scene.NPCWeaponCraftBox.GreenCell,
                scene.NPCWeaponCraftBox.GreyCell }.SelectMany(g => g.Grid).ToArray();
        return new DXItemCell[0];
    }
    private static void ExerciseNpcLifecycle(GameScene scene, CConnection connection, string evidence,
        string style, List<string> trace)
    {
        var services = NpcServices(scene);
        var log = new List<string>();
        var originalCells = scene.InventoryBox.Grid.Grid.Take(7).ToArray();
        var oldItems = originalCells.Select(c => c.Item).ToArray();
        var npc = Globals.NPCInfoList.CreateNewObject(); npc.NPCName = "生命周期测试";
        var visited = new HashSet<string>();
        using (DXWindow.SuppressSceneLayoutWrites(scene))
        try
        {
            foreach (NPCDialogType type in Enum.GetValues(typeof(NPCDialogType)).Cast<NPCDialogType>()
                .OrderBy(t => t == NPCDialogType.WeaponCraft ? 0 : 1))
            foreach (string route in new[] { "close-button", "leave-option", "server-close", "object-remove", "replace-page" })
            {
                var page = NpcPage("生命周期 " + type + "\n[离开:0]", type);
                if (type == NPCDialogType.BuySell)
                {
                    var item = ServiceItem(800).Info;
                    var good = CEnvir.Session.GetCollection<NPCGood>().CreateNewObject(); good.Item = item; good.Page = page;
                    var saleType = CEnvir.Session.GetCollection<NPCType>().CreateNewObject(); saleType.ItemType = ItemType.Weapon; saleType.Page = page;
                }
                var mapNpc = new Client.Models.NPCObject(new S.ObjectNPC {
                    ObjectID = 9001, NPCIndex = npc.Index, CurrentLocation = scene.User.CurrentLocation });
                try
                {
                    NpcReply(scene, connection, page);
                    scene.NPCQuestBox.Visible = true;
                    foreach (var p in services.Where(p => p.Value.Visible)) visited.Add(p.Key);
                    var identities = services.ToDictionary(p => p.Key, p => p.Value);
                    var targets = NpcLifecycleLinks(scene, type);
                    for (int i = 0; i < targets.Length; i++)
                    {
                        var item = ServiceItem(810 + i);
                        if (type == NPCDialogType.AccessoryReset) item.Info.ItemType = ItemType.Ring;
                        originalCells[i].Item = item; originalCells[i].Locked = false;
                        targets[i].LinkedCount = 1; targets[i].Link = originalCells[i];
                        NpcCheck(targets[i].Link == originalCells[i] && originalCells[i].Link == targets[i],
                            "lifecycle fixture links both original cells: " + type + "/" + route + "/" + i);
                    }
                    // Isolate main-card hit testing from permitted manual overlap.
                    scene.InventoryBox.Visible = false; scene.StorageBox.Visible = false;
                    scene.NPCBox.BringToFront();
                    switch (route)
                    {
                        case "close-button": NpcClick(scene, scene.NPCBox.CloseButton, DateTime.UtcNow); break;
                        case "leave-option":
                            scene.NPCTime = DateTime.MinValue;
                            NpcClick(scene, scene.NPCBox.Buttons.Last(), DateTime.UtcNow); break;
                        case "server-close": connection.Process(new S.NPCClose()); break;
                        case "object-remove":
                            connection.Process(new S.ObjectRemove { ObjectID = 9002 });
                            NpcCheck(scene.NPCBox.Visible && connection.SendList.IsEmpty,
                                "unrelated object removal retains active NPC");
                            connection.Process(new S.ObjectRemove { ObjectID = 9001 });
                            NpcCheck(!scene.MapControl.Objects.Contains(mapNpc) && !Client.Models.NPCObject.NPCs.ContainsKey(npc),
                                "current NPC object removed through decoded packet"); break;
                        case "replace-page": NpcReply(scene, connection, NpcPage("替换为普通服务\n[离开:0]")); break;
                    }
                    bool replaced = route == "replace-page";
                    if (!replaced) NpcPacket<C.NPCClose>(connection, trace);
                    NpcCheck(scene.NPCBox.Visible == replaced && scene.NPCBox.Opened == replaced && scene.NPCID == (replaced ? 9001u : 0u),
                        "main lifecycle state: " + type + "/" + route);
                    foreach (var p in services)
                    {
                        bool expectedVisible = replaced && p.Key == "NPCQuestBox";
                        NpcCheck(p.Value.Visible == expectedVisible && !p.Value.IsDisposed && ReferenceEquals(p.Value, identities[p.Key]),
                            "dependent lifecycle state and identity: " + type + "/" + route + "/" + p.Key);
                    }
                    NpcCheck(targets.All(c => c.Link == null) && originalCells.Take(targets.Length).All(c => c.Link == null && !c.Locked),
                        "hidden service releases both link directions without locking: " + type + "/" + route);
                    NpcCheck(connection.SendList.IsEmpty, "lifecycle sends no extra service transaction: " + type + "/" + route);
                    // A late duplicate close remains a no-op; replacement still owns one close.
                    connection.Process(new S.NPCClose());
                    if (replaced) NpcPacket<C.NPCClose>(connection, trace);
                    connection.Process(new S.NPCClose());
                    NpcCheck(connection.SendList.IsEmpty && services.Values.All(w => !w.Visible), "duplicate lifecycle close is idempotent");
                    NpcReply(scene, connection, page);
                    NpcCheck(targets.All(c => c.Link == null), "reopen cannot reuse a previous service link: " + type + "/" + route);
                    NpcClick(scene, scene.NPCBox.CloseButton, DateTime.UtcNow); NpcPacket<C.NPCClose>(connection, trace);
                    log.Add(type + "|" + route + "|links=" + targets.Length + "|reopened=true");
                }
                finally { if (scene.MapControl.Objects.Contains(mapNpc)) mapNpc.Remove(); }
            }
            NpcCheck(visited.Count == 16 && services.Keys.All(visited.Contains), "lifecycle matrix visited all sixteen native dependent windows");
            var repeated = NpcPage("重复开关\n[离开:0]");
            for (int i = 0; i < 100; i++)
            {
                NpcReply(scene, connection, repeated);
                NpcClick(scene, scene.NPCBox.CloseButton, DateTime.UtcNow); NpcPacket<C.NPCClose>(connection, trace);
                NpcCheck(services.Values.All(w => !w.Visible), "repeated close leaves no orphan NPC service: " + i);
            }
            File.WriteAllLines(Path.Combine(evidence, style + "-lifecycle.txt"), log);
        }
        finally
        {
            foreach (var target in new[] { NPCDialogType.AccessoryReset, NPCDialogType.WeaponCraft }.SelectMany(t => NpcLifecycleLinks(scene, t)))
                target.Link = null;
            for (int i = 0; i < originalCells.Length; i++) originalCells[i].Item = oldItems[i];
        }
    }
    private static DXControl[] PrepareNpcDisposal(GameScene scene, CConnection connection, string evidence,
        string style, List<string> trace)
    {
        NpcReply(scene, connection, NpcPage("退出前保留服务选择\n[离开:0]", NPCDialogType.WeaponCraft));
        var source = scene.InventoryBox.Grid.Grid[0]; source.Item = ServiceItem(900); source.Locked = false;
        var target = scene.NPCWeaponCraftBox.TemplateCell.Grid[0]; target.LinkedCount = 1; target.Link = source;
        scene.SelectedCell = source;
        var windows = new[] { (DXControl)scene.NPCBox }.Concat(NpcServices(scene).Values).ToArray();
        var controls = windows.Concat(windows.SelectMany(w => ServiceChildren(w))).Concat(new DXControl[] { source, target }).ToArray();
        connection.Process(new Library.Network.GeneralPackets.Disconnect { Reason = DisconnectReason.TimedOut });
        var modal = ServiceChildren(scene).OfType<DXMessageBox>().Single();
        NpcCheck(connection.Disconnecting && CEnvir.Connection == null && modal.Visible && modal.Modal && modal.WindowSkin == null,
            "decoded disconnect retains native return-to-login modal and disconnect state");
        NpcCheck(scene.NPCBox.Visible && target.Link == source && source.Link == target,
            "disconnect waits for native scene-exit action before disposing live NPC selection");
        NpcCheck(connection.SendList.IsEmpty, "disconnect modal adds no NPC/service packets");
        trace.Add("disconnect|TimedOut|native-modal|scene-exit-pending");
        Render(scene, Path.Combine(evidence, style + "-disconnect-modal.png"));
        return controls.Concat(new DXControl[] { modal }).ToArray();
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
