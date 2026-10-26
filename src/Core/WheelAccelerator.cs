using System;

namespace KeyBank.Core
{
    // Scales wheel deltas while the wheel keeps turning. Three boosts add on top of 1.0. Two ramp in linearly
    // inside one burst: by the ticks accumulated and by how long the burst has lasted. The third is a step by
    // how many bursts in a row followed each other: a scroll, a natural pause, another scroll. A pause longer
    // than GapMs or a direction change ends a burst, and a pause longer than ChainGapMs ends the chain. It only computes a number, so the hook callback
    // stays free of I/O and the caller decides how to deliver the scaled delta.
    public sealed class WheelAccelerator
    {
        public const int DefaultGapMs = 100;
        public const double DefaultStartTicks = 3;
        public const double DefaultRampTicks = 5;
        public const double DefaultTickBoost = 1.0;
        public const int DefaultTimeStartMs = 500;
        public const int DefaultTimeRampMs = 1000;
        public const double DefaultTimeBoost = 2.0;
        public const double DefaultMaxMultiplier = 5.0;
        public const int DefaultChainGapMs = 1200;
        public const int DefaultChainStart1 = 3;
        public const int DefaultChainStart2 = 5;

        public const int CustomGear = 0;
        public const int DefaultGear = 3;
        public const int MaxGear = 5;

        const double TickDelta = 120.0;

        // Gear 1 is gentle, gear 5 is aggressive. Measured wheel bursts are about 4 to 7 ticks inside 150 ms,
        // so the stronger gears start boosting at the second tick and reach their cap within one burst.
        static readonly WheelAccelRuntime[] Gears =
        {
            Preset(100, 3, 6, 1, 600, 1500, 1, 3, 1000, 0.5, 1.0),
            Preset(120, 2, 4, 2, 400, 1000, 2, 5, 1200, 1.0, 2.0),
            Preset(150, 1, 3, 4, 300, 800, 3, 8, 1500, 2.0, 4.0),
            Preset(200, 1, 2, 6, 250, 600, 4, 12, 1500, 3.0, 7.0),
            Preset(250, 0, 2, 9, 200, 500, 6, 16, 2000, 4.0, 10.0)
        };

        static WheelAccelRuntime Preset(int gapMs, double startTicks, double rampTicks, double tickBoost,
            int timeStartMs, int timeRampMs, double timeBoost, double max,
            int chainGapMs, double chainBoost1, double chainBoost2)
        {
            WheelAccelRuntime r = new WheelAccelRuntime();
            r.GapMs = gapMs;
            r.StartTicks = startTicks;
            r.RampTicks = rampTicks;
            r.TickBoost = tickBoost;
            r.TimeStartMs = timeStartMs;
            r.TimeRampMs = timeRampMs;
            r.TimeBoost = timeBoost;
            r.MaxMultiplier = max;
            r.ChainGapMs = chainGapMs;
            r.ChainStart1 = DefaultChainStart1;
            r.ChainBoost1 = chainBoost1;
            r.ChainStart2 = DefaultChainStart2;
            r.ChainBoost2 = chainBoost2;
            return r;
        }

        // Copies a preset's numbers into target. Gear 0 or out of range leaves target alone.
        public static bool ApplyGear(int gear, WheelAccelRuntime target)
        {
            if (gear < 1 || gear > Gears.Length)
                return false;
            WheelAccelRuntime g = Gears[gear - 1];
            target.GapMs = g.GapMs;
            target.StartTicks = g.StartTicks;
            target.RampTicks = g.RampTicks;
            target.TickBoost = g.TickBoost;
            target.TimeStartMs = g.TimeStartMs;
            target.TimeRampMs = g.TimeRampMs;
            target.TimeBoost = g.TimeBoost;
            target.MaxMultiplier = g.MaxMultiplier;
            target.ChainGapMs = g.ChainGapMs;
            target.ChainStart1 = g.ChainStart1;
            target.ChainBoost1 = g.ChainBoost1;
            target.ChainStart2 = g.ChainStart2;
            target.ChainBoost2 = g.ChainBoost2;
            return true;
        }

        readonly WheelAccelRuntime cfg;

        int direction;
        long burstStart;
        long lastAt;
        double ticks;
        int bursts;

        public WheelAccelerator(WheelAccelRuntime cfg)
        {
            this.cfg = cfg;
        }

        public double Ticks
        {
            get { return ticks; }
        }

        public int Bursts
        {
            get { return bursts; }
        }

        public void Reset()
        {
            direction = 0;
            ticks = 0;
            bursts = 0;
        }

        // Returns the delta to deliver. Equal to the input while the multiplier is 1.
        public int Scale(int delta, long nowMs)
        {
            if (delta == 0)
                return 0;
            int sign = delta > 0 ? 1 : -1;

            if (direction == 0 || sign != direction || nowMs - lastAt > cfg.GapMs)
            {
                bool chained = direction == sign && nowMs - lastAt <= cfg.ChainGapMs;
                bursts = chained ? bursts + 1 : 1;
                direction = sign;
                burstStart = nowMs;
                ticks = 0;
            }
            lastAt = nowMs;
            ticks += Math.Abs(delta) / TickDelta;

            double m = Multiplier(ticks, nowMs - burstStart, bursts);
            if (m <= 1.0)
                return delta;
            double scaled = Math.Round(delta * m, MidpointRounding.AwayFromZero);
            if (scaled > int.MaxValue / 2) scaled = int.MaxValue / 2;
            if (scaled < int.MinValue / 2) scaled = int.MinValue / 2;
            return (int)scaled;
        }

        public double Multiplier(double burstTicks, long burstMs, int chainBursts)
        {
            double m = 1.0
                + cfg.TickBoost * Ramp(burstTicks - cfg.StartTicks, cfg.RampTicks)
                + cfg.TimeBoost * Ramp(burstMs - cfg.TimeStartMs, cfg.TimeRampMs)
                + ChainBoost(chainBursts);
            return Math.Min(m, cfg.MaxMultiplier);
        }

        double ChainBoost(int chainBursts)
        {
            if (chainBursts >= cfg.ChainStart2)
                return cfg.ChainBoost2;
            if (chainBursts >= cfg.ChainStart1)
                return cfg.ChainBoost1;
            return 0;
        }

        static double Ramp(double over, double span)
        {
            if (over <= 0)
                return 0;
            if (span <= 0 || over >= span)
                return 1;
            return over / span;
        }
    }
}
