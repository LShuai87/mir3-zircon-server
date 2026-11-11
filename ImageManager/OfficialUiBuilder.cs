using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace ImageManager
{
    /// <summary>Headless reference conversion. Reference libraries never qualify as runtime UI assets.</summary>
    public static class OfficialUiBuilder
    {
        public sealed class Manifest
        {
            public int schemaVersion;
            public string kind;
            public bool alphaVerified;
            public List<SourceLibrary> libraries;
        }
        public sealed class SourceLibrary
        {
            public string name, mixSha256, mizSha256;
            public int slots, present;
            public List<SourceImage> items;
        }
        public sealed class SourceImage
        {
            public int index, width, height, offsetX, offsetY;
            public string png, pngSha256, rgbaSha256, alphaMode;
            public bool alphaVerified;
        }
        private const long MaxLibraryBytes = 512L * 1024 * 1024;
        private static readonly Regex NamePattern = new Regex(@"\A[A-Za-z][A-Za-z0-9_-]{0,63}\z");
        private static readonly Regex HashPattern = new Regex(@"\A[0-9a-f]{64}\z");

        public static string Hash(byte[] bytes)
        {
            using (SHA256 hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        private static bool IsWithin(string path, string parent)
        {
            return path.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static void RejectReparsePoints(string path)
        {
            for (string current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                if ((Directory.Exists(current) || File.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Reparse points are not supported in conversion paths: " + current);
        }

        public static void Build(string inputDirectory, string outputDirectory, string manifestPath, string mode)
        {
            if (mode != "reference")
                throw new ArgumentException("Only reference conversion is enabled. Runtime packaging requires the frozen semantic manifest and layout.");
            string input = Path.GetFullPath(inputDirectory).TrimEnd(Path.DirectorySeparatorChar);
            string output = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar);
            if (!Directory.Exists(input)) throw new DirectoryNotFoundException(input);
            if (IsWithin(input, output) || IsWithin(output, input))
                throw new InvalidDataException("Input and output must be disjoint.");
            if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output already exists: " + output);
            RejectReparsePoints(input); RejectReparsePoints(output);
            string manifestFile = Path.GetFullPath(manifestPath);
            if (!IsWithin(manifestFile, input)) throw new InvalidDataException("Manifest must be inside the decoded input.");
            RejectReparsePoints(manifestFile);
            if (new FileInfo(manifestFile).Length > 16 * 1024 * 1024) throw new InvalidDataException("Manifest too large.");
            byte[] manifestBytes = File.ReadAllBytes(manifestFile);
            var serializer = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024, RecursionLimit = 64 };
            var manifest = serializer.Deserialize<Manifest>(Encoding.UTF8.GetString(manifestBytes).TrimStart('\uFEFF'));
            ValidateManifest(manifest);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            string staging = output + ".staging-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(staging);
            try
            {
                Directory.CreateDirectory(Path.Combine(staging, "Reference"));
                var libraries = new List<object>();
                foreach (SourceLibrary library in manifest.libraries)
                {
                    var target = new Mir3Library { Images = new Mir3Image[library.slots] };
                    long size = checked(8L + library.slots + library.present * (long)Mir3Image.HeaderSize);
                    for (int index = 0; index < library.slots; index++)
                    {
                        SourceImage item = library.items[index];
                        if (item == null) continue;
                        try
                        {
                            if (item.index != index || item.png != library.name + "/" + index + ".png" ||
                                item.width <= 0 || item.width > 8192 || item.height <= 0 || item.height > 8192 ||
                                (long)item.width * item.height > 16777216 || item.offsetX < short.MinValue || item.offsetX > short.MaxValue ||
                                item.offsetY < short.MinValue || item.offsetY > short.MaxValue ||
                                item.alphaMode != "opaque" || item.alphaVerified || !HashPattern.IsMatch(item.pngSha256 ?? ""))
                                throw new InvalidDataException("Invalid indexed image metadata or alpha policy.");
                            string png = Path.Combine(input, item.png.Replace('/', Path.DirectorySeparatorChar));
                            RejectReparsePoints(png);
                            if (new FileInfo(png).Length > 64 * 1024 * 1024) throw new InvalidDataException("PNG too large.");
                            byte[] bytes = File.ReadAllBytes(png);
                            if (Hash(bytes) != item.pngSha256) throw new InvalidDataException("PNG SHA256 mismatch.");
                            using (var stream = new MemoryStream(bytes, false))
                            using (var bitmap = new Bitmap(stream))
                            {
                                if (bitmap.Width != item.width || bitmap.Height != item.height)
                                    throw new InvalidDataException("PNG dimensions do not match manifest.");
                                size = checked(size + ((item.width + 3L) / 4) * ((item.height + 3L) / 4) * 8);
                                if (size > MaxLibraryBytes) throw new InvalidDataException("Zl exceeds reference size limit.");
                                target.Images[index] = new Mir3Image
                                {
                                    Width = (short)item.width, Height = (short)item.height,
                                    OffSetX = (short)item.offsetX, OffSetY = (short)item.offsetY,
                                    Data = MImage.GetBytes(bitmap, Dxt1AlphaMode.Opaque)
                                };
                            }
                        }
                        catch (Exception error) when (error is ArgumentException || error is IOException || error is InvalidDataException || error is OverflowException)
                        {
                            throw new InvalidDataException(library.name + "[" + index + "]: " + error.Message, error);
                        }
                    }
                    string relative = "Reference/" + library.name + ".Zl";
                    string destination = Path.Combine(staging, relative.Replace('/', Path.DirectorySeparatorChar));
                    target.Save(destination);
                    if (new FileInfo(destination).Length != size) throw new InvalidDataException("Unexpected Zl size: " + library.name);
                    libraries.Add(new { library.name, library.slots, library.present, library.mixSha256, library.mizSha256,
                                        path = relative, sha256 = Hash(File.ReadAllBytes(destination)), alphaMode = "opaque", alphaVerified = false });
                }
                string report = serializer.Serialize(new { schemaVersion = 1, kind = "official-ui-reference-zl", alphaVerified = false,
                    sourceManifestSha256 = Hash(manifestBytes), libraries });
                File.WriteAllText(Path.Combine(staging, "reference-manifest.json"), report + "\n", new UTF8Encoding(false));
                Directory.Move(staging, output);
            }
            finally
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
            }
        }

        private static void ValidateManifest(Manifest manifest)
        {
            if (manifest == null || manifest.schemaVersion != 1 || manifest.kind != "official-ui-decoded-reference" ||
                manifest.alphaVerified || manifest.libraries == null || manifest.libraries.Count == 0 || manifest.libraries.Count > 32)
                throw new InvalidDataException("Invalid decoded reference manifest.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SourceLibrary library in manifest.libraries)
            {
                if (library == null || !NamePattern.IsMatch(library.name ?? "") || !names.Add(library.name) ||
                    !HashPattern.IsMatch(library.mixSha256 ?? "") || !HashPattern.IsMatch(library.mizSha256 ?? "") ||
                    library.slots < 0 || library.slots > 100000 || library.items == null || library.slots != library.items.Count ||
                    library.present != library.items.Count(item => item != null))
                    throw new InvalidDataException("Invalid library identity, counts or source hashes.");
            }
        }
    }
}
