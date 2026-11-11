using System;
using System.Drawing;
using Client.Envir;
using Client.Models;
using Library;
using SlimDX;

namespace Client.Controls
{
    public enum GameUiHudFillDirection { Bottom, Left }

    // One-to-one atlas pixels. The parent carries the original tooltip/input surface.
    public sealed class GameUiHudFill : DXImageControl
    {
        private readonly Func<decimal> readFraction;
        public GameUiHudFillDirection Direction { get; }
        public GameUiHudFill(GameUiHudFillDirection direction, Func<decimal> readFraction)
        {
            if (direction != GameUiHudFillDirection.Bottom && direction != GameUiHudFillDirection.Left) throw new ArgumentOutOfRangeException(nameof(direction));
            this.readFraction = readFraction ?? throw new ArgumentNullException(nameof(readFraction));
            Direction = direction; FixedSize = true; IsControl = false; PixelDetect = false; UseOffSet = false;
        }
        public decimal Fraction => Math.Max(0M, Math.Min(1M, readFraction()));
        public Rectangle FilledSource
        {
            get
            {
                decimal fraction = Fraction;
                int extent = (int)decimal.Floor((Direction == GameUiHudFillDirection.Bottom ? Size.Height : Size.Width) * fraction);
                if (extent <= 0) return Rectangle.Empty;
                return Direction == GameUiHudFillDirection.Bottom ? new Rectangle(0, Size.Height - extent, Size.Width, extent) : new Rectangle(0, 0, extent, Size.Height);
            }
        }
        public override void BindImage(MirLibrary library, int index)
        {
            if (library != null && library.GetSize(index) != Size) throw new ArgumentException("HUD fill frame must match its logical size without scaling.");
            base.BindImage(library, index);
        }
        protected override void DrawMirTexture()
        {
            if (Library == null || Index < 0 || Library.GetSize(Index) != Size) return;
            Rectangle source = FilledSource;
            if (source.IsEmpty) return;
            MirImage image = Library.CreateImage(Index, ImageType.Image);
            if (image?.Image == null) return;
            var destination = new Rectangle(DisplayArea.X + source.X, DisplayArea.Y + source.Y, source.Width, source.Height);
            Rectangle clipped = Rectangle.Intersect(ActiveScene.DisplayArea, destination);
            for (DXControl parent = Parent; parent != null && !clipped.IsEmpty; parent = parent.Parent)
                clipped = Rectangle.Intersect(clipped, parent.DisplayArea);
            if (clipped.IsEmpty) return;
            source = new Rectangle(source.X + clipped.X - destination.X, source.Y + clipped.Y - destination.Y, clipped.Width, clipped.Height);
            float oldOpacity = DXManager.Opacity;
            bool oldBlend = DXManager.Blending; float oldRate = DXManager.BlendRate;
            try
            {
                if (Blend) DXManager.SetBlend(true, ImageOpacity);
                else DXManager.SetOpacity(ImageOpacity);
                DXManager.Sprite.Draw(image.Image, source, Vector3.Zero, new Vector3(clipped.X, clipped.Y, 0), IsEnabled ? ForeColour : Color.FromArgb(75, 75, 75));
            }
            finally { DXManager.SetBlend(oldBlend, oldRate); DXManager.SetOpacity(oldOpacity); }
            image.ExpireTime = Time.Now + Config.CacheDuration;
        }
    }
}
