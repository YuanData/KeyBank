using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Web.Script.Serialization;

namespace KeyBank.Core
{
    public class ConfigException : Exception
    {
        public ConfigException(string message) : base(message) { }
    }

    public class HalfPageConfig
    {
        public bool Enabled = true;
        public string Fraction = "1/2";
        public double PageTicks = 8;
    }

    public class ScrollAppConfig
    {
        public double StepTicks = 4;
        public double PageTicks = 8;
    }

    public class ScrollConfig
    {
        public bool Enabled = true;
        public double StepTicks = 4;
        public double PageTicks = 8;
        public int BurstTaps = 3;
        public int BurstIntervalMs = 400;
        public int HoldRepeatsPerSec = 8;
        public Dictionary<string, ScrollAppConfig> Apps = new Dictionary<string, ScrollAppConfig>();
    }

    public class WheelFilterConfig
    {
        public bool Enabled = false;
        public int WindowMs = WheelReversalFilter.DefaultWindowMs;
        public int ThresholdDelta = WheelReversalFilter.DefaultThresholdDelta;
    }

    public class DeviceRule
    {
        public string VendorId = "";
        public string Profile = "";
    }

    public class RemapRule
    {
        public string From = "";
        public string To = "";
    }

    public class TapHoldRule
    {
        public string Key = "";
        public int HoldMs = 500;
        public string Tap = "";
        public string Hold = "";
    }

    public class ProfileConfig
    {
        public string Description = "";
        public List<string> AltChords = new List<string>();
        public List<RemapRule> Remap = new List<RemapRule>();
        public List<TapHoldRule> TapHold = new List<TapHoldRule>();
    }

    public class ConfigFile
    {
        public bool Enabled = true;
        public string ActiveProfile = "auto";
        public string DefaultProfile = "full";
        public HalfPageConfig HalfPage;
        public ScrollConfig Scroll;
        public bool AltMouse = false;
        public WheelFilterConfig WheelFilter = new WheelFilterConfig();
        public List<string> ExcludeApps = new List<string>();
        public Dictionary<string, List<string>> AppAltChords;
        public List<DeviceRule> Devices = new List<DeviceRule>();
        public Dictionary<string, ProfileConfig> Profiles = new Dictionary<string, ProfileConfig>();

        // A file written before the scroll and appAltChords sections existed is read with defaults,
        // and ToJson then writes the new layout.
        public bool NeedsUpgrade
        {
            get { return Scroll == null || AppAltChords == null; }
        }

        public ScrollConfig EffectiveScroll()
        {
            if (Scroll != null)
                return Scroll;
            ScrollConfig s = new ScrollConfig();
            if (HalfPage != null)
            {
                s.Enabled = HalfPage.Enabled;
                s.PageTicks = HalfPage.PageTicks;
                s.StepTicks = Math.Max(1, Math.Round(HalfPage.PageTicks * Settings.ParseFraction(HalfPage.Fraction, "halfPage.fraction")));
            }
            return s;
        }

        public Dictionary<string, List<string>> EffectiveAppAltChords()
        {
            return AppAltChords ?? DefaultConfig.AppChords();
        }

        public static ConfigFile Parse(string json)
        {
            try
            {
                return new JavaScriptSerializer().Deserialize<ConfigFile>(json);
            }
            catch (ArgumentException ex)
            {
                throw new ConfigException("設定檔不是有效的 JSON：" + ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                throw new ConfigException("設定檔格式錯誤：" + ex.Message);
            }
        }

        public string ToJson()
        {
            Dictionary<string, object> profiles = new Dictionary<string, object>();
            foreach (KeyValuePair<string, ProfileConfig> p in Profiles)
            {
                Dictionary<string, object> profile = new Dictionary<string, object>();
                profile["description"] = p.Value.Description;
                profile["altChords"] = p.Value.AltChords;
                profile["remap"] = p.Value.Remap.ConvertAll<object>(delegate(RemapRule r)
                {
                    Dictionary<string, object> d = new Dictionary<string, object>();
                    d["from"] = r.From;
                    d["to"] = r.To;
                    return d;
                });
                profile["tapHold"] = p.Value.TapHold.ConvertAll<object>(delegate(TapHoldRule t)
                {
                    Dictionary<string, object> d = new Dictionary<string, object>();
                    d["key"] = t.Key;
                    d["holdMs"] = t.HoldMs;
                    d["tap"] = t.Tap;
                    d["hold"] = t.Hold;
                    return d;
                });
                profiles[p.Key] = profile;
            }

            ScrollConfig sc = EffectiveScroll();
            Dictionary<string, object> apps = new Dictionary<string, object>();
            foreach (KeyValuePair<string, ScrollAppConfig> a in sc.Apps)
            {
                Dictionary<string, object> app = new Dictionary<string, object>();
                app["stepTicks"] = a.Value.StepTicks;
                app["pageTicks"] = a.Value.PageTicks;
                apps[a.Key] = app;
            }
            Dictionary<string, object> scroll = new Dictionary<string, object>();
            scroll["enabled"] = sc.Enabled;
            scroll["stepTicks"] = sc.StepTicks;
            scroll["pageTicks"] = sc.PageTicks;
            scroll["burstTaps"] = sc.BurstTaps;
            scroll["burstIntervalMs"] = sc.BurstIntervalMs;
            scroll["holdRepeatsPerSec"] = sc.HoldRepeatsPerSec;
            scroll["apps"] = apps;

            Dictionary<string, object> root = new Dictionary<string, object>();
            root["enabled"] = Enabled;
            root["activeProfile"] = ActiveProfile;
            root["defaultProfile"] = DefaultProfile;
            root["scroll"] = scroll;
            root["altMouse"] = AltMouse;
            WheelFilterConfig wf = WheelFilter ?? new WheelFilterConfig();
            Dictionary<string, object> wheel = new Dictionary<string, object>();
            wheel["enabled"] = wf.Enabled;
            wheel["windowMs"] = wf.WindowMs;
            wheel["thresholdDelta"] = wf.ThresholdDelta;
            root["wheelFilter"] = wheel;
            root["excludeApps"] = ExcludeApps;
            root["appAltChords"] = EffectiveAppAltChords();
            root["devices"] = Devices.ConvertAll<object>(delegate(DeviceRule d)
            {
                Dictionary<string, object> m = new Dictionary<string, object>();
                m["vendorId"] = d.VendorId;
                m["profile"] = d.Profile;
                return m;
            });
            root["profiles"] = profiles;
            return JsonText.Pretty(new JavaScriptSerializer().Serialize(root));
        }
    }

    public static class JsonText
    {
        public static string Pretty(string compact)
        {
            StringBuilder sb = new StringBuilder();
            int depth = 0;
            bool inString = false;
            for (int i = 0; i < compact.Length; i++)
            {
                char c = compact[i];
                if (inString)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < compact.Length)
                        sb.Append(compact[++i]);
                    else if (c == '"')
                        inString = false;
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inString = true;
                        sb.Append(c);
                        break;
                    case '{':
                    case '[':
                        if (i + 1 < compact.Length && (compact[i + 1] == '}' || compact[i + 1] == ']'))
                        {
                            sb.Append(c).Append(compact[++i]);
                            break;
                        }
                        sb.Append(c).AppendLine();
                        depth++;
                        sb.Append(' ', depth * 2);
                        break;
                    case '}':
                    case ']':
                        sb.AppendLine();
                        depth--;
                        sb.Append(' ', depth * 2).Append(c);
                        break;
                    case ',':
                        sb.Append(c).AppendLine();
                        sb.Append(' ', depth * 2);
                        break;
                    case ':':
                        sb.Append(": ");
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }

    public sealed class TapHoldRuntime
    {
        public int Key;
        public int HoldMs;
        public int Tap;
        public int Hold;
    }

    public sealed class ProfileRuntime
    {
        public string Name;
        public string Description;
        public HashSet<int> AltChords = new HashSet<int>();
        public Dictionary<int, int> Remap = new Dictionary<int, int>();
        public Dictionary<int, TapHoldRuntime> TapHold = new Dictionary<int, TapHoldRuntime>();
    }

    public sealed class ScrollRuntime
    {
        public bool Enabled;
        public double StepTicks;
        public double PageTicks;
        public int BurstTaps;
        public int BurstIntervalMs;
        public int HoldRepeatsPerSec;
        public Dictionary<string, ScrollAppConfig> Apps = new Dictionary<string, ScrollAppConfig>();

        public void TicksFor(string app, out double step, out double page)
        {
            ScrollAppConfig a;
            if (app != null && Apps.TryGetValue(Settings.AppKey(app), out a))
            {
                step = a.StepTicks;
                page = a.PageTicks;
                return;
            }
            step = StepTicks;
            page = PageTicks;
        }
    }

    public sealed class WheelFilterRuntime
    {
        public bool Enabled;
        public int WindowMs;
        public int ThresholdDelta;
    }

    public sealed class Settings
    {
        public ConfigFile File;
        public bool Enabled;
        public bool Upgraded;
        public string ActiveProfile;
        public string DefaultProfile;
        public ScrollRuntime Scroll;
        public bool AltMouse;
        public WheelFilterRuntime WheelFilter;
        public HashSet<string> ExcludeApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, HashSet<int>> AppChords = new Dictionary<string, HashSet<int>>();
        public List<DeviceRule> Devices;
        public Dictionary<string, ProfileRuntime> Profiles = new Dictionary<string, ProfileRuntime>();

        public static Settings Load(string json)
        {
            return Compile(ConfigFile.Parse(json));
        }

        public static string AppKey(string exe)
        {
            string k = (exe ?? "").Trim().ToLowerInvariant();
            return k.EndsWith(".exe") ? k.Substring(0, k.Length - 4) : k;
        }

        public static Settings Compile(ConfigFile f)
        {
            if (f == null)
                throw new ConfigException("設定檔是空的。");

            Settings s = new Settings();
            s.File = f;
            s.Enabled = f.Enabled;
            s.Upgraded = f.NeedsUpgrade;
            s.ActiveProfile = f.ActiveProfile ?? "auto";
            s.DefaultProfile = f.DefaultProfile ?? "";
            s.AltMouse = f.AltMouse;
            s.WheelFilter = CompileWheelFilter(f.WheelFilter ?? new WheelFilterConfig());
            s.Devices = f.Devices ?? new List<DeviceRule>();
            s.Scroll = CompileScroll(f.EffectiveScroll());

            if (f.ExcludeApps != null)
                foreach (string app in f.ExcludeApps)
                    s.ExcludeApps.Add(app);

            foreach (KeyValuePair<string, List<string>> a in f.EffectiveAppAltChords())
            {
                HashSet<int> keys = new HashSet<int>();
                for (int i = 0; i < a.Value.Count; i++)
                    keys.Add(ParseKey(a.Value[i], "appAltChords." + a.Key + "[" + i + "]"));
                s.AppChords[AppKey(a.Key)] = keys;
            }

            if (f.Profiles == null || f.Profiles.Count == 0)
                throw new ConfigException("profiles 至少要有一組設定。");
            foreach (KeyValuePair<string, ProfileConfig> p in f.Profiles)
                s.Profiles[p.Key] = CompileProfile(p.Key, p.Value);

            if (!s.Profiles.ContainsKey(s.DefaultProfile))
                throw new ConfigException("defaultProfile '" + s.DefaultProfile + "' 不在 profiles 裡。");
            if (s.ActiveProfile != "auto" && !s.Profiles.ContainsKey(s.ActiveProfile))
                throw new ConfigException("activeProfile '" + s.ActiveProfile + "' 不在 profiles 裡。");
            for (int i = 0; i < s.Devices.Count; i++)
                if (!s.Profiles.ContainsKey(s.Devices[i].Profile))
                    throw new ConfigException("devices[" + i + "].profile '" + s.Devices[i].Profile + "' 不在 profiles 裡。");

            return s;
        }

        public ProfileRuntime Profile(string name)
        {
            return Profiles[name];
        }

        public HashSet<int> AppChordsFor(string app)
        {
            HashSet<int> keys;
            return app != null && AppChords.TryGetValue(AppKey(app), out keys) ? keys : null;
        }

        public string ResolveProfile(string vendorId)
        {
            if (ActiveProfile != "auto")
                return ActiveProfile;
            if (vendorId != null)
                foreach (DeviceRule d in Devices)
                    if (string.Equals(d.VendorId, vendorId, StringComparison.OrdinalIgnoreCase))
                        return d.Profile;
            return DefaultProfile;
        }

        static void CheckTicks(double value, string path)
        {
            if (value <= 0 || value > 100)
                throw new ConfigException(path + " 必須大於 0 且不超過 100。");
        }

        static WheelFilterRuntime CompileWheelFilter(WheelFilterConfig c)
        {
            if (c.WindowMs < 10 || c.WindowMs > 500)
                throw new ConfigException("wheelFilter.windowMs 必須介於 10 到 500。");
            if (c.ThresholdDelta < 1 || c.ThresholdDelta > 1200)
                throw new ConfigException("wheelFilter.thresholdDelta 必須介於 1 到 1200。");
            WheelFilterRuntime r = new WheelFilterRuntime();
            r.Enabled = c.Enabled;
            r.WindowMs = c.WindowMs;
            r.ThresholdDelta = c.ThresholdDelta;
            return r;
        }

        static ScrollRuntime CompileScroll(ScrollConfig c)
        {
            CheckTicks(c.StepTicks, "scroll.stepTicks");
            CheckTicks(c.PageTicks, "scroll.pageTicks");
            if (c.BurstTaps < 2 || c.BurstTaps > 10)
                throw new ConfigException("scroll.burstTaps 必須介於 2 到 10。");
            if (c.BurstIntervalMs < 100 || c.BurstIntervalMs > 2000)
                throw new ConfigException("scroll.burstIntervalMs 必須介於 100 到 2000。");
            if (c.HoldRepeatsPerSec < 1 || c.HoldRepeatsPerSec > 30)
                throw new ConfigException("scroll.holdRepeatsPerSec 必須介於 1 到 30。");

            ScrollRuntime r = new ScrollRuntime();
            r.Enabled = c.Enabled;
            r.StepTicks = c.StepTicks;
            r.PageTicks = c.PageTicks;
            r.BurstTaps = c.BurstTaps;
            r.BurstIntervalMs = c.BurstIntervalMs;
            r.HoldRepeatsPerSec = c.HoldRepeatsPerSec;
            if (c.Apps != null)
                foreach (KeyValuePair<string, ScrollAppConfig> a in c.Apps)
                {
                    CheckTicks(a.Value.StepTicks, "scroll.apps." + a.Key + ".stepTicks");
                    CheckTicks(a.Value.PageTicks, "scroll.apps." + a.Key + ".pageTicks");
                    r.Apps[AppKey(a.Key)] = a.Value;
                }
            return r;
        }

        internal static double ParseFraction(string text, string path)
        {
            string[] parts = (text ?? "").Split('/');
            int num, den;
            if (parts.Length != 2
                || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out num)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out den)
                || num <= 0 || den <= 0 || num > den)
                throw new ConfigException(path + " '" + text + "' 格式要像 1/2、1/3。");
            return (double)num / den;
        }

        static int ParseKey(string name, string path)
        {
            int vk;
            if (!KeyNames.TryParse(name, out vk))
                throw new ConfigException(path + "：不認得按鍵名稱 '" + name + "'。");
            return vk;
        }

        static ProfileRuntime CompileProfile(string name, ProfileConfig c)
        {
            string at = "profiles." + name;
            ProfileRuntime p = new ProfileRuntime();
            p.Name = name;
            p.Description = c.Description ?? name;

            if (c.AltChords != null)
                for (int i = 0; i < c.AltChords.Count; i++)
                    p.AltChords.Add(ParseKey(c.AltChords[i], at + ".altChords[" + i + "]"));

            if (c.Remap != null)
                for (int i = 0; i < c.Remap.Count; i++)
                {
                    string r = at + ".remap[" + i + "]";
                    int from = ParseKey(c.Remap[i].From, r + ".from");
                    int to = ParseKey(c.Remap[i].To, r + ".to");
                    if (p.Remap.ContainsKey(from))
                        throw new ConfigException(r + ".from：" + c.Remap[i].From + " 重複對應。");
                    p.Remap[from] = to;
                }

            if (c.TapHold != null)
                for (int i = 0; i < c.TapHold.Count; i++)
                {
                    string r = at + ".tapHold[" + i + "]";
                    TapHoldRule t = c.TapHold[i];
                    if (t.HoldMs < 100 || t.HoldMs > 5000)
                        throw new ConfigException(r + ".holdMs 必須介於 100 到 5000。");
                    TapHoldRuntime rt = new TapHoldRuntime();
                    rt.Key = ParseKey(t.Key, r + ".key");
                    rt.Tap = ParseKey(t.Tap, r + ".tap");
                    rt.Hold = ParseKey(t.Hold, r + ".hold");
                    rt.HoldMs = t.HoldMs;
                    p.TapHold[rt.Key] = rt;
                }

            return p;
        }
    }

    public static class DefaultConfig
    {
        public static Dictionary<string, List<string>> AppChords()
        {
            Dictionary<string, List<string>> d = new Dictionary<string, List<string>>();
            d["chrome.exe"] = new List<string>(new string[]
            {
                "W", "T", "N", "R", "L", "D",
                "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "Equals", "Minus"
            });
            return d;
        }

        public const string Json = @"{
  ""enabled"": true,
  ""activeProfile"": ""auto"",
  ""defaultProfile"": ""full"",
  ""scroll"": {
    ""enabled"": true,
    ""stepTicks"": 4,
    ""pageTicks"": 8,
    ""burstTaps"": 3,
    ""burstIntervalMs"": 400,
    ""holdRepeatsPerSec"": 8,
    ""apps"": {}
  },
  ""altMouse"": false,
  ""wheelFilter"": { ""enabled"": false, ""windowMs"": 80, ""thresholdDelta"": 120 },
  ""excludeApps"": [],
  ""appAltChords"": {
    ""chrome.exe"": [""W"", ""T"", ""N"", ""R"", ""L"", ""D"", ""1"", ""2"", ""3"", ""4"", ""5"", ""6"", ""7"", ""8"", ""9"", ""0"", ""Equals"", ""Minus""]
  },
  ""devices"": [
    { ""vendorId"": ""3434"", ""profile"": ""full"" },
    { ""vendorId"": ""046D"", ""profile"": ""mini"" }
  ],
  ""profiles"": {
    ""full"": {
      ""description"": ""完整鍵盤"",
      ""altChords"": [""C"", ""X"", ""V"", ""A"", ""Z"", ""S"", ""F""],
      ""remap"": [],
      ""tapHold"": []
    },
    ""mini"": {
      ""description"": ""迷你鍵盤"",
      ""altChords"": [""C"", ""X"", ""V"", ""A"", ""Z"", ""S"", ""F""],
      ""remap"": [
        { ""from"": ""PageUp"", ""to"": ""Home"" },
        { ""from"": ""PageDown"", ""to"": ""PageUp"" },
        { ""from"": ""Multiply"", ""to"": ""End"" },
        { ""from"": ""Subtract"", ""to"": ""PageDown"" }
      ],
      ""tapHold"": [
        { ""key"": ""Home"", ""holdMs"": 500, ""tap"": ""Home"", ""hold"": ""PrintScreen"" }
      ]
    }
  }
}";
    }
}
