using System;

namespace KeyBank.Core
{
    // Drops the small opposite-direction wheel events some mice emit while scrolling in one direction.
    // It never alters an event: each one is either passed untouched or dropped, never held and replayed,
    // so it adds no latency and needs no synthetic input.
    public sealed class WheelReversalFilter
    {
        public const int DefaultWindowMs = 80;
        public const int DefaultThresholdDelta = 120;

        readonly int windowMs;
        readonly int thresholdDelta;

        int primary;
        long lastValidAt;
        int reverseAccum;

        public WheelReversalFilter(int windowMs, int thresholdDelta)
        {
            this.windowMs = windowMs;
            this.thresholdDelta = thresholdDelta;
        }

        public int WindowMs
        {
            get { return windowMs; }
        }

        public int ThresholdDelta
        {
            get { return thresholdDelta; }
        }

        // +1 or -1 while a direction is established, 0 after a reset.
        public int PrimaryDirection
        {
            get { return primary; }
        }

        public int ReverseAccumulated
        {
            get { return reverseAccum; }
        }

        public void Reset()
        {
            primary = 0;
            reverseAccum = 0;
        }

        // True means drop the event. Only events that are passed (valid) move the clock and the direction.
        public bool ShouldDrop(int delta, long nowMs)
        {
            if (delta == 0)
                return false;
            int sign = delta > 0 ? 1 : -1;

            if (primary != 0 && nowMs - lastValidAt > windowMs)
                Reset();

            if (primary == 0 || sign == primary)
            {
                primary = sign;
                lastValidAt = nowMs;
                reverseAccum = 0;
                return false;
            }

            reverseAccum += Math.Abs(delta);
            if (reverseAccum <= thresholdDelta)
                return true;

            primary = sign;
            lastValidAt = nowMs;
            reverseAccum = 0;
            return false;
        }
    }
}
