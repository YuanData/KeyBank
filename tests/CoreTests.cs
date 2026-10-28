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

    // "mini" means the numpad stands in for the editing cluster, "full" means it does not.
    static Pipeline NewWith(string json, string profile)
    {
        clock = 0;
        currentApp = null;
        if (profile == "mini")
            json = json.Replace("\"numpadSixKeys\": false", "\"numpadSixKeys\": true");
        return new Pipeline(Settings.Load(json), delegate { return currentApp; });
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

    static string AccelRun(WheelAccelerator a, int delta, int count, int stepMs, long start)
    {
        List<string> outs = new List<string>();
        for (int i = 0; i < count; i++)
            outs.Add(a.Scale(delta, start + (long)i * stepMs).ToString());
        return string.Join(" ", outs.ToArray());
    }

    static WheelAccelerator Accel(string find, string replace)
    {
        string json = Cfg.Replace("\"wheelAccel\": { \"enabled\": false, \"gear\": 3", "\"wheelAccel\": { \"enabled\": true, \"gear\": 0");
        if (find != null)
            json = json.Replace(find, replace);
        return new WheelAccelerator(Settings.Load(json).WheelAccel);
    }

    static WheelAccelerator GearAccel(int gear)
    {
        string json = Cfg.Replace("\"wheelAccel\": { \"enabled\": false, \"gear\": 3", "\"wheelAccel\": { \"enabled\": true, \"gear\": " + gear);
        return new WheelAccelerator(Settings.Load(json).WheelAccel);
    }

    static void WheelAccelTests()
    {
        WheelAccelerator a = Accel(null, null);
        Expect("accel: the first three ticks are untouched", "120 120 120", AccelRun(a, 120, 3, 40, 0));
        a = Accel(null, null);
        Expect("accel: ticks past the start ramp in linearly up to double", "120 120 120 144 168 192 216 240 240",
            AccelRun(a, 120, 9, 40, 0));
        a = Accel(null, null);
        Expect("accel: scrolling down mirrors scrolling up", "-120 -120 -120 -144",
            AccelRun(a, -120, 4, 40, 0));

        a = Accel(null, null);
        AccelRun(a, 120, 13, 40, 0);
        Expect("accel: time boost starts after 500 ms of one burst", "245", a.Scale(120, 520).ToString());
        a = Accel(null, null);
        Expect("accel: time boost is fully in after 1500 ms", "480", AccelRun(a, 120, 40, 40, 0).Split(' ')[39]);

        a = Accel("\"maxMultiplier\": 5", "\"maxMultiplier\": 3");
        AccelRun(a, 120, 40, 40, 0);
        Expect("accel: the multiplier is capped", "360", a.Scale(120, 1600).ToString());

        a = Accel(null, null);
        AccelRun(a, 120, 10, 40, 0);
        Expect("accel: a pause longer than the gap starts a new burst", "120 120 120 144", AccelRun(a, 120, 4, 40, 360 + 101));
        a = Accel(null, null);
        AccelRun(a, 120, 10, 40, 0);
        Expect("accel: a pause exactly at the gap keeps the burst", "240", a.Scale(120, 360 + 100).ToString());

        a = Accel(null, null);
        AccelRun(a, 120, 10, 40, 0);
        Expect("accel: reversing direction starts a new burst", "-120", a.Scale(-120, 400).ToString());

        a = Accel(null, null);
        Expect("accel: high resolution wheels count fractional ticks", "30 30 30 30 30 30 30 30 30 30 30 30 32",
            AccelRun(a, 30, 13, 10, 0));

        a = Accel(null, null);
        Expect("accel: a zero delta changes nothing", "0", a.Scale(0, 0).ToString());
        a.Reset();
        Expect("accel: reset forgets the burst", "120", a.Scale(120, 10).ToString());

        string on = Cfg.Replace("\"wheelAccel\": { \"enabled\": false, \"gear\": 3", "\"wheelAccel\": { \"enabled\": true, \"gear\": 0");
        Expect("accel: off by default in the shipped config", "False", Settings.Load(Cfg).WheelAccel.Enabled.ToString());
        Expect("accel: config values are read", "True/100/5", Settings.Load(on).WheelAccel.Enabled + "/" + Settings.Load(on).WheelAccel.GapMs + "/" + Settings.Load(on).WheelAccel.MaxMultiplier);
        Expect("accel: a file without the section keeps it off", "False",
            Settings.Load(Cfg.Substring(0, Cfg.IndexOf("  \"wheelAccel\"")) + Cfg.Substring(Cfg.IndexOf("  \"diagnostics\""))).WheelAccel.Enabled.ToString());
        Expect("accel: written back by ToJson", "True", Settings.Load(on).File.ToJson().Contains("\"wheelAccel\"").ToString());
        Expect("accel: gear 3 is the shipped default", "3", Settings.Load(Cfg).WheelAccel.Gear.ToString());
        Expect("accel: gear 3 reaches its cap within one burst", "120 280 440 600 600",
            AccelRun(GearAccel(3), 120, 5, 40, 0));
        Expect("accel: gear 1 starts gently", "120 120 120 140", AccelRun(GearAccel(1), 120, 4, 40, 0));
        Expect("accel: gear 5 boosts from the first tick", "660 1200 1200", AccelRun(GearAccel(5), 120, 3, 40, 0));
        Expect("accel: a preset ignores the custom numbers", "120 280",
            AccelRun(new WheelAccelerator(Settings.Load(Cfg.Replace("\"gapMs\": 100", "\"gapMs\": 999").Replace("\"enabled\": false, \"gear\": 3", "\"enabled\": true, \"gear\": 3")).WheelAccel), 120, 2, 40, 0));
        WheelAccelerator chain = Accel("\"chainBoost1\": 0", "\"chainBoost1\": 1");
        Expect("accel chain: bursts 1 and 2 pass, burst 3 is doubled", "120 120 240 240",
            AccelRun(chain, 120, 1, 40, 0) + " " + AccelRun(chain, 120, 1, 40, 400) + " " + AccelRun(chain, 120, 1, 40, 800) + " " + AccelRun(chain, 120, 1, 40, 1200));
        chain = Accel("\"chainBoost1\": 0, \"chainStart2\": 5, \"chainBoost2\": 0", "\"chainBoost1\": 1, \"chainStart2\": 5, \"chainBoost2\": 3");
        Expect("accel chain: burst 5 and later get the larger boost", "120 120 240 240 480 480",
            AccelRun(chain, 120, 1, 40, 0) + " " + AccelRun(chain, 120, 1, 40, 400) + " " + AccelRun(chain, 120, 1, 40, 800) + " " + AccelRun(chain, 120, 1, 40, 1200) + " " + AccelRun(chain, 120, 1, 40, 1600) + " " + AccelRun(chain, 120, 1, 40, 2000));
        Expect("accel chain: bursts counted", "6", chain.Bursts.ToString());
        Expect("accel chain: a pause longer than the chain gap restarts the count", "120 1",
            chain.Scale(120, 2000 + 1201).ToString() + " " + chain.Bursts);
        chain = Accel("\"chainBoost1\": 0", "\"chainBoost1\": 1");
        AccelRun(chain, 120, 1, 40, 0);
        AccelRun(chain, 120, 1, 40, 400);
        Expect("accel chain: reversing direction restarts the count", "-120 1", chain.Scale(-120, 800).ToString() + " " + chain.Bursts);
        ExpectThrows("accel: gear out of range", "wheelAccel.gear", Cfg.Replace("\"gear\": 3", "\"gear\": 6"));
        ExpectThrows("accel: gap too small", "wheelAccel.gapMs", Cfg.Replace("\"gapMs\": 100", "\"gapMs\": 5"));
        ExpectThrows("accel: cap below one", "wheelAccel.maxMultiplier", Cfg.Replace("\"maxMultiplier\": 5", "\"maxMultiplier\": 0.5"));

        Pipeline p = NewWith(Cfg, "full");
        Expect("pipeline accel: off passes every tick", "pass pass pass pass",
            Wheel(p, 120, 0) + " " + Wheel(p, 120, 40) + " " + Wheel(p, 120, 80) + " " + Wheel(p, 120, 120));
        p = NewWith(on, "full");
        Expect("pipeline accel: early ticks pass, later ticks are replaced", "pass pass pass wheel(144) wheel(168)",
            Wheel(p, 120, 0) + " " + Wheel(p, 120, 40) + " " + Wheel(p, 120, 80) + " " + Wheel(p, 120, 120) + " " + Wheel(p, 120, 160));
        p = NewWith(on, "full");
        p.Process(new KeyEvent(Vk.LCtrl, true), 0);
        Expect("pipeline accel: Ctrl held (zoom) is never accelerated", "pass pass pass pass pass",
            Wheel(p, 120, 10) + " " + Wheel(p, 120, 50) + " " + Wheel(p, 120, 90) + " " + Wheel(p, 120, 130) + " " + Wheel(p, 120, 170));
        p = NewWith(on, "full");
        MouseEvent injected = MouseEvent.Wheel(120);
        injected.Injected = true;
        string seen = "";
        for (int i = 0; i < 6; i++)
            seen += p.ProcessMouse(injected, i * 40) + " ";
        Expect("pipeline accel: injected events are never accelerated", "pass pass pass pass pass pass ", seen);
    }

    static int Presses(Result r, int vk)
    {
        int n = 0;
        foreach (Out o in r.Emit)
            if (o.Kind == OutKind.Key && o.Vk == vk && o.Down)
                n++;
        return n;
    }

    static string Hold(Pipeline p, int vk, int downs)
    {
        List<string> seen = new List<string>();
        for (int i = 0; i < downs; i++)
        {
            clock += 30;
            Result r = p.Process(new KeyEvent(vk, true), clock);
            seen.Add(!r.Handled ? "pass" : r.Emit.Count == 0 ? "swallow" : Presses(r, vk) > 0 ? "fill" + Presses(r, vk) : "other");
        }
        return string.Join(" ", seen.ToArray());
    }

    static void SeparatorTests()
    {
        Pipeline p = New("full");
        Expect("separator: five repeats still type normally", "pass pass pass pass pass", Hold(p, Vk.OemMinus, 5));
        Expect("separator: the sixth repeat fills the line to 88", "fill83", Hold(p, Vk.OemMinus, 1));
        Expect("separator: later repeats are swallowed", "swallow swallow swallow", Hold(p, Vk.OemMinus, 3));
        Expect("separator: the release passes", "pass", Send(p, "-Minus"));
        Expect("separator: a fresh hold counts from one again", "pass pass pass pass pass fill83", Hold(p, Vk.OemMinus, 6));
        Send(p, "-Minus");

        p = New("full");
        string taps = "";
        for (int i = 0; i < 10; i++)
        {
            taps += Hold(p, Vk.OemMinus, 1) + " ";
            Send(p, "-Minus");
        }
        Expect("separator: tapping ten times never fills", "pass pass pass pass pass pass pass pass pass pass ", taps);

        p = New("full");
        Expect("separator: = fills too", "pass pass pass pass pass fill83", Hold(p, Vk.OemEquals, 6));

        p = New("full");
        Send(p, "+LeftShift");
        Expect("separator: Shift+- (underscore) is left alone", "pass pass pass pass pass pass pass", Hold(p, Vk.OemMinus, 7));

        p = New("full");
        Send(p, "+LeftShift");
        Expect("separator: Shift+8 (asterisk) fills with 8", "pass pass pass pass pass fill83", Hold(p, Vk.Key8, 6));

        p = New("full");
        Expect("separator: 8 without Shift is left alone", "pass pass pass pass pass pass pass", Hold(p, Vk.Key8, 7));

        p = New("full");
        Expect("separator: numpad minus fills when the numpad is a numpad", "pass pass pass pass pass fill83", Hold(p, Vk.Subtract, 6));
        p = New("full");
        Expect("separator: numpad asterisk fills", "pass pass pass pass pass fill83", Hold(p, Vk.Multiply, 6));
        p = New("mini");
        string emulated = Hold(p, Vk.Subtract, 7);
        Expect("separator: numpad minus is PgDn while the numpad stands in for the editing keys", "False", emulated.Contains("fill").ToString());

        p = New("full");
        Send(p, "+LeftCtrl");
        Expect("separator: Ctrl held is left alone", "pass pass pass pass pass pass pass", Hold(p, Vk.OemMinus, 7));

        p = New("full");
        Hold(p, Vk.OemMinus, 3);
        Send(p, "+A");
        Expect("separator: another key restarts the count", "pass pass pass pass pass fill83", Hold(p, Vk.OemMinus, 6));

        string off = Cfg.Replace("\"separator\": { \"enabled\": true", "\"separator\": { \"enabled\": false");
        p = NewWith(off, "full");
        Expect("separator: off passes everything", "pass pass pass pass pass pass pass", Hold(p, Vk.OemMinus, 7));

        string custom = Cfg.Replace("\"count\": 88, \"afterRepeats\": 6", "\"count\": 20, \"afterRepeats\": 4");
        p = NewWith(custom, "full");
        Expect("separator: custom count and trigger", "pass pass pass fill17", Hold(p, Vk.OemMinus, 4));

        Expect("separator: on by default at 88 after 6", "True/88/6",
            Settings.Load(Cfg).Separator.Enabled + "/" + Settings.Load(Cfg).Separator.Count + "/" + Settings.Load(Cfg).Separator.AfterRepeats);
        Expect("separator: written back by ToJson", "True", Settings.Load(Cfg).File.ToJson().Contains("\"separator\"").ToString());
        Expect("separator: a file without the section keeps it on", "True",
            Settings.Load(Cfg.Replace("  \"separator\": { \"enabled\": true, \"count\": 88, \"afterRepeats\": 6 },\n", "")).Separator.Enabled.ToString());
        ExpectThrows("separator: afterRepeats too small", "separator.afterRepeats", Cfg.Replace("\"afterRepeats\": 6", "\"afterRepeats\": 2"));
        ExpectThrows("separator: count not above afterRepeats", "separator.count", Cfg.Replace("\"count\": 88", "\"count\": 6"));
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
        Expect("numpad six keys: off by default", "False/0", s.NumpadSixKeys + "/" + s.Keys.Remap.Count);
        Settings numpadOn = Settings.Load(Cfg.Replace("\"numpadSixKeys\": false", "\"numpadSixKeys\": true"));
        Expect("numpad six keys: on applies the four remaps", "True/4", numpadOn.NumpadSixKeys + "/" + numpadOn.Keys.Remap.Count);
        Expect("numpad six keys: tap-hold is on either way", "1/1", s.Keys.TapHold.Count + "/" + numpadOn.Keys.TapHold.Count);
        Expect("numpad six keys: written back by ToJson", "True", numpadOn.File.ToJson().Contains("\"numpadSixKeys\": true").ToString());
        string oldFormat = "{ \"activeProfile\": \"mini\", \"defaultProfile\": \"full\", "
            + "\"devices\": [ { \"vendorId\": \"046D\", \"profile\": \"mini\" } ], \"profiles\": { "
            + "\"full\": { \"description\": \"f\", \"altChords\": [\"C\", \"X\"], \"remap\": [], \"tapHold\": [] }, "
            + "\"mini\": { \"description\": \"m\", \"altChords\": [\"C\"], \"remap\": [ { \"from\": \"Multiply\", \"to\": \"End\" } ], "
            + "\"tapHold\": [ { \"key\": \"Home\", \"holdMs\": 500, \"tap\": \"Home\", \"hold\": \"PrintScreen\" } ] } } }";
        Settings migrated = Settings.Load(oldFormat);
        Expect("migrate: locked mini profile turns numpad six keys on", "True/1/1", migrated.NumpadSixKeys + "/" + migrated.Keys.Remap.Count + "/" + migrated.Keys.TapHold.Count);
        Expect("migrate: alt chords come from the default profile", "2", migrated.Keys.AltChords.Count.ToString());
        Expect("migrate: flagged as an upgrade", "True", migrated.Upgraded.ToString());
        string migratedJson = migrated.File.ToJson();
        Expect("migrate: written file drops profiles and devices", "False", (migratedJson.Contains("\"profiles\"") || migratedJson.Contains("\"devices\"") || migratedJson.Contains("activeProfile")).ToString());
        Expect("migrate: written file is stable", "False/True", Settings.Load(migratedJson).Upgraded + "/" + Settings.Load(migratedJson).NumpadSixKeys);
        Settings migratedAuto = Settings.Load(oldFormat.Replace("\"activeProfile\": \"mini\"", "\"activeProfile\": \"auto\""));
        Expect("migrate: auto detection turns numpad six keys off but keeps the rules", "False/0/True",
            migratedAuto.NumpadSixKeys + "/" + migratedAuto.Keys.Remap.Count + "/" + migratedAuto.File.ToJson().Contains("Multiply"));
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
        Expect("full: Home waits for the tap-hold decision", "swallow", Send(p, "+Home"));
        Expect("full: Home tap still emits Home", "+Home -Home", Send(p, "-Home"));

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

        p = New("full");
        Send(p, "+Home");
        clock = 500;
        Expect("tap-hold: works with numpad six keys off", "+PrintScreen -PrintScreen", Tick(p));

        p = New("mini");
        Send(p, "+LeftAlt");
        Send(p, "+C");
        Expect("releaseAll releases injected ctrl", "-C -LeftCtrl", Join(p.ReleaseAll()));

        string legacy = Cfg
            .Replace("\"scroll\": {\n    \"enabled\": true,\n    \"stepTicks\": 4,\n    \"pageTicks\": 8,\n    \"burstTaps\": 3,\n    \"burstIntervalMs\": 400,\n    \"holdRepeatsPerSec\": 8,\n    \"apps\": {}\n  },", "\"halfPage\": { \"enabled\": true, \"fraction\": \"1/3\", \"pageTicks\": 9 },");
        legacy = legacy.Substring(0, legacy.IndexOf("\"appAltChords\"")) + "\"unused\": 0\n}";
        Settings old = Settings.Load(legacy);
        Expect("legacy halfPage: flagged as upgrade", "True", old.Upgraded.ToString());
        Expect("legacy halfPage: step from fraction", "3", old.Scroll.StepTicks.ToString());
        Expect("legacy halfPage: page ticks kept", "9", old.Scroll.PageTicks.ToString());
        Expect("legacy: chrome chords get the defaults", "True", (old.AppChordsFor("chrome.exe") != null && old.AppChordsFor("chrome.exe").Contains('W')).ToString());
        string upgraded = old.File.ToJson();
        Expect("legacy: written file has the new sections", "True", (upgraded.Contains("\"scroll\"") && upgraded.Contains("\"appAltChords\"") && !upgraded.Contains("halfPage")).ToString());
        Expect("legacy: upgraded file is no longer an upgrade", "False", Settings.Load(upgraded).Upgraded.ToString());

        WheelFilterTests();
        WheelAccelTests();
        SeparatorTests();
        WheelLogTests();

        ExpectThrows("bad key name", "不認得按鍵名稱 'Foo'", Cfg.Replace("\"to\": \"Home\"", "\"to\": \"Foo\""));
        ExpectThrows("bad chrome chord", "appAltChords.chrome.exe[0]", Cfg.Replace("\"chrome.exe\": [\"W\"", "\"chrome.exe\": [\"Nope\""));
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
