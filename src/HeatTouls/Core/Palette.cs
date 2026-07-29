namespace HeatTouls.Core;

public readonly record struct Rgb(byte R, byte G, byte B);

/// <summary>
/// 配色の計算。UI フレームワークに依存しないので、アプリ本体と
/// アイコン生成ツール(tools/IconGen)の両方から同じ定義を使える。
/// </summary>
public static class Palette
{
    /// <summary>記録なしのマス。</summary>
    public static readonly Rgb Empty = new(0x31, 0x31, 0x31);

    // ホームタブでアプリを見分けやすくするための色相。彩度と明度は共通なので、
    // どの色相でも「明るいほど使用時間が長い」の読み方は変わらない。
    public static readonly int[] AppHues = [212, 145, 275, 28, 190, 330, 45, 255, 100, 12, 168, 300];

    // 使用時間が多いほど「濃い」色になるように、段階が上がるごとに彩度を上げる。
    // 明度も少しずつ上げているので、暗い背景に沈むことはない。
    // 一番上を明るくしすぎると色が白っぽく抜けて薄く見えるため、明度は0.55で止める。
    public static readonly (double Light, double Sat)[] Levels =
    [
        (0.26, 0.34),   // わずかに使った日
        (0.36, 0.55),
        (0.46, 0.76),
        (0.55, 0.96),   // よく使った日 = 一番くっきり濃い
    ];

    public const int DefaultHue = 212;

    /// <summary>指定した色相のヒートマップ配色(記録なし + 4段階)。</summary>
    public static Rgb[] HeatRamp(int hue)
    {
        var ramp = new Rgb[Levels.Length + 1];
        ramp[0] = Empty;
        for (var i = 0; i < Levels.Length; i++)
        {
            ramp[i + 1] = FromHls(hue, Levels[i].Light, Levels[i].Sat);
        }
        return ramp;
    }

    public static readonly Rgb[] Heat = HeatRamp(DefaultHue);

    /// <summary>カラーチップなどに使う代表色。ヒートマップの一番濃い段階に合わせる。</summary>
    public static Rgb Accent(int hue) => FromHls(hue, Levels[^1].Light, Levels[^1].Sat);

    /// <summary>アプリ名 → 色相の対応。並び順が変わっても色が動かないようハッシュを使う。</summary>
    public static int HueIndex(string name) =>
        (int)(Crc32(System.Text.Encoding.UTF8.GetBytes(name)) % (uint)AppHues.Length);

    /// <summary>Python の colorsys.hls_to_rgb と同じ変換。</summary>
    public static Rgb FromHls(int hue, double lightness, double saturation)
    {
        var h = hue / 360.0;
        if (saturation == 0)
        {
            var v = Byte(lightness);
            return new Rgb(v, v, v);
        }

        var m2 = lightness <= 0.5
            ? lightness * (1 + saturation)
            : lightness + saturation - lightness * saturation;
        var m1 = 2 * lightness - m2;

        return new Rgb(
            Channel(m1, m2, h + 1.0 / 3.0),
            Channel(m1, m2, h),
            Channel(m1, m2, h - 1.0 / 3.0));
    }

    private static byte Channel(double m1, double m2, double hue)
    {
        hue -= Math.Floor(hue);   // Python の % 1.0 と同じ(負の値も正へ回る)
        double value;
        if (hue < 1.0 / 6.0)
        {
            value = m1 + (m2 - m1) * hue * 6.0;
        }
        else if (hue < 0.5)
        {
            value = m2;
        }
        else if (hue < 2.0 / 3.0)
        {
            value = m1 + (m2 - m1) * (2.0 / 3.0 - hue) * 6.0;
        }
        else
        {
            value = m1;
        }
        return Byte(value);
    }

    private static byte Byte(double value) =>
        (byte)Math.Round(value * 255, MidpointRounding.ToEven);

    /// <summary>zlib.crc32 と同じ標準 CRC-32。Python 版と色の割り当てを揃えるため。</summary>
    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(crc & 1));
            }
        }
        return crc ^ 0xFFFFFFFFu;
    }
}
