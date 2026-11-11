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

internal static class OfficialUiCharacterCompositionTests
{
    private static int checks;
    private static void Check(bool value, string detail) { checks++; if (!value) throw new Exception(detail); }
    private static void Reject<T>(Action action, string detail) where T : Exception
    { bool rejected=false; try { action(); } catch(T) { rejected=true; } Check(rejected,detail); }
    private static T Field<T>(object owner,string name) => (T)owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner);
    private static void Set(object owner,string name,object value) => owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(owner,value);
    private sealed class TestScene : DXScene { internal TestScene():base(new Size(1024,768)) {} }
    private static Rectangle Bounds(CharacterDialog d,DXControl c) => new Rectangle(c.DisplayArea.X-d.DisplayArea.X,c.DisplayArea.Y-d.DisplayArea.Y,c.Size.Width,c.Size.Height);
    private static DXLabel[] Values(GameUiCharacterReadouts b) => new[]{b.LevelLabel,b.ExperienceLabel,b.HealthLabel,b.ManaLabel};
    private static GameUiCharacterCompositionLayout Change(GameUiCharacterCompositionLayout c,Rectangle preview,Point pivot)
        => new GameUiCharacterCompositionLayout(preview,pivot,c.Level,c.Experience,c.Health,c.Mana);
    private static void Geometry(string fixture)
    {
        var c=GameUiCharacterCompositionLayout.CreateOfficialCandidate();
        var rows=File.ReadAllLines(fixture).Where(l=>!string.IsNullOrWhiteSpace(l)).ToArray();
        var expected=c.GetBounds().Select(r=>r.X+","+r.Y+","+r.Width+","+r.Height).ToArray();
        Check(rows.Take(5).SequenceEqual(expected) && rows[5]==c.Pivot.X+","+c.Pivot.Y,"Compiled composition differs from JSON fixture");
        var copy=c.GetBounds(); copy[0]=Rectangle.Empty; Check(c.Preview!=Rectangle.Empty,"Mutable bounds escaped");
        Reject<ArgumentException>(()=>Change(c,c.Preview,new Point(c.Preview.Right,c.Preview.Bottom)),"Pivot outside preview accepted");
        Reject<ArgumentOutOfRangeException>(()=>Change(c,new Rectangle(-1,0,10,10),new Point()),"Negative preview accepted");
        Reject<ArgumentOutOfRangeException>(()=>Change(c,new Rectangle(int.MaxValue,0,10,10),new Point()),"Overflow preview accepted");
        Reject<ArgumentException>(()=>Change(c,c.Level,c.Level.Location),"Preview/readout overlap accepted");
        // Equip[1040] is 84x180 at offset (-70,-131): its left edge is x=18 at this pivot.
        Rectangle weaponSource; Point weaponDestination;
        Check(GameUiCharacterCompositionLayout.TryClip(new Rectangle(18,102,84,180),c.Preview,out weaponSource,out weaponDestination)
            && weaponSource.Left==0 && weaponDestination.X==18,"Left side of native weapon is clipped by the body viewport");
        var random=new Random(19427);
        for(int i=0;i<300;i++)
        {
            var image=new Rectangle(random.Next(-400,1200),random.Next(-400,900),random.Next(1,700),random.Next(1,700));
            var viewport=new Rectangle(random.Next(0,300),random.Next(0,300),random.Next(1,500),random.Next(1,500));
            Rectangle source; Point destination; bool visible=GameUiCharacterCompositionLayout.TryClip(image,viewport,out source,out destination);
            int left=Math.Max(image.Left,viewport.Left),top=Math.Max(image.Top,viewport.Top),right=Math.Min(image.Right,viewport.Right),bottom=Math.Min(image.Bottom,viewport.Bottom);
            Check(visible==(right>left&&bottom>top),"Clip visibility mismatch");
            if(visible) Check(source==new Rectangle(left-image.X,top-image.Y,right-left,bottom-top)&&destination==new Point(left,top),"Clip changed scale or source origin");
            else Check(source==Rectangle.Empty&&destination==Point.Empty,"Invisible image returned coordinates");
        }
        Rectangle ignored; Point position;
        Reject<ArgumentOutOfRangeException>(()=>GameUiCharacterCompositionLayout.TryClip(new Rectangle(int.MaxValue,0,5,5),c.Preview,out ignored,out position),"Overflow image accepted");
        Reject<ArgumentOutOfRangeException>(()=>GameUiCharacterCompositionLayout.TryClip(new Rectangle(0,0,0,5),c.Preview,out ignored,out position),"Empty image accepted");
    }
    private static void Composition(TestScene root,GameScene scene)
    {
        var pages=GameUiCharacterEquipmentLayout.CreateOfficialCandidate(); var attrs=GameUiCharacterAttributeLayout.CreateOfficialCandidate();
        var controls=GameUiCharacterControlLayout.CreateOfficialCandidate(); var layout=GameUiCharacterCompositionLayout.CreateOfficialCandidate();
        using(var panel=new MainPanel {Parent=root})
        using(var d=new CharacterDialog {Parent=root,Size=new Size(412,562),Visible=true})
        {
            scene.MainPanel=panel; scene.CharacterBox=d;
            Reject<InvalidOperationException>(()=>d.UseGameUiComposition(panel,layout),"Composition accepted without required layouts");
            var cells=d.Grid.ToArray(); var labels=d.DisplayStats.Values.ToArray();
            var tab=Field<DXTab>(d,"CharacterTab"); var tabs=Field<DXTabControl>(d,"TabControl");
            for(int cycle=0;cycle<8;cycle++)
            {
                d.UseGameUiEquipmentPages(pages); d.UseGameUiAttributes(attrs); d.UseGameUiControls(controls);
                d.UseGameUiComposition(panel,layout); var binding=d.GameUiReadouts;
                var values=Values(binding); var source=new[]{panel.LevelLabel,panel.ExperienceLabel,panel.HealthLabel,panel.ManaLabel};
                for(int placement=0;placement<6;placement++)
                {
                    d.Location=new Point(placement*35,placement*19);
                    var regions=(Rectangle[])typeof(CharacterDialog).GetMethod("GameUiPreviewViewports",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(d,null);
                    Check(regions.Any(r=>r.Contains(d.DisplayArea.X+30,d.DisplayArea.Y+150)),"Left weapon pixels missing after moving window");
                    foreach(var region in regions)
                    {
                        Check(root.DisplayArea.Contains(region)&&tab.DisplayArea.Contains(region),"Preview escaped parent bounds");
                        for(int slot=0;slot<16;slot++)
                        {
                            var frame=d.Grid[slot].DisplayArea; frame.Inflate(1,1);
                            Check(!region.IntersectsWith(frame),"Preview painted over an equipment frame");
                        }
                    }
                    for(int a=0;a<regions.Length;a++) for(int b=a+1;b<regions.Length;b++)
                        Check(!regions[a].IntersectsWith(regions[b]),"Preview alpha drawn twice in overlapping regions");
                    for(int i=0;i<4;i++)
                    {
                        source[i].Text="999999999999/123456789012345 "+cycle+"-"+placement;
                        source[i].Hint="Value "+i;
                        Check(Bounds(d,values[i])==layout.GetBounds()[i+1],"Readout window coordinates drifted");
                        Check(values[i].Text==source[i].Text&&!values[i].AutoSize,"Live readout was truncated or resized");
                        Check(values[i].Hint==(i==1?panel.ExperienceBar.Hint:source[i].Hint),"Tooltip source changed");
                    }
                    foreach(var selected in new[]{tab,Field<DXTab>(d,"StatsTab"),Field<DXTab>(d,"HermitTab")})
                    {
                        tabs.SelectedTab=selected;
                        foreach(var value in values) Check(value.IsVisible==(selected==tab),"Readout visible on wrong page");
                    }
                }
                Reject<InvalidOperationException>(()=>d.UseGameUiReadouts(panel,new GameUiCharacterReadoutLayout(new Rectangle(0,0,10,10),new Rectangle(20,0,10,10),new Rectangle(40,0,10,10),new Rectangle(60,0,10,10))),"Standalone readouts replaced active composition");
                Reject<ArgumentNullException>(()=>d.UseGameUiComposition(null,layout),"Missing source accepted");
                Reject<ArgumentException>(()=>d.UseGameUiComposition(panel,new GameUiCharacterCompositionLayout(layout.Preview,layout.Pivot,
                    new Rectangle(191,97,36,36),layout.Experience,layout.Health,layout.Mana)),"Equipment/readout overlap accepted");
                Reject<ArgumentException>(()=>d.UseGameUiComposition(panel,Change(layout,new Rectangle(254,194,54,20),new Point(260,198))),"Named caption overlap accepted");
                Check(ReferenceEquals(binding,d.GameUiReadouts)&&!binding.IsDisposed&&d.GameUiCompositionLayout==layout,"Rejected composition changed active binding");
                var badControls=new GameUiCharacterControlLayout(controls.Rows.Select(r=>r.Id=="caption.new.level"?new GameUiCharacterControlRow(r.Id,r.Page,layout.Preview,r.Text):r).ToArray());
                Reject<ArgumentException>(()=>d.UseGameUiControls(badControls),"Active preview overlap accepted from control replacement");
                var first=attrs.Rows.First(r=>r.Page==GameUiCharacterPage.Character);
                var badAttrs=new GameUiCharacterAttributeLayout(attrs.Rows.Select(r=>r==first?new GameUiCharacterAttributeRow(r.Id,r.Page,layout.Preview,r.Caption,r.Icon,r.CaptionText):r).ToArray(),attrs.GroupCaptions);
                Reject<ArgumentException>(()=>d.UseGameUiAttributes(badAttrs),"Active preview overlap accepted from attribute replacement");
                d.UseGameUiEquipmentPages(pages); d.UseGameUiAttributes(attrs); d.UseGameUiControls(controls);
                Check(ReferenceEquals(binding,d.GameUiReadouts)&&values.SequenceEqual(Values(d.GameUiReadouts)),"Unchanged layout replacement recreated readouts");
                for(int i=0;i<4;i++) Check(Bounds(d,values[i])==layout.GetBounds()[i+1],"Page replacement moved readout");
                d.Size=new Size(414,564);
                var shifted=new GameUiCharacterEquipmentLayout(new Rectangle(1,1,412,562),pages.Character,pages.Status,pages.Hermit,Enumerable.Range(0,16).Select(pages.GetEquipmentBounds).ToArray());
                d.UseGameUiEquipmentPages(shifted);
                Check(ReferenceEquals(binding,d.GameUiReadouts),"Moving the page origin recreated the binding");
                for(int i=0;i<4;i++) Check(Bounds(d,values[i])==layout.GetBounds()[i+1],"Container offset was applied twice");
                d.UseGameUiEquipmentPages(pages); d.Size=new Size(412,562);
                d.UseGameUiComposition(panel,layout); Check(binding.IsDisposed&&d.GameUiCompositionLayout==layout,"Rebinding left old readouts live");
                binding=d.GameUiReadouts;
                if(cycle%4==0) d.UseGameUiComposition(null,null);
                else if(cycle%4==1) d.UseGameUiControls(null);
                else if(cycle%4==2) d.UseGameUiAttributes(null);
                else d.UseGameUiEquipmentPages(null);
                Check(binding.IsDisposed&&d.GameUiCompositionLayout==null&&d.GameUiReadouts==null,"Unbinding left active preview or subscriptions");
                d.UseGameUiEquipmentPages(null);
                Check(cells.SequenceEqual(d.Grid)&&labels.SequenceEqual(d.DisplayStats.Values),"Composition replaced original equipment/attributes");
            }
            d.UseGameUiEquipmentPages(pages); d.UseGameUiAttributes(attrs); d.UseGameUiControls(controls); d.UseGameUiComposition(panel,layout);
            var last=d.GameUiReadouts; panel.LevelLabel.Dispose();
            Check(last.IsDisposed&&d.GameUiCompositionLayout==null&&d.GameUiReadouts==null,"Source disposal left composition active");
        }
        using(var panel=new MainPanel {Parent=root})
        using(var d=new CharacterDialog {Parent=root,Size=new Size(412,562)})
        {
            d.UseGameUiEquipmentPages(pages); d.UseGameUiAttributes(attrs); d.UseGameUiControls(controls); d.UseGameUiComposition(panel,layout);
            var active=d.GameUiReadouts; d.Dispose(); Check(active.IsDisposed&&d.GameUiCompositionLayout==null,"Window disposal leaked composition");
        }
    }
    [STAThread] private static int Main(string[] args)
    {
        GameScene previous=GameScene.Game; DXScene oldActive=DXControl.ActiveScene; string file=Path.GetTempFileName();
        MirLibrary oldLibrary; bool had=CEnvir.LibraryList.TryGetValue(LibraryFile.GameInter,out oldLibrary);
        try
        {
            using(var library=new MirLibrary(file){Loaded=true,Images=new MirImage[0]})
            using(var target=new TargetForm {ClientSize=new Size(1024,768)})
            {
                CEnvir.Target=target; CEnvir.LibraryList[LibraryFile.GameInter]=library;
                using(var root=new TestScene {IsVisible=true})
                {
                    DXControl.ActiveScene=root; var scene=(GameScene)FormatterServices.GetUninitializedObject(typeof(GameScene)); GC.SuppressFinalize(scene);
                    var user=(UserObject)FormatterServices.GetUninitializedObject(typeof(UserObject)); Set(user,"_Stats",new Stats()); Set(user,"_HermitStats",new Stats()); Set(scene,"_User",user);
                    scene.Equipment=new ClientUserItem[Globals.EquipmentSize]; GameScene.Game=scene;
                    Geometry(args[0]); Composition(root,scene);
                }
            }
            Console.WriteLine("Character composition: "+checks+" checks passed; compiled JSON geometry, 300 clip cases, live HUD mirrors, six placements, eight cycles, cross-layout rejection and teardown. No rendering/network/game acceptance."); return 0;
        }
        catch(Exception error) {Console.Error.WriteLine(error); return 1;}
        finally {GameScene.Game=previous; DXControl.ActiveScene=oldActive; CEnvir.Target=null; if(had) CEnvir.LibraryList[LibraryFile.GameInter]=oldLibrary; else CEnvir.LibraryList.Remove(LibraryFile.GameInter); File.Delete(file);}
    }
}
