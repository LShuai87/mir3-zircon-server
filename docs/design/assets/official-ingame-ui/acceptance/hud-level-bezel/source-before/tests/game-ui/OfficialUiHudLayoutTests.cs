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
using UserObject = Client.Models.UserObject;

internal static partial class OfficialUiHudLayoutTests
{
    private static int checks;
    private static void Check(bool condition, string detail) { checks++; if (!condition) throw new Exception(detail); }
    private static void Reject<T>(Action action, string detail) where T : Exception
    { bool rejected = false; try { action(); } catch (T) { rejected = true; } Check(rejected, detail); }
    private static FieldInfo Field(Type type, string name)
    { for (; type != null; type = type.BaseType) { var f = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public); if (f != null) return f; } throw new Exception(name); }
    private static void Set(object o, string name, object value) => Field(o.GetType(), name).SetValue(o, value);
    private static T Get<T>(object o, string name) => (T)Field(o.GetType(), name).GetValue(o);
    private static List<DXControl> Children(DXControl c) => (List<DXControl>)typeof(DXControl).GetProperty("Controls", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(c);
    private static Rectangle Local(DXControl c) => new Rectangle(c.Location, c.Size);
    private sealed class TestScene : DXScene { internal TestScene() : base(new Size(1024, 768)) { } }
    private static Dictionary<string, GameUiHudRegion> Regions() => GameUiHudLayout.OfficialV1.Regions.ToDictionary(p => p.Key, p => p.Value);
    private static Dictionary<string, Size> Owners() => GameUiHudLayout.OfficialV1.Owners.ToDictionary(p => p.Key, p => p.Value);
    private static GameUiHudLayout Shifted()
    { var r = Regions(); var b = r["HealthLabel"].Bounds; b.X++; r["HealthLabel"] = new GameUiHudRegion("orb", b); return new GameUiHudLayout(Owners(), r); }
    private static void Geometry(string file)
    {
        var layout = GameUiHudLayout.OfficialV1; var expected = File.ReadAllLines(file);
        Check(expected.Length == 30, "HUD JSON fixture shape changed");
        foreach (string line in expected)
        {
            var t = line.Split('|');
            if (t.Length == 2) Check(t[1] == layout.Owners[t[0]].Width + "," + layout.Owners[t[0]].Height, "Owner differs from JSON: " + line);
            else { var r = layout.Regions[t[0]]; Check(t[1] == r.Owner && t[2] == string.Join(",", new[] { r.Bounds.X, r.Bounds.Y, r.Bounds.Width, r.Bounds.Height }), "Binding differs from JSON: " + line); }
        }
        Reject<ArgumentNullException>(() => new GameUiHudLayout(null, Regions()), "Null owners accepted");
        Reject<ArgumentNullException>(() => new GameUiHudLayout(Owners(), null), "Null regions accepted");
        var owners = Owners(); owners.Remove("orb"); Reject<ArgumentException>(() => new GameUiHudLayout(owners, Regions()), "Missing owner accepted");
        owners = Owners(); owners["orb"] = Size.Empty; Reject<ArgumentOutOfRangeException>(() => new GameUiHudLayout(owners, Regions()), "Empty owner accepted");
        owners = Owners(); owners["orb"] = new Size(int.MaxValue, 202); Reject<ArgumentOutOfRangeException>(() => new GameUiHudLayout(owners, Regions()), "Oversized owner accepted");
        var insensitive = new Dictionary<string, Size>(StringComparer.OrdinalIgnoreCase); foreach (var p in Owners()) insensitive[p.Key.ToUpperInvariant()] = p.Value;
        Reject<ArgumentException>(() => new GameUiHudLayout(insensitive, Regions()), "Nonordinal owner keys accepted");
        foreach (var bad in new[] { new GameUiHudRegion("invalid", new Rectangle(0,0,1,1)), new GameUiHudRegion("orb", new Rectangle(int.MaxValue,0,int.MaxValue,1)), new GameUiHudRegion("orb", Rectangle.Empty), new GameUiHudRegion("orb", new Rectangle(-1,0,1,1)), null })
        { var r = Regions(); r["HealthLabel"] = bad; Reject<ArgumentException>(() => new GameUiHudLayout(Owners(),r), "Invalid region accepted"); }
        var overlap = Regions(); overlap["SpellButton"] = overlap["QuestButton"]; Reject<ArgumentException>(() => new GameUiHudLayout(Owners(), overlap), "Overlapping actions accepted");
        var wrong = Regions(); wrong["HealthLabel"] = new GameUiHudRegion("commands",new Rectangle(1,190,1,1)); Reject<ArgumentException>(() => new GameUiHudLayout(Owners(),wrong), "Semantic owner changed");
        owners = Owners(); var regions = Regions(); var saved = new GameUiHudLayout(owners,regions); owners.Clear(); regions.Clear(); Check(saved.Owners.Count == 4 && saved.Regions.Count == 26, "Layout retains mutable input");
    }
    private static void Adapt(TestScene root, GameScene scene, UserObject user)
    {
        using (var panel = new MainPanel { Parent = root })
        {
            scene.MainPanel = panel;
            var children = Children(panel).ToArray(); var locations = children.ToDictionary(c=>c, c=>c.Location);
            var buttons = children.OfType<DXButton>().ToArray(); var handlers = buttons.ToDictionary(c=>c, c=>Get<Delegate>(c,"MouseClick"));
            var originalImage = children.OfType<DXImageControl>().ToDictionary(c=>c, c=>Tuple.Create(c.Library,c.Index,c.FixedSize,c.DrawImage,c.PixelDetect));
            var captions = Get<List<DXLabel>>(panel,"gameUiLegacyCaptions");
            var hp = Get<DXControl>(panel,"HealthBar"); var mp = Get<DXControl>(panel,"ManaBar");
            // A previously sortable native button must regain that behavior on detach.
            buttons[0].Sort=true;
            var nativeSort=buttons.ToDictionary(c=>c,c=>c.Sort);
            children=Children(panel).ToArray();
            bool oldDraw = panel.DrawImage, oldPass = panel.PassThrough;
            panel.Enabled = false; panel.Visible = false; panel.UseGameUiHud(GameUiHudLayout.OfficialV1);
            Check(panel.GameUiHudRoots.Count == 4 && panel.GameUiHudControls.Count == 26,"Missing original bindings");
            Check(panel.GameUiHudRoots.Values.All(c=>!c.Enabled && !c.Visible),"Initial root flags lost");
            panel.Enabled = true; panel.Visible = true;
            foreach (var position in new[] { Point.Empty, new Point(225,202), new Point(799,566), new Point(-10,-10), new Point(1024,768), new Point(300,100) })
            {
                foreach (var p in panel.GameUiHudRoots) p.Value.Location = position;
                foreach (var p in panel.GameUiHudControls)
                {
                    var r=GameUiHudLayout.OfficialV1.Regions[p.Key]; var c=p.Value;
                    Check(c.Parent==panel.GameUiHudRoots[r.Owner] && Local(c)==r.Bounds,"Original region changed: "+p.Key);
                    Check(c.DisplayArea.Location==new Point(position.X+r.Bounds.X,position.Y+r.Bounds.Y),"Display offset changed: "+p.Key);
                }
                foreach(var b in buttons)
                { Check(ReferenceEquals(handlers[b],Get<Delegate>(b,"MouseClick")),"Original action replaced"); Check(b.IsMouseOver(new Point(b.DisplayArea.X+b.Size.Width/2,b.DisplayArea.Y+b.Size.Height/2)),"Button target misses center"); }
            }
            Check(panel.GameUiHudCaptions.Count==7,"Visible HUD captions missing");
            foreach(var pair in panel.GameUiHudCaptions)
            {
                var value=panel.GameUiHudControls[pair.Key];var region=GameUiHudLayout.OfficialV1.Regions[pair.Key].Bounds;
                Check(pair.Value.Text.Length==2 && pair.Value.Parent==panel.GameUiHudRoots["readouts"] && Local(pair.Value)==new Rectangle(region.X-28,region.Y,28,region.Height),"Caption bounds or text incorrect");
                Check(!Local(pair.Value).IntersectsWith(Local(value)),"Caption overlaps stat value");
            }
            Check(captions.All(c=>!c.Visible) && !panel.ExperienceLabel.Visible && !panel.ExperienceBar.DrawImage,"Legacy decoration still draws");
            Check(Children(panel.GameUiHudRoots["orb"]).IndexOf(hp)<Children(panel.GameUiHudRoots["orb"]).IndexOf(panel.HealthLabel),"HP fill overlays numeric label");
            Check(Children(panel.GameUiHudRoots["orb"]).IndexOf(mp)<Children(panel.GameUiHudRoots["orb"]).IndexOf(panel.ManaLabel),"MP fill overlays numeric label");
            foreach (bool enabled in new[] { false,true,false,true })
            { panel.Enabled=enabled; Check(panel.GameUiHudRoots.Values.All(c=>c.Enabled==enabled),"Enabled not propagated"); Check(buttons.All(c=>c.IsEnabled==enabled),"Original actions not disabled"); }
            root.Enabled=false; Check(buttons.All(c=>!c.IsEnabled),"Scene disabled state bypassed"); root.Enabled=true;
            panel.Visible=false; Check(panel.GameUiHudRoots.Values.All(c=>!c.IsVisible),"Hidden HUD remains visible"); panel.Visible=true;
            foreach (var cultureName in new[] { "zh-CN", "en-US", "de-DE" })
            {
                var oldCulture=System.Threading.Thread.CurrentThread.CurrentCulture;
                try
                {
                    System.Threading.Thread.CurrentThread.CurrentCulture=new System.Globalization.CultureInfo(cultureName);
                    Set(user,"_level",1);
                    user.Stats[Stat.Health]=200; user.Stats[Stat.Mana]=100; user.CurrentHP=100; user.CurrentMP=25; user.MaxExperience=1000; user.Experience=250;
                    Check(panel.HealthLabel.Text=="100/200" && panel.ManaLabel.Text=="25/100","Native health/mana setter path changed");
                    Check(panel.GameUiExperienceFill.Fraction==1M,"XP must retain native level-table semantics");
                    user.Experience=25; Check(panel.GameUiExperienceFill.Fraction==.25M,"XP fraction wrong"); user.Experience=-1; Check(panel.GameUiExperienceFill.Fraction==0M,"Negative XP not clamped"); user.Experience=250;
                    Set(user,"_level",0); Check(panel.GameUiExperienceFill.Fraction==0M,"Zero XP maximum not handled");
                    Set(user,"_level",Globals.ExperienceList.Count); Check(panel.GameUiExperienceFill.Fraction==0M,"XP level outside table not handled");
                    Set(user,"_level",-1); Check(panel.GameUiExperienceFill.Fraction==0M,"Negative XP level not handled");
                    Set(user,"_level",1);
                    Check(panel.GameUiHealthFill.Fraction==.5M && panel.GameUiManaFill.Fraction==.25M,"Native resource ratios wrong");
                    Check(panel.HealthLabel.Hint=="生命: "+panel.HealthLabel.Text && panel.ManaLabel.Hint=="魔法值: "+panel.ManaLabel.Text,"Native value hint changed");
                    Check(panel.ExperienceBar.Hint.Contains("250") && panel.ExperienceBar.Hint.Contains("1"),"Native experience hint lost");
                    Check(Local(panel.HealthLabel)==GameUiHudLayout.OfficialV1.Regions["HealthLabel"].Bounds,"Live value moved fixed label");
                    user.CurrentHP=int.MaxValue; user.CurrentMP=-1;
                    Check(panel.GameUiHealthFill.Fraction==1M && panel.GameUiManaFill.Fraction==0M,"Resource overflow/negative not clamped");
                    user.Stats[Stat.Health]=0; Check(panel.GameUiHealthFill.Fraction==0M,"Zero maximum not handled");
                    panel.LevelLabel.Text="999999999999999999999"; Check(panel.LevelLabel.Hint.EndsWith(panel.LevelLabel.Text),"Long value hint truncated");
                }
                finally { System.Threading.Thread.CurrentThread.CurrentCulture=oldCulture; }
            }
            panel.AvailableQuestIcon.Visible=true; panel.CompletedQuestIcon.Visible=true;
            Check(panel.CompletedQuestIcon.Location==new Point(2,panel.GameUiHudControls["QuestButton"].Size.Height-panel.CompletedQuestIcon.Size.Height),"Quest alerts overlap");
            panel.AvailableQuestIcon.Visible=false; Check(panel.CompletedQuestIcon.Location==new Point(2,2),"Single quest alert moved");
            panel.MCLabel.Visible=false; panel.SCLabel.Visible=true; Check(captions.All(c=>!c.Visible),"Class change exposed old captions");
            var identities=panel.GameUiHudControls.ToDictionary(p=>p.Key,p=>p.Value); var roots=panel.GameUiHudRoots.Values.ToArray();
            for(int i=0;i<25;i++)
            {
                panel.UseGameUiHud(Shifted()); panel.UseGameUiHud(GameUiHudLayout.OfficialV1);
                Check(identities.All(p=>ReferenceEquals(panel.GameUiHudControls[p.Key],p.Value)),"Rebind replaced original controls");
                Check(panel.GameUiHudRoots.Values.SequenceEqual(roots),"Rebind recreated roots");
            }
            int failures=0; EventHandler<EventArgs> fail=(s,e)=>{if(failures++==0)throw new InvalidOperationException("injected location observer");};
            panel.HealthLabel.LocationChanged+=fail;
            Reject<InvalidOperationException>(()=>panel.UseGameUiHud(Shifted()),"Replacement observer not surfaced"); panel.HealthLabel.LocationChanged-=fail;
            Check(panel.GameUiHudLayout==GameUiHudLayout.OfficialV1 && Local(panel.HealthLabel)==GameUiHudLayout.OfficialV1.Regions["HealthLabel"].Bounds,"Replacement rollback failed");
            // Restoration must include artist-supplied flags; all originals keep live text and data.
            panel.ExperienceBar.UseOffSet=true; panel.ExperienceBar.Blend=true; panel.ExperienceBar.ImageOpacity=.25F;
            panel.UseGameUiHud(null);
            Check(roots.All(c=>c.IsDisposed),"Detached roots leaked"); Check(children.SequenceEqual(Children(panel)),"Original draw order not restored");
            Check(panel.DrawImage==oldDraw && panel.PassThrough==oldPass,"Panel native flags not restored");
            Check(buttons.All(b=>b.Sort==nativeSort[b]),"Original command sorting flags not restored");
            foreach(var pair in originalImage) { var c=pair.Key; var s=pair.Value; Check(c.Library==s.Item1 && c.Index==s.Item2 && c.FixedSize==s.Item3 && c.DrawImage==s.Item4 && c.PixelDetect==s.Item5,"Native artwork binding not restored"); }
            Check(!panel.ExperienceBar.UseOffSet && !panel.ExperienceBar.Blend && panel.ExperienceBar.ImageOpacity==1F,"Image flags not restored");
            Check(!Get<DXLabel>(panel,"MCLabelLabel").Visible && Get<DXLabel>(panel,"SCLabelLabel").Visible,"Live class caption visibility not restored");
            Check(panel.HealthLabel.AutoSize && panel.HealthLabel.Hint==null,"Native label format/hint not restored");
            Check(panel.HealthLabel.Text=="2147483647/200","Live text lost during restore");
            foreach(var c in children.Where(c=>!(c is DXLabel))) Check(c.Location==locations[c],"Native control bounds lost");
            for(int i=0;i<10;i++) { panel.UseGameUiHud(GameUiHudLayout.OfficialV1); panel.UseGameUiHud(null); Check(children.SequenceEqual(Children(panel)),"Repeated enable/disable lost child order"); }
            panel.UseGameUiHud(GameUiHudLayout.OfficialV1); roots=panel.GameUiHudRoots.Values.ToArray(); panel.Parent=null;
            Check(panel.GameUiHudLayout==null && roots.All(c=>c.IsDisposed) && children.SequenceEqual(Children(panel)),"Parent change leaked sibling roots");
            panel.Parent=root;
        }
        using(var panel=new MainPanel{Parent=root})
        {
            var children=Children(panel).ToArray(); int failures=0;
            EventHandler<EventArgs> fail=(s,e)=>{if(failures++==0)throw new InvalidOperationException("initial observer");};
            panel.HealthLabel.LocationChanged+=fail; Reject<InvalidOperationException>(()=>panel.UseGameUiHud(GameUiHudLayout.OfficialV1),"Initial observer not surfaced"); panel.HealthLabel.LocationChanged-=fail;
            Check(panel.GameUiHudLayout==null && Children(panel).SequenceEqual(children),"Initial failure rollback lost originals");
            panel.UseGameUiHud(GameUiHudLayout.OfficialV1); var controls=panel.GameUiHudControls.Values.ToArray(); var roots=panel.GameUiHudRoots.Values.ToArray(); var fills=new[]{panel.GameUiHealthFill,panel.GameUiManaFill,panel.GameUiExperienceFill};
            panel.Dispose(); Check(controls.All(c=>c.IsDisposed) && roots.All(c=>c.IsDisposed) && fills.All(c=>c.IsDisposed),"Disposal leaked HUD controls");
            Reject<ObjectDisposedException>(()=>panel.UseGameUiHud(GameUiHudLayout.OfficialV1),"Disposed HUD accepted");
        }
        foreach(int exit in new[]{0,1,2,3})
        using(var panel=new MainPanel{Parent=root})
        {
            var originals=Children(panel).ToArray(); panel.UseGameUiHud(GameUiHudLayout.OfficialV1);
            var roots=panel.GameUiHudRoots.Values.ToArray();var fill=panel.GameUiHealthFill;
            if(exit==0)roots[0].Dispose();
            else if(exit==1)roots[0].Parent=null;
            else if(exit==2)fill.Dispose();
            else { Children(root).Remove(panel); Children(root).Add(panel); roots[0].Dispose(); }
            Check(panel.GameUiHudLayout==null&&roots.All(r=>r.IsDisposed),"External HUD root/fill lifecycle left adapter active");
            Check(Children(panel).SequenceEqual(originals)&&originals.All(c=>!c.IsDisposed),"External HUD lifecycle destroyed originals");
        }
        using(var panel=new MainPanel{Parent=root})
        {
            var button=Get<DXButton>(panel,"CharacterButton");var library=CEnvir.LibraryList[LibraryFile.GameInter];
            var originals=Children(panel).ToArray();
            button.BindButtonImages(library,new ButtonImageStates(0,0,0,0));
            Reject<ArgumentException>(()=>panel.UseGameUiHud(GameUiHudLayout.OfficialV1),"Wrong-size explicit button states accepted");
            Check(panel.GameUiHudLayout==null&&Children(panel).SequenceEqual(originals),"Preflight mutated native controls");
            var states=new ButtonImageStates(245,245,245,245,Color.Yellow,Color.Red,Color.Blue,Color.Gray);
            button.BindButtonImages(library,states);button.Blend=true;button.ImageOpacity=.6F;
            panel.UseGameUiHud(GameUiHudLayout.OfficialV1);button.BindImage(null,-1);button.ImageOpacity=.2F;button.Blend=false;panel.UseGameUiHud(null);
            Check(ReferenceEquals(button.ImageStates,states)&&button.Library==library&&button.ImageOpacity==.6F&&button.Blend,"Explicit states/tints/image flags not restored");
        }
        using(var panel=new MainPanel()) Reject<InvalidOperationException>(()=>panel.UseGameUiHud(GameUiHudLayout.OfficialV1),"Parentless HUD accepted");
    }
    [STAThread] private static int Main(string[] args)
    {
        var oldGame=GameScene.Game; var oldActive=DXControl.ActiveScene; var oldTarget=CEnvir.Target;
        MirLibrary oldLibrary; bool hadLibrary=CEnvir.LibraryList.TryGetValue(LibraryFile.GameInter,out oldLibrary);
        string file=Path.GetTempFileName();
        try
        {
            var frames=Enumerable.Range(0,250).Select(i=>{var f=(MirImage)FormatterServices.GetUninitializedObject(typeof(MirImage));f.Width=36;f.Height=36;return f;}).ToArray();
            frames[245].Width=58;frames[245].Height=58;
            frames[50].Width=1024;frames[50].Height=64; frames[51].Width=574;frames[51].Height=8;frames[52].Width=100;frames[52].Height=12;frames[240].Width=frames[241].Width=14;frames[240].Height=frames[241].Height=14;
            using(var library=new MirLibrary(file){Loaded=true,Images=frames})
            using(var target=new TargetForm{ClientSize=new Size(1024,768)})
            {
                CEnvir.Target=target; CEnvir.LibraryList[LibraryFile.GameInter]=library;
                using(var root=new TestScene{IsVisible=true})
                {
                DXControl.ActiveScene=root;
                var scene=(GameScene)FormatterServices.GetUninitializedObject(typeof(GameScene));GC.SuppressFinalize(scene);GameScene.Game=scene;
                var user=(UserObject)FormatterServices.GetUninitializedObject(typeof(UserObject));Set(user,"_Stats",new Stats());Set(scene,"_User",user);
                Geometry(args[0]); Adapt(root,scene,user);
                if(args.Length>1)Render(root,args[1]);
                }
            }
            Console.WriteLine("HUD layout: "+checks+" checks passed; real controls, native HP/MP/XP setters, geometry, action identity, flags, foreground order, repeated rebind/restore, observer rollback, lifecycle. No game login, packets or final-art acceptance.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally{GameScene.Game=oldGame;DXControl.ActiveScene=oldActive;CEnvir.Target=oldTarget;if(hadLibrary)CEnvir.LibraryList[LibraryFile.GameInter]=oldLibrary;else CEnvir.LibraryList.Remove(LibraryFile.GameInter);File.Delete(file);}
    }
}
