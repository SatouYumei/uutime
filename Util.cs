using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace UuTime;

internal static class AppIcon
{
    static Stream Resource(string name) => typeof(AppIcon).Assembly.GetManifestResourceStream(name)!;

    /// <summary>程序图标（uutime.ico，由 tools\make-icon.ps1 从头像生成），按托盘小图标尺寸取帧。</summary>
    public static Icon Create()
    {
        using var s = Resource("uutime.ico");
        return new Icon(s, SystemInformation.SmallIconSize);
    }

    /// <summary>圆形头像，边缘抗锯齿。用完要 Dispose。</summary>
    public static Bitmap Avatar(int size)
    {
        using var s = Resource("avatar.png");
        using var src = Image.FromStream(s);
        using var scaled = new Bitmap(size, size);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(src, 0, 0, size, size);
        }
        var round = new Bitmap(size, size);
        using (var g = Graphics.FromImage(round))
        using (var brush = new TextureBrush(scaled))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.FillEllipse(brush, 0, 0, size, size);
        }
        return round;
    }
}

internal static class Links
{
    /// <summary>用默认浏览器打开网址。</summary>
    public static void Open(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { }
    }
}

internal static class AppInfo
{
    /// <summary>应用的显示名：exe 文件说明，没有就用文件名。</summary>
    public static string FriendlyName(string? path, string exe)
    {
        try
        {
            if (path != null && System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } d) return d.Trim();
        }
        catch (Exception) { }
        return Path.GetFileNameWithoutExtension(exe);
    }
}

internal static class Autostart
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    // 从 bin 运行的开发版用另一个名字，免得覆盖安装版的开机自启
    static string Name => Updater.Installed ? "uutime" : "uutime-dev";

    static string Command => $"\"{Environment.ProcessPath}\" --tray";

    public static bool Enabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key);
            return k?.GetValue(Name) is string v && v == Command;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(Key);
            if (value) k.SetValue(Name, Command);
            else k.DeleteValue(Name, false);
        }
    }
}

internal static class Fmt
{
    static readonly string[] Weekdays = ["周日", "周一", "周二", "周三", "周四", "周五", "周六"];

    public static string Duration(long ms)
    {
        long s = ms / 1000;
        if (s < 60) return $"{s}秒";
        long m = s / 60;
        if (m < 60) return $"{m}分{s % 60:D2}秒";
        return $"{m / 60}小时{m % 60:D2}分";
    }

    /// <summary>大号数字用：数字和单位分开，单位画小一号。</summary>
    public static (string text, bool unit)[] Parts(long ms)
    {
        long s = ms / 1000, m = s / 60;
        if (s < 60) return [($"{s}", false), ("秒", true)];
        if (m < 60) return [($"{m}", false), ("分", true), ($"{s % 60:D2}", false), ("秒", true)];
        return [($"{m / 60}", false), ("小时", true), ($"{m % 60:D2}", false), ("分", true)];
    }

    public static string Axis(long minutes) =>
        minutes == 0 ? "0" : minutes < 60 ? $"{minutes}分" : minutes % 60 == 0 ? $"{minutes / 60}小时" : $"{minutes / 60.0:0.#}小时";

    public static string Day(DateTime d) => $"{d.Month}月{d.Day}日 {Weekdays[(int)d.DayOfWeek]}";
}
