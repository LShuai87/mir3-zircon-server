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

internal static class OfficialUiCharacterAttributeTests
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static void Reject<T>(Action action, string message) where T : Exception
    { bool rejected = false; try { action(); } catch (T) { rejected = true; } Check(rejected, message); }
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
    private static void SetField(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner,value);
    private static List<DXControl> Children(DXControl c) => (List<DXControl>)typeof(DXControl).GetProperty("Controls",BindingFlags.Instance | BindingFlags.NonPublic).GetValue(c);
    private static int Handlers(DXControl c, string name) => ((Delegate)typeof(DXControl).GetField(name,BindingFlags.Instance | BindingFlags.NonPublic).GetValue(c))?.GetInvocationList().Length ?? 0;
    private static string Rect(Rectangle r) => string.Join(",", new[] { r.X,r.Y,r.Width,r.Height });
    private static string Rect(Rectangle? r) => r.HasValue ? Rect(r.Value) : "-";
    private static DXTab Page(CharacterDialog d, GameUiCharacterPage p) => Field<DXTab>(d,p == GameUiCharacterPage.Character ? "CharacterTab" : p == GameUiCharacterPage.Status ? "StatsTab" : "HermitTab");
    private static Dictionary<string,DXLabel> Values(CharacterDialog d)
    {
        var result = new Dictionary<string,DXLabel>();
        foreach (string group in new[] { "DisplayStats","AttackStats","AdvantageStats","DisadvantageStats","HermitDisplayStats","HermitAttackStats" })
            foreach (var pair in (Dictionary<Stat,DXLabel>)typeof(CharacterDialog).GetField(group).GetValue(d)) result.Add(group+"."+pair.Key,pair.Value);
        return result;
    }
    private static IEnumerable<DXControl> Descendants(DXControl c)
    { foreach (var child in Children(c)) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    private static Rectangle WindowBounds(CharacterDialog d, DXControl c) => new Rectangle(c.DisplayArea.X-d.DisplayArea.X,c.DisplayArea.Y-d.DisplayArea.Y,c.Size.Width,c.Size.Height);
    private static void Catalog(string expectedPath)
    {
        var layout = GameUiCharacterAttributeLayout.CreateOfficialCandidate();
        var expected = File.ReadAllLines(expectedPath).ToDictionary(line=>line.Split('=')[0],line=>line.Split('=')[1]);
        Check(expected.Count == 60,"Incomplete attribute catalog fixture");
        foreach (var row in layout.Rows)
        {
            string actual = row.Page+"|"+Rect(row.Value)+"|"+Rect(row.Caption)+"|"+Rect(row.Icon)+"|"+(row.CaptionText ?? "-");
            Check(expected[row.Id] == actual,"Compiled attribute drifted: "+row.Id);
        }
        foreach (var group in layout.GroupCaptions) Check(expected["group."+group.Key] == Rect(group.Value),"Compiled group caption drifted");
        // Reference UI[2765]: full element backing tiles, native row pitch and home-page attack speed.
        foreach (var group in new[] { "AttackStats", "AdvantageStats", "DisadvantageStats" })
        {
            int rowY = group == "AttackStats" ? 450 : group == "AdvantageStats" ? 473 : 496;
            var elements = layout.Rows.Where(r => r.Page == GameUiCharacterPage.Character && r.Id.StartsWith(group + ".", StringComparison.Ordinal)).ToArray();
            Check(elements.Length == 7, "Missing main-page elements");
            for (int i = 0; i < elements.Length; i++)
                Check(elements[i].Icon == new Rectangle(76 + i * 46, rowY, 20, 20), "Element tile differs from native backing");
        }
        var speed = layout.Rows.Single(r => r.Id == "DisplayStats.AttackSpeed");
        Check(speed.Page == GameUiCharacterPage.Character && speed.Value == new Rectangle(306,426,70,20), "Attack speed lost its native home-page frame");
        var rows = layout.Rows.ToArray(); var groups = layout.GroupCaptions.ToDictionary(p=>p.Key,p=>p.Value);
        var copy = new GameUiCharacterAttributeLayout(rows,groups); rows[0]=null; groups["AttackStats"]=Rectangle.Empty;
        Check(copy.Rows[0]!=null && copy.GroupCaptions["AttackStats"]!=Rectangle.Empty,"Mutable input retained");
        Reject<ArgumentException>(()=>new GameUiCharacterAttributeLayout(new GameUiCharacterAttributeRow[56],layout.GroupCaptions),"Missing rows accepted");
        rows=layout.Rows.ToArray(); rows[1]=rows[0];
        Reject<ArgumentException>(()=>new GameUiCharacterAttributeLayout(rows,layout.GroupCaptions),"Duplicate ID accepted");
        Reject<ArgumentException>(()=>new GameUiCharacterAttributeLayout(layout.Rows.ToArray(),new Dictionary<string,Rectangle>()),"Missing group accepted");
        Reject<ArgumentOutOfRangeException>(()=>new GameUiCharacterAttributeRow("bad",(GameUiCharacterPage)99,new Rectangle(1,1,2,2),null,null),"Invalid page accepted");
        Reject<ArgumentOutOfRangeException>(()=>new GameUiCharacterAttributeRow("bad",GameUiCharacterPage.Character,new Rectangle(int.MaxValue,1,2,2),null,null),"Overflow accepted");
    }
    private sealed class Saved
    {
        internal readonly DXControl Control, Parent;
        private readonly Point location;
        private readonly Size size;
        private readonly object tag;
        private readonly int clicks, texts;
        private readonly bool visible, autoSize, fixedSize, useOffset;
        private readonly TextFormatFlags format;
        private readonly string text, hint;
        internal Saved(DXControl c)
        {
            Control=c; Parent=c.Parent; location=c.Location; size=c.Size; tag=c.Tag; visible=c.Visible; text=c.Text; hint=c.Hint;
            clicks=Handlers(c,"MouseClick"); texts=Handlers(c,"TextChanged");
            var label=c as DXLabel; if(label!=null) {autoSize=label.AutoSize; format=label.DrawFormat;}
            var icon=c as DXImageControl; if(icon!=null) {fixedSize=icon.FixedSize; useOffset=icon.UseOffSet;}
        }
        internal void Verify()
        {
            Check(!Control.IsDisposed && Control.Parent==Parent && Control.Location==location && Control.Size==size,"Legacy geometry not restored: "+text);
            Check(ReferenceEquals(Control.Tag,tag) && Control.Visible==visible,"Tag/visibility changed");
            Check(Handlers(Control,"MouseClick")==clicks && Handlers(Control,"TextChanged")==texts,"Action or subscription leaked");
            var label=Control as DXLabel;
            if(label!=null) Check(label.AutoSize==autoSize && label.DrawFormat==format && label.Text==text && label.Hint==hint,"Legacy label style/content not restored");
            var icon=Control as DXImageControl;
            if(icon!=null) Check(icon.FixedSize==fixedSize && icon.UseOffSet==useOffset,"Legacy icon behavior not restored");
        }
    }
    private static GameUiCharacterAttributeLayout Mutated(GameUiCharacterAttributeLayout original, Func<GameUiCharacterAttributeRow,GameUiCharacterAttributeRow> mutate)
    { var rows=original.Rows.ToArray(); rows[0]=mutate(rows[0]); return new GameUiCharacterAttributeLayout(rows,original.GroupCaptions); }
    private static void Controls(UserObject user)
    {
        using (var root=new DXControl {Size=new Size(1024,768),IsVisible=true})
        using (var d=new CharacterDialog {Parent=root,Visible=true})
        {
            var layout=GameUiCharacterAttributeLayout.CreateOfficialCandidate(); var pages=GameUiCharacterEquipmentLayout.CreateOfficialCandidate();
            var values=Values(d); var captions=Field<Dictionary<string,DXLabel>>(d,"gameUiStatCaptions");
            var groups=Field<Dictionary<string,DXLabel>>(d,"gameUiGroupCaptions");
            d.UpdateStats();
            Check(values.Count==57 && captions.Count==27 && groups.Count==4,"Original control counts changed");
            var saved=Descendants(d).Select(c=>new Saved(c)).ToArray();
            var originalTabs=new[] {Page(d,GameUiCharacterPage.Character),Page(d,GameUiCharacterPage.Status),Page(d,GameUiCharacterPage.Hermit)};
            var originalChildren=originalTabs.Select(t=>Children(t).ToArray()).ToArray();
            Reject<InvalidOperationException>(()=>d.UseGameUiAttributes(layout),"Attribute layout accepted without prepared pages");
            foreach(var c in saved) c.Verify();
            d.Size=new Size(412,562);
            var baselineTexts=values.ToDictionary(p=>p.Key,p=>Handlers(p.Value,"TextChanged"));
            var tags=values.ToDictionary(p=>p.Key,p=>p.Value.Tag);
            for(int cycle=0;cycle<12;cycle++)
            {
                d.UseGameUiEquipmentPages(pages); d.UseGameUiAttributes(layout);
                Check(ReferenceEquals(d.GameUiAttributeLayout,layout),"Active layout missing");
                foreach(var row in layout.Rows)
                {
                    var value=values[row.Id]; var parent=Page(d,row.Page);
                    Check(ReferenceEquals(Values(d)[row.Id],value) && ReferenceEquals(value.Tag,tags[row.Id]),"Stat or Tag identity changed");
                    Check(value.Parent==parent && WindowBounds(d,value)==row.Value,"Value page/coordinate mismatch "+row.Id);
                    Check(value.DrawFormat.HasFlag(TextFormatFlags.HorizontalCenter) == (row.Page == GameUiCharacterPage.Character), "Numeric alignment differs from page policy");
                    Check(!value.AutoSize && (value.DrawFormat&TextFormatFlags.EndEllipsis)!=0,"Value lacks fixed overflow bounds");
                    Check(Handlers(value,"TextChanged")==baselineTexts[row.Id]+1 && value.Hint==value.Text,"Duplicate subscription or missing tooltip");
                    if(row.Caption.HasValue)
                    {
                        var caption=captions[row.Id];
                        Check(caption.Parent==parent && WindowBounds(d,caption)==row.Caption.Value,"Original caption route mismatch");
                        if(row.CaptionText!=null) Check(caption.Text==row.CaptionText,"Candidate caption text missing");
                    }
                    else
                    {
                        var icon=(DXImageControl)value.Tag;
                        Check(icon.Parent==parent && WindowBounds(d,icon)==row.Icon.Value && icon.FixedSize && !icon.UseOffSet,"Original icon route mismatch");
                    }
                }
                Check(groups["AttackStats"].Text == "攻击元素", "Attack element caption missing");
                Check(!groups["HermitAttackStats"].Visible,"Obsolete Hermit group heading remains visible");
                foreach(var group in layout.GroupCaptions) Check(WindowBounds(d,groups[group.Key])==group.Value && groups[group.Key].Parent==originalTabs[0],"Element heading route mismatch");
                foreach(var selected in new[] {GameUiCharacterPage.Character,GameUiCharacterPage.Status,GameUiCharacterPage.Hermit})
                {
                    Page(d,selected).TabButton.InvokeMouseClick();
                    foreach(var row in layout.Rows) Check(values[row.Id].IsVisible==(row.Page==selected),"Hidden-page attribute still visible");
                }
                foreach(var size in new[] {new Size(1024,768),new Size(1366,768),new Size(1280,800),new Size(1440,900),new Size(1600,900),new Size(1920,1080)})
                {
                    root.Size=size; d.Location=new Point(size.Width-420,size.Height-570);
                    foreach(var row in layout.Rows) Check(WindowBounds(d,values[row.Id])==row.Value,"Window movement shifted attribute");
                }
                d.UseGameUiAttributes(layout); d.UseGameUiEquipmentPages(pages);
                foreach(var pair in values) Check(Handlers(pair.Value,"TextChanged")==baselineTexts[pair.Key]+1,"Replacement leaked subscriptions");
                var retained=d.GameUiAttributeLayout;
                Reject<ArgumentException>(()=>d.UseGameUiAttributes(Mutated(layout,r=>new GameUiCharacterAttributeRow("unknown.id",r.Page,r.Value,r.Caption,r.Icon))),"Unknown ID accepted");
                Reject<ArgumentException>(()=>d.UseGameUiAttributes(Mutated(layout,r=>new GameUiCharacterAttributeRow(r.Id,r.Page,new Rectangle(1,1,5,5),r.Caption,r.Icon))),"Out-of-page value accepted");
                Reject<ArgumentException>(()=>d.UseGameUiAttributes(Mutated(layout,r=>new GameUiCharacterAttributeRow(r.Id,r.Page,r.Caption.Value,r.Caption,r.Icon))),"Overlapping caption/value accepted");
                Check(d.GameUiAttributeLayout==retained && WindowBounds(d,values[layout.Rows[0].Id])==layout.Rows[0].Value,"Rejected replacement mutated active layout");
                var longValue=values["DisplayStats.MaxDC"]; var longRect=WindowBounds(d,longValue);
                longValue.Text="1000000000-2000000000 & 完整数值";
                Check(longValue.Hint==longValue.Text && WindowBounds(d,longValue)==longRect,"Overflow resized control or lost full text");
                foreach(int sign in new[] {1,-1,0})
                {
                    user.Stats[Stat.FireAttack]=sign*7; user.Stats[Stat.FireResistance]=sign*3;
                    user.HermitStats[Stat.FireAttack]=sign*5; user.HermitPoints=cycle; d.UpdateStats();
                    Check(d.AttackStats[Stat.FireAttack].Text==(sign>0?"+7":"0"),"Original attack formatting changed");
                    Check(d.AdvantageStats[Stat.FireResistance].Text==(sign>0?"x3":"0"),"Original advantage formatting changed");
                    Check(d.DisadvantageStats[Stat.FireResistance].Text==(sign<0?"x3":"0"),"Original disadvantage formatting changed");
                    Check(d.HermitAttackStats[Stat.FireAttack].Text==(sign>0?"+5":"0"),"Original Hermit formatting changed");
                    Check(d.AttackStats[Stat.FireAttack].ForeColour==(sign>0?Color.DeepSkyBlue:Color.FromArgb(60,60,60)),"Original attack color changed");
                    Check(((DXImageControl)d.DisadvantageStats[Stat.FireResistance].Tag).ForeColour==(sign<0?Color.White:Color.FromArgb(60,60,60)),"Original icon color changed");
                    foreach(var row in layout.Rows) Check(values[row.Id].Hint==values[row.Id].Text && WindowBounds(d,values[row.Id])==row.Value,"Refresh lost tooltip/bounds");
                }
                user.HermitPoints=0; d.UpdateStats();
                if(cycle%2==0) d.UseGameUiAttributes(null); else d.UseGameUiEquipmentPages(null);
                Check(d.GameUiAttributeLayout==null,"Attribute binding not released");
                if(d.GameUiEquipmentLayout!=null) d.UseGameUiEquipmentPages(null);
                // Restore the original window size before comparing legacy tab auto-sizing.
                d.Size=new Size(282,460);
                foreach(var c in saved.Where(c=>values.Values.Contains(c.Control as DXLabel) || captions.Values.Contains(c.Control as DXLabel) || groups.Values.Contains(c.Control as DXLabel) || tags.Values.Contains(c.Control))) c.Verify();
                for(int p=0;p<3;p++) Check(Children(originalTabs[p]).SequenceEqual(originalChildren[p]),"Original page z-order not restored");
                d.Size=new Size(412,562);
            }
            d.UseGameUiEquipmentPages(pages); d.UseGameUiAttributes(layout);
            var explicitHint=values["DisplayStats.MaxAC"]; explicitHint.Hint="external tooltip"; explicitHint.Text="updated";
            Check(explicitHint.Hint=="external tooltip","External tooltip was overwritten");
            d.UseGameUiAttributes(null); Check(explicitHint.Hint=="external tooltip","External tooltip was lost on disable");
            d.UseGameUiAttributes(layout); var references=values.Values.ToArray(); d.Dispose();
            Check(references.All(v=>v.IsDisposed) && d.GameUiAttributeLayout==null,"Owner disposal retained attribute binding");
            Reject<ObjectDisposedException>(()=>d.UseGameUiAttributes(layout),"Disposed owner accepted a binding");
        }
    }
    [STAThread]
    private static int Main(string[] args)
    {
        GameScene old=GameScene.Game; string file=Path.GetTempFileName(); MirLibrary oldLibrary;
        bool hadLibrary=CEnvir.LibraryList.TryGetValue(LibraryFile.GameInter,out oldLibrary);
        try
        {
            using(var library=new MirLibrary(file) {Loaded=true,Images=new MirImage[0]})
            using(var target=new TargetForm())
            {
                CEnvir.Target=target; CEnvir.LibraryList[LibraryFile.GameInter]=library;
                var scene=(GameScene)FormatterServices.GetUninitializedObject(typeof(GameScene)); GC.SuppressFinalize(scene);
                var user=(UserObject)FormatterServices.GetUninitializedObject(typeof(UserObject));
                SetField(user,"_Stats",new Stats()); SetField(user,"_HermitStats",new Stats()); SetField(scene,"_User",user);
                scene.Equipment=new ClientUserItem[Globals.EquipmentSize]; GameScene.Game=scene;
                Catalog(args[0]); Controls(user);
            }
            Console.WriteLine("Character attributes: "+checks+" checks passed; 57 original values, 27 captions, 30 Tag icons; compiled catalog, three pages, six placements, 12 cycles, real UpdateStats, rollback and disposal. No render/packets/game acceptance."); return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
        finally {GameScene.Game=old; CEnvir.Target=null; if(hadLibrary) CEnvir.LibraryList[LibraryFile.GameInter]=oldLibrary; else CEnvir.LibraryList.Remove(LibraryFile.GameInter); File.Delete(file);}
    }
}
