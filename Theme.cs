namespace UuTime;

/// <summary>界面配色，和 chatuutts 共用同一套。</summary>
internal sealed record Theme(
    string Id, string Name,
    Color Bg, Color Sidebar, Color Card, Color Input,
    Color Text, Color Muted, Color Accent, Color AccentText,
    Color Border, Color Hover, Color Selected, Color Danger)
{
    public bool IsDark => Bg.GetBrightness() < 0.5f;

    /// <summary>后台时间用的浅色强调：强调色按比例混进卡片底色。</summary>
    public Color AccentSoft => Mix(Accent, Card, 0.38f);

    public static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R * t + b.R * (1 - t)), (int)(a.G * t + b.G * (1 - t)), (int)(a.B * t + b.B * (1 - t)));

    static Theme Make(string id, string name, string bg, string sidebar, string card, string input, string text,
        string muted, string accent, string accentText, string border, string hover, string selected, string danger) =>
        new(id, name, C(bg), C(sidebar), C(card), C(input), C(text), C(muted), C(accent), C(accentText),
            C(border), C(hover), C(selected), C(danger));

    static Color C(string hex) => ColorTranslator.FromHtml(hex);

    public static readonly Theme[] All =
    [
        Make("dark", "玄青", "#16181D", "#12141A", "#1C1F26", "#101216", "#F4F4F5", "#9CA3AF", "#2DD4BF", "#042F2E", "#2A2E36", "#22262E", "#16302E", "#F07178"),
        Make("light", "月白", "#F6F7F9", "#FFFFFF", "#FFFFFF", "#EEF1F4", "#1A1C1F", "#667085", "#0F766E", "#FFFFFF", "#E4E7EC", "#E7F6F4", "#E6F4F1", "#DC2626"),
        Make("nord", "霁青", "#2E3440", "#272C36", "#3B4252", "#242933", "#ECEFF4", "#D8DEE9", "#88C0D0", "#2E3440", "#434C5E", "#3B4252", "#3B4F5C", "#BF616A"),
        Make("dracula", "紫檀", "#282A36", "#21222C", "#343746", "#1E1F29", "#F8F8F2", "#BFBFBF", "#BD93F9", "#282A36", "#44475A", "#343746", "#3A3450", "#FF5555"),
        Make("catppuccin", "丁香", "#1E1E2E", "#181825", "#313244", "#11111B", "#CDD6F4", "#A6ADC8", "#CBA6F7", "#1E1E2E", "#45475A", "#313244", "#3B3454", "#F38BA8"),
        Make("gruvbox", "赭石", "#282828", "#1D2021", "#3C3836", "#1D2021", "#EBDBB2", "#A89984", "#FE8019", "#1D2021", "#504945", "#3C3836", "#4A3424", "#FB4934"),
        Make("tokyo", "靛蓝", "#1A1B26", "#16161E", "#24283B", "#13131A", "#C0CAF5", "#9AA5CE", "#7DCFFF", "#1A1B26", "#292E42", "#24283B", "#1A3344", "#F7768E"),
        Make("onedark", "鸦青", "#282C34", "#21252B", "#2C313A", "#1B1E24", "#ABB2BF", "#7F848E", "#61AFEF", "#1B212B", "#3E4451", "#2C313A", "#243446", "#E06C75"),
        Make("solarized", "秘色", "#002B36", "#073642", "#073642", "#00212B", "#EEE8D5", "#93A1A1", "#B58900", "#002B36", "#586E75", "#0A4050", "#12424A", "#DC322F"),
        Make("github", "竹青", "#0D1117", "#010409", "#161B22", "#0D1117", "#E6EDF3", "#8B949E", "#3FB950", "#052E16", "#30363D", "#161B22", "#0F2A1A", "#F85149"),
        Make("monokai", "胭脂", "#272822", "#1E1F1C", "#3E3D32", "#1E1F1C", "#F8F8F2", "#75715E", "#F92672", "#FFFFFF", "#49483E", "#3E3D32", "#4A2434", "#F92672"),
        Make("rosepine", "海棠", "#191724", "#1F1D2E", "#26233A", "#13111C", "#E0DEF4", "#908CAA", "#EBBCBA", "#191724", "#403D52", "#26233A", "#3A2E38", "#EB6F92"),
        Make("sunny", "杏黄", "#FFF4CC", "#FFE58A", "#FFFBEA", "#FFF0B3", "#3D2A00", "#8A6414", "#FF6A00", "#FFFFFF", "#F5D56A", "#FFE9A8", "#FFD59A", "#E11D48"),
        Make("bubblegum", "桃红", "#FFE6F4", "#FFC6E4", "#FFF5FB", "#FFD4EC", "#4A1234", "#A24B78", "#FF2E93", "#FFFFFF", "#F5B3D6", "#FFD8EE", "#FFC2E4", "#BE123C"),
        Make("lime", "柳绿", "#F3FFE4", "#E2F9C4", "#F9FFEF", "#DFF5B8", "#1C3A08", "#5A7A32", "#5BD100", "#143000", "#C8E89A", "#E7FBCF", "#D4F5A8", "#E11D48"),
        Make("azure", "天蓝", "#E5F3FF", "#C9E6FF", "#F4FAFF", "#D4ECFF", "#062544", "#3A6A94", "#0066FF", "#FFFFFF", "#B5D7F5", "#D7EEFF", "#C4E2FF", "#E11D48"),
        Make("violet", "雪青", "#F3E8FF", "#E4CCFF", "#FBF7FF", "#EDD9FF", "#2A0848", "#7A4E9A", "#7C3AED", "#FFFFFF", "#D4B4F5", "#EAD9FF", "#E0C8FF", "#E11D48"),
        Make("coral", "朱红", "#FFE8E0", "#FFD0C2", "#FFF6F2", "#FFDDD2", "#4A160C", "#A85A48", "#FF3B1F", "#FFFFFF", "#F5C2B4", "#FFDDD4", "#FFD0C4", "#9F1239"),
        Make("mango", "橘黄", "#FFF1D6", "#FFE0A8", "#FFF8EC", "#FFE8C2", "#3F2404", "#9A6230", "#FF8A00", "#3F2404", "#F6D39A", "#FFE7C0", "#FFD9A0", "#E11D48"),
        Make("lagoon", "缥色", "#E7FFFB", "#C8F8F1", "#F4FFFD", "#D2F6F0", "#063832", "#3D7A70", "#00A99A", "#FFFFFF", "#B4E8E0", "#D6F8F2", "#C2F0E8", "#E11D48"),
    ];

    public static Theme ById(string? id) => All.FirstOrDefault(t => t.Id == id) ?? All[0];
}
