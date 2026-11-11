using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using Client.Controls;
using Client.Envir;
using ImageManager;
using SlimDX.Direct3D9;

internal static partial class OfficialUiHudLayoutTests
{
    private static void Property(string name, object value) => typeof(DXManager).GetProperty(name).SetValue(null,value,null);
    private static readonly ushort[] Colours={0xf800,0x07e0,0x001f,0xffff};
    private static int ColourIndex(int x,int y)=>(x/4+3*(y/4))%4;
    private static Color Pixel(int x,int y)=>new[]{Color.Red,Color.Lime,Color.Blue,Color.White}[ColourIndex(x,y)];
    private static Mir3Image Pattern(short width,short height)
    {
        using(var stream=new MemoryStream())using(var w=new BinaryWriter(stream))
        {
            for(int y=0;y<height;y+=4)for(int x=0;x<width;x+=4){w.Write(Colours[ColourIndex(x,y)]);w.Write((ushort)0);w.Write(0U);}
            return new Mir3Image{Width=width,Height=height,Data=stream.ToArray()};
        }
    }
    private static void Render(TestScene root,string output)
    {
        Directory.CreateDirectory(output);string file=Path.Combine(Path.GetTempPath(),"hud-render-"+Guid.NewGuid().ToString("N")+".Zl");
        new Mir3Library{Images=new[]{Pattern(53,101),Pattern(48,101),Pattern(574,8)}}.Save(file);
        try
        {
            using(var d3d=new Direct3D())
            {
                var parameters=new PresentParameters{Windowed=true,SwapEffect=SwapEffect.Discard,BackBufferFormat=Format.X8R8G8B8,BackBufferWidth=1024,BackBufferHeight=768,PresentFlags=PresentFlags.LockableBackBuffer};
                using(var device=new Device(d3d,0,DeviceType.Hardware,CEnvir.Target.Handle,CreateFlags.SoftwareVertexProcessing,parameters))
                {
                    Property("Device",device);Property("Parameters",parameters);
                    try
                    {
                        typeof(DXManager).GetMethod("LoadTextures",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
                        using(var library=new MirLibrary(file))
                        using(var parent=new DXControl{Parent=root,DrawTexture=false})
                        {
                            library.ReadLibrary(); int frame=0;long pixels=0;
                            for(int kind=0;kind<3;kind++)
                            {
                                decimal ratio=0;
                                using(var fill=new GameUiHudFill(kind==2?GameUiHudFillDirection.Left:GameUiHudFillDirection.Bottom,()=>ratio){Parent=parent,Size=library.GetSize(kind),DrawTexture=false})
                                {
                                    fill.BindImage(library,kind);
                                    foreach(int clip in new[]{0,1,2})
                                    foreach(decimal fraction in new[]{-1M,0M,.001M,.25M,.5M,.999M,1M,2M})
                                    {
                                        ratio=fraction;fill.Location=clip==1?new Point(-7,-11):new Point(3,5);
                                        parent.Location=clip==2?new Point(1000,730):new Point(20,20);
                                        parent.Size=clip==1?new Size(Math.Max(1,fill.Size.Width-20),Math.Max(1,fill.Size.Height-3)):new Size(fill.Size.Width+10,fill.Size.Height+12);
                                        var source=fill.FilledSource;
                                        int extent=(int)decimal.Floor((kind==2?fill.Size.Width:fill.Size.Height)*Math.Min(1M,Math.Max(0M,fraction)));
                                        var expectedSource=extent==0?Rectangle.Empty:kind==2?new Rectangle(0,0,extent,fill.Size.Height):new Rectangle(0,fill.Size.Height-extent,fill.Size.Width,extent);
                                        Check(source==expectedSource,"Fill source rounding/direction incorrect");
                                        device.Clear(ClearFlags.Target,Color.FromArgb(40,80,120),1,0);device.BeginScene();DXManager.Sprite.Begin(SpriteFlags.AlphaBlend);
                                        device.SetSamplerState(0,SamplerState.MinFilter,TextureFilter.Point);device.SetSamplerState(0,SamplerState.MagFilter,TextureFilter.Point);
                                        float opacity=DXManager.Opacity;bool blend=DXManager.Blending;float rate=DXManager.BlendRate;var transform=DXManager.Sprite.Transform;var surface=DXManager.CurrentSurface;
                                        parent.Draw();
                                        Check(DXManager.Opacity==opacity && DXManager.Blending==blend && DXManager.BlendRate==rate && DXManager.Sprite.Transform==transform && DXManager.CurrentSurface==surface,"Fill leaked native render state");
                                        DXManager.Sprite.End();device.EndScene();
                                        string image=Path.Combine(output,"fill-"+(++frame)+".png");Surface.ToFile(DXManager.MainSurface,image,ImageFileFormat.Png);
                                        using(var bitmap=new Bitmap(image))
                                        {
                                            var scan=Rectangle.Intersect(new Rectangle(0,0,1024,768),new Rectangle(parent.Location.X-12,parent.Location.Y-12,Math.Max(parent.Size.Width,fill.Size.Width)+28,Math.Max(parent.Size.Height,fill.Size.Height)+28));
                                            for(int y=scan.Top;y<scan.Bottom;y++)for(int x=scan.Left;x<scan.Right;x++)
                                            {
                                                int sx=x-fill.DisplayArea.X,sy=y-fill.DisplayArea.Y;
                                                bool draws=parent.DisplayArea.Contains(x,y)&&expectedSource.Contains(sx,sy);
                                                Color expected=draws?Pixel(sx,sy):Color.FromArgb(40,80,120);Color actual=bitmap.GetPixel(x,y);
                                                if(Math.Abs(expected.R-actual.R)>3||Math.Abs(expected.G-actual.G)>3||Math.Abs(expected.B-actual.B)>3)throw new Exception("HUD GPU crop mismatch kind="+kind+" clip="+clip+" fraction="+fraction+" pixel="+x+","+y+" source="+sx+","+sy+" actual="+actual+" expected="+expected);
                                                pixels++;
                                            }
                                        }
                                    }
                                    // Nondefault drawing state must be restored, including additive image mode.
                                    ratio=.5M;fill.ImageOpacity=.5F;fill.Blend=true;parent.Location=new Point(20,20);
                                    device.BeginScene();DXManager.Sprite.Begin(SpriteFlags.AlphaBlend);DXManager.SetOpacity(.75F);DXManager.SetBlend(false,.3F);
                                    float oldOpacity=DXManager.Opacity,oldRate=DXManager.BlendRate;bool oldBlend=DXManager.Blending; parent.Draw();
                                    Check(DXManager.Opacity==oldOpacity&&DXManager.Blending==oldBlend&&DXManager.BlendRate==oldRate,"Blend/opacity state not restored");
                                    DXManager.SetOpacity(1F);DXManager.Sprite.End();device.EndScene();
                                    Reject<ArgumentException>(()=>fill.BindImage(library,(kind+1)%3),"Wrong-size fill frame accepted");
                                    fill.BindImage(null,-1);Check(fill.Library==null&&fill.Index==-1,"Missing frame not cleared");
                                }
                            }
                            Console.WriteLine("HUD GPU: "+pixels+" pixel comparisons across "+frame+" frames; three source-sensitive fills, eight fractions, scene/ancestor clipping and draw-state restoration. Synthetic artwork only.");
                        }
                    }
                    finally{typeof(DXManager).GetMethod("CleanUp",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);Property("Device",null);}
                }
            }
        }
        finally{File.Delete(file);}
    }
}
