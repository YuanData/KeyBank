using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using KeyBank.Core;

namespace KeyBank.Win
{
    // handler(event) returns true to swallow the original event. It runs inside the hook callback for
    // every button and wheel event, so it must not block or do I/O. Mouse moves never reach it.
    internal sealed class MouseHook : IDisposable
    {
        // MSLLHOOKSTRUCT: pt(8) mouseData(4) flags(4) time(4), then dwExtraInfo aligned to pointer size.
        // flags bit 0 is LLMHF_INJECTED.
        static readonly int ExtraInfoOffset = IntPtr.Size == 8 ? 24 : 20;
        const int MouseDataOffset = 8;
        const int FlagsOffset = 12;
        const int TimeOffset = 16;
        const int InjectedFlag = 0x1;

        readonly Func<MouseEvent, bool> handler;
        readonly LowLevelKeyboardProc proc;
        volatile IntPtr hook = IntPtr.Zero;

        public Exception Fault;

        public MouseHook(Func<MouseEvent, bool> handler)
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
            hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, proc, Native.GetModuleHandle(null), 0);
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

        // Forces the JIT and the marshalling stubs the callback needs, with no hook installed.
        internal void WarmUp()
        {
            RuntimeHelpers.PrepareDelegate(proc);
            IntPtr buffer = Marshal.AllocHGlobal(64);
            try
            {
                for (int i = 0; i < 64; i += 4)
                    Marshal.WriteInt32(buffer, i, 0);
                MouseEvent e;
                Parse(Native.WM_LBUTTONDOWN, Marshal.ReadInt32(buffer, MouseDataOffset), Marshal.ReadInt32(buffer, FlagsOffset), Marshal.ReadInt32(buffer, TimeOffset), (long)Marshal.ReadIntPtr(buffer, ExtraInfoOffset), out e);
                Parse(Native.WM_MOUSEWHEEL, 120 << 16, InjectedFlag, -1, 0, out e);
                Parse(Native.WM_XBUTTONUP, 1 << 16, 0, 0, 0, out e);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public void Dispose()
        {
            Uninstall();
        }

        // Returns false for anything KeyBank does not act on: moves, our own injected events, zero wheel deltas.
        internal static bool Parse(int msg, int mouseData, int flags, int time, long extraInfo, out MouseEvent e)
        {
            e = new MouseEvent();
            if (extraInfo == Native.Marker)
                return false;
            if (!ParseKind(msg, mouseData, out e))
                return false;
            e.Injected = (flags & InjectedFlag) != 0;
            e.HardwareMs = (long)(uint)time;
            return true;
        }

        static bool ParseKind(int msg, int mouseData, out MouseEvent e)
        {
            e = new MouseEvent();

            switch (msg)
            {
                case Native.WM_LBUTTONDOWN: e = MouseEvent.Click(MouseButton.Left, true); return true;
                case Native.WM_LBUTTONUP: e = MouseEvent.Click(MouseButton.Left, false); return true;
                case Native.WM_RBUTTONDOWN: e = MouseEvent.Click(MouseButton.Right, true); return true;
                case Native.WM_RBUTTONUP: e = MouseEvent.Click(MouseButton.Right, false); return true;
                case Native.WM_MBUTTONDOWN: e = MouseEvent.Click(MouseButton.Middle, true); return true;
                case Native.WM_MBUTTONUP: e = MouseEvent.Click(MouseButton.Middle, false); return true;
                case Native.WM_XBUTTONDOWN:
                case Native.WM_XBUTTONUP:
                    int which = (mouseData >> 16) & 0xFFFF;
                    if (which != Native.XBUTTON1 && which != Native.XBUTTON2)
                        return false;
                    e = MouseEvent.Click(which == Native.XBUTTON1 ? MouseButton.X1 : MouseButton.X2, msg == Native.WM_XBUTTONDOWN);
                    return true;
                case Native.WM_MOUSEWHEEL:
                    int delta = (short)(mouseData >> 16);
                    if (delta == 0)
                        return false;
                    e = MouseEvent.Wheel(delta);
                    return true;
                default:
                    return false;
            }
        }

        internal static bool ParseStruct(int msg, byte[] s, out MouseEvent e)
        {
            long extra = IntPtr.Size == 8 ? BitConverter.ToInt64(s, ExtraInfoOffset) : BitConverter.ToInt32(s, ExtraInfoOffset);
            return Parse(msg, BitConverter.ToInt32(s, MouseDataOffset), BitConverter.ToInt32(s, FlagsOffset), BitConverter.ToInt32(s, TimeOffset), extra, out e);
        }

        IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                int msg = (int)wParam;
                if (msg >= Native.WM_LBUTTONDOWN && msg <= Native.WM_XBUTTONUP)
                {
                    try
                    {
                        MouseEvent e;
                        if (Parse(msg, Marshal.ReadInt32(lParam, MouseDataOffset), Marshal.ReadInt32(lParam, FlagsOffset), Marshal.ReadInt32(lParam, TimeOffset), (long)Marshal.ReadIntPtr(lParam, ExtraInfoOffset), out e)
                            && handler(e))
                            return (IntPtr)1;
                    }
                    catch (Exception ex)
                    {
                        Fault = ex;
                    }
                }
            }
            return Native.CallNextHookEx(hook, code, wParam, lParam);
        }
    }
}
