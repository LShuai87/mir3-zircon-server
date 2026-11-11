using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Client.Controls;
using Client.Envir;
using Client.Models;

namespace Client.Scenes.Views
{
    public sealed partial class MiniMapDialog
    {
        private MiniMapLayoutSnapshot gameUiMiniMapSnapshot;
        private DXLabel gameUiCoordinates, gameUiSafety;
        private DXControl gameUiExpandStroke;
        private Font gameUiTitleFont;
        private DXButton gameUiBigMap, gameUiTransparency, gameUiZoom;
        private MiniMapButtonArtwork gameUiZoomArt;
        private GameUiMiniMapLayout gameUiEnlargedLayout;
        public bool GameUiMiniMapEnlarged { get; private set; }
        public GameUiMiniMapLayout GameUiMiniMapActiveLayout => GameUiMiniMapEnlarged ? gameUiEnlargedLayout : GameUiMiniMapLayout;
        private GameUiWindowSkin gameUiExpandedSkin;
        private bool applyingGameUiMiniMap, applyingGameUiSkin;
        public bool GameUiMiniMapCollapsed { get; private set; }
        public Size GameUiMiniMapSize => GameUiMiniMapLayout == null ? Size :
            GameUiMiniMapCollapsed ? GameUiMiniMapActiveLayout.CollapsedSize : GameUiMiniMapActiveLayout.WindowSize;
        public GameUiMiniMapLayout GameUiMiniMapLayout { get; private set; }

        // Bind before installing the skin/loading settings. The expanded frame and close
        // rectangle must match the layout. Null restores the captured native controls.
        public void UseGameUiMiniMap(GameUiMiniMapLayout layout)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(MiniMapDialog));
            if (layout == null) { DisableGameUiMiniMap(); return; }
            ValidateGameUiMiniMap(layout);
            var previousLayout = GameUiMiniMapLayout;
            var previousEnlargedLayout = gameUiEnlargedLayout;
            var previousSize = Size;
            var previousArea = Area;
            var previousImageLocation = Image.Location;
            bool starting = gameUiMiniMapSnapshot == null;
            if (starting)
            {
                gameUiMiniMapSnapshot = new MiniMapLayoutSnapshot(this);
                gameUiExpandedSkin = WindowSkin;
            }
            GameUiMiniMapLayout = layout;
            gameUiEnlargedLayout = layout.CreateEnlarged();
            try
            {
                if (gameUiCoordinates == null)
                    gameUiCoordinates = new DXLabel
                    {
                        AutoSize = false,
                        IsControl = false,
                        DrawFormat = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine,
                        ForeColour = Color.White,
                        Outline = true,
                        Opacity = Opacity,
                        Parent = this
                    };
                if (gameUiSafety == null)
                    gameUiSafety = new DXLabel
                    {
                        AutoSize = false, IsControl = false,
                        DrawFormat = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix,
                        Font = new Font(Config.FontName, CEnvir.FontSize(8F), FontStyle.Regular),
                        Opacity = Opacity, Parent = this
                    };
                if (gameUiExpandStroke == null)
                    gameUiExpandStroke = new DXControl
                    {
                        Parent = CloseButton, IsControl = false, DrawTexture = true,
                        BackColour = Color.FromArgb(198, 166, 99), Size = new Size(1, 7),
                        Location = new Point(CloseButton.Size.Width / 2, 4), Visible = false, Opacity = Opacity
                    };
                CreateGameUiMiniMapToolbar();
                if (gameUiTitleFont == null)
                    gameUiTitleFont = new Font(Config.FontName, CEnvir.FontSize(8F), FontStyle.Regular);
                TitleLabel.Font = gameUiTitleFont;
                TitleLabel.ForeColour = Color.White;
                Panel.BackColour = Color.Black;
                Panel.DrawTexture = true;
                AllowResize = false;
                CloseButton.FixedSize = false;
                CloseButton.Visible = true;
                ApplyGameUiSkin();
                ApplyGameUiMiniMap();
                if (starting) UpdateMapPosition();
                RefreshGameUiCoordinates();
                RefreshGameUiSafety();
            }
            catch
            {
                if (starting) DisableGameUiMiniMap();
                else
                {
                    GameUiMiniMapLayout = previousLayout;
                    gameUiEnlargedLayout = previousEnlargedLayout;
                    ApplyGameUiSkin();
                    Size = previousSize;
                    ApplyGameUiMiniMap();
                    Area = previousArea;
                    Image.Location = previousImageLocation;
                }
                throw;
            }
        }

        private void ValidateGameUiMiniMap(GameUiMiniMapLayout layout)
        {
            if (IsMoving || IsResizing || Panel == null || Panel.IsDisposed || Panel.Parent != this ||
                Panel.IsMoving || Panel.IsResizing || Image == null || Image.IsDisposed || Image.Parent != Panel ||
                Image.IsMoving || Image.IsResizing || Image.AllowDragOut || CloseButton == null || CloseButton.IsDisposed ||
                CloseButton.Parent != this || CloseButton.IsMoving || CloseButton.IsResizing)
                throw new InvalidOperationException("The original minimap controls are missing or in use.");
            var skin = gameUiExpandedSkin ?? WindowSkin;
            if (skin != null && (skin.FrameSize != layout.WindowSize ||
                skin.GetCloseArea(layout.WindowSize) != layout.Collapse))
                throw new ArgumentException("The minimap layout does not match the installed skin.", nameof(layout));
            if (gameUiMiniMapSnapshot != null &&
                (Panel != gameUiMiniMapSnapshot.Panel || Image != gameUiMiniMapSnapshot.Image || CloseButton != gameUiMiniMapSnapshot.Close))
                throw new InvalidOperationException("An original minimap control was replaced.");
        }

        public override Size Size
        {
            get => base.Size;
            set => base.Size = GameUiMiniMapLayout == null ? value : GameUiMiniMapSize;
        }

        protected override void OnCloseButtonClick()
        {
            if (GameUiMiniMapLayout == null) { base.OnCloseButtonClick(); return; }
            SetGameUiMiniMapCollapsed(!GameUiMiniMapCollapsed);
        }

        public void SetGameUiMiniMapCollapsed(bool collapsed)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(MiniMapDialog));
            if (GameUiMiniMapLayout == null || GameUiMiniMapCollapsed == collapsed) return;
            GameUiMiniMapCollapsed = collapsed;
            try
            {
                ApplyGameUiSkin();
                ApplyGameUiMiniMap();
                // A collapsed bar can be dragged below the expanded window's bottom edge.
                if (!collapsed && Parent != null)
                    Location = new Point(Math.Max(0, Math.Min(Location.X, Parent.Size.Width - Size.Width)),
                        Math.Max(0, Math.Min(Location.Y, Parent.Size.Height - Size.Height)));
            }
            catch
            {
                GameUiMiniMapCollapsed = !collapsed;
                ApplyGameUiSkin();
                ApplyGameUiMiniMap();
                throw;
            }
        }

        private void ApplyGameUiSkin()
        {
            if (applyingGameUiSkin || GameUiMiniMapLayout == null) return;
            applyingGameUiSkin = true;
            try
            {
                var skin = gameUiExpandedSkin;
                if (GameUiMiniMapEnlarged && skin != null)
                {
                    var layout = GameUiMiniMapLayout;
                    // Preserve native corners, footer buttons and header height while the centre grows.
                    skin = new GameUiWindowSkin(skin.Library, skin.FrameIndex, Padding.Empty,
                        new Padding(44, layout.Viewport.Top, 20, layout.WindowSize.Height - layout.Viewport.Bottom),
                        Padding.Empty, layout.CollapsedSize, false, layout.Title, skin.DragArea,
                        layout.Collapse, skin.CloseImages,
                        new Rectangle(layout.Coordinates.Left, layout.Viewport.Bottom,
                            layout.Coordinates.Width, layout.WindowSize.Height - layout.Viewport.Bottom));
                }
                else if (GameUiMiniMapCollapsed && skin != null)
                {
                    var layout = GameUiMiniMapLayout;
                    // Keep the original header/footer pixels and omit the entire centre slice.
                    skin = new GameUiWindowSkin(skin.Library, skin.FrameIndex, Padding.Empty,
                        new Padding(0, layout.Viewport.Top, 0, layout.WindowSize.Height - layout.Viewport.Bottom),
                        Padding.Empty, layout.CollapsedSize, false, layout.Title, skin.DragArea,
                        layout.Collapse, skin.CloseImages);
                }
                WindowSkin = skin;
            }
            finally { applyingGameUiSkin = false; }
        }

        private void ApplyGameUiMiniMap()
        {
            if (GameUiMiniMapLayout == null || applyingGameUiMiniMap) return;
            applyingGameUiMiniMap = true;
            try
            {
                var layout = GameUiMiniMapActiveLayout;
                bool positionChanged = Panel.Size != layout.Viewport.Size || (!GameUiMiniMapCollapsed && !Panel.Visible);
                Size = GameUiMiniMapSize;
                Area = layout.Viewport;
                Panel.Location = Area.Location;
                Panel.Size = Area.Size;
                Panel.Visible = !GameUiMiniMapCollapsed;
                if (gameUiCoordinates != null)
                {
                    var coordinates = GameUiMiniMapCollapsed ? layout.CollapsedCoordinates : layout.Coordinates;
                    gameUiCoordinates.Location = coordinates.Location;
                    gameUiCoordinates.Size = coordinates.Size;
                }
                if (gameUiSafety != null)
                {
                    gameUiSafety.Location = layout.Safety.Location;
                    gameUiSafety.Size = layout.Safety.Size;
                }
                TitleLabel.AutoSize = false;
                TitleLabel.DrawFormat = TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                TitleLabel.Location = layout.Title.Location;
                TitleLabel.Size = layout.Title.Size;
                CloseButton.Visible = true;
                CloseButton.Location = layout.Collapse.Location;
                CloseButton.Size = layout.Collapse.Size;
                CloseButton.Hint = GameUiMiniMapCollapsed ? "展开小地图" : "收起小地图";
                if (gameUiExpandStroke != null)
                {
                    gameUiExpandStroke.Location = new Point(CloseButton.Size.Width / 2, 4);
                    gameUiExpandStroke.Visible = GameUiMiniMapCollapsed;
                }
                ArrangeGameUiMiniMapToolbar(layout);
                if (!GameUiMiniMapCollapsed && positionChanged) UpdateMapPosition();
            }
            finally { applyingGameUiMiniMap = false; }
        }

        protected override void OnWindowSkinChanged()
        {
            base.OnWindowSkinChanged();
            if (applyingGameUiSkin || GameUiMiniMapLayout == null) return;
            gameUiExpandedSkin = WindowSkin;
            ApplyGameUiSkin();
            ApplyGameUiMiniMap();
        }

        public void RefreshGameUiSafety()
        {
            if (gameUiSafety == null) return;
            var user = MapObject.User;
            bool available = user != null && mapSource.MapInfo != null;
            gameUiSafety.Text = !available ? string.Empty : user.InSafeZone ? "安全" : "危险";
            gameUiSafety.ForeColour = user != null && user.InSafeZone ? Color.Lime : Color.Red;
        }

        private void RefreshGameUiCoordinates()
        {
            if (gameUiCoordinates == null) return;
            var user = MapObject.User;
            gameUiCoordinates.Text = user == null || mapSource.MapInfo == null ? string.Empty :
                string.Format(CultureInfo.InvariantCulture, "{0}:{1}", user.CurrentLocation.X, user.CurrentLocation.Y);
        }

        public override void Process()
        {
            base.Process();
            RefreshGameUiCoordinates();
            RefreshGameUiSafety();
            RefreshGameUiBigMapButton();
        }

        public override Size GetAcceptableResize(Size size)
        {
            return GameUiMiniMapLayout == null ? base.GetAcceptableResize(size) : GameUiMiniMapSize;
        }

        private void DisableGameUiMiniMap()
        {
            if (gameUiMiniMapSnapshot == null) return;
            var snapshot = gameUiMiniMapSnapshot;
            GameUiMiniMapLayout = null;
            GameUiMiniMapCollapsed = false;
            GameUiMiniMapEnlarged = false;
            gameUiEnlargedLayout = null;
            gameUiExpandedSkin = null;
            if (gameUiBigMap != null) { gameUiBigMap.Dispose(); gameUiBigMap = null; }
            if (gameUiTransparency != null) { gameUiTransparency.Dispose(); gameUiTransparency = null; }
            if (gameUiZoom != null) { gameUiZoom.Dispose(); gameUiZoom = null; gameUiZoomArt = null; }
            if (gameUiSafety != null) { gameUiSafety.Dispose(); gameUiSafety = null; }
            if (gameUiExpandStroke != null) { gameUiExpandStroke.Dispose(); gameUiExpandStroke = null; }
            if (gameUiCoordinates != null) { gameUiCoordinates.Dispose(); gameUiCoordinates = null; }
            // Suppress viewport callbacks until skin, size and native controls are restored.
            WindowSkin = snapshot.Skin;
            TitleLabel.Font = snapshot.TitleFont;
            TitleLabel.ForeColour = snapshot.TitleColour;
            TitleLabel.AutoSize = snapshot.TitleAutoSize;
            TitleLabel.DrawFormat = snapshot.TitleDrawFormat;
            TitleLabel.Size = snapshot.TitleSize;
            if (gameUiTitleFont != null) { gameUiTitleFont.Dispose(); gameUiTitleFont = null; }
            Size = snapshot.WindowSize;
            Area = snapshot.Area;
            snapshot.Panel.Visible = snapshot.PanelVisible;
            snapshot.Panel.DrawTexture = snapshot.PanelDrawTexture;
            snapshot.Panel.BackColour = snapshot.PanelBackColour;
            snapshot.Panel.Location = snapshot.PanelBounds.Location;
            snapshot.Panel.Size = snapshot.PanelBounds.Size;
            snapshot.Image.Location = snapshot.ImageLocation;
            snapshot.Close.FixedSize = snapshot.CloseFixedSize;
            snapshot.Close.Location = snapshot.CloseBounds.Location;
            snapshot.Close.Size = snapshot.CloseBounds.Size;
            snapshot.Close.Visible = snapshot.CloseVisible;
            snapshot.Close.Hint = snapshot.CloseHint;
            AllowResize = snapshot.AllowResize;
            gameUiMiniMapSnapshot = null;
            UpdateMapPosition();
        }

        private sealed class MiniMapLayoutSnapshot
        {
            internal readonly DXControl Panel;
            internal readonly DXImageControl Image;
            internal readonly DXButton Close;
            internal readonly Rectangle Area, PanelBounds, CloseBounds;
            internal readonly Point ImageLocation;
            internal readonly Size WindowSize;
            internal readonly GameUiWindowSkin Skin;
            internal readonly bool AllowResize, CloseVisible, CloseFixedSize, PanelDrawTexture;
            internal readonly Color PanelBackColour, TitleColour;
            internal readonly Font TitleFont;
            internal readonly bool PanelVisible, TitleAutoSize;
            internal readonly TextFormatFlags TitleDrawFormat;
            internal readonly Size TitleSize;
            internal readonly string CloseHint;
            internal MiniMapLayoutSnapshot(MiniMapDialog window)
            {
                Panel = window.Panel;
                Image = window.Image;
                Close = window.CloseButton;
                Area = window.Area;
                PanelBounds = new Rectangle(Panel.Location, Panel.Size);
                PanelVisible = Panel.Visible;
                TitleFont = window.TitleLabel.Font;
                TitleColour = window.TitleLabel.ForeColour;
                TitleAutoSize = window.TitleLabel.AutoSize;
                TitleDrawFormat = window.TitleLabel.DrawFormat;
                TitleSize = window.TitleLabel.Size;
                CloseHint = Close.Hint;
                PanelDrawTexture = Panel.DrawTexture;
                PanelBackColour = Panel.BackColour;
                CloseBounds = new Rectangle(Close.Location, Close.Size);
                ImageLocation = Image.Location;
                WindowSize = window.Size;
                Skin = window.WindowSkin;
                AllowResize = window.AllowResize;
                CloseVisible = Close.Visible;
                CloseFixedSize = Close.FixedSize;
            }
        }
    }
}
