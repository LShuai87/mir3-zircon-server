using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
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

internal static partial class OfficialUiFullSceneTests
{
    private static List<DXControl> ServiceChildren(DXControl control) =>
        (List<DXControl>)typeof(DXControl).GetProperty("Controls", Instance).GetValue(control);
    private static DXButton ServiceButton(DXWindow window, string text) =>
        ServiceChildren(window).OfType<DXButton>().Single(b => b.Label != null && b.Label.Text == text);
    private static ClientUserItem ServiceItem(int index, long count = 1)
    {
        var info = Globals.ItemInfoList.CreateNewObject();
        info.ItemName = "服务测试装备" + index; info.ItemType = ItemType.Weapon;
        info.Price = 1000; info.Durability = 1000; info.StackSize = count > 1 ? 10 : 1;
        info.RequiredClass = RequiredClass.All; info.RequiredGender = RequiredGender.None;
        return new ClientUserItem(info, count) { Index = 7000 + index, CurrentDurability = 500, MaxDurability = 1000 };
    }
    private static void CheckServiceGrid(GameScene scene, DXWindow window, DXItemGrid grid, Size capacity)
    {
        NpcCheck(grid.GridSize == capacity && grid.Grid.Length == capacity.Width * capacity.Height, "complete original service capacity " + capacity);
        NpcCheck(window.ClientArea.Contains(new Rectangle(grid.Location, grid.Size)), "whole service grid lies inside content region");
        window.BringToFront();
        foreach (var cell in grid.Grid)
        {
            NpcCheck(cell.Size == new Size(36,36) && cell.GridType == grid.GridType && cell.Parent == grid && cell.Linked, "original linked cell geometry and identity " + cell.Slot);
            NpcCheck(window.DisplayArea.Contains(cell.DisplayArea) && scene.DisplayArea.Contains(cell.DisplayArea), "complete cell within window and 1024x768 viewport " + cell.Slot);
            NpcClick(scene, cell, DateTime.UtcNow);
        }
    }
    private static void CheckLinks(CConnection connection, IEnumerable<CellLinkInfo> links, DXItemCell[] sources, List<string> trace, string service)
    {
        var values = links.ToArray();
        NpcCheck(values.Length == sources.Length, "packet retains every original service link: " + service);
        for (int i = 0; i < sources.Length; i++)
            NpcCheck(values[i].GridType == sources[i].GridType && values[i].Slot == sources[i].Slot && values[i].Count == sources[i].Item.Count,
                "exact source grid, slot and quantity " + service + " " + i);
        NpcCheck(sources.All(c => c.Locked && c.Link == null), "original submit locks and unlinks source cells: " + service);
        trace.Add(service + "|" + string.Join(";", values.Select(v => v.GridType + ":" + v.Slot + ":" + v.Count)));
    }
    private static void ExerciseNpcItemServices(GameScene scene, CConnection connection, bool official, string evidence, string style, List<string> trace)
    {
        var sell = scene.NPCSellBox; var repair = scene.NPCRepairBox;
        var sellGrid = sell.Grid; var repairGrid = repair.Grid;
        var sellCells = sellGrid.Grid.ToArray(); var repairCells = repairGrid.Grid.ToArray();
        var sellPage = NpcPage("出售服务\n[离开:0]", NPCDialogType.BuySell);
        var saleType = CEnvir.Session.GetCollection<NPCType>().CreateNewObject(); saleType.ItemType = ItemType.Weapon; saleType.Page = sellPage;
        NpcReply(scene, connection, sellPage);
        CheckServiceGrid(scene, sell, sellGrid, new Size(7,7));
        NpcCheck(sell.WindowSkin == null && sell.ClientArea.Size == new Size(246,296), "deferred sale window retains its native frame and client size");
        var sources = scene.InventoryBox.Grid.Grid;
        for (int i = 0; i < 49; i++) sources[i].Item = ServiceItem(i, i == 0 ? 3 : 1);
        sources[0].MoveItem(sellCells.Last());
        var amount = ServiceChildren(scene).OfType<DXItemAmountWindow>().Single();
        NpcCheck(amount.WindowSkin == null && amount.AmountBox.Value == 3 && amount.AmountBox.MaxValue == 3, "sale keeps the original quantity popup, default and limit");
        trace.Add("sale-quantity|" + amount.Size + "|" + amount.ClientArea + "|" + amount.AmountBox.Location + "|" + amount.ConfirmButton.Location);
        amount.AmountBox.Value = 2;
        NpcClick(scene, amount.ConfirmButton, DateTime.UtcNow);
        NpcCheck(sellCells.Last().Link == sources[0] && sellCells.Last().LinkedCount == 2 && connection.SendList.IsEmpty, "original quantity confirmation links exact last sale slot without request");
        sellGrid.ClearLinks();
        NpcClick(scene, ServiceButton(sell, "全选"), DateTime.UtcNow);
        NpcCheck(sellCells.All(c => c.Link != null) && sellCells[0].LinkedCount == 3 && connection.SendList.IsEmpty, "original select-all fills all 49 slots and preserves stack count without request");
        NpcCheck(sell.GoldLabel.Text == sources.Sum(c => c.Item.Price(c.Item.Count)).ToString("#,##0") && sell.SellButton.Enabled, "original total and sell eligibility remain live");
        NpcMove(new Point(sell.DisplayArea.Left + 140, sell.DisplayArea.Top + 15));
        Render(scene, Path.Combine(evidence, style + "-sell-full.png"));
        scene.Observer = true;
        NpcClick(scene, sell.SellButton, DateTime.UtcNow);
        NpcCheck(connection.SendList.IsEmpty && sellCells.All(c => c.Link != null), "observer cannot submit sale or consume links");
        scene.Observer = false;
        NpcClick(scene, sell.SellButton, DateTime.UtcNow);
        CheckLinks(connection, NpcPacket<C.NPCSell>(connection, trace).Links, sources, trace, "sell");
        NpcCheck(sellCells.All(c => c.Link == null) && !sell.SellButton.Enabled && sell.GoldLabel.Text == "0", "sale resets original cost and links");
        foreach (var cell in sources) cell.Locked = false;
        NpcClick(scene, ServiceButton(sell, "全选"), DateTime.UtcNow);
        NpcClick(scene, sell.CloseButton, DateTime.UtcNow);
        NpcCheck(!sell.Visible && sellCells.All(c => c.Link == null) && connection.SendList.IsEmpty, "original service close clears links without closing NPC or sending request");
        NpcClick(scene, scene.NPCBox.CloseButton, DateTime.UtcNow); NpcPacket<C.NPCClose>(connection, trace);

        var repairPage = NpcPage("修理服务\n[离开:0]", NPCDialogType.Repair);
        var repairType = CEnvir.Session.GetCollection<NPCType>().CreateNewObject(); repairType.ItemType = ItemType.Weapon; repairType.Page = repairPage;
        bool oldSpecial = repair.SpecialCheckBox.Checked;
        repair.SpecialCheckBox.Checked = false;
        NpcReply(scene, connection, repairPage);
        CheckServiceGrid(scene, repair, repairGrid, new Size(14,5));
        NpcCheck(repair.WindowSkin == null && repair.ClientArea.Size == new Size(491,246), "deferred repair window retains its native frame and fourteen columns");
        if (scene.StorageBox.Grid.ItemGrid == null) scene.StorageBox.Grid.ItemGrid = new ClientUserItem[1000];
        scene.StorageSize = 130;
        var stored = scene.StorageBox.Grid.Grid.Take(21).ToArray();
        NpcCheck(stored.Length == 21, "storage fixture provides remaining 21 original sources");
        for (int i = 0; i < stored.Length; i++) stored[i].Item = ServiceItem(49 + i);
        NpcClick(scene, ServiceButton(repair, "背包"), DateTime.UtcNow);
        NpcClick(scene, ServiceButton(repair, "仓库"), DateTime.UtcNow);
        var repairSources = sources.Concat(stored).ToArray();
        NpcCheck(repairCells.All(c => c.Link != null) && connection.SendList.IsEmpty, "original inventory/storage actions fill all seventy repair slots without requests");
        int cost = repairSources.Sum(c => c.Item.RepairCost(false));
        NpcCheck(repair.GoldLabel.Text == cost.ToString("#,##0") && repair.RepairButton.Enabled, "normal repair cost uses original calculation");
        NpcClick(scene, repair.SpecialCheckBox, DateTime.UtcNow);
        NpcCheck(repair.SpecialCheckBox.Checked && Config.SpecialRepair && repair.GoldLabel.Text == repairSources.Sum(c => c.Item.RepairCost(true)).ToString("#,##0"), "original special repair checkbox persists config and recalculates");
        NpcMove(new Point(repair.DisplayArea.Left + 180, repair.DisplayArea.Top + 15));
        Render(scene, Path.Combine(evidence, style + "-repair-full.png"));
        foreach (var size in new[] { new Size(1366,768), new Size(1024,768) })
        {
            Reset(size); scene.Size = size;
            NpcCheck(sell.Grid == sellGrid && repair.Grid == repairGrid && sellGrid.Grid.SequenceEqual(sellCells) && repairGrid.Grid.SequenceEqual(repairCells), "reset retains all original service grids and cells");
            NpcCheck(repairCells.All(c => c.Link != null) && connection.SendList.IsEmpty, "reset retains all live repair links without requests");
        }
        repair.BringToFront(); scene.Observer = true;
        NpcClick(scene, repair.RepairButton, DateTime.UtcNow);
        NpcCheck(connection.SendList.IsEmpty && repairCells.All(c => c.Link != null), "observer cannot submit repair or consume links");
        scene.Observer = false;
        NpcClick(scene, repair.RepairButton, DateTime.UtcNow);
        var packet = NpcPacket<C.NPCRepair>(connection, trace);
        NpcCheck(packet.Special && !packet.GuildFunds && !repair.GuildCheckBox.Checked, "original repair flags and post-submit currency reset preserved");
        CheckLinks(connection, packet.Links, repairSources, trace, "repair");
        NpcCheck(repairCells.All(c => c.Link == null) && !repair.RepairButton.Enabled && repair.GoldLabel.Text == "0", "repair resets original cost and links");
        foreach (var cell in repairSources) cell.Locked = false;
        var equipment = scene.CharacterBox.Grid[(int)EquipmentSlot.Weapon];
        equipment.Item = ServiceItem(90);
        NpcClick(scene, ServiceButton(repair, "装备"), DateTime.UtcNow);
        NpcCheck(repairCells.Count(c => c.Link != null) == 1 && repairCells[0].Link == equipment, "original equipment action links original equipment source");
        NpcClick(scene, repair.RepairButton, DateTime.UtcNow);
        CheckLinks(connection, NpcPacket<C.NPCRepair>(connection, trace).Links, new[] { equipment }, trace, "equipment-repair");
        equipment.Locked = false; equipment.Item = null;
        NpcClick(scene, ServiceButton(repair, "背包"), DateTime.UtcNow);
        NpcClick(scene, scene.NPCBox.CloseButton, DateTime.UtcNow); NpcPacket<C.NPCClose>(connection, trace);
        NpcCheck(!repair.Visible && repairCells.All(c => c.Link == null), "NPC close clears all links in adapted repair window");
        repair.SpecialCheckBox.Checked = oldSpecial;
        scene.InventoryBox.Visible = scene.CharacterBox.Visible = scene.StorageBox.Visible = false;
        // Standalone styling candidates remain outside the scene installation scope.
        foreach (var cell in repairSources) cell.Item = null;
    }
    private static void CheckServiceRestoration(GameScene scene, GameUiWindowSkin skin)
    {
        foreach (DXWindow window in new DXWindow[] { new NPCSellDialog(), new NPCRepairDialog() })
        using (window)
        {
            window.Parent = scene; window.Visible = false;
            var sale = window as NPCSellDialog; var repair = window as NPCRepairDialog;
            var grid = sale != null ? sale.Grid : repair.Grid; var cells = grid.Grid.ToArray(); var original = window.Size; var client = window.ClientArea;
            var content = ServiceChildren(window).ToArray(); var positions = content.Select(c => c.Location).ToArray();
            var type = typeof(GameUiOfficialV1WindowSet).Assembly.GetType("Client.Envir.GameUiNpcItemServiceBinding", true);
            var binding = (IDisposable)Activator.CreateInstance(type, Instance, null, new object[] { window, grid, grid.GridSize }, null);
            Call(type, binding, "Install", skin);
            grid.Grid.Last().LinkedCount = 1; grid.Grid.Last().Link = scene.InventoryBox.Grid.Grid[0];
            NpcCheck(grid.Grid.SequenceEqual(cells), "standalone install reuses original cells: " + window.GetType().Name);
            bool fail = true;
            EventHandler<EventArgs> observer = (sender, e) => { if (fail) { fail = false; throw new InvalidOperationException("intentional restore observer"); } };
            window.SizeChanged += observer;
            try { binding.Dispose(); NpcCheck(false, "restore observer must surface"); }
            catch (InvalidOperationException error) { NpcCheck(error.Message == "intentional restore observer", "restore surfaces original observer failure"); }
            window.SizeChanged -= observer;
            binding.Dispose(); binding.Dispose();
            NpcCheck(window.WindowSkin == null && window.Size == original && window.ClientArea == client, "retry and repeated dispose restore complete native shell");
            NpcCheck(content.Select(c => c.Location).SequenceEqual(positions) && grid.Grid.SequenceEqual(cells), "detach restores all original child positions without replacement");
            NpcCheck(grid.Grid.Last().Link == scene.InventoryBox.Grid.Grid[0] && (sale != null ? sale.SellButton.Enabled : repair.RepairButton.Enabled), "detach preserves live original service links and eligibility");
            grid.ClearLinks();
        }
    }
}
