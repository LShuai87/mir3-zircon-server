using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using Client;
using Client.Controls;
using Client.Envir;
using Client.Models;
using Client.Scenes;
using Client.Scenes.Views;
using Client.UserModels;
using Library;
using Library.SystemModels;
using MirDB;
using SlimDX.Direct3D9;
using UserObject = Client.Models.UserObject;

internal static partial class OfficialUiWindowSetTests
{
    private static int checks;
    private static readonly Dictionary<string, Rectangle> expectedPlacements = new Dictionary<string, Rectangle>();
    private static void Check(bool value, string detail) { checks++; if (!value) throw new Exception(detail); }
    private static void Reject<T>(Action action, string detail) where T:Exception
    { try { action(); } catch(T) { checks++; return; } throw new Exception("Accepted invalid operation: " + detail); }
    private static FieldInfo Field(Type type,string name)
    { for(;type!=null;type=type.BaseType) { var f=type.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic); if(f!=null)return f; } throw new Exception(name); }
    private static void Set(object o,string name,object value) => Field(o.GetType(),name).SetValue(o,value);
    private static List<DXControl> Children(DXControl c) => (List<DXControl>)typeof(DXControl).GetProperty("Controls",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(c);
    private static IEnumerable<DXControl> Descendants(DXControl c)
    { foreach(var child in Children(c).ToArray()) { yield return child; foreach(var d in Descendants(child))yield return d; } }
    private sealed class TestScene:DXScene { internal TestScene():base(new Size(1024,768)){} }
    private sealed class Native
    {
        internal readonly DXControl Control,Parent;
        internal readonly Rectangle Bounds;
        internal readonly DXControl[] ChildrenBefore;
        internal readonly bool PixelHitBefore, SortBefore;
        internal Native(DXControl control) { Control=control;Parent=control.Parent;Bounds=new Rectangle(control.Location,control.Size);ChildrenBefore=Children(control).ToArray();PixelHitBefore=(control as DXButton)?.NormalFrameHitMask!=null; SortBefore=control.Sort; }
        internal void AssertRestored()
        {
            Check(!Control.IsDisposed && Control.Parent==Parent,"Native control lifetime/parent changed: "+Control.GetType().Name);
            Check(new Rectangle(Control.Location,Control.Size)==Bounds,"Native bounds not restored: "+Control.GetType().Name+" text="+Control.Text+" parent="+Control.Parent.GetType().Name+" "+new Rectangle(Control.Location,Control.Size)+" vs "+Bounds);
            Check(Children(Control).SequenceEqual(ChildrenBefore),"Native children/order not restored: "+Control.GetType().Name);
            Check(((Control as DXButton)?.NormalFrameHitMask!=null)==PixelHitBefore,"Native button mask opt-in not restored");
            Check(Control.Sort==SortBefore,"Native input sorting flag not restored");
        }
    }
    private static void LegacyGlyph(CharacterDialog character,GameUiVerifiedPackage package,string directory,bool suppressed)
    {
        var field=typeof(DXControl).GetField("InterfaceLibrary",BindingFlags.Static|BindingFlags.NonPublic);var previous=field.GetValue(null);
        try
        {
            field.SetValue(null,package.Library);
            var device=DXManager.Device;device.Clear(ClearFlags.Target,Color.FromArgb(40,80,120),1,0);device.BeginScene();DXManager.Sprite.Begin(SpriteFlags.AlphaBlend);
            try { character.Draw(character.Grid[0],GameUiOfficialV1Catalog.PackageManifest.Frames["hud-hp-fill"].Index); }
            finally { DXManager.Sprite.End();device.EndScene(); }
            string path=Path.Combine(directory,suppressed?"legacy-glyph-suppressed.png":"legacy-glyph-restored.png");Surface.ToFile(DXManager.MainSurface,path,ImageFileFormat.Png);
            bool changed=false;using(var image=new Bitmap(path))for(int y=0;y<image.Height&&!changed;y++)for(int x=0;x<image.Width;x++)
                if(image.GetPixel(x,y).ToArgb()!=Color.FromArgb(40,80,120).ToArgb()){changed=true;break;}
            Check(changed!=suppressed,suppressed?"Legacy glyph leaked through Official frame":"Legacy glyph did not return after detach");
        }
        finally { field.SetValue(null,previous); }
    }
    private static void CommandDecorationHits(MainPanel hud)
    {
        var command = hud.GameUiHudRoots["commands"];
        var regions = GameUiHudLayout.OfficialV1.Regions.Where(r=>r.Value.Owner=="commands").ToArray();
        var oracles = regions.Where(r=>hud.GameUiHudControls[r.Key] is DXButton && true)
            .ToDictionary(r=>r.Key,r=>ButtonHitMaskTests.ReferenceAlpha(((DXButton)hud.GameUiHudControls[r.Key]).Library,((DXButton)hud.GameUiHudControls[r.Key]).ImageStates.Normal));
        Check(command.DrawImage && command.PassThrough && !command.PixelDetect,"Command decoration must draw without capturing its full rectangle");
        Check(regions.Length==11 && oracles.Count==9,"Original nine buttons and two readouts require eight explicit alpha masks");
        // The native exit action is a separate immediate control, not one of the nine
        // legacy command bindings. Include its independently specified hit rectangle.
        var exit = hud.GameUiExitButton;
        var exitBounds = new Rectangle(198,174,22,18);
        Check(exit != null && exit.Parent == command && new Rectangle(exit.Location,exit.Size) == exitBounds,
            "Exit action geometry differs from its independent hit rectangle");
        int decoration=0, controls=0, transparentCorners=0;
        for(int y=0;y<command.Size.Height;y++) for(int x=0;x<command.Size.Width;x++)
        {
            var local = new Point(x,y); bool expected=exit.IsVisible && exitBounds.Contains(local);
            // The four-slot shortcut column is composed of controls that are not part
            // of the legacy wheel-region manifest. Include their independent hit
            // rectangles so the shell oracle covers the complete commands root.
            foreach (var shortcut in new[] { hud.GameUiMountButton, hud.GameUiCombatButton, hud.GameUiPotentialButton })
                if (shortcut != null && shortcut.IsVisible && new Rectangle(shortcut.Location, shortcut.Size).Contains(local))
                    expected = true;
            foreach(var pair in regions)
            {
                Rectangle area=pair.Value.Bounds; if(!area.Contains(local))continue;
                bool[] alpha; bool hit=!oracles.TryGetValue(pair.Key,out alpha) || alpha[(y-area.Y)*area.Width+x-area.X];
                if(!hit)transparentCorners++; expected|=hit;
                Check(hud.GameUiHudControls[pair.Key].IsMouseOver(new Point(command.DisplayArea.X+x,command.DisplayArea.Y+y))==hit,"Button differs from independent normal-alpha oracle: "+pair.Key);
            }
            bool actual = command.IsMouseOver(new Point(command.DisplayArea.X+x,command.DisplayArea.Y+y));
            if(actual!=expected) throw new Exception("Command hit changed at "+local+"; expected original visible control="+expected+"; actual hits="+string.Join(";",Children(command).Where(c=>c.IsMouseOver(new Point(command.DisplayArea.X+x,command.DisplayArea.Y+y))).Select(c=>c.GetType().Name+" "+c.Location+" "+c.Size+" visible="+c.IsVisible)));
            if(expected) controls++; else decoration++;
        }
        Check(decoration>0 && controls>0 && transparentCorners>0,"Command decoration, alpha corners and original controls were not covered");
        var button = hud.GameUiHudControls["CharacterButton"];
        var center = new Point(button.DisplayArea.X+button.Size.Width/2,button.DisplayArea.Y+button.Size.Height/2);
        bool visible = button.Visible;
        try { button.Visible=false; Check(!command.IsMouseOver(center),"Hidden command button still captures shell clicks"); }
        finally { button.Visible=visible; }
        Check(command.IsMouseOver(center),"Restored command button is not clickable");
    }
    private static void CommandActions(TestScene root, GameScene game, MainPanel hud, string directory)
    {
        // Keep native constructor-installed callbacks; click by independent clockwise positions.
        var stackingBefore = Children(root).ToArray();
        using (DXWindow.SuppressSceneLayoutWrites(root))
        using (var magic = new MagicDialog { Parent = root, Visible = false })
        using (var config = new DXConfigWindow { Parent = root, Visible = false })
        using (var guild = new GuildDialog { Parent = root, Visible = false })
        using (var quest = new QuestDialog { Parent = root, Visible = false })
        using (var group = new GroupDialog { Parent = root, Visible = false })
        using (var mail = new MailDialog { Parent = root, Visible = false })
        {
            game.MagicBox = magic; game.ConfigBox = config; game.GuildBox = guild;
            game.QuestBox = quest; game.GroupBox = group; game.MailBox = mail;
            var windows = new DXWindow[] { game.InventoryBox, magic, config, guild, quest, group, mail, game.CharacterBox };
            var visibleBefore = windows.Select(w => w.Visible).ToArray();
            string[] bindings = { "InventoryButton", "SpellButton", "ConfigButton", "GuildButton", "QuestButton", "GroupButton" };
            string[] names = { "inventory", "skills", "settings", "guild", "quests", "group" };
            Rectangle[] positions = {
                new Rectangle(87,65,54,32), new Rectangle(134,76,42,50), new Rectangle(133,126,42,50),
                new Rectangle(87,153,54,32), new Rectangle(54,126,42,50), new Rectangle(3,89,31,24)
            };
            var command = hud.GameUiHudRoots["commands"];
            var oldMouse = DXControl.MouseControl; var oldFocus = DXControl.FocusControl;
            var evidence = new List<string> { "clockwise,button,target,opened,closed,other_windows_unchanged" };
            try
            {
                foreach (var window in windows) window.Visible = false;
                for (int i = 0; i < bindings.Length; i++)
                {
                    var button = (DXButton)hud.GameUiHudControls[bindings[i]];
                    Check(new Rectangle(button.Location,button.Size)==positions[i], "Wrong action at clockwise position " + i);
                    var alpha = ButtonHitMaskTests.ReferenceAlpha(button.Library,button.ImageStates.Normal);
                    var pixel = Enumerable.Range(0,alpha.Length).Where(n => alpha[n])
                        .OrderBy(n => Math.Abs(n % button.Size.Width-button.Size.Width/2)+Math.Abs(n / button.Size.Width-button.Size.Height/2)).First();
                    int x = command.DisplayArea.X+positions[i].X+pixel%button.Size.Width;
                    int y = command.DisplayArea.Y+positions[i].Y+pixel/button.Size.Width;
                    var move = new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.None,0,x,y,0);
                    var click = new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left,1,x,y,0);
                    var sound = button.Sound;
                    try
                    {
                        button.Sound = SoundIndex.None;
                        for (int repeat = 0; repeat < 2; repeat++)
                        {
                            command.OnMouseMove(move);
                            Check(ReferenceEquals(DXControl.MouseControl,button), "Position routes to wrong native button: " + names[i]);
                            DXControl.MouseControl.OnMouseClick(click);
                            for (int j = 0; j < windows.Length; j++)
                                Check(windows[j].Visible==(j==i), "Click opened wrong native window: " + names[i]+" index="+j);
                            command.OnMouseMove(move);
                            DXControl.MouseControl.OnMouseClick(click);
                            Check(windows.All(w => !w.Visible), "Repeated click failed to close only its native window: " + names[i]);
                        }
                    }
                    finally { button.Sound = sound; }
                    evidence.Add((i+1)+","+bindings[i]+","+names[i]+",true,true,true");
                }
                File.WriteAllLines(Path.Combine(directory,"command-actions.csv"),evidence);
                Console.WriteLine("Native command actions: top then clockwise inventory -> skills -> settings -> guild -> quests -> group; actual mouse routing and original callbacks open/close only the intended window twice. Existing mail and character windows stay closed.");
            }
            finally
            {
                for (int i = 0; i < windows.Length; i++) windows[i].Visible = visibleBefore[i];
                DXControl.FocusControl = oldFocus; DXControl.MouseControl = oldMouse;
                // Native windows bring themselves forward on open; isolate that effect from later layout tests.
                var extra = Children(root).Except(stackingBefore).ToArray();
                Children(root).Clear(); Children(root).AddRange(stackingBefore); Children(root).AddRange(extra);
                game.MagicBox = null; game.ConfigBox = null; game.GuildBox = null;
                game.QuestBox = null; game.GroupBox = null; game.MailBox = null;
            }
        }
    }
    private static void CompactCommandOwnership(MainPanel hud)
    {
        // Independent expected order, not read from the implementation's child list.
        string[] order={"BeltButton","SpellButton","QuestButton","ConfigButton","InventoryButton","GuildButton","CharacterButton","CashShopButton","GroupButton"};
        Check(GameUiHudLayout.CommandImageDrawOrder.SequenceEqual(order),"Command draw order differs from the layout contract");
        var root=hud.GameUiHudRoots["commands"];
        var images=order.Select(key=>(DXButton)hud.GameUiHudControls[key]).ToArray();
        var alpha=images.ToDictionary(b=>b,b=>ButtonHitMaskTests.ReferenceAlpha(b.Library,b.ImageStates.Normal));
        Check(!hud.AvailableQuestIcon.DrawImage && !hud.CompletedQuestIcon.DrawImage,
            "Candidate wheel must suppress both quest badges");
        var before=Children(root).ToArray();
        Check(before.Where(c=>images.Contains(c)).SequenceEqual(images),"Actual draw order differs from pointer ownership");
        Check(images.All(b=>!b.Sort && b.NormalFrameHitMask!=null),"Compact buttons must keep alpha and fixed sorting");
        var delegates=images.ToDictionary(b=>b,b=>Field(b.GetType(),"MouseClick").GetValue(b));
        var sounds=images.ToDictionary(b=>b,b=>b.Sound);
        var clicks=images.ToDictionary(b=>b,b=>0);
        DXControl oldMouse=DXControl.MouseControl,oldFocus=DXControl.FocusControl;
        int visible=0,hidden=0,disabled=0;
        try
        {
            foreach(var b in images)
            {
                DXButton captured=b;
                Set(b,"MouseClick",(EventHandler<System.Windows.Forms.MouseEventArgs>)((s,e)=>clicks[captured]++));
                b.Sound=SoundIndex.None;
            }
            for(int y=0;y<root.Size.Height;y++) for(int x=0;x<root.Size.Width;x++)
            {
                var local=new Point(x,y);
                var hits=images.Where(b=>new Rectangle(b.Location,b.Size).Contains(local) && alpha[b][(y-b.Location.Y)*b.Size.Width+x-b.Location.X]).ToArray();
                // A topmost click target is insufficient: every opaque pixel of
                // each wedge must remain visible, including its gold border.
                Check(hits.Length<=1,"Command artwork is occluded at "+local+": "+string.Join(",",order.Where(key=>hits.Contains((DXButton)hud.GameUiHudControls[key]))));
                if(hits.Length==0)continue;
                visible++;
                var expected=hits[0];
                var move=new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.None,0,root.DisplayArea.X+x,root.DisplayArea.Y+y,0);
                var click=new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left,1,move.X,move.Y,0);
                root.OnMouseMove(move);
                Check(ReferenceEquals(DXControl.MouseControl,expected),"Visible command pixel went to a different action at "+local);
                int total=clicks.Values.Sum(),own=clicks[expected];
                expected.OnMouseDown(click);expected.OnMouseUp(click);expected.OnMouseClick(click);
                Check(clicks.Values.Sum()==total+1 && clicks[expected]==own+1,"Visible command pixel dispatched duplicate/wrong clicks");
                Check(Children(root).SequenceEqual(before),"Input changed wheel layering");
                expected.Visible=false;
                try { root.OnMouseMove(move);Check(!images.Contains(DXControl.MouseControl as DXButton),"Hidden command pixel still reaches a wheel action");hidden++; }
                finally { expected.Visible=true; }
                expected.Enabled=false;
                try
                {
                    root.OnMouseMove(move);Check(ReferenceEquals(DXControl.MouseControl,expected),"Disabled front layer leaked pointer to a different action");
                    total=clicks.Values.Sum();expected.OnMouseClick(click);Check(clicks.Values.Sum()==total,"Disabled command dispatched a click");disabled++;
                }
                finally { expected.Enabled=true; }
            }
            Check(visible>0 && hidden==visible && disabled==visible,"Visible command routing was not exercised");
            foreach(var image in images)image.BringToFront();
            Check(Children(root).SequenceEqual(before),"BringToFront reordered fixed wheel layers");
            Console.WriteLine("Compact wheel ownership: zero shared opaque pixels; "+visible+" visible pixels tested for single click, hidden passthrough, disabled blocking and fixed draw order.");
        }
        finally
        {
            foreach(var b in images){Set(b,"MouseClick",delegates[b]);b.Sound=sounds[b];}
            DXControl.FocusControl=oldFocus;DXControl.MouseControl=oldMouse;
        }
    }
    private static void OrbForeground(GameScene game, MainPanel hud, string directory)
    {
        var orb = hud.GameUiHudRoots["orb"];
        var overlay = hud.GameUiOrbOverlay;
        var user = game.User;
        int hp = user.CurrentHP, mp = user.CurrentMP, maxHp = user.Stats[Stat.Health], maxMp = user.Stats[Stat.Mana];
        var oldMouse = DXControl.MouseControl;
        using (var stream = typeof(MainPanel).Assembly.GetManifestResourceStream("Client.Resources.GameUi.hud-orb-overlay.png"))
        using (var art = new Bitmap(stream))
        {
            try
            {
                user.Stats[Stat.Health] = user.Stats[Stat.Mana] = 100;
                int[] values = {0,50,100}; string[] states = {"empty","half","full"};
                for (int state = 0; state < values.Length; state++)
                {
                    Set(user,"_CurrentHP",values[state]); Set(user,"_CurrentMP",values[state]);
                    Check(hud.GameUiHealthFill.Fraction == values[state]/100M && hud.GameUiManaFill.Fraction == values[state]/100M,"Orb preview uses incorrect live ratio");
                    string baseline = Path.Combine(directory,"hud-orb-baseline.png");
                    string output = Path.Combine(directory,"hud-orb-"+states[state]+".png");
                    foreach (bool visible in new[] {false,true})
                    {
                        overlay.Visible = visible;
                        var device = DXManager.Device;
                        device.Clear(ClearFlags.Target,Color.FromArgb(40,80,120),1,0); device.BeginScene(); DXManager.Sprite.Begin(SpriteFlags.AlphaBlend);
                        var opacity = DXManager.Opacity; var blend = DXManager.Blending; var rate = DXManager.BlendRate;
                        try { orb.Draw(); }
                        finally { DXManager.Sprite.End(); device.EndScene(); }
                        Check(DXManager.Opacity == opacity && DXManager.Blending == blend && DXManager.BlendRate == rate,"Orb overlay leaked render state");
                        using (var capture = Surface.ToStream(DXManager.MainSurface,ImageFileFormat.Png))
                        using (var image = new Bitmap(capture))
                        using (var crop = image.Clone(orb.DisplayArea,System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                            crop.Save(visible ? output : baseline,System.Drawing.Imaging.ImageFormat.Png);
                    }
                    using (var before = new Bitmap(baseline))
                    using (var after = new Bitmap(output))
                    {
                        int changed = 0, opaque = 0;
                        for (int y=0;y<art.Height;y++) for (int x=0;x<art.Width;x++)
                        {
                            var expected = art.GetPixel(x,y);
                            Check(expected.A == 0 || expected.A == 255,"Orb ornament alpha is not binary");
                            if (expected.A == 255)
                            {
                                Check(after.GetPixel(x,y).ToArgb() == expected.ToArgb(),"Orb ornament missing/scaled/covered at "+states[state]+" "+x+","+y);
                                opaque++; if (before.GetPixel(x,y).ToArgb() != after.GetPixel(x,y).ToArgb()) changed++;
                            }
                            else Check(before.GetPixel(x,y) == after.GetPixel(x,y),"Transparent ornament pixel masks the live orb");
                        }
                        Check(opaque > 500 && changed > 100,"Orb ornament did not affect the rendered resource surface");
                    }
                    var probe = new Point(orb.DisplayArea.X+94,orb.DisplayArea.Y+125);
                    orb.OnMouseMove(new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.None,0,probe.X,probe.Y,0));
                    Check(DXControl.MouseControl != overlay,"Orb decoration intercepted pointer input");
                }
                File.Delete(Path.Combine(directory,"hud-orb-baseline.png"));
            }
            finally
            {
                Set(user,"_CurrentHP",hp); Set(user,"_CurrentMP",mp);
                user.Stats[Stat.Health] = maxHp; user.Stats[Stat.Mana] = maxMp;
                overlay.Visible = true; DXControl.MouseControl = oldMouse;
            }
        }
    }
    private static void QuickbarSavedDefaultMigration(string directory)
    {
        var store=new GameUiLayoutStore(Path.Combine(directory,"quickbar-migration"));
        foreach(var resolution in new[]{new Size(1024,768),new Size(1366,768),new Size(1280,800),new Size(1440,900),new Size(1600,900),new Size(1920,1080)})
        {
            foreach(var oldOffset in new[]{198,182})
            {
            var oldDefault=new Point((resolution.Width-459)/2,resolution.Height-oldOffset);
            var size=new Size(459,44);
            foreach(var delta in new[]{Point.Empty,new Point(1,0),new Point(0,-1)})
            {
                var position=new Point(oldDefault.X+delta.X,oldDefault.Y+delta.Y);
                Check(store.TrySave(resolution,WindowType.BeltBox,new GameUiWindowLayout(position,size,false)),"Cannot seed saved belt geometry");
                var bytes=File.ReadAllBytes(store.FilePath);
                store.Reload(); GameUiWindowLayout saved;
                Check(store.TryGet(resolution,WindowType.BeltBox,out saved),"Saved belt disappeared");
                var expected=delta==Point.Empty?new Point((resolution.Width-518)/2,resolution.Height-182):position;
                Check(saved.Location==expected && saved.Size==new Size(518,44) && saved.Visible==false,"Migration moved custom belt or lost settings");
                store.Reload(); Check(store.TryGet(resolution,WindowType.BeltBox,out saved) && saved.Location==expected,"Migration applied twice");
                Check(File.ReadAllBytes(store.FilePath).SequenceEqual(bytes),"Read migration changed on-disk settings");
            }
            }
        }
    }
    private static void Exercise(TestScene root,GameScene game,GameUiVerifiedPackage package,string directory)
    {
        QuickbarSavedDefaultMigration(directory);
        using(var hud=new MainPanel {Parent=root})
        using(var character=new CharacterDialog {Parent=root,Visible=false})
        using(var inventory=new InventoryDialog {Parent=root,Visible=false})
        using(var belt=new BeltDialog(true) {Parent=root})
        using(var minimap=new MiniMapDialog {Parent=root})
        using(var goods=new NPCGoodsDialog {Parent=root,Visible=false})
        using(var input=new ChatTextBox {Parent=root,Visible=true})
        using(var chatGroup=new DXTabControl {Parent=root,Size=new Size(400,147),Location=new Point(225,560),Movable=true})
        using(var chat=new ChatTab {Parent=chatGroup,AllowResize=true})
        {
            game.MainPanel=hud; game.CharacterBox=character; game.InventoryBox=inventory; game.BeltBox=belt; game.MiniMapBox=minimap;game.ChatTextBox=input;
            var originalSettings=(WindowSetting)FormatterServices.GetUninitializedObject(typeof(WindowSetting));character.Settings=originalSettings;
            var native=Descendants(root).Select(c=>new Native(c)).ToArray();
            var order=Children(root).ToArray(); var equipment=character.Grid.ToArray(); var items=inventory.Grid.Grid.ToArray();
            var slots=belt.Grid.Grid.ToArray();var links=belt.Links.ToArray();var grid=belt.Grid;var panel=minimap.Panel;
            var inputBox=input.TextBox;var text=inputBox.TextBox; text.Text="未发送中文草稿 English";text.Select(2,4);
            var handle=text.Handle; var selectionStart=text.SelectionStart;var selectionLength=text.SelectionLength;
            DXTextBox.ActiveTextBox=inputBox;
            input.Mode=ChatMode.Guild; input.LastPM="/原角色";
            var message=new DXLabel {Parent=chat.TextPanel,Text="已有聊天消息",ForeColour=Color.Cyan};chat.History.Add(message);chat.UpdateItems();
            var font=message.Font; var tag=message.Tag;var history=chat.History.ToArray(); int tabCount=ChatTab.Tabs.Count;
            // Chat state is captured after adding the existing message; it is never recreated by this installer.
            native=Descendants(root).Select(c=>new Native(c)).ToArray();
            var store=new GameUiLayoutStore(directory); var set=new GameUiOfficialV1WindowSet(root,hud,character,inventory,belt,minimap,package,store);
            Exception otherThread=null;var thread=new Thread(()=> {try{set.Install(Config.GameSize);}catch(Exception e){otherThread=e;}});thread.Start();thread.Join();
            Check(otherThread is InvalidOperationException && !set.IsInstalled,"Cross-thread installation mutated controls");
            var delayed=new GameUiOfficialV1WindowSet(root,hud,character,inventory,belt,minimap,package,store);
            Reject<ArgumentException>(()=>character.UseGameUiCharacterBackdrop(package.Library,GameUiOfficialV1Catalog.PackageManifest.Frames["character-main-background"].Index),"Backdrop without page layout");
            Check(character.GameUiCharacterBackdropLibrary==null&&character.GameUiCharacterBackdropFrame==-1,"Rejected backdrop mutated binding");
            set.Install(Config.GameSize);
            Check(goods.UsesGameUiGoods&&goods.WindowSkin!=null&&goods.Size==new Size(344,438),"Independent goods window included in scene transaction");
            Reject<InvalidOperationException>(()=>delayed.Install(Config.GameSize),"Stale uninstalled owner");delayed.Dispose();
            Check(character.WindowSkin!=null&&set.IsInstalled,"Uninstalled owner disposal removed active skin");
            Check(set.IsInstalled&&!set.IsFaulted,"Combined install failed");
            Check(character.Size==new Size(412,562)&&inventory.Size==new Size(276,412)&&belt.Size==new Size(518,44)&&minimap.Size==new Size(166,198),"Combined window geometry differs");
            Check(Children(root).Where(c=>order.Contains(c)).SequenceEqual(order),"Installation changed scene stacking order: "+string.Join(",",Children(root).Select(c=>c.GetType().Name))+" expected "+string.Join(",",order.Select(c=>c.GetType().Name)));
            Check(character.GameUiReadouts!=null && character.GameUiEquipmentCellLibrary==package.Library,"Character composition or frame not installed");
            Check(character.GameUiCharacterBackdropLibrary==package.Library && character.GameUiCharacterBackdropFrame==GameUiOfficialV1Catalog.PackageManifest.Frames["character-main-background"].Index,"Page backdrop not installed");
            Reject<ArgumentException>(()=>character.UseGameUiCharacterBackdrop(package.Library,GameUiOfficialV1Catalog.PackageManifest.Frames["character"].Index),"Backdrop with window dimensions");
            Check(character.GameUiCharacterBackdropLibrary==package.Library&&character.GameUiCharacterBackdropFrame==GameUiOfficialV1Catalog.PackageManifest.Frames["character-main-background"].Index,"Invalid frame changed active backdrop");
            Check(hud.GameUiHudRoots.Count==4 && hud.GameUiHudCaptions.Count==7,"HUD roots/captions not installed");
            Check(new[]{"orb","commands","experience"}.All(id=>hud.GameUiHudRoots[id].DrawImage),"Bound HUD frame is hidden");
            Check(Children(root).IndexOf(hud.GameUiHudRoots["commands"])<Children(root).IndexOf(character),"HUD installed above service windows");
            Check(hud.GameUiHealthFill.Library==package.Library&&hud.GameUiManaFill.Library==package.Library&&hud.GameUiExperienceFill.Library==package.Library,"HUD fills not bound to verified package");
            Check(hud.GameUiHudRoots["experience"].Index!=hud.GameUiExperienceFill.Index,"XP track and fill share a frame");
            Check(((DXButton)hud.GameUiHudControls["BeltButton"]).DrawImage && ((DXButton)hud.GameUiHudControls["BeltButton"]).Library==package.Library && new Rectangle(hud.GameUiHudControls["BeltButton"].Location,hud.GameUiHudControls["BeltButton"].Size)==new Rectangle(52,76,42,50),"Belt action artwork or geometry is not installed");
            LegacyGlyph(character,package,directory,true);
            Reject<InvalidOperationException>(()=>set.Install(Config.GameSize),"Duplicate installation");
            Reject<InvalidOperationException>(()=>new GameUiOfficialV1WindowSet(root,hud,character,inventory,belt,minimap,package,store),"Conflicting owner");
            var resolutions=new[]{new Size(1024,768),new Size(1366,768),new Size(1280,800),new Size(1440,900),new Size(1600,900),new Size(1920,1080)};
            foreach(var resolution in resolutions)
            {
                Config.GameSize=resolution;root.Size=resolution;Check(set.Apply(resolution),"Resize did not apply");
                Check(set.Apply(resolution),"Repeated resize failed");
                Check(set.Placements.Count==8,"Missing placement");
                foreach(var pair in set.Placements)
                {
                    Check(new Rectangle(Point.Empty,resolution).Contains(pair.Value.Bounds),"Window outside client");
                    string id=pair.Key.StartsWith("hud-")?pair.Key.Substring(4):pair.Key;
                    Check(pair.Value.Bounds==expectedPlacements[resolution.Width+"x"+resolution.Height+"|"+id],"Implementation differs from independent layout contract: "+resolution+" "+id+" actual="+pair.Value.Bounds+" expected="+expectedPlacements[resolution.Width+"x"+resolution.Height+"|"+id]);
                }
                Check(hud.GameUiHudRoots["orb"].Location==new Point((resolution.Width-1024)/2,resolution.Height-202)&&hud.GameUiHudRoots["commands"].Location==new Point((resolution.Width-1024)/2+799,resolution.Height-202),"HUD bottom anchors differ");
                CommandDecorationHits(hud);
                Check(hud.GameUiHudRoots["readouts"].Location==new Point((resolution.Width-574)/2,resolution.Height-154)&&hud.GameUiHudRoots["experience"].Location==new Point((resolution.Width-574)/2,resolution.Height-8),"HUD central anchors differ");
                Check(!hud.GameUiHudRoots["readouts"].IsVisible,"Hidden HUD stats returned after resize");
                Check(belt.DisplayArea.Bottom==chatGroup.DisplayArea.Top,"Quickbar is not directly above chat");
                Check(character.Grid.SequenceEqual(equipment)&&inventory.Grid.Grid.SequenceEqual(items),"Equipment/inventory cell replaced");
                Check(belt.Grid==grid&&belt.Grid.Grid.SequenceEqual(slots)&&belt.Links.SequenceEqual(links),"Belt cell/link replaced");
                Check(minimap.Panel==panel,"Minimap viewport replaced");
                minimap.Visible=true;minimap.CloseButton.InvokeMouseClick();
                Check(minimap.Visible&&minimap.GameUiMiniMapCollapsed&&minimap.Size==new Size(166,46),"Combined collapse hid window");
                Check(set.Apply(resolution)&&set.Apply(resolution)&&!set.IsFaulted,"Collapsed layout reapply failed");
                Check(set.Placements["minimap"].Bounds.Size==new Size(166,46)&&!minimap.Panel.Visible,"Coordinator expanded collapsed window");
                var otherResolution=resolution.Width==1024?new Size(1366,768):new Size(1024,768);
                Config.GameSize=otherResolution;root.Size=otherResolution;
                Check(set.Apply(otherResolution)&&minimap.GameUiMiniMapCollapsed&&minimap.Size==new Size(166,46),"Resolution change expanded collapsed map");
                Config.GameSize=resolution;root.Size=resolution;Check(set.Apply(resolution),"Collapsed resolution restore failed");
                minimap.CloseButton.InvokeMouseClick();Check(set.Apply(resolution)&&minimap.Size==new Size(166,198)&&minimap.Panel==panel&&panel.Visible,"Combined expand did not restore viewport");
                minimap.SetGameUiMiniMapEnlarged(true);
                Check(set.Apply(resolution)&&minimap.Size==new Size(316,348)&&minimap.Panel.Visible,"Coordinator reset enlarged map");
                minimap.SetGameUiMiniMapCollapsed(true);
                Check(set.Apply(resolution)&&set.Placements["minimap"].Bounds.Size==new Size(316,46)&&!minimap.Panel.Visible,"Coordinator reset enlarged collapsed map");
                Config.GameSize=otherResolution;root.Size=otherResolution;
                Check(set.Apply(otherResolution)&&minimap.GameUiMiniMapEnlarged&&minimap.GameUiMiniMapCollapsed&&minimap.Size==new Size(316,46),"Resolution change reset enlarged collapsed map");
                Config.GameSize=resolution;root.Size=resolution;Check(set.Apply(resolution),"Enlarged resolution restore failed");
                minimap.SetGameUiMiniMapCollapsed(false);minimap.SetGameUiMiniMapEnlarged(false);Check(set.Apply(resolution),"Normal map restore failed");
                Check(new Rectangle(chatGroup.Location,chatGroup.Size)==expectedPlacements[resolution.Width+"x"+resolution.Height+"|chat"],"Chat group differs from independent placement contract");
                Check(new Rectangle(input.Location,input.Size)==expectedPlacements[resolution.Width+"x"+resolution.Height+"|chatInput"],"Input differs from independent placement contract");
                Check(chat.Location==Point.Empty&&chat.TextPanel.Location==new Point(4,2)&&chat.TextPanel.Size==new Size(548,100),"Grouped message viewport differs");
                Check(input.TextBox.Location==new Point(64,1)&&input.TextBox.Size.Width==480&&input.OptionsButton.Label.Text=="设","Original input controls not fitted to frame");
                Check(chatGroup.GameUiChatGroup!=null&&input.GameUiInputLayout!=null&&input.WindowSkin!=null,"Chat adapters missing");
                Check(Children(root).Where(c=>order.Contains(c)).SequenceEqual(order),"Resize changed scene stacking order");
                Check(ChatTab.Tabs.Count==tabCount&&chat.History.SequenceEqual(history)&&message.Font==font&&message.ForeColour==Color.Cyan&&message.Tag==tag,"Chat history or style changed");
                Check(input.TextBox==inputBox&&text.Handle==handle&&text.Text=="未发送中文草稿 English"&&text.SelectionStart==selectionStart&&text.SelectionLength==selectionLength,"Chat HWND/draft/selection changed");
                Check(DXTextBox.ActiveTextBox==inputBox&&input.Mode==ChatMode.Guild&&input.LastPM=="/原角色","Chat focus/channel state changed");
            }
            Config.GameSize=new Size(1024,768);root.Size=Config.GameSize;set.Apply(Config.GameSize);
            OrbForeground(game,hud,directory);
            CommandActions(root,game,hud,directory);
            CompactCommandOwnership(hud);
            // Reentrant callbacks must not detach the package while a resize is reading it.
            EventHandler<EventArgs> reenter=(s,e)=>{Reject<InvalidOperationException>(()=>set.Dispose(),"Dispose during apply");Reject<InvalidOperationException>(()=>set.Apply(Config.GameSize),"Reentrant apply");};
            inventory.LocationChanged+=reenter;Config.GameSize=new Size(1366,768);root.Size=Config.GameSize;set.Apply(Config.GameSize);inventory.LocationChanged-=reenter;
            Config.GameSize=new Size(1024,768);root.Size=Config.GameSize;set.Apply(Config.GameSize);
            set.Dispose();set.Dispose();Check(!set.IsInstalled&&!package.IsDisposed,"Disposal leaked installer or disposed borrowed package");
            Check(hud.GameUiHudLayout==null&&hud.GameUiHudRoots==null&&hud.GameUiHudCaptions.Count==0,"HUD detach leaked roots/captions");
            foreach(var n in native)n.AssertRestored();Check(Children(root).Where(c=>order.Contains(c)).SequenceEqual(order),"Disposal changed root order");
            LegacyGlyph(character,package,directory,false);
            Check(character.WindowSkin==null&&inventory.WindowSkin==null&&belt.WindowSkin==null&&minimap.WindowSkin==null,"Skin remained installed");
            Check(character.GameUiCharacterBackdropLibrary==null&&character.GameUiCharacterBackdropFrame==-1,"Page backdrop remained attached after rollback");
            Check(ReferenceEquals(character.Settings,originalSettings),"Original Legacy settings association was lost");
            Check(!File.Exists(store.FilePath),"Programmatic layout wrote persistent settings");
            Reject<ObjectDisposedException>(()=>set.Apply(Config.GameSize),"Disposed installer");
            // Goods-specific install failure must unwind the assigned binding and all earlier windows.
            set=new GameUiOfficialV1WindowSet(root,hud,character,inventory,belt,minimap,package,store);
            bool failGoods=true;EventHandler<EventArgs> goodsFailure=(o,e)=>{if(failGoods&&goods.WindowSkin!=null){failGoods=false;throw new InvalidOperationException("injected goods observer");}};
            goods.SizeChanged+=goodsFailure;Reject<InvalidOperationException>(()=>set.Install(Config.GameSize),"Goods installation rollback");goods.SizeChanged-=goodsFailure;
            Check(!failGoods&&!goods.UsesGameUiGoods&&goods.WindowSkin==null&&set.IsFaulted&&!set.IsInstalled&&!package.IsDisposed,"Goods rollback retains borrowed package and clears adapter");
            foreach(var n in native)n.AssertRestored();set.Dispose();
            // A failed detach keeps ownership, then completes its partially assigned native geometry on retry.
            set=new GameUiOfficialV1WindowSet(root,hud,character,inventory,belt,minimap,package,store);set.Install(Config.GameSize);
            bool failGoodsDetach=true;EventHandler<EventArgs> detachFailure=(o,e)=>{if(failGoodsDetach&&goods.WindowSkin==null){failGoodsDetach=false;throw new InvalidOperationException("injected goods detach observer");}};
            goods.SizeChanged+=detachFailure;Reject<AggregateException>(()=>set.Dispose(),"Retryable goods detach failure");goods.SizeChanged-=detachFailure;
            Check(goods.UsesGameUiGoods&&!package.IsDisposed,"Failed goods detach retains owner for retry");set.Dispose();
            Check(!goods.UsesGameUiGoods&&goods.WindowSkin==null,"Goods detach retry completes");foreach(var n in native)n.AssertRestored();
            // A late installation failure must unwind already installed character, inventory and belt.
            set=new GameUiOfficialV1WindowSet(root,hud,character,inventory,belt,minimap,package,store);
            bool fail=true;EventHandler<EventArgs> failOnce=(s,e)=>{if(fail){fail=false;throw new InvalidOperationException("injected minimap observer");}};
            minimap.SizeChanged+=failOnce;Reject<InvalidOperationException>(()=>set.Install(Config.GameSize),"Late observer failure");minimap.SizeChanged-=failOnce;
            Check(set.IsFaulted&&!set.IsInstalled,"Failed installation not latched");
            foreach(var n in native)n.AssertRestored();Check(Children(root).Where(c=>order.Contains(c)).SequenceEqual(order),"Rollback changed root order");
            Check(!package.IsDisposed,"Rollback disposed borrowed package");set.Dispose();
            // Inject a failure after HUD artwork binding, during the first root placement.
            set=new GameUiOfficialV1WindowSet(root,hud,character,inventory,belt,minimap,package,store);
            bool armed=false; EventHandler<EventArgs> armHud=(o,e)=>
            {
                if(armed || hud.GameUiHudRoots==null)return; armed=true;
                hud.GameUiHudRoots["orb"].LocationChanged+=(sender,args)=>{throw new InvalidOperationException("injected HUD placement observer");};
            };
            hud.HealthLabel.SizeChanged+=armHud;
            Reject<InvalidOperationException>(()=>set.Install(Config.GameSize),"HUD placement rollback");
            hud.HealthLabel.SizeChanged-=armHud;
            Check(armed&&hud.GameUiHudLayout==null&&set.IsFaulted&&!set.IsInstalled,"HUD failure did not detach fully");
            foreach(var n in native)n.AssertRestored();Check(Children(root).SequenceEqual(order),"HUD rollback leaked sibling roots");
            set.Dispose();
            // Chat is installed after the HUD; failure here must unwind the entire scene transaction.
            set=new GameUiOfficialV1WindowSet(root,hud,character,inventory,belt,minimap,package,store);
            int listeners=ChatListenerCount();bool failChat=true;
            EventHandler<EventArgs> chatFailure=(o,e)=>{if(failChat&&input.WindowSkin!=null){failChat=false;throw new InvalidOperationException("injected chat placement observer");}};
            input.LocationChanged+=chatFailure;
            Reject<InvalidOperationException>(()=>set.Install(Config.GameSize),"Late chat placement rollback");
            input.LocationChanged-=chatFailure;
            Check(!failChat&&set.IsFaulted&&!set.IsInstalled,"Chat failure did not fault/unwind scene owner");
            foreach(var n in native)n.AssertRestored();
            Check(ChatListenerCount()==listeners&&input.GameUiInputLayout==null&&chatGroup.GameUiChatGroup==null,"Chat rollback leaked listeners/layout");
            Check(Children(root).SequenceEqual(order)&&!package.IsDisposed,"Chat rollback leaked roots/package");
            set.Dispose();
            // A resize observer fault latches the owner; subsequent calls must not replay or rebuild windows.
            set=new GameUiOfficialV1WindowSet(root,hud,character,inventory,belt,minimap,package,store);set.Install(Config.GameSize);
            bool failResize=true;EventHandler<EventArgs> resizeFailure=(o,e)=>{if(failResize){failResize=false;throw new InvalidOperationException("injected resize observer");}};
            inventory.LocationChanged+=resizeFailure;Config.GameSize=new Size(1366,768);root.Size=Config.GameSize;
            Reject<InvalidOperationException>(()=>set.Apply(Config.GameSize),"Resize observer failure");inventory.LocationChanged-=resizeFailure;
            Check(set.IsFaulted&&set.IsInstalled,"Resize fault did not latch");
            Reject<InvalidOperationException>(()=>set.Apply(Config.GameSize),"Replaying faulted layout");set.Dispose();
            Config.GameSize=new Size(1024,768);root.Size=Config.GameSize;foreach(var n in native)n.AssertRestored();
            Check(ReferenceEquals(character.Settings,originalSettings),"Faulted resize lost Legacy settings");
            // Reproduce a hidden native window plus a persisted hidden OfficialV1 layout.
            string visibilityDirectory=Path.Combine(directory,"minimap-visibility");Directory.CreateDirectory(visibilityDirectory);
            var visibilityStore=new GameUiLayoutStore(visibilityDirectory);
            var savedMapPosition=new Point(800,10);
            Check(visibilityStore.TrySave(Config.GameSize,WindowType.MiniMapBox,
                new GameUiWindowLayout(savedMapPosition,new Size(166,198),false)),"Seed hidden map layout");
            byte[] visibilityBytes=File.ReadAllBytes(visibilityStore.FilePath);
            minimap.Visible=false;
            using(var visibleSet=new GameUiOfficialV1WindowSet(root,hud,character,inventory,belt,minimap,package,visibilityStore))
            {
                visibleSet.Install(Config.GameSize);
                Check(minimap.Visible&&minimap.Panel.Visible&&minimap.Location==savedMapPosition,"Install restored hidden minimap");
                Check(File.ReadAllBytes(visibilityStore.FilePath).SequenceEqual(visibilityBytes),"Visibility recovery rewrote saved layout");
                minimap.ToggleDisplay();visibleSet.Apply(Config.GameSize);
                Check(minimap.Visible&&minimap.GameUiMiniMapCollapsed&&!minimap.Panel.Visible,"Layout pass hid collapsed header");
                minimap.ToggleDisplay();
            }
            minimap.Visible=true;
            using(var invalid=new BeltDialog {Parent=root})
                Reject<InvalidOperationException>(()=>new GameUiOfficialV1WindowSet(root,hud,character,inventory,invalid,minimap,package,store),"Invalid belt accepted");
        }
    }
    private static void InstallScene(GameScene scene,GameUiVerifiedPackage package,GameUiLayoutStore store)
    {
        try { typeof(GameScene).GetMethod("InstallOfficialWindowCandidate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(scene,new object[]{package,store}); }
        catch(TargetInvocationException e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw; }
    }
    // Only the constructor's network-heavy world setup is bypassed. All tested methods,
    // controls, event dispatch, size setters and disposal are the real production types.
    private static GameScene SceneFixture()
    {
        var scene=(GameScene)FormatterServices.GetUninitializedObject(typeof(GameScene));GC.SuppressFinalize(scene);
        Set(scene,"<Controls>k__BackingField",new List<DXControl>());
        Set(scene,"_Size",Config.GameSize);Set(scene,"_DisplayArea",new Rectangle(Point.Empty,Config.GameSize));
        Set(scene,"_Visible",true);Set(scene,"_IsVisible",true);Set(scene,"_Enabled",true);Set(scene,"_IsEnabled",true);Set(scene,"_Opacity",1F);
        GameScene.Game=scene;DXControl.ActiveScene=scene;
        var user=(UserObject)FormatterServices.GetUninitializedObject(typeof(UserObject));Set(user,"_Stats",new Stats());Set(user,"_HermitStats",new Stats());Set(scene,"_User",user);
        scene.Equipment=new ClientUserItem[Globals.EquipmentSize];scene.Inventory=new ClientUserItem[500];scene.DataDictionary=new SortedDictionary<uint,ClientObjectData>();
        scene.MapControl=new MapControl {Parent=scene,Width=600,Height=1040};Set(scene.MapControl,"_MapInfo",new MapInfo());
        scene.MainPanel=new MainPanel {Parent=scene};scene.CharacterBox=new CharacterDialog {Parent=scene,Visible=false};
        scene.InventoryBox=new InventoryDialog {Parent=scene,Visible=false};scene.BeltBox=new BeltDialog(true) {Parent=scene};scene.MiniMapBox=new MiniMapDialog {Parent=scene};
        scene.StorageBox=new StorageDialog {Parent=scene,Visible=false};
        scene.NPCBox=new NPCDialog {Parent=scene,Visible=false};
        scene.ChatTextBox=new ChatTextBox {Parent=scene,Visible=true};return scene;
    }
    private static int ChatListenerCount()
    {
        var listeners=(Delegate)typeof(ChatTab).GetField("GameUiTopologyChanged",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        return listeners?.GetInvocationList().Length ?? 0;
    }
    private static void RefreshChatFrame(DXTabControl group)
        => typeof(DXControl).GetMethod("OnBeforeDraw",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(group,null);
    private static void RenderChat(DXTabControl group, ChatTextBox input, DXControl rail, string path)
    {
        var device=DXManager.Device;
        device.Clear(ClearFlags.Target,Color.Magenta,1,0);device.BeginScene();DXManager.Sprite.Begin(SpriteFlags.AlphaBlend);
        var interfaceField=typeof(DXControl).GetField("InterfaceLibrary",BindingFlags.Static|BindingFlags.NonPublic);
        var previousLibrary=interfaceField.GetValue(null);interfaceField.SetValue(null,input.WindowSkin.Library);
        try { group.Draw();input.Draw();rail.Draw(); }
        finally { interfaceField.SetValue(null,previousLibrary);DXManager.Sprite.End();device.EndScene(); }
        Surface.ToFile(DXManager.MainSurface,path,ImageFileFormat.Png);
        using(var image=new Bitmap(path))
        {
            Check(image.GetPixel(group.Location.X+280,group.Location.Y+10).ToArgb()==Color.Black.ToArgb(),"Header area is not opaque black");
            Check(image.GetPixel(group.Location.X+280,group.Location.Y+50).ToArgb()==Color.Black.ToArgb(),"History background is not opaque black");
            Check(image.GetPixel(input.Location.X+280,input.Location.Y+10).ToArgb()==Color.Black.ToArgb(),"Input background is not opaque black");
            Check(image.GetPixel(group.Location.X+100,group.Location.Y).ToArgb()!=Color.Black.ToArgb(),"Original bronze border artwork was not drawn");
        }
    }
    private static void ExerciseChat(TestScene root,GameUiVerifiedPackage package,string directory)
    {
        Config.GameSize=new Size(1024,768);root.Size=Config.GameSize;
        int initialCount=ChatTab.Tabs.Count,initialListeners=ChatListenerCount();
        using(var input=new ChatTextBox {Parent=root,Visible=true,Location=new Point(225,730)})
        using(var primary=new DXTabControl {Parent=root,Size=new Size(400,147),Location=new Point(225,560),Movable=true})
        using(var secondary=new DXTabControl {Parent=root,Size=new Size(300,140),Location=new Point(30,30),Movable=true})
        using(var a=new ChatTab {Parent=primary})
        using(var b=new ChatTab {Parent=primary})
        using(var c=new ChatTab {Parent=secondary})
        {
            a.Panel=new ChatOptionsPanel {Parent=root,Visible=false};
            a.Panel.TransparentCheckBox.CheckedChanged+=(o,e)=>a.TransparencyChanged();
            primary.SelectedTab=a;a.Panel.TransparentCheckBox.Checked=false;a.TransparencyChanged();
            var historySkin=new GameUiWindowSkin(package.Library,GameUiOfficialV1Catalog.PackageManifest.Frames["chat-history-frame"].Index,
                System.Windows.Forms.Padding.Empty,new System.Windows.Forms.Padding(4),System.Windows.Forms.Padding.Empty,
                GameUiChatGroupBinding.MinimumSize,false,Rectangle.Empty,Rectangle.Empty,Rectangle.Empty);
            var inputSkin=new GameUiWindowSkin(package.Library,GameUiOfficialV1Catalog.PackageManifest.Frames["chat-input-frame"].Index,
                System.Windows.Forms.Padding.Empty,System.Windows.Forms.Padding.Empty,System.Windows.Forms.Padding.Empty,
                new Size(574,22),true,Rectangle.Empty,Rectangle.Empty,Rectangle.Empty);
            var inputBounds=new Rectangle(input.Location,input.Size);string options=input.OptionsButton.Label.Text;
            var primaryBounds=new Rectangle(primary.Location,primary.Size);var secondaryBounds=new Rectangle(secondary.Location,secondary.Size);
            var editor=input.TextBox.TextBox;editor.Text="拖动期间保留草稿";editor.Select(1,2);var handle=editor.Handle;
            DXTextBox.ActiveTextBox=input.TextBox;
            var label=new DXLabel {Parent=a.TextPanel,Text="保留原生消息",ForeColour=Color.Cyan,Tag=MessageType.System};a.History.Add(label);a.UpdateItems();
            var set=new GameUiOfficialV1ChatSet(root,input,historySkin,inputSkin);
            try
            {
                set.Install(Config.GameSize);
                Check(!a.TabButton.Visible&&!b.TabButton.Visible&&!c.TabButton.Visible&&a.Location==Point.Empty, "Official chat retained a visible header or its empty row");
                Check(set.BoundGroupCount==2&&ChatListenerCount()==initialListeners+1,"Chat owner missing or leaked listener");
                Check(new Rectangle(primary.Location,primary.Size)==new Rectangle(225,630,574,106),"Primary chat docking differs");
                Check(new Rectangle(secondary.Location,secondary.Size)==secondaryBounds,"Secondary group moved on install");
                var frame=Children(primary).OfType<DXWindow>().Single();
                Check(frame.WindowSkin==historySkin&&!a.DrawTexture&&!a.DrawOtherBorder,"Chat frame/native background conflict");
                a.Panel.TransparentCheckBox.Checked=true;RefreshChatFrame(primary);
                Check(!frame.Visible&&!a.ScrollBar.Visible&&!a.AllowResize,"Transparent chat frame/resize changed");
                a.Panel.TransparentCheckBox.Checked=false;RefreshChatFrame(primary);
                Check(frame.Visible&&a.ScrollBar.Visible&&a.AllowResize&&!a.DrawTexture&&!a.DrawOtherBorder,"Opaque chat did not retain frame ownership");
                Check(frame.Opacity==1F,"Chat background inherited the native half opacity");
                var buttons=(DXButton[])Field(typeof(GameUiChatGroupBinding),"channelButtons").GetValue(primary.GameUiChatGroup);
                var filters=new[]{a.Panel.LocalCheckBox,a.Panel.WhisperCheckBox,a.Panel.ShoutCheckBox,a.Panel.GroupCheckBox,a.Panel.GuildCheckBox};
                var types=new[]{MessageType.Normal,MessageType.WhisperIn,MessageType.Shout,MessageType.Group,MessageType.Guild};
                var names=new[]{"公聊","私聊","喊话","组队聊天","行会聊天"};
                for(int i=0;i<5;i++)
                {
                    filters[i].Checked=true;
                    Check(buttons[i].Hint.StartsWith("拒绝"+names[i]+"信息")&&!buttons[i].Pressed,"Channel order/initial state differs");
                    var bounds=buttons[i].DisplayArea;int mouseX=bounds.X+bounds.Width/2,mouseY=bounds.Y+bounds.Height/2;
                    root.OnMouseMove(new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.None,0,mouseX,mouseY,0));
                    Check(DXControl.MouseControl==buttons[i],"Scene pointer routing did not reach channel button");
                    var originalSound=buttons[i].Sound;buttons[i].Sound=SoundIndex.None;
                    try
                    {
                        DXControl.MouseControl.OnMouseClick(new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Right,1,mouseX,mouseY,0));
                        Check(filters[i].Checked,"Right click unexpectedly toggled channel filter");
                        DXControl.MouseControl.OnMouseClick(new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left,1,mouseX,mouseY,0));
                    }
                    finally { buttons[i].Sound=originalSound; }
                    Check(!filters[i].Checked&&buttons[i].Pressed&&buttons[i].Enabled,"Reject did not persist its appearance or disabled re-enabling");
                    a.ReceiveChat("channel-filter-probe",types[i]);
                    Check(a.History.Count==1,"Rejected channel still added messages");
                    buttons[i].InvokeMouseClick();
                    Check(filters[i].Checked&&!buttons[i].Pressed,"Second click did not restore receiving");
                    a.ReceiveChat("channel-filter-probe",types[i]);
                    Check(a.History.Count==2,"Re-enabled channel did not receive messages");
                    var received=a.History[1];a.History.Remove(received);received.Dispose();a.UpdateItems();
                }
                root.OnMouseMove(new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.None,0,0,0,0));
                string receiving=Path.Combine(directory,"chat-receiving.png"),rejected=Path.Combine(directory,"chat-rejected.png");
                RenderChat(primary,input,buttons[0].Parent,receiving);
                foreach(var button in buttons) button.InvokeMouseClick();
                RenderChat(primary,input,buttons[0].Parent,rejected);
                using(var before=new Bitmap(receiving)) using(var after=new Bitmap(rejected))
                foreach(var button in buttons)
                {
                    bool differs=false;var bounds=button.DisplayArea;
                    for(int y=bounds.Top;y<bounds.Bottom&&!differs;y++) for(int x=bounds.Left;x<bounds.Right;x++)
                        if(before.GetPixel(x,y)!=after.GetPixel(x,y)) { differs=true;break; }
                    Check(differs,"Rejected channel did not change actual rendered artwork");
                }
                foreach(var button in buttons) button.InvokeMouseClick();
                primary.SelectedTab=b;
                Check(!a.TabButton.Visible&&!b.TabButton.Visible,"Selection restored a hidden chat header");
                Check(buttons.All(button=>!button.Enabled),"Panel-less page reused another page's filters");
                primary.SelectedTab=a;
                Check(buttons.All(button=>button.Enabled&&!button.Pressed),"Selected page did not restore its channel state");
                a.Panel.LocalCheckBox.Checked=false;
                Check(buttons[0].Pressed,"Settings panel did not synchronize reject appearance");
                a.Panel.LocalCheckBox.Checked=true;
                primary.Location=new Point(300,500);
                Check(buttons[0].Parent.Location==new Point(265,500),"Channel rail did not follow a dragged group");
                primary.Location=new Point(225,630);
                // Execute original native drag handlers: temporary label, drop-created group, then merge.
                a.TabButton.IsMoving=true;a.TabButton.Location=new Point(40,-200);
                Check(a.Parent is DXLabel&&a.GameUiViewportLayout==null,"Native drag-out did not release viewport");
                var ghost=a.Parent;
                a.TabButton.IsMoving=false;
                var detached=a.Parent as DXTabControl;
                Check(detached!=null&&detached!=primary&&ghost.IsDisposed&&detached.GameUiChatGroup!=null,"Native drop did not discover new group");
                Check(a.Location.Y==0&&set.BoundGroupCount==3,"Drop-created group not adapted");
                Check(Children(detached).First() is DXWindow,"Dropped header renders below frame");
                a.TabButton.IsMoving=true;
                a.TabButton.Location=new Point(secondary.Location.X-detached.Location.X+6,secondary.Location.Y-detached.Location.Y+2);
                a.TabButton.IsMoving=false;
                Check(a.Parent==secondary&&a.GameUiViewportLayout!=null&&detached.IsDisposed,"Native merge retained empty group or lost adapter");
                Check(set.BoundGroupCount==2&&secondary.TabButtons.Contains(a.TabButton),"Native merge lost group/button");
                Check(Children(secondary).First() is DXWindow,"Merged header renders below frame");
                var order=secondary.TabButtons.ToArray();
                primary.Location=new Point(35,500);primary.Size=new Size(460,160);
                set.Apply(new Size(1920,1080));
                Check(primary.Location==new Point(35,500)&&primary.Size==new Size(460,160),"User floating geometry replaced on resize");
                secondary.Size=new Size(1200,780);secondary.Location=new Point(950,700);set.Apply(new Size(1024,768));
                Check(secondary.Size==new Size(1024,768)&&secondary.Location==Point.Empty,"Floating group did not clamp into client");
                Check(secondary.TabButtons.SequenceEqual(order),"Resize reordered chat tabs");
                var dynamicGroup=new DXTabControl {Parent=root,Size=new Size(320,140),Location=new Point(600,100)};
                using(var added=new ChatTab {Parent=dynamicGroup})
                {
                    Check(dynamicGroup.GameUiChatGroup!=null&&added.GameUiViewportLayout!=null&&set.BoundGroupCount==3,"New native group/tab not discovered");
                }
                dynamicGroup.Dispose();
                Check(set.BoundGroupCount==2,"Disposed group retained in live group count");
                a.Panel.TransparentCheckBox.Checked=false;
                set.Dispose();set.Dispose();
                Check(ChatListenerCount()==initialListeners,"Chat topology listener retained after dispose");
                Check(new Rectangle(primary.Location,primary.Size)==primaryBounds&&new Rectangle(secondary.Location,secondary.Size)==secondaryBounds,"Original group bounds not restored");
                Check(input.GameUiInputLayout==null&&input.WindowSkin==null&&input.OptionsButton.Label.Text==options&&new Rectangle(input.Location,input.Size)==inputBounds,"Input rollback incomplete");
                Check(a.DrawTexture&&a.DrawOtherBorder&&a.GameUiViewportLayout==null&&Children(secondary).OfType<DXWindow>().Count()==0,"Chat background/frame rollback incomplete");
                Check(a.Parent==secondary&&secondary.TabButtons.SequenceEqual(order)&&a.History.Count==1&&a.History[0]==label&&!label.IsDisposed,"Rollback rewound tab topology/history");
                Check(editor.Handle==handle&&editor.Text=="拖动期间保留草稿"&&editor.SelectionStart==1&&editor.SelectionLength==2&&DXTextBox.ActiveTextBox==input.TextBox,"Native drag/rollback lost editor state");
                using(var post=new ChatTab {Parent=primary}) Check(post.GameUiViewportLayout==null,"Disposed owner adapted later tab");
            }
            finally {set.Dispose();}
        }
        Check(ChatTab.Tabs.Count==initialCount&&ChatListenerCount()==initialListeners,"Chat set leaked static state");
    }
    private static void ExerciseScene(GameUiVerifiedPackage package,string directory)
    {
        foreach(bool injectResizeFailure in new[]{false,true})
        {
            Config.GameSize=new Size(1024,768);var scene=SceneFixture();
            try
            {
                var store=new GameUiLayoutStore(Path.Combine(directory,injectResizeFailure?"faulted-scene.json":"scene.json"));
                var character=scene.CharacterBox;var inventory=scene.InventoryBox;var belt=scene.BeltBox;var minimap=scene.MiniMapBox;
                var settings=(WindowSetting)FormatterServices.GetUninitializedObject(typeof(WindowSetting));character.Settings=settings;
                var native=Descendants(scene).Select(c=>new Native(c)).ToArray();var originalOrder=Children(scene).ToArray();
                var input=scene.ChatTextBox;var text=input.TextBox.TextBox;text.Text="场景缩放保留草稿";text.Select(1,3);var hwnd=text.Handle;DXTextBox.ActiveTextBox=input.TextBox;input.Mode=ChatMode.Group;
                var chatGroup=new DXTabControl {Parent=scene,Size=new Size(400,147),Location=new Point(225,560),Movable=true};
                var chat=new ChatTab {Parent=chatGroup,AllowResize=true};var message=new DXLabel {Parent=chat.TextPanel,Text="原聊天样式",ForeColour=Color.Cyan};chat.History.Add(message);chat.UpdateItems();
                int chatCount=ChatTab.Tabs.Count;var font=message.Font;var history=chat.History.ToArray();
                // A late install failure clears the scene's owner and permits a clean retry.
                bool failInstall=true;EventHandler<EventArgs> installFailure=(o,e)=>{if(failInstall){failInstall=false;throw new InvalidOperationException("scene install failure");}};
                minimap.SizeChanged+=installFailure;Reject<InvalidOperationException>(()=>InstallScene(scene,package,store),"Scene late-install failure");minimap.SizeChanged-=installFailure;
                foreach(var n in native)n.AssertRestored();Check(Field(typeof(GameScene),"officialWindowSet").GetValue(scene)==null,"Scene kept a rolled-back owner");
                var storage=scene.StorageBox;var storageCells=storage.Grid.Grid.ToArray();
                bool failStorage=true;EventHandler<EventArgs> storageInstallFailure=(o,e)=>{if(failStorage){failStorage=false;throw new InvalidOperationException("warehouse layout failure");}};
                storage.ItemNameTextBox.Parent.LocationChanged+=storageInstallFailure;
                Reject<AggregateException>(()=>InstallScene(scene,package,store),"Warehouse adapter install failure");
                storage.ItemNameTextBox.Parent.LocationChanged-=storageInstallFailure;
                foreach(var n in native)n.AssertRestored();
                Check(storage.WindowSkin==null&&storage.GameUiStorageLayout==null&&Field(typeof(DXWindow),"_OfficialLayout").GetValue(storage)==null,"Warehouse failure retained package/layout ownership");
                InstallScene(scene,package,store);Reject<InvalidOperationException>(()=>InstallScene(scene,package,store),"Scene duplicate install");
                Check(storage.WindowSkin.FrameIndex==93&&ReferenceEquals(storage.WindowSkin.Library,package.Library)&&storage.Size==new Size(508,480),"Warehouse not installed from full catalog");
                Check(storage.Grid.Grid.SequenceEqual(storageCells)&&storage.GameUiStorageLayout!=null,"Warehouse replaced original grid");
                Check(!File.Exists(store.FilePath),"Warehouse install/rollback wrote layout settings");
                var warehousePosition=storage.Location;
                scene.NPCBox.Visible=true;scene.NPCBox.Visible=false;
                Check(storage.Location==warehousePosition,"Native NPC open/close overwrote official warehouse position");
                storage.Visible=true;Check(inventory.Visible,"Warehouse lost native inventory linkage");
                storage.CloseButton.InvokeMouseClick();Check(!storage.Visible&&inventory.Visible,"Warehouse close changed native visibility rules");
                byte[] savedLayout=File.Exists(store.FilePath)?File.ReadAllBytes(store.FilePath):null;
                var owner=(GameUiOfficialV1WindowSet)Field(typeof(GameScene),"officialWindowSet").GetValue(scene);
                EventHandler<EventArgs> reentry=(o,e)=>{
                    Reject<InvalidOperationException>(()=>scene.Dispose(),"Scene dispose during resize");
                    Reject<InvalidOperationException>(()=>InstallScene(scene,package,store),"Scene install during resize");
                    Reject<InvalidOperationException>(()=>scene.OnSizeChanged(scene.Size,scene.Size),"Scene recursive resize");
                };
                scene.SizeChanged+=reentry;
                foreach(var size in new[]{new Size(1366,768),new Size(1280,800),new Size(1440,900),new Size(1600,900),new Size(1920,1080),new Size(1024,768)})
                {
                    Config.GameSize=size;scene.Size=size;
                    Check(scene.MapControl.Size==size&&owner.Placements.Count==9,"Actual scene failed to resize map/windows");
                    Check(storage.Location==new Point(size.Width-804,86)&&storage.Size==new Size(508,480),"Warehouse anchor differs from inventory-side contract");
                    Check(!storage.DisplayArea.IntersectsWith(inventory.DisplayArea)&&storage.DisplayArea.Bottom<=size.Height-202,"Warehouse overlaps inventory/HUD");
                    Check(storage.Grid.Grid.SequenceEqual(storageCells),"Scene resize rebuilt storage cells");
                    Check(chat.History.SequenceEqual(history)&&ChatTab.Tabs.Count==chatCount&&message.Font==font&&message.ForeColour==Color.Cyan,"Scene rebuilt or restyled chat");
                    Check(text.Handle==hwnd&&text.Text=="场景缩放保留草稿"&&text.SelectionStart==1&&text.SelectionLength==3&&DXTextBox.ActiveTextBox==input.TextBox&&input.Mode==ChatMode.Group,"Scene lost native input state");
                    Check(originalOrder.All(c=>!c.IsDisposed&&c.Parent==scene),"Scene replaced an original control");
                    Check(File.Exists(store.FilePath)==(savedLayout!=null)&&(savedLayout==null||File.ReadAllBytes(store.FilePath).SequenceEqual(savedLayout)),"Actual scene resize wrote layout settings");
                }
                scene.SizeChanged-=reentry;
                if(injectResizeFailure)
                {
                    bool fail=true;EventHandler<EventArgs> observer=(o,e)=>{if(fail){fail=false;throw new InvalidOperationException("scene resize failure");}};
                    inventory.LocationChanged+=observer;Config.GameSize=new Size(1366,768);Reject<InvalidOperationException>(()=>scene.Size=Config.GameSize,"Actual scene resize fault");inventory.LocationChanged-=observer;
                    Check(owner.IsFaulted&&owner.IsInstalled,"Scene resize fault lost owner");
                    Reject<InvalidOperationException>(()=>scene.OnSizeChanged(scene.Size,scene.Size),"Scene replayed faulted owner");
                }
                bool detachmentObserved=false;
                scene.Disposing+=(o,e)=>{
                    detachmentObserved=true;
                    Check(character.WindowSkin==null&&inventory.WindowSkin==null&&belt.WindowSkin==null&&minimap.WindowSkin==null,"Scene destroyed controls before detaching skins");
                    Check(storage.WindowSkin==null&&storage.GameUiStorageLayout==null&&Field(typeof(DXWindow),"_OfficialLayout").GetValue(storage)==null,"Scene destroyed storage before detaching its catalog owner");
                    Check(scene.MainPanel.GameUiHudLayout==null&&scene.MainPanel.GameUiHudRoots==null,"Scene destroyed original controls before detaching HUD");
                    Check(character.GameUiEquipmentLayout==null&&inventory.GameUiInventoryLayout==null&&belt.GameUiBeltLayout==null&&minimap.GameUiMiniMapLayout==null,"Scene left adapters installed before destruction");
                    Check(!package.IsDisposed&&ReferenceEquals(character.Settings,settings),"Scene disposed package or lost Legacy settings");
                    Reject<ObjectDisposedException>(()=>InstallScene(scene,package,store),"Installation while disposing");
                };
                scene.Dispose();Check(detachmentObserved&&scene.IsDisposed&&!package.IsDisposed,"Actual scene teardown incomplete");
                Reject<ObjectDisposedException>(()=>InstallScene(scene,package,store),"Installation after disposal");
            }
            finally { if(!scene.IsDisposed)scene.Dispose(); }
        }
    }
    [STAThread] private static int Main(string[] args)
    {
        bool chatOnly=args.Contains("--chat-only");
        var oldGame=GameScene.Game;var oldActive=DXControl.ActiveScene;var oldSize=Config.GameSize;
        var oldNPC=Globals.NPCInfoList;var oldMovement=Globals.MovementInfoList;var oldCastles=CEnvir.CastleInfoList;
        MirLibrary oldLibrary;bool hadLibrary=CEnvir.LibraryList.TryGetValue(LibraryFile.GameInter,out oldLibrary);string emptyFile=Path.GetTempFileName();
        try
        {
            foreach(string line in File.ReadAllLines(args[2]))
            {
                var fields=line.Split('|');var values=fields[2].Split(',').Select(int.Parse).ToArray();
                expectedPlacements.Add(fields[0]+"|"+fields[1],new Rectangle(values[0],values[1],values[2],values[3]));
            }
            Config.GameSize=new Size(1024,768);
            CEnvir.CastleInfoList=(DBCollection<CastleInfo>)FormatterServices.GetUninitializedObject(typeof(DBCollection<CastleInfo>));CEnvir.CastleInfoList.Binding=new List<CastleInfo>();
            Globals.NPCInfoList=(DBCollection<NPCInfo>)FormatterServices.GetUninitializedObject(typeof(DBCollection<NPCInfo>));Globals.NPCInfoList.Binding=new List<NPCInfo>();
            Globals.MovementInfoList=(DBCollection<MovementInfo>)FormatterServices.GetUninitializedObject(typeof(DBCollection<MovementInfo>));Globals.MovementInfoList.Binding=new List<MovementInfo>();
            using(var legacy=new MirLibrary(emptyFile){Loaded=true,Images=new MirImage[0]})
            using(var target=new TargetForm {ClientSize=Config.GameSize})
            using(var d3d=new Direct3D())
            {
                CEnvir.Target=target;CEnvir.LibraryList[LibraryFile.GameInter]=legacy;var parameters=new PresentParameters {Windowed=true,SwapEffect=SwapEffect.Discard,BackBufferFormat=Format.X8R8G8B8,BackBufferWidth=1024,BackBufferHeight=768,PresentFlags=PresentFlags.LockableBackBuffer};
                using(var device=new Device(d3d,0,DeviceType.Hardware,target.Handle,CreateFlags.SoftwareVertexProcessing,parameters))
                {
                    typeof(DXManager).GetProperty("Device").SetValue(null,device,null);typeof(DXManager).GetProperty("Parameters").SetValue(null,parameters,null);
                    typeof(DXManager).GetMethod("LoadTextures",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
                    try
                    {
                        using(var package=GameUiVerifiedPackage.Verify(args[0],GameUiOfficialV1Catalog.PackageManifest))
                        {
                        if(!chatOnly) using(var cpuLibrary=new MirLibrary(args[0])) ButtonHitMaskTests.RunCpu(cpuLibrary,args.Length>3?args[3]:null,args[1]);
                        using(var root=new TestScene {IsVisible=true})
                        {
                            package.UploadAllFrames();DXControl.ActiveScene=root;
                            var game=(GameScene)FormatterServices.GetUninitializedObject(typeof(GameScene));GC.SuppressFinalize(game);GameScene.Game=game;
                            var user=(UserObject)FormatterServices.GetUninitializedObject(typeof(UserObject));Set(user,"_Stats",new Stats());Set(user,"_HermitStats",new Stats());Set(game,"_User",user);
                            game.Equipment=new ClientUserItem[Globals.EquipmentSize];game.Inventory=new ClientUserItem[500];game.DataDictionary=new SortedDictionary<uint,ClientObjectData>();
                            if(!chatOnly) ButtonHitMaskTests.RunControls(root,package);
                            using(var map=new MapControl {Width=600,Height=1040}){game.MapControl=map;Set(map,"_MapInfo",new MapInfo());if(!chatOnly) Exercise(root,game,package,args[1]);ExerciseChat(root,package,args[1]);}
                        }
                        if(chatOnly) { ExerciseChatReconstruction(args[0],args[1]);ExerciseChatPersistence(args[0],args[1]); }
                        else { ExerciseScene(package,args[1]);ExerciseStartupSessions(args[0],args[1]); }
                        }
                    }
                    finally {DXManager.Unload();}
                }
            }
            if(chatOnly) { Console.WriteLine("Chat integration: "+checks+" checks passed: original channel toggles, headerless viewport, GPU artwork, reconstruction and persistence.");return 0; }
            Console.WriteLine("Window-set integration: "+checks+" checks passed with real catalog package, eight registered participants plus original chat groups/input and six resolutions, identity/chat retention, actual GameScene resize/disposal, startup scene entry/reentry, clean/dirty rollback, render-fault input containment, loopback heartbeat and teardown. GPU probes cover glyph restoration and empty/half/full orb foreground; six native wheel actions checked; no full-scene/live-game/IME acceptance.");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally{GameScene.Game=oldGame;DXControl.ActiveScene=oldActive;CEnvir.Target=null;Config.GameSize=oldSize;Globals.NPCInfoList=oldNPC;Globals.MovementInfoList=oldMovement;CEnvir.CastleInfoList=oldCastles;if(hadLibrary)CEnvir.LibraryList[LibraryFile.GameInter]=oldLibrary;else CEnvir.LibraryList.Remove(LibraryFile.GameInter);File.Delete(emptyFile);}
    }
}
