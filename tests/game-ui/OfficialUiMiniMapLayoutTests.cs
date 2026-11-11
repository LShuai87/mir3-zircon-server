using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;
using Client;
using Client.Controls;
using Client.Envir;
using Client.Models;
using Client.Scenes;
using Client.Scenes.Views;
using Client.UserModels;
using Library;
using Library.SystemModels;
using MirDB;
using UserObject = Client.Models.UserObject;

internal static partial class OfficialUiMiniMapLayoutTests
{
    private static int checks;
    private static void Check(bool value, string detail) { checks++; if (!value) throw new Exception(detail); }
    private static void Reject<T>(Action action, string detail) where T : Exception
    { bool rejected = false; try { action(); } catch (T) { rejected = true; } Check(rejected, detail); }
    private static FieldInfo Field(Type type, string name)
    { for (; type != null; type = type.BaseType) { var f = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public); if (f != null) return f; } throw new Exception(name); }
    private static void Set(object o, string name, object value) => Field(o.GetType(), name).SetValue(o, value);
    private static T Get<T>(object o, string name) => (T)Field(o.GetType(), name).GetValue(o);
    private static List<DXControl> Children(DXControl c) => (List<DXControl>)typeof(DXControl).GetProperty("Controls", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(c);
    private static Rectangle Local(DXControl c) => new Rectangle(c.Location, c.Size);
    private sealed class TestScene : DXScene { internal TestScene() : base(new Size(1024, 768)) { } }
    private static GameUiWindowSkin Skin(MirLibrary library, int index = 0) => new GameUiWindowSkin(library, index,
        new Padding(11, 21, 5, 25), Padding.Empty, new Padding(4, 2, 0, 5), new Size(166, 198), true,
        GameUiMiniMapLayout.OfficialV1.Title, new Rectangle(4, 2, 140, 16), new Rectangle(146, 2, 20, 16));
    private static void Geometry(string fixture)
    {
        var l = GameUiMiniMapLayout.OfficialV1; var lines = File.ReadAllLines(fixture);
        Check(lines.Length == 9, "Fixture shape changed");
        Check(lines[0] == l.WindowSize.Width + "," + l.WindowSize.Height, "Compiled window differs from JSON");
        var areas = new[] { l.Viewport, l.Coordinates, l.Collapse, l.Safety, l.Title, l.BigMap, l.Transparency, l.Zoom };
        for (int i = 0; i < areas.Length; i++) Check(lines[i + 1] == string.Join(",", new[] { areas[i].X, areas[i].Y, areas[i].Width, areas[i].Height }), "Compiled region differs from JSON");
        Check(l.CollapsedSize == new Size(166, 46) && l.CollapsedCoordinates == new Rectangle(44, 25, 74, 16), "Collapsed geometry differs");
        Check(l.Safety == new Rectangle(4, 2, 28, 16) && l.Title == new Rectangle(34, 2, 110, 16), "Header geometry differs");
        Reject<ArgumentOutOfRangeException>(() => new GameUiMiniMapLayout(Size.Empty, l.Viewport, l.Coordinates, l.Collapse), "Empty window accepted");
        Reject<ArgumentException>(() => new GameUiMiniMapLayout(l.WindowSize, Rectangle.Empty, l.Coordinates, l.Collapse), "Empty viewport accepted");
        Reject<ArgumentException>(() => new GameUiMiniMapLayout(l.WindowSize, new Rectangle(-1, 0, 10, 10), l.Coordinates, l.Collapse), "Negative region accepted");
        Reject<ArgumentException>(() => new GameUiMiniMapLayout(l.WindowSize, l.Viewport, l.Viewport, l.Collapse), "Overlapping regions accepted");
        Reject<ArgumentException>(() => new GameUiMiniMapLayout(new Size(int.MaxValue, 198), new Rectangle(int.MaxValue, 0, 2, 2), l.Coordinates, l.Collapse), "Overflow accepted");
    }
    // Exercise the real minimap subscriber without loading maps, lights or audio.
    private static void ChangeMap(MapControl map, MapInfo info)
    {
        Set(map, "_MapInfo", info);
        var handlers = Get<EventHandler<EventArgs>>(map, "MapInfoChanged");
        if (handlers != null) handlers(map, EventArgs.Empty);
    }
    private static void Pan(DXImageControl image, Point value)
    {
        image.Location = value;
        Get<EventHandler<MouseEventArgs>>(image, "Moving")(image, new MouseEventArgs(MouseButtons.Left, 0, 0, 0, 0));
    }
    private static void ClickClose(TestScene root, MiniMapDialog window)
    {
        var bounds = window.CloseButton.DisplayArea;
        var click = new MouseEventArgs(MouseButtons.Left, 1, bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2, 0);
        root.OnMouseMove(click);
        Check(DXControl.MouseControl == window.CloseButton, "Header/plus decoration blocked collapse button input");
        DXControl.MouseControl.OnMouseClick(click);
    }
    private static void Adapt(TestScene root, GameScene scene, UserObject user, MapControl map, MirLibrary library)
    {
        var l = GameUiMiniMapLayout.OfficialV1;
        for (int skinCase = 0; skinCase < 2; skinCase++)
        using (var d = new MiniMapDialog { Parent = root, Visible = true })
        {
            scene.MiniMapBox = d;
            ChangeMap(map, new MapInfo());
            if (skinCase == 1) d.WindowSkin = Skin(library, 1);
            var panel = d.Panel; var image = Get<DXImageControl>(d, "Image"); var close = d.CloseButton;
            var click = Get<Delegate>(close, "MouseClick"); var moving = Get<Delegate>(image, "Moving");
            var oldSkin = d.WindowSkin; var size = d.Size; var area = d.Area; var bounds = Local(panel); var closeBounds = Local(close);
            var closeVisible = close.Visible; var closeFixed = close.FixedSize; var panelDraw = panel.DrawTexture; var panelColour = panel.BackColour; var children = Children(d).ToArray();
            MiniMapDialog.ScaleX = .5F; MiniMapDialog.ScaleY = .25F;
            var data = new ClientObjectData { ObjectID = user.ObjectID, MapIndex = map.MapInfo.Index, Name = "Player", Location = new Point(240, 360) };
            scene.DataDictionary[user.ObjectID] = data; d.Update(data);
            var marker = d.MapInfoObjects[data]; var markerLocation = marker.Location; var imageLocation = image.Location;
            int extraClick = 0; EventHandler<MouseEventArgs> extra = (s, e) => extraClick++; close.MouseClick += extra;
            for (int cycle = 0; cycle < 4; cycle++)
            {
                d.UseGameUiMiniMap(l); d.WindowSkin = Skin(library);
                var label = Get<DXLabel>(d, "gameUiCoordinates");
                Check(panel.DrawTexture && panel.BackColour == Color.Black, "Map viewport lost its black background");
                Check(d.Size == l.WindowSize && d.Area == l.Viewport && Local(panel) == l.Viewport && !d.AllowResize, "Viewport or fixed size differs");
                Check(Local(close) == l.Collapse && close.Visible && !close.FixedSize, "Original collapse button was not arranged");
                Check(!label.IsControl && Local(label) == l.Coordinates && !label.AutoSize, "Coordinate label interferes with input");
                Check(d.GetAcceptableResize(new Size(500, 500)) == l.WindowSize, "Fixed resize was not enforced");
                Check(d.Panel == panel && Get<DXImageControl>(d, "Image") == image && marker == d.MapInfoObjects[data] && marker.Parent == image, "Original controls were replaced");
                Check(Get<Delegate>(image, "Moving") == moving && Get<Delegate>(close, "MouseClick").GetInvocationList()[0].Equals(click.GetInvocationList()[0]), "Native handlers were replaced");
                Check(MiniMapDialog.ScaleX == .5F && MiniMapDialog.ScaleY == .25F && marker.Location == markerLocation, "Viewport changed map scaling or marker coordinates");
                Check(image.Location == new Point(-marker.Location.X - marker.Size.Width / 2 + l.Viewport.Width / 2, -marker.Location.Y - marker.Size.Height / 2 + l.Viewport.Height / 2), "Follow center differs");
                foreach (Point location in new[] { Point.Empty, new Point(858, 0), new Point(858, 570), new Point(200, 125) })
                {
                    d.Location = location;
                    Check(panel.DisplayArea == new Rectangle(location.X + l.Viewport.X, location.Y + l.Viewport.Y, 150, 152), "Viewport does not follow window");
                    foreach (Point offset in new[] { Point.Empty, new Point(149, 151), new Point(75, 76) })
                    {
                        var screen = new Point(panel.DisplayArea.X + offset.X, panel.DisplayArea.Y + offset.Y);
                        root.OnMouseMove(new MouseEventArgs(MouseButtons.None, 0, screen.X, screen.Y, 0));
                        Check(DXControl.MouseControl == image || DXControl.MouseControl == marker || DXControl.MouseControl == panel, "Viewport hit did not reach original map controls");
                        Check(new Point(screen.X - image.DisplayArea.X, screen.Y - image.DisplayArea.Y) == new Point(offset.X - image.Location.X, offset.Y - image.Location.Y), "Screen-to-image coordinates differ");
                    }
                    var outside = new Point(d.DisplayArea.X, d.DisplayArea.Y + 190);
                    Check(!d.IsMouseOver(outside), "Transparent decoration blocks map input");
                    Check(!panel.IsMouseOver(new Point(panel.DisplayArea.Right, panel.DisplayArea.Bottom - 1)), "Viewport right edge includes outside point");
                    Check(!panel.IsMouseOver(new Point(panel.DisplayArea.Left, panel.DisplayArea.Bottom)), "Viewport bottom edge includes outside point");
                }
                d.Opacity = .5F; Check(label.Opacity == .5F && image.Opacity == .5F && image.ImageOpacity == .5F && marker.Opacity == .5F, "Opacity propagation differs"); d.Opacity = 1F;
                Set(user, "_CurrentLocation", new Point(123, 456)); d.Process(); Check(label.Text == "123:456", "Coordinates did not follow live user");
                Pan(image, new Point(30, 40)); Check(image.Location == Point.Empty, "Positive pan was not clamped");
                Pan(image, new Point(-1000, -1000)); Check(image.Location == new Point(panel.Size.Width - image.Size.Width, panel.Size.Height - image.Size.Height), "Negative pan was not clamped");
                d.UpdateMapPosition();
                var safety = Get<DXLabel>(d, "gameUiSafety");
                user.InSafeZone = true;
                Check(safety.Text == "安全" && safety.ForeColour == Color.Lime && Local(safety) == l.Safety, "Safe-zone event did not update header");
                Check(Local(d.TitleLabel) == l.Title && !d.TitleLabel.AutoSize && !safety.IsControl, "Header bounds/input differ");
                ClickClose(root, d);
                Check(d.Visible && d.GameUiMiniMapCollapsed && extraClick == cycle * 2 + 1, "Collapse hid the entire window or lost subscribers");
                Check(d.Size == l.CollapsedSize && !panel.Visible && Local(label) == l.CollapsedCoordinates, "Collapse did not remove viewport height");
                Check(!d.CustomSize && d.GetAcceptableResize(new Size(500, 500)) == l.CollapsedSize, "Collapsed fixed sizing differs");
                Check(Get<DXControl>(d, "gameUiExpandStroke").Visible && d.TitleLabel.Visible && close.Visible, "Collapsed header lost controls");
                var slices = d.WindowSkin.GetSlices(d.Size);
                Check(slices.Length == 2, "Collapsed skin still contains a centre slice");
                Check(slices[0].Source == new Rectangle(0, 0, 166, 21) && slices[0].Destination == new Rectangle(0, 0, 166, 21) &&
                    slices[1].Source == new Rectangle(0, 173, 166, 25) && slices[1].Destination == new Rectangle(0, 21, 166, 25), "Collapsed skin does not preserve original header/footer pixels");
                Check(d.MapInfoObjects[data] == marker && marker.Parent == image && !marker.IsDisposed, "Collapse rebuilt native markers");
                Set(user, "_CurrentLocation", new Point(124, 457)); d.Process();
                Check(label.Text == "124:457", "Collapsed coordinates stopped updating");
                user.InSafeZone = false;
                Check(safety.Text == "危险" && safety.ForeColour == Color.Red, "Collapsed safety state stopped updating");
                var below = new Point(d.DisplayArea.X + 80, d.DisplayArea.Y + l.CollapsedSize.Height + 10);
                root.OnMouseMove(new MouseEventArgs(MouseButtons.None, 0, below.X, below.Y, 0));
                Check(!d.IsMouseOver(below) && DXControl.MouseControl != image && DXControl.MouseControl != panel, "Collapsed map retained viewport input");
                d.Size = new Size(500, 500); Check(d.Size == l.CollapsedSize, "External resize expanded collapsed window");
                d.WindowSkin = Skin(library, 1);
                Check(d.Size == l.CollapsedSize && d.GameUiMiniMapCollapsed && !panel.Visible, "Skin rebind expanded collapsed window");
                ClickClose(root, d);
                Check(d.Visible && !d.GameUiMiniMapCollapsed && extraClick == cycle * 2 + 2 && panel.Visible && d.Size == l.WindowSize, "Expand did not restore original viewport");
                Check(Local(label) == l.Coordinates && !Get<DXControl>(d, "gameUiExpandStroke").Visible && d.MapInfoObjects[data] == marker, "Expand lost footer or native markers");
                var alternate = new GameUiMiniMapLayout(l.WindowSize, new Rectangle(10, 22, 150, 152), l.Coordinates, l.Collapse);
                d.UseGameUiMiniMap(alternate); Check(Local(panel) == alternate.Viewport && d.MapInfoObjects[data] == marker, "Replacing layout rebuilt map objects");
                d.UseGameUiMiniMap(l); d.OnClientAreaChanged(d.ClientArea, d.ClientArea); Check(Local(panel) == l.Viewport, "Client-area callback restored legacy viewport");
                image.IsMoving = true; Reject<InvalidOperationException>(() => d.UseGameUiMiniMap(l), "Moving image accepted"); image.IsMoving = false;
                image.AllowDragOut = true; Reject<InvalidOperationException>(() => d.UseGameUiMiniMap(l), "Unclipped image accepted"); image.AllowDragOut = false;
                Reject<ArgumentException>(() => d.UseGameUiMiniMap(new GameUiMiniMapLayout(new Size(170, 198), l.Viewport, l.Coordinates, l.Collapse)), "Incompatible skin accepted");
                d.UseGameUiMiniMap(null);
                Check(d.GameUiMiniMapLayout == null && d.WindowSkin == oldSkin && d.Size == size && d.Area == area && Local(panel) == bounds && d.AllowResize, "Disable did not restore native layout");
                Check(Local(close) == closeBounds && close.Visible == closeVisible && close.FixedSize == closeFixed && label.IsDisposed, "Disable did not restore close button or remove label");
                Check(panel.DrawTexture == panelDraw && panel.BackColour == panelColour, "Panel background was not restored");
                Check(Children(d).SequenceEqual(children) && image.Location == imageLocation && !marker.IsDisposed, "Disable changed controls or native follow center");
            }
            close.MouseClick -= extra;
            var map2 = new MapInfo(); Set(map2, "_Description", "Second map"); Set(map2, "_MiniMap", 1);
            d.UseGameUiMiniMap(l); d.WindowSkin = Skin(library); scene.DataDictionary.Clear();
            d.SetGameUiMiniMapCollapsed(true);
            ChangeMap(map, map2);
            Check(d.GameUiMiniMapCollapsed && !d.Panel.Visible && d.Size == l.CollapsedSize, "Map change expanded collapsed window");
            Check(marker.IsDisposed && d.MapInfoObjects.Count == 0 && image.Index == 1 && d.TitleLabel.Text == "Second map", "Map-change handler did not replace native map state");
            Check(Math.Abs(MiniMapDialog.ScaleX - 20F / map.Width) < 0.000001F && Math.Abs(MiniMapDialog.ScaleY - 30F / map.Height) < 0.000001F, "Map-change scale differs: actual=" + MiniMapDialog.ScaleX + "," + MiniMapDialog.ScaleY + " image=" + image.Size + " map=" + map.Width + "," + map.Height);
            Pan(image, new Point(-10, -20)); Check(image.Location == new Point(65, 61), "Small map centring differs");
            ChangeMap(map, null); Check(Get<DXLabel>(d, "gameUiCoordinates").Text == string.Empty, "No-map coordinates remained visible");
            ChangeMap(map, map2); Set(scene, "_User", null); d.Process(); Check(Get<DXLabel>(d, "gameUiCoordinates").Text == string.Empty, "No-user coordinates remained visible"); Set(scene, "_User", user);
            d.UseGameUiMiniMap(null);
            d.Visible = true; close.InvokeMouseClick(); Check(!d.Visible, "Legacy close stopped hiding after detach");
            scene.MiniMapBox = null;
            if (oldSkin == null) { d.Size = new Size(250, 260); var legacy = d.ClientArea; legacy.Inflate(6, 6); Check(d.Area == legacy && Local(panel) == legacy, "Legacy resize behavior changed"); }
        }
        using (var d = new MiniMapDialog { Parent = root })
        {
            var size = d.Size; var bounds = Local(d.Panel); bool fail = true;
            EventHandler<EventArgs> onSize = (s, e) => { if (fail) { fail = false; throw new InvalidOperationException("Injected size observer failure"); } };
            d.SizeChanged += onSize; Reject<InvalidOperationException>(() => d.UseGameUiMiniMap(l), "Initial observer failure not surfaced"); d.SizeChanged -= onSize;
            Check(d.GameUiMiniMapLayout == null && d.Size == size && Local(d.Panel) == bounds && d.AllowResize && Children(d).Count == 3, "Initial failure lost legacy state");
            d.UseGameUiMiniMap(l); bool failMove = true;
            EventHandler<EventArgs> onMove = (s, e) => { if (failMove) { failMove = false; throw new InvalidOperationException("Injected viewport observer failure"); } };
            d.Panel.LocationChanged += onMove;
            Reject<InvalidOperationException>(() => d.UseGameUiMiniMap(new GameUiMiniMapLayout(l.WindowSize, new Rectangle(10, 22, 150, 152), l.Coordinates, l.Collapse)), "Replacement observer failure not surfaced"); d.Panel.LocationChanged -= onMove;
            Check(d.GameUiMiniMapLayout == l && Local(d.Panel) == l.Viewport && !d.AllowResize, "Replacement failure lost active layout");
            d.WindowSkin = Skin(library); d.SetGameUiMiniMapCollapsed(true);
            failMove = true; d.Panel.LocationChanged += onMove;
            Reject<InvalidOperationException>(() => d.UseGameUiMiniMap(new GameUiMiniMapLayout(l.WindowSize, new Rectangle(10, 22, 150, 152), l.Coordinates, l.Collapse)), "Collapsed replacement failure not surfaced");
            d.Panel.LocationChanged -= onMove;
            Check(d.GameUiMiniMapLayout == l && d.GameUiMiniMapCollapsed && d.Size == l.CollapsedSize && !d.Panel.Visible && d.WindowSkin.SlicePadding.Top == l.Viewport.Top, "Collapsed replacement failure lost skin/layout");
            d.Location = new Point(858, 720); d.SetGameUiMiniMapCollapsed(false);
            Check(d.Location == new Point(858, 570), "Expansion did not clamp to parent bounds");
            bool failCollapse = true;
            EventHandler<EventArgs> onCollapseSize = (s, e) => { if (failCollapse) { failCollapse = false; throw new InvalidOperationException("Injected collapse size failure"); } };
            d.SizeChanged += onCollapseSize;
            Reject<InvalidOperationException>(() => d.SetGameUiMiniMapCollapsed(true), "Collapse observer failure not surfaced");
            d.SizeChanged -= onCollapseSize;
            Check(!d.GameUiMiniMapCollapsed && d.Panel.Visible && d.Size == l.WindowSize && d.WindowSkin.FixedSize, "Collapse failure lost expanded state");
            var panel = d.Panel; var image = Get<DXImageControl>(d, "Image"); var label = Get<DXLabel>(d, "gameUiCoordinates"); d.SetGameUiMiniMapCollapsed(true); d.Dispose();
            Check(panel.IsDisposed && image.IsDisposed && label.IsDisposed && d.GameUiMiniMapLayout == null, "Disposal left controls or binding alive");
            var listeners = Get<Delegate>(map, "MapInfoChanged"); Check(listeners == null || !listeners.GetInvocationList().Any(h => ReferenceEquals(h.Target, d)), "Disposed window retained map subscriber");
            Reject<ObjectDisposedException>(() => d.UseGameUiMiniMap(l), "Disposed window accepted binding");
        }
    }
    private sealed class TitleTestWindow : DXWindow
    {
        public override Client.UserModels.WindowType Type => Client.UserModels.WindowType.None;
        public override bool CustomSize => true;
        public override bool AutomaticVisiblity => false;
    }
    private static void WindowTitles(TestScene root, MirLibrary library)
    {
        foreach (bool autoSize in new[] { true, false })
        using (var window = new TitleTestWindow { Parent = root })
        {
            var label = window.TitleLabel;
            var font = label.Font;
            var nativeFormat = TextFormatFlags.Right | TextFormatFlags.NoPadding;
            label.DrawFormat = nativeFormat;
            label.AutoSize = autoSize;
            label.Text = "Native title";
            if (!autoSize) label.Size = new Size(93, 23);
            Size nativeSize = label.Size;
            for (int cycle = 0; cycle < 3; cycle++)
            {
                window.WindowSkin = Skin(library);
                foreach (string text in new[] { "非常长的地图名称用于确认标题不会越过右侧收起按钮和左侧窗口边界ABCDEFGHIJKLMNOPQRSTUVWXYZ", "短地图", "地图 & 标题\r\n第二行", "" })
                {
                    label.Text = text;
                    Rectangle expected = window.WindowSkin.GetTitleArea(window.Size);
                    Check(Local(label) == expected && !label.AutoSize, "Skinned title must remain inside the declared title rectangle: " + Local(label));
                    Check(label.Text == text && ReferenceEquals(label.Font, font), "Title binding changed native text/font identity");
                    var flags = TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                    Check(label.DrawFormat == flags && !label.IsControl, "Skinned title must ellipsize without taking input");
                    Check(!Local(label).IntersectsWith(window.WindowSkin.GetCloseArea(window.Size)), "Title overlaps close button");
                }
                string restoredText = cycle == 1 ? "地图已在换肤期间发生变化" : "";
                label.Text = restoredText;
                window.WindowSkin = Skin(library, 1);
                Check(Local(label) == window.WindowSkin.GetTitleArea(window.Size), "Skin replacement lost bounded title geometry");
                window.WindowSkin = null;
                Check(label.AutoSize == autoSize && label.DrawFormat == nativeFormat && label.Text == restoredText && ReferenceEquals(label.Font, font), "Legacy title presentation was not restored");
                Check(label.Size == (autoSize ? DXLabel.GetSize(restoredText, font, label.Outline) : nativeSize), "Legacy fixed/automatic title size was not restored");
                label.Text = "Current native title after map change";
                Check(label.Size == (autoSize ? DXLabel.GetSize(label.Text, font, label.Outline) : nativeSize), "Legacy text updates use stale title measurements");
            }
            window.WindowSkin = new GameUiWindowSkin(library, 0, new Padding(11, 21, 5, 25), Padding.Empty, Padding.Empty, new Size(166, 198), true, Rectangle.Empty, Rectangle.Empty, Rectangle.Empty);
            Check(!label.Visible && Local(label) == Rectangle.Empty, "Headerless skin left a visible title or nonempty bounds");
            window.WindowSkin = Skin(library);
            window.HasTitle = false; Check(!label.Visible, "HasTitle=false failed for bounded title");
            window.HasTitle = true; Check(label.Visible && Local(label) == window.WindowSkin.GetTitleArea(window.Size), "Visible title lost bounds");
            window.WindowSkin = null;
            Check(label.AutoSize == autoSize && label.DrawFormat == nativeFormat && ReferenceEquals(label.Font, font), "Headerless/rebound skin overwrote native title settings");
        }
    }
    private static void SavedVisibility(TestScene root, MirLibrary library)
    {
        string directory = Path.Combine(Path.GetTempPath(), "ZirconMiniMapVisibility-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var store = new GameUiLayoutStore(directory);
        try
        {
            var position = new Point(800, 10);
            Check(store.TrySave(Config.GameSize, WindowType.MiniMapBox,
                new GameUiWindowLayout(position, new Size(166, 198), false)), "Cannot seed saved hidden minimap");
            byte[] saved = File.ReadAllBytes(store.FilePath);
            using (var d = new MiniMapDialog { Parent = root, Visible = true })
            {
                d.UseOfficialLayout(store);
                d.UseGameUiMiniMap(GameUiMiniMapLayout.OfficialV1);
                d.WindowSkin = Skin(library);
                d.LoadSettings();
                Check(d.Visible && d.TitleLabel.Visible && d.Panel.Visible, "Saved hidden state removed OfficialV1 minimap");
                Check(d.Location == position, "Recovering hidden minimap lost saved position");
                Check(File.ReadAllBytes(store.FilePath).SequenceEqual(saved), "Loading hidden minimap rewrote saved layout");
                d.Opacity = .5F;
                for (int i = 0; i < 6; i++)
                {
                    d.ToggleDisplay();
                    Check(d.Visible && d.GameUiMiniMapCollapsed == (i % 2 == 0) &&
                        d.Panel.Visible == (i % 2 != 0) && d.Opacity == .5F,
                        "OfficialV1 shortcut hid map or changed transparency");
                }
                d.SetGameUiMiniMapCollapsed(true);
                d.LoadSettings();
                Check(d.Visible && d.GameUiMiniMapCollapsed && !d.Panel.Visible, "Settings reload hid collapsed header");
                d.Visible = false;
                d.ToggleDisplay();
                Check(d.Visible && !d.GameUiMiniMapCollapsed && d.Panel.Visible, "Shortcut failed to recover hidden map");
                d.UseGameUiMiniMap(null);
                d.LoadSettings();
                Check(!d.Visible && d.AutomaticVisiblity, "Legacy visibility restoration changed");
                d.ToggleDisplay();
                Check(d.Visible && d.Opacity == 1F, "Legacy shortcut failed to show map");
                d.ToggleDisplay();
                Check(d.Visible && d.Opacity == .5F, "Legacy shortcut failed to dim map");
                d.ToggleDisplay();
                Check(!d.Visible, "Legacy shortcut failed to hide map");
            }
        }
        finally { File.Delete(store.FilePath); Directory.Delete(directory); }
    }
    [STAThread] private static int Main(string[] args)
    {
        var oldGame = GameScene.Game; var oldActive = DXControl.ActiveScene; var oldNPC = Globals.NPCInfoList; var oldMovement = Globals.MovementInfoList;
        MirLibrary oldMap; bool hadMap = CEnvir.LibraryList.TryGetValue(LibraryFile.MiniMap, out oldMap);
        string file = Path.GetTempFileName(), mapFile = Path.GetTempFileName();
        try
        {
            var frame = (MirImage)FormatterServices.GetUninitializedObject(typeof(MirImage)); frame.Width = 166; frame.Height = 198;
            var frame2 = (MirImage)FormatterServices.GetUninitializedObject(typeof(MirImage)); frame2.Width = 166; frame2.Height = 198;
            var mapImage = (MirImage)FormatterServices.GetUninitializedObject(typeof(MirImage)); mapImage.Width = 300; mapImage.Height = 260;
            var smallMap = (MirImage)FormatterServices.GetUninitializedObject(typeof(MirImage)); smallMap.Width = 20; smallMap.Height = 30;
            using (var library = new MirLibrary(file) { Loaded = true, Images = new[] { frame, frame2 } })
            using (var maps = new MirLibrary(mapFile) { Loaded = true, Images = new[] { mapImage, smallMap } })
            using (var target = new TargetForm { ClientSize = new Size(1024, 768) })
            {
                CEnvir.Target = target; CEnvir.LibraryList[LibraryFile.MiniMap] = maps;
                Globals.NPCInfoList = (DBCollection<NPCInfo>)FormatterServices.GetUninitializedObject(typeof(DBCollection<NPCInfo>)); Globals.NPCInfoList.Binding = new List<NPCInfo>();
                Globals.MovementInfoList = (DBCollection<MovementInfo>)FormatterServices.GetUninitializedObject(typeof(DBCollection<MovementInfo>)); Globals.MovementInfoList.Binding = new List<MovementInfo>();
                using (var root = new TestScene { IsVisible = true })
                {
                    DXControl.ActiveScene = root;
                    var scene = (GameScene)FormatterServices.GetUninitializedObject(typeof(GameScene)); GC.SuppressFinalize(scene); GameScene.Game = scene;
                    scene.DataDictionary = new SortedDictionary<uint, ClientObjectData>();
                    var user = (UserObject)FormatterServices.GetUninitializedObject(typeof(UserObject)); user.ObjectID = 7; Set(scene, "_User", user);
                    using (var map = new MapControl { Width = 600, Height = 1040 })
                    {
                        scene.MapControl = map; Set(map, "_MapInfo", new MapInfo()); Geometry(args[0]); SavedVisibility(root, library); WindowTitles(root, library); Adapt(root, scene, user, map, library); Toolbar(root, scene, user, map, library); Viewport(root, scene, user, map, library, maps);
                        if (args.Length > 1) Render(root, scene, args[1], args.Length > 2 ? args[2] : null);
                    }
                }
            }
            Console.WriteLine("Minimap layout: " + checks + " checks passed; title/safety, true collapse/expand, live collapsed coordinates, native viewport/markers/pan, geometry, map-subscriber transitions, rollback and disposal. No real map loading, packets or final-art/game acceptance."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { GameScene.Game = oldGame; DXControl.ActiveScene = oldActive; CEnvir.Target = null; Globals.NPCInfoList = oldNPC; Globals.MovementInfoList = oldMovement; if (hadMap) CEnvir.LibraryList[LibraryFile.MiniMap] = oldMap; else CEnvir.LibraryList.Remove(LibraryFile.MiniMap); File.Delete(file); File.Delete(mapFile); }
    }
}
