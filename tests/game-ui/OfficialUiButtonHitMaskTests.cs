using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Client.Controls;
using Client.Envir;

internal static class ButtonHitMaskTests
{
    private static int checks, referencePixels;
    private static void Check(bool value, string message)
    { if (!value) throw new Exception("Button hit mask: " + message); checks++; }
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { checks++; return; } throw new Exception("Expected " + typeof(T).Name); }
    private static byte[] Block(ushort first, ushort second, byte selectors)
    { return new byte[] { (byte)first, (byte)(first >> 8), (byte)second, (byte)(second >> 8), selectors, selectors, selectors, selectors }; }
    private static string[] Roles = { "hud-command-center", "hud-command-top", "hud-command-upper-left", "hud-command-upper-right", "hud-command-lower-left", "hud-command-lower-right", "hud-command-bottom", "hud-shop-icon" };

    // Separate file-based oracle. No production mask/VisiblePixel helper is called.
    internal static bool[] ReferenceAlpha(MirLibrary library, int index)
    {
        var image = library.GetImage(index);
        var result = new bool[image.Width * image.Height];
        var stream = (FileStream)((BinaryReader)typeof(MirLibrary).GetField("_BReader", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(library)).BaseStream;
        using (var file = File.OpenRead(stream.Name))
        using (var reader = new BinaryReader(file))
        {
            int width = (image.Width + 3) / 4, height = (image.Height + 3) / 4;
            file.Position = image.Position;
            for (int by = 0; by < height; by++) for (int bx = 0; bx < width; bx++)
            {
                ushort c0 = reader.ReadUInt16(), c1 = reader.ReadUInt16(); uint bits = reader.ReadUInt32();
                for (int pixel = 0; pixel < 16; pixel++)
                {
                    int x = bx * 4 + pixel % 4, y = by * 4 + pixel / 4;
                    if (x < image.Width && y < image.Height)
                        result[y * image.Width + x] = !(c0 <= c1 && ((bits >> (pixel * 2)) & 3) == 3);
                }
            }
        }
        return result;
    }

    internal static void RunCpu(MirLibrary library, string pngDirectory, string output)
    {
        foreach (var endpoints in new[] { new ushort[] { 0, 0 }, new ushort[] { 1, 1 }, new ushort[] { 0, 65535 }, new ushort[] { 65535, 0 } })
            for (int selector = 0; selector < 4; selector++)
            {
                var bytes = Block(endpoints[0], endpoints[1], (byte)(selector * 85));
                var mask = GameUiButtonHitMask.FromBc1(new Size(4, 4), bytes);
                bool opaque = endpoints[0] > endpoints[1] || selector < 3;
                for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++)
                    Check(mask.Contains(new Point(x, y)) == opaque, "BC1 endpoints/selector");
                Array.Clear(bytes, 0, bytes.Length);
                Check(mask.Contains(Point.Empty) == opaque, "Mask changed after payload mutation");
                foreach (var point in new[] { new Point(-1, 0), new Point(0, -1), new Point(4, 0), new Point(0, 4), new Point(int.MaxValue, int.MaxValue) })
                    Check(!mask.Contains(point), "Mask accepted out-of-frame coordinate");
            }
        var mixed = Block(0, 0, 228); // each row: opaque black selectors 0,1,2, then transparent 3.
        var partial = GameUiButtonHitMask.FromBc1(new Size(3, 5), mixed.Concat(mixed).ToArray());
        for (int y = 0; y < 5; y++) for (int x = 0; x < 3; x++) Check(partial.Contains(new Point(x, y)), "Logical edge clipped opaque black");
        Check(!partial.Contains(new Point(3, 4)) && !partial.Contains(new Point(2, 5)), "BC1 padding is clickable");
        Reject<ArgumentNullException>(() => GameUiButtonHitMask.FromBc1(new Size(4, 4), null));
        foreach (var size in new[] { Size.Empty, new Size(-1, 4), new Size(4, -1) })
            Reject<ArgumentOutOfRangeException>(() => GameUiButtonHitMask.FromBc1(size, new byte[8]));
        foreach (int length in new[] { 0, 7, 9, 16 })
            Reject<ArgumentException>(() => GameUiButtonHitMask.FromBc1(new Size(4, 4), new byte[length]));
        Reject<ArgumentException>(() => GameUiButtonHitMask.FromBc1(new Size(int.MaxValue, int.MaxValue), new byte[8]));
        var readerField = typeof(MirLibrary).GetField("_BReader", BindingFlags.Instance | BindingFlags.NonPublic);
        var packageReader = (BinaryReader)readerField.GetValue(library);
        foreach (var role in Roles)
        {
            var states = GameUiOfficialV1Catalog.Buttons[role]; var image = library.GetImage(states.Normal);
            Check(!image.ImageValid, "Mask check must run before GPU upload: " + role);
            long before = packageReader.BaseStream.Position;
            var mask = library.ReadButtonHitMask(states.Normal);
            Check(packageReader.BaseStream.Position == before && !image.ImageValid, "CPU mask changed reader/GPU state");
            Check(mask.Size == new Size(image.Width, image.Height), "Logical mask size");
            var oracle = ReferenceAlpha(library, states.Normal);
            foreach (int frame in new[] { states.Hover, states.Pressed, states.Disabled })
                Check(ReferenceAlpha(library, frame).SequenceEqual(oracle), "Visual state alpha differs from stable normal mask: " + role);
            using (var png = pngDirectory == null ? null : new Bitmap(Path.Combine(pngDirectory, role + ".png")))
            {
                if (png != null) Check(png.Size == mask.Size, "PNG fixture size: " + role);
                int opaque = 0, transparent = 0;
                for (int y = 0; y < mask.Size.Height; y++) for (int x = 0; x < mask.Size.Width; x++)
                {
                    bool expected = oracle[y * mask.Size.Width + x];
                    Check(mask.Contains(new Point(x, y)) == expected, "Package alpha: " + role);
                    if (png != null) { Check((png.GetPixel(x, y).A != 0) == expected, "Source PNG/package alpha: " + role); referencePixels++; }
                    if (expected) opaque++; else transparent++;
                }
                Check(opaque > 0 && transparent > 0, "Package button lacks visible/transparent samples: " + role);
            }
        }
        // Missing/truncated payloads must fail with the original reader cursor restored.
        string truncated = Path.Combine(output, "truncated-button.zl"); File.WriteAllBytes(truncated, new byte[4]);
        try
        {
            using (var bad = new MirLibrary(truncated) { Loaded = true, Images = new[] { library.GetImage(GameUiOfficialV1Catalog.Buttons[Roles[0]].Normal) } })
            {
                var reader = (BinaryReader)readerField.GetValue(bad); long before = reader.BaseStream.Position;
                Reject<EndOfStreamException>(() => bad.ReadButtonHitMask(0));
                Check(reader.BaseStream.Position == before, "Failed read moved cursor");
                // This fixture borrows metadata only; its owner still owns all textures.
                bad.Images = new MirImage[0];
            }
        }
        finally { File.Delete(truncated); }
        Reject<ArgumentException>(() => library.ReadButtonHitMask(-1));
        File.WriteAllText(Path.Combine(output, "button-hitmask-cpu.txt"), "synthetic_and_package_checks=" + checks + "\nsource_png_pixels=" + referencePixels + "\nreference_pngs=" + (pngDirectory == null ? "not_supplied" : "8") + "\n", Encoding.UTF8);
        Console.WriteLine("CPU button masks: " + checks + " checks; " + referencePixels + " source PNG alpha comparisons; no GPU uploads.");
    }

    internal static void RunControls(DXControl root, GameUiVerifiedPackage package)
    {
        foreach (var role in Roles)
        {
            var states = GameUiOfficialV1Catalog.Buttons[role]; var oracle = ReferenceAlpha(package.Library, states.Normal);
            using (var button = new DXButton { Parent = root, Location = new Point(100, 100) })
            {
                button.BindButtonImages(package.Library, states, normalFrameHitTest: true);
                var saved = button.NormalFrameHitMask;
                foreach (int state in Enumerable.Range(0, 4))
                {
                    DXControl.MouseControl = state == 1 ? button : null; button.Pressed = state == 2; button.Enabled = state != 3;
                    for (int y = 0; y < button.Size.Height; y++) for (int x = 0; x < button.Size.Width; x++)
                        Check(button.IsMouseOver(new Point(button.DisplayArea.X + x, button.DisplayArea.Y + y)) == oracle[y * button.Size.Width + x], "Hit changed with visual state: " + role);
                    Check(ReferenceEquals(saved, button.NormalFrameHitMask), "Input rebuilt normal mask");
                }
                button.Enabled = true; button.Pressed = false; DXControl.MouseControl = null;
                int visible = Array.FindIndex(oracle, p => p); var solid = new Point(button.DisplayArea.X + visible % button.Size.Width, button.DisplayArea.Y + visible / button.Size.Width);
                button.Visible = false; Check(!button.IsMouseOver(solid), "Hidden button hit"); button.Visible = true;
                Check(button.IsMouseOver(solid), "Restored button hit");
                button.PassThrough = true; Check(!button.IsMouseOver(solid), "PassThrough semantics changed"); button.PassThrough = false;
                Reject<ArgumentException>(() => button.BindButtonImages(package.Library, new ButtonImageStates(states.Normal, int.MaxValue, states.Normal, states.Normal), true));
                Check(ReferenceEquals(saved, button.NormalFrameHitMask) && ReferenceEquals(states, button.ImageStates), "Invalid rebind changed original binding");
                button.BindButtonImages(package.Library, states); Check(button.NormalFrameHitMask == null, "Default binding enabled pixel hits");
                for (int y = 0; y < button.Size.Height; y++) for (int x = 0; x < button.Size.Width; x++)
                    Check(button.IsMouseOver(new Point(button.DisplayArea.X + x, button.DisplayArea.Y + y)), "Default rectangular hit changed");
                button.BindButtonImages(package.Library, states, true); button.BindImage(null, -1);
                Check(button.NormalFrameHitMask == null && button.ImageStates == null, "Legacy binding retained mask");
            }
        }
        // A transparent top corner must route to the lower original control exactly once.
        var centerStates = GameUiOfficialV1Catalog.Buttons[Roles[0]];
        var centerAlpha = ReferenceAlpha(package.Library, centerStates.Normal);
        using (var group = new DXControl { Parent = root, Location = new Point(200, 200), Size = new Size(58, 58), PassThrough = true })
        using (var lower = new DXButton { Parent = group, Size = group.Size, Sound = Library.SoundIndex.None })
        using (var upper = new DXButton { Parent = group, Sound = Library.SoundIndex.None })
        {
            upper.BindButtonImages(package.Library, centerStates, true);
            int lowerClicks = 0, upperClicks = 0;
            lower.MouseClick += (s,e) => lowerClicks++; upper.MouseClick += (s,e) => upperClicks++;
            for (int y = 0; y < upper.Size.Height; y++) for (int x = 0; x < upper.Size.Width; x++)
            {
                var move = new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.None, 0, group.DisplayArea.X + x, group.DisplayArea.Y + y, 0);
                group.OnMouseMove(move);
                DXControl expected = centerAlpha[y * upper.Size.Width + x] ? upper : lower;
                Check(ReferenceEquals(DXControl.MouseControl, expected), "Transparent top button swallowed underlying hover");
                int before = lowerClicks + upperClicks;
                DXControl.MouseControl.OnMouseClick(new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 1, move.X, move.Y, 0));
                Check(lowerClicks + upperClicks == before + 1, "Overlapping buttons dispatched duplicate clicks");
            }
            Check(lowerClicks > 0 && upperClicks > 0, "Both alpha routes must receive clicks");
        }
        // Preserve the optional mode when an already-bound original HUD is restored.
        using (var hud = new Client.Scenes.Views.MainPanel { Parent = root })
        {
            var characterButton = (DXButton)typeof(Client.Scenes.Views.MainPanel).GetField("CharacterButton", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(hud);
            characterButton.BindButtonImages(package.Library, centerStates, true);
            var before = new Rectangle(characterButton.Location, characterButton.Size);
            hud.UseGameUiHud(GameUiHudLayout.OfficialV1);
            characterButton.BindImage(null, -1);
            hud.UseGameUiHud(null);
            Check(characterButton.NormalFrameHitMask != null && ReferenceEquals(characterButton.ImageStates, centerStates), "HUD snapshot lost existing mask binding");
            Check(new Rectangle(characterButton.Location, characterButton.Size) == before, "HUD snapshot lost original geometry");
            for (int y = 0; y < before.Height; y++) for (int x = 0; x < before.Width; x++)
                Check(characterButton.NormalFrameHitMask.Contains(new Point(x, y)) == centerAlpha[y * before.Width + x], "HUD snapshot changed original alpha");
        }
        DXControl.MouseControl = null;
        Console.WriteLine("Button mask checks including real controls: " + checks + "; hover, pressed, disabled, hide/restore, PassThrough, invalid rebind and Legacy reset.");
    }
}
