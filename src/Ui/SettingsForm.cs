using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using KeyBank.Core;

namespace KeyBank.Ui
{
    public sealed class SettingsForm : Form
    {
        readonly IEngineHost host;
        readonly Label keyboardLabel = new Label();
        readonly Label stateLabel = new Label();
        readonly KeyClusterPanel cluster = new KeyClusterPanel();
        readonly ListView rules = new ListView();
        readonly Label lastKeyLabel = new Label();
        readonly LinkLabel pathLink = new LinkLabel();
        readonly Label errorLabel = new Label();
        readonly Timer keyTimer = new Timer();
        bool unhooked;

        public SettingsForm(IEngineHost host)
        {
            this.host = host;

            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            Font = new Font("Microsoft JhengHei UI", 9f);
            Text = "KeyBank 目前設定";
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            MinimumSize = new Size(560, 380);
            ClientSize = new Size(1240, 600);

            BuildLayout();

            host.Changed += OnHostChanged;
            keyTimer.Interval = 200;
            keyTimer.Tick += delegate { lastKeyLabel.Text = "最後按下的鍵：" + (host.LastKeyText ?? ""); };
            keyTimer.Start();
            RefreshView();
        }

        void BuildLayout()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.Padding = new Padding(12);
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            FlowLayoutPanel header = new FlowLayoutPanel();
            header.FlowDirection = FlowDirection.TopDown;
            header.WrapContents = false;
            header.AutoSize = true;
            header.Dock = DockStyle.Fill;
            keyboardLabel.AutoSize = true;
            stateLabel.AutoSize = true;
            header.Controls.Add(keyboardLabel);
            header.Controls.Add(stateLabel);
            root.Controls.Add(header, 0, 0);

            TableLayoutPanel body = new TableLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.ColumnCount = 2;
            body.RowCount = 1;
            body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            cluster.Margin = new Padding(0, 0, 12, 0);
            cluster.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            body.Controls.Add(cluster, 0, 0);

            rules.Dock = DockStyle.Fill;
            rules.View = View.Details;
            rules.FullRowSelect = true;
            rules.HideSelection = false;
            rules.MultiSelect = false;
            rules.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            rules.ShowGroups = true;
            rules.Columns.Add("類別", 100);
            rules.Columns.Add("按下", 150);
            rules.Columns.Add("變成", 230);
            rules.Columns.Add("說明", 260);
            rules.SizeChanged += delegate { FitColumns(); };
            body.Controls.Add(rules, 1, 0);
            root.Controls.Add(body, 0, 1);

            TableLayoutPanel status = new TableLayoutPanel();
            status.AutoSize = true;
            status.Dock = DockStyle.Fill;
            status.ColumnCount = 1;
            status.RowCount = 3;
            status.Margin = new Padding(0, 8, 0, 0);
            lastKeyLabel.AutoSize = true;
            lastKeyLabel.Text = "最後按下的鍵：";
            pathLink.AutoSize = true;
            pathLink.LinkClicked += delegate { OpenFolder(); };
            errorLabel.AutoSize = true;
            errorLabel.ForeColor = Color.Firebrick;
            errorLabel.MaximumSize = new Size(2000, 0);
            status.Controls.Add(lastKeyLabel, 0, 0);
            status.Controls.Add(pathLink, 0, 1);
            status.Controls.Add(errorLabel, 0, 2);
            root.Controls.Add(status, 0, 2);
        }

        bool fitting;

        void FitColumns()
        {
            if (fitting)
                return;
            fitting = true;
            try
            {
                DoFitColumns();
            }
            finally
            {
                fitting = false;
            }
        }

        void DoFitColumns()
        {
            int w = rules.ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
            if (w <= 0 || rules.Columns.Count < 4)
                return;
            rules.Columns[0].Width = w * 12 / 100;
            rules.Columns[1].Width = w * 17 / 100;
            int minPress = rules.Columns[1].Width;
            rules.Columns[1].AutoResize(ColumnHeaderAutoResizeStyle.ColumnContent);
            rules.Columns[2].AutoResize(ColumnHeaderAutoResizeStyle.ColumnContent);
            rules.Columns[3].AutoResize(ColumnHeaderAutoResizeStyle.ColumnContent);
            int pad = TextRenderer.MeasureText("MM", Font).Width;
            rules.Columns[1].Width = Math.Max(minPress, rules.Columns[1].Width + pad);
            rules.Columns[2].Width += pad;
            rules.Columns[3].Width += pad;
            int rest = w - rules.Columns[0].Width - rules.Columns[1].Width - rules.Columns[2].Width;
            if (rest > rules.Columns[3].Width)
                rules.Columns[3].Width = rest;
        }

        void OnHostChanged(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated)
                return;
            if (InvokeRequired)
                BeginInvoke(new Action(RefreshView));
            else
                RefreshView();
        }

        public void RefreshView()
        {
            Settings s = host.Settings;
            keyboardLabel.Text = "數字鍵模擬編輯鍵區：" + (s.NumpadSixKeys ? "開啟" : "關閉");
            stateLabel.Text = s.Enabled ? "KeyBank 已啟用" : "KeyBank 已停用";
            stateLabel.ForeColor = s.Enabled ? Color.FromArgb(0x1B, 0x7A, 0x3A) : Color.DimGray;

            pathLink.Text = host.ConfigPath ?? "";
            string err = host.LastError;
            errorLabel.Text = err == null ? "" : "設定檔有錯誤：" + err;
            errorLabel.Visible = err != null;
            lastKeyLabel.Text = "最後按下的鍵：" + (host.LastKeyText ?? "");
            ShowRules();
        }

        void ShowRules()
        {
            Settings s = host.Settings;
            cluster.SetRules(s.Keys);

            rules.BeginUpdate();
            rules.Items.Clear();
            rules.Groups.Clear();
            Dictionary<string, ListViewGroup> groups = new Dictionary<string, ListViewGroup>();
            foreach (RuleRow r in RuleModel.BuildRows(s))
            {
                ListViewGroup g;
                string header = r.Group ?? r.Category;
                if (!groups.TryGetValue(header, out g))
                {
                    g = new ListViewGroup(header);
                    groups[header] = g;
                    rules.Groups.Add(g);
                }
                ListViewItem item = new ListViewItem(r.Category);
                item.SubItems.Add(r.Press);
                item.SubItems.Add(r.Becomes);
                item.SubItems.Add(r.Note);
                item.Group = g;
                rules.Items.Add(item);
            }
            rules.EndUpdate();
            FitColumns();
        }

        void OpenFolder()
        {
            string path = host.ConfigPath;
            if (string.IsNullOrEmpty(path))
                return;
            try
            {
                Process.Start("explorer.exe", "/select,\"" + path + "\"");
            }
            catch (Exception ex)
            {
                errorLabel.Text = "無法開啟資料夾：" + ex.Message;
                errorLabel.Visible = true;
            }
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            keyboardLabel.Font = new Font(Font, FontStyle.Bold);
        }

        void Unhook()
        {
            if (unhooked)
                return;
            unhooked = true;
            host.Changed -= OnHostChanged;
            keyTimer.Stop();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Unhook();
            base.OnFormClosed(e);
            Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Unhook();
                keyTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
