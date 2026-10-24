using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using KeyBank;
using KeyBank.Core;
using KeyBank.Ui;

static class UiPreview
{
    static int failures;

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what);
        if (!ok)
            failures++;
    }

    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string outDir = args.Length > 0 ? args[0] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview");
        Directory.CreateDirectory(outDir);

        TrayAssertions();
        RuleAssertions();
        FormAssertions();
        StripAssertions();
        TuneAssertions();
        Render(outDir, "full", 1.0f);
        Render(outDir, "mini", 1.0f);
        Render(outDir, "full", 1.5f);
        Render(outDir, "mini", 1.5f);
        RenderTune(outDir, "tune", 1.0f, 8, 4, false);
        RenderTune(outDir, "tune-overflow", 1.0f, 90, 6, false);
        RenderTune(outDir, "tune-150", 1.5f, 8, 4, true);
        RenderIcons(outDir);

        Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILED");
        return failures == 0 ? 0 : 1;
    }

    sealed class FakeAutoStart : IAutoStart
    {
        public bool Value;
        public bool Enabled { get { return Value; } set { Value = value; } }
    }

    static void Pump(int ms)
    {
        Stopwatch sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Application.DoEvents();
    }

    static void SetScroll(FakeHost host, double step, double page, bool withChrome)
    {
        host.Mutate(delegate(ConfigFile f)
        {
            f.Scroll = f.EffectiveScroll();
            f.Scroll.StepTicks = step;
            f.Scroll.PageTicks = page;
            if (withChrome)
            {
                ScrollAppConfig a = new ScrollAppConfig();
                a.StepTicks = 6;
                a.PageTicks = 12;
                f.Scroll.Apps["chrome"] = a;
            }
        });
    }

    static void TrayAssertions()
    {
        FakeHost host = new FakeHost();
        FakeAutoStart auto = new FakeAutoStart();
        TrayContext tray = new TrayContext(host, auto, false);
        try
        {
            Check(tray.EnabledItem.Text == "停用" && !tray.EnabledItem.Checked, "menu: 停用 starts unchecked");
            Check(TrayContext.CanOpenMenuOnLeftClick, "left click can open the menu (NotifyIcon.ShowContextMenu found)");
            Check(!tray.AutoStartItem.Checked, "menu: 開機自動啟動 starts off");
            Check(tray.ErrorItem == null, "menu: no error item without LastError");
            Check(tray.ScrollItem.Text == "分段捲動" && tray.ScrollItem.Checked, "menu: 分段捲動 checked");
            Check(tray.AltMouseItem.Text == "Alt 加滑鼠變 Ctrl" && tray.AltMouseItem.Checked, "menu: Alt 加滑鼠變 Ctrl checked");
            Check(tray.TuneItem.Text == "捲動微調…", "menu: 捲動微調… present");
            Check(tray.AutoProfileItem.Checked, "menu: 自動辨識 checked");
            Check(tray.TooltipText() == "KeyBank：啟用｜完整鍵盤｜單次 4 格 / 整頁 8 格", "tooltip: " + tray.TooltipText());

            tray.EnabledItem.PerformClick();
            Check(!host.Settings.Enabled, "click 停用 -> Settings.Enabled false");
            Application.DoEvents();
            Check(tray.EnabledItem.Checked, "menu rebuilt: 停用 checked");
            Check(tray.TooltipText().StartsWith("KeyBank：停用"), "tooltip shows 停用");
            tray.EnabledItem.PerformClick();
            Check(host.Settings.Enabled, "click 停用 again -> Enabled true");
            Application.DoEvents();
            Check(!tray.EnabledItem.Checked, "menu rebuilt: 停用 unchecked again");

            Application.DoEvents();
            tray.ScrollItem.PerformClick();
            Check(!host.Settings.Scroll.Enabled, "click 分段捲動 -> Scroll.Enabled false");
            Check(host.Settings.Scroll.StepTicks == 4 && host.Settings.Scroll.PageTicks == 8, "toggle keeps ticks");
            Application.DoEvents();
            Check(!tray.ScrollItem.Checked, "menu rebuilt: 分段捲動 unchecked");
            Check(tray.TooltipText().EndsWith("分段捲動 關閉"), "tooltip: " + tray.TooltipText());
            tray.ScrollItem.PerformClick();
            Check(host.Settings.Scroll.Enabled, "click 分段捲動 again -> true");
            Application.DoEvents();

            tray.AltMouseItem.PerformClick();
            Check(!host.Settings.AltMouse, "click Alt 加滑鼠變 Ctrl -> AltMouse false");
            Application.DoEvents();
            Check(!tray.AltMouseItem.Checked, "menu rebuilt: Alt 加滑鼠變 Ctrl unchecked");
            tray.AltMouseItem.PerformClick();
            Check(host.Settings.AltMouse, "click Alt 加滑鼠變 Ctrl again -> true");
            Application.DoEvents();

            Check(tray.WheelFilterItem.Text == "滾輪反向抑制" && !tray.WheelFilterItem.Checked && !host.Settings.WheelFilter.Enabled, "menu: 滾輪反向抑制 starts unchecked");
            int altIdx = tray.AltMouseItem.Owner.Items.IndexOf(tray.AltMouseItem);
            Check(tray.AltMouseItem.Owner.Items.IndexOf(tray.WheelFilterItem) == altIdx + 1, "menu: 滾輪反向抑制 right after Alt 加滑鼠變 Ctrl");
            tray.WheelFilterItem.PerformClick();
            Check(host.Settings.WheelFilter.Enabled, "click 滾輪反向抑制 -> WheelFilter.Enabled true");
            Application.DoEvents();
            Check(tray.WheelFilterItem.Checked, "menu rebuilt: 滾輪反向抑制 checked");
            tray.WheelFilterItem.PerformClick();
            Check(!host.Settings.WheelFilter.Enabled, "click 滾輪反向抑制 again -> false");
            Application.DoEvents();
            Check(!tray.WheelFilterItem.Checked, "menu rebuilt: 滾輪反向抑制 unchecked again");
            host.RejectMutationsWith = "wheelFilter 測試拒絕。";
            tray.WheelFilterItem.PerformClick();
            Check(!host.Settings.WheelFilter.Enabled && tray.LastBalloon != null && tray.LastBalloon.Contains("wheelFilter"), "rejected Mutate leaves 滾輪反向抑制 off and raises a balloon");
            host.RejectMutationsWith = null;
            tray.LastBalloon = null;
            Application.DoEvents();

            Check(tray.TuneForm == null, "tune form not created before click");
            tray.TuneItem.PerformClick();
            ScrollTuneForm first = tray.TuneForm;
            Check(first != null && first.Visible, "click 捲動微調… opens tuning window");
            Application.DoEvents();
            tray.TuneItem.PerformClick();
            Check(tray.TuneForm == first, "second click reuses the same window (single instance)");
            first.Close();
            Application.DoEvents();
            Check(tray.TuneForm == null, "closing the tuning window clears the reference");
            tray.TuneItem.PerformClick();
            Check(tray.TuneForm != null && tray.TuneForm != first && tray.TuneForm.Visible, "click after close opens a fresh window");
            tray.TuneForm.Close();
            Application.DoEvents();

            ToolStripMenuItem mini = null;
            foreach (ToolStripMenuItem it in tray.ProfileItems)
                if ((string)it.Tag == "mini")
                    mini = it;
            Check(mini != null && mini.Text == "迷你鍵盤", "menu: mini profile item uses Description");
            mini.PerformClick();
            Check(host.Settings.ActiveProfile == "mini", "click 迷你鍵盤 -> ActiveProfile mini");
            Application.DoEvents();
            Check(!tray.AutoProfileItem.Checked, "menu: 自動辨識 unchecked after lock");
            Check(tray.TooltipText() == "KeyBank：啟用｜迷你鍵盤｜單次 4 格 / 整頁 8 格", "tooltip with mini: " + tray.TooltipText());
            tray.AutoProfileItem.PerformClick();
            Check(host.Settings.ActiveProfile == "auto", "click 自動辨識 -> auto");

            tray.AutoStartItem.PerformClick();
            Check(auto.Value, "click 開機自動啟動 -> injected IAutoStart on (registry untouched)");

            host.LastError = "profiles.mini.remap[0].to：不認得按鍵名稱 'Foo'。";
            host.RaiseChanged();
            Application.DoEvents();
            Check(tray.ErrorItem != null && !tray.ErrorItem.Enabled && tray.ErrorItem.Text.StartsWith("設定檔有錯誤："), "menu: disabled error item first");
            Check(tray.TooltipText().StartsWith("設定檔有錯誤｜") && tray.TooltipText().Length <= 63, "tooltip error prefix, len " + tray.TooltipText().Length);

            tray.Apply(delegate(ConfigFile f)
            {
                f.Scroll = f.EffectiveScroll();
                f.Scroll.StepTicks = 0;
            });
            Check(tray.LastBalloon != null && tray.LastBalloon.Contains("scroll.stepTicks"), "ConfigException surfaced as balloon: " + tray.LastBalloon);
            Check(host.Settings.Scroll.StepTicks == 4, "failed Mutate left Settings unchanged");
        }
        finally
        {
            tray.Dispose();
        }
        Check(host.Subscribers == 0, "tray Dispose unhooks host.Changed");
    }

    static void RuleAssertions()
    {
        Settings s = Settings.Load(DefaultConfig.Json.Replace("\"altMouse\": false", "\"altMouse\": true"));
        ClusterCell[][] mini = RuleModel.BuildCluster(s.Profile("mini"));
        string after = "";
        foreach (ClusterCell[] r in mini)
        {
            foreach (ClusterCell c in r)
                after += "[" + c.AfterLabel + "]";
            after += "/";
        }
        Check(after == "[Home][End][Home][PgUp]/[NumLk][/][End][PgDn]/", "mini cluster after = " + after);
        Check(mini[0][0].Badge == "長按 500ms → PrtScr", "mini Home badge");
        Check(!mini[0][0].Changed && mini[0][2].Changed && mini[0][3].Changed && mini[1][2].Changed && mini[1][3].Changed, "mini changed flags");

        ClusterCell[][] full = RuleModel.BuildCluster(s.Profile("full"));
        bool anyChanged = false;
        foreach (ClusterCell[] r in full)
            foreach (ClusterCell c in r)
                anyChanged |= c.Changed || c.Badge != null;
        Check(!anyChanged, "full cluster has no changes");

        List<RuleRow> rows = RuleModel.BuildRows(s, s.Profile("mini"));
        Check(rows[0].Press == "Alt+C" && rows[0].Becomes == "Ctrl+C" && rows[0].Note.StartsWith("Shift 照常"), "first alt row");
        Check(rows[1].Note == "", "only first alt row has note");

        RuleRow scroll = Find(rows, RuleModel.CatScroll, "PgUp / PgDn");
        Check(scroll != null && scroll.Becomes == "單次 4 格；連點第 3 下起整頁 8 格；按住每秒最多 8 次", "scroll row: " + (scroll == null ? "missing" : scroll.Becomes));
        RuleRow alt = Find(rows, RuleModel.CatAltMouse, "Alt + 滑鼠按鍵或滾輪");
        Check(alt != null && alt.Becomes == "Ctrl + 同一個動作", "Alt mouse row on");

        string chords = "";
        string header = null;
        foreach (RuleRow r in rows)
            if (r.Group != null)
            {
                header = r.Group;
                chords += r.Press.Substring(4);
            }
        Check(header == "Chrome（chrome.exe）專用的 Alt 手勢", "chrome group header: " + header);
        Check(chords == "DLNRTW0123456789=-", "chrome rows stable order: " + chords);
        Check(RuleModel.ShortLabel(0xBB) == "=" && RuleModel.ShortLabel(0xBD) == "-", "Equals/Minus short labels");

        ConfigFile f = ConfigFile.Parse(DefaultConfig.Json);
        f.AltMouse = false;
        f.Scroll.Apps["chrome"] = new ScrollAppConfig { StepTicks = 6, PageTicks = 12 };
        Settings s2 = Settings.Compile(f);
        List<RuleRow> rows2 = RuleModel.BuildRows(s2, s2.Profile("full"));
        RuleRow app = Find(rows2, RuleModel.CatScroll, "PgUp / PgDn（chrome）");
        Check(app != null && app.Becomes == "單次 6 格；整頁 12 格", "per-app scroll row");
        Check(Find(rows2, RuleModel.CatAltMouse, "Alt + 滑鼠按鍵或滾輪").Becomes == "已關閉", "Alt mouse row off");

        RuleRow wheelOff = Find(rows2, RuleModel.CatWheel, "滾輪（同方向捲動中夾雜的反向小幅事件）");
        Check(wheelOff != null && wheelOff.Becomes == "已關閉" && wheelOff.Note == "真正的反向（超過門檻）仍會放行", "wheel row off");
        f.WheelFilter.Enabled = true;
        f.WheelFilter.WindowMs = 100;
        f.WheelFilter.ThresholdDelta = 240;
        Settings sw = Settings.Compile(f);
        RuleRow wheelOn = Find(RuleModel.BuildRows(sw, sw.Profile("full")), RuleModel.CatWheel, "滾輪（同方向捲動中夾雜的反向小幅事件）");
        Check(wheelOn != null && wheelOn.Becomes == "丟棄；時間窗 100 毫秒、位移門檻 240" && wheelOn.Note == "真正的反向（超過門檻）仍會放行", "wheel row on: " + (wheelOn == null ? "missing" : wheelOn.Becomes));
        f.WheelFilter.Enabled = false;

        f.Scroll.Enabled = false;
        Settings s3 = Settings.Compile(f);
        List<RuleRow> rows3 = RuleModel.BuildRows(s3, s3.Profile("full"));
        Check(Find(rows3, RuleModel.CatScroll, "PgUp / PgDn").Becomes == "已關閉" && Find(rows3, RuleModel.CatScroll, "PgUp / PgDn（chrome）") == null, "scroll off hides rows");
    }

    static RuleRow Find(List<RuleRow> rows, string cat, string press)
    {
        foreach (RuleRow r in rows)
            if (r.Category == cat && r.Press == press)
                return r;
        return null;
    }

    static void FormAssertions()
    {
        FakeHost host = new FakeHost();
        SettingsForm f = new SettingsForm(host);
        Check(host.Subscribers == 1, "form hooks Changed");
        f.Show();
        Application.DoEvents();
        f.Close();
        Check(host.Subscribers == 0, "form close unhooks Changed");
        Check(f.IsDisposed, "form disposed on close");
    }

    static string Labels(List<StripMarker> ms)
    {
        List<string> parts = new List<string>();
        foreach (StripMarker m in ms)
            parts.Add(m.Label + "@" + m.Pos);
        return string.Join(" | ", parts.ToArray());
    }

    static void StripAssertions()
    {
        string a = Labels(ScrollReferenceStrip.ComputeMarkers(8, 1, 30));
        Check(a == "1/4 ≈ 2 格@2 | 1/3 ≈ 3 格@3 | 1/2 ≈ 4 格@4 | 整頁 = 8 格@8", "markers for page 8: " + a);
        string b = Labels(ScrollReferenceStrip.ComputeMarkers(60, 1, 30));
        Check(b == "1/4 ≈ 15 格@15 | 1/3 ≈ 20 格@20 | 1/2 ≈ 30 格@30 | 整頁 = 60 格 →@30", "markers for page 60 (clamped + arrow): " + b);
        string c = Labels(ScrollReferenceStrip.ComputeMarkers(40, 1, 30));
        Check(c == "1/4 ≈ 10 格@10 | 1/3 ≈ 13 格@13 | 1/2 ≈ 20 格@20 | 整頁 = 40 格 →@30", "markers for page 40: " + c);
    }

    static void Settle(ScrollTuneForm f, FakeHost host, int before)
    {
        for (int i = 0; i < 100 && host.MutateCount == before; i++)
            Pump(20);
    }

    static void TuneAssertions()
    {
        FakeHost host = new FakeHost();
        ScrollTuneForm f = new ScrollTuneForm(host);
        Check(host.Subscribers == 1, "tune form hooks Changed");
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(40, 40);
        f.Show();
        Application.DoEvents();

        Check(f.StepTrack.Value == 4 && f.PageTrack.Value == 8, "sliders start at config 4 / 8");
        Check(f.StepTrack.Minimum == 1 && f.StepTrack.Maximum == 30 && f.PageTrack.Minimum == 2 && f.PageTrack.Maximum == 60, "slider ranges 1-30 / 2-60");
        Check(f.StepValueText == "4 格" && f.PageValueText == "8 格", "value labels: " + f.StepValueText + " / " + f.PageValueText);
        Check(f.BurstText == "連點第 3 下起捲整頁（間隔 400 毫秒內）；按住每秒最多 8 次", "burst line: " + f.BurstText);
        Check(f.AppsText == "各程式覆寫（請改設定檔）：（無）", "apps line: " + f.AppsText);

        int n = host.MutateCount;
        f.StepTrack.Value = 6;
        Check(f.StepValueText == "6 格", "value label follows slider immediately");
        Check(host.Settings.Scroll.StepTicks == 4 && host.MutateCount == n, "debounce: nothing written yet");
        f.StepTrack.Value = 9;
        Pump(100);
        f.StepTrack.Value = 10;
        Settle(f, host, n);
        Check(host.Settings.Scroll.StepTicks == 10, "step slider -> StepTicks " + host.Settings.Scroll.StepTicks);
        Check(host.Settings.Scroll.PageTicks == 8, "untouched PageTicks stays 8");
        Pump(500);
        Check(host.MutateCount == n + 1, "rapid moves coalesce into one write (" + (host.MutateCount - n) + ")");

        n = host.MutateCount;
        f.PageTrack.Value = 16;
        Settle(f, host, n);
        Check(host.Settings.Scroll.PageTicks == 16 && host.Settings.Scroll.StepTicks == 10, "page slider -> PageTicks 16");
        Check(f.Strip.PageTicks == 16, "strip follows page slider");
        Pump(500);
        Check(host.MutateCount == n + 1, "own change did not echo back into another write");

        n = host.MutateCount;
        host.Mutate(delegate(ConfigFile c)
        {
            c.Scroll = c.EffectiveScroll();
            c.Scroll.StepTicks = 12;
            c.Scroll.PageTicks = 24;
            ScrollAppConfig a = new ScrollAppConfig();
            a.StepTicks = 6;
            a.PageTicks = 12;
            c.Scroll.Apps["chrome"] = a;
        });
        Application.DoEvents();
        Check(f.StepTrack.Value == 12 && f.PageTrack.Value == 24, "host.Changed refreshes sliders to 12 / 24");
        Check(f.AppsText == "各程式覆寫（請改設定檔）：chrome 6/12", "apps line after change: " + f.AppsText);
        Pump(500);
        Check(host.MutateCount == n + 1, "refresh from host did not write back");

        host.Mutate(delegate(ConfigFile c)
        {
            c.Scroll.StepTicks = 0.5;
            c.Scroll.PageTicks = 90;
        });
        Application.DoEvents();
        Check(f.StepTrack.Value == 1 && f.PageTrack.Value == 60, "out-of-range config clamped for display only");
        Check(f.PageValueText.Contains("90"), "page label shows real config value: " + f.PageValueText);
        n = host.MutateCount;
        Pump(500);
        Check(host.MutateCount == n && host.Settings.Scroll.PageTicks == 90 && host.Settings.Scroll.StepTicks == 0.5, "clamped display never rewrites the config");

        f.StepTrack.Value = 3;
        Settle(f, host, n);
        Check(host.Settings.Scroll.StepTicks == 3 && host.Settings.Scroll.PageTicks == 90, "moving only step keeps the out-of-range page value");

        host.Mutate(delegate(ConfigFile c) { c.Scroll.Enabled = false; });
        Application.DoEvents();
        host.RejectMutationsWith = "scroll.stepTicks 測試拒絕。";
        n = host.MutateCount;
        f.StepTrack.Value = 7;
        Settle(f, host, n);
        Pump(50);
        Check(f.ErrorText.Contains("測試拒絕"), "ConfigException shown in window: " + f.ErrorText);
        Check(f.StepTrack.Value == 3, "slider reverted to the settings after rejection: " + f.StepTrack.Value);
        host.RejectMutationsWith = null;

        f.StepTrack.Value = 5;
        Check(f.Pending, "pending write before close");
        n = host.MutateCount;
        f.Close();
        Check(host.Settings.Scroll.StepTicks == 5 && host.MutateCount == n + 1, "close flushes a pending change");
        Check(host.Subscribers == 0, "tune form close unhooks Changed");
        Check(f.IsDisposed, "tune form disposed on close");
    }

    static void RenderIcons(string outDir)
    {
        int[] sizes = { 16, 20, 24, 32, 48 };
        const int zoom = 6;
        int width = 12;
        foreach (int s in sizes)
            width += s * zoom + 12;
        using (Bitmap sheet = new Bitmap(width, 48 * zoom * 2 + 36))
        using (Graphics g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.FromArgb(32, 32, 32));
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            for (int row = 0; row < 2; row++)
            {
                int x = 12;
                foreach (int s in sizes)
                {
                    using (Icon icon = TrayContext.DrawIcon(row == 0, new Size(s, s)))
                    using (Bitmap bmp = icon.ToBitmap())
                        g.DrawImage(bmp, new Rectangle(x, 12 + row * (48 * zoom + 12), s * zoom, s * zoom));
                    x += s * zoom + 12;
                }
            }
            string path = Path.Combine(outDir, "tray-icons.png");
            sheet.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine("RENDER " + path);
        }
        Check(true, "tray icon sheet rendered");
    }

    static void Render(string outDir, string profile, float scale)
    {
        FakeHost host = new FakeHost();
        SetScroll(host, 4, 8, true);
        host.Mutate(delegate(ConfigFile c)
        {
            c.WheelFilter.Enabled = true;
            c.WheelFilter.WindowMs = 100;
            c.WheelFilter.ThresholdDelta = 240;
        });
        SettingsForm f = new SettingsForm(host);
        if (scale != 1.0f)
        {
            f.Font = new Font("Microsoft JhengHei UI", 9f * scale);
            f.ClientSize = new Size((int)(1240 * scale), (int)(600 * scale));
        }
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(40, 40);
        f.SelectProfile(profile);
        f.Show();
        Application.DoEvents();
        f.PerformLayout();
        Application.DoEvents();
        string name = "settings-" + profile + (scale == 1.0f ? "" : "-" + (int)(scale * 100)) + ".png";
        Snap(f, outDir, name);
        ListView list = FindList(f);
        list.EnsureVisible(list.Items.Count - 1);
        Application.DoEvents();
        Snap(f, outDir, name.Replace(".png", "-bottom.png"));
        f.Close();
    }

    static ListView FindList(Control c)
    {
        foreach (Control child in c.Controls)
        {
            ListView lv = child as ListView;
            if (lv != null)
                return lv;
            lv = FindList(child);
            if (lv != null)
                return lv;
        }
        return null;
    }

    static void RenderTune(string outDir, string name, float scale, double page, double step, bool withApps)
    {
        FakeHost host = new FakeHost();
        SetScroll(host, step, page, withApps);
        ScrollTuneForm f = new ScrollTuneForm(host);
        if (scale != 1.0f)
        {
            f.Font = new Font("Microsoft JhengHei UI", 9f * scale);
            f.ClientSize = new Size((int)(620 * scale), (int)(720 * scale));
        }
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(40, 40);
        f.Show();
        Application.DoEvents();
        f.PerformLayout();
        Pump(100);
        Snap(f, outDir, name + ".png");
        f.Close();
    }

    static void Snap(Form f, string outDir, string name)
    {
        using (Bitmap bmp = new Bitmap(f.Width, f.Height))
        {
            f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
            string path = Path.Combine(outDir, name);
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine("RENDER " + path);
        }
    }
}
