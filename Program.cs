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
        int checkupdate = Array.IndexOf(args, "--checkupdate");
        if (checkupdate >= 0)
        {
            // --checkupdate <结果文件>：查最新版本并下载校验（不安装）
            string result;
            try
            {
                using var http = Updater.Client();
                var latest = Updater.LatestAsync(http).GetAwaiter().GetResult();
                string file = Updater.DownloadAsync(http, latest!).GetAwaiter().GetResult();
                result = $"current={Updater.Current} latest={latest} installed={Updater.Installed} downloaded={file} size={new FileInfo(file).Length}";
            }
            catch (Exception ex) { result = "error: " + ex; }
            File.WriteAllText(args[checkupdate + 1], result);
            return;
        }
        int selftest = Array.IndexOf(args, "--selftest");
        if (selftest >= 0)
        {
            // --selftest <结果文件>：在 UUTIME_DATA 指向的数据上走一遍存档管理
            var log = new List<string>();
            var store = Store.Load();
            string firstId = store.ProfileId;
            string backup = Path.Combine(Path.GetTempPath(), "uutime-selftest.json");
            store.Export(firstId, backup);
            string? imported = store.Import(backup);
            log.Add($"import: {imported != null}");
            log.Add($"import again (name clash): {store.Import(backup) != null}");
            log.Add($"import junk: {store.Import(args[selftest + 1]) == null}");
            store.RenameProfile(imported!, "测试改名");
            store.SwitchProfile(imported!);
            log.Add($"switched: {store.ProfileId == imported} name={store.ProfileName} apps={store.Apps.Count} firstDay={store.FirstDay():yyyy-MM-dd}");
            log.Add($"delete current refused: {!store.DeleteProfile(imported!)}");
            store.SwitchProfile(firstId);
            log.Add($"delete imported: {store.DeleteProfile(imported!)}");
            foreach (var p in store.Profiles()) log.Add($"profile {p.Id} {p.Name} apps={p.Apps} days={p.Days} fg={p.Fg}");
            File.Delete(backup);
            File.WriteAllLines(args[selftest + 1], log);
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
        Application.Run(new TrayContext(startHidden, args.Contains("--updated"), showEvent));
    }
}
