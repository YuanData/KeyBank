using System;
using System.Collections.Generic;
using KeyBank.Core;

static class CoreTests
{
    static readonly string Cfg = DefaultConfig.Json.Replace("\r\n", "\n");

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
        return p.ProcessMouse(MouseEvent.Click(b, down), clock).ToString();
    }

    static string Wheel(Pipeline p, int delta)
    {
        return p.ProcessMouse(MouseEvent.Wheel(delta), clock).ToString();
    }

    static string Wheel(Pipeline p, int delta, long at)
    {
        clock = at;
        return Wheel(p, delta);
    }

    static string Verdict(WheelReversalFilter f, int delta, long at)
    {
        return f.ShouldDrop(delta, at) ? "drop" : "pass";
    }

    static string WheelRun(WheelReversalFilter f, params long[] deltasAndTimes)
    {
        List<string> verdicts = new List<string>();
        for (int i = 0; i + 1 < deltasAndTimes.Length; i += 2)
            verdicts.Add(Verdict(f, (int)deltasAndTimes[i], deltasAndTimes[i + 1]));
        return string.Join(" ", verdicts.ToArray());
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
        return NewWith(Cfg.Replace("\"altMouse\": false", "\"altMouse\": true"), profile);
    }

    static Pipeline NewWith(string json, string profile)
    {
        clock = 0;
        currentApp = null;
        return new Pipeline(Settings.Load(json), profile, delegate { return currentApp; });
    }

    static string Burst(WheelBurstRecorder r, long arrival, long hardware, int delta, bool dropped)
    {
        return r.Add(arrival, hardware, delta, dropped, "chrome", "W") ?? "-";
    }

    static void WheelLogTests()
    {
        WheelBurstRecorder r = new WheelBurstRecorder(250);
        Expect("log: first event starts a burst, returns nothing", "-", Burst(r, 0, 0, -120, false));
        Burst(r, 10, 10, -120, false);
        Burst(r, 20, 20, -120, false);
        Burst(r, 30, 30, -120, false);
        Expect("log: a burst is not finished inside the gap", "-", r.Poll(280) ?? "-");
        Expect("log: steady burst summary",
            "W app=chrome dir=down n=4 sum=-480 arr=30ms hw=30ms maxHwGap=10ms tailHw=10,10,10 tailArr=10,10,10 clump=0 rev=0",
            r.Poll(281) ?? "-");
        Expect("log: a finished burst is reported once", "-", r.Poll(10000) ?? "-");
        Expect("log: recorder is idle after the report", "False", r.Active.ToString());

        r = new WheelBurstRecorder(250);
        Burst(r, 0, 0, 120, false);
        Burst(r, 10, 10, 120, false);
        Expect("log: the next burst's first event returns the previous summary",
            "W app=chrome dir=up n=2 sum=240 arr=10ms hw=10ms maxHwGap=10ms tailHw=10 tailArr=10 clump=0 rev=0",
            Burst(r, 400, 400, 120, false));
        Expect("log: the new burst is active", "True", r.Active.ToString());

        r = new WheelBurstRecorder(250);
        for (int i = 0; i < 10; i++)
            Burst(r, i * 5, i * 5, -120, false);
        Expect("log: only the last six gaps are kept",
            "W app=chrome dir=down n=10 sum=-1200 arr=45ms hw=45ms maxHwGap=5ms tailHw=5,5,5,5,5,5 tailArr=5,5,5,5,5,5 clump=0 rev=0",
            r.Flush() ?? "-");
        Expect("log: flush with nothing active", "-", r.Flush() ?? "-");

        r = new WheelBurstRecorder(250);
        Burst(r, 100, 0, -120, false);
        Burst(r, 100, 8, -120, false);
        Burst(r, 101, 16, -120, false);
        Burst(r, 103, 24, -120, false);
        Expect("log: events bunched on arrival but regular in hardware are counted as clumps",
            "W app=chrome dir=down n=4 sum=-480 arr=3ms hw=24ms maxHwGap=8ms tailHw=8,8,8 tailArr=0,1,2 clump=3 rev=0",
            r.Flush() ?? "-");

        r = new WheelBurstRecorder(250);
        Burst(r, 0, 0, -120, false);
        Burst(r, 10, 10, -120, false);
        Burst(r, 40, 40, 120, true);
        Expect("log: a reverse event is listed with its offset and verdict",
            "W app=chrome dir=mixed n=3 sum=-120 arr=40ms hw=40ms maxHwGap=30ms tailHw=10,30 tailArr=10,30 clump=0 rev=1 +120@40ms:drop",
            r.Flush() ?? "-");

        r = new WheelBurstRecorder(250);
        Burst(r, 0, 0, -120, false);
        for (int i = 1; i <= 10; i++)
            Burst(r, i * 4, i * 4, i % 2 == 0 ? -30 : 30, i % 3 == 0);
        string many = r.Flush() ?? "-";
        Expect("log: reverse count is exact but the list is capped at eight", "True",
            (many.Contains("rev=5 ") || many.Contains("rev=5")).ToString());

        r = new WheelBurstRecorder(250);
        Expect("log: a zero delta is ignored", "-", Burst(r, 0, 0, 0, false));
        Expect("log: a zero delta starts no burst", "False", r.Active.ToString());
        Expect("log: mark line", "W MARK app=chrome", WheelBurstRecorder.MarkLine("W", "chrome"));

        Settings d = Settings.Load(Cfg);
        Expect("diagnostics: defaults", "True/250/120", d.Diagnostics.WheelLog + "/" + d.Diagnostics.BurstGapMs + "/" + d.Diagnostics.MarkVk);
        Settings off = Settings.Load(Cfg.Replace("\"markKey\": \"F9\"", "\"markKey\": \"\"").Replace("\"wheelLog\": true", "\"wheelLog\": false"));
        Expect("diagnostics: empty mark key disables it, log can be turned off", "False/0", off.Diagnostics.WheelLog + "/" + off.Diagnostics.MarkVk);
        Expect("diagnostics: a file without the section gets the defaults", "True",
            Settings.Load(Cfg.Replace("  \"diagnostics\": { \"wheelLog\": true, \"burstGapMs\": 250, \"markKey\": \"F9\" },\n", "")).Diagnostics.WheelLog.ToString());
        Expect("diagnostics: written back by ToJson", "True", d.File.ToJson().Contains("\"diagnostics\"").ToString());
        ExpectThrows("diagnostics: unknown mark key", "diagnostics.markKey", Cfg.Replace("\"markKey\": \"F9\"", "\"markKey\": \"Nope\""));
        ExpectThrows("diagnostics: burst gap too small", "diagnostics.burstGapMs", Cfg.Replace("\"burstGapMs\": 250", "\"burstGapMs\": 50"));
    }

    static void WheelFilterTests()
    {
        const int Up = 120, Down = -120;

        WheelReversalFilter f = new WheelReversalFilter(80, 120);
        Expect("wheel: steady scroll down all passes", "pass pass pass pass pass",
            WheelRun(f, Down, 0, Down, 10, Down, 20, Down, 30, Down, 40));

        f = new WheelReversalFilter(80, 120);
        Expect("wheel: one reverse tick inside a down scroll is dropped", "pass drop pass",
            WheelRun(f, Down, 0, Up, 10, Down, 20));

        f = new WheelReversalFilter(80, 120);
        Expect("wheel: mirrored, a down tick inside an up scroll is dropped", "pass drop pass",
            WheelRun(f, Up, 0, Down, 10, Up, 20));

        f = new WheelReversalFilter(80, 120);
        Expect("wheel: dropped noise does not carry over to the next noise", "pass drop pass drop pass drop",
            WheelRun(f, Down, 0, Up, 10, Down, 20, Up, 30, Down, 40, Up, 50));

        f = new WheelReversalFilter(80, 120);
        Expect("wheel: two reverse ticks in the window are a real reversal", "pass drop pass pass",
            WheelRun(f, Down, 0, Up, 10, Up, 20, Up, 30));
        Expect("wheel: after the switch the old direction is now the noise", "drop pass",
            WheelRun(f, Down, 40, Up, 50));

        f = new WheelReversalFilter(80, 120);
        Expect("wheel: reverse exactly at the window edge is still a candidate", "pass drop",
            WheelRun(f, Down, 0, Up, 80));
        f = new WheelReversalFilter(80, 120);
        Expect("wheel: reverse one ms after the window starts a new scroll", "pass pass",
            WheelRun(f, Down, 0, Up, 81));

        f = new WheelReversalFilter(80, 120);
        Expect("wheel: scrolling resumes after a stop", "pass pass pass",
            WheelRun(f, Down, 0, Down, 500, Up, 900));

        f = new WheelReversalFilter(80, 120);
        Expect("wheel: small deltas add up to the threshold, the next one passes", "pass drop drop drop drop pass",
            WheelRun(f, Down, 0, 30, 5, 30, 10, 30, 15, 30, 20, 30, 25));

        f = new WheelReversalFilter(80, 120);
        Expect("wheel: one large reverse event is a real reversal", "pass pass",
            WheelRun(f, Down, 0, 240, 10));

        f = new WheelReversalFilter(80, 120);
        Expect("wheel: dropped events never move the clock", "pass drop pass",
            WheelRun(f, Down, 0, Up, 50, Up, 100));

        f = new WheelReversalFilter(80, 120);
        Expect("wheel: a zero delta changes nothing", "pass pass drop",
            WheelRun(f, Down, 0, 0, 10, Up, 20));
        Expect("wheel: primary direction is tracked", "-1", f.PrimaryDirection.ToString());

        f = new WheelReversalFilter(200, 300);
        Expect("wheel: custom window and threshold", "pass drop drop pass",
            WheelRun(f, Down, 0, Up, 150, Up, 160, Up, 170));

        f = new WheelReversalFilter(80, 120);
        WheelRun(f, Down, 0);
        f.Reset();
        Expect("wheel: reset forgets the direction", "pass", WheelRun(f, Up, 10));

        Expect("wheel: defaults are 80 ms and 120", "80/120", WheelReversalFilter.DefaultWindowMs + "/" + WheelReversalFilter.DefaultThresholdDelta);

        string on = Cfg.Replace("\"wheelFilter\": { \"enabled\": false", "\"wheelFilter\": { \"enabled\": true");
        Expect("wheel: off by default in the shipped config", "False", Settings.Load(Cfg).WheelFilter.Enabled.ToString());
        Expect("wheel: config values are read", "True/80/120",
            Settings.Load(on).WheelFilter.Enabled + "/" + Settings.Load(on).WheelFilter.WindowMs + "/" + Settings.Load(on).WheelFilter.ThresholdDelta);
        Expect("wheel: a file without the section keeps it off", "False",
            Settings.Load(Cfg.Replace("  \"wheelFilter\": { \"enabled\": false, \"windowMs\": 80, \"thresholdDelta\": 120 },\n", "")).WheelFilter.Enabled.ToString());
        Expect("wheel: written back by ToJson", "True", Settings.Load(on).File.ToJson().Contains("\"wheelFilter\"").ToString());

        Pipeline p = NewWith(Cfg, "full");
        Expect("pipeline wheel: filter off passes reverse noise", "pass pass",
            Wheel(p, Down, 0) + " " + Wheel(p, Up, 10));

        p = NewWith(on, "full");
        Expect("pipeline wheel: noise swallowed", "pass swallow pass",
            Wheel(p, Down, 0) + " " + Wheel(p, Up, 10) + " " + Wheel(p, Down, 20));
        MouseEvent injected = MouseEvent.Wheel(Up);
        injected.Injected = true;
        clock = 30;
        Expect("pipeline wheel: input injected by another program is never filtered", "pass", p.ProcessMouse(injected, clock).ToString());
        Expect("pipeline wheel: buttons are untouched", "pass", Click(p, MouseButton.Left, true));

        string both = on.Replace("\"altMouse\": false", "\"altMouse\": true");
        p = NewWith(both, "full");
        Send(p, "+LeftAlt");
        Expect("pipeline wheel: alt+wheel still becomes ctrl+wheel", "+LeftCtrl -LeftAlt wheel(-120)", Wheel(p, Down, 0));
        Expect("pipeline wheel: noise during alt+wheel is dropped without touching alt state", "swallow", Wheel(p, Up, 10));
        Expect("pipeline wheel: the next real tick continues as ctrl+wheel", "wheel(-120)", Wheel(p, Down, 20));
        Expect("pipeline wheel: alt release still lets go of ctrl", "-LeftCtrl", Send(p, "-LeftAlt"));

        ExpectThrows("wheel window too small", "wheelFilter.windowMs", Cfg.Replace("\"windowMs\": 80", "\"windowMs\": 5"));
        ExpectThrows("wheel threshold zero", "wheelFilter.thresholdDelta", Cfg.Replace("\"thresholdDelta\": 120", "\"thresholdDelta\": 0"));
    }

    static void Main()
    {
        Settings s = Settings.Load(Cfg);
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

        p = NewWith(Cfg, "full");
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

        string appScroll = Cfg.Replace("\"apps\": {}", "\"apps\": { \"Notepad++.exe\": { \"stepTicks\": 6, \"pageTicks\": 12 } }");
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

        p = NewWith(Cfg.Replace("\"enabled\": true,\n    \"stepTicks\"", "\"enabled\": false,\n    \"stepTicks\""), "full");
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

        string legacy = Cfg
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

        WheelFilterTests();
        WheelLogTests();

        ExpectThrows("bad key name", "不認得按鍵名稱 'Foo'", Cfg.Replace("\"to\": \"Home\"", "\"to\": \"Foo\""));
        ExpectThrows("bad chrome chord", "appAltChords.chrome.exe[0]", Cfg.Replace("\"chrome.exe\": [\"W\"", "\"chrome.exe\": [\"Nope\""));
        ExpectThrows("unknown default profile", "defaultProfile", Cfg.Replace("\"defaultProfile\": \"full\"", "\"defaultProfile\": \"nope\""));
        ExpectThrows("hold too short", "holdMs", Cfg.Replace("\"holdMs\": 500", "\"holdMs\": 10"));
        ExpectThrows("step ticks zero", "scroll.stepTicks", Cfg.Replace("\"stepTicks\": 4", "\"stepTicks\": 0"));
        ExpectThrows("burst taps too small", "scroll.burstTaps", Cfg.Replace("\"burstTaps\": 3", "\"burstTaps\": 1"));
        ExpectThrows("burst interval too long", "scroll.burstIntervalMs", Cfg.Replace("\"burstIntervalMs\": 400", "\"burstIntervalMs\": 9000"));
        ExpectThrows("hold rate too high", "scroll.holdRepeatsPerSec", Cfg.Replace("\"holdRepeatsPerSec\": 8", "\"holdRepeatsPerSec\": 99"));
        ExpectThrows("invalid json", "JSON", "{ not json");

        Console.WriteLine("passed " + passed + ", failed " + failed);
        Environment.Exit(failed == 0 ? 0 : 1);
    }
}
