using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Client.Controls;
using Client.Envir;
using Library;

namespace Client.Scenes.Views
{
    public sealed partial class CharacterDialog
    {
        // Captured at construction; captions are never discovered by their display text.
        private readonly Dictionary<string, DXLabel> gameUiStatCaptions = new Dictionary<string, DXLabel>(StringComparer.Ordinal);
        private readonly Dictionary<string, DXLabel> gameUiGroupCaptions = new Dictionary<string, DXLabel>(StringComparer.Ordinal);
        private List<AttributeControlSnapshot> attributeSnapshots;
        private Dictionary<DXControl, DXControl[]> attributeChildOrder;
        public GameUiCharacterAttributeLayout GameUiAttributeLayout { get; private set; }

        public void UseGameUiAttributes(GameUiCharacterAttributeLayout layout)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(CharacterDialog));
            if (layout == null) { DisableGameUiAttributes(); return; }
            ValidateGameUiAttributes(layout, GameUiEquipmentLayout);
            if (GameUiControlLayout != null) ValidateGameUiControls(GameUiControlLayout, GameUiEquipmentLayout, layout);
            if (GameUiCompositionLayout != null) ValidateGameUiComposition(GameUiCompositionLayout, GameUiEquipmentLayout, layout, GameUiControlLayout);
            if (attributeSnapshots == null)
            {
                attributeSnapshots = new List<AttributeControlSnapshot>();
                attributeChildOrder = new Dictionary<DXControl, DXControl[]>();
                foreach (var pair in GetGameUiStatControls())
                {
                    RememberAttribute(pair.Value, true);
                    DXLabel caption;
                    if (gameUiStatCaptions.TryGetValue(pair.Key, out caption)) RememberAttribute(caption, false);
                    else RememberAttribute((DXImageControl)pair.Value.Tag, false);
                }
                foreach (DXLabel caption in gameUiGroupCaptions.Values) RememberAttribute(caption, false);
            }
            GameUiAttributeLayout = layout;
            ApplyGameUiAttributes();
        }

        private Dictionary<string, DXLabel> GetGameUiStatControls()
        {
            var result = new Dictionary<string, DXLabel>(StringComparer.Ordinal);
            AddAttributeGroup(result, "DisplayStats", DisplayStats);
            AddAttributeGroup(result, "AttackStats", AttackStats);
            AddAttributeGroup(result, "AdvantageStats", AdvantageStats);
            AddAttributeGroup(result, "DisadvantageStats", DisadvantageStats);
            AddAttributeGroup(result, "HermitDisplayStats", HermitDisplayStats);
            AddAttributeGroup(result, "HermitAttackStats", HermitAttackStats);
            return result;
        }

        private static void AddAttributeGroup(Dictionary<string, DXLabel> result, string name, Dictionary<Stat, DXLabel> group)
        {
            if (group == null) throw new InvalidOperationException("Character attribute group is unavailable.");
            foreach (var pair in group) result.Add(name + "." + pair.Key, pair.Value);
        }

        private void ValidateGameUiAttributes(GameUiCharacterAttributeLayout layout, GameUiCharacterEquipmentLayout pages)
        {
            if (pages == null)
                throw new InvalidOperationException("Install the character equipment pages before attribute placement.");
            var controls = GetGameUiStatControls();
            if (controls.Count != 57 || gameUiStatCaptions.Count != 27 || gameUiGroupCaptions.Count != 4)
                throw new InvalidOperationException("Original character attribute controls no longer match the layout.");
            var used = new HashSet<DXControl>();
            var occupied = new Dictionary<GameUiCharacterPage, List<Rectangle>>();
            foreach (var row in layout.Rows)
            {
                DXLabel value, caption;
                if (!controls.TryGetValue(row.Id, out value)) throw new ArgumentException("Unknown attribute ID: " + row.Id, nameof(layout));
                ValidateAttributeControl(value, used);
                AddAttributeBounds(occupied, pages, row.Page, row.Value);
                if (gameUiStatCaptions.TryGetValue(row.Id, out caption))
                {
                    if (!row.Caption.HasValue || row.Icon.HasValue) throw new ArgumentException("The original stat caption is required: " + row.Id);
                    ValidateAttributeControl(caption, used);
                    AddAttributeBounds(occupied, pages, row.Page, row.Caption.Value);
                }
                else
                {
                    var icon = value.Tag as DXImageControl;
                    if (!row.Icon.HasValue || row.Caption.HasValue || icon == null)
                        throw new ArgumentException("The original Tag icon is required: " + row.Id);
                    ValidateAttributeControl(icon, used);
                    AddAttributeBounds(occupied, pages, row.Page, row.Icon.Value);
                }
            }
            foreach (var group in layout.GroupCaptions)
            {
                ValidateAttributeControl(gameUiGroupCaptions[group.Key], used);
                AddAttributeBounds(occupied, pages, GameUiCharacterPage.Character, group.Value);
            }
            ValidateAttributeControl(gameUiGroupCaptions["HermitAttackStats"], used);
        }

        private void ValidateAttributeControl(DXControl control, HashSet<DXControl> used)
        {
            if (control == null || control.IsDisposed || !used.Add(control) ||
                (control.Parent != CharacterTab && control.Parent != StatsTab && control.Parent != HermitTab))
                throw new InvalidOperationException("Original attribute control is missing, replaced, disposed or reparented.");
        }

        private static void AddAttributeBounds(Dictionary<GameUiCharacterPage, List<Rectangle>> occupied,
            GameUiCharacterEquipmentLayout pages, GameUiCharacterPage page, Rectangle bounds)
        {
            var geometry = page == GameUiCharacterPage.Character ? pages.Character : page == GameUiCharacterPage.Status ? pages.Status : pages.Hermit;
            var windowPage = geometry.Page; windowPage.Offset(pages.Container.Location);
            if (!windowPage.Contains(bounds)) throw new ArgumentException("Attribute bounds exceed the owning page.");
            if (page == GameUiCharacterPage.Character)
            {
                for (int slot = 0; slot < 16; slot++)
                    {
                        var cell = pages.GetEquipmentBounds(slot); cell.Offset(pages.Container.Location);
                        if (cell.IntersectsWith(bounds)) throw new ArgumentException("Attribute bounds overlap equipment.");
                    }
            }
            List<Rectangle> rectangles;
            if (!occupied.TryGetValue(page, out rectangles)) occupied.Add(page, rectangles = new List<Rectangle>());
            foreach (var rectangle in rectangles)
                if (rectangle.IntersectsWith(bounds)) throw new ArgumentException("Attribute controls overlap on the same page.");
            rectangles.Add(bounds);
        }

        private void RememberAttribute(DXControl control, bool value)
        {
            if (!attributeChildOrder.ContainsKey(control.Parent))
                attributeChildOrder.Add(control.Parent, control.Parent.Controls.ToArray());
            var snapshot = new AttributeControlSnapshot(control, value);
            attributeSnapshots.Add(snapshot);
            if (value) control.TextChanged += GameUiAttributeTextChanged;
        }

        private void GameUiAttributeTextChanged(object sender, EventArgs e)
        {
            if (attributeSnapshots == null) return;
            foreach (var snapshot in attributeSnapshots)
                if (ReferenceEquals(snapshot.Control, sender)) { snapshot.RefreshHint(); return; }
        }

        private DXTab AttributePage(GameUiCharacterPage page) => page == GameUiCharacterPage.Character ? CharacterTab :
            page == GameUiCharacterPage.Status ? StatsTab : HermitTab;

        private void PlaceAttribute(DXControl control, DXTab parent, Rectangle windowBounds, bool center = false)
        {
            control.Parent = parent;
            var label = control as DXLabel;
            if (label != null)
            {
                label.AutoSize = false;
                label.DrawFormat = (center ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left) | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                if (!center && parent == CharacterTab) label.DrawFormat |= TextFormatFlags.NoPadding;
            }
            var icon = control as DXImageControl;
            if (icon != null) { icon.FixedSize = true; icon.UseOffSet = false; }
            control.Location = new Point(windowBounds.X - TabControl.Location.X - parent.Location.X,
                windowBounds.Y - TabControl.Location.Y - parent.Location.Y);
            control.Size = windowBounds.Size;
        }

        private void ApplyGameUiAttributes()
        {
            if (GameUiAttributeLayout == null) return;
            var controls = GetGameUiStatControls();
            foreach (var row in GameUiAttributeLayout.Rows)
            {
                var parent = AttributePage(row.Page);
                var value = controls[row.Id];
                PlaceAttribute(value, parent, row.Value, row.Page == GameUiCharacterPage.Character);
                DXLabel caption;
                if (gameUiStatCaptions.TryGetValue(row.Id, out caption))
                {
                    PlaceAttribute(caption, parent, row.Caption.Value);
                    if (row.CaptionText != null) caption.Text = row.CaptionText;
                }
                else PlaceAttribute((DXImageControl)value.Tag, parent, row.Icon.Value);
            }
            foreach (var group in GameUiAttributeLayout.GroupCaptions)
            {
                var caption = gameUiGroupCaptions[group.Key];
                PlaceAttribute(caption, CharacterTab, group.Value);
                caption.Text = group.Key == "AttackStats" ? "攻击元素" :
                    group.Key == "AdvantageStats" ? "强 元 素" : "弱 元 素";
            }
            // Each Hermit element now carries its own original icon. Hide the obsolete group heading.
            gameUiGroupCaptions["HermitAttackStats"].Visible = false;
            foreach (var snapshot in attributeSnapshots) snapshot.RefreshHint();
        }

        private void DisableGameUiAttributes()
        {
            DisableGameUiComposition();
            if (attributeSnapshots == null) return;
            foreach (var snapshot in attributeSnapshots)
            {
                snapshot.Control.TextChanged -= GameUiAttributeTextChanged;
                snapshot.Restore();
            }
            foreach (var pair in attributeChildOrder)
            {
                if (pair.Key.IsDisposed) continue;
                var children = new List<DXControl>();
                foreach (var child in pair.Value)
                    if (!child.IsDisposed && child.Parent == pair.Key) { pair.Key.Controls.Remove(child); children.Add(child); }
                pair.Key.Controls.InsertRange(0, children);
            }
            attributeSnapshots = null; attributeChildOrder = null; GameUiAttributeLayout = null;
        }

        private sealed class AttributeControlSnapshot
        {
            internal DXControl Control { get; }
            private readonly DXControl parent;
            private readonly Rectangle bounds;
            private readonly bool value, visible, autoSize, fixedSize, useOffset;
            private readonly TextFormatFlags format;
            private readonly string captionText, originalHint;
            private string generatedHint;
            private bool ownsHint;

            internal AttributeControlSnapshot(DXControl control, bool value)
            {
                Control = control; this.value = value; parent = control.Parent;
                bounds = new Rectangle(control.Location, control.Size); visible = control.Visible;
                var label = control as DXLabel;
                if (label != null) { autoSize = label.AutoSize; format = label.DrawFormat; captionText = label.Text; }
                var icon = control as DXImageControl;
                if (icon != null) { fixedSize = icon.FixedSize; useOffset = icon.UseOffSet; }
                originalHint = control.Hint; ownsHint = value && originalHint == null;
            }

            internal void RefreshHint()
            {
                if (!ownsHint || Control.IsDisposed) return;
                if (Control.Hint != generatedHint) { ownsHint = false; return; }
                generatedHint = Control.Text;
                Control.Hint = generatedHint;
            }

            internal void Restore()
            {
                if (Control.IsDisposed) return;
                if (ownsHint && Control.Hint == generatedHint) Control.Hint = originalHint;
                var label = Control as DXLabel;
                if (label != null)
                {
                    label.AutoSize = false; label.DrawFormat = format;
                    if (!value) label.Text = captionText;
                }
                var icon = Control as DXImageControl;
                if (icon != null) { icon.FixedSize = fixedSize; icon.UseOffSet = useOffset; }
                if (parent != null && !parent.IsDisposed) Control.Parent = parent;
                Control.Location = bounds.Location; Control.Size = bounds.Size; Control.Visible = visible;
                if (label != null) label.AutoSize = autoSize;
            }
        }
    }
}
