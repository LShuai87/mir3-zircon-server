using System;
using System.Drawing;
using System.Windows.Forms;
using Client.Controls;
using Client.Envir;
using Client.Models;
using Client.UserModels;
using Library;
using C = Library.Network.ClientPackets;

namespace Client.Scenes.Views
{
    public sealed class ExitDialog : DXWindow
    {
        public DXButton ToSelectButton, ExitButton;
        public DXLabel ConfirmationLabel { get; private set; }
        private DXTextBox hiddenTextBox;

        public override WindowType Type => WindowType.ExitBox;
        public override bool CustomSize => false;
        public override bool AutomaticVisiblity => false;

        public ExitDialog()
        {
            Modal = true;
            TitleLabel.Text = @"操作选择";
            SetClientSize(new Size(310, 85));

            ConfirmationLabel = new DXLabel
            {
                Parent = this,
                Location = new Point(ClientArea.X, ClientArea.Y + 5),
                Text = "确定要退出游戏或返回角色选择吗？",
                ForeColour = Color.FromArgb(198, 166, 99),
                IsControl = false,
            };
            ToSelectButton = new DXButton
            {
                Location = new Point(ClientArea.X + 30, ClientArea.Y + 50),
                Size = new Size(120, DefaultHeight),
                Parent = this,
                Label = { Text = "返回角色选择" },
            };
            ToSelectButton.MouseClick += (o, e) =>
            {
                if (!CanLeave()) return;
                CEnvir.Enqueue(new C.Logout());
                Visible = false;
            };

            ExitButton = new DXButton
            {
                Location = new Point(ClientArea.X + 160, ClientArea.Y + 50),
                Size = new Size(120, DefaultHeight),
                Parent = this,
                Label = { Text = "退出游戏" },
            };
            ExitButton.MouseClick += (o, e) =>
            {
                if (CanLeave()) CEnvir.Target.CloseAfterConfirmation();
            };
        }

        public void ShowConfirmation()
        {
            if (IsDisposed || Parent == null || Parent.IsDisposed) return;
            Location = new Point((Parent.Size.Width - Size.Width) / 2, (Parent.Size.Height - Size.Height) / 2);
            Visible = true;
            BringToFront();
            // Release a held map click and capture input immediately, even before the mouse moves.
            FocusControl = this;
            MouseControl = this;
            if (GameScene.Game?.MapControl != null)
                GameScene.Game.MapControl.MapButtons = MouseButtons.None;
        }

        private static bool CanLeave()
        {
            var game = GameScene.Game;
            if (game == null || MapObject.User == null) return false;
            if (!game.Observer && CEnvir.Now < MapObject.User.CombatTime.AddSeconds(10))
            {
                game.ReceiveChat("战斗中无法退出游戏.", MessageType.System);
                return false;
            }
            return true;
        }

        public override void OnIsVisibleChanged(bool oValue, bool nValue)
        {
            base.OnIsVisibleChanged(oValue, nValue);
            if (IsVisible)
            {
                hiddenTextBox = DXTextBox.ActiveTextBox;
                DXTextBox.ActiveTextBox = null;
            }
            else RestoreTextBox();
        }

        private void RestoreTextBox()
        {
            var previous = hiddenTextBox;
            hiddenTextBox = null;
            if (previous == null || previous.IsDisposed || previous.TextBox == null || previous.TextBox.IsDisposed) return;
            for (DXControl owner = previous.Parent; owner != null; owner = owner.Parent)
                if (owner.IsDisposed) return;
            DXTextBox.ActiveTextBox = previous;
        }

        public override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            e.Handled = true;
        }

        public override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            e.Handled = true;
        }

        public override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            e.Handled = true;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing) return;
            RestoreTextBox();
            ToSelectButton?.Dispose();
            ExitButton?.Dispose();
            ConfirmationLabel?.Dispose();
            ToSelectButton = ExitButton = null;
            ConfirmationLabel = null;
        }
    }
}
