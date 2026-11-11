using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using Client;
using Client.Envir;
using ImageManager;
using SlimDX.Direct3D9;

internal static class OfficialUiPackagePreflightTests
{
    private static int checks, pixelChecks;
    private static string directory, packagePath;
    private static byte[] original;
    private static Mir3Image[] images;
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }
    private static string Hash(byte[] bytes)
    { using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    private static void Reject<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { checks++; return; }
        throw new Exception("Accepted invalid input: " + message);
    }
    private static Mir3Image Image(short w, short h, ushort colour, short ox, short oy)
    {
        using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
        {
            for (int i = 0; i < ((w + 3) / 4) * ((h + 3) / 4); i++)
            { writer.Write(colour); writer.Write((ushort)0); writer.Write(0U); }
            return new Mir3Image { Width = w, Height = h, OffSetX = ox, OffSetY = oy, Data = stream.ToArray() };
        }
    }
    private static GameUiPackageFrame[] Frames()
    {
        return new[]
        {
            new GameUiPackageFrame("fixture.red", 0, new Size(8,4), new Point(-3,2), Hash(images[0].Data)),
            new GameUiPackageFrame("fixture.green", 2, new Size(5,7), new Point(4,-5), Hash(images[2].Data))
        };
    }
    private static GameUiPackageManifest Manifest(byte[] bytes, GameUiPackageFrame[] frames = null)
    {
        return new GameUiPackageManifest(Hash(bytes), frames ?? Frames(), new[] { "fixture.red", "fixture.green" });
    }
    private static void Mutate(string name, Action<byte[]> mutate, string expected)
    {
        byte[] bytes = (byte[])original.Clone(); mutate(bytes);
        File.WriteAllBytes(packagePath, bytes);
        try
        {
            using (GameUiVerifiedPackage.Verify(packagePath, Manifest(bytes))) { }
            throw new Exception("Accepted malformed package: " + name);
        }
        catch (InvalidDataException e) { Check(e.Message.Contains(expected), name + ": wrong failure: " + e.Message); }
        finally { File.WriteAllBytes(packagePath, original); }
        using (var reopened = new FileStream(packagePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Check(reopened.Length == original.Length, name + ": file lease leaked");
    }
    private static void Int32(byte[] bytes, int offset, int value) => Buffer.BlockCopy(BitConverter.GetBytes(value),0,bytes,offset,4);
    private static void Cpu()
    {
        var input = Frames(); var manifest = Manifest(original, input); input[0] = input[1];
        Check(manifest.Frames["fixture.red"].Index == 0, "Manifest did not snapshot caller array");
        Reject<NotSupportedException>(() => ((IDictionary<string,GameUiPackageFrame>)manifest.Frames).Clear(), "mutable manifest");
        Reject<ArgumentException>(() => new GameUiPackageFrame(" ",0,new Size(1,1),Point.Empty,Hash(original)), "empty ID");
        Reject<ArgumentException>(() => new GameUiPackageFrame("id",0,new Size(1,1),Point.Empty,"zz" + new string('0',62)), "invalid hash");
        Reject<ArgumentOutOfRangeException>(() => new GameUiPackageFrame("id",65536,new Size(1,1),Point.Empty,Hash(original)), "large index");
        Reject<ArgumentOutOfRangeException>(() => new GameUiPackageFrame("id",-1,new Size(1,1),Point.Empty,Hash(original)), "negative index");
        Reject<ArgumentOutOfRangeException>(() => new GameUiPackageFrame("id",0,Size.Empty,Point.Empty,Hash(original)), "empty size");
        Reject<ArgumentOutOfRangeException>(() => new GameUiPackageFrame("id",0,new Size(4097,1),Point.Empty,Hash(original)), "large size");
        Reject<ArgumentOutOfRangeException>(() => new GameUiPackageFrame("id",0,new Size(1,1),new Point(32768,0),Hash(original)), "bad offset");
        Reject<ArgumentException>(() => new GameUiPackageManifest(Hash(original),Frames(),new[]{"fixture.red"}), "missing semantic role");
        Reject<ArgumentException>(() => new GameUiPackageManifest(Hash(original),Frames(),new[]{"fixture.red","fixture.green","extra"}), "extra role");
        Reject<ArgumentException>(() => new GameUiPackageManifest(Hash(original),Frames(),new[]{"fixture.red","fixture.red"}), "duplicate required role");
        Reject<ArgumentException>(() => new GameUiPackageManifest(Hash(original),new[]{Frames()[0],Frames()[0]},new[]{"fixture.red"}), "duplicate frame");
        Reject<ArgumentException>(() => new GameUiPackageManifest(Hash(original),new GameUiPackageFrame[0],new string[0]), "empty manifest");
        Reject<ArgumentException>(() => new GameUiPackageManifest(Hash(original),new[]{Frames()[0],new GameUiPackageFrame("other",0,new Size(1,1),Point.Empty,Hash(original))},new[]{"fixture.red","other"}), "duplicate index");
        Reject<ArgumentException>(() => new GameUiPackageManifest(Hash(original),new[]{Frames()[0],new GameUiPackageFrame("fixture.red",3,new Size(1,1),Point.Empty,Hash(original))},new[]{"fixture.red"}), "duplicate ID");
        var oversized = Enumerable.Range(0,9).Select(i=>new GameUiPackageFrame("large"+i,i,new Size(4096,4096),Point.Empty,Hash(original))).ToArray();
        Reject<ArgumentException>(()=>new GameUiPackageManifest(Hash(original),oversized,oversized.Select(f=>f.Id)),"payload budget");
        using (var verified = GameUiVerifiedPackage.Verify(packagePath,manifest))
        {
            Check(!verified.IsDisposed && verified.Manifest == manifest, "Verified owner missing");
            Reject<InvalidOperationException>(()=>{var unused=verified.Library;},"publishing before GPU preflight");
            Reject<IOException>(()=>File.Open(packagePath,FileMode.Open,FileAccess.Write,FileShare.Read).Dispose(),"writing verified file");
            Reject<IOException>(()=>File.Delete(packagePath),"deleting verified file");
            Exception threadFailure = null;
            var worker = new Thread(()=>{try{verified.Dispose();}catch(Exception e){threadFailure=e;}});
            worker.Start(); worker.Join();
            Check(threadFailure is InvalidOperationException && !verified.IsDisposed,"foreign thread released GPU owner");
        }
        using(var writable = new FileStream(packagePath,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            Check(writable.Length==original.Length,"CPU owner did not release lease");
        Mutate("package hash",b=>b[b.Length-1]^=1,"Frame SHA256 mismatch");
        File.WriteAllBytes(packagePath,original.Take(original.Length-1).ToArray());
        Reject<InvalidDataException>(()=>GameUiVerifiedPackage.Verify(packagePath,manifest),"stale package hash");
        File.WriteAllBytes(packagePath,original);
        Mutate("negative header",b=>Int32(b,0,-1),"header length");
        Mutate("header past EOF",b=>Int32(b,0,int.MaxValue),"header length");
        Mutate("short header",b=>Int32(b,0,5),"slot count");
        Mutate("negative count",b=>Int32(b,4,-1),"slot count");
        Mutate("huge count",b=>Int32(b,4,int.MaxValue),"slot count");
        Mutate("flag value",b=>b[8]=2,"presence flag");
        Mutate("missing required frame",b=>b[8]=0,"Required frame is absent");
        Mutate("unbound frame",b=>b[34]=1,"Unbound Zl frame");
        Mutate("width",b=>b[13]=9,"geometry mismatch");
        Mutate("signed offset",b=>b[17]^=1,"geometry mismatch");
        for(int i=0;i<13;i++){int offset=i;Mutate("auxiliary field "+i,b=>b[21+offset]=1,"Shadow/overlay");}
        Mutate("payload gap",b=>Int32(b,9,images[0].Position+1),"out of order");
        Mutate("payload overlap",b=>Int32(b,36,images[0].Position),"overlapping");
        Mutate("frame before header",b=>Int32(b,9,1),"out of order");
        Mutate("header boundary",b=>Int32(b,0,BitConverter.ToInt32(b,0)-1),"out of order");
        var extra=original.Concat(new byte[]{0}).ToArray();File.WriteAllBytes(packagePath,extra);
        Reject<InvalidDataException>(()=>GameUiVerifiedPackage.Verify(packagePath,Manifest(extra)),"trailing data");File.WriteAllBytes(packagePath,original);
        var shortPayload=original.Take(original.Length-1).ToArray();File.WriteAllBytes(packagePath,shortPayload);
        Reject<InvalidDataException>(()=>GameUiVerifiedPackage.Verify(packagePath,Manifest(shortPayload)),"truncated payload");File.WriteAllBytes(packagePath,original);
        var wrong=Frames();wrong[1]=new GameUiPackageFrame("fixture.green",2,new Size(5,7),new Point(4,-5),new string('0',64));
        Reject<InvalidDataException>(()=>GameUiVerifiedPackage.Verify(packagePath,Manifest(original,wrong)),"compiled frame digest");
        var outOfTable=Frames();outOfTable[1]=new GameUiPackageFrame("fixture.green",6,new Size(5,7),new Point(4,-5),Hash(images[2].Data));
        Reject<InvalidDataException>(()=>GameUiVerifiedPackage.Verify(packagePath,Manifest(original,outOfTable)),"required index outside table");
        string big=Path.Combine(directory,"oversized.Zl");
        using(var file=File.Create(big)) file.SetLength(GameUiPackageManifest.MaxPackageBytes+1);
        Reject<InvalidDataException>(()=>GameUiVerifiedPackage.Verify(big,manifest),"package byte budget");File.Delete(big);
        GameUiVerifiedPackage unavailable;string diagnostic;
        Check(!GameUiVerifiedPackage.TryLoad(Path.Combine(directory,"missing.Zl"),manifest,out unavailable,out diagnostic)&&unavailable==null&&diagnostic.Contains("FileNotFoundException"),"missing package fallback");
        Check(!GameUiVerifiedPackage.TryLoad(packagePath,manifest,out unavailable,out diagnostic)&&unavailable==null&&diagnostic.Contains("device is not ready"),"no-device fallback");
        Check(DXManager.TextureList.Count==0,"No-device preflight leaked textures");
        File.WriteAllBytes(packagePath,original.Take(original.Length-1).ToArray());
        Check(!GameUiVerifiedPackage.TryLoad(packagePath,manifest,out unavailable,out diagnostic)&&unavailable==null&&diagnostic.Contains("SHA256 mismatch"),"wrong-hash fallback");
        byte[] malformed=(byte[])original.Clone();malformed[8]=2;File.WriteAllBytes(packagePath,malformed);
        Check(!GameUiVerifiedPackage.TryLoad(packagePath,Manifest(malformed),out unavailable,out diagnostic)&&unavailable==null&&diagnostic.Contains("presence flag"),"malformed-header fallback");
        File.WriteAllBytes(packagePath,original);
        using(var writable=new FileStream(packagePath,FileMode.Open,FileAccess.ReadWrite,FileShare.None))Check(writable.Length==original.Length,"Fallback leaked file lease");
    }
    private static void DeviceProperty(object value)=>typeof(DXManager).GetProperty("Device").SetValue(null,value,null);
    private static void Gpu()
    {
        using(var target=new TargetForm()) using(var d3d=new Direct3D())
        {
            CEnvir.Target=target;
            var parameters=new PresentParameters{Windowed=true,SwapEffect=SwapEffect.Discard,BackBufferFormat=Format.X8R8G8B8,BackBufferWidth=1024,BackBufferHeight=768};
            using(var device=new Device(d3d,0,DeviceType.Hardware,target.Handle,CreateFlags.SoftwareVertexProcessing,parameters))
            {
                DeviceProperty(device);
                try
                {
                    for(int cycle=0;cycle<3;cycle++)
                    {
                        GameUiVerifiedPackage owner; string diagnostic;
                        Check(GameUiVerifiedPackage.TryLoad(packagePath,Manifest(original),out owner,out diagnostic)&&diagnostic==null,"GPU preflight failed: "+diagnostic);
                        MirLibrary library=owner.Library;
                        Check(DXManager.TextureList.Count==2,"All frames were not uploaded");
                        owner.UploadAllFrames(); Check(ReferenceEquals(library,owner.Library)&&DXManager.TextureList.Count==2,"Repeated upload duplicated library/textures");
                        foreach(var frame in owner.Manifest.Frames.Values)
                        {
                            var image=library.GetImage(frame.Index); var expected=frame.Index==0?Color.Red:Color.Lime;
                            using(var stream=Texture.ToStream(image.Image,ImageFileFormat.Png)) using(var bitmap=new Bitmap(stream))
                                for(int y=0;y<bitmap.Height;y++) for(int x=0;x<bitmap.Width;x++)
                                { Check(bitmap.GetPixel(x,y).ToArgb()==expected.ToArgb(),"GPU data mismatch");pixelChecks++; }
                            Check(library.GetOffSet(frame.Index)==frame.Offset,"Signed source offsets changed");
                        }
                        foreach(var image in library.Images.Where(x=>x!=null))image.ExpireTime=DateTime.MinValue;
                        DXManager.MemoryClear();Check(DXManager.TextureList.Count==0,"Cache expiry did not release UI textures");
                        foreach(var frame in owner.Manifest.Frames.Values)library.CreateImage(frame.Index,ImageType.Image);
                        Check(DXManager.TextureList.Count==2,"Frame data not readable after cache expiry");
                        // The native device reset path also calls DisposeTexture. Exercise
                        // the same invalidation without resetting the global game scene.
                        foreach(var image in library.Images.Where(x=>x!=null).ToArray())image.DisposeTexture();
                        foreach(var frame in owner.Manifest.Frames.Values)library.CreateImage(frame.Index,ImageType.Image);
                        Check(DXManager.TextureList.Count==2,"Texture invalidation/reupload failed");
                        owner.Dispose();owner.Dispose();Check(owner.IsDisposed&&library.IsDisposed&&DXManager.TextureList.Count==0,"Owner cleanup leaked textures");
                        Reject<ObjectDisposedException>(()=>{var unused=owner.Library;},"access after disposal");
                        Reject<ObjectDisposedException>(()=>owner.UploadAllFrames(),"upload after disposal");
                        using(var writable=new FileStream(packagePath,FileMode.Open,FileAccess.ReadWrite,FileShare.None))Check(writable.Length==original.Length,"GPU owner leaked file lease");
                    }
                    // Inject an impossible second descriptor index after CPU verification
                    // to force failure after the first real texture has been uploaded.
                    var badManifest=Manifest(original);var failing=GameUiVerifiedPackage.Verify(packagePath,badManifest);
                    var field=typeof(GameUiPackageFrame).GetField("<Index>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic);
                    field.SetValue(badManifest.Frames["fixture.green"],65535);
                    Reject<InvalidDataException>(()=>failing.UploadAllFrames(),"second-frame upload failure");
                    Check(failing.IsDisposed&&DXManager.TextureList.Count==0,"Partial GPU upload leaked resources");
                    using(var writable=new FileStream(packagePath,FileMode.Open,FileAccess.ReadWrite,FileShare.None))Check(writable.Length==original.Length,"Partial GPU failure leaked file lease");
                }
                finally{DeviceProperty(null);CEnvir.Target=null;}
            }
        }
    }
    [STAThread]
    public static int Main()
    {
        directory=Path.Combine(Path.GetTempPath(),"ZirconPreflightData-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        packagePath=Path.Combine(directory,"Fixture.Zl");
        try
        {
            images=new[]{Image(8,4,0xf800,-3,2),null,Image(5,7,0x07e0,4,-5),null};
            new Mir3Library{Images=images}.Save(packagePath);original=File.ReadAllBytes(packagePath);
            Cpu();Gpu();Console.WriteLine("Package preflight checks passed: "+checks+"; GPU pixel comparisons: "+pixelChecks+"; 3 owner lifetimes; partial-upload cleanup verified.");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally
        {
            // Only paths explicitly created by this fixture are removed.
            if(File.Exists(packagePath))File.Delete(packagePath);
            string big=Path.Combine(directory,"oversized.Zl");if(File.Exists(big))File.Delete(big);
            Directory.Delete(directory,false);
        }
    }
}
