namespace UuTime;

internal static class Program
{
    const string MutexName = @"Local\uutime.single";
    const string ShowEventName = @"Local\uutime.show";

    [STAThread]
    static void Main(string[] args)
    {
#if DEBUG
        int render = Array.IndexOf(args, "--render");
        if (render >= 0)
        {
            // --render <输出目录> [主题id...]，不占单实例，不影响正在运行的 uutime
            ApplicationConfiguration.Initialize();
            var store = Store.Load();
            using var tracker = new Tracker(store);
            using var icon = AppIcon.Create();
            string[] themes = args.Length > render + 2 ? args[(render + 2)..] : ["dark", "light"];
            MainForm.RenderPreviews(store, tracker, icon, args[render + 1], themes);
            return;
        }
#endif
        using var mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            // 已经在运行：通知它把窗口打开
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var ev)) { ev.Set(); ev.Dispose(); }
            return;
        }

        ApplicationConfiguration.Initialize();
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        bool startHidden = args.Contains("--tray");
        Application.Run(new TrayContext(startHidden, showEvent));
    }
}
