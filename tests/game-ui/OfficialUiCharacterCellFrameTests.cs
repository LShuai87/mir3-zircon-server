using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Client.Controls;
using Client.Envir;
using Client.Models;
using Client.Scenes;
using Client.Scenes.Views;
using Library;
using Library.SystemModels;
using ImageManager;
using SlimDX.Direct3D9;

internal static partial class OfficialUiCharacterPageTests
{
    private static Delegate[] DrawHandlers(DXItemCell cell) =>
        ((Delegate)typeof(DXControl).GetField("BeforeDraw", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(cell))?.GetInvocationList() ?? new Delegate[0];
    private static MirImage Frame(short width = 36, short height = 36, short offset = 0)
    {
        var frame = (MirImage)FormatterServices.GetUninitializedObject(typeof(MirImage));
        frame.Width = width; frame.Height = height; frame.OffSetX = offset; return frame;
    }
    private static void CellFrames()
    {
        string file = Path.GetTempFileName();
        try
        {
            using (var library = new MirLibrary(file) { Loaded = true, Images = new[] { Frame(), Frame(), Frame(35), Frame(36,36,1), null } })
            using (var root = new DXControl { Size = new Size(1024,768), IsVisible = true })
            using (var d = new CharacterDialog { Parent = root, Visible = true, Size = new Size(412,562) })
            {
                Reject<InvalidOperationException>(() => d.UseGameUiEquipmentCellFrame(library,0), "Uninstalled pages accepted a cell frame");
                var cells = (DXItemCell[])d.Grid.Clone();
                EventHandler<EventArgs> observer = (o,e) => { };
                foreach (var cell in cells) cell.BeforeDraw += observer;
                var handlers = cells.Select(DrawHandlers).ToArray();
                var clicks = cells.Select(Clicks).ToArray();
                var children = cells.Select(c => Children(c).ToArray()).ToArray();
                var flags = cells.Select(c => new[] { c.FixedBorder,c.Border,c.DrawTexture,c.Selected,c.Locked,c.Enabled }).ToArray();
                var layout = GameUiCharacterEquipmentLayout.CreateOfficialCandidate();
                for (int cycle = 0; cycle < 25; cycle++)
                {
                    d.UseGameUiEquipmentPages(layout);
                    foreach (int invalid in new[] { -1,2,3,4,5 })
                        Reject<ArgumentException>(() => d.UseGameUiEquipmentCellFrame(library,invalid), "Invalid frame accepted");
                    for (int replace = 0; replace < 3; replace++)
                    {
                        d.UseGameUiEquipmentCellFrame(library,replace % 2);
                        Check(d.GameUiEquipmentCellLibrary == library && d.GameUiEquipmentCellFrame == replace % 2, "Frame replacement failed");
                        for (int i = 0; i < 16; i++)
                        {
                            var current = DrawHandlers(cells[i]);
                            Check(current.Length == handlers[i].Length+1 && current.Take(handlers[i].Length).SequenceEqual(handlers[i]), "Duplicate frame hook or lost observer");
                            Check(Clicks(cells[i]) == clicks[i] && Children(cells[i]).SequenceEqual(children[i]), "Frame introduced an input surface or changed input handlers");
                            Check(new[] { cells[i].FixedBorder,cells[i].Border,cells[i].DrawTexture,cells[i].Selected,cells[i].Locked,cells[i].Enabled }.SequenceEqual(flags[i]), "Frame changed item highlight/interaction flags");
                        }
                        Reject<ArgumentException>(() => d.UseGameUiEquipmentCellFrame(library,2), "Bad replacement accepted");
                        Check(d.GameUiEquipmentCellLibrary == library && d.GameUiEquipmentCellFrame == replace % 2, "Bad replacement changed active frame");
                    }
                    cells[15].Size = new Size(35,36);
                    Reject<InvalidOperationException>(() => d.UseGameUiEquipmentCellFrame(library,1), "Changed cell size accepted");
                    cells[15].Size = new Size(36,36);
                    Check(d.GameUiEquipmentCellFrame == 0, "Failed validation partially replaced frame");
                    d.UseGameUiEquipmentPages(layout);
                    for (int i = 0; i < 16; i++) Check(DrawHandlers(cells[i]).Length == handlers[i].Length+1, "Page replacement duplicated/lost frame hooks");
                    if (cycle % 2 == 0)
                    {
                        d.UseGameUiEquipmentCellFrame(null,-1); d.UseGameUiEquipmentCellFrame(null,-1);
                        Check(d.GameUiEquipmentCellLibrary == null && d.GameUiEquipmentCellFrame == -1, "Frame clear failed");
                        for (int i = 0; i < 16; i++) Check(DrawHandlers(cells[i]).SequenceEqual(handlers[i]), "Frame clear removed a foreign observer");
                        d.UseGameUiEquipmentCellFrame(library,1);
                    }
                    d.UseGameUiEquipmentPages(null);
                    Check(d.GameUiEquipmentCellLibrary == null && d.GameUiEquipmentCellFrame == -1, "Page disable retained frame state");
                    for (int i = 0; i < 16; i++) Check(DrawHandlers(cells[i]).SequenceEqual(handlers[i]), "Page disable leaked a hook");
                    Check(library.GetSize(0) == new Size(36,36), "Borrowed library was disposed");
                }
                d.UseGameUiEquipmentPages(layout); d.UseGameUiEquipmentCellFrame(library,0); d.Dispose();
                Check(cells.All(c => c.IsDisposed && DrawHandlers(c).Length == 0), "Dialog disposal retained cell callbacks");
                Check(d.GameUiEquipmentCellLibrary == null && library.GetSize(0) == new Size(36,36), "Dialog retained/disposed the borrowed library");
                Reject<ObjectDisposedException>(() => d.UseGameUiEquipmentCellFrame(library,0), "Disposed dialog accepted a frame");
            }
        }
        finally { File.Delete(file); }
    }

    private sealed class CellScene : DXScene { internal CellScene() : base(new Size(1024,768)) { } }
    private static void Property(string name, object value) => typeof(DXManager).GetProperty(name).SetValue(null,value,null);
    private static readonly ushort[] Palette = { 0xf800,0x07e0,0x001f,0xffff };
    private static Color PatternPixel(int x,int y,int frame) => new[] { Color.Red,Color.Lime,Color.Blue,Color.White }[(x/4+3*(y/4)+frame)%4];
    private static Mir3Image Pattern(short width,short height,int frame)
    {
        using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
        {
            for (int y=0;y<height;y+=4) for (int x=0;x<width;x+=4)
            { writer.Write(frame==2 ? (ushort)0xffe0 : Palette[(x/4+3*(y/4)+frame)%4]); writer.Write((ushort)0); writer.Write(0U); }
            return new Mir3Image { Width=width,Height=height,Data=stream.ToArray() };
        }
    }
    private static Color Highlight(Color pixel) => Color.FromArgb((255*125+pixel.R*130)/255,(125*125+pixel.G*130)/255,(125*125+pixel.B*130)/255);
    private static void RenderCellFrames(string output)
    {
        if (Directory.Exists(output)) throw new ArgumentException("Use a fresh GPU evidence directory.");
        Directory.CreateDirectory(output);
        string file = Path.Combine(Path.GetTempPath(),"cell-render-"+Guid.NewGuid().ToString("N")+".Zl");
        new Mir3Library { Images=new[] { Pattern(36,36,0),Pattern(36,36,1),Pattern(16,16,2) } }.Save(file);
        DXScene oldActive=DXControl.ActiveScene; MirLibrary oldItems; bool hadItems=CEnvir.LibraryList.TryGetValue(LibraryFile.StoreItems,out oldItems);
        try
        {
            using (var root=new CellScene { IsVisible=true })
            using (var d3d=new Direct3D())
            {
                DXControl.ActiveScene=root;
                var parameters=new PresentParameters { Windowed=true,SwapEffect=SwapEffect.Discard,BackBufferFormat=Format.X8R8G8B8,BackBufferWidth=1024,BackBufferHeight=768,PresentFlags=PresentFlags.LockableBackBuffer };
                using (var device=new Device(d3d,0,DeviceType.Hardware,CEnvir.Target.Handle,CreateFlags.SoftwareVertexProcessing,parameters))
                {
                    Property("Device",device); Property("Parameters",parameters);
                    try
                    {
                        typeof(DXManager).GetMethod("LoadTextures",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
                        using (var library=new MirLibrary(file))
                        using (var parent=new DXControl { Parent=root,Size=root.Size,DrawTexture=false })
                        using (var d=new CharacterDialog { Parent=parent,Visible=true,Size=new Size(412,562) })
                        {
                            library.ReadLibrary(); CEnvir.LibraryList[LibraryFile.StoreItems]=library;
                            d.UseGameUiEquipmentPages(GameUiCharacterEquipmentLayout.CreateOfficialCandidate());
                            var tabs=Tabs(d); var tabControl=Field<DXTabControl>(d,"TabControl");
                            var info=new ItemInfo();
                            typeof(ItemInfo).GetField("_Image",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(info,2);
                            typeof(ItemInfo).GetField("_StackSize",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(info,1);
                            var item=new ClientUserItem { Info=info,Count=1 }; int frames=0; long pixels=0; int maximumError=0;
                            for (int page=0;page<4;page++) for (int clip=0;clip<3;clip++) for (int state=0;state<3;state++)
                            {
                                parent.Location=clip==2?new Point(970,650):new Point(20,24);
                                parent.Size=clip==1?new Size(240,300):new Size(500,650);
                                d.Location=clip==0?new Point(4,6):clip==1?new Point(-20,-50):new Point(-10,-90);
                                tabControl.SelectedTab=tabs[page]; int atlas=(clip+state)%2; d.UseGameUiEquipmentCellFrame(library,atlas);
                                foreach (var cell in d.Grid) { cell.ItemGrid[cell.Slot]=state==0?null:item; cell.Selected=state==2; cell.UpdateBorder(); }
                                device.Clear(ClearFlags.Target,Color.FromArgb(40,80,120),1,0);device.BeginScene();DXManager.Sprite.Begin(SpriteFlags.AlphaBlend);
                                device.SetSamplerState(0,SamplerState.MinFilter,TextureFilter.Point);device.SetSamplerState(0,SamplerState.MagFilter,TextureFilter.Point);
                                foreach (var cell in d.Grid) cell.Draw();
                                DXManager.Sprite.End();device.EndScene();
                                string image=Path.Combine(output,"cells-"+(++frames)+".png"); Surface.ToFile(DXManager.MainSurface,image,ImageFileFormat.Png);
                                using (var bitmap=new Bitmap(image))
                                {
                                    var scan=Rectangle.Intersect(root.DisplayArea,new Rectangle(d.DisplayArea.X-4,d.DisplayArea.Y+86,420,470));
                                    for (int y=scan.Top;y<scan.Bottom;y++) for (int x=scan.Left;x<scan.Right;x++)
                                    {
                                        Color expected=Color.FromArgb(40,80,120); bool skip=false;
                                        foreach (var cell in d.Grid)
                                        {
                                            if (!cell.IsVisible) continue;
                                            Rectangle area=cell.DisplayArea,clipped=Rectangle.Intersect(root.DisplayArea,area);
                                            for (DXControl ancestor=cell.Parent;ancestor!=null;ancestor=ancestor.Parent) clipped=Rectangle.Intersect(clipped,ancestor.DisplayArea);
                                            // Original fixed/selection outlines use the line renderer; test their interior here.
                                            if (Rectangle.Inflate(area,2,2).Contains(x,y) && !Rectangle.Inflate(area,-3,-3).Contains(x,y)) { skip=true; break; }
                                            if (!clipped.Contains(x,y)) continue;
                                            int sx=x-area.X,sy=y-area.Y;
                                            expected=PatternPixel(sx,sy,atlas);
                                            if (state>0 && new Rectangle(10,10,16,16).Contains(sx,sy)) expected=Color.Yellow;
                                            if (state==2) expected=Highlight(expected);
                                        }
                                        if (skip) continue;
                                        Color actual=bitmap.GetPixel(x,y);
                                        int error=Math.Max(Math.Abs(expected.R-actual.R),Math.Max(Math.Abs(expected.G-actual.G),Math.Abs(expected.B-actual.B))); maximumError=Math.Max(maximumError,error);
                                        if (error>3) throw new Exception("Cell frame pixel mismatch frame "+frames+" at "+x+","+y+" expected "+expected+" actual "+actual);
                                        pixels++;
                                    }
                                }
                            }
                            tabControl.SelectedTab=tabs[0]; parent.Location=Point.Empty;parent.Size=root.Size;d.Location=Point.Empty;
                            var probe=d.Grid[0];probe.Selected=false;probe.ItemGrid[probe.Slot]=null;
                            device.BeginScene();DXManager.Sprite.Begin(SpriteFlags.AlphaBlend);DXManager.SetOpacity(.75F);DXManager.SetBlend(true);
                            float oldOpacity=DXManager.Opacity,oldRate=DXManager.BlendRate;bool oldBlend=DXManager.Blending;var transform=DXManager.Sprite.Transform;var surface=DXManager.CurrentSurface;
                            typeof(DXControl).GetMethod("OnBeforeDraw",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(probe,new object[0]);
                            Check(DXManager.Opacity==oldOpacity && DXManager.Blending==oldBlend && DXManager.BlendRate==oldRate && DXManager.Sprite.Transform==transform && DXManager.CurrentSurface==surface,"Cell frame leaked native drawing state");
                            DXManager.SetBlend(false);DXManager.SetOpacity(1F);DXManager.Sprite.End();device.EndScene();
                            Console.WriteLine("Equipment frame GPU: "+frames+" frames, "+pixels+" pixel comparisons, maximum RGB error "+maximumError+"; real original cells, four pages, empty/item/selection order, scene/ancestor clipping and render-state restoration. Synthetic artwork; native outline edges excluded.");
                        }
                    }
                    finally { typeof(DXManager).GetMethod("CleanUp",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null); Property("Device",null); }
                }
            }
        }
        finally { DXControl.ActiveScene=oldActive; if(hadItems)CEnvir.LibraryList[LibraryFile.StoreItems]=oldItems;else CEnvir.LibraryList.Remove(LibraryFile.StoreItems); File.Delete(file); }
    }
}
