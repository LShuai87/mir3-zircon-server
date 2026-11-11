using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using Client.Envir;

internal static class OfficialUiChatStoreTests
{
    private static int checks;
    private static readonly List<string> rows = new List<string>();
    private static readonly Size[] Sizes = { new Size(1024,768), new Size(1366,768), new Size(1280,800), new Size(1440,900), new Size(1600,900), new Size(1920,1080) };
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 2 * 1048576 };
    private static void Check(bool condition, string reason) { checks++; if (!condition) throw new Exception(reason); }
    private static GameUiChatStore.Snapshot Snapshot(Size size)
    {
        return new GameUiChatStore.Snapshot { width=size.Width, height=size.Height, groups=new[] {
            new GameUiChatStore.Group { id="00112233445566778899aabbccddeeff", x=(size.Width-574)/2, y=size.Height-138,
                width=574, height=106, primary=true, floating=false, selected=1,
                tabs=new[] { new GameUiChatStore.Tab { name="综合\"\\\n频道", flags=4095 }, new GameUiChatStore.Tab { name="队伍😀", flags=5 } } },
            new GameUiChatStore.Group { id="ffeeddccbbaa99887766554433221100", x=9, y=8, width=200, height=120,
                primary=false, floating=true, selected=-1, tabs=new[] { new GameUiChatStore.Tab { name="", flags=0 } } }
        } };
    }
    private static string Serialize(GameUiChatStore.Snapshot snapshot) => Json.Serialize(new GameUiChatStore.Document { schemaVersion=1, styleVersion="OfficialV1", resolutions=new[] { snapshot } });
    private static string GroupName(GameUiChatStore store, Size size)
    { GameUiChatStore.Snapshot value; Check(store.TryGet(size,out value),"Missing cached resolution"); return value.groups[0].tabs[0].name; }
    private static void NoTemps(string directory)
    { Check(!Directory.GetFiles(directory,GameUiChatStore.FileName+".*.tmp").Any(),"Temporary save file leaked"); }
    private static void RoundTrips(string root)
    {
        string directory=Path.Combine(root,"roundtrip");
        var store=new GameUiChatStore(directory);
        GameUiChatStore.Snapshot value;
        Check(!store.TryGet(Sizes[0],out value) && value==null,"Missing file fabricated chat topology");
        Check(!Directory.Exists(directory),"Read created a directory");
        foreach (Size size in Sizes)
        {
            var saved=Snapshot(size);
            saved.groups[0].tabs[0].name=size.ToString();
            Check(store.TrySave(saved),"Save failed: "+store.LastDiagnostic);
            saved.groups[0].tabs[0].name="mutated caller";
            var reloaded=new GameUiChatStore(directory);
            Check(reloaded.TryGet(size,out value),"Saved resolution not found");
            Check(value.groups[0].tabs[0].name==size.ToString(),"Save did not deep-copy caller");
            Check(value.groups[0].selected==1 && value.groups[1].selected==-1,"Selection not retained");
            Check(value.groups[0].tabs[0].flags==4095 && value.groups[0].tabs[1].name=="队伍😀","Flags or Unicode changed");
            value.groups[0].tabs[0].name="mutated reader";
            Check(GroupName(reloaded,size)==size.ToString(),"Read exposed mutable cache");
            rows.Add("resolution|"+size.Width+"x"+size.Height+"|roundtrip");
        }
        var all=new GameUiChatStore(directory);
        foreach (Size size in Sizes) Check(GroupName(all,size)==size.ToString(),"Saving another resolution removed earlier entry");
        Check(!all.TryGet(new Size(800,600),out value),"Unsupported resolution resolved");
        var empty=new GameUiChatStore.Snapshot { width=1024,height=768,groups=new GameUiChatStore.Group[0] };
        Check(all.TrySave(empty),"Explicit empty topology rejected");
        Check(new GameUiChatStore(directory).TryGet(Sizes[0],out value) && value.groups.Length==0,"Empty topology turned into defaults");
        Check(GroupName(new GameUiChatStore(directory),Sizes[1])==Sizes[1].ToString(),"Empty save erased another resolution");
        NoTemps(directory);
    }
    private static void FlagCombinations(string root)
    {
        string directory=Path.Combine(root,"flags"); var store=new GameUiChatStore(directory);
        for (int first=0; first<4096; first+=256)
        {
            var value=Snapshot(Sizes[0]); value.groups=new[] { value.groups[0] };
            value.groups[0].tabs=Enumerable.Range(first,256).Select(flags=>new GameUiChatStore.Tab {name="频道"+flags,flags=flags}).ToArray();
            value.groups[0].selected=255;
            Check(store.TrySave(value),"Valid filter combination rejected");
            GameUiChatStore.Snapshot actual;
            Check(new GameUiChatStore(directory).TryGet(Sizes[0],out actual),"Flags reload failed");
            for(int i=0;i<256;i++) Check(actual.groups[0].tabs[i].flags==first+i && actual.groups[0].tabs[i].name=="频道"+(first+i),"Channel flag/name order changed");
            Check(actual.groups[0].selected==255,"Last selected page changed");
        }
        rows.Add("flags|4096|roundtrip");
    }
    private static void InvalidSnapshots(string root)
    {
        string directory=Path.Combine(root,"invalid-save"); var store=new GameUiChatStore(directory);
        Check(store.TrySave(Snapshot(Sizes[0])),"Initial invalid-case seed failed");
        byte[] before=File.ReadAllBytes(store.FilePath); string cached=GroupName(store,Sizes[0]);
        var cases=new Dictionary<string,Action<GameUiChatStore.Snapshot>> {
            {"resolution",s=>s.width=1023}, {"null-groups",s=>s.groups=null}, {"null-group",s=>s.groups[0]=null},
            {"group-count",s=>s.groups=Enumerable.Range(0,65).Select(i=>s.groups[0]).ToArray()},
            {"duplicate-id",s=>s.groups[1].id=s.groups[0].id}, {"bad-id",s=>s.groups[0].id="bad"},
            {"null-id",s=>s.groups[0].id=null}, {"uppercase-id",s=>s.groups[0].id=s.groups[0].id.ToUpperInvariant()},
            {"null-tabs",s=>s.groups[0].tabs=null}, {"empty-tabs",s=>s.groups[0].tabs=new GameUiChatStore.Tab[0]},
            {"null-tab",s=>s.groups[0].tabs[0]=null}, {"null-name",s=>s.groups[0].tabs[0].name=null},
            {"long-name",s=>s.groups[0].tabs[0].name=new string('x',257)},
            {"invalid-utf16",s=>s.groups[0].tabs[0].name="\ud800"},
            {"negative-flags",s=>s.groups[0].tabs[0].flags=-1}, {"flags-overflow",s=>s.groups[0].tabs[0].flags=4096},
            {"negative-selected",s=>s.groups[0].selected=-2}, {"selected-overflow",s=>s.groups[0].selected=2},
            {"small-width",s=>s.groups[1].width=105}, {"small-height",s=>s.groups[1].height=80},
            {"negative-x",s=>s.groups[1].x=-1}, {"negative-y",s=>s.groups[1].y=-1},
            {"right-overflow",s=>s.groups[1].x=1024}, {"bottom-overflow",s=>s.groups[1].y=768},
            {"integer-overflow",s=>s.groups[1].width=int.MaxValue}, {"two-primary",s=>s.groups[1].primary=true},
            {"secondary-docked",s=>s.groups[1].floating=false}, {"docked-x",s=>s.groups[0].x++},
            {"docked-y",s=>s.groups[0].y++}, {"docked-width",s=>s.groups[0].width++},
            {"docked-height",s=>s.groups[0].height++},
            {"group-tab-limit",s=>s.groups[0].tabs=Enumerable.Range(0,257).Select(i=>new GameUiChatStore.Tab { name="x" }).ToArray()},
            {"total-tab-limit",s=>s.groups[0].tabs=Enumerable.Range(0,256).Select(i=>new GameUiChatStore.Tab { name="x" }).ToArray()}
        };
        foreach(var item in cases)
        {
            var value=Snapshot(Sizes[0]); item.Value(value);
            Check(!store.TrySave(value),"Invalid snapshot accepted: "+item.Key);
            Check(!string.IsNullOrEmpty(store.LastDiagnostic),"Failed save lacks diagnostic: "+item.Key);
            Check(before.SequenceEqual(File.ReadAllBytes(store.FilePath)),"Failed save changed disk: "+item.Key);
            Check(GroupName(store,Sizes[0])==cached,"Failed save changed cache: "+item.Key);
            NoTemps(directory); rows.Add("invalid-save|"+item.Key+"|retained");
        }
        Check(!store.TrySave(null),"Null snapshot accepted");
        var boundary=Snapshot(Sizes[0]); boundary.groups[1].width=106; boundary.groups[1].height=81;
        boundary.groups[1].x=1024-106; boundary.groups[1].y=768-81; boundary.groups[0].tabs[0].name=new string('中',256);
        Check(store.TrySave(boundary),"Valid exact boundaries rejected");
    }
    private static void InvalidDocuments(string root)
    {
        string directory=Path.Combine(root,"invalid-read"); Directory.CreateDirectory(directory);
        var store=new GameUiChatStore(directory); string good=Serialize(Snapshot(Sizes[0]));
        var cases=new Dictionary<string,string> {
            {"empty",""}, {"null-root","null"}, {"root-array","[]"}, {"broken","{"},
            {"schema",good.Replace("\"schemaVersion\":1","\"schemaVersion\":2")},
            {"style",good.Replace("OfficialV1","Legacy")},
            {"unknown",good.Replace("\"schemaVersion\":1","\"schemaVersion\":1,\"extra\":0")},
            {"missing",good.Replace("\"styleVersion\":\"OfficialV1\",", "")},
            {"float",good.Replace("\"schemaVersion\":1","\"schemaVersion\":1.0")},
            {"string-int",good.Replace("\"schemaVersion\":1","\"schemaVersion\":\"1\"")},
            {"null-resolutions",good.Replace("\"resolutions\":[", "\"resolutions\":null,\"ignored\":[")},
            {"string-bool",good.Replace("\"primary\":true","\"primary\":\"true\"")},
            {"unknown-tab",good.Replace("\"flags\":4095","\"flags\":4095,\"unknown\":0")},
            {"wrong-name",good.Replace("\"name\":\"\"","\"name\":0")},
            {"wrong-groups",good.Replace("\"groups\":[", "\"groups\":{},\"ignored\":[")},
            {"duplicate-resolution",Json.Serialize(new GameUiChatStore.Document {schemaVersion=1,styleVersion="OfficialV1",resolutions=new[] {Snapshot(Sizes[0]),Snapshot(Sizes[0])}})},
            {"seven-resolutions",Json.Serialize(new GameUiChatStore.Document {schemaVersion=1,styleVersion="OfficialV1",resolutions=Enumerable.Range(0,7).Select(i=>Snapshot(Sizes[i%6])).ToArray()})},
            {"oversized",new string(' ',1048577)}
        };
        foreach (var item in cases)
        {
            File.WriteAllText(store.FilePath,good,new UTF8Encoding(false)); store.Reload();
            Check(store.LastDiagnostic==null,"Good file did not recover");
            File.WriteAllText(store.FilePath,item.Value,new UTF8Encoding(false)); byte[] before=File.ReadAllBytes(store.FilePath);
            store.Reload(); GameUiChatStore.Snapshot value;
            Check(store.LastDiagnostic!=null && !store.TryGet(Sizes[0],out value),"Bad document silently retained or applied: "+item.Key);
            Check(before.SequenceEqual(File.ReadAllBytes(store.FilePath)),"Bad read rewrote disk: "+item.Key);
            rows.Add("invalid-read|"+item.Key+"|ignored");
        }
        foreach(var encoded in new[] { new byte[] {0xc0,0xaf}, new byte[] {0xef,0xbb,0xbf,0xff}, Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(good)).ToArray() })
        {
            File.WriteAllBytes(store.FilePath,encoded); store.Reload();
            Check(store.LastDiagnostic!=null,"Invalid UTF-8 or UTF-16 accepted");
            Check(encoded.SequenceEqual(File.ReadAllBytes(store.FilePath)),"Encoding failure rewrote disk");
        }
        File.WriteAllText(store.FilePath,good,new UTF8Encoding(true)); store.Reload();
        Check(store.LastDiagnostic==null && GroupName(store,Sizes[0])==Snapshot(Sizes[0]).groups[0].tabs[0].name,"UTF-8 BOM not accepted");
        File.Delete(store.FilePath); store.Reload(); GameUiChatStore.Snapshot missing;
        Check(store.LastDiagnostic==null && !store.TryGet(Sizes[0],out missing),"Removed file left stale cache");
        NoTemps(directory);
    }
    private static void IoFailures(string root)
    {
        string directory=Path.Combine(root,"io"); var store=new GameUiChatStore(directory);
        Check(store.TrySave(Snapshot(Sizes[0])),"IO seed failed");
        byte[] before=File.ReadAllBytes(store.FilePath); string cached=GroupName(store,Sizes[0]);
        using(var locked=new FileStream(store.FilePath,FileMode.Open,FileAccess.Read,FileShare.None))
        {
            var next=Snapshot(Sizes[0]); next.groups[0].tabs[0].name="must not commit";
            Check(!store.TrySave(next),"Locked replace reported success");
            Check(GroupName(store,Sizes[0])==cached,"Failed replace updated cache");
            Check(store.LastDiagnostic!=null,"Locked replace lacks diagnostic");
            NoTemps(directory);
        }
        Check(before.SequenceEqual(File.ReadAllBytes(store.FilePath)),"Locked replace damaged previous bytes");
        using(var locked=new FileStream(store.FilePath,FileMode.Open,FileAccess.Read,FileShare.None))
        { store.Reload(); GameUiChatStore.Snapshot value; Check(store.LastDiagnostic!=null && !store.TryGet(Sizes[0],out value),"Locked read used stale cache"); }
        Check(before.SequenceEqual(File.ReadAllBytes(store.FilePath)),"Locked read changed disk");
        store.Reload(); Check(store.LastDiagnostic==null && GroupName(store,Sizes[0])==cached,"Unlock reload did not recover");
        string blocker=Path.Combine(root,"file-as-directory"); File.WriteAllText(blocker,"sentinel");
        Check(!new GameUiChatStore(blocker).TrySave(Snapshot(Sizes[0])),"Invalid parent reported success");
        Check(File.ReadAllText(blocker)=="sentinel","Invalid parent was modified");
        rows.Add("io|exclusive-read-and-replace|retained");
    }
    public static int Main(string[] args)
    {
        try
        {
            string root=Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
            string native=Path.Combine(root,"Users.db"); byte[] sentinel=Enumerable.Range(0,256).Select(i=>(byte)i).ToArray(); File.WriteAllBytes(native,sentinel);
            RoundTrips(root); FlagCombinations(root); InvalidSnapshots(root); InvalidDocuments(root); IoFailures(root);
            Check(sentinel.SequenceEqual(File.ReadAllBytes(native)),"Store touched native database sentinel");
            File.WriteAllLines(Path.Combine(root,"chat-store-results.txt"),rows,new UTF8Encoding(false));
            Console.WriteLine("Chat store: "+checks+" checks passed; "+rows.Count+" evidence rows."); return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
