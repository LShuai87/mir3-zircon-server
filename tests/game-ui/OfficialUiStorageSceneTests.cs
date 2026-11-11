using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Client;
using Client.Controls;
using Client.Envir;
using Client.Models;
using Client.Scenes;
using Library;

internal static class OfficialUiStorageSceneEntry
{
    [STAThread] private static int Main(string[] args) => OfficialUiFullSceneTests.RunStorage(args);
}

internal static partial class OfficialUiFullSceneTests
{
    internal static int RunStorage(string[] args)
    {
        try
        {
            string package=args[0], data=args[1], evidence=args[2], candidate=args[3];
            Directory.CreateDirectory(evidence);
            Check(Hash(candidate)=="471169dfa093b521f3c700c5a993e4e763aa17a45b2e16f440d961c0c7bf4cec","Unexpected isolated warehouse package");
            Config.FullScreen=false; Config.GameSize=new Size(1024,768); Config.LogChat=false;
            Config.SystemVolume=Config.MusicVolume=Config.PlayerVolume=Config.MonsterVolume=Config.MagicVolume=0;
            CEnvir.Now=DateTime.UtcNow; Directory.CreateDirectory("Data"); InitializeDatabase();
            var map=BlankMap(Path.GetFullPath("Map")); LoadLibraries(data,evidence); CEnvir.Storage=new ClientUserItem[1000];
            string installed=Path.Combine(Environment.CurrentDirectory,GameUiOfficialV1Catalog.PackageRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(installed)); File.Copy(package,installed);
            using(var target=new TargetForm {ClientSize=Config.GameSize})
            {
                CEnvir.Target=target; DXManager.Create();
                try
                {
                    using(var owner=(GameUiStartupSession)typeof(GameUiStartupSession).GetMethod("Prepare",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{"OfficialV1",Environment.CurrentDirectory,true}))
                    using(var art=new MirLibrary(candidate))
                    {
                        Check(owner.EffectiveStyle==GameUiStyle.OfficialV1,"Official fixture failed preflight: "+owner.Diagnostic); Publish(owner); art.ReadLibrary();
                        Check(art.Images.Length==1&&art.GetSize(0)==new Size(508,480),"Candidate geometry differs");
                        using(var scene=new GameScene(Config.GameSize))
                        {
                            // Exclude animated world pixels behind the transparent candidate edge from the reset comparison.
                            scenes++; Populate(scene,map); scene.MapControl.Visible=false; scene.StorageSize=131;
                            var d=scene.StorageBox;
                            var originalCells=d.Grid.Grid.ToArray(); var filterParent=d.ItemNameTextBox.Parent;
                            Check(d.GameUiStorageLayout!=null&&d.WindowSkin!=null,"Full candidate entry did not install warehouse");
                            Check(d.WindowSkin.FrameIndex==93&&ReferenceEquals(d.WindowSkin.Library,owner.Package.Library),"Warehouse is not borrowing the verified full catalog");
                            Check(d.Size==new Size(508,480)&&d.Location==new Point(220,86),"Warehouse initial placement differs");
                            {
                                d.Visible=true; d.BringToFront();
                                Check(scene.InventoryBox.Visible,"Native inventory linkage lost");
                                Check(scene.DisplayArea.Contains(d.DisplayArea)&&scene.DisplayArea.Contains(scene.InventoryBox.DisplayArea),"Warehouse/inventory outside 1024x768");
                                Check(!d.DisplayArea.IntersectsWith(scene.InventoryBox.DisplayArea),"Warehouse overlaps inventory");
                                Check(d.Grid.Grid.SequenceEqual(originalCells),"Skin installation rebuilt slots");
                                string first=Path.Combine(evidence,"warehouse-before-reset.png"); Render(scene,first);
                                Reset(Config.GameSize); scene.Size=Config.GameSize;
                                string after=Path.Combine(evidence,"warehouse-after-reset.png"); Render(scene,after);
                                SamePixels(first,after,d.DisplayArea,"warehouse candidate");
                                Check(d.Grid.Grid.SequenceEqual(originalCells)&&d.GameUiStorageLayout!=null,"Device reset recreated grid or lost layout");
                                d.StorageScrollBar.Value=1; Check(d.Grid.ScrollValue==1&&originalCells[130].Visible&&originalCells[130].Enabled,"Dynamic last valid slot is not reachable");
                                foreach(int slot in new[]{13,130})
                                {
                                    var cell=originalCells[slot]; var area=cell.DisplayArea; var p=new Point(area.Left+18,area.Top+18);
                                    Call(typeof(TargetForm),target,"OnMouseMove",new MouseEventArgs(MouseButtons.None,0,p.X,p.Y,0));
                                    Check(DXControl.MouseControl==cell,"Coordinate did not hit original storage slot "+slot);
                                }
                                Check(!originalCells[131].Enabled,"Capacity boundary enabled extra slot");
                                Render(scene,Path.Combine(evidence,"warehouse-scrolled.png"));
                                d.ItemNameTextBox.TextBox.Text="不存在的物品";
                                Check(originalCells.All(c=>!c.Enabled),"Native name filter not applied to empty slots");
                                Render(scene,Path.Combine(evidence,"warehouse-filtered.png"));
                                d.ClearButton.InvokeMouseClick(); Check(originalCells[130].Enabled,"Native clear did not restore last valid slot");
                            }
                            var cells=d.Grid.Grid.ToArray();
                            foreach(var viewport in new[]{new Size(1366,768),new Size(1920,1080),new Size(1024,768)})
                            {
                                Config.GameSize=viewport;Reset(viewport);scene.Size=viewport;
                                Check(d.Location==new Point(viewport.Width-804,86)&&d.Size==new Size(508,480),"Warehouse lost resolution-aware anchor");
                                Check(!d.DisplayArea.IntersectsWith(scene.InventoryBox.DisplayArea)&&d.DisplayArea.Bottom<=viewport.Height-202,"Warehouse overlaps inventory/HUD after resize");
                                Check(d.Grid.Grid.SequenceEqual(cells)&&d.GameUiStorageLayout!=null,"Resize replaced original warehouse cells/layout");
                            }
                            var position=d.Location;
                            scene.NPCBox.Visible=true;scene.NPCBox.Visible=false;
                            Check(d.Location==position,"NPC open/close overwrote catalog warehouse position");
                            d.CloseButton.InvokeMouseClick(); Check(!d.Visible,"Original warehouse close callback lost");
                        }
                        Publish(null);
                    }
                }
                finally{Publish(null);DXManager.Unload();}
            }
            Check(!GameUiOfficialV1Catalog.RuntimeReady,"Production gate was enabled");
            File.WriteAllLines(Path.Combine(evidence,"reset-pixel-probes.txt"),pixelProbes);
            Console.WriteLine("Storage candidate offline scene: "+checks+" checks; "+renders+" presented frames; "+resets+" device resets; "+comparedPixels+" identical warehouse pixels; 1024x768 original-cell coordinate hits and native filters. Installed through full verified catalog entry; no live server or final visual acceptance."); return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally{foreach(var library in CEnvir.LibraryList.Values)library.Dispose();CEnvir.LibraryList.Clear();CEnvir.Target=null;CEnvir.Storage=null;}
    }
}
