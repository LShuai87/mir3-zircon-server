using System;
using System.Drawing;
using System.Windows.Forms;
using Library;

//Cleaned
namespace Client.Controls
{
    // Optional fixed pixel geometry; legacy checkboxes retain content-based sizing.
    public sealed class DXCheckBoxGeometry
    {
        public Size Size { get; }
        public Rectangle Label { get; }
        public Rectangle Box { get; }

        public DXCheckBoxGeometry(Size size, Rectangle label, Rectangle box)
        {
            var area = new Rectangle(Point.Empty, size);
            if (size.Width <= 0 || size.Height <= 0 || size.Width > 16384 || size.Height > 16384 ||
                label.X < 0 || label.Y < 0 || box.X < 0 || box.Y < 0 ||
                label.Width <= 0 || label.Height <= 0 || box.Width <= 0 || box.Height <= 0 ||
                (long)label.X + label.Width > size.Width || (long)label.Y + label.Height > size.Height ||
                (long)box.X + box.Width > size.Width || (long)box.Y + box.Height > size.Height ||
                !area.Contains(label) || !area.Contains(box) || label.IntersectsWith(box))
                throw new ArgumentException("Checkbox label and box must fit without overlap.");
            Size = size; Label = label; Box = box;
        }
    }

    public sealed class DXCheckBox : DXControl
    {
        #region Properites

        #region Checked

        public bool Checked
        {
            get => _Checked;
            set
            {
                if (_Checked == value) return;

                bool oldValue = _Checked;
                _Checked = value;

                OnCheckedChanged(oldValue, value);
            }
        }
        private bool _Checked;
        public event EventHandler<EventArgs> CheckedChanged;
        public void OnCheckedChanged(bool oValue, bool nValue)
        {
            CheckedChanged?.Invoke(this, EventArgs.Empty);

            Box.Index = Checked ? 162 : 161;
        }

        #endregion

        #region ReadOnly

        public bool ReadOnly
        {
            get => _ReadOnly;
            set
            {
                if (_ReadOnly == value) return;

                bool oldValue = _ReadOnly;
                _ReadOnly = value;

                OnReadOnlyChanged(oldValue, value);
            }
        }
        private bool _ReadOnly;
        public event EventHandler<EventArgs> ReadOnlyChanged;
        public void OnReadOnlyChanged(bool oValue, bool nValue)
        {
            ReadOnlyChanged?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        public DXCheckBoxGeometry FixedGeometry { get; private set; }
        private CheckBoxGeometrySnapshot geometrySnapshot;
        private bool updatingGeometry;

        public void UseFixedGeometry(DXCheckBoxGeometry geometry)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(DXCheckBox));
            if (Label == null || Box == null || Label.IsDisposed || Box.IsDisposed)
                throw new InvalidOperationException("Checkbox controls are unavailable.");
            if (geometry != null && (IsMoving || IsResizing))
                throw new InvalidOperationException("Cannot replace geometry during a checkbox interaction.");
            if (geometry == null)
            {
                if (geometrySnapshot == null) return;
                updatingGeometry = true;
                try { FixedGeometry = null; geometrySnapshot.Restore(this); geometrySnapshot = null; }
                finally { updatingGeometry = false; }
                UpdateChildGeometry();
                return;
            }
            if (geometrySnapshot == null) geometrySnapshot = new CheckBoxGeometrySnapshot(this);
            FixedGeometry = geometry;
            UpdateControl();
        }

        private sealed class CheckBoxGeometrySnapshot
        {
            private readonly Size size;
            private readonly Rectangle label, box;
            private readonly bool autoSize, fixedSize, useOffset;
            private readonly TextFormatFlags format;
            internal CheckBoxGeometrySnapshot(DXCheckBox control)
            {
                size = control.Size; label = new Rectangle(control.Label.Location, control.Label.Size);
                box = new Rectangle(control.Box.Location, control.Box.Size);
                autoSize = control.Label.AutoSize; format = control.Label.DrawFormat;
                fixedSize = control.Box.FixedSize; useOffset = control.Box.UseOffSet;
            }
            internal void Restore(DXCheckBox control)
            {
                control.Label.AutoSize = false; control.Label.DrawFormat = format;
                control.Label.Location = label.Location; control.Label.Size = label.Size;
                control.Box.FixedSize = fixedSize; control.Box.UseOffSet = useOffset;
                control.Box.Location = box.Location; control.Box.Size = box.Size;
                control.Label.AutoSize = autoSize; control.Size = size;
            }
        }

        public DXLabel Label { get; private set; }
        public DXImageControl Box { get; private set; }

        public override void OnDisplayAreaChanged(Rectangle oValue, Rectangle nValue)
        {
            base.OnDisplayAreaChanged(oValue, nValue);

            UpdateControl();
        }

        #endregion

        public DXCheckBox()
        {
            Label = new DXLabel
            {
                Parent = this,
                IsControl = false,
                Location = new Point(0, -1)
            };

            Label.DisplayAreaChanged += (o, e) => UpdateChildGeometry();


            Box = new DXImageControl
            {
                Location = new Point(Label.Size.Width + 2, 0),
                Index = 161,
                LibraryFile = LibraryFile.GameInter,
                Parent = this,
                IsControl = false,
            };
            Box.DisplayAreaChanged += (o, e) => UpdateChildGeometry();


            Size = new Size(18, 18);
        }

        #region Methods

        private void UpdateChildGeometry()
        {
            if (Label == null || Box == null || updatingGeometry) return;
            if (FixedGeometry != null) { UpdateControl(); return; }
            Size = new Size(Label.DisplayArea.Width + Box.DisplayArea.Width, Box.DisplayArea.Height);
            Box.Location = new Point(Label.DisplayArea.Width, 0);
        }

        private void UpdateControl()
        {
            if (Label == null || Box == null || updatingGeometry) return;
            if (FixedGeometry != null)
            {
                updatingGeometry = true;
                try
                {
                    Label.AutoSize = false;
                    Label.DrawFormat = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                    Box.FixedSize = true; Box.UseOffSet = false;
                    Label.Location = FixedGeometry.Label.Location; Label.Size = FixedGeometry.Label.Size;
                    Box.Location = FixedGeometry.Box.Location; Box.Size = FixedGeometry.Box.Size;
                    Size = FixedGeometry.Size;
                }
                finally { updatingGeometry = false; }
                return;
            }

            Label.Location = new Point(0, 0);
            Size = new Size(Label.DisplayArea.Width + Box.DisplayArea.Width, Box.DisplayArea.Height);
            Box.Location = new Point(Label.DisplayArea.Width, 0);
        }

        public override void OnMouseClick(MouseEventArgs e)
        {
            if (!IsEnabled) return;

            base.OnMouseClick(e);

            if (ReadOnly) return;

            Checked = !Checked;
        }
        #endregion

        #region IDisposable
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                if (Label != null)
                {
                    if (!Label.IsDisposed)
                        Label.Dispose();
                    Label = null;
                }

                if (Box != null)
                {
                    if (!Box.IsDisposed)
                        Box.Dispose();
                    Box = null;
                }

                FixedGeometry = null; geometrySnapshot = null;
                _Checked = false;
                CheckedChanged = null;

                _ReadOnly = false;
                ReadOnlyChanged = null;
            }
        }
        #endregion
    }
}
