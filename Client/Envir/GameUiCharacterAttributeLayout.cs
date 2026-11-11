using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;

namespace Client.Envir
{
    public enum GameUiCharacterPage { Character, Status, Hermit }

    // All bounds are relative to the character window, before conversion to the owning page.
    public sealed class GameUiCharacterAttributeRow
    {
        public string Id { get; }
        public GameUiCharacterPage Page { get; }
        public Rectangle Value { get; }
        public Rectangle? Caption { get; }
        public Rectangle? Icon { get; }
        public string CaptionText { get; }

        public GameUiCharacterAttributeRow(string id, GameUiCharacterPage page, Rectangle value,
            Rectangle? caption, Rectangle? icon, string captionText = null)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("An attribute ID is required.", nameof(id));
            if (!Enum.IsDefined(typeof(GameUiCharacterPage), page)) throw new ArgumentOutOfRangeException(nameof(page));
            Validate(value);
            if (caption.HasValue) Validate(caption.Value);
            if (icon.HasValue) Validate(icon.Value);
            if (caption.HasValue && icon.HasValue) throw new ArgumentException("A row must reuse either its caption or its Tag icon.");
            Id = id; Page = page; Value = value; Caption = caption; Icon = icon; CaptionText = captionText;
        }

        internal static void Validate(Rectangle value)
        {
            if (value.X < 0 || value.Y < 0 || value.Width <= 0 || value.Height <= 0 ||
                (long)value.X + value.Width > 16384 || (long)value.Y + value.Height > 16384)
                throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    public sealed class GameUiCharacterAttributeLayout
    {
        public ReadOnlyCollection<GameUiCharacterAttributeRow> Rows { get; }
        public ReadOnlyDictionary<string, Rectangle> GroupCaptions { get; }

        public GameUiCharacterAttributeLayout(GameUiCharacterAttributeRow[] rows, IDictionary<string, Rectangle> groups)
        {
            if (rows == null || rows.Length != 57) throw new ArgumentException("Exactly 57 attribute rows are required.", nameof(rows));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows)
                if (row == null || !ids.Add(row.Id)) throw new ArgumentException("Null or duplicate attribute row.", nameof(rows));
            if (groups == null || groups.Count != 3 || !groups.ContainsKey("AttackStats") ||
                !groups.ContainsKey("AdvantageStats") || !groups.ContainsKey("DisadvantageStats"))
                throw new ArgumentException("All three main element group captions are required.", nameof(groups));
            foreach (var bounds in groups.Values) GameUiCharacterAttributeRow.Validate(bounds);
            Rows = Array.AsReadOnly((GameUiCharacterAttributeRow[])rows.Clone());
            GroupCaptions = new ReadOnlyDictionary<string, Rectangle>(new Dictionary<string, Rectangle>(groups, StringComparer.Ordinal));
        }

        // Compiled candidate from layout-official-v1.json. No runtime JSON or GameScene activation.
        public static GameUiCharacterAttributeLayout CreateOfficialCandidate()
        {
            return new GameUiCharacterAttributeLayout(new[] {
                new GameUiCharacterAttributeRow("DisplayStats.MaxAC", GameUiCharacterPage.Character, new Rectangle(54, 426, 70, 20), new Rectangle(24, 426, 28, 20), null, "防御"),
                new GameUiCharacterAttributeRow("DisplayStats.MaxMR", GameUiCharacterPage.Character, new Rectangle(180, 426, 70, 20), new Rectangle(150, 426, 28, 20), null, "魔防"),
                new GameUiCharacterAttributeRow("DisplayStats.MaxDC", GameUiCharacterPage.Character, new Rectangle(54, 402, 70, 20), new Rectangle(24, 402, 28, 20), null, "破坏"),
                new GameUiCharacterAttributeRow("DisplayStats.MaxMC", GameUiCharacterPage.Character, new Rectangle(180, 402, 70, 20), new Rectangle(150, 402, 28, 20), null, "自然"),
                new GameUiCharacterAttributeRow("DisplayStats.MaxSC", GameUiCharacterPage.Character, new Rectangle(306, 402, 70, 20), new Rectangle(276, 402, 28, 20), null, "灵魂"),
                new GameUiCharacterAttributeRow("DisplayStats.Accuracy", GameUiCharacterPage.Character, new Rectangle(306, 240, 82, 20), new Rectangle(276, 240, 28, 20), null, "准确"),
                new GameUiCharacterAttributeRow("DisplayStats.Agility", GameUiCharacterPage.Character, new Rectangle(306, 264, 82, 20), new Rectangle(276, 264, 28, 20), null, "敏捷"),
                new GameUiCharacterAttributeRow("DisplayStats.AttackSpeed", GameUiCharacterPage.Character, new Rectangle(306, 426, 70, 20), new Rectangle(276, 426, 28, 20), null, "攻速"),
                new GameUiCharacterAttributeRow("DisplayStats.Luck", GameUiCharacterPage.Character, new Rectangle(306, 312, 82, 20), new Rectangle(276, 312, 28, 20), null, "幸运"),
                new GameUiCharacterAttributeRow("DisplayStats.Comfort", GameUiCharacterPage.Character, new Rectangle(306, 288, 82, 20), new Rectangle(276, 288, 28, 20), null, "舒适"),
                new GameUiCharacterAttributeRow("DisplayStats.LifeSteal", GameUiCharacterPage.Status, new Rectangle(120, 130, 76, 28), new Rectangle(16, 130, 104, 28), null, null),
                new GameUiCharacterAttributeRow("DisplayStats.GoldRate", GameUiCharacterPage.Status, new Rectangle(120, 166, 76, 28), new Rectangle(16, 166, 104, 28), null, null),
                new GameUiCharacterAttributeRow("DisplayStats.CriticalChance", GameUiCharacterPage.Status, new Rectangle(120, 202, 76, 28), new Rectangle(16, 202, 104, 28), null, null),
                new GameUiCharacterAttributeRow("DisplayStats.DropRate", GameUiCharacterPage.Status, new Rectangle(120, 238, 76, 28), new Rectangle(16, 238, 104, 28), null, null),
                new GameUiCharacterAttributeRow("DisplayStats.PickUpRadius", GameUiCharacterPage.Status, new Rectangle(120, 274, 76, 28), new Rectangle(16, 274, 104, 28), null, null),
                new GameUiCharacterAttributeRow("DisplayStats.ExperienceRate", GameUiCharacterPage.Status, new Rectangle(120, 310, 76, 28), new Rectangle(16, 310, 104, 28), null, null),
                new GameUiCharacterAttributeRow("DisplayStats.CriticalDamage", GameUiCharacterPage.Status, new Rectangle(314, 94, 76, 28), new Rectangle(210, 94, 104, 28), null, null),
                new GameUiCharacterAttributeRow("DisplayStats.Rebirth", GameUiCharacterPage.Status, new Rectangle(314, 130, 76, 28), new Rectangle(210, 130, 104, 28), null, null),
                new GameUiCharacterAttributeRow("DisplayStats.BlockChance", GameUiCharacterPage.Status, new Rectangle(314, 166, 76, 28), new Rectangle(210, 166, 104, 28), null, null),
                new GameUiCharacterAttributeRow("DisplayStats.EvasionChance", GameUiCharacterPage.Status, new Rectangle(314, 202, 76, 28), new Rectangle(210, 202, 104, 28), null, null),
                // ProgUse element tiles include their 20px backing; use the full tile and native 46px column / 23px row spacing.
                new GameUiCharacterAttributeRow("AttackStats.FireAttack", GameUiCharacterPage.Character, new Rectangle(96, 450, 26, 20), null, new Rectangle(76, 450, 20, 20), null),
                new GameUiCharacterAttributeRow("AttackStats.IceAttack", GameUiCharacterPage.Character, new Rectangle(142, 450, 26, 20), null, new Rectangle(122, 450, 20, 20), null),
                new GameUiCharacterAttributeRow("AttackStats.LightningAttack", GameUiCharacterPage.Character, new Rectangle(188, 450, 26, 20), null, new Rectangle(168, 450, 20, 20), null),
                new GameUiCharacterAttributeRow("AttackStats.WindAttack", GameUiCharacterPage.Character, new Rectangle(234, 450, 26, 20), null, new Rectangle(214, 450, 20, 20), null),
                new GameUiCharacterAttributeRow("AttackStats.HolyAttack", GameUiCharacterPage.Character, new Rectangle(280, 450, 26, 20), null, new Rectangle(260, 450, 20, 20), null),
                new GameUiCharacterAttributeRow("AttackStats.DarkAttack", GameUiCharacterPage.Character, new Rectangle(326, 450, 26, 20), null, new Rectangle(306, 450, 20, 20), null),
                new GameUiCharacterAttributeRow("AttackStats.PhantomAttack", GameUiCharacterPage.Character, new Rectangle(372, 450, 26, 20), null, new Rectangle(352, 450, 20, 20), null),
                new GameUiCharacterAttributeRow("AdvantageStats.FireResistance", GameUiCharacterPage.Character, new Rectangle(96, 473, 26, 20), null, new Rectangle(76, 473, 20, 20), null),
                new GameUiCharacterAttributeRow("AdvantageStats.IceResistance", GameUiCharacterPage.Character, new Rectangle(142, 473, 26, 20), null, new Rectangle(122, 473, 20, 20), null),
                new GameUiCharacterAttributeRow("AdvantageStats.LightningResistance", GameUiCharacterPage.Character, new Rectangle(188, 473, 26, 20), null, new Rectangle(168, 473, 20, 20), null),
                new GameUiCharacterAttributeRow("AdvantageStats.WindResistance", GameUiCharacterPage.Character, new Rectangle(234, 473, 26, 20), null, new Rectangle(214, 473, 20, 20), null),
                new GameUiCharacterAttributeRow("AdvantageStats.HolyResistance", GameUiCharacterPage.Character, new Rectangle(280, 473, 26, 20), null, new Rectangle(260, 473, 20, 20), null),
                new GameUiCharacterAttributeRow("AdvantageStats.DarkResistance", GameUiCharacterPage.Character, new Rectangle(326, 473, 26, 20), null, new Rectangle(306, 473, 20, 20), null),
                new GameUiCharacterAttributeRow("AdvantageStats.PhantomResistance", GameUiCharacterPage.Character, new Rectangle(372, 473, 26, 20), null, new Rectangle(352, 473, 20, 20), null),
                new GameUiCharacterAttributeRow("AdvantageStats.PhysicalResistance", GameUiCharacterPage.Status, new Rectangle(314, 238, 76, 28), null, new Rectangle(210, 238, 16, 28), null),
                new GameUiCharacterAttributeRow("DisadvantageStats.FireResistance", GameUiCharacterPage.Character, new Rectangle(96, 496, 26, 20), null, new Rectangle(76, 496, 20, 20), null),
                new GameUiCharacterAttributeRow("DisadvantageStats.IceResistance", GameUiCharacterPage.Character, new Rectangle(142, 496, 26, 20), null, new Rectangle(122, 496, 20, 20), null),
                new GameUiCharacterAttributeRow("DisadvantageStats.LightningResistance", GameUiCharacterPage.Character, new Rectangle(188, 496, 26, 20), null, new Rectangle(168, 496, 20, 20), null),
                new GameUiCharacterAttributeRow("DisadvantageStats.WindResistance", GameUiCharacterPage.Character, new Rectangle(234, 496, 26, 20), null, new Rectangle(214, 496, 20, 20), null),
                new GameUiCharacterAttributeRow("DisadvantageStats.HolyResistance", GameUiCharacterPage.Character, new Rectangle(280, 496, 26, 20), null, new Rectangle(260, 496, 20, 20), null),
                new GameUiCharacterAttributeRow("DisadvantageStats.DarkResistance", GameUiCharacterPage.Character, new Rectangle(326, 496, 26, 20), null, new Rectangle(306, 496, 20, 20), null),
                new GameUiCharacterAttributeRow("DisadvantageStats.PhantomResistance", GameUiCharacterPage.Character, new Rectangle(372, 496, 26, 20), null, new Rectangle(352, 496, 20, 20), null),
                new GameUiCharacterAttributeRow("DisadvantageStats.PhysicalResistance", GameUiCharacterPage.Status, new Rectangle(314, 274, 76, 28), null, new Rectangle(210, 274, 16, 28), null),
                new GameUiCharacterAttributeRow("HermitDisplayStats.MaxAC", GameUiCharacterPage.Hermit, new Rectangle(96, 94, 74, 26), new Rectangle(16, 94, 80, 26), null, null),
                new GameUiCharacterAttributeRow("HermitDisplayStats.MaxMR", GameUiCharacterPage.Hermit, new Rectangle(96, 128, 74, 26), new Rectangle(16, 128, 80, 26), null, null),
                new GameUiCharacterAttributeRow("HermitDisplayStats.MaxDC", GameUiCharacterPage.Hermit, new Rectangle(96, 162, 74, 26), new Rectangle(16, 162, 80, 26), null, null),
                new GameUiCharacterAttributeRow("HermitDisplayStats.MaxMC", GameUiCharacterPage.Hermit, new Rectangle(96, 196, 74, 26), new Rectangle(16, 196, 80, 26), null, null),
                new GameUiCharacterAttributeRow("HermitDisplayStats.MaxSC", GameUiCharacterPage.Hermit, new Rectangle(96, 230, 74, 26), new Rectangle(16, 230, 80, 26), null, null),
                new GameUiCharacterAttributeRow("HermitDisplayStats.Health", GameUiCharacterPage.Hermit, new Rectangle(96, 264, 74, 26), new Rectangle(16, 264, 80, 26), null, null),
                new GameUiCharacterAttributeRow("HermitDisplayStats.Mana", GameUiCharacterPage.Hermit, new Rectangle(96, 298, 74, 26), new Rectangle(16, 298, 80, 26), null, null),
                new GameUiCharacterAttributeRow("HermitAttackStats.FireAttack", GameUiCharacterPage.Hermit, new Rectangle(304, 94, 90, 26), null, new Rectangle(224, 94, 16, 26), null),
                new GameUiCharacterAttributeRow("HermitAttackStats.IceAttack", GameUiCharacterPage.Hermit, new Rectangle(304, 128, 90, 26), null, new Rectangle(224, 128, 16, 26), null),
                new GameUiCharacterAttributeRow("HermitAttackStats.LightningAttack", GameUiCharacterPage.Hermit, new Rectangle(304, 162, 90, 26), null, new Rectangle(224, 162, 16, 26), null),
                new GameUiCharacterAttributeRow("HermitAttackStats.WindAttack", GameUiCharacterPage.Hermit, new Rectangle(304, 196, 90, 26), null, new Rectangle(224, 196, 16, 26), null),
                new GameUiCharacterAttributeRow("HermitAttackStats.HolyAttack", GameUiCharacterPage.Hermit, new Rectangle(304, 230, 90, 26), null, new Rectangle(224, 230, 16, 26), null),
                new GameUiCharacterAttributeRow("HermitAttackStats.DarkAttack", GameUiCharacterPage.Hermit, new Rectangle(304, 264, 90, 26), null, new Rectangle(224, 264, 16, 26), null),
                new GameUiCharacterAttributeRow("HermitAttackStats.PhantomAttack", GameUiCharacterPage.Hermit, new Rectangle(304, 298, 90, 26), null, new Rectangle(224, 298, 16, 26), null)
            }, new Dictionary<string, Rectangle> {
                { "AttackStats", new Rectangle(24, 450, 52, 20) },
                { "AdvantageStats", new Rectangle(24, 473, 52, 20) },
                { "DisadvantageStats", new Rectangle(24, 496, 52, 20) }
            });
        }
    }
}
