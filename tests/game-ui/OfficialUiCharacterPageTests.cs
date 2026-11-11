using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Client;
using Client.Controls;
using Client.Envir;
using Client.Models;
using Client.Scenes;
using Client.Scenes.Views;
using Library;

internal static partial class OfficialUiCharacterPageTests
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static void Reject<T>(Action action, string message) where T : Exception
    { bool rejected = false; try { action(); } catch (T) { rejected = true; } Check(rejected, message); }
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
    private static List<DXControl> Children(DXControl c) => (List<DXControl>)typeof(DXControl).GetProperty("Controls",BindingFlags.Instance | BindingFlags.NonPublic).GetValue(c);
    private static int Clicks(DXControl c) => ((Delegate)typeof(DXControl).GetField("MouseClick",BindingFlags.Instance | BindingFlags.NonPublic).GetValue(c))?.GetInvocationList().Length ?? 0;
    private static DXTab[] Tabs(CharacterDialog d) => new[] { Field<DXTab>(d,"CharacterTab"), Field<DXTab>(d,"StatsTab"), Field<DXTab>(d,"HermitTab") };
    private static IEnumerable<DXLabel> Stats(CharacterDialog d) => d.DisplayStats.Values.Concat(d.AttackStats.Values).Concat(d.AdvantageStats.Values).Concat(d.DisadvantageStats.Values).Concat(d.HermitDisplayStats.Values).Concat(d.HermitAttackStats.Values);

    private static void Catalog(string expectedPath)
    {
        var layout = GameUiCharacterEquipmentLayout.CreateOfficialCandidate();
        var expected = File.ReadAllLines(expectedPath).ToDictionary(line => line.Split('=')[0],line => line.Split('=')[1]);
        Func<Rectangle,string> text = rect => string.Join(",",new[] { rect.X,rect.Y,rect.Width,rect.Height });
        // Independent section 6.2 semantics: checking only JSON/code equality misses shared drift.
        var preview = GameUiCharacterCompositionLayout.CreateOfficialCandidate().Preview;
        var helmet = layout.GetEquipmentBounds((int)EquipmentSlot.Helmet);
        var shoes = layout.GetEquipmentBounds((int)EquipmentSlot.Shoes);
        Check(helmet == new Rectangle(103,293,36,36),
            "Helmet is not aligned to the third bottom-row frame");
        Check(shoes.Right <= preview.Left && shoes.Top >= preview.Bottom,
            "Shoes are not at the lower left of the preview");
        // UI[2765] native frame centres, independent of the JSON fixture. The original
        // 50px estimate drifted by 1/2/3/4px across the upper equipment rows.
        var native = new[] {
            new Rectangle(191,293,36,36),
            new Rectangle(147,293,36,36),
            new Rectangle(103,293,36,36),
            new Rectangle(59,293,36,36),
            new Rectangle(191,97,36,36),
            new Rectangle(15,195,36,36),
            new Rectangle(191,195,36,36),
            new Rectangle(15,244,36,36),
            new Rectangle(191,244,36,36),
            new Rectangle(15,293,36,36),
            new Rectangle(15,339,36,36),
            new Rectangle(147,339,36,36),
            new Rectangle(191,339,36,36),
            new Rectangle(103,339,36,36),
            new Rectangle(191,146,36,36),
            new Rectangle(59,339,36,36)
        };
        for (int i = 0; i < native.Length; i++)
            Check(layout.GetEquipmentBounds(i) == native[i], "Equipment centre differs from UI[2765]: " + (EquipmentSlot)i);
        Check(expected.Count == 20,"Incomplete catalog fixture");
        Check(expected["container"] == text(layout.Container),"Compiled container drifted from build input");
        var pages = new[] { layout.Character,layout.Status,layout.Hermit };
        for (int i = 0; i < 3; i++) Check(expected["tab."+i] == text(pages[i].Button),"Compiled header drifted from build input");
        for (int i = 0; i < 16; i++) Check(expected["slot."+i] == text(layout.GetEquipmentBounds(i)),"Compiled cell drifted from build input");
    }

    private static void Geometry()
    {
        var good = GameUiCharacterEquipmentLayout.CreateOfficialCandidate();
        var cells = Enumerable.Range(0,16).Select(good.GetEquipmentBounds).ToArray();
        Reject<ArgumentOutOfRangeException>(() => new DXTabGeometry(new Rectangle(int.MaxValue,0,10,10),new Rectangle(0,0,10,10)),"Overflow accepted");
        Reject<ArgumentException>(() => new DXTabGeometry(new Rectangle(0,0,100,100),new Rectangle(10,10,10,10)),"Overlapping header accepted");
        Reject<ArgumentException>(() => new GameUiCharacterEquipmentLayout(good.Container,good.Character,good.Status,good.Hermit,new Rectangle[15]),"Missing slot accepted");
        var overlap = (Rectangle[])cells.Clone(); overlap[1] = overlap[0];
        Reject<ArgumentException>(() => new GameUiCharacterEquipmentLayout(good.Container,good.Character,good.Status,good.Hermit,overlap),"Same-page cell overlap accepted");
        var outOfPage = (Rectangle[])cells.Clone(); outOfPage[0] = new Rectangle(16,20,36,36);
        Reject<ArgumentException>(() => new GameUiCharacterEquipmentLayout(good.Container,good.Character,good.Status,good.Hermit,outOfPage),"Cell outside page accepted");
        Reject<ArgumentException>(() => new GameUiCharacterEquipmentLayout(good.Container,good.Character,good.Character,good.Hermit,cells),"Duplicate header accepted");
        var copied = new GameUiCharacterEquipmentLayout(good.Container,good.Character,good.Status,good.Hermit,cells);
        cells[0] = Rectangle.Empty; Check(copied.GetEquipmentBounds(0) == good.GetEquipmentBounds(0),"Input array was retained");
        Reject<ArgumentOutOfRangeException>(() => good.GetEquipmentBounds(16),"Invalid index accepted");
        Reject<ArgumentOutOfRangeException>(() => good.GetEquipmentBounds(-1),"Negative index accepted");
    }

    private static void AutomaticTabs()
    {
        using (var root = new DXControl { Size = new Size(500,500) })
        using (var control = new DXTabControl { Parent = root, Size = new Size(300,250) })
        {
            var a = new DXTab { Parent = control, TabButton = { Label = { Text = "A" }, Movable = true } };
            var b = new DXTab { Parent = control, TabButton = { Label = { Text = "B" } } };
            control.SelectedTab = a;
            Check(a.Location == new Point(0,DXControl.TabHeight-1),"Legacy tab origin changed");
            Check(a.Size == new Size(300,250-a.Location.Y),"Legacy tab autofill changed");
            Check(a.TabButton.Location.X == 0 && b.TabButton.Location.X == a.TabButton.Size.Width+1,"Legacy header flow changed");
            a.TabButton.Label.Text = "A longer automatic label"; control.TabsChanged();
            Check(a.TabButton.Size.Width > 60 && b.TabButton.Location.X == a.TabButton.Size.Width+1,"Legacy text sizing changed");
            var geometry = new DXTabGeometry(new Rectangle(0,64,300,180),new Rectangle(170,10,100,24));
            a.AllowResize = true; a.UseFixedGeometry(geometry);
            Check(!a.TabButton.Movable && !a.AllowResize,"Fixed tab remains draggable/resizable");
            Reject<ArgumentException>(() => a.UseFixedGeometry(new DXTabGeometry(new Rectangle(0,64,500,180),geometry.Button)),"Oversize tab accepted");
            Check(ReferenceEquals(a.FixedGeometry,geometry),"Rejected replacement changed binding");
            foreach (Size size in new[] { new Size(300,250),new Size(450,400),new Size(500,500) })
            {
                control.Size = size; control.Location = new Point(size.Width/10,size.Height/10); a.TabButton.Label.Text += "x"; control.TabsChanged();
                Check(new Rectangle(a.Location,a.Size) == geometry.Page && new Rectangle(a.TabButton.Location,a.TabButton.Size) == geometry.Button,"Fixed tab moved during resize/text change");
            }
            a.UseFixedGeometry(null);
            Check(a.FixedGeometry == null && a.TabButton.Movable && a.AllowResize,"Tab options were not restored");
            Check(a.Size == new Size(control.Size.Width,control.Size.Height-a.Location.Y),"Automatic fill not restored");
            b.TabButton.InvokeMouseClick(); Check(control.SelectedTab == b && b.Visible && !a.Visible,"Original tab click handler failed");
        }
    }

    private static void Pages()
    {
        using (var root = new DXControl { Size = new Size(1024,768), IsVisible = true })
        using (var d = new CharacterDialog { Parent = root, Visible = true })
        {
            var tabControl = Field<DXTabControl>(d,"TabControl"); var originalTabs = Tabs(d).Where(t => t != null).ToArray();
            var legacyBounds = new Rectangle(tabControl.Location,tabControl.Size);
            var originalChildren = originalTabs.Select(t => Children(t).ToArray()).ToArray();
            var originalLocations = d.Grid.Select(c => c.Location).ToArray(); var originalParents = d.Grid.Select(c => c.Parent).ToArray();
            var cells = (DXItemCell[])d.Grid.Clone(); var itemArrays = cells.Select(c => c.ItemGrid).ToArray();
            var slots = cells.Select(c => c.Slot).ToArray(); var handlers = cells.Select(Clicks).ToArray();
            var stats = Stats(d).ToArray(); var statParents = stats.Select(c => c.Parent).ToArray();
            var statPositions = stats.Select(c => c.Location).ToArray(); var tags = stats.Select(c => c.Tag).ToArray();
            var layout = GameUiCharacterEquipmentLayout.CreateOfficialCandidate();
            Reject<ArgumentException>(() => d.UseGameUiEquipmentPages(layout),"Unprepared window accepted");
            Check(d.GameUiEquipmentLayout == null && Children(tabControl).OfType<DXTab>().Count() == 3,"Rejected layout mutated pages");
            d.Size = new Size(412,562);
            for (int cycle = 0; cycle < 25; cycle++)
            {
                d.UseGameUiEquipmentPages(layout); var tabs = Tabs(d);
                Check(Children(tabControl).OfType<DXTab>().Count() == 3 && tabControl.TabButtons.Count == 3,"Incorrect tab count");
                Check(tabControl.TabButtons.SequenceEqual(tabs.Select(t => t.TabButton)),"Incorrect header order");
                Check(d.Grid.SequenceEqual(cells) && Stats(d).SequenceEqual(stats),"Equipment/stat identities changed");
                var geometries = new[] { layout.Character,layout.Status,layout.Hermit };
                for (int page = 0; page < tabs.Length; page++)
                {
                    Check(new Rectangle(tabs[page].Location,tabs[page].Size) == geometries[page].Page,"Page geometry differs");
                    Check(new Rectangle(tabs[page].TabButton.Location,tabs[page].TabButton.Size) == geometries[page].Button,"Header geometry differs");
                    tabs[page].TabButton.InvokeMouseClick();
                    Check(tabControl.SelectedTab == tabs[page] && tabs.Count(t => t.Visible) == 1,"Selection must show exactly one page");
                    for (int slot = 0; slot < 16; slot++)
                    {
                        int target = 0;
                        Check(cells[slot].Parent == tabs[target] && cells[slot].Slot == slots[slot] && cells[slot].GridType == GridType.Equipment && ReferenceEquals(cells[slot].ItemGrid,itemArrays[slot]),"Slot/data mapping changed");
                        Check(Clicks(cells[slot]) == handlers[slot],"Equipment handlers replaced");
                        Rectangle actual = new Rectangle(cells[slot].DisplayArea.X-d.DisplayArea.X,cells[slot].DisplayArea.Y-d.DisplayArea.Y,36,36);
                        Check(actual == layout.GetEquipmentBounds(slot),"Window-to-page coordinate conversion failed");
                        Check(cells[slot].IsVisible == (target == page && slot != (int)EquipmentSlot.Torch),"Hidden-page equipment remains visible");
                        var area = cells[slot].DisplayArea;
                        Check(cells[slot].IsMouseOver(new Point(area.X+18,area.Y+18)) == (target == page && slot != (int)EquipmentSlot.Torch),"Equipment hit target differs from visible page");
                        Check(!cells[slot].IsMouseOver(new Point(area.Right,area.Bottom)),"Equipment hit rectangle includes its exclusive border");
                    }
                }
                foreach (Size size in new[] { new Size(1024,768),new Size(1366,768),new Size(1280,800),new Size(1440,900),new Size(1600,900),new Size(1920,1080) })
                {
                    root.Size = size; d.Location = new Point(size.Width-420,size.Height-570);
                    tabs[1].TabButton.Label.Text = "状态 ("+cycle+")"; tabControl.TabsChanged();
                    Check(new Rectangle(tabs[1].TabButton.Location,tabs[1].TabButton.Size) == layout.Status.Button,"Moving window changed header geometry");
                }
                for (int i = 0; i < stats.Length; i++)
                    Check(stats[i].Parent == statParents[i] && stats[i].Location == statPositions[i] && ReferenceEquals(stats[i].Tag,tags[i]),"Equipment adapter changed original stat binding");
                DXTab keep = tabs[cycle % 3]; keep.TabButton.InvokeMouseClick();
                d.UseGameUiEquipmentPages(layout); Check(tabControl.SelectedTab == keep,"Replacement lost selected tab");
                d.UseGameUiEquipmentPages(null);
                Check(d.GameUiEquipmentLayout == null && tabControl.TabButtons.Count == 3,"Disable changed original pages");
                Check(tabControl.SelectedTab == keep,"Disable lost selected original tab");
                Check(new Rectangle(tabControl.Location,tabControl.Size) == legacyBounds,"Disable did not restore tab container");
                for (int slot = 0; slot < 16; slot++)
                    Check(cells[slot].Parent == originalParents[slot] && cells[slot].Location == originalLocations[slot] && !cells[slot].IsDisposed,"Disable changed equipment");
                for (int i = 0; i < originalTabs.Length; i++) Check(Children(originalTabs[i]).SequenceEqual(originalChildren[i]),"Disable changed child z-order");
            }
            d.UseGameUiEquipmentPages(layout); var extra = Tabs(d)[1]; d.Dispose();
            Check(extra.IsDisposed && cells.All(c => c.IsDisposed),"Dialog disposal leaked page/equipment");
            Reject<ObjectDisposedException>(() => d.UseGameUiEquipmentPages(layout),"Disposed dialog accepted");
        }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        GameScene old = GameScene.Game; string path = Path.GetTempFileName();
        MirLibrary oldLibrary; bool hadLibrary = CEnvir.LibraryList.TryGetValue(LibraryFile.GameInter,out oldLibrary);
        try
        {
            using (var library = new MirLibrary(path) { Loaded = true, Images = new MirImage[0] })
            using (var target = new TargetForm())
            {
                CEnvir.Target = target; CEnvir.LibraryList[LibraryFile.GameInter] = library;
                var scene = (GameScene)FormatterServices.GetUninitializedObject(typeof(GameScene)); GC.SuppressFinalize(scene);
                scene.Equipment = new ClientUserItem[Globals.EquipmentSize]; GameScene.Game = scene;
                Catalog(args[0]); Geometry(); AutomaticTabs(); Pages(); CellFrames();
                if (args.Length > 1) RenderCellFrames(args[1]);
            }
            Console.WriteLine("Character pages: "+checks+" checks passed; original 16 cells/data/handlers; three pages, six placements, 25 enable/replace/disable cycles, legacy tab regression, equipment-frame lifecycle and disposal. Optional GPU results are reported separately; no packets or game acceptance.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { GameScene.Game = old; CEnvir.Target = null; if (hadLibrary) CEnvir.LibraryList[LibraryFile.GameInter] = oldLibrary; else CEnvir.LibraryList.Remove(LibraryFile.GameInter); File.Delete(path); }
    }
}
