using System.Text.Json;
using System.Text.Json.Serialization;

namespace UuTime;

internal sealed class AppEntry
{
    public string Exe { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Path { get; set; }
    /// <summary>自动监控发现的（不是手动添加的）。关掉自动监控时不显示、不计时，记录保留。</summary>
    public bool Auto { get; set; }
}

/// <summary>自动监控范围</summary>
internal enum AutoScope
{
    /// <summary>前台 + 有窗口的应用（默认）</summary>
    Windows = 0,
    /// <summary>只记前台，不记后台</summary>
    Foreground = 1,
    /// <summary>所有进程（不含 Windows 系统目录里的）</summary>
    All = 2,
}

/// <summary>存档自己的设置，跟着存档走（切换、备份、导入都带着）。</summary>
internal sealed class Prefs
{
    /// <summary>无键鼠操作超过这么多分钟后，前台时间不再累计（计入后台）。0 表示不判断。</summary>
    public int IdleMinutes { get; set; } = 5;
    public string Theme { get; set; } = "dark";
    /// <summary>定时检查新版本并推送通知。</summary>
    public bool CheckUpdates { get; set; } = true;
    public bool AutoMode { get; set; }
    public AutoScope AutoScope { get; set; }
    /// <summary>主界面侧边栏显示自动监控开关。</summary>
    public bool ShowAutoToggle { get; set; } = true;
    /// <summary>自动监控时不记录的 exe。</summary>
    public List<string> Blocked { get; set; } = new();

    public Prefs Clone() => new()
    {
        IdleMinutes = IdleMinutes, Theme = Theme, CheckUpdates = CheckUpdates, AutoMode = AutoMode,
        AutoScope = AutoScope, ShowAutoToggle = ShowAutoToggle, Blocked = new(Blocked),
    };
}

/// <summary>全局：当前是哪个存档。theme / idleMinutes 是 1.1 以前的全局设置，只用来给老存档补上。</summary>
internal sealed class Settings
{
    public int IdleMinutes { get; set; } = 5;
    public string Theme { get; set; } = "dark";
    /// <summary>当前存档的 id（profiles 下的文件夹名）。</summary>
    public string Profile { get; set; } = "";
}

/// <summary>一个存档：名字、监控的应用、设置。统计在同目录的 usage.json。</summary>
internal sealed class Profile
{
    public string Name { get; set; } = "";
    public List<AppEntry> Apps { get; set; } = new();
    public Prefs? Prefs { get; set; }
}

/// <summary>1.0 版的 config.json，只在迁移时读取。</summary>
internal sealed class LegacyConfig
{
    public List<AppEntry> Apps { get; set; } = new();
    public int IdleMinutes { get; set; } = 5;
    public string Theme { get; set; } = "dark";
}

/// <summary>备份文件：一个存档的全部内容。</summary>
internal sealed class Backup
{
    public string Format { get; set; } = "";
    public int Version { get; set; }
    public string Name { get; set; } = "";
    public string Exported { get; set; } = "";
    public List<AppEntry>? Apps { get; set; }
    public Prefs? Prefs { get; set; }
    public Dictionary<string, Dictionary<string, Stat>>? Usage { get; set; }
}

/// <summary>毫秒</summary>
internal sealed class Stat
{
    public long Fg { get; set; }
    public long Bg { get; set; }
}

internal sealed record ProfileInfo(string Id, string Name, int Apps, int Days, long Fg);

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(Settings))]
[JsonSerializable(typeof(Profile))]
[JsonSerializable(typeof(LegacyConfig))]
[JsonSerializable(typeof(Backup))]
[JsonSerializable(typeof(Dictionary<string, Dictionary<string, Stat>>))]
internal sealed partial class JsonCtx : JsonSerializerContext;

/// <summary>
/// 数据都在 %LOCALAPPDATA%\uutime：
///   settings.json                 全局设置
///   profiles\&lt;id&gt;\profile.json   存档名和应用
///   profiles\&lt;id&gt;\usage.json     每日统计
/// 1.0 版的 config.json / usage.json 只复制、不改动，可以随时退回旧版。
/// </summary>
internal sealed class Store
{
    const string BackupFormat = "uutime-backup";

    static readonly string Dir = Environment.GetEnvironmentVariable("UUTIME_DATA") ?? System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "uutime");
    static readonly string SettingsFile = System.IO.Path.Combine(Dir, "settings.json");
    static readonly string ProfilesDir = System.IO.Path.Combine(Dir, "profiles");
    static readonly string LegacyConfigFile = System.IO.Path.Combine(Dir, "config.json");
    static readonly string LegacyUsageFile = System.IO.Path.Combine(Dir, "usage.json");

    // 统计文件没必要缩进，体积更小
    static readonly JsonCtx Compact = new(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    readonly object sync = new();
    bool dirty;

    public Settings Settings { get; private set; } = new();
    Profile profile = new();
    /// <summary>日期(yyyy-MM-dd) → exe → 统计</summary>
    Dictionary<string, Dictionary<string, Stat>> usage = new();
    /// <summary>正在计时的 exe：手动添加的，加上自动监控开着时自动发现且没屏蔽的。</summary>
    public HashSet<string> Watched { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>存档里所有出现过的 exe（含屏蔽的、自动模式关着时的），自动发现时用来判断是不是新的。</summary>
    public HashSet<string> Known { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Blocked { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string ProfileId => Settings.Profile;
    public string ProfileName => profile.Name;
    public Prefs Prefs => profile.Prefs!;
    /// <summary>界面上显示的应用（和 Watched 一致）。</summary>
    public List<AppEntry> Apps { get; private set; } = new();

    static string ProfileDir(string id) => System.IO.Path.Combine(ProfilesDir, id);
    static string ProfileFile(string id) => System.IO.Path.Combine(ProfileDir(id), "profile.json");
    static string UsageFile(string id) => System.IO.Path.Combine(ProfileDir(id), "usage.json");

    public static Store Load()
    {
        var s = new Store();
        s.Settings = Read(SettingsFile, JsonCtx.Default.Settings) ?? Migrate();
        if (!File.Exists(ProfileFile(s.Settings.Profile)))
        {
            // 当前存档不见了：用第一个现有存档，一个都没有就新建
            s.Settings.Profile = ProfileIds().FirstOrDefault() ?? CreateEmpty(Environment.UserName);
            s.SaveSettings();
        }
        s.LoadProfile(s.Settings.Profile);
        return s;
    }

    /// <summary>第一次运行新版：把 1.0 的数据复制成第一个存档，旧文件原样保留。</summary>
    static Settings Migrate()
    {
        var legacy = Read(LegacyConfigFile, JsonCtx.Default.LegacyConfig);
        var settings = new Settings { IdleMinutes = legacy?.IdleMinutes ?? 5, Theme = legacy?.Theme ?? "dark" };
        string id = ProfileIds().FirstOrDefault() ?? CreateEmpty(Environment.UserName, legacy?.Apps);
        if (legacy != null && File.Exists(LegacyUsageFile) && !File.Exists(UsageFile(id)))
            File.Copy(LegacyUsageFile, UsageFile(id));
        settings.Profile = id;
        WriteAtomic(SettingsFile, JsonSerializer.Serialize(settings, JsonCtx.Default.Settings));
        return settings;
    }

    static IEnumerable<string> ProfileIds() =>
        Directory.Exists(ProfilesDir)
            ? Directory.GetDirectories(ProfilesDir).Select(d => System.IO.Path.GetFileName(d)!).Where(id => File.Exists(ProfileFile(id))).Order()
            : [];

    static string NewId() => DateTime.Now.ToString("yyyyMMdd") + "-" + Guid.NewGuid().ToString("N")[..6];

    static string CreateEmpty(string name, List<AppEntry>? apps = null, Prefs? prefs = null)
    {
        string id = NewId();
        Directory.CreateDirectory(ProfileDir(id));
        WriteAtomic(ProfileFile(id), JsonSerializer.Serialize(new Profile { Name = name, Apps = apps ?? new(), Prefs = prefs }, JsonCtx.Default.Profile));
        return id;
    }

    void LoadProfile(string id)
    {
        lock (sync)
        {
            profile = Read(ProfileFile(id), JsonCtx.Default.Profile) ?? new Profile { Name = Environment.UserName };
            usage = Read(UsageFile(id), JsonCtx.Default.DictionaryStringDictionaryStringStat) ?? new();
            dirty = false;
            if (profile.Prefs == null)
            {
                // 1.1 以前设置是全局的：补到这个存档里
                profile.Prefs = new Prefs { IdleMinutes = Settings.IdleMinutes, Theme = Settings.Theme };
                WriteAtomic(ProfileFile(id), JsonSerializer.Serialize(profile, JsonCtx.Default.Profile));
            }
            RebuildWatched();
        }
    }

    static T? Read<T>(string file, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info)
    {
        try
        {
            if (File.Exists(file)) return JsonSerializer.Deserialize(File.ReadAllText(file), info);
        }
        catch (Exception)
        {
            // 文件损坏时保留备份，从空数据开始
            try { File.Copy(file, file + ".bad", true); } catch { }
        }
        return default;
    }

    void RebuildWatched()
    {
        Watched.Clear();
        Known.Clear();
        Blocked.Clear();
        foreach (string b in Prefs.Blocked) Blocked.Add(b);
        foreach (var a in profile.Apps)
        {
            Known.Add(a.Exe);
            if (!a.Auto || (Prefs.AutoMode && !Blocked.Contains(a.Exe))) Watched.Add(a.Exe);
        }
        Apps = profile.Apps.Where(a => Watched.Contains(a.Exe)).ToList();
    }

    // ───── 当前存档 ─────

    public void AddApps(IEnumerable<AppEntry> apps)
    {
        lock (sync)
        {
            foreach (var a in apps)
            {
                // 自动发现过的转成手动添加
                if (profile.Apps.Find(x => string.Equals(x.Exe, a.Exe, StringComparison.OrdinalIgnoreCase)) is { } old) old.Auto = false;
                else profile.Apps.Add(a);
            }
            RebuildWatched();
        }
        SaveProfile();
    }

    /// <summary>自动监控发现了新应用。</summary>
    public void AddAuto(string exe, string name, string? path)
    {
        lock (sync)
        {
            if (Known.Contains(exe)) return;
            profile.Apps.Add(new AppEntry { Exe = exe, Name = name, Path = path, Auto = true });
            RebuildWatched();
        }
        SaveProfile();
    }

    public void SetBlocked(string exe, bool blocked)
    {
        lock (sync)
        {
            Prefs.Blocked.RemoveAll(b => string.Equals(b, exe, StringComparison.OrdinalIgnoreCase));
            if (blocked) Prefs.Blocked.Add(exe.ToLowerInvariant());
            RebuildWatched();
        }
        SaveProfile();
    }

    /// <summary>改了存档设置后写盘；自动监控相关的会重新算要计时的应用。</summary>
    public void SavePrefs()
    {
        lock (sync) RebuildWatched();
        SaveProfile();
    }

    /// <summary>某个 exe 在存档里记的名字和路径（屏蔽列表显示用）。</summary>
    public AppEntry? Find(string exe) =>
        profile.Apps.Find(a => string.Equals(a.Exe, exe, StringComparison.OrdinalIgnoreCase));

    public void RemoveApp(string exe)
    {
        lock (sync)
        {
            profile.Apps.RemoveAll(a => string.Equals(a.Exe, exe, StringComparison.OrdinalIgnoreCase));
            RebuildWatched();
        }
        SaveProfile();
    }


    public void Add(string exe, long fg, long bg)
    {
        string day = DateTime.Now.ToString("yyyy-MM-dd");
        lock (sync)
        {
            if (!usage.TryGetValue(day, out var apps)) usage[day] = apps = new(StringComparer.OrdinalIgnoreCase);
            if (!apps.TryGetValue(exe, out var st)) apps[exe] = st = new Stat();
            st.Fg += fg;
            st.Bg += bg;
            dirty = true;
        }
    }

    /// <summary>[from, to] 闭区间内的合计。</summary>
    public (long fg, long bg) Sum(string exe, DateTime from, DateTime to)
    {
        string a = from.ToString("yyyy-MM-dd"), b = to.ToString("yyyy-MM-dd");
        long fg = 0, bg = 0;
        lock (sync)
        {
            foreach (var (day, apps) in usage)
            {
                if (string.CompareOrdinal(day, a) < 0 || string.CompareOrdinal(day, b) > 0) continue;
                if (apps.TryGetValue(exe, out var st)) { fg += st.Fg; bg += st.Bg; }
            }
        }
        return (fg, bg);
    }

    /// <summary>最早有记录的那天，没有记录就是今天。</summary>
    public DateTime FirstDay()
    {
        lock (sync)
        {
            string? min = usage.Keys.Min(StringComparer.Ordinal);
            return min != null && DateTime.TryParse(min, out var d) ? d : DateTime.Today;
        }
    }

    // ───── 存档管理 ─────

    public List<ProfileInfo> Profiles()
    {
        var list = new List<ProfileInfo>();
        foreach (string id in ProfileIds())
        {
            if (id == ProfileId)
            {
                lock (sync) list.Add(Info(id, profile, usage));
                continue;
            }
            if (Read(ProfileFile(id), JsonCtx.Default.Profile) is not { } p) continue;
            list.Add(Info(id, p, Read(UsageFile(id), JsonCtx.Default.DictionaryStringDictionaryStringStat) ?? new()));
        }
        return list;
    }

    static ProfileInfo Info(string id, Profile p, Dictionary<string, Dictionary<string, Stat>> u) =>
        new(id, p.Name, p.Apps.Count, u.Count, u.Values.Sum(d => d.Values.Sum(s => s.Fg)));

    /// <summary>切换前先把当前存档写盘。</summary>
    public void SwitchProfile(string id)
    {
        if (id == ProfileId || !File.Exists(ProfileFile(id))) return;
        Save();
        LoadProfile(id);
        Settings.Profile = id;
        SaveSettings();
    }

    /// <summary>新存档沿用当前的主题等设置，但不带自动监控和屏蔽列表。</summary>
    public string CreateProfile(string name)
    {
        var prefs = Prefs.Clone();
        prefs.AutoMode = false;
        prefs.Blocked.Clear();
        return CreateEmpty(name, null, prefs);
    }

    public void RenameProfile(string id, string name)
    {
        if (id == ProfileId)
        {
            profile.Name = name;
            SaveProfile();
            return;
        }
        var p = Read(ProfileFile(id), JsonCtx.Default.Profile);
        if (p == null) return;
        p.Name = name;
        WriteAtomic(ProfileFile(id), JsonSerializer.Serialize(p, JsonCtx.Default.Profile));
    }

    /// <summary>删除存档（移到回收站，可以找回）。当前存档不能删。</summary>
    public bool DeleteProfile(string id)
    {
        if (id == ProfileId || !Directory.Exists(ProfileDir(id))) return false;
        try
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(ProfileDir(id),
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>把一个存档导出成单个 json 备份文件。</summary>
    public void Export(string id, string file)
    {
        Backup b;
        if (id == ProfileId)
        {
            lock (sync) b = new Backup { Name = profile.Name, Apps = profile.Apps, Prefs = profile.Prefs, Usage = usage };
        }
        else
        {
            var p = Read(ProfileFile(id), JsonCtx.Default.Profile) ?? new Profile();
            b = new Backup { Name = p.Name, Apps = p.Apps, Prefs = p.Prefs, Usage = Read(UsageFile(id), JsonCtx.Default.DictionaryStringDictionaryStringStat) ?? new() };
        }
        b.Format = BackupFormat;
        b.Version = 1;
        b.Exported = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string json;
        lock (sync) json = JsonSerializer.Serialize(b, JsonCtx.Default.Backup);
        File.WriteAllText(file, json);
    }

    /// <summary>导入备份为一个新存档（不会覆盖已有存档），返回新存档 id；不是备份文件返回 null。</summary>
    public string? Import(string file)
    {
        Backup? b;
        try { b = JsonSerializer.Deserialize(File.ReadAllText(file), JsonCtx.Default.Backup); }
        catch (Exception) { return null; }
        if (b == null || b.Format != BackupFormat || b.Apps == null) return null;

        var names = Profiles().Select(p => p.Name).ToHashSet();
        string name = string.IsNullOrWhiteSpace(b.Name) ? "导入的存档" : b.Name;
        if (names.Contains(name))
        {
            string baseName = name + "（导入）";
            name = baseName;
            for (int n = 2; names.Contains(name); n++) name = $"{baseName}{n}";
        }
        string id = CreateEmpty(name, b.Apps, b.Prefs);
        WriteAtomic(UsageFile(id), JsonSerializer.Serialize(b.Usage ?? new(), Compact.DictionaryStringDictionaryStringStat));
        return id;
    }

    // ───── 写盘 ─────

    void SaveSettings() => WriteAtomic(SettingsFile, JsonSerializer.Serialize(Settings, JsonCtx.Default.Settings));

    void SaveProfile()
    {
        lock (sync) WriteAtomic(ProfileFile(ProfileId), JsonSerializer.Serialize(profile, JsonCtx.Default.Profile));
    }

    public void Save()
    {
        lock (sync)
        {
            if (!dirty) return;
            WriteAtomic(UsageFile(ProfileId), JsonSerializer.Serialize(usage, Compact.DictionaryStringDictionaryStringStat));
            dirty = false;
        }
    }

    static void WriteAtomic(string file, string content)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
            string tmp = file + ".tmp";
            File.WriteAllText(tmp, content);
            File.Move(tmp, file, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
