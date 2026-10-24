using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace KeyBank.Win
{
    // Raw Input reports which physical keyboard produced a key. The low-level hook cannot tell.
    // WM_INPUT can arrive after the hook callback for the same key, so the first key after
    // switching keyboards may still be handled with the previous profile. This is accepted.
    internal sealed class DeviceTracker : NativeWindow, IDisposable
    {
        static readonly Regex usbVendor = new Regex(@"VID_([0-9A-F]{4})", RegexOptions.IgnoreCase);
        static readonly Regex bluetoothVendor = new Regex(@"VID&[0-9A-F]{2}([0-9A-F]{4})", RegexOptions.IgnoreCase);

        readonly Dictionary<IntPtr, string> vendorByDevice = new Dictionary<IntPtr, string>();
        bool seen;

        // Written by the hook thread, read by the UI thread for display.
        public volatile string CurrentVendor;
        public event EventHandler VendorChanged;

        public DeviceTracker()
        {
            CreateParams cp = new CreateParams();
            cp.Parent = Native.HWND_MESSAGE;
            CreateHandle(cp);

            RAWINPUTDEVICE[] devices = new RAWINPUTDEVICE[1];
            devices[0].usUsagePage = 0x01;
            devices[0].usUsage = 0x06;
            devices[0].dwFlags = Native.RIDEV_INPUTSINK;
            devices[0].hwndTarget = Handle;
            if (!Native.RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE))))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        // USB paths carry VID_xxxx. Bluetooth HID paths carry VID&ssxxxx, where ss is the vendor id source.
        internal static string ExtractVendorId(string path)
        {
            if (path == null)
                return null;
            Match m = usbVendor.Match(path);
            if (m.Success)
                return m.Groups[1].Value.ToUpperInvariant();
            m = bluetoothVendor.Match(path);
            if (m.Success)
                return m.Groups[1].Value.ToUpperInvariant();
            return null;
        }

        internal static string DevicePath(IntPtr device)
        {
            uint size = 0;
            Native.GetRawInputDeviceInfo(device, Native.RIDI_DEVICENAME, null, ref size);
            if (size == 0)
                return null;
            StringBuilder sb = new StringBuilder((int)size);
            return Native.GetRawInputDeviceInfo(device, Native.RIDI_DEVICENAME, sb, ref size) == uint.MaxValue ? null : sb.ToString();
        }

        public static string Describe()
        {
            uint count = 0;
            uint entry = (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICELIST));
            Native.GetRawInputDeviceList(null, ref count, entry);
            RAWINPUTDEVICELIST[] list = new RAWINPUTDEVICELIST[count];
            if (count > 0 && Native.GetRawInputDeviceList(list, ref count, entry) == uint.MaxValue)
                return "(無法列舉鍵盤)";

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                if (list[i].dwType != Native.RIM_TYPEKEYBOARD)
                    continue;
                string path = DevicePath(list[i].hDevice);
                string vendor = ExtractVendorId(path);
                sb.AppendLine((vendor ?? "(無，內建或未知)") + "  " + path);
            }
            return sb.Length == 0 ? "(沒有鍵盤)" : sb.ToString();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_INPUT)
            {
                try
                {
                    OnRawInput(m.LParam);
                }
                catch (Exception)
                {
                    // A failed lookup must not break the message pump; the next key retries.
                }
            }
            base.WndProc(ref m);
        }

        void OnRawInput(IntPtr hRawInput)
        {
            uint size = 0;
            uint header = (uint)(IntPtr.Size * 2 + 8);
            Native.GetRawInputData(hRawInput, Native.RID_INPUT, null, ref size, header);
            if (size == 0)
                return;
            byte[] buffer = new byte[size];
            if (Native.GetRawInputData(hRawInput, Native.RID_INPUT, buffer, ref size, header) == uint.MaxValue)
                return;

            // RAWINPUTHEADER: dwType(4) dwSize(4) hDevice(ptr) wParam(ptr)
            IntPtr device = IntPtr.Size == 8 ? new IntPtr(BitConverter.ToInt64(buffer, 8)) : new IntPtr(BitConverter.ToInt32(buffer, 8));
            if (device == IntPtr.Zero)
                return;

            string vendor;
            if (!vendorByDevice.TryGetValue(device, out vendor))
            {
                vendor = ExtractVendorId(DevicePath(device));
                vendorByDevice[device] = vendor;
            }

            if (seen && string.Equals(vendor, CurrentVendor, StringComparison.OrdinalIgnoreCase))
                return;
            seen = true;
            CurrentVendor = vendor;
            EventHandler handler = VendorChanged;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
                DestroyHandle();
        }
    }
}
