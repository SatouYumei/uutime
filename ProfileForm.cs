namespace UuTime;

/// <summary>存档管理：切换、改名、新建、删除，备份成文件、从文件导入。</summary>
internal sealed class ProfileForm : ChromeForm
{
    readonly Kit kit;
    readonly Store store;
    readonly Action<string> switchTo;
    readonly Action flush;
    readonly ProfileList list;
    readonly TextBox nameBox;
    readonly TextButton switchBtn, renameBtn, exportBtn, deleteBtn, newBtn, importBtn, doneBtn;
    readonly System.Windows.Forms.Timer disarm = new() { Interval = 3000 };
    bool deleteArmed, showSwitch;
    string status = "";
    bool statusError;
    Rectangle statusRect;

    /// <param name="switchTo">切换当前存档（由主窗口处理，顺便结算计时）</param>
    /// <param name="flush">导出当前存档前把计时结算进去</param>
    public ProfileForm(Kit kit, Store store, Action<string> switchTo, Action flush)
    {
        this.kit = kit;
        this.store = store;
        this.switchTo = switchTo;
        this.flush = flush;
        var t = kit.T;
        Text = "存档管理";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        SetBack(t.Bg);
        CaptionHeight = S(56);
        Size = new Size(S(600), S(560));
        MinimumSize = new Size(S(520), S(440));

        var close = new IconButton(kit, t.Bg, "close", "关闭");
        close.Click += (_, _) => Close();
        close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        close.Location = new Point(Width - S(12) - close.Width, (CaptionHeight - close.Height) / 2);

        list = new ProfileList(kit, t.Bg);
        list.SelectionChanged += () => { EndRename(commit: true); Disarm(); UpdateButtons(); };
        list.DoubleClicked += () => SwitchSelected();

        nameBox = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = t.Input,
            ForeColor = t.Text,
            Font = kit.BodyBold,
            MaxLength = 40,
            Visible = false,
        };
        nameBox.LostFocus += (_, _) => EndRename(commit: true);

        switchBtn = new TextButton(kit, t.Bg, "切换到这个存档", ButtonKind.Accent);
        switchBtn.Click += (_, _) => SwitchSelected();
        renameBtn = new TextButton(kit, t.Bg, "改名", ButtonKind.Outline);
        renameBtn.Click += (_, _) => BeginRename();
        exportBtn = new TextButton(kit, t.Bg, "备份…", ButtonKind.Outline, "export");
        exportBtn.Click += (_, _) => ExportSelected();
        deleteBtn = new TextButton(kit, t.Bg, "删除", ButtonKind.Danger);
        deleteBtn.Click += (_, _) => DeleteSelected();
        kit.Tip.SetToolTip(deleteBtn, "删除的存档会放进回收站，可以找回");

        newBtn = new TextButton(kit, t.Bg, "新建存档", ButtonKind.Ghost, "plus");
        newBtn.Click += (_, _) => CreateNew();
        importBtn = new TextButton(kit, t.Bg, "导入备份…", ButtonKind.Ghost, "import");
        importBtn.Click += (_, _) => ImportFile();
        doneBtn = new TextButton(kit, t.Bg, "完成", ButtonKind.Outline);
        doneBtn.Click += (_, _) => Close();

        disarm.Tick += (_, _) => Disarm();

        Root.Controls.AddRange([close, nameBox, list, switchBtn, renameBtn, exportBtn, deleteBtn, newBtn, importBtn, doneBtn]);
        Reload(store.ProfileId);
    }

    public override int S(float v) => kit.S(v);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyChrome(kit.T);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) disarm.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (doneBtn == null) return;
        int pad = S(22), w = ClientSize.Width, h = ClientSize.Height;

        int by = h - S(18) - doneBtn.Height;
        doneBtn.Location = new Point(w - pad - doneBtn.Width, by);
        newBtn.Location = new Point(pad - S(10), by);
        importBtn.Location = new Point(newBtn.Right + S(4), by);

        int ay = by - S(20) - switchBtn.Height;
        // 窗口显示前 Visible 总是 false，所以按自己记的状态排
        int x = pad;
        foreach (var b in showSwitch ? new[] { switchBtn, renameBtn, exportBtn } : [renameBtn, exportBtn])
        {
            b.Location = new Point(x, ay);
            x = b.Right + S(10);
        }
        deleteBtn.Location = new Point(w - pad - deleteBtn.Width, ay);

        statusRect = new Rectangle(pad, ay - S(30), w - pad * 2, S(22));
        int top = CaptionHeight + S(30);
        list.Bounds = new Rectangle(pad - S(6), top, w - pad * 2 + S(12), statusRect.Y - S(6) - top);
        PlaceNameBox();
        Root.Invalidate();
    }

    protected override void PaintRoot(Graphics g)
    {
        var t = kit.T;
        Gfx.Text(g, "存档管理", kit.H2, t.Text, new Rectangle(S(22), 0, S(320), CaptionHeight));
        Gfx.Text(g, "每个存档有自己的应用和记录，计时只记到当前存档里。", kit.Small, t.Muted,
            new Rectangle(S(22), CaptionHeight - S(6), ClientSize.Width - S(44), S(20)));
        if (status.Length > 0)
            Gfx.Text(g, status, kit.Small, statusError ? t.Danger : t.Accent, statusRect);
        using var line = new SolidBrush(t.Border);
        g.FillRectangle(line, S(22), doneBtn.Top - S(12), ClientSize.Width - S(44), 1);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (nameBox.Visible && (keyData == Keys.Enter || keyData == Keys.Escape))
        {
            EndRename(commit: keyData == Keys.Enter);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ───── 操作 ─────

    void Reload(string? select)
    {
        list.SetItems(store.Profiles(), store.ProfileId, select);
        UpdateButtons();
    }

    ProfileInfo? Selected => list.Selected;

    void UpdateButtons()
    {
        bool current = Selected?.Id == store.ProfileId;
        showSwitch = Selected != null && !current;
        switchBtn.Visible = showSwitch;
        deleteBtn.Visible = Selected != null && !current;
        renameBtn.Visible = exportBtn.Visible = Selected != null;
        OnResize(EventArgs.Empty);
    }

    void SetStatus(string text, bool error = false)
    {
        status = text;
        statusError = error;
        Root.Invalidate();
    }

    void SwitchSelected()
    {
        if (Selected is not { } p || p.Id == store.ProfileId) return;
        EndRename(commit: true);
        switchTo(p.Id);
        Reload(p.Id);
        SetStatus($"已切换到「{p.Name}」，之后的时间都记在这个存档里");
    }

    void BeginRename()
    {
        if (Selected == null) return;
        nameBox.Text = Selected.Name;
        list.Editing = true;
        PlaceNameBox();
        nameBox.Visible = true;
        nameBox.Focus();
        nameBox.SelectAll();
    }

    void PlaceNameBox()
    {
        if (!list.Editing) return;
        var r = list.NameRect();
        nameBox.Bounds = new Rectangle(list.Left + r.X + S(10), list.Top + r.Y + (r.Height - nameBox.PreferredHeight) / 2,
            r.Width - S(20), nameBox.PreferredHeight);
    }

    void EndRename(bool commit)
    {
        if (!nameBox.Visible) return;
        nameBox.Visible = false;
        list.Editing = false;
        string name = nameBox.Text.Trim();
        if (commit && Selected is { } p && name.Length > 0 && name != p.Name)
        {
            store.RenameProfile(p.Id, name);
            Reload(p.Id);
            SetStatus($"已改名为「{name}」");
            NameChanged?.Invoke();
        }
        list.Invalidate();
    }

    /// <summary>当前存档改了名，主窗口要更新标题栏。</summary>
    public event Action? NameChanged;

    void CreateNew()
    {
        var names = store.Profiles().Select(p => p.Name).ToHashSet();
        string name = "新存档";
        for (int n = 2; names.Contains(name); n++) name = $"新存档 {n}";
        string id = store.CreateProfile(name);
        Reload(id);
        SetStatus("已新建存档，可以直接输入名字");
        BeginRename();
    }

    void ExportSelected()
    {
        if (Selected is not { } p) return;
        string safe = string.Concat(p.Name.Split(Path.GetInvalidFileNameChars()));
        using var dlg = new SaveFileDialog
        {
            Title = "备份存档",
            Filter = "uutime 备份 (*.json)|*.json",
            FileName = $"uutime-{safe}-{DateTime.Now:yyyyMMdd}.json",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            if (p.Id == store.ProfileId) flush();
            store.Export(p.Id, dlg.FileName);
            SetStatus($"已备份到 {dlg.FileName}");
        }
        catch (Exception ex)
        {
            SetStatus("备份失败：" + ex.Message, error: true);
        }
    }

    void ImportFile()
    {
        using var dlg = new OpenFileDialog { Title = "导入备份", Filter = "uutime 备份 (*.json)|*.json|所有文件 (*.*)|*.*" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        string? id = store.Import(dlg.FileName);
        if (id == null)
        {
            SetStatus("这个文件不是 uutime 的备份", error: true);
            return;
        }
        Reload(id);
        SetStatus($"已导入为新存档「{Selected?.Name}」，原来的存档都没有动");
    }

    /// <summary>点两下才删：第一下变成确认，3 秒内再点生效。</summary>
    void DeleteSelected()
    {
        if (Selected is not { } p || p.Id == store.ProfileId) return;
        if (!deleteArmed)
        {
            deleteArmed = true;
            deleteBtn.Text = "再点一次确认删除";
            deleteBtn.FitWidth();
            OnResize(EventArgs.Empty);
            disarm.Start();
            return;
        }
        Disarm();
        if (store.DeleteProfile(p.Id))
        {
            Reload(store.ProfileId);
            SetStatus($"「{p.Name}」已移到回收站");
        }
        else SetStatus("删除失败，文件可能正在被占用", error: true);
    }

    void Disarm()
    {
        disarm.Stop();
        if (!deleteArmed) return;
        deleteArmed = false;
        deleteBtn.Text = "删除";
        deleteBtn.FitWidth();
        OnResize(EventArgs.Empty);
    }

    /// <summary>存档列表：头像、名字、当前标记、应用数和记录天数。</summary>
    sealed class ProfileList : Flat
    {
        List<ProfileInfo> items = new();
        string currentId = "";
        int selected = -1;
        int hover = -1;
        int scroll;

        public bool Editing;
        public event Action? SelectionChanged;
        public event Action? DoubleClicked;

        public ProfileList(Kit kit, Color back) : base(kit, back)
        {
            SetStyle(ControlStyles.StandardDoubleClick, true);
        }

        public ProfileInfo? Selected => selected >= 0 && selected < items.Count ? items[selected] : null;

        int RowH => S(68);

        public void SetItems(List<ProfileInfo> list, string current, string? select)
        {
            items = list;
            currentId = current;
            selected = Math.Max(0, items.FindIndex(p => p.Id == (select ?? current)));
            Clamp();
            Reveal();
            Invalidate();
        }

        Rectangle RowRect(int i) => new(0, i * (RowH + S(4)) - scroll, Width, RowH);

        /// <summary>选中行名字的位置（改名输入框放这里）。</summary>
        public Rectangle NameRect()
        {
            var r = RowRect(selected);
            return new Rectangle(r.X + S(62), r.Y + S(9), Math.Min(S(300), r.Width - S(62) - S(110)), S(30));
        }

        void Clamp() => scroll = Math.Clamp(scroll, 0, Math.Max(0, items.Count * (RowH + S(4)) - Height));

        void Reveal()
        {
            if (selected < 0) return;
            var r = RowRect(selected);
            if (r.Top < 0) scroll += r.Top;
            else if (r.Bottom > Height) scroll += r.Bottom - Height;
            Clamp();
        }

        int IndexAt(Point p)
        {
            for (int i = 0; i < items.Count; i++)
                if (RowRect(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = IndexAt(e.Location);
            if (i != hover) { hover = i; Cursor = i >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
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
            if (i >= 0 && i != selected && e.Button == MouseButtons.Left)
            {
                selected = i;
                Invalidate();
                SelectionChanged?.Invoke();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (IndexAt(e.Location) == selected && selected >= 0) DoubleClicked?.Invoke();
            base.OnMouseDoubleClick(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (!Editing)
            {
                scroll -= e.Delta / 120 * RowH;
                Clamp();
                Invalidate();
            }
            base.OnMouseWheel(e);
        }

        protected override void OnResize(EventArgs e)
        {
            Clamp();
            base.OnResize(e);
        }

        protected override void Draw(Graphics g)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var r = RowRect(i);
                if (r.Bottom < 0 || r.Top > Height) continue;
                var p = items[i];
                bool sel = i == selected, current = p.Id == currentId;
                if (sel) Gfx.FillRound(g, T.Selected, r, S(10));
                else if (i == hover) Gfx.FillRound(g, T.Hover, r, S(10));

                var av = new Rectangle(r.X + S(14), r.Y + (r.Height - S(36)) / 2, S(36), S(36));
                Gfx.Disc(g, current ? T.Accent : Theme.Mix(T.Accent, T.Card, 0.22f), av.X + av.Width / 2f, av.Y + av.Height / 2f, av.Width);
                string letter = p.Name.Length > 0 ? p.Name[..1].ToUpperInvariant() : "?";
                TextRenderer.DrawText(g, letter, K.BodyBold, av, current ? T.AccentText : T.Accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                int tx = r.X + S(62);
                if (sel && Editing)
                {
                    Gfx.FillRound(g, T.Input, NameRect(), S(8));
                    Gfx.StrokeRound(g, Theme.Mix(T.Accent, T.Input, 0.6f), NameRect(), S(8), Math.Max(1, S(1.5f)));
                }
                else
                {
                    int nw = Math.Min(Gfx.Measure(p.Name, K.BodyBold).Width, r.Width - S(62) - S(130));
                    Gfx.Text(g, p.Name, K.BodyBold, T.Text, new Rectangle(tx, r.Y + S(12), nw + S(2), S(22)));
                    if (current)
                    {
                        string tag = "当前";
                        int tw = Gfx.Measure(tag, K.Small).Width + S(14);
                        var tr = new Rectangle(tx + nw + S(10), r.Y + S(13), tw, S(20));
                        Gfx.FillRound(g, Theme.Mix(T.Accent, sel ? T.Selected : T.Bg, 0.2f), tr, S(6));
                        Gfx.Text(g, tag, K.Small, T.Accent, tr, TextFormatFlags.HorizontalCenter);
                    }
                }
                string sub = $"{p.Apps} 个应用   ·   记录了 {p.Days} 天   ·   前台合计 {Fmt.Duration(p.Fg)}";
                Gfx.Text(g, sub, K.Small, T.Muted, new Rectangle(tx, r.Y + S(40), r.Width - S(62) - S(16), S(18)));

                if (current)
                {
                    string rec = "● 正在记录";
                    int rw = Gfx.Measure(rec, K.Small).Width;
                    Gfx.Text(g, rec, K.Small, T.Accent, new Rectangle(r.Right - S(16) - rw, r.Y + S(13), rw + S(2), S(20)));
                }
            }
        }
    }
}
