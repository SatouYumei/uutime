namespace UuTime;

/// <summary>
/// 界面配色。默认只有暗色、亮色两套；自定义时用户只挑「底色」和「主题色」两个颜色，
/// 侧边栏、卡片、输入框、边框、文字等都按底色明暗自动推出来。
/// </summary>
internal sealed record Theme(
    string Id, string Name,
    Color Bg, Color Sidebar, Color Card, Color Input,
    Color Text, Color Muted, Color Accent, Color AccentText,
    Color Border, Color Hover, Color Selected, Color Danger)
{
    public bool IsDark => Luma(Bg) < 0.45f;

    /// <summary>后台时间用的浅色强调：强调色按比例混进卡片底色。</summary>
    public Color AccentSoft => Mix(Accent, Card, 0.38f);

    public static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R * t + b.R * (1 - t)), (int)(a.G * t + b.G * (1 - t)), (int)(a.B * t + b.B * (1 - t)));

    /// <summary>amount &gt; 0 往白色调，&lt; 0 往黑色调。</summary>
    public static Color Shade(Color c, float amount) =>
        amount >= 0 ? Mix(Color.White, c, amount) : Mix(Color.Black, c, -amount);

    public static float Luma(Color c) => (0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B) / 255f;

    public static Color C(string hex) => ColorTranslator.FromHtml(hex);

    public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static bool TryParse(string? hex, out Color c)
    {
        c = Color.Empty;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        string s = hex.Trim().TrimStart('#');
        if (s.Length != 6 || !int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out int v)) return false;
        c = Color.FromArgb((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
        return true;
    }

    public static readonly Theme Dark = new("dark", "暗色",
        C("#16181D"), C("#12141A"), C("#1C1F26"), C("#101216"), C("#F4F4F5"), C("#9CA3AF"),
        C("#2DD4BF"), C("#042F2E"), C("#2A2E36"), C("#22262E"), C("#16302E"), C("#F07178"));

    public static readonly Theme Light = new("light", "亮色",
        C("#F6F7F9"), C("#FFFFFF"), C("#FFFFFF"), C("#EEF1F4"), C("#1A1C1F"), C("#667085"),
        C("#0F766E"), C("#FFFFFF"), C("#E4E7EC"), C("#E7F6F4"), C("#E6F4F1"), C("#DC2626"));

    /// <summary>主题色可选的预设色块。</summary>
    public static readonly Color[] Accents =
    [
        C("#2DD4BF"), C("#0F766E"), C("#3B82F6"), C("#6366F1"), C("#8B5CF6"), C("#EC4899"),
        C("#F43F5E"), C("#FE8019"), C("#F59E0B"), C("#84CC16"), C("#22C55E"), C("#64748B"),
    ];

    /// <summary>底色预设：暗色和亮色各几种冷暖。</summary>
    public static readonly Color[] DarkBases = [C("#16181D"), C("#1E1E1E"), C("#282828"), C("#1A1B26"), C("#1E1E2E"), C("#0D1117"), C("#1B2421")];
    public static readonly Color[] LightBases = [C("#F6F7F9"), C("#FFFFFF"), C("#FBF7F0"), C("#F3F6FB"), C("#F7F3FA"), C("#F2F7F2"), C("#FDF4F5")];

    /// <summary>按存档设置得出配色：没自定义就是默认暗色 / 亮色。</summary>
    public static Theme From(Prefs p)
    {
        var baseTheme = p.Theme == "light" ? Light : Dark;
        bool hasBg = TryParse(p.Base, out var bg), hasAccent = TryParse(p.Accent, out var accent);
        if (!hasBg && !hasAccent) return baseTheme;
        if (!hasBg) return WithAccent(baseTheme, accent);
        return Derive(bg, hasAccent ? accent : (Luma(bg) < 0.45f ? Dark.Accent : Light.Accent));
    }

    static Color TextOn(Color c) => Luma(c) > 0.55f ? Mix(Color.Black, c, 0.82f) : Color.White;

    /// <summary>默认底色只换主题色：只动和主题色有关的几种颜色，其它保持原样。</summary>
    static Theme WithAccent(Theme t, Color a) => t with
    {
        Id = "custom",
        Name = "自定义",
        Accent = a,
        AccentText = TextOn(a),
        Selected = Mix(a, t.Sidebar, t.IsDark ? 0.17f : 0.11f),
        Hover = t.IsDark ? t.Hover : Mix(a, t.Sidebar, 0.08f),
    };

    /// <summary>从底色和主题色推出整套配色。</summary>
    public static Theme Derive(Color bg, Color a)
    {
        if (Luma(bg) < 0.45f)
        {
            var sidebar = Shade(bg, -0.18f);
            var text = Mix(Color.White, bg, 0.94f);
            return new Theme("custom", "自定义", bg, sidebar, Shade(bg, 0.035f), Shade(bg, -0.28f),
                text, Mix(text, bg, 0.6f), a, TextOn(a), Shade(bg, 0.09f), Shade(bg, 0.045f),
                Mix(a, sidebar, 0.17f), C("#F07178"));
        }
        else
        {
            var sidebar = Shade(bg, 0.6f);
            var text = Mix(C("#1A1C1F"), bg, 0.96f);
            return new Theme("custom", "自定义", bg, sidebar, Shade(bg, 0.7f), Shade(bg, -0.03f),
                text, Mix(text, bg, 0.62f), a, TextOn(a), Shade(bg, -0.075f), Mix(a, sidebar, 0.08f),
                Mix(a, sidebar, 0.11f), C("#DC2626"));
        }
    }

    /// <summary>1.2 以前的 20 套预设只剩底色和主题色，用来把老存档换成自定义配色，尽量保持原来的样子。</summary>
    static readonly Dictionary<string, (string Bg, string Accent)> Legacy = new()
    {
        ["nord"] = ("#2E3440", "#88C0D0"), ["dracula"] = ("#282A36", "#BD93F9"), ["catppuccin"] = ("#1E1E2E", "#CBA6F7"),
        ["gruvbox"] = ("#282828", "#FE8019"), ["tokyo"] = ("#1A1B26", "#7DCFFF"), ["onedark"] = ("#282C34", "#61AFEF"),
        ["solarized"] = ("#002B36", "#B58900"), ["github"] = ("#0D1117", "#3FB950"), ["monokai"] = ("#272822", "#F92672"),
        ["rosepine"] = ("#191724", "#EBBCBA"), ["sunny"] = ("#FFF4CC", "#FF6A00"), ["bubblegum"] = ("#FFE6F4", "#FF2E93"),
        ["lime"] = ("#F3FFE4", "#5BD100"), ["azure"] = ("#E5F3FF", "#0066FF"), ["violet"] = ("#F3E8FF", "#7C3AED"),
        ["coral"] = ("#FFE8E0", "#FF3B1F"), ["mango"] = ("#FFF1D6", "#FF8A00"), ["lagoon"] = ("#E7FFFB", "#00A99A"),
    };

    /// <summary>老存档的预设主题换成暗色/亮色 + 自定义颜色。改了返回 true。</summary>
    public static bool Migrate(Prefs p)
    {
        if (p.Theme is "dark" or "light") return false;
        if (Legacy.TryGetValue(p.Theme, out var old))
        {
            p.Base = old.Bg;
            p.Accent = old.Accent;
            p.Theme = Luma(C(old.Bg)) < 0.45f ? "dark" : "light";
        }
        else p.Theme = "dark";
        return true;
    }
}
