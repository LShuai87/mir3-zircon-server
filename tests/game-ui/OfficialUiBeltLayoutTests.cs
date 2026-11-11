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

internal static class OfficialUiBeltLayoutTests
{
    private static int checks;
    private static void Check(bool value, string detail) { checks++; if (!value) throw new Exception(detail); }
    private static void Reject<T>(Action action, string detail) where T : Exception
    { bool rejected=false; try { action(); } catch(T) { rejected=true; } Check(rejected,detail); }
    private static List<DXControl> Children(DXControl c) => (List<DXControl>)typeof(DXControl).GetProperty("Controls",BindingFlags.Instance | BindingFlags.NonPublic).GetValue(c);
    private static Delegate Click(DXControl c) => (Delegate)typeof(DXControl).GetField("MouseClick",BindingFlags.Instance | BindingFlags.NonPublic).GetValue(c);
    private sealed class TestScene : DXScene { internal TestScene() : base(new Size(1024,768)) { } }
    private static Rectangle Local(DXControl c) => new Rectangle(c.Location,c.Size);
    private static GameUiWindowSkin Skin(MirLibrary library, int index) => new GameUiWindowSkin(library,index,
        index==1 ? new Padding(83,4,84,4) : Padding.Empty,Padding.Empty,Padding.Empty,
        index==1 ? new Size(168,9) : new Size(1,1),true,Rectangle.Empty,Rectangle.Empty,Rectangle.Empty);
    private static void Geometry(string fixture)
    {
        var l=GameUiBeltLayout.OfficialV1; var lines=File.ReadAllLines(fixture);
        var size=lines[0].Split(',').Select(int.Parse).ToArray(); Check(l.WindowSize==new Size(size[0],size[1]),"Compiled window differs from JSON");
        var rects=lines.Skip(1).Select(s=>s.Split(',').Select(int.Parse).ToArray()).Select(v=>new Rectangle(v[0],v[1],v[2],v[3])).ToArray();
        Check(rects.Length==11&&l.Grid==rects[0],"Compiled grid envelope differs from JSON");
        for(int i=0;i<10;i++) Check(rects[i+1]==new Rectangle(l.Grid.X+i*41,l.Grid.Y,41,40),"JSON slot geometry differs");
        Reject<ArgumentOutOfRangeException>(()=>new GameUiBeltLayout(Size.Empty,l.Grid),"Empty window accepted");
        Reject<ArgumentException>(()=>new GameUiBeltLayout(new Size(350,36),new Rectangle(0,0,351,36)),"Clipped grid accepted");
        Reject<ArgumentException>(()=>new GameUiBeltLayout(l.WindowSize,new Rectangle(-1,4,351,36)),"Negative position accepted");
        Reject<ArgumentException>(()=>new GameUiBeltLayout(l.WindowSize,new Rectangle(60,4,350,36)),"Rescaled grid accepted");
        Reject<ArgumentException>(()=>new GameUiBeltLayout(new Size(int.MaxValue,44),new Rectangle(int.MaxValue,4,351,36)),"Overflow accepted");
    }
    private static void Adapt(TestScene root, GameScene scene, MirLibrary library)
    {
        var l=GameUiBeltLayout.OfficialV1;
        using(var legacy=new BeltDialog { Parent=root })
        {
            scene.BeltBox=legacy; var grid=legacy.Grid; var size=legacy.Size;
            Check(grid.GridSize==new Size(1,1)&&legacy.AllowResize,"Default legacy behavior changed");
            Reject<InvalidOperationException>(()=>legacy.UseGameUiBelt(l),"One-slot grid accepted");
            Check(legacy.Grid==grid&&legacy.Size==size&&legacy.GameUiBeltLayout==null,"Rejected grid mutated");
            legacy.Size=legacy.GetAcceptableResize(legacy.GetSize(new Size(36,361)));
            Check(grid.IsDisposed&&legacy.Grid.GridSize==new Size(1,10),"Legacy vertical resize changed");
            grid=legacy.Grid;
            Reject<InvalidOperationException>(()=>legacy.UseGameUiBelt(l),"Vertical grid accepted");
            Check(legacy.Grid==grid&&!grid.IsDisposed,"Vertical rejection replaced grid");
        }
        foreach(bool priorSkin in new[]{false,true})
        using(var d=new BeltDialog(true) { Parent=root, Visible=true })
        {
            scene.BeltBox=d;
            if(priorSkin) d.WindowSkin=Skin(library,1);
            var originalSkin=d.WindowSkin; var originalSize=d.Size; var grid=d.Grid; var originalGrid=Local(grid);
            Check(grid.GridSize==new Size(10,1),"Opt-in did not create ten native slots");
            var cells=grid.Grid.ToArray(); var links=d.Links; var linkEntries=links.ToArray();
            var labels=cells.Select(c=>Children(c).OfType<DXLabel>().Single(x=>x.Text==((c.Slot+1)%10).ToString())).ToArray();
            var originalFonts=labels.Select(l=>l.Font).ToArray(); var originalColours=labels.Select(l=>l.ForeColour).ToArray(); var originalLabelLocations=labels.Select(l=>l.Location).ToArray();
            var children=cells.Select(c=>Children(c).ToArray()).ToArray(); var callbacks=cells.Select(Click).ToArray();
            var order=Children(d).ToArray(); int gridEvents=0,linkEvents=0;
            grid.GridSizeChanged+=(s,e)=>gridEvents++;
            foreach(var c in cells) c.LinkedItemChanged+=(s,e)=>linkEvents++;
            var marker=new ClientUserItem { Index=12345, Info=new ItemInfo(), Count=13 };
            scene.Inventory[3]=marker; links[3].LinkItemIndex=marker.Index; d.UpdateLinks();
            Check(cells[3].QuickItem==marker&&linkEvents==1,"Original link update failed");
            var info=new ItemInfo(); cells[7].QuickInfo=info; var quickInfoItem=cells[7].QuickInfoItem;
            cells[3].Selected=true; cells[4].Locked=true; var countLabel=cells[3].CountLabel;
            for(int cycle=0;cycle<8;cycle++)
            {
                d.UseGameUiBelt(l);
                if(!priorSkin)
                {
                    d.Size=new Size(500,80);
                    Check(d.Grid==grid&&cells.SequenceEqual(grid.Grid)&&Local(grid)==l.Grid,"Client-area resize rebuilt active grid");
                    d.UseGameUiBelt(l);
                }
                d.WindowSkin=Skin(library,0);
                Check(!d.AllowResize&&d.Size==l.WindowSize&&Local(grid)==l.Grid,"Active belt geometry wrong");
                Check(!grid.Border&&!grid.DrawTexture,"Native grid covers rounded art");
                Check(l.Grid.Right<=d.Size.Width && l.Grid.Bottom<=d.Size.Height,"Rightmost slot clipped");
                Check(d.GetAcceptableResize(new Size(1,1))==l.WindowSize,"Active resize not fixed");
                for(int placement=0;placement<6;placement++)
                {
                    d.Location=new Point(placement*27,placement*17);
                    d.ClientArea=new Rectangle(placement,placement,400,40); d.UpdateClientArea();
                    d.UseGameUiBelt(new GameUiBeltLayout(l.WindowSize,l.Grid));
                    for(int i=0;i<10;i++)
                    {
                        Check(d.Grid==grid&&grid.Grid[i]==cells[i]&&!cells[i].IsDisposed,"Cell recreated");
                        Check(d.Links==links&&links[i]==linkEntries[i]&&cells[i].Slot==i,"Link identity changed");
                        Check(cells[i].Location==new Point(i*41,0)&&cells[i].Size==new Size(41,40),"Native slot geometry changed");
                        Check(cells[i].DisplayArea.Location==new Point(d.DisplayArea.X+l.Grid.X+i*41,d.DisplayArea.Y+l.Grid.Y),"Global slot geometry wrong");
                        Check(Children(cells[i]).SequenceEqual(children[i])&&Children(cells[i]).Contains(labels[i]),"Key label duplicated or replaced");
                        Check(Click(cells[i])==callbacks[i],"Original click callbacks changed");
                        Check(labels[i].Font.Style==FontStyle.Regular&&labels[i].Location==new Point(1,0)&&labels[i].ForeColour==Color.FromArgb(214,143,66),"Official key label style mismatch");
                    }
                }
                Check(Children(d).SequenceEqual(order),"Control order changed");
                Check(cells[3].QuickItem==marker&&cells[3].CountLabel==countLabel&&cells[3].Selected&&cells[4].Locked,"Live state lost");
                Check(cells[7].QuickInfo==info&&cells[7].QuickInfoItem==quickInfoItem,"Quick-info reference changed");
                marker.Count=cycle+20; cells[3].RefreshItem();
                Check(cells[3].CountLabel==countLabel&&countLabel.Text==(cycle+20).ToString(),"Live item count stopped updating");
                Check(linkEvents==1&&gridEvents==0,"Layout mutated item links or grid structure");
                var current=d.GameUiBeltLayout;
                Action unchanged=()=>Check(d.GameUiBeltLayout==current&&d.Grid==grid&&d.Size==l.WindowSize&&Local(grid)==l.Grid,"Rejected bind mutated state");
                d.IsResizing=true; Reject<InvalidOperationException>(()=>d.UseGameUiBelt(l),"Resizing accepted"); d.IsResizing=false; unchanged();
                cells[0].IsMoving=true; Reject<InvalidOperationException>(()=>d.UseGameUiBelt(l),"Moving cell accepted"); cells[0].IsMoving=false; unchanged();
                links[0]=new ClientBeltLink { Slot=0 }; Reject<InvalidOperationException>(()=>d.UseGameUiBelt(l),"Replaced link accepted"); links[0]=linkEntries[0]; unchanged();
                d.Links=links.ToArray(); Reject<InvalidOperationException>(()=>d.UseGameUiBelt(l),"Replaced link array accepted"); d.Links=links; unchanged();
                links[0].Slot=1; Reject<InvalidOperationException>(()=>d.UseGameUiBelt(l),"Wrong slot accepted"); links[0].Slot=0; unchanged();
                Reject<ArgumentException>(()=>d.UseGameUiBelt(new GameUiBeltLayout(new Size(519,44),l.Grid)),"Incompatible skin accepted"); unchanged();
                d.UseGameUiBelt(null);
                Check(d.GameUiBeltLayout==null&&d.WindowSkin==originalSkin&&d.Size==originalSize&&Local(grid)==originalGrid&&d.AllowResize,"Rollback failed");
                Check(d.Grid==grid&&cells.SequenceEqual(grid.Grid)&&d.Links==links&&linkEvents==1&&gridEvents==0,"Rollback changed identities or live links");
                Check(grid.Border&&grid.DrawTexture&&cells.Select((c,i)=>c.Size==new Size(36,36)&&c.Location==new Point(i*35,0)).All(v=>v),"Legacy grid geometry/painting not restored");
                Check(labels.Select((label,i)=>label.Font==originalFonts[i]&&label.ForeColour==originalColours[i]&&label.Location==originalLabelLocations[i]).All(v=>v),"Legacy key style not restored");
                d.UseGameUiBelt(null); Check(d.Grid==grid,"Repeated disable rebuilt grid");
            }
            scene.Inventory[3]=null; links[3].LinkItemIndex=-1; cells[3].QuickItem=null;
            Check(linkEvents==2,"Original link removal changed");
            if(!priorSkin)
            {
                d.Size=d.GetAcceptableResize(d.GetSize(new Size(36,361)));
                Check(grid.IsDisposed&&d.Grid.GridSize==new Size(1,10),"Legacy resize not restored");
            }
        }
        using(var d=new BeltDialog(true) { Parent=root })
        {
            scene.BeltBox=d; var grid=d.Grid; var size=d.Size; var bounds=Local(grid);
            bool fail=true;
            EventHandler<EventArgs> onSize=(s,e)=> { if(fail) { fail=false; throw new InvalidOperationException("Injected observer failure"); } };
            d.SizeChanged+=onSize;
            Reject<InvalidOperationException>(()=>d.UseGameUiBelt(l),"Observer failure not surfaced");
            d.SizeChanged-=onSize;
            Check(d.GameUiBeltLayout==null&&d.Size==size&&d.AllowResize&&d.Grid==grid&&Local(grid)==bounds,"Initial failure did not restore snapshot");
            d.UseGameUiBelt(l);
            bool failMove=true;
            EventHandler<EventArgs> onMove=(s,e)=> { if(failMove) { failMove=false; throw new InvalidOperationException("Injected grid observer failure"); } };
            grid.LocationChanged+=onMove;
            Reject<InvalidOperationException>(()=>d.UseGameUiBelt(new GameUiBeltLayout(l.WindowSize,new Rectangle(61,3,410,40))),"Replacement observer failure not surfaced");
            grid.LocationChanged-=onMove;
            Check(d.GameUiBeltLayout==l&&d.Size==l.WindowSize&&Local(grid)==l.Grid&&!grid.IsDisposed,"Failed replacement lost active layout");
            d.UseGameUiBelt(null);
            Check(d.Size==size&&Local(grid)==bounds&&d.AllowResize,"Failure recovery lost legacy state");
        }
        using(var d=new BeltDialog(true) { Parent=root })
        {
            scene.BeltBox=d; var grid=d.Grid; var cells=grid.Grid.ToArray(); d.UseGameUiBelt(l); d.WindowSkin=Skin(library,0); d.Dispose();
            Check(d.GameUiBeltLayout==null&&grid.IsDisposed&&cells.All(c=>c.IsDisposed),"Disposal left binding or cells alive");
            Reject<ObjectDisposedException>(()=>d.UseGameUiBelt(l),"Disposed belt accepted");
        }
        scene.BeltBox=null;
    }
    [STAThread] private static int Main(string[] args)
    {
        var oldGame=GameScene.Game; var oldActive=DXControl.ActiveScene; string file=Path.GetTempFileName();
        MirLibrary oldLibrary; bool had=CEnvir.LibraryList.TryGetValue(LibraryFile.GameInter,out oldLibrary);
        try
        {
            var frame=(MirImage)FormatterServices.GetUninitializedObject(typeof(MirImage)); frame.Width=518; frame.Height=44;
            var frame2=(MirImage)FormatterServices.GetUninitializedObject(typeof(MirImage)); frame2.Width=518; frame2.Height=44;
            using(var library=new MirLibrary(file) { Loaded=true, Images=new[]{frame,frame2} })
            using(var target=new TargetForm { ClientSize=new Size(1024,768) })
            {
                CEnvir.Target=target; CEnvir.LibraryList[LibraryFile.GameInter]=library;
                using(var root=new TestScene { IsVisible=true })
                {
                    DXControl.ActiveScene=root; var scene=(GameScene)FormatterServices.GetUninitializedObject(typeof(GameScene)); GC.SuppressFinalize(scene);
                    scene.Inventory=new ClientUserItem[49]; GameScene.Game=scene;
                    Geometry(args[0]); Adapt(root,scene,library);
                }
            }
            Console.WriteLine("Belt layout: "+checks+" checks passed; ten original slots and links, JSON geometry, legacy constructors/resize, skin switches, invalid bindings, live items, rollback and disposal. No rendering/network/game acceptance."); return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { GameScene.Game=oldGame; DXControl.ActiveScene=oldActive; CEnvir.Target=null; if(had) CEnvir.LibraryList[LibraryFile.GameInter]=oldLibrary; else CEnvir.LibraryList.Remove(LibraryFile.GameInter); File.Delete(file); }
    }
}
