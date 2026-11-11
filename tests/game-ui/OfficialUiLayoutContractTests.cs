using System;
using System.Drawing;
using Client.Models.GameUi;
class LayoutReadback {
 static void Require(bool value,string detail) { if(!value) throw new Exception(detail); }
 static int Main() {
  Require(!OfficialUiLayoutContract.ProductionBindingAllowed,"draft must remain non-production");
  Require(OfficialUiLayoutContract.Nodes.Count==237,"node count");
  var ids=new System.Collections.Generic.HashSet<string>(); int controls=0, equipment=0, tabs=0;
  foreach(var node in OfficialUiLayoutContract.Nodes) {
   Require(ids.Add(node.Id),"duplicate node");
   if(node.Id.StartsWith("character.equipment.", StringComparison.Ordinal)) {
    Require(node.Owner=="character" && node.Page=="character", "equipment must stay on character homepage");
    Require(node.Rect.Size==new Size(36,36) && node.HitRect==node.Rect, "equipment hit rectangle");
    equipment++;
   }
   if(node.Id.StartsWith("character.tab.", StringComparison.Ordinal)) tabs++;
   Require(node.Page!="extraEquipment", "unexpected extra equipment page");
   if(node.HitRect.HasValue) { Require(node.Rect.Contains(node.HitRect.Value),"hit region");controls++; }
  }
  Require(controls==111,"control count");
  foreach(var id in new[]{"minimap.collapse","minimap.footer.bigMap","minimap.footer.transparency","minimap.footer.zoom"}) {
   bool found=false;
   foreach(var node in OfficialUiLayoutContract.Nodes)
    if(node.Id==id) found=node.Owner=="minimap" && node.Kind=="control" && node.HitRect.HasValue;
   Require(found,"missing minimap control: "+id);
  }
  Require(equipment==16,"all 16 equipment cells must remain on the homepage");
  Require(tabs==3 && ids.Contains("character.tab.character") && ids.Contains("character.tab.status") && ids.Contains("character.tab.hermit"),"three original tabs");
  int[,] sizes={{1024,768},{1366,768},{1280,800},{1440,900},{1600,900},{1920,1080}};
  string[] owners={"character","inventory","quickbar","minimap","chat","chatInput","orb","commands","readouts","experience"};
  int placements=0;
  for(int i=0;i<6;i++) foreach(var owner in owners) {
   Rectangle bounds; Require(OfficialUiLayoutContract.TryGetPlacement(sizes[i,0],sizes[i,1],owner,out bounds),"missing placement");
   Require(new Rectangle(0,0,sizes[i,0],sizes[i,1]).Contains(bounds),"placement out of screen"); placements++;
  }
  Rectangle missing;
  Require(!OfficialUiLayoutContract.TryGetPlacement(1024,768,"unknown",out missing)&&missing==Rectangle.Empty,"unknown owner");
  Require(!OfficialUiLayoutContract.TryGetPlacement(800,600,"character",out missing)&&missing==Rectangle.Empty,"unknown resolution");
  Console.WriteLine("PASS: compiled C# readback: "+ids.Count+" unique nodes, "+controls+" contained hit regions, "+placements+" placements, all 16 equipment cells on homepage, three tabs, unknown lookup guards, production disabled.");
  return 0;
 }
}
