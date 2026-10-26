using System;
using System.Collections.Generic;
using System.Globalization;
using KeyBank.Core;

namespace KeyBank.Ui
{
    public sealed class RuleRow
    {
        public string Category;
        public string Press;
        public string Becomes;
        public string Note;
        // List header when it differs from Category (per-app Alt chords).
        public string Group;
    }

    public sealed class ClusterCell
    {
        public int Physical;
        public int Output;
        public bool Fixed;
        public string Label;
        public string AfterLabel;
        public bool Changed;
        public string Badge;
    }

    public static class RuleModel
    {
        public const string CatAlt = "Alt 手勢";
        public const string CatRemap = "位置對調";
        public const string CatHold = "長按";
        public const string CatScroll = "分段捲動";
        public const string CatAltMouse = "Alt 加滑鼠";
        public const string CatWheel = "滾輪";
        public const string CatExclude = "排除的程式";

        public static string TickText(double ticks)
        {
            return ticks.ToString("0.#", CultureInfo.InvariantCulture);
        }

        // Letters first, then digits, then Equals and Minus.
        static int KeyOrder(int vk)
        {
            if (vk >= 'A' && vk <= 'Z') return vk;
            if (vk >= '0' && vk <= '9') return 1000 + vk;
            return 2000 + vk;
        }

        public static string ShortLabel(int vk)
        {
            switch (vk)
            {
                case 0xBB: return "=";
                case 0xBD: return "-";
                default: return KeyNames.NameOf(vk);
            }
        }

        static string AppTitle(string key)
        {
            return key.Length == 0 ? key : char.ToUpperInvariant(key[0]) + key.Substring(1);
        }

        public static string CapLabel(int vk)
        {
            switch (vk)
            {
                case Vk.PageUp: return "PgUp";
                case Vk.PageDown: return "PgDn";
                case Vk.Multiply: return "*";
                case Vk.Subtract: return "-";
                case Vk.Divide: return "/";
                case Vk.Add: return "+";
                case Vk.PrintScreen: return "PrtScr";
                default: return KeyNames.NameOf(vk);
            }
        }

        public static string ScrollSummary(Settings s)
        {
            ScrollRuntime sc = s.Scroll;
            return sc.Enabled ? "單次 " + TickText(sc.StepTicks) + " 格 / 整頁 " + TickText(sc.PageTicks) + " 格" : "分段捲動 關閉";
        }

        public static ClusterCell[][] BuildCluster(ProfileRuntime p)
        {
            int[] top = { Vk.Home, Vk.End, Vk.PageUp, Vk.PageDown };
            int[] bottom = { -1, Vk.Divide, Vk.Multiply, Vk.Subtract };
            ClusterCell[][] rows = new ClusterCell[2][];
            rows[0] = new ClusterCell[4];
            rows[1] = new ClusterCell[4];
            for (int i = 0; i < 4; i++)
            {
                rows[0][i] = Cell(p, top[i], false);
                rows[1][i] = bottom[i] < 0 ? NumLockCell() : Cell(p, bottom[i], bottom[i] == Vk.Divide);
            }
            return rows;
        }

        static ClusterCell NumLockCell()
        {
            ClusterCell c = new ClusterCell();
            c.Fixed = true;
            c.Label = "NumLk";
            c.AfterLabel = "NumLk";
            return c;
        }

        static ClusterCell Cell(ProfileRuntime p, int vk, bool isFixed)
        {
            ClusterCell c = new ClusterCell();
            c.Physical = vk;
            c.Output = vk;
            c.Fixed = isFixed;
            int mapped;
            if (!isFixed && p.Remap.TryGetValue(vk, out mapped))
                c.Output = mapped;
            c.Changed = c.Output != vk;
            c.Label = CapLabel(vk);
            c.AfterLabel = CapLabel(c.Output);
            TapHoldRuntime t;
            if (!isFixed && p.TapHold.TryGetValue(vk, out t))
                c.Badge = "長按 " + t.HoldMs.ToString(CultureInfo.InvariantCulture) + "ms → " + CapLabel(t.Hold);
            return c;
        }

        public static List<RuleRow> BuildRows(Settings s, ProfileRuntime p)
        {
            List<RuleRow> rows = new List<RuleRow>();

            List<int> chords = new List<int>(p.AltChords);
            ProfileConfig raw;
            if (s.File != null && s.File.Profiles != null && s.File.Profiles.TryGetValue(p.Name, out raw) && raw.AltChords != null)
            {
                chords.Clear();
                foreach (string name in raw.AltChords)
                {
                    int vk;
                    if (KeyNames.TryParse(name, out vk))
                        chords.Add(vk);
                }
            }
            for (int i = 0; i < chords.Count; i++)
            {
                string k = ShortLabel(chords[i]);
                rows.Add(Row(CatAlt, "Alt+" + k, "Ctrl+" + k,
                    i == 0 ? "Shift 照常，例如 Alt+Shift+Z 變 Ctrl+Shift+Z" : ""));
            }

            List<string> appKeys = new List<string>(s.AppChords.Keys);
            appKeys.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string app in appKeys)
            {
                List<int> keys = new List<int>(s.AppChords[app]);
                keys.Sort(delegate(int a, int b) { return KeyOrder(a).CompareTo(KeyOrder(b)); });
                string header = AppTitle(app) + "（" + app + ".exe）專用的 Alt 手勢";
                foreach (int vk in keys)
                {
                    RuleRow r = Row(CatAlt, "Alt+" + ShortLabel(vk), "Ctrl+" + ShortLabel(vk), "");
                    r.Group = header;
                    rows.Add(r);
                }
            }

            ClusterCell[][] cl = BuildCluster(p);
            foreach (ClusterCell[] line in cl)
                foreach (ClusterCell c in line)
                    if (c.Changed)
                        rows.Add(Row(CatRemap, "實體 " + c.Label, c.AfterLabel, ""));
            foreach (KeyValuePair<int, int> kv in p.Remap)
                if (!IsInCluster(cl, kv.Key))
                    rows.Add(Row(CatRemap, "實體 " + CapLabel(kv.Key), CapLabel(kv.Value), ""));

            foreach (TapHoldRuntime t in p.TapHold.Values)
            {
                string key = KeyNames.NameOf(t.Key);
                rows.Add(Row(CatHold, key + " 短按", KeyNames.NameOf(t.Tap), ""));
                rows.Add(Row(CatHold, "長按 " + t.HoldMs.ToString(CultureInfo.InvariantCulture) + "ms", KeyNames.NameOf(t.Hold), ""));
            }

            ScrollRuntime sc = s.Scroll;
            if (sc.Enabled)
            {
                rows.Add(Row(CatScroll, "PgUp / PgDn",
                    "單次 " + TickText(sc.StepTicks) + " 格；連點第 " + sc.BurstTaps.ToString(CultureInfo.InvariantCulture)
                    + " 下起整頁 " + TickText(sc.PageTicks) + " 格；按住每秒最多 " + sc.HoldRepeatsPerSec.ToString(CultureInfo.InvariantCulture) + " 次",
                    "連點間隔 " + sc.BurstIntervalMs.ToString(CultureInfo.InvariantCulture) + " 毫秒內"));
                List<string> scrollApps = new List<string>(sc.Apps.Keys);
                scrollApps.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string app in scrollApps)
                {
                    ScrollAppConfig a = sc.Apps[app];
                    rows.Add(Row(CatScroll, "PgUp / PgDn（" + app + "）",
                        "單次 " + TickText(a.StepTicks) + " 格；整頁 " + TickText(a.PageTicks) + " 格", "此程式覆寫"));
                }
            }
            else
                rows.Add(Row(CatScroll, "PgUp / PgDn", "已關閉", ""));

            rows.Add(s.AltMouse
                ? Row(CatAltMouse, "Alt + 滑鼠按鍵或滾輪", "Ctrl + 同一個動作", "")
                : Row(CatAltMouse, "Alt + 滑鼠按鍵或滾輪", "已關閉", ""));

            WheelFilterRuntime wf = s.WheelFilter;
            rows.Add(wf.Enabled
                ? Row(CatWheel, "滾輪（同方向捲動中夾雜的反向小幅事件）",
                    "丟棄；時間窗 " + wf.WindowMs.ToString(CultureInfo.InvariantCulture) + " 毫秒、位移門檻 " + wf.ThresholdDelta.ToString(CultureInfo.InvariantCulture),
                    "真正的反向（超過門檻）仍會放行")
                : Row(CatWheel, "滾輪（同方向捲動中夾雜的反向小幅事件）", "已關閉", "真正的反向（超過門檻）仍會放行"));

            WheelAccelRuntime wa = s.WheelAccel;
            rows.Add(wa.Enabled
                ? Row(CatWheel, "滾輪（持續轉動時）",
                    "加速；間隔不超過 " + wa.GapMs.ToString(CultureInfo.InvariantCulture) + " 毫秒算連續，上限 " + wa.MaxMultiplier.ToString("0.##", CultureInfo.InvariantCulture) + " 倍",
                    "累積格數與持續時間各加一段倍率；停頓或反向即重算")
                : Row(CatWheel, "滾輪（持續轉動時）", "已關閉", "累積格數與持續時間各加一段倍率；停頓或反向即重算"));

            if (s.ExcludeApps.Count == 0)
                rows.Add(Row(CatExclude, "（無）", "", ""));
            else
            {
                List<string> apps = new List<string>(s.ExcludeApps);
                apps.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string a in apps)
                    rows.Add(Row(CatExclude, a, "KeyBank 不處理", ""));
            }
            return rows;
        }

        static bool IsInCluster(ClusterCell[][] cl, int vk)
        {
            foreach (ClusterCell[] line in cl)
                foreach (ClusterCell c in line)
                    if (!c.Fixed && c.Physical == vk)
                        return true;
            return false;
        }

        static RuleRow Row(string cat, string press, string becomes, string note)
        {
            RuleRow r = new RuleRow();
            r.Category = cat;
            r.Press = press;
            r.Becomes = becomes;
            r.Note = note;
            return r;
        }
    }
}
