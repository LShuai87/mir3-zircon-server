// Synthetic fixture only. No official artwork or final layout coordinates.
using System;
using System.IO;
using System.Security.Cryptography;
#if CATALOG_VERIFY
using System.Collections.Generic;
using Client.Models.GameUi.Preparatory;
#else
using ImageManager;
#endif

internal static class OfficialUiCatalogFixture
{
#if CATALOG_VERIFY
    private static int checks;
    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception(message);
    }
    private static string Hash(byte[] bytes)
    {
        using (SHA256 hash = SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
    public static int Main(string[] args)
    {
        byte[] package = File.ReadAllBytes(args[0]);
        // Access via reflection so the compiler does not fold a const-false guard.
        Check(!(bool)typeof(PreparedUiCatalog).GetField("ProductionBindingAllowed").GetValue(null), "production disabled");
        Check(Hash(package) == PreparedUiCatalog.PackageSha256, "package hash");
        Check(PreparedUiCatalog.Assets.Count == 5, "asset count");
        using (BinaryReader reader = new BinaryReader(new MemoryStream(package)))
        {
            int header = reader.ReadInt32();
            int count = reader.ReadInt32();
            Check(count == 8, "slot count including gaps");
            int seen = 0;
            for (int index = 0; index < count; index++)
            {
                if (!reader.ReadBoolean()) continue;
                int position = reader.ReadInt32();
                int width = reader.ReadInt16(), height = reader.ReadInt16();
                int ox = reader.ReadInt16(), oy = reader.ReadInt16();
                reader.ReadBytes(13);
                PreparedUiCatalog.Asset descriptor = null;
                foreach (PreparedUiCatalog.Asset asset in PreparedUiCatalog.Assets.Values)
                    if (asset.Index == index) descriptor = asset;
                Check(descriptor != null, "bound index");
                Check(descriptor.Width == width && descriptor.Height == height, "logical size");
                Check(descriptor.TextureWidth == (width + 3) / 4 * 4 && descriptor.TextureHeight == (height + 3) / 4 * 4, "padded size");
                Check(ox == (descriptor.OffsetMode == "source" ? descriptor.OffsetX : 0) && oy == (descriptor.OffsetMode == "source" ? descriptor.OffsetY : 0), "offset applied once");
                byte[] payload = new byte[descriptor.TextureWidth / 4 * (descriptor.TextureHeight / 4) * 8];
                Buffer.BlockCopy(package, position, payload, 0, payload.Length);
                Check(Hash(payload) == descriptor.DataSha256, "payload hash");
                seen++;
            }
            Check(reader.BaseStream.Position == 4 + header && seen == 5, "header consumption");
        }
        PreparedUiCatalog.Button button = PreparedUiCatalog.Buttons["fixture.close"];
        Check(PreparedUiCatalog.Assets[button.Normal].Index == 2, "normal mapping");
        Check(PreparedUiCatalog.Assets[button.Hover].Index == 4, "hover mapping");
        Check(PreparedUiCatalog.Assets[button.Pressed].Index == 5, "pressed mapping");
        Check(PreparedUiCatalog.Assets[button.Disabled].Index == 7, "disabled mapping");
        Check(PreparedUiCatalog.Assets[button.Normal].PivotX == 2 && PreparedUiCatalog.Assets[button.Normal].PivotY == 1, "pivot");
        Check(PreparedUiCatalog.Layouts.Count == 6, "six layouts");
        foreach (var layout in PreparedUiCatalog.Layouts.Values)
        {
            Check(layout.Count == 2, "layout fields");
            Check(layout["fixture.window"].Width == 200 && layout["fixture.close"].Width == 4, "layout constants");
        }
        Check(PreparedUiCatalog.Bindings["fixture.attack"].Page == "character", "binding page");
        Check(PreparedUiCatalog.Bindings["fixture.attack"].Source == "User.Stats.MinDC", "binding source");
        bool immutable = false;
        try { ((IDictionary<string, PreparedUiCatalog.Asset>)PreparedUiCatalog.Assets).Clear(); }
        catch (NotSupportedException) { immutable = true; }
        Check(immutable, "immutable catalog");
        Console.WriteLine("Compiled catalog checks passed: " + checks);
        return 0;
    }
#else
    public static int Main(string[] args)
    {
        var library = new Mir3Library { Images = new Mir3Image[8] };
        library.Images[0] = new Mir3Image { Width = 5, Height = 7, OffSetX = -3, OffSetY = 2, Data = PaddedBlocks(5, 7) };
        int[] indices = { 2, 4, 5, 7 };
        for (int state = 0; state < indices.Length; state++)
        {
            byte[] block = new byte[8];
            // c0 > c1, all color selector 0: solid opaque even when c1 is black.
            block[0] = (byte)(state + 1); block[1] = 0xF8;
            library.Images[indices[state]] = new Mir3Image { Width = 4, Height = 4, Data = block };
        }
        library.Save(args[0]);
        return 0;
    }
    private static byte[] PaddedBlocks(int width, int height)
    {
        int bw = (width + 3) / 4, bh = (height + 3) / 4;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            for (int y = 0; y < bh; y++)
                for (int x = 0; x < bw; x++)
                {
                    uint selectors = 0;
                    for (int pixel = 0; pixel < 16; pixel++)
                        if (x * 4 + pixel % 4 >= width || y * 4 + pixel / 4 >= height)
                            selectors |= 3u << (2 * pixel);
                    writer.Write((ushort)0); writer.Write((ushort)1); writer.Write(selectors);
                }
            return stream.ToArray();
        }
    }
#endif
}
