using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Client.Controls;
using Client.Scenes.Views;
using Client.UserModels;

namespace Client.Envir
{
    // Candidate scene integration. Startup must still honor Catalog.RuntimeReady.
    // Borrows the verified package: detach this set BEFORE releasing its owner.
    // Chat bindings retain the original tabs, messages and native editor.
    public sealed class GameUiOfficialV1WindowSet : IDisposable
    {
        private readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        private readonly DXControl scene;
        private readonly MainPanel hud;
        private readonly CharacterDialog character;
        private readonly InventoryDialog inventory;
        private readonly BeltDialog belt;
        private readonly MiniMapDialog minimap;
        private readonly NPCGoodsDialog goods;
        private readonly StorageDialog storage;
        private readonly GameUiVerifiedPackage package;
        private readonly WindowState[] originals;
        private readonly GameUiSceneLayoutHost host;
        private readonly GameUiLayoutCoordinator coordinator;
        private DXControl[] passOrder;
        private GameUiOfficialV1ChatSet chat;
        private bool installed, installing, applying, restoring, disposed, faulted, needsRestore;
        public bool IsInstalled => installed;
        public bool IsFaulted => faulted;
        public IReadOnlyDictionary<string, GameUiLayoutPlacement> Placements => coordinator.LastPlacements;

        public GameUiOfficialV1WindowSet(DXControl scene, MainPanel hud, CharacterDialog character,
            InventoryDialog inventory, BeltDialog belt, MiniMapDialog minimap,
            GameUiVerifiedPackage package, GameUiLayoutStore store)
        {
            this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
            this.hud = hud ?? throw new ArgumentNullException(nameof(hud));
            this.character = character ?? throw new ArgumentNullException(nameof(character));
            this.inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            this.belt = belt ?? throw new ArgumentNullException(nameof(belt));
            this.minimap = minimap ?? throw new ArgumentNullException(nameof(minimap));
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            goods = scene.Controls.OfType<NPCGoodsDialog>().SingleOrDefault();
            storage = scene.Controls.OfType<StorageDialog>().SingleOrDefault();
            if (store == null) throw new ArgumentNullException(nameof(store));
            CheckThread();
            if (package.Manifest.PackageSha256 != GameUiOfficialV1Catalog.PackageSha256)
                throw new ArgumentException("Package does not match the compiled OfficialV1 catalog.", nameof(package));
            var library = package.Library; // Requires completed GPU preflight before any mutation.
            foreach (var frame in GameUiOfficialV1Catalog.PackageManifest.Frames.Values)
                if (library.GetSize(frame.Index) != frame.Size || library.GetOffSet(frame.Index) != frame.Offset)
                    throw new ArgumentException("Package frame differs from the compiled catalog: " + frame.Id, nameof(package));
            ValidateOriginalWindows();
            originals = OriginalWindows().Select(w => new WindowState(w)).ToArray();
            host = new GameUiSceneLayoutHost(scene, store, () => { }, RestorePassOrder);
            coordinator = new GameUiLayoutCoordinator(host);
        }

        private IEnumerable<DXWindow> OriginalWindows()
        {
            foreach (var window in new DXWindow[] { character, inventory, belt, minimap }) yield return window;
            if (storage != null) yield return storage;
        }

        private void ValidateOriginalWindows()
        {
            var windows = OriginalWindows();
            foreach (var window in windows)
                if (window.IsDisposed || window.Parent != scene || window.IsMoving || window.IsResizing ||
                    window.WindowSkin != null || window.OfficialLayoutStore != null)
                    throw new InvalidOperationException("Window set requires idle original scene windows without another skin or layout owner.");
            if (hud.IsDisposed || hud.Parent != scene || hud.GameUiHudLayout != null || character.GameUiEquipmentLayout != null ||
                character.GameUiAttributeLayout != null || character.GameUiControlLayout != null ||
                character.GameUiCompositionLayout != null || inventory.GameUiInventoryLayout != null ||
                belt.GameUiBeltLayout != null || minimap.GameUiMiniMapLayout != null)
                throw new InvalidOperationException("An original window is missing or already adapted.");
            if (goods != null && (goods.IsDisposed || goods.Parent != scene || goods.WindowSkin != null || goods.UsesGameUiGoods || goods.IsMoving || goods.IsResizing))
                throw new InvalidOperationException("Goods window is missing or already adapted.");
            if (storage != null && storage.GameUiStorageLayout != null)
                throw new InvalidOperationException("Storage window is already adapted.");
            if (belt.Grid == null || belt.Grid.GridSize != new Size(10, 1) || belt.Grid.Grid.Length != 10)
                throw new InvalidOperationException("Create the original ten-slot horizontal belt before installing OfficialV1.");
        }

        private void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThread)
                throw new InvalidOperationException("Window set belongs to the scene thread.");
            if (disposed) throw new ObjectDisposedException(nameof(GameUiOfficialV1WindowSet));
            if (scene.IsDisposed) throw new ObjectDisposedException(nameof(scene));
        }
        private int Frame(string id) => GameUiOfficialV1Catalog.PackageManifest.Frames[id].Index;
        private GameUiWindowSkin Skin(string id, Rectangle title, Rectangle drag, Rectangle close,
            ButtonImageStates states = null)
        {
            var frame = GameUiOfficialV1Catalog.PackageManifest.Frames[id];
            return new GameUiWindowSkin(package.Library, frame.Index, Padding.Empty, Padding.Empty,
                Padding.Empty, frame.Size, true, title, drag, close, states);
        }
        public void Install(Size resolution)
        {
            CheckThread();
            if (installed || installing || applying || restoring || faulted) throw new InvalidOperationException("Window set cannot be installed twice or after a fault.");
            ValidateOriginalWindows();
            foreach (var state in originals) state.CheckUnchanged();
            installing = true;
            needsRestore = true;
            passOrder = scene.Controls.ToArray();
            try
            {
                using (DXWindow.SuppressSceneLayoutWrites(scene))
                {
                    character.WindowSkin = Skin("character", Rectangle.Empty, new Rectangle(8, 0, 372, 36), new Rectangle(386, 10, 18, 18));
                    character.UseGameUiEquipmentPages(GameUiCharacterEquipmentLayout.CreateOfficialCandidate());
                    character.UseGameUiAttributes(GameUiCharacterAttributeLayout.CreateOfficialCandidate());
                    character.UseGameUiControls(GameUiCharacterControlLayout.CreateOfficialCandidate());
                    character.UseGameUiComposition(hud, GameUiCharacterCompositionLayout.CreateOfficialCandidate());
                    character.UseGameUiEquipmentCellFrame(package.Library, Frame("equipment-cell-frame"));
                    character.UseGameUiCharacterBackdrop(package.Library, Frame("character-main-background"));
                    inventory.WindowSkin = Skin("inventory", new Rectangle(16, 10, 228, 18), new Rectangle(8, 0, 236, 36), new Rectangle(250, 10, 18, 18));
                    inventory.UseGameUiInventory(GameUiInventoryLayout.CreateOfficialCandidate());
                    belt.UseGameUiBelt(GameUiBeltLayout.OfficialV1);
                    belt.WindowSkin = Skin("quickbar-base", Rectangle.Empty, new Rectangle(0, 0, 54, 44), Rectangle.Empty);
                    minimap.UseGameUiMiniMap(GameUiMiniMapLayout.OfficialV1);
                    minimap.WindowSkin = Skin("minimap-frame", new Rectangle(12, 2, 130, 16), new Rectangle(0, 0, 142, 20),
                        GameUiMiniMapLayout.OfficialV1.Collapse, GameUiOfficialV1Catalog.Buttons["minimap-collapse-button"]);
                    if (storage != null)
                    {
                        storage.WindowSkin = Skin("warehouse-background", new Rectangle(24, 10, 440, 18),
                            new Rectangle(8, 0, 460, 38), new Rectangle(478, 10, 18, 18));
                        storage.UseGameUiStorage(GameUiStorageLayout.CreateOfficialCandidate());
                        // Leave the native NPC header reachable while fitting the full grid above the 202px HUD.
                        Register("warehouse", storage, GameUiAnchor.TopRight, new Point(-296, 86), true);
                    }
                    Register("character", character, GameUiAnchor.TopLeft, new Point(8, 4), true);
                    Register("inventory", inventory, GameUiAnchor.TopRight, new Point(-8, 56), true);
                    Register("quickbar", belt, GameUiAnchor.BottomCenter, new Point(0, -154), false);
                    Register("minimap", minimap, GameUiAnchor.TopRight, Point.Empty, true);
                    if (goods != null)
                        goods.UseGameUiGoods(new GameUiWindowSkin(package.Library, Frame("npc-shop"),
                            new Padding(24, 48, 40, 66), Padding.Empty, Padding.Empty, new Size(344, 438), true,
                            new Rectangle(24, 10, 280, 18), Rectangle.Empty, new Rectangle(318, 10, 18, 18)));
                    // User scope: secondary NPC service windows retain their native presentation.
                    // Their standalone candidate adapters are intentionally not installed here.
                    InstallHud();
                    var input = scene.Controls.OfType<ChatTextBox>().SingleOrDefault();
                    if (input != null)
                    {
                        var historySkin = new GameUiWindowSkin(package.Library, Frame("chat-history-frame"),
                            Padding.Empty, new Padding(4), Padding.Empty, GameUiChatGroupBinding.MinimumSize,
                            false, Rectangle.Empty, Rectangle.Empty, Rectangle.Empty);
                        chat = new GameUiOfficialV1ChatSet(scene, input, historySkin,
                            Skin("chat-input-frame", Rectangle.Empty, Rectangle.Empty, Rectangle.Empty));
                        chat.Install(resolution);
                    }
                    coordinator.Apply(resolution, SafeArea(resolution));
                }
                installed = true;
            }
            catch (Exception installError)
            {
                faulted = true;
                try { RestoreOriginals(true); }
                catch (Exception rollbackError)
                {
                    throw new AggregateException("Window installation failed and rollback needs retry. Keep the package owner alive.", installError, rollbackError);
                }
                throw;
            }
            finally { installing = false; passOrder = null; }
        }
        private void InstallHud()
        {
            // Snapshot native controls before binding any package image.
            hud.UseGameUiHud(GameUiHudLayout.OfficialV1);
            hud.GameUiHudRoots["orb"].BindImage(package.Library, Frame("hud-orb-shell"));
            hud.GameUiHudRoots["commands"].BindImage(package.Library, Frame("hud-command-frame"));
            hud.GameUiHudRoots["experience"].BindImage(package.Library, Frame("hud-experience-track"));
            foreach (var id in new[] { "orb", "commands", "experience" })
            { hud.GameUiHudRoots[id].DrawImage = true; hud.GameUiHudRoots[id].UseOffSet = false; }
            hud.GameUiHealthFill.BindImage(package.Library, Frame("hud-hp-fill"));
            hud.GameUiManaFill.BindImage(package.Library, Frame("hud-mp-fill"));
            hud.GameUiExperienceFill.BindImage(package.Library, Frame("hud-experience-fill"));
            var buttons = new Dictionary<string, string>
            {
                ["CharacterButton"] = "hud-command-center", ["InventoryButton"] = "hud-command-top",
                ["SpellButton"] = "hud-command-upper-left", ["QuestButton"] = "hud-command-upper-right",
                ["MailButton"] = "hud-command-lower-left", ["GroupButton"] = "hud-command-lower-right",
                ["ConfigButton"] = "hud-command-bottom", ["CashShopButton"] = "hud-shop-icon"
            };
            foreach (var binding in buttons)
                ((DXButton)hud.GameUiHudControls[binding.Key]).BindButtonImages(package.Library, GameUiOfficialV1Catalog.Buttons[binding.Value], normalFrameHitTest: true);
            // Keep the original 1024-pixel HUD strip centered as a single composition.
            RegisterHudRoot("orb", GameUiAnchor.BottomLeft, resolution => new Point((resolution.Width - 1024) / 2, 0));
            RegisterHudRoot("commands", GameUiAnchor.BottomRight, resolution => new Point(-(resolution.Width - 1024) / 2, 0));
            RegisterHudRoot("readouts", GameUiAnchor.BottomCenter, resolution => new Point(0, -138));
            RegisterHudRoot("experience", GameUiAnchor.BottomCenter, resolution => Point.Empty);
        }
        private void RegisterHudRoot(string id, GameUiAnchor anchor, Func<Size, Point> offset)
        {
            var control = hud.GameUiHudRoots[id];
            Size size = control.Size;
            host.Register("hud-" + id, control, () => { }, resolution =>
                new GameUiLayoutRequest(size, size, anchor, offset(resolution), false, false, false, false));
        }
        private void Register(string id, DXWindow window, GameUiAnchor anchor, Point offset, bool safe)
        {
            Size size = window.Size;
            host.Register(id, window, () => { }, resolution => new GameUiLayoutRequest(size, size, anchor,
                offset, safe, window.CustomSize, false, false), true);
        }
        private static Rectangle SafeArea(Size size) => new Rectangle(0, 0, size.Width, size.Height - 202);
        private void RestorePassOrder()
        {
            if (passOrder == null) return;
            // BringToFront ignores Sort=false controls (including chat containers).
            // Restore the actual captured sibling sequence without invoking input sorting.
            var live = passOrder.Where(c => !c.IsDisposed && c.Parent == scene).ToArray();
            foreach (var control in live) scene.Controls.Remove(control);
            scene.Controls.InsertRange(0, live);
            // Newly created HUD siblings occupy the native panel layer on the first pass.
            if (hud.GameUiHudRoots != null)
            {
                var added = hud.GameUiHudRoots.Values.Where(r => !passOrder.Contains(r)).ToArray();
                foreach (var control in added) scene.Controls.Remove(control);
                scene.Controls.InsertRange(scene.Controls.IndexOf(hud) + 1, added);
            }
        }
        public bool Apply(Size resolution)
        {
            CheckThread();
            if (!installed || installing || applying || restoring || faulted) throw new InvalidOperationException("Only a healthy installed window set can be laid out.");
            passOrder = scene.Controls.ToArray();
            applying = true;
            try
            {
                using (host.SuppressLayoutWrites())
                {
                    bool result = coordinator.Apply(resolution, SafeArea(resolution));
                    chat?.Apply(resolution);
                    if (scene is Client.Scenes.GameScene game) game.NPCBox?.ApplyServiceLocations();
                    RestorePassOrder();
                    return result;
                }
            }
            catch { faulted = true; throw; } // Never rebuild controls or replay gameplay requests on a resize fault.
            finally { applying = false; passOrder = null; }
        }
        private void RestoreOriginals(bool restoreInitialVisibility)
        {
            var failures = new List<Exception>();
            using (host.SuppressLayoutWrites())
            {
                TryRestore(() => { chat?.Dispose(); chat = null; }, failures);
                TryRestore(() => { if (storage != null && !storage.IsDisposed) storage.UseGameUiStorage(null); }, failures);
                TryRestore(() => { if (goods != null && !goods.IsDisposed) goods.UseGameUiGoods(null); }, failures);
                TryRestore(() => { if (!hud.IsDisposed) hud.UseGameUiHud(null); }, failures);
                TryRestore(() => { if (!minimap.IsDisposed) minimap.UseGameUiMiniMap(null); }, failures);
                TryRestore(() => { if (!belt.IsDisposed) belt.UseGameUiBelt(null); }, failures);
                TryRestore(() => { if (!inventory.IsDisposed) inventory.UseGameUiInventory(null); }, failures);
                TryRestore(() => { if (!character.IsDisposed) character.UseGameUiEquipmentPages(null); }, failures);
                foreach (var state in originals.Reverse()) TryRestore(() => state.Restore(restoreInitialVisibility), failures);
                RestorePassOrder();
            }
            if (failures.Count != 0) throw new AggregateException("Window rollback did not finish. Keep the verified package alive and retry disposal.", failures);
            installed = false;
            needsRestore = false;
        }
        private static void TryRestore(Action action, List<Exception> failures)
        { try { action(); } catch (Exception error) { failures.Add(error); } }
        public void Dispose()
        {
            if (disposed) return;
            CheckThread();
            if (installing || applying || restoring) throw new InvalidOperationException("Cannot dispose during a layout transaction.");
            if (!needsRestore) { disposed = true; return; }
            passOrder = scene.Controls.ToArray();
            restoring = true;
            try { RestoreOriginals(!installed); disposed = true; }
            finally { restoring = false; passOrder = null; }
        }
        private sealed class WindowState
        {
            private readonly DXWindow window;
            private readonly Rectangle bounds;
            private readonly bool allowResize, visible;
            private readonly WindowSetting settings;
            internal WindowState(DXWindow window)
            { this.window = window; bounds = new Rectangle(window.Location, window.Size); allowResize = window.AllowResize; visible = window.Visible; settings = window.Settings; }
            internal void CheckUnchanged()
            {
                if (new Rectangle(window.Location, window.Size) != bounds || window.AllowResize != allowResize ||
                    window.Visible != visible || !ReferenceEquals(window.Settings, settings))
                    throw new InvalidOperationException("Original window changed before installation.");
            }
            internal void Restore(bool restoreInitialVisibility)
            {
                if (window.IsDisposed) return;
                window.RestoreLegacyLayout(settings);
                window.WindowSkin = null;
                window.AllowResize = allowResize;
                window.Size = bounds.Size;
                window.Location = bounds.Location;
                if (restoreInitialVisibility) window.Visible = visible;
            }
        }
    }
}
