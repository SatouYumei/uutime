namespace UuTime;

/// <summary>
/// 设置页：开机自启、主题、空闲判定、更新推送、自动监控（范围、主界面开关、屏蔽列表）。
/// 除开机自启外都存在当前存档里。改了立即生效；换主题时返回 Retry，由主窗口重建后再打开。
/// </summary>
internal sealed class SettingsForm : ChromeForm
{
    static readonly string[] Scopes = ["有窗口的", "只前台", "所有进程"];
    static readonly string[] ScopeHints =
    [
        "前台用过的，加上开着窗口的应用",
        "只记在前台用过的应用，不算后台",
        "所有进程，含没窗口的后台程序（不含系统进程）",
    ];

    sealed record Row(string Label, Func<string> Hint, Control Control);

    readonly Kit kit;
    readonly Store store;
    readonly Action changed;
    readonly List<(string Section, Row[] Rows)> sections;
    readonly Toggle autoMode;
    readonly Segmented scope;
    readonly TextButton blockAdd, done;
    readonly BlockList blockList;
    readonly List<(Rectangle Rect, Row Row)> rowRects = new();
    readonly List<(int Y, string Text)> sectionRects = new();
    Rectangle blockHead;

    /// <param name="changed">自动监控相关设置改了（主窗口刷新列表、重新扫描）</param>
    public SettingsForm(Kit kit, Store store, Action changed)
    {
        this.kit = kit;
        this.store = store;
        this.changed = changed;
        var t = kit.T;
        var prefs = store.Prefs;
        Text = "设置";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        SetBack(t.Bg);
        CaptionHeight = S(56);
        Size = new Size(S(640), S(760));
        MinimumSize = new Size(S(560), S(600));

        var close = new IconButton(kit, t.Bg, "close", "关闭");
        close.Click += (_, _) => Close();
        close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        close.Location = new Point(Width - S(12) - close.Width, (CaptionHeight - close.Height) / 2);

        var autostart = new Toggle(kit, t.Bg) { Checked = Autostart.Enabled };
        autostart.CheckedChanged += () => Autostart.Enabled = autostart.Checked;

        var theme = new TextButton(kit, t.Bg, Theme.ById(prefs.Theme).Name, ButtonKind.Outline, "palette");
        theme.Click += (_, _) => PickTheme();

        var idle = new Stepper(kit, t.Bg, prefs.IdleMinutes, 0, 60, v => v == 0 ? "不判定" : $"{v} 分钟");
        idle.ValueChanged += v => { prefs.IdleMinutes = v; store.SavePrefs(); };

        var updates = new Toggle(kit, t.Bg) { Checked = prefs.CheckUpdates };
        updates.CheckedChanged += () => { prefs.CheckUpdates = updates.Checked; store.SavePrefs(); };

        autoMode = new Toggle(kit, t.Bg) { Checked = prefs.AutoMode };
        autoMode.CheckedChanged += () => { prefs.AutoMode = autoMode.Checked; Apply(); };

        scope = new Segmented(kit, t.Bg, Scopes) { Selected = (int)prefs.AutoScope };
        scope.Changed += () => { prefs.AutoScope = (AutoScope)scope.Selected; Apply(); };

        var showToggle = new Toggle(kit, t.Bg) { Checked = prefs.ShowAutoToggle };
        showToggle.CheckedChanged += () => { prefs.ShowAutoToggle = showToggle.Checked; Apply(); };

        blockAdd = new TextButton(kit, t.Bg, "添加", ButtonKind.Ghost, "plus");
        blockAdd.Click += (_, _) => AddBlocked();
        blockList = new BlockList(kit, t.Bg, store);
        blockList.Removed += exe => { store.SetBlocked(exe, false); blockList.Reload(); changed(); };

        done = new TextButton(kit, t.Bg, "完成", ButtonKind.Outline);
        done.Click += (_, _) => Close();

        sections =
        [
            ("常规", [
                new Row("开机自启", () => "这台电脑的设置，不跟存档走", autostart),
                new Row("主题", () => "", theme),
                new Row("空闲判定", () => "这么久没碰键鼠或手柄，前台时间就暂停", idle),
            ]),
            ("更新", [
                new Row("自动检查更新", () => "每次启动 uutime 时看一下，有新版本就通知你，点了才装", updates),
            ]),
            ("自动监控", [
                new Row("自动监控", () => "打开的应用自动开始记录，不用一个个添加", autoMode),
                new Row("监控范围", () => ScopeHints[scope.Selected], scope),
                new Row("主界面显示开关", () => "在侧边栏显示「自动监控」开关", showToggle),
            ]),
        ];

        Root.Controls.AddRange([close, autostart, theme, idle, updates, autoMode, scope, showToggle, blockAdd, blockList, done]);
    }

    public override int S(float v) => kit.S(v);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyChrome(kit.T);
    }

    void Apply()
    {
        store.SavePrefs();
        changed();
        Root.Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (done == null) return;
        int pad = S(24), w = ClientSize.Width, h = ClientSize.Height;
        rowRects.Clear();
        sectionRects.Clear();

        int y = CaptionHeight + S(2);
        foreach (var (section, rows) in sections)
        {
            sectionRects.Add((y, section));
            y += S(30);
            foreach (var row in rows)
            {
                var r = new Rectangle(pad, y, w - pad * 2, S(54));
                rowRects.Add((r, row));
                row.Control.Location = new Point(r.Right - row.Control.Width, r.Y + (r.Height - row.Control.Height) / 2);
                y += r.Height;
            }
            y += S(10);
        }

        blockHead = new Rectangle(pad, y, w - pad * 2, S(36));
        blockAdd.Location = new Point(blockHead.Right - blockAdd.Width + S(10), blockHead.Y + (blockHead.Height - blockAdd.Height) / 2);
        done.Location = new Point(w - pad - done.Width, h - S(18) - done.Height);
        blockList.Bounds = new Rectangle(pad - S(6), blockHead.Bottom + S(2), w - pad * 2 + S(12), done.Top - S(14) - blockHead.Bottom - S(2));
        Root.Invalidate();
    }

    protected override void PaintRoot(Graphics g)
    {
        var t = kit.T;
        Gfx.Text(g, "设置", kit.H2, t.Text, new Rectangle(S(24), 0, S(120), CaptionHeight));
        string note = $"保存在存档「{store.ProfileName}」里，切换存档会一起切换";
        Gfx.Text(g, note, kit.Small, t.Muted, new Rectangle(S(84), S(3), ClientSize.Width - S(84) - S(56), CaptionHeight));

        using var line = new SolidBrush(t.Border);
        foreach (var (y, text) in sectionRects)
            Gfx.Text(g, text, kit.Small, t.Accent, new Rectangle(S(24), y, S(200), S(28)));
        foreach (var (r, row) in rowRects)
        {
            int textW = row.Control.Left - r.X - S(16);
            string hint = row.Hint();
            if (hint.Length == 0)
                Gfx.Text(g, row.Label, kit.Body, t.Text, new Rectangle(r.X, r.Y, textW, r.Height));
            else
            {
                Gfx.Text(g, row.Label, kit.Body, t.Text, new Rectangle(r.X, r.Y + S(7), textW, S(22)));
                Gfx.Text(g, hint, kit.Small, t.Muted, new Rectangle(r.X, r.Y + S(30), textW, S(18)));
            }
            g.FillRectangle(line, r.X, r.Bottom - 1, r.Width, 1);
        }

        Gfx.Text(g, "屏蔽列表", kit.Body, t.Text, new Rectangle(blockHead.X, blockHead.Y, S(100), blockHead.Height));
        string blockHint = store.Prefs.AutoMode ? "自动监控不会记录这些应用" : "自动监控打开后生效";
        Gfx.Text(g, blockHint, kit.Small, t.Muted, new Rectangle(blockHead.X + S(76), blockHead.Y + S(2), blockAdd.Left - blockHead.X - S(86), blockHead.Height));
        g.FillRectangle(line, S(24), done.Top - S(12), ClientSize.Width - S(48), 1);
    }

    void PickTheme()
    {
        using var picker = new ThemePicker(kit, store.Prefs.Theme);
        if (picker.ShowDialog(this) != DialogResult.OK || picker.Chosen == store.Prefs.Theme) return;
        store.Prefs.Theme = picker.Chosen;
        store.SavePrefs();
        DialogResult = DialogResult.Retry; // 主窗口换好主题后重新打开设置
    }

    void AddBlocked()
    {
        using var dlg = new AddForm(kit, store.Blocked, "选择要屏蔽的应用", "屏蔽");
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        foreach (var a in dlg.Result) store.SetBlocked(a.Exe, true);
        blockList.Reload();
        changed();
    }

    /// <summary>屏蔽列表：图标、名字、exe，右边点 × 移除。</summary>
    sealed class BlockList : Flat
    {
        readonly Store store;
        List<string> items = new();
        int hover = -1;
        bool hoverX;
        int scroll;

        public event Action<string>? Removed;

        public BlockList(Kit kit, Color back, Store store) : base(kit, back)
        {
            this.store = store;
            Reload();
        }

        int RowH => S(48);

        public void Reload()
        {
            items = store.Prefs.Blocked.ToList();
            Clamp();
            Invalidate();
        }

        void Clamp() => scroll = Math.Clamp(scroll, 0, Math.Max(0, items.Count * RowH - Height));

        Rectangle XRect(Rectangle r) => new(r.Right - S(40), r.Y + (r.Height - S(30)) / 2, S(30), S(30));

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = (e.Y + scroll) / RowH;
            if (i >= items.Count) i = -1;
            bool x = i >= 0 && XRect(new Rectangle(0, i * RowH - scroll, Width, RowH)).Contains(e.Location);
            if (i != hover || x != hoverX)
            {
                hover = i;
                hoverX = x;
                Cursor = x ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = -1;
            hoverX = false;
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && hoverX && hover >= 0) Removed?.Invoke(items[hover]);
            base.OnMouseDown(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            scroll -= e.Delta / 120 * RowH;
            Clamp();
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void OnResize(EventArgs e)
        {
            Clamp();
            base.OnResize(e);
        }

        protected override void Draw(Graphics g)
        {
            if (items.Count == 0)
            {
                Gfx.Text(g, "还没有屏蔽的应用。可以点「添加」，或在主界面选中应用后点「屏蔽」。", K.Small, T.Muted,
                    new Rectangle(S(6), S(8), Width - S(12), S(20)));
                return;
            }
            for (int i = 0; i < items.Count; i++)
            {
                var r = new Rectangle(0, i * RowH - scroll, Width, RowH - S(2));
                if (r.Bottom < 0 || r.Top > Height) continue;
                if (i == hover) Gfx.FillRound(g, T.Hover, r, S(10));
                var app = store.Find(items[i]);
                string name = app?.Name ?? Path.GetFileNameWithoutExtension(items[i]);
                K.DrawApp(g, name, app?.Path, new Rectangle(r.X + S(10), r.Y + (r.Height - S(26)) / 2, S(26), S(26)));
                int tx = r.X + S(48), tw = r.Width - S(48) - S(52);
                Gfx.Text(g, name, K.Body, T.Text, new Rectangle(tx, r.Y + S(5), tw, S(20)));
                Gfx.Text(g, items[i], K.Small, T.Muted, new Rectangle(tx, r.Y + S(25), tw, S(16)));
                if (i == hover)
                {
                    var x = XRect(r);
                    if (hoverX) Gfx.FillRound(g, Theme.Mix(T.Danger, T.Hover, 0.18f), x, S(8));
                    float box = S(20);
                    Gfx.Icon(g, "close", new RectangleF(x.X + (x.Width - box) / 2, x.Y + (x.Height - box) / 2, box, box), hoverX ? T.Danger : T.Muted);
                }
            }
        }
    }
}
