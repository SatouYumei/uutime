namespace UuTime;

/// <summary>主题选择：4 列色卡，和 chatuutts 的一样。</summary>
internal sealed class ThemePicker : ChromeForm
{
    readonly Kit kit;

    public string Chosen { get; private set; }

    public ThemePicker(Kit kit, string current)
    {
        this.kit = kit;
        Chosen = current;
        Text = "主题";
        Resizable = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        SetBack(kit.T.Bg);
        CaptionHeight = S(52);

        var grid = new ThemeGrid(kit, kit.T.Bg, current);
        grid.Picked += id =>
        {
            Chosen = id;
            DialogResult = DialogResult.OK;
        };
        var close = new IconButton(kit, kit.T.Bg, "close", "关闭");
        close.Click += (_, _) => DialogResult = DialogResult.Cancel;

        int pad = S(18);
        grid.Location = new Point(pad, CaptionHeight + S(4));
        grid.Size = grid.PreferredSize;
        Size = new Size(grid.Width + pad * 2, grid.Bottom + pad);
        close.Location = new Point(Width - S(12) - close.Width, (CaptionHeight - close.Height) / 2);
        Root.Controls.AddRange([grid, close]);
    }

    public override int S(float v) => kit.S(v);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyChrome(kit.T);
    }

    protected override void PaintRoot(Graphics g) =>
        Gfx.Text(g, "主题", kit.H2, kit.T.Text, new Rectangle(S(22), 0, S(200), CaptionHeight));

    sealed class ThemeGrid : Flat
    {
        const int Cols = 4;
        readonly string current;
        int hover = -1;

        public event Action<string>? Picked;

        public ThemeGrid(Kit kit, Color back, string current) : base(kit, back)
        {
            this.current = current;
            Cursor = Cursors.Hand;
        }

        int CardW => S(118);
        int CardH => S(72);
        int Gap => S(10);

        public override Size GetPreferredSize(Size proposed)
        {
            int rows = (Theme.All.Length + Cols - 1) / Cols;
            return new Size(Cols * CardW + (Cols - 1) * Gap, rows * CardH + (rows - 1) * Gap);
        }

        Rectangle CardRect(int i) => new(i % Cols * (CardW + Gap), i / Cols * (CardH + Gap), CardW, CardH);

        int IndexAt(Point p)
        {
            for (int i = 0; i < Theme.All.Length; i++)
                if (CardRect(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = IndexAt(e.Location);
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
            int i = IndexAt(e.Location);
            if (i >= 0) Picked?.Invoke(Theme.All[i].Id);
            base.OnMouseDown(e);
        }

        protected override void Draw(Graphics g)
        {
            for (int i = 0; i < Theme.All.Length; i++)
            {
                var item = Theme.All[i];
                var r = CardRect(i);
                Gfx.FillRound(g, item.Bg, r, S(10));
                bool sel = item.Id == current;
                var edge = sel ? T.Accent : i == hover ? T.Muted : T.Border;
                Gfx.StrokeRound(g, edge, r, S(10), sel ? S(2.5f) : Math.Max(1, S(1)));

                Gfx.FillRound(g, item.Input, new RectangleF(r.X + S(14), r.Y + S(16), S(62), S(10)), S(5));
                Gfx.Disc(g, item.Accent, r.Right - S(26), r.Y + S(21), S(20));
                Gfx.Text(g, item.Name, K.Small, item.Text, new Rectangle(r.X, r.Y + S(40), r.Width, S(20)), TextFormatFlags.HorizontalCenter);
            }
        }
    }
}
