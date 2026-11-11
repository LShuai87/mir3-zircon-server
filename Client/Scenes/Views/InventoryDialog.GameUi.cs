using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using SlimDX;
using SlimDX.Direct3D9;
using System.Windows.Forms;
using Client.Controls;
using Client.Envir;
using Library;

namespace Client.Scenes.Views
{
    public sealed partial class InventoryDialog
    {
        private DXLabel gameUiGoldCaption, gameUiWeightCaption;
        private InventoryControlSnapshot[] gameUiInventorySnapshots;
        private InventoryTextureArt gameUiWeightFill, gameUiGoldFrame;
        private InventoryTextureArt[] gameUiButtonArt;
        private InventoryTextureArt gameUiCloseArt;
        private InventoryControlSnapshot gameUiCloseSnapshot;
        private int gameUiBagWeight, gameUiMaxBagWeight;
        private string gameUiOriginalTitle;
        private Color gameUiOriginalTitleColour;
        private static readonly Color InventoryGold = Color.FromArgb(198, 166, 99);
        public GameUiInventoryLayout GameUiInventoryLayout { get; private set; }

        // The caller installs/sizes the window skin first. Item slots and popup windows are untouched.
        public void UseGameUiInventory(GameUiInventoryLayout layout)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(InventoryDialog));
            if (layout == null) { DisableGameUiInventory(); return; }
            ValidateGameUiInventory(layout);
            var controls = GetGameUiInventoryControls();
            if (gameUiInventorySnapshots == null)
            {
                gameUiOriginalTitle = TitleLabel.Text;
                gameUiOriginalTitleColour = TitleLabel.ForeColour;
                gameUiInventorySnapshots = new InventoryControlSnapshot[controls.Length];
                for (int i = 0; i < controls.Length; i++)
                {
                    var snapshot = new InventoryControlSnapshot(controls[i], controls[i] == GoldLabel || controls[i] == WeightLabel);
                    gameUiInventorySnapshots[i] = snapshot;
                    if (snapshot.DynamicText) controls[i].TextChanged += GameUiInventoryTextChanged;
                    if (controls[i] is DXButton) controls[i].BeforeDraw += GameUiInventoryButtonBeforeDraw;
                }
            }
            var bounds = layout.GetBounds();
            for (int i = 0; i < controls.Length; i++)
            {
                var label = controls[i] as DXLabel;
                if (label != null)
                {
                    label.AutoSize = false;
                    label.DrawFormat = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                }
                controls[i].Location = bounds[i].Location;
                controls[i].Size = bounds[i].Size;
                gameUiInventorySnapshots[i].RefreshHint();
            }
            GameUiInventoryLayout = layout;
            ApplyGameUiInventoryAppearance();
        }

        private void ApplyGameUiInventoryAppearance()
        {
            TitleLabel.Text = "包裹";
            TitleLabel.ForeColour = Color.FromArgb(225, 211, 177);
            gameUiWeightCaption.Text = "负重";
            foreach (var caption in new[] { gameUiWeightCaption, gameUiGoldCaption })
            {
                caption.Border = false;
                caption.BackColour = Color.Empty;
                caption.ForeColour = InventoryGold;
            }
            GoldLabel.Border = false;
            GoldLabel.BackColour = Color.Empty;
            if (gameUiGoldFrame == null)
            {
                gameUiGoldFrame = new InventoryTextureArt(null, "inventory-gold.png", 1) { Parent = this };
                Controls.Remove(gameUiGoldFrame);
                Controls.Insert(Controls.IndexOf(GoldLabel), gameUiGoldFrame);
            }
            gameUiGoldFrame.Location = GoldLabel.Location;
            gameUiGoldFrame.Size = GoldLabel.Size;
            // The original label remains the tooltip/input surface; its text stays live for Legacy.
            WeightLabel.Border = false;
            WeightLabel.DrawTexture = false;
            if (gameUiWeightFill == null)
            {
                gameUiWeightFill = new InventoryTextureArt(null, "inventory-weight.png", 2, true)
                    { Parent = WeightLabel, Location = new Point(2, 2) };
            }
            // The atlas already carries the 49 recessed slots. Keep native cell overlays for
            // hover, selection and locking, while removing the opaque grid and straight rules.
            Grid.DrawTexture = false;
            Grid.Border = false;
            var buttons = new[] { RemoteSellButton, RemoteFragmentButton, MonsterDropListButton };
            if (gameUiButtonArt == null)
            {
                gameUiButtonArt = new InventoryTextureArt[buttons.Length];
                for (int i = 0; i < buttons.Length; i++)
                {
                    var art = new InventoryTextureArt(buttons[i]) { Parent = buttons[i], Size = buttons[i].Size };
                    buttons[i].Controls.Remove(art);
                    buttons[i].Controls.Insert(0, art);
                    gameUiButtonArt[i] = art;
                }
            }
            for (int i = 0; i < buttons.Length; i++)
            {
                var button = buttons[i];
                button.DrawImage = false;
                button.DrawTexture = false;
                button.Border = false;
                gameUiButtonArt[i].Size = button.Size;
                GameUiInventoryButtonBeforeDraw(button, EventArgs.Empty);
            }
            if (gameUiCloseArt == null)
            {
                gameUiCloseSnapshot = new InventoryControlSnapshot(CloseButton, false);
                gameUiCloseArt = new InventoryTextureArt(CloseButton, "inventory-close.png") { Parent = CloseButton };
                CloseButton.Controls.Remove(gameUiCloseArt);
                CloseButton.Controls.Insert(0, gameUiCloseArt);
            }
            CloseButton.DrawImage = false;
            CloseButton.DrawTexture = false;
            CloseButton.Border = false;
            CloseButton.Size = WindowSkin == null ? new Size(21, 22) : WindowSkin.CloseArea.Size;
            gameUiCloseArt.Size = CloseButton.Size;
            UpdateGameUiInventoryWeight(gameUiBagWeight, gameUiMaxBagWeight);
        }

        // Called with authoritative numeric values, never by parsing the localized label text.
        public void UpdateGameUiInventoryWeight(int current, int maximum)
        {
            gameUiBagWeight = current;
            gameUiMaxBagWeight = maximum;
            if (GameUiInventoryLayout == null) return;
            decimal fraction = maximum <= 0 ? 0M : Math.Max(0M, Math.Min(1M, (decimal)current / maximum));
            int width = (int)decimal.Floor(Math.Max(0, WeightLabel.Size.Width - 4) * fraction);
            int height = Math.Max(0, WeightLabel.Size.Height - 3);
            bool overweight = current > maximum;
            gameUiWeightFill.Size = new Size(width, height);
            gameUiWeightFill.Visible = width > 0 && height > 0;
            gameUiWeightFill.Frame = overweight ? 1 : 0;
            gameUiWeightCaption.ForeColour = overweight ? Color.FromArgb(240, 98, 71) : InventoryGold;
            foreach (var snapshot in gameUiInventorySnapshots)
                if (snapshot.Control == WeightLabel)
                    snapshot.RefreshHint($"负重：{current} / {maximum}" + (overweight ? "（超重）" : ""));
        }

        private void GameUiInventoryButtonBeforeDraw(object sender, EventArgs e)
        {
            var button = (DXButton)sender;
            button.Label.ForeColour = button.IsEnabled ? InventoryGold : Color.FromArgb(112, 103, 81);
        }

        private DXControl[] GetGameUiInventoryControls() => new DXControl[] {
            Grid, gameUiWeightCaption, WeightLabel, gameUiGoldCaption, GoldLabel,
            RemoteSellButton, RemoteFragmentButton, MonsterDropListButton
        };

        private void ValidateGameUiInventory(GameUiInventoryLayout layout)
        {
            if (IsMoving || IsResizing) throw new InvalidOperationException("The inventory window is in use.");
            var window = new Rectangle(Point.Empty, Size);
            foreach (var rect in layout.GetBounds())
                if (!window.Contains(rect)) throw new ArgumentException("The inventory window is smaller than the layout.", nameof(layout));
            foreach (var chrome in new DXControl[] { TitleLabel, CloseButton })
            {
                if (chrome == null || chrome.IsDisposed || !chrome.Visible || chrome.Size.Width <= 0 || chrome.Size.Height <= 0) continue;
                var chromeBounds = new Rectangle(chrome.Location, chrome.Size);
                foreach (var rect in layout.GetBounds())
                    if (rect.IntersectsWith(chromeBounds)) throw new ArgumentException("Inventory content overlaps window title or close control.", nameof(layout));
            }
            var used = new HashSet<DXControl>();
            foreach (var control in GetGameUiInventoryControls())
                if (control == null || control.IsDisposed || control.Parent != this || !used.Add(control) || control.IsMoving || control.IsResizing)
                    throw new InvalidOperationException("An original inventory control is missing, reparented or in use.");
            if (Grid.GridSize != new Size(7, 7) || Grid.Grid == null || Grid.Grid.Length != 49 ||
                Grid.Size != layout.Grid.Size || Grid.ScrollValue != 0 || Grid.VisibleHeight < 7 || Grid.GridType != GridType.Inventory)
                throw new InvalidOperationException("The original inventory grid no longer matches the layout.");
            var cells = new HashSet<DXItemCell>();
            for (int i = 0; i < Grid.Grid.Length; i++)
            {
                var cell = Grid.Grid[i];
                if (cell == null || cell.IsDisposed || !cells.Add(cell) || cell.Parent != Grid || cell.HostGrid != Grid ||
                    cell.Slot != i || cell.ItemGrid != Grid.ItemGrid || cell.GridType != GridType.Inventory ||
                    cell.Size != new Size(36, 36) || cell.Location != new Point(i % 7 * 35, i / 7 * 35) ||
                    !cell.Visible || cell.IsMoving || cell.IsResizing)
                    throw new InvalidOperationException("An original inventory slot no longer matches the grid.");
            }
        }

        private void GameUiInventoryTextChanged(object sender, EventArgs e)
        {
            if (gameUiInventorySnapshots == null) return;
            foreach (var snapshot in gameUiInventorySnapshots)
                if (snapshot.Control == sender) { snapshot.RefreshHint(); return; }
        }

        private void DisableGameUiInventory()
        {
            if (gameUiInventorySnapshots == null) return;
            foreach (var snapshot in gameUiInventorySnapshots)
            {
                snapshot.Control.TextChanged -= GameUiInventoryTextChanged;
                snapshot.Control.BeforeDraw -= GameUiInventoryButtonBeforeDraw;
                snapshot.Restore();
            }
            if (gameUiButtonArt != null)
                foreach (var art in gameUiButtonArt) art.Dispose();
            gameUiButtonArt = null;
            gameUiCloseArt?.Dispose();
            gameUiCloseArt = null;
            gameUiCloseSnapshot?.Restore();
            gameUiCloseSnapshot = null;
            if (gameUiWeightFill != null) gameUiWeightFill.Dispose();
            gameUiWeightFill = null;
            gameUiGoldFrame?.Dispose();
            gameUiGoldFrame = null;
            TitleLabel.Text = gameUiOriginalTitle;
            TitleLabel.ForeColour = gameUiOriginalTitleColour;
            gameUiOriginalTitle = null;
            gameUiInventorySnapshots = null;
            GameUiInventoryLayout = null;
        }

        // Embedded source art preserves the native inventory fields and button states without
        // loading the user's official install or changing the verified window atlas at runtime.
        private sealed class InventoryTextureArt : DXControl
        {
            private readonly DXButton button;
            private Bitmap sheet;
            private readonly int frameWidth, frameHeight;
            private readonly bool cropToSize;
            internal int Frame { get; set; }
            private int renderedState = -1;
            private int State => button == null ? Frame : !button.IsEnabled ? 3 :
                button.Pressed || (button.HasFocus && MouseControl == button && button.CanBePressed) ? 2 :
                MouseControl == button ? 1 : 0;

            internal InventoryTextureArt(DXButton owner, string resource = "inventory-button.png", int frameCount = 4, bool crop = false)
            {
                button = owner;
                cropToSize = crop;
                IsControl = false;
                DrawTexture = true;
                using (var stream = typeof(InventoryDialog).Assembly.GetManifestResourceStream("Client.Resources.GameUi." + resource))
                using (var image = new Bitmap(stream)) sheet = new Bitmap(image);
                frameWidth = sheet.Width; frameHeight = sheet.Height / frameCount;
            }

            protected override void DrawControl()
            {
                if (renderedState != State) TextureValid = false;
                if (!TextureValid) CreateTexture();
                float oldOpacity = DXManager.Opacity;
                try
                {
                    DXManager.SetOpacity(Opacity);
                    // Disabled has its own official frame; avoid applying a second grey tint.
                    PresentTexture(ControlTexture, Parent, DisplayArea, Color.White, this);
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
                    using (var target = new Bitmap(Size.Width, Size.Height, pixels.Pitch, PixelFormat.Format32bppArgb, pixels.Data.DataPointer))
                    using (var graphics = Graphics.FromImage(target))
                    {
                        graphics.Clear(Color.Transparent);
                        renderedState = State;
                        // A partial gauge reveals native pixels without squeezing the full gradient.
                        int sourceWidth = cropToSize ? Math.Min(Size.Width, frameWidth) : frameWidth;
                        int sourceHeight = cropToSize ? Math.Min(Size.Height, frameHeight) : frameHeight;
                        graphics.DrawImage(sheet, new Rectangle(Point.Empty, Size), 0, renderedState * frameHeight,
                            sourceWidth, sourceHeight, GraphicsUnit.Pixel);
                    }
                }
                finally { ControlTexture.UnlockRectangle(0); pixels.Data.Dispose(); }
                TextureValid = true;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) { sheet?.Dispose(); sheet = null; }
                base.Dispose(disposing);
            }
        }

        private sealed class InventoryControlSnapshot
        {
            internal DXControl Control { get; }
            internal bool DynamicText { get; }
            private readonly Rectangle bounds;
            private readonly bool autoSize;
            private readonly TextFormatFlags format;
            private readonly string hint, text;
            private readonly bool border, drawTexture, drawImage;
            private readonly Color backColour, borderColour, foreColour, buttonLabelColour;
            private string generatedHint;
            private bool ownsHint;
            internal InventoryControlSnapshot(DXControl control, bool dynamicText)
            {
                Control = control; DynamicText = dynamicText;
                bounds = new Rectangle(control.Location, control.Size); hint = control.Hint;
                text = control.Text; border = control.Border; drawTexture = control.DrawTexture;
                backColour = control.BackColour; borderColour = control.BorderColour; foreColour = control.ForeColour;
                if (control is DXImageControl image) drawImage = image.DrawImage;
                if (control is DXButton button) buttonLabelColour = button.Label.ForeColour;
                var label = control as DXLabel;
                if (label != null) { autoSize = label.AutoSize; format = label.DrawFormat; }
                ownsHint = dynamicText && hint == null;
            }
            internal void RefreshHint(string value = null)
            {
                if (!ownsHint || Control.IsDisposed) return;
                if (Control.Hint != generatedHint) { ownsHint = false; return; }
                generatedHint = value ?? Control.Text; Control.Hint = generatedHint;
            }
            internal void Restore()
            {
                if (Control.IsDisposed) return;
                if (ownsHint && Control.Hint == generatedHint) Control.Hint = hint;
                var label = Control as DXLabel;
                if (label != null) { label.AutoSize = false; label.DrawFormat = format; }
                Control.Border = border; Control.BorderColour = borderColour; Control.BackColour = backColour;
                Control.DrawTexture = drawTexture;
                if (!DynamicText) { Control.Text = text; Control.ForeColour = foreColour; }
                if (Control is DXImageControl image) image.DrawImage = drawImage;
                if (Control is DXButton button) button.Label.ForeColour = buttonLabelColour;
                Control.Location = bounds.Location; Control.Size = bounds.Size;
                if (label != null) label.AutoSize = autoSize;
                // Keep live text, colours, visibility, item links and the original event handlers.
            }
        }
    }
}
