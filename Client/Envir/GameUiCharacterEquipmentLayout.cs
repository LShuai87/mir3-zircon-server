using System;
using System.Drawing;
using Client.Controls;

namespace Client.Envir
{
    // Container is window-relative; page/header/cell rectangles are relative to the container.
    // This adapter covers page headers and equipment. Attribute/caption geometry is a separate binding.
    public sealed class GameUiCharacterEquipmentLayout
    {
        public Rectangle Container { get; }
        public DXTabGeometry Character { get; }
        public DXTabGeometry Status { get; }
        public DXTabGeometry Hermit { get; }
        private readonly Rectangle[] equipment;

        public GameUiCharacterEquipmentLayout(Rectangle container, DXTabGeometry character,
            DXTabGeometry status, DXTabGeometry hermit, Rectangle[] equipment)
        {
            if (container.X < 0 || container.Y < 0 || container.Width <= 0 || container.Height <= 0 ||
                (long)container.X + container.Width > 16384 || (long)container.Y + container.Height > 16384)
                throw new ArgumentOutOfRangeException(nameof(container));
            var pages = new[] { character, status, hermit };
            var area = new Rectangle(Point.Empty, container.Size);
            for (int i = 0; i < pages.Length; i++)
            {
                if (pages[i] == null) throw new ArgumentNullException(nameof(character));
                if (!area.Contains(pages[i].Page) || !area.Contains(pages[i].Button))
                    throw new ArgumentException("Tab geometry exceeds the container.");
                for (int j = 0; j < pages.Length; j++)
                {
                    if (pages[j] == null) continue;
                    if (pages[i].Page.IntersectsWith(pages[j].Button) ||
                        (i != j && pages[i].Button.IntersectsWith(pages[j].Button)))
                        throw new ArgumentException("Headers overlap a page or another header.");
                }
            }
            if (equipment == null || equipment.Length != 16)
                throw new ArgumentException("Exactly 16 equipment slots are required.", nameof(equipment));
            for (int i = 0; i < equipment.Length; i++)
            {
                Rectangle rect = equipment[i];
                if (rect.Width != 36 || rect.Height != 36 || !character.Page.Contains(rect))
                    throw new ArgumentException("Equipment must be a 36 pixel cell inside its page.");
                for (int j = 0; j < i; j++)
                    if (rect.IntersectsWith(equipment[j]))
                        throw new ArgumentException("Equipment cells on the same page overlap.");
            }
            Container = container;
            Character = character; Status = status; Hermit = hermit;
            this.equipment = (Rectangle[])equipment.Clone();
        }

        public Rectangle GetEquipmentBounds(int slot)
        {
            if (slot < 0 || slot >= 16) throw new ArgumentOutOfRangeException(nameof(slot));
            return equipment[slot];
        }

        // Coordinates from layout-official-v1.json; not selected by GameScene until the complete skin is ready.
        public static GameUiCharacterEquipmentLayout CreateOfficialCandidate()
        {
            var page = new Rectangle(0, 64, 412, 498);
            return new GameUiCharacterEquipmentLayout(new Rectangle(0, 0, 412, 562),
                new DXTabGeometry(page, new Rectangle(8, 38, 130, 24)),
                new DXTabGeometry(page, new Rectangle(140, 38, 130, 24)),
                new DXTabGeometry(page, new Rectangle(272, 38, 132, 24)),
                // Native frames are 38px: inset the 36px controls by 1px. Upper rows step by 49px.
                new[] {
                    new Rectangle(191,293,36,36),
                    new Rectangle(147,293,36,36),
                    new Rectangle(103,293,36,36),
                    new Rectangle(59,293,36,36),
                    new Rectangle(191,97,36,36),
                    new Rectangle(15,195,36,36),
                    new Rectangle(191,195,36,36),
                    new Rectangle(15,244,36,36),
                    new Rectangle(191,244,36,36),
                    new Rectangle(15,293,36,36),
                    new Rectangle(15,339,36,36),
                    new Rectangle(147,339,36,36),
                    new Rectangle(191,339,36,36),
                    new Rectangle(103,339,36,36),
                    new Rectangle(191,146,36,36),
                    new Rectangle(59,339,36,36)
                });
        }
    }
}
