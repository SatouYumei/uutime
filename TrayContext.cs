using Microsoft.Win32;

namespace UuTime;

/// <summary>常驻托盘。主窗口按需创建，关闭即销毁，以保持后台内存最低。</summary>
internal sealed class TrayContext : ApplicationContext
{
    readonly Store store;
    readonly Tracker tracker;
    readonly Icon icon;
    readonly NotifyIcon tray;
    readonly ContextMenuStrip menu;
    readonly RegisteredWaitHandle showWait;
    readonly Updater updater;
    MainForm? form;

    void Notify(string text) => tray.ShowBalloonTip(5000, "uutime", text, ToolTipIcon.None);

    public TrayContext(bool startHidden, bool updated, EventWaitHandle showEvent)
    {
        store = Store.Load();
        tracker = new Tracker(store);
        icon = AppIcon.Create();
        string version = $"{Updater.Current.Major}.{Updater.Current.Minor}.{Updater.Current.Build}";

        menu = new ContextMenuStrip { Font = new Font(Kit.FontName, 9.5f), ShowImageMargin = false };
        menu.Items.Add("打开 uutime", null, (_, _) => ShowMain());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitThread());
        ThemeMenu();

        tray = new NotifyIcon { Icon = icon, Text = $"uutime {version} · 应用使用时间", ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowMain(); };

        // 推送通知点了就打开主窗口，在侧边栏点「更新」才装
        updater = new Updater(ExitThread, () => store.Prefs.CheckUpdates,
            v => Notify($"uutime {Updater.Text(v)} 发布了，点这里打开 uutime 更新"));
        tray.BalloonTipClicked += (_, _) => ShowMain();
        if (updated) Notify($"已更新到 {version}");

        // 创建控件后才有 WinForms 同步上下文
        var ui = SynchronizationContext.Current!;
        showWait = ThreadPool.RegisterWaitForSingleObject(showEvent,
            (_, _) => ui.Post(_ => ShowMain(), null), null, Timeout.Infinite, false);

        SystemEvents.SessionEnding += OnSessionEnding;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        if (startHidden) Native.TrimWorkingSet();
        else ShowMain();
    }

    void ThemeMenu()
    {
        var t = Theme.From(store.Prefs);
        menu.Renderer = new ToolStripProfessionalRenderer(new MenuColors(t)) { RoundedEdges = false };
        menu.BackColor = t.Card;
        foreach (ToolStripItem item in menu.Items)
        {
            item.ForeColor = t.Text;
            item.Padding = new Padding(4, 4, 12, 4);
        }
    }

    sealed class MenuColors(Theme t) : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => t.Card;
        public override Color MenuBorder => t.Border;
        public override Color MenuItemBorder => t.Hover;
        public override Color MenuItemSelected => t.Hover;
        public override Color SeparatorDark => t.Border;
        public override Color SeparatorLight => t.Card;
        public override Color ImageMarginGradientBegin => t.Card;
        public override Color ImageMarginGradientMiddle => t.Card;
        public override Color ImageMarginGradientEnd => t.Card;
    }

    void OnSessionEnding(object? sender, SessionEndingEventArgs e) => store.Save();

    void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) store.Save();
    }

    void ShowMain()
    {
        if (form == null)
        {
            form = new MainForm(store, tracker, icon, updater);
            form.ThemeChanged += ThemeMenu;
            form.FormClosed += (_, _) =>
            {
                form = null;
                Native.TrimWorkingSet();
            };
            form.Show();
        }
        if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
        if (!form.Visible) form.Show(); // 最小化时只是藏到托盘，页面状态还在
        form.Activate();
    }

    protected override void ExitThreadCore()
    {
        SystemEvents.SessionEnding -= OnSessionEnding;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        showWait.Unregister(null);
        updater.Dispose();
        form?.Close();
        tracker.Dispose();
        store.Save();
        tray.Visible = false;
        tray.Dispose();
        icon.Dispose();
        base.ExitThreadCore();
    }
}
