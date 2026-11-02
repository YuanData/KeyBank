using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using KeyBank;
using KeyBank.Core;
using KeyBank.Win;

static partial class WinTests
{
    static int failed;
    static int passed;

    static void Expect(string name, object expected, object actual)
    {
        bool ok = Equals(expected, actual);
        if (ok)
        {
            passed++;
            return;
        }
        failed++;
        Console.WriteLine("FAIL " + name + "\n   expected: " + expected + "\n   actual:   " + actual);
    }

    static bool WaitFor(Func<bool> condition, int timeoutMs)
    {
        DateTime end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < end)
        {
            Application.DoEvents();
            if (condition())
                return true;
            Thread.Sleep(20);
        }
        Application.DoEvents();
        return condition();
    }

    static void Pump(int ms)
    {
        WaitFor(delegate { return false; }, ms);
    }

    static int Main(string[] args)
    {
        InjectorTests();
        MouseParseTests();
        ForegroundTests();
        ConfigStoreTests();
        UpgradeTests();
        HookTests();
        WatchdogTests();
        EngineTests();
        MouseQueueTests();
        WheelLogTests();
        NewFileNameTests();
        try { Directory.Delete(SharedLogDir, true); } catch (IOException) { }
        Console.WriteLine("passed " + passed + ", failed " + failed);
        return failed == 0 ? 0 : 1;
    }

    static void NewFileNameTests()
    {
        DateTime t = new DateTime(2026, 11, 2, 17, 9, 0);
        Expect("new file name", "文字文件 2026-11-02_1709.txt", NewFileName.Build(t));
        string dir = @"C:\x";
        Expect("free name", dir + @"\文字文件 2026-11-02_1709.txt", NewFileName.Unique(dir, t, delegate { return false; }));
        int calls = 0;
        string second = NewFileName.Unique(dir, t, delegate { return calls++ < 1; });
        Expect("taken name gets (2)", dir + @"\文字文件 2026-11-02_1709 (2).txt", second);
        calls = 0;
        string third = NewFileName.Unique(dir, t, delegate { return calls++ < 2; });
        Expect("taken twice gets (3)", dir + @"\文字文件 2026-11-02_1709 (3).txt", third);
    }

    static void InjectorTests()
    {
        List<Out> outs = new List<Out>();
        outs.Add(Out.Key(Vk.LCtrl, true));
        outs.Add(Out.Key(Vk.PageDown, true));
        outs.Add(Out.Key(Vk.PageDown, false));
        outs.Add(Out.Scroll(-1, 4));
        outs.Add(Out.Key('C', false));
        INPUT[] inputs = Injector.Build(outs);

        Expect("batch size", 5, inputs.Length);
        Expect("struct size is x64 INPUT", IntPtr.Size == 8 ? 40 : 28, System.Runtime.InteropServices.Marshal.SizeOf(typeof(INPUT)));

        Expect("ctrl type", Native.INPUT_KEYBOARD, inputs[0].type);
        Expect("ctrl vk", (ushort)Vk.LCtrl, inputs[0].u.ki.wVk);
        Expect("ctrl flags", 0u, inputs[0].u.ki.dwFlags);
        Expect("ctrl scan", (ushort)Native.MapVirtualKey(Vk.LCtrl, 0), inputs[0].u.ki.wScan);
        Expect("marker", (IntPtr)0x4B42414E, inputs[0].u.ki.dwExtraInfo);

        Expect("pgdn down extended", Native.KEYEVENTF_EXTENDEDKEY, inputs[1].u.ki.dwFlags);
        Expect("pgdn scan", (ushort)Native.MapVirtualKey(Vk.PageDown, 0), inputs[1].u.ki.wScan);
        Expect("pgdn up extended+up", Native.KEYEVENTF_EXTENDEDKEY | Native.KEYEVENTF_KEYUP, inputs[2].u.ki.dwFlags);

        Expect("scroll type", Native.INPUT_MOUSE, inputs[3].type);
        Expect("scroll flag", Native.MOUSEEVENTF_WHEEL, inputs[3].u.mi.dwFlags);
        Expect("scroll down 4 ticks", -480, inputs[3].u.mi.mouseData);
        Expect("scroll marker", (IntPtr)0x4B42414E, inputs[3].u.mi.dwExtraInfo);

        Expect("letter up", Native.KEYEVENTF_KEYUP, inputs[4].u.ki.dwFlags);
        Expect("order preserved", (ushort)Vk.PageDown, inputs[2].u.ki.wVk);

        Expect("wheel 4 ticks up", 480, Injector.WheelDelta(Out.Scroll(1, 4)));
        Expect("wheel 0.2 ticks", 24, Injector.WheelDelta(Out.Scroll(1, 0.2)));
        Expect("wheel 2.5 ticks", 300, Injector.WheelDelta(Out.Scroll(1, 2.5)));
        Expect("wheel minimum", 1, Injector.WheelDelta(Out.Scroll(1, 0.0001)));
        Expect("wheel minimum down", -1, Injector.WheelDelta(Out.Scroll(-1, 0.0001)));
        Expect("empty batch", 0, Injector.Build(new List<Out>()).Length);

        MouseButton[] buttons = { MouseButton.Left, MouseButton.Right, MouseButton.Middle, MouseButton.X1, MouseButton.X2 };
        uint[] downFlags = { Native.MOUSEEVENTF_LEFTDOWN, Native.MOUSEEVENTF_RIGHTDOWN, Native.MOUSEEVENTF_MIDDLEDOWN, Native.MOUSEEVENTF_XDOWN, Native.MOUSEEVENTF_XDOWN };
        uint[] upFlags = { Native.MOUSEEVENTF_LEFTUP, Native.MOUSEEVENTF_RIGHTUP, Native.MOUSEEVENTF_MIDDLEUP, Native.MOUSEEVENTF_XUP, Native.MOUSEEVENTF_XUP };
        int[] xData = { 0, 0, 0, 1, 2 };
        for (int i = 0; i < buttons.Length; i++)
        {
            INPUT d = Injector.Build(new List<Out>(new Out[] { Out.Mouse(buttons[i], true) }))[0];
            INPUT u = Injector.Build(new List<Out>(new Out[] { Out.Mouse(buttons[i], false) }))[0];
            Expect(buttons[i] + " down type", Native.INPUT_MOUSE, d.type);
            Expect(buttons[i] + " down flags", downFlags[i], d.u.mi.dwFlags);
            Expect(buttons[i] + " up flags", upFlags[i], u.u.mi.dwFlags);
            Expect(buttons[i] + " mouseData", xData[i], d.u.mi.mouseData);
            Expect(buttons[i] + " up mouseData", xData[i], u.u.mi.mouseData);
            Expect(buttons[i] + " marker", (IntPtr)Native.Marker, d.u.mi.dwExtraInfo);
            Expect(buttons[i] + " up marker", (IntPtr)Native.Marker, u.u.mi.dwExtraInfo);
            Expect(buttons[i] + " no coordinates", 0, d.u.mi.dx + d.u.mi.dy);
        }

        INPUT wheel = Injector.Build(new List<Out>(new Out[] { Out.Wheel(-240) }))[0];
        Expect("raw wheel type", Native.INPUT_MOUSE, wheel.type);
        Expect("raw wheel flag", Native.MOUSEEVENTF_WHEEL, wheel.u.mi.dwFlags);
        Expect("raw wheel delta", -240, wheel.u.mi.mouseData);
        Expect("raw wheel marker", (IntPtr)Native.Marker, wheel.u.mi.dwExtraInfo);
    }

    static byte[] MouseStruct(int mouseData, int flags, long extra)
    {
        return MouseStruct(mouseData, flags, 0u, extra);
    }

    static byte[] MouseStruct(int mouseData, int flags, uint time, long extra)
    {
        byte[] s = new byte[IntPtr.Size == 8 ? 32 : 24];
        BitConverter.GetBytes(mouseData).CopyTo(s, 8);
        BitConverter.GetBytes(flags).CopyTo(s, 12);
        BitConverter.GetBytes(time).CopyTo(s, 16);
        byte[] e = IntPtr.Size == 8 ? BitConverter.GetBytes(extra) : BitConverter.GetBytes((int)extra);
        e.CopyTo(s, IntPtr.Size == 8 ? 24 : 20);
        return s;
    }

    static string Parsed(int msg, int mouseData, long extra)
    {
        return Parsed(msg, mouseData, 0, extra);
    }

    static string Parsed(int msg, int mouseData, int flags, long extra)
    {
        MouseEvent e;
        if (!MouseHook.ParseStruct(msg, MouseStruct(mouseData, flags, extra), out e))
            return "ignored";
        string text = e.Kind == MouseKind.Wheel ? "wheel " + e.Delta : e.Button + (e.Down ? " down" : " up");
        return e.Injected ? text + " injected" : text;
    }

    static void MouseParseTests()
    {
        Expect("left down", "Left down", Parsed(Native.WM_LBUTTONDOWN, 0, 0));
        Expect("left up", "Left up", Parsed(Native.WM_LBUTTONUP, 0, 0));
        Expect("right down", "Right down", Parsed(Native.WM_RBUTTONDOWN, 0, 0));
        Expect("right up", "Right up", Parsed(Native.WM_RBUTTONUP, 0, 0));
        Expect("middle down", "Middle down", Parsed(Native.WM_MBUTTONDOWN, 0, 0));
        Expect("middle up", "Middle up", Parsed(Native.WM_MBUTTONUP, 0, 0));
        Expect("x1 down", "X1 down", Parsed(Native.WM_XBUTTONDOWN, 1 << 16, 0));
        Expect("x1 up", "X1 up", Parsed(Native.WM_XBUTTONUP, 1 << 16, 0));
        Expect("x2 down", "X2 down", Parsed(Native.WM_XBUTTONDOWN, 2 << 16, 0));
        Expect("x2 up", "X2 up", Parsed(Native.WM_XBUTTONUP, 2 << 16, 0));
        Expect("x unknown button", "ignored", Parsed(Native.WM_XBUTTONDOWN, 3 << 16, 0));
        Expect("x ignores low word", "X2 down", Parsed(Native.WM_XBUTTONDOWN, (2 << 16) | 0x1234, 0));
        Expect("wheel up", "wheel 120", Parsed(Native.WM_MOUSEWHEEL, 120 << 16, 0));
        Expect("wheel down", "wheel -120", Parsed(Native.WM_MOUSEWHEEL, unchecked((int)((uint)(ushort)(short)-120 << 16)), 0));
        Expect("wheel big down", "wheel -480", Parsed(Native.WM_MOUSEWHEEL, unchecked((int)((uint)(ushort)(short)-480 << 16)), 0));
        Expect("wheel zero ignored", "ignored", Parsed(Native.WM_MOUSEWHEEL, 0, 0));
        Expect("move ignored", "ignored", Parsed(0x0200, 0, 0));
        Expect("double click msg ignored", "ignored", Parsed(0x0203, 0, 0));
        Expect("own marker left ignored", "ignored", Parsed(Native.WM_LBUTTONDOWN, 0, Native.Marker));
        Expect("own marker wheel ignored", "ignored", Parsed(Native.WM_MOUSEWHEEL, 120 << 16, Native.Marker));
        Expect("other extra info passes", "Left down", Parsed(Native.WM_LBUTTONDOWN, 0, 0x1234));
        Expect("wheel flags 0 not injected", "wheel 120", Parsed(Native.WM_MOUSEWHEEL, 120 << 16, 0, 0));
        Expect("wheel flags 1 injected", "wheel 120 injected", Parsed(Native.WM_MOUSEWHEEL, 120 << 16, 1, 0));
        Expect("wheel other flag bits not injected", "wheel 120", Parsed(Native.WM_MOUSEWHEEL, 120 << 16, 0x2, 0));
        Expect("wheel injected plus other bits", "wheel 120 injected", Parsed(Native.WM_MOUSEWHEEL, 120 << 16, 0x3, 0));
        Expect("button flags 0 not injected", "Left down", Parsed(Native.WM_LBUTTONDOWN, 0, 0, 0));
        Expect("button flags 1 injected", "Left down injected", Parsed(Native.WM_LBUTTONDOWN, 0, 1, 0));
        Expect("x button flags 1 injected", "X1 up injected", Parsed(Native.WM_XBUTTONUP, 1 << 16, 1, 0));
        Expect("marker wins over injected flag", "ignored", Parsed(Native.WM_MOUSEWHEEL, 120 << 16, 1, Native.Marker));
        Expect("move with injected flag ignored", "ignored", Parsed(0x0200, 0, 1, 0));

        MouseEvent t;
        Expect("time parsed", true, MouseHook.ParseStruct(Native.WM_MOUSEWHEEL, MouseStruct(120 << 16, 0, 123456u, 0), out t));
        Expect("hardware ms from offset 16", 123456L, t.HardwareMs);
        MouseHook.ParseStruct(Native.WM_MOUSEWHEEL, MouseStruct(120 << 16, 0, 3000000000u, 0), out t);
        Expect("hardware ms above int.MaxValue stays positive", 3000000000L, t.HardwareMs);
        MouseHook.ParseStruct(Native.WM_MOUSEWHEEL, MouseStruct(120 << 16, 0, uint.MaxValue, 0), out t);
        Expect("hardware ms uint max", 4294967295L, t.HardwareMs);
        MouseHook.ParseStruct(Native.WM_LBUTTONDOWN, MouseStruct(0, 1, 777u, 0), out t);
        Expect("hardware ms on a button event", 777L, t.HardwareMs);
        Expect("time does not disturb delta and flags", "wheel -120 injected", Parsed(Native.WM_MOUSEWHEEL, unchecked((int)((uint)(ushort)(short)-120 << 16)), 1, 0));
    }

    static void ForegroundTests()
    {
        IntPtr current = IntPtr.Zero;
        int lookups = 0;
        ForegroundApp app = new ForegroundApp(
            delegate { return current; },
            delegate(IntPtr h) { lookups++; return h == (IntPtr)2 ? "" : h == (IntPtr)3 ? null : "Chrome.exe"; });

        Expect("no window is null", null, app.CurrentExeName());
        Expect("no window no lookup", 0, lookups);

        current = (IntPtr)1;
        Expect("resolves name", "Chrome.exe", app.CurrentExeName());
        Expect("cached second call", "Chrome.exe", app.CurrentExeName());
        Expect("one lookup per hwnd", 1, lookups);

        current = (IntPtr)2;
        Expect("unreadable is null", null, app.CurrentExeName());
        Expect("unreadable cached too", null, app.CurrentExeName());
        current = (IntPtr)3;
        Expect("null resolver result is null", null, app.CurrentExeName());
        Expect("lookups so far", 3, lookups);

        current = (IntPtr)1;
        Expect("back to first still cached", "Chrome.exe", app.CurrentExeName());
        Expect("no extra lookup", 3, lookups);

        HashSet<string> excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        excluded.Add("chrome");
        Expect("excluded by bare name", true, app.IsExcluded(excluded));
        current = (IntPtr)2;
        Expect("unknown never excluded", false, app.IsExcluded(excluded));

        app.Reset();
        current = (IntPtr)1;
        app.CurrentExeName();
        Expect("reset forces lookup", 4, lookups);

        for (int i = 10; i < 400; i++)
        {
            current = (IntPtr)i;
            app.CurrentExeName();
        }
        Expect("cache stays bounded and works", "Chrome.exe", app.CurrentExeName());
    }

    static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "KeyBankWinTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    static void ConfigStoreTests()
    {
        string dir = TempDir();
        string path = Path.Combine(dir, "sub", "config.json");
        try
        {
            ConfigStore store = new ConfigStore(path);
            int changes = 0;
            store.Changed += delegate { changes++; };

            Expect("default created", true, File.Exists(path));
            Expect("default parses", false, store.Settings.NumpadSixKeys);
            Expect("default no error", null, store.LastError);

            string edited = File.ReadAllText(path).Replace("\"enabled\": true,\n  \"numpadSixKeys\"", "\"enabled\": false,\n  \"numpadSixKeys\"")
                .Replace("\"enabled\": true,\r\n  \"numpadSixKeys\"", "\"enabled\": false,\r\n  \"numpadSixKeys\"");
            File.WriteAllText(path, edited);
            Expect("reload on edit", true, WaitFor(delegate { return !store.Settings.Enabled; }, 3000));
            Expect("reload raised Changed", true, changes >= 1);

            int before = changes;
            File.WriteAllText(path, "{ this is not json");
            Expect("broken json sets LastError", true, WaitFor(delegate { return store.LastError != null; }, 3000));
            Expect("broken json keeps last good", false, store.Settings.Enabled);
            Expect("broken json raised Changed", true, changes > before);

            File.WriteAllText(path, DefaultConfig.Json);
            Expect("fixed json clears error", true, WaitFor(delegate { return store.LastError == null && store.Settings.Enabled; }, 3000));

            before = changes;
            store.Mutate(delegate(ConfigFile f) { f.Enabled = false; f.Scroll.StepTicks = 2; });
            Expect("mutate applied", 2.0, store.Settings.Scroll.StepTicks);
            Expect("mutate raised once", before + 1, changes);
            Expect("mutate saved", 2.0, Settings.Load(File.ReadAllText(path)).Scroll.StepTicks);
            Expect("mutate saved enabled", false, Settings.Load(File.ReadAllText(path)).Enabled);
            Expect("mutate left no temp", false, File.Exists(path + ".tmp"));
            Pump(900);
            Expect("mutate caused no reload loop", before + 1, changes);

            string text = File.ReadAllText(path);
            before = changes;
            bool threw = false;
            try
            {
                store.Mutate(delegate(ConfigFile f) { f.AltChords = new List<string>(new string[] { "Nope" }); });
            }
            catch (ConfigException)
            {
                threw = true;
            }
            Expect("invalid mutate throws", true, threw);
            Expect("invalid mutate leaves file", text, File.ReadAllText(path));
            Expect("invalid mutate leaves settings", 7, store.Settings.Keys.AltChords.Count);
            Pump(600);
            Expect("invalid mutate raised nothing", before, changes);

            store.Dispose();

            ConfigStore broken = null;
            File.WriteAllText(path, "nonsense");
            broken = new ConfigStore(path);
            Expect("broken at startup falls back to default", 7, broken.Settings.Keys.AltChords.Count);
            Expect("broken at startup reports", true, broken.LastError != null);
            broken.Dispose();
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    const string OldFormat = "{\"enabled\":true,\"activeProfile\":\"auto\",\"defaultProfile\":\"p\","
        + "\"halfPage\":{\"enabled\":true,\"fraction\":\"1/4\",\"pageTicks\":8},"
        + "\"excludeApps\":[\"keepme.exe\"],\"devices\":[],"
        + "\"profiles\":{\"p\":{\"description\":\"old\",\"altChords\":[\"C\"],\"remap\":[],\"tapHold\":[]}}}";

    static void UpgradeTests()
    {
        string dir = TempDir();
        string path = Path.Combine(dir, "config.json");
        try
        {
            File.WriteAllText(path, OldFormat);
            ConfigStore store = new ConfigStore(path);
            Expect("startup upgrade backup", OldFormat, File.Exists(path + ".bak") ? File.ReadAllText(path + ".bak") : null);
            string upgraded = File.ReadAllText(path);
            Expect("startup upgrade has scroll", true, upgraded.Contains("\"scroll\""));
            Expect("startup upgrade has appAltChords", true, upgraded.Contains("\"appAltChords\""));
            Expect("startup upgrade drops halfPage", false, upgraded.Contains("halfPage"));
            Expect("startup upgrade settings not flagged", false, store.Settings.Upgraded);
            Expect("startup upgrade keeps step from fraction", 2.0, store.Settings.Scroll.StepTicks);
            Expect("startup upgrade keeps page ticks", 8.0, store.Settings.Scroll.PageTicks);
            Expect("startup upgrade keeps exclude", true, store.Settings.ExcludeApps.Contains("keepme.exe"));
            Expect("startup upgrade no temp", false, File.Exists(path + ".tmp"));

            int changes = 0;
            store.Changed += delegate { changes++; };
            Pump(900);
            Expect("startup upgrade no reload loop", 0, changes);
            Expect("startup upgrade file stable", upgraded, File.ReadAllText(path));

            File.WriteAllText(path, OldFormat);
            Expect("reload upgrade rewrites", true, WaitFor(delegate { return File.Exists(path + ".bak2") && changes >= 1; }, 3000));
            Pump(900);
            Expect("reload upgrade Changed once", 1, changes);
            Expect("reload upgrade kept first backup", OldFormat, File.ReadAllText(path + ".bak"));
            Expect("reload upgrade second backup", OldFormat, File.ReadAllText(path + ".bak2"));
            Expect("reload upgrade file", upgraded, File.ReadAllText(path));
            Expect("reload upgrade applied", false, store.Settings.Upgraded);

            File.WriteAllText(path, OldFormat);
            Expect("third upgrade", true, WaitFor(delegate { return File.Exists(path + ".bak3"); }, 3000));
            Pump(900);
            Expect("third upgrade Changed", 2, changes);

            string stable = File.ReadAllText(path);
            Pump(900);
            Expect("already new format untouched", stable, File.ReadAllText(path));
            Expect("no bak4 for new format", false, File.Exists(path + ".bak4"));
            store.Dispose();

            ConfigStore again = new ConfigStore(path);
            Expect("second load stable", stable, File.ReadAllText(path));
            Expect("second load no new backup", false, File.Exists(path + ".bak4"));
            Expect("second load not flagged", false, again.Settings.Upgraded);
            again.Dispose();
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    // Pass-through only: the config handles no key, so even a real keypress during the run is never changed.
    static void HookTests()
    {
        int moves = 0;
        MouseHook mouse = new MouseHook(delegate(MouseEvent e) { moves++; return false; });
        bool mouseInstalled = mouse.Install();
        Expect("mouse hook installs", true, mouseInstalled);
        Pump(400);
        bool mouseUninstalled = mouse.Uninstall();
        Expect("mouse hook uninstalls", true, mouseUninstalled);
        Expect("mouse hook not installed afterwards", false, mouse.Installed);
        Expect("mouse hook no fault", null, mouse.Fault);
        Console.WriteLine("mouse hook: Install=" + mouseInstalled + " Uninstall=" + mouseUninstalled + " events seen=" + moves);

        int seen = 0;
        KeyboardHook hook = new KeyboardHook(delegate(int vk, bool down) { seen++; return false; });
        bool installed = hook.Install();
        Expect("hook installs", true, installed);
        Pump(400);
        bool uninstalled = hook.Uninstall();
        Expect("hook uninstalls", true, uninstalled);
        Expect("hook not installed afterwards", false, hook.Installed);
        Expect("hook no fault", null, hook.Fault);
        Console.WriteLine("hook: Install=" + installed + " Uninstall=" + uninstalled + " keys seen=" + seen);
    }

    static string ConfigJson(bool altMouse, bool remapF12)
    {
        return ConfigJson(altMouse, false, remapF12);
    }

    static string ConfigJson(bool altMouse, bool wheelFilter, bool remapF12)
    {
        return ConfigJson(altMouse, wheelFilter, remapF12, "{\"wheelLog\":false}");
    }

    // remapF12 turns numpadSixKeys on with a single F12 to F11 rule, so one key proves the rules reached the hook.
    static string ConfigJson(bool altMouse, bool wheelFilter, bool remapF12, string diagnostics)
    {
        string remap = remapF12 ? "{\"from\":\"F12\",\"to\":\"F11\"}" : "";
        return "{\"enabled\":true,\"numpadSixKeys\":" + (remapF12 ? "true" : "false") + ",\"altChords\":[],\"numpadRemap\":[" + remap + "],\"tapHold\":[],"
            + "\"scroll\":{\"enabled\":false,\"stepTicks\":4,\"pageTicks\":8,\"burstTaps\":3,\"burstIntervalMs\":400,\"holdRepeatsPerSec\":8,\"apps\":{}},"
            + "\"altMouse\":" + (altMouse ? "true" : "false") + ",\"wheelFilter\":{\"enabled\":" + (wheelFilter ? "true" : "false") + "},\"diagnostics\":" + diagnostics + ",\"excludeApps\":[],\"appAltChords\":{}}";
    }

    static int sends;

    static readonly string SharedLogDir = Path.Combine(Path.GetTempPath(), "KeyBankWinTestsLog-" + Guid.NewGuid().ToString("N"));

    static EngineOptions Options()
    {
        EngineOptions o = new EngineOptions();
        o.WheelLogPath = Path.Combine(SharedLogDir, "wheel.log");
        o.Send = delegate(List<Out> outs) { Interlocked.Add(ref sends, outs.Count); return outs.Count; };
        return o;
    }

    static void WatchdogTests()
    {
        long now = 0;
        long last = 0;
        int fired = 0;
        Watchdog w = new Watchdog(delegate { return now; }, delegate { return last; }, 1500, delegate { fired++; });

        now = 1000;
        Expect("healthy", false, w.Check());
        now = 1500;
        Expect("exactly the limit is healthy", false, w.Check());
        Expect("healthy fired nothing", 0, fired);
        now = 1501;
        Expect("one ms over stalls", true, w.Check());
        Expect("stall fired once", 1, fired);
        now = 9000;
        Expect("still stalled does not refire", false, w.Check());
        Expect("fired exactly once", 1, fired);
        last = 9000;
        Expect("recovered", false, w.Check());
        now = 10500;
        Expect("recovered boundary healthy", false, w.Check());
        now = 10501;
        Expect("second stall detected", true, w.Check());
        Expect("fired twice", 2, fired);
    }

    static string WheelTrace(string dir, string path)
    {
        File.WriteAllText(path, ConfigJson(false, true, false));
        Engine traced = new Engine(path, true, Options());
        traced.MouseForTest(MouseEvent.Wheel(120));
        traced.MouseForTest(MouseEvent.Wheel(-30));
        traced.Shutdown();
        return File.ReadAllText(Path.Combine(Path.GetTempPath(), "KeyBank.trace.log"));
    }

    static void WheelFilterEngineTests(string path)
    {
        File.WriteAllText(path, ConfigJson(false, true, false));
        Engine e = new Engine(path, false, Options());
        try
        {
            Expect("wheelFilter only: mouse hook installed at startup", true, e.MouseHookInstalled);
            int before = sends;
            MouseEvent up = MouseEvent.Wheel(120);
            MouseEvent small = MouseEvent.Wheel(-30);
            Expect("first wheel event passes", false, e.MouseForTest(up));
            Expect("small reverse wheel event dropped", true, e.MouseForTest(small));
            Expect("next primary wheel event passes", false, e.MouseForTest(up));
            small.Injected = true;
            Expect("injected reverse wheel event never dropped", false, e.MouseForTest(small));
            Expect("button event passes", false, e.MouseForTest(MouseEvent.Click(MouseButton.Left, true)));
            Expect("wheel filter path injected nothing", before, sends);

            e.Mutate(delegate(ConfigFile f) { f.WheelFilter.Enabled = false; });
            Expect("wheelFilter off uninstalls mouse hook", true, WaitFor(delegate { return !e.MouseHookInstalled; }, 3000));
            e.Mutate(delegate(ConfigFile f) { f.WheelFilter.Enabled = true; });
            Expect("wheelFilter on installs mouse hook", true, WaitFor(delegate { return e.MouseHookInstalled; }, 3000));
            e.Mutate(delegate(ConfigFile f) { f.AltMouse = true; f.WheelFilter.Enabled = false; });
            Expect("altMouse alone keeps mouse hook", true, WaitFor(delegate { return e.Settings.AltMouse && !e.Settings.WheelFilter.Enabled; }, 3000));
            Pump(300);
            Expect("altMouse alone: hook installed", true, e.MouseHookInstalled);
            Expect("wheelFilter off passes reverse wheel", false, e.MouseForTest(MouseEvent.Wheel(120)) || e.MouseForTest(MouseEvent.Wheel(-30)));
            e.Mutate(delegate(ConfigFile f) { f.AltMouse = false; });
            Expect("both off uninstalls mouse hook", true, WaitFor(delegate { return !e.MouseHookInstalled; }, 3000));
            Expect("wheel filter flips injected nothing", before, sends);
        }
        finally
        {
            e.Shutdown();
        }
        ShutdownChecks("wheelFilter", e);

        File.WriteAllText(path, ConfigJson(false, false, false));
        Engine none = new Engine(path, false, Options());
        try
        {
            Expect("both off at startup: mouse hook not installed", false, none.MouseHookInstalled);
            Expect("filter off passes wheel", false, none.MouseForTest(MouseEvent.Wheel(120)));
            Expect("filter off passes reverse wheel", false, none.MouseForTest(MouseEvent.Wheel(-30)));
        }
        finally
        {
            none.Shutdown();
        }
        ShutdownChecks("no mouse hook", none);

        string trace = WheelTrace(Path.GetDirectoryName(path), path);
        Expect("trace marks dropped wheel", true, trace.Contains("wheelfilter"));
        Expect("trace marks only the dropped wheel", 1, trace.Split(new string[] { "wheelfilter" }, StringSplitOptions.None).Length - 1);
    }

    static void ShutdownChecks(string name, Engine engine)
    {
        Expect(name + " hook thread ended", false, engine.HookThreadAlive);
        Expect(name + " watchdog thread ended", false, engine.WatchdogThreadAlive);
        Expect(name + " keyboard hook gone", false, engine.KeyboardHookInstalled);
        Expect(name + " mouse hook gone", false, engine.MouseHookInstalled);
    }

    // No key or mouse input is injected anywhere: every Engine here gets a fake sink, and the only swallowed
    // key is F12 on a config built for it.
    static void EngineTests()
    {
        string dir = TempDir();
        string oldLog = CrashLog.Path;
        CrashLog.Path = Path.Combine(dir, "crash.log");
        Engine engine = null;
        try
        {
            string path = Path.Combine(dir, "config.json");
            File.WriteAllText(path, ConfigJson(false, false));
            int mainId = Thread.CurrentThread.ManagedThreadId;

            for (int cycle = 1; cycle <= 2; cycle++)
            {
                string c = "cycle " + cycle;
                engine = new Engine(path, false, Options());
                Expect(c + " hook thread alive", true, engine.HookThreadAlive);
                Expect(c + " keyboard hook installed", true, engine.KeyboardHookInstalled);
                Expect(c + " prewarm ran", true, engine.Prewarmed);
                Expect(c + " mouse hook not installed by default", false, engine.MouseHookInstalled);
                Expect(c + " hook thread is not the UI thread", true, engine.HookThreadManagedId != mainId);

                Stopwatch sw = Stopwatch.StartNew();
                bool swallowed = engine.KeyForTest('A', true);
                double firstMs = sw.Elapsed.TotalMilliseconds;
                engine.KeyForTest('A', false);
                Expect(c + " neutral config passes keys", false, swallowed);
                Expect(c + " last key text", true, engine.LastKeyText.StartsWith("A "));
                Console.WriteLine(c + ": prewarm " + engine.PrewarmMs + " ms, first key round trip " + firstMs.ToString("F2") + " ms");

                engine.Shutdown();
                ShutdownChecks(c, engine);
                engine.Shutdown();
                engine.Dispose();
                engine = null;
            }
            Expect("no input was injected by the tests", 0, sends);

            engine = new Engine(path, false, Options());
            Engine other = engine;
            Thread t = new Thread(delegate() { other.Shutdown(); });
            t.Start();
            Expect("shutdown from another thread returns", true, t.Join(5000));
            ShutdownChecks("other thread", engine);
            engine = null;

            engine = new Engine(path, false, Options());
            object gate = new object();
            bool done = false;
            long elapsedMs = -1;
            Engine blocked = engine;
            Thread worker = new Thread(delegate()
            {
                Thread.Sleep(200);
                Stopwatch timer = Stopwatch.StartNew();
                blocked.Shutdown();
                elapsedMs = timer.ElapsedMilliseconds;
                lock (gate)
                {
                    done = true;
                    Monitor.PulseAll(gate);
                }
            });
            worker.Start();
            lock (gate)
            {
                DateTime end = DateTime.UtcNow.AddSeconds(8);
                while (!done && DateTime.UtcNow < end)
                    Monitor.Wait(gate, 500);
            }
            Expect("shutdown finished while the UI thread was blocked", true, done);
            Expect("shutdown while UI blocked is quick", true, elapsedMs >= 0 && elapsedMs < 2500);
            ShutdownChecks("ui blocked", engine);
            engine = null;

            engine = new Engine(path, false, Options());
            Engine flip = engine;
            Pump(300);
            Expect("altMouse false: mouse hook never installed", false, flip.MouseHookInstalled);
            flip.Mutate(delegate(ConfigFile f) { f.Scroll.StepTicks = 3; });
            Pump(300);
            Expect("unrelated change keeps mouse hook off", false, flip.MouseHookInstalled);
            flip.Mutate(delegate(ConfigFile f) { f.AltMouse = true; });
            Expect("altMouse true installs mouse hook", true, WaitFor(delegate { return flip.MouseHookInstalled; }, 3000));
            flip.Mutate(delegate(ConfigFile f) { f.AltMouse = false; });
            Expect("altMouse false uninstalls mouse hook", true, WaitFor(delegate { return !flip.MouseHookInstalled; }, 3000));
            flip.Shutdown();
            ShutdownChecks("altMouse flip", flip);
            engine = null;

            WheelFilterEngineTests(path);

            File.WriteAllText(path, ConfigJson(true, false));
            engine = new Engine(path, false, Options());
            Expect("altMouse true at startup installs mouse hook", true, engine.MouseHookInstalled);
            engine.Shutdown();
            ShutdownChecks("altMouse startup", engine);
            engine = null;

            File.WriteAllText(path, ConfigJson(false, false));
            engine = new Engine(path, false, Options());
            Engine swap = engine;
            int changedThread = -1;
            int changedCount = 0;
            swap.Changed += delegate { changedThread = Thread.CurrentThread.ManagedThreadId; changedCount++; };
            swap.Mutate(delegate(ConfigFile f) { f.NumpadSixKeys = true; });
            Expect("settings swap reaches hook thread", true, WaitFor(delegate { return swap.AppliedSettings.NumpadSixKeys; }, 3000));
            Expect("Changed raised after swap", true, WaitFor(delegate { return changedCount >= 1; }, 3000));
            Expect("Changed arrives on the captured thread", mainId, changedThread);
            Expect("engine Settings reflects swap", true, swap.Settings.NumpadSixKeys);
            swap.Shutdown();
            ShutdownChecks("settings swap", swap);
            engine = null;

            File.WriteAllText(path, ConfigJson(false, true));
            EngineOptions slow = Options();
            slow.SlowCallbackMs = 40;
            slow.Send = delegate(List<Out> outs) { Thread.Sleep(120); Interlocked.Add(ref sends, outs.Count); return outs.Count; };
            engine = new Engine(path, false, slow);
            int before = sends;
            Expect("F12 remap swallowed", true, engine.KeyForTest(0x7B, true));
            Expect("remap went to the fake sink only", before + 1, sends);
            Pump(100);
            string log = File.Exists(CrashLog.Path) ? File.ReadAllText(CrashLog.Path) : "";
            Expect("slow callback logged", true, log.Contains("slow key callback"));
            engine.Shutdown();
            ShutdownChecks("slow callback", engine);
            engine = null;

            File.WriteAllText(path, ConfigJson(false, false));
            File.Delete(CrashLog.Path);
            int exitCode = -1;
            EngineOptions stall = Options();
            stall.StallLimitMs = 500;
            stall.WatchdogPeriodMs = 100;
            stall.Exit = delegate(int code) { exitCode = code; };
            engine = new Engine(path, false, stall);
            Expect("stall test hook installed", true, engine.KeyboardHookInstalled);
            engine.RunOnHookThread<int>(delegate { Thread.Sleep(1800); return 0; }, 6000);
            Expect("watchdog asked for exit 3", 3, exitCode);
            Expect("watchdog removed the keyboard hook", false, engine.KeyboardHookInstalled);
            string stallLog = File.Exists(CrashLog.Path) ? File.ReadAllText(CrashLog.Path) : "";
            Expect("watchdog wrote the crash log", true, stallLog.Contains("WATCHDOG") && stallLog.Contains("keyboard=True"));
            engine.Shutdown();
            ShutdownChecks("stall", engine);
            engine = null;
        }
        finally
        {
            if (engine != null)
                engine.Shutdown();
            CrashLog.Path = oldLog;
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    sealed class SendLog
    {
        public readonly object Gate = new object();
        public readonly List<string> Calls = new List<string>();
        public readonly List<int> Threads = new List<int>();
        public int InMouse;
        public int Count { get { lock (Gate) return Calls.Count; } }
        public string All { get { lock (Gate) return string.Join(" | ", Calls.ToArray()); } }
    }

    static EngineOptions Recording(SendLog log)
    {
        EngineOptions o = new EngineOptions();
        o.WheelLogPath = Path.Combine(SharedLogDir, "wheel.log");
        o.Send = delegate(List<Out> outs)
        {
            string text = string.Join(" ", outs.ConvertAll<string>(delegate(Out x) { return x.ToString(); }).ToArray());
            lock (log.Gate)
            {
                log.Calls.Add(text);
                log.Threads.Add(Thread.CurrentThread.ManagedThreadId);
                if (Engine.InMouseCallbackNow)
                    log.InMouse++;
            }
            return outs.Count;
        };
        return o;
    }

    static void MouseQueueTests()
    {
        string dir = TempDir();
        string oldLog = CrashLog.Path;
        CrashLog.Path = Path.Combine(dir, "crash.log");
        Engine e = null;
        try
        {
            string path = Path.Combine(dir, "config.json");

            File.WriteAllText(path, ConfigJson(true, false));
            SendLog log = new SendLog();
            e = new Engine(path, false, Recording(log));
            Expect("queue: alt down passes", false, e.KeyForTest(Vk.LAlt, true));
            Expect("queue: nothing sent for alt down", 0, log.Count);

            Expect("queue: click down swallowed", true, e.MouseForTest(MouseEvent.Click(MouseButton.Left, true)));
            Expect("queue: send happened outside the callback", true, WaitFor(delegate { return log.Count >= 1; }, 2000));
            Pump(100);
            Expect("queue: exactly one send for click down", 1, log.Count);
            Expect("queue: ctrl click sequence", "+LeftCtrl -LeftAlt +LeftBtn", log.Calls[0]);
            Expect("queue: sent on the hook thread", e.HookThreadManagedId, log.Threads[0]);
            Expect("queue: not inside a mouse callback", 0, log.InMouse);
            Expect("queue: guard counter zero", 0, e.InjectionsInMouseCallback);

            Expect("queue: click up swallowed", true, e.MouseForTest(MouseEvent.Click(MouseButton.Left, false)));
            Expect("queue: up injected", true, WaitFor(delegate { return log.Count >= 2; }, 2000));
            Expect("queue: up after down", "-LeftBtn", log.Calls[1]);
            Expect("queue: alt up swallowed after chord", true, e.KeyForTest(Vk.LAlt, false));
            Pump(100);
            Expect("queue: alt up releases ctrl", true, log.Count == 3 && log.Calls[2] == "-LeftCtrl");
            Expect("queue: still not inside a mouse callback", 0, log.InMouse);
            Expect("queue: guard counter still zero", 0, e.InjectionsInMouseCallback);

            Expect("queue: alt down again", false, e.KeyForTest(Vk.LAlt, true));
            int before = log.Count;
            Expect("queue: alt wheel swallowed", true, e.MouseForTest(MouseEvent.Wheel(-120)));
            Expect("queue: alt wheel goes through the queue", true, WaitFor(delegate { return log.Count > before; }, 2000));
            Expect("queue: alt wheel sequence", "+LeftCtrl -LeftAlt wheel(-120)", log.Calls[before]);
            Expect("queue: wheel send outside callback", 0, log.InMouse);
            e.KeyForTest(Vk.LAlt, false);
            e.Shutdown();
            ShutdownChecks("queue alt", e);
            Expect("queue: guard zero at the end", 0, e.InjectionsInMouseCallback);
            e = null;

            File.WriteAllText(path, ConfigJson(false, true, false));
            SendLog wlog = new SendLog();
            e = new Engine(path, false, Recording(wlog));
            Expect("queue: wheel passes first", false, e.MouseForTest(MouseEvent.Wheel(120)));
            Expect("queue: reverse wheel dropped", true, e.MouseForTest(MouseEvent.Wheel(-30)));
            Pump(150);
            Expect("queue: dropped wheel sends nothing", 0, wlog.Count);
            e.Shutdown();
            e = null;

            File.WriteAllText(path, ConfigJson(true, false));
            SendLog slog = new SendLog();
            e = new Engine(path, false, Recording(slog));
            e.KeyForTest(Vk.LAlt, true);
            e.PauseMouseDrain = true;
            e.MouseForTest(MouseEvent.Click(MouseButton.Left, true));
            Pump(100);
            Expect("queue: output is pending while paused", 1, e.PendingMouseOutputs);
            Expect("queue: nothing sent while paused", 0, slog.Count);
            e.Shutdown();
            Expect("queue: shutdown discarded pending output", 0, e.PendingMouseOutputs);
            Expect("queue: only releases were sent", "-LeftBtn -LeftCtrl", slog.All);
            e.Shutdown();
            Expect("queue: second shutdown harmless", "-LeftBtn -LeftCtrl", slog.All);
            ShutdownChecks("queue shutdown", e);
            Expect("queue: no guard hits", 0, e.InjectionsInMouseCallback);
            e = null;

            string crash = File.Exists(CrashLog.Path) ? File.ReadAllText(CrashLog.Path) : "";
            Expect("queue: no injection-in-callback line logged", false, crash.Contains("injection inside mouse callback"));
        }
        finally
        {
            if (e != null)
                e.Shutdown();
            CrashLog.Path = oldLog;
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
