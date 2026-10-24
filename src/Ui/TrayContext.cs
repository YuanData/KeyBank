using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using KeyBank.Core;

namespace KeyBank.Ui
{
    public sealed class TrayContext : ApplicationContext
    {
        const string FontName = "Microsoft JhengHei UI";

        readonly IEngineHost host;
        readonly IAutoStart autoStart;
        readonly NotifyIcon tray;
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly Control sync = new Control();
        // NotifyIcon only opens its menu on a right click; the private method is what a right click calls.
        static readonly MethodInfo ShowContextMenu = typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
        Icon currentIcon;
        bool iconEnabled;
        bool iconBuilt;
        SettingsForm settingsForm;
        ScrollTuneForm tuneForm;
        bool disposed;

        internal ToolStripMenuItem EnabledItem;
        internal ToolStripMenuItem ScrollItem;
        internal ToolStripMenuItem AltMouseItem;
        internal ToolStripMenuItem WheelFilterItem;
        internal ToolStripMenuItem TuneItem;
        internal ToolStripMenuItem AutoProfileItem;
        internal ToolStripMenuItem AutoStartItem;
        internal ToolStripItem ErrorItem;
        internal readonly List<ToolStripMenuItem> ProfileItems = new List<ToolStripMenuItem>();
        internal string LastBalloon;

        public TrayContext(IEngineHost host) : this(host, new RegistryAutoStart(), true)
        {
        }

        internal TrayContext(IEngineHost host, IAutoStart autoStart, bool showIcon)
        {
            this.host = host;
            this.autoStart = autoStart;

            sync.CreateControl();
            IntPtr forceHandle = sync.Handle;

            menu.Font = new Font(FontName, 9f);
            tray = new NotifyIcon();
            tray.ContextMenuStrip = menu;
            tray.MouseClick += OnTrayMouseClick;

            host.Changed += OnHostChanged;
            Refresh();
            tray.Visible = showIcon;
        }

        void OnHostChanged(object sender, EventArgs e)
        {
            if (disposed)
                return;
            // Always deferred: Changed fires inside a menu Click, and Refresh disposes the clicked item.
            sync.BeginInvoke(new Action(delegate { if (!disposed) Refresh(); }));
        }

        void OnTrayMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;
            if (ShowContextMenu != null)
                ShowContextMenu.Invoke(tray, null);
            else
                menu.Show(Cursor.Position);
        }

        internal static bool CanOpenMenuOnLeftClick
        {
            get { return ShowContextMenu != null; }
        }

        internal void Refresh()
        {
            Settings s = host.Settings;
            string err = host.LastError;

            menu.SuspendLayout();
            List<ToolStripItem> stale = new List<ToolStripItem>();
            foreach (ToolStripItem old in menu.Items)
                stale.Add(old);
            menu.Items.Clear();
            foreach (ToolStripItem old in stale)
                old.Dispose();
            ProfileItems.Clear();
            ErrorItem = null;

            if (err != null)
            {
                ErrorItem = new ToolStripMenuItem("設定檔有錯誤：" + Truncate(err, 60));
                ErrorItem.Enabled = false;
                menu.Items.Add(ErrorItem);
                menu.Items.Add(new ToolStripSeparator());
            }

            EnabledItem = new ToolStripMenuItem("停用");
            EnabledItem.Checked = !s.Enabled;
            EnabledItem.Click += delegate { ToggleEnabled(); };
            menu.Items.Add(EnabledItem);

            ScrollItem = new ToolStripMenuItem("分段捲動");
            ScrollItem.Checked = s.Scroll.Enabled;
            ScrollItem.Click += delegate { ToggleScroll(); };
            menu.Items.Add(ScrollItem);

            AltMouseItem = new ToolStripMenuItem("Alt 加滑鼠變 Ctrl");
            AltMouseItem.Checked = s.AltMouse;
            AltMouseItem.Click += delegate { ToggleAltMouse(); };
            menu.Items.Add(AltMouseItem);

            WheelFilterItem = new ToolStripMenuItem("滾輪反向抑制");
            WheelFilterItem.Checked = s.WheelFilter.Enabled;
            WheelFilterItem.Click += delegate { ToggleWheelFilter(); };
            menu.Items.Add(WheelFilterItem);

            TuneItem = new ToolStripMenuItem("捲動微調…");
            TuneItem.Click += delegate { ShowTune(); };
            menu.Items.Add(TuneItem);

            ToolStripMenuItem kb = new ToolStripMenuItem("鍵盤設定");
            AutoProfileItem = new ToolStripMenuItem("自動辨識");
            AutoProfileItem.Checked = s.ActiveProfile == "auto";
            AutoProfileItem.Tag = "auto";
            AutoProfileItem.Click += delegate { LockProfile("auto"); };
            kb.DropDownItems.Add(AutoProfileItem);
            List<string> names = new List<string>(s.Profiles.Keys);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string name in names)
            {
                string n = name;
                ToolStripMenuItem item = new ToolStripMenuItem(s.Profiles[n].Description);
                item.Checked = s.ActiveProfile == n;
                item.Tag = n;
                item.Click += delegate { LockProfile(n); };
                ProfileItems.Add(item);
                kb.DropDownItems.Add(item);
            }
            menu.Items.Add(kb);

            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem view = new ToolStripMenuItem("查看目前設定…");
            view.Click += delegate { ShowSettings(); };
            menu.Items.Add(view);
            ToolStripMenuItem openCfg = new ToolStripMenuItem("開啟設定檔");
            openCfg.Click += delegate { OpenConfigFile(); };
            menu.Items.Add(openCfg);

            AutoStartItem = new ToolStripMenuItem("開機自動啟動");
            AutoStartItem.Checked = ReadAutoStart();
            AutoStartItem.Click += delegate { ToggleAutoStart(); };
            menu.Items.Add(AutoStartItem);

            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem exit = new ToolStripMenuItem("結束");
            exit.Click += delegate { ExitThread(); };
            menu.Items.Add(exit);
            menu.ResumeLayout();

            UpdateIcon(s.Enabled);
            tray.Text = TooltipText();
        }

        internal string TooltipText()
        {
            Settings s = host.Settings;
            string name = host.ActiveProfileName;
            ProfileRuntime p;
            string desc = name != null && s.Profiles.TryGetValue(name, out p) ? p.Description : (name ?? "");
            string text = "KeyBank：" + (s.Enabled ? "啟用" : "停用") + "｜" + desc + "｜" + RuleModel.ScrollSummary(s);
            if (host.LastError != null)
                text = "設定檔有錯誤｜" + text;
            return Truncate(text, 63);
        }

        static string Truncate(string text, int max)
        {
            if (text == null)
                return "";
            text = text.Replace("\r", " ").Replace("\n", " ");
            return text.Length <= max ? text : text.Substring(0, max - 1) + "…";
        }

        bool ReadAutoStart()
        {
            try { return autoStart.Enabled; }
            catch (Exception) { return false; }
        }

        internal void ToggleEnabled()
        {
            bool target = !host.Settings.Enabled;
            Apply(delegate(ConfigFile f) { f.Enabled = target; });
        }

        internal void ToggleScroll()
        {
            bool target = !host.Settings.Scroll.Enabled;
            Apply(delegate(ConfigFile f)
            {
                f.Scroll = f.EffectiveScroll();
                f.Scroll.Enabled = target;
            });
        }

        internal void ToggleAltMouse()
        {
            bool target = !host.Settings.AltMouse;
            Apply(delegate(ConfigFile f) { f.AltMouse = target; });
        }

        internal void ToggleWheelFilter()
        {
            bool target = !host.Settings.WheelFilter.Enabled;
            Apply(delegate(ConfigFile f)
            {
                f.WheelFilter = f.WheelFilter ?? new WheelFilterConfig();
                f.WheelFilter.Enabled = target;
            });
        }

        internal void LockProfile(string name)
        {
            Apply(delegate(ConfigFile f) { f.ActiveProfile = name; });
        }

        void ToggleAutoStart()
        {
            try
            {
                autoStart.Enabled = !autoStart.Enabled;
            }
            catch (Exception ex)
            {
                Balloon("無法變更開機自動啟動：" + ex.Message);
            }
            Refresh();
        }

        internal void Apply(Action<ConfigFile> change)
        {
            try
            {
                host.Mutate(change);
            }
            catch (ConfigException ex)
            {
                Balloon(ex.Message);
            }
        }

        void Balloon(string message)
        {
            LastBalloon = message;
            tray.ShowBalloonTip(5000, "KeyBank", Truncate(message, 250), ToolTipIcon.Error);
        }

        void ShowSettings()
        {
            if (settingsForm == null || settingsForm.IsDisposed)
            {
                settingsForm = new SettingsForm(host);
                settingsForm.FormClosed += delegate { settingsForm = null; };
            }
            if (!settingsForm.Visible)
                settingsForm.Show();
            if (settingsForm.WindowState == FormWindowState.Minimized)
                settingsForm.WindowState = FormWindowState.Normal;
            settingsForm.Activate();
            settingsForm.BringToFront();
        }

        public void ShowSettingsWindow()
        {
            ShowSettings();
        }

        internal ScrollTuneForm TuneForm
        {
            get { return tuneForm; }
        }

        internal void ShowTune()
        {
            if (tuneForm == null || tuneForm.IsDisposed)
            {
                tuneForm = new ScrollTuneForm(host);
                tuneForm.FormClosed += delegate { tuneForm = null; };
            }
            if (!tuneForm.Visible)
                tuneForm.Show();
            if (tuneForm.WindowState == FormWindowState.Minimized)
                tuneForm.WindowState = FormWindowState.Normal;
            tuneForm.Activate();
            tuneForm.BringToFront();
        }

        void OpenConfigFile()
        {
            string path = host.ConfigPath;
            try
            {
                Process.Start(path);
            }
            catch (Exception)
            {
                try
                {
                    Process.Start("explorer.exe", "/select,\"" + path + "\"");
                }
                catch (Exception ex)
                {
                    Balloon("無法開啟設定檔：" + ex.Message);
                }
            }
        }

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);

        void UpdateIcon(bool enabled)
        {
            if (iconBuilt && iconEnabled == enabled)
                return;
            Icon fresh = DrawIcon(enabled);
            tray.Icon = fresh;
            if (currentIcon != null)
                currentIcon.Dispose();
            currentIcon = fresh;
            iconEnabled = enabled;
            iconBuilt = true;
        }

        internal static Icon DrawIcon(bool enabled)
        {
            return DrawIcon(enabled, SystemInformation.SmallIconSize);
        }

        // Gray tile with a white asterisk. Disabled adds a red "no" sign (ring and slash) over the whole tile.
        internal static Icon DrawIcon(bool enabled, Size size)
        {
            using (Bitmap bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    float w = size.Width, h = size.Height;
                    RectangleF r = new RectangleF(0.5f, 0.5f, w - 1.5f, h - 1.5f);
                    using (GraphicsPath path = RoundRect(r, w * 0.22f))
                    using (Brush b = new SolidBrush(Color.FromArgb(0x6E, 0x73, 0x7C)))
                        g.FillPath(b, path);

                    float cx = w / 2f, cy = h / 2f, arm = Math.Min(w, h) * 0.3f;
                    using (Pen star = new Pen(Color.White, Math.Max(1.5f, w * 0.115f)))
                    {
                        star.StartCap = LineCap.Round;
                        star.EndCap = LineCap.Round;
                        for (int i = 0; i < 3; i++)
                        {
                            double a = Math.PI / 2 + i * Math.PI / 3;
                            float dx = (float)(Math.Cos(a) * arm), dy = (float)(Math.Sin(a) * arm);
                            g.DrawLine(star, cx - dx, cy - dy, cx + dx, cy + dy);
                        }
                    }

                    if (!enabled)
                    {
                        float ring = Math.Max(1.6f, w * 0.12f);
                        float inset = ring / 2f + 0.5f;
                        RectangleF circle = new RectangleF(inset, inset, w - 2 * inset, h - 2 * inset);
                        using (Pen halo = new Pen(Color.White, ring + Math.Max(1f, w * 0.06f)))
                        using (Pen red = new Pen(Color.FromArgb(0xE0, 0x24, 0x24), ring))
                        {
                            float k = (float)(Math.Sqrt(0.5) * circle.Width / 2f);
                            PointF a = new PointF(cx - k, cy - k), b = new PointF(cx + k, cy + k);
                            g.DrawEllipse(halo, circle);
                            g.DrawLine(halo, a, b);
                            g.DrawEllipse(red, circle);
                            g.DrawLine(red, a, b);
                        }
                    }
                }
                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    using (Icon temp = Icon.FromHandle(hIcon))
                        return (Icon)temp.Clone();
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }
        }

        static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            float d = radius * 2;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true;
                host.Changed -= OnHostChanged;
                tray.Visible = false;
                tray.Dispose();
                if (settingsForm != null && !settingsForm.IsDisposed)
                    settingsForm.Dispose();
                if (tuneForm != null && !tuneForm.IsDisposed)
                    tuneForm.Dispose();
                menu.Dispose();
                if (currentIcon != null)
                    currentIcon.Dispose();
                sync.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
