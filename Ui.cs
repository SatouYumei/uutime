using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace UuTime;

/// <summary>一次界面构建用到的主题、字体、缩放。换主题或换屏幕 DPI 时整套重建。</summary>
internal sealed class Kit : IDisposable
{
    public const string FontName = "Microsoft YaHei UI";

    public Theme T { get; }
    public int Dpi { get; }
    public Font Small { get; }
    public Font Body { get; }
    public Font BodyBold { get; }
    public Font H2 { get; }
    public Font Title { get; }
    public Font Big { get; }
    public Font Brand { get; }
    public ToolTip Tip { get; }
    readonly Dictionary<string, Bitmap?> icons = new(StringComparer.OrdinalIgnoreCase);

    public Kit(Theme theme, int dpi)
    {
        T = theme;
        Dpi = dpi;
        Small = F(12);
        Body = F(14);
        BodyBold = F(14, FontStyle.Bold);
        H2 = F(18, FontStyle.Bold);
        Title = F(26, FontStyle.Bold);
        Big = F(34, FontStyle.Bold);
        Brand = F(21, FontStyle.Bold);

        Tip = new ToolTip { OwnerDraw = true, InitialDelay = 450, ReshowDelay = 100 };
        Tip.Popup += (_, e) =>
        {
            var size = Gfx.Measure(Tip.GetToolTip(e.AssociatedControl) ?? "", Small);
            e.ToolTipSize = new Size(size.Width + S(16), size.Height + S(10));
        };
        Tip.Draw += (_, e) =>
        {
            e.Graphics.Clear(T.Card);
            using var pen = new Pen(T.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, Small, e.Bounds, T.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        };
    }

    public int S(float v) => (int)MathF.Round(v * Dpi / 96f);

    Font F(float px, FontStyle style = FontStyle.Regular) => new(FontName, px * Dpi / 96f, style, GraphicsUnit.Pixel);

    /// <summary>画应用图标；拿不到图标时画一个带首字的色块。</summary>
    public void DrawApp(Graphics g, string name, string? path, Rectangle r)
    {
        if (AppIcon(path, r.Width) is { } bmp)
        {
            g.DrawImage(bmp, r);
            return;
        }
        Gfx.FillRound(g, Theme.Mix(T.Accent, T.Card, 0.22f), r, r.Width * 0.28f);
        string letter = string.IsNullOrEmpty(name) ? "?" : name[..1].ToUpperInvariant();
        TextRenderer.DrawText(g, letter, r.Width >= S(26) ? BodyBold : Small, r, T.Accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    Bitmap? AppIcon(string? path, int px)
    {
        if (string.IsNullOrEmpty(path)) return null;
        string key = path + "|" + px;
        if (icons.TryGetValue(key, out var bmp)) return bmp;
        try
        {
            if (File.Exists(path) && Icon.ExtractAssociatedIcon(path) is { } ico)
            {
                using (ico)
                using (var src = ico.ToBitmap())
                {
                    bmp = new Bitmap(px, px);
                    using var g = Graphics.FromImage(bmp);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(src, 0, 0, px, px);
                }
            }
        }
        catch (Exception) { bmp = null; }
        icons[key] = bmp;
        return bmp;
    }

    Bitmap? avatar;

    /// <summary>标题栏用的圆形头像。</summary>
    public Bitmap Avatar => avatar ??= UuTime.AppIcon.Avatar(S(34));

    public void Dispose()
    {
        avatar?.Dispose();
        foreach (var f in new[] { Small, Body, BodyBold, H2, Title, Big, Brand }) f.Dispose();
        foreach (var b in icons.Values) b?.Dispose();
        Tip.Dispose();
    }
}

internal static class Gfx
{
    public const TextFormatFlags Line = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine |
        TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

    public static void Hq(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    public static GraphicsPath Round(RectangleF r, float radius, bool bottom = true)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f)
        {
            p.AddRectangle(r);
            return p;
        }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        if (bottom)
        {
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        }
        else
        {
            p.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
        }
        p.CloseFigure();
        return p;
    }

    public static void FillRound(Graphics g, Color c, RectangleF r, float radius, bool bottom = true)
    {
        using var b = new SolidBrush(c);
        using var p = Round(r, radius, bottom);
        g.FillPath(b, p);
    }

    public static void StrokeRound(Graphics g, Color c, RectangleF r, float radius, float width)
    {
        float h = width / 2;
        using var pen = new Pen(c, width);
        using var p = Round(new RectangleF(r.X + h, r.Y + h, r.Width - width, r.Height - width), radius - h);
        g.DrawPath(pen, p);
    }

    public static void Disc(Graphics g, Color c, float cx, float cy, float d)
    {
        using var b = new SolidBrush(c);
        g.FillEllipse(b, cx - d / 2, cy - d / 2, d, d);
    }

    public static void Text(Graphics g, string s, Font f, Color c, Rectangle r, TextFormatFlags extra = 0) =>
        TextRenderer.DrawText(g, s, f, r, c, Line | extra);

    public static Size Measure(string s, Font f) =>
        TextRenderer.MeasureText(s, f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

    public static void Star(Graphics g, Color c, float cx, float cy, float r)
    {
        var pts = new PointF[10];
        for (int i = 0; i < 10; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI / 5, rr = i % 2 == 0 ? r : r * 0.45;
            pts[i] = new PointF(cx + (float)(rr * Math.Cos(a)), cy + (float)(rr * Math.Sin(a)));
        }
        using var b = new SolidBrush(c);
        g.FillPolygon(b, pts);
    }

    /// <summary>
    /// uutime 字标：两个 u 一深一浅的主题色，time 用正文色，i 的点换成黄色星星。
    /// 用 Segoe UI Black 的字形路径画，系统没有这个字体时退回雅黑粗体。返回占用的宽度。
    /// </summary>
    public static int Logo(Graphics g, Theme t, float px, float x, float midY)
    {
        FontFamily fam;
        try { fam = new FontFamily("Segoe UI Black"); }
        catch (ArgumentException) { fam = new FontFamily(Kit.FontName); }
        using (fam)
        {
            var style = fam.Name == "Segoe UI Black" ? FontStyle.Regular : FontStyle.Bold;
            using var font = new Font(fam, px, style, GraphicsUnit.Pixel);
            var fmt = StringFormat.GenericTypographic;
            float top = midY - px * 0.62f, start = x;

            RectangleF ink = default; // 最近一次画的字形实际范围
            float Draw(string s, Color c, float at)
            {
                using var path = new GraphicsPath();
                path.AddString(s, fam, (int)style, px, new PointF(at, top), fmt);
                ink = path.GetBounds();
                using var b = new SolidBrush(c);
                g.FillPath(b, path);
                return g.MeasureString(s, font, int.MaxValue, fmt).Width;
            }

            x += Draw("u", t.Accent, x);
            x += Draw("u", Theme.Mix(t.Accent, t.Bg, 0.55f), x - px * 0.04f) + px * 0.02f;
            x += Draw("t", t.Text, x);
            float iw = Draw("ı", t.Text, x); // 没有点的 i
            float r = px * 0.2f; // 星星放在 i 的竖笔正上方，和点的位置一样
            Star(g, Color.FromArgb(0xFF, 0xC9, 0x3C), ink.X + ink.Width / 2, ink.Top - r * 1.05f, r);
            x += iw;
            x += Draw("me", t.Text, x);
            return (int)Math.Ceiling(x - start);
        }
    }

    /// <summary>线条图标，28×28 网格，画法同 chatuutts。</summary>
    public static void Icon(Graphics g, string kind, RectangleF box, Color c, bool on = false)
    {
        float u = box.Width / 28f;
        PointF P(float x, float y) => new(box.X + x * u, box.Y + y * u);
        RectangleF R(float x, float y, float w, float h) => new(box.X + x * u, box.Y + y * u, w * u, h * u);
        using var pen = new Pen(c, Math.Max(1.4f, 1.65f * u)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(c);
        switch (kind)
        {
            case "palette":
                var ring = R(4.4f, 4.4f, 19.2f, 19.2f);
                g.DrawEllipse(pen, ring);
                g.FillPie(brush, ring.X, ring.Y, ring.Width, ring.Height, 90, 180);
                break;
            case "close":
                g.DrawLine(pen, P(8.4f, 8.4f), P(19.6f, 19.6f));
                g.DrawLine(pen, P(19.6f, 8.4f), P(8.4f, 19.6f));
                break;
            case "minus":
                g.DrawLine(pen, P(8, 14), P(20, 14));
                break;
            case "plus":
                g.DrawLine(pen, P(8, 14), P(20, 14));
                g.DrawLine(pen, P(14, 8), P(14, 20));
                break;
            case "grid":
                foreach (var (x, y) in new[] { (5.5f, 5.5f), (15.5f, 5.5f), (5.5f, 15.5f), (15.5f, 15.5f) })
                {
                    using var p = Round(R(x, y, 7, 7), 2 * u);
                    if (on) g.FillPath(brush, p);
                    g.DrawPath(pen, p);
                }
                break;
            case "check":
                g.DrawLines(pen, [P(8, 14.6f), P(12.2f, 18.6f), P(20, 9.6f)]);
                break;
            case "search":
                g.DrawEllipse(pen, R(5.5f, 5.5f, 12.5f, 12.5f));
                g.DrawLine(pen, P(16.6f, 16.6f), P(22, 22));
                break;
            case "gear":
                g.DrawEllipse(pen, R(9.5f, 9.5f, 9, 9));
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    float c1 = (float)Math.Cos(a), s1 = (float)Math.Sin(a);
                    g.DrawLine(pen, P(14 + 7.2f * c1, 14 + 7.2f * s1), P(14 + 9.6f * c1, 14 + 9.6f * s1));
                }
                g.DrawEllipse(pen, R(7, 7, 14, 14));
                break;
            case "user":
                g.DrawEllipse(pen, R(9.5f, 4.5f, 9, 9));
                g.DrawArc(pen, R(5.5f, 15.5f, 17, 14), 180, 180);
                break;
            case "export": // 箭头朝上出托盘：备份
            case "import": // 箭头朝下进托盘：导入
                g.DrawLines(pen, [P(5.5f, 16.5f), P(5.5f, 22.5f), P(22.5f, 22.5f), P(22.5f, 16.5f)]);
                g.DrawLine(pen, P(14, 5.5f), P(14, 17.5f));
                if (kind == "export") g.DrawLines(pen, [P(9.5f, 10), P(14, 5.5f), P(18.5f, 10)]);
                else g.DrawLines(pen, [P(9.5f, 13), P(14, 17.5f), P(18.5f, 13)]);
                break;
            case "folder":
                using (var p = new GraphicsPath())
                {
                    p.AddLines([P(4.5f, 21.5f), P(4.5f, 7), P(11, 7), P(13.2f, 9.6f), P(23.5f, 9.6f), P(23.5f, 21.5f)]);
                    p.CloseFigure();
                    g.DrawPath(pen, p);
                }
                break;
        }
    }
}

/// <summary>
/// 无边框但保留系统窗框：Win11 原生圆角、阴影、贴靠、拖边缩放都在。
/// 做法是去掉整块非客户区（WM_NCCALCSIZE），再自己回答命中测试。
/// </summary>
internal class ChromeForm : Form
{
    const int WM_NCCALCSIZE = 0x0083, WM_NCHITTEST = 0x0084, WM_NCACTIVATE = 0x0086;
    const int HTCLIENT = 1, HTCAPTION = 2, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13,
        HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll")]
    static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    public int CaptionHeight { get; set; }

    /// <summary>背景图（只有主窗口会设）。各面板据此画图再盖底色。</summary>
    public Backdrop? Backdrop { get; protected set; }
    public bool Resizable { get; set; } = true;

    /// <summary>
    /// 铺满窗口的底板。去掉非客户区后，窗口自身表面在 DWM 下会透明，
    /// 所以背景、标题文字等都画在这块子面板上，控件也放在它里面。
    /// </summary>
    protected Surface Root { get; }

    public ChromeForm()
    {
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.Sizable;
        DoubleBuffered = true;
        Font = new Font(Kit.FontName, 9f);
        Root = new RootSurface(this) { Dock = DockStyle.Fill };
        Root.Paint += (_, e) =>
        {
            Gfx.Hq(e.Graphics);
            PaintRoot(e.Graphics);
        };
        Controls.Add(Root);
    }

    protected virtual void PaintRoot(Graphics g) { }

    protected void SetBack(Color c)
    {
        BackColor = c;
        Root.BackColor = c;
    }

    sealed class RootSurface(ChromeForm owner) : Surface(Color.Black)
    {
        protected override void WndProc(ref Message m)
        {
            // 标题栏区域让给窗口处理，这样能拖动、双击最大化、贴靠
            if (m.Msg == 0x0084 && owner.PointToClient(ScreenPoint(m.LParam)).Y < owner.CaptionHeight)
            {
                m.Result = -1; // HTTRANSPARENT
                return;
            }
            base.WndProc(ref m);
        }
    }

    public virtual int S(float v) => (int)MathF.Round(v * DeviceDpi / 96f);

#if DEBUG
    /// <summary>渲染预览图时不抢前台焦点。</summary>
    public static bool PreviewMode;
    protected override bool ShowWithoutActivation => PreviewMode;
#endif

    public void ApplyChrome(Theme t)
    {
        if (!IsHandleCreated) return;
        int round = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(Handle, 33, ref round, 4);
        int border = ColorTranslator.ToWin32(t.Border);
        DwmSetWindowAttribute(Handle, 34, ref border, 4);
        int dark = t.IsDark ? 1 : 0;
        DwmSetWindowAttribute(Handle, 20, ref dark, 4);
    }

    /// <summary>鼠标是否在可拖动缩放的窗口边缘上（子控件据此把命中测试让给窗口）。</summary>
    public bool InGrip(Point screen)
    {
        if (!Resizable || WindowState != FormWindowState.Normal) return false;
        var p = PointToClient(screen);
        int g = S(6);
        return p.X < g || p.Y < g || p.X >= ClientSize.Width - g || p.Y >= ClientSize.Height - g;
    }

    internal static Point ScreenPoint(IntPtr lParam)
    {
        long v = lParam.ToInt64();
        return new Point((short)(v & 0xFFFF), (short)((v >> 16) & 0xFFFF));
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && Modal)
        {
            DialogResult = DialogResult.Cancel;
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_NCCALCSIZE when m.WParam != IntPtr.Zero:
                if (WindowState == FormWindowState.Maximized)
                {
                    // 最大化时窗口会超出屏幕一圈边框，往里收回来
                    var r = Marshal.PtrToStructure<RECT>(m.LParam);
                    int f = GetSystemMetricsForDpi(32, (uint)DeviceDpi) + GetSystemMetricsForDpi(92, (uint)DeviceDpi);
                    r.Left += f; r.Top += f; r.Right -= f; r.Bottom -= f;
                    Marshal.StructureToPtr(r, m.LParam, false);
                }
                m.Result = IntPtr.Zero;
                return;
            case WM_NCACTIVATE:
                m.LParam = -1; // 不让系统重画标题栏
                base.WndProc(ref m);
                return;
            case WM_NCHITTEST:
                m.Result = HitTest(PointToClient(ScreenPoint(m.LParam)));
                return;
        }
        base.WndProc(ref m);
    }

    IntPtr HitTest(Point p)
    {
        if (Resizable && WindowState == FormWindowState.Normal)
        {
            int g = S(6), w = ClientSize.Width, h = ClientSize.Height;
            bool l = p.X < g, r = p.X >= w - g, t = p.Y < g, b = p.Y >= h - g;
            if (t && l) return HTTOPLEFT;
            if (t && r) return HTTOPRIGHT;
            if (b && l) return HTBOTTOMLEFT;
            if (b && r) return HTBOTTOMRIGHT;
            if (l) return HTLEFT;
            if (r) return HTRIGHT;
            if (t) return HTTOP;
            if (b) return HTBOTTOM;
        }
        return p.Y < CaptionHeight ? HTCAPTION : HTCLIENT;
    }
}

internal static class Hit
{
    /// <summary>在窗口边缘时让鼠标“穿透”到窗口，以便拖边缩放。</summary>
    public static bool PassToForm(Control c, ref Message m)
    {
        if (m.Msg == 0x0084 && c.FindForm() is ChromeForm f && f.InGrip(ChromeForm.ScreenPoint(m.LParam)))
        {
            m.Result = -1; // HTTRANSPARENT
            return true;
        }
        return false;
    }
}

internal class Surface : Panel
{
    public Surface(Color back)
    {
        BackColor = back;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if ((FindForm() as ChromeForm)?.Backdrop is { } b) b.Paint(e.Graphics, this, BackColor);
        else base.OnPaintBackground(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (!Hit.PassToForm(this, ref m)) base.WndProc(ref m);
    }
}

/// <summary>自绘控件基类。</summary>
internal class Flat : Control
{
    protected readonly Kit K;
    protected bool Hover;

    public Color Back { get; }
    protected Theme T => K.T;

    public Flat(Kit kit, Color back)
    {
        K = kit;
        Back = back;
        BackColor = back;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable | ControlStyles.StandardDoubleClick, false);
    }

    protected int S(float v) => K.S(v);

    protected override void OnMouseEnter(EventArgs e)
    {
        Hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        Hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var backdrop = (FindForm() as ChromeForm)?.Backdrop;
        Glass = backdrop != null;
        glassAlpha = backdrop?.CardAlpha ?? 255;
        if (backdrop != null) backdrop.Paint(e.Graphics, this, Back);
        else e.Graphics.Clear(Back);
        Gfx.Hq(e.Graphics);
        Draw(e.Graphics);
    }

    protected virtual void Draw(Graphics g) { }

    /// <summary>这次绘制时窗口有背景图：实心的块画成半透明加细描边。</summary>
    protected bool Glass { get; private set; }
    int glassAlpha = 255;

    /// <summary>
    /// 画一个实心块（卡片、选中项、分段控件底等）。有背景图时按设置的卡片透明度（默认 50%）透出图片，
    /// 再加 1px 的浅描边，免得在花哨的图上看不出块的边界；文字照常画在上面，不透明。
    /// </summary>
    protected void Block(Graphics g, Color c, RectangleF r, float radius, bool edge = true)
    {
        if (!Glass)
        {
            Gfx.FillRound(g, c, r, radius);
            return;
        }
        Gfx.FillRound(g, Color.FromArgb(glassAlpha, c), r, radius);
        if (edge) Gfx.StrokeRound(g, T.IsDark ? Color.FromArgb(34, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0), r, radius, 1);
    }

    protected override void WndProc(ref Message m)
    {
        if (!Hit.PassToForm(this, ref m)) base.WndProc(ref m);
    }
}

internal sealed class IconButton : Flat
{
    readonly string kind;

    public IconButton(Kit kit, Color back, string kind, string tip) : base(kit, back)
    {
        this.kind = kind;
        Size = new Size(S(32), S(32));
        Cursor = Cursors.Hand;
        kit.Tip.SetToolTip(this, tip);
    }

    protected override void Draw(Graphics g)
    {
        if (Hover) Gfx.FillRound(g, T.Hover, ClientRectangle, S(8));
        float box = S(24);
        Gfx.Icon(g, kind, new RectangleF((Width - box) / 2, (Height - box) / 2, box, box), Hover ? T.Text : T.Muted);
    }
}

internal enum ButtonKind { Accent, Outline, Ghost, Danger }

internal sealed class TextButton : Flat
{
    ButtonKind kind;
    readonly string? icon;

    public ButtonKind Kind
    {
        get => kind;
        set { kind = value; FitWidth(); Invalidate(); }
    }

    public TextButton(Kit kit, Color back, string text, ButtonKind kind, string? icon = null) : base(kit, back)
    {
        this.kind = kind;
        this.icon = icon;
        Text = text;
        Cursor = Cursors.Hand;
        Height = S(36);
        FitWidth();
    }

    public void FitWidth()
    {
        Width = Gfx.Measure(Text, kind == ButtonKind.Accent ? K.BodyBold : K.Body).Width + S(32) + (icon != null ? S(26) : 0);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void Draw(Graphics g)
    {
        var r = new RectangleF(0, 0, Width, Height);
        float rad = S(8);
        bool hot = Hover && Enabled;
        Color ink;
        switch (kind)
        {
            case ButtonKind.Accent:
                var fill = !Enabled ? Theme.Mix(T.Accent, Back, 0.35f)
                    : hot ? Theme.Mix(T.Accent, T.IsDark ? Color.White : Color.Black, 0.88f) : T.Accent;
                Gfx.FillRound(g, fill, r, rad);
                ink = Enabled ? T.AccentText : Theme.Mix(T.AccentText, fill, 0.6f);
                break;
            case ButtonKind.Danger:
                if (hot) Gfx.FillRound(g, Theme.Mix(T.Danger, Back, 0.14f), r, rad);
                Gfx.StrokeRound(g, Theme.Mix(T.Danger, Back, 0.55f), r, rad, Math.Max(1, S(1)));
                ink = T.Danger;
                break;
            case ButtonKind.Outline:
                if (hot) Gfx.FillRound(g, T.Hover, r, rad);
                Gfx.StrokeRound(g, T.Border, r, rad, Math.Max(1, S(1)));
                ink = T.Text;
                break;
            default:
                if (hot) Gfx.FillRound(g, T.Hover, r, rad);
                ink = hot ? T.Text : T.Muted;
                break;
        }

        var font = kind == ButtonKind.Accent ? K.BodyBold : K.Body;
        int textW = Gfx.Measure(Text, font).Width;
        int iconW = icon != null ? S(26) : 0;
        int x = kind == ButtonKind.Ghost ? S(10) : (Width - textW - iconW) / 2;
        if (icon != null)
        {
            float box = S(22);
            Gfx.Icon(g, icon, new RectangleF(x, (Height - box) / 2, box, box), ink);
            x += iconW;
        }
        Gfx.Text(g, Text, font, ink, new Rectangle(x, 0, Width - x, Height));
    }
}

/// <summary>分段选择：今天 / 昨天 / 7 天 / 30 天。</summary>
internal sealed class Segmented : Flat
{
    readonly string[] items;
    readonly int[] widths;
    int hover = -1;
    int selected;

    public event Action? Changed;

    public int Selected
    {
        get => selected;
        set { selected = value; Invalidate(); }
    }

    public Segmented(Kit kit, Color back, string[] items) : base(kit, back)
    {
        this.items = items;
        widths = items.Select(s => Gfx.Measure(s, kit.BodyBold).Width + S(28)).ToArray();
        Size = new Size(widths.Sum() + S(8), S(36));
        Cursor = Cursors.Hand;
    }

    int IndexAt(int x)
    {
        int left = S(4);
        for (int i = 0; i < items.Length; i++)
        {
            if (x >= left && x < left + widths[i]) return i;
            left += widths[i];
        }
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int i = IndexAt(e.X);
        if (i != hover) { hover = i; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hover = -1;
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        int i = IndexAt(e.X);
        if (i >= 0 && i != selected)
        {
            Selected = i;
            Changed?.Invoke();
        }
        base.OnMouseDown(e);
    }

    protected override void Draw(Graphics g)
    {
        Block(g, T.Input, new RectangleF(0, 0, Width, Height), Height / 2f);
        int x = S(4);
        for (int i = 0; i < items.Length; i++)
        {
            var r = new Rectangle(x, S(4), widths[i], Height - S(8));
            if (i == selected) Gfx.FillRound(g, T.Accent, r, r.Height / 2f);
            var ink = i == selected ? T.AccentText : i == hover ? T.Text : T.Muted;
            Gfx.Text(g, items[i], i == selected ? K.BodyBold : K.Body, ink, r, TextFormatFlags.HorizontalCenter);
            x += widths[i];
        }
    }
}

internal sealed class Toggle : Flat
{
    bool on;

    public event Action? CheckedChanged;

    public bool Checked
    {
        get => on;
        set { on = value; Invalidate(); }
    }

    public Toggle(Kit kit, Color back) : base(kit, back)
    {
        Size = new Size(S(40), S(22));
        Cursor = Cursors.Hand;
    }

    protected override void OnClick(EventArgs e)
    {
        Checked = !on;
        CheckedChanged?.Invoke();
        base.OnClick(e);
    }

    protected override void Draw(Graphics g)
    {
        var track = on ? T.Accent : Hover ? Theme.Mix(T.Muted, Back, 0.55f) : T.Border;
        Gfx.FillRound(g, track, new RectangleF(0, 0, Width, Height), Height / 2f);
        float d = Height - S(6);
        float cx = on ? Width - S(3) - d / 2 : S(3) + d / 2;
        Gfx.Disc(g, on ? T.AccentText : T.Card, cx, Height / 2f, d);
    }
}

/// <summary>− 数值 + 的步进器。</summary>
internal sealed class Stepper : Flat
{
    readonly int min, max, step;
    readonly Func<int, string> format;
    int value;
    int hoverPart; // -1 减，1 加

    public event Action<int>? ValueChanged;

    public Stepper(Kit kit, Color back, int value, int min, int max, Func<int, string> format, int step = 1) : base(kit, back)
    {
        this.min = min;
        this.max = max;
        this.step = step;
        this.format = format;
        this.value = Math.Clamp(value, min, max);
        Size = new Size(S(112), S(30));
    }

    public int Value
    {
        get => value;
        set { this.value = Math.Clamp(value, min, max); Invalidate(); }
    }

    int PartAt(int x) => x < S(30) ? -1 : x >= Width - S(30) ? 1 : 0;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int p = PartAt(e.X);
        if (p != hoverPart) { hoverPart = p; Cursor = p == 0 ? Cursors.Default : Cursors.Hand; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hoverPart = 0;
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        int p = PartAt(e.X);
        int next = Math.Clamp(value + p * step, min, max);
        if (p != 0 && next != value)
        {
            value = next;
            Invalidate();
            ValueChanged?.Invoke(value);
        }
        base.OnMouseDown(e);
    }

    protected override void Draw(Graphics g)
    {
        Gfx.FillRound(g, T.Input, new RectangleF(0, 0, Width, Height), S(8));
        float box = S(20), y = (Height - box) / 2;
        if (hoverPart == -1) Gfx.FillRound(g, T.Hover, new RectangleF(S(3), S(3), S(26), Height - S(6)), S(6));
        if (hoverPart == 1) Gfx.FillRound(g, T.Hover, new RectangleF(Width - S(29), S(3), S(26), Height - S(6)), S(6));
        Gfx.Icon(g, "minus", new RectangleF(S(6), y, box, box), value > min ? T.Text : T.Border);
        Gfx.Icon(g, "plus", new RectangleF(Width - S(6) - box, y, box, box), value < max ? T.Text : T.Border);
        Gfx.Text(g, format(value), K.Body, T.Text, new Rectangle(S(28), 0, Width - S(56), Height), TextFormatFlags.HorizontalCenter);
    }
}

/// <summary>滑块：拖动或点轨道改值，右边显示当前值。</summary>
internal sealed class Slider : Flat
{
    readonly int min, max;
    readonly Func<int, string> format;
    int value;
    bool drag;

    public event Action<int>? ValueChanged;

    public Slider(Kit kit, Color back, int value, int min, int max, Func<int, string> format) : base(kit, back)
    {
        this.min = min;
        this.max = max;
        this.format = format;
        this.value = Math.Clamp(value, min, max);
        Size = new Size(S(240), S(30));
        Cursor = Cursors.Hand;
    }

    public int Value
    {
        get => value;
        set { this.value = Math.Clamp(value, min, max); Invalidate(); }
    }

    int LabelW => S(48);
    float Left0 => S(9);
    float Right0 => Width - LabelW - S(9);

    void Pick(int x)
    {
        float k = Math.Clamp((x - Left0) / (Right0 - Left0), 0, 1);
        int next = (int)Math.Round(min + k * (max - min));
        if (next == value) return;
        value = next;
        Invalidate();
        ValueChanged?.Invoke(value);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && Enabled) { drag = true; Capture = true; Pick(e.X); }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (drag) Pick(e.X);
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        drag = false;
        Capture = false;
        base.OnMouseUp(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (!Enabled) return;
        int next = Math.Clamp(value + Math.Sign(e.Delta) * Math.Max(1, (max - min) / 20), min, max);
        if (next != value) { value = next; Invalidate(); ValueChanged?.Invoke(value); }
        base.OnMouseWheel(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void Draw(Graphics g)
    {
        float cy = Height / 2f, k = (value - min) / (float)(max - min);
        float x = Left0 + k * (Right0 - Left0);
        var track = new RectangleF(Left0, cy - S(2), Right0 - Left0, S(4));
        Gfx.FillRound(g, T.Input, track, S(2));
        var fill = Enabled ? T.Accent : T.Border;
        if (x - Left0 >= 1) Gfx.FillRound(g, fill, new RectangleF(Left0, track.Y, x - Left0, track.Height), S(2));
        float d = S(16);
        Gfx.Disc(g, Enabled ? T.Text : T.Border, x, cy, d);
        Gfx.Disc(g, fill, x, cy, d - S(6));
        Gfx.Text(g, format(value), K.Body, Enabled ? T.Text : T.Muted, new Rectangle(Width - LabelW, 0, LabelW, Height), TextFormatFlags.Right);
    }
}

/// <summary>侧边栏列表：总览 + 分组标题 + 各个应用。整块自绘，不为每行建控件。</summary>
internal sealed class ItemList : Flat
{
    public sealed class Item
    {
        public string Id = "";
        public string Title = "";
        public string? Sub;
        public string? Path;
        public string? Glyph;
        public bool Header;
        public int State; // 0 未运行，1 前台，2 后台
    }

    public List<Item> Items { get; } = new();
    public string SelectedId { get; set; } = "";
    public event Action<string>? Picked;

    int scroll;
    int hover = -1;

    public ItemList(Kit kit, Color back) : base(kit, back) { }

    int RowHeight(Item i) => i.Header ? S(34) : i.Sub != null ? S(54) : S(44);

    int ContentHeight() => Items.Sum(i => RowHeight(i) + S(2)) + S(8);

    int IndexAt(int y)
    {
        int top = S(4) - scroll;
        for (int i = 0; i < Items.Count; i++)
        {
            int h = RowHeight(Items[i]);
            if (y >= top && y < top + h) return Items[i].Header ? -1 : i;
            top += h + S(2);
        }
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int i = IndexAt(e.Y);
        if (i != hover)
        {
            hover = i;
            Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hover = -1;
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        int i = IndexAt(e.Y);
        if (i >= 0 && e.Button == MouseButtons.Left) Picked?.Invoke(Items[i].Id);
        base.OnMouseDown(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        int maxScroll = Math.Max(0, ContentHeight() - Height);
        scroll = Math.Clamp(scroll - e.Delta / 120 * S(48), 0, maxScroll);
        Invalidate();
        base.OnMouseWheel(e);
    }

    protected override void OnResize(EventArgs e)
    {
        scroll = Math.Clamp(scroll, 0, Math.Max(0, ContentHeight() - Height));
        base.OnResize(e);
    }

    protected override void Draw(Graphics g)
    {
        int y = S(4) - scroll;
        for (int i = 0; i < Items.Count; i++)
        {
            var item = Items[i];
            int h = RowHeight(item);
            if (y + h > 0 && y < Height) DrawItem(g, item, new Rectangle(S(8), y, Width - S(16), h), i == hover);
            y += h + S(2);
        }
    }

    void DrawItem(Graphics g, Item item, Rectangle r, bool hot)
    {
        if (item.Header)
        {
            Gfx.Text(g, item.Title, K.Small, T.Muted, new Rectangle(r.X + S(10), r.Y + S(12), r.Width, r.Height - S(14)));
            return;
        }
        bool sel = item.Id == SelectedId;
        if (sel)
        {
            Block(g, T.Selected, r, S(8));
            Gfx.FillRound(g, T.Accent, new RectangleF(r.X, r.Y + S(12), S(3), r.Height - S(24)), S(1.5f));
        }
        else if (hot)
        {
            Block(g, T.Hover, r, S(8), edge: false);
        }

        int icon = S(24);
        var ir = new Rectangle(r.X + S(12), r.Y + (r.Height - icon) / 2, icon, icon);
        if (item.Glyph != null) Gfx.Icon(g, item.Glyph, ir, sel ? T.Accent : T.Muted, sel);
        else K.DrawApp(g, item.Title, item.Path, ir);

        int tx = r.X + S(46), tw = r.Right - tx - S(24);
        var font = sel ? K.BodyBold : K.Body;
        if (item.Sub == null)
        {
            Gfx.Text(g, item.Title, font, T.Text, new Rectangle(tx, r.Y, tw, r.Height));
        }
        else
        {
            Gfx.Text(g, item.Title, font, T.Text, new Rectangle(tx, r.Y + S(8), tw, S(20)));
            Gfx.Text(g, item.Sub, K.Small, T.Muted, new Rectangle(tx, r.Y + S(29), tw, S(16)));
        }

        float cx = r.Right - S(14), cy = r.Y + r.Height / 2f;
        if (item.State == 1) Gfx.Disc(g, T.Accent, cx, cy, S(8));
        else if (item.State == 2)
        {
            using var pen = new Pen(T.Muted, Math.Max(1.2f, S(1.5f)));
            float d = S(7);
            g.DrawEllipse(pen, cx - d / 2, cy - d / 2, d, d);
        }
    }
}
