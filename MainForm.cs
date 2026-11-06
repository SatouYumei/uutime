namespace UuTime;

internal sealed class MainForm : ChromeForm
{
    static readonly string[] Ranges = ["今天", "昨天", "7 天", "30 天", "全部"];
    const int ChartDays = 14;

    readonly Store store;
    readonly Tracker tracker;
    readonly Updater? updater;
    readonly System.Windows.Forms.Timer refresh = new() { Interval = 1000 };
    readonly System.Windows.Forms.Timer disarm = new() { Interval = 3000 };

    Kit kit = null!;
    IconButton minimize = null!, close = null!;
    TextButton profileBtn = null!, updateBtn = null!, settingsBtn = null!;
    Surface sidebar = null!, main = null!;
    ItemList list = null!;
    TextButton addBtn = null!, removeBtn = null!;
    Toggle autoToggle = null!;
    Segmented seg = null!;
    StatsView stats = null!;

    string? selected; // null 表示总览
    int range;
    bool removeArmed;
    string caption = "", heading = "", meta = "";
    Rectangle updateRow, autoRow;
    int dividerY;

    public event Action? ThemeChanged;

    public MainForm(Store store, Tracker tracker, Icon icon, Updater? updater = null)
    {
        this.store = store;
        this.tracker = tracker;
        this.updater = updater;
        Icon = icon;
        Text = "uutime";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(S(940), S(640));

        if (updater != null) updater.Changed += OnUpdaterChanged;
        tracker.Discovered += OnDiscovered;
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
            if (updater != null) updater.Changed -= OnUpdaterChanged;
            tracker.Discovered -= OnDiscovered;
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
        kit = new Kit(Theme.ById(store.Prefs.Theme), DeviceDpi);
        var t = kit.T;
        SetBack(t.Bg);
        CaptionHeight = S(56);
        MinimumSize = new Size(S(780), S(520));

        profileBtn = new TextButton(kit, t.Bg, ProfileLabel(), ButtonKind.Ghost, "user");
        profileBtn.Click += (_, _) => OpenProfiles();
        kit.Tip.SetToolTip(profileBtn, "存档管理：切换、改名、备份、导入");
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
        autoToggle = new Toggle(kit, t.Sidebar) { Checked = store.Prefs.AutoMode };
        autoToggle.CheckedChanged += () =>
        {
            store.Prefs.AutoMode = autoToggle.Checked;
            store.SavePrefs();
            PrefsChanged();
        };
        kit.Tip.SetToolTip(autoToggle, "打开后，打开的应用会自动开始记录。范围和屏蔽列表在设置里。");
        updateBtn = new TextButton(kit, t.Sidebar, "检查更新", ButtonKind.Outline) { Height = S(30), Visible = updater != null };
        updateBtn.Click += async (_, _) => await UpdateClicked();
        settingsBtn = new TextButton(kit, t.Sidebar, "设置", ButtonKind.Ghost, "gear");
        settingsBtn.Click += (_, _) => OpenSettings();
        sidebar.Controls.AddRange([list, addBtn, autoToggle, updateBtn, settingsBtn]);

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

        Root.Controls.AddRange([profileBtn, minimize, close, sidebar, main]);
        ApplyChrome(t);
        ResumeLayout();
        LayoutAll();
        RebuildItems();
        SyncUpdate();
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
        foreach (var b in new Control[] { close, minimize, profileBtn })
        {
            x -= b.Width;
            b.Location = new Point(x, (head - b.Height) / 2);
            x -= S(4);
        }

        int sw = S(240);
        sidebar.Bounds = new Rectangle(0, head + 1, sw, h - head - 1);
        main.Bounds = new Rectangle(sw + 1, head + 1, w - sw - 1, h - head - 1);

        // 侧边栏底部：[自动监控 开关]（可在设置里隐藏）/ 版本号 + 检查更新 / 设置
        int sh = sidebar.Height;
        settingsBtn.Bounds = new Rectangle(S(8), sh - S(12) - S(40), sw - S(16), S(40));
        updateRow = new Rectangle(0, settingsBtn.Top - S(44), sw, S(44));
        updateBtn.Location = new Point(sw - S(16) - updateBtn.Width, updateRow.Y + (updateRow.Height - updateBtn.Height) / 2);
        bool showAuto = store.Prefs.ShowAutoToggle;
        autoToggle.Visible = showAuto;
        autoRow = new Rectangle(0, updateRow.Y - (showAuto ? S(42) : 0), sw, showAuto ? S(42) : 0);
        autoToggle.Location = new Point(sw - S(18) - autoToggle.Width, autoRow.Y + (autoRow.Height - autoToggle.Height) / 2);
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

        int x = S(18);
        var av = kit.Avatar;
        g.DrawImage(av, x, (head - av.Height) / 2);
        x += av.Width + S(11);
        x += Gfx.Logo(g, t, S(25), x, head / 2f);
        Gfx.Text(g, "应用使用时间", kit.Small, t.Muted, new Rectangle(x + S(13), S(4), S(160), head));

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
        // 版本号，检查过就换成检查结果
        string ver = updater == null || updater.Status.Length == 0 ? $"版本 {Updater.Text(Updater.Current)}" : updater.Status;
        var verInk = updater?.Failed == true ? t.Danger : updater?.Available != null ? t.Accent : t.Text;
        Gfx.Text(g, ver, kit.Body, verInk, new Rectangle(S(20), updateRow.Y, updateBtn.Left - S(28), updateRow.Height));
        if (autoRow.Height > 0) Gfx.Text(g, "自动监控", kit.Body, t.Text, new Rectangle(S(20), autoRow.Y, S(120), autoRow.Height));
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
            4 => (store.FirstDay(), today),
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
        if (store.Apps.Count > 0)
            list.Items.Add(new ItemList.Item { Header = true, Title = $"{(store.Prefs.AutoMode ? "自动监控" : "监控中")} · {store.Apps.Count}" });
        foreach (var a in store.Apps)
            list.Items.Add(new ItemList.Item { Id = a.Exe, Title = a.Name, Path = a.Path, Sub = "" });
        list.SelectedId = selected ?? "";
        LayoutAll();
        RefreshData();
    }

    void RefreshData()
    {
        tracker.Flush();
        var (from, to) = RangeDates();
        var rows = store.Apps.Select(a =>
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

        caption = range == 4 ? $"全部记录 · 从 {from.Year}年{from.Month}月{from.Day}日 起"
            : from == to ? (from == DateTime.Today ? "今天 · " : "昨天 · ") + Fmt.Day(from)
            : $"{from.Month}月{from.Day}日 – {to.Month}月{to.Day}日 · {Ranges[range]}";

        if (rows.Count == 0)
        {
            bool auto = store.Prefs.AutoMode;
            heading = auto ? "自动监控中" : "开始记录";
            meta = auto ? "打开的应用会自动出现在这里" : "选择要统计的应用，uutime 只记录你添加的程序";
            stats.ShowEmpty(auto);
        }
        else if (selected == null)
        {
            heading = "全部应用";
            meta = $"前台 {Fmt.Duration(rows.Sum(r => r.Fg))}   ·   {rows.Count} 个应用   ·   存档「{store.ProfileName}」";
            stats.ShowOverview(rows.OrderByDescending(r => r.Fg).ThenByDescending(r => r.Bg).ThenBy(r => r.Name).ToList());
        }
        else
        {
            var row = rows.First(r => r.Exe == selected);
            var app = store.Apps.First(a => a.Exe == selected);
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
        removeArmed = true; // 让 Disarm 按选中的应用重设按钮文字
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

    /// <summary>选中的是自动监控发现的应用：按钮是「屏蔽」，否则是「停止监控」。</summary>
    bool SelectedIsAuto => selected != null && store.Find(selected)?.Auto == true;

    /// <summary>点两下才生效：第一下变成“确认”，3 秒内再点一下。</summary>
    void RemoveSelected()
    {
        if (selected == null) return;
        if (!removeArmed)
        {
            removeArmed = true;
            removeBtn.Text = SelectedIsAuto ? "再点一次确认屏蔽（记录会保留）" : "再点一次确认（历史数据会保留）";
            removeBtn.FitWidth();
            disarm.Start();
            return;
        }
        tracker.Flush();
        if (SelectedIsAuto) store.SetBlocked(selected, true);
        else store.RemoveApp(selected);
        tracker.Rescan();
        Disarm();
        selected = null;
        RebuildItems();
    }

    void Disarm()
    {
        disarm.Stop();
        if (!removeArmed) return;
        removeArmed = false;
        removeBtn.Text = SelectedIsAuto ? "屏蔽" : "停止监控";
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
            store.Prefs.Theme = id;
            using var f = new MainForm(store, tracker, icon)
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-30000, -30000),
                ShowInTaskbar = false,
            };
            f.Show();
            Pump();
            Save(f, Path.Combine(outDir, $"{id}-overview.png"));
            if (store.Apps.Count > 0)
            {
                f.Select(store.Apps[0].Exe);
                Pump();
                Save(f, Path.Combine(outDir, $"{id}-app.png"));
            }
            if (id == themes[0])
            {
                ShowDialogPreview(new ThemePicker(f.kit, id), Path.Combine(outDir, $"{id}-themes.png"));
                ShowDialogPreview(new AddForm(f.kit, store.Watched), Path.Combine(outDir, $"{id}-add.png"));
                ShowDialogPreview(new ProfileForm(f.kit, store, _ => { }, () => { }), Path.Combine(outDir, $"{id}-profiles.png"));
                ShowDialogPreview(new SettingsForm(f.kit, store, () => { }), Path.Combine(outDir, $"{id}-settings.png"));
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

    /// <summary>按钮：没检查过 / 已是最新 → 检查更新；发现新版本 → 更新。</summary>
    async Task UpdateClicked()
    {
        if (updater == null || updater.Busy) return;
        var task = updater.Available != null ? updater.UpdateAsync() : updater.CheckAsync();
        SyncUpdate();
        await task;
        if (!IsDisposed) SyncUpdate();
    }

    void SyncUpdate()
    {
        if (updater == null) return;
        bool has = updater.Available != null;
        updateBtn.Text = updater.Busy ? (has ? "下载中…" : "检查中…") : has ? "更新" : "检查更新";
        updateBtn.Kind = has ? ButtonKind.Accent : ButtonKind.Outline;
        updateBtn.Enabled = !updater.Busy;
        kit.Tip.SetToolTip(updateBtn, updater.Failed ? updater.Detail
            : has ? "下载新版本并安装，装完自动重新打开" : $"当前版本 {Updater.Text(Updater.Current)}，点一下去 GitHub 看有没有新版本");
        LayoutAll();
        sidebar.Invalidate();
    }

    string ProfileLabel() => store.ProfileName.Length > 12 ? store.ProfileName[..11] + "…" : store.ProfileName;

    void UpdateProfileButton()
    {
        profileBtn.Text = ProfileLabel();
        profileBtn.FitWidth();
        LayoutAll();
    }

    void OpenProfiles()
    {
        tracker.Flush();
        store.Save();
        using var dlg = new ProfileForm(kit, store, SwitchProfile, tracker.Flush);
        dlg.NameChanged += UpdateProfileButton;
        dlg.ShowDialog(this);
        UpdateProfileButton();
        // 设置跟着存档走：换了存档可能换了主题，对话框关了再重建
        if (kit.T.Id != store.Prefs.Theme)
        {
            Build();
            ThemeChanged?.Invoke();
        }
    }

    /// <summary>切换存档：先把到现在为止的时间记到旧存档，再换成新存档的应用列表。</summary>
    void SwitchProfile(string id)
    {
        tracker.Flush();
        store.SwitchProfile(id);
        tracker.Rescan();
        selected = null;
        Disarm();
        PrefsChanged();
        UpdateProfileButton();
    }

    /// <summary>自动监控相关的设置变了：重新扫描、刷新列表和侧边栏开关。</summary>
    void PrefsChanged()
    {
        tracker.Rescan();
        autoToggle.Checked = store.Prefs.AutoMode;
        RebuildItems();
        sidebar.Invalidate();
    }

    void OpenSettings()
    {
        // 换主题时设置页返回 Retry：主窗口按新主题重建后再打开设置
        while (true)
        {
            DialogResult r;
            using (var dlg = new SettingsForm(kit, store, PrefsChanged)) r = dlg.ShowDialog(this);
            if (r != DialogResult.Retry) break;
            Build();
            ThemeChanged?.Invoke();
        }
        PrefsChanged();
    }

    void OnUpdaterChanged()
    {
        if (!IsDisposed && IsHandleCreated) SyncUpdate();
    }

    void OnDiscovered()
    {
        if (!IsDisposed && Visible) RebuildItems();
    }
}
