using HeatTouls.Core;

namespace HeatTouls.IconGen;

/// <summary>
/// アプリアイコンの生成。ヒートマップを模した3x3のタイル。
/// Python 版 heattouls/icon.py の移植で、exe に埋め込む .ico を書き出す。
///
///     dotnet run --project tools/IconGen -- <出力先.ico>
/// </summary>
public static class Program
{
    /// <summary>各タイルの濃さ (Palette.Heat のindex)。</summary>
    private static readonly int[][] Pattern =
    [
        [1, 0, 2],
        [3, 2, 4],
        [2, 4, 3],
    ];

    private static readonly Rgb Background = new(25, 25, 25);

    private static readonly int[] Sizes = [16, 24, 32, 48, 64, 128, 256];

    /// <summary>細部を滑らかにするため、この倍率で描いてから縮小する。</summary>
    private const int Supersample = 4;

    public static int Main(string[] args)
    {
        var target = args.Length > 0
            ? args[0]
            : Path.Combine(AppContext.BaseDirectory, "HeatTouls.ico");

        var directory = Path.GetDirectoryName(Path.GetFullPath(target));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var images = Sizes.Select(Render).ToArray();
        WriteIco(target, Sizes, images);

        Console.WriteLine($"アイコンを生成しました: {Path.GetFullPath(target)}");
        return 0;
    }

    /// <summary>指定サイズの BGRA ピクセル列(上から下)を作る。</summary>
    private static byte[] Render(int size)
    {
        var big = size * Supersample;
        var canvas = new Rgb[big * big];
        Array.Fill(canvas, Background);

        // Python 版と同じ比率でタイルを置く
        var pad = Math.Max(2.0, Math.Round(size * 0.125)) * Supersample;
        var gap = Math.Max(1.0, Math.Round(size * 0.0625)) * Supersample;
        var cell = (big - pad * 2 - gap * 2) / 3.0;

        for (var row = 0; row < 3; row++)
        {
            for (var col = 0; col < 3; col++)
            {
                var color = Palette.Heat[Pattern[row][col]];
                var x0 = pad + col * (cell + gap);
                var y0 = pad + row * (cell + gap);
                FillRect(canvas, big, x0, y0, x0 + cell, y0 + cell, color);
            }
        }

        return Downscale(canvas, big, size);
    }

    private static void FillRect(Rgb[] canvas, int stride,
                                 double x0, double y0, double x1, double y1, Rgb color)
    {
        var left = Math.Max(0, (int)Math.Round(x0));
        var top = Math.Max(0, (int)Math.Round(y0));
        var right = Math.Min(stride - 1, (int)Math.Round(x1));
        var bottom = Math.Min(stride - 1, (int)Math.Round(y1));

        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                canvas[y * stride + x] = color;
            }
        }
    }

    /// <summary>面積平均で実寸へ縮める。倍率が整数なので単純平均でよい。</summary>
    private static byte[] Downscale(Rgb[] canvas, int big, int size)
    {
        var pixels = new byte[size * size * 4];
        var block = Supersample * Supersample;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                int r = 0, g = 0, b = 0;
                for (var sy = 0; sy < Supersample; sy++)
                {
                    for (var sx = 0; sx < Supersample; sx++)
                    {
                        var source = canvas[(y * Supersample + sy) * big + x * Supersample + sx];
                        r += source.R;
                        g += source.G;
                        b += source.B;
                    }
                }

                var offset = (y * size + x) * 4;
                pixels[offset + 0] = (byte)(b / block);   // BGRA 並び
                pixels[offset + 1] = (byte)(g / block);
                pixels[offset + 2] = (byte)(r / block);
                pixels[offset + 3] = 255;
            }
        }
        return pixels;
    }

    /// <summary>複数解像度を持つ .ico を書き出す。各画像は 32bpp の BMP として格納する。</summary>
    private static void WriteIco(string path, IReadOnlyList<int> sizes, IReadOnlyList<byte[]> images)
    {
        var blobs = new List<byte[]>(sizes.Count);
        for (var i = 0; i < sizes.Count; i++)
        {
            blobs.Add(BuildDib(sizes[i], images[i]));
        }

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        writer.Write((ushort)0);              // reserved
        writer.Write((ushort)1);              // type: icon
        writer.Write((ushort)sizes.Count);

        // ディレクトリの直後から画像が並ぶ
        var offset = 6 + 16 * sizes.Count;
        for (var i = 0; i < sizes.Count; i++)
        {
            var size = sizes[i];
            writer.Write((byte)(size >= 256 ? 0 : size));   // 256 は 0 で表す
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);            // パレット数(トゥルーカラーなので0)
            writer.Write((byte)0);            // reserved
            writer.Write((ushort)1);          // planes
            writer.Write((ushort)32);         // bit count
            writer.Write(blobs[i].Length);
            writer.Write(offset);
            offset += blobs[i].Length;
        }

        foreach (var blob in blobs)
        {
            writer.Write(blob);
        }
    }

    /// <summary>BITMAPINFOHEADER + ピクセル(下から上) + ANDマスク。</summary>
    private static byte[] BuildDib(int size, byte[] bgra)
    {
        var maskStride = (size + 31) / 32 * 4;   // 1bpp、4バイト境界に合わせる
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);

        writer.Write(40);                     // biSize
        writer.Write(size);                   // biWidth
        writer.Write(size * 2);               // biHeight (XOR + AND の合計)
        writer.Write((ushort)1);              // biPlanes
        writer.Write((ushort)32);             // biBitCount
        writer.Write(0);                      // biCompression = BI_RGB
        writer.Write(size * size * 4 + maskStride * size);
        writer.Write(0);                      // 解像度は指定しない
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        // DIB は下から上に並ぶ
        for (var y = size - 1; y >= 0; y--)
        {
            writer.Write(bgra, y * size * 4, size * 4);
        }

        // 32bpp のアルファを使うので、ANDマスクは全部 0(不透明)でよい
        writer.Write(new byte[maskStride * size]);

        writer.Flush();
        return buffer.ToArray();
    }
}
