using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using Client.Controls;
using Client.Scenes.Views;
using Client.UserModels;

namespace Client.Envir
{
    // Concrete control adapter. Final catalog coordinates and skins are supplied by
    // the startup style context after preflight; constructing this does not enable a style.
    public sealed class GameUiSceneLayoutHost : IGameUiLayoutHost
    {
        private readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        private readonly GameUiLayoutStore store;
        private readonly Action restoreChat, restoreOverlayOrder;
        private readonly List<ControlParticipant> participants = new List<ControlParticipant>();
        private readonly List<GameUiLayoutGroup> groups = new List<GameUiLayoutGroup>();
        private int applying;
        private bool chatRestored, restoringChat;
        public DXControl SceneRoot { get; }

        public GameUiSceneLayoutHost(DXControl sceneRoot, GameUiLayoutStore layoutStore,
            Action restoreChat, Action restoreOverlayOrder)
        {
            SceneRoot = sceneRoot ?? throw new ArgumentNullException(nameof(sceneRoot));
            store = layoutStore ?? throw new ArgumentNullException(nameof(layoutStore));
            this.restoreChat = restoreChat ?? throw new ArgumentNullException(nameof(restoreChat));
            this.restoreOverlayOrder = restoreOverlayOrder ?? throw new ArgumentNullException(nameof(restoreOverlayOrder));
            CheckThread();
        }

        private void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThread)
                throw new InvalidOperationException("Scene layout must run on its owning thread.");
            if (SceneRoot.IsDisposed) throw new ObjectDisposedException(nameof(SceneRoot));
        }

        // Root-level controls use scene-relative coordinates. Scrolling content must
        // supply a viewport implementation, rather than merely advertising scroll flags.
        public void Register(string id, DXControl control, Action applySkin,
            Func<Size, GameUiLayoutRequest> describeLayout, bool persistWindowLayout = false,
            Action<GameUiLayoutPlacement> applyViewport = null)
        {
            CheckThread();
            if (applying != 0 && !restoringChat) throw new InvalidOperationException("Cannot register controls during a layout pass.");
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Layout ID is required.", nameof(id));
            if (control == null) throw new ArgumentNullException(nameof(control));
            if (control.IsDisposed || control.Parent != SceneRoot)
                throw new ArgumentException("Layout controls must be live, direct children of this scene.", nameof(control));
            if (applySkin == null) throw new ArgumentNullException(nameof(applySkin));
            if (describeLayout == null) throw new ArgumentNullException(nameof(describeLayout));
            if (participants.Any(p => p.Id == id || ReferenceEquals(p.Control, control)))
                throw new ArgumentException("Duplicate layout ID or control.", nameof(id));
            DXWindow window = control as DXWindow;
            if (persistWindowLayout && (window == null || window.Type == WindowType.None))
                throw new ArgumentException("Persisted participants require a named DXWindow.", nameof(control));
            if (persistWindowLayout && participants.Any(p => p.Persist && ((DXWindow)p.Control).Type == window.Type))
                throw new ArgumentException("Duplicate persisted window type.", nameof(control));
            if (persistWindowLayout) window.UseOfficialLayout(store);
            participants.Add(new ControlParticipant(id, control, applySkin, describeLayout,
                persistWindowLayout ? store : null, applyViewport));
        }

        public void AddGroup(GameUiLayoutGroup group)
        {
            CheckThread();
            if (applying != 0 && !restoringChat) throw new InvalidOperationException("Cannot change groups during layout.");
            if (group == null) throw new ArgumentNullException(nameof(group));
            // Participant existence and disjointness are checked by the coordinator.
            groups.Add(group);
        }

        public IDisposable SuppressLayoutWrites()
        {
            CheckThread();
            // Also protects existing legacy windows and windows created by restoreChat.
            IDisposable writes = DXWindow.SuppressSceneLayoutWrites(SceneRoot);
            try
            {
                var chat = Descendants(SceneRoot).OfType<ChatTab>().Select(t => new GameUiChatScrollAnchor(t)).ToArray();
                applying++;
                return new PassScope(this, writes, chat);
            }
            catch { writes.Dispose(); throw; }
        }

        public void EnsureChatRestored()
        {
            CheckThread();
            if (chatRestored) return;
            // Initial restoration may register the newly created chat containers.
            // Later passes keep the same controls and never call this callback again.
            restoringChat = true;
            try { restoreChat(); chatRestored = true; }
            finally { restoringChat = false; }
        }
        public IEnumerable<IGameUiLayoutParticipant> GetParticipants()
        {
            CheckThread();
            return participants.Cast<IGameUiLayoutParticipant>().ToArray();
        }
        public IEnumerable<GameUiLayoutGroup> GetGroups() { CheckThread(); return groups.ToArray(); }
        public void RestoreOverlayOrder() { CheckThread(); restoreOverlayOrder(); }
        public void RefreshChatInputBounds()
        {
            CheckThread();
            foreach (DXTextBox input in Descendants(SceneRoot).OfType<DXTextBox>())
            {
                // Do not activate, hide, replace or assign text: an active IME must
                // keep its HWND, composition, selection and focus throughout resizing.
                // DisplayArea uses the native font-constrained text height.
                input.TextBox.Bounds = input.DisplayArea;
            }
        }
        private static IEnumerable<DXControl> Descendants(DXControl root)
        {
            foreach (DXControl child in root.Controls.ToArray())
            {
                if (child.IsDisposed) continue;
                yield return child;
                foreach (DXControl descendant in Descendants(child)) yield return descendant;
            }
        }

        private sealed class PassScope : IDisposable
        {
            private GameUiSceneLayoutHost host;
            private readonly IDisposable writes;
            private readonly GameUiChatScrollAnchor[] chat;
            public PassScope(GameUiSceneLayoutHost host, IDisposable writes, GameUiChatScrollAnchor[] chat)
            { this.host = host; this.writes = writes; this.chat = chat; }
            public void Dispose()
            {
                if (host == null) return;
                if (Thread.CurrentThread.ManagedThreadId != host.ownerThread)
                    throw new InvalidOperationException("Layout scope must end on its owning thread.");
                try { foreach (GameUiChatScrollAnchor position in chat) position.Restore(); }
                finally { writes.Dispose(); host.applying--; host = null; }
            }
        }

        private sealed class ControlParticipant : IGameUiLayoutParticipant
        {
            private readonly Action applySkin;
            private readonly Func<Size, GameUiLayoutRequest> describe;
            private readonly Action<GameUiLayoutPlacement> viewport;
            private readonly GameUiLayoutStore store;
            private readonly DXControl parent;
            private bool settingsRestored;
            public string Id { get; }
            public DXControl Control { get; }
            public bool Persist => store != null;
            public ControlParticipant(string id, DXControl control, Action skin,
                Func<Size, GameUiLayoutRequest> describe, GameUiLayoutStore store,
                Action<GameUiLayoutPlacement> viewport)
            {
                Id = id; Control = control; parent = control.Parent;
                applySkin = skin; this.describe = describe; this.store = store; this.viewport = viewport;
            }
            private void ValidateControl()
            {
                if (Control.IsDisposed || Control.Parent != parent)
                    throw new InvalidOperationException("Layout participant was disposed or reparented: " + Id);
            }
            public void ApplySkin() { ValidateControl(); applySkin(); }
            public GameUiLayoutRequest DescribeLayout(Size resolution)
            {
                ValidateControl();
                if (Persist && resolution != Config.GameSize)
                    throw new InvalidOperationException("Layout resolution must match the active game configuration.");
                if (Persist && !settingsRestored)
                {
                    // Initial visibility may be restored once. Later resize passes
                    // only restore geometry; NPC/business visibility is never reset.
                    ((DXWindow)Control).LoadSettings();
                    settingsRestored = true;
                }
                GameUiLayoutRequest request = describe(resolution) ?? throw new InvalidOperationException("Missing layout request: " + Id);
                if ((request.HorizontalScroll || request.VerticalScroll) && viewport == null)
                    throw new InvalidOperationException("A real scrolling viewport is required: " + Id);
                if (!Persist) return request;
                if (request.SavedBounds.HasValue)
                    throw new InvalidOperationException("Persisted bounds must come from the matching OfficialV1 layout store.");
                var window = (DXWindow)Control;
                if (request.AllowResize != window.CustomSize)
                    throw new InvalidOperationException("Resize policy differs from the window persistence contract: " + Id);
                GameUiWindowLayout saved;
                Rectangle? bounds = store.TryGet(resolution, window.Type, out saved)
                    ? (Rectangle?)new Rectangle(saved.Location, saved.Size) : null;
                return new GameUiLayoutRequest(request.ContentSize, request.MinimumViewportSize, request.Anchor,
                    request.Offset, request.UseSafeArea, request.AllowResize, request.HorizontalScroll, request.VerticalScroll, bounds);
            }
            public void ApplyLayout(GameUiLayoutPlacement placement)
            {
                ValidateControl();
                Control.Size = placement.Bounds.Size;
                Control.Location = placement.Bounds.Location;
                viewport?.Invoke(placement);
                if (Control.Size != placement.Bounds.Size || Control.Location != placement.Bounds.Location)
                    throw new InvalidOperationException("Control rejected verified layout geometry: " + Id);
            }
            public void BringToFront() { ValidateControl(); Control.BringToFront(); }
        }
    }
}
