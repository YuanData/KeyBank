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

    [ComImport, Guid("FC4801A3-2BA9-11CF-A229-00AA003D7352"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IObjectWithSite
    {
        [PreserveSig] int SetSite([MarshalAs(UnmanagedType.IUnknown)] object site);
        [PreserveSig] int GetSite(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object site);
    }

    [ComImport, Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFolderView
    {
        [PreserveSig] int GetCurrentViewMode(out int mode);
        [PreserveSig] int SetCurrentViewMode(int mode);
        [PreserveSig] int GetFolder(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object folder);
    }

    [ComImport, Guid("1AC3D9F0-175C-11D1-95BE-00609797EA4F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPersistFolder2
    {
        [PreserveSig] int GetClassID(out Guid clsid);
        [PreserveSig] int Initialize(IntPtr pidl);
        [PreserveSig] int GetCurFolder(out IntPtr pidl);
    }

    [ComVisible(true), Guid(NewTextCommand.ClsidText), ClassInterface(ClassInterfaceType.None)]
    class NewTextCommand : IExplorerCommand, IObjectWithSite
    {
        object site;
        public int SetSite(object s) { site = s; return 0; }
        public int GetSite(ref Guid riid, out object s) { s = site; return site == null ? unchecked((int)0x80004005) : 0; }

        public const string ClsidText = "6B1D2E4A-3C57-4F0B-9A52-7E8C1D0F4B93";
        const int E_NOTIMPL = unchecked((int)0x80004001);
        const int S_OK = 0;

        public int GetTitle(IntPtr items, out string name) { name = "▶ 新增文字文件"; return S_OK; }
        public int GetIcon(IntPtr items, out string icon)
        {
            string file = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\package\newtext.ico"));
            icon = file;
            return File.Exists(file) ? S_OK : E_NOTIMPL;
        }
        public int GetToolTip(IntPtr items, out string tip) { tip = null; return E_NOTIMPL; }
        public int GetCanonicalName(out Guid guid) { guid = new Guid(ClsidText); return S_OK; }
        public int GetState(IntPtr items, bool okToBeSlow, out int state) { state = 0; return S_OK; }
        public int GetFlags(out int flags) { flags = 0; return S_OK; }
        public int EnumSubCommands(out IntPtr enumerator) { enumerator = IntPtr.Zero; return E_NOTIMPL; }

        public int Invoke(IntPtr items, IntPtr bindCtx)
        {
            try
            {
                string how = "site";
                string folder = FolderFromSite();
                if (folder == null)
                {
                    how = "guess";
                    folder = CurrentFolder();
                }
                Trace("folder(" + how + ")=" + folder);
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

        // Explorer hands the command a site for the exact view that was right-clicked, so no guessing among
        // windows and tabs. SID_SFolderView gives that view's folder.
        string FolderFromSite()
        {
            try
            {
                IServiceProvider sp = site as IServiceProvider;
                if (sp == null) return null;
                Guid sid = new Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE"), iid = sid;
                object view;
                if (sp.QueryService(ref sid, ref iid, out view) != 0 || view == null) return null;
                Guid pf = new Guid("1AC3D9F0-175C-11D1-95BE-00609797EA4F");
                object folder;
                if (((IFolderView)view).GetFolder(ref pf, out folder) != 0) return null;
                IntPtr pidl;
                if (((IPersistFolder2)folder).GetCurFolder(out pidl) != 0 || pidl == IntPtr.Zero) return null;
                try
                {
                    StringBuilder sb = new StringBuilder(520);
                    return SHGetPathFromIDListW(pidl, sb) ? sb.ToString() : null;
                }
                finally { Marshal.FreeCoTaskMem(pidl); }
            }
            catch (Exception ex)
            {
                Trace("FolderFromSite: " + ex.Message);
                return null;
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern bool SHGetPathFromIDListW(IntPtr pidl, StringBuilder path);

        // Fallback when there is no site. A background click gives no item, so find the folder of the Explorer window in front.
        // Windows 11 tabs share one top-level window, so Shell.Windows lists one entry per tab with the same
        // HWND. Taking the first match picks an arbitrary tab; the active tab is the one whose view is visible.
        static string CurrentFolder()
        {
            IntPtr fg = Native.GetForegroundWindow();
            Type shellType = Type.GetTypeFromProgID("Shell.Application");
            object shell = Activator.CreateInstance(shellType);
            object windows = Call(shell, "Windows");
            int count = (int)Get(windows, "Count");
            string firstMatch = null;
            for (int i = 0; i < count; i++)
            {
                object w = Call(windows, "Item", i);
                if (w == null) continue;
                if ((long)Get(w, "HWND") != fg.ToInt64()) continue;
                object doc = Get(w, "Document");
                string path = (string)Get(Get(Get(doc, "Folder"), "Self"), "Path");
                if (firstMatch == null) firstMatch = path;
                if (IsActiveTab(w))
                    return path;
            }
            if (firstMatch != null)
                return firstMatch;
            // No Explorer window in front: the desktop.
            return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IServiceProvider
        {
            [PreserveSig] int QueryService(ref Guid service, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object obj);
        }

        [ComImport, Guid("00000114-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IOleWindow
        {
            [PreserveSig] int GetWindow(out IntPtr hwnd);
        }

        static readonly Guid SidTopLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
        static readonly Guid IidOleWindow = new Guid("00000114-0000-0000-C000-000000000046");

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hwnd);

        // The view window of a background tab is hidden; the active tab's is visible.
        static bool IsActiveTab(object shellWindow)
        {
            try
            {
                IServiceProvider sp = (IServiceProvider)shellWindow;
                Guid sid = SidTopLevelBrowser, iid = IidOleWindow;
                object browser;
                if (sp.QueryService(ref sid, ref iid, out browser) != 0 || browser == null)
                    return false;
                IntPtr tab;
                return ((IOleWindow)browser).GetWindow(out tab) == 0 && IsWindowVisible(tab);
            }
            catch (Exception ex)
            {
                Trace("IsActiveTab: " + ex.Message);
                return false;
            }
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
