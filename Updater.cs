using System.Diagnostics;
using System.Security.Cryptography;

namespace UuTime;

/// <summary>
/// 从 GitHub Releases 检查更新。不走 API（共用出口 IP 时匿名额度很容易用完），
/// 而是看 releases/latest 跳转到哪个 tag，再按固定文件名下载安装包和它的 .sha256 校验。
/// 推送：打开设置里的「自动检查更新」后，每次启动 uutime 只查一次（启动 15 秒后），有新版本只通知，不自动装。
/// 用户点「更新」后才下载（显示进度）、安装，装完重新打开主窗口。下载失败就引导去 GitHub 手动下载。
/// </summary>
internal sealed class Updater : IDisposable
{
    public const string Repo = "https://github.com/SatouYumei/uutime";
    public const string ReleasesPage = Repo + "/releases";
    /// <summary>下载时这么久一点数据都没收到就算失败（连不上 GitHub 时常见）。</summary>
    static readonly TimeSpan Stall = TimeSpan.FromSeconds(30);
    const int FirstCheckMs = 15_000;

    readonly Action exit;
    readonly System.Windows.Forms.Timer timer = new() { Interval = FirstCheckMs };

    /// <param name="exit">退出 uutime，让安装程序替换文件</param>
    /// <param name="enabled">定时检查是否开着（存档设置）</param>
    /// <param name="push">发现新版本时的通知</param>
    public Updater(Action exit, Func<bool> enabled, Action<Version> push)
    {
        this.exit = exit;
        timer.Tick += async (_, _) =>
        {
            timer.Stop(); // 每次启动只查这一次
            if (!enabled() || Busy) return;
            await CheckAsync();
            if (Available is { } v) push(v);
            Native.TrimWorkingSet();
        };
        timer.Start();
    }

    /// <summary>检查或更新的状态变了（界面刷新按钮和文字）。</summary>
    public event Action? Changed;

    public void Dispose() => timer.Dispose();

    /// <summary>检查到的新版本，没有为 null。窗口关了再开也还记得。</summary>
    public Version? Available { get; private set; }
    public bool Busy { get; private set; }

    public static Version Current => Normalize(typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0));

    /// <summary>只有装在安装目录（有卸载程序）时才能自己更新；从 bin 运行的开发版不动。</summary>
    public static bool Installed => File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    public static string Text(Version v) => $"{v.Major}.{v.Minor}.{v.Build}";

    internal static HttpClient Client()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"uutime/{Text(Current)}");
        return http;
    }

    /// <summary>最新发布的版本号：releases/latest 会跳转到 releases/tag/vX.Y.Z。</summary>
    internal static async Task<Version?> LatestAsync(HttpClient http)
    {
        using var req = new HttpRequestMessage(HttpMethod.Head, Repo + "/releases/latest");
        using var resp = await http.SendAsync(req);
        string tag = resp.RequestMessage?.RequestUri?.Segments.LastOrDefault() ?? "";
        return Version.TryParse(tag.TrimStart('v', 'V'), out var v) ? Normalize(v) : null;
    }

    /// <summary>下载安装包并核对 SHA-256，返回本地路径。progress：已下载字节、总字节（不知道时为 -1）。</summary>
    internal static async Task<string> DownloadAsync(HttpClient http, Version v, Action<long, long>? progress = null)
    {
        string name = $"uutime-Setup-{Text(v)}.exe";
        string url = $"{Repo}/releases/download/v{Text(v)}/{name}";
        string expected = (await http.GetStringAsync(url + ".sha256")).Trim().Split(' ', '\t', '\r', '\n')[0].ToLowerInvariant();

        string dir = Path.Combine(Path.GetTempPath(), "uutime-update");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, name);
        using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? -1, done = 0;
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(file);
            var buf = new byte[81920];
            while (true)
            {
                using var idle = new CancellationTokenSource(Stall);
                int n;
                try { n = await src.ReadAsync(buf, idle.Token); }
                catch (OperationCanceledException) { throw new TimeoutException($"{Stall.TotalSeconds:0} 秒没有收到数据"); }
                if (n == 0) break;
                await dst.WriteAsync(buf.AsMemory(0, n));
                done += n;
                progress?.Invoke(done, total);
            }
            if (total > 0 && done != total) throw new IOException("下载不完整");
        }

        string actual;
        await using (var f = File.OpenRead(file))
            actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(f));
        if (actual != expected)
        {
            File.Delete(file);
            throw new InvalidDataException("下载的文件校验不一致");
        }
        return file;
    }

    /// <summary>界面上显示的检查结果，如「已是最新 1.0.1」。</summary>
    public string Status { get; private set; } = "";
    /// <summary>失败原因的详细说明（放在提示里）。</summary>
    public string Detail { get; private set; } = "";
    public bool Failed { get; private set; }
    /// <summary>下载进度 0~1，没在下载为 -1。</summary>
    public double Progress { get; private set; } = -1;
    /// <summary>自动更新走不通（下载失败、开发版），界面上引导去 GitHub 手动下载。</summary>
    public bool ManualDownload { get; private set; }

    /// <summary>这个版本的下载页。</summary>
    public string DownloadPage => Available is { } v ? $"{Repo}/releases/tag/v{Text(v)}" : ReleasesPage;

    /// <summary>网络错误说成人话（项目开了 UseSystemResourceKeys，系统自带的错误文字是资源键名）。</summary>
    static string Explain(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: { } code } => $"GitHub 返回了错误（{(int)code}）",
        HttpRequestException => "连不上 GitHub",
        TaskCanceledException => "连接 GitHub 超时",
        TimeoutException t => "下载卡住了：" + t.Message,
        InvalidDataException or IOException => ex.Message,
        _ => ex.GetType().Name,
    };

    const string Manual ="。连不上 GitHub 的话，可以在浏览器打开 " + ReleasesPage + " 手动下载安装包";

    void Set(string status, bool failed = false, string detail = "")
    {
        Status = status;
        Failed = failed;
        Detail = detail;
        Changed?.Invoke();
    }

#if DEBUG
    /// <summary>渲染预览用：直接设成某个状态。</summary>
    internal void Fake(Version? available, string status, bool failed, bool manual, double progress, bool busy)
    {
        Available = available;
        ManualDownload = manual;
        Progress = progress;
        Busy = busy;
        Set(status, failed, manual ? "连不上 GitHub" + Manual : "");
    }
#endif

    /// <summary>查最新版本；有新版本时 Available 不为 null。</summary>
    public async Task CheckAsync()
    {
        if (Busy) return;
        Busy = true;
        try
        {
            using var http = Client();
            var latest = await LatestAsync(http);
            if (latest == null) { Set("检查失败", true, "没能从 GitHub 读到最新版本号" + Manual); return; }
            Available = latest > Current ? latest : null;
            ManualDownload = false;
            Set(Available != null ? $"新版本 {Text(latest)}" : $"已是最新 {Text(Current)}");
        }
        catch (Exception ex)
        {
            Set("检查失败", true, Explain(ex) + Manual);
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>下载、校验、静默安装，然后退出 uutime。</summary>
    public async Task UpdateAsync()
    {
        if (Busy || Available is not { } v) return;
        if (!Installed)
        {
            ManualDownload = true;
            Set("开发版请手动下载", true, "从 bin 运行的开发版不会自己安装更新");
            return;
        }
        Busy = true;
        ManualDownload = false;
        Progress = 0;
        Set("下载中 0%");
        try
        {
            using var http = Client();
            int shown = -1;
            string file = await DownloadAsync(http, v, (done, total) =>
            {
                Progress = total > 0 ? done / (double)total : -1;
                int step = total > 0 ? (int)(done * 100 / total) : (int)(done / 1048576);
                if (step == shown) return; // 每变 1%（不知道总大小时每 1MB）才刷新界面
                shown = step;
                Set(total > 0 ? $"下载中 {step}%" : $"下载中 {done / 1048576.0:0.0} MB",
                    detail: total > 0 ? $"{done / 1048576.0:0.0} / {total / 1048576.0:0.0} MB" : "");
            });
            Progress = 1;
            Set("正在安装…");
            // /SILENT 只显示一个安装进度小窗，不用点任何按钮；装完由安装程序重新打开 uutime
            Process.Start(new ProcessStartInfo(file, "/SILENT /SUPPRESSMSGBOXES /NORESTART /SP- /UPDATE=1") { UseShellExecute = true });
            exit();
        }
        catch (Exception ex)
        {
            Progress = -1;
            ManualDownload = true;
            Set("下载失败", true, Explain(ex) + Manual);
        }
        finally
        {
            Busy = false;
        }
    }
}
