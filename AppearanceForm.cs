namespace UuTime;

/// <summary>
/// 外观：暗色 / 亮色，底色和主题色（预设色块或调色盘），背景图和遮罩浓度。右边实时预览，点「应用」才保存到存档。
/// </summary>
internal sealed class AppearanceForm : ChromeForm
{
    readonly Kit kit;
    readonly Store store;
    readonly Segmented mode;
    readonly SwatchRow bases, accents;
    readonly TextButton pickImage, removeImage, reset, cancel, apply;
    readonly Slider baseClear, cardClear;
    readonly Preview preview;
    Rectangle modeRow, baseHead, accentHead, imageHead, baseClearRow, cardClearRow;
    string status = "";
    bool statusError;

    // 正在编辑的值（应用前不写进存档）
    bool light;
    Color? baseColor, accent;
    int dimValue, cardOpacity;
    string? pendingImage;   // 新选的图（已压缩好的临时文件）
    bool dropImage;         // 移除背景图

    public AppearanceForm(Kit kit, Store store)
    {
        this.kit = kit;
        this.store = store;
        var t = kit.T;
        var p = store.Prefs;
        light = p.Theme == "light";
        baseColor = Theme.TryParse(p.Base, out var b) ? b : null;
        accent = Theme.TryParse(p.Accent, out var a) ? a : null;
        dimValue = p.BackdropDim;
        cardOpacity = p.CardOpacity;

        Text = "外观";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Resizable = false;
        SetBack(t.Bg);
        CaptionHeight = S(56);
        Size = new Size(S(880), S(640));

        var close = new IconButton(kit, t.Bg, "close", "关闭");
        close.Click += (_, _) => DialogResult = DialogResult.Cancel;
        close.Location = new Point(Width - S(12) - close.Width, (CaptionHeight - close.Height) / 2);

        mode = new Segmented(kit, t.Bg, ["暗色", "亮色"]) { Selected = light ? 1 : 0 };
        mode.Changed += () =>
        {
            light = mode.Selected == 1;
            baseColor = null; // 换模式回到这个模式的默认底色
            Changed();
        };

        bases = new SwatchRow(kit, t.Bg, BaseList(), custom: true);
        bases.Clicked += i =>
        {
            if (i >= 0) baseColor = i == 0 ? null : BaseList()[i]; // 第一个就是默认底色
            else if (Pick(Bg(), "自定义底色") is { } c) baseColor = c;
            Changed();
        };
        accents = new SwatchRow(kit, t.Bg, Theme.Accents, custom: true);
        accents.Clicked += i =>
        {
            if (i >= 0) accent = Theme.Accents[i];
            else if (Pick(Current().Accent, "自定义主题色") is { } c) accent = c;
            Changed();
        };

        pickImage = new TextButton(kit, t.Bg, "选择图片…", ButtonKind.Outline, "folder");
        pickImage.Click += (_, _) => ChooseImage();
        removeImage = new TextButton(kit, t.Bg, "移除", ButtonKind.Ghost);
        removeImage.Click += (_, _) =>
        {
            DeletePending();
            dropImage = true;
            SetStatus("");
            Changed();
        };
        // 滑块显示「透明度」，存的是不透明度：越透明图越清楚，越不透明字越清楚
        baseClear = new Slider(kit, t.Bg, 100 - dimValue, 0, 100, v => $"{v}%");
        baseClear.ValueChanged += v => { dimValue = 100 - v; Changed(); };
        kit.Tip.SetToolTip(baseClear, "盖在背景图上的底色有多透明：越大图越清楚，越小文字越清楚");
        cardClear = new Slider(kit, t.Bg, 100 - cardOpacity, 0, 90, v => $"{v}%");
        cardClear.ValueChanged += v => { cardOpacity = 100 - v; Changed(); };
        kit.Tip.SetToolTip(cardClear, "卡片、选中项这些实心块有多透明");

        preview = new Preview(kit, t.Bg);

        reset = new TextButton(kit, t.Bg, "恢复默认", ButtonKind.Ghost);
        reset.Click += (_, _) =>
        {
            baseColor = accent = null;
            dimValue = 72;
            cardOpacity = 50;
            baseClear.Value = 100 - dimValue;
            cardClear.Value = 100 - cardOpacity;
            Changed();
        };
        cancel = new TextButton(kit, t.Bg, "取消", ButtonKind.Outline);
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        apply = new TextButton(kit, t.Bg, "应用", ButtonKind.Accent);
        apply.Click += (_, _) => Apply();

        Root.Controls.AddRange([close, mode, bases, accents, pickImage, removeImage, baseClear, cardClear, preview, reset, cancel, apply]);
        LayoutAll();
        Changed();
    }

    public override int S(float v) => kit.S(v);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyChrome(kit.T);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DeletePending();
            preview.Image?.Dispose();
        }
        base.Dispose(disposing);
    }

    Color Bg() => baseColor ?? (light ? Theme.Light.Bg : Theme.Dark.Bg);

    Color[] BaseList() => light ? Theme.LightBases : Theme.DarkBases;

    /// <summary>按正在编辑的值得出配色。</summary>
    Theme Current() => Theme.From(new Prefs
    {
        Theme = light ? "light" : "dark",
        Base = baseColor is { } b ? Theme.Hex(b) : null,
        Accent = accent is { } a ? Theme.Hex(a) : null,
    });

    /// <summary>预览里用的图：新选的、原来的，或者被移除了就没有。</summary>
    string? ImageFile() => pendingImage ?? (dropImage ? null : store.Background);

    void LayoutAll()
    {
        int pad = S(24), left = S(380), y = CaptionHeight + S(4);
        modeRow = new Rectangle(pad, y, left, S(44));
        mode.Location = new Point(modeRow.Right - mode.Width, modeRow.Y + (modeRow.Height - mode.Height) / 2);
        y = modeRow.Bottom + S(10);

        baseHead = new Rectangle(pad, y, left, S(26));
        bases.Bounds = new Rectangle(pad - S(4), baseHead.Bottom + S(2), left + S(8), 0);
        bases.Height = bases.PreferredHeight(bases.Width);
        y = bases.Bottom + S(10);

        accentHead = new Rectangle(pad, y, left, S(26));
        accents.Bounds = new Rectangle(pad - S(4), accentHead.Bottom + S(2), left + S(8), 0);
        accents.Height = accents.PreferredHeight(accents.Width);
        y = accents.Bottom + S(14);

        imageHead = new Rectangle(pad, y, left, S(26));
        pickImage.Location = new Point(pad, imageHead.Bottom + S(6));
        removeImage.Location = new Point(pickImage.Right + S(8), pickImage.Top);
        baseClearRow = new Rectangle(pad, pickImage.Bottom + S(50), left, S(38));
        baseClear.Location = new Point(baseClearRow.Right - baseClear.Width, baseClearRow.Y + (baseClearRow.Height - baseClear.Height) / 2);
        cardClearRow = new Rectangle(pad, baseClearRow.Bottom, left, S(38));
        cardClear.Location = new Point(cardClearRow.Right - cardClear.Width, cardClearRow.Y + (cardClearRow.Height - cardClear.Height) / 2);

        int by = ClientSize.Height - S(18) - apply.Height;
        apply.Location = new Point(ClientSize.Width - pad - apply.Width, by);
        cancel.Location = new Point(apply.Left - S(10) - cancel.Width, by);
        reset.Location = new Point(pad - S(10), by);
        int px = pad + left + S(28);
        preview.Bounds = new Rectangle(px, CaptionHeight + S(10), ClientSize.Width - px - pad, by - S(22) - CaptionHeight - S(10));
    }

    protected override void PaintRoot(Graphics g)
    {
        var t = kit.T;
        Gfx.Text(g, "外观", kit.H2, t.Text, new Rectangle(S(24), 0, S(120), CaptionHeight));
        Gfx.Text(g, "改动先在右边预览，点「应用」才保存到这个存档", kit.Small, t.Muted, new Rectangle(S(84), S(3), S(400), CaptionHeight));

        Gfx.Text(g, "模式", kit.Body, t.Text, modeRow);
        Gfx.Text(g, "底色", kit.Body, t.Text, baseHead);
        Gfx.Text(g, "主题色", kit.Body, t.Text, accentHead);
        Gfx.Text(g, "背景图", kit.Body, t.Text, imageHead);

        string? file = ImageFile();
        string line = status.Length > 0 ? status : file != null ? Wallpaper.Describe(file) : "没有背景图";
        Gfx.Text(g, line, kit.Small, statusError ? t.Danger : t.Muted, new Rectangle(S(24), pickImage.Bottom + S(6), S(380), S(20)));
        Gfx.Text(g, Wallpaper.Limits, kit.Small, t.Muted, new Rectangle(S(24), pickImage.Bottom + S(26), S(380), S(20)));
        Gfx.Text(g, "底色透明度", kit.Body, file != null ? t.Text : t.Muted, baseClearRow);
        Gfx.Text(g, "卡片透明度", kit.Body, file != null ? t.Text : t.Muted, cardClearRow);

        using var border = new SolidBrush(t.Border);
        g.FillRectangle(border, S(24), apply.Top - S(12), ClientSize.Width - S(48), 1);
    }

    void Changed()
    {
        bases.Colors = BaseList();
        bases.Current = Bg();
        accents.Current = Current().Accent;
        baseClear.Enabled = cardClear.Enabled = ImageFile() != null;
        removeImage.Visible = ImageFile() != null;
        preview.Show(Current(), ImageFile(), dimValue, cardOpacity);
        bases.Invalidate();
        accents.Invalidate();
        Root.Invalidate();
    }

    Color? Pick(Color initial, string title)
    {
        using var dlg = new ColorPickerForm(kit, initial, title);
        return dlg.ShowDialog(this) == DialogResult.OK ? dlg.Chosen : null;
    }

    void SetStatus(string text, bool error = false)
    {
        status = text;
        statusError = error;
        Root.Invalidate();
    }

    void ChooseImage()
    {
        using var dlg = new OpenFileDialog { Title = "选择背景图", Filter = Wallpaper.Filter };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        Cursor = Cursors.WaitCursor;
        string tmp = Path.Combine(Path.GetTempPath(), $"uutime-bg-{Guid.NewGuid():N}.jpg");
        string? error = Wallpaper.Import(dlg.FileName, tmp);
        Cursor = Cursors.Default;
        if (error != null)
        {
            SetStatus(error, error: true);
            return;
        }
        DeletePending();
        pendingImage = tmp;
        dropImage = false;
        long before = new FileInfo(dlg.FileName).Length / 1024;
        SetStatus($"{Wallpaper.Describe(tmp)}（原图 {before} KB）");
        Changed();
    }

    void DeletePending()
    {
        if (pendingImage == null) return;
        try { File.Delete(pendingImage); } catch (IOException) { }
        pendingImage = null;
    }

    void Apply()
    {
        var p = store.Prefs;
        p.Theme = light ? "light" : "dark";
        p.Base = baseColor is { } b ? Theme.Hex(b) : null;
        p.Accent = accent is { } a ? Theme.Hex(a) : null;
        p.BackdropDim = dimValue;
        p.CardOpacity = cardOpacity;
        if (pendingImage != null) store.SetBackground(pendingImage);
        else if (dropImage) store.ClearBackground();
        store.SavePrefs();
        DialogResult = DialogResult.OK;
    }

    /// <summary>缩小版的主窗口，用正在编辑的配色和背景图画出来。</summary>
    sealed class Preview : Flat
    {
        Theme? theme;
        string? file;
        int dimPercent, cardPercent;
        public Bitmap? Image;
        Bitmap? cover;

        public Preview(Kit kit, Color back) : base(kit, back) { }

        public void Show(Theme t, string? imageFile, int dim, int card)
        {
            theme = t;
            dimPercent = dim;
            cardPercent = card;
            if (imageFile != file)
            {
                file = imageFile;
                Image?.Dispose();
                Image = Wallpaper.Load(file);
                cover?.Dispose();
                cover = null;
            }
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) cover?.Dispose();
            base.Dispose(disposing);
        }

        protected override void Draw(Graphics g)
        {
            if (theme is not { } t) return;
            // 和主窗口一样：有背景图时实心块按卡片透明度画，加细描边
            void Block(Color c, RectangleF r, float radius)
            {
                if (Image == null) { Gfx.FillRound(g, c, r, radius); return; }
                Gfx.FillRound(g, Color.FromArgb(cardPercent * 255 / 100, c), r, radius);
                Gfx.StrokeRound(g, t.IsDark ? Color.FromArgb(34, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0), r, radius, 1);
            }
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using var clip = Gfx.Round(r, S(12));
            var old = g.Clip;
            g.SetClip(clip, System.Drawing.Drawing2D.CombineMode.Intersect);

            int head = S(34), side = S(118);
            var sideR = new Rectangle(0, head, side, Height - head);
            var mainR = new Rectangle(side, head, Width - side, Height - head);

            if (Image != null)
            {
                if (cover == null || cover.Size != Size) { cover?.Dispose(); cover = Wallpaper.Cover(Image, Size); }
                g.DrawImageUnscaled(cover, 0, 0);
                int alpha = dimPercent * 255 / 100;
                using var bgB = new SolidBrush(Color.FromArgb(alpha, t.Bg));
                using var sbB = new SolidBrush(Color.FromArgb(alpha, t.Sidebar));
                g.FillRectangle(bgB, 0, 0, Width, head);
                g.FillRectangle(bgB, mainR);
                g.FillRectangle(sbB, sideR);
            }
            else
            {
                using var bgB = new SolidBrush(t.Bg);
                using var sbB = new SolidBrush(t.Sidebar);
                g.FillRectangle(bgB, 0, 0, Width, Height);
                g.FillRectangle(sbB, sideR);
            }
            using (var line = new SolidBrush(t.Border))
            {
                g.FillRectangle(line, 0, head, Width, 1);
                g.FillRectangle(line, side, head, 1, Height - head);
            }

            // 标题栏
            Gfx.Disc(g, t.Accent, S(16), head / 2f, S(16));
            Gfx.Logo(g, t, S(14), S(28), head / 2f);

            // 侧边栏：选中项 + 几个应用
            var sel = new Rectangle(S(6), head + S(8), side - S(12), S(26));
            Block(t.Selected, sel, S(6));
            Gfx.FillRound(g, t.Accent, new RectangleF(sel.X, sel.Y + S(7), S(2), sel.Height - S(14)), S(1));
            Gfx.Text(g, "全部应用", K.Small, t.Text, new Rectangle(sel.X + S(12), sel.Y, sel.Width, sel.Height));
            string[] names = ["FF14", "QQ", "Chrome", "Claude"];
            for (int i = 0; i < names.Length; i++)
            {
                int y = sel.Bottom + S(10) + i * S(30);
                Gfx.FillRound(g, Theme.Mix(t.Accent, t.Card, 0.25f), new Rectangle(S(12), y + S(4), S(16), S(16)), S(4));
                Gfx.Text(g, names[i], K.Small, t.Text, new Rectangle(S(34), y, side - S(40), S(14)));
                Gfx.Text(g, $"前台 {12 + i * 7}分", K.Small, t.Muted, new Rectangle(S(34), y + S(13), side - S(40), S(14)));
            }

            // 主区域：标题、分段、卡片里的环形图和排行
            int mx = mainR.X + S(16);
            Gfx.Text(g, "全部应用", K.BodyBold, t.Text, new Rectangle(mx, head + S(10), S(120), S(20)));
            Gfx.Text(g, "前台 5小时41分 · 6 个应用", K.Small, t.Muted, new Rectangle(mx, head + S(32), S(200), S(16)));
            var segR = new Rectangle(mainR.Right - S(110), head + S(12), S(96), S(22));
            Block(t.Input, segR, segR.Height / 2f);
            Gfx.FillRound(g, t.Accent, new Rectangle(segR.X + S(3), segR.Y + S(3), S(34), segR.Height - S(6)), (segR.Height - S(6)) / 2f);
            Gfx.Text(g, "今天", K.Small, t.AccentText, new Rectangle(segR.X + S(3), segR.Y, S(34), segR.Height), TextFormatFlags.HorizontalCenter);

            var card = new Rectangle(mx, head + S(58), S(120), Height - head - S(72));
            Block(t.Card, card, S(8));
            float d = Math.Min(card.Width - S(30), S(84)), ring = S(10);
            var box = new RectangleF(card.X + (card.Width - d) / 2 + ring / 2, card.Y + S(14) + ring / 2, d - ring, d - ring);
            using (var pen = new Pen(t.Input, ring)) g.DrawEllipse(pen, box);
            Color[] slices = [t.Accent, Color.FromArgb(0x7F, 0x77, 0xDD), Color.FromArgb(0xE0, 0x7B, 0x4F)];
            float[] sweeps = [200, 90, 50];
            float start = -90;
            for (int i = 0; i < 3; i++)
            {
                using var pen = new Pen(slices[i], ring);
                g.DrawArc(pen, box, start + 1, sweeps[i] - 2);
                start += sweeps[i];
            }
            Gfx.Text(g, "5:41", K.BodyBold, t.Text, Rectangle.Round(box), TextFormatFlags.HorizontalCenter);

            int lx = card.Right + S(14), lw = mainR.Right - lx - S(14);
            for (int i = 0; i < 4; i++)
            {
                int y = card.Y + S(4) + i * S(34);
                if (i == 0) Block(t.Card, new Rectangle(lx - S(4), y, lw + S(8), S(30)), S(6));
                Gfx.Disc(g, i < 3 ? slices[i] : t.Border, lx + S(4), y + S(15), S(6));
                Gfx.Text(g, names[i], K.Small, t.Text, new Rectangle(lx + S(14), y + S(2), lw - S(60), S(14)));
                Gfx.Text(g, "后台 2小时", K.Small, t.Muted, new Rectangle(lx + S(14), y + S(15), lw - S(60), S(14)));
                Gfx.Text(g, $"{3 - i}:12", K.Small, t.Text, new Rectangle(lx, y + S(2), lw, S(14)), TextFormatFlags.Right);
            }

            g.Clip = old;
            Gfx.StrokeRound(g, t.Border, r, S(12), Math.Max(1, S(1)));
        }
    }
}
