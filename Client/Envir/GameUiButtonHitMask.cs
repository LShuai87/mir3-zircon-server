using System;
using System.Drawing;

namespace Client.Envir
{
    // Immutable CPU copy of a normal frame's binary alpha. No textures or file IO on input.
    public sealed class GameUiButtonHitMask
    {
        private readonly bool[] pixels;
        public Size Size { get; }

        private GameUiButtonHitMask(Size size, bool[] pixels)
        {
            Size = size;
            this.pixels = pixels;
        }

        public bool Contains(Point point) => point.X >= 0 && point.Y >= 0 &&
            point.X < Size.Width && point.Y < Size.Height && pixels[point.Y * Size.Width + point.X];

        public static GameUiButtonHitMask FromBc1(Size size, byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (size.Width <= 0 || size.Height <= 0) throw new ArgumentOutOfRangeException(nameof(size));
            long blocksWide = ((long)size.Width + 3) / 4;
            long blocksHigh = ((long)size.Height + 3) / 4;
            long length = blocksWide * blocksHigh * 8;
            long pixelCount = (long)size.Width * size.Height;
            if (length != payload.Length || pixelCount > int.MaxValue)
                throw new ArgumentException("BC1 payload must contain exactly the logical frame's padded blocks.", nameof(payload));
            var pixels = new bool[(int)pixelCount];
            for (int y = 0; y < size.Height; y++)
                for (int x = 0; x < size.Width; x++)
                {
                    int block = (int)(((y / 4) * blocksWide + x / 4) * 8);
                    int first = payload[block] | payload[block + 1] << 8;
                    int second = payload[block + 2] | payload[block + 3] << 8;
                    int selector = payload[block + 4 + y % 4] >> ((x % 4) * 2) & 3;
                    // Equal black endpoints still have opaque selectors 0..2.
                    pixels[y * size.Width + x] = first > second || selector != 3;
                }
            return new GameUiButtonHitMask(size, pixels);
        }
    }
}
