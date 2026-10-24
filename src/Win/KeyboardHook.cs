using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace KeyBank.Win
{
    // handler(vk, down) returns true to swallow the original event. It runs inside the hook callback on
    // the hook thread, so it must not block or do I/O.
    internal sealed class KeyboardHook : IDisposable
    {
        readonly Func<int, bool, bool> handler;
        readonly LowLevelKeyboardProc proc;
        volatile IntPtr hook = IntPtr.Zero;

        public Exception Fault;

        public KeyboardHook(Func<int, bool, bool> handler)
        {
            this.handler = handler;
            proc = Callback;
        }

        public bool Installed
        {
            get { return hook != IntPtr.Zero; }
        }

        // Read by the watchdog thread, which may unhook while the hook thread is stuck.
        public IntPtr Handle
        {
            get { return hook; }
        }

        public bool Install()
        {
            if (hook != IntPtr.Zero)
                return true;
            hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, proc, Native.GetModuleHandle(null), 0);
            return hook != IntPtr.Zero;
        }

        public bool Uninstall()
        {
            IntPtr h = hook;
            if (h == IntPtr.Zero)
                return true;
            hook = IntPtr.Zero;
            return Native.UnhookWindowsHookEx(h);
        }

        public void Dispose()
        {
            Uninstall();
        }

        // Forces the JIT and the marshalling stubs the callback needs, with no hook installed.
        internal void WarmUp()
        {
            RuntimeHelpers.PrepareDelegate(proc);
            IntPtr buffer = Marshal.AllocHGlobal(64);
            try
            {
                Marshal.WriteInt32(buffer, 0, 0x41);
                Marshal.WriteIntPtr(buffer, 16, IntPtr.Zero);
                int vk;
                bool down;
                Read(Native.WM_KEYDOWN, buffer, out vk, out down);
                Native.CallNextHookEx(IntPtr.Zero, -1, IntPtr.Zero, IntPtr.Zero);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        // KBDLLHOOKSTRUCT: vkCode at 0, dwExtraInfo at 16 on both x86 and x64.
        static bool Read(int msg, IntPtr lParam, out int vk, out bool down)
        {
            vk = 0;
            down = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN;
            bool up = msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP;
            if (!(down || up) || Marshal.ReadIntPtr(lParam, 16) == (IntPtr)Native.Marker)
                return false;
            vk = Marshal.ReadInt32(lParam, 0);
            return true;
        }

        IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                try
                {
                    int vk;
                    bool down;
                    if (Read((int)wParam, lParam, out vk, out down) && handler(vk, down))
                        return (IntPtr)1;
                }
                catch (Exception ex)
                {
                    Fault = ex;
                }
            }
            return Native.CallNextHookEx(hook, code, wParam, lParam);
        }
    }
}
