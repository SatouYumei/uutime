namespace UuTime;

/// <summary>主区域：总览卡片 / 单个应用详情 + 14 天柱状图 / 空状态。整块自绘。</summary>
internal sealed class StatsView : Flat
{
    public sealed record Row(string Exe, string Name, string? Path, long Fg, long Bg, int State);

    enum Mode { Empty, Overview, App }

    Mode mode = Mode.Empty;
    List<Row> rows = new();
    Row? app;
    (DateTime Day, long Fg, long Bg)[] series = [];

    int scroll;
    int hoverRow = -1;
    int hoverBar = -1;
    bool hoverAdd;
    Rectangle addRect;
    Rectangle plot;

    public event Action<string>? RowClicked;
    public event Action? AddClicked;

    public StatsView(Kit kit, Color back) : base(kit, back) { }

    public void ShowEmpty()
    {
        mode = Mode.Empty;
        Invalidate();
    }

    public void ShowOverview(List<Row> data)
    {
        if (mode != Mode.Overview) scroll = 0;
        mode = Mode.Overview;
        rows = data;
        ClampScroll();
        Invalidate();
    }

    public void ShowApp(Row row, (DateTime, long, long)[] days)
    {
        mode = Mode.App;
        app = row;
        series = days;
        Invalidate();
    }

    // ───── 总览 ─────

    int CardH => S(86);
    int Gap => S(10);
    int ListTop => S(30);

    Rectangle CardRect(int i) => new(0, ListTop + i * (CardH + Gap) - scroll, Width, CardH);

    void ClampScroll()
    {
        int content = ListTop + rows.Count * (CardH + Gap);
        scroll = Math.Clamp(scroll, 0, Math.Max(0, content - Height));
    }

    void DrawOverview(Graphics g)
    {
        DrawLegend(g, new Rectangle(0, 0, Width, S(20)), alignRight: false);

        long max = Math.Max(1, rows.Max(r => r.Fg + r.Bg));
        var clip = g.Clip;
        g.SetClip(new Rectangle(0, ListTop - S(4), Width, Height));
        for (int i = 0; i < rows.Count; i++)
        {
            var r = CardRect(i);
            if (r.Bottom < 0 || r.Top > Height) continue;
            var row = rows[i];
            Gfx.FillRound(g, T.Card, r, S(12));
            if (i == hoverRow) Gfx.StrokeRound(g, Theme.Mix(T.Accent, T.Card, 0.55f), r, S(12), Math.Max(1, S(1.5f)));

            int pad = S(18);
            K.DrawApp(g, row.Name, row.Path, new Rectangle(r.X + pad, r.Y + S(16), S(30), S(30)));
            int tx = r.X + pad + S(44);
            string total = Fmt.Duration(row.Fg + row.Bg);
            int totalW = Gfx.Measure(total, K.H2).Width;
            Gfx.Text(g, total, K.H2, T.Text, new Rectangle(r.Right - pad - totalW, r.Y + S(12), totalW + S(2), S(26)));
            Gfx.Text(g, row.Name, K.BodyBold, T.Text, new Rectangle(tx, r.Y + S(13), r.Right - pad - totalW - tx - S(12), S(20)));

            string sub = $"前台 {Fmt.Duration(row.Fg)}   ·   后台 {Fmt.Duration(row.Bg)}";
            Gfx.Text(g, sub, K.Small, T.Muted, new Rectangle(tx, r.Y + S(35), r.Width, S(16)));
            var (state, stateInk) = row.State switch
            {
                1 => ("● 正在使用", T.Accent),
                2 => ("○ 后台运行中", T.Muted),
                _ => ("未运行", Theme.Mix(T.Muted, T.Card, 0.7f)),
            };
            int sw = Gfx.Measure(state, K.Small).Width;
            Gfx.Text(g, state, K.Small, stateInk, new Rectangle(r.Right - pad - sw, r.Y + S(38), sw + S(2), S(16)));

            var bar = new RectangleF(tx, r.Y + S(62), r.Right - pad - tx, S(7));
            Gfx.FillRound(g, T.Input, bar, bar.Height / 2);
            float fgW = bar.Width * row.Fg / max, allW = bar.Width * (row.Fg + row.Bg) / max;
            if (allW >= 1) Gfx.FillRound(g, T.AccentSoft, new RectangleF(bar.X, bar.Y, Math.Max(allW, bar.Height), bar.Height), bar.Height / 2);
            if (fgW >= 1) Gfx.FillRound(g, T.Accent, new RectangleF(bar.X, bar.Y, Math.Max(fgW, bar.Height), bar.Height), bar.Height / 2);
        }
        g.Clip = clip;
    }

    void DrawLegend(Graphics g, Rectangle r, bool alignRight)
    {
        var items = new[] { ("前台使用", T.Accent), ("后台运行", T.AccentSoft) };
        int dot = S(10), gap = S(6), space = S(18);
        int total = items.Sum(i => dot + gap + Gfx.Measure(i.Item1, K.Small).Width) + space;
        int x = alignRight ? r.Right - total : r.X;
        foreach (var (text, color) in items)
        {
            Gfx.FillRound(g, color, new RectangleF(x, r.Y + (r.Height - dot) / 2f, dot, dot), S(3));
            x += dot + gap;
            int w = Gfx.Measure(text, K.Small).Width;
            Gfx.Text(g, text, K.Small, T.Muted, new Rectangle(x, r.Y, w + S(2), r.Height));
            x += w + space;
        }
    }

    // ───── 单个应用 ─────

    void DrawApp(Graphics g)
    {
        if (app == null) return;
        int gap = S(12), cardH = S(118);
        int w = (Width - gap) / 2;
        long run = app.Fg + app.Bg;
        string pct = run == 0 ? "—" : $"{app.Fg * 100 / run}%";
        StatCard(g, new Rectangle(0, 0, w, cardH), "前台使用", T.Accent, app.Fg, $"占运行时间 {pct}", T.Accent);
        StatCard(g, new Rectangle(w + gap, 0, Width - w - gap, cardH), "后台运行", T.AccentSoft, app.Bg, $"运行合计 {Fmt.Duration(run)}", T.Text);

        var c = new Rectangle(0, cardH + gap, Width, Math.Max(S(200), Height - cardH - gap));
        Gfx.FillRound(g, T.Card, c, S(12));
        Gfx.Text(g, $"最近 {series.Length} 天", K.BodyBold, T.Text, new Rectangle(c.X + S(20), c.Y + S(16), S(200), S(22)));
        DrawLegend(g, new Rectangle(c.X, c.Y + S(16), c.Width - S(20), S(22)), alignRight: true);
        DrawChart(g, c);
    }

    void StatCard(Graphics g, Rectangle r, string label, Color dot, long ms, string note, Color ink)
    {
        Gfx.FillRound(g, T.Card, r, S(12));
        int pad = S(20);
        Gfx.FillRound(g, dot, new RectangleF(r.X + pad, r.Y + S(21), S(10), S(10)), S(3));
        Gfx.Text(g, label, K.Small, T.Muted, new Rectangle(r.X + pad + S(16), r.Y + S(16), r.Width, S(20)));

        int x = r.X + pad;
        foreach (var (text, unit) in Fmt.Parts(ms))
        {
            var font = unit ? K.Body : K.Big;
            int tw = Gfx.Measure(text, font).Width;
            // 数字和单位按底部对齐
            int h = unit ? S(20) : S(44);
            int top = r.Y + S(40) + (unit ? S(44) - h - S(6) : 0);
            Gfx.Text(g, text, font, unit ? T.Muted : ink, new Rectangle(x, top, tw + S(2), h));
            x += tw + (unit ? S(8) : S(3));
        }
        Gfx.Text(g, note, K.Small, T.Muted, new Rectangle(r.X + pad, r.Bottom - S(30), r.Width - pad * 2, S(18)));
    }

    void DrawChart(Graphics g, Rectangle c)
    {
        int n = series.Length;
        if (n == 0) return;
        long maxMs = series.Max(d => d.Fg + d.Bg);
        long maxMin = Math.Max(1, (maxMs + 59_999) / 60_000);
        long[] steps = [5, 10, 15, 30, 60, 120, 180, 240, 360, 480, 720];
        long step = steps.FirstOrDefault(s => s * 4 >= maxMin, 720);
        long top = step * 4 * 60_000;

        int axisW = S(56);
        plot = new Rectangle(c.X + S(20) + axisW, c.Y + S(58), c.Width - S(40) - axisW, c.Height - S(58) - S(40));
        if (plot.Width < 20 || plot.Height < 20) return;

        using (var pen = new Pen(Theme.Mix(T.Border, T.Card, 0.7f), Math.Max(1, S(1))))
        {
            for (int i = 0; i <= 4; i++)
            {
                int y = plot.Bottom - plot.Height * i / 4;
                g.DrawLine(pen, plot.X, y, plot.Right, y);
                Gfx.Text(g, Fmt.Axis(step * i), K.Small, T.Muted, new Rectangle(c.X + S(12), y - S(9), axisW, S(18)), TextFormatFlags.Right);
            }
        }

        float slot = plot.Width / (float)n;
        float bw = Math.Min(slot * 0.56f, S(30));
        bool sparse = slot < S(40);
        var today = DateTime.Today;
        for (int i = 0; i < n; i++)
        {
            var (day, fg, bg) = series[i];
            float cx = plot.X + slot * (i + 0.5f);
            if (i == hoverBar) Gfx.FillRound(g, T.Hover, new RectangleF(cx - slot / 2 + S(2), plot.Y - S(6), slot - S(4), plot.Height + S(6)), S(6));

            float allH = plot.Height * (fg + bg) / (float)top;
            float fgH = plot.Height * fg / (float)top;
            float rad = Math.Min(S(5), bw / 2);
            if (allH >= 1) Gfx.FillRound(g, T.AccentSoft, new RectangleF(cx - bw / 2, plot.Bottom - allH, bw, allH), rad, bottom: false);
            if (fgH >= 1) Gfx.FillRound(g, T.Accent, new RectangleF(cx - bw / 2, plot.Bottom - fgH, bw, fgH), bg == 0 ? rad : 0, bottom: false);

            bool isToday = day == today;
            if (!sparse || isToday || (n - 1 - i) % 2 == 0)
            {
                string label = isToday ? "今天" : $"{day.Month}/{day.Day}";
                Gfx.Text(g, label, isToday ? K.BodyBold : K.Small, isToday ? T.Text : T.Muted,
                    new Rectangle((int)(cx - S(30)), plot.Bottom + S(10), S(60), S(20)), TextFormatFlags.HorizontalCenter);
            }
        }

        if (hoverBar >= 0 && hoverBar < n) DrawBarTip(g, hoverBar, slot);
    }

    void DrawBarTip(Graphics g, int i, float slot)
    {
        var (day, fg, bg) = series[i];
        string[] lines = [Fmt.Day(day), $"前台  {Fmt.Duration(fg)}", $"后台  {Fmt.Duration(bg)}"];
        int lh = S(20), pad = S(12);
        int w = lines.Max(l => Gfx.Measure(l, K.Body).Width) + pad * 2;
        int h = lh * lines.Length + pad * 2 - S(4);
        float cx = plot.X + slot * (i + 0.5f);
        int x = (int)Math.Clamp(cx + slot / 2 + S(4), plot.X, plot.Right - w);
        if (x < cx && cx - slot / 2 - S(4) - w >= plot.X) x = (int)(cx - slot / 2 - S(4) - w);
        var r = new Rectangle(x, plot.Y, w, h);
        Gfx.FillRound(g, T.Bg, r, S(8));
        Gfx.StrokeRound(g, T.Border, r, S(8), Math.Max(1, S(1)));
        for (int k = 0; k < lines.Length; k++)
            Gfx.Text(g, lines[k], k == 0 ? K.BodyBold : K.Body, k == 0 ? T.Text : T.Muted,
                new Rectangle(r.X + pad, r.Y + pad - S(2) + k * lh, w - pad * 2, lh));
    }

    // ───── 空状态 ─────

    void DrawEmpty(Graphics g)
    {
        int cy = Height / 2 - S(70);
        float icon = S(56);
        Gfx.Icon(g, "grid", new RectangleF((Width - icon) / 2, cy, icon, icon), T.Muted);
        Gfx.Text(g, "还没有监控任何应用", K.H2, T.Text, new Rectangle(0, cy + S(70), Width, S(28)), TextFormatFlags.HorizontalCenter);
        Gfx.Text(g, "默认什么都不记录。添加之后，会分别统计前台使用和后台运行的时间。", K.Small, T.Muted,
            new Rectangle(0, cy + S(102), Width, S(20)), TextFormatFlags.HorizontalCenter);

        string text = "添加应用";
        int bw = Gfx.Measure(text, K.BodyBold).Width + S(62);
        addRect = new Rectangle((Width - bw) / 2, cy + S(140), bw, S(40));
        var fill = hoverAdd ? Theme.Mix(T.Accent, T.IsDark ? Color.White : Color.Black, 0.88f) : T.Accent;
        Gfx.FillRound(g, fill, addRect, S(10));
        float box = S(22);
        Gfx.Icon(g, "plus", new RectangleF(addRect.X + S(18), addRect.Y + (addRect.Height - box) / 2, box, box), T.AccentText);
        Gfx.Text(g, text, K.BodyBold, T.AccentText, new Rectangle(addRect.X + S(42), addRect.Y, bw - S(42), addRect.Height));
    }

    // ───── 交互 ─────

    protected override void Draw(Graphics g)
    {
        switch (mode)
        {
            case Mode.Overview: DrawOverview(g); break;
            case Mode.App: DrawApp(g); break;
            default: DrawEmpty(g); break;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int row = -1, bar = -1;
        bool add = false;
        if (mode == Mode.Overview && e.Y >= ListTop)
            for (int i = 0; i < rows.Count; i++)
                if (CardRect(i).Contains(e.Location)) { row = i; break; }
        if (mode == Mode.App && series.Length > 0 && e.X >= plot.X && e.X < plot.Right && e.Y >= plot.Y - S(6) && e.Y < plot.Bottom + S(30))
            bar = (int)((e.X - plot.X) / (plot.Width / (float)series.Length));
        if (mode == Mode.Empty) add = addRect.Contains(e.Location);

        if (row != hoverRow || bar != hoverBar || add != hoverAdd)
        {
            hoverRow = row;
            hoverBar = bar;
            hoverAdd = add;
            Cursor = row >= 0 || add ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hoverRow = hoverBar = -1;
        hoverAdd = false;
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            if (mode == Mode.Overview && hoverRow >= 0 && hoverRow < rows.Count) RowClicked?.Invoke(rows[hoverRow].Exe);
            else if (mode == Mode.Empty && hoverAdd) AddClicked?.Invoke();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (mode == Mode.Overview)
        {
            scroll -= e.Delta / 120 * S(60);
            ClampScroll();
            Invalidate();
        }
        base.OnMouseWheel(e);
    }

    protected override void OnResize(EventArgs e)
    {
        ClampScroll();
        base.OnResize(e);
    }
}
