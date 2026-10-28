using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using KeyBank.Ui;
using KeyBank.Win;

namespace KeyBank
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string configPath = ValueOf(args, "--config");
            bool trace = Array.IndexOf(args, "--trace") >= 0;
            bool quit = Array.IndexOf(args, "--quit") >= 0;
            string suffix = Hash(configPath ?? "");

            EventWaitHandle showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\KeyBank-show-" + suffix);
            EventWaitHandle quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\KeyBank-quit-" + suffix);

            bool created;
            using (new Mutex(true, @"Local\KeyBank-" + suffix, out created))
            {
                if (!created)
                {
                    // The running copy listens for these, even when its tray icon is unreachable.
                    (quit ? quitEvent : showEvent).Set();
                    return 0;
                }
                if (quit)
                    return 0;

                try
                {
                    Control ui = new Control();
                    ui.CreateControl();
                    IntPtr forceHandle = ui.Handle;

                    using (Engine engine = new Engine(configPath, trace))
                    {
                        TrayContext tray = new TrayContext(engine);
                        StartListener(showEvent, quitEvent, engine, ui, tray);
                        Application.Run(tray);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "KeyBank 無法啟動", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
            }
            return 0;
        }

        // Runs on its own thread so --quit still works when the UI thread is stuck.
        static void StartListener(EventWaitHandle showEvent, EventWaitHandle quitEvent, Engine engine, Control ui, TrayContext tray)
        {
            Thread listener = new Thread(delegate()
            {
                WaitHandle[] handles = new WaitHandle[] { quitEvent, showEvent };
                while (true)
                {
                    int which = WaitHandle.WaitAny(handles);
                    if (which == 0)
                    {
                        engine.Shutdown();
                        Environment.Exit(0);
                    }
                    try
                    {
                        ui.BeginInvoke(new Action(delegate { tray.ShowSettingsWindow(); }));
                    }
                    catch (InvalidOperationException)
                    {
                        return;
                    }
                }
            });
            listener.IsBackground = true;
            listener.Name = "KeyBank.Listener";
            listener.Start();
        }

        static string ValueOf(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static string Hash(string text)
        {
            using (MD5 md5 = MD5.Create())
                return BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(text.ToLowerInvariant()))).Replace("-", "");
        }
    }
}
