using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Client.Controls;
using Client.Envir;
using Client.Models;
using Client.Scenes;
using Library;
using SlimDX.Direct3D9;
using SlimDX.Windows;
using Font = System.Drawing.Font;

namespace Client
{
    public sealed class TargetForm : RenderForm
    {
        public bool Resizing { get; private set; }
        public TargetForm() : base("Zircon Client")
        {
            AutoScaleMode = AutoScaleMode.None;

            AutoScaleDimensions = new SizeF(96F, 96F);

            ClientSize = new Size(1024, 768);
            
            Icon = Properties.Resources.Zircon;
            
            FormBorderStyle = (Config.FullScreen || Config.Borderless) ? FormBorderStyle.None : FormBorderStyle.FixedSingle;

            MaximizeBox = false;
        }

        private bool closeConfirmed;
        private DXMessageBox closeConfirmation;

        // The confirmation buttons use a one-shot bypass so Close() does not reopen the dialog.
        internal void CloseAfterConfirmation()
        {
            closeConfirmed = true;
            try { Close(); }
            finally { closeConfirmed = false; }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel || closeConfirmed || e.CloseReason != CloseReason.UserClosing) return;
            e.Cancel = true;

            var game = DXControl.ActiveScene as GameScene;
            if (!CEnvir.GameUiOperationsBlocked && gameUiFailureNotice == null && game?.ExitBox != null && !game.ExitBox.IsDisposed)
            {
                game.ExitBox.ShowConfirmation();
                return;
            }

            // A native prompt remains usable if the renderer has stopped or there is no scene.
            if (CEnvir.GameUiOperationsBlocked || gameUiFailureNotice != null || DXControl.ActiveScene == null || DXControl.ActiveScene.IsDisposed)
            {
                if (MessageBox.Show(this, "确定要退出游戏吗？", "退出游戏", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                    e.Cancel = false;
                return;
            }

            if (closeConfirmation != null && !closeConfirmation.IsDisposed)
            {
                closeConfirmation.Visible = true;
                closeConfirmation.BringToFront();
                return;
            }
            closeConfirmation = new DXMessageBox("确定要退出游戏吗？", "退出游戏", DXMessageBoxButtons.YesNo);
            closeConfirmation.YesButton.Label.Text = "退出游戏";
            closeConfirmation.NoButton.Label.Text = "取消";
            closeConfirmation.YesButton.MouseClick += (o, args) => CloseAfterConfirmation();
        }

        private Panel gameUiFailureNotice;
        internal void ShowGameUiFailureNotice()
        {
            if (IsDisposed || gameUiFailureNotice != null) return;
            Text = "界面故障，请关闭客户端后重启";
            gameUiFailureNotice = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
            var message = new Label
            {
                Dock = DockStyle.Fill, ForeColor = Color.White, TextAlign = ContentAlignment.MiddleCenter,
                Text = "游戏界面发生故障，已停止新的操作。\r\n请关闭客户端后重新启动。"
            };
            var close = new Button { Dock = DockStyle.Bottom, Height = 48, Text = "退出客户端" };
            close.Click += (o, e) => Close();
            gameUiFailureNotice.Controls.Add(message);
            gameUiFailureNotice.Controls.Add(close);
            Controls.Add(gameUiFailureNotice);
            gameUiFailureNotice.BringToFront();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            if (GameScene.Game != null)
                GameScene.Game.MapControl.MapButtons = MouseButtons.None;

            GameScene.Game?.MonsterBox?.ReleaseDetails();

            CEnvir.Shift = false;
            CEnvir.Alt = false;
            CEnvir.Ctrl = false;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (CEnvir.GameUiOperationsBlocked) return;
            //TODO Move to other locations instead?
            if (Config.ClipMouse && Focused)
                Cursor.Clip = RectangleToScreen(ClientRectangle);
            else
                Cursor.Clip = Rectangle.Empty;

            CEnvir.MouseLocation = e.Location;

            try
            {
                DXControl.ActiveScene?.OnMouseMove(e);
            }
            catch (Exception ex)
            {
                if (!CEnvir.HandleGameUiFailure(ex)) CEnvir.SaveError(ex.ToString());
            }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (CEnvir.GameUiOperationsBlocked) return;
            if (GameScene.Game != null && e.Button == MouseButtons.Right && (GameScene.Game.SelectedCell != null || GameScene.Game.GoldPickedUp))
            {
                GameScene.Game.SelectedCell = null;
                GameScene.Game.GoldPickedUp = false;
                return;
            }

            try
            {
                DXControl.ActiveScene?.OnMouseDown(e);
            }
            catch (Exception ex)
            {
                if (!CEnvir.HandleGameUiFailure(ex)) CEnvir.SaveError(ex.ToString());
            }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (CEnvir.GameUiOperationsBlocked) return;

            if (GameScene.Game != null)
                GameScene.Game.MapControl.MapButtons &= ~e.Button;

            try
            {
                DXControl.ActiveScene?.OnMouseUp(e);
            }
            catch (Exception ex)
            {
                if (!CEnvir.HandleGameUiFailure(ex)) CEnvir.SaveError(ex.ToString());
            }
        }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (CEnvir.GameUiOperationsBlocked) return;
            try
            {
                DXControl.ActiveScene?.OnMouseClick(e);
            }
            catch (Exception ex)
            {
                if (!CEnvir.HandleGameUiFailure(ex)) CEnvir.SaveError(ex.ToString());
            }
        }
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (CEnvir.GameUiOperationsBlocked) return;
            try
            {
                DXControl.ActiveScene?.OnMouseClick(e);
            }
            catch (Exception ex)
            {
                if (!CEnvir.HandleGameUiFailure(ex)) CEnvir.SaveError(ex.ToString());
            }
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (CEnvir.GameUiOperationsBlocked) return;
            try
            {
                DXControl.ActiveScene?.OnMouseWheel(e);
            }
            catch (Exception ex)
            {
                if (!CEnvir.HandleGameUiFailure(ex)) CEnvir.SaveError(ex.ToString());
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (CEnvir.GameUiOperationsBlocked) { e.Handled = e.SuppressKeyPress = true; return; }
            base.OnKeyDown(e);

            CEnvir.Shift = e.Shift;
            CEnvir.Alt = e.Alt;
            CEnvir.Ctrl = e.Control;

            try
            {
                if (e.Alt && e.KeyCode == Keys.Enter)
                {
                    DXManager.ToggleFullScreen();
                    return;
                }
                
                DXControl.ActiveScene?.OnKeyDown(e);
                e.Handled = true;
            }
            catch (Exception ex)
            {
                if (!CEnvir.HandleGameUiFailure(ex)) CEnvir.SaveError(ex.ToString());
            }
        }
        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (CEnvir.GameUiOperationsBlocked) { e.Handled = e.SuppressKeyPress = true; return; }
            CEnvir.Shift = e.Shift;
            CEnvir.Alt = e.Alt;
            CEnvir.Ctrl = e.Control;

            if (e.KeyCode == Keys.Pause)
               CreateScreenShot();

            try
            {
                DXControl.ActiveScene?.OnKeyUp(e);
                e.Handled = true;
            }
            catch (Exception ex)
            {
                if (!CEnvir.HandleGameUiFailure(ex)) CEnvir.SaveError(ex.ToString());
            }
        }
        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (CEnvir.GameUiOperationsBlocked) { e.Handled = true; return; }
            try
            {
                DXControl.ActiveScene?.OnKeyPress(e);
                e.Handled = true;
            }
            catch (Exception ex)
            {
                if (!CEnvir.HandleGameUiFailure(ex)) CEnvir.SaveError(ex.ToString());
            }
        }
        
        public void ChaneSize(Size size)
        {
            if (ClientSize == size) return;

            ClientSize = size;
        }

        public static void CreateScreenShot()
        {
            Bitmap image = CEnvir.Target.GetImage();

            using (Graphics graphics = Graphics.FromImage(image))
            {
                string text = $"Date: {CEnvir.Now.ToShortDateString()}{Environment.NewLine}";
                text += $"Time: {CEnvir.Now.TimeOfDay:hh\\:mm\\:ss}{Environment.NewLine}";
                if (GameScene.Game != null)
                    text += $"Player: {MapObject.User.Name}{Environment.NewLine}";

                using (Font font = new Font(Config.FontName, CEnvir.FontSize(8F)))
                {
                    graphics.DrawString(text, font, Brushes.Black, 3, 33);
                    graphics.DrawString(text, font, Brushes.Black, 4, 32);
                    graphics.DrawString(text, font, Brushes.Black, 5, 33);
                    graphics.DrawString(text, font, Brushes.Black, 4, 34);
                    graphics.DrawString(text, font, Brushes.White, 4, 33);
                }
            }

            string path = Path.Combine(Application.StartupPath, @"Screenshots\");
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);

            int count = Directory.GetFiles(path, "*.png").Length;
            string fileName = $"Image {count}.png";

            image.Save(Path.Combine(path, fileName), ImageFormat.Png);
            image.Dispose();

            if (GameScene.Game != null)
                GameScene.Game.ReceiveChat("Screenshot Saved " + fileName, MessageType.System);
        }

        #region ScreenCapture

        [DllImport("user32.dll")]
        static extern IntPtr GetWindowDC(IntPtr handle);
        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr handle);
        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleBitmap(IntPtr handle, int width, int height);
        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr handle, IntPtr handle2);
        [DllImport("gdi32.dll")]
        public static extern bool BitBlt(IntPtr handle, int destX, int desty, int width, int height,
                                         IntPtr handle2, int sourX, int sourY, int flag);
        [DllImport("gdi32.dll")]
        public static extern int DeleteDC(IntPtr handle);
        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr handle, IntPtr handle2);
        [DllImport("gdi32.dll")]
        public static extern int DeleteObject(IntPtr handle);

        public Bitmap GetImage()
        {
            Point location = PointToClient(Location);

            location = new Point(-location.X, -location.Y);

            Rectangle r = new Rectangle(location, ClientSize);


            IntPtr sourceDc = GetWindowDC(Handle);
            IntPtr destDc = CreateCompatibleDC(sourceDc);

            IntPtr hBmp = CreateCompatibleBitmap(sourceDc, r.Width, r.Height);
            if (hBmp != IntPtr.Zero)
            {
                IntPtr hOldBmp = SelectObject(destDc, hBmp);
                BitBlt(destDc, 0, 0, r.Width, r.Height, sourceDc, r.X, r.Y, 0xCC0020); //0, 0, 13369376);
                SelectObject(destDc, hOldBmp);
                DeleteDC(destDc);
                ReleaseDC(Handle, sourceDc);

                Bitmap bmp = Image.FromHbitmap(hBmp);

                DeleteObject(hBmp);

                return bmp;
            }

            return null;
        }

        #endregion
    }

}
