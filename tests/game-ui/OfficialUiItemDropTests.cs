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
using Client.Models;
using Client.Scenes;
using Library;
using Library.Network;
using Library.SystemModels;
using C = Library.Network.ClientPackets;
using S = Library.Network.ServerPackets;

internal static class OfficialUiItemDropEntry
{
    [STAThread] private static int Main(string[] args) => OfficialUiFullSceneTests.RunItemDrops(args);
}

internal static partial class OfficialUiFullSceneTests
{
    private static List<DXControl> Children(DXControl control) => (List<DXControl>)typeof(DXControl).GetProperty("Controls", Instance).GetValue(control);
    private static Point Center(DXControl control) => new Point(control.DisplayArea.Left + control.Size.Width / 2, control.DisplayArea.Top + control.Size.Height / 2);
    private static void Pointer(string method, Point point, MouseButtons button = MouseButtons.None)
    {
        CEnvir.Now = DateTime.UtcNow;
        Call(typeof(TargetForm), CEnvir.Target, method, new MouseEventArgs(button, 1, point.X, point.Y, 0));
        ItemCheck(!(bool)typeof(CEnvir).GetProperty("GameUiOperationsBlocked", Static).GetValue(null), method + " does not fault startup");
    }
    private static void MapPress(GameScene scene, CConnection connection)
    {
        var point = new Point(600, 280);
        Pointer("OnMouseMove", point);
        ItemCheck(DXControl.MouseControl == scene.MapControl, "map target is original map control");
        Pointer("OnMouseDown", point, MouseButtons.Left);
        Pointer("OnMouseUp", point, MouseButtons.Left);
        ItemCheck(scene.MapControl.MapButtons == MouseButtons.None, "release clears held map buttons");
    }
    private static void DropReply(CConnection connection, C.ItemDrop packet, bool success, long count)
    {
        connection.Process(new S.ItemChanged { Link = new CellLinkInfo { GridType = packet.Link.GridType, Slot = packet.Link.Slot, Count = count }, Success = success });
    }
    private static List<string> ExerciseDrops(GameUiStartupSession owner, MapInfo map, string evidence, string style)
    {
        interactionStyle = style;
        var trace = new List<string>();
        using (var scene = new GameScene(Config.GameSize))
        {
            scenes++; Populate(scene, map);
            var connection = (CConnection)FormatterServices.GetUninitializedObject(typeof(CConnection));
            connection.Connected = true; connection.SendList = new ConcurrentQueue<Packet>(); CEnvir.Connection = connection;
            try
            {
                scene.ChatTextBox.TextBox.KeepFocus = false; DXTextBox.ActiveTextBox = null;
                CEnvir.Shift = CEnvir.Alt = CEnvir.Ctrl = false;
                scene.CharacterBox.Visible = false; scene.InventoryBox.Visible = true;
                if (owner.EffectiveStyle == GameUiStyle.Legacy)
                {
                    scene.InventoryBox.Location = new Point(330, 30);
                    scene.BeltBox.Size = scene.BeltBox.GetAcceptableResize(scene.BeltBox.GetSize(new Size(361, 36)));
                    scene.BeltBox.Location = new Point(330, 590);
                }
                foreach (var link in scene.BeltBox.Links) { link.LinkInfoIndex = -1; link.LinkItemIndex = -1; }
                var bag = scene.InventoryBox.Grid.Grid;
                var item = MakeItem(ItemType.Consumable, 8100, 20); bag[0].Item = item;
                // The original client selects on click. Holding/moving/releasing is not a transfer.
                var first = Center(bag[0]); var last = Center(bag[48]);
                Pointer("OnMouseMove", first); ItemCheck(DXControl.MouseControl == bag[0], "held source hit");
                Pointer("OnMouseDown", first, MouseButtons.Left);
                Pointer("OnMouseMove", last, MouseButtons.Left);
                Pointer("OnMouseUp", last, MouseButtons.Left);
                ItemCheck(scene.SelectedCell == null && ReferenceEquals(bag[0].Item, item) && bag[48].Item == null, "held source/release alone does not move or select");
                NoPackets(connection, "held source/release emits nothing");
                ClickControl(scene, bag[0]);
                Pointer("OnMouseMove", last, MouseButtons.Left); Pointer("OnMouseUp", last, MouseButtons.Left);
                ItemCheck(scene.SelectedCell == bag[0], "moving/releasing selected item retains click selection");
                NoPackets(connection, "selected item held move/release emits nothing");
                ClickControl(scene, bag[48]); var moved = OnePacket<C.ItemMove>(connection); trace.Add(MoveTrace(moved)); Reply(connection, moved, true);
                ItemCheck(bag[0].Item == null && ReferenceEquals(bag[48].Item, item), "next actual click transfers to destination");
                ClickControl(scene, bag[48]); ClickControl(scene, bag[48], MouseButtons.Right);
                ItemCheck(scene.SelectedCell == null && ReferenceEquals(bag[48].Item, item), "right button cancels selection without using selected item");
                NoPackets(connection, "selection cancel no use/drop"); bag[48].Item = null;
                // All 49 original IDs reach the map drop handler. Failure preserves each exact item.
                for (int slot = 0; slot < bag.Length; slot++)
                {
                    item = MakeItem(ItemType.Consumable, 8200 + slot, 20); bag[slot].Item = item;
                    ClickControl(scene, bag[slot]); MapPress(scene, connection);
                    var drop = OnePacket<C.ItemDrop>(connection);
                    ItemCheck(drop.Link.GridType == GridType.Inventory && drop.Link.Slot == slot && drop.Link.Count == 20, "exact original drop ID/count " + slot);
                    ItemCheck(bag[slot].Locked && scene.SelectedCell == null && ReferenceEquals(bag[slot].Item, item), "drop pending locks without speculative removal " + slot);
                    trace.Add("drop|" + drop.Link.GridType + "|" + drop.Link.Slot + "|" + drop.Link.Count);
                    DropReply(connection, drop, false, 0);
                    ItemCheck(!bag[slot].Locked && ReferenceEquals(bag[slot].Item, item) && item.Count == 20, "drop rejection unlocks and retains identity/count " + slot);
                    bag[slot].Item = null;
                }
                bag[48].Item = item = MakeItem(ItemType.Consumable, 8300, 20);
                ClickControl(scene, bag[48]); MapPress(scene, connection); var accepted = OnePacket<C.ItemDrop>(connection);
                DropReply(connection, accepted, true, 7);
                ItemCheck(!bag[48].Locked && ReferenceEquals(bag[48].Item, item) && item.Count == 7, "authoritative remaining count applied");
                ClickControl(scene, bag[48]); MapPress(scene, connection); accepted = OnePacket<C.ItemDrop>(connection);
                ItemCheck(accepted.Link.Count == 7, "repeat drop uses authoritative remaining count");
                DropReply(connection, accepted, true, 0);
                ItemCheck(bag[48].Item == null && !bag[48].Locked, "accepted full drop empties/unlocks cell");
                trace.Add("accepted-drop|20-to-7-to-0");
                bag[0].Item = item = MakeItem(ItemType.Consumable, 8400, 20); item.Flags |= UserItemFlags.Locked;
                ClickControl(scene, bag[0]); MapPress(scene, connection);
                ItemCheck(scene.SelectedCell == null && !bag[0].Locked && ReferenceEquals(bag[0].Item, item), "flag-locked item refuses map drop");
                NoPackets(connection, "flag-locked item no drop"); bag[0].Item = null;
                scene.CharacterBox.Visible = true;
                if (owner.EffectiveStyle == GameUiStyle.Legacy) scene.CharacterBox.Location = new Point(0, 30);
                scene.CharacterBox.Grid[0].Item = item = MakeItem(ItemType.Weapon, 8401);
                ClickControl(scene, scene.CharacterBox.Grid[0]); scene.CharacterBox.Visible = false; MapPress(scene, connection);
                ItemCheck(scene.SelectedCell == null && ReferenceEquals(scene.CharacterBox.Grid[0].Item, item), "equipped item refuses direct map drop");
                NoPackets(connection, "equipment cannot bypass unequip"); scene.CharacterBox.Grid[0].Item = null;
                // Belt drop clears the original link and never discards the referenced bag item.
                bag[0].Item = item = MakeItem(ItemType.Consumable, 8500, 20); scene.BeltBox.Visible = true;
                ClickControl(scene, bag[0]); ClickControl(scene, scene.BeltBox.Grid.Grid[0]); OnePacket<C.BeltLinkChanged>(connection);
                ClickControl(scene, scene.BeltBox.Grid.Grid[0]); MapPress(scene, connection);
                var unlink = OnePacket<C.BeltLinkChanged>(connection);
                ItemCheck(unlink.Slot == 0 && unlink.LinkIndex == -1 && unlink.LinkItemIndex == -1, "belt map drop only clears link");
                ItemCheck(ReferenceEquals(bag[0].Item, item) && item.Count == 20 && !bag[0].Locked, "belt unlink retains bag identity/count/unlocked");
                trace.Add("belt-unlink|0|-1|-1"); bag[0].Item = null;
                // Amount popup stays unskinned; only confirmation queues gold, only reply changes it.
                Globals.GoldInfo = Globals.ItemInfoList.CreateNewObject(); Globals.GoldInfo.ItemName = "离线金币";
                Globals.GoldInfo.Effect = ItemEffect.Gold; Globals.GoldInfo.Image = fixtureImage;
                var gold = scene.User.Gold;
                ClickControl(scene, scene.InventoryBox.GoldLabel); ItemCheck(scene.GoldPickedUp, "gold label selects positive gold");
                ClickControl(scene, scene.InventoryBox.GoldLabel, MouseButtons.Right); ItemCheck(!scene.GoldPickedUp, "right button cancels gold selection");
                NoPackets(connection, "gold selection/cancel local only");
                ClickControl(scene, scene.InventoryBox.GoldLabel); MapPress(scene, connection);
                var amount = DXWindow.MessageBoxList.OfType<DXItemAmountWindow>().Single(w => !w.IsDisposed && w.IsVisible);
                ItemCheck(amount.WindowSkin == null && amount.AmountBox.MaxValue == gold && !scene.GoldPickedUp, "original amount window and max gold retained");
                NoPackets(connection, "gold popup opening sends nothing");
                ClickControl(scene, amount.CloseButton); NoPackets(connection, "gold popup close cancels");
                ItemCheck(scene.User.Gold == gold, "gold close does not deduct");
                ClickControl(scene, scene.InventoryBox.GoldLabel); MapPress(scene, connection);
                amount = DXWindow.MessageBoxList.OfType<DXItemAmountWindow>().Single(w => !w.IsDisposed && w.IsVisible);
                amount.AmountBox.Value = 0; ItemCheck(!amount.ConfirmButton.IsEnabled, "zero gold confirmation disabled");
                amount.AmountBox.Value = 17; ClickControl(scene, amount.ConfirmButton);
                var goldDrop = OnePacket<C.GoldDrop>(connection);
                ItemCheck(goldDrop.Amount == 17 && scene.User.Gold == gold && !scene.GoldPickedUp, "confirm sends exact gold without speculative deduction");
                trace.Add("gold-drop|" + goldDrop.Amount); connection.Process(new S.GoldChanged { Gold = gold - 17 });
                ItemCheck(scene.User.Gold == gold - 17, "gold response applies authoritative total");
                scene.User.Gold = 0; ClickControl(scene, scene.InventoryBox.GoldLabel);
                ItemCheck(!scene.GoldPickedUp, "zero balance cannot pick up gold"); NoPackets(connection, "zero balance no request"); scene.User.Gold = gold;
                DXTextBox.ActiveTextBox = null;
                var buttons = new[] { "RemoteSellButton", "RemoteFragmentButton", "MonsterDropListButton" };
                var windows = new DXWindow[] { scene.NPCSellBox, scene.NPCItemFragmentBox, scene.MonsterDropListBox };
                var sizes = new[] { new Size(1024, 768), new Size(800, 600), new Size(1280, 720), new Size(1280, 800), new Size(1366, 768), new Size(1920, 1080), new Size(1024, 768) };
                int checkpoint = 0;
                foreach (var size in sizes)
                {
                    checkpoint++;
                    if (Config.GameSize != size) Reset(size);
                    scene.InventoryBox.Visible = true;
                    if (owner.EffectiveStyle == GameUiStyle.Legacy) scene.InventoryBox.Location = new Point(330, 30);
                    for (int i = 0; i < buttons.Length; i++)
                    {
                        var popup = windows[i]; var originalSize = popup.Size; var originalControls = Children(popup).ToArray();
                        ClickControl(scene, Field<DXButton>(scene.InventoryBox, buttons[i]));
                        ItemCheck(popup.IsVisible && popup.WindowSkin == null && popup.Size == originalSize && Children(popup).SequenceEqual(originalControls), "button opens same original unskinned popup " + buttons[i]);
                        ItemCheck(scene.DisplayArea.Contains(popup.DisplayArea), "opened popup fits viewport " + buttons[i] + " bounds=" + popup.DisplayArea);
                        var preferred = new Point(i == 0 ? 0 : i == 1 ? scene.InventoryBox.Size.Width - popup.Size.Width : 500, scene.InventoryBox.Size.Height);
                        var expected = new Point(Math.Max(0, Math.Min(preferred.X, scene.Size.Width - popup.Size.Width)), Math.Max(0, Math.Min(preferred.Y, scene.Size.Height - popup.Size.Height)));
                        ItemCheck(popup.Location == expected, "preserve preferred position except viewport overflow " + buttons[i]);
                        NoPackets(connection, "popup opening does not submit item operation " + buttons[i]);
                        Pointer("OnMouseMove", new Point(600, 280)); Render(scene, Path.Combine(evidence, style + "-v" + checkpoint + "-" + size.Width + "x" + size.Height + "-" + buttons[i] + ".png"));
                        ClickControl(scene, popup.CloseButton); ItemCheck(!popup.IsVisible && !popup.IsDisposed, "popup close retains original object " + buttons[i]);
                        NoPackets(connection, "popup close no item submission " + buttons[i]); trace.Add("popup-open-close|" + size + "|" + buttons[i]);
                    }
                }
                File.WriteAllLines(Path.Combine(evidence, style + "-packet-trace.txt"), trace);
            }
            finally { CEnvir.Connection = null; CEnvir.Shift = CEnvir.Alt = CEnvir.Ctrl = false; DXTextBox.ActiveTextBox = null; }
        }
        DXControl.ActiveScene = null;
        ItemCheck(GameScene.Game == null && Client.Scenes.Views.ChatTab.Tabs.Count == 0, "drop scene teardown clears controls");
        return trace;
    }
    internal static int RunItemDrops(string[] args)
    {
        try
        {
            string package = args[0], data = args[1], evidence = args[2]; Directory.CreateDirectory(evidence);
            Config.FullScreen = false; Config.GameSize = new Size(1024, 768); Config.LogChat = false;
            Config.SystemVolume = Config.MusicVolume = Config.PlayerVolume = Config.MonsterVolume = Config.MagicVolume = 0;
            CEnvir.Now = DateTime.UtcNow; Directory.CreateDirectory("Data"); InitializeDatabase();
            var map = BlankMap(Path.GetFullPath("Map")); LoadLibraries(data, evidence);
            fixtureImage = Enumerable.Range(1, CEnvir.LibraryList[LibraryFile.StoreItems].Images.Length - 1).First(i => CEnvir.LibraryList[LibraryFile.StoreItems].GetSize(i).Width > 0);
            string installed = Path.Combine(Environment.CurrentDirectory, GameUiOfficialV1Catalog.PackageRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(installed)); File.Copy(package, installed);
            using (var target = new TargetForm { ClientSize = Config.GameSize })
            {
                CEnvir.Target = target; DXManager.Create();
                try
                {
                    List<string> baseline;
                    using (var legacy = GameUiStartupSession.Prepare("Legacy", Environment.CurrentDirectory))
                    { Publish(legacy); baseline = ExerciseDrops(legacy, map, evidence, "legacy"); Publish(null); }
                    using (var official = (GameUiStartupSession)typeof(GameUiStartupSession).GetMethod("Prepare", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { "OfficialV1", Environment.CurrentDirectory, true }))
                    {
                        ItemCheck(official.EffectiveStyle == GameUiStyle.OfficialV1, "test-only package accepted"); Publish(official);
                        var actual = ExerciseDrops(official, map, evidence, "official");
                        ItemCheck(baseline.SequenceEqual(actual), "Legacy/Official ordered semantic packet traces identical");
                        actual = ExerciseDrops(official, map, evidence, "official-reentry");
                        ItemCheck(baseline.SequenceEqual(actual), "reentry ordered semantic packet traces identical"); Publish(null);
                    }
                }
                finally { CEnvir.Connection = null; Publish(null); DXManager.Unload(); }
            }
            ItemCheck(!GameUiOfficialV1Catalog.RuntimeReady, "production readiness remains false");
            File.WriteAllLines(Path.Combine(evidence, "coordinate-hits.txt"), hits);
            File.WriteAllLines(Path.Combine(evidence, "assertions.txt"), interactionChecks);
            File.WriteAllText(Path.Combine(evidence, "metrics.json"), "{\"checks\":" + checks + ",\"scenes\":" + scenes + ",\"presentedFrames\":" + renders + ",\"deviceResets\":" + resets + ",\"coordinateHits\":" + (hits.Count-1) + ",\"fixtureImage\":" + fixtureImage + ",\"serverConnected\":false,\"releaseAccepted\":false}");
            Console.WriteLine("Item-drop and bag-operation offline integration: " + checks + " checks passed; " + scenes + " real scenes, " + (hits.Count-1) + " coordinate hits, " + renders + " presented frames, " + resets + " device resets. Held pointer routing, item drop replies, gold cancel/confirm, belt unlink, inventory buttons and original popup controls. Legacy/Official/reentry traces identical. Socket-free queue and synthetic response injection; no live server or IME acceptance.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { CEnvir.Connection = null; foreach (var library in CEnvir.LibraryList.Values) library.Dispose(); CEnvir.LibraryList.Clear(); CEnvir.Target = null; }
    }
}
