using System;
using System.Drawing;
using System.Linq;
using System.Runtime.Serialization;
using Client;
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
    private static MirImage MapFrame(Size size)
    {
        var frame = (MirImage)FormatterServices.GetUninitializedObject(typeof(MirImage));
        frame.Width = (short)size.Width; frame.Height = (short)size.Height; return frame;
    }
    private static void Viewport(TestScene root, GameScene scene, UserObject user, MapControl map, MirLibrary skin, MirLibrary maps)
    {
        var originalFrames = maps.Images;
        var sizes = new[] { new Size(300, 260), new Size(20, 30), new Size(400, 30), new Size(20, 400),
            new Size(150, 152), new Size(300, 302), new Size(800, 900) };
        maps.Images = sizes.Select(MapFrame).ToArray();
        scene.DataDictionary.Clear();
        try
        {
            using (var d = new MiniMapDialog { Parent = root, Visible = true })
            {
                d.UseGameUiMiniMap(GameUiMiniMapLayout.OfficialV1); d.WindowSkin = Skin(skin);
                var image = Get<DXImageControl>(d, "Image");
                var data = new ClientObjectData { ObjectID = user.ObjectID, Name = "Player" };
                for (int i = 0; i < sizes.Length; i++)
                foreach (bool large in new[] { false, true })
                {
                    var info = new MapInfo(); Set(info, "_MiniMap", i);
                    data.MapIndex = info.Index; scene.DataDictionary[user.ObjectID] = data;
                    ChangeMap(map, info); d.SetGameUiMiniMapEnlarged(large);
                    var viewport = d.Panel.Size;
                    Check(image.Visible && image.Size == sizes[i], "Viewport resized the source map");
                    foreach (var position in new[] { Point.Empty, new Point(300, 520), new Point(599, 1039) })
                    {
                        data.Location = position; d.Update(data);
                        var marker = d.MapInfoObjects[data];
                        var projected = new Point((int)(position.X * MiniMapDialog.ScaleX), (int)(position.Y * MiniMapDialog.ScaleY));
                        Check(marker.Location == new Point(projected.X - 1, projected.Y - 1), "Marker projection or native scale changed");
                        // Player markers are 3x3. Their centre must follow the projected world position.
                        foreach (bool xAxis in new[] { true, false })
                        {
                            int source = xAxis ? sizes[i].Width : sizes[i].Height;
                            int view = xAxis ? viewport.Width : viewport.Height;
                            int offset = xAxis ? image.Location.X : image.Location.Y;
                            int player = xAxis ? projected.X : projected.Y;
                            if (source <= view) Check(offset == (view - source) / 2, "Small/exact-fit axis not centred");
                            else
                            {
                                Check(offset <= 0 && offset >= view - source, "Follow exposed empty space at a map edge");
                                if (offset < 0 && offset > view - source) Check(offset + player == view / 2, "Player centre drifted");
                                if (player == 0) Check(offset == 0, "Top/left edge not aligned");
                                if (position.X == 599 && position.Y == 1039) Check(offset == view - source, "Bottom/right edge not aligned");
                            }
                        }
                        var markerLocation = marker.Location; var scaleX = MiniMapDialog.ScaleX; var scaleY = MiniMapDialog.ScaleY;
                        Pan(image, new Point(-10000, 10000)); var panned = image.Location;
                        Check(panned.X == (sizes[i].Width <= viewport.Width ? (viewport.Width - sizes[i].Width) / 2 : viewport.Width - sizes[i].Width), "Pan left limit differs");
                        Check(panned.Y == (sizes[i].Height <= viewport.Height ? (viewport.Height - sizes[i].Height) / 2 : 0), "Pan top limit differs");
                        d.OnClientAreaChanged(d.ClientArea, d.ClientArea); d.UseGameUiMiniMap(GameUiMiniMapLayout.OfficialV1); d.WindowSkin = Skin(skin);
                        Check(image.Location == panned, "Repeated layout/skin reset manual pan");
                        d.SetGameUiMiniMapCollapsed(true); d.SetGameUiMiniMapCollapsed(false);
                        Check(d.MapInfoObjects[data] == marker && marker.Location == markerLocation && MiniMapDialog.ScaleX == scaleX && MiniMapDialog.ScaleY == scaleY, "Collapse changed map projection");
                    }
                    scene.DataDictionary.Clear(); ChangeMap(map, info);
                    Check(image.Location == new Point((viewport.Width - sizes[i].Width) / 2, (viewport.Height - sizes[i].Height) / 2), "No-marker fallback not centred");
                }
                // Null maps, absent frames/libraries and invalid world dimensions must clear stale content and recover.
                foreach (int failure in new[] { 0, 1, 2, 3, 4, 5, 6 })
                {
                    var info = new MapInfo(); Set(info, "_MiniMap", failure == 1 ? 100 : 0);
                    if (failure == 2) map.Width = 0;
                    if (failure == 3) map.Height = 0;
                    if (failure == 4) image.Library = null;
                    if (failure == 5) maps.Images[0] = null;
                    if (failure == 6) maps.Images[0] = MapFrame(new Size(0, 30));
                    ChangeMap(map, failure == 0 ? null : info);
                    d.Update(data); d.Update(new NPCInfo()); d.Update(new MovementInfo());
                    Check(!image.Visible && image.Location == Point.Empty && MiniMapDialog.ScaleX == 0 && MiniMapDialog.ScaleY == 0 && d.MapInfoObjects.Count == 0, "Invalid map retained image, projection or markers");
                    map.Width = 600; map.Height = 1040; image.Library = maps; maps.Images[0] = MapFrame(sizes[0]);
                    Set(info, "_MiniMap", 0); ChangeMap(map, info);
                    Check(image.Visible && MiniMapDialog.ScaleX == .5F && MiniMapDialog.ScaleY == .25F, "Valid map failed to recover");
                }
                Set(scene, "_User", null); d.UpdateMapPosition();
                Check(image.Location == new Point(0, 21), "No-user fallback differs");
                Set(scene, "_User", user);
            }
        }
        finally { maps.Images = originalFrames; scene.DataDictionary.Clear(); Set(scene, "_User", user); map.Width = 600; map.Height = 1040; }
    }
    private static void RenderViewportMatrix(MiniMapDialog d, MirLibrary library, GameScene scene,
        SlimDX.Direct3D9.Device device, string output)
    {
        var image = Get<DXImageControl>(d, "Image");
        var originalLibrary = CEnvir.LibraryList[LibraryFile.MiniMap];
        CEnvir.LibraryList[LibraryFile.MiniMap] = library;
        image.LibraryFile = LibraryFile.MiniMap; image.Library = library;
        long pixels = 0; int frames = 0;
        try
        {
            var user = scene.User;
            var data = new ClientObjectData { ObjectID = user.ObjectID, Name = "Player" };
            scene.DataDictionary[user.ObjectID] = data;
            foreach (int index in new[] { 1, 2, 3, 4, 5, 6 })
            foreach (bool large in new[] { false, true })
            foreach (string mode in new[] { "centre", "edge", "pan", "collapsed" })
            {
                d.SetGameUiMiniMapCollapsed(false); d.SetGameUiMiniMapEnlarged(large); d.Location = new Point(20, 20);
                var info = new MapInfo(); Set(info, "_MiniMap", index); data.MapIndex = info.Index;
                data.Location = mode == "edge" ? Point.Empty : new Point(300, 520);
                ChangeMap(scene.MapControl, info); d.Update(data);
                if (mode == "pan") Pan(image, new Point(-10000, -10000));
                if (mode == "collapsed") d.SetGameUiMiniMapCollapsed(true);
                float opacity = mode == "pan" ? .5F : 1F; d.Opacity = opacity;
                ToolbarVisible(d, false); d.CloseButton.Visible = false; d.TitleLabel.Visible = false;
                Get<DXControl>(d, "gameUiExpandStroke").Visible = false;
                Get<DXLabel>(d, "gameUiSafety").Visible = false; Get<DXLabel>(d, "gameUiCoordinates").Visible = false;
                var viewport = d.GameUiMiniMapActiveLayout.Viewport;
                var mapArea = new Rectangle(viewport.X + image.Location.X, viewport.Y + image.Location.Y, image.Size.Width, image.Size.Height);
                var marker = d.MapInfoObjects[data];
                var markerArea = new Rectangle(mapArea.X + marker.Location.X, mapArea.Y + marker.Location.Y, marker.Size.Width, marker.Size.Height);
                string name = "matrix-" + index + "-" + (large ? "large" : "normal") + "-" + mode + ".png";
                string path = System.IO.Path.Combine(output, name); Capture(d, device, path); frames++;
                using (var bitmap = new Bitmap(path))
                for (int y = -2; y < d.Size.Height + 5; y++)
                for (int x = -2; x < d.Size.Width + 2; x++)
                {
                    var expected = Color.FromArgb(40, 80, 120);
                    bool inside = new Rectangle(Point.Empty, d.Size).Contains(x, y);
                    if (inside) expected = Mix(Color.Red, expected, opacity);
                    if (inside && !d.GameUiMiniMapCollapsed && viewport.Contains(x, y))
                    {
                        expected = Mix(Color.Black, expected, opacity);
                        if (mapArea.Contains(x, y))
                        {
                            expected = Mix(Color.Lime, expected, opacity);
                            if (markerArea.Contains(x, y)) expected = Mix(Color.Cyan, expected, opacity);
                        }
                    }
                    var actual = bitmap.GetPixel(20 + x, 20 + y);
                    Check(Math.Abs(actual.R - expected.R) <= 3 && Math.Abs(actual.G - expected.G) <= 3 && Math.Abs(actual.B - expected.B) <= 3,
                        name + " wrong map/marker/clip at " + x + "," + y + ": " + actual + " expected " + expected);
                    pixels++;
                }
                Check(DXManager.CurrentSurface == DXManager.MainSurface && DXManager.Sprite.Transform == SlimDX.Matrix.Identity, "Viewport matrix leaked device state");
            }
            // Clearing a rendered map must leave only the viewport backing, never the previous texture.
            d.Opacity = 1F; d.SetGameUiMiniMapCollapsed(false); d.CloseButton.Visible = false;
            ChangeMap(scene.MapControl, null);
            string emptyPath = System.IO.Path.Combine(output, "matrix-empty.png"); Capture(d, device, emptyPath);
            using (var bitmap = new Bitmap(emptyPath))
            {
                var area = d.Panel.DisplayArea;
                for (int y = area.Top; y < area.Bottom; y++) for (int x = area.Left; x < area.Right; x++)
                { Check(bitmap.GetPixel(x, y).ToArgb() == Color.Black.ToArgb(), "Cleared map left stale pixels"); pixels++; }
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(output, "viewport-matrix-metrics.json"),
                "{\"frames\":" + (frames + 1) + ",\"pixelComparisons\":" + pixels + ",\"sourceSizes\":6,\"viewportSizes\":2}");
            Console.WriteLine("Minimap viewport matrix: " + pixels + " pixel comparisons across " + (frames + 1) + " frames; centre/edge/manual pan/transparency/collapse/clear on both sizes.");
        }
        finally { CEnvir.LibraryList[LibraryFile.MiniMap] = originalLibrary; scene.DataDictionary.Clear(); }
    }

}
