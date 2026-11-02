using System.Text.Json;
using System.Text.Json.Serialization;

namespace UuTime;

internal sealed class AppEntry
{
    public string Exe { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Path { get; set; }
}

internal sealed class Config
{
    public List<AppEntry> Apps { get; set; } = new();
    /// <summary>无键鼠操作超过这么多分钟后，前台时间不再累计（计入后台）。0 表示不判断。</summary>
    public int IdleMinutes { get; set; } = 5;
    public string Theme { get; set; } = "dark";
}

/// <summary>毫秒</summary>
internal sealed class Stat
{
    public long Fg { get; set; }
    public long Bg { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Config))]
[JsonSerializable(typeof(Dictionary<string, Dictionary<string, Stat>>))]
internal sealed partial class JsonCtx : JsonSerializerContext;

/// <summary>配置和每日统计，存放在 %LOCALAPPDATA%\uutime。</summary>
internal sealed class Store
{
    static readonly string Dir = Environment.GetEnvironmentVariable("UUTIME_DATA") ?? System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "uutime");
    static readonly string ConfigFile = System.IO.Path.Combine(Dir, "config.json");
    static readonly string UsageFile = System.IO.Path.Combine(Dir, "usage.json");

    // 统计文件没必要缩进，体积更小
    static readonly JsonCtx Compact = new(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    readonly object sync = new();
    bool dirty;

    public Config Config { get; private set; } = new();
    /// <summary>日期(yyyy-MM-dd) → exe → 统计</summary>
    Dictionary<string, Dictionary<string, Stat>> usage = new();
    public HashSet<string> Watched { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static Store Load()
    {
        var s = new Store();
        s.Config = Read(ConfigFile, JsonCtx.Default.Config) ?? new Config();
        s.usage = Read(UsageFile, JsonCtx.Default.DictionaryStringDictionaryStringStat) ?? new();
        s.RebuildWatched();
        return s;
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
        foreach (var a in Config.Apps) Watched.Add(a.Exe);
    }

    public void AddApps(IEnumerable<AppEntry> apps)
    {
        lock (sync)
        {
            foreach (var a in apps)
                if (Watched.Add(a.Exe)) Config.Apps.Add(a);
            dirty = true;
        }
        SaveConfig();
    }

    public void RemoveApp(string exe)
    {
        lock (sync)
        {
            Config.Apps.RemoveAll(a => string.Equals(a.Exe, exe, StringComparison.OrdinalIgnoreCase));
            RebuildWatched();
        }
        SaveConfig();
    }

    public void SetIdleMinutes(int minutes)
    {
        Config.IdleMinutes = minutes;
        SaveConfig();
    }

    public void SetTheme(string id)
    {
        Config.Theme = id;
        SaveConfig();
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

    void SaveConfig()
    {
        lock (sync) WriteAtomic(ConfigFile, JsonSerializer.Serialize(Config, JsonCtx.Default.Config));
    }

    public void Save()
    {
        lock (sync)
        {
            if (!dirty) return;
            WriteAtomic(UsageFile, JsonSerializer.Serialize(usage, Compact.DictionaryStringDictionaryStringStat));
            dirty = false;
        }
    }

    static void WriteAtomic(string file, string content)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            string tmp = file + ".tmp";
            File.WriteAllText(tmp, content);
            File.Move(tmp, file, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
