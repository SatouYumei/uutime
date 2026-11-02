using System.Diagnostics;

namespace UuTime;

/// <summary>从正在运行的程序里挑（可多选、可搜索），或者浏览选一个 exe。</summary>
internal sealed class AddForm : ChromeForm
{
    internal sealed record Candidate(string Exe, string Name, string? Path, bool HasWindow);

    readonly Kit kit;
    readonly HashSet<string> watched;
    readonly List<Candidate> candidates;
    readonly HashSet<string> picked = new(StringComparer.OrdinalIgnoreCase);
    readonly TextBox search;
    readonly Toggle showAll;
    readonly PickList pickList;
    readonly TextButton browse, cancel, ok;
    Rectangle searchRect, toggleRow;

    public List<AppEntry> Result { get; } = new();

    public AddForm(Kit kit, HashSet<string> watched)
    {
        this.kit = kit;
        this.watched = watched;
        var t = kit.T;
        Text = "添加应用";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        SetBack(t.Bg);
        CaptionHeight = S(56);
        Size = new Size(S(560), S(640));
        MinimumSize = new Size(S(440), S(460));

        var close = new IconButton(kit, t.Bg, "close", "关闭");
        close.Click += (_, _) => DialogResult = DialogResult.Cancel;
        close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        close.Location = new Point(Width - S(12) - close.Width, (CaptionHeight - close.Height) / 2);

        search = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = t.Input,
            ForeColor = t.Text,
            Font = kit.Body,
            PlaceholderText = "搜索名称或 exe",
        };
        search.TextChanged += (_, _) => Fill();
        search.GotFocus += (_, _) => Root.Invalidate(searchRect);
        search.LostFocus += (_, _) => Root.Invalidate(searchRect);

        showAll = new Toggle(kit, t.Bg);
        showAll.CheckedChanged += Fill;

        pickList = new PickList(kit, t.Bg, picked);
        pickList.Toggled += UpdateOk;

        browse = new TextButton(kit, t.Bg, "浏览 exe…", ButtonKind.Outline, "folder");
        browse.Click += (_, _) => Browse();
        cancel = new TextButton(kit, t.Bg, "取消", ButtonKind.Outline);
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        ok = new TextButton(kit, t.Bg, "添加", ButtonKind.Accent);
        ok.Click += (_, _) => Accept();

        Root.Controls.AddRange([close, search, showAll, pickList, browse, cancel, ok]);
        candidates = Collect();
        Fill();
        UpdateOk();
    }

    public override int S(float v) => kit.S(v);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyChrome(kit.T);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        search.Focus();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (pickList == null) return;
        int pad = S(22), w = ClientSize.Width, h = ClientSize.Height;
        searchRect = new Rectangle(pad, CaptionHeight + S(2), w - pad * 2, S(40));
        int box = S(22);
        search.Bounds = new Rectangle(searchRect.X + S(14) + box + S(8), searchRect.Y + (searchRect.Height - search.PreferredHeight) / 2,
            searchRect.Width - S(14) - box - S(8) - S(14), search.PreferredHeight);
        toggleRow = new Rectangle(pad, searchRect.Bottom + S(10), w - pad * 2, S(36));
        showAll.Location = new Point(toggleRow.X + S(2), toggleRow.Y + (toggleRow.Height - showAll.Height) / 2);

        int by = h - S(20) - ok.Height;
        ok.Location = new Point(w - pad - ok.Width, by);
        cancel.Location = new Point(ok.Left - S(10) - cancel.Width, by);
        browse.Location = new Point(pad, by);
        pickList.Bounds = new Rectangle(pad - S(6), toggleRow.Bottom + S(6), w - pad * 2 + S(12), by - S(14) - toggleRow.Bottom - S(6));
        Root.Invalidate();
    }

    protected override void PaintRoot(Graphics g)
    {
        var t = kit.T;
        Gfx.Text(g, "添加要监控的应用", kit.H2, t.Text, new Rectangle(S(22), 0, S(320), CaptionHeight));

        Gfx.FillRound(g, t.Input, searchRect, S(10));
        if (search.Focused) Gfx.StrokeRound(g, Theme.Mix(t.Accent, t.Input, 0.6f), searchRect, S(10), Math.Max(1, S(1.5f)));
        float box = S(22);
        Gfx.Icon(g, "search", new RectangleF(searchRect.X + S(14), searchRect.Y + (searchRect.Height - box) / 2, box, box), t.Muted);

        Gfx.Text(g, "显示没有窗口的后台进程", kit.Body, t.Muted,
            new Rectangle(showAll.Right + S(10), toggleRow.Y, toggleRow.Width, toggleRow.Height));
    }

    void UpdateOk()
    {
        ok.Text = picked.Count == 0 ? "添加" : $"添加 {picked.Count} 个";
        ok.Enabled = picked.Count > 0;
        ok.FitWidth();
        OnResize(EventArgs.Empty);
    }

    void Fill()
    {
        string q = search.Text.Trim();
        pickList.SetItems(candidates.Where(c =>
            (showAll.Checked || c.HasWindow || picked.Contains(c.Exe)) &&
            (q.Length == 0 || c.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || c.Exe.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList());
    }

    void Accept()
    {
        foreach (var c in candidates)
            if (picked.Contains(c.Exe)) Result.Add(new AppEntry { Exe = c.Exe, Name = c.Name, Path = c.Path });
        DialogResult = DialogResult.OK;
    }

    void Browse()
    {
        using var dlg = new OpenFileDialog { Filter = "程序 (*.exe)|*.exe", Title = "选择要监控的程序" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        string exe = Native.ExeName(dlg.FileName)!;
        if (!watched.Contains(exe) && !picked.Contains(exe))
            Result.Add(new AppEntry { Exe = exe, Name = FriendlyName(dlg.FileName, exe), Path = dlg.FileName });
        Accept();
    }

    List<Candidate> Collect()
    {
        var map = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);
        int self = Environment.ProcessId;
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (p.Id == self || p.Id <= 4) continue;
                bool hasWindow = p.MainWindowHandle != IntPtr.Zero;
                string? path = Native.ProcessImagePath((uint)p.Id);
                string exe = Native.ExeName(path) ?? p.ProcessName.ToLowerInvariant() + ".exe";
                if (watched.Contains(exe)) continue;
                if (map.TryGetValue(exe, out var existing))
                {
                    if (hasWindow && !existing.HasWindow) map[exe] = existing with { HasWindow = true };
                    continue;
                }
                map[exe] = new Candidate(exe, FriendlyName(path, exe), path, hasWindow);
            }
        }
        return map.Values.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    static string FriendlyName(string? path, string exe)
    {
        try
        {
            if (path != null && FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } d) return d.Trim();
        }
        catch (Exception) { }
        return Path.GetFileNameWithoutExtension(exe);
    }

    /// <summary>候选列表：点一下选中/取消，右侧圆形勾选。</summary>
    sealed class PickList : Flat
    {
        readonly HashSet<string> picked;
        List<Candidate> items = new();
        int scroll;
        int hover = -1;

        public event Action? Toggled;

        public PickList(Kit kit, Color back, HashSet<string> picked) : base(kit, back)
        {
            this.picked = picked;
        }

        int RowH => S(54);

        public void SetItems(List<Candidate> list)
        {
            items = list;
            scroll = 0;
            hover = -1;
            Invalidate();
        }

        void Clamp() => scroll = Math.Clamp(scroll, 0, Math.Max(0, items.Count * RowH - Height));

        int IndexAt(int y)
        {
            int i = (y + scroll) / RowH;
            return y >= 0 && i < items.Count ? i : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = IndexAt(e.Y);
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
            int i = IndexAt(e.Y);
            if (i >= 0 && e.Button == MouseButtons.Left)
            {
                if (!picked.Remove(items[i].Exe)) picked.Add(items[i].Exe);
                Invalidate();
                Toggled?.Invoke();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            scroll -= e.Delta / 120 * RowH * 2;
            Clamp();
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void OnResize(EventArgs e)
        {
            Clamp();
            base.OnResize(e);
        }

        protected override void Draw(Graphics g)
        {
            if (items.Count == 0)
            {
                Gfx.Text(g, "没有匹配的程序", K.Body, T.Muted, new Rectangle(0, S(20), Width, S(24)), TextFormatFlags.HorizontalCenter);
                return;
            }
            int first = scroll / RowH;
            for (int i = first; i < items.Count; i++)
            {
                var r = new Rectangle(0, i * RowH - scroll, Width, RowH - S(2));
                if (r.Top > Height) break;
                var c = items[i];
                bool sel = picked.Contains(c.Exe);
                if (sel) Gfx.FillRound(g, T.Selected, r, S(10));
                else if (i == hover) Gfx.FillRound(g, T.Hover, r, S(10));

                K.DrawApp(g, c.Name, c.Path, new Rectangle(r.X + S(12), r.Y + (r.Height - S(28)) / 2, S(28), S(28)));
                int tx = r.X + S(54), tw = r.Width - S(54) - S(48);
                Gfx.Text(g, c.Name, sel ? K.BodyBold : K.Body, T.Text, new Rectangle(tx, r.Y + S(8), tw, S(20)));
                Gfx.Text(g, c.HasWindow ? c.Exe : c.Exe + " · 后台", K.Small, T.Muted, new Rectangle(tx, r.Y + S(29), tw, S(16)));

                float d = S(22), cx = r.Right - S(26), cy = r.Y + r.Height / 2f;
                if (sel)
                {
                    Gfx.Disc(g, T.Accent, cx, cy, d);
                    Gfx.Icon(g, "check", new RectangleF(cx - d / 2, cy - d / 2, d, d), T.AccentText);
                }
                else
                {
                    using var pen = new Pen(i == hover ? T.Muted : T.Border, Math.Max(1.2f, S(1.5f)));
                    g.DrawEllipse(pen, cx - d / 2, cy - d / 2, d, d);
                }
            }
        }
    }
}
