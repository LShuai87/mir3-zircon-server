using System;
using System.Drawing;
using System.Collections.Generic;
using Client.Controls;
using Client.Envir;
using Client.Models;
using Library;
using SlimDX;
using SlimDX.Direct3D9;

namespace Client.Scenes.Views
{
    public sealed partial class CharacterDialog
    {
        public GameUiCharacterCompositionLayout GameUiCompositionLayout { get; private set; }
        private Rectangle[] gameUiPreviewRegions;

        private ClientUserItem gameUiPreviewMouseItem;
        private static readonly EquipmentSlot[] PreviewHitOrder =
            { EquipmentSlot.Helmet, EquipmentSlot.Shield, EquipmentSlot.Weapon, EquipmentSlot.Armour };

        private void ClearGameUiPreviewHover()
        {
            if (gameUiPreviewMouseItem == null) return;
            if (ReferenceEquals(GameScene.Game?.MouseItem, gameUiPreviewMouseItem))
                GameScene.Game.MouseItem = null;
            gameUiPreviewMouseItem = null;
        }

        private void UpdateGameUiPreviewHover(Point point)
        {
            ClientUserItem item = null;
            if (GameUiCompositionLayout != null && IsVisible && CharacterTab.IsVisible &&
                CharacterTab.IsEnabled && (MouseControl == this || MouseControl == CharacterTab) &&
                Array.Exists(GameUiPreviewViewports(), region => region.Contains(point)) &&
                CEnvir.LibraryList.TryGetValue(LibraryFile.Equip, out MirLibrary library))
            {
                var relative = new Point(point.X - DisplayArea.X - GameUiCompositionLayout.Pivot.X,
                    point.Y - DisplayArea.Y - GameUiCompositionLayout.Pivot.Y);
                // Match the visible sprite layers, including offsets and transparent pixels.
                foreach (var slot in PreviewHitOrder)
                {
                    var equipped = Grid[(int)slot].Item;
                    if (equipped == null) continue;
                    var image = library.CreateImage(equipped.Info.Image, ImageType.Image);
                    if (image?.Image == null) continue;
                    var pixel = new Point(relative.X - image.OffSetX, relative.Y - image.OffSetY);
                    if (!new Rectangle(0, 0, image.Width, image.Height).Contains(pixel) ||
                        !image.VisiblePixel(pixel, true)) continue;
                    item = equipped;
                    break;
                }
            }
            if (item == null) { ClearGameUiPreviewHover(); return; }
            gameUiPreviewMouseItem = item;
            GameScene.Game.MouseItem = item;
        }

        public override void Process()
        {
            base.Process();
            if (gameUiPreviewMouseItem != null || MouseControl == this || MouseControl == CharacterTab)
                UpdateGameUiPreviewHover(CEnvir.MouseLocation);
        }

        // Install the original controls/pages/attributes first. This entrypoint never reads files or sends packets.
        public void UseGameUiComposition(MainPanel source, GameUiCharacterCompositionLayout layout)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(CharacterDialog));
            if (layout == null) { DisableGameUiComposition(); return; }
            ValidateGameUiComposition(layout, GameUiEquipmentLayout, GameUiAttributeLayout, GameUiControlLayout);
            var local = layout.GetReadoutLayout(GameUiCharacterPageOrigin(GameUiEquipmentLayout));
            var previewRegions = layout.GetPreviewRegions(GameUiEquipmentLayout);
            // Construct and validate before disposing the active binding.
            var replacement = new GameUiCharacterReadouts(CharacterTab, source, local);
            GameUiReadouts?.Dispose();
            GameUiReadouts = replacement;
            GameUiCompositionLayout = layout;
            gameUiPreviewRegions = previewRegions;
            replacement.Disposed += GameUiCompositionReadoutsDisposed;
        }

        private void GameUiCompositionReadoutsDisposed(object sender, EventArgs e)
        {
            if (!ReferenceEquals(sender, GameUiReadouts)) return;
            ClearGameUiPreviewHover();
            GameUiCompositionLayout = null;
            gameUiPreviewRegions = null;
            GameUiReadouts = null;
        }

        private void DisableGameUiComposition()
        {
            if (GameUiCompositionLayout == null) return;
            GameUiReadouts?.Dispose();
            GameUiReadouts = null;
            ClearGameUiPreviewHover();
            GameUiCompositionLayout = null;
            gameUiPreviewRegions = null;
        }

        private static Point GameUiCharacterPageOrigin(GameUiCharacterEquipmentLayout pages)
            => new Point(pages.Container.X + pages.Character.Page.X, pages.Container.Y + pages.Character.Page.Y);

        private void ValidateGameUiComposition(GameUiCharacterCompositionLayout layout,
            GameUiCharacterEquipmentLayout pages, GameUiCharacterAttributeLayout attributes,
            GameUiCharacterControlLayout controls)
        {
            if (pages == null || attributes == null || controls == null)
                throw new InvalidOperationException("Install all character page, attribute and original-control layouts before composition.");
            Point origin = GameUiCharacterPageOrigin(pages);
            Rectangle page = new Rectangle(origin, pages.Character.Page.Size);
            var occupied = layout.GetBounds();
            for (int region = 0; region < occupied.Length; region++)
            {
                Rectangle rect = occupied[region];
                if (!page.Contains(rect)) throw new ArgumentException("Character composition exceeds its page.");
                // Preview is masked around the slot frames; readouts must remain outside them.
                if (region > 0)
                    for (int slot = 0; slot < Grid.Length; slot++)
                    {
                        Rectangle cell = pages.GetEquipmentBounds(slot); cell.Offset(pages.Container.Location);
                        if (rect.IntersectsWith(cell)) throw new ArgumentException("Character composition overlaps equipment.");
                    }
                foreach (var row in controls.Rows)
                    if ((row.Page == GameUiCharacterControlPage.Character || row.Page == GameUiCharacterControlPage.Chrome) && rect.IntersectsWith(row.Bounds))
                        throw new ArgumentException("Character composition overlaps a named control.");
                foreach (var row in attributes.Rows)
                {
                    if (row.Page != GameUiCharacterPage.Character) continue;
                    if (rect.IntersectsWith(row.Value) || (row.Caption.HasValue && rect.IntersectsWith(row.Caption.Value)) ||
                        (row.Icon.HasValue && rect.IntersectsWith(row.Icon.Value)))
                        throw new ArgumentException("Character composition overlaps an attribute.");
                }
                foreach (var group in attributes.GroupCaptions)
                    if (rect.IntersectsWith(group.Value)) throw new ArgumentException("Character composition overlaps an attribute heading.");
            }
        }

        private void ApplyGameUiComposition()
        {
            if (GameUiCompositionLayout == null) return;
            gameUiPreviewRegions = GameUiCompositionLayout.GetPreviewRegions(GameUiEquipmentLayout);
            GameUiReadouts.Relocate(GameUiCompositionLayout.GetReadoutLayout(GameUiCharacterPageOrigin(GameUiEquipmentLayout)));
        }

        private Rectangle[] GameUiPreviewViewports()
        {
            var viewports = new List<Rectangle>();
            foreach (Rectangle region in gameUiPreviewRegions)
            {
                Rectangle viewport = region;
                viewport.Offset(DisplayArea.Location);
                viewport.Intersect(ActiveScene.DisplayArea);
                for (DXControl parent = CharacterTab; parent != null; parent = parent.Parent)
                    viewport.Intersect(parent.DisplayArea);
                if (viewport.Width > 0 && viewport.Height > 0) viewports.Add(viewport);
            }
            return viewports.ToArray();
        }

        private void DrawGameUiPreview(MirLibrary library, int index, int x, int y, Color colour, ImageType type)
        {
            if (GameUiCompositionLayout == null)
            {
                library.Draw(index, x, y, colour, true, 1F, type);
                return;
            }
            MirImage image = library.CreateImage(index, type);
            if (image == null) return;
            Texture texture = type == ImageType.Image ? image.Image : image.Overlay;
            if (texture == null) return;
            Size size = type == ImageType.Image ? new Size(image.Width, image.Height) : new Size(image.OverlayWidth, image.OverlayHeight);
            var bounds = new Rectangle(x + image.OffSetX, y + image.OffSetY, size.Width, size.Height);
            float opacity = DXManager.Opacity;
            try
            {
                DXManager.SetOpacity(1F);
                DrawGameUiPreviewTexture(texture, bounds, colour);
            }
            finally { DXManager.SetOpacity(opacity); }
            image.ExpireTime = Library.Time.Now + Config.CacheDuration;
        }

        private void DrawGameUiPreviewTexture(Texture texture, Rectangle bounds, Color colour)
        {
            if (GameUiCompositionLayout == null)
            {
                PresentTexture(texture, CharacterTab, bounds, colour, this);
                return;
            }
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            foreach (Rectangle viewport in GameUiPreviewViewports())
            {
                Rectangle source; Point destination;
                if (!GameUiCharacterCompositionLayout.TryClip(bounds, viewport, out source, out destination)) continue;
                DXManager.Sprite.Draw(texture, source, Vector3.Zero, new Vector3(destination.X, destination.Y, 0), colour);
                CEnvir.DPSCounter++;
            }
        }
    }
}
