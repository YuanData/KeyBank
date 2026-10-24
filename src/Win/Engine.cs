using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using KeyBank.Core;

namespace KeyBank.Win
{
    internal sealed class EngineOptions
    {
        public Func<List<Out>, int> Send = Injector.Send;
        public Action<int> Exit = null;
        public int StallLimitMs = 1500;
        public int WatchdogPeriodMs = 250;
        public int SlowCallbackMs = 250;
    }

    // All input work (hooks, Raw Input, Pipeline, SendInput, the tick) lives on one background thread with
    // its own message loop, so the tray can no longer starve the hooks and a stuck hook cannot freeze the tray.
    // The UI thread only reads volatile snapshots and posts messages.
    public sealed class Engine : IEngineHost, IDisposable
    {
        const int TickMs = 25;
        const int TraceFlushTicks = 8;
        const int StartTimeoutMs = 15000;
        const uint MsgSettings = Native.WM_APP + 1;
        const uint MsgWork = Native.WM_APP + 2;

        struct TraceEntry
        {
            public long At;
            public int Vk;
            public bool Down;
            public Result Result;
            public string Vendor;
            public string Note;
            public bool IsMouse;
            public MouseEvent Mouse;
        }

        readonly EngineOptions opt;
        readonly SynchronizationContext ui;
        readonly ConfigStore store;
        readonly KeyboardHook keyboard;
        readonly MouseHook mouse;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly bool trace;
        readonly ConcurrentQueue<TraceEntry> traceQueue = new ConcurrentQueue<TraceEntry>();
        readonly ConcurrentQueue<Action> work = new ConcurrentQueue<Action>();
        readonly string tracePath = Path.Combine(Path.GetTempPath(), "KeyBank.trace.log");

        readonly Thread hookThread;
        readonly ManualResetEvent ready = new ManualResetEvent(false);
        readonly ManualResetEvent exited = new ManualResetEvent(false);
        readonly ManualResetEvent stopDog = new ManualResetEvent(false);
        readonly object shutdownGate = new object();
        Thread watchdogThread;

        // Hook thread only.
        ForegroundApp foreground;
        Settings cur;
        UIntPtr timerId = UIntPtr.Zero;
        int appliedStamp;
        int ticks;
        long lastSlowLogMs = long.MinValue / 2;
        long lastLoopErrorMs = long.MinValue / 2;

        // Written by the hook thread, read from any thread.
        volatile Pipeline pipeline;
        volatile DeviceTracker tracker;
        volatile string activeProfile = "";
        volatile int lastVk = -1;
        volatile Exception startError;
        volatile bool prewarmed;
        long prewarmMs = -1;
        long beatMs;
        volatile int hookThreadId;

        // Written by the UI thread.
        int settingsStamp;
        volatile bool shutdownStarted;
        bool shutdownDone;

        public event EventHandler Changed;

        public Engine(string configPathOrNull, bool trace)
            : this(configPathOrNull, trace, new EngineOptions())
        {
        }

        // Must be created on the UI thread: the captured context is where Changed is raised.
        internal Engine(string configPathOrNull, bool trace, EngineOptions options)
        {
            this.trace = trace;
            opt = options;
            store = new ConfigStore(configPathOrNull);
            ui = store.Ui;
            store.Changed += OnStoreChanged;

            if (trace)
                File.WriteAllText(tracePath, "KeyBank trace " + DateTime.Now.ToString("s") + Environment.NewLine);

            keyboard = new KeyboardHook(OnKey);
            mouse = new MouseHook(OnMouse);

            Beat();
            hookThread = new Thread(HookMain);
            hookThread.IsBackground = true;
            hookThread.Name = "KeyBank.Hooks";
            hookThread.Start();

            if (!ready.WaitOne(StartTimeoutMs))
            {
                Shutdown();
                throw new Win32Exception(0, "KeyBank 輸入執行緒沒有在時限內啟動。");
            }

            Exception failure = startError;
            if (failure != null)
            {
                hookThread.Join(2000);
                store.Dispose();
                shutdownDone = true;
                Win32Exception w = failure as Win32Exception;
                if (w != null)
                    throw new Win32Exception(w.NativeErrorCode, w.Message);
                throw new Win32Exception("KeyBank 輸入執行緒啟動失敗：" + failure.Message, failure);
            }

            watchdogThread = new Thread(WatchdogMain);
            watchdogThread.IsBackground = true;
            watchdogThread.Name = "KeyBank.Watchdog";
            watchdogThread.Start();
        }

        public Settings Settings
        {
            get { return store.Settings; }
        }

        public string ConfigPath
        {
            get { return store.Path; }
        }

        public string LastError
        {
            get { return store.LastError; }
        }

        public string ActiveProfileName
        {
            get { return activeProfile; }
        }

        public string LastKeyText
        {
            get
            {
                int vk = lastVk;
                if (vk < 0)
                    return "";
                DeviceTracker t = tracker;
                string vendor = t == null ? null : t.CurrentVendor;
                string from = vendor == null
                    ? "內建/未知鍵盤"
                    : vendor + " " + ProfileDescription();
                return KeyNames.NameOf(vk) + " 來自 " + from;
            }
        }

        internal bool MouseHookInstalled
        {
            get { return mouse.Installed; }
        }

        internal bool KeyboardHookInstalled
        {
            get { return keyboard.Installed; }
        }

        internal bool HookThreadAlive
        {
            get { return hookThread.IsAlive; }
        }

        internal bool WatchdogThreadAlive
        {
            get { Thread t = watchdogThread; return t != null && t.IsAlive; }
        }

        internal bool Prewarmed
        {
            get { return prewarmed; }
        }

        internal long PrewarmMs
        {
            get { return Interlocked.Read(ref prewarmMs); }
        }

        internal int HookThreadManagedId
        {
            get { return hookThread.ManagedThreadId; }
        }

        public void Mutate(Action<ConfigFile> change)
        {
            store.Mutate(change);
        }

        public static string DescribeKeyboards()
        {
            return DeviceTracker.Describe();
        }

        string ProfileDescription()
        {
            ProfileRuntime p;
            string name = activeProfile;
            return store.Settings.Profiles.TryGetValue(name, out p) ? p.Description : name;
        }

        static long NowMs()
        {
            return Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;
        }

        void Beat()
        {
            Interlocked.Exchange(ref beatMs, NowMs());
        }

        long LastBeat()
        {
            return Interlocked.Read(ref beatMs);
        }

        int Send(List<Out> outs)
        {
            if (outs == null || outs.Count == 0)
                return 0;
            return opt.Send(outs);
        }

        // ---- hook thread ----

        void HookMain()
        {
            bool signalled = false;
            try
            {
                MSG probe;
                Native.PeekMessage(out probe, IntPtr.Zero, 0, 0, Native.PM_NOREMOVE);
                hookThreadId = (int)Native.GetCurrentThreadId();

                foreground = new ForegroundApp();
                DeviceTracker t = new DeviceTracker();
                t.VendorChanged += OnVendorChanged;
                tracker = t;

                appliedStamp = Volatile.Read(ref settingsStamp);
                cur = store.Settings;
                pipeline = NewPipeline();
                activeProfile = pipeline.ProfileName;

                Prewarm();
                Beat();

                if (!keyboard.Install())
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "無法安裝鍵盤攔截 (WH_KEYBOARD_LL)。");
                ApplyMouseHook();

                timerId = Native.SetTimer(IntPtr.Zero, UIntPtr.Zero, TickMs, IntPtr.Zero);
                if (timerId == UIntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "無法建立輸入執行緒的計時器。");

                signalled = true;
                ready.Set();
                RunLoop();
            }
            catch (Exception ex)
            {
                if (!signalled)
                {
                    startError = ex;
                    ready.Set();
                }
                else
                {
                    CrashLog.Write("hook thread died: " + ex);
                }
            }
            finally
            {
                Cleanup();
                exited.Set();
            }
        }

        void RunLoop()
        {
            MSG m;
            while (!shutdownStarted)
            {
                int r = Native.GetMessage(out m, IntPtr.Zero, 0, 0);
                if (r == 0)
                    break;
                if (r < 0)
                {
                    CrashLog.Write("GetMessage failed, error " + Marshal.GetLastWin32Error());
                    break;
                }

                try
                {
                    if (m.hwnd == IntPtr.Zero)
                    {
                        if (m.message == Native.WM_TIMER)
                        {
                            OnTick();
                            continue;
                        }
                        if (m.message == MsgSettings || m.message == MsgWork)
                        {
                            Beat();
                            DrainWork();
                            ApplyPendingSettings();
                            continue;
                        }
                    }
                    Native.DispatchMessage(ref m);
                }
                catch (Exception ex)
                {
                    long now = NowMs();
                    if (now - lastLoopErrorMs > 5000)
                    {
                        lastLoopErrorMs = now;
                        CrashLog.Write("hook loop error: " + ex);
                    }
                }
            }
        }

        void Cleanup()
        {
            try
            {
                if (timerId != UIntPtr.Zero)
                    Native.KillTimer(IntPtr.Zero, timerId);
            }
            catch (Exception)
            {
            }
            keyboard.Uninstall();
            mouse.Uninstall();
            try
            {
                DeviceTracker t = tracker;
                if (t != null)
                    t.Dispose();
            }
            catch (Exception)
            {
            }
            try
            {
                Pipeline p = pipeline;
                if (p != null)
                    Send(p.ReleaseAll());
            }
            catch (Exception)
            {
            }
            if (trace)
                FlushTrace();
        }

        Pipeline NewPipeline()
        {
            return new Pipeline(cur, cur.ResolveProfile(tracker.CurrentVendor), foreground.CurrentExeName);
        }

        // A hook callback that runs past Windows' limit can get the hook dropped, and the first call of every
        // method pays JIT time. Run the whole path once on dummy data, with no SendInput and no hook installed.
        void Prewarm()
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                keyboard.WarmUp();
                mouse.WarmUp();

                Settings s = cur;
                string name = s.ResolveProfile(null);
                s.Profile(name);
                Pipeline p = new Pipeline(s, name, foreground.CurrentExeName);
                List<Out> built = new List<Out>();
                long t = 0;
                int[] vks = { 'A', Vk.LAlt, 'A', Vk.PageDown, Vk.Home };
                for (int i = 0; i < vks.Length; i++)
                {
                    for (int d = 1; d >= 0; d--)
                    {
                        Result r = p.Process(new KeyEvent(vks[i], d == 1), t += 10);
                        built.AddRange(r.Emit);
                        r.ToString();
                    }
                }
                p.Process(new KeyEvent(Vk.LAlt, true), t += 10);
                Result m = p.ProcessMouse(MouseEvent.Click(MouseButton.Left, true));
                built.AddRange(m.Emit);
                built.AddRange(p.ProcessMouse(MouseEvent.Click(MouseButton.Left, false)).Emit);
                built.AddRange(p.ProcessMouse(MouseEvent.Wheel(-120)).Emit);
                built.AddRange(p.Tick(t + 1000));
                built.AddRange(p.SetProfile(name));
                built.AddRange(p.ReleaseAll());

                built.Add(Out.Key(Vk.PageDown, true));
                built.Add(Out.Key('C', false));
                built.Add(Out.Scroll(-1, 4));
                built.Add(Out.Mouse(MouseButton.Left, true));
                built.Add(Out.Mouse(MouseButton.X1, false));
                built.Add(Out.Wheel(120));
                Injector.Build(built);
                for (int i = 0; i < built.Count; i++)
                    built[i].ToString();

                s.ExcludeApps.Contains("x.exe");
                foreground.IsExcluded(s.ExcludeApps);
                s.AppChordsFor("x.exe");
                double step, page;
                s.Scroll.TicksFor("x.exe", out step, out page);
                KeyNames.NameOf(0x41);
                prewarmed = true;
            }
            catch (Exception ex)
            {
                CrashLog.Write("prewarm failed: " + ex);
            }
            Interlocked.Exchange(ref prewarmMs, sw.ElapsedMilliseconds);
        }

        void ApplyMouseHook()
        {
            bool want = cur.AltMouse;
            if (want && !mouse.Installed)
            {
                if (!mouse.Install())
                    CrashLog.Write("mouse hook install failed, error " + Marshal.GetLastWin32Error());
            }
            else if (!want && mouse.Installed)
            {
                mouse.Uninstall();
            }
        }

        void ApplyPendingSettings()
        {
            int stamp = Volatile.Read(ref settingsStamp);
            if (stamp == appliedStamp)
                return;
            appliedStamp = stamp;

            Settings next = store.Settings;
            if (!object.ReferenceEquals(next, cur))
            {
                foreground.Reset();
                Send(pipeline.ReleaseAll());
                cur = next;
                pipeline = NewPipeline();
                activeProfile = pipeline.ProfileName;
                ApplyMouseHook();
            }
            RaiseChanged();
        }

        void DrainWork()
        {
            Action a;
            while (work.TryDequeue(out a))
            {
                try
                {
                    a();
                }
                catch (Exception)
                {
                }
            }
        }

        void EndCallback(long startedAt, string kind)
        {
            Beat();
            long ms = (Stopwatch.GetTimestamp() - startedAt) * 1000 / Stopwatch.Frequency;
            if (ms <= opt.SlowCallbackMs)
                return;
            long now = NowMs();
            if (now - lastSlowLogMs < 5000)
                return;
            lastSlowLogMs = now;
            CrashLog.Write("slow " + kind + " callback: " + ms + " ms");
        }

        bool OnKey(int vk, bool down)
        {
            long t0 = Stopwatch.GetTimestamp();
            Beat();
            try
            {
                return HandleKey(vk, down);
            }
            finally
            {
                EndCallback(t0, "key");
            }
        }

        bool OnMouse(MouseEvent e)
        {
            long t0 = Stopwatch.GetTimestamp();
            Beat();
            try
            {
                return HandleMouse(e);
            }
            finally
            {
                EndCallback(t0, "mouse");
            }
        }

        // Runs inside the hook callback. Only the pipeline, one SendInput and a queue push happen here.
        bool HandleKey(int vk, bool down)
        {
            Settings s = cur;
            if (!s.Enabled)
                return false;
            if (s.ExcludeApps.Count > 0 && foreground.IsExcluded(s.ExcludeApps))
                return false;

            if (down)
                lastVk = vk;
            long now = clock.ElapsedMilliseconds;
            Result r = pipeline.Process(new KeyEvent(vk, down), now);

            if (trace)
            {
                TraceEntry t = new TraceEntry();
                t.At = now;
                t.Vk = vk;
                t.Down = down;
                t.Result = r;
                t.Vendor = tracker.CurrentVendor;
                traceQueue.Enqueue(t);
            }

            if (!r.Handled)
                return false;
            Send(r.Emit);
            return true;
        }

        // Runs inside the hook callback for button and wheel events only. Mouse moves never get here.
        bool HandleMouse(MouseEvent e)
        {
            Settings s = cur;
            if (!s.Enabled)
                return false;
            if (s.ExcludeApps.Count > 0 && foreground.IsExcluded(s.ExcludeApps))
                return false;

            long now = clock.ElapsedMilliseconds;
            Result r = pipeline.ProcessMouse(e);
            if (!r.Handled)
                return false;

            if (trace)
            {
                TraceEntry t = new TraceEntry();
                t.At = now;
                t.IsMouse = true;
                t.Mouse = e;
                t.Result = r;
                t.Vendor = tracker.CurrentVendor;
                traceQueue.Enqueue(t);
            }

            Send(r.Emit);
            return true;
        }

        static string MouseText(MouseEvent e)
        {
            return e.Kind == MouseKind.Wheel ? Out.Wheel(e.Delta).ToString() : Out.Mouse(e.Button, e.Down).ToString();
        }

        void OnTick()
        {
            long t0 = Stopwatch.GetTimestamp();
            Beat();
            DrainWork();
            ApplyPendingSettings();

            List<Out> fired = pipeline.Tick(clock.ElapsedMilliseconds);
            Send(fired);
            if (trace && fired.Count > 0)
            {
                TraceEntry t = new TraceEntry();
                t.At = clock.ElapsedMilliseconds;
                t.Note = "tick => " + string.Join(" ", fired.ConvertAll<string>(delegate(Out o) { return o.ToString(); }).ToArray());
                traceQueue.Enqueue(t);
            }
            if (trace && ++ticks % TraceFlushTicks == 0)
                FlushTrace();
            EndCallback(t0, "tick");
        }

        void FlushTrace()
        {
            if (traceQueue.IsEmpty)
                return;
            StringBuilder sb = new StringBuilder();
            TraceEntry t;
            while (traceQueue.TryDequeue(out t))
            {
                sb.Append(t.At).Append("ms ");
                if (t.Note != null)
                    sb.Append(t.Note).AppendLine();
                else if (t.IsMouse)
                    sb.Append(MouseText(t.Mouse))
                      .Append(" => ").Append(t.Result.ToString())
                      .Append(" dev=").Append(t.Vendor ?? "none").AppendLine();
                else
                    sb.Append(t.Down ? "+" : "-").Append(KeyNames.NameOf(t.Vk))
                      .Append(" => ").Append(t.Result.ToString())
                      .Append(" dev=").Append(t.Vendor ?? "none").AppendLine();
            }
            try
            {
                File.AppendAllText(tracePath, sb.ToString());
            }
            catch (IOException)
            {
                // Tracing is best effort; dropping a batch is better than disturbing typing.
            }
        }

        void OnVendorChanged(object sender, EventArgs e)
        {
            string name = cur.ResolveProfile(tracker.CurrentVendor);
            Send(pipeline.SetProfile(name));
            activeProfile = pipeline.ProfileName;
            RaiseChanged();
        }

        // ---- any thread ----

        // Raised through the context captured at construction, never from the hook thread.
        void RaiseChanged()
        {
            if (shutdownStarted)
                return;
            ui.Post(delegate
            {
                if (shutdownStarted)
                    return;
                EventHandler handler = Changed;
                if (handler != null)
                    handler(this, EventArgs.Empty);
            }, null);
        }

        // UI thread. Only a stamp and a wake-up message; the tick re-checks the stamp, so a lost post is harmless.
        void OnStoreChanged(object sender, EventArgs e)
        {
            Interlocked.Increment(ref settingsStamp);
            PostToHook(MsgSettings);
        }

        void PostToHook(uint msg)
        {
            int id = hookThreadId;
            if (id != 0)
                Native.PostThreadMessage((uint)id, msg, IntPtr.Zero, IntPtr.Zero);
        }

        // Test seam: run f on the hook thread, where the pipeline lives.
        internal T RunOnHookThread<T>(Func<T> f, int timeoutMs)
        {
            T result = default(T);
            Exception error = null;
            ManualResetEvent done = new ManualResetEvent(false);
            work.Enqueue(delegate
            {
                try
                {
                    result = f();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    done.Set();
                }
            });
            PostToHook(MsgWork);
            if (!done.WaitOne(timeoutMs))
                throw new TimeoutException("hook thread did not run the work item");
            if (error != null)
                throw new InvalidOperationException(error.Message, error);
            return result;
        }

        internal bool KeyForTest(int vk, bool down)
        {
            return RunOnHookThread<bool>(delegate { return OnKey(vk, down); }, 5000);
        }

        // ---- watchdog ----

        void WatchdogMain()
        {
            Watchdog dog = new Watchdog(NowMs, LastBeat, opt.StallLimitMs, OnStall);
            long prev = NowMs();
            // A gap this long means the watchdog itself was starved or the machine slept, so the heartbeat
            // is stale for a reason that says nothing about the hook thread. Give it one more period.
            long maxGap = opt.StallLimitMs / 2 + opt.WatchdogPeriodMs;
            while (!stopDog.WaitOne(opt.WatchdogPeriodMs))
            {
                long now = NowMs();
                long gap = now - prev;
                prev = now;
                if (gap > maxGap)
                    continue;
                try
                {
                    dog.Check();
                }
                catch (Exception ex)
                {
                    CrashLog.Write("watchdog error: " + ex);
                }
            }
        }

        void OnStall()
        {
            // Unhook first: that frees the user's input immediately, whatever happens to the process after.
            bool hadKeyboard = keyboard.Installed;
            bool hadMouse = mouse.Installed;
            bool keyboardOk = keyboard.Uninstall();
            bool mouseOk = mouse.Uninstall();
            CrashLog.Write("WATCHDOG: hook thread silent for over " + opt.StallLimitMs + " ms. Unhooked keyboard="
                + (hadKeyboard ? keyboardOk.ToString() : "none") + " mouse=" + (hadMouse ? mouseOk.ToString() : "none")
                + ". Exiting.");

            if (opt.Exit != null)
            {
                opt.Exit(3);
                return;
            }

            Thread killer = new Thread(delegate()
            {
                Thread.Sleep(1000);
                Process.GetCurrentProcess().Kill();
            });
            killer.IsBackground = true;
            killer.Start();
            Environment.Exit(3);
        }

        // ---- shutdown ----

        // Safe from any thread, repeatable, and independent of the UI thread.
        public void Shutdown()
        {
            lock (shutdownGate)
            {
                if (shutdownDone)
                    return;
                shutdownStarted = true;
                stopDog.Set();

                Thread dog = watchdogThread;
                if (dog != null && dog != Thread.CurrentThread)
                    dog.Join(500);

                if (hookThread.IsAlive)
                {
                    PostToHook(Native.WM_QUIT);
                    if (!exited.WaitOne(1000))
                    {
                        keyboard.Uninstall();
                        mouse.Uninstall();
                        CrashLog.Write("Shutdown: hook thread did not exit within 1 s, hooks removed from the calling thread");
                        try
                        {
                            Pipeline p = pipeline;
                            if (p != null)
                                Send(p.ReleaseAll());
                        }
                        catch (Exception)
                        {
                        }
                    }
                    else
                    {
                        hookThread.Join(500);
                    }
                }

                store.Dispose();
                shutdownDone = true;
            }
        }

        public void Dispose()
        {
            Shutdown();
        }
    }
}
