using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
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

internal static class OfficialUiCharacterReadoutTests
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static void Reject<T>(Action action, string message) where T : Exception
    { bool rejected = false; try { action(); } catch (T) { rejected = true; } Check(rejected, message); }
    private static void Field(object owner, string name, object value)
        => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
    private static int Handlers(DXControl control, string name)
    {
        var field = typeof(DXControl).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        return (field.GetValue(control) as Delegate)?.GetInvocationList().Length ?? 0;
    }
    private static DXControl Tab(CharacterDialog dialog)
        => (DXControl)typeof(CharacterDialog).GetField("CharacterTab", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialog);
    private static GameUiCharacterReadoutLayout Layout(int offset = 0)
        => new GameUiCharacterReadoutLayout(new Rectangle(2,2+offset,110,16), new Rectangle(114,2+offset,110,16),
                                           new Rectangle(2,20+offset,110,16), new Rectangle(114,20+offset,110,16));
    private static DXLabel[] Labels(GameUiCharacterReadouts binding)
        => new[] { binding.LevelLabel, binding.ExperienceLabel, binding.HealthLabel, binding.ManaLabel };
    private static DXLabel[] Sources(MainPanel panel)
        => new[] { panel.LevelLabel, panel.ExperienceLabel, panel.HealthLabel, panel.ManaLabel };
    private static DXControl[] Observed(MainPanel panel)
        => Sources(panel).Cast<DXControl>().Concat(new[] { panel.ExperienceBar }).ToArray();
    private static void Equal(GameUiCharacterReadouts binding, MainPanel panel)
    {
        var labels = Labels(binding); var sources = Sources(panel);
        for (int i = 0; i < labels.Length; i++)
        {
            Check(labels[i].Text == sources[i].Text, "HUD text mismatch " + i);
            Check(labels[i].Hint == (i == 1 ? panel.ExperienceBar.Hint : sources[i].Hint ?? sources[i].Text), "HUD tooltip mismatch " + i);
        }
    }
    private static Dictionary<Stat, DXLabel>[] Dictionaries(CharacterDialog dialog)
        => new[] { dialog.DisplayStats, dialog.AttackStats, dialog.AdvantageStats, dialog.DisadvantageStats,
                   dialog.HermitDisplayStats, dialog.HermitAttackStats };
    private sealed class ControlSnapshot
    {
        private readonly DXControl control, parent;
        private readonly Point location;
        private readonly Size size;
        private readonly int clicks;
        internal ControlSnapshot(DXControl c)
        { control = c; parent = c.Parent; location = c.Location; size = c.Size; clicks = Handlers(c,"MouseClick"); }
        internal void Verify()
        {
            Check(!control.IsDisposed && control.Parent == parent && control.Location == location && control.Size == size,
                  "Existing character control geometry/identity changed");
            Check(Handlers(control,"MouseClick") == clicks,"Existing character action handler changed");
        }
    }
    private static List<DXControl> Children(DXControl control)
        => (List<DXControl>)typeof(DXControl).GetProperty("Controls", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(control);
    private static IEnumerable<DXControl> Descendants(DXControl root)
    {
        foreach (DXControl child in Children(root))
        { yield return child; foreach (DXControl nested in Descendants(child)) yield return nested; }
    }
    private static void Geometry()
    {
        Reject<ArgumentOutOfRangeException>(() => new GameUiCharacterReadoutLayout(Rectangle.Empty, new Rectangle(0,20,1,1), new Rectangle(0,40,1,1), new Rectangle(0,60,1,1)),"Empty rectangle accepted");
        Reject<ArgumentOutOfRangeException>(() => new GameUiCharacterReadoutLayout(new Rectangle(-1,0,1,1), new Rectangle(0,20,1,1), new Rectangle(0,40,1,1), new Rectangle(0,60,1,1)),"Negative location accepted");
        Reject<ArgumentOutOfRangeException>(() => new GameUiCharacterReadoutLayout(new Rectangle(int.MaxValue,0,100,1), new Rectangle(0,20,1,1), new Rectangle(0,40,1,1), new Rectangle(0,60,1,1)),"Rectangle overflow accepted");
        Reject<ArgumentException>(() => new GameUiCharacterReadoutLayout(new Rectangle(0,0,20,20), new Rectangle(19,19,1,1), new Rectangle(0,40,1,1), new Rectangle(0,60,1,1)),"Overlap accepted");
        new GameUiCharacterReadoutLayout(new Rectangle(0,0,20,20), new Rectangle(20,0,20,20), new Rectangle(0,20,20,20), new Rectangle(20,20,20,20));
    }
    private static void Bindings(GameScene scene, UserObject user, MainPanel panel)
    {
        using (var root = new DXControl { Size = new Size(1024,768), IsVisible = true })
        using (var dialog = new CharacterDialog { Parent = root })
        {
            var tab = Tab(dialog);
            var original = Descendants(dialog).Select(c => new ControlSnapshot(c)).ToArray();
            var cells = dialog.Grid.ToArray(); var dicts = Dictionaries(dialog); var stats = dicts.SelectMany(d => d.Values).ToArray();
            Check(cells.Length == 16 && stats.Length == 57, "Character baseline count changed");
            Check(dialog.GameUiReadouts == null, "Legacy character allocated readouts");
            int childCount = Children(tab).Count;
            var observed = Observed(panel);
            int[] textCounts = observed.Select(c => Handlers(c,"TextChanged")).ToArray();
            int[] hintCounts = observed.Select(c => Handlers(c,"HintChanged")).ToArray();
            int[] disposeCounts = observed.Select(c => Handlers(c,"Disposing")).ToArray();
            int parentDisposals = Handlers(tab,"Disposing"), panelDisposals = Handlers(panel,"Disposing");
            panel.LevelLabel.Text = "15"; user.MaxExperience = 1000; user.Experience = 125; user.CurrentHP = 111; user.CurrentMP = 55;
            dialog.UseGameUiReadouts(panel,Layout()); var first = dialog.GameUiReadouts;
            Equal(first,panel);
            Check(Children(tab).Count == childCount + 4, "Readouts not attached to original tab");
            foreach (DXLabel label in Labels(first))
                Check(label.Parent == tab && !label.AutoSize && label.DrawFormat.HasFlag(TextFormatFlags.SingleLine) &&
                      label.DrawFormat.HasFlag(TextFormatFlags.EndEllipsis),"Readout ownership/overflow contract");
            Reject<ArgumentNullException>(() => dialog.UseGameUiReadouts(null,Layout()),"Missing HUD accepted");
            Reject<ArgumentException>(() => dialog.UseGameUiReadouts(panel,Layout(16000)),"Outside tab accepted");
            Check(ReferenceEquals(dialog.GameUiReadouts,first) && !first.IsDisposed && Children(tab).Count == childCount + 4,"Rejected replacement changed active binding");
            var culture = CultureInfo.CurrentCulture;
            try
            {
                foreach (string name in new[] { "zh-CN", "en-US", "de-DE" })
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                    foreach (decimal max in new[] { 0m, 1m, 1000m, 999999999999999999999999m })
                    foreach (decimal xp in new[] { 0m, 125.25m, 999999999999999m })
                    {
                        user.MaxExperience = max; user.Experience = xp;
                        // These setters execute the real GameScene formatter (both max=0 and percentage branches).
                        Equal(first,panel);
                        Check(first.ExperienceLabel.Text == (max > 0 ? $"{xp / max: #,##0.00%}" : $"{xp: #,##0#}"), "Experience formatter branch changed");
                        Check(first.ExperienceLabel.Hint == $"{xp:#,##0.#}/{max: #,##0.#}", "Raw experience tooltip changed");
                    }
                }
            }
            finally { CultureInfo.CurrentCulture = culture; }
            dialog.Visible = false; tab.Visible = false;
            foreach (int value in new[] { 0, 1, 999, int.MaxValue })
            {
                user.CurrentHP = value; user.CurrentMP = value; panel.LevelLabel.Text = value.ToString();
                Equal(first,panel);
                user.Stats[Stat.Health] = value; user.Stats[Stat.Mana] = value;
                scene.HealthChanged(); scene.ManaChanged(); Equal(first,panel);
            }
            panel.HealthLabel.Hint = "生命 tooltip"; panel.ManaLabel.Hint = "魔法 tooltip";
            panel.ExperienceBar.Hint = "原始经验提示"; Equal(first,panel);
            var targets = Labels(first); var fonts = targets.Select(l => l.Font).ToArray(); var colours = targets.Select(l => l.ForeColour).ToArray();
            foreach (Size size in new[] { new Size(1024,768),new Size(1366,768),new Size(1280,800),new Size(1440,900),new Size(1600,900),new Size(1920,1080) })
            {
                root.Size = size; dialog.Location = new Point(size.Width - 300, size.Height - 480);
                for (int i = 0; i < targets.Length; i++)
                {
                    Check(ReferenceEquals(targets[i],Labels(first)[i]) && ReferenceEquals(targets[i].Font,fonts[i]) && targets[i].ForeColour == colours[i],"Move replaced readout/style");
                    Check(targets[i].DisplayArea.Location == new Point(tab.DisplayArea.X + targets[i].Location.X, tab.DisplayArea.Y + targets[i].Location.Y),"Display rectangle stale");
                }
                Equal(first,panel);
            }
            for (int cycle = 0; cycle < 100; cycle++)
            {
                var previous = dialog.GameUiReadouts;
                dialog.UseGameUiReadouts(panel,Layout(cycle % 2));
                Check(previous.IsDisposed && Labels(previous).All(l => l.IsDisposed),"Replacement leaked old labels");
                Check(Children(tab).Count == childCount + 4,"Replacement leaked tab children");
                for (int i = 0; i < observed.Length; i++)
                    Check(Handlers(observed[i],"TextChanged") == textCounts[i] + 1 && Handlers(observed[i],"HintChanged") == hintCounts[i] + 1 &&
                          Handlers(observed[i],"Disposing") == disposeCounts[i] + 1,"Duplicate subscriptions after rebind");
                Equal(dialog.GameUiReadouts,panel);
            }
            foreach (ControlSnapshot snapshot in original) snapshot.Verify();
            Check(cells.SequenceEqual(dialog.Grid) && dicts.SequenceEqual(Dictionaries(dialog)) && stats.SequenceEqual(dicts.SelectMany(d => d.Values)),"Equipment/stat identity replaced");
            var last = dialog.GameUiReadouts; dialog.UseGameUiReadouts(null,null);
            Check(last.IsDisposed && dialog.GameUiReadouts == null && Children(tab).Count == childCount,"Disable failed");
            for (int i = 0; i < observed.Length; i++)
                Check(Handlers(observed[i],"TextChanged") == textCounts[i] && Handlers(observed[i],"HintChanged") == hintCounts[i] &&
                      Handlers(observed[i],"Disposing") == disposeCounts[i],"Disable leaked source subscription");
            Check(Handlers(tab,"Disposing") == parentDisposals && Handlers(panel,"Disposing") == panelDisposals,"Disable leaked owner subscription");
            dialog.UseGameUiReadouts(panel,Layout()); var disposed = dialog.GameUiReadouts;
            dialog.Dispose(); disposed.Dispose();
            Check(disposed.IsDisposed && Labels(disposed).All(l => l.IsDisposed),"Dialog disposal leaked targets");
            Reject<ObjectDisposedException>(() => dialog.UseGameUiReadouts(panel,Layout()),"Disposed dialog accepted");
        }
    }
    private static void ProposedCoordinates(MainPanel panel)
    {
        using (var dialog = new CharacterDialog())
        {
            // Layout JSON uses owner coordinates; when integrated, subtract the actual tab origin.
            var tab = Tab(dialog); tab.Parent.Size = new Size(412,562); tab.Location = new Point(0,30); tab.Size = new Size(412,532);
            var layout = new GameUiCharacterReadoutLayout(new Rectangle(310,68,86,20), new Rectangle(310,92,86,20),
                                                        new Rectangle(310,116,86,20), new Rectangle(310,140,86,20));
            dialog.UseGameUiReadouts(panel,layout); var binding = dialog.GameUiReadouts;
            var labels = Labels(binding);
            for (int i = 0; i < labels.Length; i++)
            {
                Check(labels[i].Location == new Point(310,68+24*i) && labels[i].Size == new Size(86,20),"Proposed readout bounds differ");
                Check(labels[i].DisplayArea.Y - tab.Parent.DisplayArea.Y == 98+24*i,"Owner-to-tab offset applied incorrectly");
            }
            SetLongText(panel); Equal(binding,panel);
        }
    }
    private static void SetLongText(MainPanel panel)
    {
        panel.HealthLabel.Text = "2147483647/2147483647";
        panel.ManaLabel.Text = "2147483647/2147483647";
        panel.LevelLabel.Text = "999999";
        panel.ExperienceLabel.Text = "1234567890000000000000.00%";
        panel.ExperienceBar.Hint = "1234567890000000000000/12345678900000000000000";
    }
    private static void Disposal(MainPanel panel)
    {
        using (var dialog = new CharacterDialog())
        {
            dialog.UseGameUiReadouts(panel,Layout()); var binding = dialog.GameUiReadouts;
            panel.HealthLabel.Dispose();
            Check(binding.IsDisposed && Labels(binding).All(l => l.IsDisposed),"Disposed source label left a live binding");
            Reject<ArgumentException>(() => dialog.UseGameUiReadouts(panel,Layout()),"Disposed HUD child accepted");
            dialog.UseGameUiReadouts(null,null);
        }
        using (var secondPanel = new MainPanel())
        using (var dialog = new CharacterDialog())
        {
            dialog.UseGameUiReadouts(secondPanel,Layout()); var binding = dialog.GameUiReadouts;
            secondPanel.Dispose();
            Check(binding.IsDisposed,"HUD disposal left binding active");
            Reject<ObjectDisposedException>(() => dialog.UseGameUiReadouts(secondPanel,Layout()),"Disposed HUD accepted");
            dialog.UseGameUiReadouts(null,null);
        }
        using (var thirdPanel = new MainPanel())
        using (var dialog = new CharacterDialog())
        {
            dialog.UseGameUiReadouts(thirdPanel,Layout()); var binding = dialog.GameUiReadouts;
            Tab(dialog).Dispose();
            Check(binding.IsDisposed,"Tab disposal left binding active");
            Reject<ObjectDisposedException>(() => dialog.UseGameUiReadouts(thirdPanel,Layout()),"Disposed tab accepted");
        }
    }
    [STAThread]
    private static int Main()
    {
        GameScene old = GameScene.Game; string libraryPath = Path.GetTempFileName();
        MirLibrary oldLibrary; bool hadLibrary = CEnvir.LibraryList.TryGetValue(LibraryFile.GameInter,out oldLibrary);
        try
        {
            using (var emptyLibrary = new MirLibrary(libraryPath) { Loaded = true, Images = new MirImage[0] })
            using (var target = new TargetForm())
            {
                CEnvir.LibraryList[LibraryFile.GameInter] = emptyLibrary; CEnvir.Target = target;
                // A shell scene/user permits actual HP/MP/XP setter->GameScene paths without network/login or draw.
                var scene = (GameScene)FormatterServices.GetUninitializedObject(typeof(GameScene)); GC.SuppressFinalize(scene);
                var user = (UserObject)FormatterServices.GetUninitializedObject(typeof(UserObject));
                Field(user,"_Stats",new Stats()); Field(scene,"_User",user);
                scene.Equipment = new ClientUserItem[Globals.EquipmentSize]; GameScene.Game = scene;
                using (var panel = new MainPanel()) { scene.MainPanel = panel; Geometry(); Bindings(scene,user,panel); ProposedCoordinates(panel); Disposal(panel); }
            }
            Console.WriteLine("Character readouts: " + checks + " checks passed; real character/HUD controls; real HP/MP/XP setters, three cultures, six placements, 100 rebindings and disposal. No game/login/render/packets or full LevelChanged/StatsChanged acceptance.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            GameScene.Game = old; CEnvir.Target = null;
            if (hadLibrary) CEnvir.LibraryList[LibraryFile.GameInter] = oldLibrary; else CEnvir.LibraryList.Remove(LibraryFile.GameInter);
            File.Delete(libraryPath);
        }
    }
}
