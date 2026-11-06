namespace UuTime;

/// <summary>
/// 前台切换靠系统事件推送（不轮询）；进程是否在运行每 5 秒扫描一次。
/// 某个被监控的 exe：是前台且用户不空闲 → 前台时间；否则只要进程活着 → 后台时间。
/// 自动监控时，扫描顺便把新出现的应用加进存档（按设置的范围：前台和有窗口的 / 只前台 / 所有进程）。
/// </summary>
internal sealed class Tracker : IDisposable
{
    const int TickMs = 5000;
    const int SaveEveryTicks = 12;   // 约 1 分钟落盘一次
    const long MaxGapMs = 30_000;    // 超过这个间隔说明睡眠/休眠过，不计入

    /// <summary>系统外壳组件，自动监控时不当成应用。</summary>
    static readonly HashSet<string> Shell = new(StringComparer.OrdinalIgnoreCase)
    {
        "uutime.exe", "applicationframehost.exe", "textinputhost.exe", "shellexperiencehost.exe",
        "startmenuexperiencehost.exe", "searchhost.exe", "searchapp.exe", "lockapp.exe", "dwm.exe",
        "ctfmon.exe", "sihost.exe", "shellhost.exe", "systemsettingsbroker.exe", "searchui.exe",
    };

    static readonly string WindowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\";

    readonly Store store;
    readonly Native.WinEventProc proc; // 必须持有引用，防止被 GC
    readonly IntPtr hook;
    readonly System.Windows.Forms.Timer timer;
    readonly HashSet<string> running = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<uint> windowPids = new();
    /// <summary>这次运行里判定为系统进程、不收录的 exe（不写盘，下次启动重新判断）。</summary>
    readonly HashSet<string> ignored = new(StringComparer.OrdinalIgnoreCase);
    IntPtr fgWindow;
    uint fgPid;
    long lastTick;
    long lastPadInput = long.MinValue / 2; // 键盘鼠标之外，手柄最后一次有操作的时刻
    int ticks;

    public string? ForegroundExe { get; private set; }

    public Tracker(Store store)
    {
        this.store = store;
        proc = OnForeground;
        hook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, proc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
        CheckForeground();
        Rescan();
        lastTick = Environment.TickCount64;

        timer = new System.Windows.Forms.Timer { Interval = TickMs };
        timer.Tick += (_, _) =>
        {
            if (Native.GamepadActive()) lastPadInput = Environment.TickCount64;
            Flush();
            CheckForeground(); // 事件偶尔会漏（全屏游戏切入时常见），每次 tick 再核对一次
            Rescan();
            if (++ticks % SaveEveryTicks == 0) store.Save();
        };
        timer.Start();
    }

    /// <summary>自动监控发现了新应用（界面要刷新列表）。</summary>
    public event Action? Discovered;

    public bool IsRunning(string exe) => running.Contains(exe) || IsForeground(exe);

    bool IsForeground(string exe) => string.Equals(ForegroundExe, exe, StringComparison.OrdinalIgnoreCase);

    void OnForeground(IntPtr h, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        Flush(); // 先把切换前的时间结算给旧的前台
        CheckForeground();
    }

    /// <summary>以 GetForegroundWindow 为准；窗口没变就不重新查进程。</summary>
    void CheckForeground()
    {
        IntPtr hwnd = Native.GetForegroundWindow();
        if (hwnd == fgWindow) return;
        fgWindow = hwnd;
        ForegroundExe = Native.WindowExe(hwnd, out fgPid);
        DiscoverForeground();
    }

    void DiscoverForeground()
    {
        if (store.Prefs.AutoMode && ForegroundExe != null && IsNew(ForegroundExe))
            Discover(fgPid, ForegroundExe, systemFilter: false);
    }

    bool IsNew(string exe) => !store.Known.Contains(exe) && !store.Blocked.Contains(exe) && !Shell.Contains(exe) && !ignored.Contains(exe);

    /// <summary>新应用：查路径和文件说明里的名字，收进存档。</summary>
    void Discover(uint pid, string exe, bool systemFilter)
    {
        string? path = Native.ProcessImagePath(pid);
        // “所有进程”里排除 Windows 自己的后台进程，打不开的（受保护的系统进程）也算
        if (systemFilter && (path == null || path.StartsWith(WindowsDir, StringComparison.OrdinalIgnoreCase)))
        {
            ignored.Add(exe);
            return;
        }
        store.AddAuto(exe, AppInfo.FriendlyName(path, exe), path);
        Discovered?.Invoke();
    }

    /// <summary>刷新哪些被监控的应用在运行；自动监控时顺便发现新应用。</summary>
    public void Rescan()
    {
        var prefs = store.Prefs;
        if (!prefs.AutoMode)
        {
            Native.FindRunning(store.Watched, running);
            return;
        }
        DiscoverForeground();
        running.Clear();
        if (prefs.AutoScope == AutoScope.Foreground) return; // 只记前台：不算后台时间
        bool windowsOnly = prefs.AutoScope == AutoScope.Windows;
        if (windowsOnly) Native.WindowedPids(windowPids);

        var watched = store.Watched.GetAlternateLookup<ReadOnlySpan<char>>();
        var known = store.Known.GetAlternateLookup<ReadOnlySpan<char>>();
        var blocked = store.Blocked.GetAlternateLookup<ReadOnlySpan<char>>();
        var shell = Shell.GetAlternateLookup<ReadOnlySpan<char>>();
        var skip = ignored.GetAlternateLookup<ReadOnlySpan<char>>();
        var fresh = new List<(uint pid, string exe)>();
        Native.ScanProcesses((pid, name) =>
        {
            // 已经在监控的：进程在就算后台（不管有没有窗口，和手动添加的一样）
            if (watched.TryGetValue(name, out string? exe)) { running.Add(exe); return; }
            if (known.Contains(name) || blocked.Contains(name) || shell.Contains(name) || skip.Contains(name)) return;
            if (windowsOnly && !windowPids.Contains(pid)) return;
            // 只有真正没见过的名字才分配字符串
            string lower = name.ToString().ToLowerInvariant();
            if (!fresh.Exists(f => f.exe == lower)) fresh.Add((pid, lower));
        });
        foreach (var (pid, exe) in fresh)
        {
            Discover(pid, exe, systemFilter: !windowsOnly);
            if (store.Watched.Contains(exe)) running.Add(exe);
        }
    }

    /// <summary>把距上次结算的时间计入各 app。</summary>
    public void Flush()
    {
        long now = Environment.TickCount64;
        long dt = now - lastTick;
        lastTick = now;
        if (dt <= 0 || dt > MaxGapMs || store.Watched.Count == 0) return;

        int idleMin = store.Prefs.IdleMinutes;
        long limit = idleMin * 60_000L;
        bool active = idleMin <= 0 || Native.IdleMilliseconds() < limit || now - lastPadInput < limit;

        foreach (string exe in store.Watched)
        {
            if (active && IsForeground(exe)) store.Add(exe, dt, 0);
            else if (IsRunning(exe)) store.Add(exe, 0, dt);
        }
    }

    public void Dispose()
    {
        timer.Dispose();
        Flush();
        Native.UnhookWinEvent(hook);
    }
}
