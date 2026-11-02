using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace UuTime;

internal static class AppIcon
{
    public static Icon Create()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var fill = new SolidBrush(Color.FromArgb(0x3B, 0x82, 0xF6));
            g.FillEllipse(fill, 1, 1, 30, 30);
            using var pen = new Pen(Color.White, 3.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(pen, 16, 16, 16, 7);
            g.DrawLine(pen, 16, 16, 23, 20);
        }
        IntPtr h = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(h).Clone();
        Native.DestroyIcon(h);
        return icon;
    }
}

internal static class Autostart
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "uutime";

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
