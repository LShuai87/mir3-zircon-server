using System;
using System.Drawing;

namespace Client.Envir
{
    // All rectangles are relative to the window in logical pixels.
    public sealed class GameUiMiniMapLayout
    {
        public Size WindowSize { get; }
        public Rectangle Viewport { get; }
        public Rectangle Coordinates { get; }
        public Rectangle Collapse { get; }
        public Rectangle BigMap => new Rectangle(4, Coordinates.Y, 20, 16);
        public Rectangle Transparency => new Rectangle(24, Coordinates.Y, 20, 16);
        public Rectangle Zoom => new Rectangle(WindowSize.Width - 20, Coordinates.Y, 20, 16);

        public Rectangle FooterBounds(Rectangle area, bool collapsed)
            => collapsed ? new Rectangle(area.X, area.Y - Viewport.Height, area.Width, area.Height) : area;

        // The official magnifier enlarges the viewport, retaining native map pixels and markers.
        public GameUiMiniMapLayout CreateEnlarged() => new GameUiMiniMapLayout(
            new Size(WindowSize.Width + 150, WindowSize.Height + 150),
            new Rectangle(Viewport.X, Viewport.Y, Viewport.Width + 150, Viewport.Height + 150),
            new Rectangle(Coordinates.X + 75, Coordinates.Y + 150, Coordinates.Width, Coordinates.Height),
            new Rectangle(Collapse.X + 150, Collapse.Y, Collapse.Width, Collapse.Height));
        public Rectangle Safety { get; }
        public Rectangle Title { get; }
        public Size CollapsedSize => new Size(WindowSize.Width, WindowSize.Height - Viewport.Height);
        public Rectangle CollapsedCoordinates => new Rectangle(Coordinates.X, Coordinates.Y - Viewport.Height, Coordinates.Width, Coordinates.Height);

        public GameUiMiniMapLayout(Size windowSize, Rectangle viewport, Rectangle coordinates, Rectangle collapse)
        {
            if (windowSize.Width <= 0 || windowSize.Height <= 0)
                throw new ArgumentOutOfRangeException(nameof(windowSize));
            var safety = new Rectangle(4, collapse.Y, 28, collapse.Height);
            var title = new Rectangle(safety.Right + 2, collapse.Y, collapse.Left - safety.Right - 4, collapse.Height);
            var areas = new[] { viewport, coordinates, collapse, safety, title,
                new Rectangle(4, coordinates.Y, 20, 16), new Rectangle(24, coordinates.Y, 20, 16),
                new Rectangle(windowSize.Width - 20, coordinates.Y, 20, 16) };
            for (int i = 0; i < areas.Length; i++)
            {
                var area = areas[i];
                if (area.X < 0 || area.Y < 0 || area.Width <= 0 || area.Height <= 0 ||
                    (long)area.X + area.Width > windowSize.Width || (long)area.Y + area.Height > windowSize.Height)
                    throw new ArgumentException("Every minimap region must fit inside the window.");
                for (int j = 0; j < i; j++)
                    if (area.IntersectsWith(areas[j])) throw new ArgumentException("Minimap regions must not overlap.");
            }
            WindowSize = windowSize;
            Viewport = viewport;
            Coordinates = coordinates;
            Collapse = collapse;
            Safety = safety;
            Title = title;
            if (coordinates.Top < viewport.Bottom || collapse.Bottom > viewport.Top || safety.Bottom > viewport.Top)
                throw new ArgumentException("The minimap header and footer must sit outside the collapsible viewport.");
        }

        public static GameUiMiniMapLayout OfficialV1 { get; } = new GameUiMiniMapLayout(
            new Size(166, 198), new Rectangle(11, 21, 150, 152),
            new Rectangle(44, 177, 74, 16), new Rectangle(146, 2, 20, 16));
    }
}
