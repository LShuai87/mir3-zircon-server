using System;
using System.Collections.Concurrent;
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
using Library.Network;
using C = Library.Network.ClientPackets;
using S = Library.Network.ServerPackets;

internal static class OfficialUiCharacterControlTests
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static void Reject<T>(Action action, string message) where T:Exception
    { bool rejected=false; try {action();} catch(T) {rejected=true;} Check(rejected,message); }
    private static T Field<T>(object owner,string name) => (T)owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner);
    private static void SetField(object owner,string name,object value) => owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(owner,value);
    private static List<DXControl> Children(DXControl c) => (List<DXControl>)typeof(DXControl).GetProperty("Controls",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(c);
    private static int Handlers(DXControl c,string name) => ((Delegate)typeof(DXControl).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(c))?.GetInvocationList().Length ?? 0;
    private static Rectangle Bounds(CharacterDialog d,DXControl c) => new Rectangle(c.DisplayArea.X-d.DisplayArea.X,c.DisplayArea.Y-d.DisplayArea.Y,c.Size.Width,c.Size.Height);
    private static string Rect(Rectangle r) => string.Join(",",new[]{r.X,r.Y,r.Width,r.Height});
    private sealed class TestScene : DXScene { internal TestScene(Size size):base(size) {} }
    private static DXControl Page(CharacterDialog d,GameUiCharacterControlPage page) => page==GameUiCharacterControlPage.Chrome ? (DXControl)d : Field<DXTab>(d,page==GameUiCharacterControlPage.Character?"CharacterTab":"HermitTab");
    private static Dictionary<string,DXControl> Originals(CharacterDialog d)
    {
        var result=new Dictionary<string,DXControl> {
            {"control.CharacterNameLabel",d.CharacterNameLabel},{"control.GuildNameLabel",d.GuildNameLabel},
            {"control.GuildRankLabel",d.GuildRankLabel},{"control.HandWeightLabel",d.HandWeightLabel},
            {"control.WearWeightLabel",d.WearWeightLabel},{"control.RemainingLabel",d.RemainingLabel},
            {"control.MarriageIcon",d.MarriageIcon},{"control.ShowHelmetBox",d.ShowHelmetBox},
            {"hermit.confirmation",Field<DXCheckBox>(d,"gameUiHermitConfirmation")}
        };
        foreach(var p in Field<Dictionary<string,DXLabel>>(d,"gameUiDetailCaptions")) result.Add("caption.existing."+p.Key,p.Value);
        foreach(var p in Field<Dictionary<Stat,DXButton>>(d,"gameUiHermitButtons")) result.Add("action.hermit."+p.Key,p.Value);
        return result;
    }
    private sealed class Saved
    {
        private readonly DXControl c,parent;
        private readonly Point location;
        private readonly Size size;
        private readonly object tag;
        private readonly int clicks,texts;
        private readonly bool autoSize,fixedSize,useOffset;
        private readonly TextFormatFlags format;
        private readonly string text,buttonText,checkText;
        private readonly DXCheckBoxGeometry checkGeometry;
        internal Saved(DXControl control)
        {
            c=control; parent=c.Parent; location=c.Location; size=c.Size; tag=c.Tag; clicks=Handlers(c,"MouseClick"); texts=Handlers(c,"TextChanged");
            var label=c as DXLabel; if(label!=null) {autoSize=label.AutoSize; format=label.DrawFormat; text=label.Text;}
            var icon=c as DXImageControl; if(icon!=null) {fixedSize=icon.FixedSize; useOffset=icon.UseOffSet;}
            var button=c as DXButton; if(button!=null) buttonText=button.Label.Text;
            var check=c as DXCheckBox; if(check!=null) {checkText=check.Label.Text; checkGeometry=check.FixedGeometry;}
        }
        internal void Verify(bool dynamicText)
        {
            Check(!c.IsDisposed && c.Parent==parent && c.Location==location && (c.Size==size || (dynamicText && autoSize && c is DXLabel && c.Size==DXLabel.GetSize(c.Text,((DXLabel)c).Font,((DXLabel)c).Outline))),"Original geometry not restored: "+c.GetType()+" "+text+" current="+c.Location+"/"+c.Size+" expected="+location+"/"+size);
            Check(ReferenceEquals(c.Tag,tag) && Handlers(c,"MouseClick")==clicks && Handlers(c,"TextChanged")==texts,"Tag/handler changed");
            var label=c as DXLabel; if(label!=null) Check(label.AutoSize==autoSize && label.DrawFormat==format && (dynamicText || label.Text==text),"Label style changed");
            var icon=c as DXImageControl; if(icon!=null) Check(icon.FixedSize==fixedSize && icon.UseOffSet==useOffset,"Icon options changed");
            var button=c as DXButton; if(button!=null) Check(button.Label.Text==buttonText,"Original button caption lost");
            var check=c as DXCheckBox; if(check!=null) Check(check.Label.Text==checkText && check.FixedGeometry==checkGeometry,"Checkbox not restored");
        }
    }
    private static bool Dynamic(string id) => id.StartsWith("control.") && id!="control.ShowHelmetBox" && id!="control.MarriageIcon";
    private static GameUiCharacterControlLayout Mutate(GameUiCharacterControlLayout layout,string id,Func<GameUiCharacterControlRow,GameUiCharacterControlRow> map)
    {return new GameUiCharacterControlLayout(layout.Rows.Select(r=>r.Id==id?map(r):r).ToArray());}
    private static void Catalog(string fixture)
    {
        var layout=GameUiCharacterControlLayout.CreateOfficialCandidate();
        var expected=File.ReadAllLines(fixture).ToDictionary(l=>l.Split('=')[0],l=>l.Substring(l.IndexOf('=')+1));
        Check(expected.Count==24,"Incomplete independent catalog");
        foreach(var row in layout.Rows) Check(expected[row.Id]==row.Page+"|"+Rect(row.Bounds)+"|"+(row.Text??"-"),"Compiled geometry drift: "+row.Id);
        var rows=layout.Rows.ToArray(); var copy=new GameUiCharacterControlLayout(rows); rows[0]=null;
        Check(copy.Rows[0]!=null,"Mutable rows retained");
        Reject<ArgumentException>(()=>new GameUiCharacterControlLayout(new GameUiCharacterControlRow[30]),"Missing control accepted");
        rows=layout.Rows.ToArray(); rows[1]=rows[0];
        Reject<ArgumentException>(()=>new GameUiCharacterControlLayout(rows),"Duplicate ID accepted");
        Reject<ArgumentOutOfRangeException>(()=>new GameUiCharacterControlRow("bad",(GameUiCharacterControlPage)99,new Rectangle(1,1,2,2)),"Invalid page accepted");
        Reject<ArgumentOutOfRangeException>(()=>new GameUiCharacterControlRow("bad",GameUiCharacterControlPage.Chrome,new Rectangle(int.MaxValue,1,2,2)),"Overflow accepted");
    }
    private static void Checkbox(DXControl root)
    {
        using(var box=new DXCheckBox {Parent=root,Label={Text="Original text"}})
        {
            var original=new Saved(box); var labelSaved=new Saved(box.Label); var iconSaved=new Saved(box.Box);
            var geometry=new DXCheckBoxGeometry(new Size(142,22),new Rectangle(0,0,122,22),new Rectangle(128,4,14,14));
            int changes=0; box.CheckedChanged+=(s,e)=>changes++;
            box.UseFixedGeometry(geometry);
            for(int i=0;i<12;i++)
            {
                root.Location=new Point(i*3,i*2); box.Label.Text=new string('x',i*30+2); box.Checked=!box.Checked;
                Check(box.Size==geometry.Size && new Rectangle(box.Label.Location,box.Label.Size)==geometry.Label && new Rectangle(box.Box.Location,box.Box.Size)==geometry.Box,"Content/move/toggle changed fixed checkbox bounds");
            }
            Check(changes==12,"Geometry caused a checked event");
            var replacement=new DXCheckBoxGeometry(new Size(180,24),new Rectangle(0,0,160,24),new Rectangle(166,5,14,14));
            box.UseFixedGeometry(replacement); Check(box.FixedGeometry==replacement && box.Size==replacement.Size,"Checkbox geometry replacement failed");
            box.Label.Text="Original text"; box.UseFixedGeometry(null); original.Verify(false); labelSaved.Verify(false); iconSaved.Verify(false);
            Check(!box.Checked && changes==12,"Geometry reset live state");
            box.UseFixedGeometry(geometry); box.Label.Text="A longer legacy checkbox label"; box.UseFixedGeometry(null);
            Check(box.Size.Width==box.Label.Size.Width+box.Box.Size.Width,"Legacy size ignored text changed under fixed geometry");
            box.Label.Text="Original text";
            var beforeSize=box.Size; box.Label.Text="A longer legacy checkbox label";
            Check(box.Size.Width>beforeSize.Width && box.FixedGeometry==null,"Legacy autosize no longer works");
            Reject<ArgumentException>(()=>new DXCheckBoxGeometry(new Size(20,20),new Rectangle(0,0,15,20),new Rectangle(10,0,10,10)),"Overlapping checkbox rectangles accepted");
            Reject<ArgumentException>(()=>new DXCheckBoxGeometry(new Size(20,20),new Rectangle(int.MaxValue,0,5,20),new Rectangle(0,0,10,10)),"Overflow checkbox accepted");
            box.Dispose(); Reject<ObjectDisposedException>(()=>box.UseFixedGeometry(geometry),"Disposed checkbox accepted geometry");
        }
        root.Location=Point.Empty;
    }
    private static void Click(DXControl c) => c.OnMouseClick(new MouseEventArgs(MouseButtons.Left,1,c.DisplayArea.X+1,c.DisplayArea.Y+1,0));
    private static void Actions(CharacterDialog d,UserObject user,CConnection connection,TestScene root)
    {
        var tabs=Field<DXTabControl>(d,"TabControl");
        tabs.SelectedTab=Field<DXTab>(d,"HermitTab");
        var confirm=Field<DXCheckBox>(d,"gameUiHermitConfirmation");
        var buttons=Field<Dictionary<Stat,DXButton>>(d,"gameUiHermitButtons");
        foreach(var pair in buttons)
        {
            user.HermitPoints=0; confirm.Checked=false; Click(pair.Value); Check(connection.SendList.IsEmpty,"Zero points queued Hermit");
            confirm.Checked=true; Click(pair.Value); Check(connection.SendList.IsEmpty && !Children(root).OfType<DXMessageBox>().Any(),"Zero points opened confirmation");
            user.HermitPoints=3; confirm.Checked=false; Click(pair.Value);
            Packet packet; Check(connection.SendList.TryDequeue(out packet) && packet is C.Hermit && ((C.Hermit)packet).Stat==pair.Key && connection.SendList.IsEmpty,"Unchecked Hermit queued wrong/duplicate packet");
            confirm.Checked=true; Click(pair.Value); var popup=Children(root).OfType<DXMessageBox>().Single();
            Check(connection.SendList.IsEmpty,"Confirmation queued before yes"); Click(popup.NoButton);
            Check(popup.IsDisposed && connection.SendList.IsEmpty,"Cancel queued Hermit");
            Click(pair.Value); popup=Children(root).OfType<DXMessageBox>().Single(); Click(popup.YesButton);
            Check(popup.IsDisposed && connection.SendList.TryDequeue(out packet) && packet is C.Hermit && ((C.Hermit)packet).Stat==pair.Key && connection.SendList.IsEmpty,"Confirmed Hermit queued wrong/duplicate packet");
        }
        tabs.SelectedTab=Field<DXTab>(d,"CharacterTab");
        foreach(bool checkedBefore in new[]{false,true})
        {
            d.ShowHelmetBox.Checked=checkedBefore; Click(d.ShowHelmetBox); Packet packet;
            Check(d.ShowHelmetBox.ReadOnly && d.ShowHelmetBox.Checked==checkedBefore && connection.SendList.TryDequeue(out packet) && packet is C.HelmetToggle && ((C.HelmetToggle)packet).HideHelmet==checkedBefore && connection.SendList.IsEmpty,"Original helmet click semantics changed");
            connection.Process(new S.HelmetToggle {HideHelmet=checkedBefore});
            Check(d.ShowHelmetBox.Checked==!checkedBefore && connection.SendList.IsEmpty,"Helmet server refresh queued another request");
        }
    }
    private static void Controls(TestScene root,UserObject user,CConnection connection,GameScene scene)
    {
        using(var d=new CharacterDialog {Parent=root,Visible=true})
        {
            scene.CharacterBox=d;
            var layout=GameUiCharacterControlLayout.CreateOfficialCandidate(); var pages=GameUiCharacterEquipmentLayout.CreateOfficialCandidate(); var attrs=GameUiCharacterAttributeLayout.CreateOfficialCandidate();
            var originals=Originals(d); Check(originals.Count==20,"Missing stable original controls");
            var saved=originals.ToDictionary(p=>p.Key,p=>new Saved(p.Value));
            var oldChildren=new[]{d,Field<DXControl>(d,"gameUiNamePanel"),Field<DXTab>(d,"CharacterTab"),Field<DXTab>(d,"StatsTab"),Field<DXTab>(d,"HermitTab")}.ToDictionary(p=>p,p=>Children(p).ToArray());
            var initialHandlerCounts=originals.ToDictionary(p=>p.Key,p=>Handlers(p.Value,"TextChanged"));
            Reject<InvalidOperationException>(()=>d.UseGameUiControls(layout),"Controls accepted without pages");
            d.Size=new Size(412,562);
            for(int cycle=0;cycle<8;cycle++)
            {
                d.UseGameUiEquipmentPages(pages); d.UseGameUiAttributes(attrs); d.UseGameUiControls(layout);
                var added=Field<Dictionary<string,DXLabel>>(d,"gameUiAddedCaptions"); Check(added.Count==4,"Wrong number of added captions");
                var all=Originals(d); foreach(var p in added) all.Add(p.Key,p.Value);
                foreach(var p in originals) Check(ReferenceEquals(p.Value,all[p.Key]),"Original control replaced");
                for(int placement=0;placement<6;placement++)
                {
                    d.Location=new Point(placement*10,placement*5);
                    foreach(string name in new[]{"角色", "Centered Hero", "角色名居中测试", new string('X', 120)})
                    {
                        d.CharacterNameLabel.Text=name;
                        var title=Bounds(d,d.CharacterNameLabel);
                        Check(title.X*2+title.Width==d.Size.Width,"Character name is not centered on the window");
                        Check(!d.CharacterNameLabel.AutoSize && (d.CharacterNameLabel.DrawFormat & TextFormatFlags.HorizontalCenter)!=0,"Character name lost fixed centered text alignment");
                        Check((d.CharacterNameLabel.DrawFormat & TextFormatFlags.EndEllipsis)!=0,"Long names lost overflow protection");
                    }
                    foreach(var row in layout.Rows)
                    {
                        var c=all[row.Id]; Check(c.Parent==Page(d,row.Page) && Bounds(d,c)==row.Bounds,"Window-relative control mismatch: "+row.Id);
                        var label=c as DXLabel; if(label!=null) Check(!label.AutoSize && (label.DrawFormat&TextFormatFlags.EndEllipsis)!=0,"Long text can resize its cell");
                    }
                }
                foreach(var tab in new[]{Field<DXTab>(d,"CharacterTab"),Field<DXTab>(d,"StatsTab"),Field<DXTab>(d,"HermitTab")})
                {
                    Field<DXTabControl>(d,"TabControl").SelectedTab=tab;
                    foreach(var row in layout.Rows) Check(all[row.Id].IsVisible==(row.Id=="control.MarriageIcon"?d.MarriageIcon.Visible:row.Page==GameUiCharacterControlPage.Chrome || all[row.Id].Parent==tab),"Detail is visible on the wrong page: "+row.Id);
                }
                d.CharacterNameLabel.Text="角色姓名 & very long text "+cycle; d.GuildNameLabel.Text="公会"+cycle; d.GuildRankLabel.Text="职位"+cycle;
                d.HandWeightLabel.Text="123456789/999999999"; d.WearWeightLabel.Text="999999999/123456789"; d.WearWeightLabel.ForeColour=Color.Red;
                user.HermitPoints=cycle+1; d.UpdateStats(); d.MarriageIcon.Visible=true; d.MarriageIcon.Hint="Live partner";
                foreach(var p in originals.Where(p=>Dynamic(p.Key))) Check(p.Value.Hint==p.Value.Text && Handlers(p.Value,"TextChanged")==initialHandlerCounts[p.Key]+1,"Full text hint/subscription mismatch");
                d.UseGameUiControls(layout);
                foreach(var p in added) Check(ReferenceEquals(p.Value,Field<Dictionary<string,DXLabel>>(d,"gameUiAddedCaptions")[p.Key]),"Replacement recreated captions");
                d.UseGameUiEquipmentPages(pages); Check(d.GameUiControlLayout==layout,"Page replacement detached controls");
                Reject<ArgumentException>(()=>d.UseGameUiControls(Mutate(layout,"control.GuildNameLabel",r=>new GameUiCharacterControlRow("unknown",r.Page,r.Bounds))),"Unknown control accepted");
                Reject<ArgumentException>(()=>d.UseGameUiControls(Mutate(layout,"control.GuildNameLabel",r=>new GameUiCharacterControlRow(r.Id,GameUiCharacterControlPage.Hermit,r.Bounds))),"Incorrect owning page accepted");
                Reject<ArgumentException>(()=>d.UseGameUiControls(Mutate(layout,"control.GuildNameLabel",r=>new GameUiCharacterControlRow(r.Id,r.Page,new Rectangle(0,0,20,20)))),"Out of page accepted");
                Reject<ArgumentException>(()=>d.UseGameUiControls(Mutate(layout,"control.GuildNameLabel",r=>new GameUiCharacterControlRow(r.Id,r.Page,new Rectangle(310,242,20,20)))),"Attribute overlap accepted");
                Reject<ArgumentException>(()=>d.UseGameUiControls(Mutate(layout,"control.GuildNameLabel",r=>new GameUiCharacterControlRow(r.Id,r.Page,r.Bounds,"stale"))),"Static layout overwrote live data");
                var attrRows=attrs.Rows.ToArray(); var first=attrRows[0]; attrRows[0]=new GameUiCharacterAttributeRow(first.Id,first.Page,new Rectangle(310,218,76,20),first.Caption,first.Icon,first.CaptionText);
                Reject<ArgumentException>(()=>d.UseGameUiAttributes(new GameUiCharacterAttributeLayout(attrRows,attrs.GroupCaptions)),"Attribute replacement overlapped active weight control");
                Check(d.GameUiControlLayout==layout && d.GameUiAttributeLayout==attrs && Bounds(d,d.GuildNameLabel)==layout.Rows.Single(r=>r.Id=="control.GuildNameLabel").Bounds,"Rejected replacement mutated layout");
                if(cycle==0 || cycle==7) Actions(d,user,connection,root);
                var retired=added.Values.ToArray();
                if(cycle%2==0) d.UseGameUiControls(null); else d.UseGameUiEquipmentPages(null);
                Check(retired.All(c=>c.IsDisposed) && d.GameUiControlLayout==null,"Caption ownership leaked");
                if(d.GameUiEquipmentLayout!=null) d.UseGameUiEquipmentPages(null);
                d.Size=new Size(282,460);
                foreach(var p in saved) p.Value.Verify(Dynamic(p.Key));
                Check(d.MarriageIcon.Visible && d.MarriageIcon.Hint=="Live partner" && d.WearWeightLabel.ForeColour==Color.Red && d.CharacterNameLabel.Text.EndsWith(cycle.ToString()),"Rollback reset live server data");
                foreach(var p in oldChildren) Check(Children(p.Key).SequenceEqual(p.Value),"Original child order not restored");
                d.Size=new Size(412,562);
            }
            d.UseGameUiEquipmentPages(pages); d.UseGameUiControls(layout); d.GuildNameLabel.Hint="external"; d.GuildNameLabel.Text="external-update";
            Check(d.GuildNameLabel.Hint=="external","External hint overwritten"); d.UseGameUiControls(null); Check(d.GuildNameLabel.Hint=="external","Rollback lost external hint");
            d.UseGameUiControls(layout); var held=Field<Dictionary<string,DXLabel>>(d,"gameUiAddedCaptions").Values.ToArray();
            d.Dispose(); Check(held.All(c=>c.IsDisposed) && d.GameUiControlLayout==null,"Owner disposal retained controls");
            Reject<ObjectDisposedException>(()=>d.UseGameUiControls(layout),"Disposed character accepted controls");
        }
    }
    [STAThread]
    private static int Main(string[] args)
    {
        GameScene oldScene=GameScene.Game; DXScene oldActive=DXControl.ActiveScene; CConnection oldConnection=CEnvir.Connection;
        string file=Path.GetTempFileName(); MirLibrary oldLibrary; bool had=CEnvir.LibraryList.TryGetValue(LibraryFile.GameInter,out oldLibrary);
        try
        {
            using(var library=new MirLibrary(file){Loaded=true,Images=new MirImage[0]})
            using(var target=new TargetForm {ClientSize=new Size(1024,768)})
            {
                CEnvir.Target=target; CEnvir.LibraryList[LibraryFile.GameInter]=library; DXSoundManager.Error=true;
                using(var root=new TestScene(new Size(1024,768)) {IsVisible=true})
                {
                    DXControl.ActiveScene=root;
                    var scene=(GameScene)FormatterServices.GetUninitializedObject(typeof(GameScene)); GC.SuppressFinalize(scene);
                    var user=(UserObject)FormatterServices.GetUninitializedObject(typeof(UserObject)); SetField(user,"_Stats",new Stats()); SetField(user,"_HermitStats",new Stats()); SetField(scene,"_User",user);
                    scene.Equipment=new ClientUserItem[Globals.EquipmentSize]; GameScene.Game=scene;
                    var connection=(CConnection)FormatterServices.GetUninitializedObject(typeof(CConnection)); connection.Connected=true; connection.SendList=new ConcurrentQueue<Packet>(); CEnvir.Connection=connection;
                    Catalog(args[0]); Checkbox(root); Controls(root,user,connection,scene);
                }
            }
            Console.WriteLine("Character controls: "+checks+" checks passed; 20 original controls, 4 added captions, fixed checkboxes, six placements, eight cycles, original local packet queues/confirm/cancel, rollback and disposal. No network, rendering or game acceptance."); return 0;
        }
        catch(Exception e) {Console.Error.WriteLine(e); return 1;}
        finally {GameScene.Game=oldScene; DXControl.ActiveScene=oldActive; CEnvir.Connection=oldConnection; CEnvir.Target=null; if(had) CEnvir.LibraryList[LibraryFile.GameInter]=oldLibrary; else CEnvir.LibraryList.Remove(LibraryFile.GameInter); File.Delete(file);}
    }
}
