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

internal static class OfficialUiItemInteractionEntry
{
    [STAThread] private static int Main(string[] args) => OfficialUiFullSceneTests.RunItemInteractions(args);
}

internal static partial class OfficialUiFullSceneTests
{
    private static readonly List<string> hits = new List<string> { "style|control|grid|slot|x|y" };
    private static readonly List<string> interactionChecks = new List<string>();
    private static string interactionStyle;
    private static int fixtureImage;
    private static void ItemCheck(bool condition, string message)
    {
        Check(condition, interactionStyle + ": " + message);
        interactionChecks.Add(interactionStyle + "|" + message);
    }
    private static ClientUserItem MakeItem(ItemType type, int identity, long count = 1, ItemInfo shared = null)
    {
        var info = shared ?? Globals.ItemInfoList.CreateNewObject();
        if (shared == null)
        {
            info.ItemName = "离线道具" + identity; info.ItemType = type; info.Image = fixtureImage;
            info.RequiredClass = RequiredClass.All; info.RequiredGender = RequiredGender.None;
            info.RequiredAmount = 0; info.Weight = 0; info.StackSize = count > 1 ? 100 : 1;
            info.Durability = type == ItemType.Consumable ? 0 : 1000;
        }
        return new ClientUserItem(info, count) { Index = identity };
    }
    private static void ClickControl(GameScene scene, DXControl control, MouseButtons button = MouseButtons.Left, bool second = false)
    {
        var cell = control as DXItemCell;
        if (cell != null && cell.GridType == GridType.Equipment)
            Field<DXTabControl>(scene.CharacterBox, "TabControl").SelectedTab = (DXTab)cell.Parent;
        ItemCheck(control.IsVisible && control.IsEnabled, "click target visible/enabled " + control.GetType().Name + "/" + cell?.Slot);
        var area = control.DisplayArea;
        var point = new Point(area.Left + area.Width / 2, area.Top + area.Height / 2);
        ItemCheck(scene.DisplayArea.Contains(point), "click target inside viewport " + cell?.GridType + "/" + cell?.Slot);
        Call(typeof(TargetForm), CEnvir.Target, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, point.X, point.Y, 0));
        ItemCheck(DXControl.MouseControl == control, "coordinate hit " + cell?.GridType + "/" + cell?.Slot + " actual=" + DXControl.MouseControl?.GetType().Name);
        hits.Add(interactionStyle + "|" + control.GetType().Name + "|" + cell?.GridType + "|" + cell?.Slot + "|" + point.X + "|" + point.Y);
        if (!second) scene.ClickTime = DateTime.MinValue;
        CEnvir.Now = DateTime.UtcNow;
        var e = new MouseEventArgs(button, second ? 2 : 1, point.X, point.Y, 0);
        Call(typeof(TargetForm), CEnvir.Target, "OnMouseDown", e);
        Call(typeof(TargetForm), CEnvir.Target, second ? "OnMouseDoubleClick" : "OnMouseClick", e);
        Call(typeof(TargetForm), CEnvir.Target, "OnMouseUp", e);
        ItemCheck(!(bool)typeof(CEnvir).GetProperty("GameUiOperationsBlocked", Static).GetValue(null), "input did not fault startup session");
    }
    private static void NoPackets(CConnection connection, string reason) => ItemCheck(connection.SendList.IsEmpty, reason);
    private static T OnePacket<T>(CConnection connection, string context = null) where T : Packet
    {
        Packet packet;
        ItemCheck(connection.SendList.TryDequeue(out packet) && packet is T, "one expected packet " + typeof(T).Name + " " + context);
        NoPackets(connection, "no duplicate packet " + typeof(T).Name);
        return (T)packet;
    }
    private static string MoveTrace(C.ItemMove p) => "move|" + p.FromGrid + "|" + p.FromSlot + "|" + p.ToGrid + "|" + p.ToSlot + "|" + p.MergeItem;
    private static void Reply(CConnection connection, C.ItemMove p, bool success)
    {
        connection.Process(new S.ItemMove { FromGrid = p.FromGrid, FromSlot = p.FromSlot, ToGrid = p.ToGrid, ToSlot = p.ToSlot, MergeItem = p.MergeItem, Success = success });
    }
    private static C.ItemMove Move(GameScene scene, CConnection connection, DXItemCell from, DXItemCell to, List<string> trace)
    {
        var originalFrom = from.Item; var originalTo = to.Item;
        ClickControl(scene, from);
        ItemCheck(scene.SelectedCell == from, "source selected " + from.GridType + "/" + from.Slot);
        NoPackets(connection, "select alone sends nothing");
        ClickControl(scene, to);
        var packet = OnePacket<C.ItemMove>(connection);
        ItemCheck(packet.FromGrid == from.GridType && packet.FromSlot == from.Slot && packet.ToGrid == to.GridType && packet.ToSlot == to.Slot, "exact source/destination slot IDs");
        ItemCheck(from.Locked && to.Locked && scene.SelectedCell == null, "pending move locks both cells and clears selection");
        ItemCheck(ReferenceEquals(from.Item, originalFrom) && ReferenceEquals(to.Item, originalTo), "no speculative item mutation before reply");
        ClickControl(scene, from); ClickControl(scene, to);
        NoPackets(connection, "locked clicks cannot duplicate request");
        trace.Add(MoveTrace(packet));
        return packet;
    }
    private static void EquipmentGesture(GameScene scene, DXItemCell cell, bool twice)
    {
        if (twice) { ClickControl(scene, cell); ClickControl(scene, cell, MouseButtons.Left, true); }
        else ClickControl(scene, cell, MouseButtons.Right);
    }
    private static void OtherEquipmentUnchanged(DXItemCell[] equipment, ClientUserItem[] before, int changedSlot, string reason)
    {
        for (int slot = 0; slot < equipment.Length; slot++)
            if (slot != changedSlot) ItemCheck(ReferenceEquals(equipment[slot].Item, before[slot]) && !equipment[slot].Locked, reason + "/" + slot);
    }
    private static void EquipmentGestures(GameScene scene, CConnection connection, ItemType[] types, List<string> trace)
    {
        var bag = scene.InventoryBox.Grid.Grid;
        var equipment = scene.CharacterBox.Grid;
        var tabs = Field<DXTabControl>(scene.CharacterBox, "TabControl");
        var hiddenPage = Field<DXTab>(scene.CharacterBox, "StatsTab");
        foreach (bool twice in new[] { false, true })
        for (int test = 0; test < 17; test++)
        {
            // Torch shares Emblem; DarkStone remains an alternate type for Amulet.
            int slot = test == 16 ? (int)EquipmentSlot.Amulet : test == 3 ? (int)EquipmentSlot.Emblem : test;
            ItemType type = test == 16 ? ItemType.DarkStone : test == 3 ? ItemType.Torch : types[slot];
            string id = (twice ? "double" : "right") + "/" + type + "/" + (EquipmentSlot)slot;
            int pairedLeft = slot == (int)EquipmentSlot.BraceletR ? (int)EquipmentSlot.BraceletL :
                slot == (int)EquipmentSlot.RingR ? (int)EquipmentSlot.RingL : -1;
            if (pairedLeft >= 0) equipment[pairedLeft].Item = MakeItem(types[pairedLeft], 4100 + pairedLeft);
            var before = equipment.Select(c => c.Item).ToArray();
            var item = MakeItem(type, 4200 + test); bag[0].Item = item;
            tabs.SelectedTab = hiddenPage;
            // Automatic wear must work while its target page is hidden, without changing tabs.
            foreach (bool success in new[] { false, true })
            {
                EquipmentGesture(scene, bag[0], twice);
                var packet = OnePacket<C.ItemMove>(connection, "automatic wear " + id + "/" + success);
                ItemCheck(packet.FromGrid == GridType.Inventory && packet.FromSlot == 0 && packet.ToGrid == GridType.Equipment && packet.ToSlot == slot && !packet.MergeItem,
                    "automatic wear exact grids/slots " + id);
                ItemCheck(bag[0].Locked && equipment[slot].Locked && ReferenceEquals(bag[0].Item, item) && equipment[slot].Item == null,
                    "automatic wear defers mutation " + id);
                ItemCheck(tabs.SelectedTab == hiddenPage && !equipment[slot].IsVisible, "automatic wear retains hidden target page " + id);
                EquipmentGesture(scene, bag[0], twice); NoPackets(connection, "pending automatic wear cannot duplicate " + id);
                trace.Add("automatic-wear|" + id + "|" + success + "|" + MoveTrace(packet));
                Reply(connection, packet, success);
                ItemCheck(!bag[0].Locked && !equipment[slot].Locked &&
                    (success ? bag[0].Item == null && ReferenceEquals(equipment[slot].Item, item) : ReferenceEquals(bag[0].Item, item) && equipment[slot].Item == null),
                    "automatic wear response identity/locks " + id + "/" + success);
                OtherEquipmentUnchanged(equipment, before, slot, "automatic wear retains other slots " + id);
                NoPackets(connection, "automatic wear response sends nothing " + id);
            }
            // Native equipment double-click keeps normal equipment in place; removal uses right-click.
            if (twice)
            {
                EquipmentGesture(scene, equipment[slot], true);
                NoPackets(connection, "equipment double-click preserves native no-remove behavior " + id);
                ItemCheck(ReferenceEquals(equipment[slot].Item, item) && bag[0].Item == null && !equipment[slot].Locked && !bag[0].Locked,
                    "equipment double-click preserves identity/locks " + id);
                trace.Add("equipped-double|" + id + "|" + (scene.SelectedCell == equipment[slot]));
                scene.SelectedCell = null;
            }
            foreach (bool success in new[] { false, true })
            {
                EquipmentGesture(scene, equipment[slot], false);
                var packet = OnePacket<C.ItemMove>(connection, "automatic remove " + id + "/" + success);
                ItemCheck(packet.FromGrid == GridType.Equipment && packet.FromSlot == slot && packet.ToGrid == GridType.Inventory && packet.ToSlot == 0 && !packet.MergeItem,
                    "automatic remove exact grids/slots " + id);
                ItemCheck(bag[0].Locked && equipment[slot].Locked && bag[0].Item == null && ReferenceEquals(equipment[slot].Item, item),
                    "automatic remove defers mutation " + id);
                tabs.SelectedTab = hiddenPage;
                trace.Add("automatic-remove|" + id + "|" + success + "|" + MoveTrace(packet));
                Reply(connection, packet, success);
                ItemCheck(!bag[0].Locked && !equipment[slot].Locked &&
                    (success ? ReferenceEquals(bag[0].Item, item) && equipment[slot].Item == null : bag[0].Item == null && ReferenceEquals(equipment[slot].Item, item)),
                    "automatic remove hidden-page response identity/locks " + id + "/" + success);
                OtherEquipmentUnchanged(equipment, before, slot, "automatic remove retains other slots " + id);
                NoPackets(connection, "automatic remove response sends nothing " + id);
            }
            bag[0].Item = null;
            if (pairedLeft >= 0) equipment[pairedLeft].Item = null;
        }
        // With both paired slots occupied, native automatic wear replaces the right slot and returns it to the source bag cell.
        foreach (bool twice in new[] { false, true })
        foreach (var pair in new[] { new[] { EquipmentSlot.BraceletL, EquipmentSlot.BraceletR }, new[] { EquipmentSlot.RingL, EquipmentSlot.RingR } })
        {
            int left = (int)pair[0], right = (int)pair[1];
            var leftItem = MakeItem(types[left], 4300 + left); var rightItem = MakeItem(types[right], 4300 + right);
            var replacement = MakeItem(types[left], 4400 + left);
            equipment[left].Item = leftItem; equipment[right].Item = rightItem; bag[0].Item = replacement;
            tabs.SelectedTab = hiddenPage; EquipmentGesture(scene, bag[0], twice);
            var packet = OnePacket<C.ItemMove>(connection, "full pair " + pair[0] + "/" + twice);
            ItemCheck(packet.FromGrid == GridType.Inventory && packet.FromSlot == 0 && packet.ToGrid == GridType.Equipment && packet.ToSlot == right && !packet.MergeItem,
                "full pair automatic replacement targets right " + pair[0] + "/" + twice);
            ItemCheck(ReferenceEquals(equipment[left].Item, leftItem) && ReferenceEquals(equipment[right].Item, rightItem) && ReferenceEquals(bag[0].Item, replacement),
                "full pair replacement defers identity changes");
            trace.Add("automatic-pair|" + pair[0] + "|" + twice + "|" + MoveTrace(packet));
            Reply(connection, packet, true);
            ItemCheck(ReferenceEquals(equipment[left].Item, leftItem) && ReferenceEquals(equipment[right].Item, replacement) && ReferenceEquals(bag[0].Item, rightItem) &&
                !equipment[left].Locked && !equipment[right].Locked && !bag[0].Locked, "full pair replacement preserves all identities");
            equipment[left].Item = equipment[right].Item = bag[0].Item = null;
            NoPackets(connection, "full pair response sends nothing");
        }
        scene.SelectedCell = null;
    }
    private static void Key(GameScene scene, Keys key)
    {
        DXTextBox.ActiveTextBox = null;
        Call(typeof(TargetForm), CEnvir.Target, "OnKeyDown", new KeyEventArgs(key));
        ItemCheck(!(bool)typeof(CEnvir).GetProperty("GameUiOperationsBlocked", Static).GetValue(null), "key did not fault startup session");
    }
    private static List<string> ExerciseItems(GameUiStartupSession owner, MapInfo map, string evidence, string style)
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
                scene.CharacterBox.Visible = scene.InventoryBox.Visible = true;
                if (owner.EffectiveStyle == GameUiStyle.Legacy)
                {
                    scene.CharacterBox.Location = new Point(0, 30); scene.InventoryBox.Location = new Point(330, 30);
                    scene.BeltBox.Size = scene.BeltBox.GetAcceptableResize(scene.BeltBox.GetSize(new Size(361, 36)));
                    scene.BeltBox.Location = new Point(330, 590);
                }
                scene.BeltBox.Visible = true;
                // Synthetic StartInformation has no persisted links; use explicit empty-link sentinels.
                foreach (var emptyLink in scene.BeltBox.Links) { emptyLink.LinkInfoIndex = -1; emptyLink.LinkItemIndex = -1; }
                var bag = scene.InventoryBox.Grid.Grid; var equipment = scene.CharacterBox.Grid;
                ItemCheck(bag.Length == 49 && equipment.Length == 16 && scene.BeltBox.Grid.Grid.Length == 10, "original 16/49/10 controls");
                var types = new[] { ItemType.Weapon, ItemType.Armour, ItemType.Helmet, ItemType.Torch, ItemType.Necklace, ItemType.Bracelet, ItemType.Bracelet, ItemType.Ring, ItemType.Ring, ItemType.Shoes, ItemType.Poison, ItemType.Amulet, ItemType.Flower, ItemType.HorseArmour, ItemType.Emblem, ItemType.Shield };
                // Forty-nine distinct identities also expose icon/count overlays swallowing cell clicks.
                for (int i = 0; i < bag.Length; i++) bag[i].Item = MakeItem(ItemType.Consumable, 1000 + i, 20);
                for (int i = 0; i < bag.Length; i++)
                {
                    ClickControl(scene, bag[i]); ItemCheck(scene.SelectedCell == bag[i], "filled bag selection " + i);
                    ClickControl(scene, bag[i]); ItemCheck(scene.SelectedCell == null, "filled bag deselection " + i);
                    NoPackets(connection, "bag selection is local " + i);
                }
                Call(typeof(TargetForm), CEnvir.Target, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, 600, 280, 0));
                Render(scene, Path.Combine(evidence, style + "-filled-bag.png"));
                foreach (var cell in bag) cell.Item = null;
                // Every original slot is tested through its displayed page and physical coordinates.
                for (int slot = 0; slot < equipment.Length; slot++)
                {
                    if (slot == (int)EquipmentSlot.Torch) continue;
                    var item = MakeItem(types[slot], 2000 + slot); bag[0].Item = item;
                    var packet = Move(scene, connection, bag[0], equipment[slot], trace);
                    // Reply while another character page is selected: hidden cells must still update.
                    Field<DXTabControl>(scene.CharacterBox, "TabControl").SelectedTab = Field<DXTab>(scene.CharacterBox, "StatsTab");
                    Reply(connection, packet, false);
                    ItemCheck(!bag[0].Locked && !equipment[slot].Locked && ReferenceEquals(bag[0].Item, item) && equipment[slot].Item == null, "rejected hidden-page move restores exact state " + slot);
                    packet = Move(scene, connection, bag[0], equipment[slot], trace);
                    Field<DXTabControl>(scene.CharacterBox, "TabControl").SelectedTab = Field<DXTab>(scene.CharacterBox, "StatsTab");
                    Reply(connection, packet, true);
                    ItemCheck(bag[0].Item == null && ReferenceEquals(equipment[slot].Item, item) && !equipment[slot].Locked && !bag[0].Locked, "successful hidden-page equip " + slot);
                    NoPackets(connection, "equip response no unexpected links");
                    packet = Move(scene, connection, equipment[slot], bag[48], trace);
                    Reply(connection, packet, true);
                    ItemCheck(equipment[slot].Item == null && ReferenceEquals(bag[48].Item, item) && !equipment[slot].Locked && !bag[48].Locked, "unequip to last bag cell " + slot);
                    bag[48].Item = null;
                }
                // Replacing worn equipment swaps the original instance back into the source bag slot.
                equipment[0].Item = MakeItem(ItemType.Weapon, 2800); bag[48].Item = MakeItem(ItemType.Weapon, 2801);
                var swap = Move(scene, connection, bag[48], equipment[0], trace); Reply(connection, swap, true);
                ItemCheck(bag[48].Item.Index == 2800 && equipment[0].Item.Index == 2801, "occupied equipment swap preserves both identities");
                equipment[0].Item = bag[48].Item = null;
                // Invalid type and unmet requirements must not issue a wear request.
                bag[0].Item = MakeItem(ItemType.Weapon, 2802);
                ClickControl(scene, bag[0]); ClickControl(scene, equipment[7]);
                NoPackets(connection, "wrong equipment type sends nothing");
                ItemCheck(scene.SelectedCell == bag[0] && !bag[0].Locked && !equipment[7].Locked, "wrong type keeps selection and unlock state");
                scene.SelectedCell = null; bag[0].Item.Info.RequiredAmount = 99;
                ClickControl(scene, bag[0]); ClickControl(scene, equipment[0]);
                NoPackets(connection, "unmet level requirement sends nothing");
                ItemCheck(!bag[0].Locked && !equipment[0].Locked && equipment[0].Item == null, "unmet level cannot mutate equipment");
                scene.SelectedCell = null; bag[0].Item = null;
                // First/last bag cells: rejection, empty move, occupied swap, partial and full stack merge.
                bag[0].Item = MakeItem(ItemType.Consumable, 2900, 20);
                var bagMove = Move(scene, connection, bag[0], bag[48], trace); Reply(connection, bagMove, false);
                ItemCheck(bag[0].Item.Count == 20 && bag[48].Item == null && !bag[0].Locked && !bag[48].Locked, "bag move refusal preserves state");
                bagMove = Move(scene, connection, bag[0], bag[48], trace); Reply(connection, bagMove, true);
                ItemCheck(bag[0].Item == null && bag[48].Item.Index == 2900, "bag move first to last");
                bag[0].Item = MakeItem(ItemType.Consumable, 2901, 20);
                bagMove = Move(scene, connection, bag[48], bag[0], trace); Reply(connection, bagMove, true);
                ItemCheck(!bagMove.MergeItem && bag[0].Item.Index == 2900 && bag[48].Item.Index == 2901, "bag occupied swap keeps identities");
                var sharedInfo = bag[0].Item.Info; bag[0].Item.Count = 80;
                bag[48].Item = MakeItem(ItemType.Consumable, 2902, 30, sharedInfo);
                bagMove = Move(scene, connection, bag[48], bag[0], trace); Reply(connection, bagMove, true);
                ItemCheck(bagMove.MergeItem && bag[0].Item.Count == 100 && bag[48].Item.Count == 10, "partial merge conserves count at capacity");
                bag[0].Item.Count = 80;
                bagMove = Move(scene, connection, bag[48], bag[0], trace); Reply(connection, bagMove, true);
                ItemCheck(bagMove.MergeItem && bag[0].Item.Count == 90 && bag[48].Item == null, "full merge conserves count and empties source");
                bag[0].Item = null;
                // Same kind, distinct identity: neither paired slot may alias or swap its counterpart.
                foreach (int slot in new[] { 5, 6, 7, 8 }) equipment[slot].Item = MakeItem(types[slot], 3000 + slot);
                foreach (int slot in new[] { 6, 5, 8, 7 })
                {
                    var paired = equipment.Select(c => c.Item).ToArray();
                    var packet = Move(scene, connection, equipment[slot], bag[48], trace); Reply(connection, packet, true);
                    ItemCheck(bag[48].Item.Index == 3000 + slot && equipment[slot].Item == null, "paired slot exact identity " + slot);
                    for (int j = 0; j < equipment.Length; j++) if (j != slot) ItemCheck(ReferenceEquals(equipment[j].Item, paired[j]), "other equipment unchanged " + slot + "/" + j);
                    bag[48].Item = null;
                }
                EquipmentGestures(scene, connection, types, trace);
                // Native amount popup remains unskinned; cancel emits nothing, confirm waits for response.
                bag[0].Item = MakeItem(ItemType.Consumable, 5000, 20);
                foreach (bool confirm in new[] { false, true })
                {
                    CEnvir.Shift = true; try { ClickControl(scene, bag[0]); } finally { CEnvir.Shift = false; }
                    var popup = DXControl.MessageBoxList.OfType<DXItemAmountWindow>().Single(w => !w.IsDisposed && w.IsVisible);
                    ItemCheck(popup.WindowSkin == null, "secondary split popup remains original"); NoPackets(connection, "opening split sends nothing");
                    if (!confirm) { ClickControl(scene, popup.CloseButton); NoPackets(connection, "cancel split sends nothing"); continue; }
                    popup.AmountBox.Value = 7; ClickControl(scene, popup.ConfirmButton);
                    var split = OnePacket<C.ItemSplit>(connection); trace.Add("split|" + split.Grid + "|" + split.Slot + "|" + split.Count);
                    ItemCheck(split.Grid == GridType.Inventory && split.Slot == 0 && split.Count == 7 && bag[0].Locked && bag[0].Item.Count == 20, "confirmed split exact count, deferred mutation");
                    connection.Process(new S.ItemSplit { Grid = split.Grid, Slot = 0, Count = 7, NewSlot = 48, Success = false });
                    ItemCheck(!bag[0].Locked && bag[0].Item.Count == 20 && bag[48].Item == null, "split refusal unlocks without mutation");
                    CEnvir.Shift = true; try { ClickControl(scene, bag[0]); } finally { CEnvir.Shift = false; }
                    popup = DXControl.MessageBoxList.OfType<DXItemAmountWindow>().Single(w => !w.IsDisposed && w.IsVisible);
                    popup.AmountBox.Value = 7; ClickControl(scene, popup.ConfirmButton);
                    split = OnePacket<C.ItemSplit>(connection);
                    ItemCheck(split.Grid == GridType.Inventory && split.Slot == 0 && split.Count == 7 && bag[0].Locked, "split retry sends exact request after refusal");
                    trace.Add("split-retry|" + split.Grid + "|" + split.Slot + "|" + split.Count);
                    connection.Process(new S.ItemSplit { Grid = split.Grid, Slot = 0, Count = 7, NewSlot = 48, Success = true });
                    ItemCheck(!bag[0].Locked && bag[0].Item.Count == 13 && bag[48].Item.Count == 7 && bag[0].Item.Info == bag[48].Item.Info, "split accepted response preserves total and info");
                    bag[0].Item = bag[48].Item = null;
                }
                DXTextBox.ActiveTextBox = null;
                // Ten real default key mappings and ten physical belt cell targets, before and after reset.
                for (int i = 0; i < 10; i++) bag[i].Item = MakeItem(ItemType.Consumable, 6000 + i, 20);
                for (int i = 0; i < 10; i++)
                {
                    ClickControl(scene, bag[i]); ClickControl(scene, scene.BeltBox.Grid.Grid[i]);
                    var link = OnePacket<C.BeltLinkChanged>(connection);
                    ItemCheck(link.Slot == i && link.LinkIndex == bag[i].Item.Info.Index && link.LinkItemIndex == -1 && scene.SelectedCell == null, "belt exact info link " + i);
                    trace.Add("link|" + i + "|bag|" + i); // Info DB indexes vary between isolated scene fixtures.
                }
                var equipBefore = equipment.ToArray(); var beltBefore = scene.BeltBox.Grid.Grid.ToArray(); var linksBefore = scene.BeltBox.Links;
                foreach (bool resize in new[] { false, true })
                {
                    if (resize)
                    {
                        Reset(new Size(1280, 800)); Render(scene, null); Reset(new Size(1024, 768)); Render(scene, null);
                        ItemCheck(bag.SequenceEqual(scene.InventoryBox.Grid.Grid) && equipBefore.SequenceEqual(scene.CharacterBox.Grid) && beltBefore.SequenceEqual(scene.BeltBox.Grid.Grid) && ReferenceEquals(linksBefore, scene.BeltBox.Links), "reset retains cells and links");
                    }
                    for (int i = 0; i < 10; i++)
                    {
                        scene.UseItemTime = DateTime.MinValue; CEnvir.Now = DateTime.UtcNow;
                        Key(scene, i == 9 ? Keys.D0 : (Keys)((int)Keys.D1 + i));
                        var use = OnePacket<C.ItemUse>(connection);
                        ItemCheck(use.Link.GridType == GridType.Inventory && use.Link.Slot == i && use.Link.Count == 1 && bag[i].Locked, "belt keyboard exact source " + i);
                        trace.Add("use|" + resize + "|" + i + "|" + use.Link.GridType + "|" + use.Link.Slot + "|" + use.Link.Count);
                        // No server item-use acceptance is claimed; unlock only to exercise the next gesture.
                        bag[i].Locked = false; scene.UseItemTime = DateTime.MinValue;
                        ClickControl(scene, scene.BeltBox.Grid.Grid[i], MouseButtons.Right);
                        use = OnePacket<C.ItemUse>(connection);
                        ItemCheck(use.Link.GridType == GridType.Inventory && use.Link.Slot == i && use.Link.Count == 1, "belt mouse exact source " + i);
                        trace.Add("mouse-use|" + resize + "|" + i + "|" + use.Link.Slot); bag[i].Locked = false;
                    }
                }
                foreach (var cell in bag) cell.Item = null;
                for (int i = 0; i < 16; i++) if (i != (int)EquipmentSlot.Torch) equipment[i].Item = MakeItem(types[i], 7000 + i);
                Call(typeof(TargetForm), CEnvir.Target, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, 600, 280, 0));
                Field<DXTabControl>(scene.CharacterBox, "TabControl").SelectedTab = (DXTab)equipment[0].Parent;
                Render(scene, Path.Combine(evidence, style + "-filled-equipment.png"));
                Field<DXTabControl>(scene.CharacterBox, "TabControl").SelectedTab = (DXTab)equipment[15].Parent;
                Render(scene, Path.Combine(evidence, style + "-filled-extra.png"));
                NoPackets(connection, "rendering and resets send no requests");
                File.WriteAllLines(Path.Combine(evidence, style + "-packet-trace.txt"), trace);
            }
            finally { CEnvir.Connection = null; CEnvir.Shift = CEnvir.Alt = CEnvir.Ctrl = false; DXTextBox.ActiveTextBox = null; }
        }
        DXControl.ActiveScene = null;
        ItemCheck(GameScene.Game == null && Client.Scenes.Views.ChatTab.Tabs.Count == 0, "scene teardown clears original controls");
        return trace;
    }
    internal static int RunItemInteractions(string[] args)
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
                    { Publish(legacy); baseline = ExerciseItems(legacy, map, evidence, "legacy"); Publish(null); }
                    using (var official = (GameUiStartupSession)typeof(GameUiStartupSession).GetMethod("Prepare", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { "OfficialV1", Environment.CurrentDirectory, true }))
                    {
                        ItemCheck(official.EffectiveStyle == GameUiStyle.OfficialV1, "test-only package accepted"); Publish(official);
                        var actual = ExerciseItems(official, map, evidence, "official");
                        ItemCheck(baseline.SequenceEqual(actual), "Legacy/Official ordered semantic packet traces identical");
                        actual = ExerciseItems(official, map, evidence, "official-reentry");
                        ItemCheck(baseline.SequenceEqual(actual), "reentry ordered semantic packet traces identical"); Publish(null);
                    }
                }
                finally { CEnvir.Connection = null; Publish(null); DXManager.Unload(); }
            }
            ItemCheck(!GameUiOfficialV1Catalog.RuntimeReady, "production readiness remains false");
            File.WriteAllLines(Path.Combine(evidence, "coordinate-hits.txt"), hits);
            File.WriteAllLines(Path.Combine(evidence, "assertions.txt"), interactionChecks);
            File.WriteAllText(Path.Combine(evidence, "metrics.json"), "{\"checks\":" + checks + ",\"scenes\":" + scenes + ",\"presentedFrames\":" + renders + ",\"deviceResets\":" + resets + ",\"coordinateHits\":" + (hits.Count-1) + ",\"fixtureImage\":" + fixtureImage + ",\"serverConnected\":false,\"releaseAccepted\":false}");
            Console.WriteLine("Filled-item offline integration: " + checks + " checks passed; " + scenes + " real scenes, " + (hits.Count-1) + " coordinate hits, " + renders + " presented frames, " + resets + " device resets. 15 active equip/unequip slots and retired Torch index, 49 filled bag cells, 17 automatic-wear cases per gesture, full-pair replacement, hidden-page success/refusal, right-click remove and native double-click no-remove, split cancel/confirm, 10 belt links/keys/mouse uses. Legacy/Official/reentry traces identical. Socket-free queue and synthetic response injection; no live server or IME acceptance.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { CEnvir.Connection = null; foreach (var library in CEnvir.LibraryList.Values) library.Dispose(); CEnvir.LibraryList.Clear(); CEnvir.Target = null; }
    }
}
