using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Threading;

namespace Client.Envir
{
    public enum GameUiAnchor
    {
        TopLeft, TopCenter, TopRight, MiddleLeft, Center, MiddleRight,
        BottomLeft, BottomCenter, BottomRight
    }

    // Values come from the verified catalog and matching layout store, never inferred art.
    public sealed class GameUiLayoutRequest
    {
        public Size ContentSize { get; }
        public Size MinimumViewportSize { get; }
        public GameUiAnchor Anchor { get; }
        public Point Offset { get; }
        public bool UseSafeArea { get; }
        public bool AllowResize { get; }
        public bool HorizontalScroll { get; }
        public bool VerticalScroll { get; }
        public Rectangle? SavedBounds { get; }

        public GameUiLayoutRequest(Size contentSize, Size minimumViewportSize, GameUiAnchor anchor,
            Point offset, bool useSafeArea, bool allowResize, bool horizontalScroll,
            bool verticalScroll, Rectangle? savedBounds = null)
        {
            if (contentSize.Width <= 0 || contentSize.Height <= 0 ||
                minimumViewportSize.Width <= 0 || minimumViewportSize.Height <= 0 ||
                minimumViewportSize.Width > contentSize.Width || minimumViewportSize.Height > contentSize.Height)
                throw new ArgumentException("Invalid content or minimum viewport size.");
            if (!Enum.IsDefined(typeof(GameUiAnchor), anchor)) throw new ArgumentOutOfRangeException(nameof(anchor));
            ContentSize = contentSize;
            MinimumViewportSize = minimumViewportSize;
            Anchor = anchor;
            Offset = offset;
            UseSafeArea = useSafeArea;
            AllowResize = allowResize;
            HorizontalScroll = horizontalScroll;
            VerticalScroll = verticalScroll;
            SavedBounds = savedBounds;
        }
    }

    public sealed class GameUiLayoutPlacement
    {
        public Rectangle Bounds { get; }
        public Size ContentSize { get; }
        public bool Restored { get; }
        public bool RequiresHorizontalScroll => ContentSize.Width > Bounds.Width;
        public bool RequiresVerticalScroll => ContentSize.Height > Bounds.Height;
        public GameUiLayoutPlacement(Rectangle bounds, Size contentSize, bool restored)
        {
            Bounds = bounds;
            ContentSize = contentSize;
            Restored = restored;
        }
    }

    public sealed class GameUiLayoutGroup
    {
        public string PrimaryId { get; }
        public string ServiceId { get; }
        public int Gap { get; }
        public int PrimaryHeaderHeight { get; }
        public GameUiLayoutGroup(string primaryId, string serviceId, int gap, int primaryHeaderHeight)
        {
            if (string.IsNullOrWhiteSpace(primaryId) || string.IsNullOrWhiteSpace(serviceId) || primaryId == serviceId)
                throw new ArgumentException("Two different group participants are required.");
            if (gap < 0 || primaryHeaderHeight <= 0) throw new ArgumentOutOfRangeException(nameof(gap));
            PrimaryId = primaryId;
            ServiceId = serviceId;
            Gap = gap;
            PrimaryHeaderHeight = primaryHeaderHeight;
        }
    }

    public interface IGameUiLayoutParticipant
    {
        string Id { get; }
        void ApplySkin();
        GameUiLayoutRequest DescribeLayout(Size resolution);
        // Implement scrolling/clipping before declaring the corresponding scroll capability.
        // Never set business visibility or rebuild chat tabs/messages in this callback.
        void ApplyLayout(GameUiLayoutPlacement placement);
        void BringToFront();
    }

    public interface IGameUiLayoutHost
    {
        // Must cover existing and newly created windows until the scope is disposed.
        IDisposable SuppressLayoutWrites();
        // Called once per coordinator, with writes suppressed. Restore instances, not messages on resize.
        void EnsureChatRestored();
        IEnumerable<IGameUiLayoutParticipant> GetParticipants();
        IEnumerable<GameUiLayoutGroup> GetGroups();
        // Keep modal dialogs and tooltips above grouped service windows.
        void RestoreOverlayOrder();
        void RefreshChatInputBounds();
    }

    // Independent of any final resource index/rectangle. GameScene connects this only after G0.
    public sealed class GameUiLayoutCoordinator
    {
        private readonly IGameUiLayoutHost host;
        private readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        private bool applying;
        private bool chatRestored;
        public IReadOnlyDictionary<string, GameUiLayoutPlacement> LastPlacements { get; private set; }
        private static readonly HashSet<Size> Resolutions = new HashSet<Size>
        {
            new Size(1024, 768), new Size(1366, 768), new Size(1280, 800),
            new Size(1440, 900), new Size(1600, 900), new Size(1920, 1080)
        };

        public GameUiLayoutCoordinator(IGameUiLayoutHost host)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
        }

        // False means a synchronous resize callback reentered this pass; it must not save or recurse.
        // Call from the owning scene thread; visibility and persistence stay with the host.
        public bool Apply(Size resolution, Rectangle safeArea)
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThread)
                throw new InvalidOperationException("Layout must run on its owning scene thread.");
            if (!Resolutions.Contains(resolution)) throw new ArgumentException("Unsupported game resolution.", nameof(resolution));
            Rectangle client = new Rectangle(Point.Empty, resolution);
            if (!Positive(safeArea) || !Contains(client, safeArea)) throw new ArgumentException("Invalid safe area.", nameof(safeArea));
            if (applying) return false;
            applying = true;
            try
            {
                using (IDisposable scope = host.SuppressLayoutWrites() ?? throw new InvalidOperationException("Missing write suppression scope."))
                {
                    if (!chatRestored)
                    {
                        host.EnsureChatRestored();
                        chatRestored = true;
                    }
                    var participants = (host.GetParticipants() ?? throw new InvalidOperationException("Missing participants.")).ToArray();
                    var byId = new Dictionary<string, IGameUiLayoutParticipant>(StringComparer.Ordinal);
                    foreach (IGameUiLayoutParticipant participant in participants)
                    {
                        if (participant == null || string.IsNullOrWhiteSpace(participant.Id) || byId.ContainsKey(participant.Id))
                            throw new InvalidOperationException("Null participant or duplicate/empty layout ID.");
                        byId.Add(participant.Id, participant);
                    }
                    var groups = (host.GetGroups() ?? throw new InvalidOperationException("Missing groups.")).ToArray();
                    var grouped = new HashSet<string>(StringComparer.Ordinal);
                    foreach (GameUiLayoutGroup group in groups)
                    {
                        if (group == null || !byId.ContainsKey(group.PrimaryId) || !byId.ContainsKey(group.ServiceId) ||
                            !grouped.Add(group.PrimaryId) || !grouped.Add(group.ServiceId))
                            throw new InvalidOperationException("Missing, duplicate or overlapping layout group.");
                    }
                    foreach (IGameUiLayoutParticipant participant in participants) participant.ApplySkin();
                    var requests = new Dictionary<string, GameUiLayoutRequest>(StringComparer.Ordinal);
                    var placements = new Dictionary<string, GameUiLayoutPlacement>(StringComparer.Ordinal);
                    foreach (IGameUiLayoutParticipant participant in participants)
                    {
                        GameUiLayoutRequest request = participant.DescribeLayout(resolution);
                        if (request == null) throw new InvalidOperationException("Missing layout request: " + participant.Id);
                        requests.Add(participant.Id, request);
                        placements.Add(participant.Id, Place(request, client, request.UseSafeArea ? safeArea : client));
                    }
                    foreach (GameUiLayoutGroup group in groups) Arrange(group, requests, placements, safeArea);
                    // All geometry is valid before any ApplyLayout callback. Rendering remains owned by the scene.
                    foreach (IGameUiLayoutParticipant participant in participants) participant.ApplyLayout(placements[participant.Id]);
                    foreach (GameUiLayoutGroup group in groups) byId[group.ServiceId].BringToFront();
                    host.RestoreOverlayOrder();
                    host.RefreshChatInputBounds();
                    LastPlacements = new ReadOnlyDictionary<string, GameUiLayoutPlacement>(placements);
                }
                return true;
            }
            finally { applying = false; }
        }

        private static bool Positive(Rectangle value) => value.Width > 0 && value.Height > 0;
        private static bool Contains(Rectangle outer, Rectangle inner) => Positive(inner) && inner.X >= outer.X && inner.Y >= outer.Y &&
            inner.X + (long)inner.Width <= outer.X + (long)outer.Width && inner.Y + (long)inner.Height <= outer.Y + (long)outer.Height;
        private static int Clamp(long value, int low, int high) => (int)Math.Max(low, Math.Min((long)high, value));
        private static Size Viewport(GameUiLayoutRequest request, Size desired, Size available)
        {
            if (available.Width < request.MinimumViewportSize.Width || available.Height < request.MinimumViewportSize.Height)
                throw new InvalidOperationException("Minimum viewport does not fit; verified alternate layout is required.");
            int width = Math.Min(desired.Width, available.Width), height = Math.Min(desired.Height, available.Height);
            if ((!request.HorizontalScroll && width < request.ContentSize.Width) ||
                (!request.VerticalScroll && height < request.ContentSize.Height))
                throw new InvalidOperationException("Content would be unreachable without an implemented scrolling viewport.");
            return new Size(width, height);
        }
        private static GameUiLayoutPlacement Place(GameUiLayoutRequest request, Rectangle client, Rectangle area)
        {
            bool restored = request.SavedBounds.HasValue && Contains(client, request.SavedBounds.Value);
            Rectangle saved = request.SavedBounds.GetValueOrDefault();
            if (restored && request.AllowResize &&
                (saved.Width < request.MinimumViewportSize.Width || saved.Height < request.MinimumViewportSize.Height ||
                 (!request.HorizontalScroll && saved.Width < request.ContentSize.Width) ||
                 (!request.VerticalScroll && saved.Height < request.ContentSize.Height))) restored = false;
            Size desired = restored && request.AllowResize ? saved.Size : request.ContentSize;
            Size viewport = Viewport(request, desired, area.Size);
            long x, y;
            if (restored) { x = saved.X; y = saved.Y; }
            else
            {
                int anchor = (int)request.Anchor;
                int column = anchor % 3, row = anchor / 3;
                x = area.X + (column == 0 ? 0L : column == 1 ? (area.Width - viewport.Width) / 2L : area.Width - viewport.Width) + request.Offset.X;
                y = area.Y + (row == 0 ? 0L : row == 1 ? (area.Height - viewport.Height) / 2L : area.Height - viewport.Height) + request.Offset.Y;
            }
            var bounds = new Rectangle(Clamp(x, area.Left, area.Right - viewport.Width), Clamp(y, area.Top, area.Bottom - viewport.Height), viewport.Width, viewport.Height);
            return new GameUiLayoutPlacement(bounds, request.ContentSize, restored);
        }
        private static void Arrange(GameUiLayoutGroup group, Dictionary<string, GameUiLayoutRequest> requests,
            Dictionary<string, GameUiLayoutPlacement> placements, Rectangle safeArea)
        {
            GameUiLayoutRequest primaryRequest = requests[group.PrimaryId], serviceRequest = requests[group.ServiceId];
            if (!primaryRequest.UseSafeArea || !serviceRequest.UseSafeArea)
                throw new InvalidOperationException("Window groups must use the shared safe area.");
            GameUiLayoutPlacement primary = placements[group.PrimaryId], service = placements[group.ServiceId];
            Rectangle a = primary.Bounds, b = service.Bounds;
            if (group.PrimaryHeaderHeight > a.Height) throw new InvalidOperationException("Primary header exceeds its viewport.");
            // Keep deliberate, reachable saved placement; default group placement is recomputed below.
            if (primary.Restored && service.Restored && !a.IntersectsWith(b)) return;
            long vertical = a.Height + (long)group.Gap + b.Height;
            long horizontal = a.Width + (long)group.Gap + b.Width;
            if (vertical <= safeArea.Height)
            {
                a.Y = Clamp(a.Y, safeArea.Top, (int)(safeArea.Bottom - vertical));
                b.X = Clamp(a.X, safeArea.Left, safeArea.Right - b.Width);
                b.Y = a.Bottom + group.Gap;
            }
            else if (horizontal <= safeArea.Width)
            {
                a.X = Clamp(a.X, safeArea.Left, (int)(safeArea.Right - horizontal));
                b.X = a.Right + group.Gap;
                b.Y = Clamp(a.Y, safeArea.Top, safeArea.Bottom - b.Height);
            }
            else
            {
                long reserved = group.PrimaryHeaderHeight + (long)group.Gap;
                if (reserved >= safeArea.Height) throw new InvalidOperationException("No reachable service viewport below the primary header.");
                Size viewport = Viewport(serviceRequest, b.Size, new Size(safeArea.Width, (int)(safeArea.Height - reserved)));
                a.Y = Clamp(a.Y, safeArea.Top, Math.Min(safeArea.Bottom - a.Height, safeArea.Bottom - (int)reserved - viewport.Height));
                b = new Rectangle(Clamp(a.X, safeArea.Left, safeArea.Right - viewport.Width), a.Y + (int)reserved, viewport.Width, viewport.Height);
            }
            if (!Contains(safeArea, a) || !Contains(safeArea, b)) throw new InvalidOperationException("Invalid grouped viewport.");
            placements[group.PrimaryId] = new GameUiLayoutPlacement(a, primary.ContentSize, primary.Restored);
            placements[group.ServiceId] = new GameUiLayoutPlacement(b, service.ContentSize, service.Restored);
        }
    }
}
