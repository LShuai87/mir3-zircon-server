using System;
using System.Drawing;

namespace Client.Envir
{
    // All rectangles are relative to the inventory window, including the original grid.
    public sealed class GameUiInventoryLayout
    {
        public Rectangle Grid { get; }
        public Rectangle WeightCaption { get; }
        public Rectangle Weight { get; }
        public Rectangle GoldCaption { get; }
        public Rectangle Gold { get; }
        public Rectangle Sell { get; }
        public Rectangle Fragment { get; }
        public Rectangle Drops { get; }

        public GameUiInventoryLayout(Rectangle grid, Rectangle weightCaption, Rectangle weight,
            Rectangle goldCaption, Rectangle gold, Rectangle sell, Rectangle fragment, Rectangle drops)
        {
            Grid = grid; WeightCaption = weightCaption; Weight = weight; GoldCaption = goldCaption;
            Gold = gold; Sell = sell; Fragment = fragment; Drops = drops;
            if (grid.Size != new Size(246, 246))
                throw new ArgumentException("The existing 7 by 7 grid requires 246 by 246 pixels.", nameof(grid));
            var bounds = GetBounds();
            for (int i = 0; i < bounds.Length; i++)
            {
                var rect = bounds[i];
                if (rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0 ||
                    (long)rect.X + rect.Width > int.MaxValue || (long)rect.Y + rect.Height > int.MaxValue)
                    throw new ArgumentOutOfRangeException(nameof(grid), "Invalid inventory rectangle.");
                for (int j = 0; j < i; j++)
                    if (rect.IntersectsWith(bounds[j])) throw new ArgumentException("Inventory controls overlap.");
            }
        }

        public Rectangle[] GetBounds() => new[] { Grid, WeightCaption, Weight, GoldCaption, Gold, Sell, Fragment, Drops };

        // Compiled candidate; runtime does not read the JSON build input.
        public static GameUiInventoryLayout CreateOfficialCandidate() => new GameUiInventoryLayout(
            new Rectangle(15, 49, 246, 246),
            new Rectangle(16, 300, 32, 20), new Rectangle(51, 306, 124, 9),
            new Rectangle(16, 326, 32, 20), new Rectangle(51, 326, 209, 20),
            new Rectangle(16, 365, 78, 22), new Rectangle(99, 365, 78, 22), new Rectangle(182, 365, 78, 22));
    }
}
