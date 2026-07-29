"""角丸・アンチエイリアス付きの描画ヘルパ。

tkinterのCanvasは角丸もアンチエイリアスも扱えないため、Pillowで画像として
描いてからPhotoImageにして貼り付ける。3倍で描いて縮小することで縁を滑らかにする。

Pillowが無い環境では各関数がNoneを返し、呼び出し側は素のrectangleへ退避する。
"""

# 2倍で描いて縮める。3倍でも見た目はほとんど変わらないのに、描画面積が
# 2.25倍になってウィンドウのリサイズが目に見えて重くなる。
SUPERSAMPLE = 2

# カード背景やバーは同じ寸法・同じ色の組み合わせが何度も要求される。
# PhotoImageの生成はそれなりに重いので、作ったものを使い回す。
_CACHE = {}
_CACHE_LIMIT = 512

try:
    from PIL import Image, ImageDraw, ImageTk

    AVAILABLE = True
except ImportError:
    AVAILABLE = False


def _downscale(image, width: int, height: int):
    """3倍で描いた画像を実寸へ縮める。

    倍率がちょうど整数なので、BOX(面積平均)が最も速く、かつ結果は
    スーパーサンプリングの理想形と一致する。LANCZOSより大幅に速い。
    """
    return image.resize((width, height), Image.BOX)


def clear_cache() -> None:
    _CACHE.clear()


def rounded_rect(width: int, height: int, radius: float, fill: str,
                 outline: str | None = None, border: int = 1):
    """角丸の矩形を描いたPhotoImageを返す。四隅は透過するので下地が透ける。"""
    if not AVAILABLE or width <= 0 or height <= 0:
        return None

    key = ("rect", width, height, round(radius, 2), fill, outline, border)
    cached = _CACHE.get(key)
    if cached is not None:
        return cached

    s = SUPERSAMPLE
    image = Image.new("RGBA", (width * s, height * s), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle(
        [0, 0, width * s - 1, height * s - 1],
        radius=max(0.0, radius) * s,
        fill=fill,
        outline=outline,
        width=border * s if outline else 0,
    )
    photo = ImageTk.PhotoImage(_downscale(image, width, height))

    if len(_CACHE) >= _CACHE_LIMIT:
        _CACHE.clear()
    _CACHE[key] = photo
    return photo


def heatmap(colors, rows: int, cell: float, gap: float, radius: float):
    """ヒートマップのマス目を1枚の画像にまとめて描く。

    colorsは列優先(上から下へ埋めて次の列)の並び。Noneのマスは描かない。

    マスは大きさも角丸も全部同じで、違うのは色だけ。なので角丸を1マスずつ
    描かず、色ごとにタイルを1枚作って貼り付ける。数百マスあると、この差が
    そのままウィンドウのリサイズの重さになる。
    """
    if not AVAILABLE or not colors or rows <= 0 or cell <= 0:
        return None
    cols = (len(colors) + rows - 1) // rows
    step = cell + gap
    width = max(1, round(cols * step - gap))
    height = max(1, round(rows * step - gap))

    s = SUPERSAMPLE
    side = max(1, round(cell * s))
    tiles = {}
    for color in set(colors):
        if color is None:
            continue
        tile = Image.new("RGBA", (side, side), (0, 0, 0, 0))
        ImageDraw.Draw(tile).rounded_rectangle(
            [0, 0, side - 1, side - 1], radius=radius * s, fill=color
        )
        tiles[color] = tile

    image = Image.new("RGBA", (width * s, height * s), (0, 0, 0, 0))
    for index, color in enumerate(colors):
        if color is None:
            continue
        tile = tiles[color]
        x = round((index // rows) * step * s)
        y = round((index % rows) * step * s)
        image.paste(tile, (x, y), tile)
    return ImageTk.PhotoImage(_downscale(image, width, height))


def pill(width: int, height: int, fill: str):
    """セグメントコントロール用の、完全に丸い両端を持つ矩形。"""
    return rounded_rect(width, height, height / 2, fill)
