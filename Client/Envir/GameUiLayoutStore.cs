using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using Client.UserModels;

namespace Client.Envir
{
    public sealed class GameUiWindowLayout
    {
        public Point Location { get; }
        public Size Size { get; }
        public bool? Visible { get; }
        public GameUiWindowLayout(Point location, Size size, bool? visible)
        {
            Location = location;
            Size = size;
            Visible = visible;
        }
    }

    // The owner creates one store per startup style context, beside Zircon.ini.
    // This store never receives a WindowSetting/Session and cannot write Legacy data.
    public sealed class GameUiLayoutStore
    {
        public const int SchemaVersion = 1;
        public const string StyleVersion = "OfficialV1";
        public const string FileName = "GameUiLayout.OfficialV1.json";
        private const int MaxBytes = 262144;
        private readonly object sync = new object();
        private Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
        public string FilePath { get; }
        public string LastDiagnostic { get; private set; }

        public sealed class Document
        {
            public int schemaVersion { get; set; }
            public string styleVersion { get; set; }
            public Entry[] windows { get; set; }
        }
        public sealed class Entry
        {
            public int resolutionWidth { get; set; }
            public int resolutionHeight { get; set; }
            public int windowType { get; set; }
            public int x { get; set; }
            public int y { get; set; }
            public int width { get; set; }
            public int height { get; set; }
            public bool? visible { get; set; }
        }
        private static readonly HashSet<Size> Resolutions = new HashSet<Size>
        {
            new Size(1024, 768), new Size(1366, 768), new Size(1280, 800),
            new Size(1440, 900), new Size(1600, 900), new Size(1920, 1080)
        };
        public GameUiLayoutStore(string configurationDirectory)
        {
            if (string.IsNullOrWhiteSpace(configurationDirectory)) throw new ArgumentException("Configuration directory is required.", nameof(configurationDirectory));
            FilePath = Path.Combine(Path.GetFullPath(configurationDirectory), FileName);
            Reload();
        }
        private static string Key(Size resolution, WindowType type) => resolution.Width + "x" + resolution.Height + ":" + (int)type;
        private static bool Valid(Size resolution, WindowType type, Point location, Size size)
        {
            return Resolutions.Contains(resolution) && type != WindowType.None && Enum.IsDefined(typeof(WindowType), type) &&
                size.Width > 0 && size.Height > 0 && location.X >= 0 && location.Y >= 0 &&
                (long)location.X + size.Width <= resolution.Width && (long)location.Y + size.Height <= resolution.Height;
        }
        private static JavaScriptSerializer Serializer() => new JavaScriptSerializer { MaxJsonLength = MaxBytes, RecursionLimit = 12 };
        private static Dictionary<string, object> Fields(object value, params string[] required)
        {
            var fields = value as Dictionary<string, object>;
            if (fields == null || fields.Count != required.Length || required.Any(key => !fields.ContainsKey(key)))
                throw new InvalidDataException("Layout contains missing or unknown fields.");
            return fields;
        }
        private static int Integer(Dictionary<string, object> fields, string name)
        {
            if (!(fields[name] is int)) throw new InvalidDataException("Layout integer required: " + name);
            return (int)fields[name];
        }
        private Dictionary<string, Entry> Read(string path)
        {
            var info = new FileInfo(path);
            if (info.Length > MaxBytes) throw new InvalidDataException("Layout exceeds size limit.");
            string json = File.ReadAllText(path, new UTF8Encoding(false, true));
            var root = Fields(Serializer().DeserializeObject(json), "schemaVersion", "styleVersion", "windows");
            if (Integer(root, "schemaVersion") != SchemaVersion || !Equals(root["styleVersion"], StyleVersion))
                throw new InvalidDataException("Unsupported layout schema or style version.");
            var windows = root["windows"] as object[];
            if (windows == null || windows.Length > 512) throw new InvalidDataException("Invalid layout window list.");
            var result = new Dictionary<string, Entry>();
            foreach (object item in windows)
            {
                var fields = Fields(item, "resolutionWidth", "resolutionHeight", "windowType", "x", "y", "width", "height", "visible");
                var entry = new Entry
                {
                    resolutionWidth = Integer(fields, "resolutionWidth"), resolutionHeight = Integer(fields, "resolutionHeight"),
                    windowType = Integer(fields, "windowType"), x = Integer(fields, "x"), y = Integer(fields, "y"),
                    width = Integer(fields, "width"), height = Integer(fields, "height")
                };
                if (fields["visible"] != null && !(fields["visible"] is bool)) throw new InvalidDataException("Layout visibility must be boolean or null.");
                entry.visible = (bool?)fields["visible"];
                Size resolution = new Size(entry.resolutionWidth, entry.resolutionHeight);
                var type = (WindowType)entry.windowType;
                if (!Valid(resolution, type, new Point(entry.x, entry.y), new Size(entry.width, entry.height)))
                    throw new InvalidDataException("Layout window geometry or identity is invalid.");
                // Upgrade old belt dimensions without writing settings during load.
                // Move only known default positions; keep customized x/y and visibility.
                if (type == WindowType.BeltBox && entry.width == 459 && entry.height == 44)
                {
                    if (entry.x == (resolution.Width - 459) / 2 &&
                        (entry.y == resolution.Height - 198 || entry.y == resolution.Height - 182))
                    {
                        entry.x = (resolution.Width - 518) / 2;
                        entry.y = resolution.Height - 182;
                    }
                    entry.width = 518;
                }
                string key = Key(resolution, type);
                if (result.ContainsKey(key)) throw new InvalidDataException("Duplicate layout window key.");
                result.Add(key, entry);
            }
            return result;
        }
        public void Reload()
        {
            lock (sync)
            {
                LastDiagnostic = null;
                try
                {
                    if (!File.Exists(FilePath)) { entries.Clear(); return; }
                    entries = Read(FilePath);
                }
                catch (Exception error) when (Recoverable(error))
                {
                    entries.Clear();
                    LastDiagnostic = "Layout ignored; defaults retained: " + error.Message;
                }
            }
        }
        public bool TryGet(Size resolution, WindowType type, out GameUiWindowLayout layout)
        {
            lock (sync)
            {
                Entry entry;
                if (entries.TryGetValue(Key(resolution, type), out entry))
                {
                    layout = new GameUiWindowLayout(new Point(entry.x, entry.y), new Size(entry.width, entry.height), entry.visible);
                    return true;
                }
                layout = null;
                return false;
            }
        }
        public bool TrySave(Size resolution, WindowType type, GameUiWindowLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            lock (sync)
            {
                if (!Valid(resolution, type, layout.Location, layout.Size))
                {
                    LastDiagnostic = "Layout save rejected: invalid resolution, window type or geometry.";
                    return false;
                }
                var next = new Dictionary<string, Entry>(entries);
                next[Key(resolution, type)] = new Entry
                {
                    resolutionWidth = resolution.Width, resolutionHeight = resolution.Height, windowType = (int)type,
                    x = layout.Location.X, y = layout.Location.Y, width = layout.Size.Width, height = layout.Size.Height, visible = layout.Visible
                };
                string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    string json = Serializer().Serialize(new Document
                    {
                        schemaVersion = SchemaVersion, styleVersion = StyleVersion,
                        windows = next.Values.OrderBy(x => x.resolutionWidth).ThenBy(x => x.resolutionHeight).ThenBy(x => x.windowType).ToArray()
                    });
                    byte[] bytes = new UTF8Encoding(false).GetBytes(json);
                    if (bytes.Length > MaxBytes) throw new InvalidDataException("Layout exceeds size limit.");
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }
                    // Replace is atomic on the client filesystem; failure preserves the old file.
                    if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
                    else File.Move(temporary, FilePath);
                    entries = next;
                    LastDiagnostic = null;
                    return true;
                }
                catch (Exception error) when (Recoverable(error))
                {
                    LastDiagnostic = "Layout save failed; previous layout retained: " + error.Message;
                    return false;
                }
                finally
                {
                    try { if (File.Exists(temporary)) File.Delete(temporary); }
                    catch (Exception error) when (Recoverable(error)) { LastDiagnostic += " Temporary-file cleanup failed: " + error.Message; }
                }
            }
        }
        private static bool Recoverable(Exception error)
        {
            return error is InvalidDataException || error is IOException || error is UnauthorizedAccessException || error is ArgumentException ||
                error is InvalidOperationException || error is System.Security.SecurityException;
        }
    }
}
