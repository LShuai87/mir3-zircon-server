using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Client.Controls;
using Client.Envir;
using SlimDX.Direct3D9;

namespace Client.Scenes.Views
{
    public sealed partial class MiniMapDialog
    {
        private void CreateGameUiMiniMapToolbar()
        {
            if (gameUiBigMap == null)
            {
                gameUiBigMap = CreateMiniMapButton("打开大地图", 0);
                gameUiBigMap.MouseClick += (sender, args) =>
                {
                    if (args.Button != MouseButtons.Left) return;
                    var bigMap = GameScene.Game?.BigMapBox;
                    if (bigMap == null || bigMap.IsDisposed || mapSource.MapInfo == null) return;
                    bigMap.Opacity = 1F;
                    bigMap.Visible = true;
                    bigMap.SelectedInfo = mapSource.MapInfo;
                    bigMap.BringToFront();
                };
            }
            if (gameUiTransparency == null)
            {
                gameUiTransparency = CreateMiniMapButton("半透明显示", 4);
                gameUiTransparency.MouseClick += (sender, args) =>
                {
                    if (args.Button == MouseButtons.Left) Opacity = Opacity < 1F ? 1F : .5F;
                };
            }
            if (gameUiZoom == null)
            {
                gameUiZoom = CreateMiniMapButton("放大小地图", 8);
                gameUiZoomArt = (MiniMapButtonArtwork)gameUiZoom.Tag;
                gameUiZoom.MouseClick += (sender, args) =>
                {
                    if (args.Button == MouseButtons.Left) SetGameUiMiniMapEnlarged(!GameUiMiniMapEnlarged);
                };
            }
        }

        private DXButton CreateMiniMapButton(string hint, int firstRow)
        {
            var button = new DXButton
            {
                Parent = this, Size = new Size(20, 16), Hint = hint,
                DrawImage = false, DrawTexture = false, Opacity = Opacity
            };
            button.Tag = new MiniMapButtonArtwork(button, firstRow);
            return button;
        }

        private void ArrangeGameUiMiniMapToolbar(GameUiMiniMapLayout layout)
        {
            if (gameUiBigMap == null) return;
            gameUiBigMap.Location = layout.FooterBounds(layout.BigMap, GameUiMiniMapCollapsed).Location;
            gameUiTransparency.Location = layout.FooterBounds(layout.Transparency, GameUiMiniMapCollapsed).Location;
            gameUiZoom.Location = layout.FooterBounds(layout.Zoom, GameUiMiniMapCollapsed).Location;
            gameUiZoom.Hint = GameUiMiniMapEnlarged ? "缩小小地图" : "放大小地图";
            gameUiZoomArt.FirstRow = GameUiMiniMapEnlarged ? 12 : 8;
            RefreshGameUiTransparencyHint();
            RefreshGameUiBigMapButton();
        }

        private void RefreshGameUiTransparencyHint()
        {
            if (gameUiTransparency != null)
                gameUiTransparency.Hint = Opacity < 1F ? "取消半透明显示" : "半透明显示";
        }

        private void RefreshGameUiBigMapButton()
        {
            if (gameUiBigMap != null)
                gameUiBigMap.Enabled = mapSource.MapInfo != null && GameScene.Game?.BigMapBox != null && !GameScene.Game.BigMapBox.IsDisposed;
        }

        public void SetGameUiMiniMapEnlarged(bool enlarged)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(MiniMapDialog));
            if (GameUiMiniMapLayout == null || GameUiMiniMapEnlarged == enlarged) return;
            int right = Location.X + Size.Width;
            GameUiMiniMapEnlarged = enlarged;
            try
            {
                ApplyGameUiSkin();
                ApplyGameUiMiniMap();
                int x = right - Size.Width, y = Location.Y;
                if (Parent != null)
                {
                    x = Math.Max(0, Math.Min(x, Parent.Size.Width - Size.Width));
                    y = Math.Max(0, Math.Min(y, Parent.Size.Height - Size.Height));
                }
                Location = new Point(x, y);
            }
            catch
            {
                GameUiMiniMapEnlarged = !enlarged;
                ApplyGameUiSkin();
                ApplyGameUiMiniMap();
                throw;
            }
        }

        // Pinned native UI[330..333,670..673,320..323,310..313]. The atlas is embedded,
        // keeping these missing footer sprites independent of the verified window package.
        private sealed class MiniMapButtonArtwork : DXControl
        {
            private static readonly Lazy<Bitmap> Atlas = new Lazy<Bitmap>(() =>
            {
                using (var stream = typeof(MiniMapDialog).Assembly.GetManifestResourceStream("Client.Resources.GameUi.minimap-buttons.png"))
                using (var bitmap = new Bitmap(stream)) return new Bitmap(bitmap);
            });
            private readonly DXButton button;
            private int renderedRow = -1;
            internal int FirstRow { get; set; }
            internal MiniMapButtonArtwork(DXButton button, int firstRow)
            {
                this.button = button;
                FirstRow = firstRow;
                Parent = button;
                Size = button.Size;
                IsControl = false;
                PassThrough = true;
                DrawTexture = true;
            }
            private int Row => FirstRow + (!button.IsEnabled ? 3 :
                button.Pressed || (button.HasFocus && MouseControl == button && button.CanBePressed) ? 2 :
                MouseControl == button ? 1 : 0);
            protected override void DrawControl()
            {
                if (renderedRow != Row) TextureValid = false;
                if (!TextureValid) CreateTexture();
                float oldOpacity = DXManager.Opacity;
                try
                {
                    // Keep source alpha for the native button's transparent edge pixels.
                    DXManager.SetOpacity(1F);
                    float alpha = Math.Max(0F, Math.Min(1F, button.Opacity));
                    PresentTexture(ControlTexture, Parent, DisplayArea, Color.FromArgb((int)(255 * alpha), Color.White), this);
                }
                finally { DXManager.SetOpacity(oldOpacity); }
                ExpireTime = CEnvir.Now + Config.CacheDuration;
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
                    using (var bitmap = new Bitmap(Size.Width, Size.Height, pixels.Pitch, PixelFormat.Format32bppArgb, pixels.Data.DataPointer))
                    using (var graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.Clear(Color.Transparent);
                        renderedRow = Row;
                        graphics.DrawImage(Atlas.Value, new Rectangle(Point.Empty, Size), new Rectangle(0, renderedRow * 16, 20, 16), GraphicsUnit.Pixel);
                    }
                }
                finally { ControlTexture.UnlockRectangle(0); pixels.Data.Dispose(); }
                TextureValid = true;
            }
        }
    }
}
