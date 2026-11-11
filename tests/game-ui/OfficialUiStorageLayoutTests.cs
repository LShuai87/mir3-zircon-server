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

internal static class OfficialUiStorageLayoutTests
{
    private static int checks;
    private static void Check(bool value, string detail) { checks++; if (!value) throw new Exception(detail); }
    private static void Reject<T>(Action action, string detail) where T : Exception
    { bool rejected = false; try { action(); } catch (T) { rejected = true; } Check(rejected, detail); }
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
    private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
    private static Delegate Event(DXControl owner, string name) => (Delegate)typeof(DXControl).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
    private sealed class TestScene : DXScene { internal TestScene() : base(new Size(1024,768)) { } }
    private static DXControl[] Controls(StorageDialog d) => new DXControl[] { Field<DXControl>(d,"gameUiStorageFilterPanel"), d.Grid, d.StorageScrollBar };
    private static DXControl[] Children(DXControl c) => ((List<DXControl>)typeof(DXControl).GetProperty("Controls", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(c)).ToArray();
    private static Point[] Positions(StorageDialog d) => Controls(d).Select(c=>c.Location).ToArray();
    private static GameUiStorageLayout Shift(GameUiStorageLayout l, int x, int y)
    { var b=l.GetBounds(); for(int i=0;i<b.Length;i++) b[i].Offset(x,y); return new GameUiStorageLayout(b[0],b[1],b[2]); }
    private static void Geometry(string path)
    {
        var l=GameUiStorageLayout.CreateOfficialCandidate(); var b=l.GetBounds();
        var expected=File.ReadAllLines(path).Select(s=>s.Split(',').Select(int.Parse).ToArray()).Select(v=>new Rectangle(v[0],v[1],v[2],v[3])).ToArray();
        Check(b.SequenceEqual(expected),"Compiled geometry differs from JSON candidate"); b[0]=Rectangle.Empty;
        Check(l.Filter.Size==new Size(473,26),"Mutable rectangles escaped");
        Reject<ArgumentOutOfRangeException>(()=>Shift(l,-17,0),"Negative coordinate accepted");
        Reject<ArgumentOutOfRangeException>(()=>new GameUiStorageLayout(new Rectangle(int.MaxValue,48,473,26),l.Grid,l.Scrollbar),"Overflow accepted");
        Reject<ArgumentException>(()=>new GameUiStorageLayout(l.Filter,l.Grid,l.Grid),"Overlap accepted");
        Reject<ArgumentException>(()=>new GameUiStorageLayout(l.Filter,new Rectangle(16,78,455,351),l.Scrollbar),"Rescaled grid accepted");
    }
    private static ItemInfo Item(string name, ItemType type)
    { var info=new ItemInfo(); Set(info,"_ItemName",name); Set(info,"_ItemType",type); return info; }
    private static void CheckCells(StorageDialog d, DXItemCell[] cells, ClientUserItem[] items)
    {
        Check(d.Grid.Grid.SequenceEqual(cells),"Layout recreated item cells");
        for(int i=0;i<cells.Length;i++)
        {
            var c=cells[i]; Check(c.Parent==d.Grid&&c.HostGrid==d.Grid&&c.Slot==i&&c.ItemGrid==items&&c.Size==new Size(36,36)&&c.GridType==GridType.Storage,"Cell identity/data/size changed "+i);
            int row=i/13; bool visible=row>=d.Grid.ScrollValue&&row<d.Grid.ScrollValue+10;
            Check(c.Visible==visible,"Scroll visibility changed "+i);
            if(visible) Check(c.Location==new Point(i%13*35,(row-d.Grid.ScrollValue)*35),"Visible cell location changed "+i);
        }
    }
    private static void Adapt(TestScene root, GameScene scene)
    {
        var layout=GameUiStorageLayout.CreateOfficialCandidate();
        using(var inventory=new InventoryDialog {Parent=root})
        using(var d=new StorageDialog {Parent=root,Size=new Size(508,480)})
        {
            scene.InventoryBox=inventory; scene.StorageBox=d;
            var controls=Controls(d); var native=Positions(d); var originalChildren=Children(d); var filters=Children(controls[0]);
            Console.WriteLine("Native filter children: "+string.Join("; ",filters.Select(c=>c.GetType().Name+" "+new Rectangle(c.Location,c.Size))));
            Check(DXControl.SmallButtonHeight>0&&DXControl.HeaderSize>0,"Native Interface dimensions were not loaded");
            foreach(var c in filters) Check(new Rectangle(Point.Empty,controls[0].Size).Contains(new Rectangle(c.Location,c.Size)),"Native filter child already clipped");
            var placeholder=d.Grid.Grid[0]; int rebuilds=0; d.Grid.GridSizeChanged+=(s,e)=>rebuilds++;
            d.UseGameUiStorage(layout); Check(d.Grid.Grid[0]==placeholder&&rebuilds==0&&d.Grid.Size==new Size(36,36),"Startup placeholder recreated/resized");
            Console.WriteLine("Dynamic capacity refresh starting"); Set(scene,"_StorageSize",131); d.RefreshStorage();
            Console.WriteLine("Dynamic capacity refresh completed");
            Check(rebuilds==1&&d.Grid.GridSize==new Size(13,11)&&d.Grid.Size==layout.Grid.Size&&Positions(d).SequenceEqual(layout.GetBounds().Select(b=>b.Location)),"Native refresh while adapted lost geometry");
            d.StorageScrollBar.Value=1; var refreshedCells=d.Grid.Grid.ToArray(); d.UseGameUiStorage(layout);
            Check(d.Grid.Grid.SequenceEqual(refreshedCells)&&rebuilds==1,"Reapply after capacity update rebuilt grid");
            d.UseGameUiStorage(null); Check(Positions(d).SequenceEqual(native)&&d.Grid.GridSize==new Size(13,11)&&d.Grid.ScrollValue==1,"Restore rolled back live capacity/scroll");
            for(int cycle=0;cycle<8;cycle++)
            {
                Console.WriteLine("Cycle "+cycle+" starting"); int capacity=new[]{0,27,131,1000}[cycle%4]; Set(scene,"_StorageSize",capacity); d.RefreshStorage();
                var cells=d.Grid.Grid.ToArray(); var callbacks=cells.Select(c=>Event(c,"MouseWheel")).ToArray();
                var itemHandlers=cells.Select(c=>(Delegate)typeof(DXItemCell).GetField("ItemChanged",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(c)).ToArray();
                var count=rebuilds; var rows=Math.Max(10,(int)Math.Ceiling(capacity/13f));
                CEnvir.Storage[0]=new ClientUserItem {Info=Item("Test Sword",ItemType.Weapon),Count=2};
                CEnvir.Storage[1]=new ClientUserItem {Info=Item("Test Ring",ItemType.Ring),Count=1};
                cells[0].RefreshItem(); cells[1].RefreshItem();
                d.StorageScrollBar.Value=Math.Min(2,rows-10); int scroll=d.StorageScrollBar.Value;
                d.ItemNameTextBox.TextBox.Text="sWoRd"; d.ItemTypeComboBox.ListBox.SelectItem(ItemType.Weapon);
                bool[] enabled=cells.Select(c=>c.Enabled).ToArray();
                using(var linked=new DXItemCell {GridType=GridType.Sell,Linked=true})
                {
                    linked.Link=cells[0]; cells[0].Selected=true; cells[1].Locked=true;
                    d.UseGameUiStorage(layout);
                    Check(d.GameUiStorageLayout==layout&&Positions(d).SequenceEqual(layout.GetBounds().Select(b=>b.Location)),"Candidate layout missing");
                    Check(rebuilds==count&&d.Grid.GridSize==new Size(13,rows)&&d.StorageScrollBar.MaxValue==rows&&d.StorageScrollBar.Value==scroll&&d.Grid.ScrollValue==scroll,"Install changed capacity/scroll or rebuilt grid");
                    Check(cells.Select(c=>c.Enabled).SequenceEqual(enabled)&&d.ItemNameTextBox.TextBox.Text=="sWoRd"&&Equals(d.ItemTypeComboBox.SelectedItem,ItemType.Weapon),"Install changed filters");
                    for(int placement=0;placement<4;placement++)
                    {
                        d.Location=new Point(placement*20,placement*11); CheckCells(d,cells,CEnvir.Storage);
                        Check(Positions(d).SequenceEqual(layout.GetBounds().Select(b=>b.Location)),"Window move drifted local coordinates");
                    }
                    Check(linked.Link==cells[0]&&cells[0].Link==linked&&cells[0].Selected&&cells[1].Locked,"Install changed links/selection/locks");
                    d.ClearButton.InvokeMouseClick();
                    Check(d.ItemNameTextBox.TextBox.Text==string.Empty&&d.ItemTypeComboBox.SelectedItem==null,"Native clear callback lost");
                    for(int i=0;i<cells.Length;i++) Check(cells[i].Enabled==(i<capacity),"Native capacity filter changed "+i);
                    d.ItemNameTextBox.TextBox.Text="sword";
                    Check(cells[0].Enabled==(capacity>0)&&!cells[1].Enabled&&!cells[2].Enabled,"Live name filtering broken");
                    Console.WriteLine("Cycle "+cycle+" item callback"); var sword=cells[0].Item; cells[0].Item=cells[1].Item;
                    Check(!cells[0].Enabled,"Native item-change filter callback lost"); cells[0].Item=sword;
                    Check(cells[0].Enabled==(capacity>0),"Native item-change filter did not recover");
                    d.ItemNameTextBox.TextBox.Text=string.Empty; d.ItemTypeComboBox.ListBox.SelectItem(ItemType.Ring);
                    Check(!cells[0].Enabled&&cells[1].Enabled==(capacity>1),"Live type filtering broken");
                    d.ClearButton.InvokeMouseClick(); d.StorageScrollBar.Value=0;
                    cells[0].OnMouseWheel(new MouseEventArgs(MouseButtons.None,0,0,0,-120));
                    Check(d.StorageScrollBar.Value==Math.Min(1,rows-10)&&d.Grid.ScrollValue==d.StorageScrollBar.Value,"Original cell wheel callback broken/duplicated");
                    var shifted=Shift(layout,1,1); d.UseGameUiStorage(shifted);
                    Check(Positions(d).SequenceEqual(shifted.GetBounds().Select(b=>b.Location)),"Replacement applied wrong origin");
                    Check(Children(d).SequenceEqual(originalChildren)&&Children(controls[0]).SequenceEqual(filters),"Control parent/order changed");
                    for(int i=0;i<cells.Length;i++)
                    {
                        Check(Equals(callbacks[i],Event(cells[i],"MouseWheel")),"Cell wheel delegate replaced");
                        Check(Equals(itemHandlers[i],typeof(DXItemCell).GetField("ItemChanged",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(cells[i])),"Cell filter delegate replaced");
                    }
                    d.UseGameUiStorage(null);
                    Check(Positions(d).SequenceEqual(native)&&d.GameUiStorageLayout==null&&rebuilds==count,"Native positions not restored or grid rebuilt");
                    Check(linked.Link==cells[0]&&cells[0].Selected&&cells[1].Locked,"Restore changed live cell state");
                    // The existing close path still clears links and opens inventory on visibility.
                    d.Visible=true; Check(inventory.Visible,"Storage no longer opens original inventory");
                    d.CloseButton.InvokeMouseClick(); Check(!d.Visible&&linked.Link==null&&cells[0].Link==null,"Original close/link cleanup changed");
                }
            }
            Console.WriteLine("Failure controls starting"); d.UseGameUiStorage(layout); var active=Positions(d);
            Reject<ArgumentException>(()=>d.UseGameUiStorage(Shift(layout,100,0)),"Out-of-window layout accepted");
            Check(d.GameUiStorageLayout==layout&&Positions(d).SequenceEqual(active),"Rejected layout changed active binding");
            Console.WriteLine("Reparent rejection starting"); var filter=d.ItemNameTextBox; filter.Parent=root;
            Reject<InvalidOperationException>(()=>d.UseGameUiStorage(layout),"Reparented filter accepted"); filter.Parent=controls[0];
            Console.WriteLine("Replacement grid starting"); var originalGrid=d.Grid;
            using(var replacement=new DXItemGrid { GridSize=new Size(1,1) })
            {
                replacement.Parent=d; replacement.GridType=GridType.Storage; replacement.ItemGrid=CEnvir.Storage;
                replacement.VisibleHeight=10; replacement.GridSize=new Size(13,10); d.Grid=replacement; Reject<InvalidOperationException>(()=>d.UseGameUiStorage(layout),"Replacement grid accepted"); d.Grid=originalGrid;
            }
            Console.WriteLine("Observer failures starting"); bool once=true; EventHandler<EventArgs> fail=(s,e)=>{if(once){once=false;throw new InvalidOperationException("injected observer");}};
            d.Grid.LocationChanged+=fail;
            Reject<AggregateException>(()=>d.UseGameUiStorage(Shift(layout,1,1)),"Observer failure not surfaced"); d.Grid.LocationChanged-=fail;
            Check(d.GameUiStorageLayout==layout&&Positions(d).SequenceEqual(active),"Failed replacement did not restore previous layout");
            once=true; d.Grid.LocationChanged+=fail;
            Reject<AggregateException>(()=>d.UseGameUiStorage(null),"Failed disable not surfaced"); d.Grid.LocationChanged-=fail;
            Check(d.GameUiStorageLayout==layout&&Positions(d).SequenceEqual(active),"Failed disable lost binding");
            Console.WriteLine("Reentrant control starting"); bool reentrant=false; EventHandler<EventArgs> reenter=(s,e)=>{Reject<InvalidOperationException>(()=>d.UseGameUiStorage(null),"Reentrant mutation accepted");reentrant=true;};
            d.Grid.LocationChanged+=reenter; d.UseGameUiStorage(Shift(layout,1,1)); d.Grid.LocationChanged-=reenter; Check(reentrant,"Reentrant test did not execute");
            d.UseGameUiStorage(null); Check(Positions(d).SequenceEqual(native),"Final restore failed");
            Console.WriteLine("Undersized control starting"); d.Size=new Size(490,430); Reject<ArgumentException>(()=>d.UseGameUiStorage(layout),"Undersized native window accepted"); d.Size=new Size(508,480);
            once=true; d.Grid.LocationChanged+=fail;
            Reject<AggregateException>(()=>d.UseGameUiStorage(layout),"Initial install observer failure not surfaced"); d.Grid.LocationChanged-=fail;
            Check(d.GameUiStorageLayout==null&&Positions(d).SequenceEqual(native),"Initial install did not roll back");
            Console.WriteLine("Disposal control starting"); d.UseGameUiStorage(layout); d.Dispose(); Check(d.GameUiStorageLayout==null&&Field<object>(d,"gameUiStorageSnapshots")==null,"Dispose retained binding");
            Check(controls.All(c=>c.IsDisposed),"Dispose leaked original controls"); Reject<ObjectDisposedException>(()=>d.UseGameUiStorage(layout),"Disposed storage accepted");
        }
        using(var d=new StorageDialog {Parent=root,Size=new Size(508,480)})
        {
            scene.StorageBox=d; var controls=Controls(d); d.Grid.LocationChanged+=(s,e)=>d.Dispose();
            Reject<ObjectDisposedException>(()=>d.UseGameUiStorage(layout),"Disposal during install was accepted");
            Check(d.IsDisposed&&d.GameUiStorageLayout==null&&Field<object>(d,"gameUiStorageSnapshots")==null,"Disposal observer left an active layout");
            Check(controls.All(c=>c.IsDisposed),"Disposal observer leaked controls");
        }
        scene.StorageBox=null; scene.InventoryBox=null;
    }
    [STAThread] private static int Main(string[] args)
    {
        var oldStorage=CEnvir.Storage; var oldGame=GameScene.Game;
        try
        {
            // Load actual native metrics before the first DXControl static initialization.
            using(var native=new MirLibrary(Path.Combine(args[1],"Interface.Zl")))
            using(var game=new MirLibrary(Path.Combine(args[1],"GameInter.Zl")))
            using(var target=new TargetForm {ClientSize=new Size(1024,768)})
            {
                native.ReadLibrary(); game.ReadLibrary(); CEnvir.LibraryList[LibraryFile.Interface]=native; CEnvir.LibraryList[LibraryFile.GameInter]=game;
                CEnvir.Target=target; CEnvir.Storage=new ClientUserItem[1000];
                using(var root=new TestScene {IsVisible=true})
                {
                    DXControl.ActiveScene=root; var scene=(GameScene)FormatterServices.GetUninitializedObject(typeof(GameScene)); GC.SuppressFinalize(scene);
                    var user=(UserObject)FormatterServices.GetUninitializedObject(typeof(UserObject)); Set(scene,"_User",user); scene.Inventory=new ClientUserItem[49]; GameScene.Game=scene;
                    Geometry(args[0]); Adapt(root,scene);
                }
            }
            Console.WriteLine("Storage layout: "+checks+" checks passed; real native control dimensions; placeholder plus 0/27/131/1000 capacities; filters, scroll, identity, event delegates, rollback, reentry and disposal. No rendering/network/live acceptance."); return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally{GameScene.Game=oldGame;CEnvir.Storage=oldStorage;CEnvir.Target=null;DXControl.ActiveScene=null;}
    }
}
