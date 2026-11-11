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
        private static readonly string[] BindingNames = { "CharacterButton", "InventoryButton", "SpellButton", "QuestButton", "GuildButton", "GroupButton", "ConfigButton", "CashShopButton", "BeltButton", "AttackModeLabel", "PetModeLabel", "ClassLabel", "LevelLabel", "HealthLabel", "ManaLabel", "ACLabel", "MRLabel", "DCLabel", "MCLabel", "SCLabel", "AccuracyLabel", "AgilityLabel", "ExperienceBar", "ExperienceLabel", "HealthBar", "ManaBar" };
        private static readonly string[] WheelNames = { "BeltButton", "SpellButton", "QuestButton", "ConfigButton", "InventoryButton", "GuildButton", "CharacterButton" };
        // Back to front, shared by rendering and reverse-order pointer routing.
        public static IReadOnlyList<string> CommandImageDrawOrder { get; } = Array.AsReadOnly(WheelNames.Concat(new[] { "CashShopButton", "GroupButton" }).ToArray());
        // The native side bezel is 36 pixels wide. Four 31x24 buttons sit inside
        // its one-pixel vertical inset; the group action remains an original control.
        public static Rectangle ShortcutFrameBounds => new Rectangle(0, 64, 36, 99);
        public static Rectangle MountButtonBounds => new Rectangle(3, 65, 31, 24);
        public static Rectangle CombatButtonBounds => new Rectangle(3, 113, 31, 24);
        public static Rectangle PotentialButtonBounds => new Rectangle(3, 137, 31, 24);
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
            var orbLabels = new[] { "ClassLabel", "LevelLabel", "HealthLabel", "ManaLabel" };
            for (int i = 0; i < orbLabels.Length; i++)
                for (int j = 0; j < i; j++)
                    if (areas[orbLabels[i]].Bounds.IntersectsWith(areas[orbLabels[j]].Bounds))
                        throw new ArgumentException("HUD orb values must not overlap.");
            foreach (var region in areas.Values.Where(r => r.Owner == "readouts"))
                if (region.Bounds.X < 28) throw new ArgumentException("HUD readouts require 28 pixels for their visible caption.");
            var commands = areas.Where(p => p.Value.Owner == "commands").ToArray();
            for (int i = 0; i < commands.Length; i++)
                for (int j = 0; j < i; j++)
                    if (commands[i].Value.Bounds.IntersectsWith(commands[j].Value.Bounds))
                    {
                        Rectangle a = commands[i].Value.Bounds, b = commands[j].Value.Bounds;
                        // Wheel rectangles include transparent corners and may intersect.
                        // Integration tests independently require disjoint opaque pixels.
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
                ["CharacterButton"] = new GameUiHudRegion("commands", new Rectangle(85, 96, 58, 58)),
                ["InventoryButton"] = new GameUiHudRegion("commands", new Rectangle(87, 65, 54, 32)),
                ["SpellButton"] = new GameUiHudRegion("commands", new Rectangle(134, 76, 42, 50)),
                ["QuestButton"] = new GameUiHudRegion("commands", new Rectangle(54, 126, 42, 50)),
                ["GuildButton"] = new GameUiHudRegion("commands", new Rectangle(87, 153, 54, 32)),
                ["GroupButton"] = new GameUiHudRegion("commands", new Rectangle(3, 89, 31, 24)),
                ["ConfigButton"] = new GameUiHudRegion("commands", new Rectangle(133, 126, 42, 50)),
                ["CashShopButton"] = new GameUiHudRegion("commands", new Rectangle(180, 23, 40, 40)),
                ["BeltButton"] = new GameUiHudRegion("commands", new Rectangle(52, 76, 42, 50)),
                ["AttackModeLabel"] = new GameUiHudRegion("commands", new Rectangle(4, 4, 172, 16)),
                ["PetModeLabel"] = new GameUiHudRegion("commands", new Rectangle(4, 21, 172, 16)),
                ["ClassLabel"] = new GameUiHudRegion("orb", new Rectangle(4, 4, 84, 16)),
                ["LevelLabel"] = new GameUiHudRegion("orb", new Rectangle(12, 158, 28, 26)),
                ["HealthLabel"] = new GameUiHudRegion("orb", new Rectangle(43, 150, 53, 18)),
                ["ManaLabel"] = new GameUiHudRegion("orb", new Rectangle(96, 150, 48, 18)),
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
