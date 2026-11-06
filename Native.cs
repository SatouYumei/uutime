using System.Runtime.InteropServices;

namespace UuTime;

internal static unsafe class Native
{
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    const uint TH32CS_SNAPPROCESS = 0x00000002;
    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    public delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmod, WinEventProc proc, uint pid, uint tid, uint flags);

    [DllImport("user32.dll")]
    public static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [DllImport("user32.dll")]
    static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc proc, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);

    [DllImport("user32.dll")]
    static extern int GetWindowTextLengthW(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern nint GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("dwmapi.dll")]
    static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

    [DllImport("user32.dll")]
    static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("kernel32.dll")]
    static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, char* buffer, ref uint size);

    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);

    [DllImport("kernel32.dll")]
    static extern bool Process32FirstW(IntPtr snapshot, ref PROCESSENTRY32W entry);

    [DllImport("kernel32.dll")]
    static extern bool Process32NextW(IntPtr snapshot, ref PROCESSENTRY32W entry);

    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    static extern bool SetProcessWorkingSetSize(IntPtr process, nint min, nint max);

    [StructLayout(LayoutKind.Sequential)]
    struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public nuint th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        public fixed char szExeFile[260];
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public ushort wButtons;
        public byte bLeftTrigger, bRightTrigger;
        public short sThumbLX, sThumbLY, sThumbRX, sThumbRY;
    }

    [DllImport("xinput1_4.dll")]
    static extern uint XInputGetState(uint index, out XINPUT_STATE state);

    const int ThumbDeadzone = 8000;  // 摇杆漂移一般在这个范围内
    const byte TriggerThreshold = 30;
    static readonly bool[] padConnected = new bool[4];
    static readonly ushort[] padButtons = new ushort[4];
    static int padScan;

    /// <summary>
    /// 手柄（XInput）此刻是否有人在操作：按着键、扳机、推着摇杆，或按键和上次采样不同。
    /// GetLastInputInfo 不认手柄，所以单独采样。没接的手柄槽查询较慢，每 6 次才查一遍。
    /// </summary>
    public static bool GamepadActive()
    {
        bool scanEmpty = padScan++ % 6 == 0;
        bool active = false;
        for (uint i = 0; i < 4; i++)
        {
            if (!padConnected[i] && !scanEmpty) continue;
            padConnected[i] = XInputGetState(i, out var s) == 0;
            if (!padConnected[i]) continue;
            if (s.wButtons != 0 || s.wButtons != padButtons[i]
                || s.bLeftTrigger > TriggerThreshold || s.bRightTrigger > TriggerThreshold
                || Math.Abs((int)s.sThumbLX) > ThumbDeadzone || Math.Abs((int)s.sThumbLY) > ThumbDeadzone
                || Math.Abs((int)s.sThumbRX) > ThumbDeadzone || Math.Abs((int)s.sThumbRY) > ThumbDeadzone)
                active = true;
            padButtons[i] = s.wButtons;
        }
        return active;
    }

    public static uint IdleMilliseconds()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)sizeof(LASTINPUTINFO) };
        return GetLastInputInfo(ref info) ? (uint)Environment.TickCount - info.dwTime : 0;
    }

    public static string? ProcessImagePath(uint pid)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            char* buf = stackalloc char[1024];
            uint size = 1024;
            return QueryFullProcessImageNameW(h, 0, buf, ref size) ? new string(buf, 0, (int)size) : null;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    /// <summary>窗口所属进程的 exe 名（小写）。UWP 应用的外壳是 ApplicationFrameHost，需要找到真正的子进程。</summary>
    public static string? WindowExe(IntPtr hwnd) => WindowExe(hwnd, out _);

    public static string? WindowExe(IntPtr hwnd, out uint pid)
    {
        pid = 0;
        if (hwnd == IntPtr.Zero) return null;
        GetWindowThreadProcessId(hwnd, out pid);
        string? exe = ExeName(ProcessImagePath(pid));
        if (exe == "applicationframehost.exe")
        {
            uint host = pid, child = 0;
            EnumChildWindows(hwnd, (c, _) =>
            {
                GetWindowThreadProcessId(c, out uint cp);
                if (cp != host) { child = cp; return false; }
                return true;
            }, IntPtr.Zero);
            if (child != 0 && ExeName(ProcessImagePath(child)) is { } real)
            {
                exe = real;
                pid = child;
            }
        }
        return exe;
    }

    public static string? ExeName(string? path) =>
        path == null ? null : Path.GetFileName(path).ToLowerInvariant();

    /// <summary>枚举所有进程，把命中 watched 的 exe 名放进 result。不为每个进程分配字符串。</summary>
    public static void FindRunning(HashSet<string> watched, HashSet<string> result)
    {
        result.Clear();
        if (watched.Count == 0) return;
        var lookup = watched.GetAlternateLookup<ReadOnlySpan<char>>();
        IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == IntPtr.Zero || snap == -1) return;
        try
        {
            var e = new PROCESSENTRY32W { dwSize = (uint)sizeof(PROCESSENTRY32W) };
            if (!Process32FirstW(snap, ref e)) return;
            do
            {
                var name = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(e.szExeFile);
                if (lookup.TryGetValue(name, out string? exe)) result.Add(exe);
            } while (Process32NextW(snap, ref e));
        }
        finally
        {
            CloseHandle(snap);
        }
    }

    public delegate void ProcessVisitor(uint pid, ReadOnlySpan<char> exe);

    /// <summary>遍历所有进程（只拿 pid 和 exe 名，不打开进程）。</summary>
    public static void ScanProcesses(ProcessVisitor visit)
    {
        IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == IntPtr.Zero || snap == -1) return;
        try
        {
            var e = new PROCESSENTRY32W { dwSize = (uint)sizeof(PROCESSENTRY32W) };
            if (!Process32FirstW(snap, ref e)) return;
            do
            {
                if (e.th32ProcessID > 4) visit(e.th32ProcessID, MemoryMarshal.CreateReadOnlySpanFromNullTerminated(e.szExeFile));
            } while (Process32NextW(snap, ref e));
        }
        finally
        {
            CloseHandle(snap);
        }
    }

    static HashSet<uint>? windowPids;
    static readonly EnumWindowsProc collectWindow = (hwnd, _) =>
    {
        const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, DWMWA_CLOAKED = 14;
        if (!IsWindowVisible(hwnd) || GetWindow(hwnd, 4 /* GW_OWNER */) != IntPtr.Zero) return true;
        if ((GetWindowLongPtr(hwnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0 || GetWindowTextLengthW(hwnd) == 0) return true;
        if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, 4) == 0 && cloaked != 0) return true; // 挂起的商店应用、其他虚拟桌面
        GetWindowThreadProcessId(hwnd, out uint pid);
        windowPids!.Add(pid);
        return true;
    };

    /// <summary>有“正常窗口”（任务栏上能看到的那种）的进程。</summary>
    public static void WindowedPids(HashSet<uint> result)
    {
        result.Clear();
        windowPids = result;
        EnumWindows(collectWindow, IntPtr.Zero);
        windowPids = null;
    }

    public static void TrimWorkingSet()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);
    }
}
