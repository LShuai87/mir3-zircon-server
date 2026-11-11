using System;
using System.Drawing;

namespace Client.Envir
{
    // Window-local geometry. Visible rows do not define storage capacity.
    public sealed class GameUiStorageLayout
    {
        public Rectangle Filter { get; }
        public Rectangle Grid { get; }
        public Rectangle Scrollbar { get; }

        public GameUiStorageLayout(Rectangle filter, Rectangle grid, Rectangle scrollbar)
        {
            Filter = filter; Grid = grid; Scrollbar = scrollbar;
            var bounds = GetBounds();
            for (int i = 0; i < bounds.Length; i++)
            {
                var rect = bounds[i];
                if (rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0 ||
                    (long)rect.X + rect.Width > int.MaxValue || (long)rect.Y + rect.Height > int.MaxValue)
                    throw new ArgumentOutOfRangeException(nameof(filter), "Invalid storage rectangle.");
                for (int j = 0; j < i; j++)
                    if (rect.IntersectsWith(bounds[j])) throw new ArgumentException("Storage controls overlap.");
            }
            if (filter.Size != new Size(473, 26) || grid.Size != new Size(456, 351) || scrollbar.Size != new Size(14, 349))
                throw new ArgumentException("Storage requires the original filter, 13-column grid and scrollbar sizes.");
        }

        public Rectangle[] GetBounds() => new[] { Filter, Grid, Scrollbar };

        public static GameUiStorageLayout CreateOfficialCandidate() => new GameUiStorageLayout(
            new Rectangle(16, 48, 473, 26), new Rectangle(16, 78, 456, 351), new Rectangle(478, 79, 14, 349));
    }
}
