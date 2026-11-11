using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Client.Envir;
using System.Collections.Generic;
using SlimDX.Direct3D9;

#pragma warning disable 0649 // JSON fields populated by JavaScriptSerializer.
internal static class OfficialUiComposedRenderTests
{
    public sealed class Manifest { public List<Asset> assets; public List<Composition> compositions; }
    public sealed class Asset { public string role; public int outputIndex; public int[] logicalSize, textureSize; }
    public sealed class Composition { public string id; public List<Layer> layers; }
    public sealed class Layer { public string role, fill; public int[] offset; }
    private static long pixels;
    private static int frames, maximumRgbDifference;
    private static void Property(string name, object value) => typeof(DXManager).GetProperty(name).SetValue(null,value,null);
    private static byte[] Bgra(Bitmap bitmap)
    {
        using (var copy = bitmap.Clone(new Rectangle(0,0,bitmap.Width,bitmap.Height), PixelFormat.Format32bppArgb))
        {
            var locked = copy.LockBits(new Rectangle(0,0,copy.Width,copy.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] bytes = new byte[copy.Width*copy.Height*4];
                for(int y=0;y<copy.Height;y++) Marshal.Copy(IntPtr.Add(locked.Scan0,y*locked.Stride),bytes,y*copy.Width*4,copy.Width*4);
                return bytes;
            }
            finally { copy.UnlockBits(locked); }
        }
    }
    private static void Compare(Bitmap actual, Bitmap source, int x, int y, string role)
    {
        if(x<0 || y<0 || x+source.Width+4>actual.Width || y+source.Height+4>actual.Height) throw new Exception(role+": test target too small");
        byte[] result = Bgra(actual), expected = Bgra(source);
        for(int row=0;row<source.Height;row++) for(int col=0;col<source.Width;col++)
        {
            int e=(row*source.Width+col)*4, a=((row+y)*actual.Width+col+x)*4;
            bool transparent=expected[e+3]==0;
            int[] background={120,80,40};
            for(int channel=0;channel<3;channel++)
            {
                int desired=transparent?background[channel]:expected[e+channel];
                int difference=Math.Abs(result[a+channel]-desired);
                maximumRgbDifference=Math.Max(maximumRgbDifference,difference);
                if((transparent && difference!=0) || difference>8)
                    throw new Exception(role+" rendered mismatch at "+col+","+row+" channel "+channel+": "+result[a+channel]+" vs "+desired);
            }
            pixels++;
        }
        // Transparent padded texels must not alter the background beyond the logical frame.
        for(int row=y;row<y+source.Height+4;row++) for(int col=x;col<x+source.Width+4;col++)
        {
            if(col<x+source.Width && row<y+source.Height) continue;
            int a=(row*actual.Width+col)*4;
            if(result[a]!=120 || result[a+1]!=80 || result[a+2]!=40) throw new Exception(role+": padding leaked into background");
        }
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if(args.Length!=3) throw new ArgumentException("<package-directory> <BC1-verification-directory> <render-output>");
            string package=Path.GetFullPath(args[0]), oracle=Path.Combine(args[1],"GpuExpected"), output=Path.GetFullPath(args[2]);
            if(Directory.Exists(output)) throw new IOException("Output exists");
            Directory.CreateDirectory(output);
            var serializer=new JavaScriptSerializer { MaxJsonLength=8*1024*1024 };
            var manifest=serializer.Deserialize<Manifest>(File.ReadAllText(Path.Combine(package,"prepared-manifest.json")));
            using(var host=new Form { ClientSize=new Size(1024,768), ShowInTaskbar=false })
            using(var d3d=new Direct3D())
            {
                var parameters=new PresentParameters { Windowed=true, SwapEffect=SwapEffect.Discard, BackBufferFormat=Format.X8R8G8B8,
                    BackBufferWidth=1024,BackBufferHeight=768,PresentFlags=PresentFlags.LockableBackBuffer };
                using(var device=new Device(d3d,0,DeviceType.Hardware,host.Handle,CreateFlags.SoftwareVertexProcessing,parameters))
                {
                    Property("Device",device); Property("Parameters",parameters);
                    typeof(DXManager).GetMethod("LoadTextures",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
                    try
                    {
                        using(var library=new MirLibrary(Path.Combine(package,"OfficialV1.Zl")))
                        {
                            library.ReadLibrary();
                            if(library.Images.Length!=manifest.assets.Count) throw new Exception("Client image count mismatch");
                            foreach(var a in manifest.assets)
                            {
                                var image=library.CreateImage(a.outputIndex,Client.Envir.ImageType.Image);
                                if(image==null || !image.ImageValid || image.OffSetX!=0 || image.OffSetY!=0 || image.Width!=a.logicalSize[0] || image.Height!=a.logicalSize[1])
                                    throw new Exception("Client image metadata mismatch: "+a.role);
                                var description=image.Image.GetLevelDescription(0);
                                if(description.Format!=Format.Dxt1 || description.Width!=a.textureSize[0] || description.Height!=a.textureSize[1]) throw new Exception("GPU texture mismatch");
                                device.Clear(ClearFlags.Target,Color.FromArgb(40,80,120),1,0);
                                device.BeginScene(); DXManager.Sprite.Begin(SpriteFlags.AlphaBlend);
                                device.SetSamplerState(0,SamplerState.MinFilter,TextureFilter.Point); device.SetSamplerState(0,SamplerState.MagFilter,TextureFilter.Point);
                                library.Draw(a.outputIndex,8,8,Color.White,true,1,Client.Envir.ImageType.Image);
                                DXManager.Sprite.End(); device.EndScene();
                                string path=Path.Combine(output,a.role+".png"); Surface.ToFile(DXManager.MainSurface,path,ImageFileFormat.Png);
                                using(var actual=new Bitmap(path)) using(var expected=new Bitmap(Path.Combine(oracle,a.role+".png"))) Compare(actual,expected,8,8,a.role);
                                frames++;
                            }
                            foreach(var composition in manifest.compositions)
                            {
                                foreach(int percent in new[] {0,50,100})
                                {
                                    device.Clear(ClearFlags.Target,Color.FromArgb(40,80,120),1,0);
                                    device.BeginScene(); DXManager.Sprite.Begin(SpriteFlags.AlphaBlend);
                                    device.SetSamplerState(0,SamplerState.MinFilter,TextureFilter.Point); device.SetSamplerState(0,SamplerState.MagFilter,TextureFilter.Point);
                                    foreach(var layer in composition.layers)
                                    {
                                        var a=manifest.assets.Single(v=>v.role==layer.role);
                                        int top=0;
                                        if(layer.fill!=null)
                                        {
                                            int visible=(a.logicalSize[1]*percent+50)/100;
                                            if(visible==0) continue;
                                            top=a.logicalSize[1]-visible;
                                        }
                                        library.Draw(a.outputIndex,8+layer.offset[0],8+layer.offset[1]+top,Color.White,
                                            new Rectangle(0,top,a.logicalSize[0],a.logicalSize[1]-top),1,Client.Envir.ImageType.Image);
                                    }
                                    DXManager.Sprite.End(); device.EndScene();
                                    string path=Path.Combine(output,"orb-"+percent+".png"); Surface.ToFile(DXManager.MainSurface,path,ImageFileFormat.Png);
                                    var shell=manifest.assets.Single(v=>v.role=="hud-orb-shell");
                                    using(var expected=new Bitmap(shell.logicalSize[0],shell.logicalSize[1]))
                                    {
                                        using(var g=Graphics.FromImage(expected))
                                        {
                                            g.Clear(Color.FromArgb(40,80,120));
                                            foreach(var layer in composition.layers)
                                            {
                                                using(var part=new Bitmap(Path.Combine(oracle,layer.role+".png")))
                                                {
                                                    int top=0;
                                                    if(layer.fill!=null)
                                                    {
                                                        int visible=(part.Height*percent+50)/100;
                                                        if(visible==0) continue;
                                                        top=part.Height-visible;
                                                    }
                                                    g.DrawImage(part,new Rectangle(layer.offset[0],layer.offset[1]+top,part.Width,part.Height-top),
                                                        new Rectangle(0,top,part.Width,part.Height-top),GraphicsUnit.Pixel);
                                                }
                                            }
                                        }
                                        using(var actual=new Bitmap(path)) Compare(actual,expected,8,8,"orb-"+percent);
                                    }
                                    frames++;
                                }
                            }
                        }
                    }
                    finally
                    {
                        typeof(DXManager).GetMethod("CleanUp",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
                        Property("Device",null);
                    }
                }
            }
            Console.WriteLine("Composed UI D3D9: "+frames+" real frames/compositions; "+pixels+" rendered pixels matched independent BC1 oracle (opaque RGB tolerance 8; measured max "+maximumRgbDifference+"; transparent RGB exact). Baked offsets and transparent padding verified.");
            return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
