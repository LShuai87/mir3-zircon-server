using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Client;
using Client.Envir;
using SlimDX.Direct3D9;

internal static class OfficialUiCatalogTests
{
    private static int checks, uploads;
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }
    private static void Reject<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { checks++; return; }
        throw new Exception("Accepted invalid operation: " + message);
    }
    private static string Hash(byte[] data)
    {
        using (var algorithm = SHA256.Create())
            return BitConverter.ToString(algorithm.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
    }
    private static void SetDevice(Device device)
    { typeof(DXManager).GetProperty("Device").SetValue(null, device, null); }
    private static void Frames(MirLibrary library)
    {
        foreach (var frame in GameUiOfficialV1Catalog.PackageManifest.Frames.Values)
        {
            var image = library.GetImage(frame.Index);
            Check(image != null && image.ImageValid && image.Image != null && !image.Image.Disposed, frame.Id + ": missing GPU texture");
            Check(library.GetSize(frame.Index) == frame.Size && library.GetOffSet(frame.Index) == frame.Offset, frame.Id + ": geometry mismatch");
            var description = image.Image.GetLevelDescription(0);
            Check(description.Width == (frame.Size.Width + 3) / 4 * 4 && description.Height == (frame.Size.Height + 3) / 4 * 4 &&
                description.Format == Format.Dxt1, frame.Id + ": texture padding/format mismatch");
            uploads++;
        }
        foreach (var entry in GameUiOfficialV1Catalog.Buttons)
        {
            var states = entry.Value;
            var indices = new[] { states.Normal, states.Hover, states.Pressed, states.Disabled };
            var roles = new[] { entry.Key, entry.Key + "-hover", entry.Key + "-pressed", entry.Key + "-disabled" };
            Check(indices.Distinct().Count() == 4, entry.Key + ": state index alias");
            Size size = library.GetSize(indices[0]);
            for (int i = 0; i < indices.Length; i++)
            {
                Check(GameUiOfficialV1Catalog.PackageManifest.Frames[roles[i]].Index == indices[i], roles[i] + ": wrong semantic frame");
                Check(library.GetSize(indices[i]) == size && library.GetImage(indices[i]).ImageValid, roles[i] + ": invalid button geometry");
            }
        }
    }
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 1) { Console.Error.WriteLine("Expected the prepared OfficialV1.Zl path."); return 2; }
        string originalPath = Path.GetFullPath(args[0]);
        string copy = Path.Combine(Environment.CurrentDirectory, "catalog-copy.Zl");
        string damaged = Path.Combine(Environment.CurrentDirectory, "catalog-damaged.Zl");
        try
        {
            byte[] bytes = File.ReadAllBytes(originalPath);
            var manifest = GameUiOfficialV1Catalog.PackageManifest;
            Check(!GameUiOfficialV1Catalog.RuntimeReady, "Catalog unexpectedly enabled runtime activation");
            Check(Hash(bytes) == GameUiOfficialV1Catalog.PackageSha256 && manifest.PackageSha256 == GameUiOfficialV1Catalog.PackageSha256, "Prepared package hash differs from compiled catalog");
            Check(manifest.Frames.Count == 93 && GameUiOfficialV1Catalog.Buttons.Count == 19, "Incomplete compiled catalog");
            string[] movedRoles = { "npc-shop", "chat-history-frame", "chat-input-frame", "equipment-cell-frame", "hud-experience-track", "hud-experience-fill" };
            for (int i = 0; i < movedRoles.Length; i++)
                Check(manifest.Frames[movedRoles[i]].Index == 87 + i, "Shop insertion/index migration differs: " + movedRoles[i]);
            Check(manifest.Frames["npc-shop"].Size == new Size(344, 438), "Shop geometry differs");
            Check(manifest.Frames["character-main-background"].Size == new Size(412, 498), "Character page backdrop geometry differs");
            Check(manifest.Frames["hud-experience-track"].Size == new Size(574, 8) &&
                manifest.Frames["hud-experience-fill"].Size == new Size(574, 8), "Experience art does not match native target geometry");
            Check(manifest.Frames["hud-experience-fill"].Index != manifest.Frames["hud-experience-track"].Index,
                "Experience fill aliases the empty track");
            using (var owner = GameUiVerifiedPackage.Verify(originalPath, manifest))
            {
                Check(owner.Manifest == manifest && !owner.IsDisposed, "Actual package CPU verification failed");
                Reject<InvalidOperationException>(() => { var unpublished = owner.Library; }, "publishing before GPU verification");
            }
            File.WriteAllBytes(copy, bytes);
            using (var target = new TargetForm())
            using (var direct3D = new Direct3D())
            {
                CEnvir.Target = target;
                var parameters = new PresentParameters { Windowed = true, SwapEffect = SwapEffect.Discard,
                    BackBufferFormat = Format.X8R8G8B8, BackBufferWidth = 1024, BackBufferHeight = 768 };
                using (var device = new Device(direct3D, 0, DeviceType.Hardware, target.Handle, CreateFlags.SoftwareVertexProcessing, parameters))
                {
                    SetDevice(device);
                    try
                    {
                        Check(DXManager.TextureList.Count == 0, "Test process already owns textures");
                        for (int lifetime = 0; lifetime < 3; lifetime++)
                        {
                            GameUiVerifiedPackage owner; string diagnostic;
                            Check(GameUiVerifiedPackage.TryLoad(copy, manifest, out owner, out diagnostic) && diagnostic == null,
                                "Actual catalog GPU preflight failed: " + diagnostic);
                            using (owner)
                            {
                                var library = owner.Library;
                                Check(library.Images.Length == 93 && DXManager.TextureList.Count == 93, "Incomplete GPU upload");
                                Frames(library);
                                owner.UploadAllFrames();
                                Check(ReferenceEquals(library, owner.Library) && DXManager.TextureList.Count == 93, "Idempotent upload duplicated resources");
                                Reject<IOException>(() => File.Open(copy, FileMode.Open, FileAccess.Write, FileShare.Read).Dispose(), "write during live owner");
                                Reject<IOException>(() => File.Delete(copy), "delete during live owner");
                                foreach (var image in library.Images) image.ExpireTime = DateTime.MinValue;
                                DXManager.MemoryClear();
                                Check(DXManager.TextureList.Count == 0, "Cache expiry leaked catalog textures");
                                foreach (var frame in manifest.Frames.Values) library.CreateImage(frame.Index, ImageType.Image);
                                Frames(library);
                                Check(DXManager.TextureList.Count == 93, "Cache expiry prevented reupload");
                                var textures = library.Images.Select(image => image.Image).ToArray();
                                foreach (var image in library.Images) image.DisposeTexture();
                                Check(DXManager.TextureList.Count == 0 && textures.All(texture => texture.Disposed), "Invalidation leaked GPU handles");
                                foreach (var frame in manifest.Frames.Values) library.CreateImage(frame.Index, ImageType.Image);
                                Frames(library);
                                textures = library.Images.Select(image => image.Image).ToArray();
                                owner.Dispose(); owner.Dispose();
                                Check(owner.IsDisposed && library.IsDisposed && textures.All(texture => texture.Disposed) && DXManager.TextureList.Count == 0, "Owner disposal leaked library/textures");
                                Reject<ObjectDisposedException>(() => { var released = owner.Library; }, "library after disposal");
                                using (var writable = File.Open(copy, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                                    Check(writable.Length == bytes.Length, "Owner retained file handles");
                            }
                        }
                        var corrupt = (byte[])bytes.Clone(); corrupt[corrupt.Length - 1] ^= 1; File.WriteAllBytes(damaged, corrupt);
                        GameUiVerifiedPackage failure; string reason;
                        Check(!GameUiVerifiedPackage.TryLoad(damaged, manifest, out failure, out reason) && failure == null && reason.Contains("SHA256 mismatch"), "Damaged actual package published a library");
                        Check(DXManager.TextureList.Count == 0, "Rejected actual package leaked textures");
                        using (var writable = File.Open(damaged, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                            Check(writable.Length == bytes.Length, "Rejected package leaked file lease");
                    }
                    finally { SetDevice(null); CEnvir.Target = null; }
                }
            }
            Check(Hash(File.ReadAllBytes(originalPath)) == GameUiOfficialV1Catalog.PackageSha256, "Source package changed");
            Console.WriteLine("OfficialV1 actual catalog passed: " + checks + " checks; " + uploads + " frame uploads verified; 93 frames; 19 button groups; 3 owner lifetimes; runtime disabled.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            if (File.Exists(copy)) File.Delete(copy);
            if (File.Exists(damaged)) File.Delete(damaged);
        }
    }
}
