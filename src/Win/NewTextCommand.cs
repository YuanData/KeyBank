using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace KeyBank.Win
{
    [ComImport, Guid("A08CE4D0-FA25-44AB-B57C-C7B1C323E0B9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IExplorerCommand
    {
        [PreserveSig] int GetTitle(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int GetIcon(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] out string icon);
        [PreserveSig] int GetToolTip(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] out string tip);
        [PreserveSig] int GetCanonicalName(out Guid guid);
        [PreserveSig] int GetState(IntPtr items, bool okToBeSlow, out int state);
        [PreserveSig] int Invoke(IntPtr items, IntPtr bindCtx);
        [PreserveSig] int GetFlags(out int flags);
        [PreserveSig] int EnumSubCommands(out IntPtr enumerator);
    }

    [ComVisible(true), Guid(NewTextCommand.ClsidText), ClassInterface(ClassInterfaceType.None)]
    class NewTextCommand : IExplorerCommand
    {
        public const string ClsidText = "6B1D2E4A-3C57-4F0B-9A52-7E8C1D0F4B93";
        const int E_NOTIMPL = unchecked((int)0x80004001);
        const int S_OK = 0;

        public int GetTitle(IntPtr items, out string name) { name = "文字文件"; return S_OK; }
        public int GetIcon(IntPtr items, out string icon) { icon = null; return E_NOTIMPL; }
        public int GetToolTip(IntPtr items, out string tip) { tip = null; return E_NOTIMPL; }
        public int GetCanonicalName(out Guid guid) { guid = new Guid(ClsidText); return S_OK; }
        public int GetState(IntPtr items, bool okToBeSlow, out int state) { state = 0; return S_OK; }
        public int GetFlags(out int flags) { flags = 0; return S_OK; }
        public int EnumSubCommands(out IntPtr enumerator) { enumerator = IntPtr.Zero; return E_NOTIMPL; }

        public int Invoke(IntPtr items, IntPtr bindCtx)
        {
            try
            {
                string folder = CurrentFolder();
                if (folder == null || !Directory.Exists(folder))
                    return S_OK;
                string path = NewFileName.Unique(folder, DateTime.Now, File.Exists);
                using (FileStream fs = new FileStream(path, FileMode.CreateNew)) { }
                Process.Start("explorer.exe", "/select,\"" + path + "\"");
            }
            catch (Exception ex)
            {
                Trace(ex.Message);
            }
            return S_OK;
        }

        // A background click gives no item, so find the folder of the Explorer window in front.
        static string CurrentFolder()
        {
            IntPtr fg = Native.GetForegroundWindow();
            Type shellType = Type.GetTypeFromProgID("Shell.Application");
            object shell = Activator.CreateInstance(shellType);
            object windows = Call(shell, "Windows");
            int count = (int)Get(windows, "Count");
            for (int i = 0; i < count; i++)
            {
                object w = Call(windows, "Item", i);
                if (w == null) continue;
                if ((long)Get(w, "HWND") != fg.ToInt64()) continue;
                object doc = Get(w, "Document");
                object self = Get(Get(doc, "Folder"), "Self");
                return (string)Get(self, "Path");
            }
            // No Explorer window in front: the desktop.
            return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        static object Get(object o, string name)
        {
            return o.GetType().InvokeMember(name, BindingFlags.GetProperty, null, o, null);
        }

        static object Call(object o, string name, params object[] args)
        {
            return o.GetType().InvokeMember(name, BindingFlags.InvokeMethod | BindingFlags.GetProperty, null, o, args);
        }

        static void Trace(string text)
        {
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "KeyBank.newfile.log"),
                    DateTime.Now.ToString("s") + " " + text + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
    }

    // Out-of-process COM server: Explorer launches KeyBank.exe -Embedding and asks it for the command.
    sealed class NewTextServer : IDisposable
    {
        [ComImport, Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IClassFactory
        {
            [PreserveSig] int CreateInstance(IntPtr outer, ref Guid iid, out IntPtr obj);
            [PreserveSig] int LockServer(bool lockIt);
        }

        class Factory : IClassFactory
        {
            const int CLASS_E_NOAGGREGATION = unchecked((int)0x80040110);
            public int CreateInstance(IntPtr outer, ref Guid iid, out IntPtr obj)
            {
                obj = IntPtr.Zero;
                if (outer != IntPtr.Zero) return CLASS_E_NOAGGREGATION;
                IntPtr unk = Marshal.GetIUnknownForObject(new NewTextCommand());
                try { return Marshal.QueryInterface(unk, ref iid, out obj); }
                finally { Marshal.Release(unk); }
            }
            public int LockServer(bool lockIt) { return 0; }
        }

        [DllImport("ole32.dll")]
        static extern int CoRegisterClassObject(ref Guid clsid, [MarshalAs(UnmanagedType.Interface)] object factory,
            int context, int flags, out int cookie);
        [DllImport("ole32.dll")]
        static extern int CoRevokeClassObject(int cookie);

        const int CLSCTX_LOCAL_SERVER = 4;
        const int REGCLS_MULTIPLEUSE = 1;
        int cookie;
        readonly Factory factory = new Factory();

        public void Register()
        {
            Guid clsid = new Guid(NewTextCommand.ClsidText);
            int hr = CoRegisterClassObject(ref clsid, factory, CLSCTX_LOCAL_SERVER, REGCLS_MULTIPLEUSE, out cookie);
            if (hr != 0) throw new InvalidOperationException("CoRegisterClassObject 0x" + hr.ToString("X8"));
        }

        public void Dispose()
        {
            if (cookie != 0) { CoRevokeClassObject(cookie); cookie = 0; }
        }
    }
}
