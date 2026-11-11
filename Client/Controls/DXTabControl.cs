using System;
using System.Collections.Generic;
using System.Drawing;
using Client.Envir;
using Library;
using SlimDX;
using SlimDX.Direct3D9;

//Cleaned
namespace Client.Controls
{
    // Optional per-page geometry. The default tab layout remains automatic.
    public sealed class DXTabGeometry
    {
        public Rectangle Page { get; }
        public Rectangle Button { get; }

        public DXTabGeometry(Rectangle page, Rectangle button)
        {
            foreach (Rectangle rect in new[] { page, button })
                if (rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0 ||
                    (long)rect.X + rect.Width > 16384 || (long)rect.Y + rect.Height > 16384)
                    throw new ArgumentOutOfRangeException(nameof(page), "Tab rectangles must be positive and within 16384 pixels.");
            if (page.IntersectsWith(button))
                throw new ArgumentException("The tab header must be outside its page.");
            Page = page;
            Button = button;
        }
    }

    public class DXTabControl : DXControl
    {
        #region Properties
        
        #region SelectedTab

        public DXTab SelectedTab
        {
            get => _SelectedTab;
            set
            {
                if (_SelectedTab == value) return;

                DXTab oldValue = _SelectedTab;
                _SelectedTab = value;

                OnSelectedTabChanged(oldValue, value);
            }
        }
        private DXTab _SelectedTab;
        public event EventHandler<EventArgs> SelectedTabChanged;
        public virtual void OnSelectedTabChanged(DXTab oValue, DXTab nValue)
        {
            SelectedTabChanged?.Invoke(this, EventArgs.Empty);

            if (oValue != null && oValue.Parent == this)
                oValue.Selected = false;

            if (nValue != null)
                nValue.Selected = true;
            GameUiChatGroup?.RevealSelectedTab();
        }

        #endregion

        public List<DXButton> TabButtons = new List<DXButton>();

        // Opt-in chat geometry preserves the existing movable tabs and their parents.
        public GameUiChatGroupBinding GameUiChatGroup { get; internal set; }
        public override Size Size
        {
            get => base.Size;
            set => base.Size = GameUiChatGroup?.ConstrainSize(value) ?? value;
        }
        public override Size GetAcceptableResize(Size size)
            => GameUiChatGroup?.ConstrainSize(base.GetAcceptableResize(size)) ?? base.GetAcceptableResize(size);

        public override void OnDisplayAreaChanged(Rectangle oValue, Rectangle nValue)
        {
            base.OnDisplayAreaChanged(oValue, nValue);

            if (Parent == null) return;
            if (GameUiChatGroup != null) { GameUiChatGroup.Refresh(); return; }

            foreach (DXControl control in Controls)
            {
                DXTab tab = control as DXTab;

                if (tab == null || tab.Updating || tab.FixedGeometry != null) continue;

                control.Size = new Size(Size.Width - control.Location.X, Size.Height - control.Location.Y);
            }
            TabsChanged();
        }
        #endregion

        public DXTabControl()
        {
            PassThrough = true;
        }

        #region Methods
        
        public void SetNewTab()
        {
            if (IsDisposed) return;

            foreach (DXControl control in Controls)
            {
                DXTab tab = control as DXTab;

                if (tab == null || tab == SelectedTab) continue;

                _SelectedTab = null;
                SelectedTab = tab;
                return;
            }

            _SelectedTab = null;
        }
        
        private DXTab FindFixedTab(DXButton button)
        {
            foreach (DXControl control in Controls)
                if (control is DXTab tab && tab.TabButton == button && tab.FixedGeometry != null)
                    return tab;
            return null;
        }

        public void TabsChanged()
        {
            if (GameUiChatGroup != null) { GameUiChatGroup.Refresh(); return; }

            if (SelectedTab == null)
            {
                foreach (DXControl control in Controls)
                {
                    DXTab tab = control as DXTab;

                    if (tab == null || tab == SelectedTab) continue;
                    
                    SelectedTab = tab;
                    break;
                }
            }

            int x = 0;
            int width = 0;
            foreach (DXButton control in TabButtons)
            {
                if (!control.Visible) continue;
                DXTab fixedTab = FindFixedTab(control);
                if (fixedTab != null) { fixedTab.ApplyFixedGeometry(); continue; }

                if (control.RightAligned)
                {
                    width = control.Size.Width + 1;
                    continue;
                }

                //control.Visible = true;
                control.Location = new Point(x, 0);
                x += control.Size.Width + 1;
            }


            foreach (DXButton control in TabButtons)
            {
                if (!control.Visible) continue;

                if (!control.RightAligned || FindFixedTab(control) != null) continue;

            //    control.Visible = true;
                control.Location = new Point(Size.Width - width, 0);
                width -= control.Size.Width + 1;
            }

        }

        #endregion

        #region IDisposable

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                _SelectedTab = null;
                SelectedTabChanged = null;
                
                TabButtons.Clear();
                TabButtons = null;
                }
        }

        #endregion
    }

    public class DXTab : DXControl
    {
        #region Properties

        #region CurrentTabControl

        public DXTabControl CurrentTabControl
        {
            get => _CurrentTabControl;
            set
            {
                if (_CurrentTabControl == value) return;

                DXTabControl oldValue = _CurrentTabControl;
                _CurrentTabControl = value;

                OnCurrentTabControlChanged(oldValue, value);
            }
        }
        private DXTabControl _CurrentTabControl;
        public event EventHandler<EventArgs> CurrentTabControlChanged;
        public virtual void OnCurrentTabControlChanged(DXTabControl oValue, DXTabControl nValue)
        {
            if (oValue?.SelectedTab == this)
            {
                oValue.SelectedTab = null;
                oValue.SetNewTab();
            }


            if (oValue != null && nValue != null)
                TabButton.MovePoint = new Point(TabButton.MovePoint.X - oValue.DisplayArea.X + nValue.DisplayArea.X, TabButton.MovePoint.Y - oValue.DisplayArea.Y + nValue.DisplayArea.Y);

            if (oValue != null && oValue.Controls.Count == 0)
                oValue.Dispose();

            CurrentTabControlChanged?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region DrawOtherBorder

        public bool DrawOtherBorder
        {
            get => _DrawOtherBorder;
            set
            {
                if (_DrawOtherBorder == value) return;

                bool oldValue = _DrawOtherBorder;
                _DrawOtherBorder = value;

                OnDrawOtherBorderChanged(oldValue, value);
            }
        }
        private bool _DrawOtherBorder;
        public event EventHandler<EventArgs> DrawOtherBorderChanged;
        public virtual void OnDrawOtherBorderChanged(bool oValue, bool nValue)
        {
            DrawOtherBorderChanged?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region Selected

        public bool Selected
        {
            get => _Selected;
            set
            {
                if (_Selected == value) return;

                bool oldValue = _Selected;
                _Selected = value;

                OnSelectedChanged(oldValue, value);
            }
        }
        private bool _Selected;
        public event EventHandler<EventArgs> SelectedChanged;
        public virtual void OnSelectedChanged(bool oValue, bool nValue)
        {
            if (Selected)
            {
                Visible = true;
                if (TabButton.LibraryFile != LibraryFile.None)
                {
                    TabButton.Pressed = true;
                }
                else
                {
                    TabButton.ButtonType = ButtonType.SelectedTab;
                    TabButton.Label.ForeColour = Color.White;
                }
            }
            else
            {
                Visible = false;

                if (TabButton.LibraryFile != LibraryFile.None)
                {
                    TabButton.Pressed = false;
                }
                else
                {
                    TabButton.ButtonType = ButtonType.DeselectedTab;
                    TabButton.Label.ForeColour = Color.FromArgb(198, 166, 99);
                }
            }

            SelectedChanged?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        public DXButton TabButton { get; private set; }

        public float? OldOpacity { get; set; }

        public bool Updating;

        public DXTabGeometry FixedGeometry { get; private set; }
        private bool fixedGeometryApplying;
        private bool legacyButtonMovable, legacyAllowResize;

        public void UseFixedGeometry(DXTabGeometry geometry)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(DXTab));
            if (IsMoving || IsResizing || TabButton.IsMoving || TabButton.IsResizing)
                throw new InvalidOperationException("A moving or resizing tab cannot change layout.");
            if (geometry != null)
            {
                if (!(Parent is DXTabControl)) throw new InvalidOperationException("A fixed tab needs a tab control parent.");
                Rectangle parentBounds = new Rectangle(Point.Empty, Parent.Size);
                if (!parentBounds.Contains(geometry.Page) || !parentBounds.Contains(geometry.Button))
                    throw new ArgumentException("Tab geometry exceeds its parent.", nameof(geometry));
            }
            if (FixedGeometry == null && geometry != null)
            {
                legacyButtonMovable = TabButton.Movable;
                legacyAllowResize = AllowResize;
            }
            bool wasFixed = FixedGeometry != null;
            FixedGeometry = geometry;
            if (geometry != null) ApplyFixedGeometry();
            else if (wasFixed)
            {
                TabButton.Movable = legacyButtonMovable;
                AllowResize = legacyAllowResize;
                Location = new Point(0, (Parent as DXTabControl)?.GameUiChatGroup?.ContentTop ?? TabHeight - 1);
                if (Parent != null) Size = new Size(Parent.Size.Width, Parent.Size.Height - Location.Y);
                TabButton.Size = new Size(Math.Max(60, DXLabel.GetSize(TabButton.Label.Text, TabButton.Label.Font, TabButton.Label.Outline).Width), TabHeight);
            }
            (Parent as DXTabControl)?.TabsChanged();
        }

        internal void ApplyFixedGeometry()
        {
            if (FixedGeometry == null || fixedGeometryApplying || IsDisposed) return;
            fixedGeometryApplying = true;
            try
            {
                TabButton.Movable = false;
                AllowResize = false;
                Location = FixedGeometry.Page.Location;
                Size = FixedGeometry.Page.Size;
                TabButton.Location = FixedGeometry.Button.Location;
                TabButton.Size = FixedGeometry.Button.Size;
            }
            finally { fixedGeometryApplying = false; }
        }

        public override void OnDisplayAreaChanged(Rectangle oValue, Rectangle nValue)
        {
            base.OnDisplayAreaChanged(oValue, nValue);

            if (Parent == null) return;
            if (FixedGeometry != null) { ApplyFixedGeometry(); return; }

            if (IsResizing && Updating) return;

            Size = new Size(Parent.Size.Width - Location.X, Parent.Size.Height - Location.Y);
        }

        public override void OnParentChanged(DXControl oValue, DXControl nValue)
        {
            base.OnParentChanged(oValue, nValue);

            DXTabControl oldTab = oValue as DXTabControl;

            if (oldTab != null)
            {
                oldTab.TabButtons.Remove(TabButton);
                oldTab.TabsChanged();
            }

            if (Parent == null)
                TabButton.Parent = null;

            if (Parent == null) return;

            Size = new Size(Parent.Size.Width - Location.X, Parent.Size.Height - Location.Y);

            DXTabControl tab = Parent as DXTabControl;

            if (tab == null) return;
            Selected = tab.SelectedTab == this;
            TabButton.Parent = tab;
            tab.Controls.Remove(TabButton);
            tab.Controls.Insert(0, TabButton);
            tab.TabButtons.Add(TabButton);
            tab.TabsChanged();

            CurrentTabControl = tab;
        }
        
        public override void OnSizeChanged(Size oValue, Size nValue)
        {
            if (IsResizing)
            {
                if (Updating) return;
                Point location = Parent.Location;
                Size size = new Size(Parent.Size.Width - oValue.Width + nValue.Width, Parent.Size.Height - oValue.Height + nValue.Height);

                if (ResizeUp)
                    location = new Point(location.X, location.Y + oValue.Height - nValue.Height);

                if (ResizeLeft)
                    location = new Point(location.X + oValue.Width - nValue.Width, location.Y);

                Updating = true;
                Parent.Size = size;
                Parent.Location = location;
                Location = new Point(0, (Parent as DXTabControl)?.GameUiChatGroup?.ContentTop ?? TabHeight - 1);
                Updating = false;
                return;
            }

            base.OnSizeChanged(oValue, nValue);

        }
        #endregion
        
        public DXTab()
        {
            Location = new Point(0, TabHeight - 1);
            BackColour = BackColour = Color.FromArgb(16, 8, 8);
            DrawTexture = true;
            BorderColour = Color.FromArgb(198, 166, 99);
            PassThrough = true;
            Visible = false;


            TabButton = new DXButton
            {
                ButtonType = ButtonType.DeselectedTab,
                Size = new Size(60, TabHeight),
            };
            TabButton.Label.TextChanged += (o, e) =>
            {
                if (FixedGeometry != null) ApplyFixedGeometry();
                else TabButton.Size = new Size(Math.Max(60, DXLabel.GetSize(TabButton.Label.Text, TabButton.Label.Font, TabButton.Label.Outline).Width), TabHeight);

            };
            TabButton.MouseClick += (o, e) =>
            {
                DXTabControl tab = TabButton.Parent as DXTabControl;
                if (tab == null) return;
                tab.SelectedTab = this;
            };
            TabButton.LocationChanged += TabButton_LocationChanged;
            TabButton.IsMovingChanged += TabButton_IsMovingChanged;
        }

        #region Methods
        private void TabButton_IsMovingChanged(object sender, EventArgs e)
        {
            if (FixedGeometry != null) { ApplyFixedGeometry(); return; }
            if (!IsMoving)
            {
                DXTabControl cTab = Parent as DXTabControl;
                if (cTab != null)
                {
                    cTab.Controls.Remove(TabButton);
                    cTab.Controls.Insert(0, TabButton);
                    cTab.TabsChanged();
                }
                else
                {
                    DXControl oldParent = Parent;

                    DXTabControl nTab = new DXTabControl
                    {
                        Parent = TabButton.Parent.Parent,
                        Location = new Point(TabButton.DisplayArea.X - ActiveScene.Location.X, TabButton.DisplayArea.Y - ActiveScene.Location.Y),
                        Visible =  true,

                        PassThrough = TabButton.Parent.PassThrough,
                        Size = TabButton.Parent.Size,
                        Movable = TabButton.Parent.Movable,
                        Border = TabButton.Parent.Border,
                        BorderColour = TabButton.Parent.BorderColour,
                        AllowResize = TabButton.Parent.AllowResize,
                    };

                    Parent = nTab;
                    nTab.SelectedTab = this;
                    oldParent.Dispose();
                }

                TabButton.Tag = null;
                Size = new Size(Parent.Size.Width - Location.X, Parent.Size.Height - Location.Y);


            }
        }

        private void TabButton_LocationChanged(object sender, EventArgs e)
        {
            if (FixedGeometry != null) { ApplyFixedGeometry(); return; }
            if (Updating || !TabButton.IsMoving) return;

            if (Parent?.Parent == null) return;


            const int threshhold = 20;
            foreach (DXControl control in Parent?.Parent.Controls) //parent.parent
            {
                DXTabControl tab = control as DXTabControl;

                if (tab == null) continue;
                if (tab.DisplayArea.Left - TabButton.DisplayArea.Right > threshhold || tab.DisplayArea.Top - TabButton.DisplayArea.Bottom > threshhold ||
                    TabButton.DisplayArea.Left - tab.DisplayArea.Right > threshhold || TabButton.DisplayArea.Top - tab.DisplayArea.Top > threshhold) continue;


                DXControl oldParent = Parent;
                Updating = true;
                Parent = control;

                Updating = false;

                if (!(oldParent is DXTabControl))
                    oldParent.Dispose();
                //FOUND 

                //Visible = tab == Parent;
                Parent = tab;

                if (OldOpacity.HasValue)
                    Opacity = OldOpacity.Value;

                TabButton.Tag = null;

                int w = 0;
                int pivot = TabButton.Location.X + TabButton.Size.Width / 2;
                for (int i = 0; i < tab.TabButtons.Count; i++)
                {
                    DXButton button = tab.TabButtons[i];

                    w += button.Size.Width;

                    if (w < pivot) continue;

                    if (tab.TabButtons[i] == TabButton) return; //IF SAME PARENT


                    tab.TabButtons.Remove(TabButton);
                    tab.TabButtons.Insert(i, TabButton);
                    Updating = true;
                    tab.TabsChanged();
                    Updating = false;
                    break;
                }


                return;
            }

            if (!(Parent is DXTabControl))
            {
                Parent.Location = new Point(TabButton.DisplayArea.X - ActiveScene.Location.X, TabButton.DisplayArea.Y - ActiveScene.Location.Y);
                return;
            }


            DXControl panel = new DXLabel
            {
                Visible = true,
                Parent = Parent.Parent, //Parent.Parent
                Size = Parent.Size,
                Location = new Point(TabButton.DisplayArea.X - ActiveScene.Location.X, TabButton.DisplayArea.Y - ActiveScene.Location.Y),
            };

            TabButton.Tag = Parent.Size;
            Parent = panel;
            Visible = true;

            OldOpacity = Opacity;
            Opacity = 0.5F;

        }

        protected internal override void UpdateBorderInformation()
        {
            BorderInformation = null;
            if (!Border || Size.Width == 0 || Size.Height == 0) return;

            BorderInformation = new[]
            {
                new Vector2(1, 1),
                new Vector2(Size.Width - 1, 1 ),
                new Vector2(Size.Width - 1, Size.Height - 1),
                new Vector2(1 , Size.Height - 1),
                new Vector2(1 , 1)
            };
        }

        public override void Draw()
        {
            if (!IsVisible || Size.Width == 0 || Size.Height == 0) return;

            OnBeforeDraw();
            DrawControl();
            OnBeforeChildrenDraw();
            if (DrawOtherBorder)
                DrawTabBorder();
            DrawChildControls();
            DrawBorder();
            OnAfterDraw();
        }

        protected void DrawTabBorder()
        {
            if (InterfaceLibrary == null) return;

            Surface oldSurface = DXManager.CurrentSurface;
            DXManager.SetSurface(DXManager.ScratchSurface);
            DXManager.Device.Clear(ClearFlags.Target, 0, 0, 0);

            DrawEdges();

            DXManager.SetSurface(oldSurface);

            float oldOpacity = DXManager.Opacity;

            DXManager.SetOpacity(Opacity);

            PresentTexture(DXManager.ScratchTexture, Parent, DisplayArea, ForeColour, this);

            DXManager.SetOpacity(oldOpacity);
        }
        public void DrawEdges()
        {
            InterfaceLibrary.Draw(25, 0, 0, Color.White, false, 1F, ImageType.Image);

            Size s = InterfaceLibrary.GetSize(26);
            InterfaceLibrary.Draw(26, Size.Width - s.Width, 0, Color.White, false, 1F, ImageType.Image);

            s = InterfaceLibrary.GetSize(8);
            InterfaceLibrary.Draw(8, 0, Size.Height - s.Height, Color.White, false, 1F, ImageType.Image);

            s = InterfaceLibrary.GetSize(9);
            InterfaceLibrary.Draw(9, Size.Width - s.Width, Size.Height - s.Height, Color.White, false, 1F, ImageType.Image);

            int x = s.Width;
            int y = s.Height;

            s = InterfaceLibrary.GetSize(2);
            InterfaceLibrary.Draw(2, x, 0, Color.White, new Rectangle(0, 0, Size.Width - x * 2, s.Height), 1f, ImageType.Image);
            InterfaceLibrary.Draw(2, x, Size.Height - s.Height, Color.White, new Rectangle(0, 0, Size.Width - x * 2, s.Height), 1f, ImageType.Image);

            s = InterfaceLibrary.GetSize(1);
            InterfaceLibrary.Draw(1, 0, y, Color.White, new Rectangle(0, 0, s.Width, Size.Height - y * 2), 1F, ImageType.Image);
            InterfaceLibrary.Draw(1, Size.Width - s.Width, y, Color.White, new Rectangle(0, 0, s.Width, Size.Height - y * 2), 1F, ImageType.Image);
        }

        #endregion

        #region IDisposable
        protected override void Dispose(bool disposing)
        {
            (Parent as DXTabControl)?.SetNewTab();

            base.Dispose(disposing);

            if (disposing)
            {

                if (TabButton != null)
                {
                    if (!TabButton.IsDisposed)
                        TabButton.Dispose();

                    TabButton = null;
                }

                OldOpacity = null;
                _DrawOtherBorder = false;
                Updating = false;

                _CurrentTabControl = null;
                _Selected = false;


                SelectedChanged = null;
                DrawOtherBorderChanged = null;
                CurrentTabControlChanged = null;
            }
        }
        #endregion
    }
}
