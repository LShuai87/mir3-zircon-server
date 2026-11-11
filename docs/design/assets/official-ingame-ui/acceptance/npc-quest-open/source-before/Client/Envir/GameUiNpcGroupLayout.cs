using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Client.Controls;
using Client.Scenes;

namespace Client.Envir
{
    // Transient NPC geometry only. The response owns visibility, controls and actions.
    internal static class GameUiNpcGroupLayout
    {
        private const int Gap = 8;
        private sealed class Entry
        {
            internal DXWindow Window;
            internal Size Size;
            internal int Header;
            internal Point Preferred;
        }

        internal static void Apply(GameScene game, Size resolution)
        {
            var card = game.NPCBox;
            if (card == null || !card.IsVisible || card.IsResponseLayoutPending) return;
            var area = new Rectangle(0, 0, resolution.Width, resolution.Height - 202);
            var services = new DXWindow[] {
                game.NPCGoodsBox, game.NPCSellBox, game.NPCRepairBox, game.NPCRefinementStoneBox,
                game.NPCRefineBox, game.NPCRefineRetrieveBox, game.NPCCompanionStorageBox,
                game.NPCAdoptCompanionBox, game.NPCWeddingRingBox, game.NPCMasterRefineBox,
                game.NPCItemFragmentBox, game.NPCAccessoryUpgradeBox, game.NPCAccessoryLevelBox,
                game.NPCAccessoryResetBox, game.NPCWeaponCraftBox, game.NPCQuestBox
            }.Where(w => w != null && !w.IsDisposed && w.IsVisible).ToArray();
            // Keep each service's complete native client area. Paired services form one
            // non-overlapping row (or rows), and move as a single unit beside the card.
            var offsets = new Dictionary<DXWindow, Point>();
            int x = 0, y = 0, rowHeight = 0, width = 0;
            foreach (var window in services)
            {
                if (x != 0 && x + window.Size.Width > area.Width)
                { y += rowHeight + Gap; x = 0; rowHeight = 0; }
                offsets.Add(window, new Point(x, y));
                x += window.Size.Width + Gap;
                width = Math.Max(width, x - Gap);
                rowHeight = Math.Max(rowHeight, window.Size.Height);
            }
            var entries = new List<Entry> { Describe(card, Point.Empty) };
            foreach (var window in new DXWindow[] { game.StorageBox, game.InventoryBox })
                if (window != null && !window.IsDisposed && window.IsVisible)
                    entries.Add(Describe(window, window.Location));
            if (services.Length != 0)
                entries.Add(new Entry { Size = new Size(width, y + rowHeight), Header = 1,
                    Preferred = new Point(0, card.Size.Height + Gap) });
            var bounds = new Rectangle[entries.Count];
            bounds[0] = new Rectangle(Point.Empty, card.Size);
            if (!area.Contains(bounds[0]) || entries.Any(e => e.Size.Width > area.Width || e.Size.Height > area.Height))
                throw new InvalidOperationException("NPC group requires an implemented viewport for its oversized content.");
            long total = entries.Sum(e => (long)e.Size.Width * e.Size.Height);
            bool placed = total <= (long)area.Width * area.Height && Place(entries, bounds, 1, area, false);
            if (!placed) placed = Place(entries, bounds, 1, area, true);
            if (!placed) throw new InvalidOperationException("NPC group has no reachable title/close layout at this resolution.");
            using (DXWindow.SuppressSceneLayoutWrites(game))
            {
                // Solve the complete group before mutating a control. No intermediate
                // response layout is observed, and hidden windows are never repositioned.
                for (int i = 0; i < entries.Count; i++)
                    if (entries[i].Window != null) entries[i].Window.Location = bounds[i].Location;
                if (services.Length != 0)
                    foreach (var window in services)
                        window.Location = Point.Add(bounds[bounds.Length - 1].Location, new Size(offsets[window]));
                foreach (var entry in entries) entry.Window?.BringToFront();
                foreach (var window in services) window.BringToFront();
                // BringToFront cannot order Sort=false tooltip roots. Preserve their
                // sibling sequence and keep native modal/tooltip overlays above services.
                var overlays = game.Controls.Where(c => c.Modal || c == game.ItemLabel || c == game.MagicLabel).ToArray();
                foreach (var overlay in overlays) game.Controls.Remove(overlay);
                game.Controls.AddRange(overlays);
            }
            if (DXControl.ActiveScene == game)
                game.OnMouseMove(new MouseEventArgs(MouseButtons.None, 0,
                    CEnvir.MouseLocation.X, CEnvir.MouseLocation.Y, 0));
        }

        private static Entry Describe(DXWindow window, Point preferred)
        {
            int header = Math.Max(window.ClientArea.Top, window.CloseButton.Location.Y + window.CloseButton.Size.Height);
            if (window.WindowSkin != null)
                header = Math.Max(header, Math.Max(window.WindowSkin.TitleArea.Bottom, window.WindowSkin.DragArea.Bottom));
            return new Entry { Window = window, Size = window.Size, Preferred = preferred,
                Header = Math.Max(1, Math.Min(header, window.Size.Height)) };
        }

        // First seek disjoint complete windows. If they cannot fit together, allow
        // bodies to overlap while preserving every underlying title and close strip.
        // Services are last, so their entire operation area remains on top.
        private static bool Place(List<Entry> entries, Rectangle[] bounds, int index, Rectangle area, bool overlapBodies)
        {
            if (index == entries.Count) return true;
            var entry = entries[index];
            var xs = new HashSet<int> { area.Left, area.Right - entry.Size.Width, entry.Preferred.X };
            var ys = new HashSet<int> { area.Top, area.Bottom - entry.Size.Height, entry.Preferred.Y };
            for (int i = 0; i < index; i++)
            {
                var previous = bounds[i];
                xs.Add(previous.Right + Gap); xs.Add(previous.Left - entry.Size.Width - Gap);
                ys.Add(previous.Bottom + Gap); ys.Add(previous.Top - entry.Size.Height - Gap);
                ys.Add(previous.Top + entries[i].Header + Gap);
            }
            var candidates = (from px in xs from py in ys
                let rect = new Rectangle(px, py, entry.Size.Width, entry.Size.Height)
                where area.Contains(rect)
                orderby Math.Abs((long)px - entry.Preferred.X) + Math.Abs((long)py - entry.Preferred.Y), py, px
                select rect).ToArray();
            foreach (var candidate in candidates)
            {
                bool clear = true;
                for (int i = 0; i < index; i++)
                {
                    var obstacle = bounds[i];
                    if (overlapBodies) obstacle.Height = entries[i].Header;
                    if (candidate.IntersectsWith(obstacle)) { clear = false; break; }
                }
                if (!clear) continue;
                bounds[index] = candidate;
                if (Place(entries, bounds, index + 1, area, overlapBodies)) return true;
            }
            return false;
        }
    }
}
