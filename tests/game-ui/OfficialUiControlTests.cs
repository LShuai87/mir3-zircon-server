using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using Client.Controls;
using Client.Envir;
using ImageManager;

internal static class OfficialUiControlTests
{
    public sealed class DecodedManifest { public System.Collections.Generic.List<DecodedLibrary> libraries { get; set; } }
    public sealed class DecodedLibrary { public string name { get; set; } public int slots { get; set; } public System.Collections.Generic.List<DecodedImage> items { get; set; } }
    public sealed class DecodedImage { public int width { get; set; } public int height { get; set; } public int offsetX { get; set; } public int offsetY { get; set; } }
    private static int checks;
    private static void Check(bool value, string message) { checks++; if(!value) throw new Exception(message); }
    private static void Reject(Action action, string message)
    {
        bool rejected=false; try { action(); } catch(ArgumentException) { rejected=true; }
        Check(rejected,message);
    }
    private static MirLibrary Fixture(string path, int width)
    {
        var images=new Mir3Image[10];
        foreach(int index in new[] {1,4,7,9}) images[index]=new Mir3Image {Width=(short)width, Height=12, OffSetX=-3,OffSetY=5, Data=new byte[((width+3)/4)*3*8]};
        new Mir3Library {Images=images}.Save(path);
        var library=new MirLibrary(path); library.ReadLibrary(); return library;
    }
    private static void Controls(string directory)
    {
        using(var library=Fixture(Path.Combine(directory,"one.Zl"),20))
        using(var second=Fixture(Path.Combine(directory,"two.Zl"),24))
        using(var parent=new DXControl {Location=new Point(30,40),Size=new Size(100,100)})
        {
            using(var image=new DXImageControl {Parent=parent,Location=new Point(10,15),UseOffSet=true})
            {
                int changes=0;
                image.IndexChanged+=(o,e)=> {changes++; Check(image.Library==library && image.Index==1 && image.DisplayArea==new Rectangle(37,60,20,12),"Atomic binding observer saw stale geometry");};
                image.TextureValid=true; image.BindImage(library,1);
                Check(changes==1 && !image.TextureValid,"Image bind did not invalidate or notify");
                Check(image.Size==new Size(20,12),"Intrinsic size mismatch");
                image.BindImage(second,1);
                Check(image.Size==new Size(24,12) && image.DisplayArea.Size==image.Size,"Same-index library switch left stale size");
                Reject(()=>image.BindImage(library,2),"Missing frame accepted");
                Check(image.Library==second && image.Index==1,"Rejected binding changed image");
                image.FixedSize=true; image.Size=new Size(50,60); image.BindImage(library,1);
                Check(image.Size==new Size(50,60),"Fixed image geometry changed");
            }
            using(var button=new DXButton {Parent=parent,Location=new Point(10,15)})
            {
                button.BindButtonImages(library,new ButtonImageStates(1,7,4,9));
                var area=button.DisplayArea;
                Check(button.CurrentImageIndex==1 && button.Size==new Size(20,12),"Normal frame");
                DXControl.MouseControl=button; button.OnMouseEnter(); Check(button.CurrentImageIndex==7,"Hover explicit index");
                button.OnFocus(); Check(button.CurrentImageIndex==4 && button.DisplayArea==area,"Pressed frame moved hit area");
                button.Enabled=false; Check(button.CurrentImageIndex==9 && button.DisplayArea==area,"Disabled frame");
                button.Enabled=true; button.OnLostFocus(); button.Pressed=true; Check(button.CurrentImageIndex==4,"Latched pressed frame");
                button.Pressed=false; DXControl.MouseControl=null; button.OnMouseLeave(); Check(button.CurrentImageIndex==1,"Normal after release");
                Reject(()=>button.BindButtonImages(second,new ButtonImageStates(1,2,4,9)),"Missing state accepted");
                Check(button.Library==library && button.DisplayArea==area,"Rejected states changed bound button");
                int clicks=0; button.MouseClick+=(o,e)=>clicks++;
                for(int i=0;i<100;i++) button.BindButtonImages(library,new ButtonImageStates(1,7,4,9));
                button.InvokeMouseClick(); Check(clicks==1,"Repeated binding duplicated click handlers");
                button.Enabled=false; button.InvokeMouseClick(); Check(clicks==1,"Disabled click fired");
                button.Enabled=true; button.BindImage(second,1); Check(button.ImageStates==null,"Direct image bind did not clear states");
            }
            using(var button=new DXButton {Parent=parent,Location=new Point(10,15)})
            {
                int notices=0;
                var states=new ButtonImageStates(1,7,4,9);
                button.IndexChanged+=(o,e)=> {
                    notices++;
                    if(notices==1) Check(button.ImageStates==states && button.FixedSize && button.Size==new Size(20,12) && button.Library==library && button.CurrentImageIndex==1,"Explicit button observer saw incomplete binding");
                    else if(notices==2) Check(button.ImageStates==null && !button.FixedSize && button.Library==second && button.Size==new Size(24,12),"Direct button observer saw old explicit state");
                };
                button.BindButtonImages(library,states);
                button.BindImage(second,4);
                Check(notices==2,"Button binding notifications were missing or duplicated");
                Rectangle area=button.DisplayArea;
                DXControl.MouseControl=button; button.OnFocus();
                Check(button.DisplayArea==new Rectangle(area.X,area.Y+1,area.Width,area.Height),"Legacy pressed geometry changed");
                button.OnLostFocus(); DXControl.MouseControl=null;
                Check(button.DisplayArea==area,"Legacy release geometry changed");
                button.FixedSize=true; button.Size=new Size(70,40); button.BindImage(library,1);
                Check(button.Size==new Size(70,40),"Legacy fixed button geometry changed");
            }
            Check(library.GetSize(1)==new Size(20,12),"Control disposal disposed shared library");
        }
        var uninitialized=new MirLibrary(Path.Combine(directory,"one.Zl"));
        uninitialized.Dispose(); uninitialized.Dispose(); Check(uninitialized.IsDisposed,"Unread library dispose must be safe and idempotent");
    }
    private static void RealReferences(string decoded, string reference)
    {
        var serializer=new JavaScriptSerializer {MaxJsonLength=16*1024*1024};
        var manifest=serializer.Deserialize<DecodedManifest>(File.ReadAllText(Path.Combine(decoded,"manifest.json")));
        foreach(var source in manifest.libraries)
        {
            string path=Path.Combine(reference,"Reference",source.name+".Zl");
            using(var library=new MirLibrary(path))
            using(var stream=File.OpenRead(path))
            {
                library.ReadLibrary(); Check(library.Images.Length==source.slots,"Actual client reader count: "+source.name);
                long end=4+new BinaryReader(stream).ReadInt32();
                for(int i=0;i<source.slots;i++)
                {
                    var item=source.items[i]; var frame=library.Images[i];
                    if(item==null) {Check(frame==null,"Empty slot changed");continue;}
                    Check(frame!=null && frame.Width==item.width && frame.Height==item.height && frame.OffSetX==item.offsetX && frame.OffSetY==item.offsetY,"Actual client header mismatch "+source.name+"["+i+"]");
                    Check(frame.Position==end,"Actual client texture offset mismatch");
                    end+=frame.ImageDataSize;
                    Check(end<=stream.Length,"Texture payload outside file");
                }
                Check(end==stream.Length,"Unreferenced trailing bytes");
            }
        }
    }
    [STAThread]
    private static int Main(string[] args)
    {
        string root=Path.Combine(Path.GetTempPath(),"ZirconOfficialUiControls-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try {Controls(root); OfficialUiWindowTests.Run(root); if(args.Length>=2 && args[0]!="--render") RealReferences(args[0],args[1]); int renderIndex=Array.IndexOf(args,"--render"); if(renderIndex>=0) OfficialUiRenderTests.Run(root,args[renderIndex+1],args.Length>=2 && args[0]!="--render" ? args[1] : null); Console.WriteLine("Official UI controls/reader: "+checks+" checks passed.");return 0;}
        catch(Exception error) {Console.Error.WriteLine(error);return 1;}
        finally {DXControl.MouseControl=null; Directory.Delete(root,true);}
    }
}
