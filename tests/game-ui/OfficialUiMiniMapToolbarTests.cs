using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Client.Controls;
using Client.Envir;
using Client.Models;
using Client.Scenes;
using Client.Scenes.Views;
using Library;
using Library.SystemModels;
using UserObject = Client.Models.UserObject;

internal static partial class OfficialUiMiniMapLayoutTests
{
    private static Color Mix(Color source, Color background, float opacity) => Color.FromArgb(
        (int)(source.R * opacity + background.R * (1 - opacity)),
        (int)(source.G * opacity + background.G * (1 - opacity)),
        (int)(source.B * opacity + background.B * (1 - opacity)));
    private static void ToolbarVisible(MiniMapDialog d, bool visible)
    {
        foreach (string field in new[] { "gameUiBigMap", "gameUiTransparency", "gameUiZoom" })
            Get<DXButton>(d, field).Visible = visible;
    }
    private static void ClickToolbar(TestScene root, DXButton button, MouseButtons mouse = MouseButtons.Left)
    {
        button.Sound = SoundIndex.None;
        var b = button.DisplayArea;
        var e = new MouseEventArgs(mouse, 1, b.X + b.Width / 2, b.Y + b.Height / 2, 0);
        root.OnMouseMove(e);
        Check(DXControl.MouseControl == button, "Toolbar artwork stole button input: " + button.Hint);
        DXControl.MouseControl.OnMouseClick(e);
    }
    private static void Toolbar(TestScene root, GameScene scene, UserObject user, MapControl map, MirLibrary library)
    {
        string oldPath = Config.MapPath;
        string temp = Path.Combine(Path.GetTempPath(), "minimap-toolbar-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        Config.MapPath = temp + Path.DirectorySeparatorChar;
        try
        {
            using (var stream = File.Create(Path.Combine(temp, "toolbar.map")))
            using (var writer = new BinaryWriter(stream))
            { writer.Write(new byte[22]); writer.Write((short)600); writer.Write((short)1040); }
            var info = new MapInfo(); Set(info, "_FileName", "toolbar"); Set(info, "_Description", "工具栏测试地图");
            scene.DataDictionary.Clear();
            Set(scene, "_Size", root.Size);
            using (var bigMap = new BigMapDialog { Visible = false, Parent = root })
            using (var d = new MiniMapDialog { Parent = root, Visible = true })
            {
                scene.BigMapBox = bigMap; scene.MiniMapBox = d;
                ChangeMap(map, info);
                var data = new ClientObjectData { ObjectID = user.ObjectID, MapIndex = info.Index, Name = "Player", Location = new Point(240, 360) };
                scene.DataDictionary[user.ObjectID] = data; d.Update(data);
                var originalChildren = Children(d).ToArray();
                var marker = d.MapInfoObjects[data]; var markerLocation = marker.Location;
                float sx = MiniMapDialog.ScaleX, sy = MiniMapDialog.ScaleY;
                var image = Get<DXImageControl>(d, "Image"); Size imageSize = image.Size;
                d.UseGameUiMiniMap(GameUiMiniMapLayout.OfficialV1); d.WindowSkin = Skin(library);
                var open = Get<DXButton>(d, "gameUiBigMap"); var opacity = Get<DXButton>(d, "gameUiTransparency"); var zoom = Get<DXButton>(d, "gameUiZoom");
                Check(Children(d).Count == originalChildren.Length + 5, "Toolbar controls were duplicated or missing");
                foreach (bool collapsed in new[] { false, true })
                {
                    d.SetGameUiMiniMapCollapsed(collapsed);
                    d.Location = new Point(root.Size.Width - d.Size.Width, 15);
                    foreach (bool enlarged in new[] { false, true, false, true, false })
                    {
                        if (d.GameUiMiniMapEnlarged != enlarged) ClickToolbar(root, zoom);
                        var layout = d.GameUiMiniMapActiveLayout;
                        Check(d.GameUiMiniMapEnlarged == enlarged && d.GameUiMiniMapCollapsed == collapsed, "Zoom lost collapse/size state");
                        Check(d.Size == (collapsed ? layout.CollapsedSize : layout.WindowSize) && d.Location.X + d.Size.Width == root.Size.Width, "Zoom geometry/right anchor differs");
                        Check(d.Panel.Visible == !collapsed && Local(d.Panel) == layout.Viewport, "Zoom did not resize the native viewport");
                        Check(Local(open) == layout.FooterBounds(layout.BigMap, collapsed) && Local(opacity) == layout.FooterBounds(layout.Transparency, collapsed) && Local(zoom) == layout.FooterBounds(layout.Zoom, collapsed), "Footer buttons did not follow geometry");
                        Check(Local(Get<DXLabel>(d, "gameUiCoordinates")) == layout.FooterBounds(layout.Coordinates, collapsed), "Coordinates did not stay in footer");
                        Check(image.Size == imageSize && marker.Location == markerLocation && d.MapInfoObjects[data] == marker && MiniMapDialog.ScaleX == sx && MiniMapDialog.ScaleY == sy, "Viewport magnifier changed map pixels, marker identity or coordinate scale");
                        ClickToolbar(root, zoom, MouseButtons.Right);
                        Check(d.GameUiMiniMapEnlarged == enlarged, "Right click changed viewport size");
                        ClickToolbar(root, opacity); Check(d.Opacity == .5F && d.Visible && image.ImageOpacity == .5F && marker.Opacity == .5F && open.Opacity == .5F && zoom.Opacity == .5F && Get<DXControl>(d, "gameUiExpandStroke").Opacity == .5F, "Transparency did not propagate");
                        Check(opacity.Hint == "取消半透明显示", "Transparency hint stale");
                        ClickToolbar(root, opacity, MouseButtons.Right); Check(d.Opacity == .5F, "Right click changed opacity");
                        ClickToolbar(root, opacity); Check(d.Opacity == 1F && opacity.Hint == "半透明显示", "Transparency did not restore opaque state");
                        d.Opacity = .5F; Check(opacity.Hint == "取消半透明显示", "Keyboard/external opacity left stale toolbar state"); d.Opacity = 1F;
                        ClickToolbar(root, open, MouseButtons.Right); Check(!bigMap.Visible, "Right click opened big map");
                        ClickToolbar(root, open); Check(bigMap.Visible && bigMap.SelectedInfo == info && bigMap.Opacity == 1F && d.Visible, "Big map button did not open current map");
                        bigMap.Opacity = .5F; ClickToolbar(root, open); Check(bigMap.Opacity == 1F && bigMap.SelectedInfo == info, "Repeated big map action did not restore opaque current map");
                        bigMap.Visible = false;
                    }
                }
                // Map and skin changes retain toolbar state and map conversion.
                d.SetGameUiMiniMapEnlarged(true); d.SetGameUiMiniMapCollapsed(true); d.Opacity = .5F;
                d.WindowSkin = Skin(library, 1); d.UseGameUiMiniMap(GameUiMiniMapLayout.OfficialV1);
                Check(d.GameUiMiniMapEnlarged && d.GameUiMiniMapCollapsed && d.Size == new Size(316, 46) && d.Opacity == .5F, "Rebind reset footer state");
                ChangeMap(map, null); d.Process(); Check(!open.Enabled, "No-map big map entry remained enabled");
                ChangeMap(map, info); d.Process(); Check(open.Enabled && d.GameUiMiniMapEnlarged && d.GameUiMiniMapCollapsed, "Map change reset footer state");
                var other = new MapInfo(); Set(other, "_FileName", "toolbar");
                bigMap.Visible = true; bigMap.SelectedInfo = other;
                d.BringToFront(); ClickToolbar(root, open); Check(bigMap.SelectedInfo == info, "Big map entry retained a previously browsed map"); bigMap.Visible = false;
                d.Opacity = 1F;
                d.Location = new Point(0, 740); d.SetGameUiMiniMapCollapsed(false);
                Check(d.Location == new Point(0, root.Size.Height - 348), "Large expansion failed parent clamp");
                bool fail = true;
                EventHandler<EventArgs> onSize = (sender, args) => { if (fail) { fail = false; throw new InvalidOperationException("Injected zoom size failure"); } };
                d.SizeChanged += onSize;
                Reject<InvalidOperationException>(() => d.SetGameUiMiniMapEnlarged(false), "Zoom observer failure not surfaced");
                d.SizeChanged -= onSize;
                Check(d.GameUiMiniMapEnlarged && d.Size == new Size(316, 348) && d.Panel.Visible, "Failed zoom did not restore previous size/state");
                d.UseGameUiMiniMap(null);
                Check(open.IsDisposed && opacity.IsDisposed && zoom.IsDisposed && Children(d).SequenceEqual(originalChildren) && !d.GameUiMiniMapEnlarged, "Detach leaked toolbar controls/state");
                d.UseGameUiMiniMap(GameUiMiniMapLayout.OfficialV1);
                var disposedButton = Get<DXButton>(d, "gameUiZoom"); d.Dispose(); Check(disposedButton.IsDisposed, "Disposal leaked footer button");
                scene.DataDictionary.Clear(); scene.MiniMapBox = null; scene.BigMapBox = null;
            }
        }
        finally
        {
            Config.MapPath = oldPath;
            File.Delete(Path.Combine(temp, "toolbar.map")); Directory.Delete(temp);
        }
    }
}
