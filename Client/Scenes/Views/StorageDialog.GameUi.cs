using System;
using System.Collections.Generic;
using System.Drawing;
using Client.Controls;
using Client.Envir;
using Library;

namespace Client.Scenes.Views
{
    public sealed partial class StorageDialog
    {
        private readonly DXControl gameUiStorageFilterPanel;
        private readonly DXControl[] gameUiStorageOriginalControls, gameUiStorageOriginalFilters;
        private StoragePositionSnapshot[] gameUiStorageSnapshots;
        private bool gameUiStorageChanging;
        public GameUiStorageLayout GameUiStorageLayout { get; private set; }

        // The owner sizes/skins the window before installation and removes this layout before
        // removing its skin. Only positions are owned: capacity, grid size and live state remain native.
        public void UseGameUiStorage(GameUiStorageLayout layout)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(StorageDialog));
            if (gameUiStorageChanging) throw new InvalidOperationException("Storage layout is already changing.");
            if (layout == null && gameUiStorageSnapshots == null) return;
            if (layout != null) ValidateGameUiStorage(layout);
            var controls = GetGameUiStorageControls();
            var previous = CaptureStoragePositions(controls);
            var original = gameUiStorageSnapshots ?? previous;
            var target = layout == null ? original : CaptureStoragePositions(controls, layout);
            gameUiStorageChanging = true;
            try
            {
                try { RestoreStoragePositions(target); }
                catch (Exception installError)
                {
                    try { RestoreStoragePositions(previous); }
                    catch (Exception rollbackError) { throw new AggregateException("Storage layout and rollback failed.", installError, rollbackError); }
                    throw;
                }
                if (IsDisposed) throw new ObjectDisposedException(nameof(StorageDialog));
                gameUiStorageSnapshots = layout == null ? null : original;
                GameUiStorageLayout = layout;
            }
            finally { gameUiStorageChanging = false; }
        }

        private DXControl[] GetGameUiStorageControls() => gameUiStorageOriginalControls;

        private static StoragePositionSnapshot[] CaptureStoragePositions(DXControl[] controls, GameUiStorageLayout layout = null)
        {
            var result = new StoragePositionSnapshot[controls.Length];
            var bounds = layout?.GetBounds();
            for (int i = 0; i < controls.Length; i++)
                result[i] = new StoragePositionSnapshot(controls[i], bounds == null ? controls[i].Location : bounds[i].Location);
            return result;
        }

        private static void RestoreStoragePositions(StoragePositionSnapshot[] positions)
        {
            List<Exception> errors = null;
            foreach (var position in positions)
            {
                if (position.Control.IsDisposed) continue;
                try { position.Control.Location = position.Location; }
                catch (Exception error) { if (errors == null) errors = new List<Exception>(); errors.Add(error); }
            }
            if (errors != null) throw new AggregateException("A storage control rejected its position.", errors);
        }

        private void ValidateGameUiStorage(GameUiStorageLayout layout)
        {
            if (IsMoving || IsResizing) throw new InvalidOperationException("The storage window is in use.");
            var window = new Rectangle(Point.Empty, Size);
            foreach (var rect in layout.GetBounds())
            {
                if (!window.Contains(rect)) throw new ArgumentException("The storage window is smaller than the layout.", nameof(layout));
                foreach (var chrome in new DXControl[] { TitleLabel, CloseButton })
                    if (chrome != null && !chrome.IsDisposed && chrome.Visible && rect.IntersectsWith(new Rectangle(chrome.Location, chrome.Size)))
                        throw new ArgumentException("Storage content overlaps window chrome.", nameof(layout));
            }
            if (Grid != gameUiStorageOriginalControls[1] || StorageScrollBar != gameUiStorageOriginalControls[2] ||
                ItemNameTextBox != gameUiStorageOriginalFilters[0] || ItemTypeComboBox != gameUiStorageOriginalFilters[1] || ClearButton != gameUiStorageOriginalFilters[2])
                throw new InvalidOperationException("An original storage control was replaced.");
            foreach (var control in GetGameUiStorageControls())
                if (control == null || control.IsDisposed || control.Parent != this || control.IsMoving || control.IsResizing)
                    throw new InvalidOperationException("An original storage control is missing, reparented or in use.");
            if (gameUiStorageFilterPanel.Size != layout.Filter.Size || StorageScrollBar.Size != layout.Scrollbar.Size)
                throw new InvalidOperationException("Original storage control sizes changed.");
            foreach (var child in gameUiStorageFilterPanel.Controls)
                if (child.IsDisposed || child.IsMoving || child.IsResizing ||
                    !new Rectangle(Point.Empty, layout.Filter.Size).Contains(new Rectangle(child.Location, child.Size)))
                    throw new InvalidOperationException("An original filter child does not fit the filter panel.");
            if (ItemNameTextBox == null || ItemNameTextBox.IsDisposed || ItemNameTextBox.Parent != gameUiStorageFilterPanel ||
                ItemTypeComboBox == null || ItemTypeComboBox.IsDisposed || ItemTypeComboBox.Parent != gameUiStorageFilterPanel ||
                ClearButton == null || ClearButton.IsDisposed || ClearButton.Parent != gameUiStorageFilterPanel)
                throw new InvalidOperationException("An original storage filter was replaced or reparented.");
            // Scene startup precedes RefreshStorage: the native placeholder is one 36x36 cell.
            bool initial = Grid.GridSize == new Size(1, 1);
            if ((!initial && (Grid.GridSize.Width != 13 || Grid.GridSize.Height < 10)) || Grid.VisibleHeight != 10 ||
                Grid.Size != (initial ? new Size(36, 36) : layout.Grid.Size) || Grid.GridType != GridType.Storage ||
                Grid.Grid == null || Grid.Grid.Length != Grid.GridSize.Width * Grid.GridSize.Height ||
                Grid.ScrollValue != StorageScrollBar.Value)
                throw new InvalidOperationException("The original storage grid no longer matches the layout.");
            var seen = new HashSet<DXItemCell>();
            for (int i = 0; i < Grid.Grid.Length; i++)
            {
                var cell = Grid.Grid[i];
                if (cell == null || cell.IsDisposed || !seen.Add(cell) || cell.Parent != Grid || cell.HostGrid != Grid ||
                    cell.Slot != i || cell.ItemGrid != Grid.ItemGrid || cell.GridType != GridType.Storage ||
                    cell.Size != new Size(36, 36) || cell.IsMoving || cell.IsResizing)
                    throw new InvalidOperationException("An original storage slot no longer matches the grid.");
            }
        }

        private sealed class StoragePositionSnapshot
        {
            internal readonly DXControl Control;
            internal readonly Point Location;
            internal StoragePositionSnapshot(DXControl control, Point location) { Control = control; Location = location; }
        }
    }
}
