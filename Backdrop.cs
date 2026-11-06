using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace UuTime;

/// <summary>
/// 背景图导入：限制原图大小，统一缩到 2560 像素以内、存成 JPEG（几百 KB），客户端只用压缩后的这张。
/// </summary>
internal static class Wallpaper
{
    public const long MaxFileBytes = 50L * 1024 * 1024;
    const long MaxPixels = 100_000_000;
    const int MaxSide = 2560;
    const long JpegQuality = 88;

    public const string Filter = "图片 (*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp)|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp";
    public const string Limits = "JPG / PNG / BMP / GIF / WEBP，最大 50MB，导入时自动压缩";

    /// <summary>读原图、检查、缩放、压缩后写到 dest。失败返回给用户看的原因，成功返回 null。</summary>
    public static string? Import(string source, string dest)
    {
        var info = new FileInfo(source);
        if (!info.Exists) return "找不到这个文件";
        if (info.Length > MaxFileBytes) return $"图片有 {info.Length / 1024 / 1024}MB，超过了 50MB";

        Bitmap? scaled;
        try
        {
            scaled = DecodeGdi(source) ?? DecodeWic(source);
        }
        catch (TooLargeException)
        {
            return "图片太大了（超过 1 亿像素）";
        }
        if (scaled == null) return "打不开这张图片，换一张 JPG 或 PNG 试试";

        using (scaled)
        {
            try
            {
                var jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                using var ps = new EncoderParameters(1);
                ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, JpegQuality);
                string tmp = dest + ".tmp";
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                scaled.Save(tmp, jpeg, ps);
                File.Move(tmp, dest, true);
                return null;
            }
            catch (Exception ex)
            {
                return "保存图片失败：" + ex.Message;
            }
        }
    }

    sealed class TooLargeException : Exception;

    static Size Fit(int w, int h)
    {
        if ((long)w * h > MaxPixels) throw new TooLargeException();
        float k = Math.Min(1f, MaxSide / (float)Math.Max(w, h));
        return new Size(Math.Max(1, (int)(w * k)), Math.Max(1, (int)(h * k)));
    }

    static Bitmap Redraw(Image src, Size size)
    {
        var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Black); // 透明的 PNG 铺在黑底上
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(src, new Rectangle(Point.Empty, size));
        return bmp;
    }

    /// <summary>JPG / PNG / BMP / GIF：先只读文件头看尺寸，太大的不解码。</summary>
    static Bitmap? DecodeGdi(string file)
    {
        try
        {
            using var fs = File.OpenRead(file);
            using var img = Image.FromStream(fs, false, false);
            var size = Fit(img.Width, img.Height);
            // 手机竖拍的照片：按 EXIF 方向转正
            if (img.PropertyIdList.Contains(0x0112))
            {
                var flip = img.GetPropertyItem(0x0112)?.Value?[0] switch
                {
                    3 => RotateFlipType.Rotate180FlipNone,
                    6 => RotateFlipType.Rotate90FlipNone,
                    8 => RotateFlipType.Rotate270FlipNone,
                    _ => RotateFlipType.RotateNoneFlipNone,
                };
                if (flip != RotateFlipType.RotateNoneFlipNone)
                {
                    img.RotateFlip(flip);
                    size = Fit(img.Width, img.Height);
                }
            }
            return Redraw(img, size);
        }
        catch (TooLargeException) { throw; }
        catch (Exception) { return null; }
    }

    /// <summary>WEBP 等 GDI+ 不认的格式：用系统的 WIC 解码器（Windows 自带 WEBP 支持）。</summary>
    static Bitmap? DecodeWic(string file)
    {
        try
        {
            using var fs = File.OpenRead(file);
            var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(fs,
                System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            System.Windows.Media.Imaging.BitmapSource frame = decoder.Frames[0];
            var size = Fit(frame.PixelWidth, frame.PixelHeight);
            frame = new System.Windows.Media.Imaging.FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            using var full = new Bitmap(frame.PixelWidth, frame.PixelHeight, PixelFormat.Format32bppArgb);
            var data = full.LockBits(new Rectangle(0, 0, full.Width, full.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { frame.CopyPixels(System.Windows.Int32Rect.Empty, data.Scan0, data.Stride * data.Height, data.Stride); }
            finally { full.UnlockBits(data); }
            return Redraw(full, size);
        }
        catch (TooLargeException) { throw; }
        catch (Exception) { return null; }
    }

    /// <summary>图片尺寸和文件大小，给界面显示。</summary>
    public static string Describe(string file)
    {
        try
        {
            using var fs = File.OpenRead(file);
            using var img = Image.FromStream(fs, false, false);
            return $"{img.Width}×{img.Height} · {new FileInfo(file).Length / 1024} KB";
        }
        catch (Exception) { return ""; }
    }

    /// <summary>读进内存（不锁文件）。</summary>
    public static Bitmap? Load(string? file)
    {
        if (file == null || !File.Exists(file)) return null;
        try
        {
            using var ms = new MemoryStream(File.ReadAllBytes(file));
            using var img = Image.FromStream(ms);
            return new Bitmap(img);
        }
        catch (Exception) { return null; }
    }

    /// <summary>按「铺满裁切」把图缩放到 size。</summary>
    public static Bitmap Cover(Image src, Size size)
    {
        var bmp = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        float k = Math.Max(bmp.Width / (float)src.Width, bmp.Height / (float)src.Height);
        float w = src.Width * k, h = src.Height * k;
        g.DrawImage(src, (bmp.Width - w) / 2, (bmp.Height - h) / 2, w, h);
        return bmp;
    }
}

/// <summary>
/// 主窗口的背景图：按窗口大小缩好缓存一张，各个面板画自己那一块，再盖一层半透明底色保证文字看得清。
/// 只在主窗口开着时存在，关窗口就释放。
/// </summary>
internal sealed class Backdrop : IDisposable
{
    readonly Bitmap source;
    Bitmap? cache;
    readonly int alpha;

    Backdrop(Bitmap source, int dimPercent)
    {
        this.source = source;
        alpha = Math.Clamp(dimPercent, 0, 100) * 255 / 100;
    }

    public static Backdrop? Load(string? file, int dimPercent) =>
        Wallpaper.Load(file) is { } bmp ? new Backdrop(bmp, dimPercent) : null;

    /// <summary>在控件 c 上画它所在位置的那块背景图，再盖上 tint 底色。</summary>
    public void Paint(Graphics g, Control c, Color tint)
    {
        if (c.FindForm() is not { } form) return;
        var size = form.ClientSize;
        if (cache == null || cache.Size != size)
        {
            cache?.Dispose();
            cache = Wallpaper.Cover(source, size);
        }
        var at = form.PointToClient(c.PointToScreen(Point.Empty));
        g.DrawImage(cache, new Rectangle(0, 0, c.Width, c.Height), new Rectangle(at.X, at.Y, c.Width, c.Height), GraphicsUnit.Pixel);
        using var b = new SolidBrush(Color.FromArgb(alpha, tint));
        g.FillRectangle(b, 0, 0, c.Width, c.Height);
    }

    public void Dispose()
    {
        cache?.Dispose();
        source.Dispose();
    }
}
