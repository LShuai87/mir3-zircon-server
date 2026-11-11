using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Client.Controls;
using Client.Scenes.Views;

namespace Client.Envir
{
    // Borrows every original quest/reward control and restores it on detach.
    internal sealed class GameUiNpcQuestBinding : IDisposable
    {
        private readonly NPCQuestDialog window;
        private readonly Size originalSize;
        private readonly ControlState[] content;
        private ScrollText description, tasks;
        private bool disposed, nativeRestorePending;

        internal GameUiNpcQuestBinding(NPCQuestDialog window)
        {
            this.window = window ?? throw new ArgumentNullException(nameof(window));
            if (window.IsDisposed || window.WindowSkin != null || window.IsMoving || window.IsResizing ||
                window.RewardGrid.Grid.Length != 5 || window.ChoiceGrid.Grid.Length != 3)
                throw new InvalidOperationException("Quest binding requires the original idle quest window and reward grids.");
            originalSize = window.Size;
            content = window.Controls.Where(c => c != window.TitleLabel && c != window.CloseButton)
                .Select(c => new ControlState(c)).ToArray();
        }

        internal void Install(GameUiWindowSkin skin)
        {
            if (disposed) throw new ObjectDisposedException(nameof(GameUiNpcQuestBinding));
            if (skin == null || skin.FixedSize) throw new ArgumentException("Quest window requires a variable size skin.", nameof(skin));
            var origin = window.ClientArea.Location;
            var client = window.ClientArea.Size;
            // Eight native 36px cells and their original inter-grid spacing need 306px.
            client.Width = Math.Max(320, client.Width);
            skin.GetWindowSize(client);
            window.WindowSkin = skin;
            window.SetClientSize(client);
            var delta = new Size(window.ClientArea.X - origin.X, window.ClientArea.Y - origin.Y);
            foreach (var state in content) state.Move(delta);
            description = new ScrollText(window.DescriptionLabel);
            description.Install(client.Width - 4);
            tasks = new ScrollText(window.TasksLabel);
            tasks.Install(client.Width - 4);
        }

        public void Dispose()
        {
            if (disposed) return;
            // Reparent borrowed labels before disposing their temporary clipping panels.
            tasks?.Dispose(); tasks = null;
            description?.Dispose(); description = null;
            if (!window.IsDisposed)
            {
                if (nativeRestorePending) window.OnSizeChanged(window.Size, window.Size);
                nativeRestorePending = true;
                window.WindowSkin = null;
                window.Size = originalSize;
                foreach (var state in content) state.Restore();
                nativeRestorePending = false;
            }
            disposed = true;
        }

        private sealed class ControlState
        {
            private readonly DXControl control;
            private readonly Point location;
            internal ControlState(DXControl control) { this.control = control; location = control.Location; }
            internal void Move(Size delta) { if (!control.IsDisposed) control.Location = location + delta; }
            internal void Restore() { if (!control.IsDisposed) control.Location = location; }
        }

        private sealed class ScrollText : IDisposable
        {
            private readonly DXLabel label;
            private readonly DXControl parent;
            private readonly Point location;
            private readonly Size size;
            private readonly bool border;
            private DXControl viewport;
            private DXVScrollBar scroll;
            private bool disposed;

            internal ScrollText(DXLabel label)
            {
                this.label = label; parent = label.Parent; location = label.Location;
                size = label.Size; border = label.Border;
            }
            internal void Install(int width)
            {
                viewport = new DXControl { Parent = parent, Location = location,
                    Size = new Size(width, size.Height), DrawTexture = true,
                    Border = border, BorderColour = label.BorderColour };
                scroll = new DXVScrollBar { Parent = viewport, Location = new Point(width - 15, 1),
                    Size = new Size(14, size.Height - 2), VisibleSize = size.Height - 2, Change = 16 };
                label.Parent = viewport; label.Border = false;
                label.TextChanged += Refresh; label.FontChanged += Refresh;
                scroll.ValueChanged += MoveLabel;
                viewport.MouseWheel += Wheel; label.MouseWheel += Wheel;
                Refresh(this, EventArgs.Empty);
            }
            private void Refresh(object sender, EventArgs e)
            {
                if (disposed || label.IsDisposed) return;
                int width = viewport.Size.Width - scroll.Size.Width - 3;
                // DXLabel.GetHeight caps at 2000px; measure all wrapped quest text instead.
                var measured = TextRenderer.MeasureText(DXManager.Graphics, label.Text ?? string.Empty,
                    label.Font, new Size(width, int.MaxValue), label.DrawFormat);
                label.Size = new Size(width, Math.Max(scroll.VisibleSize, measured.Height + (label.Outline ? 2 : 0)));
                scroll.MaxValue = label.Size.Height;
                scroll.Value = 0;
                MoveLabel(this, EventArgs.Empty);
            }
            private void MoveLabel(object sender, EventArgs e) { label.Location = new Point(1, 1 - scroll.Value); }
            private void Wheel(object sender, MouseEventArgs e) { scroll.Value -= e.Delta / 120 * scroll.Change; }
            public void Dispose()
            {
                if (disposed) return;
                label.TextChanged -= Refresh; label.FontChanged -= Refresh; label.MouseWheel -= Wheel;
                if (viewport != null) viewport.MouseWheel -= Wheel;
                if (scroll != null) scroll.ValueChanged -= MoveLabel;
                if (!label.IsDisposed)
                {
                    label.Parent = parent; label.Location = location;
                    label.Size = size; label.Border = border;
                }
                viewport?.Dispose();
                disposed = true;
            }
        }
    }
}
