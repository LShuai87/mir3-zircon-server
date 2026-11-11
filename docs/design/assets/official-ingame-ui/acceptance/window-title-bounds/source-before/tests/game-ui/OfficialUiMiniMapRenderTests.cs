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
    // Synthetic colours isolate frame/viewport order and ancestor clipping on the real GPU path.
    // They do not assert acceptance of the final official artwork.
    private static void Render(TestScene root, GameScene scene, string output)
    {
        Directory.CreateDirectory(output);
        string fixture = Path.Combine(Path.GetTempPath(), "minimap-render-" + Guid.NewGuid().ToString("N") + ".Zl");
        new Mir3Library { Images = new[] { Solid(166, 198, 0xf800), Solid(300, 260, 0x07e0), Solid(40, 32, 0x07e0) } }.Save(fixture);
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
                    using (var d = new MiniMapDialog { Parent = root, Visible = true })
                    {
                        library.ReadLibrary();
                        d.UseGameUiMiniMap(GameUiMiniMapLayout.OfficialV1); d.WindowSkin = Skin(library);
                        d.TitleLabel.Visible = false; d.CloseButton.Visible = false; Get<DXLabel>(d, "gameUiCoordinates").Visible = false;
                        int frame = 0; long pixels = 0;
                        foreach (int mapCase in new[] { 0, 1, 2 })
                        foreach (Point location in new[] { new Point(20, 20), new Point(840, 540) })
                        {
                            d.Location = location;
                            var image = Get<DXImageControl>(d, "Image");
                            image.BindImage(mapCase == 2 ? null : library, mapCase == 0 ? 1 : mapCase == 1 ? 2 : -1);
                            image.Location = mapCase == 0 ? new Point(-90, -70) : new Point(30, 40);
                            device.Clear(ClearFlags.Target, Color.FromArgb(40, 80, 120), 1, 0);
                            device.BeginScene(); DXManager.Sprite.Begin(SpriteFlags.AlphaBlend);
                            device.SetSamplerState(0, SamplerState.MinFilter, TextureFilter.Point); device.SetSamplerState(0, SamplerState.MagFilter, TextureFilter.Point);
                            d.Draw(); DXManager.Sprite.End(); device.EndScene();
                            string path = Path.Combine(output, "viewport-" + (++frame) + ".png"); Surface.ToFile(DXManager.MainSurface, path, ImageFileFormat.Png);
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
                        Console.WriteLine("Minimap GPU viewport: " + pixels + " pixel comparisons passed across 2 placements and 3 map states; native Draw path, opaque frame, original image, black backdrop and ancestor clipping. Synthetic artwork only.");
                    }
                }
                finally
                {
                    typeof(DXManager).GetMethod("CleanUp", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                    Property("Device", null); File.Delete(fixture);
                }
            }
        }
    }
}
