using System.Diagnostics;
using System.Security.Cryptography;

namespace UuTime;

/// <summary>
/// 从 GitHub Releases 检查更新，只在用户点「检查更新」时联网。不走 API（共用出口 IP 时匿名额度很容易用完），
/// 而是看 releases/latest 跳转到哪个 tag，再按固定文件名下载安装包和它的 .sha256 校验。
/// 用户确认更新后静默安装，装完在托盘里重新启动。
/// </summary>
internal sealed class Updater(Action exit)
{
    const string Repo = "https://github.com/SatouYumei/uutime";

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

    /// <summary>下载安装包并核对 SHA-256，返回本地路径。</summary>
    internal static async Task<string> DownloadAsync(HttpClient http, Version v)
    {
        string name = $"uutime-Setup-{Text(v)}.exe";
        string url = $"{Repo}/releases/download/v{Text(v)}/{name}";
        string expected = (await http.GetStringAsync(url + ".sha256")).Trim().Split(' ', '\t', '\r', '\n')[0].ToLowerInvariant();

        string dir = Path.Combine(Path.GetTempPath(), "uutime-update");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, name);
        await using (var src = await http.GetStreamAsync(url))
        await using (var dst = File.Create(file))
            await src.CopyToAsync(dst);

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

    void Set(string status, bool failed = false, string detail = "")
    {
        Status = status;
        Failed = failed;
        Detail = detail;
    }

    /// <summary>查最新版本；有新版本时 Available 不为 null。</summary>
    public async Task CheckAsync()
    {
        if (Busy) return;
        Busy = true;
        try
        {
            using var http = Client();
            var latest = await LatestAsync(http);
            if (latest == null) { Set("检查失败", true, "没能从 GitHub 读到最新版本号"); return; }
            Available = latest > Current ? latest : null;
            Set(Available != null ? $"新版本 {Text(latest)}" : $"已是最新 {Text(Current)}");
        }
        catch (Exception ex)
        {
            Set("检查失败", true, ex.Message);
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
            Set("开发版不能自己更新", true, "从 bin 运行的开发版不会安装更新，请到 GitHub Releases 下载安装包");
            return;
        }
        Busy = true;
        try
        {
            using var http = Client();
            string file = await DownloadAsync(http, v);
            Process.Start(new ProcessStartInfo(file, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /UPDATE=1") { UseShellExecute = true });
            exit();
        }
        catch (Exception ex)
        {
            Set("更新失败", true, ex.Message);
        }
        finally
        {
            Busy = false;
        }
    }
}
