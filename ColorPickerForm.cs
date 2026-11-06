using System.Drawing.Imaging;

namespace UuTime;

/// <summary>取色：左边饱和度 / 明度方块，中间色相条，右边新旧对比、十六进制输入和常用色。</summary>
internal sealed class ColorPickerForm : ChromeForm
{
    readonly Kit kit;
    readonly Color original;
    readonly Picker picker;
    readonly TextBox hex;
    readonly SwatchRow presets;
    readonly TextButton cancel, ok;
    readonly string title;
    Rectangle hexRect, swatchRect;
    bool syncing;

    public Color Chosen { get; private set; }

    public ColorPickerForm(Kit kit, Color initial, string title)
    {
        this.kit = kit;
        this.title = title;
        original = Chosen = initial;
        var t = kit.T;
        Text = title;
        Resizable = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        SetBack(t.Bg);
        CaptionHeight = S(54);
        Size = new Size(S(520), S(440));

        var close = new IconButton(kit, t.Bg, "close", "关闭");
        close.Click += (_, _) => DialogResult = DialogResult.Cancel;
        close.Location = new Point(Width - S(12) - close.Width, (CaptionHeight - close.Height) / 2);

        picker = new Picker(kit, t.Bg, initial) { Bounds = new Rectangle(S(22), CaptionHeight + S(4), S(296), S(236)) };
        picker.Changed += c => Set(c, fromPicker: true);

        int rx = picker.Right + S(22);
        swatchRect = new Rectangle(rx, picker.Top, Width - rx - S(22), S(84));
        hexRect = new Rectangle(rx, swatchRect.Bottom + S(16), swatchRect.Width, S(36));
        hex = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = t.Input,
            ForeColor = t.Text,
            Font = kit.Body,
            MaxLength = 7,
            Text = Theme.Hex(initial),
        };
        hex.Bounds = new Rectangle(hexRect.X + S(12), hexRect.Y + (hexRect.Height - hex.PreferredHeight) / 2, hexRect.Width - S(24), hex.PreferredHeight);
        hex.TextChanged += (_, _) =>
        {
            if (!syncing && Theme.TryParse(hex.Text, out var c)) Set(c, fromHex: true);
        };

        presets = new SwatchRow(kit, t.Bg, Theme.Accents, custom: false, size: 24) { Location = new Point(S(22), picker.Bottom + S(18)) };
        presets.Width = Width - S(44);
        presets.Height = presets.PreferredHeight(presets.Width);
        presets.Clicked += i => Set(Theme.Accents[i]);

        cancel = new TextButton(kit, t.Bg, "取消", ButtonKind.Outline);
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        ok = new TextButton(kit, t.Bg, "确定", ButtonKind.Accent);
        ok.Click += (_, _) => DialogResult = DialogResult.OK;
        int by = Height - S(18) - ok.Height;
        ok.Location = new Point(Width - S(22) - ok.Width, by);
        cancel.Location = new Point(ok.Left - S(10) - cancel.Width, by);

        Root.Controls.AddRange([close, picker, hex, presets, cancel, ok]);
    }

    public override int S(float v) => kit.S(v);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyChrome(kit.T);
    }

    void Set(Color c, bool fromPicker = false, bool fromHex = false)
    {
        Chosen = c;
        syncing = true;
        if (!fromHex) hex.Text = Theme.Hex(c);
        if (!fromPicker) picker.Value = c;
        syncing = false;
        Root.Invalidate(swatchRect);
    }

    protected override void PaintRoot(Graphics g)
    {
        var t = kit.T;
        Gfx.Text(g, title, kit.H2, t.Text, new Rectangle(S(22), 0, S(300), CaptionHeight));
        // 上半新色，下半原色
        var top = new Rectangle(swatchRect.X, swatchRect.Y, swatchRect.Width, swatchRect.Height / 2);
        var bottom = new Rectangle(swatchRect.X, top.Bottom, swatchRect.Width, swatchRect.Height - top.Height);
        using (var clip = Gfx.Round(swatchRect, S(10)))
        {
            var old = g.Clip;
            g.SetClip(clip);
            using (var b = new SolidBrush(Chosen)) g.FillRectangle(b, top);
            using (var b = new SolidBrush(original)) g.FillRectangle(b, bottom);
            g.Clip = old;
        }
        Gfx.StrokeRound(g, t.Border, swatchRect, S(10), Math.Max(1, S(1)));
        Gfx.Text(g, "新", kit.Small, Theme.Luma(Chosen) > 0.55f ? Color.Black : Color.White, new Rectangle(top.X + S(10), top.Y, S(40), top.Height));
        Gfx.Text(g, "原来", kit.Small, Theme.Luma(original) > 0.55f ? Color.Black : Color.White, new Rectangle(bottom.X + S(10), bottom.Y, S(60), bottom.Height));
        Gfx.FillRound(g, t.Input, hexRect, S(8));
    }

    // ───── HSV ─────

    public static (float h, float s, float v) ToHsv(Color c)
    {
        float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
        float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        float h = d == 0 ? 0 : max == r ? 60 * (((g - b) / d) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        if (h < 0) h += 360;
        return (h, max == 0 ? 0 : d / max, max);
    }

    public static Color FromHsv(float h, float s, float v)
    {
        float c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (float r, float g, float b) = h switch
        {
            < 60 => (c, x, 0f), < 120 => (x, c, 0f), < 180 => (0f, c, x),
            < 240 => (0f, x, c), < 300 => (x, 0f, c), _ => (c, 0f, x),
        };
        return Color.FromArgb((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
    }

    /// <summary>饱和度/明度方块 + 竖的色相条，可拖动。</summary>
    sealed class Picker : Flat
    {
        float h, s, v;
        Bitmap? field, hues;
        float fieldHue = -1;
        int drag; // 0 无，1 方块，2 色相条

        public event Action<Color>? Changed;

        public Picker(Kit kit, Color back, Color c) : base(kit, back)
        {
            (h, s, v) = ToHsv(c);
            Cursor = Cursors.Cross;
        }

        public Color Value
        {
            get => FromHsv(h, s, v);
            set { (h, s, v) = ToHsv(value); Invalidate(); }
        }

        Rectangle Field => new(0, 0, Width - S(40), Height);
        Rectangle Hue => new(Width - S(24), 0, S(24), Height);

        protected override void Dispose(bool disposing)
        {
            if (disposing) { field?.Dispose(); hues?.Dispose(); }
            base.Dispose(disposing);
        }

        static unsafe Bitmap Render(int w, int hgt, Func<int, int, Color> at)
        {
            var bmp = new Bitmap(Math.Max(1, w), Math.Max(1, hgt), PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < bmp.Height; y++)
            {
                int* row = (int*)((byte*)data.Scan0 + y * data.Stride);
                for (int x = 0; x < bmp.Width; x++) row[x] = at(x, y).ToArgb();
            }
            bmp.UnlockBits(data);
            return bmp;
        }

        protected override void Draw(Graphics g)
        {
            var f = Field;
            if (field == null || field.Width != f.Width || fieldHue != h)
            {
                field?.Dispose();
                field = Render(f.Width, f.Height, (x, y) => FromHsv(h, x / (float)(f.Width - 1), 1 - y / (float)(f.Height - 1)));
                fieldHue = h;
            }
            var hb = Hue;
            if (hues == null || hues.Height != hb.Height)
            {
                hues?.Dispose();
                hues = Render(hb.Width, hb.Height, (_, y) => FromHsv(360f * y / hb.Height, 1, 1));
            }
            using (var clip = Gfx.Round(f, S(10)))
            {
                var old = g.Clip;
                g.SetClip(clip);
                g.DrawImageUnscaled(field, f.X, f.Y);
                g.Clip = old;
            }
            using (var clip = Gfx.Round(hb, S(8)))
            {
                var old = g.Clip;
                g.SetClip(clip);
                g.DrawImageUnscaled(hues, hb.X, hb.Y);
                g.Clip = old;
            }

            // 方块上的圆圈、色相条上的横杠
            float cx = f.X + s * (f.Width - 1), cy = f.Y + (1 - v) * (f.Height - 1), d = S(14);
            using (var pen = new Pen(Color.White, Math.Max(2, S(2)))) g.DrawEllipse(pen, cx - d / 2, cy - d / 2, d, d);
            using (var pen = new Pen(Color.FromArgb(120, 0, 0, 0), 1)) g.DrawEllipse(pen, cx - d / 2 - 1.5f, cy - d / 2 - 1.5f, d + 3, d + 3);
            float hy = hb.Y + h / 360f * hb.Height;
            var bar = new RectangleF(hb.X - S(3), hy - S(4), hb.Width + S(6), S(8));
            Gfx.FillRound(g, Color.White, bar, S(4));
            Gfx.StrokeRound(g, Color.FromArgb(120, 0, 0, 0), bar, S(4), 1);
        }

        void Pick(Point p)
        {
            if (drag == 1)
            {
                var f = Field;
                s = Math.Clamp((p.X - f.X) / (float)(f.Width - 1), 0, 1);
                v = 1 - Math.Clamp((p.Y - f.Y) / (float)(f.Height - 1), 0, 1);
            }
            else if (drag == 2)
                h = Math.Clamp(360f * (p.Y - Hue.Y) / Hue.Height, 0, 359.9f);
            Invalidate();
            Changed?.Invoke(Value);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                drag = Field.Contains(e.Location) ? 1 : Hue.Contains(e.Location) ? 2 : 0;
                if (drag != 0) { Capture = true; Pick(e.Location); }
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (drag != 0) Pick(e.Location);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            drag = 0;
            Capture = false;
            base.OnMouseUp(e);
        }
    }
}

/// <summary>一排圆形色块，可带一个「自定义」色块（彩虹圈）。会按宽度自动换行。</summary>
internal sealed class SwatchRow : Flat
{
    IReadOnlyList<Color> colors;
    readonly bool custom;

    /// <summary>色块颜色（暗色 / 亮色模式切换时换一组）。</summary>
    public IReadOnlyList<Color> Colors
    {
        get => colors;
        set { colors = value; Invalidate(); }
    }
    readonly int size;
    int hover = -2;

    /// <summary>点了第几个色块；-1 是自定义。</summary>
    public event Action<int>? Clicked;

    /// <summary>当前选中的颜色（和某个色块一样就圈出来，否则圈自定义并显示这个颜色）。</summary>
    public Color? Current { get; set; }

    public SwatchRow(Kit kit, Color back, IReadOnlyList<Color> colors, bool custom, int size = 30) : base(kit, back)
    {
        this.colors = colors;
        this.custom = custom;
        this.size = size;
        Cursor = Cursors.Hand;
    }

    int Cell => S(size + 10);
    int Count => colors.Count + (custom ? 1 : 0);
    int PerRow(int width) => Math.Max(1, width / Cell);
    public int PreferredHeight(int width) => (Count + PerRow(width) - 1) / PerRow(width) * Cell;

    Rectangle Slot(int i)
    {
        int per = PerRow(Width);
        return new Rectangle(i % per * Cell, i / per * Cell, Cell, Cell);
    }

    int IndexAt(Point p)
    {
        for (int i = 0; i < Count; i++)
            if (Slot(i).Contains(p)) return i < colors.Count ? i : -1;
        return -2;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int i = IndexAt(e.Location);
        if (i != hover) { hover = i; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hover = -2;
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        int i = IndexAt(e.Location);
        if (i != -2 && e.Button == MouseButtons.Left) Clicked?.Invoke(i);
        base.OnMouseDown(e);
    }

    protected override void Draw(Graphics g)
    {
        int match = Current is { } cur ? colors.ToList().FindIndex(c => c.ToArgb() == cur.ToArgb()) : -3;
        for (int i = 0; i < Count; i++)
        {
            var r = Slot(i);
            float d = S(size), cx = r.X + S(5) + d / 2, cy = r.Y + r.Height / 2f;
            bool isCustom = i >= colors.Count;
            int id = isCustom ? -1 : i;
            bool sel = isCustom ? Current != null && match < 0 : match == i;
            if (isCustom)
            {
                // 彩虹圈；选中了自定义颜色时中间填上这个颜色
                for (int k = 0; k < 12; k++)
                    using (var b = new SolidBrush(ColorPickerForm.FromHsv(k * 30, 0.75f, 0.95f)))
                        g.FillPie(b, cx - d / 2, cy - d / 2, d, d, k * 30 - 90, 31);
                float inner = d * 0.56f;
                Gfx.Disc(g, sel ? Current!.Value : Back, cx, cy, inner);
                if (!sel) Gfx.Icon(g, "plus", new RectangleF(cx - inner / 2, cy - inner / 2, inner, inner), T.Text);
            }
            else Gfx.Disc(g, colors[i], cx, cy, d);

            if (sel || hover == id)
            {
                using var pen = new Pen(sel ? T.Text : T.Muted, Math.Max(1.5f, S(2)));
                float ring = d + S(7);
                g.DrawEllipse(pen, cx - ring / 2, cy - ring / 2, ring, ring);
            }
            else if (Theme.Luma(colors[Math.Min(i, colors.Count - 1)]) is var l && !isCustom && Math.Abs(l - Theme.Luma(Back)) < 0.06f)
            {
                using var pen = new Pen(T.Border, 1); // 和底色太接近的色块描个边
                g.DrawEllipse(pen, cx - d / 2, cy - d / 2, d, d);
            }
        }
    }
}
