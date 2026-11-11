using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Client;
using Client.Controls;
using Client.Envir;
using Client.Scenes.Views;
using Library;

internal static class OfficialUiChatLayoutTests
{
    private static int checks;
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }
    private static void Reject<T>(Action action, string message) where T : Exception
    { bool rejected=false; try { action(); } catch(T) { rejected=true; } Check(rejected,message); }
    private static GameUiChatViewportLayout Viewport()
        => new GameUiChatViewportLayout(new Padding(4,3,4,4),16,2);
    private static GameUiChatInputLayout Input()
        => new GameUiChatInputLayout(new Padding(4,1,4,1),56,22,4);
    private static int Top(ChatTab tab, DXLabel message)
    {
        int y=0;
        foreach(var label in tab.History) { if(ReferenceEquals(label,message)) return y; y+=label.Size.Height; }
        throw new Exception("Missing message");
    }
    private static void SetBody(DXTabControl parent, ChatTab tab, Size size)
    { parent.Size=new Size(size.Width+tab.Location.X,size.Height+tab.Location.Y); Check(tab.Size==size,"Body size differs"); }
    private static void CheckLabels(ChatTab tab, DXLabel[] labels, Font[] fonts, Color[] colors, object[] tags)
    {
        Check(tab.History.Count==labels.Length,"History count changed");
        int y=-tab.ScrollBar.Value;
        for(int i=0;i<labels.Length;i++)
        {
            var label=tab.History[i];
            Check(ReferenceEquals(label,labels[i]) && !label.IsDisposed,"Message replaced");
            Check(ReferenceEquals(label.Font,fonts[i]) && label.ForeColour==colors[i] && ReferenceEquals(label.Tag,tags[i]),"Message style or channel tag changed");
            Check(label.Parent==tab.TextPanel && label.Location==new Point(0,y),"Message placement differs from scroll");
            y+=label.Size.Height;
        }
    }
    private static void Geometry()
    {
        Rectangle history,bar,mode,input,options;
        var viewport=Viewport(); viewport.GetBounds(new Size(574,83),out history,out bar);
        Check(history==new Rectangle(4,3,548,76) && bar==new Rectangle(554,3,16,76),"1024 history contract");
        var row=Input(); row.GetBounds(new Size(574,22),out mode,out input,out options);
        Check(mode==new Rectangle(4,1,56,20) && input==new Rectangle(64,1,480,20) && options==new Rectangle(548,1,22,20),"1024 input contract");
        foreach(int width in new[]{574,720,1024}) foreach(int height in new[]{83,120,200})
        {
            viewport.GetBounds(new Size(width,height),out history,out bar);
            Check(new Rectangle(Point.Empty,new Size(width,height)).Contains(history) && !history.IntersectsWith(bar),"Viewport overlap or escape");
            Check(bar.Height>50,"Scrollbar drag divisor could be zero");
        }
        Reject<ArgumentOutOfRangeException>(()=>new GameUiChatViewportLayout(new Padding(-1,0,0,0),16,2),"Negative padding accepted");
        Reject<ArgumentOutOfRangeException>(()=>new GameUiChatViewportLayout(new Padding(0),13,2),"Narrow scrollbar accepted");
        Reject<ArgumentOutOfRangeException>(()=>new GameUiChatViewportLayout(new Padding(0),16,-1),"Negative viewport gap accepted");
        Reject<ArgumentOutOfRangeException>(()=>new GameUiChatInputLayout(new Padding(0),0,22,4),"Zero mode accepted");
        Reject<ArgumentOutOfRangeException>(()=>new GameUiChatInputLayout(new Padding(0),56,257,4),"Oversized options accepted");
        Reject<ArgumentOutOfRangeException>(()=>new GameUiChatInputLayout(new Padding(257,0,0,0),56,22,4),"Oversized padding accepted");
        Reject<ArgumentOutOfRangeException>(()=>new GameUiChatInputLayout(new Padding(0),56,22,-1),"Negative input gap accepted");
        Reject<ArgumentException>(()=>viewport.GetBounds(new Size(574,57),out history,out bar),"Too short viewport accepted");
        Reject<ArgumentException>(()=>row.GetBounds(new Size(574,21),out mode,out input,out options),"Too short input accepted");
    }
    private static void History()
    {
        int tabCount=ChatTab.Tabs.Count;
        using(var root=new DXControl { Size=new Size(1024,768), IsVisible=true })
        using(var parent=new DXTabControl { Parent=root, Size=new Size(574,110) })
        using(var tab=new ChatTab { Parent=parent })
        using(var other=new ChatTab { Parent=parent })
        {
            parent.SelectedTab=tab; tab.TabButton.Label.Text="本地";
            SetBody(parent,tab,new Size(574,83));
            Check(tab.GameUiViewportLayout==null && tab.ScrollBar.Size.Width==14 && tab.TextPanel.Location==new Point(9,9),"Legacy default changed");
            for(int i=0;i<48;i++) tab.History.Add(new DXLabel {
                Parent=tab.TextPanel, AutoSize=false, Text="消息 "+i+" 中文和 English existing history wraps when the frame narrows. Keep the original style, channel, order, and reference.",
                ForeColour=i%2==0?Color.Red:Color.Yellow, Outline=true, Tag=new object() });
            tab.ResizeChat();
            var labels=tab.History.ToArray(); var fonts=labels.Select(l=>l.Font).ToArray();
            var colors=labels.Select(l=>l.ForeColour).ToArray(); var tags=labels.Select(l=>l.Tag).ToArray();
            var textPanel=tab.TextPanel; var scrollbar=tab.ScrollBar; var button=tab.TabButton;
            tab.ScrollBar.Value=Top(tab,labels[12])+3;
            tab.UseGameUiViewport(Viewport());
            Check(tab.TextPanel.Location==new Point(4,3) && tab.TextPanel.Size==new Size(548,76),"Actual history bounds");
            Check(tab.ScrollBar.Location==new Point(554,3) && tab.ScrollBar.Size==new Size(16,76),"Actual scrollbar bounds");
            Check(tab.ScrollBar.Value==Top(tab,labels[12])+3,"First message lost on first apply");
            foreach(var size in new[]{new Size(350,110),new Size(574,83),new Size(720,135),new Size(440,95)})
            {
                tab.ScrollBar.Value=Top(tab,labels[12])+3;
                SetBody(parent,tab,size);
                Check(tab.ScrollBar.Value==Top(tab,labels[12])+3,"Anchor lost on parent resize");
                CheckLabels(tab,labels,fonts,colors,tags);
                Check(parent.SelectedTab==tab && ReferenceEquals(tab.TabButton,button) && ReferenceEquals(tab.TextPanel,textPanel) && ReferenceEquals(tab.ScrollBar,scrollbar),"Tab or child replaced");
                tab.ScrollBar.Value=tab.ScrollBar.MaxValue-tab.ScrollBar.VisibleSize;
                SetBody(parent,tab,new Size(size.Width+10,size.Height+12));
                Check(tab.ScrollBar.Value==Math.Max(0,tab.ScrollBar.MaxValue-tab.ScrollBar.VisibleSize),"Bottom follow lost");
            }
            // Exercise real resize events and their parent-size propagation.
            tab.ScrollBar.Value=Top(tab,labels[10])+2;
            tab.IsResizing=true; tab.Size=new Size(500,100); tab.IsResizing=false;
            Check(tab.ScrollBar.Value==Top(tab,labels[10])+2,"Anchor lost after drag reflow");
            Check(tab.GetAcceptableResize(Size.Empty).Height>=58,"Drag accepts zero-divisor scrollbar height");
            tab.Size=Size.Empty;
            Check(tab.Size.Width>=27 && tab.Size.Height>=58 && tab.ScrollBar.Size.Height>=51,"Programmatic minimum not enforced");
            SetBody(parent,tab,new Size(574,83));
            tab.UseGameUiViewport(null);
            Check(tab.GameUiViewportLayout==null && tab.TextPanel.Location==new Point(9,9) && tab.ScrollBar.Size.Width==14,"Legacy reset failed");
            CheckLabels(tab,labels,fonts,colors,tags);
            var invalid=new GameUiChatViewportLayout(new Padding(256),256,256);
            Reject<ArgumentException>(()=>tab.UseGameUiViewport(invalid),"Invalid apply accepted");
            Check(tab.GameUiViewportLayout==null,"Rejected apply changed layout");
            other.Dispose(); Reject<ObjectDisposedException>(()=>other.UseGameUiViewport(Viewport()),"Disposed tab accepted");
        }
        Check(ChatTab.Tabs.Count==tabCount,"Static tab registry leaked");
    }
    private static void InputState()
    {
        using(var root=new DXControl { Size=new Size(1024,768), Location=new Point(5,7), IsVisible=true })
        using(var chat=new ChatTextBox { Parent=root, Size=new Size(574,22), Location=new Point(225,738), Visible=true })
        {
            var mode=chat.ChatModeButton; var options=chat.OptionsButton; var box=chat.TextBox; var native=box.TextBox;
            Size legacyMode=mode.Size,legacyOptions=options.Size;
            chat.Mode=ChatMode.Guild; chat.LastPM="/此前角色";
            native.Text="未发送的中文草稿 English"; native.Select(2,4);
            var handle=native.Handle; var font=box.Font; int maxLength=box.MaxLength;
            DXTextBox.ActiveTextBox=box;
            int modeChanges=0,textChanges=0,optionEvents=0;
            chat.ModeChanged+=(s,e)=>modeChanges++;
            native.TextChanged+=(s,e)=>textChanges++;
            options.MouseClick+=(s,e)=>optionEvents++;
            int selectionStart=native.SelectionStart,selectionLength=native.SelectionLength;
            chat.UseGameUiInputLayout(Input());
            Check(mode.Location==new Point(4,1) && mode.Size==new Size(56,20),"Mode bounds differ");
            Check(options.Location==new Point(548,1) && options.Size==new Size(22,20),"Options bounds differ");
            Check(box.Location==new Point(64,1) && box.Size.Width==480,"Input bounds differ");
            foreach(var resolution in new[]{new Size(1024,768),new Size(1366,768),new Size(1280,800),new Size(1440,900),new Size(1600,900),new Size(1920,1080)})
            {
                root.Size=resolution; chat.Location=new Point((resolution.Width-1024)/2+225,resolution.Height-30);
                foreach(int width in new[]{574,660,420,574})
                {
                    chat.Size=new Size(width,22);
                    Check(native.Handle==handle && ReferenceEquals(chat.TextBox,box) && ReferenceEquals(chat.ChatModeButton,mode) && ReferenceEquals(chat.OptionsButton,options),"Input or HWND replaced");
                    Check(native.Text=="未发送的中文草稿 English" && native.SelectionStart==selectionStart && native.SelectionLength==selectionLength,"Draft or selection lost");
                    Check(chat.Mode==ChatMode.Guild && chat.LastPM=="/此前角色" && ReferenceEquals(box.Font,font) && box.MaxLength==maxLength,"Input state changed");
                    Check(DXTextBox.ActiveTextBox==box && native.Bounds==box.DisplayArea,"Focus or native IME location differs: active="+(DXTextBox.ActiveTextBox==box)+" native="+native.Bounds+" display="+box.DisplayArea);
                }
            }
            Check(modeChanges==0 && textChanges==0 && optionEvents==0,"Layout triggered input events");
            Check(chat.GetAcceptableResize(Size.Empty).Width>=95,"Input drag minimum missing");
            chat.Size=Size.Empty; Check(chat.Size.Height>=22 && box.Size.Width>0,"Input minimum not enforced");
            chat.Size=new Size(574,22); chat.UseGameUiInputLayout(null);
            Check(chat.GameUiInputLayout==null && mode.Size==legacyMode && options.Size==legacyOptions,"Input legacy dimensions not restored");
            Check(native.Handle==handle && native.Text=="未发送的中文草稿 English" && DXTextBox.ActiveTextBox==box,"Legacy reset lost input");
            var invalid=new GameUiChatInputLayout(new Padding(256),256,256,256);
            Reject<ArgumentException>(()=>chat.UseGameUiInputLayout(invalid),"Invalid input apply accepted");
            Check(chat.GameUiInputLayout==null,"Rejected input changed layout");
            DXTextBox.ActiveTextBox=null;
        }
        var disposed=new ChatTextBox(); disposed.Dispose();
        Reject<ObjectDisposedException>(()=>disposed.UseGameUiInputLayout(Input()),"Disposed input accepted");
    }
    private static void Groups()
    {
        int count=ChatTab.Tabs.Count;
        using(var root=new DXControl { Size=new Size(1920,1080), IsVisible=true })
        using(var parent=new DXTabControl { Parent=root, Size=new Size(574,106), Location=new Point(225,630), Movable=true })
        using(var target=new DXTabControl { Parent=root, Size=new Size(340,180), Location=new Point(10,10) })
        {
            var tabs=Enumerable.Range(0,12).Select(i=>new ChatTab { Parent=parent, AllowResize=true }).ToArray();
            for(int i=0;i<tabs.Length;i++) { tabs[i].TabButton.Label.Text="频道"+i; tabs[i].TabButton.Movable=true; }
            var first=tabs[0]; parent.SelectedTab=first;
            for(int i=0;i<40;i++) first.History.Add(new DXLabel { Parent=first.TextPanel, AutoSize=false,
                Text="保留消息 "+i+" 中文频道 old chat text wraps across resized window.", ForeColour=Color.Lime, Outline=false });
            first.ResizeChat(); first.ScrollBar.Value=Top(first,first.History[8])+2;
            var messages=first.History.ToArray(); var font=messages[0].Font;
            var nativeButtons=parent.TabButtons.ToArray();
            var legacyBounds=new Rectangle(parent.Location,parent.Size);
            var buttonSize=first.TabButton.Size;
            using(var binding=new GameUiChatGroupBinding(parent))
            {
                Check(parent.GameUiChatGroup==binding && parent.TabButtons.SequenceEqual(nativeButtons),"Group replaced or reordered native headers");
                Check(first.Location==new Point(0,24) && first.Size==new Size(574,82),"Group page contract");
                Check(first.TextPanel.DisplayArea.Location==new Point(229,656) && first.TextPanel.Size==new Size(548,76),"Absolute history contract");
                Check(first.ScrollBar.Location==new Point(554,2) && first.ScrollBar.Size==new Size(16,76),"Grouped scrollbar contract");
                Check(first.ScrollBar.Value==Top(first,messages[8])+2,"Group install moved scroll anchor");
                Check(tabs.All(t=>t.AllowResize && t.TabButton.Movable && t.FixedGeometry==null) && parent.Movable,"Native drag or resize disabled");
                Reject<InvalidOperationException>(()=>new GameUiChatGroupBinding(parent),"Two owners accepted");
                Check(((System.Collections.Generic.List<DXControl>)typeof(DXControl).GetProperty("Controls", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(parent)).OfType<DXButton>().Count(b=>b.Hint=="Next chat tabs")==1,"Header overflow lacks paging");
                var next=((System.Collections.Generic.List<DXControl>)typeof(DXControl).GetProperty("Controls", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(parent)).OfType<DXButton>().Single(b=>b.Hint=="Next chat tabs");
                next.InvokeMouseClick();
                Check(binding.FirstVisibleTabIndex>0 && !first.TabButton.Visible,"Paging did not expose later tabs");
                parent.SelectedTab=first;
                // Selection did not change, so explicitly request the same tab's header.
                binding.RevealSelectedTab();
                Check(first.TabButton.Visible && binding.FirstVisibleTabIndex==0,"Selected header not revealed");
                parent.SelectedTab=tabs[11];
                Check(tabs[11].TabButton.Visible && tabs[11].Selected && !first.Selected,"Off-page selection lost");
                tabs[11].TabButton.Label.Text=new string('W',200);
                Check(tabs[11].TabButton.Location.X+tabs[11].TabButton.Size.Width<=parent.Size.Width-40,"Long title escaped header viewport");
                foreach(var size in new[]{new Size(720,160),new Size(340,130),new Size(574,106),new Size(106,81)})
                {
                    first.ScrollBar.Value=Top(first,messages[8])+2;
                    parent.Size=size;
                    Check(first.Size==new Size(size.Width,size.Height-24),"Group resize differs from frame");
                    Check(first.ScrollBar.Size.Height>=51,"Grouped scrollbar allows zero divisor");
                    Check(first.ScrollBar.Value==Top(first,messages[8])+2,"Group resize lost message anchor");
                    Check(first.History.SequenceEqual(messages) && ReferenceEquals(messages[0].Font,font) && messages[0].ForeColour==Color.Lime,"Group changed text content/style");
                }
                parent.Size=Size.Empty;
                Check(parent.Size==GameUiChatGroupBinding.MinimumSize,"Group minimum not enforced");
                parent.Size=new Size(574,106);
                first.IsResizing=true; first.Size=new Size(500,100); first.IsResizing=false;
                Check(parent.Size==new Size(500,124) && first.Location==new Point(0,24),"Native tab resize corrupted group geometry");
                // Move an existing tab into an unadapted group, then back. No reconstruction.
                var moving=tabs[4]; moving.Parent=target;
                Check(moving.GameUiViewportLayout==null && !moving.IsDisposed,"Leaving group retained geometry owner");
                Check(moving.Location.Y==DXTab.TabHeight-1,"Leaving group retained official header height");
                moving.Parent=parent;
                Check(moving.GameUiViewportLayout!=null && moving.Location==new Point(0,24),"Re-entered group not adapted");
                using(var added=new ChatTab { Parent=parent })
                {
                    added.TabButton.Label.Text="新增";
                    Check(added.GameUiViewportLayout!=null && added.Location.Y==24,"New native tab not adapted");
                }
                parent.Size=new Size(574,106);
                first.ScrollBar.Value=Top(first,messages[8])+2;
            }
            Check(parent.GameUiChatGroup==null && first.GameUiViewportLayout==null,"Group rollback retained owner");
            Check(new Rectangle(parent.Location,parent.Size)==legacyBounds && first.TabButton.Size==buttonSize,"Group rollback changed original bounds");
            Check(first.ScrollBar.Value==Top(first,messages[8])+2,"Group rollback lost message anchor");
            Check(first.History.SequenceEqual(messages) && tabs[11].TabButton.Label.Text==new string('W',200),"Rollback rewound user data/title");
            Check(parent.TabButtons.Count==12 && tabs.All(t=>!t.IsDisposed),"Tab lifecycle changed");
            foreach(var tab in tabs.Skip(1)) tab.Dispose();
            using(var binding=new GameUiChatGroupBinding(parent))
            {
                first.Parent=new DXTabControl { Parent=root, Size=new Size(340,180) };
                Check(parent.IsDisposed,"Native cleanup kept an empty adapted group alive");
            }
        }
        Check(ChatTab.Tabs.Count==count,"Grouped chat leaked registry entries");
        using(var group=new DXTabControl { Size=new Size(300,100) })
        using(var tab=new DXTab { Parent=group })
            Reject<InvalidOperationException>(()=>new GameUiChatGroupBinding(group),"Non-chat group accepted");
    }
    private static void RetryRollback()
    {
        using(var root=new DXControl {Size=new Size(1024,768)})
        using(var group=new DXTabControl {Parent=root,Size=new Size(400,147),Location=new Point(200,450)})
        using(var tab=new ChatTab {Parent=group})
        {
            var bounds=new Rectangle(group.Location,group.Size);
            for(int i=0;i<30;i++)tab.History.Add(new DXLabel {Parent=tab.TextPanel,Text="Rollback anchor "+i+new string('x',70)});
            tab.UpdateItems();
            var messages=tab.History.ToArray();
            var binding=new GameUiChatGroupBinding(group);group.Size=new Size(574,106);
            tab.ScrollBar.Value=Top(tab,messages[8])+2;
            bool fail=true;EventHandler<EventArgs> observer=(s,e)=>{if(fail){fail=false;throw new InvalidOperationException("one-time rollback observer");}};
            tab.TextPanel.SizeChanged+=observer;
            Reject<InvalidOperationException>(()=>binding.Dispose(),"Rollback observer failure not surfaced");
            tab.TextPanel.SizeChanged-=observer;binding.Dispose();binding.Dispose();
            Check(!fail&&group.GameUiChatGroup==null&&tab.GameUiViewportLayout==null&&new Rectangle(group.Location,group.Size)==bounds,"Rollback retry did not restore geometry");
            Check(tab.History.SequenceEqual(messages)&&tab.ScrollBar.Value==Top(tab,messages[8])+2,"Rollback retry lost retained message anchor");
        }
    }
    [STAThread]
    private static int Main()
    {
        try
        {
            using(var target=new TargetForm()) { CEnvir.Target=target; Geometry(); History(); InputState(); Groups(); RetryRollback(); }
            Console.WriteLine("Chat geometry: "+checks+" checks passed; real controls, retained history/scroll and native draft/selection/HWND across six resolutions; no login or network.");
            return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { DXTextBox.ActiveTextBox=null; CEnvir.Target=null; }
    }
}
