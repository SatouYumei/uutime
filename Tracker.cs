namespace UuTime;

/// <summary>
/// 前台切换靠系统事件推送（不轮询）；进程是否在运行每 5 秒扫描一次。
/// 某个被监控的 exe：是前台且用户不空闲 → 前台时间；否则只要进程活着 → 后台时间。
/// </summary>
internal sealed class Tracker : IDisposable
{
    const int TickMs = 5000;
    const int SaveEveryTicks = 12;   // 约 1 分钟落盘一次
    const long MaxGapMs = 30_000;    // 超过这个间隔说明睡眠/休眠过，不计入

    readonly Store store;
    readonly Native.WinEventProc proc; // 必须持有引用，防止被 GC
    readonly IntPtr hook;
    readonly System.Windows.Forms.Timer timer;
    readonly HashSet<string> running = new(StringComparer.OrdinalIgnoreCase);
    IntPtr fgWindow;
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
        Native.FindRunning(store.Watched, running);
        lastTick = Environment.TickCount64;

        timer = new System.Windows.Forms.Timer { Interval = TickMs };
        timer.Tick += (_, _) =>
        {
            if (Native.GamepadActive()) lastPadInput = Environment.TickCount64;
            Flush();
            CheckForeground(); // 事件偶尔会漏（全屏游戏切入时常见），每次 tick 再核对一次
            Native.FindRunning(store.Watched, running);
            if (++ticks % SaveEveryTicks == 0) store.Save();
        };
        timer.Start();
    }

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
        ForegroundExe = Native.WindowExe(hwnd);
    }

    /// <summary>把距上次结算的时间计入各 app。</summary>
    public void Flush()
    {
        long now = Environment.TickCount64;
        long dt = now - lastTick;
        lastTick = now;
        if (dt <= 0 || dt > MaxGapMs || store.Watched.Count == 0) return;

        int idleMin = store.Settings.IdleMinutes;
        long limit = idleMin * 60_000L;
        bool active = idleMin <= 0 || Native.IdleMilliseconds() < limit || now - lastPadInput < limit;

        foreach (string exe in store.Watched)
        {
            if (active && IsForeground(exe)) store.Add(exe, dt, 0);
            else if (IsRunning(exe)) store.Add(exe, 0, dt);
        }
    }

    public void Rescan() => Native.FindRunning(store.Watched, running);

    public void Dispose()
    {
        timer.Dispose();
        Flush();
        Native.UnhookWinEvent(hook);
    }
}
