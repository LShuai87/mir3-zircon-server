using System;
using System.Drawing;
using Client.Controls;
using Client.Scenes.Views;

namespace Client.Envir
{
    // Keeps original result cells, scrolling, selection and retrieval handlers.
    internal sealed class GameUiNpcRefineRetrieveBinding : IDisposable
    {
        private readonly NPCRefineRetrieveDialog window;
        private readonly DXVScrollBar scroll;
        private readonly DXButton retrieve;
        private readonly Action restoreCurrentLayout;
        private readonly Size originalSize, scrollSize;
        private readonly Point scrollLocation, buttonLocation;
        private readonly int originalMax, originalVisible, originalValue;
        private bool refreshed, disposed, nativeRestorePending;

        internal GameUiNpcRefineRetrieveBinding(NPCRefineRetrieveDialog window, DXVScrollBar scroll,
            DXButton retrieve, Action restoreCurrentLayout)
        {
            this.window = window; this.scroll = scroll; this.retrieve = retrieve;
            this.restoreCurrentLayout = restoreCurrentLayout;
            originalSize = window.Size; scrollSize = scroll.Size;
            scrollLocation = scroll.Location; buttonLocation = retrieve.Location;
            originalMax = scroll.MaxValue; originalVisible = scroll.VisibleSize; originalValue = scroll.Value;
        }
        internal void Install(GameUiWindowSkin skin)
        {
            if (disposed) throw new ObjectDisposedException(nameof(GameUiNpcRefineRetrieveBinding));
            if (skin == null || skin.FixedSize)
                throw new ArgumentException("Result window requires a variable size skin.", nameof(skin));
            var client = window.ClientArea.Size;
            window.WindowSkin = skin;
            window.SetClientSize(client);
            LocateChrome();
        }
        internal void Refresh()
        {
            refreshed = true;
            scroll.MaxValue = window.Refines.Count * 43 - 2;
            window.SetClientSize(new Size(491, Math.Min(Math.Max(3 * 43 - 2, scroll.MaxValue), 7 * 43 - 3) + 1));
            scroll.VisibleSize = window.ClientArea.Height;
            scroll.Size = new Size(scroll.Size.Width, window.ClientArea.Height - 2);
            LocateChrome();
            scroll.Value = 0;
            for (int i = 0; i < window.Cells.Count; i++) window.Cells[i].Location = new Point(1, i * 43 + 1);
        }
        private void LocateChrome()
        {
            scroll.Location = new Point(window.ClientArea.Right - scroll.Size.Width - 2, window.ClientArea.Top + 1);
            retrieve.Location = new Point((window.Size.Width - retrieve.Size.Width) / 2,
                window.ClientArea.Bottom + (window.WindowSkin.ClientPadding.Bottom - retrieve.Size.Height) / 2);
        }
        public void Dispose()
        {
            if (disposed) return;
            if (!window.IsDisposed)
            {
                int value = scroll.Value;
                if (nativeRestorePending) window.OnSizeChanged(window.Size, window.Size);
                nativeRestorePending = true;
                window.WindowSkin = null;
                window.Size = originalSize;
                scroll.Location = scrollLocation; scroll.Size = scrollSize; retrieve.Location = buttonLocation;
                if (refreshed)
                {
                    restoreCurrentLayout();
                    scroll.Value = value;
                }
                else
                {
                    scroll.MaxValue = originalMax; scroll.VisibleSize = originalVisible;
                    scroll.Value = originalValue;
                }
                nativeRestorePending = false;
            }
            disposed = true;
        }
    }
}
