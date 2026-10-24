using System;
using System.IO;

namespace KeyBank.Win
{
    // Pure decision logic: the clocks are injected so a stall can be tested without waiting for one.
    // onStall fires once per stall; a fresh heartbeat re-arms it.
    public sealed class Watchdog
    {
        readonly Func<long> nowMs;
        readonly Func<long> lastBeatMs;
        readonly long limitMs;
        readonly Action onStall;
        bool stalled;

        public Watchdog(Func<long> nowMs, Func<long> lastBeatMs, long limitMs, Action onStall)
        {
            this.nowMs = nowMs;
            this.lastBeatMs = lastBeatMs;
            this.limitMs = limitMs;
            this.onStall = onStall;
        }

        // True only on the check that detects a new stall. Exactly the limit is still healthy.
        public bool Check()
        {
            bool late = nowMs() - lastBeatMs() > limitMs;
            if (!late)
            {
                stalled = false;
                return false;
            }
            if (stalled)
                return false;
            stalled = true;
            onStall();
            return true;
        }
    }

    internal static class CrashLog
    {
        static readonly object gate = new object();

        internal static volatile string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KeyBank.crash.log");

        public static void Write(string line)
        {
            try
            {
                lock (gate)
                    File.AppendAllText(Path, DateTime.Now.ToString("s") + " " + line + Environment.NewLine);
            }
            catch (Exception)
            {
                // The log is a diagnostic aid. It must never become a second failure.
            }
        }
    }
}
