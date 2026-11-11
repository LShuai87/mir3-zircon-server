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
using C = Library.Network.ClientPackets;

internal static partial class OfficialUiFullSceneTests
{
    private static void ExerciseNpcRefineServices(GameScene scene, CConnection connection, bool official, string evidence, string style, List<string> trace)
    {
        var refine = scene.NPCRefineBox;
        NpcReply(scene, connection, NpcPage("精炼服务\n[离开:0]", NPCDialogType.Refine));
        var grids = new[] { refine.BlackIronGrid, refine.AccessoryGrid, refine.SpecialGrid };
        var cells = grids.SelectMany(g => g.Grid).ToArray();
        NpcCheck(grids.Select(g => g.Grid.Length).SequenceEqual(new[] { 5,3,1 }), "original refine material capacities 5/3/1");
        foreach (var grid in grids) CheckServiceGrid(scene, refine, grid, grid.GridSize);
        var sources = scene.InventoryBox.Grid.Grid.Take(9).ToArray();
        for (int i = 0; i < sources.Length; i++)
        {
            sources[i].Item = ServiceItem(200 + i, i == 8 ? 3 : 1);
            if (i < 5) sources[i].Item.Info.Effect = ItemEffect.BlackIronOre;
            else if (i < 8) sources[i].Item.Info.ItemType = ItemType.Ring;
            else { sources[i].Item.Info.ItemType = ItemType.RefineSpecial; sources[i].Item.Info.Shape = 1; }
            sources[i].MoveItem(i < 5 ? grids[0] : i < 8 ? grids[1] : grids[2], true);
            NpcCheck(cells[i].Link == sources[i], "original valid material move " + i);
        }
        var checkboxes = new[] { refine.DCCheckBox, refine.SPCheckBox, refine.FireCheckBox, refine.IceCheckBox,
            refine.LightningCheckBox, refine.WindCheckBox, refine.HolyCheckBox, refine.DarkCheckBox, refine.PhantomCheckBox };
        var types = new[] { RefineType.DC, RefineType.SpellPower, RefineType.Fire, RefineType.Ice, RefineType.Lightning, RefineType.Wind, RefineType.Holy, RefineType.Dark, RefineType.Phantom };
        for (int i = 0; i < types.Length; i++)
        {
            NpcClick(scene, checkboxes[i], DateTime.UtcNow);
            NpcCheck(refine.RefineType == types[i] && checkboxes.Count(c => c.Checked) == 1, "original exclusive refine type " + types[i]);
        }
        foreach (var quality in Globals.RefineTimes.Keys)
        {
            refine.RefineQualityBox.ListBox.SelectItem(quality);
            NpcCheck(refine.RefineQuality == quality && (quality == RefineQuality.Rush ? refine.DurationLabel.Text == "" : refine.DurationLabel.Text == Functions.ToString(Globals.RefineTimes[quality], false)), "original quality and duration " + quality);
        }
        refine.RefineQualityBox.ListBox.SelectItem(RefineQuality.Quick);
        refine.BringToFront();
        Render(scene, Path.Combine(evidence, style + "-refine.png"));
        scene.Observer = true;
        NpcClick(scene, refine.SubmitButton, DateTime.UtcNow);
        NpcCheck(connection.SendList.IsEmpty && cells.All(c => c.Link != null), "observer cannot refine or consume material links");
        scene.Observer = false;
        NpcClick(scene, refine.SubmitButton, DateTime.UtcNow);
        var packet = NpcPacket<C.NPCRefine>(connection, trace);
        NpcCheck(packet.RefineType == RefineType.Phantom && packet.RefineQuality == RefineQuality.Quick, "original refine request choices");
        var groups = new[] { packet.Ores, packet.Items, packet.Specials };
        for (int g = 0, offset = 0; g < groups.Length; offset += grids[g++].Grid.Length)
        {
            NpcCheck(groups[g].Count == grids[g].Grid.Length, "refine request retains material group capacity " + g);
            for (int i = 0; i < groups[g].Count; i++)
                NpcCheck(groups[g][i].GridType == sources[offset+i].GridType && groups[g][i].Slot == sources[offset+i].Slot && groups[g][i].Count == 1, "exact refine material request " + g + ":" + i);
        }
        trace.Add("refine|" + packet.RefineType + "|" + packet.RefineQuality + "|" + string.Join(";", groups.SelectMany(g => g).Select(l => l.GridType + ":" + l.Slot + ":" + l.Count)));
        NpcCheck(sources.All(c => c.Locked) && cells.All(c => c.Link == null), "original submit locks sources and clears material links");
        foreach (var cell in sources) cell.Locked = false;
        sources[0].MoveItem(grids[0], true);
        NpcClick(scene, refine.CloseButton, DateTime.UtcNow);
        NpcCheck(cells.All(c => c.Link == null) && !refine.Visible && connection.SendList.IsEmpty, "refine close clears original links without speculative request");
        NpcClick(scene, scene.NPCBox.CloseButton, DateTime.UtcNow); NpcPacket<C.NPCClose>(connection, trace);
        foreach (var cell in sources) cell.Item = null;

        var retrieve = scene.NPCRefineRetrieveBox;
        NpcReply(scene, connection, NpcPage("领取服务\n[离开:0]", NPCDialogType.RefineRetrieve));
        foreach (int count in new[] { 0,1,3,7,12 })
        {
            retrieve.Refines.Clear();
            retrieve.SelectedCell = null;
            for (int i = 0; i < count; i++) retrieve.Refines.Add(new ClientRefineInfo { Index = 800 + i,
                Weapon = ServiceItem(300+i), Type = RefineType.DC, Quality = RefineQuality.Quick,
                Chance = 20, MaxChance = 50, RetrieveTime = DateTime.UtcNow.AddMinutes(i == 0 ? -1 : 10) });
            retrieve.RefreshList();
            var scroll = ServiceChildren(retrieve).OfType<DXVScrollBar>().Single();
            var button = ServiceButton(retrieve, "取回");
            NpcCheck(retrieve.Cells.Count == count && retrieve.ClientArea.Height == Math.Min(Math.Max(127, count*43-2),298)+1, "original result viewport row boundary " + count);
            NpcCheck(retrieve.ClientPanel.Location == retrieve.ClientArea.Location && retrieve.ClientPanel.Size == retrieve.ClientArea.Size, "result panel tracks current client bounds");
            NpcCheck(retrieve.DisplayArea.Contains(button.DisplayArea) && button.Location.Y >= retrieve.ClientArea.Bottom, "retrieve action remains inside footer");
            if (count == 0) { NpcCheck(!button.Enabled && connection.SendList.IsEmpty, "empty results keep retrieval disabled"); continue; }
            foreach (var cell in retrieve.Cells)
                NpcCheck(cell.ItemCell.Size == new Size(36,36) && cell.ItemCell.ReadOnly && cell.Parent == retrieve.ClientPanel, "original readonly result item slot preserved");
            retrieve.BringToFront();
            NpcClick(scene, retrieve.Cells.First(), DateTime.UtcNow);
            NpcCheck(retrieve.SelectedCell == retrieve.Cells.First() && button.Enabled, "first result remains selectable");
            scroll.Value = Math.Max(0, scroll.MaxValue-scroll.VisibleSize);
            NpcClick(scene, retrieve.Cells.Last(), DateTime.UtcNow);
            NpcCheck(retrieve.SelectedCell == retrieve.Cells.Last(), "last scrolled result remains selectable");
            scene.Observer = true;
            NpcClick(scene, button, DateTime.UtcNow);
            NpcCheck(connection.SendList.IsEmpty, "observer cannot retrieve selected result");
            scene.Observer = false;
            NpcClick(scene, button, DateTime.UtcNow);
            var request = NpcPacket<C.NPCRefineRetrieve>(connection, trace);
            NpcCheck(request.Index == 800+count-1 && retrieve.Refines.Count == count && retrieve.Cells.Last().Selected, "retrieve requests exact result without local removal or early success");
            trace.Add("retrieve|" + count + "|" + request.Index);
            if (count == 12) Render(scene, Path.Combine(evidence, style + "-refine-retrieve.png"));
        }
        NpcClick(scene, scene.NPCBox.CloseButton, DateTime.UtcNow); NpcPacket<C.NPCClose>(connection, trace);
        if (official) CheckRefineRetrieveRestoration(scene, refine.WindowSkin);
        scene.InventoryBox.Visible = false;
    }
    private static void CheckRefineRetrieveRestoration(GameScene scene, GameUiWindowSkin skin)
    {
        using (var window = new NPCRefineRetrieveDialog { Parent = scene, Visible = false })
        {
            window.UseGameUiRefineRetrieve(skin);
            for (int i = 0; i < 12; i++) window.Refines.Add(new ClientRefineInfo { Index=i, Weapon=ServiceItem(i), Type=RefineType.DC, RetrieveTime=DateTime.UtcNow.AddMinutes(-1) });
            window.RefreshList();
            var cells = window.Cells.ToArray(); window.SelectedCell = cells.Last();
            var scroll = ServiceChildren(window).OfType<DXVScrollBar>().Single();
            scroll.Value = scroll.MaxValue-scroll.VisibleSize; int value=scroll.Value;
            bool fail=true;
            EventHandler<EventArgs> handler=(s,e)=> { if(fail) { fail=false; throw new InvalidOperationException("restore result observer"); } };
            window.SizeChanged += handler;
            try { window.UseGameUiRefineRetrieve(null); NpcCheck(false,"restore observer must surface"); }
            catch(InvalidOperationException e) { NpcCheck(e.Message=="restore result observer","restore result failure surfaces"); }
            window.SizeChanged -= handler;
            window.UseGameUiRefineRetrieve(null); window.UseGameUiRefineRetrieve(null);
            NpcCheck(window.WindowSkin==null && window.ClientArea.Size==new Size(491,299),"detach restores native size for current result count");
            NpcCheck(window.Cells.SequenceEqual(cells) && window.SelectedCell==cells.Last() && scroll.Value==value,"detach preserves current original result identities selection and scroll");
            NpcCheck(window.ClientPanel.Location==window.ClientArea.Location && window.ClientPanel.Size==window.ClientArea.Size,"detach restores native result clipping panel");
        }
    }
}
