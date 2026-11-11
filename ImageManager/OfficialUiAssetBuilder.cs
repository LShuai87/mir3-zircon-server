using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace ImageManager
{
    /// <summary>Packages composed art for integration testing; never grants production acceptance.</summary>
    public static class OfficialUiAssetBuilder
    {
        public sealed class Manifest
        {
            public int schemaVersion;
            public string kind, sourceManifestSha256, recipeSha256;
            public bool runtimeReady, productionBindingAllowed, alphaVerified;
            public List<Asset> assets;
            public Dictionary<string, Dictionary<string, string>> buttons;
            public Dictionary<string, Grid> grids;
            public List<Composition> compositions;
        }
        public sealed class Asset
        {
            public string role, png, pngSha256, rgbaSha256, sourceLibrary, sourcePngSha256;
            public string offsetMode, alphaMode, blendMode, sourceRole, state;
            public int outputIndex, sourceIndex;
            public int[] logicalSize, textureSize, sourceOffset, outputOffset, pivot;
        }
        public sealed class Grid
        {
            public int columns, rows;
            public int[] cellSize, step, origin, size, sourceArea, sourceCell;
            public List<int[]> cells;
        }
        public sealed class Composition
        {
            public string id, offsetDerivation;
            public List<Layer> layers;
        }
        public sealed class Layer { public string role, fill; public int[] offset; }

        private static readonly Regex Role = new Regex(@"\A[a-z][a-z0-9-]{0,79}\z");
        private static readonly Regex Hash = new Regex(@"\A[0-9a-f]{64}\z");
        private static void Require(bool condition, string reason)
        {
            if (!condition) throw new InvalidDataException(reason);
        }
        private static bool Pair(int[] value, int x, int y)
        {
            return value != null && value.Length == 2 && value[0] == x && value[1] == y;
        }
        private static bool Within(string path, string parent)
        {
            return path.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        private static void Ordinary(string path)
        {
            for (string part = path; !string.IsNullOrEmpty(part); part = Path.GetDirectoryName(part))
                if ((File.Exists(part) || Directory.Exists(part)) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Reparse point in packaging path: " + part);
        }
        private static byte[] Read(string path, long max)
        {
            Ordinary(path);
            Require(new FileInfo(path).Length <= max, "Input exceeds size limit: " + path);
            return File.ReadAllBytes(path);
        }
        private static void Validate(Manifest m)
        {
            Require(m != null && m.schemaVersion == 1 && m.kind == "official-ui-composed-art" &&
                !m.runtimeReady && !m.productionBindingAllowed && !m.alphaVerified, "Expected unaccepted composed-art manifest.");
            Require(Hash.IsMatch(m.recipeSha256 ?? "") && Hash.IsMatch(m.sourceManifestSha256 ?? ""), "Missing provenance hashes.");
            Require(m.assets != null && m.assets.Count > 0 && m.assets.Count <= 1000, "Invalid asset count.");
            var assets = new Dictionary<string, Asset>(StringComparer.OrdinalIgnoreCase);
            long pixels = 0;
            for (int index = 0; index < m.assets.Count; index++)
            {
                Asset a = m.assets[index];
                Require(a != null && Role.IsMatch(a.role ?? "") && !assets.ContainsKey(a.role) && a.outputIndex == index,
                    "Invalid or duplicate asset role/index.");
                Require(a.png == "Assets/" + a.role + ".png" && Hash.IsMatch(a.pngSha256 ?? "") && Hash.IsMatch(a.rgbaSha256 ?? "") &&
                    Hash.IsMatch(a.sourcePngSha256 ?? "") && Role.IsMatch(a.sourceRole ?? "") && a.sourceLibrary == "UI" &&
                    a.sourceIndex >= 0 && a.sourceIndex < 100000, "Invalid asset provenance/path.");
                Require(a.logicalSize != null && a.logicalSize.Length == 2 && a.logicalSize.All(x => x > 0 && x <= 8192), "Invalid dimensions.");
                int w = a.logicalSize[0], h = a.logicalSize[1];
                pixels = checked(pixels + (long)w * h);
                Require((long)w * h <= 16777216 && pixels <= 67108864 && Pair(a.textureSize, (w+3)/4*4, (h+3)/4*4), "Invalid texture dimensions/budget.");
                Require(a.sourceOffset != null && a.sourceOffset.Length == 2 && a.sourceOffset.All(x => x >= short.MinValue && x <= short.MaxValue) &&
                    Pair(a.outputOffset, 0, 0) && Pair(a.pivot, 0, 0) && a.offsetMode == "baked" && a.alphaMode == "BinaryMask" && a.blendMode == "Normal",
                    "Output offset/alpha/blend must be explicit.");
                Require(a.state == null || new[] { "hover", "pressed", "disabled" }.Contains(a.state), "Invalid state.");
                assets.Add(a.role, a);
            }
            Require(m.buttons != null && m.grids != null && m.compositions != null, "Missing composition maps.");
            var stateRoles = new HashSet<string>();
            foreach (var button in m.buttons)
            {
                Require(assets.ContainsKey(button.Key) && assets[button.Key].state == null && button.Value != null &&
                    button.Value.Count == 4 && new[] { "normal", "hover", "pressed", "disabled" }.All(button.Value.ContainsKey), "Invalid button states.");
                Asset normal = assets[button.Key];
                Require(button.Value["normal"] == button.Key, "Button normal binding mismatch.");
                foreach (string state in new[] { "hover", "pressed", "disabled" })
                {
                    string role = button.Value[state];
                    Require(role == button.Key + "-" + state && assets.ContainsKey(role) && stateRoles.Add(role), "Duplicate/missing state frame.");
                    Asset a = assets[role];
                    Require(a.state == state && a.sourceRole == normal.sourceRole && a.sourceLibrary == normal.sourceLibrary &&
                        a.sourceIndex >= 0 && Hash.IsMatch(a.sourcePngSha256 ?? "") && a.logicalSize.SequenceEqual(normal.logicalSize), "State provenance mismatch.");
                }
            }
            Require(m.assets.Where(a => a.state != null).All(a => stateRoles.Contains(a.role)), "Unbound state frame.");
            foreach (var entry in m.grids)
            {
                Grid g = entry.Value;
                bool inventory = entry.Key == "inventory", quickbar = entry.Key == "quickbar-base";
                Require((inventory || quickbar) && assets.ContainsKey(entry.Key) && g != null, "Unknown grid.");
                bool nativeBelt = quickbar && Pair(g.cellSize, 41, 40);
                int width = nativeBelt ? 41 : 36, height = nativeBelt ? 40 : 36;
                int stepX = nativeBelt ? 41 : 35, stepY = nativeBelt ? 40 : 35;
                Require(g.columns == (inventory ? 7 : 10) && g.rows == (inventory ? 7 : 1) && Pair(g.cellSize, width, height) && Pair(g.step, stepX, stepY) &&
                    g.origin != null && g.origin.Length == 2 && g.origin.All(x => x >= 0) && Pair(g.size, (g.columns-1)*stepX+width, (g.rows-1)*stepY+height) &&
                    g.cells != null && g.cells.Count == g.columns*g.rows, "Grid must preserve project cell count/geometry.");
                Asset a = assets[entry.Key];
                for (int i=0; i<g.cells.Count; i++)
                {
                    int[] c = g.cells[i];
                    int x = g.origin[0] + i%g.columns*stepX, y = g.origin[1] + i/g.columns*stepY;
                    Require(c != null && c.SequenceEqual(new[] { x, y, width, height }) && x+width <= a.logicalSize[0] && y+height <= a.logicalSize[1], "Invalid cell rectangle.");
                }
            }
            Require(m.compositions.Count <= 1, "Duplicate composition.");
            foreach (Composition c in m.compositions)
            {
                Require(c != null && c.id == "hud-orb" && c.layers != null && c.layers.Count == 3 &&
                    c.layers.Select(l => l == null ? null : l.role).SequenceEqual(new[] { "hud-orb-shell", "hud-hp-fill", "hud-mp-fill" }) &&
                    c.layers.All(l => assets.ContainsKey(l.role)), "Invalid orb layers.");
                Asset shell = assets[c.layers[0].role];
                for (int i=0; i<3; i++)
                {
                    Layer layer = c.layers[i]; Asset a = assets[layer.role];
                    Require(Pair(layer.offset, a.sourceOffset[0]-shell.sourceOffset[0], a.sourceOffset[1]-shell.sourceOffset[1]) &&
                        layer.fill == (i == 0 ? null : "vertical-bottom"), "Orb must use one original anchor and bottom fill.");
                    Require(layer.offset[0] >= 0 && layer.offset[1] >= 0 && layer.offset[0]+a.logicalSize[0] <= shell.logicalSize[0] &&
                        layer.offset[1]+a.logicalSize[1] <= shell.logicalSize[1], "Orb layer outside shell.");
                }
            }
        }
        private static byte[] Rgba(Bitmap bitmap)
        {
            var result = new byte[checked(bitmap.Width*bitmap.Height*4)];
            int n = 0;
            // Explicit channel order is shared with Pillow's RGBA hash, independent of GDI stride/BGRA storage.
            for (int y=0; y<bitmap.Height; y++) for (int x=0; x<bitmap.Width; x++)
            {
                Color c = bitmap.GetPixel(x,y);
                Require(c.A == 0 || c.A == 255, "Fractional alpha is unsupported.");
                result[n++] = c.R; result[n++] = c.G; result[n++] = c.B; result[n++] = c.A;
            }
            return result;
        }
        private static int BigEndian(byte[] bytes, int offset)
        {
            return checked((int)(((uint)bytes[offset]<<24) | ((uint)bytes[offset+1]<<16) | ((uint)bytes[offset+2]<<8) | bytes[offset+3]));
        }
        public static void Build(string inputDirectory, string outputDirectory, string manifestPath)
        {
            string input = Path.GetFullPath(inputDirectory).TrimEnd(Path.DirectorySeparatorChar);
            string output = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar);
            string file = Path.GetFullPath(manifestPath);
            Ordinary(input); Ordinary(output); Ordinary(file);
            Require(Directory.Exists(input) && Within(file, input) && !Within(input, output) && !Within(output, input), "Input/output/manifest paths overlap or are invalid.");
            Require(!Directory.Exists(output) && !File.Exists(output), "Output must be new.");
            byte[] manifestBytes = Read(file, 8*1024*1024);
            var serializer = new JavaScriptSerializer { MaxJsonLength = 8*1024*1024, RecursionLimit = 64 };
            string text = Encoding.UTF8.GetString(manifestBytes).TrimStart('\uFEFF');
            var flags = serializer.Deserialize<Dictionary<string, object>>(text);
            Require(flags != null && new[] { "runtimeReady", "productionBindingAllowed", "alphaVerified" }.All(k => flags.ContainsKey(k) && flags[k] is bool && !(bool)flags[k]), "Missing or accepted runtime flags.");
            Manifest manifest = serializer.Deserialize<Manifest>(text);
            Validate(manifest);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            string staging = output + ".staging-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(staging);
            try
            {
                var target = new Mir3Library { Images = new Mir3Image[manifest.assets.Count] };
                var snapshots = new Dictionary<string, string> { {file, OfficialUiBuilder.Hash(manifestBytes)} };
                long size = checked(8L + manifest.assets.Count * (1L+Mir3Image.HeaderSize));
                foreach (Asset a in manifest.assets)
                {
                    string png = Path.Combine(input, a.png.Replace('/', Path.DirectorySeparatorChar));
                    byte[] bytes = Read(png, 64*1024*1024);
                    Require(OfficialUiBuilder.Hash(bytes) == a.pngSha256, a.role + ": PNG hash mismatch.");
                    Require(bytes.Length >= 33 && bytes.Take(8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) &&
                        BigEndian(bytes,8) == 13 && bytes[24] == 8 && bytes[25] == 6 &&
                        bytes[12] == 73 && bytes[13] == 72 && bytes[14] == 68 && bytes[15] == 82 &&
                        BigEndian(bytes,16) == a.logicalSize[0] && BigEndian(bytes,20) == a.logicalSize[1], a.role + ": PNG header dimensions mismatch.");
                    snapshots.Add(png, a.pngSha256);
                    using (var stream = new MemoryStream(bytes, false))
                    using (var image = new Bitmap(stream))
                    {
                        Require(image.Width == a.logicalSize[0] && image.Height == a.logicalSize[1] && OfficialUiBuilder.Hash(Rgba(image)) == a.rgbaSha256,
                            a.role + ": RGBA mismatch.");
                        byte[] data = MImage.GetBytes(image, Dxt1AlphaMode.BinaryMask);
                        Require(data.Length == a.textureSize[0]*a.textureSize[1]/2, "Unexpected BC1 size.");
                        size = checked(size + data.Length);
                        Require(size <= 128L*1024*1024, "Composed Zl exceeds size budget.");
                        target.Images[a.outputIndex] = new Mir3Image { Width = (short)image.Width, Height = (short)image.Height, Data = data };
                    }
                }
                string zl = Path.Combine(staging, "OfficialV1.Zl");
                target.Save(zl);
                Require(new FileInfo(zl).Length == size, "Unexpected composed Zl size.");
                foreach (var snapshot in snapshots)
                    Require(OfficialUiBuilder.Hash(Read(snapshot.Key, 64*1024*1024)) == snapshot.Value, "Input changed during packaging.");
                var report = new { schemaVersion = 1, kind = "official-ui-prepared-zl", runtimeReady = false, productionBindingAllowed = false,
                    alphaVerified = false, alphaMode = "BinaryMask", sourceManifestSha256 = OfficialUiBuilder.Hash(manifestBytes),
                    manifest.recipeSha256, path = "OfficialV1.Zl", sha256 = OfficialUiBuilder.Hash(File.ReadAllBytes(zl)),
                    manifest.assets, buttons = flags["buttons"], grids = flags["grids"], compositions = flags["compositions"] };
                File.WriteAllText(Path.Combine(staging,"prepared-manifest.json"), serializer.Serialize(report)+"\n", new UTF8Encoding(false));
                Require(!Directory.Exists(output) && !File.Exists(output), "Output appeared during packaging.");
                Ordinary(output);
                Directory.Move(staging, output);
            }
            finally
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
            }
        }
    }
}
