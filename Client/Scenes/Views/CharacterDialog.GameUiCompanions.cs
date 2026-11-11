using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Client.Controls;
using Client.Envir;
using Client.UserModels;
using Library;

namespace Client.Scenes.Views
{
    public sealed partial class CharacterDialog
    {
        private CharacterCompanionWindow gameUiCompanion;
        private DXControl gameUiCompanionStatus, gameUiCompanionHermit, gameUiCompanionScene;
        private DXButton gameUiStatusButton, gameUiHermitButton;
        private List<CompanionControlState> gameUiCompanionOriginals;
        private DXTab gameUiCompanionOriginalTab;
        private GameUiCharacterPage? gameUiCompanionPage;
        private bool gameUiCompanionUpdating;
        public DXWindow GameUiCompanionWindow => gameUiCompanion;
        public GameUiCharacterPage? GameUiCompanionPage => gameUiCompanionPage;

        // A sibling window is essential: children of the character card are clipped to its bounds.
        // Reuse the real stat labels and action buttons so live updates and packet handlers survive.
        public void UseGameUiCompanionCards(GameUiWindowSkin skin, MirLibrary library, ButtonImageStates buttons)
        {
            DisableGameUiCompanionCards();
            if (skin == null) return;
            if (GameUiControlLayout == null || GameUiAttributeLayout == null || Parent == null)
                throw new InvalidOperationException("Install the character bindings before its companion cards.");
            gameUiCompanionOriginals = new List<CompanionControlState>();
            gameUiCompanionOriginalTab = TabControl.SelectedTab;
            try
            {
                gameUiCompanion = new CharacterCompanionWindow { WindowSkin = skin, Parent = Parent, Visible = false };
                gameUiCompanionStatus = new DXControl { Parent = gameUiCompanion, Size = skin.MinimumSize, PassThrough = true, Sort = false };
                gameUiCompanionHermit = new DXControl { Parent = gameUiCompanion, Size = skin.MinimumSize, Visible = false, PassThrough = true, Sort = false };
                // Page containers never capture empty chrome or reorder over the close button.
                // Their real action controls still participate in native pointer routing.
                gameUiCompanion.CloseButton.BringToFront();
                gameUiCompanion.TitleLabel.BringToFront();
                gameUiCompanion.CloseButton.MouseClick += (s, e) => ShowGameUiCompanion(null);
                var stats = GetGameUiStatControls();
                int statusIndex = 0, hermitIndex = 0, elementIndex = 0;
                CompanionCaption(gameUiCompanionStatus, "详细属性", new Rectangle(18, 48, 260, 22));
                CompanionCaption(gameUiCompanionHermit, "基础修炼", new Rectangle(18, 48, 122, 22));
                CompanionCaption(gameUiCompanionHermit, "元素修炼", new Rectangle(158, 48, 120, 22));
                string[] elements = { "火", "冰", "雷", "风", "神圣", "暗黑", "幻影" };
                foreach (var row in GameUiAttributeLayout.Rows)
                {
                    if (row.Page == GameUiCharacterPage.Character) continue;
                    DXLabel value = stats[row.Id];
                    DXLabel caption;
                    bool hasCaption = gameUiStatCaptions.TryGetValue(row.Id, out caption);
                    var icon = hasCaption ? null : (DXImageControl)value.Tag;
                    if (row.Page == GameUiCharacterPage.Status)
                    {
                        int y = 80 + statusIndex++ * 25;
                        if (hasCaption) MoveCompanionControl(caption, gameUiCompanionStatus, new Rectangle(18, y, 160, 24));
                        else
                        {
                            MoveCompanionControl(icon, gameUiCompanionStatus, new Rectangle(18, y + 4, 16, 16));
                            CompanionCaption(gameUiCompanionStatus, row.Id.StartsWith("Advantage", StringComparison.Ordinal) ? "强物理" : "弱物理", new Rectangle(40, y, 138, 24));
                        }
                        MoveCompanionControl(value, gameUiCompanionStatus, new Rectangle(184, y, 94, 24), true);
                    }
                    else if (hasCaption)
                    {
                        int y = 82 + hermitIndex++ * 30;
                        MoveCompanionControl(caption, gameUiCompanionHermit, new Rectangle(18, y, 58, 24));
                        MoveCompanionControl(value, gameUiCompanionHermit, new Rectangle(76, y, 38, 24), true);
                        string stat = row.Id.Substring(row.Id.IndexOf('.') + 1);
                        foreach (var pair in gameUiHermitButtons)
                            if (pair.Key.ToString() == stat)
                                MoveCompanionControl(pair.Value, gameUiCompanionHermit, new Rectangle(118, y, 22, 24));
                    }
                    else
                    {
                        int i = elementIndex++, y = 82 + i * 30;
                        MoveCompanionControl(icon, gameUiCompanionHermit, new Rectangle(158, y + 4, 16, 16));
                        CompanionCaption(gameUiCompanionHermit, elements[i], new Rectangle(180, y, 30, 24));
                        MoveCompanionControl(value, gameUiCompanionHermit, new Rectangle(214, y, 64, 24), true);
                    }
                }
                MoveCompanionControl(gameUiHermitButtons[Stat.WeaponElement], gameUiCompanionHermit, new Rectangle(118, 292, 22, 24));
                CompanionCaption(gameUiCompanionHermit, "武器元素", new Rectangle(18, 292, 96, 24));
                MoveCompanionControl(gameUiDetailCaptions["RemainingLabel"], gameUiCompanionHermit, new Rectangle(18, 332, 116, 24));
                MoveCompanionControl(RemainingLabel, gameUiCompanionHermit, new Rectangle(148, 332, 130, 24), true);
                MoveCompanionControl(gameUiHermitConfirmation, gameUiCompanionHermit, new Rectangle(18, 368, 180, 24));
                MoveCompanionControl(GuildRankLabel, this, new Rectangle(254, 40, 142, 22));
                foreach (var tab in new[] { CharacterTab, StatsTab, HermitTab })
                {
                    gameUiCompanionOriginals.Add(new CompanionControlState(tab.TabButton));
                    tab.TabButton.Visible = false;
                }
                TabControl.SelectedTab = CharacterTab;
                gameUiStatusButton = CompanionButton(library, buttons, "状态", new Point(288, 66), GameUiCharacterPage.Status);
                gameUiHermitButton = CompanionButton(library, buttons, "修炼", new Point(346, 66), GameUiCharacterPage.Hermit);
                LocationChanged += CompanionOwnerChanged;
                SizeChanged += CompanionOwnerChanged;
                IsVisibleChanged += CompanionOwnerChanged;
                ParentChanged += CompanionOwnerParentChanged;
                MouseDown += CompanionOwnerMouseDown;
                SortChanged += CompanionOwnerChanged;
                BindCompanionScene();
                PositionGameUiCompanion();
            }
            catch { DisableGameUiCompanionCards(); throw; }
        }

        private DXButton CompanionButton(MirLibrary library, ButtonImageStates states, string text, Point location, GameUiCharacterPage page)
        {
            var button = new DXButton { Parent = this, Location = location, Hint = text + "（点击展开 / 收起）" };
            button.BindButtonImages(library, states);
            button.Label.Text = text;
            button.Label.ForeColour = Color.FromArgb(198, 166, 99);
            button.MouseClick += (s, e) => { if (e == null || e.Button == MouseButtons.Left) ShowGameUiCompanion(gameUiCompanionPage == page ? (GameUiCharacterPage?)null : page); };
            return button;
        }
        private static void CompanionCaption(DXControl parent, string text, Rectangle bounds)
        {
            new DXLabel { Parent = parent, Text = text, AutoSize = false, Location = bounds.Location, Size = bounds.Size,
                ForeColour = Color.FromArgb(198, 166, 99), IsControl = false,
                DrawFormat = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix };
        }
        private void MoveCompanionControl(DXControl control, DXControl parent, Rectangle bounds, bool right = false)
        {
            gameUiCompanionOriginals.Add(new CompanionControlState(control));
            control.Parent = parent; control.Location = bounds.Location; control.Size = bounds.Size;
            if (control is DXLabel label)
            {
                label.AutoSize = false;
                if (right) label.ForeColour = Color.White;
                label.DrawFormat = (right ? TextFormatFlags.Right : TextFormatFlags.Left) | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            }
        }

        public void ShowGameUiCompanion(GameUiCharacterPage? page)
        {
            if (gameUiCompanion == null) throw new InvalidOperationException("Companion cards are not installed.");
            if (page.HasValue && page != GameUiCharacterPage.Status && page != GameUiCharacterPage.Hermit)
                throw new ArgumentOutOfRangeException(nameof(page));
            gameUiCompanionPage = page;
            TabControl.SelectedTab = CharacterTab;
            gameUiCompanionStatus.Visible = page == GameUiCharacterPage.Status;
            gameUiCompanionHermit.Visible = page == GameUiCharacterPage.Hermit;
            gameUiCompanion.TitleLabel.Text = page == GameUiCharacterPage.Hermit ? "修炼" : "状态";
            gameUiStatusButton.Pressed = page == GameUiCharacterPage.Status;
            gameUiHermitButton.Pressed = page == GameUiCharacterPage.Hermit;
            PositionGameUiCompanion();
            if (gameUiCompanion.IsVisible) gameUiCompanion.BringToFront();
        }
        public override void OnSorted()
        {
            base.OnSorted();
            // Equipment controls bring the owner forward recursively; keep its companion above peers too.
            if (gameUiCompanion?.IsVisible == true) gameUiCompanion.BringToFront();
        }
        private void BindCompanionScene()
        {
            if (gameUiCompanionScene != null) gameUiCompanionScene.SizeChanged -= CompanionOwnerChanged;
            gameUiCompanionScene = Parent;
            if (gameUiCompanionScene != null) gameUiCompanionScene.SizeChanged += CompanionOwnerChanged;
            if (gameUiCompanion != null) gameUiCompanion.Parent = Parent;
        }
        private void CompanionOwnerParentChanged(object sender, EventArgs e) { BindCompanionScene(); PositionGameUiCompanion(); }
        private void CompanionOwnerChanged(object sender, EventArgs e) => PositionGameUiCompanion();
        private void CompanionOwnerMouseDown(object sender, MouseEventArgs e) { if (gameUiCompanion?.IsVisible == true) gameUiCompanion.BringToFront(); }
        private void PositionGameUiCompanion()
        {
            if (gameUiCompanion == null || gameUiCompanionUpdating) return;
            gameUiCompanionUpdating = true;
            try
            {
                bool visible = IsVisible && Parent != null && gameUiCompanionPage.HasValue;
                gameUiCompanion.Visible = visible;
                if (!visible) return;
                // Keep right docking even near the screen edge: shift the pair left, never overlay the character.
                int pairWidth = Size.Width + gameUiCompanion.Size.Width;
                int x = Math.Max(0, Math.Min(Location.X, Parent.Size.Width - pairWidth));
                int y = Math.Max(0, Math.Min(Location.Y, Parent.Size.Height - Math.Max(Size.Height, gameUiCompanion.Size.Height)));
                Location = new Point(x, y);
                gameUiCompanion.Location = new Point(x + Size.Width, y);
            }
            finally { gameUiCompanionUpdating = false; }
        }

        private void DisableGameUiCompanionCards()
        {
            LocationChanged -= CompanionOwnerChanged; SizeChanged -= CompanionOwnerChanged;
            IsVisibleChanged -= CompanionOwnerChanged; ParentChanged -= CompanionOwnerParentChanged;
            MouseDown -= CompanionOwnerMouseDown; SortChanged -= CompanionOwnerChanged;
            if (gameUiCompanionScene != null) gameUiCompanionScene.SizeChanged -= CompanionOwnerChanged;
            gameUiCompanionScene = null;
            if (gameUiCompanionOriginals != null)
                foreach (var state in gameUiCompanionOriginals) state.Restore();
            gameUiCompanionOriginals = null;
            gameUiStatusButton?.Dispose(); gameUiHermitButton?.Dispose(); gameUiCompanion?.Dispose();
            gameUiStatusButton = null; gameUiHermitButton = null; gameUiCompanion = null;
            gameUiCompanionStatus = null; gameUiCompanionHermit = null; gameUiCompanionPage = null;
            if (gameUiCompanionOriginalTab != null && !gameUiCompanionOriginalTab.IsDisposed)
                TabControl.SelectedTab = gameUiCompanionOriginalTab;
            gameUiCompanionOriginalTab = null;
        }
        private sealed class CompanionControlState
        {
            private readonly DXControl control, parent;
            private readonly Rectangle bounds;
            private readonly bool visible, autoSize;
            private readonly TextFormatFlags format;
            private readonly Color colour;
            internal CompanionControlState(DXControl value)
            {
                control = value; parent = value.Parent; bounds = new Rectangle(value.Location, value.Size); visible = value.Visible;
                if (value is DXLabel label) { autoSize = label.AutoSize; format = label.DrawFormat; colour = label.ForeColour; }
            }
            internal void Restore()
            {
                if (control.IsDisposed) return;
                control.Parent = parent;
                if (control is DXLabel label) { label.AutoSize = autoSize; label.DrawFormat = format; label.ForeColour = colour; }
                control.Location = bounds.Location; control.Size = bounds.Size; control.Visible = visible;
            }
        }
        private sealed class CharacterCompanionWindow : DXWindow
        {
            public override WindowType Type => WindowType.None;
            public override bool CustomSize => false;
            public override bool AutomaticVisiblity => false;
            internal CharacterCompanionWindow() { Movable = false; AllowResize = false; }
        }
    }
}
