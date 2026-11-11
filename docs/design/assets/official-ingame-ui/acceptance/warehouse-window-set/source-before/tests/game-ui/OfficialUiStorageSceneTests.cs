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
                            var d=scene.StorageBox; var size=d.Size; var nativeLocation=d.Location;
                            var originalCells=d.Grid.Grid.ToArray(); var filterParent=d.ItemNameTextBox.Parent;
                            var nativePositions=new[]{filterParent.Location,d.Grid.Location,d.StorageScrollBar.Location};
                            var skin=new GameUiWindowSkin(art,0,new Padding(16,48,16,51),Padding.Empty,Padding.Empty,new Size(508,480),true,
                                new Rectangle(24,10,440,18),new Rectangle(8,0,460,38),new Rectangle(478,10,18,18));
                            try
                            {
                                using(d.SuppressLayoutWrites())
                                {
                                    d.WindowSkin=skin; d.UseGameUiStorage(GameUiStorageLayout.CreateOfficialCandidate()); d.Location=new Point(8,8);
                                }
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
                            finally
                            {
                                using(d.SuppressLayoutWrites())
                                {
                                    d.UseGameUiStorage(null); d.WindowSkin=null; d.Size=size; d.Location=nativeLocation;
                                }
                            }
                            Check(d.GameUiStorageLayout==null&&d.WindowSkin==null&&d.Size==size&&d.Grid.Grid.SequenceEqual(originalCells),"Candidate teardown did not restore original warehouse");
                            Check(new[]{filterParent.Location,d.Grid.Location,d.StorageScrollBar.Location}.SequenceEqual(nativePositions),"Native positions not restored after rendering");
                            d.CloseButton.InvokeMouseClick(); Check(!d.Visible,"Original warehouse close callback lost");
                        }
                        Publish(null);
                    }
                }
                finally{Publish(null);DXManager.Unload();}
            }
            Check(!GameUiOfficialV1Catalog.RuntimeReady,"Production gate was enabled");
            File.WriteAllLines(Path.Combine(evidence,"reset-pixel-probes.txt"),pixelProbes);
            Console.WriteLine("Storage candidate offline scene: "+checks+" checks; "+renders+" presented frames; "+resets+" device resets; "+comparedPixels+" identical warehouse pixels; 1024x768 original-cell coordinate hits and native filters. Isolated uninstalled skin; no live server or final visual acceptance."); return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally{foreach(var library in CEnvir.LibraryList.Values)library.Dispose();CEnvir.LibraryList.Clear();CEnvir.Target=null;CEnvir.Storage=null;}
    }
}
