using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;
using Client;
using Client.Controls;
using Client.Envir;
using Client.Models;
using Client.Scenes;
using Client.Scenes.Views;
using Library;
using Library.SystemModels;
using Client.UserModels;
using MirDB;
using UserObject = Client.Models.UserObject;
using SlimDX.Direct3D9;

// Real GameScene/UserObject on isolated synthetic data; native transitions use loopback TCP only.
internal static partial class OfficialUiFullSceneTests
{
    private static int checks, renders, resets, scenes;
    private static long comparedPixels;
    private static bool noScreenshots;
    private static readonly List<string> pixelProbes = new List<string> { "name|x|y|width|height|identicalPixels" };
    private static readonly BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    private static object Call(Type type, object owner, string name, params object[] args)
    {
        try { return type.GetMethod(name, owner == null ? Static : Instance).Invoke(owner, args); }
        catch (TargetInvocationException e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Instance).GetValue(owner);
    private static void Publish(GameUiStartupSession owner) => typeof(CEnvir).GetProperty("GameUiSession", Static).SetValue(null, owner);
    private static string Hash(string file) { using (var sha = SHA256.Create()) using (var stream = File.OpenRead(file)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
    private static void LoadLibraries(string data, string evidence)
    {
        var selected = new[] { LibraryFile.Interface, LibraryFile.GameInter, LibraryFile.Equip, LibraryFile.ProgUse,
            LibraryFile.Inventory, LibraryFile.StoreItems, LibraryFile.MagicIcon, LibraryFile.CBIcon,
            LibraryFile.M_Hum, LibraryFile.M_Hair };
        var records = new List<string> { "library|absolutePath|bytes|sha256" };
        foreach (var file in selected)
        {
            string location = Path.Combine(data, Path.GetFileName(Libraries.LibraryList[file]));
            Check(File.Exists(location), "Missing required read-only legacy library: " + location);
            var library = new MirLibrary(location);
            CEnvir.LibraryList.Add(file, library);
            library.ReadLibrary();
            Check(library.Loaded && library.Images != null, "Library did not load: " + location);
            records.Add(file + "|" + location + "|" + new FileInfo(location).Length + "|" + Hash(location));
        }
        File.WriteAllLines(Path.Combine(evidence, "legacy-resources.txt"), records);
    }
    private static void InitializeDatabase()
    {
        // MirDB discovers Entry/Executing/Calling assemblies. This independent test entry point
        // must register Client's empty user collections explicitly; no persisted game DB is read.
        var session = new Session(SessionMode.Users, @".\Data\") { BackUp = false };
        var collections = (System.Collections.IDictionary)typeof(Session).GetField("Collections", Instance).GetValue(session);
        foreach (var type in typeof(GameScene).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(DBObject))))
            if (!collections.Contains(type)) collections.Add(type, Activator.CreateInstance(typeof(DBCollection<>).MakeGenericType(type), session));
        CEnvir.Session = session;
        Globals.ItemInfoList = session.GetCollection<ItemInfo>(); Globals.DropInfoList = session.GetCollection<DropInfo>();
        Globals.MagicInfoList = session.GetCollection<MagicInfo>(); Globals.MapInfoList = session.GetCollection<MapInfo>();
        Globals.NPCPageList = session.GetCollection<NPCPage>(); Globals.MonsterInfoList = session.GetCollection<MonsterInfo>();
        Globals.StoreInfoList = session.GetCollection<StoreInfo>(); Globals.NPCInfoList = session.GetCollection<NPCInfo>();
        Globals.MovementInfoList = session.GetCollection<MovementInfo>(); Globals.QuestInfoList = session.GetCollection<QuestInfo>();
        Globals.QuestTaskList = session.GetCollection<QuestTask>(); Globals.CompanionInfoList = session.GetCollection<CompanionInfo>();
        Globals.CompanionLevelInfoList = session.GetCollection<CompanionLevelInfo>();
        CEnvir.KeyBinds = session.GetCollection<KeyBindInfo>(); CEnvir.WindowSettings = session.GetCollection<WindowSetting>();
        CEnvir.CastleInfoList = session.GetCollection<CastleInfo>(); CEnvir.CheckKeyBinds(); CEnvir.Loaded = true;
        // Original companion dialog assumes at least one system record even while hidden.
        Globals.CompanionInfoList.CreateNewObject().Available = true;
        Check(CEnvir.Session.GetCollection<ChatTabControlSetting>() != null, "Real typed chat settings collection missing");
    }
    private static MapInfo BlankMap(string directory)
    {
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "offline.map");
        using (var stream = File.Create(file)) using (var writer = new BinaryWriter(stream))
        {
            writer.Write(new byte[22]); writer.Write((short)128); writer.Write((short)128); writer.Write((short)0);
            for (int i = 0; i < 64 * 64; i++) { writer.Write((byte)255); writer.Write((ushort)0); }
            for (int i = 0; i < 128 * 128; i++)
            {
                writer.Write((byte)3); writer.Write((byte)0); writer.Write((byte)0);
                writer.Write((byte)255); writer.Write((byte)255);
                writer.Write((ushort)65535); writer.Write((ushort)65535);
                writer.Write(new byte[3]); writer.Write((byte)0); writer.Write((byte)0);
            }
        }
        Config.MapPath = directory + Path.DirectorySeparatorChar;
        var map = Globals.MapInfoList.CreateNewObject();
        map.FileName = "offline"; map.Description = "离线界面验证"; map.MiniMap = 0;
        return map;
    }
    private static void Populate(GameScene scene, MapInfo map)
    {
        DXControl.ActiveScene = scene;
        scene.MapControl.MapInfo = map;
        Check(scene.MapControl.Cells != null && scene.MapControl.Width == 128, "Normal map loader did not load synthetic map");
        var info = new StartInformation {
            Index = 1, ObjectID = 1, Name = "离线测试角色", GuildName = "", GuildRank = "", NameColour = Color.White,
            Class = MirClass.Warrior, Gender = MirGender.Male, Location = new Point(64,64), Direction = MirDirection.Down,
            Level = 35, CurrentHP = 650, CurrentMP = 240, Gold = 123456, Experience = 12345, DayTime = 1,
            HairColour = Color.Brown, ArmourColour = Color.White, InSafeZone = true,
            Items = new List<ClientUserItem>(), BeltLinks = new List<ClientBeltLink>(), AutoPotionLinks = new List<ClientAutoPotionLink>(),
            Magics = new List<ClientUserMagic>(), Buffs = new List<ClientBuffInfo>(), Quests = new List<ClientUserQuest>(),
            CompanionUnlocks = new List<int>(), Companions = new List<ClientUserCompanion>()
        };
        scene.QuestLog = info.Quests;
        scene.NPCAdoptCompanionBox.AvailableCompanions = info.AvailableCompanions;
        scene.NPCAdoptCompanionBox.SelectedIndex = 0;
        scene.NPCAdoptCompanionBox.RefreshUnlockButton();
        scene.NPCCompanionStorageBox.Companions = info.Companions;
        scene.NPCCompanionStorageBox.UpdateScrollBar();
        scene.Companion = info.Companions.FirstOrDefault();
        scene.User = new UserObject(info);
        scene.User.Stats[Stat.Health] = 1000; scene.User.Stats[Stat.Mana] = 400;
        scene.User.Stats[Stat.MaxDC] = 80; scene.User.Stats[Stat.MinDC] = 40;
        scene.UserChanged();
        scene.ReceiveChat("离线界面验证：聊天继续使用原有内容与样式。", MessageType.System);
        scene.ReceiveChat("[测试角色] 原有聊天行，不经过服务器发送。", MessageType.Normal);
        Check(CEnvir.Connection == null, "Offline fixture created a connection");
        Check(scene.Equipment.Length == 16 && scene.InventoryBox.Grid.Grid.Length == 49 && scene.BeltBox.Links.Length == 10, "Original slot counts changed");
        Check(scene.User.BodyLibrary != null && scene.User.HairLibrary != null, "Synthetic avatar lacks real body/hair libraries");
    }
    private static void Render(GameScene scene, string output)
    {
        CEnvir.Now = DateTime.UtcNow;
        scene.Process();
        int before = (int)typeof(CEnvir).GetField("FPSCounter", Static).GetValue(null);
        Call(typeof(CEnvir), null, "RenderGame");
        Check((int)typeof(CEnvir).GetField("FPSCounter", Static).GetValue(null) == before + 1, "Actual RenderGame did not present a complete frame");
        Check(!DXManager.DeviceLost, "Render path latched DeviceLost");
        var owner = (GameUiStartupSession)typeof(CEnvir).GetProperty("GameUiSession", Static).GetValue(null);
        Check(owner == null || !owner.RequiresRestart, "Full-scene render faulted Official session: " + owner?.Diagnostic);
        if (output != null && !noScreenshots)
        {
            // Capture before Present; discard-swap backbuffer contents after Present are unspecified.
            DXManager.Device.Clear(ClearFlags.Target, Color.Black, 1, 0);
            DXManager.Device.BeginScene(); DXManager.Sprite.Begin(SpriteFlags.AlphaBlend);
            try { scene.Draw(); } finally { DXManager.Sprite.End(); DXManager.Device.EndScene(); }
            Surface.ToFile(DXManager.MainSurface, output, ImageFileFormat.Png);
            using (var image = Image.FromFile(output)) Check(image.Size == Config.GameSize, "Capture/backbuffer dimensions differ");
            DXManager.Device.Present();
        }
        renders++;
    }
    private static void SamePixels(string before, string after, Rectangle region, string name)
    {
        if (noScreenshots) return;
        using (var a = new Bitmap(before)) using (var b = new Bitmap(after))
        {
            int nonBlack = 0;
            for (int y = region.Top; y < region.Bottom; y++) for (int x = region.Left; x < region.Right; x++)
            {
                int pixel = a.GetPixel(x,y).ToArgb();
                if ((pixel & 0xffffff) != 0) nonBlack++;
                if (pixel != b.GetPixel(x,y).ToArgb()) throw new Exception("Reset changed " + name + " pixel at " + x + "," + y);
            }
            comparedPixels += (long)region.Width * region.Height;
            pixelProbes.Add(name + "|" + region.X + "|" + region.Y + "|" + region.Width + "|" + region.Height + "|" + ((long)region.Width * region.Height));
            Check(nonBlack > 100, "Pixel probe was empty: " + name);
            Check(true, "Reset restored identical " + name + " pixels");
        }
    }
    private static void Reset(Size size)
    {
        Config.GameSize = size; CEnvir.Target.ClientSize = size;
        DXManager.ResetDevice(); DXManager.AttemptReset();
        Check(!DXManager.DeviceLost && DXManager.Parameters.BackBufferWidth == size.Width && DXManager.Parameters.BackBufferHeight == size.Height,
            "Actual Device.Reset did not restore expected backbuffer");
        resets++;
    }
    private static void ExerciseWeaponPreview(GameScene scene, string evidence)
    {
        if (noScreenshots) return;
        var character = scene.CharacterBox;
        var original = character.Location;
        var cell = character.Grid[(int)EquipmentSlot.Weapon];
        var previous = cell.Item;
        var info = Globals.ItemInfoList.CreateNewObject();
        info.ItemName = "左侧武器裁剪验证"; info.ItemType = ItemType.Weapon; info.Image = 1040;
        info.RequiredClass = RequiredClass.All; info.Durability = 1000;
        try
        {
            int placement = 0;
            foreach (var position in new[] { new Point(8,8), new Point(320,100) })
            {
                character.Location = position;
                cell.Item = null;
                string before = Path.Combine(evidence,"weapon-empty-"+placement+".png");
                Render(scene,before);
                cell.Item = new ClientUserItem(info,1) { Index=91040, Colour=Color.White };
                string after = Path.Combine(evidence,"weapon-visible-"+placement+".png");
                Render(scene,after);
                var left = new Rectangle(character.DisplayArea.X+18,character.DisplayArea.Y+102,36,92);
                int changed = 0;
                using (var a = new Bitmap(before)) using (var b = new Bitmap(after))
                    for (int y=left.Top;y<left.Bottom;y++) for (int x=left.Left;x<left.Right;x++)
                        if (a.GetPixel(x,y)!=b.GetPixel(x,y)) changed++;
                Check(changed>50,"Native weapon left pixels are still clipped: "+changed);
                pixelProbes.Add("weapon-left-visible|"+left.X+"|"+left.Y+"|36|92|changed="+changed);
                for (int slot=1;slot<character.Grid.Length;slot++)
                {
                    Rectangle frame=character.Grid[slot].DisplayArea; frame.Inflate(1,1);
                    SamePixels(before,after,frame,"weapon preserved equipment "+slot);
                }
                var attributes=new Rectangle(character.DisplayArea.X+252,character.DisplayArea.Y+96,147,197);
                SamePixels(before,after,attributes,"weapon preserved attributes");
                placement++;
            }
        }
        finally { cell.Item=previous; character.Location=original; }
    }
    private static void ExerciseScene(GameUiStartupSession owner, MapInfo map, string evidence, string prefix, bool comprehensive)
    {
        using (var scene = new GameScene(Config.GameSize))
        {
            scenes++; Populate(scene, map);
            bool official = owner.EffectiveStyle == GameUiStyle.OfficialV1;
            Check((scene.MainPanel.GameUiHudLayout != null) == official, "Constructor chose wrong UI style");
            Check((scene.CharacterBox.WindowSkin != null) == official && (scene.ChatTextBox.GameUiInputLayout != null) == official, "Constructor did not install all adapted participants");
            Check(scene.NPCBox.WindowSkin == null && scene.ConfigBox.WindowSkin == null, "Excluded NPC or settings window was skinned");
            var cells = scene.CharacterBox.Grid.ToArray(); var inventory = scene.InventoryBox.Grid.Grid.ToArray(); var belt = scene.BeltBox.Grid.Grid.ToArray();
            var links = scene.BeltBox.Links; var input = scene.ChatTextBox.TextBox; var text = input.TextBox;
            text.Text = "未发送中文草稿 English"; text.Select(2, 4); var hwnd = text.Handle;
            var tabs = ChatTab.Tabs.ToArray(); var histories = tabs.Select(t=>t.History.ToArray()).ToArray();
            Render(scene, Path.Combine(evidence, prefix + "-hud.png"));
            scene.CharacterBox.Visible = true; scene.InventoryBox.Visible = true;
            Render(scene, Path.Combine(evidence, prefix + "-panels.png"));
            if (comprehensive)
            {
                var tabControl = Field<DXTabControl>(scene.CharacterBox, "TabControl");
                var pages = new[] { "CharacterTab", "StatsTab", "HermitTab" };
                foreach (string page in pages)
                {
                    var tab = Field<DXTab>(scene.CharacterBox, page);
                    Check(tab != null, "Missing character page " + page);
                    if (official) scene.CharacterBox.ShowGameUiCompanion(page == "CharacterTab" ? (GameUiCharacterPage?)null :
                        page == "StatsTab" ? GameUiCharacterPage.Status : GameUiCharacterPage.Hermit);
                    else tabControl.SelectedTab = tab;
                    Render(scene, Path.Combine(evidence, prefix + "-" + page + ".png"));
                }
                tabControl.SelectedTab = Field<DXTab>(scene.CharacterBox, "CharacterTab");
                if (official) scene.CharacterBox.ShowGameUiCompanion(null);
                foreach (var size in new[] { new Size(1024,768), new Size(1366,768), new Size(1280,800), new Size(1440,900), new Size(1600,900), new Size(1920,1080), new Size(1024,768) })
                {
                    Reset(size); scene.Size = size;
                    Render(scene, Path.Combine(evidence, prefix + "-reset-" + size.Width + "x" + size.Height + ".png"));
                    Check(scene.CharacterBox.Grid.SequenceEqual(cells) && scene.InventoryBox.Grid.Grid.SequenceEqual(inventory) && scene.BeltBox.Grid.Grid.SequenceEqual(belt), "Reset/resize replaced original cells");
                    Check(ReferenceEquals(scene.BeltBox.Links, links), "Reset replaced belt data");
                    Check(scene.ChatTextBox.TextBox == input && text.Handle == hwnd && text.Text == "未发送中文草稿 English" && text.SelectionStart == 2 && text.SelectionLength == 4, "Reset/resize changed native input identity/draft/selection");
                    Check(ChatTab.Tabs.SequenceEqual(tabs), "Reset/resize changed chat tabs");
                    for (int i = 0; i < tabs.Length; i++) Check(tabs[i].History.SequenceEqual(histories[i]), "Reset/resize changed chat history");
                    Check(!owner.RequiresRestart && owner.Package != null && !owner.Package.IsDisposed, "Reset released/faulted package");
                    if (size == new Size(1024,768))
                    {
                        string baseline = Path.Combine(evidence, prefix + "-panels.png");
                        string reset = Path.Combine(evidence, prefix + "-reset-1024x768.png");
                        SamePixels(baseline, reset, scene.CharacterBox.DisplayArea, "character panel");
                        SamePixels(baseline, reset, scene.InventoryBox.DisplayArea, "inventory panel");
                    }
                }
                // Dragging updates persisted layout anchors. Exercise it only after the reset
                // pixel-baseline checks, which intentionally assume no user window movement.
                if (official)
                {
                    ExerciseWeaponPreview(scene, evidence);
                    ExerciseCharacterCompanions(scene, evidence, prefix);
                }
            }
        }
        DXControl.ActiveScene = null;
        Check(GameScene.Game == null && ChatTab.Tabs.Count == 0, "Real scene teardown leaked scene or chat tabs");
        Check(!owner.RequiresRestart && !owner.IsDisposed, "Scene exit disposed/faulted application session");
    }
    private static void CompanionHit(GameScene scene, DXControl target, bool click)
    {
        var point = new Point(target.DisplayArea.X + target.Size.Width / 2, target.DisplayArea.Y + target.Size.Height / 2);
        scene.OnMouseMove(new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.None,0,point.X,point.Y,0));
        Check(DXControl.MouseControl == target, "Native companion hit route intercepted: " + target.GetType().Name + " " + target.DisplayArea + " hint=" + target.Hint + " actual=" + DXControl.MouseControl?.GetType().Name + " " + DXControl.MouseControl?.DisplayArea);
        if (click)
        {
            Check(target.IsEnabled, "Cannot click a disabled companion action");
            var e = new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left,1,point.X,point.Y,0);
            target.OnMouseDown(e); target.OnMouseUp(e); target.OnMouseClick(e);
        }
        DXControl.MouseControl = null;
    }
    private static void ExerciseCharacterCompanions(GameScene scene, string evidence, string prefix)
    {
        var character = scene.CharacterBox;
        var tabs = Field<DXTabControl>(character, "TabControl");
        var role = Field<DXTab>(character, "CharacterTab");
        var status = Field<DXButton>(character, "gameUiStatusButton");
        var hermit = Field<DXButton>(character, "gameUiHermitButton");
        var window = character.GameUiCompanionWindow;
        var original = character.Location;
        Check(window != null && window.Parent == scene && window.Size == new Size(296,436), "Companion must use native UI[130] as a sibling window");
        Check(!role.TabButton.Visible && !Field<DXTab>(character,"StatsTab").TabButton.Visible && !Field<DXTab>(character,"HermitTab").TabButton.Visible, "Old in-place tab buttons remain visible");
        Check(status.Location == new Point(288,66) && hermit.Location == new Point(346,66) && status.Size == new Size(56,22), "Native small buttons differ from reference geometry");
        Check(status.ImageStates != null && hermit.ImageStates != null, "Small buttons lack native frame states");
        CompanionHit(scene,status,true);
        Check(character.GameUiCompanionPage == GameUiCharacterPage.Status && window.IsVisible && tabs.SelectedTab == role && role.IsVisible, "Status button replaced character instead of opening companion");
        Check(status.Pressed && !hermit.Pressed, "Status selected state missing");
        var values = Field<Dictionary<Stat,DXLabel>>(character,"DisplayStats");
        var statusValue = values[Stat.LifeSteal];
        Check(statusValue.IsVisible && statusValue.Parent.Parent == window && values[Stat.MaxDC].Parent == role, "Real stat controls were not routed to the correct card");
        Check(window.Location == new Point(character.Location.X+character.Size.Width,character.Location.Y), "Companion did not dock flush right");
        var oldValueText = statusValue.Text;
        statusValue.Text = "12345.67%";
        Check(statusValue.Text == "12345.67%" && statusValue.IsVisible, "Companion lost live label identity");
        statusValue.Text = oldValueText;
        Render(scene,Path.Combine(evidence,prefix+"-companion-status.png"));
        CompanionHit(scene,status,true);
        Check(!window.Visible && character.GameUiCompanionPage == null && role.IsVisible, "Second status click must collapse only the companion");
        CompanionHit(scene,hermit,true);
        Check(character.GameUiCompanionPage == GameUiCharacterPage.Hermit && window.IsVisible && role.IsVisible && hermit.Pressed && !status.Pressed, "Hermit button failed to keep role visible");
        var actions = Field<Dictionary<Stat,DXButton>>(character,"gameUiHermitButtons");
        foreach (var button in actions.Values)
        {
            Check(button.Parent.Parent == window && button.IsVisible, "Original cultivation action is missing from companion");
            CompanionHit(scene,button,false);
        }
        var confirmation = Field<DXCheckBox>(character,"gameUiHermitConfirmation");
        Check(confirmation.IsVisible && confirmation.Parent.Parent == window, "Cultivation confirmation checkbox lost");
        bool checkedBefore = confirmation.Checked;
        CompanionHit(scene,confirmation,true);
        Check(confirmation.Checked != checkedBefore, "Native companion confirmation click did not toggle");
        CompanionHit(scene,confirmation,true);
        Check(confirmation.Checked == checkedBefore, "Native companion confirmation did not restore");
        foreach (var page in new[] { Field<DXControl>(character,"gameUiCompanionStatus"), Field<DXControl>(character,"gameUiCompanionHermit") })
        {
            var bounds = new Rectangle(12,70,272,338);
            foreach (var child in (List<DXControl>)typeof(DXControl).GetProperty("Controls",Instance).GetValue(page))
                if (child.Location.Y >= 70) Check(bounds.Contains(new Rectangle(child.Location,child.Size)), "Companion content exceeds native inner frame");
            var children = ((List<DXControl>)typeof(DXControl).GetProperty("Controls",Instance).GetValue(page)).Where(c=>c.Location.Y>=70).ToArray();
            for(int i=0;i<children.Length;i++) for(int j=i+1;j<children.Length;j++)
                Check(!new Rectangle(children[i].Location,children[i].Size).IntersectsWith(new Rectangle(children[j].Location,children[j].Size)), "Companion controls overlap");
        }
        Render(scene,Path.Combine(evidence,prefix+"-companion-hermit.png"));
        character.Location = new Point(700,200);
        Check(window.Location.X == character.Location.X+character.Size.Width && window.Location.Y == character.Location.Y, "Companion did not follow owner movement");
        Check(window.DisplayArea.Right<=scene.Size.Width && window.DisplayArea.Bottom<=scene.Size.Height, "Right dock must move the pair back on screen");
        character.Visible = false;
        Check(!window.IsVisible && !window.Visible, "Hidden character leaked companion");
        character.Visible = true;
        Check(window.IsVisible && character.GameUiCompanionPage == GameUiCharacterPage.Hermit, "Reopening character lost its companion selection");
        CompanionHit(scene,window.CloseButton,true);
        Check(character.IsVisible && !window.Visible && character.GameUiCompanionPage == null && !hermit.Pressed, "Companion close affected owner or retained selection");
        CompanionHit(scene,status,true); CompanionHit(scene,hermit,true);
        Check(character.GameUiCompanionPage == GameUiCharacterPage.Hermit && tabs.SelectedTab == role, "Switching companions selected an in-place tab");
        var oldParent = character.Parent;
        using(var temporaryScene = new DXControl { Size=scene.Size })
        {
            character.Parent = temporaryScene;
            Check(window.Parent == temporaryScene && window.Location.X == character.Location.X+character.Size.Width, "Companion did not follow parent changes");
            character.Parent = oldParent;
        }
        var originalSize = Config.GameSize;
        Reset(new Size(1280,800)); scene.Size = Config.GameSize;
        character.Location = new Point(1100,200);
        Check(window.DisplayArea.Right<=1280 && window.Location.X==character.Location.X+character.Size.Width, "Companion did not reflow after resolution change");
        Reset(originalSize); scene.Size = originalSize;
        character.ShowGameUiCompanion(null); character.Location = original;
    }
    [STAThread] private static int Main(string[] args)
    {
        try
        {
            string package = args[0], data = args[1], evidence = args[2];
            noScreenshots = args.Length > 3 && args[3] == "--no-screenshots";
            Directory.CreateDirectory(evidence);
            Config.FullScreen = false; Config.GameSize = new Size(1024,768); Config.LogChat = false;
            Config.SystemVolume = Config.MusicVolume = Config.PlayerVolume = Config.MonsterVolume = Config.MagicVolume = 0;
            CEnvir.Now = DateTime.UtcNow;
            Directory.CreateDirectory("Data");
            InitializeDatabase();
            var map = BlankMap(Path.GetFullPath("Map"));
            LoadLibraries(data, evidence);
            string installed = Path.Combine(Environment.CurrentDirectory, GameUiOfficialV1Catalog.PackageRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(installed)); File.Copy(package, installed);
            using (var target = new TargetForm { ClientSize = Config.GameSize })
            {
                CEnvir.Target = target; DXManager.Create();
                try
                {
                    using (var legacy = GameUiStartupSession.Prepare("Legacy", Environment.CurrentDirectory))
                    {
                        Publish(legacy); ExerciseScene(legacy, map, evidence, "legacy", false);
                        ExerciseNativeTransitions(legacy, map, evidence, false);
                        ExerciseNativeCharacterRefresh(legacy, map, evidence, false); Publish(null);
                    }
                    using (var owner = GameUiStartupSession.PrepareForLaunch("Legacy", Environment.CurrentDirectory, new[] { "--game-ui-preview" }))
                    {
                        Check(owner.EffectiveStyle == GameUiStyle.OfficialV1 && owner.IsPreview, "Explicit preview preflight failed: " + owner.Diagnostic);
                        Publish(owner);
                        ExerciseScene(owner, map, evidence, "official", true);
                        ExerciseScene(owner, map, evidence, "official-reentry", false);
                        ExerciseNativeTransitions(owner, map, evidence, true);
                        ExerciseNativeCharacterRefresh(owner, map, evidence, true);
                        Publish(null);
                    }
                    using (var lease = new FileStream(installed, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) Check(lease.Length > 0, "Package lease was not released");
                }
                finally { Publish(null); DXManager.Unload(); }
            }
            Check(!GameUiOfficialV1Catalog.RuntimeReady, "Harness must not enable production acceptance gate");
            if (!noScreenshots) File.WriteAllLines(Path.Combine(evidence, "reset-pixel-probes.txt"), pixelProbes);
            File.WriteAllText(Path.Combine(evidence, "capture-policy.txt"), noScreenshots ? "Screenshots and pixel comparisons disabled by user request. Rendering and interaction checks retained." : "Screenshots and pixel comparisons enabled.");
            Console.WriteLine("Full-scene offline integration: " + checks + " checks passed; " + scenes + " real constructors, " + renders + " presented frames, " + resets + " explicit Device.Reset calls; " + (noScreenshots ? "screenshots and pixel comparisons disabled" : comparedPixels + " identical panel pixels") + ". Real legacy resources, synthetic user/map, three character pages, six resolutions, scene reentry, cell/chat/native-input retention. Synthetic loopback covers native login/scene dispatch; no real account login, server behavior, hardware device-loss, live IME or final visual acceptance.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { foreach (var library in CEnvir.LibraryList.Values) library.Dispose(); CEnvir.LibraryList.Clear(); CEnvir.Target = null; }
    }
}
