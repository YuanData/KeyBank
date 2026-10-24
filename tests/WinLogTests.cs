using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using KeyBank;
using KeyBank.Core;
using KeyBank.Win;

static partial class WinTests
{
    const string LogOn = "{\"wheelLog\":true,\"burstGapMs\":100,\"markKey\":\"F9\"}";
    const string LogOff = "{\"wheelLog\":false}";
    const string HeaderText = "# KeyBank wheel log started";

    static string[] ReadLines(string path)
    {
        try
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader r = new StreamReader(fs))
                return r.ReadToEnd().Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        }
        catch (IOException)
        {
            return new string[0];
        }
    }

    static List<string> Bursts(string path)
    {
        List<string> r = new List<string>();
        foreach (string l in ReadLines(path))
            if (!l.StartsWith("#") && !l.Contains(" MARK "))
                r.Add(l);
        return r;
    }

    static List<string> Marks(string path)
    {
        List<string> r = new List<string>();
        foreach (string l in ReadLines(path))
            if (l.Contains(" MARK app="))
                r.Add(l);
        return r;
    }

    static int Headers(string path)
    {
        int n = 0;
        foreach (string l in ReadLines(path))
            if (l.StartsWith(HeaderText))
                n++;
        return n;
    }

    static MouseEvent WheelAt(int delta, long hardwareMs)
    {
        MouseEvent e = MouseEvent.Wheel(delta);
        e.HardwareMs = hardwareMs;
        return e;
    }

    static Engine LogEngine(string dir, string json, string logPath, EngineOptions o)
    {
        string cfg = Path.Combine(dir, "config-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(cfg, json);
        o.WheelLogPath = logPath;
        return new Engine(cfg, false, o);
    }

    static void WheelLogTests()
    {
        string dir = TempDir();
        string oldLog = CrashLog.Path;
        CrashLog.Path = Path.Combine(dir, "crash.log");
        Engine e = null;
        try
        {
            string off = Path.Combine(dir, "off.json");
            File.WriteAllText(off, ConfigJson(false, "p", false));
            e = new Engine(off, false, Options());
            Expect("log: all three off, hook not installed", false, e.MouseHookInstalled);
            e.Mutate(delegate(ConfigFile f) { f.Diagnostics.WheelLog = true; });
            Expect("log: wheelLog alone installs hook", true, WaitFor(delegate { return e.MouseHookInstalled; }, 3000));
            e.Mutate(delegate(ConfigFile f) { f.Diagnostics.WheelLog = false; });
            Expect("log: wheelLog off uninstalls hook", true, WaitFor(delegate { return !e.MouseHookInstalled; }, 3000));
            e.Mutate(delegate(ConfigFile f) { f.Diagnostics.WheelLog = true; });
            Expect("log: wheelLog on again installs hook", true, WaitFor(delegate { return e.MouseHookInstalled; }, 3000));
            e.Mutate(delegate(ConfigFile f) { f.Diagnostics.WheelLog = false; f.AltMouse = true; });
            Expect("log: altMouse applied", true, WaitFor(delegate { return e.Settings.AltMouse && !e.Settings.Diagnostics.WheelLog; }, 3000));
            Pump(300);
            Expect("log: altMouse keeps hook when wheelLog off", true, e.MouseHookInstalled);
            e.Shutdown();
            ShutdownChecks("log hook flip", e);
            e = null;

            File.WriteAllText(off, ConfigJson(false, false, "p", false, LogOn));
            e = new Engine(off, false, Options());
            Expect("log: wheelLog only at startup installs hook", true, e.MouseHookInstalled);
            e.Shutdown();
            e = null;

            string burstLog = Path.Combine(dir, "burst", "wheel.log");
            e = LogEngine(dir, ConfigJson(false, false, "p", false, LogOn), burstLog, Options());
            long hw = 4000000000L;
            for (int i = 0; i < 5; i++)
                Expect("log: wheel passes through", false, e.MouseForTest(WheelAt(-120, hw + i * 20)));
            Expect("log: burst line appears after the quiet gap", true, WaitFor(delegate { return Bursts(burstLog).Count == 1; }, 3000));
            Pump(300);
            List<string> b = Bursts(burstLog);
            Expect("log: exactly one burst line", 1, b.Count);
            if (b.Count == 1)
            {
                Expect("log: dir down", true, b[0].Contains(" dir=down "));
                Expect("log: n=5", true, b[0].Contains(" n=5 "));
                Expect("log: sum", true, b[0].Contains(" sum=-600 "));
                Expect("log: hw span", true, b[0].Contains(" hw=80ms "));
                Expect("log: tailHw", true, b[0].Contains(" tailHw=20,20,20,20 "));
                Expect("log: no reverse", true, b[0].EndsWith(" rev=0"));
                Expect("log: starts with a wall clock", true, Regex.IsMatch(b[0], @"^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3} app="));
            }
            Expect("log: header once", 1, Headers(burstLog));
            string[] all = ReadLines(burstLog);
            Expect("log: header first", true, all.Length > 0 && all[0].StartsWith(HeaderText) && all[0].EndsWith("burstGap=100ms markKey=F9"));

            for (int i = 0; i < 3; i++)
                e.MouseForTest(WheelAt(-120, 5000 + i * 10));
            e.MouseForTest(WheelAt(120, 5040));
            Expect("log: second burst with a reverse", true, WaitFor(delegate { return Bursts(burstLog).Count == 2; }, 3000));
            b = Bursts(burstLog);
            Expect("log: reverse counted", true, b.Count == 2 && b[1].Contains(" dir=mixed ") && b[1].Contains(" rev=1 +120@") && b[1].EndsWith(":pass"));
            Expect("log: header still once", 1, Headers(burstLog));
            Expect("log: no write errors", 0, e.WheelLogErrors);

            Expect("log: F9 down not swallowed", false, e.KeyForTest(0x78, true));
            Expect("log: F9 repeat not swallowed", false, e.KeyForTest(0x78, true));
            Expect("log: F9 repeat again", false, e.KeyForTest(0x78, true));
            Expect("log: F9 up not swallowed", false, e.KeyForTest(0x78, false));
            Expect("log: F9 second press not swallowed", false, e.KeyForTest(0x78, true));
            e.KeyForTest(0x78, false);
            e.KeyForTest('A', true);
            e.KeyForTest('A', false);
            Expect("log: two MARK lines", true, WaitFor(delegate { return Marks(burstLog).Count >= 2; }, 3000));
            Pump(200);
            Expect("log: auto-repeat did not add MARK lines", 2, Marks(burstLog).Count);
            Expect("log: other keys write nothing", 1 + 2 + 2, ReadLines(burstLog).Length);
            Expect("log: MARK format", true, Regex.IsMatch(Marks(burstLog)[0], @"^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3} MARK app="));
            e.Shutdown();
            ShutdownChecks("log burst", e);
            e = null;
            byte[] raw = File.ReadAllBytes(burstLog);
            Expect("log: no BOM", true, raw.Length > 3 && !(raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF));

            string dropLog = Path.Combine(dir, "drop", "wheel.log");
            e = LogEngine(dir, ConfigJson(false, true, "p", false, LogOn), dropLog, Options());
            e.MouseForTest(WheelAt(120, 100));
            Expect("log: filtered reverse is swallowed", true, e.MouseForTest(WheelAt(-30, 110)));
            Expect("log: drop burst line", true, WaitFor(delegate { return Bursts(dropLog).Count == 1; }, 3000));
            b = Bursts(dropLog);
            Expect("log: dropped event marked", true, b.Count == 1 && b[0].Contains(" rev=1 -30@") && b[0].EndsWith(":drop"));
            e.Shutdown();
            e = null;

            string probeLog = Path.Combine(dir, "probe", "wheel.log");
            object gate = new object();
            List<int> writeThreads = new List<int>();
            int writesInCallback = 0;
            EngineOptions po = Options();
            po.WheelLogWriteProbe = delegate
            {
                lock (gate)
                {
                    writeThreads.Add(Thread.CurrentThread.ManagedThreadId);
                    if (Engine.InMouseCallbackNow)
                        writesInCallback++;
                }
            };
            e = LogEngine(dir, ConfigJson(false, false, "p", false, LogOn), probeLog, po);
            for (int i = 0; i < 4; i++)
                e.MouseForTest(WheelAt(-120, 900 + i * 15));
            e.KeyForTest(0x78, true);
            e.KeyForTest(0x78, false);
            Expect("log: burst line written", true, WaitFor(delegate { return Bursts(probeLog).Count == 1; }, 3000));
            bool onHookThread = true;
            int writes;
            lock (gate)
            {
                writes = writeThreads.Count;
                foreach (int id in writeThreads)
                    if (id != e.HookThreadManagedId)
                        onHookThread = false;
            }
            Expect("log: writer was called", true, writes >= 1);
            Expect("log: writes only on the hook thread", true, onHookThread);
            Expect("log: no write inside a mouse callback", 0, writesInCallback);
            e.Shutdown();
            e = null;

            string flushLog = Path.Combine(dir, "flush", "wheel.log");
            e = LogEngine(dir, ConfigJson(false, false, "p", false, LogOn), flushLog, Options());
            for (int i = 0; i < 3; i++)
                e.MouseForTest(WheelAt(-120, 70 + i * 10));
            e.Shutdown();
            b = Bursts(flushLog);
            Expect("log: shutdown flushed the active burst", true, b.Count == 1 && b[0].Contains(" n=3 ") && b[0].Contains(" sum=-360 "));
            e = null;

            string offLog = Path.Combine(dir, "offlog", "wheel.log");
            e = LogEngine(dir, ConfigJson(false, false, "p", false, LogOff), offLog, Options());
            e.KeyForTest(0x78, true);
            e.KeyForTest(0x78, false);
            e.Shutdown();
            e = null;
            Expect("log: wheelLog off writes no file", false, File.Exists(offLog));
        }
        finally
        {
            if (e != null)
                e.Shutdown();
            CrashLog.Path = oldLog;
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }

        WheelLogWriterTests();
    }

    static void WheelLogWriterTests()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "w", "wheel.log");
            string line = new string('x', 60);
            WheelLogWriter w = new WheelLogWriter(path, 100, null);
            List<string> one = new List<string>(new string[] { line });
            w.Append(one);
            w.Append(one);
            Expect("writer: below the cap keeps one file", false, File.Exists(path + ".1"));
            Expect("writer: two lines", 2, ReadLines(path).Length);
            w.Append(one);
            Expect("writer: over the cap rotates", true, File.Exists(path + ".1"));
            Expect("writer: old file keeps its lines", 2, ReadLines(path + ".1").Length);
            Expect("writer: new file starts with one line", 1, ReadLines(path).Length);
            w.Append(one);
            w.Append(new List<string>(new string[] { "second generation" }));
            Expect("writer: second rotation replaces .1", 2, ReadLines(path + ".1").Length);
            Expect("writer: second rotation starts the new file", "second generation", ReadLines(path)[0]);
            Expect("writer: no errors", 0, w.Errors);
            Expect("writer: lines end with CRLF", true, File.ReadAllText(path).EndsWith("\r\n"));
            w.Append(new List<string>());

            string blocker = Path.Combine(dir, "blocker");
            File.WriteAllText(blocker, "file");
            WheelLogWriter bad = new WheelLogWriter(Path.Combine(blocker, "sub", "wheel.log"), 100, null);
            bool threw = false;
            try { bad.Append(one); } catch (Exception) { threw = true; }
            Expect("writer: IO failure never throws", false, threw);
            Expect("writer: IO failure counted", 1, bad.Errors);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
