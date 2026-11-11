using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Client.Envir;
using Client.UserModels;
using Library;
using SlimDX.Direct3D9;
using Font = System.Drawing.Font;

//Cleaned
namespace Client.Controls
{
    public abstract class DXWindow : DXControl
    {
        #region Properties

        public static List<DXWindow> Windows = new List<DXWindow>();

        #region HasTopBorder

        public bool HasTopBorder
        {
            get => _HasTopBorder;
            set
            {
                if (_HasTopBorder == value) return;

                bool oldValue = _HasTopBorder;
                _HasTopBorder = value;

                OnHasTopBorderChanged(oldValue, value);
            }
        }
        private bool _HasTopBorder;
        public event EventHandler<EventArgs> HasTopBorderChanged;
        public virtual void OnHasTopBorderChanged(bool oValue, bool nValue)
        {
            HasTopBorderChanged?.Invoke(this, EventArgs.Empty);

            UpdateClientArea();
        }

        #endregion
        
        #region HasTitle

        public bool HasTitle
        {
            get => _HasTitle;
            set
            {
                if (_HasTitle == value) return;

                bool oldValue = _HasTitle;
                _HasTitle = value;

                OnHasTitleChanged(oldValue, value);
            }
        }
        private bool _HasTitle;
        public event EventHandler<EventArgs> HasTitleChanged;
        public virtual void OnHasTitleChanged(bool oValue, bool nValue)
        {
            HasTitleChanged?.Invoke(this, EventArgs.Empty);

            UpdateClientArea();
            if (TitleLabel == null) return;
            TitleLabel.Visible = HasTitle && (WindowSkin == null || !WindowSkin.TitleArea.IsEmpty);
        }

        #endregion

        #region HasFooter

        public bool HasFooter
        {
            get => _HasFooter;
            set
            {
                if (_HasFooter == value) return;

                bool oldValue = _HasFooter;
                _HasFooter = value;

                OnHasFooterChanged(oldValue, value);
            }
        }
        private bool _HasFooter;
        public event EventHandler<EventArgs> HasFooterChanged;
        public virtual void OnHasFooterChanged(bool oValue, bool nValue)
        {
            HasFooterChanged?.Invoke(this, EventArgs.Empty);

            UpdateClientArea();
        }

        #endregion

        #region ClientArea

        public Rectangle ClientArea
        {
            get => _ClientArea;
            set
            {
                if (_ClientArea == value) return;

                Rectangle oldValue = _ClientArea;
                _ClientArea = value;

                OnClientAreaChanged(oldValue, value);
            }
        }
        private Rectangle _ClientArea;
        public event EventHandler<EventArgs> ClientAreaChanged;
        public virtual void OnClientAreaChanged(Rectangle oValue, Rectangle nValue)
        {
            ClientAreaChanged?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        public abstract WindowType Type { get; }
        public abstract bool CustomSize { get; }
        public abstract bool AutomaticVisiblity { get; }

        public DXButton CloseButton { get; protected set; }
        public DXLabel TitleLabel { get; protected set; }

        private GameUiWindowSkin _WindowSkin;
        private MirLibrary _LegacyCloseLibrary;
        private LibraryFile _LegacyCloseLibraryFile;
        private int _LegacyCloseIndex;
        private ButtonImageStates _LegacyCloseImages;
        private bool _LegacyCloseFixedSize, _LegacyCloseVisible;
        private Size _LegacyCloseSize, _SkinTextureSize;
        private bool _LegacyTitleAutoSize;
        private TextFormatFlags _LegacyTitleDrawFormat;
        private Size _LegacyTitleSize;

        public GameUiWindowSkin WindowSkin
        {
            get => _WindowSkin;
            set
            {
                if (ReferenceEquals(_WindowSkin, value)) return;
                if (_WindowSkin == null && CloseButton != null)
                {
                    _LegacyCloseLibrary = CloseButton.Library;
                    _LegacyCloseLibraryFile = CloseButton.LibraryFile;
                    _LegacyCloseIndex = CloseButton.Index;
                    _LegacyCloseImages = CloseButton.ImageStates;
                    _LegacyCloseFixedSize = CloseButton.FixedSize;
                    _LegacyCloseVisible = CloseButton.Visible;
                    _LegacyCloseSize = CloseButton.Size;
                }
                if (_WindowSkin == null && TitleLabel != null)
                {
                    _LegacyTitleAutoSize = TitleLabel.AutoSize;
                    _LegacyTitleDrawFormat = TitleLabel.DrawFormat;
                    _LegacyTitleSize = TitleLabel.Size;
                }
                _WindowSkin = value;
                if (TitleLabel != null)
                {
                    TitleLabel.AutoSize = false;
                    TitleLabel.DrawFormat = value == null ? _LegacyTitleDrawFormat :
                        TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                    if (value == null)
                    {
                        TitleLabel.Size = _LegacyTitleSize;
                        TitleLabel.AutoSize = _LegacyTitleAutoSize;
                    }
                }
                if (CloseButton != null)
                {
                    if (value?.CloseImages != null) CloseButton.BindButtonImages(value.Library, value.CloseImages);
                    else
                    {
                        if (_LegacyCloseImages != null) CloseButton.BindButtonImages(_LegacyCloseLibrary, _LegacyCloseImages);
                        else if (_LegacyCloseLibrary != null) CloseButton.BindImage(_LegacyCloseLibrary, _LegacyCloseIndex);
                        else
                        {
                            CloseButton.BindImage(null, -1);
                            CloseButton.LibraryFile = _LegacyCloseLibraryFile;
                            CloseButton.Index = _LegacyCloseIndex;
                        }
                        CloseButton.FixedSize = _LegacyCloseFixedSize;
                        CloseButton.Size = _LegacyCloseSize;
                    }
                    CloseButton.Visible = _LegacyCloseVisible && (value == null || !value.CloseArea.IsEmpty);
                }
                if (TitleLabel != null) TitleLabel.Visible = HasTitle && (value == null || !value.TitleArea.IsEmpty);
                DisposeTexture();
                if (value != null) Size = value.ConstrainSize(Size);
                UpdateClientArea();
                UpdateLocations();
                OnWindowSkinChanged();
            }
        }

        // Derived windows can reapply native control geometry after the skin has
        // finished updating close-button images, client bounds and title locations.
        protected virtual void OnWindowSkinChanged() { }

        protected virtual void OnCloseButtonClick() => Visible = false;

        public Texture WindowTexture;
        public Surface WindowSurface;
        public bool WindowValid;

        public override Size Size
        {
            get => base.Size;
            set => base.Size = WindowSkin == null ? value : WindowSkin.ConstrainSize(value);
        }

        public override void OnSizeChanged(Size oValue, Size nValue)
        {
            base.OnSizeChanged(oValue, nValue);

            UpdateClientArea();
            UpdateLocations();

            if (_OfficialLayout != null) { if (IsResizing) SaveOfficialLayout(); return; }
            if (!AreLayoutWritesSuppressed && Settings != null && IsResizing)
            {
                Settings.Size = nValue;
                Settings.Location = Location;
            }
        }
        public override void OnParentChanged(DXControl oValue, DXControl nValue)
        {
            base.OnParentChanged(oValue, nValue);

            if (Parent == null) return;

            UpdateClientArea();

            UpdateLocations();
        }
        public override void OnLocationChanged(Point oValue, Point nValue)
        {
            base.OnLocationChanged(oValue, nValue);

            if (_OfficialLayout != null) { if (IsMoving) SaveOfficialLayout(); return; }
            if (!AreLayoutWritesSuppressed && Settings != null && IsMoving)
                Settings.Location = nValue;
        }

        internal event EventHandler VisibilityApplied;

        public override void OnVisibleChanged(bool oValue, bool nValue)
        {
            base.OnVisibleChanged(oValue, nValue);
            
            if (IsVisible)
                BringToFront();

            if (_OfficialLayout != null) { if (AutomaticVisiblity) SaveOfficialLayout(); }
            else if (!AreLayoutWritesSuppressed && Settings != null && AutomaticVisiblity)
                Settings.Visible = nValue;
            // Layout subscribers run after native visibility and foreground ordering finish.
            VisibilityApplied?.Invoke(this, EventArgs.Empty);
        }

        public WindowSetting Settings;
        private GameUiLayoutStore _OfficialLayout;
        internal GameUiLayoutStore OfficialLayoutStore => _OfficialLayout;
        private int _SuppressLayoutWrites;
        private bool _OfficialLayoutLoaded;

        [ThreadStatic]
        private static List<SceneLayoutWriteScope> _SceneLayoutWriteScopes;

        private bool AreLayoutWritesSuppressed
        {
            get
            {
                if (_SuppressLayoutWrites != 0) return true;
                if (_SceneLayoutWriteScopes == null) return false;
                foreach (SceneLayoutWriteScope scope in _SceneLayoutWriteScopes)
                    for (DXControl control = this; control != null; control = control.Parent)
                        if (ReferenceEquals(control, scope.Root)) return true;
                return false;
            }
        }

        // Includes windows parented under this scene after the scope starts.
        // Other scenes and ordinary user changes outside the scope keep their own policy.
        public static IDisposable SuppressSceneLayoutWrites(DXControl sceneRoot)
        {
            if (sceneRoot == null) throw new ArgumentNullException(nameof(sceneRoot));
            if (sceneRoot.IsDisposed) throw new ObjectDisposedException(nameof(sceneRoot));
            if (_SceneLayoutWriteScopes == null) _SceneLayoutWriteScopes = new List<SceneLayoutWriteScope>();
            var scope = new SceneLayoutWriteScope(sceneRoot);
            _SceneLayoutWriteScopes.Add(scope);
            return scope;
        }

        private sealed class SceneLayoutWriteScope : IDisposable
        {
            internal DXControl Root { get; private set; }
            private readonly int ownerThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
            public SceneLayoutWriteScope(DXControl root) { Root = root; }
            public void Dispose()
            {
                if (Root == null) return;
                if (ownerThread != System.Threading.Thread.CurrentThread.ManagedThreadId)
                    throw new InvalidOperationException("Layout write suppression must end on its owning thread.");
                _SceneLayoutWriteScopes.Remove(this);
                Root = null;
            }
        }

        // Called by the startup style context before any scoped window LoadSettings.
        public void UseOfficialLayout(GameUiLayoutStore store)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            Settings = null;
            _OfficialLayout = store;
            _OfficialLayoutLoaded = false;
        }
        // Used only when an installation transaction restores its captured Legacy owner.
        internal void RestoreLegacyLayout(WindowSetting settings)
        {
            _OfficialLayout = null;
            _OfficialLayoutLoaded = false;
            Settings = settings;
        }
        public IDisposable SuppressLayoutWrites()
        {
            _SuppressLayoutWrites++;
            return new LayoutWriteScope(this);
        }
        private sealed class LayoutWriteScope : IDisposable
        {
            private DXWindow owner;
            public LayoutWriteScope(DXWindow owner) { this.owner = owner; }
            public void Dispose()
            {
                if (owner == null) return;
                owner._SuppressLayoutWrites--;
                owner = null;
            }
        }
        private void SaveOfficialLayout()
        {
            if (!_OfficialLayoutLoaded || AreLayoutWritesSuppressed || Type == WindowType.None) return;
            _OfficialLayout.TrySave(Config.GameSize, Type, new GameUiWindowLayout(Location, Size, AutomaticVisiblity ? (bool?)Visible : null));
        }

        #endregion
        
        protected DXWindow()
        {
            Windows.Add(this);

            DrawTexture = true;
            BackColour = Color.FromArgb(16, 8, 8);
            HasTitle = true;
            Movable = true;
            HasTopBorder = true;
            Sort = true;

            CloseButton = new DXButton
            {
                Parent = this,
                Index = 15,
                LibraryFile = LibraryFile.Interface,
            };
            CloseButton.MouseClick += (o, e) => OnCloseButtonClick();

            TitleLabel = new DXLabel
            {
                Text = "Window",
                Parent = this,
                Font = new Font(Config.FontName, CEnvir.FontSize(10F), FontStyle.Bold),
                ForeColour = Color.FromArgb(198, 166, 99),
                Outline = true,
                OutlineColour = Color.Black,
                Visible = HasTitle,
                IsControl = false,
            };
            TitleLabel.SizeChanged += (o, e) => UpdateLocations();
        }

        #region Methods

        public override void ResolutionChanged()
        {
            Settings = null;
            _OfficialLayoutLoaded = false;

            base.ResolutionChanged();

            DisposeTexture();
        }

        protected override void CreateTexture()
        {
            if (WindowSkin != null)
            {
                // Sprite.Begin fixes a projection for the scene backbuffer. Keep the
                // cache target at that size so one logical pixel remains one texel.
                Size targetSize = new Size(DXManager.Parameters.BackBufferWidth, DXManager.Parameters.BackBufferHeight);
                if (WindowTexture == null || _SkinTextureSize != targetSize)
                {
                    DisposeTexture();
                    WindowTexture = new Texture(DXManager.Device, targetSize.Width, targetSize.Height, 1, Usage.RenderTarget, Format.A8R8G8B8, Pool.Default);
                    WindowSurface = WindowTexture.GetSurfaceLevel(0);
                    _SkinTextureSize = targetSize;
                    DXManager.ControlList.Add(this);
                }
                return;
            }
            base.CreateTexture();

            if (WindowTexture == null || DisplayArea.Size != TextureSize)
            {
                WindowTexture = new Texture(DXManager.Device, DXManager.Parameters.BackBufferWidth, DXManager.Parameters.BackBufferHeight, 1, Usage.RenderTarget, Format.A8R8G8B8, Pool.Default);
                WindowSurface = WindowTexture.GetSurfaceLevel(0);
                WindowValid = false;
            }
        }
        public override void DisposeTexture()
        {
            base.DisposeTexture();
            _SkinTextureSize = Size.Empty;
            WindowValid = false;

            if (WindowTexture != null)
            {
                if (!WindowTexture.Disposed)
                    WindowTexture.Dispose();

                WindowTexture = null;
            }

            if (WindowSurface != null)
            {
                if (!WindowSurface.Disposed)
                    WindowSurface.Dispose();

                WindowSurface = null;
            }

        }

        private void UpdateLocations()
        {
            if (WindowSkin != null)
            {
                if (CloseButton != null) CloseButton.Location = WindowSkin.GetCloseArea(Size).Location;
                Rectangle title = WindowSkin.GetTitleArea(Size);
                if (TitleLabel != null)
                {
                    TitleLabel.Location = title.Location;
                    TitleLabel.Size = title.Size;
                }
                return;
            }

            if (CloseButton != null)
                CloseButton.Location = new Point(DisplayArea.Width - CloseButton.Size.Width - 5, 5);

            if (TitleLabel != null)
                TitleLabel.Location = new Point((DisplayArea.Width - TitleLabel.Size.Width) / 2, 8);

        }

        protected internal override void UpdateDisplayArea()
        {
            base.UpdateDisplayArea();

            WindowValid = false;
        }
        
        public override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            switch (e.KeyCode)
            {
                case Keys.Escape:
                    if (CloseButton.Visible)
                    {
                        CloseButton.InvokeMouseClick();
                        if (!Config.EscapeCloseAll)
                            e.Handled = true;
                    }
                    break;
            }
        }

        public void UpdateClientArea()
        {
            ClientArea = GetClientArea(Size);
        }

        public void SetClientSize(Size clientSize)
        {
            Size = GetSize(clientSize);
        }
        public Size GetSize(Size clientSize)
        {
            if (WindowSkin != null) return WindowSkin.GetWindowSize(clientSize);
            int w = 3 + 6 + 6 + 3; //Border Padding Padding Border
            int h = 6 + 6; //Padding Padding


            if (!HasTopBorder)
                h += NoFooterSize;
            else if (HasTitle)
                h += HeaderSize;
            else
                h += HeaderBarSize;

            if (!HasFooter)
                h += NoFooterSize;
            else
                h += FooterSize;

            return new Size(clientSize.Width + w, clientSize.Height + h);
        }

        public Rectangle GetClientArea(Size size)
        {
            if (WindowSkin != null) return WindowSkin.GetClientArea(size);
            int x = 6 + 3;
            int y = 6;

            if (!HasTopBorder)
                y += NoFooterSize;
            else if (HasTitle)
                y += HeaderSize;
            else
                y += HeaderBarSize;

            int w = size.Width - x * 2;
            int h = size.Height - y - 6;


            if (!HasFooter)
                h -= NoFooterSize;
            else
                h -= FooterSize;

            return new Rectangle(x, y, w, h);
        }

        public override void Draw()
        {
            if (!IsVisible || Size.Width == 0 || Size.Height == 0) return;

            OnBeforeDraw();
            if (WindowSkin == null) DrawControl();
            DrawWindow();
            OnBeforeChildrenDraw();
            DrawChildControls();
            if (WindowSkin == null) DrawBorder();
            OnAfterDraw();
        }
        protected void DrawWindow()
        {
            if (WindowSkin != null)
            {
                DrawSkin();
                return;
            }
            if (InterfaceLibrary == null) return;
            
            if (!WindowValid)
            {
                Surface oldSurface = DXManager.CurrentSurface;
                DXManager.SetSurface(WindowSurface);
                DXManager.Device.Clear(ClearFlags.Target, 0, 0, 0);

                DrawEdges();

                DXManager.SetSurface(oldSurface);
                WindowValid = true;
            }

            float oldOpacity = DXManager.Opacity;

            DXManager.SetOpacity(Opacity);
            PresentTexture(WindowTexture, Parent, DisplayArea, ForeColour, this);


            DXManager.SetOpacity(oldOpacity);
        }
        
        private void DrawSkin()
        {
            CreateTexture();
            if (!WindowValid)
            {
                Surface oldSurface = DXManager.CurrentSurface;
                SlimDX.Matrix oldTransform = DXManager.Sprite.Transform;
                try
                {
                    DXManager.SetSurface(WindowSurface);
                    DXManager.Device.Clear(ClearFlags.Target, 0, 0, 0);
                    foreach (GameUiSkinSlice slice in WindowSkin.GetSlices(Size))
                    {
                        Rectangle source = slice.Source, destination = slice.Destination;
                        DXManager.Sprite.Flush();
                        DXManager.Sprite.Transform = SlimDX.Matrix.Scaling((float)destination.Width / source.Width, (float)destination.Height / source.Height, 1F) *
                            SlimDX.Matrix.Translation(destination.X, destination.Y, 0);
                        WindowSkin.Library.Draw(WindowSkin.FrameIndex, 0, 0, Color.White, source, 1F, ImageType.Image);
                    }
                    WindowValid = true;
                }
                finally
                {
                    DXManager.Sprite.Flush();
                    DXManager.Sprite.Transform = oldTransform;
                    DXManager.SetSurface(oldSurface);
                }
            }
            float opacity = DXManager.Opacity;
            try
            {
                // Preserve per-pixel transparency at fractional window opacity.
                DXManager.SetOpacity(1F);
                float alpha = float.IsNaN(Opacity) ? 1F : Math.Max(0F, Math.Min(1F, Opacity));
                Color tint = Color.FromArgb((int)(ForeColour.A * alpha), ForeColour);
                PresentTexture(WindowTexture, Parent, DisplayArea, tint, this);
            }
            finally { DXManager.SetOpacity(opacity); }
            ExpireTime = CEnvir.Now + Config.CacheDuration;
        }

        public override Size GetAcceptableResize(Size size)
        {
            return WindowSkin == null ? base.GetAcceptableResize(size) : WindowSkin.ConstrainSize(size);
        }
        public override bool IsMouseOver(Point point)
        {
            if (WindowSkin == null) return base.IsMouseOver(point);
            if (!IsVisible || !IsControl) return false;
            if (!DisplayArea.Contains(point)) return Modal;
            for (int i = Controls.Count - 1; i >= 0; i--)
                if (Controls[i].IsMouseOver(point)) return true;
            Point local = new Point(point.X - DisplayArea.X, point.Y - DisplayArea.Y);
            return !PassThrough && (WindowSkin.GetInputArea(Size).Contains(local) || WindowSkin.GetDragArea(Size).Contains(local));
        }
        public override void OnMouseDown(MouseEventArgs e)
        {
            if (WindowSkin == null) { base.OnMouseDown(e); return; }
            bool movable = Movable;
            Point local = new Point(e.X - DisplayArea.X, e.Y - DisplayArea.Y);
            try
            {
                Movable = movable && WindowSkin.GetDragArea(Size).Contains(local);
                base.OnMouseDown(e);
            }
            finally { Movable = movable; }
        }
        public override void OnOpacityChanged(float oldValue, float newValue)
        {
            WindowValid = false;
            base.OnOpacityChanged(oldValue, newValue);
        }

        private void DrawEdges()
        {
            Size s;

            if (HasTopBorder)
            {
                s = InterfaceLibrary.GetSize(0);
                InterfaceLibrary.Draw(0, 0, 0, Color.White, new Rectangle(0, 0, Size.Width, s.Height), 1f, ImageType.Image);
            }
            else
            {
                s = InterfaceLibrary.GetSize(2);
                InterfaceLibrary.Draw(2, 0, 0, Color.White, new Rectangle(0, 0, Size.Width, s.Height), 1f, ImageType.Image);
            }

            int y = s.Height;

            s = InterfaceLibrary.GetSize(1);
            int x = s.Width;
            InterfaceLibrary.Draw(1, 0, y, Color.White, new Rectangle(0, 0, s.Width, Size.Height - y), 1f, ImageType.Image);
            InterfaceLibrary.Draw(1, Size.Width - s.Width, y, Color.White, new Rectangle(0, 0, s.Width, Size.Height - y), 1F, ImageType.Image);


            if (HasTitle)
            {
                s = InterfaceLibrary.GetSize(3);
                InterfaceLibrary.Draw(3, x, y, Color.White, new Rectangle(0, 0, Size.Width - x * 2, Size.Height), 1f, ImageType.Image);

                y += s.Height;

                InterfaceLibrary.Draw(4, 0, y - 3, Color.White, false, 1F, ImageType.Image);

                s = InterfaceLibrary.GetSize(5);
                InterfaceLibrary.Draw(5, Size.Width - s.Width, y - 3, Color.White, false, 1F, ImageType.Image);
            }

            if (HasTopBorder)
            {
                //2X Corner
                InterfaceLibrary.Draw(11, 0, 0, Color.White, false, 1F, ImageType.Image);

                s = InterfaceLibrary.GetSize(12);
                InterfaceLibrary.Draw(12, Size.Width - s.Width, 0, Color.White, false, 1F, ImageType.Image);
            }
            else
            {
                //2X Corner
                InterfaceLibrary.Draw(25, 0, 0, Color.White, false, 1F, ImageType.Image);

                s = InterfaceLibrary.GetSize(26);
                InterfaceLibrary.Draw(26, Size.Width - s.Width, 0, Color.White, false, 1F, ImageType.Image);
            }

            if (!HasFooter)
            {
                s = InterfaceLibrary.GetSize(2);
                InterfaceLibrary.Draw(2, 0, Size.Height - s.Height, Color.White, new Rectangle(0, 0, Size.Width, s.Height), 1f, ImageType.Image);

                s = InterfaceLibrary.GetSize(8);
                InterfaceLibrary.Draw(8, 0, Size.Height - s.Height, Color.White, false, 1F, ImageType.Image);

                s = InterfaceLibrary.GetSize(9);
                InterfaceLibrary.Draw(9, Size.Width - s.Width, Size.Height - s.Height, Color.White, false, 1F, ImageType.Image);
            }
            else
            {
                s = InterfaceLibrary.GetSize(0);
                InterfaceLibrary.Draw(0, 0, Size.Height - s.Height, Color.White, new Rectangle(0, 0, Size.Width, s.Height), 1f, ImageType.Image);

                y = s.Height;

                s = InterfaceLibrary.GetSize(10);
                InterfaceLibrary.Draw(10, x, Size.Height - s.Height - y, Color.White, new Rectangle(0, 0, Size.Width - x * 2, s.Height), 1f, ImageType.Image);

                y += s.Height;

                s = InterfaceLibrary.GetSize(2);
                InterfaceLibrary.Draw(2, 0, Size.Height - y - s.Height, Color.White, new Rectangle(0, 0, Size.Width, s.Height), 1f, ImageType.Image);

                y += s.Height;

                s = InterfaceLibrary.GetSize(6);
                InterfaceLibrary.Draw(6, 0, Size.Height - y - s.Height + 3, Color.White, false, 1F, ImageType.Image);

                s = InterfaceLibrary.GetSize(7);
                InterfaceLibrary.Draw(7, Size.Width - s.Width, Size.Height - y - s.Height + 3, Color.White, false, 1F, ImageType.Image);


                s = InterfaceLibrary.GetSize(13);
                InterfaceLibrary.Draw(13, 0, Size.Height - s.Height, Color.White, false, 1F, ImageType.Image);

                s = InterfaceLibrary.GetSize(14);
                InterfaceLibrary.Draw(14, Size.Width - s.Width, Size.Height - s.Height, Color.White, false, 1F, ImageType.Image);
            }



        }

        public void LoadSettings()
        {
            if (_OfficialLayout != null)
            {
                _OfficialLayoutLoaded = false;
                Settings = null;
                using (SuppressLayoutWrites())
                {
                    GameUiWindowLayout layout;
                    if (Type != WindowType.None && _OfficialLayout.TryGet(Config.GameSize, Type, out layout))
                    {
                        Size restored = CustomSize ? GetAcceptableResize(layout.Size) : Size;
                        // Skin changes cannot restore an out-of-screen or obsolete minimum size.
                        if (layout.Location.X + (long)restored.Width <= Config.GameSize.Width && layout.Location.Y + (long)restored.Height <= Config.GameSize.Height)
                        {
                            if (CustomSize) Size = restored;
                            Location = layout.Location;
                            if (AutomaticVisiblity && layout.Visible.HasValue) Visible = layout.Visible.Value;
                        }
                    }
                }
                _OfficialLayoutLoaded = true;
                return;
            }
            if (Type == WindowType.None || !CEnvir.Loaded) return;

            Settings = CEnvir.WindowSettings.Binding.FirstOrDefault(x => x.Resolution == Config.GameSize && x.Window == Type);

            if (Settings != null)
            {
                ApplySettings();
                return;
            }

            if (AreLayoutWritesSuppressed) return;

            Settings = CEnvir.WindowSettings.CreateNewObject();
            Settings.Resolution = Config.GameSize;
            Settings.Window = Type;
            Settings.Size = Size;
            Settings.Visible = Visible;
            Settings.Location = Location;
        }
        public virtual void ApplySettings()
        {
            if (_OfficialLayout != null) { LoadSettings(); return; }
            if (Settings == null) return;

            Location = Settings.Location;

            if (AutomaticVisiblity)
                Visible = Settings.Visible;

            if (CustomSize)
                Size = Settings.Size;
        }

        #endregion

        #region IDisposable
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                _OfficialLayout = null;
                _OfficialLayoutLoaded = false;
                _WindowSkin = null;
                _LegacyCloseLibrary = null;
                _LegacyCloseImages = null;
                _SkinTextureSize = Size.Empty;
                _HasTopBorder = false;
                _HasTitle = false;
                _HasFooter = false;
                _ClientArea = Rectangle.Empty;

                if (CloseButton != null)
                {
                    if (!CloseButton.IsDisposed)
                        CloseButton.Dispose();
                    CloseButton = null;
                }

                if (TitleLabel != null)
                {
                    if (!TitleLabel.IsDisposed)
                        TitleLabel.Dispose();
                    TitleLabel = null;
                }

                HasTopBorderChanged = null;
                HasTitleChanged = null;
                HasFooterChanged = null;
                ClientAreaChanged = null;

                if (WindowTexture != null)
                {
                    if (!WindowTexture.Disposed)
                        WindowTexture.Dispose();

                    WindowTexture = null;
                }

                if (WindowSurface != null)
                {
                    if (!WindowSurface.Disposed)
                        WindowSurface.Dispose();

                    WindowSurface = null;
                }

                WindowValid = false;
                Settings = null;
                Windows.Remove(this);

            }
        }
        #endregion
    }
}
