using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using ImageManager;

internal static class OfficialUiAssetTests
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++; if (!condition) throw new Exception(message);
    }
    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (ArgumentException) { rejected = true; } catch (IOException) { rejected = true; } catch (InvalidDataException) { rejected = true; }
        Check(rejected, message);
    }
    // Independent BC1 pixel read verifies alpha/color rather than just comparing encoder bytes.
    private static Color Pixel(byte[] data, int width, int x, int y)
    {
        int offset = ((y / 4) * ((width + 3) / 4) + x / 4) * 8;
        ushort c0 = BitConverter.ToUInt16(data, offset), c1 = BitConverter.ToUInt16(data, offset + 2);
        uint bits = BitConverter.ToUInt32(data, offset + 4);
        int code = (int)((bits >> (2 * ((y % 4) * 4 + x % 4))) & 3);
        Color a = Expand(c0), b = Expand(c1);
        if (code == 0) return a;
        if (code == 1) return b;
        if (c0 <= c1 && code == 3) return Color.FromArgb(0, 0, 0, 0);
        if (c0 <= c1) return Color.FromArgb(255, (a.R+b.R)/2, (a.G+b.G)/2, (a.B+b.B)/2);
        return code == 2 ? Color.FromArgb(255,(2*a.R+b.R)/3,(2*a.G+b.G)/3,(2*a.B+b.B)/3)
                         : Color.FromArgb(255,(a.R+2*b.R)/3,(a.G+2*b.G)/3,(a.B+2*b.B)/3);
    }
    private static Color Expand(ushort value)
    {
        int r = value >> 11, g = (value >> 5) & 63, b = value & 31;
        return Color.FromArgb(255, (r << 3) | (r >> 2), (g << 2) | (g >> 4), (b << 3) | (b >> 2));
    }
    private static void Encoder()
    {
        using (var black = new Bitmap(5, 3))
        {
            using (var graphics = Graphics.FromImage(black)) graphics.Clear(Color.Black);
            byte[] opaque = MImage.GetBytes(black, Dxt1AlphaMode.Opaque);
            Check(opaque.Length == 16, "5x3 must occupy two BC1 blocks");
            for (int y=0; y<4; y++) for (int x=0; x<8; x++)
            {
                Color value = Pixel(opaque, 5, x, y);
                Check(value.A == (x<5 && y<3 ? 255 : 0), "Logical black must be opaque; padding must be transparent");
                if (x<5 && y<3) Check(value.R == 0 && value.G == 0 && value.B == 0,"Black pixel changed");
            }
            byte[] legacy = MImage.GetBytes(black);
            Check(Pixel(legacy, 5, 0, 0).A == 0, "Legacy pure black key must remain transparent");
            black.SetPixel(0,0,Color.Transparent);
            Reject(() => MImage.GetBytes(black, Dxt1AlphaMode.Opaque), "Opaque must reject hidden alpha");
            byte[] masked = MImage.GetBytes(black,Dxt1AlphaMode.BinaryMask);
            Check(Pixel(masked,5,0,0).A == 0 && Pixel(masked,5,1,0).A == 255,"Explicit mask must retain black interior");
            black.SetPixel(0,0,Color.FromArgb(128,255,0,0));
            Reject(() => MImage.GetBytes(black,Dxt1AlphaMode.BinaryMask),"Continuous alpha must not silently degrade");
        }
        using (var color = new Bitmap(4,4))
        {
            using (var graphics = Graphics.FromImage(color)) graphics.Clear(Color.Red);
            Color decoded=Pixel(MImage.GetBytes(color,Dxt1AlphaMode.Opaque),4,0,0);
            Check(decoded.R>240 && decoded.B<16,"Red and blue must not swap");
        }
        using (var fixture=new Bitmap(5,3))
        {
            for(int y=0;y<3;y++) for(int x=0;x<5;x++) fixture.SetPixel(x,y,(x+y)%3==0?Color.Black:Color.FromArgb(255,x*40,y*70,200));
            Check(OfficialUiBuilder.Hash(MImage.GetBytes(fixture)) == "0bdf3962277a9f9adf1d750ca3aab6b9161b86b3b0d84346da837e026c2516d0", "Pre-change WTL fixture bytes changed");
        }
    }
    private static void Builder(string root)
    {
        string input=Path.Combine(root,"input"), output=Path.Combine(root,"output"), manifestPath=Path.Combine(input,"manifest.json");
        Directory.CreateDirectory(Path.Combine(input,"UI"));
        string png=Path.Combine(input,"UI","1.png");
        using(var image=new Bitmap(5,3)) { using(var graphics=Graphics.FromImage(image)) graphics.Clear(Color.Black); image.Save(png,System.Drawing.Imaging.ImageFormat.Png); }
        var item=new OfficialUiBuilder.SourceImage { index=1,width=5,height=3,offsetX=-9,offsetY=12,png="UI/1.png",pngSha256=OfficialUiBuilder.Hash(File.ReadAllBytes(png)),alphaMode="opaque" };
        var manifest=new OfficialUiBuilder.Manifest { schemaVersion=1,kind="official-ui-decoded-reference",libraries=new System.Collections.Generic.List<OfficialUiBuilder.SourceLibrary> {
            new OfficialUiBuilder.SourceLibrary { name="UI",slots=3,present=1,mixSha256=new string('0',64),mizSha256=new string('1',64),items=new System.Collections.Generic.List<OfficialUiBuilder.SourceImage> {null,item,null} } } };
        var serializer=new JavaScriptSerializer();
        Action save=()=>File.WriteAllText(manifestPath,serializer.Serialize(manifest)); save();
        OfficialUiBuilder.Build(input,output,manifestPath,"reference");
        byte[] original=File.ReadAllBytes(Path.Combine(output,"Reference","UI.Zl"));
        using(var reader=new BinaryReader(new MemoryStream(original)))
        {
            Check(reader.ReadInt32()==32,"Zl header length"); Check(reader.ReadInt32()==3,"Zl slot count");
            Check(!reader.ReadBoolean() && reader.ReadBoolean(),"Empty slot must retain its index");
            Check(reader.ReadInt32()==36,"First texture offset");
            Check(reader.ReadInt16()==5 && reader.ReadInt16()==3,"Logical dimensions");
            Check(reader.ReadInt16()==-9 && reader.ReadInt16()==12,"Signed offsets");
        }
        Check(original.Length==52,"Zl byte count");
        Reject(()=>OfficialUiBuilder.Build(input,output,manifestPath,"reference"),"Existing output must survive");
        Check(original.SequenceEqual(File.ReadAllBytes(Path.Combine(output,"Reference","UI.Zl"))),"Existing package changed");
        string repeated=Path.Combine(root,"repeat"); OfficialUiBuilder.Build(input,repeated,manifestPath,"reference");
        Check(original.SequenceEqual(File.ReadAllBytes(Path.Combine(repeated,"Reference","UI.Zl"))),"Non-deterministic Zl");
        Check(File.ReadAllText(Path.Combine(output,"reference-manifest.json"))==File.ReadAllText(Path.Combine(repeated,"reference-manifest.json")),"Non-deterministic reference manifest");
        Reject(()=>OfficialUiBuilder.Build(input,Path.Combine(input,"nested"),manifestPath,"reference"),"Nested output accepted");
        Reject(()=>OfficialUiBuilder.Build(input,Path.Combine(root,"runtime"),manifestPath,"runtime"),"Unfrozen runtime package accepted");
        item.png="../escape.png"; save();
        Reject(()=>OfficialUiBuilder.Build(input,Path.Combine(root,"unsafe"),manifestPath,"reference"),"Path escape accepted");
        item.png="UI/1.png"; item.pngSha256=new string('2',64); save();
        string failure=Path.Combine(root,"failure");
        Reject(()=>OfficialUiBuilder.Build(input,failure,manifestPath,"reference"),"Hash mismatch accepted");
        Check(!Directory.Exists(failure) && Directory.GetDirectories(root,"*.staging-*").Length==0,"Failed build published partial files");
    }
    [STAThread]
    private static int Main()
    {
        string root=Path.Combine(Path.GetTempPath(),"ZirconOfficialUiAssets-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { Encoder(); Builder(root); Console.WriteLine("Official UI assets: "+checks+" checks passed."); return 0; }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Directory.Delete(root,true); }
    }
}
