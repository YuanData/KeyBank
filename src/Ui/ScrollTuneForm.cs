using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using KeyBank.Core;

namespace KeyBank.Ui
{
    public sealed class ScrollTuneForm : Form
    {
        internal const int StepMin = 1;
        internal const int StepMax = 30;
        internal const int PageMin = 2;
        internal const int PageMax = 60;

        readonly IEngineHost host;
        readonly TrackBar stepTrack = new TrackBar();
        readonly TrackBar pageTrack = new TrackBar();
        readonly Label stepValue = new Label();
        readonly Label pageValue = new Label();
        readonly ScrollReferenceStrip strip = new ScrollReferenceStrip();
        readonly Label burstLabel = new Label();
        readonly Label appsLabel = new Label();
        readonly Label offLabel = new Label();
        readonly Label errorLabel = new Label();
        readonly TextBox testBox = new TextBox();
        readonly Timer debounce = new Timer();
        readonly List<Label> wrapping = new List<Label>();
        readonly List<Label> bold = new List<Label>();
        TableLayoutPanel root;
        string stepNote = "";
        string pageNote = "";
        bool refreshing;
        bool stepDirty;
        bool pageDirty;
        bool applying;
        bool unhooked;

        public ScrollTuneForm(IEngineHost host)
        {
            this.host = host;

            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            Font = new Font("Microsoft JhengHei UI", 9f);
            Text = "KeyBank 捲動微調";
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            MinimumSize = new Size(520, 600);
            ClientSize = new Size(620, 720);

            BuildLayout();

            debounce.Interval = 250;
            debounce.Tick += delegate { debounce.Stop(); ApplyPending(); };
            stepTrack.ValueChanged += delegate { OnSliderMoved(true); };
            pageTrack.ValueChanged += delegate { OnSliderMoved(false); };

            host.Changed += OnHostChanged;
            Reload(true);
        }

        internal TrackBar StepTrack { get { return stepTrack; } }
        internal TrackBar PageTrack { get { return pageTrack; } }
        internal ScrollReferenceStrip Strip { get { return strip; } }
        internal string StepValueText { get { return stepValue.Text; } }
        internal string PageValueText { get { return pageValue.Text; } }
        internal string BurstText { get { return burstLabel.Text; } }
        internal string AppsText { get { return appsLabel.Text; } }
        internal string ErrorText { get { return errorLabel.Visible ? errorLabel.Text : ""; } }
        internal bool Pending { get { return debounce.Enabled; } }

        void BuildLayout()
        {
            root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.Padding = new Padding(14);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            Controls.Add(root);

            Label intro = WrapLabel("拖曳滑桿調整 PgUp / PgDn 捲動的滾輪格數，放開約 0.25 秒後自動存檔。單次按一下捲「單次格數」，連點到第幾下起改捲「整頁格數」。");
            AddRow(intro, new Padding(0, 0, 0, 8));

            AddRow(Header("單次格數", stepValue), new Padding(0, 4, 0, 0));
            ConfigureTrack(stepTrack, StepMin, StepMax);
            stepTrack.TickStyle = TickStyle.BottomRight;
            stepTrack.TickFrequency = 1;
            AddRow(stepTrack, Padding.Empty);
            strip.Dock = DockStyle.Fill;
            strip.Margin = new Padding(0, 0, 0, 10);
            strip.Attach(stepTrack, StepMin, StepMax);
            AddRow(strip, Padding.Empty);

            AddRow(Header("整頁格數", pageValue), new Padding(0, 4, 0, 0));
            ConfigureTrack(pageTrack, PageMin, PageMax);
            pageTrack.TickStyle = TickStyle.None;
            AddRow(pageTrack, new Padding(0, 0, 0, 8));

            burstLabel.Text = "";
            burstLabel.AutoSize = true;
            wrapping.Add(burstLabel);
            AddRow(burstLabel, new Padding(0, 2, 0, 2));
            appsLabel.AutoSize = true;
            wrapping.Add(appsLabel);
            AddRow(appsLabel, new Padding(0, 2, 0, 2));

            offLabel.Text = "分段捲動目前是關閉的，滑桿的設定仍會存檔，開啟後才會生效。";
            offLabel.AutoSize = true;
            offLabel.ForeColor = Color.DarkOrange;
            wrapping.Add(offLabel);
            AddRow(offLabel, new Padding(0, 2, 0, 2));
            errorLabel.AutoSize = true;
            errorLabel.ForeColor = Color.Firebrick;
            wrapping.Add(errorLabel);
            AddRow(errorLabel, new Padding(0, 2, 0, 2));

            Label caption = WrapLabel("把滑鼠放在下面的文字上，按 PgUp、PgDn 或連點試手感");
            bold.Add(caption);
            caption.Font = new Font(Font, FontStyle.Bold);
            AddRow(caption, new Padding(0, 10, 0, 4));

            testBox.Multiline = true;
            testBox.ReadOnly = true;
            testBox.ScrollBars = ScrollBars.Vertical;
            testBox.BackColor = SystemColors.Window;
            testBox.Dock = DockStyle.Fill;
            testBox.Text = SampleText();
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowCount = root.RowStyles.Count;
            root.Controls.Add(testBox, 0, root.RowCount - 1);

            SizeChanged += delegate { FitWrapping(); };
            FitWrapping();
        }

        Label WrapLabel(string text)
        {
            Label l = new Label();
            l.AutoSize = true;
            l.Text = text;
            wrapping.Add(l);
            return l;
        }

        Control Header(string title, Label value)
        {
            FlowLayoutPanel row = new FlowLayoutPanel();
            row.AutoSize = true;
            row.WrapContents = false;
            row.Margin = Padding.Empty;
            Label name = new Label();
            name.AutoSize = true;
            name.Text = title;
            bold.Add(name);
            name.Font = new Font(Font, FontStyle.Bold);
            name.Margin = new Padding(0, 3, 12, 3);
            value.AutoSize = true;
            value.Margin = new Padding(0, 3, 0, 3);
            row.Controls.Add(name);
            row.Controls.Add(value);
            return row;
        }

        static void ConfigureTrack(TrackBar t, int min, int max)
        {
            t.Minimum = min;
            t.Maximum = max;
            t.Value = min;
            t.Dock = DockStyle.Fill;
            t.LargeChange = 5;
            t.SmallChange = 1;
            t.Margin = Padding.Empty;
        }

        void AddRow(Control c, Padding margin)
        {
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowCount = root.RowStyles.Count;
            c.Margin = c is TrackBar || c is ScrollReferenceStrip ? c.Margin : margin;
            if (!(c is TrackBar) && !(c is ScrollReferenceStrip))
                c.Dock = DockStyle.Fill;
            root.Controls.Add(c, 0, root.RowCount - 1);
        }

        // Wrapped labels need an explicit maximum width inside an auto-sized table row.
        void FitWrapping()
        {
            int w = ClientSize.Width - root.Padding.Horizontal;
            if (w <= 0)
                return;
            foreach (Label l in wrapping)
                l.MaximumSize = new Size(w, 0);
        }

        // Every paragraph is different on purpose, so how far a keypress moved is visible by what scrolled past.
        static string SampleText()
        {
            return string.Join("\r\n", new string[]
            {
                "七天旅行手記：從海邊到山城",
                "",
                "第一天　海風",
                "清晨五點的漁港還沒完全醒來，冰塊碰撞木箱的聲音從遠處傳來。",
                "我拖著行李站在碼頭邊，不確定該往左還是往右。",
                "一位正在補網的阿嬤抬頭看了我一眼，什麼也沒問，只用下巴指了指右邊那排鐵皮屋，說民宿在那後面，不用招牌，看到曬著藍色漁網的就是。",
                "果然。",
                "民宿老闆是個話不多的中年人，遞給我一把鑰匙，鑰匙圈是一顆磨得發亮的貝殼。他說熱水要等十分鐘，早餐在七點，不吃也沒關係，冰箱裡有水果，自己拿。",
                "房間不大，一張床、一張小桌、一扇對著海的窗。我把窗推開，鹹味立刻灌進來，帶著一點柴油和曬乾海藻混在一起的味道。",
                "",
                "中午我沿著堤防走到燈塔。",
                "燈塔下有三個釣客，各自坐在自己的小板凳上，彼此隔著剛好說不上話的距離。其中一位戴斗笠的伯伯桶子裡只有兩條銀白色的小魚，他說今天不是時候，潮不對。我問什麼時候才對，他想了很久，說：看天。",
                "回程繞進一條巷子，買了一支芋頭冰。老闆娘找錢時多塞給我一顆糖，說是剛從外孫那邊搶來的。",
                "",
                "第二天　公車",
                "往山城的公車一天只有四班。",
                "我搭的是九點十分那班。",
                "車上除了我，只有一對提著菜市場塑膠袋的老夫妻，和一個把耳機音量開得很大的高中生。司機把冷氣開到最強，又在每個彎道前很有禮貌地按一聲喇叭，像是在對山說：我來了，請讓一讓。",
                "窗外的風景先是整片的檳榔樹，然後是稻田，然後是一段長長的、被竹林夾住的上坡路。竹子在風裡互相摩擦，聲音很像下雨，但天是晴的。",
                "高中生在第三站下車。老夫妻在第六站下車，臨走前阿公還特地回頭對我點了點頭。",
                "後半段只剩我一個人。司機忽然開口：你是去看老街的？我說是。他說老街現在沒什麼人，要看就要趁現在。",
                "我問為什麼。他說：政府要修。",
                "修好以後會比較好看嗎？",
                "他從後視鏡看了我一眼，笑了笑，沒有回答。",
                "",
                "第三天　老街",
                "老街其實只有兩百公尺長，一頭是廟，一頭是已經停用的郵局。",
                "兩邊的店面大多拉著鐵門，只有三、四間開著。一間賣豆花，一間修鞋，一間什麼都賣，從針線到電風扇到泡麵都有，門口還擺著一台會自己轉頭的塑膠玩具熊。",
                "我在豆花店坐了很久。店裡沒有菜單，只有牆上用紅色油漆寫的兩行字：",
                "　　豆花　三十",
                "　　加料　每樣十元",
                "我加了花生和粉圓，老闆端出來的時候，花生滿到溢出碗邊。",
                "他說：你一個人？",
                "我說：嗯。",
                "他說：一個人也要吃飽。",
                "",
                "下午我走進那間停用的郵局。門沒有鎖，櫃台上積了薄薄一層灰，牆上的日曆停在一九九八年三月。",
                "抽屜裡有一疊沒有寄出去的明信片，圖案是這座山城的舊照片：廟、橋、一棵我不認得的大樹。我挑了一張，在背面寫上今天的日期，想了很久，還是不知道要寄給誰。",
                "最後把它放進了自己的口袋。",
                "",
                "第四天　雨",
                "夜裡開始下雨，到早上還沒停。",
                "我在民宿的屋簷下坐了一整個上午，看水從屋瓦邊緣一串一串地掉下來，在石階上濺成細碎的白花。老闆娘端來一壺熱茶，什麼也沒說，只是在我旁邊放了一個小板凳，自己也坐了下來。",
                "我們就這樣各自看著雨。",
                "快中午時她忽然說，這種雨最適合做筍乾，要是明天放晴，可以帶我去後山看。我說好。",
                "雨一直下到傍晚才變小。",
                "",
                "晚餐是一鍋薑母雞。鍋蓋掀開的瞬間，蒸氣撲到臉上，眼鏡全白了。我摘下眼鏡擦了半天，聽見周圍的人都在笑。",
                "隔壁桌是一家四口，最小的孩子大概五歲，專心地把每一塊香菇都夾到爸爸的碗裡，一邊夾一邊小聲報數：一、二、三、四、五。",
                "爸爸一口氣全吃了，孩子又開始報數。",
                "",
                "第五天　後山",
                "放晴了。",
                "天空乾淨得像是被人用力擦過。老闆娘果然在六點半敲了我的門，遞給我一雙膠鞋，尺寸剛好。",
                "我們沿著一條只有一人寬的小路上山。路邊的草葉上全是水珠，褲管很快就濕了一半。她走在前面，不時彎腰撿起什麼，有時是一顆野生的小番茄，有時是一片形狀特別的葉子，統統塞進口袋。",
                "走了大約四十分鐘，視野忽然開了。",
                "一整面山坡是剛冒出頭的筍，高高低低，像是誰在這裡開了一場安靜的會議。",
                "她蹲下來，用一把很小的鋤頭沿著筍根挖了幾下，然後輕輕一扳，整隻筍就脫離了土。動作俐落得像在變魔術。",
                "換我試。",
                "第一次，我把筍挖斷了。第二次，我把鋤頭卡在石縫裡拔不出來。第三次，她在旁邊忍不住笑到蹲在地上。",
                "第四次，成功了。",
                "我捧著那一支沾滿泥土的筍，覺得自己好像完成了什麼了不起的事。",
                "",
                "中午就在山上吃。她從背包拿出用荷葉包的飯糰，裡面夾著醃蘿蔔和一小撮肉鬆，另外還有一壺冷掉的麥茶。我從來沒覺得飯糰這麼好吃過。",
                "",
                "第六天　整理",
                "我花了一整個早上整理行李，其實根本沒什麼好整理的，只是想拖延一下。",
                "把筍乾、明信片、貝殼鑰匙圈一件一件放進背包，又一件一件拿出來，重新擺一次。",
                "老闆在門口看了一會兒，說：貝殼鑰匙圈你帶走吧。",
                "我說這樣你要怎麼辦。",
                "他說：山上有很多貝殼。",
                "我才發現他在開玩笑，但他的表情完全沒有變。",
                "",
                "下午去了一趟市場，買了三樣東西：一包曬乾的柿餅、一罐山蜂蜜、一把竹製的小掃帚。",
                "掃帚是賣菜的阿姨送的。她說這個很好用，可以掃桌子，也可以掃心情。我問心情怎麼掃，她說：就是你不要一直想的時候，隨手掃一掃。",
                "我想了一下，決定把這句話記下來。",
                "",
                "第七天　回程",
                "回程的公車比來的時候擠。",
                "有人抱著大包小包，有人帶著還在睡的孩子，還有一位老先生把他的鳥籠用毛巾蓋住，放在自己膝蓋上，一路上都用手護著。",
                "窗外的竹林依舊在風裡沙沙響，這次我終於聽出來，那不是下雨的聲音，比較像很多人在遠處輕聲說話，說的是同一句話，只是我聽不懂。",
                "到了終點站，我在車門邊停了一下，回頭看了一眼那條上坡的路。",
                "司機從後視鏡看見，朝我揮了揮手。",
                "",
                "後記",
                "回到城市之後，我把那張明信片貼在書桌前面的牆上。",
                "有時候工作累了，抬頭看一眼，就會想起豆花店老闆說的那句話：一個人也要吃飽。",
                "也想起那支沾滿泥土的筍。",
                "也許旅行的意義，就是帶回一些很小很小、但沒有辦法跟別人解釋清楚的東西。",
                "",
                "附錄：這趟旅行的開銷（概略）",
                "車票　　　　四百二十元",
                "住宿六晚　　四千八百元",
                "餐飲　　　　約兩千元",
                "雜支　　　　約六百元",
                "合計　　　　七千八百二十元",
                "",
                "備忘：",
                "一、公車時刻要提前一天跟民宿確認，山上收訊不穩。",
                "二、鞋子帶兩雙，一雙留著下雨用。",
                "三、現金多帶一點，老街大部分店家不收行動支付。",
                "四、如果還有下次，我想在秋天去，聽說那時候山上的芒草會整片變成銀色。",
                "",
                "（全文完）"
            });
        }

        static int ToSlider(double ticks, int min, int max)
        {
            return Math.Max(min, Math.Min(max, (int)Math.Round(ticks, MidpointRounding.AwayFromZero)));
        }

        static string NoteFor(double ticks, int shown)
        {
            return Math.Abs(ticks - shown) < 1e-9 ? "" : "（設定檔：" + RuleModel.TickText(ticks) + " 格）";
        }

        void OnSliderMoved(bool isStep)
        {
            if (refreshing)
                return;
            if (isStep)
            {
                stepDirty = true;
                stepNote = "";
            }
            else
            {
                pageDirty = true;
                pageNote = "";
            }
            errorLabel.Visible = false;
            ShowValues();
            debounce.Stop();
            debounce.Start();
        }

        void ShowValues()
        {
            stepValue.Text = stepTrack.Value.ToString(CultureInfo.InvariantCulture) + " 格" + stepNote;
            pageValue.Text = pageTrack.Value.ToString(CultureInfo.InvariantCulture) + " 格" + pageNote;
            strip.PageTicks = pageTrack.Value;
        }

        void ApplyPending()
        {
            if (!stepDirty && !pageDirty)
                return;
            bool doStep = stepDirty;
            bool doPage = pageDirty;
            double step = stepTrack.Value;
            double page = pageTrack.Value;
            stepDirty = false;
            pageDirty = false;

            applying = true;
            try
            {
                host.Mutate(delegate(ConfigFile f)
                {
                    f.Scroll = f.EffectiveScroll();
                    if (doStep)
                        f.Scroll.StepTicks = step;
                    if (doPage)
                        f.Scroll.PageTicks = page;
                });
            }
            catch (ConfigException ex)
            {
                errorLabel.Text = "無法套用：" + ex.Message;
                errorLabel.Visible = true;
                applying = false;
                Reload(true);
                return;
            }
            finally
            {
                applying = false;
            }
            Reload(false);
        }

        void OnHostChanged(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated)
                return;
            if (InvokeRequired)
                BeginInvoke(new Action(delegate { if (!IsDisposed) Reload(false); }));
            else
                Reload(false);
        }

        // Sliders follow the config except while our own write is in flight or a drag is still pending.
        void Reload(bool force)
        {
            ScrollRuntime sc = host.Settings.Scroll;

            if (force || (!applying && !stepDirty && !pageDirty))
            {
                refreshing = true;
                try
                {
                    stepTrack.Value = ToSlider(sc.StepTicks, StepMin, StepMax);
                    pageTrack.Value = ToSlider(sc.PageTicks, PageMin, PageMax);
                    stepNote = NoteFor(sc.StepTicks, stepTrack.Value);
                    pageNote = NoteFor(sc.PageTicks, pageTrack.Value);
                }
                finally
                {
                    refreshing = false;
                }
                ShowValues();
            }

            burstLabel.Text = "連點第 " + sc.BurstTaps.ToString(CultureInfo.InvariantCulture)
                + " 下起捲整頁（間隔 " + sc.BurstIntervalMs.ToString(CultureInfo.InvariantCulture)
                + " 毫秒內）；按住每秒最多 " + sc.HoldRepeatsPerSec.ToString(CultureInfo.InvariantCulture) + " 次";

            List<string> keys = new List<string>(sc.Apps.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            StringBuilder apps = new StringBuilder("各程式覆寫（請改設定檔）：");
            if (keys.Count == 0)
                apps.Append("（無）");
            for (int i = 0; i < keys.Count; i++)
            {
                ScrollAppConfig a = sc.Apps[keys[i]];
                if (i > 0)
                    apps.Append("、");
                apps.Append(keys[i]).Append(' ').Append(RuleModel.TickText(a.StepTicks)).Append('/').Append(RuleModel.TickText(a.PageTicks));
            }
            appsLabel.Text = apps.ToString();
            offLabel.Visible = !sc.Enabled;
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            foreach (Label l in bold)
                l.Font = new Font(Font, FontStyle.Bold);
        }

        void Unhook()
        {
            if (unhooked)
                return;
            unhooked = true;
            host.Changed -= OnHostChanged;
            debounce.Stop();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (debounce.Enabled)
            {
                debounce.Stop();
                ApplyPending();
            }
            Unhook();
            base.OnFormClosed(e);
            Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Unhook();
                debounce.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
