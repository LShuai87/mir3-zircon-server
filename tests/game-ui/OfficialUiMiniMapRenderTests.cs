using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Client.Controls;
using Client.Envir;
using Client.Scenes;
using Client.Scenes.Views;
using ImageManager;
using SlimDX.Direct3D9;

internal static partial class OfficialUiMiniMapLayoutTests
{
    private static void Property(string name, object value) => typeof(DXManager).GetProperty(name).SetValue(null, value, null);
    private static Mir3Image Solid(short width, short height, ushort colour)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            for (int i = 0; i < ((width + 3) / 4) * ((height + 3) / 4); i++)
            { writer.Write(colour); writer.Write((ushort)0); writer.Write(0U); }
            return new Mir3Image { Width = width, Height = height, Data = stream.ToArray() };
        }
    }
    private static void Capture(MiniMapDialog window, Device device, string path)
    {
        device.Clear(ClearFlags.Target, Color.FromArgb(40, 80, 120), 1, 0);
        device.BeginScene(); DXManager.Sprite.Begin(SpriteFlags.AlphaBlend);
        device.SetSamplerState(0, SamplerState.MinFilter, TextureFilter.Point); device.SetSamplerState(0, SamplerState.MagFilter, TextureFilter.Point);
        window.Draw(); DXManager.Sprite.End(); device.EndScene();
        Surface.ToFile(DXManager.MainSurface, path, ImageFileFormat.Png);
    }
    // Synthetic colours isolate frame/viewport order and ancestor clipping on the real GPU path.
    // They do not assert acceptance of the final official artwork.
    private static void Render(TestScene root, GameScene scene, string output, string packagePath)
    {
        Directory.CreateDirectory(output);
        string fixture = Path.Combine(Path.GetTempPath(), "minimap-render-" + Guid.NewGuid().ToString("N") + ".Zl");
        new Mir3Library { Images = new[] { Solid(166, 198, 0xf800), Solid(300, 260, 0x07e0), Solid(40, 32, 0x07e0), Solid(400, 32, 0x07e0), Solid(40, 400, 0x07e0), Solid(150, 152, 0x07e0), Solid(300, 302, 0x07e0) } }.Save(fixture);
        using (var d3d = new Direct3D())
        {
            var parameters = new PresentParameters { Windowed = true, SwapEffect = SwapEffect.Discard,
                BackBufferFormat = Format.X8R8G8B8, BackBufferWidth = 1024, BackBufferHeight = 768, PresentFlags = PresentFlags.LockableBackBuffer };
            using (var device = new Device(d3d, 0, DeviceType.Hardware, CEnvir.Target.Handle, CreateFlags.SoftwareVertexProcessing, parameters))
            {
                Property("Device", device); Property("Parameters", parameters);
                try
                {
                    typeof(DXManager).GetMethod("LoadTextures", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                    using (var library = new MirLibrary(fixture))
                    using (var bigMap = new BigMapDialog { Parent = root, Visible = false })
                    using (var d = new MiniMapDialog { Parent = root, Visible = true })
                    {
                        scene.BigMapBox = bigMap;
                        library.ReadLibrary();
                        d.UseGameUiMiniMap(GameUiMiniMapLayout.OfficialV1); d.WindowSkin = Skin(library);
                        ToolbarVisible(d, false);
                        Get<DXLabel>(d, "gameUiSafety").Visible = false; d.TitleLabel.Visible = false; d.CloseButton.Visible = false; Get<DXLabel>(d, "gameUiCoordinates").Visible = false;
                        int frame = 0; long pixels = 0;
                        foreach (int mapCase in new[] { 0, 1, 2 })
                        foreach (Point location in new[] { new Point(20, 20), new Point(840, 540) })
                        {
                            d.Location = location;
                            var image = Get<DXImageControl>(d, "Image");
                            image.BindImage(mapCase == 2 ? null : library, mapCase == 0 ? 1 : mapCase == 1 ? 2 : -1);
                            image.Location = mapCase == 0 ? new Point(-90, -70) : new Point(30, 40);
                            string path = Path.Combine(output, "viewport-" + (++frame) + ".png"); Capture(d, device, path);
                            using (var bitmap = new Bitmap(path))
                            for (int y = -2; y < d.Size.Height + 2; y++)
                            for (int x = -2; x < d.Size.Width + 2; x++)
                            {
                                var viewport = GameUiMiniMapLayout.OfficialV1.Viewport;
                                bool mapPixel = mapCase == 0 || mapCase == 1 && new Rectangle(viewport.X + 30, viewport.Y + 40, 40, 32).Contains(x, y);
                                Color expected = !new Rectangle(Point.Empty, d.Size).Contains(x, y) ? Color.FromArgb(40, 80, 120) :
                                    viewport.Contains(x, y) ? (mapPixel ? Color.Lime : Color.Black) : Color.Red;
                                Color actual = bitmap.GetPixel(location.X + x, location.Y + y);
                                if (Math.Abs(actual.R - expected.R) > 3 || Math.Abs(actual.G - expected.G) > 3 || Math.Abs(actual.B - expected.B) > 3)
                                    throw new Exception("Minimap rendered wrong layer/clip at " + x + "," + y + ": " + actual + " expected " + expected);
                                pixels++;
                            }
                            Check(DXManager.CurrentSurface == DXManager.MainSurface && DXManager.Sprite.Transform == SlimDX.Matrix.Identity, "Minimap render leaked device state");
                        }
                        long outsideTitlePixels = 0; int titleFrames = 0, changedTitlePixels = 0;
                        d.TitleLabel.Visible = true;
                        foreach (Point location in new[] { new Point(20, 20), new Point(840, 540) })
                        {
                            d.Location = location; d.TitleLabel.Text = "";
                            string blankPath = Path.Combine(output, "title-blank-" + location.X + ".png"); Capture(d, device, blankPath);
                            using (var blank = new Bitmap(blankPath))
                            foreach (string text in new[] { "边境村落", "非常长的地图名称用于确认标题不会越过右侧收起按钮和左侧窗口边界ABCDEFGHIJKLMNOPQRSTUVWXYZ", "地图 & 标题\r\n第二行" })
                            {
                                d.TitleLabel.Text = text;
                                string path = Path.Combine(output, "title-" + (++titleFrames) + ".png"); Capture(d, device, path);
                                Rectangle title = d.WindowSkin.GetTitleArea(d.Size); int changed = 0;
                                using (var bitmap = new Bitmap(path))
                                for (int y = -2; y < d.Size.Height + 2; y++)
                                for (int x = -2; x < d.Size.Width + 2; x++)
                                {
                                    bool same = blank.GetPixel(location.X + x, location.Y + y).ToArgb() == bitmap.GetPixel(location.X + x, location.Y + y).ToArgb();
                                    if (title.Contains(x, y)) { if (!same) changed++; }
                                    else { Check(same, "Title changed pixels outside its rectangle, including collapse button: " + x + "," + y); outsideTitlePixels++; }
                                }
                                Check(changed > 0, "Bounded title rendered no visible text"); changedTitlePixels += changed;
                                Check(Local(d.TitleLabel) == title && !d.TitleLabel.IsControl, "Rendered title geometry/input changed");
                                Check(DXManager.CurrentSurface == DXManager.MainSurface && DXManager.Sprite.Transform == SlimDX.Matrix.Identity, "Title render leaked device state");
                            }
                        }
                        d.Location = new Point(20, 20); d.TitleLabel.Visible = false;
                        d.SetGameUiMiniMapCollapsed(true);
                        // The collapsed frame must copy the original footer, with no centre/map pixels below it.
                        d.TitleLabel.Visible = false; d.CloseButton.Visible = false; Get<DXControl>(d, "gameUiExpandStroke").Visible = false;
                        string collapsedPath = Path.Combine(output, "collapsed-synthetic.png"); Capture(d, device, collapsedPath);
                        using (var expanded = new Bitmap(Path.Combine(output, "viewport-6.png")))
                        using (var collapsed = new Bitmap(collapsedPath))
                        for (int y = 0; y < 60; y++)
                        for (int x = 0; x < 166; x++)
                        {
                            Color expected = y >= 46 ? Color.FromArgb(40, 80, 120) :
                                expanded.GetPixel(840 + x, 540 + (y < 21 ? y : y + 152));
                            Check(collapsed.GetPixel(20 + x, 20 + y).ToArgb() == expected.ToArgb(), "Collapsed header/footer crop or removed viewport pixels differ");
                        }
                        d.SetGameUiMiniMapCollapsed(false);
                        d.SetGameUiMiniMapEnlarged(true); d.Location = new Point(20, 20);
                        d.TitleLabel.Visible = false; d.CloseButton.Visible = false;
                        var enlargedImage = Get<DXImageControl>(d, "Image");
                        enlargedImage.BindImage(library, 1); enlargedImage.Location = new Point(-40, -20);
                        foreach (float opacity in new[] { 1F, .5F })
                        {
                            d.Opacity = opacity;
                            string path = Path.Combine(output, "enlarged-synthetic-" + (opacity == 1F ? "opaque" : "transparent") + ".png");
                            Capture(d, device, path);
                            using (var bitmap = new Bitmap(path))
                            for (int y = -2; y < d.Size.Height + 2; y++) for (int x = -2; x < d.Size.Width + 2; x++)
                            {
                                var viewport = d.GameUiMiniMapActiveLayout.Viewport;
                                var mapArea = new Rectangle(viewport.X - 40, viewport.Y - 20, 300, 260);
                                bool inside = new Rectangle(Point.Empty, d.Size).Contains(x, y);
                                var background = Color.FromArgb(40, 80, 120);
                                // The native opacity API blends the frame, viewport backing and image
                                // independently; match the existing keyboard path's exact layer order.
                                Color expected = background;
                                if (inside) expected = Mix(Color.Red, expected, opacity);
                                if (inside && viewport.Contains(x, y))
                                {
                                    expected = Mix(Color.Black, expected, opacity);
                                    if (mapArea.Contains(x, y)) expected = Mix(Color.Lime, expected, opacity);
                                }
                                var actual = bitmap.GetPixel(20 + x, 20 + y);
                                Check(Math.Abs(actual.R - expected.R) <= 3 && Math.Abs(actual.G - expected.G) <= 3 && Math.Abs(actual.B - expected.B) <= 3,
                                    "Enlarged viewport clip/opacity differs at " + x + "," + y + ": " + actual + " expected " + expected);
                            }
                            Check(DXManager.CurrentSurface == DXManager.MainSurface && DXManager.Sprite.Transform == SlimDX.Matrix.Identity, "Enlarged render leaked device state");
                        }
                        d.Opacity = 1F; d.SetGameUiMiniMapEnlarged(false); d.Location = new Point(20, 20);
                        enlargedImage.BindImage(null, -1);
                        if (!string.IsNullOrEmpty(packagePath))
                        using (var art = new MirLibrary(packagePath))
                        {
                            art.ReadLibrary();
                            var layout = GameUiMiniMapLayout.OfficialV1;
                            var frameInfo = GameUiOfficialV1Catalog.PackageManifest.Frames["minimap-frame"];
                            d.WindowSkin = new GameUiWindowSkin(art, frameInfo.Index, Padding.Empty, Padding.Empty, Padding.Empty,
                                frameInfo.Size, true, layout.Title, new Rectangle(0, 0, 142, 20), layout.Collapse,
                                GameUiOfficialV1Catalog.Buttons["minimap-collapse-button"]);
                            ToolbarVisible(d, true);
                            var safety = Get<DXLabel>(d, "gameUiSafety");
                            d.TitleLabel.Visible = true; d.TitleLabel.Text = "比奇县";
                            safety.Visible = true; safety.Text = "安全"; safety.ForeColour = Color.Lime;
                            d.CloseButton.Visible = true;
                            Get<DXLabel>(d, "gameUiCoordinates").Visible = true;
                            Get<DXLabel>(d, "gameUiCoordinates").Text = "446:391";
                            Capture(d, device, Path.Combine(output, "official-expanded.png"));
                            d.SetGameUiMiniMapCollapsed(true);
                            Capture(d, device, Path.Combine(output, "official-collapsed.png"));
                            safety.Text = "危险"; safety.ForeColour = Color.Red;
                            d.TitleLabel.Text = "很长的地图名称用于验证边界";
                            Capture(d, device, Path.Combine(output, "official-collapsed-danger.png"));
                            safety.Text = "安全"; safety.ForeColour = Color.Lime; d.TitleLabel.Text = "比奇县";
                            d.SetGameUiMiniMapCollapsed(false);
                            d.SetGameUiMiniMapEnlarged(true); d.Location = new Point(20, 20);
                            Capture(d, device, Path.Combine(output, "official-enlarged.png"));
                            using (var normal = new Bitmap(Path.Combine(output, "official-expanded.png")))
                            using (var enlarged = new Bitmap(Path.Combine(output, "official-enlarged.png")))
                            for (int y = 0; y < 25; y++) for (int x = 0; x < layout.Coordinates.Width; x++)
                                Check(normal.GetPixel(20 + layout.Coordinates.Left + x, 20 + layout.Viewport.Bottom + y) ==
                                    enlarged.GetPixel(20 + layout.Coordinates.Left + 75 + x, 20 + layout.Viewport.Bottom + 150 + y),
                                    "Enlarged coordinate well stretched or lost its centred native pixels");
                            d.Opacity = .5F;
                            Capture(d, device, Path.Combine(output, "official-enlarged-transparent.png"));
                            d.Opacity = 1F; d.SetGameUiMiniMapCollapsed(true);
                            Capture(d, device, Path.Combine(output, "official-enlarged-collapsed.png"));
                            d.SetGameUiMiniMapEnlarged(false); d.Location = new Point(20, 20);
                            d.SetGameUiMiniMapCollapsed(false);
                            var transparency = Get<DXButton>(d, "gameUiTransparency");
                            var normalPath = Path.Combine(output, "official-footer-normal.png");
                            DXControl.MouseControl = null; Capture(d, device, normalPath);
                            var hoverPath = Path.Combine(output, "official-footer-hover.png");
                            root.OnMouseMove(new MouseEventArgs(MouseButtons.None, 0, transparency.DisplayArea.X + 10, transparency.DisplayArea.Y + 8, 0));
                            Capture(d, device, hoverPath);
                            using (var normal = new Bitmap(normalPath))
                            using (var hover = new Bitmap(hoverPath))
                            {
                                int changed = 0;
                                for (int y = 0; y < 16; y++) for (int x = 0; x < 20; x++)
                                    if (normal.GetPixel(transparency.DisplayArea.X + x, transparency.DisplayArea.Y + y) != hover.GetPixel(transparency.DisplayArea.X + x, transparency.DisplayArea.Y + y)) changed++;
                                Check(changed > 0, "Native footer hover sprite did not render");
                            }
                            DXControl.MouseControl = null;
                            d.Opacity = .5F;
                            Capture(d, device, Path.Combine(output, "official-transparent.png"));
                            d.Opacity = 1F;
                            // Every footer sprite participates in the same opacity change as the frame.
                            using (var normal = new Bitmap(normalPath))
                            using (var transparent = new Bitmap(Path.Combine(output, "official-transparent.png")))
                            foreach (var button in new[] { Get<DXButton>(d, "gameUiBigMap"), transparency, Get<DXButton>(d, "gameUiZoom") })
                            {
                                int changed = 0;
                                for (int y = 0; y < 16; y++) for (int x = 0; x < 20; x++)
                                {
                                    var a = normal.GetPixel(button.DisplayArea.X + x, button.DisplayArea.Y + y);
                                    var b = transparent.GetPixel(button.DisplayArea.X + x, button.DisplayArea.Y + y);
                                    if (a.ToArgb() != b.ToArgb()) changed++;
                                }
                                Check(changed > 20, "Footer button ignored transparency: " + button.Hint);
                            }
                            d.WindowSkin = Skin(library);
                        }
                        RenderViewportMatrix(d, library, scene, device, output);
                        File.WriteAllText(Path.Combine(output, "metrics.json"), "{" + string.Format("\"viewportPixels\":{0},\"outsideTitlePixels\":{1},\"changedTitlePixels\":{2},\"titleFrames\":{3}", pixels, outsideTitlePixels, changedTitlePixels, titleFrames) + "}");
                        Console.WriteLine("Minimap GPU titles: " + outsideTitlePixels + " identical pixels outside the title, " + changedTitlePixels + " visible text pixels across " + titleFrames + " title frames; long/multiline/literal ampersand, two placements. Synthetic artwork only.");
                        Console.WriteLine("Minimap GPU viewport: " + pixels + " pixel comparisons passed across 2 placements and 3 map states; native Draw path, opaque frame, original image, black backdrop and ancestor clipping. Synthetic artwork only.");
                    }
                }
                finally
                {
                    scene.BigMapBox = null;
                    typeof(DXManager).GetMethod("CleanUp", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                    Property("Device", null); File.Delete(fixture);
                }
            }
        }
    }
}
