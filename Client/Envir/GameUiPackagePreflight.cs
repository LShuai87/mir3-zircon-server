using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace Client.Envir
{
    // Values are supplied by a compiled catalog, never by runtime JSON.
    public sealed class GameUiPackageFrame
    {
        public string Id { get; }
        public int Index { get; }
        public Size Size { get; }
        public Point Offset { get; }
        public string DataSha256 { get; }
        public GameUiPackageFrame(string id, int index, Size size, Point offset, string dataSha256)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Frame ID is required.", nameof(id));
            if (index < 0 || index >= GameUiPackageManifest.MaxSlots) throw new ArgumentOutOfRangeException(nameof(index));
            if (size.Width < 1 || size.Height < 1 || size.Width > 4096 || size.Height > 4096)
                throw new ArgumentOutOfRangeException(nameof(size));
            if (offset.X < short.MinValue || offset.X > short.MaxValue || offset.Y < short.MinValue || offset.Y > short.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(offset));
            Id = id; Index = index; Size = size; Offset = offset;
            DataSha256 = GameUiPackageManifest.NormalizeHash(dataSha256);
        }
        internal int DataLength => ((Size.Width + 3) / 4) * ((Size.Height + 3) / 4) * 8;
    }

    public sealed class GameUiPackageManifest
    {
        public const int MaxSlots = 65536;
        public const int MaxFrames = 4096;
        public const long MaxPackageBytes = 64L * 1024 * 1024;
        public string PackageSha256 { get; }
        public IReadOnlyDictionary<string, GameUiPackageFrame> Frames { get; }
        internal IReadOnlyDictionary<int, GameUiPackageFrame> ByIndex { get; }
        public GameUiPackageManifest(string packageSha256, IEnumerable<GameUiPackageFrame> frames,
            IEnumerable<string> requiredIds)
        {
            PackageSha256 = NormalizeHash(packageSha256);
            if (frames == null) throw new ArgumentNullException(nameof(frames));
            if (requiredIds == null) throw new ArgumentNullException(nameof(requiredIds));
            var ids = new Dictionary<string, GameUiPackageFrame>(StringComparer.Ordinal);
            var indices = new Dictionary<int, GameUiPackageFrame>();
            foreach (GameUiPackageFrame frame in frames)
            {
                if (frame == null || ids.ContainsKey(frame.Id) || indices.ContainsKey(frame.Index))
                    throw new ArgumentException("Frames must have unique IDs and indexes.", nameof(frames));
                if (ids.Count == MaxFrames) throw new ArgumentException("Too many frames.", nameof(frames));
                ids.Add(frame.Id, frame); indices.Add(frame.Index, frame);
            }
            if (ids.Count == 0 || ids.Values.Sum(f => (long)f.DataLength) > MaxPackageBytes)
                throw new ArgumentException("Frame data exceeds the package budget or is empty.", nameof(frames));
            var required = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in requiredIds)
                if (string.IsNullOrWhiteSpace(id) || !required.Add(id) || required.Count > MaxFrames)
                    throw new ArgumentException("Required IDs must be nonempty and unique.", nameof(requiredIds));
            if (!required.SetEquals(ids.Keys)) throw new ArgumentException("Compiled semantic roles do not match frames.", nameof(requiredIds));
            Frames = new ReadOnlyDictionary<string, GameUiPackageFrame>(ids);
            ByIndex = new ReadOnlyDictionary<int, GameUiPackageFrame>(indices);
        }
        internal static string NormalizeHash(string value)
        {
            if (value == null || value.Length != 64 || value.Any(c => !Uri.IsHexDigit(c)))
                throw new ArgumentException("A SHA256 value must contain exactly 64 hex digits.");
            return value.ToLowerInvariant();
        }
    }

    // Holds a read-only file lease from hash verification until all controls release
    // the library. Windows denies write/delete/replacement for the whole lifetime.
    public sealed class GameUiVerifiedPackage : IDisposable
    {
        private FileStream lease;
        private readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        private MirLibrary library;
        public GameUiPackageManifest Manifest { get; }
        public string FilePath { get; }
        public bool IsDisposed => lease == null;
        public MirLibrary Library
        {
            get
            {
                CheckOwner();
                if (library == null) throw new InvalidOperationException("GPU preflight has not completed.");
                return library;
            }
        }
        private void CheckOwner()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThread)
                throw new InvalidOperationException("Package ownership belongs to the startup/render thread.");
            if (IsDisposed) throw new ObjectDisposedException(nameof(GameUiVerifiedPackage));
        }
        private GameUiVerifiedPackage(string filePath, GameUiPackageManifest manifest, FileStream lease)
        { FilePath = filePath; Manifest = manifest; this.lease = lease; }

        public static GameUiVerifiedPackage Verify(string filePath, GameUiPackageManifest manifest)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("Package path is required.", nameof(filePath));
            string fullPath = Path.GetFullPath(filePath);
            FileStream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                Validate(stream, manifest);
                return new GameUiVerifiedPackage(fullPath, manifest, stream);
            }
            catch { stream.Dispose(); throw; }
        }
        // Startup can keep Legacy if this fails. No scene receives a partial library.
        public static bool TryLoad(string filePath, GameUiPackageManifest manifest,
            out GameUiVerifiedPackage package, out string diagnostic)
        {
            package = null;
            diagnostic = null;
            try
            {
                var verified = Verify(filePath, manifest);
                verified.UploadAllFrames();
                package = verified;
                return true;
            }
            catch (Exception error) when (error is IOException || error is InvalidDataException || error is UnauthorizedAccessException ||
                error is ArgumentException || error is InvalidOperationException ||
                error is NotSupportedException || error is System.Security.SecurityException ||
                error is SlimDX.SlimDXException)
            {
                diagnostic = error.GetType().Name + ": " + error.Message;
                return false;
            }
        }
        private static string Hash(Stream stream)
        {
            using (SHA256 hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        private static void Validate(FileStream stream, GameUiPackageManifest manifest)
        {
            if (stream.Length < 9 || stream.Length > GameUiPackageManifest.MaxPackageBytes)
                throw new InvalidDataException("Package length is outside the supported budget.");
            if (Hash(stream) != manifest.PackageSha256) throw new InvalidDataException("Package SHA256 mismatch.");
            stream.Position = 0;
            var ranges = new List<Tuple<GameUiPackageFrame, int>>();
            using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
            {
                int headerBytes = reader.ReadInt32();
                long headerEnd = 4L + headerBytes;
                if (headerBytes < 5 || headerEnd > stream.Length) throw new InvalidDataException("Invalid Zl header length.");
                int count = reader.ReadInt32();
                if (count < 1 || count > GameUiPackageManifest.MaxSlots || headerBytes < 4L + count)
                    throw new InvalidDataException("Invalid Zl slot count.");
                long expectedPosition = headerEnd;
                for (int index = 0; index < count; index++)
                {
                    if (stream.Position >= headerEnd) throw new InvalidDataException("Truncated Zl slot table.");
                    byte present = reader.ReadByte();
                    if (present > 1) throw new InvalidDataException("Invalid Zl presence flag.");
                    GameUiPackageFrame frame;
                    bool bound = manifest.ByIndex.TryGetValue(index, out frame);
                    if (present == 0)
                    {
                        if (bound) throw new InvalidDataException("Required frame is absent: " + frame.Id);
                        continue;
                    }
                    if (!bound) throw new InvalidDataException("Unbound Zl frame: " + index);
                    if (headerEnd - stream.Position < 25) throw new InvalidDataException("Truncated Zl frame header.");
                    int position = reader.ReadInt32();
                    var size = new Size(reader.ReadInt16(), reader.ReadInt16());
                    var offset = new Point(reader.ReadInt16(), reader.ReadInt16());
                    byte[] auxiliary = reader.ReadBytes(13);
                    if (size != frame.Size || offset != frame.Offset) throw new InvalidDataException("Frame geometry mismatch: " + frame.Id);
                    if (auxiliary.Any(b => b != 0)) throw new InvalidDataException("Shadow/overlay frames are not supported: " + frame.Id);
                    if (position != expectedPosition || expectedPosition + frame.DataLength > stream.Length)
                        throw new InvalidDataException("Frame data is truncated, overlapping or out of order: " + frame.Id);
                    ranges.Add(Tuple.Create(frame, position));
                    expectedPosition += frame.DataLength;
                }
                if (stream.Position != headerEnd || expectedPosition != stream.Length || ranges.Count != manifest.Frames.Count)
                    throw new InvalidDataException("Zl table, payload or required frame count mismatch.");
                foreach (var range in ranges)
                {
                    stream.Position = range.Item2;
                    byte[] data = reader.ReadBytes(range.Item1.DataLength);
                    using (var bytes = new MemoryStream(data, false))
                        if (data.Length != range.Item1.DataLength || Hash(bytes) != range.Item1.DataSha256)
                            throw new InvalidDataException("Frame SHA256 mismatch: " + range.Item1.Id);
                }
            }
        }

        // Idempotent; all frames must upload before any scene receives this library.
        // A failed upload disposes the entire owner, including earlier frame textures.
        public void UploadAllFrames()
        {
            CheckOwner();
            if (library != null) return;
            MirLibrary pending = null;
            try
            {
                if (DXManager.Device == null || DXManager.Device.Disposed)
                    throw new InvalidOperationException("The D3D9 device is not ready.");
                pending = new MirLibrary(FilePath);
                pending.ReadLibrary();
                foreach (GameUiPackageFrame frame in Manifest.Frames.Values)
                {
                    MirImage image = pending.CreateImage(frame.Index, ImageType.Image);
                    if (image == null || !image.ImageValid || image.Image == null || image.Image.Disposed)
                        throw new InvalidDataException("GPU frame upload failed: " + frame.Id);
                }
                library = pending;
            }
            catch
            {
                try { ReleaseLibrary(pending); }
                finally { Dispose(); }
                throw;
            }
        }
        // MirLibrary.Dispose clears frame metadata but does not dispose GPU textures.
        // This package owns its frames and must explicitly release each texture first.
        private static void ReleaseLibrary(MirLibrary owned)
        {
            if (owned == null) return;
            Exception failure = null;
            if (owned.Images != null)
                foreach (MirImage image in owned.Images)
                    try { image?.DisposeTexture(); }
                    catch (Exception error) { if (failure == null) failure = error; }
            try { owned.Dispose(); }
            finally
            {
                if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
        public void Dispose()
        {
            if (IsDisposed) return;
            CheckOwner();
            try { ReleaseLibrary(library); }
            finally { library = null; lease.Dispose(); lease = null; }
        }
    }
}
