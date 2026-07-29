using Microsoft.UI.Xaml.Media;
using HeatTouls.Core;
using Windows.UI;

namespace HeatTouls;

/// <summary>
/// ダークテーマの配色とフォント。Python 版 theme.py の移植。
/// ヒートマップの配色そのものは Core.Palette が持ち、ここでは
/// WinUI の Color / Brush へ変換して使う。
/// </summary>
public static class Theme
{
    // --- 配色 ---------------------------------------------------------------
    public static readonly Color Bg = Hex(0x1e1e1e);          // ウィンドウ背景
    public static readonly Color Card = Hex(0x282828);        // カード背景
    public static readonly Color Border = Hex(0x3a3a3a);
    public static readonly Color Text = Hex(0xebebeb);
    public static readonly Color Muted = Hex(0x9b9b9b);
    public static readonly Color Faint = Hex(0x6a6a6a);

    public static readonly Color SegActiveBg = Hex(0x3c3c3c);
    public static readonly Color SegActiveFg = Hex(0xffffff);
    public static readonly Color SegIdleFg = Hex(0x9b9b9b);

    public static readonly Color BarTrack = Hex(0x323232);
    public static readonly Color CardHover = Hex(0x2f2f2f);
    public static readonly Color TooltipBg = Hex(0x0f0f0f);

    // --- 形 -----------------------------------------------------------------
    public const double Radius = 8;               // カードの角丸
    public const double CellRadiusRatio = 0.24;   // ヒートマップのマスの角丸(セルサイズに対する比)

    // --- ヒートマップの配色 ------------------------------------------------------
    public const int DefaultHue = Palette.DefaultHue;

    public static Color[] HeatRamp(int hue) =>
        Palette.HeatRamp(hue).Select(ToColor).ToArray();

    public static readonly Color[] Heat = HeatRamp(DefaultHue);
    public static readonly Color Bar = Heat[3];

    /// <summary>アプリ名から色相を決める。</summary>
    public static int AppHue(string name) => Palette.AppHues[Palette.HueIndex(name)];

    /// <summary>カラーチップなどに使う代表色。ヒートマップの一番濃い段階に合わせる。</summary>
    public static Color Accent(int hue) => ToColor(Palette.Accent(hue));

    public static Color AppAccent(string name) => Accent(AppHue(name));

    /// <summary>
    /// 一覧全体を見て、色相がかぶらないように割り当てる。
    /// ハッシュだけで決めると別のアプリが同じ色になることがある。希望の色相が
    /// すでに使われていたら隣へずらす。色相の数を超えた分は重複を許す。
    /// </summary>
    public static Dictionary<string, int> AssignHues(IEnumerable<string> names)
    {
        var hues = Palette.AppHues;
        var used = new HashSet<int>();
        var result = new Dictionary<string, int>();

        foreach (var name in names)
        {
            var start = Palette.HueIndex(name);
            var assigned = false;
            for (var offset = 0; offset < hues.Length; offset++)
            {
                var index = (start + offset) % hues.Length;
                if (used.Add(index))
                {
                    result[name] = hues[index];
                    assigned = true;
                    break;
                }
            }
            if (!assigned)
            {
                result[name] = hues[start];
            }
        }
        return result;
    }

    private static Color ToColor(Rgb rgb) => Color.FromArgb(255, rgb.R, rgb.G, rgb.B);

    private static Color Hex(uint rgb) =>
        Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    // --- フォント -------------------------------------------------------------
    // Segoe UI を最優先にし、和文は Yu Gothic UI 以降へ落とす。
    public static readonly FontFamily Family =
        new("Segoe UI, Yu Gothic UI, Meiryo UI, MS UI Gothic");

    // tkinter のフォント指定はポイント。WinUI は DIP なので 96dpi 基準で 4/3 倍する。
    private const double Pt = 4.0 / 3.0;

    public const double FontTiny = 8 * Pt;
    public const double FontSmall = 9 * Pt;
    public const double FontBody = 10 * Pt;
    public const double FontLabel = 9 * Pt;
    public const double FontValue = 19 * Pt;      // bold
    public const double FontValueSm = 13 * Pt;    // bold
    public const double FontSection = 11 * Pt;    // bold
    public const double FontSeg = 9 * Pt;

    // --- ブラシ (繰り返し使うもの) ------------------------------------------------
    public static readonly SolidColorBrush BgBrush = new(Bg);
    public static readonly SolidColorBrush TextBrush = new(Text);
    public static readonly SolidColorBrush MutedBrush = new(Muted);
    public static readonly SolidColorBrush FaintBrush = new(Faint);
    public static readonly SolidColorBrush CardBrush = new(Card);
    public static readonly SolidColorBrush BorderBrush = new(Border);
}
