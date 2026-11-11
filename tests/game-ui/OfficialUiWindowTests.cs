using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Client.Controls;
using Client.Envir;
using Client.UserModels;
using ImageManager;

internal static class OfficialUiWindowTests
{
    private static int checks;
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (ArgumentException) { rejected = true; }
        Check(rejected, message);
    }
    private sealed class Window : DXWindow
    {
        public bool PersistVisibility = true;
        public WindowType Identity = WindowType.CharacterBox;
        public override void OnIsMovingChanged(bool oldValue, bool newValue) { }
        public override WindowType Type => Identity;
        public override bool CustomSize => true;
        public override bool AutomaticVisiblity => PersistVisibility;
    }
    private static MirLibrary Fixture(string directory)
    {
        string path = Path.Combine(directory, "skin.Zl");
        var frames = new Mir3Image[5];
        frames[0] = new Mir3Image { Width = 100, Height = 80, Data = new byte[25 * 20 * 8] };
        for (int i = 1; i < frames.Length; i++) frames[i] = new Mir3Image { Width = 20, Height = 12, Data = new byte[5 * 3 * 8] };
        new Mir3Library { Images = frames }.Save(path);
        var library = new MirLibrary(path); library.ReadLibrary(); return library;
    }
    private static GameUiWindowSkin Skin(MirLibrary library, bool fixedSize = false)
    {
        return new GameUiWindowSkin(library, 0, new Padding(8, 24, 8, 8), new Padding(6, 8, 6, 6), new Padding(4),
            new Size(60, 50), fixedSize, new Rectangle(8, 4, 64, 16), new Rectangle(8, 4, 60, 16),
            new Rectangle(72, 4, 20, 12), new ButtonImageStates(1, 2, 3, 4));
    }
    internal static void Run(string directory)
    {
        using (var library = Fixture(directory))
        using (var parent = new DXControl { Location = new Point(20, 30), Size = new Size(800, 600), IsVisible = true })
        using (var window = new Window { Parent = parent, Location = new Point(10, 20), Size = new Size(100, 80) })
        {
            Size legacy = window.GetSize(new Size(80, 60));
            Point legacyClose = window.CloseButton.Location;
            Check(window.GetClientArea(legacy).Size == new Size(80, 60), "Legacy conversion round trip");
            var skin = Skin(library);
            window.WindowSkin = skin;
            Check(window.ClientArea == new Rectangle(8, 24, 84, 48), "Skin client area");
            Check(window.GetSize(window.ClientArea.Size) == window.Size, "Skin size round trip");
            window.SetClientSize(new Size(144, 88));
            Check(window.Size == new Size(160, 120) && window.ClientArea.Size == new Size(144, 88), "SetClientSize uses skin geometry");
            Check(window.CloseButton.Location == new Point(132, 4), "Close right anchor");
            Check(window.TitleLabel.Location.X == 8 + (124 - window.TitleLabel.Size.Width) / 2, "Title horizontal anchor");
            var slices = skin.GetSlices(window.Size);
            Check(slices.Length == 9 && slices.Sum(x => x.Destination.Width * x.Destination.Height) == 160 * 120, "Nine-slice coverage");
            Check(slices[0].Source.Size == slices[0].Destination.Size && slices[8].Source.Size == slices[8].Destination.Size, "Corners must not scale");
            for (int i = 0; i < slices.Length; i++)
                for (int j = i + 1; j < slices.Length; j++)
                    Check(!slices[i].Destination.IntersectsWith(slices[j].Destination), "Overlapping slices");
            Check(!window.IsMouseOver(new Point(window.DisplayArea.X + 1, window.DisplayArea.Y + 1)), "Transparent decoration intercepts map");
            Check(window.IsMouseOver(new Point(window.DisplayArea.X + 10, window.DisplayArea.Y + 30)), "Body input lost");
            window.OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, window.DisplayArea.X + 10, window.DisplayArea.Y + 30, 0));
            Check(!window.IsMoving && window.Movable, "Body starts title drag or changes Movable");
            window.OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
            window.OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, window.DisplayArea.X + 10, window.DisplayArea.Y + 10, 0));
            Check(window.IsMoving, "Title does not drag");
            window.IsMoving = false; window.OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
            window.Size = new Size(1, 1);
            Check(window.Size == new Size(60, 50) && window.ClientArea.Size == new Size(44, 18), "Minimum size and client geometry diverged");
            window.WindowValid = true; window.Opacity = 0.5F; Check(!window.WindowValid, "Opacity did not invalidate window cache");
            window.Size = new Size(100, 80); window.WindowSkin = Skin(library, true);
            window.Size = new Size(200, 160); Check(window.Size == new Size(100, 80), "Fixed pixel frame scaled");
            Check(window.WindowSkin.GetSlices(window.Size).Length == 1, "Fixed frame sliced");
            Reject(() => window.SetClientSize(new Size(100, 100)), "Fixed client size accepted scaling");
            for (int i = 0; i < 25; i++) { window.WindowSkin = null; window.WindowSkin = skin; }
            window.Visible = true; window.CloseButton.InvokeMouseClick(); Check(!window.Visible, "Close handler lost after rebinding");
            window.Visible = true; window.OnKeyDown(new KeyEventArgs(Keys.Escape)); Check(!window.Visible, "Esc no longer closes");
            window.WindowSkin = null; window.Size = new Size(100, 80);
            Check(window.GetSize(new Size(80, 60)) == legacy && window.CloseButton.Location == legacyClose, "Legacy geometry changed after skin removal");
            Check(window.CloseButton.LibraryFile == Library.LibraryFile.Interface && window.CloseButton.Index == 15 && window.CloseButton.ImageStates == null, "Legacy close binding lost");
            Reject(() => new GameUiWindowSkin(library, 0, new Padding(-1), new Padding(4), new Padding(4), new Size(60, 50), false, Rectangle.Empty, Rectangle.Empty, Rectangle.Empty), "Negative padding accepted");
            Reject(() => new GameUiWindowSkin(library, 0, new Padding(8), new Padding(4), new Padding(4), new Size(10, 10), false, Rectangle.Empty, Rectangle.Empty, Rectangle.Empty), "Invalid minimum accepted");
        }
        Check(DXWindow.Windows.All(x => !(x is Window)), "Disposed windows retained");
        Console.WriteLine("Official UI window skin: " + checks + " checks passed.");
    }
}
