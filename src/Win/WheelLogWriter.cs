using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace KeyBank.Win
{
    // Appends finished log lines to one file and rotates it once it passes the cap. It is only ever called from
    // the engine tick, never from a hook callback. Every IO failure is counted and swallowed.
    internal sealed class WheelLogWriter
    {
        static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        readonly string path;
        readonly long maxBytes;
        readonly Action probe;
        int errors;

        public WheelLogWriter(string path, long maxBytes, Action probe)
        {
            this.path = path;
            this.maxBytes = maxBytes;
            this.probe = probe;
        }

        public static string DefaultPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyBank", "logs", "wheel.log");
        }

        public int Errors
        {
            get { return Volatile.Read(ref errors); }
        }

        public void Append(List<string> lines)
        {
            if (lines.Count == 0)
                return;
            try
            {
                if (probe != null)
                    probe();
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                Rotate();
                StringBuilder sb = new StringBuilder();
                foreach (string line in lines)
                    sb.Append(line).Append("\r\n");
                File.AppendAllText(path, sb.ToString(), Utf8NoBom);
            }
            catch (Exception)
            {
                Interlocked.Increment(ref errors);
            }
        }

        void Rotate()
        {
            FileInfo info = new FileInfo(path);
            if (!info.Exists || info.Length <= maxBytes)
                return;
            string old = path + ".1";
            File.Delete(old);
            File.Move(path, old);
        }
    }
}
