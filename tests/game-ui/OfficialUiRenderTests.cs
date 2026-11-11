using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using System.Security.Cryptography;
using Client.Controls;
using Client.Envir;
using Client.Scenes;
using Client.UserModels;
using ImageManager;
using SlimDX.Direct3D9;

internal static class OfficialUiRenderTests
{
    private sealed class Window : DXWindow
    {
        public override WindowType Type => WindowType.None;
        public override bool CustomSize => false;
        public override bool AutomaticVisiblity => false;
    }
    public sealed class ReferenceManifest { public List<ReferenceLibrary> libraries { get; set; } }
    public sealed class ReferenceLibrary { public string name { get; set; } public string path { get; set; } public string sha256 { get; set; } public int present { get; set; } }
    private static int checks;
    private static void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
    private static void Property(string name, object value) => typeof(DXManager).GetProperty(name).SetValue(null, value, null);
    private static MirLibrary Fixture(string root)
    {
        string path = Path.Combine(root, "render.Zl");
        byte[] bytes;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            // Nine 4x4 BC1 blocks: opaque red/green, transparent corner,
            // green/opaque black/green, and green/green/blue.
            ushort[] colors = { 0xf800, 0x07e0, 0, 0x07e0, 0, 0x07e0, 0x07e0, 0x07e0, 0x001f };
            for (int i = 0; i < colors.Length; i++)
            {
                writer.Write(colors[i]); writer.Write((ushort)0);
                writer.Write(i == 2 ? uint.MaxValue : 0U);
            }
            bytes = stream.ToArray();
        }
        new Mir3Library { Images = new[] { new Mir3Image { Width = 12, Height = 12, Data = bytes } } }.Save(path);
        var library = new MirLibrary(path); library.ReadLibrary(); return library;
    }
    private static Bitmap Frame(Window window, string path, Color background)
    {
        var device = DXManager.Device;
        device.Clear(ClearFlags.Target, background, 1, 0);
        device.BeginScene();
        DXManager.Sprite.Begin(SpriteFlags.AlphaBlend);
        device.SetSamplerState(0, SamplerState.MinFilter, TextureFilter.Point);
        device.SetSamplerState(0, SamplerState.MagFilter, TextureFilter.Point);
        window.Draw();
        DXManager.Sprite.End();
        device.EndScene();
        Surface.ToFile(DXManager.MainSurface, path, ImageFileFormat.Png);
        return new Bitmap(path);
    }
    private static bool Near(Color actual, Color expected, int tolerance = 3)
    {
        return Math.Abs(actual.R - expected.R) <= tolerance && Math.Abs(actual.G - expected.G) <= tolerance && Math.Abs(actual.B - expected.B) <= tolerance;
    }
    internal static void Run(string root, string outputDirectory, string referenceDirectory = null)
    {
        Directory.CreateDirectory(outputDirectory);
        var background = Color.FromArgb(40, 80, 120);
        using (var host = new Form { ClientSize = new Size(64, 64), ShowInTaskbar = false })
        using (var d3d = new Direct3D())
        {
            var parameters = new PresentParameters { Windowed = true, SwapEffect = SwapEffect.Discard,
                BackBufferFormat = Format.X8R8G8B8, BackBufferWidth = 64, BackBufferHeight = 64,
                PresentFlags = PresentFlags.LockableBackBuffer };
            using (var device = new Device(d3d, 0, DeviceType.Hardware, host.Handle, CreateFlags.SoftwareVertexProcessing, parameters))
            using (var parent = new DXControl { Size = new Size(64, 64), IsVisible = true })
            using (var library = Fixture(root))
            using (var window = new Window { Parent = parent, Location = new Point(8, 8), Size = new Size(20, 20) })
            {
                Property("Device", device); Property("Parameters", parameters);
                typeof(DXManager).GetMethod("LoadTextures", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                var scene = (GameScene)FormatterServices.GetUninitializedObject(typeof(GameScene));
                typeof(DXControl).GetField("_DisplayArea", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(scene, new Rectangle(0, 0, 64, 64));
                var sceneField = typeof(DXControl).GetField("_ActiveScene", BindingFlags.Static | BindingFlags.NonPublic);
                object previousScene = sceneField.GetValue(null); sceneField.SetValue(null, scene);
                try
                {
                    window.WindowSkin = new GameUiWindowSkin(library, 0, new Padding(4), new Padding(4), new Padding(4), new Size(12, 12), false,
                        Rectangle.Empty, Rectangle.Empty, Rectangle.Empty);
                    using (var image = Frame(window, Path.Combine(outputDirectory, "skin-opaque.png"), background))
                    {
                        Check(Near(image.GetPixel(9, 9), Color.Red), "Rendered corner scaled or changed color");
                        Check(Near(image.GetPixel(13, 9), Color.Lime), "Rendered top slice missing");
                        Check(Near(image.GetPixel(25, 9), background), "Transparent BC1 corner became black");
                        Check(Near(image.GetPixel(16, 16), Color.Black), "Opaque black was color-keyed");
                        Check(Near(image.GetPixel(25, 25), Color.Blue), "Rendered bottom corner misplaced");
                        Check(Near(image.GetPixel(29, 29), background), "Skin drew outside logical frame");
                    }
                    Check(DXManager.ControlList.Contains(window), "Window cache is not registered for device loss/expiry");
                    window.Opacity = 0.5F;
                    using (var image = Frame(window, Path.Combine(outputDirectory, "skin-half-opacity.png"), background))
                    {
                        Check(Near(image.GetPixel(25, 9), background), "Fractional opacity fills transparent decoration");
                        Check(Near(image.GetPixel(16, 16), Color.FromArgb(20, 40, 60)), "Fractional opacity ignores opaque black");
                        Check(Near(image.GetPixel(9, 9), Color.FromArgb(147, 40, 60)), "Fractional opacity color mismatch");
                    }
                    window.Size = new Size(24, 24); window.Opacity = 1;
                    using (var image = Frame(window, Path.Combine(outputDirectory, "skin-resized.png"), background))
                        Check(Near(image.GetPixel(29, 29), Color.Blue), "Resize retained obsolete cached geometry");
                    window.DisposeTexture();
                    Check(!window.WindowValid && !DXManager.ControlList.Contains(window), "Texture release left stale cache registration");
                    using (var image = Frame(window, Path.Combine(outputDirectory, "skin-recreated.png"), background))
                        Check(Near(image.GetPixel(29, 29), Color.Blue), "Cache recreation lost skin");
                    Check(DXManager.Sprite.Transform == SlimDX.Matrix.Identity, "Skin leaked sprite transform");
                    Console.WriteLine("Official UI SlimDX render: " + checks + " checks passed; output=" + outputDirectory);
                    if (referenceDirectory != null)
                    {
                        int payloads = 0;
                        var manifest = new JavaScriptSerializer().Deserialize<ReferenceManifest>(
                            File.ReadAllText(Path.Combine(referenceDirectory, "reference-manifest.json")));
                        Check(manifest != null && manifest.libraries != null && manifest.libraries.Count > 0, "Reference manifest must declare libraries");
                        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var declared in manifest.libraries)
                        {
                            Check(declared != null && new[] { "UI", "Interface", "GameInter", "ProgUse" }.Contains(declared.name)
                                && seen.Add(declared.name) && declared.path == "Reference/" + declared.name + ".Zl",
                                "Reference manifest contains an invalid or duplicate library");
                            string name = declared.name;
                            string file = Path.Combine(referenceDirectory, "Reference", name + ".Zl");
                            using (var input = File.OpenRead(file))
                            using (var hash = SHA256.Create())
                                Check(BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "").ToLowerInvariant() == declared.sha256,
                                    "Reference library hash differs from manifest: " + name);
                            int libraryPayloads = 0;
                            using (var actual = new MirLibrary(file))
                            using (var reader = new BinaryReader(File.OpenRead(file)))
                            {
                                actual.ReadLibrary();
                                for (int index = 0; index < actual.Images.Length; index++)
                                {
                                    if (actual.Images[index] == null) continue;
                                    var frame = actual.CreateImage(index, ImageType.Image);
                                    Check(frame != null && frame.ImageValid, "Client failed to upload reference texture " + name + "[" + index + "]");
                                    var description = frame.Image.GetLevelDescription(0);
                                    int width = (frame.Width + 3) / 4 * 4, height = (frame.Height + 3) / 4 * 4;
                                    Check(description.Format == Format.Dxt1 && description.Width == width && description.Height == height, "Reference texture dimensions/format changed");
                                    reader.BaseStream.Position = frame.Position;
                                    byte[] expected = reader.ReadBytes(frame.ImageDataSize);
                                    byte[] uploaded = new byte[expected.Length];
                                    var locked = frame.Image.LockRectangle(0, LockFlags.ReadOnly);
                                    try
                                    {
                                        int rowBytes = width / 4 * 8;
                                        for (int row = 0; row < height / 4; row++)
                                        {
                                            locked.Data.Position = row * locked.Pitch;
                                            int copied = locked.Data.Read(uploaded, row * rowBytes, rowBytes);
                                            if (copied != rowBytes) throw new Exception("Short GPU reference readback");
                                        }
                                    }
                                    finally { frame.Image.UnlockRectangle(0); locked.Data.Dispose(); }
                                    Check(expected.Length == frame.ImageDataSize && expected.SequenceEqual(uploaded), "GPU reference payload differs from Zl bytes " + name + "[" + index + "]");
                                    frame.Dispose(); payloads++; libraryPayloads++;
                                }
                            }
                            Check(libraryPayloads == declared.present, "Reference texture count differs from manifest: " + name);
                        }
                        Console.WriteLine("Official UI GPU reference payloads: " + payloads + " textures uploaded and read back byte-identically.");
                    }
                }
                finally { sceneField.SetValue(null, previousScene); }
            }
            typeof(DXManager).GetMethod("CleanUp", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            Property("Device", null);
        }
    }
}
