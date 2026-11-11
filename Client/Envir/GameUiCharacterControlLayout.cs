using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;

namespace Client.Envir
{
    public enum GameUiCharacterControlPage { Chrome, Character, Hermit }

    // Bounds are window-relative; the adapter converts them to the existing owning control.
    public sealed class GameUiCharacterControlRow
    {
        public string Id { get; }
        public GameUiCharacterControlPage Page { get; }
        public Rectangle Bounds { get; }
        public string Text { get; }
        public GameUiCharacterControlRow(string id, GameUiCharacterControlPage page, Rectangle bounds, string text = null)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("A control ID is required.", nameof(id));
            if (!Enum.IsDefined(typeof(GameUiCharacterControlPage), page)) throw new ArgumentOutOfRangeException(nameof(page));
            GameUiCharacterAttributeRow.Validate(bounds);
            Id = id; Page = page; Bounds = bounds; Text = text;
        }
    }

    public sealed class GameUiCharacterControlLayout
    {
        public ReadOnlyCollection<GameUiCharacterControlRow> Rows { get; }
        public GameUiCharacterControlLayout(GameUiCharacterControlRow[] rows)
        {
            if (rows == null || rows.Length != 24) throw new ArgumentException("Exactly 24 character control rows are required.", nameof(rows));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows)
                if (row == null || !ids.Add(row.Id)) throw new ArgumentException("Null or duplicate character control row.", nameof(rows));
            Rows = Array.AsReadOnly((GameUiCharacterControlRow[])rows.Clone());
        }

        // Compiled candidate. Runtime never loads layout-official-v1.json.
        public static GameUiCharacterControlLayout CreateOfficialCandidate()
        {
            return new GameUiCharacterControlLayout(new[] {
                new GameUiCharacterControlRow("caption.new.level", GameUiCharacterControlPage.Character, new Rectangle(276, 96, 28, 20), "等级"),
                new GameUiCharacterControlRow("caption.new.experience", GameUiCharacterControlPage.Character, new Rectangle(276, 120, 28, 20), "经验"),
                new GameUiCharacterControlRow("caption.new.health", GameUiCharacterControlPage.Character, new Rectangle(276, 144, 28, 20), "生命"),
                new GameUiCharacterControlRow("caption.new.mana", GameUiCharacterControlPage.Character, new Rectangle(276, 168, 28, 20), "魔法"),
                new GameUiCharacterControlRow("caption.existing.HandWeightLabel", GameUiCharacterControlPage.Character, new Rectangle(276, 192, 28, 20), "腕力"),
                new GameUiCharacterControlRow("caption.existing.WearWeightLabel", GameUiCharacterControlPage.Character, new Rectangle(276, 216, 28, 20), "肩力"),
                new GameUiCharacterControlRow("control.CharacterNameLabel", GameUiCharacterControlPage.Chrome, new Rectangle(42, 10, 328, 22), null),
                new GameUiCharacterControlRow("control.GuildNameLabel", GameUiCharacterControlPage.Character, new Rectangle(16, 66, 224, 22), null),
                new GameUiCharacterControlRow("control.GuildRankLabel", GameUiCharacterControlPage.Character, new Rectangle(254, 66, 142, 22), null),
                new GameUiCharacterControlRow("control.WearWeightLabel", GameUiCharacterControlPage.Character, new Rectangle(306, 216, 82, 20), null),
                new GameUiCharacterControlRow("control.HandWeightLabel", GameUiCharacterControlPage.Character, new Rectangle(306, 192, 82, 20), null),
                new GameUiCharacterControlRow("caption.existing.RemainingLabel", GameUiCharacterControlPage.Hermit, new Rectangle(16, 370, 100, 24), "未分配点数"),
                new GameUiCharacterControlRow("control.RemainingLabel", GameUiCharacterControlPage.Hermit, new Rectangle(116, 370, 80, 24), null),
                new GameUiCharacterControlRow("control.MarriageIcon", GameUiCharacterControlPage.Chrome, new Rectangle(370, 12, 14, 16), null),
                new GameUiCharacterControlRow("control.ShowHelmetBox", GameUiCharacterControlPage.Character, new Rectangle(254, 340, 142, 22), "显示头盔"),
                new GameUiCharacterControlRow("action.hermit.MaxAC", GameUiCharacterControlPage.Hermit, new Rectangle(174, 94, 24, 26), "+"),
                new GameUiCharacterControlRow("action.hermit.MaxMR", GameUiCharacterControlPage.Hermit, new Rectangle(174, 128, 24, 26), "+"),
                new GameUiCharacterControlRow("action.hermit.Health", GameUiCharacterControlPage.Hermit, new Rectangle(174, 264, 24, 26), "+"),
                new GameUiCharacterControlRow("action.hermit.Mana", GameUiCharacterControlPage.Hermit, new Rectangle(174, 298, 24, 26), "+"),
                new GameUiCharacterControlRow("action.hermit.MaxDC", GameUiCharacterControlPage.Hermit, new Rectangle(174, 162, 24, 26), "+"),
                new GameUiCharacterControlRow("action.hermit.MaxMC", GameUiCharacterControlPage.Hermit, new Rectangle(174, 196, 24, 26), "+"),
                new GameUiCharacterControlRow("action.hermit.MaxSC", GameUiCharacterControlPage.Hermit, new Rectangle(174, 230, 24, 26), "+"),
                new GameUiCharacterControlRow("action.hermit.WeaponElement", GameUiCharacterControlPage.Hermit, new Rectangle(174, 332, 24, 26), "+"),
                new GameUiCharacterControlRow("hermit.confirmation", GameUiCharacterControlPage.Hermit, new Rectangle(16, 402, 180, 24), "加点前确认"),
            });
        }
    }
}
