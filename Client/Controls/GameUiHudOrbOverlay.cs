using System.Drawing;
using System.Drawing.Imaging;
using Client.Envir;
using SlimDX.Direct3D9;

namespace Client.Controls
{
    // Official UI[145] is a foreground layer in the same pixel canvas as the orb shell.
    public sealed class GameUiHudOrbOverlay : DXControl
    {
        private Bitmap artwork;

        public GameUiHudOrbOverlay()
        {
            IsControl = false;
            PassThrough = true;
            DrawTexture = true;
            using (var stream = typeof(GameUiHudOrbOverlay).Assembly.GetManifestResourceStream("Client.Resources.GameUi.hud-orb-overlay.png"))
            using (var image = new Bitmap(stream)) artwork = new Bitmap(image);
            Size = artwork.Size;
        }

        protected override void CreateTexture()
        {
            if (ControlTexture == null || TextureSize != Size)
            {
                DisposeTexture();
                TextureSize = Size;
                ControlTexture = new Texture(DXManager.Device, Size.Width, Size.Height, 1, Usage.None, Format.A8R8G8B8, Pool.Managed);
                DXManager.ControlList.Add(this);
            }
            var pixels = ControlTexture.LockRectangle(0, LockFlags.Discard);
            try
            {
                using (var target = new Bitmap(Size.Width, Size.Height, pixels.Pitch, PixelFormat.Format32bppArgb, pixels.Data.DataPointer))
                using (var graphics = Graphics.FromImage(target))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.DrawImageUnscaled(artwork, Point.Empty);
                }
            }
            finally { ControlTexture.UnlockRectangle(0); pixels.Data.Dispose(); }
            TextureValid = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { artwork?.Dispose(); artwork = null; }
            base.Dispose(disposing);
        }
    }
}
