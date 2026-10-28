using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace KeyBank.Win
{
    // Per-hwnd cache keeps the hook callback to one dictionary lookup on the common path.
    internal sealed class ForegroundApp
    {
        readonly Dictionary<IntPtr, string> exeByWindow = new Dictionary<IntPtr, string>();
        readonly Func<IntPtr> foregroundWindow;
        readonly Func<IntPtr, string> resolveExe;

        public ForegroundApp()
            : this(Native.GetForegroundWindow, ExeNameOf)
        {
        }

        internal ForegroundApp(Func<IntPtr> foregroundWindow, Func<IntPtr, string> resolveExe)
        {
            this.foregroundWindow = foregroundWindow;
            this.resolveExe = resolveExe;
        }

        public void Reset()
        {
            exeByWindow.Clear();
        }

        // The exe file name of the foreground window, or null when there is none or it cannot be read.
        public string CurrentExeName()
        {
            IntPtr hwnd = foregroundWindow();
            if (hwnd == IntPtr.Zero)
                return null;
            string exe;
            if (!exeByWindow.TryGetValue(hwnd, out exe))
            {
                if (exeByWindow.Count > 256)
                    exeByWindow.Clear();
                exe = resolveExe(hwnd) ?? "";
                exeByWindow[hwnd] = exe;
            }
            return exe.Length > 0 ? exe : null;
        }

        // True when the foreground window's IME is open and typing full-width characters. Any failure or
        // timeout answers false, so an app that does not talk to the IME just gets the normal line length.
        public bool IsFullWidthInput()
        {
            IntPtr hwnd = foregroundWindow();
            if (hwnd == IntPtr.Zero)
                return false;
            IntPtr ime = Native.ImmGetDefaultIMEWnd(hwnd);
            if (ime == IntPtr.Zero)
                return false;
            IntPtr open;
            IntPtr mode;
            if (Native.SendMessageTimeout(ime, Native.WM_IME_CONTROL, (IntPtr)Native.IMC_GETOPENSTATUS, IntPtr.Zero,
                    Native.SMTO_ABORTIFHUNG, 30, out open) == IntPtr.Zero || open == IntPtr.Zero)
                return false;
            if (Native.SendMessageTimeout(ime, Native.WM_IME_CONTROL, (IntPtr)Native.IMC_GETCONVERSIONMODE, IntPtr.Zero,
                    Native.SMTO_ABORTIFHUNG, 30, out mode) == IntPtr.Zero)
                return false;
            return ((long)mode & Native.IME_CMODE_FULLSHAPE) != 0;
        }

        public bool IsExcluded(HashSet<string> excluded)
        {
            string exe = CurrentExeName();
            return exe != null && (excluded.Contains(exe) || excluded.Contains(Path.GetFileNameWithoutExtension(exe)));
        }

        internal static string ExeNameOf(IntPtr hwnd)
        {
            uint pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            if (pid == 0)
                return "";
            IntPtr process = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (process == IntPtr.Zero)
                return "";
            try
            {
                StringBuilder sb = new StringBuilder(520);
                uint size = (uint)sb.Capacity;
                return Native.QueryFullProcessImageName(process, 0, sb, ref size) ? Path.GetFileName(sb.ToString()) : "";
            }
            finally
            {
                Native.CloseHandle(process);
            }
        }
    }
}
