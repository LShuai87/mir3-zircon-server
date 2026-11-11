using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Client.Controls;
using Client.Scenes.Views;

namespace Client.Envir
{
    // Opt-in geometry for an existing chat group. Never creates/replaces tabs or messages.
    // Frame rendering and scene placement are separate from this instance binding.
    public sealed class GameUiChatGroupBinding : IDisposable
    {
        public const int HeaderHeight = 24;
        private const int ArrowWidth = 18;
        public static readonly Size MinimumSize = new Size(106, 81);
        private readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        private readonly DXTabControl group;
        private readonly Rectangle originalBounds;
        private readonly Dictionary<ChatTab, TabState> states = new Dictionary<ChatTab, TabState>();
        private readonly HashSet<ChatTab> disposingTabs = new HashSet<ChatTab>();
        private readonly GameUiChatViewportLayout viewport = new GameUiChatViewportLayout(new Padding(4, 2, 4, 4), 16, 2);
        private readonly GameUiWindowSkin skin;
        private ChatFrame frame;
        private DXControl channelRail;
        private DXButton[] channelButtons;
        private DXCheckBox[] channelFilters;
        private readonly Dictionary<ChatTab, GameUiChatArtwork[]> scrollArtwork = new Dictionary<ChatTab, GameUiChatArtwork[]>();
        private static readonly string[] ChannelNames = { "公聊", "私聊", "喊话", "组队聊天", "行会聊天" };
        private bool changingBackground;
        private DXButton previous, next;
        private bool refreshing, detaching, disposed, preserveNoSelection;
        private int firstIndex;
        public DXTabControl Group => group;
        public int FirstVisibleTabIndex => firstIndex;
        public int ContentTop => skin == null ? HeaderHeight : 0;

        public GameUiChatGroupBinding(DXTabControl group, GameUiWindowSkin skin = null)
        {
            this.group = group ?? throw new ArgumentNullException(nameof(group));
            if (group.IsDisposed) throw new ObjectDisposedException(nameof(group));
            if (group.GameUiChatGroup != null || group.IsMoving || group.IsResizing)
                throw new InvalidOperationException("Chat group is not idle or already has an owner.");
            foreach (var tab in group.Controls.OfType<DXTab>()) ValidateTab(tab);
            if (skin != null && (skin.FixedSize || skin.MinimumSize != MinimumSize))
                throw new ArgumentException("Chat frame requires the resizable group minimum.", nameof(skin));
            this.skin = skin;
            originalBounds = new Rectangle(group.Location, group.Size);
            group.GameUiChatGroup = this;
            group.Disposing += GroupDisposing;
            group.BeforeDraw += RefreshBackground;
            group.SelectedTabChanged += SelectionChanged;
            group.LocationChanged += RailGeometryChanged;
            group.IsVisibleChanged += RailGeometryChanged;
            group.ParentChanged += RailGeometryChanged;
            try { Refresh(); }
            catch { Dispose(); throw; }
        }

        public Size ConstrainSize(Size size)
            => new Size(Math.Max(size.Width, MinimumSize.Width), Math.Max(size.Height, MinimumSize.Height));

        private void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThread)
                throw new InvalidOperationException("Chat geometry belongs to the scene thread.");
        }
        private static void ValidateTab(DXTab tab)
        {
            if (!(tab is ChatTab chat) || tab.FixedGeometry != null || chat.GameUiViewportLayout != null ||
                tab.IsMoving || tab.IsResizing || tab.Updating || tab.TabButton.IsMoving || tab.TabButton.IsResizing)
                throw new InvalidOperationException("Chat geometry requires original idle chat tabs.");
        }
        private ChatTab[] LiveTabs() => group.TabButtons.Where(b => !b.IsDisposed)
            .Select(b => group.Controls.OfType<ChatTab>().FirstOrDefault(t => t.TabButton == b))
            .Where(t => t != null && !t.IsDisposed && !disposingTabs.Contains(t)).ToArray();

        public void Refresh()
        {
            CheckThread();
            if (disposed || detaching || refreshing || group.IsDisposed) return;
            refreshing = true;
            try
            {
                disposingTabs.RemoveWhere(t => t.IsDisposed);
                var tabs = LiveTabs();
                foreach (var tab in states.Keys.Except(tabs).ToArray()) Release(tab, true);
                foreach (var tab in tabs)
                {
                    if (states.ContainsKey(tab)) continue;
                    // A dragged tab is allowed to enter without disabling its native drag handler.
                    if (tab.FixedGeometry != null || tab.GameUiViewportLayout != null)
                        throw new InvalidOperationException("A chat tab already has a geometry owner.");
                    states.Add(tab, new TabState(tab));
                    tab.TabButton.SizeChanged += ButtonSizeChanged;
                    tab.TabButton.Label.TextChanged += ButtonTextChanged;
                    tab.Disposing += TabDisposing;
                    if (skin != null)
                    {
                        tab.DrawTextureChanged += NativeTextureChanged; tab.DrawOtherBorderChanged += NativeBorderChanged;
                        scrollArtwork.Add(tab, new[] { new GameUiChatArtwork(tab.ScrollBar.UpButton, 20),
                            new GameUiChatArtwork(tab.ScrollBar.DownButton, 24), new GameUiChatArtwork(tab.ScrollBar.PositionBar, 28) });
                    }
                }
                if (tabs.Length == 0)
                {
                    // Native drag cleanup disposes a group only when Controls is empty.
                    RemoveArrows();
                    RemoveChannelRail();
                    frame?.Dispose(); frame = null;
                    firstIndex = 0;
                    return;
                }
                group.Size = ConstrainSize(group.Size);
                if ((!preserveNoSelection && group.SelectedTab == null) ||
                    (group.SelectedTab != null && !tabs.Contains(group.SelectedTab))) group.SelectedTab = tabs[0];
                foreach (var tab in tabs)
                {
                    if (tab.Updating) continue;
                    var anchor = new GameUiChatScrollAnchor(tab);
                    tab.Location = new Point(0, ContentTop);
                    tab.Size = new Size(group.Size.Width, group.Size.Height - ContentTop);
                    if (tab.GameUiViewportLayout != viewport) tab.UseGameUiViewport(viewport);
                    anchor.Restore();
                }
                LayoutButtons(tabs);
                if (skin != null && frame == null)
                {
                    frame = new ChatFrame { Parent = group, WindowSkin = skin, IsControl = false,
                        Sort = false, Movable = false, AllowResize = false, PassThrough = true };
                    group.Controls.Remove(frame);
                    group.Controls.Insert(0, frame);
                }
                if (frame != null)
                {
                    frame.Size = group.Size;
                    // Native drop promotes its header to index zero; the frame must stay below it.
                    group.Controls.Remove(frame);
                    group.Controls.Insert(0, frame);
                }
                if (skin != null) RefreshChannelRail();
                RefreshBackground(this, EventArgs.Empty);
            }
            finally { refreshing = false; }
        }

        private int NaturalWidth(ChatTab tab)
        {
            var state = states[tab];
            if (state.OriginalTitle == tab.TabButton.Label.Text) return state.ButtonBounds.Width;
            return Math.Max(60, DXLabel.GetSize(tab.TabButton.Label.Text, tab.TabButton.Label.Font,
                tab.TabButton.Label.Outline).Width);
        }
        private void LayoutButtons(ChatTab[] tabs)
        {
            if (skin != null)
            {
                // Official chat has no header row. Keep native tabs and settings intact.
                foreach (var tab in tabs) tab.TabButton.Visible = false;
                firstIndex = 0;
                RemoveArrows();
                return;
            }
            int width = group.Size.Width - 8;
            bool overflow = tabs.Sum(t => (long)NaturalWidth(t) + 1) - 1 > width;
            int available = width - (overflow ? ArrowWidth * 2 + 2 : 0);
            if (!overflow) firstIndex = 0;
            firstIndex = Math.Max(0, Math.Min(firstIndex, tabs.Length - 1));
            int x = 4, last = firstIndex - 1;
            for (int i = 0; i < tabs.Length; i++)
            {
                var button = tabs[i].TabButton;
                int size = Math.Min(NaturalWidth(tabs[i]), available);
                bool visible = i >= firstIndex && (i == firstIndex || x + size <= 4 + available);
                // The native drag-out/merge handler owns a moving header's coordinates.
                if (!button.IsMoving)
                {
                    button.Visible = states[tabs[i]].ButtonVisible && visible;
                    button.Size = new Size(size, 22);
                    if (visible) button.Location = new Point(x, 2);
                }
                if (visible) { x += size + 1; last = i; }
                // Once a header doesn't fit, later headers belong to the next page too.
                else if (i >= firstIndex) x = 4 + available + 1;
            }
            if (overflow)
            {
                EnsureArrows();
                previous.Location = new Point(group.Size.Width - 4 - ArrowWidth * 2, 2);
                next.Location = new Point(group.Size.Width - 4 - ArrowWidth, 2);
                previous.Enabled = firstIndex > 0;
                next.Enabled = last < tabs.Length - 1;
                previous.Tag = Math.Max(0, firstIndex - 1);
                next.Tag = Math.Min(tabs.Length - 1, last + 1);
            }
            else RemoveArrows();
        }
        private void EnsureArrows()
        {
            if (previous != null) return;
            previous = MakeArrow("<", "Previous chat tabs");
            next = MakeArrow(">", "Next chat tabs");
        }
        private DXButton MakeArrow(string text, string hint)
        {
            var button = new DXButton { Parent = group, Size = new Size(ArrowWidth, 22),
                ButtonType = ButtonType.SmallButton, Hint = hint, Sort = false };
            button.Label.Text = text;
            button.MouseClick += PageClicked;
            return button;
        }
        private void PageClicked(object sender, MouseEventArgs args)
        {
            if ((args != null && args.Button != MouseButtons.Left) || !((DXButton)sender).Enabled) return;
            firstIndex = (int)((DXButton)sender).Tag;
            Refresh();
        }
        internal void RestoreSelection(ChatTab selected)
        {
            CheckThread();
            if (disposed || group.IsDisposed) throw new ObjectDisposedException(nameof(GameUiChatGroupBinding));
            if (selected != null && !LiveTabs().Contains(selected)) throw new ArgumentException("Page does not belong to this group.", nameof(selected));
            preserveNoSelection = selected == null;
            group.SelectedTab = selected;
            Refresh();
        }
        private void SelectionChanged(object sender, EventArgs args)
        {
            if (group.SelectedTab != null) preserveNoSelection = false;
            if (skin != null && !disposed && !detaching) RefreshChannelRail();
        }
        public void RevealSelectedTab()
        {
            CheckThread();
            if (disposed || refreshing || group.IsDisposed) return;
            var tabs = LiveTabs();
            int index = Array.FindIndex(tabs, t => t == group.SelectedTab);
            if (index >= 0 && !tabs[index].TabButton.Visible) firstIndex = index;
            Refresh();
        }
        private void RefreshBackground(object sender, EventArgs args)
        {
            if (frame == null || changingBackground || disposed) return;
            changingBackground = true;
            try
            {
                foreach (var tab in states.Keys)
                { tab.DrawTexture = false; tab.DrawOtherBorder = false; }
                var selected = group.SelectedTab as ChatTab;
                frame.Visible = selected != null && selected.Panel?.TransparentCheckBox.Checked != true;
                frame.Opacity = 1F;
                ChannelFilterChanged(this, EventArgs.Empty);
            }
            finally { changingBackground = false; }
        }
        private void RailGeometryChanged(object sender, EventArgs args)
        {
            if (skin != null && !disposed && !detaching && channelRail != null) RefreshChannelRail();
        }
        private void RefreshChannelRail()
        {
            if (group.IsDisposed || group.Parent == null || LiveTabs().Length == 0) { RemoveChannelRail(); return; }
            if (channelRail == null)
            {
                channelRail = new DXControl { Parent = group.Parent, Sort = false, Size = new Size(35, 130) };
                new GameUiChatArtwork(channelRail);
                channelButtons = new DXButton[5];
                for (int i = 0; i < channelButtons.Length; i++)
                {
                    int index = i;
                    var button = channelButtons[i] = new DXButton { Parent = channelRail, Size = new Size(31, 24),
                        Location = new Point(2, 4 + i * 24), CanBePressed = false, Tag = "chat-reject-" + i };
                    new GameUiChatArtwork(button, i * 4, true);
                    button.MouseClick += (o, e) =>
                    {
                        if (e != null && e.Button != MouseButtons.Left) return;
                        RefreshChannelRail();
                        var filter = channelFilters?[index];
                        if (filter != null && !filter.IsDisposed) filter.Checked = !filter.Checked;
                    };
                }
            }
            channelRail.Parent = group.Parent;
            channelRail.Location = new Point(Math.Max(0, group.Location.X - 35), group.Location.Y);
            channelRail.Visible = group.IsVisible;
            // Keep the rail next to its group in scene draw order, below dialogs opened later.
            var siblings = group.Parent.Controls;
            if (siblings.IndexOf(channelRail) != siblings.IndexOf(group) - 1)
            {
                siblings.Remove(channelRail);
                siblings.Insert(Math.Max(0, siblings.IndexOf(group)), channelRail);
            }
            var panel = (group.SelectedTab as ChatTab)?.Panel;
            var filters = panel == null || panel.IsDisposed ? new DXCheckBox[5] : new[] {
                panel.LocalCheckBox, panel.WhisperCheckBox, panel.ShoutCheckBox, panel.GroupCheckBox, panel.GuildCheckBox };
            if (channelFilters == null || !channelFilters.SequenceEqual(filters))
            {
                UnsubscribeFilters();
                channelFilters = filters;
                foreach (var filter in channelFilters.Where(f => f != null)) filter.CheckedChanged += ChannelFilterChanged;
            }
            ChannelFilterChanged(this, EventArgs.Empty);
        }
        private void ChannelFilterChanged(object sender, EventArgs args)
        {
            if (channelButtons == null) return;
            for (int i = 0; i < channelButtons.Length; i++)
            {
                var filter = channelFilters?[i];
                var button = channelButtons[i];
                button.Enabled = filter != null && !filter.IsDisposed;
                button.Pressed = button.Enabled && !filter.Checked;
                button.Hint = "拒绝" + ChannelNames[i] + "信息" + (button.Pressed ? "（已拒绝，点击接收）" : "（接收中，点击拒绝）");
            }
        }
        private void UnsubscribeFilters()
        {
            if (channelFilters != null)
                foreach (var filter in channelFilters.Where(f => f != null)) filter.CheckedChanged -= ChannelFilterChanged;
            channelFilters = null;
        }
        private void RemoveChannelRail()
        {
            UnsubscribeFilters();
            channelRail?.Dispose(); channelRail = null; channelButtons = null;
        }
        private void NativeTextureChanged(object sender, EventArgs args)
        {
            if (changingBackground || !states.TryGetValue((ChatTab)sender, out var state)) return;
            state.DrawTexture = ((ChatTab)sender).DrawTexture;
            RefreshBackground(sender, args);
        }
        private void NativeBorderChanged(object sender, EventArgs args)
        {
            if (changingBackground || !states.TryGetValue((ChatTab)sender, out var state)) return;
            state.DrawOtherBorder = ((ChatTab)sender).DrawOtherBorder;
            RefreshBackground(sender, args);
        }
        private void RemoveArrows()
        {
            previous?.Dispose(); next?.Dispose();
            previous = next = null;
        }
        private void ButtonSizeChanged(object sender, EventArgs args) => Refresh();
        private void ButtonTextChanged(object sender, EventArgs args) => Refresh();
        private void TabDisposing(object sender, EventArgs args)
        {
            var tab = (ChatTab)sender;
            disposingTabs.Add(tab);
            Release(tab, false);
            group.TabButtons.Remove(tab.TabButton);
            Refresh();
        }
        private void GroupDisposing(object sender, EventArgs args) => Detach(false);
        private void Release(ChatTab tab, bool restore)
        {
            if (!states.TryGetValue(tab, out var state)) return;
            tab.TabButton.SizeChanged -= ButtonSizeChanged;
            tab.TabButton.Label.TextChanged -= ButtonTextChanged;
            tab.Disposing -= TabDisposing;
            tab.DrawTextureChanged -= NativeTextureChanged;
            tab.DrawOtherBorderChanged -= NativeBorderChanged;
            if (restore && !tab.IsDisposed)
            {
                var anchor = new GameUiChatScrollAnchor(tab);
                bool updating = tab.Updating; tab.Updating = true;
                try
                {
                    tab.DrawTexture = tab.Panel == null ? state.DrawTexture : !tab.Panel.TransparentCheckBox.Checked;
                    tab.DrawOtherBorder = tab.Panel == null ? state.DrawOtherBorder : !tab.Panel.TransparentCheckBox.Checked;
                    tab.UseGameUiViewport(state.Viewport);
                    if (tab.Parent == group)
                    {
                        tab.Location = state.Bounds.Location;
                        tab.Size = state.Bounds.Size;
                    }
                    else tab.Location = new Point(0, DXTab.TabHeight - 1);
                    tab.TabButton.Visible = state.ButtonVisible;
                    tab.TabButton.Size = new Size(state.OriginalTitle == tab.TabButton.Label.Text ?
                        state.ButtonBounds.Width : Math.Max(60, DXLabel.GetSize(tab.TabButton.Label.Text,
                            tab.TabButton.Label.Font, tab.TabButton.Label.Outline).Width), state.ButtonBounds.Height);
                    if (tab.Parent == group) tab.TabButton.Location = state.ButtonBounds.Location;
                }
                finally { tab.Updating = updating; }
                anchor.Restore();
            }
            if (scrollArtwork.TryGetValue(tab, out var artwork))
            {
                foreach (var control in artwork) control.Dispose();
                scrollArtwork.Remove(tab);
            }
            states.Remove(tab);
        }
        private void Detach(bool restore)
        {
            CheckThread();
            if (disposed || detaching) return;
            detaching = true;
            try
            {
                var anchors = restore ? states.Keys.Where(t => !t.IsDisposed).Select(t => new GameUiChatScrollAnchor(t)).ToArray()
                    : new GameUiChatScrollAnchor[0];
                group.Disposing -= GroupDisposing;
                group.BeforeDraw -= RefreshBackground;
                group.SelectedTabChanged -= SelectionChanged;
                group.LocationChanged -= RailGeometryChanged;
                group.IsVisibleChanged -= RailGeometryChanged;
                group.ParentChanged -= RailGeometryChanged;
                RemoveChannelRail();
                if (group.GameUiChatGroup == this) group.GameUiChatGroup = null;
                RemoveArrows();
                frame?.Dispose(); frame = null;
                foreach (var tab in states.Keys.ToArray()) Release(tab, restore);
                disposingTabs.Clear();
                if (restore && !group.IsDisposed)
                {
                    group.Size = originalBounds.Size;
                    group.Location = originalBounds.Location;
                    group.TabsChanged();
                }
                foreach (var anchor in anchors) anchor.Restore();
                disposed = true;
            }
            finally { detaching = false; }
        }
        public void Dispose() => Detach(!group.IsDisposed);

        private sealed class ChatFrame : DXWindow
        {
            public override Client.UserModels.WindowType Type => Client.UserModels.WindowType.None;
            public override bool CustomSize => false;
            public override bool AutomaticVisiblity => false;
            internal ChatFrame()
            {
                HasTitle = false; HasFooter = false; CloseButton.Visible = false;
                new GameUiChatArtwork(this);
            }
        }
        private sealed class TabState
        {
            internal readonly Rectangle Bounds, ButtonBounds;
            internal readonly bool ButtonVisible;
            internal bool DrawTexture, DrawOtherBorder;
            internal readonly string OriginalTitle;
            internal readonly GameUiChatViewportLayout Viewport;
            internal TabState(ChatTab tab)
            {
                Bounds = new Rectangle(tab.Location, tab.Size);
                ButtonBounds = new Rectangle(tab.TabButton.Location, tab.TabButton.Size);
                ButtonVisible = tab.TabButton.Visible;
                DrawTexture = tab.DrawTexture; DrawOtherBorder = tab.DrawOtherBorder;
                OriginalTitle = tab.TabButton.Label.Text;
                Viewport = tab.GameUiViewportLayout;
            }
        }
    }
}
