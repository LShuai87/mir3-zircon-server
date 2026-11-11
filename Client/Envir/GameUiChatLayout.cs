using System;
using System.Drawing;
using System.Windows.Forms;
using Client.Controls;
using Client.Scenes.Views;

namespace Client.Envir
{
    // Instance geometry only. Final generated catalog supplies these values after preflight.
    public sealed class GameUiChatViewportLayout
    {
        public Padding Insets { get; }
        public int ScrollBarWidth { get; }
        public int Gap { get; }
        public Size MinimumSize { get; }
        public GameUiChatViewportLayout(Padding insets, int scrollBarWidth, int gap)
        {
            GameUiChatGeometry.ValidateInsets(insets);
            if (scrollBarWidth < 14 || scrollBarWidth > 256 || gap < 0 || gap > 256)
                throw new ArgumentOutOfRangeException(nameof(scrollBarWidth));
            Insets = insets; ScrollBarWidth = scrollBarWidth; Gap = gap;
            // Existing DXVScrollBar divides by height - 50 while dragging its thumb.
            MinimumSize = new Size(insets.Horizontal + scrollBarWidth + gap + 1, insets.Vertical + 51);
        }
        public Size ConstrainSize(Size size) => GameUiChatGeometry.Constrain(size, MinimumSize);
        public void GetBounds(Size size, out Rectangle history, out Rectangle scrollbar)
        {
            if (size != ConstrainSize(size)) throw new ArgumentException("Chat viewport is below its minimum size.", nameof(size));
            int height = size.Height - Insets.Vertical;
            history = new Rectangle(Insets.Left, Insets.Top, size.Width - Insets.Horizontal - ScrollBarWidth - Gap, height);
            scrollbar = new Rectangle(size.Width - Insets.Right - ScrollBarWidth, Insets.Top, ScrollBarWidth, height);
        }
    }

    public sealed class GameUiChatInputLayout
    {
        public Padding Insets { get; }
        public int ModeWidth { get; }
        public int OptionsWidth { get; }
        public int Gap { get; }
        public Size MinimumSize { get; }
        public GameUiChatInputLayout(Padding insets, int modeWidth, int optionsWidth, int gap)
        {
            GameUiChatGeometry.ValidateInsets(insets);
            if (modeWidth <= 0 || modeWidth > 256 || optionsWidth <= 0 || optionsWidth > 256 || gap < 0 || gap > 256)
                throw new ArgumentOutOfRangeException(nameof(modeWidth));
            Insets = insets; ModeWidth = modeWidth; OptionsWidth = optionsWidth; Gap = gap;
            MinimumSize = new Size(insets.Horizontal + modeWidth + optionsWidth + 2 * gap + 1, insets.Vertical + 20);
        }
        public Size ConstrainSize(Size size) => GameUiChatGeometry.Constrain(size, MinimumSize);
        public void GetBounds(Size size, out Rectangle mode, out Rectangle input, out Rectangle options)
        {
            if (size != ConstrainSize(size)) throw new ArgumentException("Chat input is below its minimum size.", nameof(size));
            int height = size.Height - Insets.Vertical;
            mode = new Rectangle(Insets.Left, Insets.Top, ModeWidth, height);
            options = new Rectangle(size.Width - Insets.Right - OptionsWidth, Insets.Top, OptionsWidth, height);
            input = new Rectangle(mode.Right + Gap, Insets.Top, options.Left - Gap - mode.Right - Gap, height);
        }
    }

    internal static class GameUiChatGeometry
    {
        internal static void ValidateInsets(Padding insets)
        {
            if (insets.Left < 0 || insets.Top < 0 || insets.Right < 0 || insets.Bottom < 0 ||
                insets.Left > 256 || insets.Top > 256 || insets.Right > 256 || insets.Bottom > 256)
                throw new ArgumentOutOfRangeException(nameof(insets));
        }
        internal static Size Constrain(Size size, Size minimum)
        {
            return new Size(Math.Max(size.Width, minimum.Width), Math.Max(size.Height, minimum.Height));
        }
    }

    // Retain the top visible message through reflow, or follow the bottom if already there.
    internal sealed class GameUiChatScrollAnchor
    {
        private readonly ChatTab tab;
        private readonly DXLabel topMessage;
        private readonly int withinMessage, originalValue;
        private readonly bool atEnd;
        internal GameUiChatScrollAnchor(ChatTab tab)
        {
            this.tab = tab;
            originalValue = tab.ScrollBar.Value;
            atEnd = originalValue >= Math.Max(tab.ScrollBar.MinValue, tab.ScrollBar.MaxValue - tab.ScrollBar.VisibleSize);
            int y = 0;
            foreach (DXLabel label in tab.History)
            {
                if (y + label.Size.Height > originalValue)
                { topMessage = label; withinMessage = originalValue - y; break; }
                y += label.Size.Height;
            }
        }
        internal void Restore()
        {
            if (tab.IsDisposed) return;
            int value = originalValue;
            if (atEnd) value = Math.Max(tab.ScrollBar.MinValue, tab.ScrollBar.MaxValue - tab.ScrollBar.VisibleSize);
            else if (topMessage != null && !topMessage.IsDisposed)
            {
                int y = 0;
                foreach (DXLabel label in tab.History)
                {
                    if (ReferenceEquals(label, topMessage))
                    { value = y + Math.Min(withinMessage, Math.Max(0, label.Size.Height - 1)); break; }
                    y += label.Size.Height;
                }
            }
            tab.ScrollBar.Value = Math.Max(tab.ScrollBar.MinValue,
                Math.Min(Math.Max(tab.ScrollBar.MinValue, tab.ScrollBar.MaxValue - tab.ScrollBar.VisibleSize), value));
            // Value may be unchanged while wrapping changed each label's vertical position.
            tab.UpdateItems();
        }
    }
}
