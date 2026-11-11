using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using Client.Envir;

internal static class OfficialUiCoordinatorTests
{
    private static int checks;
    private static readonly Size[] Resolutions = {
        new Size(1024, 768), new Size(1366, 768), new Size(1280, 800),
        new Size(1440, 900), new Size(1600, 900), new Size(1920, 1080)
    };
    private static void Check(bool value, string name)
    {
        checks++;
        if (!value) throw new Exception(name);
    }
    private static void Reject<T>(Action action, string name) where T : Exception
    {
        try { action(); } catch (T) { Check(true, name); return; }
        throw new Exception("Expected " + typeof(T).Name + ": " + name);
    }
    private static GameUiLayoutRequest Request(int width = 200, int height = 120,
        GameUiAnchor anchor = GameUiAnchor.TopLeft, Point? offset = null, bool safe = true,
        bool resize = false, bool scrollX = false, bool scrollY = false,
        Rectangle? saved = null, Size? minimum = null)
    {
        return new GameUiLayoutRequest(new Size(width, height), minimum ?? new Size(50, 30),
            anchor, offset ?? Point.Empty, safe, resize, scrollX, scrollY, saved);
    }
    private sealed class Scope : IDisposable
    {
        private readonly Host host;
        public Scope(Host host) { this.host = host; host.Depth++; }
        public void Dispose() { host.Depth--; host.Disposals++; host.Events.Add("release"); }
    }
    private sealed class Host : IGameUiLayoutHost
    {
        public readonly List<Participant> Items = new List<Participant>();
        public readonly List<GameUiLayoutGroup> Groups = new List<GameUiLayoutGroup>();
        public readonly List<string> Events = new List<string>();
        public int Depth, Restores, Refreshes, Disposals;
        public Action Restoring, Refreshing;
        public bool NullScope, NullParticipants, NullGroups;
        public void Suppressed() { Check(Depth == 1, "Mutations run under exactly one suppression scope"); }
        public IDisposable SuppressLayoutWrites() { Events.Add("suppress"); return NullScope ? null : new Scope(this); }
        public void EnsureChatRestored() { Suppressed(); Events.Add("restore"); Restores++; Restoring?.Invoke(); }
        public IEnumerable<IGameUiLayoutParticipant> GetParticipants() { Suppressed(); return NullParticipants ? null : Items; }
        public IEnumerable<GameUiLayoutGroup> GetGroups() { Suppressed(); return NullGroups ? null : Groups; }
        public void RestoreOverlayOrder() { Suppressed(); Events.Add("overlays"); }
        public void RefreshChatInputBounds() { Suppressed(); Events.Add("input"); Refreshes++; Refreshing?.Invoke(); }
        public Participant Add(string id, GameUiLayoutRequest request)
        {
            var participant = new Participant(this, id, request); Items.Add(participant); return participant;
        }
    }
    private sealed class Participant : IGameUiLayoutParticipant
    {
        private readonly Host host;
        public string Id { get; }
        public GameUiLayoutRequest Request;
        public GameUiLayoutPlacement Placement;
        public int Applies;
        public Action Skinning, Applying;
        public Participant(Host host, string id, GameUiLayoutRequest request) { this.host = host; Id = id; Request = request; }
        public void ApplySkin() { host.Suppressed(); host.Events.Add("skin:" + Id); Skinning?.Invoke(); }
        public GameUiLayoutRequest DescribeLayout(Size resolution) { host.Suppressed(); host.Events.Add("describe:" + Id); return Request; }
        public void ApplyLayout(GameUiLayoutPlacement placement)
        {
            host.Suppressed(); host.Events.Add("apply:" + Id); Applies++; Placement = placement; Applying?.Invoke();
        }
        public void BringToFront() { host.Suppressed(); host.Events.Add("front:" + Id); }
    }
    private static Rectangle Area(Size resolution) { return new Rectangle(17, 29, resolution.Width - 40, resolution.Height - 150); }
    private static bool Within(Rectangle area, Rectangle bounds)
    {
        return bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= area.Left && bounds.Top >= area.Top &&
            bounds.X + (long)bounds.Width <= area.Right && bounds.Y + (long)bounds.Height <= area.Bottom;
    }
    private static void AnchorsAndSavedLayouts()
    {
        foreach (Size resolution in Resolutions)
        {
            Rectangle safe = Area(resolution);
            foreach (GameUiAnchor anchor in Enum.GetValues(typeof(GameUiAnchor)))
            {
                var host = new Host(); var item = host.Add("window", Request(anchor: anchor, offset: new Point(3, 5)));
                var coordinator = new GameUiLayoutCoordinator(host);
                Check(coordinator.Apply(resolution, safe), "Initial pass applied");
                int col = (int)anchor % 3, row = (int)anchor / 3;
                int x = safe.X + (col == 0 ? 0 : col == 1 ? (safe.Width - 200) / 2 : safe.Width - 200) + 3;
                int y = safe.Y + (row == 0 ? 0 : row == 1 ? (safe.Height - 120) / 2 : safe.Height - 120) + 5;
                x = Math.Min(safe.Right - 200, x); y = Math.Min(safe.Bottom - 120, y);
                Check(item.Placement.Bounds == new Rectangle(x, y, 200, 120), "Exact anchor placement " + anchor);
                Check(!item.Placement.Restored && !item.Placement.RequiresVerticalScroll, "Default geometry flags");
                Check(host.Depth == 0 && host.Refreshes == 1 && host.Disposals == 1, "Scope disposed and input refreshed once");
                var mutable = (IDictionary<string, GameUiLayoutPlacement>)coordinator.LastPlacements;
                Reject<NotSupportedException>(() => mutable.Add("other", item.Placement), "Read-only published geometry");
            }
            var savedHost = new Host();
            var hud = savedHost.Add("hud", Request(anchor: GameUiAnchor.BottomRight, safe: false));
            var fixedWindow = savedHost.Add("fixed", Request(saved: new Rectangle(50, 50, 9, 8)));
            var resized = savedHost.Add("resized", Request(resize: true, scrollX: true, scrollY: true, saved: new Rectangle(55, 60, 100, 80)));
            var invalid = savedHost.Add("invalid", Request(saved: new Rectangle(int.MaxValue, 0, 200, 120)));
            var tooSmall = savedHost.Add("small", Request(resize: true, scrollX: true, scrollY: true, saved: new Rectangle(60, 70, 49, 80)));
            var noScroll = savedHost.Add("noscroll", Request(resize: true, saved: new Rectangle(60, 70, 100, 80)));
            var clamped = savedHost.Add("clamp", Request(saved: new Rectangle(resolution.Width - 10, resolution.Height - 10, 5, 5)));
            new GameUiLayoutCoordinator(savedHost).Apply(resolution, safe);
            Check(hud.Placement.Bounds.Right == resolution.Width && hud.Placement.Bounds.Bottom == resolution.Height, "HUD anchors to client");
            Check(fixedWindow.Placement.Bounds == new Rectangle(50, 50, 200, 120) && fixedWindow.Placement.Restored, "Fixed saved size ignored");
            Check(resized.Placement.Bounds == new Rectangle(55, 60, 100, 80) && resized.Placement.Restored, "Resizable saved viewport restored");
            Check(resized.Placement.ContentSize == new Size(200, 120) && resized.Placement.RequiresHorizontalScroll && resized.Placement.RequiresVerticalScroll, "Content preserved for scroll");
            Check(!invalid.Placement.Restored && !tooSmall.Placement.Restored && !noScroll.Placement.Restored, "Invalid saved geometry falls back");
            Check(clamped.Placement.Bounds.Right == safe.Right && clamped.Placement.Bounds.Bottom == safe.Bottom, "Saved location clamped after fixed size");
        }
    }
    private static void Groups()
    {
        Size resolution = Resolutions[0]; Rectangle safe = Area(resolution);
        var verticalHost = new Host();
        var a = verticalHost.Add("a", Request(350, 180, GameUiAnchor.BottomRight));
        var b = verticalHost.Add("b", Request(300, 180));
        verticalHost.Groups.Add(new GameUiLayoutGroup("a", "b", 7, 24));
        new GameUiLayoutCoordinator(verticalHost).Apply(resolution, safe);
        Check(b.Placement.Bounds.Top == a.Placement.Bounds.Bottom + 7, "Vertical group");
        Check(Within(safe, a.Placement.Bounds) && Within(safe, b.Placement.Bounds), "Vertical containment");
        Check(verticalHost.Events.IndexOf("skin:b") < verticalHost.Events.IndexOf("describe:a"), "All skins before geometry");
        Check(verticalHost.Events.IndexOf("describe:b") < verticalHost.Events.IndexOf("apply:a"), "All geometry before application");
        Check(verticalHost.Events.IndexOf("front:b") < verticalHost.Events.IndexOf("overlays") &&
            verticalHost.Events.IndexOf("overlays") < verticalHost.Events.IndexOf("input"), "Group front, overlays, then input");
        Check(verticalHost.Events.First() == "suppress" && verticalHost.Events[1] == "restore" && verticalHost.Events.Last() == "release", "Restore under suppression before skin");

        var horizontalHost = new Host();
        a = horizontalHost.Add("a", Request(350, 400)); b = horizontalHost.Add("b", Request(300, 380));
        horizontalHost.Groups.Add(new GameUiLayoutGroup("a", "b", 7, 24));
        new GameUiLayoutCoordinator(horizontalHost).Apply(resolution, safe);
        Check(b.Placement.Bounds.Left == a.Placement.Bounds.Right + 7 && b.Placement.Bounds.Top == a.Placement.Bounds.Top, "Horizontal group");

        var stackedHost = new Host();
        a = stackedHost.Add("a", Request(700, 570, GameUiAnchor.BottomRight));
        b = stackedHost.Add("b", Request(650, 580, scrollY: true));
        stackedHost.Groups.Add(new GameUiLayoutGroup("a", "b", 7, 40));
        new GameUiLayoutCoordinator(stackedHost).Apply(resolution, safe);
        Check(b.Placement.Bounds.Top == a.Placement.Bounds.Top + 47 && a.Placement.Bounds.IntersectsWith(b.Placement.Bounds), "Stack keeps primary header accessible");
        Check(Within(safe, a.Placement.Bounds) && Within(safe, b.Placement.Bounds), "Bottom-anchored stacked group contained");
        Check(b.Placement.ContentSize.Height == 580 && b.Placement.RequiresVerticalScroll && b.Placement.Bounds.Height == safe.Height - 47, "Stack scroll viewport retains full content");

        var failing = new Host(); failing.Add("a", Request(700, 570)); failing.Add("b", Request(650, 580));
        failing.Groups.Add(new GameUiLayoutGroup("a", "b", 7, 40));
        var failedCoordinator = new GameUiLayoutCoordinator(failing);
        Reject<InvalidOperationException>(() => failedCoordinator.Apply(resolution, safe), "No silent inaccessible stacked content");
        Check(failing.Items.All(p => p.Applies == 0) && failing.Depth == 0 && failedCoordinator.LastPlacements == null, "Geometry failure before application");
        failing.Items[1].Request = Request(650, 580, scrollY: true);
        Check(failedCoordinator.Apply(resolution, safe), "Guard released after geometry failure");

        var saved = new Host(); a = saved.Add("a", Request(saved: new Rectangle(40, 50, 200, 120)));
        b = saved.Add("b", Request(saved: new Rectangle(500, 300, 200, 120)));
        saved.Groups.Add(new GameUiLayoutGroup("a", "b", 7, 24));
        new GameUiLayoutCoordinator(saved).Apply(resolution, safe);
        Check(a.Placement.Bounds.Location == new Point(40, 50) && b.Placement.Bounds.Location == new Point(500, 300), "Reachable separate saved placements preserved");
    }
    private static void Lifecycle()
    {
        var host = new Host(); var item = host.Add("chat", Request());
        var coordinator = new GameUiLayoutCoordinator(host); int reentries = 0;
        Action reenter = () => { Check(!coordinator.Apply(Resolutions[0], Area(Resolutions[0])), "Synchronous reentry rejected"); reentries++; };
        item.Skinning = reenter; item.Applying = reenter; host.Refreshing = reenter;
        for (int i = 0; i < 20; i++) Check(coordinator.Apply(Resolutions[i % 6], Area(Resolutions[i % 6])), "Repeated layout");
        Check(host.Restores == 1 && host.Refreshes == 20 && item.Applies == 20 && reentries == 60 && host.Disposals == 20 && host.Depth == 0, "No repeated chat restore or nested scope");
        host.Refreshing = null; item.Skinning = () => { throw new ApplicationException("skin failure"); };
        var lastSuccessful = coordinator.LastPlacements;
        Reject<ApplicationException>(() => coordinator.Apply(Resolutions[0], Area(Resolutions[0])), "Host failure surfaced");
        Check(host.Depth == 0 && ReferenceEquals(lastSuccessful, coordinator.LastPlacements), "Failure retains last successful result and releases scope");
        item.Skinning = null;
        Check(coordinator.Apply(Resolutions[0], Area(Resolutions[0])), "Can retry after host failure");
        Exception crossThread = null;
        var thread = new Thread(() => { try { coordinator.Apply(Resolutions[0], Area(Resolutions[0])); } catch (Exception e) { crossThread = e; } });
        thread.Start(); thread.Join(); Check(crossThread is InvalidOperationException, "Owner thread required");
        var restoreHost = new Host(); restoreHost.Add("chat", Request());
        restoreHost.Restoring = () => { throw new ApplicationException("restore failure"); };
        var retryCoordinator = new GameUiLayoutCoordinator(restoreHost);
        Reject<ApplicationException>(() => retryCoordinator.Apply(Resolutions[0], Area(Resolutions[0])), "Chat restore failure");
        restoreHost.Restoring = null; retryCoordinator.Apply(Resolutions[0], Area(Resolutions[0]));
        Check(restoreHost.Restores == 2 && restoreHost.Depth == 0, "Failed chat restore retries");
    }
    private static void InvalidInputs()
    {
        Reject<ArgumentNullException>(() => new GameUiLayoutCoordinator(null), "Null host");
        Reject<ArgumentException>(() => Request(0, 100), "Empty content");
        Reject<ArgumentException>(() => Request(minimum: new Size(201, 30)), "Minimum exceeds content");
        Reject<ArgumentOutOfRangeException>(() => Request(anchor: (GameUiAnchor)10), "Invalid anchor");
        Reject<ArgumentException>(() => new GameUiLayoutGroup("a", "a", 0, 24), "Repeated group ID");
        Reject<ArgumentOutOfRangeException>(() => new GameUiLayoutGroup("a", "b", -1, 24), "Negative gap");
        var host = new Host(); host.Add("a", Request()); var coordinator = new GameUiLayoutCoordinator(host);
        Reject<ArgumentException>(() => coordinator.Apply(new Size(800, 600), new Rectangle(0, 0, 800, 600)), "Unsupported resolution");
        foreach (var rect in new[] { Rectangle.Empty, new Rectangle(-1, 0, 30, 30), new Rectangle(int.MaxValue, 0, 1, 1), new Rectangle(0, int.MaxValue, 1, 1), new Rectangle(0, 0, 1025, 768) })
            Reject<ArgumentException>(() => coordinator.Apply(Resolutions[0], rect), "Invalid or overflowing safe area");
        Check(host.Disposals == 0, "Input validation before mutation");
        Action<Host, string> invalidHost = (h, reason) => {
            var c = new GameUiLayoutCoordinator(h);
            Reject<InvalidOperationException>(() => c.Apply(Resolutions[0], Area(Resolutions[0])), reason);
            Check(h.Depth == 0 && h.Items.Where(p => p != null).All(p => p.Applies == 0), "Invalid request before layout callbacks");
        };
        var duplicate = new Host(); duplicate.Add("a", Request()); duplicate.Add("a", Request()); invalidHost(duplicate, "Duplicate participant ID");
        var nullItem = new Host(); nullItem.Items.Add(null); invalidHost(nullItem, "Null participant");
        var missing = new Host(); missing.Add("a", Request()); missing.Groups.Add(new GameUiLayoutGroup("a", "missing", 0, 24)); invalidHost(missing, "Missing group member");
        var overlap = new Host(); overlap.Add("a", Request()); overlap.Add("b", Request()); overlap.Add("c", Request());
        overlap.Groups.Add(new GameUiLayoutGroup("a", "b", 0, 24)); overlap.Groups.Add(new GameUiLayoutGroup("a", "c", 0, 24)); invalidHost(overlap, "Participant in two groups");
        var noRequest = new Host(); noRequest.Add("a", null); invalidHost(noRequest, "Null request");
        invalidHost(new Host { NullScope = true }, "Missing suppression scope");
        invalidHost(new Host { NullParticipants = true }, "Missing participants");
        invalidHost(new Host { NullGroups = true }, "Missing groups");
        var oversized = new Host(); oversized.Add("a", Request(1500, 900)); invalidHost(oversized, "No unsupported shrink");
        var minimum = new Host(); minimum.Add("a", Request(1500, 900, scrollX: true, scrollY: true, minimum: new Size(1200, 700))); invalidHost(minimum, "Minimum cannot fit");
        var badGroup = new Host(); badGroup.Add("a", Request(safe: false)); badGroup.Add("b", Request()); badGroup.Groups.Add(new GameUiLayoutGroup("a", "b", 0, 24)); invalidHost(badGroup, "Groups require safe area");
        var badHeader = new Host(); badHeader.Add("a", Request()); badHeader.Add("b", Request()); badHeader.Groups.Add(new GameUiLayoutGroup("a", "b", 0, 121)); invalidHost(badHeader, "Header exceeds main viewport");
    }
    private static void RandomBounds()
    {
        var random = new Random(72179);
        foreach (Size resolution in Resolutions)
        {
            Rectangle safe = Area(resolution);
            for (int i = 0; i < 400; i++)
            {
                int width = random.Next(50, safe.Width + 1), height = random.Next(30, safe.Height + 1);
                int x = i % 3 == 0 ? int.MinValue : i % 3 == 1 ? int.MaxValue : random.Next(-2000, 2000);
                int y = i % 5 == 0 ? int.MinValue : i % 5 == 1 ? int.MaxValue : random.Next(-2000, 2000);
                var host = new Host(); var item = host.Add("fixed", Request(width, height, (GameUiAnchor)random.Next(9), new Point(x, y)));
                new GameUiLayoutCoordinator(host).Apply(resolution, safe);
                Check(Within(safe, item.Placement.Bounds), "Random anchor containment");
                Check(item.Placement.Bounds.Size == new Size(width, height), "No fixed content scaling");
            }
        }
    }
    private static int Main()
    {
        try {
            AnchorsAndSavedLayouts(); Groups(); Lifecycle(); InvalidInputs(); RandomBounds();
            Console.WriteLine("Official UI layout coordinator passed: " + checks + " checks; 6 resolutions, 9 anchors, 2400 randomized placements.");
            return 0;
        } catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
