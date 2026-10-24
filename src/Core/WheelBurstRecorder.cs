using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KeyBank.Core
{
    // Folds a stream of wheel events into one log line per scroll burst. It does no I/O and reads no clock:
    // the caller passes the arrival time (its own monotonic clock), the mouse's hardware timestamp and the
    // wall-clock text, so a burst can be replayed in a test.
    // Comparing the two clocks tells apart a mouse that produced an odd rhythm from a hook that delayed events:
    // regular hardware gaps with arrival gaps that collapse to 0 ms mean events were bunched up on the way.
    public sealed class WheelBurstRecorder
    {
        const int TailLength = 6;
        const int MaxReverseListed = 8;
        const int ClumpArrivalGapMs = 2;
        const int ClumpHardwareGapMs = 6;

        struct Reverse
        {
            public int Delta;
            public long OffsetMs;
            public bool Dropped;
        }

        readonly int burstGapMs;

        bool active;
        string app;
        string wallStart;
        int firstSign;
        int count;
        long sum;
        long firstArrival;
        long lastArrival;
        long firstHardware;
        long lastHardware;
        long maxHardwareGap;
        int clumped;
        readonly Queue<long> tailArrival = new Queue<long>();
        readonly Queue<long> tailHardware = new Queue<long>();
        readonly List<Reverse> reverses = new List<Reverse>();
        int reverseCount;

        public WheelBurstRecorder(int burstGapMs)
        {
            this.burstGapMs = burstGapMs;
        }

        public bool Active
        {
            get { return active; }
        }

        // Returns the finished line of the previous burst when this event starts a new one, otherwise null.
        public string Add(long arrivalMs, long hardwareMs, int delta, bool dropped, string appName, string wallClock)
        {
            if (delta == 0)
                return null;

            string finished = null;
            if (active && arrivalMs - lastArrival > burstGapMs)
                finished = Finish();

            int sign = delta > 0 ? 1 : -1;
            if (!active)
            {
                active = true;
                app = appName ?? "?";
                wallStart = wallClock;
                firstSign = sign;
                count = 0;
                sum = 0;
                firstArrival = arrivalMs;
                firstHardware = hardwareMs;
                maxHardwareGap = 0;
                clumped = 0;
                reverseCount = 0;
                tailArrival.Clear();
                tailHardware.Clear();
                reverses.Clear();
            }
            else
            {
                long arrivalGap = arrivalMs - lastArrival;
                long hardwareGap = hardwareMs - lastHardware;
                if (hardwareGap > maxHardwareGap)
                    maxHardwareGap = hardwareGap;
                if (arrivalGap <= ClumpArrivalGapMs && hardwareGap >= ClumpHardwareGapMs)
                    clumped++;
                Push(tailArrival, arrivalGap);
                Push(tailHardware, hardwareGap);
            }

            count++;
            sum += delta;
            lastArrival = arrivalMs;
            lastHardware = hardwareMs;

            if (sign != firstSign)
            {
                reverseCount++;
                if (reverses.Count < MaxReverseListed)
                {
                    Reverse r = new Reverse();
                    r.Delta = delta;
                    r.OffsetMs = arrivalMs - firstArrival;
                    r.Dropped = dropped;
                    reverses.Add(r);
                }
            }
            return finished;
        }

        // Call from a timer. Returns the finished line once the wheel has been quiet for the burst gap.
        public string Poll(long nowMs)
        {
            if (active && nowMs - lastArrival > burstGapMs)
                return Finish();
            return null;
        }

        // Ends the current burst immediately (used on shutdown).
        public string Flush()
        {
            return active ? Finish() : null;
        }

        public static string MarkLine(string wallClock, string appName)
        {
            return wallClock + " MARK app=" + (appName ?? "?");
        }

        static void Push(Queue<long> q, long value)
        {
            q.Enqueue(value);
            while (q.Count > TailLength)
                q.Dequeue();
        }

        static string Join(Queue<long> q)
        {
            StringBuilder sb = new StringBuilder();
            foreach (long v in q)
            {
                if (sb.Length > 0)
                    sb.Append(',');
                sb.Append(v.ToString(CultureInfo.InvariantCulture));
            }
            return sb.Length == 0 ? "-" : sb.ToString();
        }

        string Finish()
        {
            active = false;
            StringBuilder sb = new StringBuilder();
            sb.Append(wallStart).Append(" app=").Append(app)
              .Append(" dir=").Append(reverseCount > 0 ? "mixed" : (firstSign < 0 ? "down" : "up"))
              .Append(" n=").Append(count)
              .Append(" sum=").Append(sum.ToString(CultureInfo.InvariantCulture))
              .Append(" arr=").Append((lastArrival - firstArrival).ToString(CultureInfo.InvariantCulture)).Append("ms")
              .Append(" hw=").Append((lastHardware - firstHardware).ToString(CultureInfo.InvariantCulture)).Append("ms")
              .Append(" maxHwGap=").Append(maxHardwareGap.ToString(CultureInfo.InvariantCulture)).Append("ms")
              .Append(" tailHw=").Append(Join(tailHardware))
              .Append(" tailArr=").Append(Join(tailArrival))
              .Append(" clump=").Append(clumped)
              .Append(" rev=").Append(reverseCount);
            foreach (Reverse r in reverses)
                sb.Append(' ').Append(r.Delta > 0 ? "+" : "").Append(r.Delta)
                  .Append('@').Append(r.OffsetMs.ToString(CultureInfo.InvariantCulture)).Append("ms:")
                  .Append(r.Dropped ? "drop" : "pass");
            return sb.ToString();
        }
    }
}
