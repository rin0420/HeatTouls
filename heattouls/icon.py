"""アプリアイコンの生成。ヒートマップを模した3x3のタイル。

トレイアイコン(実行時)とexeのアイコン(ビルド時)の両方でこれを使う。
"""

from . import theme

# 各タイルの濃さ(theme.HEATのindex)
PATTERN = [
    [1, 0, 2],
    [3, 2, 4],
    [2, 4, 3],
]
BACKGROUND = (25, 25, 25, 255)


def make_image(size: int = 64):
    """PIL.Image を返す。Pillowが必要。"""
    from PIL import Image, ImageDraw

    image = Image.new("RGBA", (size, size), BACKGROUND)
    draw = ImageDraw.Draw(image)

    pad = max(2, round(size * 0.125))
    gap = max(1, round(size * 0.0625))
    cell = (size - pad * 2 - gap * 2) / 3
    for row, levels in enumerate(PATTERN):
        for col, level in enumerate(levels):
            x = pad + col * (cell + gap)
            y = pad + row * (cell + gap)
            draw.rectangle([x, y, x + cell, y + cell], fill=theme.HEAT[level])
    return image


def write_ico(path, sizes=(16, 24, 32, 48, 64, 128, 256)) -> str:
    """Windowsのexe/ショートカット用に、複数解像度を持つ.icoを書き出す。"""
    base = make_image(max(sizes))
    base.save(str(path), format="ICO", sizes=[(s, s) for s in sizes])
    return str(path)
