using System;
using System.Drawing;

namespace Client.Envir
{
    // UI[430] keeps its native frame and ten 41x40 slots; native item/link identities are retained.
    public sealed class GameUiBeltLayout
    {
        public Size WindowSize { get; }
        public Rectangle Grid { get; }

        public GameUiBeltLayout(Size windowSize, Rectangle grid)
        {
            if (windowSize.Width <= 0 || windowSize.Height <= 0)
                throw new ArgumentOutOfRangeException(nameof(windowSize));
            if (grid.X < 0 || grid.Y < 0 || grid.Size != new Size(410, 40) ||
                (long)grid.X + grid.Width > windowSize.Width || (long)grid.Y + grid.Height > windowSize.Height)
                throw new ArgumentException("The original ten-slot grid must fit inside the belt window.", nameof(grid));
            WindowSize = windowSize;
            Grid = grid;
        }

        public static GameUiBeltLayout OfficialV1 { get; } = new GameUiBeltLayout(
            new Size(518, 44), new Rectangle(60, 3, 410, 40));
    }
}
