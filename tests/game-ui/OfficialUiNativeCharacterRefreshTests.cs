using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Client.Controls;
using Client.Envir;
using Client.Models;
using Client.Scenes;
using Client.Scenes.Views;
using Library;
using Library.SystemModels;
using S = Library.Network.ServerPackets;

// Real deserialization/dispatch, original controls and synthetic server state on loopback only.
internal static partial class OfficialUiFullSceneTests
{
    private static readonly Dictionary<string, string> refreshLegacy = new Dictionary<string, string>();
    private static readonly List<string> refreshRows = new List<string> { "style|checkpoint|field|text|foregroundArgb|iconArgb" };
    private static int refreshCheckpoints, refreshPackets, refreshParityFields;

    private static Dictionary<string, DXLabel> RefreshFields(GameScene scene)
    {
        var fields = (Dictionary<string, DXLabel>)Call(typeof(CharacterDialog), scene.CharacterBox, "GetGameUiStatControls");
        Check(fields.Count == 57, "Native character attribute contract no longer has 57 original labels");
        fields.Add("CharacterNameLabel", scene.CharacterBox.CharacterNameLabel);
        fields.Add("GuildNameLabel", scene.CharacterBox.GuildNameLabel);
        fields.Add("GuildRankLabel", scene.CharacterBox.GuildRankLabel);
        fields.Add("WearWeightLabel", scene.CharacterBox.WearWeightLabel);
        fields.Add("HandWeightLabel", scene.CharacterBox.HandWeightLabel);
        fields.Add("RemainingLabel", scene.CharacterBox.RemainingLabel);
        fields.Add("InventoryWeightLabel", scene.InventoryBox.WeightLabel);
        fields.Add("HUD.Level", scene.MainPanel.LevelLabel);
        fields.Add("HUD.Experience", scene.MainPanel.ExperienceLabel);
        fields.Add("HUD.Health", scene.MainPanel.HealthLabel);
        fields.Add("HUD.Mana", scene.MainPanel.ManaLabel);
        return fields;
    }
    private static void CheckRefreshMirror(GameScene scene, bool official)
    {
        var mirror = scene.CharacterBox.GameUiReadouts;
        Check((mirror != null) == official, "Readout adapter presence does not match selected style");
        if (!official) return;
        var source = new[] { scene.MainPanel.LevelLabel, scene.MainPanel.ExperienceLabel, scene.MainPanel.HealthLabel, scene.MainPanel.ManaLabel };
        var target = new[] { mirror.LevelLabel, mirror.ExperienceLabel, mirror.HealthLabel, mirror.ManaLabel };
        for (int i = 0; i < source.Length; i++)
        {
            Check(target[i].Text == source[i].Text, "Native packet left stale character readout " + i);
            string hint = i == 1 ? scene.MainPanel.ExperienceBar.Hint : source[i].Hint ?? source[i].Text;
            Check(target[i].Hint == hint, "Native packet left stale character readout hint " + i);
        }
    }
    private static void RefreshCheckpoint(GameScene scene, string checkpoint, bool official, Dictionary<string, DXLabel> identities,
        DXItemCell[] equipment, DXItemCell[] inventory, IntPtr hwnd, string draft, int selectionStart, int selectionLength)
    {
        var current = RefreshFields(scene);
        Check(current.Count == identities.Count && current.All(p => ReferenceEquals(p.Value, identities[p.Key])), "Attribute packets replaced original controls");
        Check(scene.CharacterBox.Grid.SequenceEqual(equipment) && scene.InventoryBox.Grid.Grid.SequenceEqual(inventory), "Attribute packets replaced original item cells");
        var editor = scene.ChatTextBox.TextBox.TextBox;
        Check(editor.Handle == hwnd && editor.Text == draft && editor.SelectionStart == selectionStart && editor.SelectionLength == selectionLength,
            "Attribute packets changed native editor HWND/draft/selection");
        Check(scene.NPCBox.WindowSkin == null && scene.ConfigBox.WindowSkin == null, "Excluded native windows acquired a skin");
        foreach (var pair in current.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var label = pair.Value;
            var icon = label.Tag as DXImageControl;
            string value = label.Text + "|" + label.ForeColour.ToArgb() + "|" + (icon == null ? "" : icon.ForeColour.ToArgb().ToString());
            string key = checkpoint + "|" + pair.Key;
            if (official)
            {
                Check(refreshLegacy.ContainsKey(key) && refreshLegacy[key] == value, "Official/Legacy native field mismatch: " + key + " actual " + value);
                refreshParityFields++;
                if (pair.Key.Contains("Stats.")) Check(label.Hint == label.Text, "Attribute tooltip retained previous value: " + key);
            }
            else refreshLegacy.Add(key, value);
            refreshRows.Add((official ? "official" : "legacy") + "|" + key + "|" + value);
        }
        CheckRefreshMirror(scene, official);
        refreshCheckpoints++;
    }
    private static Stats RefreshStats(CharacterDialog box, int sign, int seed, int hp, int mp, int bag, int wear, int hand)
    {
        var stats = new Stats();
        foreach (var key in box.DisplayStats.Keys) stats[key] = seed + (int)key % 9;
        foreach (var key in box.AttackStats.Keys) stats[key] = sign > 0 ? seed + 2 : 0;
        foreach (var key in box.AdvantageStats.Keys) stats[key] = sign * 3;
        stats[Stat.MinAC] = 1; stats[Stat.MaxAC] = 8;
        stats[Stat.MinMR] = 2; stats[Stat.MaxMR] = 9;
        stats[Stat.MinDC] = 7; stats[Stat.MaxDC] = 19;
        stats[Stat.Health] = hp; stats[Stat.Mana] = mp;
        stats[Stat.BagWeight] = bag; stats[Stat.WearWeight] = wear; stats[Stat.HandWeight] = hand;
        return stats;
    }
    private static Stats RefreshHermit(CharacterDialog box, int seed)
    {
        var stats = new Stats();
        foreach (var key in box.HermitDisplayStats.Keys.Concat(box.HermitAttackStats.Keys)) stats[key] = seed;
        return stats;
    }
    private static void AssertElements(CharacterDialog box, int sign, int attack, int hermitAttack)
    {
        Color zero = Color.FromArgb(60,60,60);
        foreach (var pair in box.AttackStats)
        {
            Check(pair.Value.Text == (attack > 0 ? "+" + attack : "0") && pair.Value.ForeColour == (attack > 0 ? Color.DeepSkyBlue : zero), "Native elemental attack text/color stale: " + pair.Key);
            Check(((DXImageControl)pair.Value.Tag).ForeColour == (attack > 0 ? Color.White : zero), "Native elemental attack icon stale: " + pair.Key);
        }
        foreach (var pair in box.AdvantageStats)
        {
            Check(pair.Value.Text == (sign > 0 ? "x3" : "0") && pair.Value.ForeColour == (sign > 0 ? Color.Lime : zero), "Native resistance advantage stale: " + pair.Key);
            Check(((DXImageControl)pair.Value.Tag).ForeColour == (sign > 0 ? Color.White : zero), "Native resistance advantage icon stale: " + pair.Key);
        }
        foreach (var pair in box.DisadvantageStats)
        {
            Check(pair.Value.Text == (sign < 0 ? "x3" : "0") && pair.Value.ForeColour == (sign < 0 ? Color.IndianRed : zero), "Native resistance disadvantage stale: " + pair.Key);
            Check(((DXImageControl)pair.Value.Tag).ForeColour == (sign < 0 ? Color.White : zero), "Native resistance disadvantage icon stale: " + pair.Key);
        }
        foreach (var pair in box.HermitAttackStats)
        {
            Check(pair.Value.Text == (hermitAttack > 0 ? "+" + hermitAttack : "0") && pair.Value.ForeColour == (hermitAttack > 0 ? Color.White : zero), "Native Hermit attack stale: " + pair.Key);
            Check(((DXImageControl)pair.Value.Tag).ForeColour == (hermitAttack > 0 ? Color.White : zero), "Native Hermit icon stale: " + pair.Key);
        }
    }
    private static void AssertWeights(GameScene scene, string bag, string wear, string hand, Color color)
    {
        Check(scene.InventoryBox.WeightLabel.Text == bag && scene.InventoryBox.WeightLabel.ForeColour == color, "Native inventory weight value/color stale");
        Check(scene.CharacterBox.WearWeightLabel.Text == wear && scene.CharacterBox.WearWeightLabel.ForeColour == color, "Native wear weight value/color stale");
        Check(scene.CharacterBox.HandWeightLabel.Text == hand && scene.CharacterBox.HandWeightLabel.ForeColour == color, "Native hand weight value/color stale");
    }
    private static void ExerciseNativeCharacterRefresh(GameUiStartupSession owner, MapInfo map, string evidence, bool official)
    {
        int listeners = TopologyListeners(), packetsBefore = socketPackets;
        DXControl.ActiveScene = new LoginScene(Config.IntroSceneSize);
        try
        {
            using (var wire = new Loopback())
            {
                LoginThroughPacket(wire, owner, listeners);
                var scene = EnterThroughPacket(wire, owner, map, listeners, official);
                var fields = RefreshFields(scene);
                var equipment = scene.CharacterBox.Grid.ToArray(); var inventory = scene.InventoryBox.Grid.Grid.ToArray();
                var editor = scene.ChatTextBox.TextBox.TextBox;
                editor.Select(2,3); var hwnd = editor.Handle; string draft = editor.Text;
                Action<string> checkpoint = name => RefreshCheckpoint(scene, name, official, fields, equipment, inventory, hwnd, draft, 2,3);
                Action<Stats,Stats,int> statsPacket = (stats,hermit,points) => {
                    var old = scene.User.Stats; var oldHermit = scene.User.HermitStats;
                    wire.Send(new S.StatsUpdate { Stats = stats, HermitStats = hermit, HermitPoints = points });
                    Check(!ReferenceEquals(scene.User.Stats,old) && !ReferenceEquals(scene.User.Stats,stats) && !ReferenceEquals(scene.User.HermitStats,oldHermit), "StatsUpdate did not replace deserialized dictionaries");
                    CheckRefreshMirror(scene,official);
                };

                statsPacket(RefreshStats(scene.CharacterBox,1,4,1000,400,100,40,20),RefreshHermit(scene.CharacterBox,2),7);
                wire.Send(new S.WeightUpdate { BagWeight=30,WearWeight=20,HandWeight=10 });
                AssertElements(scene.CharacterBox,1,6,2); AssertWeights(scene,"30 of 100","20/40","10/20",Color.White);
                Check(scene.CharacterBox.DisplayStats[Stat.MaxDC].Text == "7-19" && scene.CharacterBox.RemainingLabel.Text == "7", "Native range/Hermit remaining values incorrect");
                Check(scene.MainPanel.HealthLabel.Text == "650/1000" && scene.MainPanel.ManaLabel.Text == "240/400", "Initial native HP/MP values incorrect");
                checkpoint("positive-normal");

                wire.Send(new S.HealthChanged { ObjectID=scene.User.ObjectID,Change=-125 });
                Check(scene.User.CurrentHP == 525 && scene.MainPanel.HealthLabel.Text == "525/1000", "Health delta did not use native object dispatcher");
                CheckRefreshMirror(scene,official);
                wire.Send(new S.ManaChanged { ObjectID=scene.User.ObjectID,Change=-40 });
                Check(scene.User.CurrentMP == 200 && scene.MainPanel.ManaLabel.Text == "200/400", "Mana delta did not use native object dispatcher");
                checkpoint("health-mana-loss");

                // Server RefreshStats emits StatsUpdate -> WeightUpdate -> HP/MP clamp deltas.
                statsPacket(RefreshStats(scene.CharacterBox,-1,9,400,100,20,10,5),RefreshHermit(scene.CharacterBox,4),3);
                wire.Send(new S.WeightUpdate { BagWeight=30,WearWeight=20,HandWeight=10 });
                AssertElements(scene.CharacterBox,-1,0,4); AssertWeights(scene,"30 of 20","20/10","10/5",Color.Red);
                Check(scene.MainPanel.HealthLabel.Text == "525/400" && scene.MainPanel.ManaLabel.Text == "200/100", "Maxima were not refreshed before native clamp packets");
                checkpoint("negative-overweight-before-clamp");
                wire.Send(new S.HealthChanged { ObjectID=scene.User.ObjectID,Change=-125 });
                wire.Send(new S.ManaChanged { ObjectID=scene.User.ObjectID,Change=-100 });
                Check(scene.MainPanel.HealthLabel.Text == "400/400" && scene.MainPanel.ManaLabel.Text == "100/100", "Native max-stat clamp delta values incorrect");
                checkpoint("negative-clamped");

                statsPacket(RefreshStats(scene.CharacterBox,0,0,1200,600,30,20,10),RefreshHermit(scene.CharacterBox,0),0);
                wire.Send(new S.WeightUpdate { BagWeight=30,WearWeight=20,HandWeight=10 });
                AssertElements(scene.CharacterBox,0,0,0); AssertWeights(scene,"30 of 30","20/20","10/10",Color.White);
                Check(scene.CharacterBox.RemainingLabel.Text == "0", "Zero Hermit points retained old value");
                checkpoint("zero-normal-at-capacity");

                wire.Send(new S.HealthChanged { ObjectID=scene.User.ObjectID,Change=350 });
                wire.Send(new S.ManaChanged { ObjectID=scene.User.ObjectID,Change=200 });
                Check(scene.MainPanel.HealthLabel.Text == "750/1200" && scene.MainPanel.ManaLabel.Text == "300/600", "Native healing/mana recovery incorrect");
                checkpoint("health-mana-recovery");

                // A sparse payload retains valid HP/MP/capacities while clearing omitted base/Hermit fields.
                var sparse = new Stats();
                sparse[Stat.Health]=1200; sparse[Stat.Mana]=600;
                sparse[Stat.BagWeight]=30; sparse[Stat.WearWeight]=20; sparse[Stat.HandWeight]=10;
                statsPacket(sparse,new Stats(),0);
                wire.Send(new S.WeightUpdate { BagWeight=0,WearWeight=0,HandWeight=0 });
                AssertElements(scene.CharacterBox,0,0,0); AssertWeights(scene,"0 of 30","0/20","0/10",Color.White);
                Check(scene.CharacterBox.DisplayStats[Stat.MaxDC].Text == "0-0", "Replacement stats retained old range");
                checkpoint("sparse-stats-replacement");
                statsPacket(RefreshStats(scene.CharacterBox,1,6,1500,700,100,40,20),RefreshHermit(scene.CharacterBox,1),9);
                wire.Send(new S.WeightUpdate { BagWeight=1,WearWeight=2,HandWeight=3 });
                AssertElements(scene.CharacterBox,1,8,1); AssertWeights(scene,"1 of 100","2/40","3/20",Color.White);
                checkpoint("positive-restored");

                wire.Send(new S.LevelChanged { Level=1,Experience=25 });
                Check(scene.User.Level == 1 && scene.User.MaxExperience == 100 && scene.MainPanel.LevelLabel.Text == "1" && scene.MainPanel.ExperienceLabel.Text.Trim() == "25.00%", "Native level/experience fraction incorrect");
                checkpoint("level-one-quarter");
                wire.Send(new S.GainedExperience { Amount=25 });
                Check(scene.User.Experience == 50 && scene.MainPanel.ExperienceLabel.Text.Trim() == "50.00%", "Native gained experience incorrect");
                checkpoint("experience-gain");
                wire.Send(new S.GainedExperience { Amount=-40 });
                Check(scene.User.Experience == 10 && scene.MainPanel.ExperienceLabel.Text.Trim() == "10.00%", "Native lost experience incorrect");
                checkpoint("experience-loss");

                wire.Send(new S.LevelChanged { Level=35,Experience=12345 });
                string text = scene.MainPanel.ExperienceLabel.Text, hint = scene.MainPanel.ExperienceBar.Hint;
                wire.Send(new S.GainedExperience { Amount=1 });
                Check(scene.MainPanel.ExperienceLabel.Text == text && scene.MainPanel.ExperienceBar.Hint != hint, "Hint-only experience fixture did not retain rounded percentage");
                checkpoint("experience-hint-only");
                wire.Send(new S.LevelChanged { Level=Globals.ExperienceList.Count,Experience=12345 });
                Check(scene.User.MaxExperience == 0 && scene.MainPanel.ExperienceLabel.Text.Trim() == "12,345" && scene.MainPanel.ExperienceBar.Hint.Trim() == "12,345/ 0", "Native no-next-level experience branch incorrect");
                checkpoint("experience-capped");
                wire.Send(new S.LevelChanged { Level=2,Experience=50 });
                Check(scene.User.MaxExperience == 200 && scene.MainPanel.ExperienceLabel.Text.Trim() == "25.00%", "Native return from capped experience retained raw display");
                checkpoint("experience-resumed");

                foreach (var page in new[] { "CharacterTab", "StatsTab", "HermitTab" })
                {
                    if (official) scene.CharacterBox.ShowGameUiCompanion(page == "CharacterTab" ? (GameUiCharacterPage?)null :
                        page == "StatsTab" ? GameUiCharacterPage.Status : GameUiCharacterPage.Hermit);
                    else Field<DXTabControl>(scene.CharacterBox,"TabControl").SelectedTab = Field<DXTab>(scene.CharacterBox,page);
                    Render(scene,Path.Combine(evidence,(official ? "official" : "legacy") + "-refresh-" + page + ".png"));
                    checkpoint("final-" + page);
                }
                var retired = new RetiredScene(scene);
                wire.Send(new S.GameLogout { Characters=Characters() });
                retired.AssertReleased(owner,listeners,"native-character-refresh");
            }
        }
        finally
        {
            CEnvir.Connection = null;
            if (DXControl.ActiveScene != null) DXControl.ActiveScene.Dispose();
            DXControl.ActiveScene = null;
        }
        refreshPackets += socketPackets-packetsBefore;
        File.WriteAllLines(Path.Combine(evidence,"native-character-refresh-fields.txt"),refreshRows);
        File.WriteAllLines(Path.Combine(evidence,"native-character-refresh-summary.txt"),new[] {
            "checkpoints=" + refreshCheckpoints, "serializedPackets=" + refreshPackets,
            "legacyParityFields=" + refreshParityFields, "originalAttributeLabels=57", "fieldsPerCheckpoint=68",
            "style=" + (official ? "official" : "legacy"), "releaseAccepted=false"
        });
        Console.WriteLine("Native character refresh: " + refreshCheckpoints + " checkpoints, " + refreshPackets + " serialized loopback packets, " + refreshParityFields + " Legacy parity fields; production acceptance remains false.");
    }
}
