using System;

namespace KeyBank.Core
{
    // Scales wheel deltas while the wheel keeps turning. Two boosts add on top of 1.0 and ramp in linearly:
    // one by how many ticks the burst has accumulated, one by how long the burst has lasted. A pause longer
    // than GapMs or a direction change starts a new burst. It only computes a number, so the hook callback
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

        const double TickDelta = 120.0;

        readonly WheelAccelRuntime cfg;

        int direction;
        long burstStart;
        long lastAt;
        double ticks;

        public WheelAccelerator(WheelAccelRuntime cfg)
        {
            this.cfg = cfg;
        }

        public double Ticks
        {
            get { return ticks; }
        }

        public void Reset()
        {
            direction = 0;
            ticks = 0;
        }

        // Returns the delta to deliver. Equal to the input while the multiplier is 1.
        public int Scale(int delta, long nowMs)
        {
            if (delta == 0)
                return 0;
            int sign = delta > 0 ? 1 : -1;

            if (direction == 0 || sign != direction || nowMs - lastAt > cfg.GapMs)
            {
                direction = sign;
                burstStart = nowMs;
                ticks = 0;
            }
            lastAt = nowMs;
            ticks += Math.Abs(delta) / TickDelta;

            double m = Multiplier(ticks, nowMs - burstStart);
            if (m <= 1.0)
                return delta;
            double scaled = Math.Round(delta * m, MidpointRounding.AwayFromZero);
            if (scaled > int.MaxValue / 2) scaled = int.MaxValue / 2;
            if (scaled < int.MinValue / 2) scaled = int.MinValue / 2;
            return (int)scaled;
        }

        public double Multiplier(double burstTicks, long burstMs)
        {
            double m = 1.0
                + cfg.TickBoost * Ramp(burstTicks - cfg.StartTicks, cfg.RampTicks)
                + cfg.TimeBoost * Ramp(burstMs - cfg.TimeStartMs, cfg.TimeRampMs);
            return Math.Min(m, cfg.MaxMultiplier);
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
