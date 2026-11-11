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
        new Rectangle(8, 2, 132, 16), new Rectangle(8, 2, 132, 16), new Rectangle(146, 2, 20, 16));
    private static void Geometry(string fixture)
    {
        var l = GameUiMiniMapLayout.OfficialV1; var lines = File.ReadAllLines(fixture);
        Check(lines.Length == 4, "Fixture shape changed");
        Check(lines[0] == l.WindowSize.Width + "," + l.WindowSize.Height, "Compiled window differs from JSON");
        var areas = new[] { l.Viewport, l.Coordinates, l.Collapse };
        for (int i = 0; i < areas.Length; i++) Check(lines[i + 1] == string.Join(",", new[] { areas[i].X, areas[i].Y, areas[i].Width, areas[i].Height }), "Compiled region differs from JSON");
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
    private static void Adapt(TestScene root, GameScene scene, UserObject user, MapControl map, MirLibrary library)
    {
        var l = GameUiMiniMapLayout.OfficialV1;
        for (int skinCase = 0; skinCase < 2; skinCase++)
        using (var d = new MiniMapDialog { Parent = root, Visible = true })
        {
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
                Check(image.Location == new Point(-marker.Location.X + l.Viewport.Width / 2, -marker.Location.Y + l.Viewport.Height / 2), "Follow center differs");
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
                Set(user, "_CurrentLocation", new Point(123, 456)); d.Process(); Check(label.Text == "123, 456", "Coordinates did not follow live user");
                Pan(image, new Point(30, 40)); Check(image.Location == Point.Empty, "Positive pan was not clamped");
                Pan(image, new Point(-1000, -1000)); Check(image.Location == new Point(panel.Size.Width - image.Size.Width, panel.Size.Height - image.Size.Height), "Negative pan was not clamped");
                d.UpdateMapPosition();
                d.CloseButton.InvokeMouseClick(); Check(!d.Visible && extraClick == cycle + 1, "Collapse lost original close action"); d.Visible = true;
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
            ChangeMap(map, map2);
            Check(marker.IsDisposed && d.MapInfoObjects.Count == 0 && image.Index == 1 && d.TitleLabel.Text == "Second map", "Map-change handler did not replace native map state");
            Check(Math.Abs(MiniMapDialog.ScaleX - 20F / map.Width) < 0.000001F && Math.Abs(MiniMapDialog.ScaleY - 30F / map.Height) < 0.000001F, "Map-change scale differs: actual=" + MiniMapDialog.ScaleX + "," + MiniMapDialog.ScaleY + " image=" + image.Size + " map=" + map.Width + "," + map.Height);
            Pan(image, new Point(-10, -20)); Check(image.Location == Point.Empty, "Small map clamp differs");
            ChangeMap(map, null); Check(Get<DXLabel>(d, "gameUiCoordinates").Text == string.Empty, "No-map coordinates remained visible");
            ChangeMap(map, map2); Set(scene, "_User", null); d.Process(); Check(Get<DXLabel>(d, "gameUiCoordinates").Text == string.Empty, "No-user coordinates remained visible"); Set(scene, "_User", user);
            d.UseGameUiMiniMap(null);
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
            var panel = d.Panel; var image = Get<DXImageControl>(d, "Image"); var label = Get<DXLabel>(d, "gameUiCoordinates"); d.WindowSkin = Skin(library); d.Dispose();
            Check(panel.IsDisposed && image.IsDisposed && label.IsDisposed && d.GameUiMiniMapLayout == null, "Disposal left controls or binding alive");
            var listeners = Get<Delegate>(map, "MapInfoChanged"); Check(listeners == null || !listeners.GetInvocationList().Any(h => ReferenceEquals(h.Target, d)), "Disposed window retained map subscriber");
            Reject<ObjectDisposedException>(() => d.UseGameUiMiniMap(l), "Disposed window accepted binding");
        }
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
                        scene.MapControl = map; Set(map, "_MapInfo", new MapInfo()); Geometry(args[0]); Adapt(root, scene, user, map, library);
                        if (args.Length > 1) Render(root, scene, args[1]);
                    }
                }
            }
            Console.WriteLine("Minimap layout: " + checks + " checks passed; native viewport/markers/pan/close, live coordinates, geometry, map-subscriber transitions, rollback and disposal. No real map loading, packets or final-art/game acceptance."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { GameScene.Game = oldGame; DXControl.ActiveScene = oldActive; CEnvir.Target = null; Globals.NPCInfoList = oldNPC; Globals.MovementInfoList = oldMovement; if (hadMap) CEnvir.LibraryList[LibraryFile.MiniMap] = oldMap; else CEnvir.LibraryList.Remove(LibraryFile.MiniMap); File.Delete(file); File.Delete(mapFile); }
    }
}
