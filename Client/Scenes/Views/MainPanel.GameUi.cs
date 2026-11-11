using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Client.Controls;
using Client.Envir;
using Client.Models;
using Library;

namespace Client.Scenes.Views
{
    public sealed partial class MainPanel
    {
        private HudSnapshot gameUiHudSnapshot;
        private Dictionary<string, DXImageControl> gameUiHudRoots;
        private Dictionary<string, DXControl> gameUiHudControls;
        private readonly Dictionary<string, DXLabel> gameUiHudCaptions = new Dictionary<string, DXLabel>();
        public IReadOnlyDictionary<string, DXLabel> GameUiHudCaptions => new ReadOnlyDictionary<string, DXLabel>(gameUiHudCaptions);
        public GameUiHudLayout GameUiHudLayout { get; private set; }
        public IReadOnlyDictionary<string, DXImageControl> GameUiHudRoots { get; private set; }
        public IReadOnlyDictionary<string, DXControl> GameUiHudControls { get; private set; }
        public DXButton GameUiExitButton { get; private set; }
        public DXButton GameUiMountButton { get; private set; }
        public DXButton GameUiCombatButton { get; private set; }
        public DXButton GameUiPotentialButton { get; private set; }
        public GameUiHudOrbOverlay GameUiOrbOverlay { get; private set; }
        public GameUiHudFill GameUiHealthFill { get; private set; }
        public GameUiHudFill GameUiManaFill { get; private set; }
        public GameUiHudFill GameUiExperienceFill { get; private set; }

        // Call before applying any HUD artwork. The scene coordinator positions these roots.
        // Null restores the native hierarchy, image bindings, geometry and label behavior.
        public void UseGameUiHud(GameUiHudLayout layout)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(MainPanel));
            if (layout == null) { DisableGameUiHud(); return; }
            var originals = HudControls();
            ValidateHud(layout, originals);
            bool starting = gameUiHudSnapshot == null;
            GameUiHudLayout previous = GameUiHudLayout;
            if (starting) gameUiHudSnapshot = new HudSnapshot(this, originals);
            GameUiHudLayout = layout;
            try
            {
                if (starting)
                {
                    gameUiHudControls = originals;
                    GameUiHudControls = new ReadOnlyDictionary<string, DXControl>(originals);
                    gameUiHudRoots = new Dictionary<string, DXImageControl>(StringComparer.Ordinal);
                    GameUiHudRoots = new ReadOnlyDictionary<string, DXImageControl>(gameUiHudRoots);
                    foreach (var owner in layout.Owners)
                    {
                        var root = new DXImageControl { FixedSize = true, DrawImage = false, PixelDetect = false, PassThrough = true };
                        gameUiHudRoots.Add(owner.Key, root);
                        root.Size = owner.Value; root.Parent = Parent; root.Visible = Visible && owner.Key != "readouts"; root.Enabled = Enabled;
                        root.Disposing += OnHudPartDisposing; root.ParentChanged += OnHudRootParentChanged;
                    }
                    GameUiMountButton = HudShortcut(Envir.GameUiHudLayout.MountButtonBounds, "坐骑：骑乘 / 下马 [M]");
                    GameUiMountButton.MouseClick += (o, e) =>
                    { if (e == null || e.Button == MouseButtons.Left) GameScene.Game?.ToggleMount(); };
                    GameUiCombatButton = HudShortcut(Envir.GameUiHudLayout.CombatButtonBounds, "战斗辅助 [A]（自动喝药 / 技能设置）");
                    GameUiCombatButton.MouseClick += (o, e) =>
                    {
                        var box = GameScene.Game?.AutoPotionBox;
                        if ((e == null || e.Button == MouseButtons.Left) && box != null && !box.IsDisposed)
                            box.Visible = !box.Visible;
                    };
                    GameUiPotentialButton = HudShortcut(Envir.GameUiHudLayout.PotentialButtonBounds, "潜能系统（暂未开放）");
                    GameUiPotentialButton.Enabled = false;
                    GameUiExitButton = new DXButton
                    {
                        Parent = gameUiHudRoots["commands"],
                        Location = new Point(198, 174),
                        Size = new Size(22, 18),
                        FixedSize = true,
                        Hint = "退出游戏 / 返回角色选择",
                    };
                    GameUiExitButton.Disposing += OnHudPartDisposing;
                    GameUiExitButton.MouseClick += (o, e) => GameScene.Game?.ExitBox?.ShowConfirmation();
                    GameUiOrbOverlay = new GameUiHudOrbOverlay { Parent = gameUiHudRoots["orb"] };
                    GameUiOrbOverlay.Disposing += OnHudPartDisposing;
                    GameUiHealthFill = new GameUiHudFill(GameUiHudFillDirection.Bottom, () => ResourceFraction(true));
                    GameUiManaFill = new GameUiHudFill(GameUiHudFillDirection.Bottom, () => ResourceFraction(false));
                    GameUiExperienceFill = new GameUiHudFill(GameUiHudFillDirection.Left, ExperienceFraction);
                    GameUiHealthFill.Parent = HealthBar; GameUiManaFill.Parent = ManaBar; GameUiExperienceFill.Parent = ExperienceBar;
                    GameUiHealthFill.Disposing += OnHudPartDisposing; GameUiManaFill.Disposing += OnHudPartDisposing; GameUiExperienceFill.Disposing += OnHudPartDisposing;
                    foreach (var pair in originals)
                    {
                        if (pair.Value is DXLabel) pair.Value.TextChanged += RefreshHudHint;
                        if (layout.Regions[pair.Key].Owner != "readouts") continue;
                        var value = (DXLabel)pair.Value;
                        gameUiHudCaptions.Add(pair.Key, new DXLabel
                        {
                            Parent = gameUiHudRoots["readouts"], Text = HudCaptions[pair.Key],
                            // DXLabel disposes its font; the temporary caption must own a separate copy.
                            Font = (Font)value.Font.Clone(), ForeColour = value.ForeColour, AutoSize = false, IsControl = false,
                            DrawFormat = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                        });
                        value.VisibleChanged += RefreshHudCaptionVisibility;
                    }
                }
                DrawImage = false; PassThrough = true;
                foreach (DXLabel caption in gameUiLegacyCaptions) caption.Visible = false;
                OtherBar.Visible = false;
                MailButton.Visible = false;
                GuildButton.Visible = true;
                InventoryButton.Hint = "包裹 [W]";
                SpellButton.Hint = "技能信息 [E]";
                ConfigButton.Hint = "系统设置 [O]";
                ExperienceBar.DrawImage = false; ExperienceBar.FixedSize = true; ExperienceBar.PixelDetect = false;
                ApplyHud(layout);
                ExperienceLabel.Visible = false;
            }
            catch
            {
                if (starting) DisableGameUiHud();
                else { GameUiHudLayout = previous; ApplyHud(previous); }
                throw;
            }
        }

        private DXButton HudShortcut(Rectangle bounds, string hint)
        {
            var button = new DXButton
            {
                Parent = gameUiHudRoots["commands"], Location = bounds.Location, Size = bounds.Size,
                FixedSize = true, Sort = false, Hint = hint,
            };
            button.Disposing += OnHudPartDisposing;
            return button;
        }

        private Dictionary<string, DXControl> HudControls() => new Dictionary<string, DXControl>(StringComparer.Ordinal)
        {
            ["CharacterButton"] = CharacterButton,
            ["InventoryButton"] = InventoryButton,
            ["SpellButton"] = SpellButton,
            ["QuestButton"] = QuestButton,
            ["GuildButton"] = GuildButton,
            ["GroupButton"] = GroupButton,
            ["ConfigButton"] = ConfigButton,
            ["CashShopButton"] = CashShopButton,
            ["BeltButton"] = BeltButton,
            ["AttackModeLabel"] = AttackModeLabel,
            ["PetModeLabel"] = PetModeLabel,
            ["ClassLabel"] = ClassLabel,
            ["LevelLabel"] = LevelLabel,
            ["HealthLabel"] = HealthLabel,
            ["ManaLabel"] = ManaLabel,
            ["ACLabel"] = ACLabel,
            ["MRLabel"] = MRLabel,
            ["DCLabel"] = DCLabel,
            ["MCLabel"] = MCLabel,
            ["SCLabel"] = SCLabel,
            ["AccuracyLabel"] = AccuracyLabel,
            ["AgilityLabel"] = AgilityLabel,
            ["ExperienceBar"] = ExperienceBar,
            ["ExperienceLabel"] = ExperienceLabel,
            ["HealthBar"] = HealthBar,
            ["ManaBar"] = ManaBar,
        };

        private void ValidateHud(GameUiHudLayout layout, Dictionary<string, DXControl> controls)
        {
            if (Parent == null || Parent.IsDisposed || IsMoving || IsResizing)
                throw new InvalidOperationException("HUD requires a live scene parent and idle original controls.");
            foreach (var pair in controls)
            {
                DXControl c = pair.Value;
                if (c == null || c.IsDisposed || c.IsMoving || c.IsResizing || c.AllowDragOut)
                    throw new InvalidOperationException("An original HUD control is missing or in use: " + pair.Key);
                DXControl expected = gameUiHudSnapshot == null ? this : gameUiHudRoots[layout.Regions[pair.Key].Owner];
                if (c.Parent != expected || gameUiHudControls != null && !ReferenceEquals(c, gameUiHudControls[pair.Key]))
                    throw new InvalidOperationException("An original HUD control was replaced or reparented: " + pair.Key);
                var button = c as DXButton;
                if (button?.ImageStates != null && button.Library.GetSize(button.ImageStates.Normal) != layout.Regions[pair.Key].Bounds.Size)
                    throw new ArgumentException("HUD button artwork does not match the target geometry: " + pair.Key);
            }
            if (gameUiLegacyCaptions.Count != 9 || gameUiLegacyCaptions.Any(c => c.IsDisposed || c.Parent != this) ||
                OtherBar == null || OtherBar.IsDisposed || OtherBar.Parent != this ||
                NewMailIcon == null || NewMailIcon.IsDisposed || NewMailIcon.Parent != MailButton ||
                CompletedQuestIcon == null || CompletedQuestIcon.IsDisposed || CompletedQuestIcon.Parent != QuestButton ||
                AvailableQuestIcon == null || AvailableQuestIcon.IsDisposed || AvailableQuestIcon.Parent != QuestButton)
                throw new InvalidOperationException("Original HUD captions or alert controls are missing.");
            if (gameUiHudRoots == null) return;
            if (GameUiExitButton == null || GameUiExitButton.IsDisposed || GameUiExitButton.Parent != gameUiHudRoots["commands"])
                throw new InvalidOperationException("The HUD exit button is missing.");
            if (GameUiOrbOverlay == null || GameUiOrbOverlay.IsDisposed || GameUiOrbOverlay.Parent != gameUiHudRoots["orb"] ||
                GameUiOrbOverlay.Size != layout.Owners["orb"])
                throw new InvalidOperationException("The orb foreground is missing or has incompatible geometry.");
            foreach (var pair in gameUiHudRoots)
                if (pair.Value.IsDisposed || pair.Value.Parent != Parent || pair.Value.IsMoving || pair.Value.IsResizing ||
                    pair.Value.DrawImage && pair.Value.Library != null && pair.Value.Library.GetSize(pair.Value.Index) != layout.Owners[pair.Key])
                    throw new InvalidOperationException("An active HUD root was replaced or has incompatible artwork.");
            foreach (var pair in new[] { Tuple.Create(GameUiHealthFill, "HealthBar", HealthBar), Tuple.Create(GameUiManaFill, "ManaBar", ManaBar), Tuple.Create(GameUiExperienceFill, "ExperienceBar", (DXControl)ExperienceBar) })
                if (pair.Item1 == null || pair.Item1.IsDisposed || pair.Item1.Parent != pair.Item3 ||
                    pair.Item1.Library != null && pair.Item1.Library.GetSize(pair.Item1.Index) != layout.Regions[pair.Item2].Bounds.Size)
                    throw new InvalidOperationException("An active HUD fill is missing or has incompatible artwork.");
        }

        private void ApplyHud(GameUiHudLayout layout)
        {
            foreach (var pair in gameUiHudRoots) pair.Value.Size = layout.Owners[pair.Key];
            foreach (var pair in gameUiHudControls)
            {
                GameUiHudRegion region = layout.Regions[pair.Key]; DXControl c = pair.Value;
                c.Parent = gameUiHudRoots[region.Owner];
                var image = c as DXImageControl; if (image != null) image.FixedSize = true;
                var label = c as DXLabel;
                if (label != null)
                {
                    label.AutoSize = false;
                    label.DrawFormat = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
                    // The source-art level bezel fits three digits without GDI side padding.
                    if (label == LevelLabel) label.DrawFormat |= TextFormatFlags.NoPadding;
                    RefreshHudHint(label, EventArgs.Empty);
                }
                c.Location = region.Bounds.Location; c.Size = region.Bounds.Size;
            }
            // Match draw order to reverse-order pointer routing and keep it stable
            // after OnMouseDown/BringToFront. Native Sort is restored on rollback.
            var commands = gameUiHudRoots["commands"].Controls;
            foreach (string key in Envir.GameUiHudLayout.CommandImageDrawOrder)
            {
                DXControl button = gameUiHudControls[key]; button.Sort = false;
                commands.Remove(button); commands.Add(button);
            }
            foreach (var pair in gameUiHudCaptions)
            {
                Rectangle value = layout.Regions[pair.Key].Bounds;
                pair.Value.Location = new Point(value.X - 28, value.Y);
                pair.Value.Size = new Size(28, value.Height);
                pair.Value.Visible = gameUiHudControls[pair.Key].Visible;
            }
            GameUiHealthFill.Size = HealthBar.Size; GameUiManaFill.Size = ManaBar.Size; GameUiExperienceFill.Size = ExperienceBar.Size;
            // Foreground ornament covers the fills; resource labels stay above the ornament.
            GameUiOrbOverlay.Location = Point.Empty;
            var orbChildren = gameUiHudRoots["orb"].Controls;
            orbChildren.Remove(GameUiOrbOverlay); orbChildren.Insert(0, GameUiOrbOverlay);
            // Labels overlap the bottom of the two fills; retain their foreground draw order.
            foreach (DXControl bar in new[] { HealthBar, ManaBar })
            { var children = gameUiHudRoots["orb"].Controls; children.Remove(bar); children.Insert(0, bar); }
            // Quest updates retain their native state without drawing badges on the wheel.
            // Snapshot restoration restores the Legacy artwork flags on rollback.
            AvailableQuestIcon.DrawImage = CompletedQuestIcon.DrawImage = false;
            // Mail stays on the hidden Legacy action; reminders never overlay the guild button.
            RefreshHudAlertLocations();
        }
        private void RefreshHudAlertLocations()
        {
            if (CompletedQuestIcon == null) return;
            NewMailIcon.Location = new Point(2, 2);
            // The left transparent corner of the quest wedge meets the bag wedge.
            // Keep reminders on the outside edge so no other button clips them.
            bool candidate = GameUiHudLayout != null;
            AvailableQuestIcon.Location = new Point(candidate ? Math.Max(2, QuestButton.Size.Width - AvailableQuestIcon.Size.Width - 2) : 2, 2);
            CompletedQuestIcon.Location = new Point(candidate ? Math.Max(2, QuestButton.Size.Width - CompletedQuestIcon.Size.Width - 2) : 2,
                AvailableQuestIcon.Visible ? QuestButton.Size.Height - CompletedQuestIcon.Size.Height : 2);
        }
        private static readonly IReadOnlyDictionary<string, string> HudCaptions = new ReadOnlyDictionary<string, string>(new Dictionary<string, string> { ["ClassLabel"] = "职业", ["LevelLabel"] = "等级", ["ACLabel"] = "防御", ["MRLabel"] = "魔防", ["DCLabel"] = "攻击", ["MCLabel"] = "魔法", ["SCLabel"] = "道术", ["AccuracyLabel"] = "准确", ["AgilityLabel"] = "敏捷", ["HealthLabel"] = "生命", ["ManaLabel"] = "魔法值", ["AttackModeLabel"] = "攻击模式", ["PetModeLabel"] = "宠物模式" });
        private void RefreshHudCaptionVisibility(object sender, EventArgs e)
        {
            foreach (var pair in gameUiHudCaptions)
                if (ReferenceEquals(gameUiHudControls[pair.Key], sender)) pair.Value.Visible = ((DXLabel)sender).Visible;
        }
        private void RefreshHudHint(object sender, EventArgs e)
        {
            var label = (DXLabel)sender;
            if (label == ExperienceLabel) return; // The original bar already owns current/max experience hints.
            var key = gameUiHudControls.First(p => ReferenceEquals(p.Value, label)).Key;
            string caption = HudCaptions[key];
            label.Hint = caption + ": " + label.Text;
        }
        private static decimal ResourceFraction(bool health)
        {
            var user = MapObject.User; if (user?.Stats == null) return 0M;
            int maximum = user.Stats[health ? Stat.Health : Stat.Mana];
            return maximum <= 0 ? 0M : Math.Max(0M, Math.Min(1M, (decimal)(health ? user.CurrentHP : user.CurrentMP) / maximum));
        }
        private static decimal ExperienceFraction()
        {
            var user = MapObject.User;
            if (user == null || user.Level < 0 || user.Level >= Globals.ExperienceList.Count) return 0M;
            decimal maximum = Globals.ExperienceList[user.Level];
            return maximum <= 0 ? 0M : Math.Max(0M, Math.Min(1M, user.Experience / maximum));
        }
        public override void OnEnabledChanged(bool oValue, bool nValue)
        {
            base.OnEnabledChanged(oValue, nValue);
            if (gameUiHudRoots != null) foreach (var root in gameUiHudRoots.Values) root.Enabled = nValue;
        }
        public override void OnVisibleChanged(bool oValue, bool nValue)
        {
            base.OnVisibleChanged(oValue, nValue);
            if (gameUiHudRoots != null) foreach (var root in gameUiHudRoots) root.Value.Visible = nValue && root.Key != "readouts";
        }
        public override void OnParentChanged(DXControl oValue, DXControl nValue)
        {
            base.OnParentChanged(oValue, nValue);
            if (gameUiHudSnapshot != null) DisableGameUiHud();
        }

        private void OnHudPartDisposing(object sender, EventArgs e) => DisableGameUiHud((DXControl)sender);
        private void OnHudRootParentChanged(object sender, EventArgs e)
        {
            if (gameUiHudSnapshot != null && ((DXControl)sender).Parent != Parent) DisableGameUiHud();
        }
        private void DisableGameUiHud(DXControl disposingPart = null)
        {
            if (gameUiHudSnapshot == null) return;
            var snapshot = gameUiHudSnapshot;
            if (gameUiHudRoots != null) foreach (var r in gameUiHudRoots.Values)
            { r.Disposing -= OnHudPartDisposing; r.ParentChanged -= OnHudRootParentChanged; }
            foreach (var fill in new[] { GameUiHealthFill, GameUiManaFill, GameUiExperienceFill })
                if (fill != null) fill.Disposing -= OnHudPartDisposing;
            if (gameUiHudControls != null)
                foreach (var pair in gameUiHudControls)
                {
                    if (pair.Value is DXLabel) pair.Value.TextChanged -= RefreshHudHint;
                    pair.Value.VisibleChanged -= RefreshHudCaptionVisibility;
                }
            if (GameUiExitButton != null) GameUiExitButton.Disposing -= OnHudPartDisposing;
            if (GameUiOrbOverlay != null) GameUiOrbOverlay.Disposing -= OnHudPartDisposing;
            foreach (var button in new[] { GameUiMountButton, GameUiCombatButton, GameUiPotentialButton })
                if (button != null) button.Disposing -= OnHudPartDisposing;
            gameUiHudCaptions.Clear();
            // Keep native SizeChanged handlers suppressed until all controls are back.
            foreach (HudControlState state in snapshot.Controls) state.Restore();
            foreach (var c in snapshot.OriginalOrder) Controls.Remove(c);
            Controls.InsertRange(0, snapshot.OriginalOrder);
            DrawImage = snapshot.DrawImage; PassThrough = snapshot.PassThrough;
            if (GameUiHealthFill != null && GameUiHealthFill != disposingPart) GameUiHealthFill.Dispose();
            if (GameUiManaFill != null && GameUiManaFill != disposingPart) GameUiManaFill.Dispose();
            if (GameUiExperienceFill != null && GameUiExperienceFill != disposingPart) GameUiExperienceFill.Dispose();
            if (GameUiOrbOverlay != null && GameUiOrbOverlay != disposingPart) GameUiOrbOverlay.Dispose();
            GameUiOrbOverlay = null;
            GameUiExitButton = null;
            GameUiMountButton = GameUiCombatButton = GameUiPotentialButton = null;
            if (gameUiHudRoots != null) foreach (var r in gameUiHudRoots.Values) if (r != disposingPart) r.Dispose();
            GameUiHealthFill = GameUiManaFill = GameUiExperienceFill = null;
            GameUiHudRoots = null; GameUiHudControls = null; gameUiHudRoots = null; gameUiHudControls = null;
            gameUiHudSnapshot = null; GameUiHudLayout = null;
            MCLabelLabel.Visible = MCLabel.Visible; SCLabelLabel.Visible = SCLabel.Visible;
            if (!HealthLabel.Size.IsEmpty) HealthLabel.Location = new Point(HealthBar.Location.X + (HealthBar.Size.Width - HealthLabel.Size.Width) / 2, HealthBar.Location.Y + (HealthBar.Size.Height - HealthLabel.Size.Height) / 2);
            if (!ManaLabel.Size.IsEmpty) ManaLabel.Location = new Point(ManaBar.Location.X + (ManaBar.Size.Width - ManaLabel.Size.Width) / 2, ManaBar.Location.Y + (ManaBar.Size.Height - ManaLabel.Size.Height) / 2);
            if (!ExperienceLabel.Size.IsEmpty) ExperienceLabel.Location = new Point(ExperienceBar.Location.X + (ExperienceBar.Size.Width - ExperienceLabel.Size.Width) / 2, ExperienceBar.Location.Y + (ExperienceBar.Size.Height - ExperienceLabel.Size.Height) / 2);
            if (!AttackModeLabel.Size.IsEmpty) AttackModeLabel.Location = new Point(OtherBar.Location.X, OtherBar.Location.Y + (OtherBar.Size.Height - AttackModeLabel.Size.Height) / 2 - 2);
            if (!PetModeLabel.Size.IsEmpty) PetModeLabel.Location = new Point(OtherBar.Location.X + OtherBar.Size.Width - PetModeLabel.Size.Width, OtherBar.Location.Y + (OtherBar.Size.Height - PetModeLabel.Size.Height) / 2 - 2);
        }
        private sealed class HudSnapshot
        {
            internal readonly HudControlState[] Controls;
            internal readonly DXControl[] OriginalOrder;
            internal readonly bool DrawImage, PassThrough;
            internal HudSnapshot(MainPanel owner, Dictionary<string, DXControl> bindings)
            {
                OriginalOrder = owner.Controls.ToArray(); DrawImage = owner.DrawImage; PassThrough = owner.PassThrough;
                Controls = OriginalOrder.SelectMany(NativeTree).Distinct()
                    .Select(c => new HudControlState(c, c == owner.OtherBar || c == owner.ExperienceLabel || c == owner.MailButton || c == owner.GuildButton || owner.gameUiLegacyCaptions.Contains(c as DXLabel), bindings.Values.Contains(c))).ToArray();
            }
        }
        private static IEnumerable<DXControl> NativeTree(DXControl control)
        {
            yield return control;
            foreach (var child in control.Controls.ToArray())
                foreach (var descendant in NativeTree(child)) yield return descendant;
        }
        private sealed class HudControlState
        {
            private readonly DXControl control, parent;
            private readonly Rectangle bounds;
            private readonly bool restoreVisible, visible, restoreHint;
            private readonly string hint;
            private readonly bool autoSize, fixedSize, drawImage, pixelDetect, useOffSet, blend;
            private readonly float imageOpacity;
            private readonly TextFormatFlags format;
            private readonly MirLibrary library;
            private readonly LibraryFile libraryFile;
            private readonly int index;
            private readonly ButtonImageStates states;
            private readonly bool normalFrameHitTest, sort;
            private readonly Color foreColour, backColour, borderColour;
            private readonly bool border, drawTexture;
            internal HudControlState(DXControl c, bool restoreVisible, bool binding)
            {
                control = c; parent = c.Parent; bounds = new Rectangle(c.Location, c.Size); visible = c.Visible; this.restoreVisible = restoreVisible;
                var label = c as DXLabel; if (label != null) { autoSize = label.AutoSize; format = label.DrawFormat; }
                restoreHint = binding; hint = c.Hint; sort = c.Sort;
                var image = c as DXImageControl; if (image != null) { fixedSize = image.FixedSize; drawImage = image.DrawImage; pixelDetect = image.PixelDetect; useOffSet = image.UseOffSet; blend = image.Blend; imageOpacity = image.ImageOpacity; library = image.Library; libraryFile = image.LibraryFile; index = image.Index; }
                states = (c as DXButton)?.ImageStates; normalFrameHitTest = (c as DXButton)?.NormalFrameHitMask != null; foreColour = c.ForeColour;
                backColour = c.BackColour; borderColour = c.BorderColour; border = c.Border; drawTexture = c.DrawTexture;
            }
            internal void Restore()
            {
                control.Parent = parent;
                control.BackColour = backColour; control.BorderColour = borderColour; control.Border = border; control.DrawTexture = drawTexture;
                var button = control as DXButton;
                if (button != null)
                {
                    if (states != null) button.BindButtonImages(library, states, normalFrameHitTest); else button.BindImage(null, -1);
                }
                var image = control as DXImageControl;
                if (image != null) { image.LibraryFile = libraryFile; image.Library = library; image.Index = index; image.FixedSize = fixedSize; image.DrawImage = drawImage; image.PixelDetect = pixelDetect; image.UseOffSet = useOffSet; image.Blend = blend; image.ImageOpacity = imageOpacity; image.ForeColour = foreColour; }
                var label = control as DXLabel;
                if (label != null) { label.DrawFormat = format; label.AutoSize = autoSize; }
                control.Location = bounds.Location;
                if (label == null || !autoSize) control.Size = bounds.Size;
                control.Sort = sort;
                if (restoreHint) control.Hint = hint;
                if (restoreVisible) control.Visible = visible;
            }
        }
    }
}
