using Client.Controls;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using SlimDX;
using SlimDX.Direct3D9;
using System.Linq;
using System.Windows.Forms;

namespace Client.Envir
{
    // Presentation only: child GPU resources are owned/disposed by the tooltip; item data and original text/colours stay intact.
    internal sealed class GameUiItemTooltipLayout
    {
        private const int Padding = 6, Margin = 4, ColumnGap = 12, MaximumColumnWidth = 360;
        private readonly DXControl tooltip;
        private readonly Row[] rows;
        private Size viewport;
        internal bool FitsViewport { get; private set; }
        internal bool IsOverflow => overflow != null && overflow.Visible;
        internal int ScrollOffset { get; private set; }
        internal int WheelRemainder { get; private set; }
        internal int MaximumScrollOffset { get; private set; }
        private OverflowViewport overflow;
        private int bodyHeight;
        private System.Drawing.Font FooterFont => rows.OrderBy(x => x.Label.Font.Size).First().Label.Font;
        private sealed class Row
        {
            internal DXLabel Label;
            internal Size NaturalSize;
            internal int Gap;
        }
        internal GameUiItemTooltipLayout(DXControl tooltip)
        {
            this.tooltip = tooltip ?? throw new ArgumentNullException(nameof(tooltip));
            var labels = tooltip.Controls.OfType<DXLabel>().OrderBy(x => x.Location.Y).ThenBy(x => x.Location.X).ToArray();
            rows = new Row[labels.Length];
            for (int i = 0; i < labels.Length; i++)
                rows[i] = new Row { Label = labels[i], NaturalSize = labels[i].Size,
                    Gap = i == 0 ? 0 : Math.Max(1, Math.Min(5, labels[i].Location.Y - labels[i - 1].Location.Y - labels[i - 1].Size.Height)) };
            tooltip.BackColour = Color.FromArgb(232, 12, 12, 12);
            tooltip.BorderColour = Color.FromArgb(104, 96, 72);
        }
        internal void Reflow(Size size)
        {
            if (viewport == size || tooltip.IsDisposed) return;
            viewport = size;
            if (rows.Length == 0) { FitsViewport = true; return; }
            int availableWidth = Math.Max(1, size.Width - 2 * Margin);
            int availableHeight = Math.Max(1, size.Height - 2 * Margin);
            // Normal cards remain compact; overflowing cards get a bounded reading viewport.
            if (overflow != null) overflow.Visible = false;
            foreach (var row in rows) row.Label.Visible = true;
            int maximumColumns = Math.Max(1, availableWidth / 120);
            FitsViewport = false;
            for (int columns = 1; columns <= maximumColumns; columns++)
            {
                int width = Math.Min(MaximumColumnWidth, (availableWidth - (columns - 1) * ColumnGap) / columns);
                if (Arrange(width, availableHeight, columns))
                { FitsViewport = true; ScrollOffset = MaximumScrollOffset = WheelRemainder = 0; return; }
            }
            Arrange(Math.Min(MaximumColumnWidth, availableWidth), int.MaxValue, 1);
            int contentHeight = tooltip.Size.Height - 2 * Padding;
            int footerHeight = DXLabel.GetSize("滚轮上下滚动 · 100%", FooterFont, false).Height + 4;
            bodyHeight = Math.Max(1, availableHeight - 2 * Padding - footerHeight);
            MaximumScrollOffset = Math.Max(0, contentHeight - bodyHeight);
            ScrollOffset = Math.Min(ScrollOffset, MaximumScrollOffset);
            foreach (var row in rows) row.Label.Visible = false;
            int overflowWidth = Math.Min(availableWidth, Math.Max(tooltip.Size.Width,
                DXLabel.GetSize("滚轮上下滚动 · 100%", FooterFont, false).Width + 2 * Padding));
            tooltip.Size = new Size(overflowWidth, Math.Min(availableHeight, bodyHeight + 2 * Padding + footerHeight));
            if (overflow == null) overflow = new OverflowViewport(this) { Parent = tooltip, IsControl = false };
            overflow.Location = new Point(Padding, Padding);
            overflow.Size = new Size(Math.Max(1, tooltip.Size.Width - 2 * Padding), Math.Max(1, tooltip.Size.Height - 2 * Padding));
            overflow.Visible = true;
            overflow.TextureValid = false;
            FitsViewport = tooltip.Size.Width <= availableWidth && tooltip.Size.Height <= availableHeight;
        }
        internal void RestoreScroll(int offset, int remainder)
        {
            if (!IsOverflow) return;
            ScrollOffset = Math.Max(0, Math.Min(offset, MaximumScrollOffset));
            WheelRemainder = remainder;
            overflow.TextureValid = false;
        }
        internal bool Scroll(int delta)
        {
            if (!IsOverflow || tooltip.IsDisposed) return false;
            // Consume even partial notches/end stops so underlying controls never also scroll.
            long total = (long)WheelRemainder + delta;
            long notches = total / SystemInformation.MouseWheelScrollDelta;
            WheelRemainder = (int)(total % SystemInformation.MouseWheelScrollDelta);
            int step = Math.Min(48, Math.Max(1, bodyHeight / 2));
            int next = (int)Math.Max(0L, Math.Min(MaximumScrollOffset, ScrollOffset - notches * step));
            if (next != ScrollOffset) { ScrollOffset = next; overflow.TextureValid = false; }
            return true;
        }

        // One viewport-sized texture, even when a single original label is taller than the GPU limit.
        // Original labels retain their complete text and styling; they are hidden only during overflow.
        private sealed class OverflowViewport : DXControl
        {
            private readonly GameUiItemTooltipLayout layout;
            internal OverflowViewport(GameUiItemTooltipLayout layout) { this.layout = layout; DrawTexture = true; }
            protected override void CreateTexture()
            {
                if (ControlTexture == null || TextureSize != Size)
                {
                    DisposeTexture();
                    TextureSize = Size;
                    ControlTexture = new Texture(DXManager.Device, Size.Width, Size.Height, 1, Usage.None, Format.A8R8G8B8, Pool.Managed);
                    DXManager.ControlList.Add(this);
                }
                DataRectangle data = ControlTexture.LockRectangle(0, LockFlags.Discard);
                try
                {
                    using (var bitmap = new Bitmap(Size.Width, Size.Height, Size.Width * 4, PixelFormat.Format32bppArgb, data.Data.DataPointer))
                    using (var graphics = Graphics.FromImage(bitmap))
                    {
                        DXManager.ConfigureGraphics(graphics);
                        graphics.Clear(Color.Transparent);
                        var body = new Rectangle(0, 0, Size.Width, Math.Min(layout.bodyHeight, Size.Height));
                        graphics.SetClip(body);
                        foreach (var row in layout.rows)
                        {
                            var label = row.Label;
                            int y = label.Location.Y - Padding - layout.ScrollOffset;
                            if (y >= body.Height || y + label.Size.Height <= 0) continue;
                            var bounds = new Rectangle(0, y, label.Size.Width, label.Size.Height);
                            var flags = label.DrawFormat | TextFormatFlags.PreserveGraphicsClipping;
                            if (label.Outline)
                            {
                                DrawText(graphics, label, bounds, 1, 0, label.OutlineColour, flags);
                                DrawText(graphics, label, bounds, 0, 1, label.OutlineColour, flags);
                                DrawText(graphics, label, bounds, 2, 1, label.OutlineColour, flags);
                                DrawText(graphics, label, bounds, 1, 2, label.OutlineColour, flags);
                                DrawText(graphics, label, bounds, 1, 1, label.ForeColour, flags);
                            }
                            else DrawText(graphics, label, bounds, 1, 0, label.ForeColour, flags);
                        }
                        graphics.ResetClip();
                        int percent = layout.MaximumScrollOffset == 0 ? 100 : (int)(100L * layout.ScrollOffset / layout.MaximumScrollOffset);
                        TextRenderer.DrawText(graphics, "滚轮上下滚动 · " + percent + "%", layout.FooterFont,
                            new Rectangle(0, body.Bottom + 2, Size.Width, Math.Max(1, Size.Height - body.Bottom - 2)),
                            Color.FromArgb(198, 166, 99), TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                    }
                }
                finally { ControlTexture.UnlockRectangle(0); data.Data.Dispose(); }
                TextureValid = true;
                ExpireTime = CEnvir.Now + Config.CacheDuration;
            }
            private static void DrawText(Graphics graphics, DXLabel label, Rectangle bounds, int x, int y, Color colour, TextFormatFlags flags)
            { bounds.Offset(x, y); TextRenderer.DrawText(graphics, label.Text, label.Font, bounds, colour, flags); }
        }
        internal Point ClampPosition(Rectangle bounds, Point preferred)
        {
            int left = bounds.Left + Margin, top = bounds.Top + Margin;
            return new Point(Math.Max(left, Math.Min(preferred.X, bounds.Right - Margin - tooltip.Size.Width)),
                Math.Max(top, Math.Min(preferred.Y, bounds.Bottom - Margin - tooltip.Size.Height)));
        }
        private bool Arrange(int width, int maximumHeight, int maximumColumns)
        {
            int naturalWidth = rows.Max(x => x.NaturalSize.Width) + 2 * Padding;
            width = Math.Max(2 * Padding + 4, Math.Min(width, naturalWidth));
            int contentWidth = Math.Max(4, width - 2 * Padding);
            int column = 0, y = Padding, bottom = Padding;
            bool fits = true;
            foreach (Row row in rows)
            {
                var label = row.Label;
                label.AutoSize = false;
                label.DrawFormat |= TextFormatFlags.TextBoxControl;
                Size measured = row.NaturalSize;
                if (measured.Width > contentWidth)
                {
                    // Same GDI renderer, font and flags as DXLabel.CreateTexture. Reserve outline pixels.
                    measured = TextRenderer.MeasureText(DXManager.Graphics, label.Text, label.Font,
                        new Size(Math.Max(1, contentWidth - 2), int.MaxValue), label.DrawFormat);
                    if (label.Outline) { measured.Width += 2; measured.Height += 2; }
                }
                int height = Math.Max(1, measured.Height);
                int gap = y == Padding ? 0 : row.Gap;
                if (y > Padding && y + gap + height + Padding > maximumHeight)
                { column++; y = Padding; gap = 0; }
                label.AutoSize = false;
                label.Size = new Size(Math.Min(contentWidth, Math.Max(1, measured.Width)), height);
                label.Location = new Point(Padding + column * (width + ColumnGap), y + gap);
                y += gap + height;
                bottom = Math.Max(bottom, y);
                if (y + Padding > maximumHeight) fits = false;
            }
            tooltip.Size = new Size((column + 1) * width + column * ColumnGap, bottom + Padding);
            return fits && column < maximumColumns;
        }
    }
}
