using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Client.Controls;
using Client.Envir;
using Client.UserModels;
using Library;

namespace Client.Scenes.Views
{
    public sealed partial class BeltDialog
    {
        private BeltLayoutSnapshot gameUiBeltSnapshot;
        public GameUiBeltLayout GameUiBeltLayout { get; private set; }

        // Enable on a native horizontal ten-slot belt BEFORE applying the new skin.
        // Disable restores the original skin and size while grid rebuilding is still suppressed.
        public void UseGameUiBelt(GameUiBeltLayout layout)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(BeltDialog));
            if (layout == null) { DisableGameUiBelt(); return; }
            ValidateGameUiBelt(layout);
            var previousLayout = GameUiBeltLayout;
            var previousSize = Size;
            var previousGrid = new Rectangle(Grid.Location, Grid.Size);
            bool starting = gameUiBeltSnapshot == null;
            if (starting) gameUiBeltSnapshot = new BeltLayoutSnapshot(this);
            GameUiBeltLayout = layout;
            try
            {
                AllowResize = false;
                Size = layout.WindowSize;
                ApplyGameUiBeltGrid();
            }
            catch
            {
                if (starting) DisableGameUiBelt();
                else
                {
                    GameUiBeltLayout = previousLayout;
                    Size = previousSize;
                    Grid.Location = previousGrid.Location;
                    Grid.Size = previousGrid.Size;
                    ApplyGameUiBeltGrid();
                }
                throw;
            }
        }

        private void ValidateGameUiBelt(GameUiBeltLayout layout)
        {
            if (IsMoving || IsResizing) throw new InvalidOperationException("The belt window is in use.");
            if (WindowSkin != null && WindowSkin.ConstrainSize(layout.WindowSize) != layout.WindowSize)
                throw new ArgumentException("The belt layout does not match the installed skin size.", nameof(layout));
            if (Grid == null || Grid.IsDisposed || Grid.Parent != this || Grid.IsMoving || Grid.IsResizing ||
                Grid.GridSize != new Size(10, 1) || Grid.Grid == null || Grid.Grid.Length != 10 ||
                Grid.Size != (gameUiBeltSnapshot == null ? new Size(351, 36) : GameUiBeltLayout.Grid.Size) || Grid.ScrollValue != 0 || Grid.VisibleHeight < 1 ||
                Grid.GridType != GridType.Belt || Grid.AllowLink || Links == null || Links.Length != 10)
                throw new InvalidOperationException("Initialize a native horizontal ten-slot belt before binding the layout.");
            if (gameUiBeltSnapshot != null && (Grid != gameUiBeltSnapshot.Grid || Links != gameUiBeltSnapshot.Links))
                throw new InvalidOperationException("The original belt grid or links were replaced.");
            var cells = new HashSet<DXItemCell>();
            for (int i = 0; i < 10; i++)
            {
                var cell = Grid.Grid[i];
                if (Links[i] == null || Links[i].Slot != i || cell == null || cell.IsDisposed || !cells.Add(cell) ||
                    cell.Parent != Grid || cell.HostGrid != Grid || cell.Slot != i || cell.ItemGrid != Grid.ItemGrid ||
                    cell.GridType != GridType.Belt || cell.AllowLink || cell.Size != (gameUiBeltSnapshot == null ? new Size(36, 36) : new Size(41, 40)) ||
                    cell.Location != new Point(i * (gameUiBeltSnapshot == null ? 35 : 41), 0) || !cell.Visible || cell.IsMoving || cell.IsResizing)
                    throw new InvalidOperationException("An original belt slot or link is missing or in use.");
                if (gameUiBeltSnapshot != null && (cell != gameUiBeltSnapshot.Cells[i] || Links[i] != gameUiBeltSnapshot.LinkEntries[i]))
                    throw new InvalidOperationException("An original belt slot or link was replaced.");
            }
        }

        private void ApplyGameUiBeltGrid()
        {
            if (GameUiBeltLayout == null || gameUiBeltSnapshot == null || gameUiBeltSnapshot.Grid.IsDisposed) return;
            gameUiBeltSnapshot.Grid.Location = GameUiBeltLayout.Grid.Location;
            gameUiBeltSnapshot.Grid.Size = GameUiBeltLayout.Grid.Size;
            // The official bitmap owns the rounded slots. Native grid lines would cover their bevels.
            gameUiBeltSnapshot.Grid.Border = false;
            gameUiBeltSnapshot.Grid.DrawTexture = false;
            for (int i = 0; i < gameUiBeltSnapshot.Cells.Length; i++)
            {
                var cell = gameUiBeltSnapshot.Cells[i];
                cell.Location = new Point(i * 41, 0);
                cell.Size = new Size(41, 40);
                var label = gameUiBeltSnapshot.KeyLabels[i];
                label.Location = new Point(1, 0);
                label.Font = gameUiBeltSnapshot.OfficialKeyFont;
                label.ForeColour = Color.FromArgb(214, 143, 66);
            }
        }

        private void DisableGameUiBelt()
        {
            if (gameUiBeltSnapshot == null) return;
            var snapshot = gameUiBeltSnapshot;
            // Keep the snapshot installed until all skin/client-area callbacks have completed.
            GameUiBeltLayout = null;
            WindowSkin = snapshot.Skin;
            Size = snapshot.WindowSize;
            if (!snapshot.Grid.IsDisposed)
            {
                snapshot.Grid.Location = snapshot.GridBounds.Location;
                snapshot.Grid.Size = snapshot.GridBounds.Size;
                snapshot.Grid.Border = snapshot.GridBorder;
                snapshot.Grid.DrawTexture = snapshot.GridDrawTexture;
                for (int i = 0; i < snapshot.Cells.Length; i++)
                {
                    snapshot.Cells[i].Location = snapshot.CellBounds[i].Location;
                    snapshot.Cells[i].Size = snapshot.CellBounds[i].Size;
                    snapshot.KeyLabels[i].Location = snapshot.LabelLocations[i];
                    snapshot.KeyLabels[i].Font = snapshot.LabelFonts[i];
                    snapshot.KeyLabels[i].ForeColour = snapshot.LabelColours[i];
                }
            }
            AllowResize = snapshot.AllowResize;
            gameUiBeltSnapshot = null;
            snapshot.OfficialKeyFont.Dispose();
        }

        private sealed class BeltLayoutSnapshot
        {
            internal readonly DXItemGrid Grid;
            internal readonly DXItemCell[] Cells;
            internal readonly ClientBeltLink[] Links, LinkEntries;
            internal readonly Rectangle GridBounds;
            internal readonly Size WindowSize;
            internal readonly GameUiWindowSkin Skin;
            internal readonly bool AllowResize, GridBorder, GridDrawTexture;
            internal readonly Rectangle[] CellBounds;
            internal readonly DXLabel[] KeyLabels;
            internal readonly Point[] LabelLocations;
            internal readonly Font[] LabelFonts;
            internal readonly Color[] LabelColours;
            internal readonly Font OfficialKeyFont;
            internal BeltLayoutSnapshot(BeltDialog window)
            {
                Grid = window.Grid;
                Cells = (DXItemCell[])Grid.Grid.Clone();
                CellBounds = Array.ConvertAll(Cells, c => new Rectangle(c.Location, c.Size));
                KeyLabels = Cells.Select(c => c.Controls.OfType<DXLabel>().Single(l => l.Text == ((c.Slot + 1) % 10).ToString())).ToArray();
                LabelLocations = KeyLabels.Select(l => l.Location).ToArray();
                LabelFonts = KeyLabels.Select(l => l.Font).ToArray();
                LabelColours = KeyLabels.Select(l => l.ForeColour).ToArray();
                OfficialKeyFont = new Font(LabelFonts[0], FontStyle.Regular);
                GridBorder = Grid.Border;
                GridDrawTexture = Grid.DrawTexture;
                Links = window.Links;
                LinkEntries = (ClientBeltLink[])Links.Clone();
                GridBounds = new Rectangle(Grid.Location, Grid.Size);
                WindowSize = window.Size;
                Skin = window.WindowSkin;
                AllowResize = window.AllowResize;
            }
        }
    }
}
