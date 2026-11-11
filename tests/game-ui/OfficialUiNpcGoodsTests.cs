using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
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
    private static void ExerciseNpcGoods(GameScene scene, CConnection connection, bool official, string evidence, string style, List<string> trace)
    {
        var page = NpcPage("原交互卡片保持样式\n[离开:0]", NPCDialogType.BuySell);
        var goods = new List<NPCGood>();
        for (int i = 0; i < 27; i++)
        {
            var item = Globals.ItemInfoList.CreateNewObject(); item.ItemName = "完整商品名称展示测试" + i;
            item.ItemType = ItemType.Consumable; item.Price = 10 + i; item.StackSize = i == 0 ? 20 : 1; item.Weight = 1;
            var good = CEnvir.Session.GetCollection<NPCGood>().CreateNewObject(); good.Item = item; good.Page = page; goods.Add(good);
        }
        scene.User.Stats[Stat.BagWeight] = 1000;
        NpcReply(scene, connection, page);
        var box = scene.NPCGoodsBox;
        var scroll = (DXVScrollBar)typeof(NPCGoodsDialog).GetField("ScrollBar", Instance).GetValue(box);
        var buy = (DXButton)typeof(NPCGoodsDialog).GetField("BuyButton", Instance).GetValue(box);
        var cells = box.Cells.ToArray();
        NpcCheck(box.UsesGameUiGoods == official && box.Visible && cells.Length == 27, "goods adapter follows style and retains server collection");
        NpcCheck(cells.All(c => c.ItemCell.Size == new Size(36,36) && c.ItemCell.ReadOnly && c.ItemCell.Slot == 0), "all original 36x36 readonly goods item cells preserved");
        NpcCheck(cells.Select(c => c.Good).SequenceEqual(goods), "goods identities and order unchanged");
        if (official)
        {
            NpcCheck(box.Size == new Size(344,438) && box.ClientPanel.Location == new Point(24,48) && box.ClientPanel.Size == new Size(280,324), "verified shop frame and clipped list bounds");
            NpcCheck(cells[0].Location == new Point(0,2) && cells[1].Location == new Point(140,2) && cells[2].Location == new Point(0,48), "two columns preserve row-major goods order");
            NpcCheck(cells.All(c => c.Size == new Size(138,45) && !c.ItemNameLabel.AutoSize && c.ItemNameLabel.Hint == c.Good.Item.ItemName), "long names use bounded text and complete hints");
        }
        box.BringToFront();
        if (official)
        {
            NpcCheck(cells.Count(c => box.ClientPanel.DisplayArea.Contains(c.DisplayArea)) == 14, "exactly seven complete two-column goods rows are visible");
            NpcCheck(cells.Skip(14).All(c => !box.ClientPanel.DisplayArea.IntersectsWith(c.DisplayArea)), "eighth row is clipped with no partial item or text");
            foreach (var cell in cells.Take(14))
            {
                var labels = new[] { cell.ItemNameLabel, cell.RequirementLabel, cell.CostLabel };
                NpcCheck(labels.All(label => label.Size.Height >= DXLabel.GetSize(label.Text, label.Font, label.Outline).Height), "original outlined fonts fit each compact text line without vertical clipping");
                NpcCheck(labels.All(label => cell.DisplayArea.Contains(label.DisplayArea)) && cell.DisplayArea.Contains(cell.ItemCell.DisplayArea) && cell.DisplayArea.Contains(cell.GoldIcon.DisplayArea), "item, all three text lines and currency icon stay within each row");
            }
            var labelPoint = cells[0].ItemNameLabel.DisplayArea.Location; labelPoint.Offset(2,2);
            NpcMove(labelPoint);
            NpcCheck(DXControl.MouseControl == cells[0] && cells[0].Hint.Contains(cells[0].ItemNameLabel.Text) && cells[0].Hint.Contains(cells[0].RequirementLabel.Text) && cells[0].Hint.Contains(cells[0].CostLabel.Text), "actual text hover exposes complete name, requirements and price through the original row target");
            var normalCost = cells[0].CostLabel.Text;
            cells[0].CostLabel.Text = "9,223,372,036,854,775,807";
            NpcCheck(cells[0].Hint.Contains(cells[0].CostLabel.Text), "long numeric price updates the reachable full hint without changing purchase state");
            cells[0].CostLabel.Text = normalCost;
            NpcClick(scene, cells[13], DateTime.UtcNow);
            NpcCheck(box.SelectedCell == cells[13] && buy.Enabled && connection.SendList.IsEmpty, "seventh-row last commodity is selectable through real coordinates");
        }
        NpcClick(scene, cells[0], DateTime.UtcNow);
        NpcCheck(box.SelectedCell == cells[0] && buy.Enabled && connection.SendList.IsEmpty, "selection enables original purchase button without sending");
        NpcMove(new Point(box.DisplayArea.Left + 160, box.DisplayArea.Top + 15));
        Render(scene, Path.Combine(evidence, style + "-goods-top.png"));
        NpcClick(scene, buy, DateTime.UtcNow);
        var amount = ((List<DXControl>)typeof(DXControl).GetProperty("Controls", Instance).GetValue(scene)).OfType<DXItemAmountWindow>().Single();
        NpcCheck(amount.WindowSkin == null && amount.AmountBox.MaxValue == 20 && connection.SendList.IsEmpty, "stackable buy opens original unskinned quantity window with original limit");
        trace.Add("quantity|" + amount.Size + "|" + amount.ClientArea + "|" + amount.AmountBox.Location + "|" + amount.AmountBox.Size + "|" + amount.ConfirmButton.Location + "|" + amount.ConfirmButton.Size);
        amount.AmountBox.Value = 3;
        Render(scene, Path.Combine(evidence, style + "-goods-quantity.png"));
        NpcClick(scene, amount.ConfirmButton, DateTime.UtcNow);
        var packet = NpcPacket<C.NPCBuy>(connection, trace);
        NpcCheck(packet.Index == goods[0].Index && packet.Amount == 3 && !packet.GuildFunds && amount.IsDisposed, "quantity confirmation sends exactly selected original good/amount/currency once");
        NpcClick(scene, cells[1], DateTime.UtcNow);
        NpcClick(scene, buy, DateTime.UtcNow);
        packet = NpcPacket<C.NPCBuy>(connection, trace);
        NpcCheck(packet.Index == goods[1].Index && packet.Amount == 1 && !packet.GuildFunds, "nonstackable original buy sends one correct packet");
        // TargetForm routes a second click through the original DXScene double-click path.
        scene.ClickControl = cells[1]; scene.ClickTime = CEnvir.Now = Time.Now; scene.Buttons = MouseButtons.Left;
        var doublePoint = new Point(cells[1].DisplayArea.Right - 10, cells[1].DisplayArea.Top + 10);
        NpcMove(doublePoint);
        Call(typeof(TargetForm), CEnvir.Target, "OnMouseDown", new MouseEventArgs(MouseButtons.Left,2,doublePoint.X,doublePoint.Y,0));
        Call(typeof(TargetForm), CEnvir.Target, "OnMouseDoubleClick", new MouseEventArgs(MouseButtons.Left,2,doublePoint.X,doublePoint.Y,0));
        Call(typeof(TargetForm), CEnvir.Target, "OnMouseUp", new MouseEventArgs(MouseButtons.Left,2,doublePoint.X,doublePoint.Y,0));
        packet = NpcPacket<C.NPCBuy>(connection, trace);
        NpcCheck(packet.Index == goods[1].Index && packet.Amount == 1 && !packet.GuildFunds, "real double-click preserves original single-packet purchase");
        box.GuildCheckBox.Checked = true;
        NpcClick(scene, buy, DateTime.UtcNow);
        packet = NpcPacket<C.NPCBuy>(connection, trace);
        NpcCheck(packet.Index == goods[1].Index && packet.GuildFunds && !box.GuildCheckBox.Checked, "guild currency flag and original post-buy reset preserved");
        NpcMove(new Point(cells[1].DisplayArea.Right - 10, cells[1].DisplayArea.Top + 10));
        NpcCheck(DXControl.MouseControl == cells[1], "wheel target hits original cell");
        Call(typeof(TargetForm), CEnvir.Target, "OnMouseWheel", new MouseEventArgs(MouseButtons.None,0,CEnvir.MouseLocation.X,CEnvir.MouseLocation.Y,-120));
        NpcCheck(scroll.Value == scroll.Change && connection.SendList.IsEmpty, "real wheel advances one configured step without purchase");
        scroll.Value = int.MaxValue;
        NpcCheck(scroll.Value == scroll.MaxValue - scroll.VisibleSize && box.ClientPanel.DisplayArea.Contains(cells.Last().DisplayArea), "scroll clamps exactly at final goods row");
        Render(scene, Path.Combine(evidence, style + "-goods-bottom.png"));
        foreach (var resolution in new[] { new Size(1366,768), new Size(1024,768) })
        {
            var selection = box.SelectedCell; int value = scroll.Value;
            Reset(resolution); scene.Size = resolution;
            NpcCheck(box.Cells.SequenceEqual(cells) && box.SelectedCell == selection && scroll.Value == value && connection.SendList.IsEmpty, "D3D reset retains goods selection/cells/scroll without packets");
            if (official) NpcCheck(box.Size == new Size(344,438), "candidate shop size remains fixed through device reset");
        }
        scroll.Value = -1000;
        NpcCheck(scroll.Value == 0 && cells[0].Location.Y >= 0, "negative scroll clamps at first row");
        var skin = box.WindowSkin;
        var replacement = new[] { goods[2], goods[3], goods[4] };
        box.NewGoods(replacement);
        NpcCheck(cells.All(c => c.IsDisposed) && box.SelectedCell == null && !buy.Enabled && scroll.Value == 0 && connection.SendList.IsEmpty, "refresh disposes old cells, clears stale selection and resets scroll without packets");
        NpcClick(scene, box.Cells[0], DateTime.UtcNow);
        if (official)
        {
            var live = box.Cells.ToArray(); var selected = box.SelectedCell; var item = live[0].ItemCell.Item;
            box.UseGameUiGoods(null);
            NpcCheck(box.WindowSkin == null && !box.UsesGameUiGoods && box.Cells.SequenceEqual(live) && box.SelectedCell == selected && live[0].ItemCell.Item == item, "Legacy restoration retains refreshed cell/item/selection identities");
            NpcCheck(live.All(c => c.Size == new Size(219,40) && c.ItemNameLabel.AutoSize && c.ItemNameLabel.Hint == null && c.Hint == null) && live[1].Location == new Point(1,44), "Legacy restores current goods single-column geometry and text policy");
            NpcCheck(box.ClientArea.Width == 227 && box.ClientArea.Height == replacement.Length * 43 - 1, "Legacy height follows current refreshed collection");
            box.UseGameUiGoods(skin);
            NpcCheck(box.Cells.SequenceEqual(live) && box.SelectedCell == selected && live[1].Location == new Point(140,2), "candidate reinstall preserves live state without rebuilding goods");
        }
        box.NewGoods(new NPCGood[0]);
        NpcCheck(box.SelectedCell == null && !buy.Enabled && box.Cells.Count == 0 && scroll.Value == 0, "empty goods refresh has no stale purchasable item");
        if (official) NpcCheck(box.Size == new Size(344,438) && scroll.MaxValue == scroll.VisibleSize, "empty candidate list retains valid nonnegative viewport");
        box.Buy(); NpcCheck(connection.SendList.IsEmpty, "buy after empty refresh sends nothing");
        box.NewGoods(new[] { goods[1] });
        NpcClick(scene, box.Cells[0], DateTime.UtcNow);
        scene.Observer = true; box.Buy();
        NpcCheck(connection.SendList.IsEmpty, "observer goods buy remains blocked");
        scene.Observer = false;
        NpcClick(scene, scene.NPCBox.CloseButton, DateTime.UtcNow); NpcPacket<C.NPCClose>(connection, trace);
        NpcCheck(!box.Visible && !scene.NPCBox.Visible, "card close hides adapted goods using original lifecycle");
    }
}
