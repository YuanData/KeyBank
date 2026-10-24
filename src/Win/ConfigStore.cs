using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using KeyBank.Core;

namespace KeyBank.Win
{
    internal sealed class ConfigStore : IDisposable
    {
        const int DebounceMs = 300;
        const int MaxReadRetries = 5;

        static readonly Encoding Utf8 = new UTF8Encoding(false);

        readonly SynchronizationContext ui;
        readonly FileSystemWatcher watcher;
        readonly System.Threading.Timer debounce;
        string lastSeenText;
        int readRetries;
        int applied;
        bool disposed;

        public readonly string Path;
        volatile Settings settings;
        volatile string lastError;

        // Swapped on the UI thread; the hook thread reads it through Engine.
        public Settings Settings
        {
            get { return settings; }
            private set { settings = value; }
        }

        public string LastError
        {
            get { return lastError; }
            private set { lastError = value; }
        }
        public event EventHandler Changed;

        // The UI-thread context captured at construction; Engine raises its own Changed through it too.
        public SynchronizationContext Ui
        {
            get { return ui; }
        }

        public static string DefaultPath()
        {
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyBank", "config.json");
        }

        public ConfigStore(string pathOrNull)
        {
            Path = System.IO.Path.GetFullPath(string.IsNullOrEmpty(pathOrNull) ? DefaultPath() : pathOrNull);
            ui = SynchronizationContext.Current as WindowsFormsSynchronizationContext ?? new WindowsFormsSynchronizationContext();

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            if (!File.Exists(Path))
                File.WriteAllText(Path, DefaultConfig.Json, Utf8);

            Settings = Settings.Load(DefaultConfig.Json);
            lastSeenText = null;
            Load();

            debounce = new System.Threading.Timer(delegate { ui.Post(delegate { Reload(); }, null); }, null, Timeout.Infinite, Timeout.Infinite);
            watcher = new FileSystemWatcher(System.IO.Path.GetDirectoryName(Path), System.IO.Path.GetFileName(Path));
            watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime;
            FileSystemEventHandler onEvent = delegate { ScheduleReload(); };
            watcher.Changed += onEvent;
            watcher.Created += onEvent;
            watcher.Renamed += delegate { ScheduleReload(); };
            watcher.EnableRaisingEvents = true;
        }

        void ScheduleReload()
        {
            if (!disposed && debounce != null)
                debounce.Change(DebounceMs, Timeout.Infinite);
        }

        // Keeps the last good Settings when the file is unreadable or invalid.
        void Load()
        {
            string text;
            try
            {
                text = File.ReadAllText(Path, Encoding.UTF8);
            }
            catch (FileNotFoundException)
            {
                LastError = "設定檔不存在：" + Path;
                return;
            }
            catch (IOException)
            {
                if (readRetries++ < MaxReadRetries)
                    ScheduleReload();
                return;
            }
            catch (UnauthorizedAccessException)
            {
                if (readRetries++ < MaxReadRetries)
                    ScheduleReload();
                return;
            }

            readRetries = 0;
            if (text == lastSeenText)
                return;
            lastSeenText = text;
            try
            {
                Settings = Settings.Load(text);
                applied++;
                LastError = null;
                if (Settings.Upgraded)
                    Upgrade();
            }
            catch (ConfigException ex)
            {
                LastError = ex.Message;
            }
            catch (Exception ex)
            {
                LastError = "設定檔內容無法套用：" + ex.Message;
            }
        }

        // Rewrites an old-format file in the current layout after keeping a backup. A failure leaves the
        // old file and the already compiled settings in place; lastSeenText stops it from retrying in a loop.
        void Upgrade()
        {
            try
            {
                string backup = Path + ".bak";
                for (int n = 2; File.Exists(backup); n++)
                    backup = Path + ".bak" + n;
                File.Copy(Path, backup);

                string text = Settings.File.ToJson();
                WriteAtomic(text);
                lastSeenText = text;
                Settings = Settings.Load(text);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        void Reload()
        {
            if (disposed)
                return;
            int before = applied;
            string errorBefore = LastError;
            Load();
            if (before != applied || errorBefore != LastError)
                RaiseChanged();
        }

        public void Mutate(Action<ConfigFile> change)
        {
            ConfigFile copy = ConfigFile.Parse(Settings.File.ToJson());
            change(copy);
            Settings next = Settings.Compile(copy);
            string text = copy.ToJson();
            WriteAtomic(text);

            // Remembering our own text lets the watcher event for this write be recognized and skipped.
            lastSeenText = text;
            Settings = next;
            LastError = null;
            RaiseChanged();
        }

        void WriteAtomic(string text)
        {
            string temp = Path + ".tmp";
            File.WriteAllText(temp, text, Utf8);
            if (File.Exists(Path))
                File.Replace(temp, Path, null);
            else
                File.Move(temp, Path);
        }

        void RaiseChanged()
        {
            EventHandler handler = Changed;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            disposed = true;
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
            debounce.Dispose();
        }
    }
}
