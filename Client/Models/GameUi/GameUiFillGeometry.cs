using System;
using System.Drawing;

namespace Client.Models.GameUi
{
    public struct GameUiFillRegion
    {
        public Rectangle Source { get; }
        public Rectangle Destination { get; }
        public bool IsEmpty => Source.Height == 0;

        internal GameUiFillRegion(Rectangle source, Rectangle destination)
        {
            Source = source;
            Destination = destination;
        }
    }

    public static class GameUiFillGeometry
    {
        // Logical pixels, drawn 1:1. The returned geometry never changes the displayed number.
        // Partial pixels round down; both rectangles move by the same amount from the top.
        public static GameUiFillRegion Bottom(Rectangle source, Point destination, long current, long maximum)
        {
            if (source.X < 0 || source.Y < 0 || source.Width <= 0 || source.Height <= 0)
                throw new ArgumentOutOfRangeException(nameof(source));
            if ((long)source.X + source.Width > int.MaxValue || (long)source.Y + source.Height > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(source));
            if ((long)destination.X + source.Width > int.MaxValue || (long)destination.Y + source.Height > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(destination));

            int height = maximum <= 0 || current <= 0 ? 0 : current >= maximum ? source.Height :
                (int)decimal.Floor((decimal)source.Height * current / maximum);
            if (height == 0)
                return new GameUiFillRegion(Rectangle.Empty, Rectangle.Empty);
            int top = source.Height - height;
            return new GameUiFillRegion(
                new Rectangle(source.X, source.Y + top, source.Width, height),
                new Rectangle(destination.X, destination.Y + top, source.Width, height));
        }
    }
}
