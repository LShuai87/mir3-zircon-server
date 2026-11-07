using System.Drawing;
using System.Windows.Forms;
using Client.Controls;
using Client.Envir;
using Client.Models;
using Library;

namespace Client.Scenes.Views
{
    // A passive overlay, deliberately outside DXWindow's saved positions and Escape handlers.
    public sealed class MonsterDialog : DXControl
    {
        private readonly DXControl bar, slot, fill, highlight;
        private readonly DXLabel nameLabel, detailLabel;
        private readonly MonsterHudSelection<MapObject> selection = new MonsterHudSelection<MapObject>();
        private readonly MonsterHudDetailHold detailHold = new MonsterHudDetailHold();
        private readonly System.Predicate<MapObject> validTarget;
        private MonsterObject monster;
        private int lastHealth = -1, lastMaxHealth = -1;
        private bool lastBoss, lastShowHealth, lastShowPercentage;
        private string lastName;

        public MonsterObject Monster
        {
            get => monster;
            set
            {
                if (monster != value)
                {
                    monster = value;
                    lastHealth = -1;
                }
                RefreshHealth();
            }
        }

        public MonsterDialog()
        {
            IsControl = false;
            PassThrough = true;
            DrawTexture = false;
            Visible = false;
            Size = new Size(360, 52);
            validTarget = IsValidTarget;
            bar = new DXControl { Parent = this, IsControl = false, DrawTexture = false, Size = new Size(360, 32) };
            // Extend beneath the opaque ornaments to cover every opening in the frame.
            slot = new DXControl { Parent = bar, IsControl = false, DrawTexture = true,
                BackColour = Color.FromArgb(0x17, 0x10, 0x0F), Location = new Point(15, 8), Size = new Size(329, 18) };
            fill = new DXControl { Parent = slot, IsControl = false, DrawTexture = true,
                BackColour = Color.DarkRed, Size = slot.Size };
            highlight = new DXControl { Parent = fill, IsControl = false, DrawTexture = true,
                BackColour = Color.FromArgb(0xA5, 0x2A, 0x22), Size = new Size(329, 1) };
            bar.AfterDraw += DrawFrame;
            nameLabel = MakeLabel(bar, 12F, new Point(24, 4), new Size(312, 26));
            detailLabel = MakeLabel(this, 9F, new Point(0, 34), new Size(360, 18));
            detailLabel.ForeColour = Color.FromArgb(0xD7, 0xBC, 0x80);
        }

        private static DXLabel MakeLabel(DXControl parent, float fontSize, Point location, Size size)
        {
            var label = new DXLabel
            {
                Parent = parent, IsControl = false, AutoSize = false, Location = location, Size = size,
                DrawFormat = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
            };
            Font oldFont = label.Font;
            label.Font = new Font(Config.FontName, CEnvir.FontSize(fontSize));
            oldFont.Dispose();
            return label;
        }

        public void LayoutForViewport(int width)
        {
            int barWidth = System.Math.Max(0, System.Math.Min(360, width - 48));
            Location = new Point((width - barWidth) / 2, 24);
            Size = new Size(barWidth, 52);
            bar.Size = new Size(barWidth, 32);
            slot.Size = new Size(System.Math.Max(0, barWidth - 31), 18);
            nameLabel.Size = new Size(System.Math.Max(0, barWidth - 48), 26);
            detailLabel.Size = new Size(barWidth, 18);
            lastHealth = -1;
            RefreshHealth();
        }

        // GameInter #327: original 24px ornaments, with only the uniform middle cropped.
        // Use the shared library cache; never open resource files or allocate textures here.
        private void DrawFrame(object sender, System.EventArgs e)
        {
            MirLibrary library;
            if (bar.Size.Width < 72 || !CEnvir.LibraryList.TryGetValue(LibraryFile.GameInter, out library) ||
                library.GetSize(327) != new Size(456, 32)) return;

            Point origin = bar.DisplayArea.Location;
            Color tint = Color.FromArgb(0xB8, 0xA0, 0x80);
            library.Draw(327, origin.X, origin.Y, tint, new Rectangle(0, 0, 24, 32), 1F, ImageType.Image);
            library.Draw(327, origin.X + 24, origin.Y, tint,
                new Rectangle(24, 0, bar.Size.Width - 48, 32), 1F, ImageType.Image);
            library.Draw(327, origin.X + bar.Size.Width - 24, origin.Y, tint,
                new Rectangle(432, 0, 24, 32), 1F, ImageType.Image);
        }

        private bool IsValidTarget(MapObject candidate)
        {
            var mob = candidate as MonsterObject;
            var scene = GameScene.Game;
            ClientObjectData data;
            return mob != null && scene?.MapControl?.MapInfo != null && mob.Visible && !mob.Dead &&
                mob.CompanionObject == null && string.IsNullOrEmpty(mob.PetOwner) && mob.MonsterInfo != null &&
                mob.MonsterInfo.AI >= 0 && scene.MapControl.Objects.Contains(mob) &&
                scene.DataDictionary.TryGetValue(mob.ObjectID, out data) && !data.Dead &&
                data.MapIndex == scene.MapControl.MapInfo.Index && data.MaxHealth > 0 && data.Health > 0;
        }

        public void UpdateTarget(MapObject hover, MapObject focus)
        {
            Monster = selection.Select(hover, focus, validTarget) as MonsterObject;
        }

        public void Suppress(MapObject hover, MapObject focus)
        {
            selection.Suppress(hover, focus);
            ReleaseDetails();
            Monster = null;
        }

        public void Resume() { selection.Reset(); }
        public void Clear()
        {
            selection.Reset();
            ReleaseDetails();
            Monster = null;
        }
        public void BeginDetails(Keys keyData) { detailHold.Begin(keyData); RefreshHealth(); }
        public void UpdateDetailKey(Keys keyData, bool released) { detailHold.Update(keyData, released); RefreshHealth(); }
        public void ReleaseDetails() { detailHold.Clear(); RefreshHealth(); }

        public void RefreshStats() { RefreshHealth(); }
        public void RefreshHealth()
        {
            if (!Config.MonsterBoxVisible || !IsValidTarget(monster))
            {
                Visible = false;
                monster = null;
                lastHealth = -1;
                if (nameLabel != null) nameLabel.Text = string.Empty;
                if (detailLabel != null) detailLabel.Text = string.Empty;
                return;
            }

            ClientObjectData data = GameScene.Game.DataDictionary[monster.ObjectID];
            bool showHealth = Config.MonsterBoxShowHealth || detailHold.Active;
            bool showPercentage = Config.MonsterBoxShowPercentage || detailHold.Active;
            bool boss = monster.MonsterInfo.IsBoss;
            string name = monster.MonsterInfo.MonsterName;
            Visible = Size.Width >= 72;
            if (lastHealth == data.Health && lastMaxHealth == data.MaxHealth && lastBoss == boss &&
                lastShowHealth == showHealth && lastShowPercentage == showPercentage && lastName == name) return;

            nameLabel.Text = name;
            nameLabel.ForeColour = boss ? Color.FromArgb(0xD7, 0xBC, 0x80) : Color.FromArgb(0xE7, 0xDD, 0xCA);
            int width = MonsterHudPresentation.FillWidth(data.Health, data.MaxHealth, slot.Size.Width);
            fill.Size = new Size(width, slot.Size.Height);
            highlight.Size = new Size(width, 1);
            detailLabel.Text = MonsterHudPresentation.Details(boss, data.Health, data.MaxHealth, showHealth, showPercentage);
            detailLabel.Visible = detailLabel.Text.Length > 0;
            lastHealth = data.Health;
            lastMaxHealth = data.MaxHealth;
            lastBoss = boss;
            lastShowHealth = showHealth;
            lastShowPercentage = showPercentage;
            lastName = name;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                selection.Reset();
                detailHold.Clear();
                monster = null;
            }
            base.Dispose(disposing);
        }
    }
}
