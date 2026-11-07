using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Client.Envir
{
    internal static class ItemListStorage
    {
        public static string[] Load(string path)
        {
            try
            {
                return File.ReadAllLines(path, Encoding.UTF8);
            }
            catch (FileNotFoundException)
            {
                return new string[0];
            }
            catch (DirectoryNotFoundException)
            {
                return new string[0];
            }
        }

        public static void Save(string path, IEnumerable<string> items)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllLines(path, items, Encoding.UTF8);
        }
    }
}
