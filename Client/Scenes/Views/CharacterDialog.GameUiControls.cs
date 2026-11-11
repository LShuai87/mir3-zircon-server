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
        private DXControl gameUiNamePanel;
        private DXCheckBox gameUiHermitConfirmation;
        private readonly Dictionary<string, DXLabel> gameUiDetailCaptions = new Dictionary<string, DXLabel>(StringComparer.Ordinal);
        private readonly Dictionary<Stat, DXButton> gameUiHermitButtons = new Dictionary<Stat, DXButton>();
        private readonly Dictionary<string, DXLabel> gameUiAddedCaptions = new Dictionary<string, DXLabel>(StringComparer.Ordinal);
        private List<DetailSnapshot> detailSnapshots;
        private Dictionary<DXControl, DXControl[]> detailChildOrder;
        private bool namePanelWasVisible;
        public GameUiCharacterControlLayout GameUiControlLayout { get; private set; }

        public void UseGameUiControls(GameUiCharacterControlLayout layout)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(CharacterDialog));
            if (layout == null) { DisableGameUiControls(); return; }
            ValidateGameUiControls(layout, GameUiEquipmentLayout, GameUiAttributeLayout);
            if (GameUiCompositionLayout != null) ValidateGameUiComposition(GameUiCompositionLayout, GameUiEquipmentLayout, GameUiAttributeLayout, layout);
            if (detailSnapshots == null)
            {
                detailSnapshots = new List<DetailSnapshot>();
                detailChildOrder = new Dictionary<DXControl, DXControl[]>();
                namePanelWasVisible = gameUiNamePanel.Visible;
                foreach (var pair in GetGameUiDetailControls())
                {
                    var control = pair.Value;
                    if (!detailChildOrder.ContainsKey(control.Parent)) detailChildOrder.Add(control.Parent, control.Parent.Controls.ToArray());
                    var snapshot = new DetailSnapshot(control, IsDynamicDetail(pair.Key));
                    detailSnapshots.Add(snapshot);
                    if (snapshot.DynamicText) control.TextChanged += GameUiDetailTextChanged;
                }
                foreach (var row in layout.Rows)
                    if (IsAddedDetailCaption(row.Id))
                    {
                        var label = new DXLabel { IsControl = false };
                        gameUiAddedCaptions.Add(row.Id, label);
                    }
            }
            GameUiControlLayout = layout;
            ApplyGameUiControls();
        }

        private static bool IsAddedDetailCaption(string id) => id.StartsWith("equipment.", StringComparison.Ordinal) || id.StartsWith("caption.new.", StringComparison.Ordinal);
        private static bool IsDynamicDetail(string id) => id.StartsWith("control.", StringComparison.Ordinal) && id != "control.ShowHelmetBox" && id != "control.MarriageIcon";

        private Dictionary<string, DXControl> GetGameUiDetailControls()
        {
            var controls = new Dictionary<string, DXControl>(StringComparer.Ordinal)
            {
                { "control.CharacterNameLabel", CharacterNameLabel }, { "control.GuildNameLabel", GuildNameLabel },
                { "control.GuildRankLabel", GuildRankLabel }, { "control.WearWeightLabel", WearWeightLabel },
                { "control.HandWeightLabel", HandWeightLabel }, { "control.RemainingLabel", RemainingLabel },
                { "control.MarriageIcon", MarriageIcon }, { "control.ShowHelmetBox", ShowHelmetBox },
                { "hermit.confirmation", gameUiHermitConfirmation }
            };
            foreach (var pair in gameUiDetailCaptions) controls.Add("caption.existing." + pair.Key, pair.Value);
            foreach (var pair in gameUiHermitButtons) controls.Add("action.hermit." + pair.Key, pair.Value);
            return controls;
        }

        private void ValidateGameUiControls(GameUiCharacterControlLayout layout, GameUiCharacterEquipmentLayout pages,
            GameUiCharacterAttributeLayout attributes)
        {
            if (pages == null) throw new InvalidOperationException("Install equipment pages before character controls.");
            if (gameUiNamePanel == null || gameUiNamePanel.IsDisposed) throw new InvalidOperationException("Character name panel is unavailable.");
            var originals = GetGameUiDetailControls();
            if (originals.Count != 20) throw new InvalidOperationException("Original character details do not match the layout.");
            var expected = GameUiCharacterControlLayout.CreateOfficialCandidate();
            var expectedRows = new Dictionary<string, GameUiCharacterControlRow>(StringComparer.Ordinal);
            foreach (var row in expected.Rows) expectedRows.Add(row.Id, row);
            var used = new HashSet<DXControl>();
            foreach (var pair in originals)
            {
                var control = pair.Value;
                if (control == null || control.IsDisposed || !used.Add(control) || control.IsMoving || control.IsResizing ||
                    (control.Parent != gameUiNamePanel && control.Parent != this && control.Parent != CharacterTab && control.Parent != StatsTab && control.Parent != HermitTab))
                    throw new InvalidOperationException("An original character detail is missing, disposed, reparented or in use.");
            }
            foreach (var label in gameUiAddedCaptions.Values)
                if (label.IsDisposed || label.Parent != CharacterTab)
                    throw new InvalidOperationException("An added character caption is unavailable.");
            var occupied = new Dictionary<GameUiCharacterControlPage, List<Rectangle>>();
            foreach (var row in layout.Rows)
            {
                GameUiCharacterControlRow originalRow;
                if (!expectedRows.TryGetValue(row.Id, out originalRow) || row.Page != originalRow.Page)
                    throw new ArgumentException("Unknown character control or incorrect page: " + row.Id, nameof(layout));
                if ((IsDynamicDetail(row.Id) || row.Id == "control.MarriageIcon") && row.Text != null)
                    throw new ArgumentException("Live character data cannot be replaced by layout text.", nameof(layout));
                if (row.Id == "control.ShowHelmetBox" || row.Id == "hermit.confirmation") CheckBoxGeometry(row.Bounds.Size);
                Rectangle page = DetailPageBounds(row.Page, pages);
                if (!page.Contains(row.Bounds)) throw new ArgumentException("Character control exceeds its page: " + row.Id);
                List<Rectangle> rectangles;
                if (!occupied.TryGetValue(row.Page, out rectangles)) occupied.Add(row.Page, rectangles = new List<Rectangle>());
                foreach (var bounds in rectangles)
                    if (bounds.IntersectsWith(row.Bounds)) throw new ArgumentException("Character detail controls overlap.");
                rectangles.Add(row.Bounds);
                if (row.Page == GameUiCharacterControlPage.Chrome)
                {
                    if (row.Bounds.IntersectsWith(new Rectangle(386, 10, 18, 18))) throw new ArgumentException("Character title overlaps the close button.");
                }
                else
                {
                    for (int slot = 0; slot < Grid.Length; slot++)
                    {
                        if (row.Page != GameUiCharacterControlPage.Character) continue;
                        var cell = pages.GetEquipmentBounds(slot); cell.Offset(pages.Container.Location);
                        if (cell.IntersectsWith(row.Bounds)) throw new ArgumentException("Character detail overlaps equipment.");
                    }
                    if (attributes != null) ValidateDetailAttributeOverlap(row, attributes);
                }
            }
        }

        private static void ValidateDetailAttributeOverlap(GameUiCharacterControlRow detail, GameUiCharacterAttributeLayout attributes)
        {
            var page = detail.Page == GameUiCharacterControlPage.Character ? GameUiCharacterPage.Character : GameUiCharacterPage.Hermit;
            foreach (var attribute in attributes.Rows)
                if (attribute.Page == page && (detail.Bounds.IntersectsWith(attribute.Value) ||
                    (attribute.Caption.HasValue && detail.Bounds.IntersectsWith(attribute.Caption.Value)) ||
                    (attribute.Icon.HasValue && detail.Bounds.IntersectsWith(attribute.Icon.Value))))
                    throw new ArgumentException("Character detail overlaps an attribute.");
            if (page == GameUiCharacterPage.Character)
                foreach (var bounds in attributes.GroupCaptions.Values)
                    if (detail.Bounds.IntersectsWith(bounds)) throw new ArgumentException("Character detail overlaps an element heading.");
        }

        private Rectangle DetailPageBounds(GameUiCharacterControlPage page, GameUiCharacterEquipmentLayout pages)
        {
            if (page == GameUiCharacterControlPage.Chrome) return new Rectangle(0, 0, Size.Width, 38);
            var rect = page == GameUiCharacterControlPage.Character ? pages.Character.Page :
                pages.Hermit.Page;
            rect.Offset(pages.Container.Location); return rect;
        }

        private DXControl DetailPage(GameUiCharacterControlPage page) => page == GameUiCharacterControlPage.Chrome ? (DXControl)this :
            page == GameUiCharacterControlPage.Character ? CharacterTab : HermitTab;

        private static DXCheckBoxGeometry CheckBoxGeometry(Size size) => new DXCheckBoxGeometry(size,
            new Rectangle(0, 0, size.Width - 20, size.Height), new Rectangle(size.Width - 14, (size.Height - 14) / 2, 14, 14));

        private void ApplyGameUiControls()
        {
            if (GameUiControlLayout == null) return;
            var controls = GetGameUiDetailControls();
            foreach (var pair in gameUiAddedCaptions) controls.Add(pair.Key, pair.Value);
            foreach (var row in GameUiControlLayout.Rows)
            {
                DXControl control = controls[row.Id], parent = DetailPage(row.Page);
                control.Parent = parent;
                var label = control as DXLabel;
                if (label != null)
                {
                    label.AutoSize = false;
                    bool centered = row.Id == "control.CharacterNameLabel" || row.Id == "control.HandWeightLabel" || row.Id == "control.WearWeightLabel";
                    label.DrawFormat = (centered ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left) | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                    if (row.Id.StartsWith("caption.", StringComparison.Ordinal) && row.Page == GameUiCharacterControlPage.Character)
                        label.DrawFormat |= TextFormatFlags.NoPadding;
                    if (row.Text != null) label.Text = row.Text;
                }
                var button = control as DXButton;
                if (button != null)
                {
                    foreach (var snapshot in detailSnapshots) if (snapshot.Control == button) snapshot.SetButtonHint();
                    if (row.Text != null) button.Label.Text = row.Text;
                }
                var check = control as DXCheckBox;
                if (check != null)
                {
                    check.UseFixedGeometry(CheckBoxGeometry(row.Bounds.Size));
                    if (row.Text != null) check.Label.Text = row.Text;
                }
                var icon = control as DXImageControl;
                if (icon != null) { icon.FixedSize = true; icon.UseOffSet = false; }
                var location = row.Bounds.Location;
                if (parent != this) location.Offset(-TabControl.Location.X - parent.Location.X, -TabControl.Location.Y - parent.Location.Y);
                control.Location = location; control.Size = row.Bounds.Size;
            }
            gameUiNamePanel.Visible = false;
            foreach (var snapshot in detailSnapshots) snapshot.RefreshHint();
        }

        private void GameUiDetailTextChanged(object sender, EventArgs e)
        {
            if (detailSnapshots == null) return;
            foreach (var snapshot in detailSnapshots)
                if (snapshot.Control == sender) { snapshot.RefreshHint(); return; }
        }

        private void DisableGameUiControls()
        {
            DisableGameUiComposition();
            if (detailSnapshots == null) return;
            foreach (var snapshot in detailSnapshots)
            {
                snapshot.Control.TextChanged -= GameUiDetailTextChanged;
                snapshot.Restore();
            }
            foreach (var label in gameUiAddedCaptions.Values) if (!label.IsDisposed) label.Dispose();
            gameUiAddedCaptions.Clear();
            if (!gameUiNamePanel.IsDisposed) gameUiNamePanel.Visible = namePanelWasVisible;
            foreach (var pair in detailChildOrder)
            {
                if (pair.Key.IsDisposed) continue;
                var children = new List<DXControl>();
                foreach (var child in pair.Value)
                    if (!child.IsDisposed && child.Parent == pair.Key) { pair.Key.Controls.Remove(child); children.Add(child); }
                pair.Key.Controls.InsertRange(0, children);
            }
            detailSnapshots = null; detailChildOrder = null; GameUiControlLayout = null;
        }

        private sealed class DetailSnapshot
        {
            internal DXControl Control { get; }
            internal bool DynamicText { get; }
            private readonly DXControl parent;
            private readonly Rectangle bounds;
            private readonly bool autoSize, fixedSize, useOffset;
            private readonly TextFormatFlags format;
            private readonly string text, hint, buttonText, checkText;
            private readonly DXCheckBoxGeometry checkGeometry;
            private bool ownsHint;
            private string generatedHint;
            internal DetailSnapshot(DXControl control, bool dynamicText)
            {
                Control = control; DynamicText = dynamicText; parent = control.Parent;
                bounds = new Rectangle(control.Location, control.Size); hint = control.Hint;
                var label = control as DXLabel;
                if (label != null) { autoSize = label.AutoSize; format = label.DrawFormat; text = label.Text; }
                var icon = control as DXImageControl;
                if (icon != null) { fixedSize = icon.FixedSize; useOffset = icon.UseOffSet; }
                var button = control as DXButton;
                if (button != null) buttonText = button.Label.Text;
                var check = control as DXCheckBox;
                if (check != null) { checkText = check.Label.Text; checkGeometry = check.FixedGeometry; }
                ownsHint = hint == null && (dynamicText || button != null);
            }
            internal void RefreshHint()
            {
                if (DynamicText) SetGeneratedHint(Control.Text);
            }
            internal void SetButtonHint() => SetGeneratedHint(buttonText);
            private void SetGeneratedHint(string value)
            {
                if (!ownsHint || Control.IsDisposed) return;
                if (Control.Hint != generatedHint) { ownsHint = false; return; }
                generatedHint = value; Control.Hint = value;
            }
            internal void Restore()
            {
                if (Control.IsDisposed) return;
                if (ownsHint && Control.Hint == generatedHint) Control.Hint = hint;
                var label = Control as DXLabel;
                if (label != null)
                {
                    label.AutoSize = false; label.DrawFormat = format;
                    if (!DynamicText) label.Text = text;
                }
                var check = Control as DXCheckBox;
                if (check != null) { check.Label.Text = checkText; check.UseFixedGeometry(checkGeometry); }
                var button = Control as DXButton;
                if (button != null) button.Label.Text = buttonText;
                var icon = Control as DXImageControl;
                if (icon != null) { icon.FixedSize = fixedSize; icon.UseOffSet = useOffset; }
                if (parent != null && !parent.IsDisposed) Control.Parent = parent;
                Control.Location = bounds.Location; Control.Size = bounds.Size;
                if (label != null) label.AutoSize = autoSize;
                // Keep live text, colours, visibility, checked state and handlers from the original game paths.
            }
        }
    }
}
