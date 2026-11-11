using System;
using System.Drawing;
using System.Linq;
using Client.Controls;

namespace Client.Envir
{
    // Changes the shell and content origin only. The original grid, cells, labels,
    // handlers, links and selection remain owned by the service window.
    internal sealed class GameUiNpcItemServiceBinding : IDisposable
    {
        private readonly DXWindow window;
        private readonly Size originalSize, clientSize;
        private readonly Point clientOrigin;
        private readonly ControlPosition[] content;
        private bool disposed, nativeRestorePending;

        internal GameUiNpcItemServiceBinding(DXWindow window, DXItemGrid grid, Size capacity)
        {
            this.window = window ?? throw new ArgumentNullException(nameof(window));
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (window.IsDisposed || window.WindowSkin != null || window.IsMoving || window.IsResizing ||
                grid.IsDisposed || grid.Parent != window || grid.GridSize != capacity ||
                grid.Grid.Length != checked(capacity.Width * capacity.Height))
                throw new InvalidOperationException("Service binding requires an idle original window and its complete original grid.");
            originalSize = window.Size; clientSize = window.ClientArea.Size; clientOrigin = window.ClientArea.Location;
            content = window.Controls.Where(c => c != window.TitleLabel && c != window.CloseButton)
                .Select(c => new ControlPosition(c)).ToArray();
        }

        internal void Install(GameUiWindowSkin skin)
        {
            if (disposed) throw new ObjectDisposedException(nameof(GameUiNpcItemServiceBinding));
            if (skin == null || skin.FixedSize)
                throw new ArgumentException("Item service skin must preserve the original client size using nine slices.", nameof(skin));
            // Validate constraints before mutating the original window.
            skin.GetWindowSize(clientSize);
            window.WindowSkin = skin;
            window.SetClientSize(clientSize);
            var delta = new Size(window.ClientArea.X - clientOrigin.X, window.ClientArea.Y - clientOrigin.Y);
            foreach (var position in content) position.Apply(delta);
        }

        public void Dispose()
        {
            if (disposed) return;
            if (!window.IsDisposed)
            {
                // A SizeChanged subscriber can throw after assigning Size but before
                // DXWindow updates client/chrome. Finish that notification on retry.
                if (nativeRestorePending) window.OnSizeChanged(window.Size, window.Size);
                nativeRestorePending = true;
                window.WindowSkin = null;
                window.Size = originalSize;
                foreach (var position in content) position.Apply(Size.Empty);
                nativeRestorePending = false;
            }
            disposed = true;
        }

        private sealed class ControlPosition
        {
            private readonly DXControl control;
            private readonly Point original;
            internal ControlPosition(DXControl control) { this.control = control; original = control.Location; }
            internal void Apply(Size delta)
            {
                if (!control.IsDisposed) control.Location = original + delta;
            }
        }
    }
}
