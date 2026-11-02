using System;
using System.IO;

namespace KeyBank.Win
{
    // Pure naming rules for the "new text document" context-menu command.
    static class NewFileName
    {
        public const string Prefix = "文字文件 ";

        public static string Build(DateTime now)
        {
            return Prefix + now.ToString("yyyy-MM-dd_HHmm") + ".txt";
        }

        // Never overwrites: a taken name gets " (2)", " (3)" ... before the extension.
        public static string Unique(string folder, DateTime now, Func<string, bool> exists)
        {
            string name = Build(now);
            string stem = Path.GetFileNameWithoutExtension(name);
            string path = Path.Combine(folder, name);
            for (int n = 2; exists(path); n++)
                path = Path.Combine(folder, stem + " (" + n + ").txt");
            return path;
        }
    }
}
