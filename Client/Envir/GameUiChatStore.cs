using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace Client.Envir
{
    // Official-only local preferences. Never receives native DB objects or writes Users.db.
    public sealed class GameUiChatStore
    {
        public const string FileName = "GameUiChat.OfficialV1.json";
        private const int MaxBytes = 1048576;
        private readonly object sync = new object();
        private Dictionary<Size, Snapshot> entries = new Dictionary<Size, Snapshot>();
        public string FilePath { get; }
        public string LastDiagnostic { get; private set; }
        public sealed class Document
        {
            public int schemaVersion { get; set; }
            public string styleVersion { get; set; }
            public Snapshot[] resolutions { get; set; }
        }
        public sealed class Snapshot
        {
            public int width { get; set; }
            public int height { get; set; }
            public Group[] groups { get; set; }
        }
        public sealed class Group
        {
            public string id { get; set; }
            public int x { get; set; }
            public int y { get; set; }
            public int width { get; set; }
            public int height { get; set; }
            public bool primary { get; set; }
            public bool floating { get; set; }
            public int selected { get; set; }
            public Tab[] tabs { get; set; }
        }
        public sealed class Tab
        {
            public string name { get; set; }
            // Bits 0..9: native channel filters; 10: transparent; 11: alert.
            public int flags { get; set; }
        }
        private static readonly HashSet<Size> Resolutions = new HashSet<Size>
        {
            new Size(1024,768), new Size(1366,768), new Size(1280,800),
            new Size(1440,900), new Size(1600,900), new Size(1920,1080)
        };
        public GameUiChatStore(string configurationDirectory)
        {
            if (string.IsNullOrWhiteSpace(configurationDirectory)) throw new ArgumentException("Configuration directory is required.", nameof(configurationDirectory));
            FilePath = Path.Combine(Path.GetFullPath(configurationDirectory), FileName);
            Reload();
        }
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static JavaScriptSerializer Serializer() => new JavaScriptSerializer { MaxJsonLength = MaxBytes, RecursionLimit = 20 };
        private static Dictionary<string, object> Fields(object value, params string[] names)
        {
            var result = value as Dictionary<string, object>;
            if (result == null || result.Count != names.Length || names.Any(n => !result.ContainsKey(n)))
                throw new InvalidDataException("Missing or unknown chat preference fields.");
            return result;
        }
        private static int Integer(Dictionary<string, object> fields, string name)
        {
            if (!(fields[name] is int)) throw new InvalidDataException("Chat integer required: " + name);
            return (int)fields[name];
        }
        private static bool Boolean(Dictionary<string, object> fields, string name)
        {
            if (!(fields[name] is bool)) throw new InvalidDataException("Chat boolean required: " + name);
            return (bool)fields[name];
        }
        private static string Text(Dictionary<string, object> fields, string name)
        {
            var value = fields[name] as string;
            if (value == null) throw new InvalidDataException("Chat string required: " + name);
            return value;
        }
        private static object[] Array(Dictionary<string, object> fields, string name, int maximum)
        {
            var result = fields[name] as object[];
            if (result == null || result.Length > maximum) throw new InvalidDataException("Invalid chat list: " + name);
            return result;
        }
        private static Snapshot ReadSnapshot(object value)
        {
            var s = Fields(value, "width", "height", "groups");
            return new Snapshot { width = Integer(s,"width"), height = Integer(s,"height"),
                groups = Array(s,"groups",64).Select(item =>
                {
                    var g = Fields(item,"id","x","y","width","height","primary","floating","selected","tabs");
                    return new Group { id = Text(g,"id"), x = Integer(g,"x"), y = Integer(g,"y"), width = Integer(g,"width"), height = Integer(g,"height"),
                        primary = Boolean(g,"primary"), floating = Boolean(g,"floating"), selected = Integer(g,"selected"),
                        tabs = Array(g,"tabs",256).Select(t => { var f = Fields(t,"name","flags"); return new Tab { name = Text(f,"name"), flags = Integer(f,"flags") }; }).ToArray() };
                }).ToArray() };
        }
        private static void Validate(Snapshot snapshot)
        {
            if (snapshot == null || !Resolutions.Contains(new Size(snapshot.width,snapshot.height)) || snapshot.groups == null || snapshot.groups.Length > 64)
                throw new InvalidDataException("Invalid chat resolution or group list.");
            var ids = new HashSet<string>(StringComparer.Ordinal); int primaryCount = 0, tabCount = 0;
            foreach (var g in snapshot.groups)
            {
                Guid guid;
                if (g == null || !Guid.TryParseExact(g.id,"N",out guid) || g.id != guid.ToString("N") || !ids.Add(g.id) ||
                    g.tabs == null || g.tabs.Length == 0 || g.tabs.Length > 256 || g.selected < -1 || g.selected >= g.tabs.Length ||
                    g.width < 106 || g.height < 81 || g.x < 0 || g.y < 0 || (long)g.x + g.width > snapshot.width || (long)g.y + g.height > snapshot.height ||
                    (!g.primary && !g.floating)) throw new InvalidDataException("Invalid chat group identity, selection or geometry.");
                if (g.primary) primaryCount++;
                if (!g.floating && (g.width != 574 || g.height != 106 || g.x != (snapshot.width - 574) / 2 || g.y != snapshot.height - 138))
                    throw new InvalidDataException("Invalid docked primary chat geometry.");
                foreach (var tab in g.tabs)
                {
                    if (tab == null || tab.name == null || tab.name.Length > 256 || tab.flags < 0 || tab.flags > 4095)
                        throw new InvalidDataException("Invalid chat page preferences.");
                    StrictUtf8.GetByteCount(tab.name);
                }
                tabCount += g.tabs.Length;
            }
            if (primaryCount > 1 || tabCount > 256) throw new InvalidDataException("Invalid primary or total chat page count.");
        }
        private static Snapshot Copy(Snapshot s) => new Snapshot { width = s.width, height = s.height,
            groups = s.groups.Select(g => new Group { id = g.id, x = g.x, y = g.y, width = g.width, height = g.height, primary = g.primary,
                floating = g.floating, selected = g.selected, tabs = g.tabs.Select(t => new Tab { name = t.name, flags = t.flags }).ToArray() }).ToArray() };
        // Bound the read itself and reject UTF-16/autodetected encodings; a UTF-8 BOM is accepted.
        private string ReadDocument()
        {
            using (var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > MaxBytes) throw new InvalidDataException("Chat preferences exceed size limit.");
                var bytes = new byte[(int)stream.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int count = stream.Read(bytes, read, bytes.Length - read);
                    if (count == 0) throw new EndOfStreamException("Chat preferences changed during read.");
                    read += count;
                }
                int skip = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
                return StrictUtf8.GetString(bytes, skip, bytes.Length - skip);
            }
        }
        public void Reload()
        {
            lock (sync)
            {
                try
                {
                    var next = new Dictionary<Size, Snapshot>();
                    if (File.Exists(FilePath))
                    {
                        var root = Fields(Serializer().DeserializeObject(ReadDocument()),"schemaVersion","styleVersion","resolutions");
                        if (Integer(root,"schemaVersion") != 1 || !Equals(root["styleVersion"],"OfficialV1")) throw new InvalidDataException("Unsupported chat schema/style.");
                        foreach (var value in Array(root,"resolutions",6))
                        {
                            var snapshot = ReadSnapshot(value); Validate(snapshot);
                            var key = new Size(snapshot.width,snapshot.height);
                            if (next.ContainsKey(key)) throw new InvalidDataException("Duplicate chat resolution.");
                            next.Add(key,snapshot);
                        }
                    }
                    entries = next; LastDiagnostic = null;
                }
                catch (Exception error) when (Recoverable(error))
                {
                    entries = new Dictionary<Size, Snapshot>();
                    LastDiagnostic = "Chat preferences ignored; native defaults retained: " + error.Message;
                }
            }
        }
        public bool TryGet(Size resolution, out Snapshot snapshot)
        {
            lock (sync)
            {
                Snapshot value;
                snapshot = entries.TryGetValue(resolution,out value) ? Copy(value) : null;
                return snapshot != null;
            }
        }
        public bool TrySave(Snapshot snapshot)
        {
            lock (sync)
            {
                string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    Validate(snapshot);
                    var next = new Dictionary<Size, Snapshot>(entries) { [new Size(snapshot.width,snapshot.height)] = Copy(snapshot) };
                    byte[] bytes = StrictUtf8.GetBytes(Serializer().Serialize(new Document { schemaVersion = 1, styleVersion = "OfficialV1",
                        resolutions = next.Values.OrderBy(s => s.width).ThenBy(s => s.height).ToArray() }));
                    if (bytes.Length > MaxBytes) throw new InvalidDataException("Chat preferences exceed size limit.");
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    using (var stream = new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                    { stream.Write(bytes,0,bytes.Length); stream.Flush(true); }
                    if (File.Exists(FilePath)) File.Replace(temporary,FilePath,null); else File.Move(temporary,FilePath);
                    entries = next; LastDiagnostic = null; return true;
                }
                catch (Exception error) when (Recoverable(error))
                { LastDiagnostic = "Chat save failed; previous preferences retained: " + error.Message; return false; }
                finally
                {
                    try { if (File.Exists(temporary)) File.Delete(temporary); }
                    catch (Exception error) when (Recoverable(error)) { LastDiagnostic += " Temporary cleanup failed: " + error.Message; }
                }
            }
        }
        private static bool Recoverable(Exception error) => error is InvalidDataException || error is IOException || error is UnauthorizedAccessException || error is ArgumentException ||
            error is InvalidOperationException || error is System.Security.SecurityException;
    }
}
