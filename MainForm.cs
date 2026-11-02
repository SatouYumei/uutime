namespace UuTime;

internal sealed class MainForm : ChromeForm
{
    static readonly string[] Ranges = ["今天", "昨天", "7 天", "30 天"];
    const int ChartDays = 14;

    readonly Store store;
    readonly Tracker tracker;
    readonly System.Windows.Forms.Timer refresh = new() { Interval = 1000 };
    readonly System.Windows.Forms.Timer disarm = new() { Interval = 3000 };

    Kit kit = null!;
    IconButton palette = null!, minimize = null!, close = null!;
    Surface sidebar = null!, main = null!;
    ItemList list = null!;
    TextButton addBtn = null!, removeBtn = null!;
    Toggle autostart = null!;
    Stepper idle = null!;
    Segmented seg = null!;
    StatsView stats = null!;

    string? selected; // null 表示总览
    int range;
    bool removeArmed;
    string caption = "", heading = "", meta = "";
    Rectangle autoRow, idleRow;
    int dividerY;

    public event Action? ThemeChanged;

    public MainForm(Store store, Tracker tracker, Icon icon)
    {
        this.store = store;
        this.tracker = tracker;
        Icon = icon;
        Text = "uutime";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(S(940), S(640));

        refresh.Tick += (_, _) => RefreshData();
        disarm.Tick += (_, _) => Disarm();
        Build();
    }

    public Theme Theme => kit.T;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyChrome(kit.T);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        refresh.Start();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Build();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            refresh.Dispose();
            disarm.Dispose();
        }
        base.Dispose(disposing);
        if (disposing) kit?.Dispose();
    }

    /// <summary>按当前主题和 DPI 重建整个界面。</summary>
    void Build()
    {
        SuspendLayout();
        foreach (var c in Root.Controls.Cast<Control>().ToList()) c.Dispose();
        kit?.Dispose();
        kit = new Kit(Theme.ById(store.Config.Theme), DeviceDpi);
        var t = kit.T;
        SetBack(t.Bg);
        CaptionHeight = S(56);
        MinimumSize = new Size(S(780), S(520));

        palette = new IconButton(kit, t.Bg, "palette", "主题");
        palette.Click += (_, _) => OpenThemes();
        minimize = new IconButton(kit, t.Bg, "minus", "最小化到托盘");
        minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        close = new IconButton(kit, t.Bg, "close", "关闭（托盘里继续记录）");
        close.Click += (_, _) => Close();

        sidebar = new Surface(t.Sidebar);
        sidebar.Paint += PaintSidebar;
        list = new ItemList(kit, t.Sidebar);
        list.Picked += id => Select(id == "" ? null : id);
        addBtn = new TextButton(kit, t.Sidebar, "添加应用", ButtonKind.Ghost, "plus");
        addBtn.Click += (_, _) => AddApps();
        autostart = new Toggle(kit, t.Sidebar) { Checked = Autostart.Enabled };
        autostart.CheckedChanged += () => Autostart.Enabled = autostart.Checked;
        idle = new Stepper(kit, t.Sidebar, store.Config.IdleMinutes, 0, 60, v => v == 0 ? "不判定" : $"{v} 分钟");
        idle.ValueChanged += v => store.SetIdleMinutes(v);
        kit.Tip.SetToolTip(idle, "这么久没碰键鼠，前台时间就暂停（计入后台）。看视频可以设为不判定。");
        sidebar.Controls.AddRange([list, addBtn, autostart, idle]);

        main = new Surface(t.Bg);
        main.Paint += PaintMain;
        seg = new Segmented(kit, t.Bg, Ranges) { Selected = range };
        seg.Changed += () => { range = seg.Selected; RefreshData(); };
        stats = new StatsView(kit, t.Bg);
        stats.RowClicked += exe => Select(exe);
        stats.AddClicked += AddApps;
        removeBtn = new TextButton(kit, t.Bg, "停止监控", ButtonKind.Danger);
        removeBtn.Click += (_, _) => RemoveSelected();
        main.Controls.AddRange([seg, stats, removeBtn]);

        Root.Controls.AddRange([palette, minimize, close, sidebar, main]);
        ApplyChrome(t);
        ResumeLayout();
        LayoutAll();
        RebuildItems();
        Root.Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        // 最小化（按钮、点任务栏、Win+↓）一律缩到托盘
        if (WindowState == FormWindowState.Minimized && Visible) { BeginInvoke(Hide); return; }
        LayoutAll();
        Root.Invalidate();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!IsHandleCreated) return;
        refresh.Enabled = Visible; // 藏在托盘时不刷新界面
        if (Visible) RefreshData();
        else Native.TrimWorkingSet();
    }

    void LayoutAll()
    {
        if (sidebar == null) return;
        int w = ClientSize.Width, h = ClientSize.Height, head = CaptionHeight;

        int x = w - S(12);
        foreach (var b in new Control[] { close, minimize, palette })
        {
            x -= b.Width;
            b.Location = new Point(x, (head - b.Height) / 2);
            x -= S(4);
        }

        int sw = S(240);
        sidebar.Bounds = new Rectangle(0, head + 1, sw, h - head - 1);
        main.Bounds = new Rectangle(sw + 1, head + 1, w - sw - 1, h - head - 1);

        int sh = sidebar.Height;
        idleRow = new Rectangle(0, sh - S(14) - S(42), sw, S(42));
        autoRow = new Rectangle(0, idleRow.Y - S(42), sw, S(42));
        idle.Location = new Point(sw - S(16) - idle.Width, idleRow.Y + (idleRow.Height - idle.Height) / 2);
        autostart.Location = new Point(sw - S(18) - autostart.Width, autoRow.Y + (autoRow.Height - autostart.Height) / 2);
        dividerY = autoRow.Y - S(8);
        addBtn.Bounds = new Rectangle(S(8), dividerY - S(8) - S(42), sw - S(16), S(42));
        list.Bounds = new Rectangle(0, S(8), sw, addBtn.Top - S(12));

        int mw = main.Width, mh = main.Height, pad = S(32);
        seg.Location = new Point(mw - pad - seg.Width, S(46));
        bool appMode = selected != null;
        removeBtn.Visible = appMode;
        removeBtn.Location = new Point(pad, mh - S(22) - removeBtn.Height);
        int statsTop = S(124);
        stats.Bounds = new Rectangle(pad, statsTop, mw - pad * 2, (appMode ? removeBtn.Top - S(16) : mh - S(24)) - statsTop);
    }

    protected override void PaintRoot(Graphics g)
    {
        var t = kit.T;
        int head = CaptionHeight;

        int x = S(22);
        int uu = Gfx.Measure("uu", kit.Brand).Width;
        Gfx.Text(g, "uu", kit.Brand, t.Accent, new Rectangle(x, 0, uu + S(2), head));
        int time = Gfx.Measure("time", kit.Brand).Width;
        Gfx.Text(g, "time", kit.Brand, t.Text, new Rectangle(x + uu, 0, time + S(2), head));
        Gfx.Text(g, "应用使用时间", kit.Small, t.Muted, new Rectangle(x + uu + time + S(12), S(4), S(160), head));

        using var line = new SolidBrush(t.Border);
        g.FillRectangle(line, 0, head, ClientSize.Width, 1);
        g.FillRectangle(line, S(240), head, 1, ClientSize.Height);
    }

    void PaintSidebar(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        var t = kit.T;
        using (var line = new SolidBrush(t.Border))
            g.FillRectangle(line, S(18), dividerY, sidebar.Width - S(36), 1);
        Gfx.Text(g, "开机自启", kit.Body, t.Text, new Rectangle(S(20), autoRow.Y, S(120), autoRow.Height));
        Gfx.Text(g, "空闲判定", kit.Body, t.Text, new Rectangle(S(20), idleRow.Y, S(100), idleRow.Height));
    }

    void PaintMain(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        var t = kit.T;
        int pad = S(32), w = seg.Left - pad - S(16);
        Gfx.Text(g, caption, kit.Small, t.Muted, new Rectangle(pad, S(22), w, S(18)));
        Gfx.Text(g, heading, kit.Title, t.Text, new Rectangle(pad - S(2), S(42), w, S(40)));
        Gfx.Text(g, meta, kit.Body, t.Muted, new Rectangle(pad, S(84), main.Width - pad * 2, S(22)));
    }

    // ───── 数据 ─────

    (DateTime from, DateTime to) RangeDates()
    {
        var today = DateTime.Today;
        return range switch
        {
            1 => (today.AddDays(-1), today.AddDays(-1)),
            2 => (today.AddDays(-6), today),
            3 => (today.AddDays(-29), today),
            _ => (today, today),
        };
    }

    int State(string exe) =>
        string.Equals(tracker.ForegroundExe, exe, StringComparison.OrdinalIgnoreCase) ? 1 : tracker.IsRunning(exe) ? 2 : 0;

    void RebuildItems()
    {
        if (selected != null && !store.Watched.Contains(selected)) selected = null;
        list.Items.Clear();
        list.Items.Add(new ItemList.Item { Id = "", Title = "全部应用", Glyph = "grid" });
        if (store.Config.Apps.Count > 0)
            list.Items.Add(new ItemList.Item { Header = true, Title = $"监控中 · {store.Config.Apps.Count}" });
        foreach (var a in store.Config.Apps)
            list.Items.Add(new ItemList.Item { Id = a.Exe, Title = a.Name, Path = a.Path, Sub = "" });
        list.SelectedId = selected ?? "";
        LayoutAll();
        RefreshData();
    }

    void RefreshData()
    {
        tracker.Flush();
        var (from, to) = RangeDates();
        var rows = store.Config.Apps.Select(a =>
        {
            var (fg, bg) = store.Sum(a.Exe, from, to);
            return new StatsView.Row(a.Exe, a.Name, a.Path, fg, bg, State(a.Exe));
        }).ToList();

        foreach (var item in list.Items)
        {
            if (item.Header || item.Id == "") continue;
            var row = rows.Find(r => r.Exe == item.Id);
            if (row == null) continue;
            item.Sub = $"前台 {Fmt.Duration(row.Fg)}";
            item.State = row.State;
        }
        list.Invalidate();

        caption = from == to ? (from == DateTime.Today ? "今天 · " : "昨天 · ") + Fmt.Day(from)
            : $"{from.Month}月{from.Day}日 – {to.Month}月{to.Day}日 · {Ranges[range]}";

        if (rows.Count == 0)
        {
            heading = "开始记录";
            meta = "选择要统计的应用，uutime 只记录你添加的程序";
            stats.ShowEmpty();
        }
        else if (selected == null)
        {
            heading = "全部应用";
            meta = $"前台 {Fmt.Duration(rows.Sum(r => r.Fg))}   ·   后台 {Fmt.Duration(rows.Sum(r => r.Bg))}   ·   {rows.Count} 个应用";
            stats.ShowOverview(rows.OrderByDescending(r => r.Fg + r.Bg).ThenBy(r => r.Name).ToList());
        }
        else
        {
            var row = rows.First(r => r.Exe == selected);
            var app = store.Config.Apps.First(a => a.Exe == selected);
            heading = row.Name;
            meta = app.Path ?? app.Exe;
            var today = DateTime.Today;
            var series = Enumerable.Range(0, ChartDays).Select(i =>
            {
                var d = today.AddDays(i - ChartDays + 1);
                var (fg, bg) = store.Sum(row.Exe, d, d);
                return (d, fg, bg);
            }).ToArray();
            stats.ShowApp(row, series);
        }
        main.Invalidate(new Rectangle(0, 0, seg.Left, S(112)));
    }

    void Select(string? exe)
    {
        if (exe == selected) return;
        selected = exe;
        list.SelectedId = exe ?? "";
        Disarm();
        LayoutAll();
        RefreshData();
    }

    // ───── 操作 ─────

    void AddApps()
    {
        using var dlg = new AddForm(kit, store.Watched);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Result.Count == 0) return;
        store.AddApps(dlg.Result);
        tracker.Rescan();
        selected = dlg.Result.Count == 1 ? dlg.Result[0].Exe : null;
        RebuildItems();
    }

    /// <summary>点两下才移除：第一下变成“确认”，3 秒内再点一下生效。</summary>
    void RemoveSelected()
    {
        if (selected == null) return;
        if (!removeArmed)
        {
            removeArmed = true;
            removeBtn.Text = "再点一次确认（历史数据会保留）";
            removeBtn.FitWidth();
            disarm.Start();
            return;
        }
        tracker.Flush();
        store.RemoveApp(selected);
        Disarm();
        selected = null;
        RebuildItems();
    }

    void Disarm()
    {
        disarm.Stop();
        if (!removeArmed) return;
        removeArmed = false;
        removeBtn.Text = "停止监控";
        removeBtn.FitWidth();
    }

#if DEBUG
    /// <summary>开发用：不显示窗口，直接把各主题、各页面画成图片，检查界面。</summary>
    public static void RenderPreviews(Store store, Tracker tracker, Icon icon, string outDir, string[] themes)
    {
        Directory.CreateDirectory(outDir);
        PreviewMode = true;
        foreach (string id in themes)
        {
            store.Config.Theme = id;
            using var f = new MainForm(store, tracker, icon)
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-30000, -30000),
                ShowInTaskbar = false,
            };
            f.Show();
            Pump();
            Save(f, Path.Combine(outDir, $"{id}-overview.png"));
            if (store.Config.Apps.Count > 0)
            {
                f.Select(store.Config.Apps[0].Exe);
                Pump();
                Save(f, Path.Combine(outDir, $"{id}-app.png"));
            }
            if (id == themes[0])
            {
                ShowDialogPreview(new ThemePicker(f.kit, id), Path.Combine(outDir, $"{id}-themes.png"));
                ShowDialogPreview(new AddForm(f.kit, store.Watched), Path.Combine(outDir, $"{id}-add.png"));
            }
            f.Close();
        }

        static void ShowDialogPreview(Form d, string file)
        {
            using (d)
            {
                d.StartPosition = FormStartPosition.Manual;
                d.Location = new Point(-30000, -30000);
                d.Show();
                Pump();
                Save(d, file);
                d.Close();
            }
        }

        static void Pump()
        {
            for (int i = 0; i < 10; i++) { Application.DoEvents(); Thread.Sleep(30); }
        }

        static void Save(Form f, string file)
        {
            f.Refresh();
            using var bmp = new Bitmap(f.Width, f.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                PrintWindow(f.Handle, hdc, 0);
                g.ReleaseHdc(hdc);
            }
            bmp.Save(file);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
#endif

    void OpenThemes()
    {
        string chosen;
        using (var picker = new ThemePicker(kit, store.Config.Theme))
        {
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            chosen = picker.Chosen;
        }
        if (chosen == store.Config.Theme) return;
        store.SetTheme(chosen);
        Build();
        ThemeChanged?.Invoke();
    }
}
