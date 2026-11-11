using System;
using System.Collections.Generic;
using System.Drawing;
using Client.Controls;
using Client.Envir;
using Library;

namespace Client.Scenes.Views
{
    public sealed partial class CharacterDialog
    {
        public MirLibrary GameUiCharacterBackdropLibrary { get; private set; }
        public int GameUiCharacterBackdropFrame { get; private set; } = -1;

        // A page-only background retains official ornament without drawing it behind other tabs.
        public void UseGameUiCharacterBackdrop(MirLibrary library, int frame)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(CharacterDialog));
            if (library == null)
            {
                GameUiCharacterBackdropLibrary = null;
                GameUiCharacterBackdropFrame = -1;
                return;
            }
            if (GameUiEquipmentLayout == null || CharacterTab.IsDisposed ||
                library.GetSize(frame) != CharacterTab.Size || library.GetOffSet(frame) != Point.Empty)
                throw new ArgumentException("Character backdrop must match the installed page at zero offset.", nameof(frame));
            GameUiCharacterBackdropLibrary = library;
            GameUiCharacterBackdropFrame = frame;
        }

        private void DrawGameUiCharacterBackdrop()
        {
            if (GameUiCharacterBackdropLibrary == null) return;
            MirImage image = GameUiCharacterBackdropLibrary.CreateImage(GameUiCharacterBackdropFrame, ImageType.Image);
            if (image?.Image == null)
                throw new InvalidOperationException("The installed character backdrop is unavailable.");
            bool oldBlend = DXManager.Blending;
            float oldRate = DXManager.BlendRate, oldOpacity = DXManager.Opacity;
            try
            {
                DXManager.SetBlend(false);
                DXManager.SetOpacity(1F);
                DXControl.PresentTexture(image.Image, CharacterTab.Parent, CharacterTab.DisplayArea,
                    CharacterTab.IsEnabled ? Color.White : Color.FromArgb(75, 75, 75), CharacterTab);
            }
            finally
            {
                DXManager.SetBlend(oldBlend, oldRate);
                DXManager.SetOpacity(oldOpacity);
            }
            image.ExpireTime = Time.Now + Config.CacheDuration;
        }

        private DXControl gameUiReservedBelt;
        private DXItemCell[] gameUiFramedEquipmentCells;
        public MirLibrary GameUiEquipmentCellLibrary { get; private set; }
        public int GameUiEquipmentCellFrame { get; private set; } = -1;

        // Borrow the preflight-owned frame; attach only to existing item cells, below their normal draw path.
        public void UseGameUiEquipmentCellFrame(MirLibrary library, int frame)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(CharacterDialog));
            if (library == null) { DisableGameUiEquipmentCellFrame(); return; }
            if (GameUiEquipmentLayout == null)
                throw new InvalidOperationException("Install the equipment pages before their cell frame.");
            if (library.GetSize(frame) != new Size(36, 36) || library.GetOffSet(frame) != Point.Empty)
                throw new ArgumentException("Equipment cell frames must be native 36x36 pixels with zero offset.", nameof(frame));
            if (Grid == null || Grid.Length != 16)
                throw new InvalidOperationException("Equipment cells are unavailable.");
            for (int i = 0; i < Grid.Length; i++)
            {
                DXItemCell cell = Grid[i];
                DXTab parent = CharacterTab;
                if (cell == null || cell.IsDisposed || cell.Size != new Size(36, 36) || cell.Parent != parent ||
                    (gameUiFramedEquipmentCells != null && cell != gameUiFramedEquipmentCells[i]))
                    throw new InvalidOperationException("Equipment cells no longer match their pages.");
            }
            if (gameUiFramedEquipmentCells == null)
            {
                gameUiFramedEquipmentCells = (DXItemCell[])Grid.Clone();
                foreach (DXItemCell cell in gameUiFramedEquipmentCells) cell.BeforeDraw += DrawGameUiEquipmentCellFrame;
            }
            GameUiEquipmentCellLibrary = library;
            GameUiEquipmentCellFrame = frame;
        }

        private void DrawGameUiEquipmentCellFrame(object sender, EventArgs e)
        {
            var cell = (DXItemCell)sender;
            if (GameUiCharacterBackdropLibrary != null && cell != Grid[(int)EquipmentSlot.Poison]) return;
            MirImage image = GameUiEquipmentCellLibrary.CreateImage(GameUiEquipmentCellFrame, ImageType.Image);
            if (image?.Image == null)
                throw new InvalidOperationException("The installed equipment cell frame is unavailable.");
            bool oldBlend = DXManager.Blending;
            float oldRate = DXManager.BlendRate, oldOpacity = DXManager.Opacity;
            try
            {
                DXManager.SetBlend(false);
                DXManager.SetOpacity(1F);
                DXControl.PresentTexture(image.Image, cell.Parent, cell.DisplayArea,
                    cell.IsEnabled ? Color.White : Color.FromArgb(75, 75, 75), cell);
            }
            finally
            {
                DXManager.SetBlend(oldBlend, oldRate);
                DXManager.SetOpacity(oldOpacity);
            }
            image.ExpireTime = Time.Now + Config.CacheDuration;
        }

        private void DisableGameUiEquipmentCellFrame()
        {
            if (gameUiFramedEquipmentCells != null)
                foreach (DXItemCell cell in gameUiFramedEquipmentCells) cell.BeforeDraw -= DrawGameUiEquipmentCellFrame;
            gameUiFramedEquipmentCells = null;
            GameUiEquipmentCellLibrary = null;
            GameUiEquipmentCellFrame = -1;
        }

        private EquipmentPageSnapshot equipmentPageSnapshot;
        public GameUiCharacterEquipmentLayout GameUiEquipmentLayout { get; private set; }

        // The caller installs and sizes the window skin first. No assets or game settings are loaded here.
        public void UseGameUiEquipmentPages(GameUiCharacterEquipmentLayout layout)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(CharacterDialog));
            if (layout == null) { DisableGameUiEquipmentPages(); return; }
            if (!new Rectangle(Point.Empty, Size).Contains(layout.Container))
                throw new ArgumentException("The character window is smaller than the layout.", nameof(layout));
            if (Grid == null || Grid.Length != 16 || TabControl.IsDisposed)
                throw new InvalidOperationException("Character controls are unavailable.");
            foreach (DXTab tab in new[] { CharacterTab, StatsTab, HermitTab })
                if (tab != null && (tab.IsDisposed || tab.Parent != TabControl || tab.IsMoving || tab.IsResizing || tab.TabButton.IsMoving || tab.TabButton.IsResizing))
                    throw new InvalidOperationException("Character tabs are moving, resizing or disposed.");
            foreach (DXItemCell cell in Grid)
                if (cell == null || cell.IsDisposed || cell.Size != new Size(36, 36) ||
                    cell.Parent != CharacterTab)
                    throw new InvalidOperationException("Equipment cells no longer match the character page.");

            if (GameUiAttributeLayout != null) ValidateGameUiAttributes(GameUiAttributeLayout, layout);

            if (GameUiControlLayout != null) ValidateGameUiControls(GameUiControlLayout, layout, GameUiAttributeLayout);

            if (GameUiCompositionLayout != null) ValidateGameUiComposition(GameUiCompositionLayout, layout, GameUiAttributeLayout, GameUiControlLayout);

            if (equipmentPageSnapshot == null)
                equipmentPageSnapshot = new EquipmentPageSnapshot(TabControl, CharacterTab, StatsTab, HermitTab, Grid);
            TabControl.Location = layout.Container.Location;
            TabControl.Size = layout.Container.Size;
            CharacterTab.UseFixedGeometry(layout.Character);
            StatsTab.UseFixedGeometry(layout.Status);
            HermitTab.UseFixedGeometry(layout.Hermit);
            for (int i = 0; i < Grid.Length; i++)
            {
                DXTab parent = CharacterTab;
                Rectangle rect = layout.GetEquipmentBounds(i);
                Grid[i].Parent = parent;
                Grid[i].Location = new Point(rect.X - parent.Location.X, rect.Y - parent.Location.Y);
            }
            if (gameUiReservedBelt == null)
                gameUiReservedBelt = new DXControl { Parent = CharacterTab, Size = new Size(36, 36), Hint = "腰带（暂未开放）" };
            Rectangle belt = layout.GetEquipmentBounds((int)EquipmentSlot.Torch);
            gameUiReservedBelt.Location = new Point(belt.X - CharacterTab.Location.X, belt.Y - CharacterTab.Location.Y);
            Grid[(int)EquipmentSlot.Emblem].Hint = "勋章 / 蜡烛 / 火把（共用）";
            Grid[(int)EquipmentSlot.Poison].Hint = "毒药";
            Grid[(int)EquipmentSlot.Amulet].Hint = "护符";
            Grid[(int)EquipmentSlot.Flower].Hint = "功能道具";
            GameUiEquipmentLayout = layout;
            ApplyGameUiAttributes();
            ApplyGameUiControls();
            ApplyGameUiComposition();
            TabControl.TabsChanged();
        }

        private void DisableGameUiEquipmentPages()
        {
            DisableGameUiCompanionCards();
            gameUiReservedBelt?.Dispose();
            gameUiReservedBelt = null;
            UseGameUiCharacterBackdrop(null, -1);
            DisableGameUiEquipmentCellFrame();
            DisableGameUiComposition();
            DisableGameUiControls();
            DisableGameUiAttributes();
            if (equipmentPageSnapshot == null) return;
            DXTab selected = TabControl.SelectedTab;
            foreach (DXTab tab in new[] { CharacterTab, StatsTab, HermitTab })
                if (tab != null && !tab.IsDisposed) tab.UseFixedGeometry(null);
            equipmentPageSnapshot.Restore(TabControl, Grid);
            TabControl.TabsChanged();
            if (selected != null && !selected.IsDisposed) TabControl.SelectedTab = selected;
            equipmentPageSnapshot = null;
            GameUiEquipmentLayout = null;
        }

        private sealed class EquipmentPageSnapshot
        {
            private readonly Rectangle container;
            private readonly DXTab[] tabs;
            private readonly Rectangle[] bounds;
            private readonly DXTabGeometry[] geometries;
            private readonly DXControl[] parents;
            private readonly Point[] locations;
            private readonly List<DXControl>[] children;

            internal EquipmentPageSnapshot(DXTabControl control, DXTab character, DXTab stats, DXTab hermit, DXItemCell[] cells)
            {
                container = new Rectangle(control.Location, control.Size);
                tabs = new[] { character, stats, hermit };
                bounds = new Rectangle[tabs.Length]; geometries = new DXTabGeometry[tabs.Length];
                children = new List<DXControl>[tabs.Length];
                for (int i = 0; i < tabs.Length; i++)
                {
                    bounds[i] = new Rectangle(tabs[i].Location, tabs[i].Size);
                    geometries[i] = tabs[i].FixedGeometry;
                    children[i] = new List<DXControl>(tabs[i].Controls);
                }
                parents = new DXControl[cells.Length]; locations = new Point[cells.Length];
                for (int i = 0; i < cells.Length; i++) { parents[i] = cells[i].Parent; locations[i] = cells[i].Location; }
            }

            internal void Restore(DXTabControl control, DXItemCell[] cells)
            {
                control.Location = container.Location; control.Size = container.Size;
                for (int i = 0; i < tabs.Length; i++)
                {
                    if (geometries[i] != null) tabs[i].UseFixedGeometry(geometries[i]);
                    else { tabs[i].Location = bounds[i].Location; tabs[i].Size = bounds[i].Size; }
                }
                for (int i = 0; i < cells.Length; i++) { cells[i].Parent = parents[i]; cells[i].Location = locations[i]; }
                // Reparenting appends children. Restore original z-order while retaining later readout bindings.
                for (int i = 0; i < tabs.Length; i++)
                {
                    foreach (DXControl child in children[i]) tabs[i].Controls.Remove(child);
                    tabs[i].Controls.InsertRange(0, children[i]);
                }
                control.TabsChanged();
            }
        }
    }
}
