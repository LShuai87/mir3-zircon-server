using System;
using System.IO;
using System.Linq;
using System.Text;
using Client.Envir;

internal static class ItemListStorageTests
{
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "ZirconItemLists-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (string name in new[] { "拾取列表.txt", "显名列表.txt", "高亮列表.txt" })
            {
                string path = Path.Combine(root, name);
                Assert(ItemListStorage.Load(path).Length == 0, "Missing list must load as empty: " + name);
                Assert(!File.Exists(path), "Loading must not create or replace a user file.");
                ItemListStorage.Save(path, new string[0]);
                Assert(File.Exists(path), "Saving must create a missing list: " + name);
                Assert(ItemListStorage.Load(path).Length == 0, "Empty save must round-trip.");
                string[] expected = { "金创药（小）", "太阳水", " dragon sword " };
                File.WriteAllLines(path, expected, Encoding.UTF8);
                byte[] original = File.ReadAllBytes(path);
                Assert(ItemListStorage.Load(path).SequenceEqual(expected), "Existing UTF-8 entries must be preserved.");
                Assert(File.ReadAllBytes(path).SequenceEqual(original), "Reading must not change the user's file.");
                ItemListStorage.Save(path, new[] { "回城卷" });
                Assert(ItemListStorage.Load(path).SequenceEqual(new[] { "回城卷" }), "Saving must replace stale entries.");
                ItemListStorage.Save(path, new string[0]);
                Assert(ItemListStorage.Load(path).Length == 0, "Clearing must remove existing entries.");
                Console.WriteLine("PASS: " + name + " missing, create, UTF-8, preserve, replace and clear");
            }
            string nested = Path.Combine(root, "settings", "items.txt");
            Assert(ItemListStorage.Load(nested).Length == 0, "Missing parent directory must load as empty.");
            ItemListStorage.Save(nested, new[] { "疗伤药" });
            Assert(ItemListStorage.Load(nested).SequenceEqual(new[] { "疗伤药" }), "Saving must create a parent directory.");
            bool errorVisible = false;
            try { ItemListStorage.Load(root); }
            catch (UnauthorizedAccessException) { errorVisible = true; }
            Assert(errorVisible, "Other I/O errors must remain visible.");
            Console.WriteLine("PASS: nested path creation and non-missing I/O errors");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
