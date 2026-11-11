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
        private DXTabControl primary;
        private Rectangle lastDock;
        private bool busy, disposed, installed, primaryFloating;
        public int BoundGroupCount => groups.Count(g => !g.Key.IsDisposed);

        public GameUiOfficialV1ChatSet(DXControl scene, ChatTextBox input,
            GameUiWindowSkin historySkin, GameUiWindowSkin inputSkin)
        {
            this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
            this.input = input ?? throw new ArgumentNullException(nameof(input));
            this.historySkin = historySkin ?? throw new ArgumentNullException(nameof(historySkin));
            this.inputSkin = inputSkin ?? throw new ArgumentNullException(nameof(inputSkin));
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
                ChatTab.GameUiTopologyChanged += TopologyChanged;
            }
            catch { Dispose(); throw; }
            finally { busy = false; }
        }
        private void Discover()
        {
            foreach (var group in groups.Keys.Where(g => g.IsDisposed || g.Parent != scene).ToArray())
            { groups[group].Dispose(); groups.Remove(group); if (primary == group) primary = null; }
            foreach (var group in CurrentGroups())
            {
                if (groups.ContainsKey(group)) { groups[group].Refresh(); continue; }
                // A native drag may be midway through creating a group. Its drop event retries discovery.
                if (group.IsMoving || group.IsResizing || group.Controls.OfType<ChatTab>()
                    .Any(t => t.Updating || t.IsMoving || t.IsResizing || t.TabButton.IsMoving || t.TabButton.IsResizing)) continue;
                groups.Add(group, new GameUiChatGroupBinding(group, historySkin));
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
            try { Discover(); ApplyCore(resolution, false); }
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
