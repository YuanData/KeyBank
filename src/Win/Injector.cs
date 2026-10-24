using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using KeyBank.Core;

namespace KeyBank.Win
{
    internal static class Injector
    {
        static readonly int inputSize = Marshal.SizeOf(typeof(INPUT));

        // One array means one SendInput call, so Windows keeps the batch contiguous and ordered.
        internal static INPUT[] Build(List<Out> outs)
        {
            INPUT[] inputs = new INPUT[outs.Count];
            for (int i = 0; i < outs.Count; i++)
            {
                switch (outs[i].Kind)
                {
                    case OutKind.Scroll:
                    case OutKind.MouseWheel:
                        inputs[i] = BuildWheel(outs[i]);
                        break;
                    case OutKind.MouseButton:
                        inputs[i] = BuildMouseButton(outs[i]);
                        break;
                    default:
                        inputs[i] = BuildKey(outs[i]);
                        break;
                }
            }
            return inputs;
        }

        internal static INPUT BuildKey(Out o)
        {
            INPUT input = new INPUT();
            input.type = Native.INPUT_KEYBOARD;
            input.u.ki.wVk = (ushort)o.Vk;
            input.u.ki.wScan = (ushort)Native.MapVirtualKey((uint)o.Vk, Native.MAPVK_VK_TO_VSC);
            uint flags = 0;
            if (Vk.IsExtended(o.Vk))
                flags |= Native.KEYEVENTF_EXTENDEDKEY;
            if (!o.Down)
                flags |= Native.KEYEVENTF_KEYUP;
            input.u.ki.dwFlags = flags;
            input.u.ki.dwExtraInfo = (IntPtr)Native.Marker;
            return input;
        }

        internal static INPUT BuildWheel(Out o)
        {
            INPUT input = new INPUT();
            input.type = Native.INPUT_MOUSE;
            input.u.mi.dwFlags = Native.MOUSEEVENTF_WHEEL;
            input.u.mi.mouseData = WheelDelta(o);
            input.u.mi.dwExtraInfo = (IntPtr)Native.Marker;
            return input;
        }

        // No coordinates and no MOUSEEVENTF_ABSOLUTE, so the click lands where the cursor already is.
        internal static INPUT BuildMouseButton(Out o)
        {
            INPUT input = new INPUT();
            input.type = Native.INPUT_MOUSE;
            input.u.mi.dwExtraInfo = (IntPtr)Native.Marker;
            switch (o.Button)
            {
                case MouseButton.Left:
                    input.u.mi.dwFlags = o.Down ? Native.MOUSEEVENTF_LEFTDOWN : Native.MOUSEEVENTF_LEFTUP;
                    break;
                case MouseButton.Right:
                    input.u.mi.dwFlags = o.Down ? Native.MOUSEEVENTF_RIGHTDOWN : Native.MOUSEEVENTF_RIGHTUP;
                    break;
                case MouseButton.Middle:
                    input.u.mi.dwFlags = o.Down ? Native.MOUSEEVENTF_MIDDLEDOWN : Native.MOUSEEVENTF_MIDDLEUP;
                    break;
                default:
                    input.u.mi.dwFlags = o.Down ? Native.MOUSEEVENTF_XDOWN : Native.MOUSEEVENTF_XUP;
                    input.u.mi.mouseData = o.Button == MouseButton.X1 ? Native.XBUTTON1 : Native.XBUTTON2;
                    break;
            }
            return input;
        }

        internal static int WheelDelta(Out o)
        {
            if (o.Kind == OutKind.MouseWheel)
                return o.Delta;
            double delta = Math.Round(o.Ticks * 120, MidpointRounding.AwayFromZero);
            return o.Direction * (int)Math.Max(1, delta);
        }

        // Returns how many events Windows accepted; fewer than requested means UIPI or another hook blocked it.
        public static int Send(List<Out> outs)
        {
            if (outs == null || outs.Count == 0)
                return 0;
            INPUT[] inputs = Build(outs);
            return (int)Native.SendInput((uint)inputs.Length, inputs, inputSize);
        }
    }
}
