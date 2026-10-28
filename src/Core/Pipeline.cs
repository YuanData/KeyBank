using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KeyBank.Core
{
    public struct KeyEvent
    {
        public readonly int Vk;
        public readonly bool Down;

        public KeyEvent(int vk, bool down)
        {
            Vk = vk;
            Down = down;
        }
    }

    public enum MouseButton { Left, Right, Middle, X1, X2 }

    public enum MouseKind { Button, Wheel }

    public struct MouseEvent
    {
        public MouseKind Kind;
        public MouseButton Button;
        public bool Down;
        public int Delta;
        // Set by the hook for input some other program injected. KeyBank's own events never reach the pipeline.
        public bool Injected;
        // The mouse's own event time in ms (MSLLHOOKSTRUCT.time), for diagnostics only.
        public long HardwareMs;

        public static MouseEvent Click(MouseButton button, bool down)
        {
            MouseEvent e = new MouseEvent();
            e.Kind = MouseKind.Button;
            e.Button = button;
            e.Down = down;
            return e;
        }

        public static MouseEvent Wheel(int delta)
        {
            MouseEvent e = new MouseEvent();
            e.Kind = MouseKind.Wheel;
            e.Delta = delta;
            return e;
        }
    }

    public enum OutKind { Key, Scroll, MouseButton, MouseWheel }

    public struct Out
    {
        public OutKind Kind;
        public int Vk;
        public bool Down;
        public int Direction;
        public double Ticks;
        public MouseButton Button;
        public int Delta;

        public static Out Key(int vk, bool down)
        {
            Out o = new Out();
            o.Kind = OutKind.Key;
            o.Vk = vk;
            o.Down = down;
            return o;
        }

        public static Out Scroll(int direction, double ticks)
        {
            Out o = new Out();
            o.Kind = OutKind.Scroll;
            o.Direction = direction;
            o.Ticks = ticks;
            return o;
        }

        public static Out Mouse(MouseButton button, bool down)
        {
            Out o = new Out();
            o.Kind = OutKind.MouseButton;
            o.Button = button;
            o.Down = down;
            return o;
        }

        public static Out Wheel(int delta)
        {
            Out o = new Out();
            o.Kind = OutKind.MouseWheel;
            o.Delta = delta;
            return o;
        }

        public override string ToString()
        {
            switch (Kind)
            {
                case OutKind.Scroll:
                    return (Direction > 0 ? "scrollUp(" : "scrollDown(") + Ticks.ToString(CultureInfo.InvariantCulture) + ")";
                case OutKind.MouseButton:
                    return (Down ? "+" : "-") + Button + "Btn";
                case OutKind.MouseWheel:
                    return "wheel(" + Delta + ")";
                default:
                    return (Down ? "+" : "-") + KeyNames.NameOf(Vk);
            }
        }
    }

    public sealed class Result
    {
        public readonly bool Handled;
        public readonly List<Out> Emit;

        public static readonly Result Pass = new Result(false, new List<Out>());

        Result(bool handled, List<Out> emit)
        {
            Handled = handled;
            Emit = emit;
        }

        public static Result Swallow()
        {
            return new Result(true, new List<Out>());
        }

        public static Result Of(params Out[] outs)
        {
            return new Result(true, new List<Out>(outs));
        }

        public static Result Of(List<Out> outs)
        {
            return new Result(true, outs);
        }

        public override string ToString()
        {
            if (!Handled)
                return "pass";
            if (Emit.Count == 0)
                return "swallow";
            return string.Join(" ", Emit.ConvertAll<string>(delegate(Out o) { return o.ToString(); }).ToArray());
        }
    }

    public sealed class Pipeline
    {
        enum HoldState { Idle, Pending, Resolved }
        enum AltState { None, Down, Chord }

        readonly Settings settings;
        readonly Func<string> foreground;
        readonly WheelReversalFilter wheelFilter;
        readonly WheelAccelerator wheelAccel;
        readonly KeyRules keys;
        readonly HashSet<int> mods = new HashSet<int>();
        readonly HashSet<int> chordHeld = new HashSet<int>();
        readonly HashSet<MouseButton> mouseHeld = new HashSet<MouseButton>();

        HoldState holdState = HoldState.Idle;
        int holdKey;
        long holdDownAt;
        TapHoldRuntime holdRule;
        AltState alt = AltState.None;

        readonly bool[] pageKeyDown = new bool[2];
        int burstDir;
        int burstCount;
        long lastPressAt = long.MinValue / 2;
        long lastScrollAt = long.MinValue / 2;

        public Pipeline(Settings settings)
            : this(settings, null)
        {
        }

        public Pipeline(Settings settings, Func<string> foregroundApp)
        {
            this.settings = settings;
            foreground = foregroundApp;
            wheelFilter = new WheelReversalFilter(settings.WheelFilter.WindowMs, settings.WheelFilter.ThresholdDelta);
            wheelAccel = new WheelAccelerator(settings.WheelAccel);
            keys = settings.Keys;
        }

        public List<Out> ReleaseAll()
        {
            List<Out> outs = new List<Out>();
            foreach (int vk in chordHeld)
                outs.Add(Out.Key(vk, false));
            foreach (MouseButton b in mouseHeld)
                outs.Add(Out.Mouse(b, false));
            if (alt == AltState.Chord)
                outs.Add(Out.Key(Vk.LCtrl, false));
            chordHeld.Clear();
            mouseHeld.Clear();
            alt = AltState.None;
            holdState = HoldState.Idle;
            pageKeyDown[0] = false;
            pageKeyDown[1] = false;
            burstCount = 0;
            return outs;
        }

        public Result Process(KeyEvent e, long now)
        {
            if (Vk.IsModifier(e.Vk))
            {
                if (e.Down)
                    mods.Add(e.Vk);
                else
                    mods.Remove(e.Vk);
            }

            List<Out> pre = null;
            if (e.Down && holdState == HoldState.Pending && e.Vk != holdKey)
                pre = ResolveTap();

            Result r = Stage(e, now);
            if (pre == null)
                return r;

            List<Out> merged = new List<Out>(pre);
            if (r.Handled)
                merged.AddRange(r.Emit);
            else
                merged.Add(Out.Key(e.Vk, e.Down));
            return Result.Of(merged);
        }

        // now is the same millisecond clock the keyboard path uses. Only the wheel filter reads it.
        public Result ProcessMouse(MouseEvent e, long now)
        {
            if (e.Kind == MouseKind.Wheel && !e.Injected && settings.WheelFilter.Enabled
                && wheelFilter.ShouldDrop(e.Delta, now))
                return Result.Swallow();

            if (e.Kind == MouseKind.Button && !e.Down)
            {
                if (mouseHeld.Remove(e.Button))
                    return Result.Of(Out.Mouse(e.Button, false));
                return Result.Pass;
            }

            if (!settings.AltMouse || alt == AltState.None || ControlOrWinHeld())
                return AccelerateWheel(e, now);

            List<Out> outs = new List<Out>();
            if (alt == AltState.Down)
            {
                outs.Add(Out.Key(Vk.LCtrl, true));
                outs.Add(Out.Key(Vk.LAlt, false));
                alt = AltState.Chord;
            }

            if (e.Kind == MouseKind.Button)
            {
                outs.Add(Out.Mouse(e.Button, true));
                mouseHeld.Add(e.Button);
            }
            else
            {
                outs.Add(Out.Wheel(e.Delta));
            }
            return Result.Of(outs);
        }

        // Ctrl and Win held means zoom or a shortcut, and an Alt chord already turned the wheel into Ctrl + wheel,
        // so neither is a scroll to speed up. A multiplier of 1 passes the physical event through untouched.
        Result AccelerateWheel(MouseEvent e, long now)
        {
            if (e.Kind != MouseKind.Wheel || e.Injected || !settings.WheelAccel.Enabled || ControlOrWinHeld())
                return Result.Pass;
            int scaled = wheelAccel.Scale(e.Delta, now);
            if (scaled == e.Delta)
                return Result.Pass;
            return Result.Of(Out.Wheel(scaled));
        }

        public List<Out> Tick(long now)
        {
            List<Out> outs = new List<Out>();
            if (holdState == HoldState.Pending && now - holdDownAt >= holdRule.HoldMs)
            {
                holdState = HoldState.Resolved;
                outs.Add(Out.Key(holdRule.Hold, true));
                outs.Add(Out.Key(holdRule.Hold, false));
            }
            return outs;
        }

        bool ControlOrWinHeld()
        {
            return mods.Contains(Vk.LCtrl) || mods.Contains(Vk.RCtrl)
                || mods.Contains(Vk.LWin) || mods.Contains(Vk.RWin);
        }

        Result Stage(KeyEvent e, long now)
        {
            Result r = TapHold(e, now);
            if (r != null)
                return r;

            int vk = e.Vk;
            int mapped;
            if (keys.Remap.TryGetValue(vk, out mapped))
                vk = mapped;
            KeyEvent logical = new KeyEvent(vk, e.Down);

            r = PageScroll(logical, now);
            if (r != null)
                return r;

            r = AltChord(logical);
            if (r != null)
                return r;

            if (vk != e.Vk)
                return Result.Of(Out.Key(vk, e.Down));
            return Result.Pass;
        }

        List<Out> ResolveTap()
        {
            holdState = HoldState.Resolved;
            List<Out> outs = new List<Out>();
            outs.Add(Out.Key(holdRule.Tap, true));
            outs.Add(Out.Key(holdRule.Tap, false));
            return outs;
        }

        Result TapHold(KeyEvent e, long now)
        {
            TapHoldRuntime rule;
            if (!keys.TapHold.TryGetValue(e.Vk, out rule))
                return null;

            if (e.Down)
            {
                if (holdState == HoldState.Idle)
                {
                    if (mods.Count > 0)
                        return null;
                    holdState = HoldState.Pending;
                    holdKey = e.Vk;
                    holdDownAt = now;
                    holdRule = rule;
                    return Result.Swallow();
                }
                return holdKey == e.Vk ? Result.Swallow() : null;
            }

            if (holdState == HoldState.Idle || holdKey != e.Vk)
                return null;

            HoldState was = holdState;
            holdState = HoldState.Idle;
            if (was == HoldState.Resolved)
                return Result.Swallow();

            if (now - holdDownAt >= rule.HoldMs)
                return Result.Of(Out.Key(rule.Hold, true), Out.Key(rule.Hold, false));
            return Result.Of(Out.Key(rule.Tap, true), Out.Key(rule.Tap, false));
        }

        // A single press scrolls StepTicks. From the BurstTaps-th press within BurstIntervalMs of the
        // previous one, in the same direction, it scrolls PageTicks. Auto-repeat counts as presses and
        // is rate limited so holding the key stays readable.
        Result PageScroll(KeyEvent e, long now)
        {
            ScrollRuntime sc = settings.Scroll;
            if (!sc.Enabled || mods.Count > 0)
                return null;
            if (e.Vk != Vk.PageUp && e.Vk != Vk.PageDown)
                return null;

            int dir = e.Vk == Vk.PageUp ? 1 : -1;
            int slot = dir > 0 ? 0 : 1;
            if (!e.Down)
            {
                pageKeyDown[slot] = false;
                return Result.Swallow();
            }

            bool repeat = pageKeyDown[slot];
            pageKeyDown[slot] = true;

            if (burstDir != dir || now - lastPressAt > sc.BurstIntervalMs)
                burstCount = 0;
            burstCount++;
            burstDir = dir;
            lastPressAt = now;

            if (repeat && now - lastScrollAt < 1000 / sc.HoldRepeatsPerSec)
                return Result.Swallow();

            double step;
            double page;
            sc.TicksFor(foreground == null ? null : foreground(), out step, out page);
            lastScrollAt = now;
            return Result.Of(Out.Scroll(dir, burstCount >= sc.BurstTaps ? page : step));
        }

        bool AppChord(int vk)
        {
            if (foreground == null || settings.AppChords.Count == 0)
                return false;
            HashSet<int> keys = settings.AppChordsFor(foreground());
            return keys != null && keys.Contains(vk);
        }

        Result AltChord(KeyEvent e)
        {
            if (e.Vk == Vk.LAlt)
            {
                if (e.Down)
                {
                    if (alt == AltState.Chord)
                        return Result.Swallow();
                    alt = AltState.Down;
                    return null;
                }

                AltState was = alt;
                alt = AltState.None;
                if (was == AltState.Chord)
                    return Result.Of(Out.Key(Vk.LCtrl, false));
                return null;
            }

            bool isChordKey = e.Down && alt != AltState.None && !ControlOrWinHeld()
                && (keys.AltChords.Contains(e.Vk) || AppChord(e.Vk));

            if (isChordKey)
            {
                List<Out> outs = new List<Out>();
                if (alt == AltState.Down)
                {
                    outs.Add(Out.Key(Vk.LCtrl, true));
                    outs.Add(Out.Key(Vk.LAlt, false));
                    alt = AltState.Chord;
                }
                outs.Add(Out.Key(e.Vk, true));
                chordHeld.Add(e.Vk);
                return Result.Of(outs);
            }

            if (!e.Down && chordHeld.Remove(e.Vk))
                return Result.Of(Out.Key(e.Vk, false));

            if (e.Down && alt == AltState.Chord && !Vk.IsModifier(e.Vk))
            {
                alt = AltState.Down;
                return Result.Of(Out.Key(Vk.LCtrl, false), Out.Key(Vk.LAlt, true), Out.Key(e.Vk, true));
            }

            return null;
        }
    }
}
