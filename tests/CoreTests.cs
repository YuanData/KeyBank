using System;
using System.Collections.Generic;
using KeyBank.Core;

static class CoreTests
{
    static int failed;
    static int passed;
    static long clock;
    static string currentApp;

    static KeyEvent Ev(string text)
    {
        int vk;
        if (!KeyNames.TryParse(text.Substring(1), out vk))
            throw new Exception("bad key " + text);
        return new KeyEvent(vk, text[0] == '+');
    }

    static string Send(Pipeline p, string text)
    {
        return p.Process(Ev(text), clock).ToString();
    }

    static string Join(List<Out> outs)
    {
        return outs.Count == 0 ? "none" : string.Join(" ", outs.ConvertAll<string>(delegate(Out o) { return o.ToString(); }).ToArray());
    }

    static string Tick(Pipeline p)
    {
        return Join(p.Tick(clock));
    }

    static string Click(Pipeline p, MouseButton b, bool down)
    {
        return p.ProcessMouse(MouseEvent.Click(b, down)).ToString();
    }

    static string Wheel(Pipeline p, int delta)
    {
        return p.ProcessMouse(MouseEvent.Wheel(delta)).ToString();
    }

    static void Expect(string name, string expected, string actual)
    {
        if (expected == actual)
        {
            passed++;
            return;
        }
        failed++;
        Console.WriteLine("FAIL " + name + "\n   expected: " + expected + "\n   actual:   " + actual);
    }

    static void ExpectThrows(string name, string fragment, string json)
    {
        try
        {
            Settings.Load(json);
            failed++;
            Console.WriteLine("FAIL " + name + " (no exception)");
        }
        catch (ConfigException ex)
        {
            Expect(name, "True", ex.Message.Contains(fragment).ToString());
            if (!ex.Message.Contains(fragment))
                Console.WriteLine("   message: " + ex.Message);
        }
    }

    static Pipeline New(string profile)
    {
        return NewWith(DefaultConfig.Json.Replace("\"altMouse\": false", "\"altMouse\": true"), profile);
    }

    static Pipeline NewWith(string json, string profile)
    {
        clock = 0;
        currentApp = null;
        return new Pipeline(Settings.Load(json), profile, delegate { return currentApp; });
    }

    static void Main()
    {
        Settings s = Settings.Load(DefaultConfig.Json);
        Expect("device 3434 -> full", "full", s.ResolveProfile("3434"));
        Expect("device 046d (case-insensitive) -> mini", "mini", s.ResolveProfile("046d"));
        Expect("unknown device -> default", "full", s.ResolveProfile("FFFF"));
        Expect("no device -> default", "full", s.ResolveProfile(null));
        Expect("default step ticks", "4", s.Scroll.StepTicks.ToString());
        Expect("default page ticks", "8", s.Scroll.PageTicks.ToString());
        Expect("default is not an upgrade", "False", s.Upgraded.ToString());
        Expect("config roundtrip", s.File.ToJson(), Settings.Load(s.File.ToJson()).File.ToJson());

        Pipeline p = New("full");
        Expect("alt chord: alt down passes", "pass", Send(p, "+LeftAlt"));
        Expect("alt chord: C down", "+LeftCtrl -LeftAlt +C", Send(p, "+C"));
        Expect("alt chord: C up", "-C", Send(p, "-C"));
        Expect("alt chord: second chord keeps ctrl held", "+V", Send(p, "+V"));
        Expect("alt chord: V up", "-V", Send(p, "-V"));
        Expect("alt chord: alt up releases ctrl", "-LeftCtrl", Send(p, "-LeftAlt"));
        Expect("alt chord: plain C afterwards passes", "pass", Send(p, "+C"));
        Send(p, "-C");

        p = New("full");
        Send(p, "+LeftAlt");
        Expect("alt tap: alt up passes", "pass", Send(p, "-LeftAlt"));

        p = New("full");
        Send(p, "+LeftAlt");
        Expect("alt+tab passes untouched", "pass", Send(p, "+Tab"));
        Send(p, "-Tab");
        Expect("alt+tab: alt up passes", "pass", Send(p, "-LeftAlt"));

        p = New("full");
        Send(p, "+LeftAlt");
        Send(p, "+C");
        Send(p, "-C");
        Expect("non-chord key after chord restores alt", "-LeftCtrl +LeftAlt +Tab", Send(p, "+Tab"));
        Send(p, "-Tab");
        Expect("alt up after restore passes", "pass", Send(p, "-LeftAlt"));

        p = New("full");
        Send(p, "+LeftAlt");
        Send(p, "+LeftShift");
        Expect("alt+shift+Z keeps shift physical", "+LeftCtrl -LeftAlt +Z", Send(p, "+Z"));

        p = New("full");
        Send(p, "+LeftCtrl");
        Send(p, "+LeftAlt");
        Expect("physical ctrl+alt+C untouched", "pass", Send(p, "+C"));

        p = New("full");
        Send(p, "+LeftAlt");
        Send(p, "+C");
        Expect("chord key repeat re-emits", "+C", Send(p, "+C"));
        Expect("alt auto-repeat swallowed while chord", "swallow", Send(p, "+LeftAlt"));

        p = New("full");
        Send(p, "+LeftAlt");
        Send(p, "+C");
        Expect("alt released before chord key", "-LeftCtrl", Send(p, "-LeftAlt"));
        Expect("chord key up still released", "-C", Send(p, "-C"));

        p = New("full");
        currentApp = "chrome.exe";
        Send(p, "+LeftAlt");
        Expect("chrome: Alt+W -> Ctrl+W", "+LeftCtrl -LeftAlt +W", Send(p, "+W"));
        Send(p, "-W");
        Expect("chrome: Alt+1 -> Ctrl+1", "+1", Send(p, "+1"));
        Send(p, "-1");
        Expect("chrome: Alt+Equals", "+Equals", Send(p, "+Equals"));
        Send(p, "-Equals");
        Expect("chrome: Alt+Minus", "+Minus", Send(p, "+Minus"));
        Send(p, "-Minus");
        Send(p, "-LeftAlt");

        p = New("full");
        currentApp = "Chrome";
        Send(p, "+LeftAlt");
        Send(p, "+LeftShift");
        Expect("chrome (name without .exe): Alt+Shift+T -> Ctrl+Shift+T", "+LeftCtrl -LeftAlt +T", Send(p, "+T"));

        p = New("full");
        currentApp = "notepad.exe";
        Send(p, "+LeftAlt");
        Expect("other app: Alt+W untouched", "pass", Send(p, "+W"));
        Expect("other app: global Alt+C still works", "+LeftCtrl -LeftAlt +C", Send(p, "+C"));

        p = New("full");
        currentApp = null;
        Send(p, "+LeftAlt");
        Expect("unknown app: Alt+T untouched", "pass", Send(p, "+T"));

        p = New("full");
        Send(p, "+LeftAlt");
        Expect("mouse: alt+left down", "+LeftCtrl -LeftAlt +LeftBtn", Click(p, MouseButton.Left, true));
        Expect("mouse: left up", "-LeftBtn", Click(p, MouseButton.Left, false));
        Expect("mouse: second click keeps ctrl", "+RightBtn", Click(p, MouseButton.Right, true));
        Expect("mouse: right up", "-RightBtn", Click(p, MouseButton.Right, false));
        Expect("mouse: alt up releases ctrl", "-LeftCtrl", Send(p, "-LeftAlt"));

        p = New("full");
        Send(p, "+LeftAlt");
        Click(p, MouseButton.Middle, true);
        Expect("mouse: alt released before button up", "-LeftCtrl", Send(p, "-LeftAlt"));
        Expect("mouse: late button up still released", "-MiddleBtn", Click(p, MouseButton.Middle, false));

        p = New("full");
        Send(p, "+LeftAlt");
        Expect("mouse: alt+wheel", "+LeftCtrl -LeftAlt wheel(120)", Wheel(p, 120));
        Expect("mouse: next wheel keeps ctrl", "wheel(-120)", Wheel(p, -120));

        p = New("full");
        Expect("mouse: click without alt passes", "pass", Click(p, MouseButton.Left, true));
        Expect("mouse: release without chord passes", "pass", Click(p, MouseButton.Left, false));
        Expect("mouse: wheel without alt passes", "pass", Wheel(p, 120));

        p = New("full");
        Send(p, "+LeftCtrl");
        Send(p, "+LeftAlt");
        Expect("mouse: physical ctrl+alt+click untouched", "pass", Click(p, MouseButton.Left, true));

        p = NewWith(DefaultConfig.Json, "full");
        Send(p, "+LeftAlt");
        Expect("mouse: off by default", "pass", Click(p, MouseButton.Left, true));
        Expect("mouse: default config has altMouse off", "False", s.AltMouse.ToString());

        p = New("full");
        Send(p, "+LeftAlt");
        Click(p, MouseButton.Left, true);
        Expect("releaseAll lets go of ctrl and mouse", "-LeftBtn -LeftCtrl", Join(p.ReleaseAll()));

        p = New("full");
        Expect("scroll: first press is one step", "scrollDown(4)", Send(p, "+PageDown"));
        Expect("scroll: key up swallowed", "swallow", Send(p, "-PageDown"));
        clock = 300;
        Expect("scroll: second press within interval is still a step", "scrollDown(4)", Send(p, "+PageDown"));
        Send(p, "-PageDown");
        clock = 600;
        Expect("scroll: third press within interval is a page", "scrollDown(8)", Send(p, "+PageDown"));
        Send(p, "-PageDown");
        clock = 900;
        Expect("scroll: fourth press stays a page", "scrollDown(8)", Send(p, "+PageDown"));
        Send(p, "-PageDown");
        clock = 1400;
        Expect("scroll: pause longer than 400ms resets to a step", "scrollDown(4)", Send(p, "+PageDown"));
        Send(p, "-PageDown");

        p = New("full");
        Send(p, "+PageDown");
        Send(p, "-PageDown");
        clock = 100;
        Send(p, "+PageDown");
        Send(p, "-PageDown");
        clock = 200;
        Expect("scroll: direction change resets the count", "scrollUp(4)", Send(p, "+PageUp"));
        Send(p, "-PageUp");
        clock = 300;
        Expect("scroll: back to the first direction restarts", "scrollDown(4)", Send(p, "+PageDown"));

        p = New("full");
        Send(p, "+PageDown");
        clock = 250;
        Expect("hold: first repeat counts as second press", "scrollDown(4)", Send(p, "+PageDown"));
        clock = 280;
        Expect("hold: repeat inside the rate limit is swallowed", "swallow", Send(p, "+PageDown"));
        clock = 375;
        Expect("hold: repeat past the limit scrolls a full page", "scrollDown(8)", Send(p, "+PageDown"));
        clock = 400;
        Expect("hold: still rate limited", "swallow", Send(p, "+PageDown"));
        clock = 500;
        Expect("hold: next allowed repeat", "scrollDown(8)", Send(p, "+PageDown"));
        Expect("hold: release swallowed", "swallow", Send(p, "-PageDown"));

        p = New("full");
        Send(p, "+PageDown");
        Send(p, "-PageDown");
        Send(p, "+LeftCtrl");
        Expect("ctrl+PgDn untouched", "pass", Send(p, "+PageDown"));
        Send(p, "-PageDown");
        Send(p, "-LeftCtrl");
        Expect("full: Home passes", "pass", Send(p, "+Home"));
        Send(p, "-Home");

        string appScroll = DefaultConfig.Json.Replace("\"apps\": {}", "\"apps\": { \"Notepad++.exe\": { \"stepTicks\": 6, \"pageTicks\": 12 } }");
        p = NewWith(appScroll, "full");
        currentApp = "notepad++.exe";
        Expect("scroll: per-app step", "scrollDown(6)", Send(p, "+PageDown"));
        Send(p, "-PageDown");
        clock = 100;
        Send(p, "+PageDown");
        Send(p, "-PageDown");
        clock = 200;
        Expect("scroll: per-app page", "scrollDown(12)", Send(p, "+PageDown"));
        Send(p, "-PageDown");
        p = NewWith(appScroll, "full");
        currentApp = "chrome.exe";
        Expect("scroll: other apps use the default", "scrollDown(4)", Send(p, "+PageDown"));

        p = NewWith(DefaultConfig.Json.Replace("\"enabled\": true,\n    \"stepTicks\"", "\"enabled\": false,\n    \"stepTicks\""), "full");
        Expect("scroll: disabled passes PgDn through", "pass", Send(p, "+PageDown"));

        p = New("mini");
        Expect("mini: physical PgUp -> Home", "+Home", Send(p, "+PageUp"));
        Expect("mini: physical PgUp up -> Home up", "-Home", Send(p, "-PageUp"));
        Expect("mini: physical PgDn -> logical PgUp -> scroll up", "scrollUp(4)", Send(p, "+PageDown"));
        Send(p, "-PageDown");
        Expect("mini: * -> End", "+End", Send(p, "+Multiply"));
        Send(p, "-Multiply");
        clock = 2000;
        Expect("mini: - -> PgDn -> scroll down", "scrollDown(4)", Send(p, "+Subtract"));
        Send(p, "-Subtract");
        Expect("mini: physical End passes", "pass", Send(p, "+End"));
        Send(p, "-End");

        p = New("mini");
        Expect("tap-hold: Home down swallowed", "swallow", Send(p, "+Home"));
        clock = 120;
        Expect("tap-hold: short tap emits Home on release", "+Home -Home", Send(p, "-Home"));

        p = New("mini");
        Send(p, "+Home");
        clock = 499;
        Expect("tap-hold: before threshold nothing fires", "none", Tick(p));
        clock = 500;
        Expect("tap-hold: threshold fires PrintScreen", "+PrintScreen -PrintScreen", Tick(p));
        clock = 900;
        Expect("tap-hold: repeats swallowed", "swallow", Send(p, "+Home"));
        Expect("tap-hold: release after hold swallowed", "swallow", Send(p, "-Home"));
        Expect("tap-hold: second tick is quiet", "none", Tick(p));

        p = New("mini");
        Send(p, "+Home");
        clock = 700;
        Expect("tap-hold: late release without tick still holds", "+PrintScreen -PrintScreen", Send(p, "-Home"));

        p = New("mini");
        Send(p, "+Home");
        clock = 50;
        Expect("tap-hold: other key interrupts as tap", "+Home -Home +A", Send(p, "+A"));
        clock = 80;
        Expect("tap-hold: Home release after interrupt swallowed", "swallow", Send(p, "-Home"));

        p = New("mini");
        Send(p, "+LeftShift");
        Expect("tap-hold: shift+Home passes", "pass", Send(p, "+Home"));
        Expect("tap-hold: shift+Home release passes", "pass", Send(p, "-Home"));

        p = New("mini");
        Send(p, "+LeftAlt");
        Send(p, "+C");
        Expect("setProfile releases injected ctrl", "-C -LeftCtrl", Join(p.SetProfile("full")));

        string legacy = DefaultConfig.Json
            .Replace("\"scroll\": {\n    \"enabled\": true,\n    \"stepTicks\": 4,\n    \"pageTicks\": 8,\n    \"burstTaps\": 3,\n    \"burstIntervalMs\": 400,\n    \"holdRepeatsPerSec\": 8,\n    \"apps\": {}\n  },", "\"halfPage\": { \"enabled\": true, \"fraction\": \"1/3\", \"pageTicks\": 9 },");
        legacy = legacy.Substring(0, legacy.IndexOf("\"appAltChords\"")) + legacy.Substring(legacy.IndexOf("\"devices\""));
        Settings old = Settings.Load(legacy);
        Expect("legacy halfPage: flagged as upgrade", "True", old.Upgraded.ToString());
        Expect("legacy halfPage: step from fraction", "3", old.Scroll.StepTicks.ToString());
        Expect("legacy halfPage: page ticks kept", "9", old.Scroll.PageTicks.ToString());
        Expect("legacy: chrome chords get the defaults", "True", (old.AppChordsFor("chrome.exe") != null && old.AppChordsFor("chrome.exe").Contains('W')).ToString());
        string upgraded = old.File.ToJson();
        Expect("legacy: written file has the new sections", "True", (upgraded.Contains("\"scroll\"") && upgraded.Contains("\"appAltChords\"") && !upgraded.Contains("halfPage")).ToString());
        Expect("legacy: upgraded file is no longer an upgrade", "False", Settings.Load(upgraded).Upgraded.ToString());

        ExpectThrows("bad key name", "不認得按鍵名稱 'Foo'", DefaultConfig.Json.Replace("\"to\": \"Home\"", "\"to\": \"Foo\""));
        ExpectThrows("bad chrome chord", "appAltChords.chrome.exe[0]", DefaultConfig.Json.Replace("\"chrome.exe\": [\"W\"", "\"chrome.exe\": [\"Nope\""));
        ExpectThrows("unknown default profile", "defaultProfile", DefaultConfig.Json.Replace("\"defaultProfile\": \"full\"", "\"defaultProfile\": \"nope\""));
        ExpectThrows("hold too short", "holdMs", DefaultConfig.Json.Replace("\"holdMs\": 500", "\"holdMs\": 10"));
        ExpectThrows("step ticks zero", "scroll.stepTicks", DefaultConfig.Json.Replace("\"stepTicks\": 4", "\"stepTicks\": 0"));
        ExpectThrows("burst taps too small", "scroll.burstTaps", DefaultConfig.Json.Replace("\"burstTaps\": 3", "\"burstTaps\": 1"));
        ExpectThrows("burst interval too long", "scroll.burstIntervalMs", DefaultConfig.Json.Replace("\"burstIntervalMs\": 400", "\"burstIntervalMs\": 9000"));
        ExpectThrows("hold rate too high", "scroll.holdRepeatsPerSec", DefaultConfig.Json.Replace("\"holdRepeatsPerSec\": 8", "\"holdRepeatsPerSec\": 99"));
        ExpectThrows("invalid json", "JSON", "{ not json");

        Console.WriteLine("passed " + passed + ", failed " + failed);
        Environment.Exit(failed == 0 ? 0 : 1);
    }
}
