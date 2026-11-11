using System;
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
using Client.Scenes.Views;
using Library;
using Library.SystemModels;
using SlimDX.Direct3D9;

internal static class OfficialUiInventoryLayoutTests
{
    private static int checks;
    private static void Check(bool value, string detail) { checks++; if (!value) throw new Exception(detail); }
    private static void Reject<T>(Action action, string detail) where T : Exception
    { bool rejected = false; try { action(); } catch (T) { rejected = true; } Check(rejected, detail); }
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
    private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
    private static Delegate Event(DXControl owner, string name) => (Delegate)typeof(DXControl).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
    private static int Count(DXControl owner, string name) => Event(owner, name)?.GetInvocationList().Length ?? 0;
    private sealed class TestScene : DXScene { internal TestScene() : base(new Size(1024, 768)) { } }
    private static DXControl[] Controls(InventoryDialog d) => new DXControl[] { d.Grid, Field<DXLabel>(d,"gameUiWeightCaption"), d.WeightLabel,
        Field<DXLabel>(d,"gameUiGoldCaption"), d.GoldLabel, Field<DXButton>(d,"RemoteSellButton"), Field<DXButton>(d,"RemoteFragmentButton"), Field<DXButton>(d,"MonsterDropListButton") };
    private static Rectangle Bounds(InventoryDialog d, DXControl c) => new Rectangle(c.DisplayArea.X-d.DisplayArea.X, c.DisplayArea.Y-d.DisplayArea.Y, c.Size.Width, c.Size.Height);
    private static List<DXControl> Children(DXControl c) => (List<DXControl>)typeof(DXControl).GetProperty("Controls", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(c);
    private static Rectangle Local(DXControl c) => new Rectangle(c.Location, c.Size);
    private static GameUiInventoryLayout Offset(GameUiInventoryLayout l, int x, int y)
    {
        var b = l.GetBounds(); for (int i=0;i<b.Length;i++) b[i].Offset(x,y);
        return new GameUiInventoryLayout(b[0],b[1],b[2],b[3],b[4],b[5],b[6],b[7]);
    }
    private static void Geometry(string fixture)
    {
        var l=GameUiInventoryLayout.CreateOfficialCandidate(); var b=l.GetBounds();
        var expected=File.ReadAllLines(fixture).Where(s=>!string.IsNullOrWhiteSpace(s)).Select(s=>s.Split(',').Select(int.Parse).ToArray()).Select(v=>new Rectangle(v[0],v[1],v[2],v[3])).ToArray();
        Check(expected.Length==57,"Fixture should contain eight groups and 49 slots");
        for(int i=0;i<8;i++) Check(b[i]==expected[i],"Compiled inventory geometry differs from build input");
        for(int i=0;i<49;i++) Check(expected[i+8]==new Rectangle(l.Grid.X+i%7*35,l.Grid.Y+i/7*35,36,36),"Compiled grid differs from JSON slot "+i);
        b[0]=Rectangle.Empty; Check(l.Grid.Size==new Size(246,246),"Mutable inventory bounds escaped");
        Reject<ArgumentException>(()=>new GameUiInventoryLayout(new Rectangle(15,49,245,246),l.WeightCaption,l.Weight,l.GoldCaption,l.Gold,l.Sell,l.Fragment,l.Drops),"Rescaled grid accepted");
        Reject<ArgumentException>(()=>new GameUiInventoryLayout(l.Grid,l.WeightCaption,l.Weight,l.GoldCaption,l.Gold,l.Sell,l.Sell,l.Drops),"Overlapping buttons accepted");
        Reject<ArgumentOutOfRangeException>(()=>Offset(l,-16,0),"Negative position accepted");
        Reject<ArgumentOutOfRangeException>(()=>new GameUiInventoryLayout(l.Grid,l.WeightCaption,new Rectangle(int.MaxValue,304,190,20),l.GoldCaption,l.Gold,l.Sell,l.Fragment,l.Drops),"Overflow position accepted");
        Reject<ArgumentOutOfRangeException>(()=>new GameUiInventoryLayout(l.Grid,l.WeightCaption,Rectangle.Empty,l.GoldCaption,l.Gold,l.Sell,l.Fragment,l.Drops),"Empty value bounds accepted");
    }
    private static void Adapt(TestScene root, GameScene scene)
    {
        var layout=GameUiInventoryLayout.CreateOfficialCandidate();
        using(var d=new InventoryDialog { Parent=root, Visible=true })
        {
            scene.InventoryBox=d; var originalSize=d.Size; d.Size=new Size(276,412);
            var controls=Controls(d); var original=controls.Select(Local).ToArray(); var order=Children(d).ToArray();
            var cells=d.Grid.Grid.ToArray(); var cellOrder=Children(d.Grid).ToArray();
            var values=new[]{d.GoldLabel,d.WeightLabel}; var formats=values.Select(c=>c.DrawFormat).ToArray();
            var callbacks=controls.Select(c=>Event(c,"MouseClick")).ToArray(); var sourceCount=values.Select(c=>Count(c,"TextChanged")).ToArray();
            int itemGridEvents=0, gridSizeEvents=0, itemEvents=0;
            d.Grid.ItemGridChanged+=(s,e)=>itemGridEvents++; d.Grid.GridSizeChanged+=(s,e)=>gridSizeEvents++;
            foreach(var cell in cells) cell.ItemChanged+=(s,e)=>itemEvents++;
            var data=scene.Inventory; var marker=new ClientUserItem { Info=new ItemInfo(), Count=13 };
            data[5]=marker; cells[5].RefreshItem();
            var countLabel=cells[5].CountLabel;
            using(var linked=new DXItemCell { GridType=GridType.Sell, Linked=true })
            {
                linked.Link=cells[5]; cells[5].Selected=true; cells[6].Locked=true;
                for(int cycle=0;cycle<8;cycle++)
                {
                    d.UseGameUiInventory(layout);
                    Check(d.GameUiInventoryLayout==layout,"Active inventory layout missing");
                    for(int i=0;i<2;i++) Check(Count(values[i],"TextChanged")==sourceCount[i]+1,"Duplicate/missing value subscription");
                    for(int placement=0;placement<6;placement++)
                    {
                        d.Location=new Point(placement*27,placement*15);
                        for(int i=0;i<controls.Length;i++)
                        {
                            Check(Bounds(d,controls[i])==layout.GetBounds()[i],"Inventory coordinate drift");
                            Check(Equals(Event(controls[i],"MouseClick"),callbacks[i]),"Original event handler changed");
                        }
                        for(int i=0;i<cells.Length;i++)
                        {
                            Check(ReferenceEquals(d.Grid.Grid[i],cells[i])&&cells[i].Slot==i&&cells[i].ItemGrid==data&&cells[i].HostGrid==d.Grid,"Original slot identity/data changed");
                            Check(Bounds(d,cells[i])==new Rectangle(layout.Grid.X+i%7*35,layout.Grid.Y+i/7*35,36,36),"Slot bounds changed");
                        }
                        d.GoldLabel.Text="12345678901234567890 "+cycle+"/"+placement;
                        d.WeightLabel.Text="9999999999/10000000000 "+cycle;
                        d.WeightLabel.ForeColour=Color.Red;
                        Check(d.GoldLabel.Hint==d.GoldLabel.Text&&d.WeightLabel.Hint==d.WeightLabel.Text,"Long value hint stale");
                        Check(d.GoldLabel.DrawFormat.HasFlag(TextFormatFlags.EndEllipsis)&&!d.GoldLabel.AutoSize,"Long value geometry not fixed");
                        Check(linked.Link==cells[5]&&cells[5].Link==linked&&cells[5].Selected&&cells[6].Locked,"Live selection/link/lock changed");
                        Check(cells[5].Item==marker&&cells[5].CountLabel==countLabel,"Item or count label recreated");
                    }
                    var shifted=Offset(layout,1,1); d.UseGameUiInventory(shifted);
                    for(int i=0;i<controls.Length;i++) Check(Bounds(d,controls[i])==shifted.GetBounds()[i],"Replacement origin applied twice");
                    Check(controls.SequenceEqual(Controls(d))&&Children(d).Where(c => c != Field<DXControl>(d, "gameUiGoldFrame")).SequenceEqual(order)&&Children(d.Grid).SequenceEqual(cellOrder),"Control identity/order changed");
                    d.Grid.UpdateGridDisplay(); Check(cells.SequenceEqual(d.Grid.Grid),"Grid display update recreated cells");
                    for(int i=0;i<49;i++) Check(Bounds(d,cells[i])==new Rectangle(shifted.Grid.X+i%7*35,shifted.Grid.Y+i/7*35,36,36),"Grid display update reset group origin");
                    Check(itemGridEvents==0&&gridSizeEvents==0&&itemEvents==cycle*2,"Successful layout mutated grid data");
                    var stable=controls.Select(Local).ToArray();
                    Reject<ArgumentException>(()=>d.UseGameUiInventory(Offset(layout,100,0)),"Out of window replacement accepted");
                    Reject<ArgumentException>(()=>d.UseGameUiInventory(Offset(layout,0,-49)),"Title/close overlap accepted");
                    Check(d.GameUiInventoryLayout==shifted&&controls.Select(Local).SequenceEqual(stable),"Invalid replacement mutated active layout");
                    cells[0].Slot=1; Reject<InvalidOperationException>(()=>d.UseGameUiInventory(layout),"Invalid slot accepted"); cells[0].Slot=0;
                    cells[0].Parent=d; Reject<InvalidOperationException>(()=>d.UseGameUiInventory(layout),"Reparented slot accepted"); cells[0].Parent=d.Grid;
                    Children(d.Grid).Remove(cells[0]); Children(d.Grid).Insert(0,cells[0]);
                    d.IsMoving=true; Reject<InvalidOperationException>(()=>d.UseGameUiInventory(layout),"Moving window accepted"); d.IsMoving=false;
                    Check(d.GameUiInventoryLayout==shifted&&controls.Select(Local).SequenceEqual(stable),"Failed validation mutated controls");
                    string gold=d.GoldLabel.Text, weight=d.WeightLabel.Text;
                    d.UseGameUiInventory(null);
                    Check(d.GameUiInventoryLayout==null&&controls.Select(Local).SequenceEqual(original),"Legacy bounds not restored");
                    Check(d.GoldLabel.Text==gold&&d.WeightLabel.Text==weight&&d.WeightLabel.ForeColour==Color.Red,"Rollback restored stale game data");
                    Check(d.GoldLabel.Hint==null&&d.WeightLabel.Hint==null,"Generated hint leaked on rollback");
                    for(int i=0;i<2;i++) Check(Count(values[i],"TextChanged")==sourceCount[i]&&values[i].DrawFormat==formats[i],"Legacy label style/subscription not restored");
                    Check(itemGridEvents==0&&gridSizeEvents==0&&itemEvents==(cycle+1)*2,"Unexpected events beyond two explicit slot edits: "+itemGridEvents+"/"+gridSizeEvents+"/"+itemEvents);
                }
                d.GoldLabel.Hint="server hint"; d.UseGameUiInventory(layout); d.GoldLabel.Text="changed";
                Check(d.GoldLabel.Hint=="server hint","Existing hint overwritten");
                d.WeightLabel.Hint="external hint"; d.WeightLabel.Text="external value"; d.UseGameUiInventory(null);
                Check(d.GoldLabel.Hint=="server hint"&&d.WeightLabel.Hint=="external hint","Rollback removed an external hint");
                d.GoldLabel.Hint=null; d.WeightLabel.Hint=null;
                d.UseGameUiInventory(layout); var replacement=new ClientUserItem[49]; replacement[5]=marker; d.Grid.ItemGrid=replacement; data=replacement;
                Check(itemGridEvents==1&&cells.All(c=>c.ItemGrid==replacement),"Live item-grid update lost");
                d.UseGameUiInventory(null); Check(d.Grid.ItemGrid==replacement&&cells[5].Item==marker,"Rollback restored stale item data");
                d.UseGameUiInventory(layout);
                scene.GoldPickedUp=false; Set(scene.User,"_Gold",123L); d.GoldLabel.InvokeMouseClick(); Check(scene.GoldPickedUp,"Original gold click no longer picks up");
                d.GoldLabel.InvokeMouseClick(); Check(!scene.GoldPickedUp,"Gold click callback duplicated");
                scene.SelectedCell=cells[0]; d.GoldLabel.InvokeMouseClick(); Check(!scene.GoldPickedUp,"Gold selected-item guard changed"); scene.SelectedCell=null;
                Set(scene.User,"_Gold",0L); d.GoldLabel.InvokeMouseClick(); Check(!scene.GoldPickedUp,"Empty-gold guard changed");
                d.CloseButton.InvokeMouseClick(); Check(!d.Visible&&linked.Link==null&&cells[5].Link==null,"Original close/link cleanup changed");
                d.Visible=true;
                linked.GridType=GridType.TradeUser; linked.Link=cells[5]; d.Visible=false;
                Check(linked.Link==cells[5],"Original trade link exception changed"); d.Visible=true; linked.Link=null;
            }
            d.UseGameUiInventory(null); d.Size=originalSize;
        }
        using(var d=new InventoryDialog { Parent=root, Size=new Size(276,412) })
        {
            scene.InventoryBox=d; var controls=Controls(d); d.UseGameUiInventory(layout); d.Dispose();
            Check(d.GameUiInventoryLayout==null&&Field<object>(d,"gameUiInventorySnapshots")==null,"Window disposal left binding active");
            Check(controls.All(c=>c.IsDisposed),"Window disposal leaked controls");
            Reject<ObjectDisposedException>(()=>d.UseGameUiInventory(layout),"Disposed inventory accepted");
        }
        scene.InventoryBox=null;
    }
    private static void Readouts(TestScene root, GameScene scene)
    {
        var layout = GameUiInventoryLayout.CreateOfficialCandidate();
        using (var d = new InventoryDialog { Parent = root, Size = new Size(276, 412), Visible = true })
        {
            scene.InventoryBox = d;
            var caption = Field<DXLabel>(d, "gameUiWeightCaption");
            var goldCaption = Field<DXLabel>(d, "gameUiGoldCaption");
            var buttons = Controls(d).OfType<DXButton>().ToArray();
            var borders = Controls(d).Select(c => c.Border).ToArray();
            var backgrounds = Controls(d).Select(c => c.BackColour).ToArray();
            var buttonColours = buttons.Select(c => c.Label.ForeColour).ToArray();
            var buttonChildren = buttons.Select(b => Children(b).ToArray()).ToArray();
            bool gridTexture = d.Grid.DrawTexture;
            var closeChildren = Children(d.CloseButton).ToArray(); var closeBounds = Local(d.CloseButton);
            bool closeImage = d.CloseButton.DrawImage, closeTexture = d.CloseButton.DrawTexture, closeBorder = d.CloseButton.Border;
            var title = d.TitleLabel.Text; var titleColour = d.TitleLabel.ForeColour;
            d.UpdateGameUiInventoryWeight(25, 100); // Received before preview installation.
            d.UseGameUiInventory(layout);
            var fill = Field<DXControl>(d, "gameUiWeightFill");
            var goldFrame = Field<DXControl>(d, "gameUiGoldFrame");
            Check(goldFrame.Parent == d && !goldFrame.IsControl && Local(goldFrame) == Local(d.GoldLabel) &&
                Children(d).IndexOf(goldFrame) < Children(d).IndexOf(d.GoldLabel) && !d.GoldLabel.Border &&
                d.GoldLabel.BackColour == Color.Empty, "Official gold field covers text or intercepts native input");
            Check(d.TitleLabel.Text == "包裹" && caption.Text == "负重", "Official title/caption missing");
            Check(!caption.Border && !goldCaption.Border && !d.WeightLabel.DrawTexture && !d.WeightLabel.Border,
                "Native text box still covers the weight trough");
            Check(fill.Parent == d.WeightLabel && !fill.IsControl && fill.Location == new Point(2, 2), "Fill intercepts input or misses native trough");
            Check(fill.Size == new Size(30, 6) && d.WeightLabel.Hint == "负重：25 / 100", "Pre-install weight was lost");
            var cases = new[] { new[] {0,100,0}, new[] {50,100,60}, new[] {100,100,120}, new[] {150,100,120},
                new[] {1,0,0}, new[] {0,0,0}, new[] {-1,100,0}, new[] {int.MaxValue,1,120}, new[] {int.MaxValue,int.MaxValue,120} };
            foreach (var c in cases)
            {
                d.UpdateGameUiInventoryWeight(c[0], c[1]);
                Check(fill.Size.Width == c[2] && fill.Visible == (c[2] > 0), "Invalid weight fraction: " + c[0] + "/" + c[1]);
                Check((int)fill.GetType().GetProperty("Frame", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(fill, null) ==
                    (c[0] > c[1] ? 1 : 0), "Official warning fill frame incorrect");
                Check(d.WeightLabel.Hint.Contains(c[0] + " / " + c[1]), "Weight tooltip lost complete values");
            }
            d.WeightLabel.Hint = "external weight hint";
            d.UpdateGameUiInventoryWeight(75, 100);
            Check(d.WeightLabel.Hint == "external weight hint", "Authoritative refresh overwrote an external hint");
            Check(!d.Grid.DrawTexture && !d.Grid.Border, "Grid still covers the recessed atlas slots");
            Check(buttons.All(b => !b.DrawImage && !b.DrawTexture && !b.Border), "Footer still draws procedural or legacy button art");
            var art = Field<Array>(d, "gameUiButtonArt").Cast<DXControl>().ToArray();
            Check(art.Length == 3, "Official button frames missing");
            var closeArt = Field<DXControl>(d, "gameUiCloseArt");
            Check(closeArt.Parent == d.CloseButton && !closeArt.IsControl && closeArt.Size == new Size(21, 22) && d.CloseButton.Size == closeArt.Size &&
                !d.CloseButton.DrawImage && !d.CloseButton.DrawTexture && !d.CloseButton.Border, "Square close art missing or intercepts native input");
            for (int i = 0; i < buttons.Length; i++)
                Check(art[i].Parent == buttons[i] && !art[i].IsControl && Children(buttons[i])[0] == art[i] &&
                    Children(buttons[i]).Skip(1).SequenceEqual(buttonChildren[i]), "Button art intercepts input or covers its native label");
            d.GoldLabel.Text = long.MaxValue.ToString("#,##0");
            Check(d.GoldLabel.Hint == d.GoldLabel.Text && Local(d.GoldLabel) == layout.Gold, "Maximum gold spills outside its field");
            d.WeightLabel.ForeColour = Color.Red;
            d.UseGameUiInventory(null);
            Check(fill.IsDisposed && goldFrame.IsDisposed && Children(d.WeightLabel).Count == 0, "Official readout visuals leaked on rollback");
            Check(d.Grid.DrawTexture == gridTexture && art.All(a => a.IsDisposed), "Atlas grid/button art leaked on rollback");
            Check(closeArt.IsDisposed && Children(d.CloseButton).SequenceEqual(closeChildren) && Local(d.CloseButton) == closeBounds &&
                d.CloseButton.DrawImage == closeImage && d.CloseButton.DrawTexture == closeTexture && d.CloseButton.Border == closeBorder,
                "Legacy close button not restored");
            for (int i = 0; i < buttons.Length; i++)
                Check(Children(buttons[i]).SequenceEqual(buttonChildren[i]), "Legacy button children not restored");
            Check(d.TitleLabel.Text == title && d.TitleLabel.ForeColour == titleColour && caption.Text == "重量", "Legacy title/caption not restored");
            Check(d.WeightLabel.DrawTexture && d.WeightLabel.ForeColour == Color.Red, "Rollback lost native rendering/live overweight state");
            Check(Controls(d).Select(c => c.Border).SequenceEqual(borders) && Controls(d).Select(c => c.BackColour).SequenceEqual(backgrounds), "Legacy footer styles not restored");
            Check(buttons.All(b => b.DrawImage) && buttons.Select(b => b.Label.ForeColour).SequenceEqual(buttonColours), "Legacy button art/text not restored");
            Check(d.GoldLabel.Hint == null && d.WeightLabel.Hint == "external weight hint", "Tooltip ownership not restored");
        }
        scene.InventoryBox = null;
    }

    private static void Capture(TestScene root, InventoryDialog d, Device device, string directory, string state)
    {
device.Clear(ClearFlags.Target, Color.FromArgb(40, 60, 70), 1, 0);
device.BeginScene(); DXManager.Sprite.Begin(SlimDX.Direct3D9.SpriteFlags.AlphaBlend);
device.SetSamplerState(0, SamplerState.MinFilter, TextureFilter.Point);
device.SetSamplerState(0, SamplerState.MagFilter, TextureFilter.Point);
root.Draw(); DXManager.Sprite.End(); device.EndScene();
string path = Path.Combine(directory, state + "-frame.png");
Surface.ToFile(DXManager.MainSurface, path, ImageFileFormat.Png);
using (var image = new Bitmap(path))
using (var crop = image.Clone(new Rectangle(d.DisplayArea.Location, d.Size), System.Drawing.Imaging.PixelFormat.Format32bppArgb))
    crop.Save(Path.Combine(directory, state + ".png"), System.Drawing.Imaging.ImageFormat.Png);
    }

    private static bool SamePixels(Bitmap first, Bitmap second, Rectangle area)
    {
        for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++)
                if (first.GetPixel(x, y) != second.GetPixel(x, y)) return false;
        return true;
    }

    private static void Render(TestScene root, GameScene scene, TargetForm target, string packagePath, string directory)
    {
        Directory.CreateDirectory(directory);
        var parameters = new PresentParameters { Windowed = true, SwapEffect = SwapEffect.Discard, BackBufferFormat = Format.X8R8G8B8,
            BackBufferWidth = 1024, BackBufferHeight = 768, PresentFlags = PresentFlags.LockableBackBuffer };
        using (var d3d = new Direct3D())
        using (var device = new Device(d3d, 0, DeviceType.Hardware, target.Handle, CreateFlags.SoftwareVertexProcessing, parameters))
        {
            typeof(DXManager).GetProperty("Device").SetValue(null, device, null);
            typeof(DXManager).GetProperty("Parameters").SetValue(null, parameters, null);
            typeof(DXManager).GetMethod("LoadTextures", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            try
            {
                using (var package = new MirLibrary(packagePath))
                {
                    package.ReadLibrary(); scene.Inventory = new ClientUserItem[49];
                    using (var d = new InventoryDialog { Parent = root, Location = new Point(32, 32), Visible = true })
                    {
                        scene.InventoryBox = d;
                        var frame = GameUiOfficialV1Catalog.PackageManifest.Frames["inventory"];
                        d.WindowSkin = new GameUiWindowSkin(package, frame.Index, Padding.Empty, Padding.Empty, Padding.Empty,
                            frame.Size, true, new Rectangle(16, 10, 228, 18), new Rectangle(8, 0, 236, 36), new Rectangle(247, 8, 21, 22));
                        d.UseGameUiInventory(GameUiInventoryLayout.CreateOfficialCandidate());
                        foreach (var state in new[] { "empty", "half", "full", "overweight", "zero-capacity", "max-gold" })
                        {
                            int weight = state == "empty" || state == "zero-capacity" ? 0 : state == "half" ? 50 : state == "overweight" ? 125 : 100;
                            d.UpdateGameUiInventoryWeight(weight, state == "zero-capacity" ? 0 : 100);
                            d.GoldLabel.Text = (state == "max-gold" ? long.MaxValue : 1234567L).ToString("#,##0");
                            Capture(root, d, device, directory, state);
                        }
                        // Removing the input grid must not change any of the 49 atlas slots.
                        d.Grid.Visible = false;
                        Capture(root, d, device, directory, "atlas-only");
                        d.Grid.Visible = true;
                        using (var baseline = new Bitmap(Path.Combine(directory, "empty.png")))
                        using (var atlas = new Bitmap(Path.Combine(directory, "atlas-only.png")))
                            Check(SamePixels(baseline, atlas, d.GameUiInventoryLayout.Grid), "Native grid still paints over the atlas slots");
                        var cells = d.Grid.Grid;
                        cells[0].Selected = true; cells[1].Locked = true;
                        DXControl.MouseControl = cells[2];
                        Capture(root, d, device, directory, "slot-feedback");
                        using (var baseline = new Bitmap(Path.Combine(directory, "empty.png")))
                        using (var feedback = new Bitmap(Path.Combine(directory, "slot-feedback.png")))
                        {
                            for (int i = 0; i < 3; i++)
                                Check(!SamePixels(baseline, feedback, Bounds(d, cells[i])), "Missing native slot feedback " + i);
                            Check(SamePixels(baseline, feedback, Bounds(d, cells[48])), "Slot feedback paints over another slot");
                        }
                        cells[0].Selected = false; cells[1].Locked = false; DXControl.MouseControl = null;
                        var buttons = Controls(d).OfType<DXButton>().ToArray();
                        d.UpdateGameUiInventoryWeight(50, 100); d.GoldLabel.Text = 1234567L.ToString("#,##0");
                        foreach (var state in new[] { "normal", "hover", "pressed", "disabled" })
                        {
                            foreach (var b in buttons) { b.Enabled = state != "disabled"; b.Pressed = state == "pressed"; }
                            DXControl.MouseControl = state == "hover" ? buttons[0] : null;
                            Capture(root, d, device, directory, "buttons-" + state);
                        }
                        using (var normal = new Bitmap(Path.Combine(directory, "buttons-normal.png")))
                            foreach (var state in new[] { "hover", "pressed", "disabled" })
                                using (var image = new Bitmap(Path.Combine(directory, "buttons-" + state + ".png")))
                                    Check(!SamePixels(normal, image, Local(buttons[0])), "Missing official button state " + state);
                        DXControl.MouseControl = null;
                        foreach (var b in buttons) { b.Enabled = true; b.Pressed = false; }
                        foreach (var state in new[] { "hover", "pressed", "disabled" })
                        {
                            d.CloseButton.Enabled = state != "disabled";
                            d.CloseButton.Pressed = state == "pressed";
                            DXControl.MouseControl = state == "hover" ? d.CloseButton : null;
                            Capture(root, d, device, directory, "close-" + state);
                        }
                        using (var normal = new Bitmap(Path.Combine(directory, "buttons-normal.png")))
                            foreach (var state in new[] { "hover", "pressed", "disabled" })
                                using (var image = new Bitmap(Path.Combine(directory, "close-" + state + ".png")))
                                    Check(!SamePixels(normal, image, Local(d.CloseButton)), "Missing square close state " + state);
                        DXControl.MouseControl = null; d.CloseButton.Enabled = true; d.CloseButton.Pressed = false;
                        Check(Local(d.CloseButton) == new Rectangle(247, 8, 21, 22), "Official close hit area is not native-sized or aligned");
                        using (var stream = typeof(InventoryDialog).Assembly.GetManifestResourceStream("Client.Resources.GameUi.inventory-close.png"))
                        using (var native = new Bitmap(stream))
                        {
                            Check(native.Size == new Size(21, 88), "Official close sheet was rescaled");
                            var closeStates = new[] { "normal", "hover", "pressed", "disabled" };
                            for (int state = 0; state < closeStates.Length; state++)
                                using (var image = new Bitmap(Path.Combine(directory, state == 0 ? "buttons-normal.png" : "close-" + closeStates[state] + ".png")))
                                    for (int y = 0; y < 22; y++)
                                        for (int x = 0; x < 21; x++)
                                        {
                                            var expected = native.GetPixel(x, state * 22 + y);
                                            if (expected.A == 255)
                                                Check(image.GetPixel(247 + x, 8 + y) == expected,
                                                    "Official close pixels were scaled or clipped: " + closeStates[state]);
                                        }
                        }
                        using (var empty = new Bitmap(Path.Combine(directory, "empty.png")))
                        using (var half = new Bitmap(Path.Combine(directory, "half.png")))
                        using (var full = new Bitmap(Path.Combine(directory, "full.png")))
                        using (var over = new Bitmap(Path.Combine(directory, "overweight.png")))
                        {
                            var area = d.GameUiInventoryLayout.Weight;
                            int y = area.Y + 4;
                            Check(empty.GetPixel(area.X + 10, y) != half.GetPixel(area.X + 10, y), "GPU did not draw half-weight fill");
                            Check(empty.GetPixel(area.X + 100, y) == half.GetPixel(area.X + 100, y), "Half fill painted beyond its numeric extent");
                            Check(empty.GetPixel(area.X + 100, y) != full.GetPixel(area.X + 100, y), "GPU did not draw full-weight fill");
                            Check(full.GetPixel(area.X + 100, y) != over.GetPixel(area.X + 100, y), "GPU did not draw overweight warning");
                            using (var stream = typeof(InventoryDialog).Assembly.GetManifestResourceStream("Client.Resources.GameUi.inventory-weight.png"))
                            using (var native = new Bitmap(stream))
                            {
                                for (int py = 0; py < 6; py++)
                                    for (int px = 0; px < 120; px++)
                                    {
                                        var actual = full.GetPixel(area.X + 2 + px, area.Y + 2 + py);
                                        var expected = native.GetPixel(px, py);
                                        if (expected.A == 255) Check(actual == expected, "Normal weight fill differs from official native pixels");
                                        if (px < 60) Check(half.GetPixel(area.X + 2 + px, area.Y + 2 + py) == actual,
                                            "Partial weight fill stretches the native texture");
                                        expected = native.GetPixel(px, py + 6);
                                        if (expected.A == 255) Check(over.GetPixel(area.X + 2 + px, area.Y + 2 + py) == expected,
                                            "Overweight fill differs from official warning pixels");
                                    }
                            }
                            using (var stream = typeof(InventoryDialog).Assembly.GetManifestResourceStream("Client.Resources.GameUi.inventory-gold.png"))
                            using (var native = new Bitmap(stream))
                            {
                                var gold = d.GameUiInventoryLayout.Gold;
                                for (int px = 3; px < gold.Width - 3; px++)
                                    foreach (int py in new[] { 0, gold.Height - 1 })
                                        if (native.GetPixel(px, py).A == 255)
                                            Check(full.GetPixel(gold.X + px, gold.Y + py) == native.GetPixel(px, py),
                                                "Gold field border differs from official art");
                            }
                        }
                    }
                    scene.InventoryBox = null;
                }
            }
            finally { DXManager.Unload(); }
        }
    }

    [STAThread] private static int Main(string[] args)
    {
        var oldGame=GameScene.Game; var oldActive=DXControl.ActiveScene; string file=Path.GetTempFileName();
        MirLibrary oldLibrary; bool had=CEnvir.LibraryList.TryGetValue(LibraryFile.GameInter,out oldLibrary);
        try
        {
            using(var library=new MirLibrary(file) { Loaded=true, Images=new MirImage[0] })
            using(var target=new TargetForm { ClientSize=new Size(1024,768) })
            {
                CEnvir.Target=target; CEnvir.LibraryList[LibraryFile.GameInter]=library;
                using(var root=new TestScene { IsVisible=true })
                {
                    DXControl.ActiveScene=root; var scene=(GameScene)FormatterServices.GetUninitializedObject(typeof(GameScene)); GC.SuppressFinalize(scene);
                    var user=(UserObject)FormatterServices.GetUninitializedObject(typeof(UserObject)); Set(scene,"_User",user);
                    scene.Inventory=new ClientUserItem[49]; GameScene.Game=scene;
                    Geometry(args[0]); Adapt(root,scene); Readouts(root,scene);
                    if (args.Length == 3) Render(root, scene, target, args[1], args[2]);
                }
            }
            Console.WriteLine("Inventory layout: "+checks+" checks passed; 49 original slots, compiled JSON geometry, live data/links/labels, six placements, eight cycles, invalid replacements and rollback. Optional D3D9 atlas, slot-feedback, four-state button and readout probes; no network/live-game acceptance."); return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { GameScene.Game=oldGame; DXControl.ActiveScene=oldActive; CEnvir.Target=null; if(had) CEnvir.LibraryList[LibraryFile.GameInter]=oldLibrary; else CEnvir.LibraryList.Remove(LibraryFile.GameInter); File.Delete(file); }
    }
}
