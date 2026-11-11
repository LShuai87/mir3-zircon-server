using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Client.Envir;
using SlimDX.Direct3D9;

namespace Client.Controls
{
    // Original UI sprites are embedded so chat does not depend on optional legacy libraries.
    internal sealed class GameUiChatArtwork : DXControl
    {
        private static readonly Lazy<Bitmap> Atlas = new Lazy<Bitmap>(() =>
        {
            using (var stream = typeof(GameUiChatArtwork).Assembly.GetManifestResourceStream("Client.Resources.GameUi.chat-artwork.png"))
            using (var image = new Bitmap(stream)) return new Bitmap(image);
        });
        private readonly DXButton button;
        private readonly bool rejection, originalDrawImage;
        private readonly int firstRow;
        private int row;
        private readonly bool border;

        internal GameUiChatArtwork(DXControl parent)
        {
            border = true;
            Initialize(parent);
            Size = parent.Size;
            parent.SizeChanged += UpdateSize;
        }
        internal GameUiChatArtwork(DXButton button, int firstRow, bool rejection = false)
        {
            this.button = button;
            this.firstRow = firstRow;
            this.rejection = rejection;
            originalDrawImage = button.DrawImage;
            button.DrawImage = false;
            Initialize(button);
            button.BeforeDraw += UpdateButton;
            UpdateButton(button, EventArgs.Empty);
        }
        private void Initialize(DXControl parent)
        {
            Parent = parent;
            PassThrough = true;
            IsControl = false;
            Sort = false;
            DrawTexture = true;
        }
        private void UpdateSize(object sender, EventArgs args) { Size = Parent.Size; }
        private void UpdateButton(object sender, EventArgs args)
        {
            Size = button.Size;
            int state = !button.IsEnabled || (rejection && button.Pressed) ? 3 :
                button.Pressed || (button.HasFocus && MouseControl == button) ? 2 : MouseControl == button ? 1 : 0;
            if (row != firstRow + state) { row = firstRow + state; TextureValid = false; }
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
                    graphics.Clear(Color.Black);
                    graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                    graphics.PixelOffsetMode = PixelOffsetMode.Half;
                    if (border)
                    {
                        // UI[115], nine-sliced. Preserve its corners; cover its dark center with solid black.
                        int[] xs = { 0, 3, 131, 134 }, ys = { 768, 771, 779, 782 };
                        int[] dx = { 0, 3, Size.Width - 3, Size.Width }, dy = { 0, 3, Size.Height - 3, Size.Height };
                        for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++)
                        {
                            if (x == 1 && y == 1) continue;
                            graphics.DrawImage(Atlas.Value, new Rectangle(dx[x], dy[y], dx[x + 1] - dx[x], dy[y + 1] - dy[y]),
                                new Rectangle(xs[x], ys[y], xs[x + 1] - xs[x], ys[y + 1] - ys[y]), GraphicsUnit.Pixel);
                        }
                    }
                    else
                    {
                        var source = new Rectangle(0, row * 24, firstRow < 20 ? 31 : 18, firstRow < 20 ? 24 : firstRow < 28 ? 18 : 12);
                        graphics.DrawImage(Atlas.Value, new Rectangle(Point.Empty, Size), source, GraphicsUnit.Pixel);
                    }
                }
            }
            finally { ControlTexture.UnlockRectangle(0); pixels.Data.Dispose(); }
            TextureValid = true;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (border && Parent != null) Parent.SizeChanged -= UpdateSize;
                if (button != null)
                {
                    button.BeforeDraw -= UpdateButton;
                    if (!button.IsDisposed) button.DrawImage = originalDrawImage;
                }
            }
            base.Dispose(disposing);
        }
    }
}
