using System;
using System.Collections.Generic;
using System.Drawing;

namespace Client.Envir
{
    // All coordinates are relative to the character window, regardless of the tab container origin.
    public sealed class GameUiCharacterCompositionLayout
    {
        public Rectangle Preview { get; }
        public Point Pivot { get; }
        public Rectangle Level { get; }
        public Rectangle Experience { get; }
        public Rectangle Health { get; }
        public Rectangle Mana { get; }

        public GameUiCharacterCompositionLayout(Rectangle preview, Point pivot, Rectangle level,
            Rectangle experience, Rectangle health, Rectangle mana)
        {
            var occupied = new List<Rectangle>();
            foreach (var rect in new[] { preview, level, experience, health, mana })
            {
                if (rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0 ||
                    (long)rect.X + rect.Width > 16384 || (long)rect.Y + rect.Height > 16384)
                    throw new ArgumentOutOfRangeException(nameof(preview));
                foreach (var prior in occupied)
                    if (rect.IntersectsWith(prior)) throw new ArgumentException("Preview and readouts overlap.");
                occupied.Add(rect);
            }
            if (!preview.Contains(pivot)) throw new ArgumentException("The preview pivot must be inside the preview.", nameof(pivot));
            Preview = preview; Pivot = pivot; Level = level; Experience = experience; Health = health; Mana = mana;
        }

        public Rectangle[] GetBounds() => new[] { Preview, Level, Experience, Health, Mana };

        internal GameUiCharacterReadoutLayout GetReadoutLayout(Point pageOrigin)
        {
            var values = new[] { Level, Experience, Health, Mana };
            for (int i = 0; i < values.Length; i++) values[i].Offset(-pageOrigin.X, -pageOrigin.Y);
            return new GameUiCharacterReadoutLayout(values[0], values[1], values[2], values[3]);
        }

        // The paper doll may extend behind equipment, but the baked slot frames stay in front.
        // Return disjoint regions so alpha/overlay pixels are never drawn more than once.
        public Rectangle[] GetPreviewRegions(GameUiCharacterEquipmentLayout equipment)
        {
            if (equipment == null) throw new ArgumentNullException(nameof(equipment));
            var regions = new List<Rectangle> { Preview };
            for (int slot = 0; slot < 16; slot++)
            {
                Rectangle frame = equipment.GetEquipmentBounds(slot);
                frame.Offset(equipment.Container.Location);
                frame.Inflate(1, 1); // Native 38px frame around the 36px item cell.
                var remaining = new List<Rectangle>();
                foreach (Rectangle region in regions)
                {
                    Rectangle overlap = Rectangle.Intersect(region, frame);
                    if (overlap.Width <= 0 || overlap.Height <= 0) { remaining.Add(region); continue; }
                    if (overlap.Top > region.Top)
                        remaining.Add(Rectangle.FromLTRB(region.Left, region.Top, region.Right, overlap.Top));
                    if (overlap.Bottom < region.Bottom)
                        remaining.Add(Rectangle.FromLTRB(region.Left, overlap.Bottom, region.Right, region.Bottom));
                    if (overlap.Left > region.Left)
                        remaining.Add(Rectangle.FromLTRB(region.Left, overlap.Top, overlap.Left, overlap.Bottom));
                    if (overlap.Right < region.Right)
                        remaining.Add(Rectangle.FromLTRB(overlap.Right, overlap.Top, region.Right, overlap.Bottom));
                }
                regions = remaining;
            }
            return regions.ToArray();
        }

        // Clip without scaling: source pixels and destination pixels always have the same dimensions.
        public static bool TryClip(Rectangle image, Rectangle viewport, out Rectangle source, out Point destination)
        {
            source = Rectangle.Empty; destination = Point.Empty;
            foreach (var rect in new[] { image, viewport })
                if (rect.Width <= 0 || rect.Height <= 0 || (long)rect.X + rect.Width > int.MaxValue ||
                    (long)rect.Y + rect.Height > int.MaxValue)
                    throw new ArgumentOutOfRangeException(nameof(image));
            var visible = Rectangle.Intersect(image, viewport);
            if (visible.Width <= 0 || visible.Height <= 0) return false;
            source = new Rectangle(visible.X - image.X, visible.Y - image.Y, visible.Width, visible.Height);
            destination = visible.Location;
            return true;
        }

        // Keep the native body/weapon pivot aligned with UI[2765]; allow weapons into the left artwork area.
        public static GameUiCharacterCompositionLayout CreateOfficialCandidate()
            => new GameUiCharacterCompositionLayout(new Rectangle(8, 96, 180, 197), new Point(88, 233),
                new Rectangle(306, 96, 82, 20), new Rectangle(306, 120, 82, 20),
                new Rectangle(306, 144, 82, 20), new Rectangle(306, 168, 82, 20));
    }
}
