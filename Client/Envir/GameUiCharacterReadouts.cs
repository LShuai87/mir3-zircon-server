using System;
using System.Drawing;
using System.Windows.Forms;
using Client.Controls;
using Client.Scenes.Views;

namespace Client.Envir
{
    // Coordinates are local to the character tab. The compiled catalog supplies the final rectangles.
    public sealed class GameUiCharacterReadoutLayout
    {
        public Rectangle Level { get; }
        public Rectangle Experience { get; }
        public Rectangle Health { get; }
        public Rectangle Mana { get; }

        public GameUiCharacterReadoutLayout(Rectangle level, Rectangle experience, Rectangle health, Rectangle mana)
        {
            var bounds = new[] { level, experience, health, mana };
            for (int i = 0; i < bounds.Length; i++)
            {
                Rectangle rect = bounds[i];
                if (rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0 ||
                    (long)rect.X + rect.Width > 16384 || (long)rect.Y + rect.Height > 16384)
                    throw new ArgumentOutOfRangeException(nameof(level), "Readout rectangles must be positive client-pixel rectangles within 16384 pixels.");
                for (int j = 0; j < i; j++)
                    if (rect.IntersectsWith(bounds[j]))
                        throw new ArgumentException("Character readout rectangles overlap.");
            }
            Level = level; Experience = experience; Health = health; Mana = mana;
        }

        internal Rectangle[] GetBounds(Size parentSize)
        {
            var bounds = new[] { Level, Experience, Health, Mana };
            var client = new Rectangle(Point.Empty, parentSize);
            foreach (Rectangle rect in bounds)
                if (!client.Contains(rect))
                    throw new ArgumentException("Character readout rectangles exceed the character tab.");
            return bounds;
        }
    }

    // Mirrors the original HUD strings. Existing GameScene/UserObject update and formatting paths remain authoritative.
    public sealed class GameUiCharacterReadouts : IDisposable
    {
        private readonly DXControl parent;
        private readonly MainPanel source;
        private readonly DXLabel[] sourceLabels;
        private readonly DXControl[] observed;
        private readonly DXLabel[] labels = new DXLabel[4];
        public GameUiCharacterReadoutLayout Layout { get; private set; }
        public DXLabel LevelLabel => labels[0];
        public DXLabel ExperienceLabel => labels[1];
        public DXLabel HealthLabel => labels[2];
        public DXLabel ManaLabel => labels[3];
        public bool IsDisposed { get; private set; }
        internal event EventHandler Disposed;

        internal GameUiCharacterReadouts(DXControl parent, MainPanel source, GameUiCharacterReadoutLayout layout)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (parent.IsDisposed || source.IsDisposed) throw new ObjectDisposedException("Character readout owner");
            Rectangle[] bounds = layout.GetBounds(parent.Size);
            sourceLabels = new[] { source.LevelLabel, source.ExperienceLabel, source.HealthLabel, source.ManaLabel };
            observed = new DXControl[] { sourceLabels[0], sourceLabels[1], sourceLabels[2], sourceLabels[3], source.ExperienceBar };
            foreach (DXControl control in observed)
                if (control == null || control.IsDisposed)
                    throw new ArgumentException("Original HUD readout controls must be present and alive.", nameof(source));
            this.parent = parent; this.source = source; Layout = layout;
            try
            {
                for (int i = 0; i < labels.Length; i++)
                {
                    // Register ownership before setting properties so partial construction can be cleaned up.
                    labels[i] = new DXLabel();
                    labels[i].AutoSize = false;
                    labels[i].DrawFormat = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                                           TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
                    labels[i].Location = bounds[i].Location;
                    labels[i].Size = bounds[i].Size;
                    labels[i].Parent = parent;
                }
                foreach (DXControl control in observed)
                {
                    control.TextChanged += Refresh;
                    control.HintChanged += Refresh;
                    control.Disposing += OwnerDisposing;
                }
                parent.Disposing += OwnerDisposing;
                source.Disposing += OwnerDisposing;
                Refresh(this, EventArgs.Empty);
            }
            catch { Dispose(); throw; }
        }

        internal void Relocate(GameUiCharacterReadoutLayout layout)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(GameUiCharacterReadouts));
            Rectangle[] bounds = layout.GetBounds(parent.Size);
            for (int i = 0; i < labels.Length; i++)
            {
                labels[i].Location = bounds[i].Location;
                labels[i].Size = bounds[i].Size;
            }
            Layout = layout;
        }

        private void Refresh(object sender, EventArgs e)
        {
            for (int i = 0; i < labels.Length && !IsDisposed; i++)
            {
                labels[i].Text = sourceLabels[i].Text;
                if (IsDisposed) return;
                // Experience retains the existing raw current/max tooltip, including the no-next-level branch.
                labels[i].Hint = i == 1 ? source.ExperienceBar.Hint : sourceLabels[i].Hint ?? sourceLabels[i].Text;
            }
        }

        private void OwnerDisposing(object sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            foreach (DXControl control in observed)
            {
                control.TextChanged -= Refresh;
                control.HintChanged -= Refresh;
                control.Disposing -= OwnerDisposing;
            }
            parent.Disposing -= OwnerDisposing;
            source.Disposing -= OwnerDisposing;
            foreach (DXLabel label in labels)
                if (label != null && !label.IsDisposed) label.Dispose();
            Disposed?.Invoke(this, EventArgs.Empty);
            Disposed = null;
        }
    }
}
