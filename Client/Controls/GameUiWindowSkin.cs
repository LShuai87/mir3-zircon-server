using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Client.Envir;

namespace Client.Controls
{
    public struct GameUiSkinSlice
    {
        public Rectangle Source { get; }
        public Rectangle Destination { get; }
        public GameUiSkinSlice(Rectangle source, Rectangle destination)
        {
            Source = source;
            Destination = destination;
        }
    }

    // Coordinates are logical pixels. Source offsets are intentionally not applied:
    // the build catalog places the complete frame, including decoration, at (0, 0).
    public sealed class GameUiWindowSkin
    {
        public MirLibrary Library { get; }
        public int FrameIndex { get; }
        public Size FrameSize { get; }
        public Padding ClientPadding { get; }
        public Padding SlicePadding { get; }
        public Padding InputInsets { get; }
        public Size MinimumSize { get; }
        public bool FixedSize { get; }
        public Rectangle TitleArea { get; }
        public Rectangle DragArea { get; }
        public Rectangle CloseArea { get; }
        public ButtonImageStates CloseImages { get; }
        public Rectangle FixedFooterCenter { get; }

        public GameUiWindowSkin(MirLibrary library, int frameIndex, Padding clientPadding,
            Padding slicePadding, Padding inputInsets, Size minimumSize, bool fixedSize,
            Rectangle titleArea, Rectangle dragArea, Rectangle closeArea, ButtonImageStates closeImages = null,
            Rectangle fixedFooterCenter = default(Rectangle))
        {
            if (library == null) throw new ArgumentNullException(nameof(library));
            Size frame = library.GetSize(frameIndex);
            if (frame.Width <= 0 || frame.Height <= 0) throw new ArgumentException("Missing window frame.", nameof(frameIndex));
            ValidatePadding(clientPadding, frame, nameof(clientPadding));
            ValidatePadding(slicePadding, frame, nameof(slicePadding));
            ValidatePadding(inputInsets, frame, nameof(inputInsets));
            ValidateRegion(titleArea, frame, nameof(titleArea));
            ValidateRegion(dragArea, frame, nameof(dragArea));
            ValidateRegion(closeArea, frame, nameof(closeArea));
            if (// A collapsed window may have no centre slice, while client/input regions remain nonempty.
                minimumSize.Width <= Math.Max(clientPadding.Horizontal, inputInsets.Horizontal) ||
                minimumSize.Height <= Math.Max(clientPadding.Vertical, inputInsets.Vertical) ||
                minimumSize.Width < slicePadding.Horizontal || minimumSize.Height < slicePadding.Vertical ||
                minimumSize.Width > frame.Width || minimumSize.Height > frame.Height)
                throw new ArgumentException("Minimum size must preserve client, slice and input regions and fit the source frame.", nameof(minimumSize));
            // These regions retain their height and horizontal margins when resized.
            foreach (Rectangle area in new[] { titleArea, dragArea })
                if (!area.IsEmpty && (minimumSize.Width <= frame.Width - area.Width || minimumSize.Height < area.Bottom))
                    throw new ArgumentException("Minimum size cannot clip a title or drag region.", nameof(minimumSize));
            if (!closeArea.IsEmpty && (minimumSize.Width < frame.Width - closeArea.Left || minimumSize.Height < closeArea.Bottom))
                throw new ArgumentException("Minimum size cannot clip the close region.", nameof(minimumSize));
            if (closeImages != null)
            {
                if (closeArea.IsEmpty) throw new ArgumentException("Close images require a close region.", nameof(closeArea));
                foreach (int index in new[] { closeImages.Normal, closeImages.Hover, closeImages.Pressed, closeImages.Disabled })
                    if (library.GetSize(index) != closeArea.Size)
                        throw new ArgumentException("Close image dimensions must match the close region.", nameof(closeImages));
            }
            if (!fixedFooterCenter.IsEmpty && (fixedSize || fixedFooterCenter.Left != slicePadding.Left ||
                fixedFooterCenter.Top != frame.Height - slicePadding.Bottom || fixedFooterCenter.Bottom != frame.Height ||
                fixedFooterCenter.Width <= 0 || fixedFooterCenter.Right >= frame.Width - slicePadding.Right))
                throw new ArgumentException("A fixed footer centre must leave a stretchable band before the right edge.", nameof(fixedFooterCenter));
            FixedFooterCenter = fixedFooterCenter;
            Library = library;
            FrameIndex = frameIndex;
            FrameSize = frame;
            ClientPadding = clientPadding;
            SlicePadding = slicePadding;
            InputInsets = inputInsets;
            MinimumSize = fixedSize ? frame : minimumSize;
            FixedSize = fixedSize;
            TitleArea = titleArea;
            DragArea = dragArea;
            CloseArea = closeArea;
            CloseImages = closeImages;
        }

        private static void ValidatePadding(Padding padding, Size size, string name)
        {
            if (padding.Left < 0 || padding.Top < 0 || padding.Right < 0 || padding.Bottom < 0 ||
                (long)padding.Left + padding.Right >= size.Width || (long)padding.Top + padding.Bottom >= size.Height)
                throw new ArgumentException("Insets must leave a nonempty rectangle.", name);
        }
        private static void ValidateRegion(Rectangle area, Size size, string name)
        {
            if (area == Rectangle.Empty) return;
            if (area.X < 0 || area.Y < 0 || area.Width <= 0 || area.Height <= 0 ||
                (long)area.X + area.Width > size.Width || (long)area.Y + area.Height > size.Height)
                throw new ArgumentException("Region must be inside the source frame.", name);
        }
        public Size ConstrainSize(Size size)
        {
            return FixedSize ? FrameSize : new Size(Math.Max(size.Width, MinimumSize.Width), Math.Max(size.Height, MinimumSize.Height));
        }
        public Size GetWindowSize(Size clientSize)
        {
            if (clientSize.Width < 0 || clientSize.Height < 0) throw new ArgumentOutOfRangeException(nameof(clientSize));
            Size size = new Size(checked(clientSize.Width + ClientPadding.Horizontal), checked(clientSize.Height + ClientPadding.Vertical));
            if (size != ConstrainSize(size)) throw new ArgumentException("Client size does not match skin constraints.", nameof(clientSize));
            return size;
        }
        public Rectangle GetClientArea(Size size) => Inset(size, ClientPadding);
        public Rectangle GetInputArea(Size size) => Inset(size, InputInsets);
        private static Rectangle Inset(Size size, Padding padding)
        {
            return new Rectangle(padding.Left, padding.Top, Math.Max(0, size.Width - padding.Horizontal), Math.Max(0, size.Height - padding.Vertical));
        }
        public Rectangle GetTitleArea(Size size) => StretchHeader(TitleArea, size);
        public Rectangle GetDragArea(Size size) => StretchHeader(DragArea, size);
        private Rectangle StretchHeader(Rectangle area, Size size)
        {
            return area.IsEmpty ? Rectangle.Empty : new Rectangle(area.X, area.Y, checked(area.Width + size.Width - FrameSize.Width), area.Height);
        }
        public Rectangle GetCloseArea(Size size)
        {
            return CloseArea.IsEmpty ? Rectangle.Empty : new Rectangle(checked(CloseArea.X + size.Width - FrameSize.Width), CloseArea.Y, CloseArea.Width, CloseArea.Height);
        }
        public GameUiSkinSlice[] GetSlices(Size size)
        {
            if (size != ConstrainSize(size)) throw new ArgumentException("Window size violates skin constraints.", nameof(size));
            if (FixedSize) return new[] { new GameUiSkinSlice(new Rectangle(Point.Empty, FrameSize), new Rectangle(Point.Empty, size)) };
            int[] sx = { 0, SlicePadding.Left, FrameSize.Width - SlicePadding.Right, FrameSize.Width };
            int[] sy = { 0, SlicePadding.Top, FrameSize.Height - SlicePadding.Bottom, FrameSize.Height };
            int[] dx = { 0, SlicePadding.Left, size.Width - SlicePadding.Right, size.Width };
            int[] dy = { 0, SlicePadding.Top, size.Height - SlicePadding.Bottom, size.Height };
            var slices = new List<GameUiSkinSlice>(9);
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                {
                    // Keep a coordinate well at native width while growing its surrounding footer.
                    if (y == 2 && x == 1 && !FixedFooterCenter.IsEmpty)
                    {
                        int growth = size.Width - FrameSize.Width;
                        int centreLeft = FixedFooterCenter.Left + growth / 2;
                        var filler = Rectangle.FromLTRB(FixedFooterCenter.Right, sy[2], sx[2], sy[3]);
                        if (centreLeft > dx[1]) slices.Add(new GameUiSkinSlice(filler,
                            Rectangle.FromLTRB(dx[1], dy[2], centreLeft, dy[3])));
                        slices.Add(new GameUiSkinSlice(FixedFooterCenter,
                            new Rectangle(centreLeft, dy[2], FixedFooterCenter.Width, dy[3] - dy[2])));
                        slices.Add(new GameUiSkinSlice(filler, Rectangle.FromLTRB(
                            centreLeft + FixedFooterCenter.Width, dy[2], dx[2], dy[3])));
                        continue;
                    }
                    var source = Rectangle.FromLTRB(sx[x], sy[y], sx[x + 1], sy[y + 1]);
                    var destination = Rectangle.FromLTRB(dx[x], dy[y], dx[x + 1], dy[y + 1]);
                    if (source.Width > 0 && source.Height > 0 && destination.Width > 0 && destination.Height > 0)
                        slices.Add(new GameUiSkinSlice(source, destination));
                }
            return slices.ToArray();
        }
    }
}
