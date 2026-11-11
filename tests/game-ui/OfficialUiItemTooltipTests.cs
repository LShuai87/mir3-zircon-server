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
using Client.Models;
using Client.Scenes;
using Library;
using Library.SystemModels;

internal static class OfficialUiItemTooltipEntry
{
    [STAThread] private static int Main(string[] args) => OfficialUiFullSceneTests.RunItemTooltips(args);
}
internal static partial class OfficialUiFullSceneTests
{
    private static readonly List<string> tooltipChecks = new List<string>();
    private static void TooltipCheck(bool condition, string message)
    { Check(condition, message); tooltipChecks.Add(message); }
    private static string[] TooltipContent(DXControl card) => ((List<DXControl>)typeof(DXControl).GetProperty("Controls", Instance).GetValue(card)).OfType<DXLabel>().Select(x =>
        x.Text + "|" + x.ForeColour.ToArgb() + "|" + x.Font.Name + "|" + x.Font.Size + "|" + x.Outline).ToArray();
    private static ClientUserItem TooltipItem(string name, ItemType type, int index)
    {
        var info = Globals.ItemInfoList.CreateNewObject();
        info.ItemName = name; info.ItemType = type; info.RequiredClass = RequiredClass.All;
        info.RequiredGender = RequiredGender.None; info.Weight = 5; info.StackSize = 1;
        info.Durability = type == ItemType.Weapon ? 10000 : 0;
        return new ClientUserItem(info, 1) { Index = index };
    }
    private static ClientUserItem[] TooltipItems()
    {
        var weapon = TooltipItem("新手木剑", ItemType.Weapon, 8100);
        weapon.Info.Stats[Stat.MinDC] = 2; weapon.Info.Stats[Stat.MaxDC] = 5;
        weapon.AddedStats[Stat.MaxDC] = 3; weapon.AddedStats[Stat.Accuracy] = -2;
        var elite = TooltipItem("精良武器中文长名称与连续英文ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ测试末尾", ItemType.Weapon, 8101);
        elite.Info.Rarity = Rarity.Elite; elite.Flags = UserItemFlags.Locked | UserItemFlags.Bound;
        elite.Info.RequiredAmount = 99; elite.Info.RequiredClass = RequiredClass.Wizard;
        var many = TooltipItem("极品多属性护甲", ItemType.Armour, 8102);
        many.Info.Rarity = Rarity.Superior; many.Flags = UserItemFlags.Locked | UserItemFlags.NonRefinable;
        foreach (Stat stat in Enum.GetValues(typeof(Stat)).Cast<Stat>().Where(x => x != Stat.None).Take(95)) many.Info.Stats[stat] = 5;
        many.AddedStats[Stat.FireResistance] = -2;
        var potion = TooltipItem("药品堆叠", ItemType.Consumable, 8103); potion.Info.StackSize = 100; potion.Count = 37;
        potion.Info.Stats[Stat.Health] = 50;
        var gold = TooltipItem("金币", ItemType.Nothing, 8104); gold.Info.Effect = ItemEffect.Gold; gold.Count = 12345;
        var ore = TooltipItem("矿石", ItemType.Ore, 8105); ore.Info.Durability = 10000;
        var fragment = TooltipItem("碎片", ItemType.Nothing, 8106); fragment.Info.Effect = ItemEffect.ItemPart;
        fragment.AddedStats[Stat.ItemIndex] = weapon.Info.Index; fragment.Count = 3;
        return new[] { weapon, elite, many, potion, gold, ore, fragment };
    }
    private static void TooltipBounds(GameScene scene, string context)
    {
        DXControl card = scene.ItemLabel;
        TooltipCheck(scene.DisplayArea.Contains(card.DisplayArea), context + ": card fits viewport " + card.DisplayArea);
        TooltipCheck(scene.DisplayArea.Contains(Rectangle.Inflate(card.DisplayArea,1,1)), context + ": border fully inside viewport");
        var labels = ((List<DXControl>)typeof(DXControl).GetProperty("Controls", Instance).GetValue(card)).OfType<DXLabel>().ToArray();
        foreach (var label in labels)
        {
            TooltipCheck(card.DisplayArea.Contains(label.DisplayArea), context + ": complete row inside card");
            foreach (var other in labels.Where(x => Array.IndexOf(labels, x) < Array.IndexOf(labels, label)))
                TooltipCheck(!label.DisplayArea.IntersectsWith(other.DisplayArea), context + ": rows do not overlap");
            // Independent draw into a taller surface detects ink beyond the allocated row height.
            if (label.Size.Width < DXLabel.GetSize(label.Text, label.Font, label.Outline).Width)
                using (var bitmap = new Bitmap(label.Size.Width + 2, 3000))
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    var measured = TextRenderer.MeasureText(graphics, label.Text, label.Font, new Size(label.Size.Width - 2, 2998), label.DrawFormat);
                    TooltipCheck(measured.Width <= label.Size.Width, context + ": long word wraps within row width");
                    graphics.Clear(Color.Black);
                    TextRenderer.DrawText(graphics, label.Text, label.Font, new Rectangle(1, 1, label.Size.Width, 2998), Color.White, label.DrawFormat);
                    int inkBelow = 0;
                    for (int y = label.Size.Height; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++)
                        if ((bitmap.GetPixel(x, y).ToArgb() & 0xffffff) != 0) inkBelow++;
                    TooltipCheck(inkBelow == 0, context + ": wrapped text has no clipped ink");
                }
        }
        TooltipCheck(!card.IsControl && !card.IsMouseOver(card.DisplayArea.Location), context + ": card cannot capture input");
    }
    private static List<string[]> ExerciseTooltips(MapInfo map, ClientUserItem[] items, string evidence, bool official)
    {
        var snapshots = new List<string[]>();
        using (var scene = new GameScene(Config.GameSize))
        {
            scenes++; Populate(scene, map); scene.InventoryBox.Visible = true;
            TooltipCheck(scene.NPCBox.WindowSkin == null && scene.ConfigBox.WindowSkin == null, "excluded NPC/settings unchanged");
            for (int i = 0; i < items.Length; i++)
            {
                CEnvir.MouseLocation = new Point(720, 180); scene.MouseItem = items[i];
                snapshots.Add(TooltipContent(scene.ItemLabel));
                Render(scene, Path.Combine(evidence, (official ? "official-" : "legacy-") + i + ".png"));
                var old = scene.ItemLabel;
                TooltipCheck(ReferenceEquals(scene.MouseItem, items[i]), "presentation preserves item identity");
                if (official)
                {
                    TooltipCheck(old.BackColour == Color.FromArgb(232,12,12,12), "official dark tooltip background");
                    TooltipBounds(scene, "item " + i);
                    TooltipCheck(!ReferenceEquals(Field<object>(scene, "itemTooltipLayout"), null), "installed owner has per-card layout");
                }
                else TooltipCheck(old.BackColour == Color.FromArgb(200,0,24,48), "Legacy blue background unchanged actual=" + old.BackColour);
                scene.MouseItem = null;
                TooltipCheck(old.IsDisposed, "leaving item disposes original card and rows");
            }
            if (official)
            {
                scene.MouseItem = items[2];
                var expected = TooltipContent(scene.ItemLabel);
                var corners = new[] { new Point(0,0), new Point(1023,0), new Point(0,767), new Point(1023,767) };
                foreach (var corner in corners)
                {
                    CEnvir.MouseLocation = corner; Render(scene, null); TooltipBounds(scene, "corner " + corner);
                }
                var sizes = new[] { new Size(1280,800), new Size(1366,768), new Size(1440,900), new Size(1600,900), new Size(1920,1080), new Size(1024,768) };
                foreach (var size in sizes)
                {
                    Reset(size); CEnvir.MouseLocation = new Point(size.Width - 1, size.Height - 1);
                    Render(scene, Path.Combine(evidence, "official-resize-" + size.Width + "x" + size.Height + ".png"));
                    TooltipCheck(TooltipContent(scene.ItemLabel).SequenceEqual(expected), "resize preserves every text, colour, font and outline");
                    TooltipBounds(scene, "resize " + size);
                }
                // Existing one-second refresh still reflects changes and disposes the previous card.
                var previous = scene.ItemLabel; items[2].AddedStats[Stat.Health] = 321;
                CEnvir.Now = DateTime.UtcNow.AddMinutes(2); scene.Process();
                TooltipCheck(previous.IsDisposed && !ReferenceEquals(previous, scene.ItemLabel), "timed refresh replaces card");
                TooltipCheck(!TooltipContent(scene.ItemLabel).SequenceEqual(expected), "timed refresh includes changed stats");
                scene.MouseItem = null;
            }
            if (official) ExerciseCharacterMapAndBuffs(scene, map, evidence);
            // Actual coordinate hover uses DXItemCell.OnMouseEnter and preserves its leave behaviour.
            var cell = scene.InventoryBox.Grid.Grid[0]; cell.Item = items[0];
            var center = new Point(cell.DisplayArea.Left + 16, cell.DisplayArea.Top + 16);
            Call(typeof(TargetForm), CEnvir.Target, "OnMouseMove", new MouseEventArgs(MouseButtons.None,0,center.X,center.Y,0));
            TooltipCheck(DXControl.MouseControl == cell && ReferenceEquals(scene.MouseItem, items[0]), "real coordinate hover selects original inventory cell");
            var hovered = scene.ItemLabel;
            Call(typeof(TargetForm), CEnvir.Target, "OnMouseMove", new MouseEventArgs(MouseButtons.None,0,500,300,0));
            TooltipCheck(scene.MouseItem == null && hovered.IsDisposed, "real coordinate leave clears tooltip");
            if (official)
            {
                Call(typeof(TargetForm), CEnvir.Target, "OnMouseMove", new MouseEventArgs(MouseButtons.None,0,center.X,center.Y,0));
                var owner = Field<object>(scene, "officialWindowSet"); ((IDisposable)owner).Dispose();
                CEnvir.Now = DateTime.UtcNow; scene.Process();
                TooltipCheck(scene.ItemLabel.BackColour == Color.FromArgb(200,0,24,48), "owner rollback rebuilds existing hover with Legacy skin actual=" + scene.ItemLabel.BackColour + " disposed=" + scene.ItemLabel.IsDisposed + " mouse=" + (scene.MouseItem != null));
                TooltipCheck(Field<object>(scene,"itemTooltipLayout") == null, "rollback clears per-card layout");
                Render(scene, Path.Combine(evidence,"rollback-tooltip.png"));
            }
            var last = scene.ItemLabel;
            scene.Dispose(); TooltipCheck(last == null || last.IsDisposed, "scene disposal releases tooltip");
        }
        return snapshots;
    }
    private static void MovePointer(Point point)
    { Call(typeof(TargetForm), CEnvir.Target, "OnMouseMove", new MouseEventArgs(MouseButtons.None,0,point.X,point.Y,0)); }
    private static void ClickPointer(Point point)
    {
        MovePointer(point);
        var e = new MouseEventArgs(MouseButtons.Left,1,point.X,point.Y,0);
        Call(typeof(TargetForm), CEnvir.Target, "OnMouseDown", e);
        Call(typeof(TargetForm), CEnvir.Target, "OnMouseClick", e);
        Call(typeof(TargetForm), CEnvir.Target, "OnMouseUp", e);
    }
    private static Point Center(DXControl control) => new Point(control.DisplayArea.X + control.Size.Width/2, control.DisplayArea.Y + control.Size.Height/2);
    private static void ExerciseCharacterMapAndBuffs(GameScene scene, MapInfo map, string evidence)
    {
        scene.InventoryBox.Visible = false;
        var character = scene.CharacterBox;
        character.Visible = true; character.Location = new Point(8,8); character.BringToFront();
        var weapon = TooltipItem("角色预览武器悬停验证", ItemType.Weapon, 92104); weapon.Info.Image = 1040;
        weapon.Info.Stats[Stat.MaxDC] = 12;
        var weaponCell = character.Grid[(int)EquipmentSlot.Weapon];
        foreach (var cell in character.Grid.Where(c => c.Slot != (int)EquipmentSlot.Torch))
        {
            cell.Item = weapon;
            MovePointer(Center(cell));
            TooltipCheck(DXControl.MouseControl == cell && ReferenceEquals(scene.MouseItem, weapon), "character equipment hover original cell " + cell.Slot);
            TooltipCheck(TooltipContent(scene.ItemLabel).Any(t => t.Contains(weapon.Info.ItemName)), "equipment tooltip contains full item name " + cell.Slot);
            cell.Item = null;
        }
        weaponCell.Item = weapon;
        MovePointer(Center(weaponCell)); var expected = TooltipContent(scene.ItemLabel);
        Render(scene,null);
        var image = CEnvir.LibraryList[LibraryFile.Equip].CreateImage(1040, ImageType.Image);
        Point? opaque = null, transparent = null;
        for (int y = 102; y < 194; y++) for (int x = 18; x < 54; x++)
        {
            var local = new Point(x-character.GameUiCompositionLayout.Pivot.X-image.OffSetX,
                y-character.GameUiCompositionLayout.Pivot.Y-image.OffSetY);
            bool hit = new Rectangle(0,0,image.Width,image.Height).Contains(local) && image.VisiblePixel(local,true);
            if (hit && !opaque.HasValue) opaque = new Point(character.DisplayArea.X+x,character.DisplayArea.Y+y);
            if (!hit && !transparent.HasValue) transparent = new Point(character.DisplayArea.X+x,character.DisplayArea.Y+y);
        }
        TooltipCheck(opaque.HasValue && transparent.HasValue,"real weapon has visible and transparent test pixels in restored left region");
        MovePointer(opaque.Value);
        TooltipCheck(ReferenceEquals(scene.MouseItem,weapon),"coordinate hover on restored weapon left edge selects equipped item");
        TooltipCheck(TooltipContent(scene.ItemLabel).SequenceEqual(expected),"preview and original equipment cell share exact attribute tooltip");
        Render(scene,Path.Combine(evidence,"character-weapon-hover.png"));
        MovePointer(transparent.Value);
        TooltipCheck(scene.MouseItem == null,"transparent preview pixel clears weapon tooltip");
        // Overlapping native sprite layers resolve from the topmost equipped layer.
        weaponCell.Item = null;
        foreach (var slot in new[]{EquipmentSlot.Armour,EquipmentSlot.Weapon,EquipmentSlot.Shield,EquipmentSlot.Helmet})
        {
            var item = TooltipItem("预览图层-"+slot,ItemType.Weapon,93000+(int)slot); item.Info.Image=1040;
            character.Grid[(int)slot].Item=item;
            MovePointer(opaque.Value); character.Process();
            TooltipCheck(ReferenceEquals(scene.MouseItem,item),"preview respects native equipment layer " + slot);
        }
        foreach (var cell in character.Grid) cell.Item=null;
        character.Process(); TooltipCheck(scene.MouseItem == null,"stationary cursor clears unequipped preview");
        weaponCell.Item=weapon; MovePointer(opaque.Value); character.Process();
        var tabs = Field<DXTabControl>(character,"TabControl");
        tabs.SelectedTab=Field<DXTab>(character,"StatsTab");
        TooltipCheck(scene.MouseItem==null,"changing character page clears preview tooltip");
        tabs.SelectedTab=Field<DXTab>(character,"CharacterTab");
        MovePointer(opaque.Value); character.Visible=false;
        TooltipCheck(scene.MouseItem==null,"hiding character panel clears preview tooltip");
        weaponCell.Item=null;

        var big = scene.BigMapBox;
        int originalMap = map.MiniMap;
        map.MiniMap=1;
        try
        {
            for (int cycle=0;cycle<3;cycle++)
            {
                big.Opacity=cycle==1 ? .5F : 1F; big.Visible=true; big.BringToFront();
                TooltipCheck(big.CloseButton.IsVisible && big.DisplayArea.Contains(big.CloseButton.DisplayArea),"big map close remains inside header cycle " + cycle);
                string visibleClose=Path.Combine(evidence,cycle==0 ? "big-map-close.png" : "big-map-close-"+cycle+".png");
                string hiddenClose=Path.Combine(evidence,"big-map-without-close-"+cycle+".png");
                big.CloseButton.Visible=false; Render(scene,hiddenClose);
                big.CloseButton.Visible=true; Render(scene,visibleClose);
                if(!noScreenshots)
                {
                    using(var shown=new Bitmap(visibleClose)) using(var hidden=new Bitmap(hiddenClose))
                    {
                        int changed=0;
                        var area=big.CloseButton.DisplayArea;
                        for(int y=area.Top;y<area.Bottom;y++) for(int x=area.Left;x<area.Right;x++)
                        {
                            var pixel=shown.GetPixel(x,y);
                            if(pixel.ToArgb()!=hidden.GetPixel(x,y).ToArgb()) changed++;
                        }
                        TooltipCheck(changed>100,"close artwork renders above window frame at opacity "+big.Opacity);
                        if(cycle==1)
                            using(var closeStream=big.GetType().Assembly.GetManifestResourceStream("Client.Resources.GameUi.inventory-close.png"))
                            using(var closeSheet=new Bitmap(closeStream))
                            {
                                int frame=DXControl.MouseControl==big.CloseButton ? 1 : 0;
                                float alpha=(int)(255*big.CloseButton.Opacity)/255F;
                                for(int y=0;y<22;y++) for(int x=0;x<21;x++)
                                {
                                    var sourcePixel=closeSheet.GetPixel(x,frame*22+y);
                                    if(sourcePixel.A!=255) continue;
                                    var backdrop=hidden.GetPixel(area.X+x,area.Y+y);
                                    var actualPixel=shown.GetPixel(area.X+x,area.Y+y);
                                    TooltipCheck(Math.Abs(actualPixel.R-(sourcePixel.R*alpha+backdrop.R*(1-alpha)))<=2 &&
                                        Math.Abs(actualPixel.G-(sourcePixel.G*alpha+backdrop.G*(1-alpha)))<=2 &&
                                        Math.Abs(actualPixel.B-(sourcePixel.B*alpha+backdrop.B*(1-alpha)))<=2,
                                        "close artwork follows map opacity without double drawing");
                                }
                            }
                    }
                }
                if (cycle == 0)
                {
                    TooltipCheck(big.CloseButton.Size == new Size(21,22) && string.IsNullOrEmpty(big.CloseButton.Label.Text),
                        "big map uses native close artwork without a text overlay");
                    using (var stream = big.GetType().Assembly.GetManifestResourceStream("Client.Resources.GameUi.inventory-close.png"))
                    using (var native = new Bitmap(stream))
                    {
                        TooltipCheck(native.Size == new Size(21,88), "close artwork retains all four native frames");
                        var states = new[]{"normal","hover","pressed","disabled"};
                        try
                        {
                            for(int state=0;state<states.Length;state++)
                            {
                                big.CloseButton.Enabled=state!=3;
                                big.CloseButton.Pressed=state==2;
                                MovePointer(state==0 ? new Point(0,scene.Size.Height-1) : Center(big.CloseButton));
                                if(state==1) TooltipCheck(DXControl.MouseControl==big.CloseButton,"close artwork passes pointer input to native button");
                                string rendered=Path.Combine(evidence,"big-map-close-"+states[state]+".png");
                                Render(scene,rendered);
                                if(!noScreenshots)
                                    using(var closeImage=new Bitmap(rendered))
                                        for(int y=0;y<22;y++) for(int x=0;x<21;x++)
                                        {
                                            var expectedPixel=native.GetPixel(x,state*22+y);
                                            if(expectedPixel.A==255)
                                                TooltipCheck(closeImage.GetPixel(big.CloseButton.DisplayArea.X+x,big.CloseButton.DisplayArea.Y+y)==expectedPixel,
                                                    "native close pixels remain unscaled and visible: "+states[state]);
                                        }
                            }
                        }
                        finally { big.CloseButton.Enabled=true; big.CloseButton.Pressed=false; }
                    }
                }
                ClickPointer(Center(big.CloseButton));
                TooltipCheck(!big.Visible && big.SelectedInfo==null,"coordinate close hides big map and releases selected map " + cycle);
            }
            var open = Field<DXButton>(scene.MiniMapBox,"gameUiBigMap");
            ClickPointer(Center(open));
            TooltipCheck(big.Visible && ReferenceEquals(big.SelectedInfo,map),"minimap toolbar reopens closed big map");
            ClickPointer(Center(big.CloseButton));
        }
        finally { big.Visible=false; map.MiniMap=originalMap; }

        map.MiniMap=1;
        Call(scene.MiniMapBox.GetType(),scene.MiniMapBox,"MapControl_MapInfoChanged",null,EventArgs.Empty);
        var buffs = scene.User.Buffs;
        var originalBuffs = buffs.ToArray();
        try
        {
            foreach(var size in new[]{new Size(1024,768),new Size(1280,800),new Size(1920,1080)})
            {
                Reset(size);
                scene.MiniMapBox.Location=new Point(size.Width-scene.MiniMapBox.Size.Width,0);
                foreach(int count in new[]{1,3,6,7,13,0})
                {
                    buffs.Clear();
                    for(int i=0;i<count;i++) buffs.Add(new ClientBuffInfo{Index=i+1,Type=BuffType.Heal,Stats=new Stats(),RemainingTime=TimeSpan.FromMinutes(10)});
                    scene.BuffBox.BuffsChanged();
                    // Replay a saved location intersecting the map, then grow/shrink/move it.
                    scene.BuffBox.Location=new Point(scene.MiniMapBox.Location.X-5,0); scene.BuffBox.Process();
                    TooltipCheck(!scene.BuffBox.DisplayArea.IntersectsWith(scene.MiniMapBox.DisplayArea),"buff growth/saved location avoids minimap " +size+" count="+count);
                    TooltipCheck(scene.DisplayArea.Contains(scene.BuffBox.DisplayArea),"buff grid stays inside viewport " +size+" count="+count);
                }
                scene.MiniMapBox.ToggleDisplay(); scene.BuffBox.Process();
                TooltipCheck(!scene.BuffBox.DisplayArea.IntersectsWith(scene.MiniMapBox.DisplayArea),"buffs avoid collapsed minimap " +size);
                scene.MiniMapBox.ToggleDisplay();
                ClickPointer(Center(Field<DXButton>(scene.MiniMapBox,"gameUiZoom"))); scene.BuffBox.Process();
                TooltipCheck(!scene.BuffBox.DisplayArea.IntersectsWith(scene.MiniMapBox.DisplayArea),"buffs avoid enlarged minimap " +size);
                ClickPointer(Center(Field<DXButton>(scene.MiniMapBox,"gameUiZoom")));
            }
            // Preserve a valid custom location and resolve dragged maps on either side.
            buffs.Clear();
            for(int i=0;i<13;i++) buffs.Add(new ClientBuffInfo{Index=i+1,Type=BuffType.Heal,Stats=new Stats(),RemainingTime=TimeSpan.FromMinutes(10)});
            scene.BuffBox.BuffsChanged();
            scene.BuffBox.Location=new Point(40,320); scene.BuffBox.Process();
            TooltipCheck(scene.BuffBox.Location==new Point(40,320),"valid custom buff location is preserved");
            foreach(var position in new[]{new Point(0,0),new Point(400,120),new Point(0,850)})
            {
                scene.BuffBox.Location=position;
                scene.MiniMapBox.Location=position;
                scene.BuffBox.Process();
                TooltipCheck(!scene.BuffBox.DisplayArea.IntersectsWith(scene.MiniMapBox.DisplayArea),"dragging minimap resolves buff overlap at "+position);
                TooltipCheck(scene.DisplayArea.Contains(scene.BuffBox.DisplayArea),"dragged map leaves buffs inside viewport at "+position);
            }
            scene.BuffBox.Location=new Point(1700,900);
            Reset(new Size(1024,768)); scene.BuffBox.Process();
            TooltipCheck(scene.DisplayArea.Contains(scene.BuffBox.DisplayArea),"resolution reduction clamps existing buff location");
            TooltipCheck(!scene.BuffBox.DisplayArea.IntersectsWith(scene.MiniMapBox.DisplayArea),"resolution reduction preserves minimap clearance");
            buffs.Clear();
            for(int i=0;i<3;i++) buffs.Add(new ClientBuffInfo{Index=i+1,Type=BuffType.Heal,Stats=new Stats(),RemainingTime=TimeSpan.FromMinutes(10)});
            scene.BuffBox.BuffsChanged(); scene.BuffBox.Location=new Point(scene.MiniMapBox.Location.X-5,0); scene.BuffBox.Process();
            scene.MiniMapBox.Location=new Point(scene.Size.Width-scene.MiniMapBox.Size.Width,0);
            scene.BuffBox.Location=new Point(scene.MiniMapBox.Location.X-5,0); scene.BuffBox.Process();
            TooltipCheck(scene.DisplayArea.Contains(scene.MiniMapBox.DisplayArea),"evidence includes the visible minimap");
            Render(scene,Path.Combine(evidence,"buff-minimap-clearance.png"));
        }
        finally {
            buffs.Clear(); buffs.AddRange(originalBuffs); scene.BuffBox.BuffsChanged();
            map.MiniMap=originalMap; Call(scene.MiniMapBox.GetType(),scene.MiniMapBox,"MapControl_MapInfoChanged",null,EventArgs.Empty);
        }
        scene.InventoryBox.Visible=true; scene.InventoryBox.BringToFront(); scene.MouseItem=null;
    }
    private static List<DXControl> TooltipChildren(DXControl card) => (List<DXControl>)typeof(DXControl).GetProperty("Controls", Instance).GetValue(card);
    private static T LayoutValue<T>(object layout, string name) => (T)layout.GetType().GetProperty(name, Instance).GetValue(layout);
    private static void OverflowWheel(int delta, Point point)
    { Call(typeof(TargetForm), CEnvir.Target, "OnMouseWheel", new MouseEventArgs(MouseButtons.None,0,point.X,point.Y,delta)); }
    private static void ExerciseOverflow(MapInfo map, string evidence)
    {
        using (var scene = new GameScene(Config.GameSize))
        {
            scenes++; Populate(scene,map); scene.InventoryBox.Visible = true;
            var item = TooltipItem(string.Concat(Enumerable.Repeat("长名称ABCDEFGHIJKLMNOPQRSTUVWXYZ最后内容",900)),ItemType.Weapon,9001);
            item.Flags = UserItemFlags.Locked | UserItemFlags.Bound;
            foreach (Stat stat in Enum.GetValues(typeof(Stat)).Cast<Stat>().Where(x => x != Stat.None)) item.Info.Stats[stat] = 5;
            var cell = scene.InventoryBox.Grid.Grid[0]; cell.Item = item;
            var point = new Point(cell.DisplayArea.Left+16,cell.DisplayArea.Top+16);
            Call(typeof(TargetForm),CEnvir.Target,"OnMouseMove",new MouseEventArgs(MouseButtons.None,0,point.X,point.Y,0));
            var content = TooltipContent(scene.ItemLabel);
            Render(scene,Path.Combine(evidence,"overflow-top.png"));
            object layout = Field<object>(scene,"itemTooltipLayout");
            TooltipCheck(LayoutValue<bool>(layout,"IsOverflow"),"extreme content activates overflow viewport");
            TooltipCheck(LayoutValue<bool>(layout,"FitsViewport"),"overflow card itself fits viewport");
            TooltipCheck(scene.DisplayArea.Contains(Rectangle.Inflate(scene.ItemLabel.DisplayArea,1,1)),"overflow including border stays inside screen");
            TooltipCheck(TooltipChildren(scene.ItemLabel).OfType<DXLabel>().All(x => !x.Visible && x.ControlTexture == null),"oversized original labels never allocate GPU textures");
            var view = TooltipChildren(scene.ItemLabel).Single(x => !(x is DXLabel));
            TooltipCheck(view.TextureSize.Width <= 1016 && view.TextureSize.Height <= 760,"overflow texture bounded by client viewport");
            TooltipCheck(view.ControlTexture != null && view.IsVisible,"actual renderer drew bounded overflow texture");
            int forwarded = 0; EventHandler<MouseEventArgs> countWheel = (o,e) => forwarded++;
            cell.MouseWheel += countWheel;
            OverflowWheel(-60,point); TooltipCheck(LayoutValue<int>(layout,"ScrollOffset")==0,"partial wheel notch accumulated");
            OverflowWheel(-60,point); TooltipCheck(LayoutValue<int>(layout,"ScrollOffset")>0,"combined partial notch scrolls");
            int offset = LayoutValue<int>(layout,"ScrollOffset");
            OverflowWheel(-60,point);
            var oldCard = scene.ItemLabel;
            CEnvir.Now=DateTime.UtcNow.AddMinutes(2); scene.Process();
            layout=Field<object>(scene,"itemTooltipLayout");
            TooltipCheck(oldCard.IsDisposed && LayoutValue<int>(layout,"ScrollOffset")==offset,"one-second refresh preserves reading offset and disposes previous viewport");
            TooltipCheck(LayoutValue<int>(layout,"WheelRemainder")==-60,"partial notch survives timed refresh");
            int maximum=LayoutValue<int>(layout,"MaximumScrollOffset");
            int previous=offset;
            bool capturedMiddle=false;
            while (previous < maximum)
            {
                OverflowWheel(-120,point); int current=LayoutValue<int>(layout,"ScrollOffset");
                TooltipCheck(current>previous && current-previous<=48,"continuous scroll exposes all intervening pixels");
                if (!capturedMiddle && current >= maximum/2)
                {
                    Render(scene,Path.Combine(evidence,"overflow-middle.png"));
                    using(var image=new Bitmap(Path.Combine(evidence,"overflow-middle.png")))
                    {
                        var area=TooltipChildren(scene.ItemLabel).Single(x => !(x is DXLabel)).DisplayArea;
                        int ink=0;
                        for(int y=area.Top+4;y<Math.Min(area.Top+80,area.Bottom);y++)
                            for(int x=area.Left+4;x<area.Right-4;x++)
                            { var pixel=image.GetPixel(x,y); if(pixel.R>160 && pixel.G>160 && pixel.B>160) ink++; }
                        TooltipCheck(ink>100,"long single label still renders text at middle offset beyond texture limits");
                    }
                    layout=Field<object>(scene,"itemTooltipLayout");
                    capturedMiddle=true;
                }
                previous=current;
            }
            TooltipCheck(forwarded==0,"overflow wheel including boundary never reaches underlying item cell");
            OverflowWheel(int.MinValue,point); TooltipCheck(LayoutValue<int>(layout,"ScrollOffset")==maximum,"large wheel delta safely clamps at bottom");
            Render(scene,Path.Combine(evidence,"overflow-bottom.png"));
            TooltipCheck(TooltipContent(scene.ItemLabel).SequenceEqual(content),"scrolling retains all original text and styling");
            Reset(new Size(1280,800)); Render(scene,Path.Combine(evidence,"overflow-resized.png"));
            layout=Field<object>(scene,"itemTooltipLayout");
            TooltipCheck(LayoutValue<int>(layout,"ScrollOffset")<=LayoutValue<int>(layout,"MaximumScrollOffset"),"resize clamps preserved reading offset");
            TooltipCheck(scene.DisplayArea.Contains(Rectangle.Inflate(scene.ItemLabel.DisplayArea,1,1)),"resized overflow stays inside viewport");
            Reset(new Size(1024,768)); Render(scene,null);
            layout=Field<object>(scene,"itemTooltipLayout");
            OverflowWheel(int.MaxValue,point); TooltipCheck(LayoutValue<int>(layout,"ScrollOffset")==0,"large opposite delta safely reaches top");
            CEnvir.Now=DateTime.UtcNow.AddMinutes(2); scene.Process();
            layout=Field<object>(scene,"itemTooltipLayout");
            TooltipCheck(TooltipContent(scene.ItemLabel).SequenceEqual(content),"extreme refreshed card retains every original field");
            scene.MouseItem=null; scene.MouseItem=item;
            layout=Field<object>(scene,"itemTooltipLayout");
            TooltipCheck(LayoutValue<int>(layout,"ScrollOffset")==0 && LayoutValue<int>(layout,"WheelRemainder")==0,"leaving and rehover resets scroll and partial notch");
            scene.MouseItem=TooltipItem("普通物品",ItemType.Weapon,9002);
            OverflowWheel(-120,point); TooltipCheck(forwarded==1,"ordinary tooltip still dispatches wheel to original cell");
            scene.MouseItem=item; OverflowWheel(-120,point);
            ((IDisposable)Field<object>(scene,"officialWindowSet")).Dispose();
            OverflowWheel(-120,point); TooltipCheck(forwarded==2,"owner rollback releases wheel immediately even before Process");
            scene.MouseItem=null;
            scene.Dispose();
            cell.MouseWheel -= countWheel;
        }
    }
    internal static int RunItemTooltips(string[] args)
    {
        try
        {
            string package = args[0], data = args[1], evidence = args[2]; Directory.CreateDirectory(evidence);
            Config.FullScreen = false; Config.GameSize = new Size(1024,768); Config.LogChat = false;
            Config.SystemVolume = Config.MusicVolume = Config.PlayerVolume = Config.MonsterVolume = Config.MagicVolume = 0;
            CEnvir.Now = DateTime.UtcNow; Directory.CreateDirectory("Data"); InitializeDatabase();
            var map = BlankMap(Path.GetFullPath("Map")); LoadLibraries(data,evidence); var items = TooltipItems();
            var miniLibrary = new MirLibrary(Path.Combine(data,"MiniMap.Zl")); miniLibrary.ReadLibrary();
            CEnvir.LibraryList.Add(LibraryFile.MiniMap,miniLibrary);
            string installed = Path.Combine(Environment.CurrentDirectory,GameUiOfficialV1Catalog.PackageRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(installed)); File.Copy(package,installed);
            using (var target = new TargetForm { ClientSize = Config.GameSize })
            {
                CEnvir.Target = target; DXManager.Create();
                try
                {
                    List<string[]> baseline;
                    using (var legacy = GameUiStartupSession.Prepare("Legacy",Environment.CurrentDirectory))
                    { Publish(legacy); baseline = ExerciseTooltips(map,items,evidence,false); Publish(null); }
                    using (var owner = (GameUiStartupSession)typeof(GameUiStartupSession).GetMethod("Prepare",Static,null,new[]{typeof(string),typeof(string),typeof(bool)},null).Invoke(null,new object[]{"OfficialV1",Environment.CurrentDirectory,true}))
                    {
                        Publish(owner); TooltipCheck(owner.EffectiveStyle == GameUiStyle.OfficialV1,"verified candidate active in fixture");
                        var actual = ExerciseTooltips(map,items,evidence,true);
                        for (int i = 0; i < items.Length; i++) TooltipCheck(baseline[i].SequenceEqual(actual[i]),"Legacy/Official exact content parity for item " + i);
                        ExerciseOverflow(map,evidence);
                        Publish(null);
                    }
                }
                finally { Publish(null); DXManager.Unload(); }
            }
            TooltipCheck(!GameUiOfficialV1Catalog.RuntimeReady,"production readiness remains false");
            File.WriteAllLines(Path.Combine(evidence,"assertions.txt"),tooltipChecks);
            File.WriteAllText(Path.Combine(evidence,"metrics.json"),"{\"checks\":"+checks+",\"scenes\":"+scenes+",\"items\":"+items.Length+",\"presentedFrames\":"+renders+",\"deviceResets\":"+resets+",\"serverConnected\":false,\"releaseAccepted\":false}");
            Console.WriteLine("Item tooltip integration: " + checks + " checks passed, " + items.Length + " paired items, " + renders + " frames, " + resets + " device resets.");
            return 0;
        }
        catch(Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { foreach(var library in CEnvir.LibraryList.Values) library.Dispose(); CEnvir.LibraryList.Clear(); CEnvir.Target = null; }
    }
}
