using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;

namespace Client.Envir
{
    public sealed class GameUiHudRegion
    {
        public string Owner { get; }
        public Rectangle Bounds { get; }
        public GameUiHudRegion(string owner, Rectangle bounds) { Owner = owner; Bounds = bounds; }
    }

    // Immutable local geometry; the scene coordinator positions the four exposed roots.
    public sealed class GameUiHudLayout
    {
        private static readonly string[] OwnerNames = { "orb", "commands", "readouts", "experience" };
        private static readonly string[] BindingNames = { "CharacterButton", "InventoryButton", "SpellButton", "QuestButton", "MailButton", "GroupButton", "ConfigButton", "CashShopButton", "BeltButton", "AttackModeLabel", "PetModeLabel", "ClassLabel", "LevelLabel", "HealthLabel", "ManaLabel", "ACLabel", "MRLabel", "DCLabel", "MCLabel", "SCLabel", "AccuracyLabel", "AgilityLabel", "ExperienceBar", "ExperienceLabel", "HealthBar", "ManaBar" };
        private static readonly string[] WheelNames = { "SpellButton", "QuestButton", "MailButton", "GroupButton", "InventoryButton", "ConfigButton", "CharacterButton" };
        // Back to front, shared by rendering and reverse-order pointer routing.
        public static IReadOnlyList<string> CommandImageDrawOrder { get; } = Array.AsReadOnly(WheelNames.Concat(new[] { "CashShopButton" }).ToArray());
        public IReadOnlyDictionary<string, Size> Owners { get; }
        public IReadOnlyDictionary<string, GameUiHudRegion> Regions { get; }
        public GameUiHudLayout(IDictionary<string, Size> owners, IDictionary<string, GameUiHudRegion> regions)
        {
            if (owners == null) throw new ArgumentNullException(nameof(owners));
            if (regions == null) throw new ArgumentNullException(nameof(regions));
            if (!new HashSet<string>(OwnerNames, StringComparer.Ordinal).SetEquals(owners.Keys) ||
                !new HashSet<string>(BindingNames, StringComparer.Ordinal).SetEquals(regions.Keys))
                throw new ArgumentException("HUD owners and original bindings must be complete and unique.");
            var sizes = new Dictionary<string, Size>(owners, StringComparer.Ordinal);
            var areas = new Dictionary<string, GameUiHudRegion>(regions, StringComparer.Ordinal);
            foreach (var pair in sizes)
                if (pair.Value.Width <= 0 || pair.Value.Height <= 0 || pair.Value.Width > 16384 || pair.Value.Height > 16384)
                    throw new ArgumentOutOfRangeException(nameof(owners));
            foreach (var pair in areas)
            {
                GameUiHudRegion region = pair.Value;
                if (region == null || region.Owner == null || !sizes.ContainsKey(region.Owner)) throw new ArgumentException("Unknown HUD region owner.");
                Rectangle r = region.Bounds; Size size = sizes[region.Owner];
                if (r.X < 0 || r.Y < 0 || r.Width <= 0 || r.Height <= 0 || (long)r.X + r.Width > size.Width || (long)r.Y + r.Height > size.Height)
                    throw new ArgumentException("HUD region exceeds its owner: " + pair.Key);
            }
            // These controls have native behavior tied to their semantic owner.
            foreach (string key in BindingNames)
            {
                string expected = key.EndsWith("Button", StringComparison.Ordinal) || key == "AttackModeLabel" || key == "PetModeLabel" ? "commands" :
                    key.StartsWith("Experience", StringComparison.Ordinal) ? "experience" :
                    new[] { "ClassLabel", "LevelLabel", "HealthLabel", "ManaLabel", "HealthBar", "ManaBar" }.Contains(key) ? "orb" : "readouts";
                if (areas[key].Owner != expected) throw new ArgumentException("HUD binding moved to an incompatible owner: " + key);
            }
            foreach (var region in areas.Values.Where(r => r.Owner == "readouts"))
                if (region.Bounds.X < 28) throw new ArgumentException("HUD readouts require 28 pixels for their visible caption.");
            var commands = areas.Where(p => p.Value.Owner == "commands").ToArray();
            for (int i = 0; i < commands.Length; i++)
                for (int j = 0; j < i; j++)
                    if (commands[i].Value.Bounds.IntersectsWith(commands[j].Value.Bounds))
                    {
                        Rectangle a = commands[i].Value.Bounds, b = commands[j].Value.Bounds;
                        // Only the seven alpha-tested wheel images may share an edge.
                        // Preserve an exclusive centre for every command; duplicate or
                        // heavily overlapping targets must not silently pass validation.
                        if (!WheelNames.Contains(commands[i].Key) || !WheelNames.Contains(commands[j].Key) ||
                            a.Contains(new Point(b.X + b.Width / 2, b.Y + b.Height / 2)) ||
                            b.Contains(new Point(a.X + a.Width / 2, a.Y + a.Height / 2)))
                            throw new ArgumentException("HUD commands overlap outside alpha-tested wheel edges.");
                    }
            Owners = new ReadOnlyDictionary<string, Size>(sizes);
            Regions = new ReadOnlyDictionary<string, GameUiHudRegion>(areas);
        }

        public static GameUiHudLayout OfficialV1 { get; } = new GameUiHudLayout(
            new Dictionary<string, Size> { ["orb"] = new Size(225, 202), ["commands"] = new Size(225, 202), ["readouts"] = new Size(574, 16), ["experience"] = new Size(574, 8) },
            new Dictionary<string, GameUiHudRegion>
            {
                ["CharacterButton"] = new GameUiHudRegion("commands", new Rectangle(85, 94, 58, 58)),
                ["InventoryButton"] = new GameUiHudRegion("commands", new Rectangle(88, 67, 54, 32)),
                ["SpellButton"] = new GameUiHudRegion("commands", new Rectangle(55, 76, 42, 50)),
                ["QuestButton"] = new GameUiHudRegion("commands", new Rectangle(130, 74, 42, 50)),
                ["MailButton"] = new GameUiHudRegion("commands", new Rectangle(56, 126, 42, 50)),
                ["GroupButton"] = new GameUiHudRegion("commands", new Rectangle(131, 125, 42, 50)),
                ["ConfigButton"] = new GameUiHudRegion("commands", new Rectangle(85, 152, 54, 32)),
                ["CashShopButton"] = new GameUiHudRegion("commands", new Rectangle(180, 23, 40, 40)),
                ["BeltButton"] = new GameUiHudRegion("commands", new Rectangle(5, 111, 31, 24)),
                ["AttackModeLabel"] = new GameUiHudRegion("commands", new Rectangle(4, 4, 172, 16)),
                ["PetModeLabel"] = new GameUiHudRegion("commands", new Rectangle(4, 21, 172, 16)),
                ["ClassLabel"] = new GameUiHudRegion("orb", new Rectangle(4, 4, 84, 16)),
                ["LevelLabel"] = new GameUiHudRegion("orb", new Rectangle(90, 4, 84, 16)),
                ["HealthLabel"] = new GameUiHudRegion("orb", new Rectangle(20, 150, 70, 18)),
                ["ManaLabel"] = new GameUiHudRegion("orb", new Rectangle(96, 150, 70, 18)),
                ["ACLabel"] = new GameUiHudRegion("readouts", new Rectangle(28, 0, 52, 16)),
                ["MRLabel"] = new GameUiHudRegion("readouts", new Rectangle(110, 0, 52, 16)),
                ["DCLabel"] = new GameUiHudRegion("readouts", new Rectangle(192, 0, 52, 16)),
                ["MCLabel"] = new GameUiHudRegion("readouts", new Rectangle(274, 0, 52, 16)),
                ["SCLabel"] = new GameUiHudRegion("readouts", new Rectangle(356, 0, 52, 16)),
                ["AccuracyLabel"] = new GameUiHudRegion("readouts", new Rectangle(438, 0, 52, 16)),
                ["AgilityLabel"] = new GameUiHudRegion("readouts", new Rectangle(520, 0, 52, 16)),
                ["ExperienceBar"] = new GameUiHudRegion("experience", new Rectangle(0, 0, 574, 8)),
                ["ExperienceLabel"] = new GameUiHudRegion("experience", new Rectangle(0, 0, 574, 8)),
                ["HealthBar"] = new GameUiHudRegion("orb", new Rectangle(43, 75, 53, 101)),
                ["ManaBar"] = new GameUiHudRegion("orb", new Rectangle(96, 75, 48, 101)),
            });
    }
}
