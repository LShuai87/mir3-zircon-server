using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Client;
using Client.Controls;
using Client.Envir;
using Client.Scenes.Views;
using Client.UserModels;
using Library.MirDB;
using MirDB;

internal static class OfficialUiSceneLayoutHostTests
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static void Reject<T>(Action action, string message) where T : Exception
    { bool failed = false; try { action(); } catch (T) { failed = true; } Check(failed, message); }
    private static readonly Size[] Resolutions = { new Size(1024,768), new Size(1366,768), new Size(1280,800), new Size(1440,900), new Size(1600,900), new Size(1920,1080) };
    private sealed class Root : DXControl { public DXControl Topmost => Controls.Last(); }
    private sealed class Window : DXWindow
    {
        public WindowType Identity = WindowType.CharacterBox;
        public override WindowType Type => Identity;
        public override bool CustomSize => true;
        public override bool AutomaticVisiblity => true;
    }
    private static GameUiLayoutRequest Request(Size size, GameUiAnchor anchor = GameUiAnchor.TopLeft, bool resize = false)
        => new GameUiLayoutRequest(size, size, anchor, Point.Empty, true, resize, false, false);
    private static int Top(ChatTab tab, DXLabel label)
    { int y=0; foreach(var item in tab.History) { if(ReferenceEquals(item,label)) return y; y+=item.Size.Height; } throw new Exception("Missing message"); }
    private static ChatTab Tab(DXTabControl parent, string name)
    {
        var tab = new ChatTab { Parent=parent };
        tab.TabButton.Label.Text = name;
        for(int i=0;i<36;i++) tab.History.Add(new DXLabel {
            Parent=tab.TextPanel, AutoSize=false,
            Text=name+" "+i+" existing message with enough words to wrap as the frame width changes, preserving colors and identity.",
            ForeColour=i%2==0 ? Color.Red : Color.Yellow, Outline=true });
        tab.ResizeChat();
        return tab;
    }
    private static void Tests(string directory)
    {
        Config.GameSize=Resolutions[0];
        var session=new Session(SessionMode.Users, Path.Combine(directory,"legacy","Data")+Path.DirectorySeparatorChar) { BackUp=false };
        var collections=(Dictionary<Type,ADBCollection>)typeof(Session).GetField("Collections",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(session);
        collections[typeof(WindowSetting)]=new DBCollection<WindowSetting>(session);
        CEnvir.WindowSettings=session.GetCollection<WindowSetting>();
        CEnvir.Loaded=true;
        var setting=CEnvir.WindowSettings.CreateNewObject();
        setting.Resolution=Config.GameSize; setting.Window=WindowType.CharacterBox;
        setting.Location=new Point(300,200); setting.Size=new Size(180,140); setting.Visible=true;
        var store=new GameUiLayoutStore(Path.Combine(directory,"official"));
        Check(store.TrySave(Resolutions[0],WindowType.CharacterBox,new GameUiWindowLayout(new Point(30,40),new Size(180,140),true)),"Seed layout");
        Check(store.TrySave(Resolutions[1],WindowType.CharacterBox,new GameUiWindowLayout(new Point(80,50),new Size(180,140),false)),"Seed second resolution");
        string saved=File.ReadAllText(store.FilePath);
        using(var root=new Root { Size=Resolutions[0], Location=new Point(5,7), IsVisible=true })
        using(var otherRoot=new DXControl { Size=Resolutions[0] })
        using(var character=new Window { Parent=root, Size=new Size(180,140) })
        using(var npc=new Window { Parent=root, Size=new Size(130,100), Identity=WindowType.InventoryBox })
        using(var tabs=new DXTabControl { Parent=root, Location=new Point(0,500), Size=new Size(470,180) })
        using(var input=new DXTextBox { Parent=root, Location=new Point(5,685), Size=new Size(420,20) })
        using(var overlay=new DXControl { Parent=root, Size=new Size(20,20), Sort=true })
        {
            var first=Tab(tabs,"Local"); var second=Tab(tabs,"System"); tabs.SelectedTab=second;
            var history=second.History.ToArray();
            input.TextBox.Text="unsent draft 中文"; input.TextBox.SelectionStart=4; input.TextBox.SelectionLength=3;
            IntPtr inputHandle=input.TextBox.Handle;
            DXTextBox.ActiveTextBox=input;
            input.TextBox.SelectionStart=4; input.TextBox.SelectionLength=3;
            Control nativeActive=CEnvir.Target.ActiveControl;
            int restores=0, skins=0, overlays=0;
            var host=new GameUiSceneLayoutHost(root,store,()=>restores++,()=> { overlays++; overlay.BringToFront(); });
            host.Register("character",character,()=>skins++,s=>Request(new Size(180,140),resize:true),true);
            host.Register("npc",npc,()=>{},s=>Request(new Size(130,100)));
            host.Register("chat",tabs,()=>{},s=>Request(new Size(s.Width==1024?470:600,180),GameUiAnchor.BottomLeft));
            host.Register("input",input,()=>{},s=>new GameUiLayoutRequest(new Size(420,20),new Size(420,20),GameUiAnchor.BottomLeft,new Point(0,184),false,false,false,false));
            host.AddGroup(new GameUiLayoutGroup("character","npc",4,24));
            var coordinator=new GameUiLayoutCoordinator(host);
            Check(coordinator.Apply(Config.GameSize,new Rectangle(0,0,1024,580)),"Initial layout");
            Check(restores==1 && character.Settings==null,"Initial restore count and routing");
            Check(File.ReadAllText(store.FilePath)==saved,"Initial layout wrote saved file");
            Check(root.Topmost==overlay,"Overlay must stay above grouped service");
            character.Visible=false; // Explicit business change persists outside layout.
            string visibilitySaved=File.ReadAllText(store.FilePath);
            DXLabel anchor=history[8];
            for(int pass=0;pass<24;pass++) foreach(Size resolution in Resolutions)
            {
                Config.GameSize=resolution; root.Size=resolution;
                second.ScrollBar.Value=Top(second,anchor)+2;
                first.ScrollBar.Value=Math.Max(0,first.ScrollBar.MaxValue-first.ScrollBar.VisibleSize);
                Color color=history[8].ForeColour; Font font=history[8].Font;
                Check(coordinator.Apply(resolution,new Rectangle(0,0,resolution.Width,resolution.Height-188)),"Resize layout");
                Check(second.ScrollBar.Value==Top(second,anchor)+2,"Scroll message anchor lost after wrapping");
                Check(first.ScrollBar.Value==Math.Max(0,first.ScrollBar.MaxValue-first.ScrollBar.VisibleSize),"Bottom-follow lost");
                Check(tabs.SelectedTab==second && history.SequenceEqual(second.History) && !first.IsDisposed,"Chat identity/selection lost");
                Check(history[8].ForeColour==color && ReferenceEquals(history[8].Font,font),"Chat style changed");
                Check(!character.Visible && character.Settings==null,"Resize restored business visibility or Legacy route");
                Check(input.TextBox.Text=="unsent draft 中文" && input.TextBox.SelectionStart==4 && input.TextBox.SelectionLength==3,"Input text/selection lost");
                Check(input.TextBox.Handle==inputHandle && DXTextBox.ActiveTextBox==input && CEnvir.Target.ActiveControl==nativeActive,"Input handle/active control changed");
                Check(input.TextBox.Bounds==input.DisplayArea,"Native input bounds stale: actual="+input.TextBox.Bounds+" expected="+new Rectangle(input.DisplayArea.Location,input.Size)+" display="+input.DisplayArea);
                Check(root.Topmost==overlay,"Overlay order changed");
            }
            Check(restores==1 && skins==145 && overlays==145,"Resize reloaded chat or skipped layout callback");
            Check(File.ReadAllText(store.FilePath)==visibilitySaved,"Programmatic resize wrote OfficialV1 layout");
            Check(CEnvir.WindowSettings.Count==1 && setting.Location==new Point(300,200) && setting.Size==new Size(180,140) && setting.Visible,"Legacy settings changed");
            Reject<ArgumentException>(()=>host.Register("chat",new DXControl { Parent=root },()=>{},s=>Request(new Size(1,1))),"Duplicate ID accepted");
            Reject<ArgumentException>(()=>host.Register("duplicate",tabs,()=>{},s=>Request(new Size(1,1))),"Duplicate control accepted");
            using(var foreign=new DXControl { Parent=otherRoot })
                Reject<ArgumentException>(()=>host.Register("foreign",foreign,()=>{},s=>Request(new Size(1,1))),"Foreign control accepted");
            Exception threadError=null;
            var thread=new Thread(()=> { try { host.GetParticipants(); } catch(Exception error) { threadError=error; } }); thread.Start(); thread.Join();
            Check(threadError is InvalidOperationException,"Foreign thread accessed host");
            DXTextBox.ActiveTextBox=null;
        }
        Config.GameSize=Resolutions[0];
        using(var root=new DXControl()) using(var other=new DXControl())
        using(var legacy=new Window { Parent=root, Settings=setting, Size=new Size(180,140) })
        using(var foreign=new Window { Parent=other, Settings=CEnvir.WindowSettings.CreateNewObject() })
        {
            var originalLocation=setting.Location; var originalSize=setting.Size; bool originalVisible=setting.Visible;
            IDisposable outer=DXWindow.SuppressSceneLayoutWrites(root), inner=DXWindow.SuppressSceneLayoutWrites(root);
            outer.Dispose(); outer.Dispose();
            legacy.IsMoving=true; legacy.Location=new Point(11,22); legacy.IsMoving=false;
            legacy.IsResizing=true; legacy.Size=new Size(210,170); legacy.IsResizing=false; legacy.Visible=!originalVisible;
            using(var late=new Window { Parent=root, Settings=setting })
            { late.Visible=originalVisible; late.Visible=!originalVisible; late.IsMoving=true; late.Location=new Point(99,88); late.IsMoving=false; }
            int count=CEnvir.WindowSettings.Count;
            using(var late=new Window { Parent=root, Identity=WindowType.InventoryBox }) { late.LoadSettings(); Check(late.Settings==null,"New legacy record created in layout"); }
            Check(CEnvir.WindowSettings.Count==count,"Late LoadSettings wrote Users collection");
            foreign.IsMoving=true; foreign.Location=new Point(5,6); foreign.IsMoving=false;
            Check(foreign.Settings.Location==new Point(5,6),"Other scene suppressed");
            Check(setting.Location==originalLocation && setting.Size==originalSize && setting.Visible==originalVisible,"Nested/late-window suppression failed");
            inner.Dispose(); inner.Dispose();
            legacy.IsMoving=true; legacy.Location=new Point(13,14); legacy.IsMoving=false;
            Check(setting.Location==new Point(13,14),"Writes did not resume");
            int restored=0;
            var host=new GameUiSceneLayoutHost(root,store,()=>restored++,()=>{});
            host.Register("legacy",legacy,()=> { throw new InvalidOperationException("deliberate skin failure"); },s=>Request(new Size(180,140)));
            var coordinator=new GameUiLayoutCoordinator(host);
            Reject<InvalidOperationException>(()=>coordinator.Apply(Config.GameSize,new Rectangle(0,0,1024,580)),"Skin exception swallowed");
            legacy.IsMoving=true; legacy.Location=new Point(15,16); legacy.IsMoving=false;
            Check(setting.Location==new Point(15,16),"Failure leaked suppression");
            Check(restored==1,"Chat restored incorrectly on failed skin");
        }
        using(var root=new DXControl()) using(var control=new DXControl { Parent=root, Size=new Size(20,20) })
        {
            GameUiSceneLayoutHost host=null; int restore=0;
            host=new GameUiSceneLayoutHost(root,store,()=> { restore++; host.Register("restored",control,()=>{},s=>Request(new Size(20,20))); },()=>{});
            var coordinator=new GameUiLayoutCoordinator(host); coordinator.Apply(Config.GameSize,new Rectangle(0,0,1024,580));
            Check(restore==1 && coordinator.LastPlacements.ContainsKey("restored"),"Initial chat registration failed");
        }
        using(var root=new DXControl()) using(var control=new DXControl { Parent=root, Size=new Size(20,20) })
        {
            var host=new GameUiSceneLayoutHost(root,store,()=>{},()=>{});
            host.Register("unimplemented-scroll",control,()=>{},s=>new GameUiLayoutRequest(new Size(100,100),new Size(20,20),GameUiAnchor.TopLeft,Point.Empty,true,false,true,true));
            Reject<InvalidOperationException>(()=>new GameUiLayoutCoordinator(host).Apply(Config.GameSize,new Rectangle(0,0,1024,580)),"False scroll capability accepted");
            Check(control.Size==new Size(20,20),"Invalid scroll request changed control bounds");
        }
    }
    [STAThread]
    private static int Main()
    {
        string root=Path.Combine(Path.GetTempPath(),"ZirconSceneLayoutHost-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        Size originalSize=Config.GameSize;
        try { using(var target=new TargetForm()) { CEnvir.Target=target; Tests(root); } Console.WriteLine("Scene layout host: "+checks+" checks passed; 145 real-control layouts across six resolutions."); return 0; }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { DXTextBox.ActiveTextBox=null; CEnvir.Target=null; CEnvir.Loaded=false; CEnvir.WindowSettings=null; Config.GameSize=originalSize; Directory.Delete(root,true); }
    }
}
