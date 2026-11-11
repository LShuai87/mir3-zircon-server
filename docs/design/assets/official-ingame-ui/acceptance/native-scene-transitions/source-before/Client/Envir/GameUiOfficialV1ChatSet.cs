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
    // Borrows skins from the verified package; dispose before releasing that package.
    // All tabs, history, channels and the native editor remain owned by the original scene.
    public sealed class GameUiOfficialV1ChatSet : IDisposable
    {
        private readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        private readonly DXControl scene;
        private readonly ChatTextBox input;
        private readonly GameUiWindowSkin historySkin, inputSkin;
        private readonly Rectangle inputBounds;
        private readonly bool inputAllowResize;
        private readonly string optionsText;
        private readonly Dictionary<DXTabControl, GameUiChatGroupBinding> groups = new Dictionary<DXTabControl, GameUiChatGroupBinding>();
        private readonly Dictionary<DXTabControl, string> identities = new Dictionary<DXTabControl, string>();
        private readonly GameUiChatStore store;
        private Size activeResolution;
        private DXTabControl primary;
        private Rectangle lastDock;
        private bool busy, disposed, installed, primaryFloating;
        public int BoundGroupCount => groups.Count(g => !g.Key.IsDisposed);

        public GameUiOfficialV1ChatSet(DXControl scene, ChatTextBox input,
            GameUiWindowSkin historySkin, GameUiWindowSkin inputSkin, GameUiChatStore store = null)
        {
            this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
            this.input = input ?? throw new ArgumentNullException(nameof(input));
            this.historySkin = historySkin ?? throw new ArgumentNullException(nameof(historySkin));
            this.inputSkin = inputSkin ?? throw new ArgumentNullException(nameof(inputSkin));
            this.store = store;
            if (scene.IsDisposed || input.IsDisposed || input.Parent != scene || input.WindowSkin != null ||
                input.GameUiInputLayout != null || input.IsMoving || input.IsResizing)
                throw new InvalidOperationException("Chat set requires the original idle input in this scene.");
            if (inputSkin.FrameSize != new Size(574, 22) || !inputSkin.FixedSize ||
                historySkin.FixedSize || historySkin.MinimumSize != GameUiChatGroupBinding.MinimumSize)
                throw new ArgumentException("Chat skins differ from the compiled layout.");
            inputBounds = new Rectangle(input.Location, input.Size);
            inputAllowResize = input.AllowResize;
            optionsText = input.OptionsButton.Label.Text;
        }
        private void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThread)
                throw new InvalidOperationException("Chat set belongs to the scene thread.");
            if (disposed) throw new ObjectDisposedException(nameof(GameUiOfficialV1ChatSet));
        }
        private DXTabControl[] CurrentGroups() => ChatTab.Tabs
            .Where(t => !t.IsDisposed && t.Parent is DXTabControl g && !g.IsDisposed && g.Parent == scene)
            .Select(t => (DXTabControl)t.Parent).Distinct().ToArray();

        public void Install(Size resolution)
        {
            CheckThread();
            if (installed || busy) throw new InvalidOperationException("Chat set is already installed or changing layout.");
            if (resolution.Width < 1024 || resolution.Height < 768) throw new ArgumentOutOfRangeException(nameof(resolution));
            if (input.IsDisposed || scene.IsDisposed || input.Parent != scene || input.WindowSkin != null ||
                input.GameUiInputLayout != null || new Rectangle(input.Location, input.Size) != inputBounds)
                throw new InvalidOperationException("Original chat input changed before installation.");
            busy = true;
            try
            {
                var initial = CurrentGroups();
                if (initial.Any(g => g.IsMoving || g.IsResizing || g.Controls.OfType<ChatTab>()
                    .Any(t => t.Updating || t.IsMoving || t.IsResizing || t.TabButton.IsMoving || t.TabButton.IsResizing)))
                    throw new InvalidOperationException("Original chat groups must be idle before installation.");
                primary = initial.OrderBy(g => Math.Abs((long)g.Location.X - inputBounds.X) +
                    Math.Abs((long)g.Location.Y + g.Size.Height - inputBounds.Y)).FirstOrDefault();
                Discover();
                input.WindowSkin = inputSkin;
                input.UseGameUiInputLayout(new GameUiChatInputLayout(new Padding(4, 1, 4, 1), 56, 22, 4));
                input.OptionsButton.Label.Text = "设";
                input.AllowResize = false;
                installed = true;
                ApplyCore(resolution, true);
                activeResolution = resolution;
                ChatTab.GameUiTopologyChanged += TopologyChanged;
            }
            catch { Dispose(); throw; }
            finally { busy = false; }
        }
        private void Discover()
        {
            foreach (var group in groups.Keys.Where(g => g.IsDisposed || g.Parent != scene).ToArray())
            { groups[group].Dispose(); groups.Remove(group); identities.Remove(group); if (primary == group) primary = null; }
            foreach (var group in CurrentGroups())
            {
                if (groups.ContainsKey(group)) { groups[group].Refresh(); continue; }
                // A native drag may be midway through creating a group. Its drop event retries discovery.
                if (group.IsMoving || group.IsResizing || group.Controls.OfType<ChatTab>()
                    .Any(t => t.Updating || t.IsMoving || t.IsResizing || t.TabButton.IsMoving || t.TabButton.IsResizing)) continue;
                groups.Add(group, new GameUiChatGroupBinding(group, historySkin));
                identities.Add(group, Guid.NewGuid().ToString("N"));
            }
        }
        private void TopologyChanged(object sender, EventArgs args)
        {
            if (disposed || busy || !installed || scene.IsDisposed) return;
            CheckThread();
            busy = true;
            try { Discover(); }
            finally { busy = false; }
        }
        public void Apply(Size resolution)
        {
            CheckThread();
            if (!installed || busy) throw new InvalidOperationException("Chat set is not ready for a layout pass.");
            if (resolution.Width < 1024 || resolution.Height < 768) throw new ArgumentOutOfRangeException(nameof(resolution));
            busy = true;
            try
            {
                Discover();
                GameUiChatStore.Snapshot saved;
                if (resolution != activeResolution && store != null && store.TryGet(resolution, out saved) && Matches(saved))
                    ApplySavedGeometry(saved, identities.ToDictionary(p => p.Value, p => p.Key));
                ApplyCore(resolution, false);
                activeResolution = resolution;
            }
            finally { busy = false; }
        }
        private static ChatTab[] OrderedTabs(DXTabControl group)
        {
            var pages = group.Controls.OfType<ChatTab>().Where(t => !t.IsDisposed).ToArray();
            var ordered = group.TabButtons.Select(b => pages.SingleOrDefault(t => t.TabButton == b)).Where(t => t != null).ToArray();
            if (ordered.Length != pages.Length || ordered.Distinct().Count() != pages.Length)
                throw new InvalidOperationException("Native chat header order is incomplete.");
            return ordered;
        }
        private static DXCheckBox[] Filters(ChatOptionsPanel panel) => new[] {
            panel.LocalCheckBox, panel.WhisperCheckBox, panel.GroupCheckBox, panel.GuildCheckBox,
            panel.ShoutCheckBox, panel.GlobalCheckBox, panel.ObserverCheckBox, panel.HintCheckBox,
            panel.SystemCheckBox, panel.GainsCheckBox, panel.TransparentCheckBox, panel.AlertCheckBox };
        internal static int CaptureFlags(ChatTab tab)
        {
            int flags = 0; var filters = Filters(tab.Panel);
            for (int i = 0; i < filters.Length; i++) if (filters[i].Checked) flags |= 1 << i;
            return flags;
        }
        internal static void RestorePage(ChatTab tab, GameUiChatStore.Tab saved)
        {
            tab.Panel.NameTextBox.TextBox.Text = saved.name;
            var filters = Filters(tab.Panel);
            for (int i = 0; i < filters.Length; i++) filters[i].Checked = (saved.flags & (1 << i)) != 0;
        }
        internal GameUiChatStore.Snapshot Capture(Size resolution)
        {
            CheckThread();
            if (!installed || busy) throw new InvalidOperationException("Chat set is not ready to save.");
            Discover();
            var current = CurrentGroups();
            if (current.Any(g => !groups.ContainsKey(g) || g.IsMoving || g.IsResizing ||
                g.Controls.OfType<ChatTab>().Any(t => t.Updating || t.IsMoving || t.IsResizing || t.TabButton.IsMoving || t.TabButton.IsResizing)))
                throw new InvalidOperationException("Finish moving chat before saving.");
            busy = true;
            try
            {
                ApplyCore(resolution, false);
                return new GameUiChatStore.Snapshot { width = resolution.Width, height = resolution.Height,
                    groups = current.Select(g => {
                        var tabs = OrderedTabs(g);
                        return new GameUiChatStore.Group { id = identities[g], x = g.Location.X, y = g.Location.Y,
                            width = g.Size.Width, height = g.Size.Height, primary = g == primary,
                            floating = g != primary || primaryFloating, selected = Array.IndexOf(tabs, g.SelectedTab),
                            tabs = tabs.Select(t => new GameUiChatStore.Tab { name = t.Panel.NameTextBox.TextBox.Text, flags = CaptureFlags(t) }).ToArray() };
                    }).ToArray() };
            }
            finally { busy = false; }
        }
        private bool Matches(GameUiChatStore.Snapshot saved)
        {
            if (saved.groups.Length != groups.Count) return false;
            var byId = identities.ToDictionary(p => p.Value, p => p.Key);
            return saved.groups.All(g => byId.ContainsKey(g.id) &&
                OrderedTabs(byId[g.id]).Select(t => t.Panel.NameTextBox.TextBox.Text).SequenceEqual(g.tabs.Select(t => t.name)));
        }
        private void ApplySavedGeometry(GameUiChatStore.Snapshot saved, Dictionary<string, DXTabControl> byId)
        {
            primary = null; primaryFloating = false;
            foreach (var value in saved.groups)
            {
                var group = byId[value.id];
                group.Size = new Size(value.width, value.height); group.Location = new Point(value.x, value.y);
                if (value.primary) { primary = group; primaryFloating = value.floating; lastDock = new Rectangle(group.Location, group.Size); }
                groups[group].Refresh();
            }
        }
        internal void Restore(GameUiChatStore.Snapshot saved, IList<DXTabControl> restored)
        {
            CheckThread();
            if (!installed || busy || saved.groups.Length != restored.Count || groups.Count != restored.Count ||
                restored.Distinct().Count() != restored.Count || restored.Any(g => !groups.ContainsKey(g)))
                throw new InvalidOperationException("Restored native chat groups do not match the saved topology.");
            busy = true;
            try
            {
                identities.Clear();
                for (int i = 0; i < restored.Count; i++) identities.Add(restored[i], saved.groups[i].id);
                if (!Matches(saved)) throw new InvalidOperationException("Restored native chat pages do not match the saved topology.");
                for (int i = 0; i < restored.Count; i++)
                {
                    var pages = OrderedTabs(restored[i]);
                    groups[restored[i]].RestoreSelection(saved.groups[i].selected < 0 ? null : pages[saved.groups[i].selected]);
                }
                ApplySavedGeometry(saved, identities.ToDictionary(p => p.Value, p => p.Key));
                activeResolution = new Size(saved.width, saved.height);
            }
            finally { busy = false; }
        }
        private void ApplyCore(Size resolution, bool initial)
        {
            if (scene.IsDisposed || input.IsDisposed || input.Parent != scene)
                throw new InvalidOperationException("Original chat input was removed from its scene.");
            input.Size = new Size(574, 22);
            input.Location = new Point((resolution.Width - 574) / 2, resolution.Height - 30);
            foreach (var pair in groups)
            {
                var group = pair.Key;
                if (group.IsDisposed || group.Parent != scene || !group.Controls.OfType<ChatTab>().Any()) continue;
                if (group == primary)
                {
                    if (!initial && new Rectangle(group.Location, group.Size) != lastDock) primaryFloating = true;
                    if (!primaryFloating)
                    {
                        group.Size = new Size(574, 106);
                        group.Location = new Point((resolution.Width - 574) / 2, resolution.Height - 138);
                        lastDock = new Rectangle(group.Location, group.Size);
                    }
                }
                // Keep user floating windows and dimensions; only clamp into a smaller client.
                if (group != primary || primaryFloating)
                {
                    group.Size = new Size(Math.Min(resolution.Width, group.Size.Width), Math.Min(resolution.Height, group.Size.Height));
                    group.Location = new Point(Math.Max(0, Math.Min(group.Location.X, resolution.Width - group.Size.Width)),
                        Math.Max(0, Math.Min(group.Location.Y, resolution.Height - group.Size.Height)));
                }
                pair.Value.Refresh();
            }
        }
        public void Dispose()
        {
            if (disposed) return;
            CheckThread();
            ChatTab.GameUiTopologyChanged -= TopologyChanged;
            var failures = new List<Exception>();
            foreach (var group in groups.Values.ToArray())
            {
                try { group.Dispose(); } catch (Exception error) { failures.Add(error); }
            }
            if (failures.Count != 0) throw new AggregateException("Chat group rollback needs retry; keep the package alive.", failures);
            groups.Clear();
            identities.Clear();
            if (!input.IsDisposed)
            {
                input.WindowSkin = null;
                input.UseGameUiInputLayout(null);
                input.AllowResize = inputAllowResize;
                input.Size = inputBounds.Size;
                input.Location = inputBounds.Location;
                input.OptionsButton.Label.Text = optionsText;
                input.TextBox.TextBox.Bounds = input.TextBox.DisplayArea;
            }
            installed = false;
            disposed = true;
        }
    }
}
