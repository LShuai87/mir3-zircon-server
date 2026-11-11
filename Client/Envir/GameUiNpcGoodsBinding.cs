using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Client.Controls;
using Client.Scenes.Views;

namespace Client.Envir
{
    // Reuses original cells and handlers. Owns layout and display hints, never goods or purchase state.
    internal sealed class GameUiNpcGoodsBinding : IDisposable
    {
        // Seven rows: 2px top inset + 7 * 46px = the full 324px viewport.
        private const int RowPitch = 46, TopInset = 2, ViewHeight = 324;
        private readonly NPCGoodsDialog window;
        private readonly DXVScrollBar scroll;
        private readonly DXButton buy;
        private readonly Action restoreCurrentGoods;
        private readonly Size originalSize;
        private readonly ControlState[] controls, scrollParts;
        private readonly int originalChange, originalMax, originalVisible, originalValue;
        private int installedValue;
        private readonly Dictionary<NPCGoodsCell, CellState> cells = new Dictionary<NPCGoodsCell, CellState>();
        private bool refreshed, restoring, disposed, nativeRestorePending;

        internal GameUiNpcGoodsBinding(NPCGoodsDialog window, DXVScrollBar scroll, DXButton buy, Action restoreCurrentGoods)
        {
            this.window = window; this.scroll = scroll; this.buy = buy; this.restoreCurrentGoods = restoreCurrentGoods;
            originalSize = window.Size; originalChange = scroll.Change;
            originalMax = scroll.MaxValue; originalVisible = scroll.VisibleSize; originalValue = scroll.Value;
            controls = new DXControl[] { scroll, buy, window.GuildCheckBox }.Select(c => new ControlState(c)).ToArray();
            scrollParts = new DXControl[] { scroll.UpButton, scroll.DownButton, scroll.PositionBar }.Select(c => new ControlState(c)).ToArray();
        }
        internal void Install(GameUiWindowSkin skin)
        {
            if (skin.FrameSize != new Size(344, 438) || !skin.FixedSize || skin.GetClientArea(skin.FrameSize) != new Rectangle(24, 48, 280, ViewHeight))
                throw new ArgumentException("Goods skin requires the verified 344x438 two-column content region.", nameof(skin));
            int firstRow = scroll.Value / 43 / 2;
            window.WindowSkin = skin;
            RefreshCore();
            scroll.Value = firstRow * RowPitch;
            installedValue = scroll.Value;
            Locate();
        }
        internal void Refresh()
        {
            refreshed = true;
            RefreshCore();
            scroll.Value = 0;
            Locate();
        }
        private void RefreshCore()
        {
            foreach (var stale in cells.Keys.Where(c => c.IsDisposed || !window.Cells.Contains(c)).ToArray())
            { cells[stale].Restore(); cells.Remove(stale); }
            foreach (var cell in window.Cells)
            {
                if (!cells.ContainsKey(cell)) cells.Add(cell, new CellState(cell));
                cells[cell].Apply();
            }
            scroll.Location = new Point(308, 48); scroll.Size = new Size(14, ViewHeight);
            scroll.VisibleSize = ViewHeight; scroll.Change = RowPitch;
            scroll.MaxValue = Math.Max(ViewHeight, ((window.Cells.Count + 1) / 2) * RowPitch + TopInset);
            scroll.Value = Math.Max(0, Math.Min(scroll.Value, scroll.MaxValue - ViewHeight));
            buy.Location = new Point(164, 400);
            window.GuildCheckBox.Location = new Point(24, 376);
        }
        internal void Locate()
        {
            if (disposed || restoring) return;
            for (int i = 0; i < window.Cells.Count; i++)
                window.Cells[i].Location = new Point(i % 2 * 140, TopInset + i / 2 * RowPitch - scroll.Value);
        }
        public void Dispose()
        {
            if (disposed) return;
            restoring = true;
            try
            {
                foreach (var state in cells.Values) state.Restore();
                if (!window.IsDisposed)
                {
                    int nativeValue = !refreshed && scroll.Value == installedValue ? originalValue : scroll.Value / RowPitch * 2 * 43;
                    // A previous observer may have thrown after assigning Size but before
                    // DXWindow updated its client area/chrome. Complete that notification on retry.
                    if (nativeRestorePending) window.OnSizeChanged(window.Size, window.Size);
                    nativeRestorePending = true;
                    window.WindowSkin = null;
                    window.Size = originalSize;
                    foreach (var state in controls) state.Restore();
                    scroll.Change = originalChange;
                    // NewGoods can replace all cells while adapted; restore the current collection.
                    if (refreshed) restoreCurrentGoods();
                    else
                    {
                        scroll.VisibleSize = originalVisible;
                        scroll.MaxValue = originalMax;
                    }
                    scroll.Value = Math.Max(0, Math.Min(nativeValue, scroll.MaxValue - scroll.VisibleSize));
                    if (!refreshed && scroll.Value == originalValue)
                        foreach (var state in scrollParts) state.Restore();
                }
                nativeRestorePending = false;
                disposed = true;
            }
            finally { restoring = false; }
        }
        private sealed class ControlState
        {
            private readonly DXControl control;
            private readonly Rectangle bounds;
            internal ControlState(DXControl control) { this.control = control; bounds = new Rectangle(control.Location, control.Size); }
            internal void Restore() { if (!control.IsDisposed) { control.Size = bounds.Size; control.Location = bounds.Location; } }
        }
        private sealed class LabelState
        {
            private readonly DXLabel label;
            private readonly Rectangle bounds;
            private readonly bool autoSize;
            private readonly TextFormatFlags format;
            private readonly string hint;
            private string generatedHint;
            private bool ownsHint;
            internal LabelState(DXLabel label)
            {
                this.label = label; bounds = new Rectangle(label.Location, label.Size);
                autoSize = label.AutoSize; format = label.DrawFormat; hint = label.Hint; ownsHint = hint == null;
                label.TextChanged += TextChanged;
            }
            private void TextChanged(object sender, EventArgs e) { RefreshHint(); }
            private void RefreshHint()
            {
                if (!ownsHint || label.IsDisposed) return;
                if (label.Hint != generatedHint) { ownsHint = false; return; }
                label.Hint = generatedHint = label.Text;
            }
            internal void Apply(Rectangle area)
            {
                label.AutoSize = false;
                label.DrawFormat = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                label.Location = area.Location; label.Size = area.Size; RefreshHint();
            }
            internal void Restore()
            {
                label.TextChanged -= TextChanged;
                if (label.IsDisposed) return;
                if (ownsHint && label.Hint == generatedHint) label.Hint = hint;
                label.AutoSize = false; label.DrawFormat = format;
                label.Location = bounds.Location; label.Size = bounds.Size; label.AutoSize = autoSize;
            }
        }
        private sealed class CellState
        {
            private readonly NPCGoodsCell cell;
            private readonly ControlState[] controls;
            private readonly LabelState name, requirement, cost;
            private readonly DXLabel[] labels;
            private readonly string originalHint;
            private string generatedHint;
            private bool ownsHint;
            internal CellState(NPCGoodsCell cell)
            {
                this.cell = cell;
                originalHint = cell.Hint; ownsHint = originalHint == null;
                controls = new DXControl[] { cell, cell.ItemCell, cell.GoldIcon }.Select(c => new ControlState(c)).ToArray();
                name = new LabelState(cell.ItemNameLabel); requirement = new LabelState(cell.RequirementLabel); cost = new LabelState(cell.CostLabel);
                cell.GoodChanged += GoodChanged;
                labels = new[] { cell.ItemNameLabel, cell.RequirementLabel, cell.CostLabel };
                foreach (var label in labels) label.TextChanged += TextChanged;
            }
            private void GoodChanged(object sender, EventArgs e) { Apply(); }
            private void TextChanged(object sender, EventArgs e) { RefreshHint(); }
            private void RefreshHint()
            {
                if (!ownsHint || cell.IsDisposed) return;
                if (cell.Hint != generatedHint) { ownsHint = false; return; }
                // Labels are IsControl=false: the original row receives hover and clicks.
                cell.Hint = generatedHint = string.Join("\n", labels.Select(label => label.Text).Where(text => !string.IsNullOrEmpty(text)));
            }
            internal void Apply()
            {
                cell.Size = new Size(138, 45);
                cell.ItemCell.Location = new Point(4, 4); // Preserve the 36x36 hit target and item instance.
                name.Apply(new Rectangle(44, 0, 92, 15));
                requirement.Apply(new Rectangle(44, 15, 92, 15));
                cost.Apply(new Rectangle(44, 30, 78, 15));
                cell.GoldIcon.Location = new Point(cell.Size.Width - cell.GoldIcon.Size.Width - 1,
                    cell.Size.Height - cell.GoldIcon.Size.Height - 1);
                RefreshHint();
            }
            internal void Restore()
            {
                cell.GoodChanged -= GoodChanged;
                foreach (var label in labels) label.TextChanged -= TextChanged;
                if (!cell.IsDisposed && ownsHint && cell.Hint == generatedHint) cell.Hint = originalHint;
                name.Restore(); requirement.Restore(); cost.Restore();
                if (cell.IsDisposed) return;
                foreach (var state in controls) state.Restore();
                cell.CostLabel.Location = new Point(cell.GoldIcon.Location.X - cell.CostLabel.Size.Width,
                    cell.GoldIcon.Location.Y + cell.GoldIcon.Size.Height - cell.CostLabel.Size.Height);
            }
        }
    }
}
