"""ダークテーマの配色とフォント。"""

import colorsys
import zlib
from tkinter import font as tkfont

# --- 配色 -----------------------------------------------------------------
BG = "#1e1e1e"            # ウィンドウ背景
CARD = "#282828"          # カード背景
BORDER = "#3a3a3a"
TEXT = "#ebebeb"
MUTED = "#9b9b9b"
FAINT = "#6a6a6a"

SEG_ACTIVE_BG = "#3c3c3c"
SEG_ACTIVE_FG = "#ffffff"
SEG_IDLE_FG = "#9b9b9b"

EMPTY = "#313131"         # 記録なしのマス
BAR_TRACK = "#323232"

# --- 形 -------------------------------------------------------------------
RADIUS = 8                # カードの角丸
CELL_RADIUS_RATIO = 0.24  # ヒートマップのマスの角丸(セルサイズに対する比)

CARD_HOVER = "#2f2f2f"
DELTA_UP = "#e0714a"      # 前の期間より増えた
DELTA_DOWN = "#4fb477"    # 減った

# --- アプリごとの色 ---------------------------------------------------------
# ホームタブでアプリを見分けやすくするための色相。彩度と明度は共通なので、
# どの色相でも「明るいほど使用時間が長い」の読み方は変わらない。
APP_HUES = [212, 145, 275, 28, 190, 330, 45, 255, 100, 12, 168, 300]

# 使用時間が多いほど「濃い」色になるように、段階が上がるごとに彩度を上げる。
# 明度も少しずつ上げているので、暗い背景に沈むことはない。
# 一番上を明るくしすぎると色が白っぽく抜けて薄く見えるため、明度は0.55で止める。
_LEVELS = (
    (0.26, 0.34),   # (明度, 彩度) わずかに使った日
    (0.36, 0.55),
    (0.46, 0.76),
    (0.55, 0.96),   # よく使った日 = 一番くっきり濃い
)

DEFAULT_HUE = 212


def _hex(hue: int, lightness: float, saturation: float) -> str:
    red, green, blue = colorsys.hls_to_rgb(hue / 360.0, lightness, saturation)
    return "#%02x%02x%02x" % (round(red * 255), round(green * 255), round(blue * 255))


def heat_ramp(hue: int) -> list:
    """指定した色相のヒートマップ配色(記録なし + 4段階)。"""
    return [EMPTY] + [_hex(hue, light, sat) for light, sat in _LEVELS]


HEAT = heat_ramp(DEFAULT_HUE)
BAR = HEAT[3]


def _hue_index(name: str) -> int:
    return zlib.crc32(name.encode("utf-8")) % len(APP_HUES)


def app_hue(name: str) -> int:
    """アプリ名から色相を決める。並び順が変わっても色が動かないようにハッシュを使う。"""
    return APP_HUES[_hue_index(name)]


def assign_hues(names) -> dict:
    """一覧全体を見て、色相がかぶらないように割り当てる。

    ハッシュだけで決めると別のアプリが同じ色になることがある。希望の色相が
    すでに使われていたら隣へずらす。色相の数を超えた分は重複を許す。
    """
    used = set()
    result = {}
    for name in names:
        start = _hue_index(name)
        for offset in range(len(APP_HUES)):
            index = (start + offset) % len(APP_HUES)
            if index not in used:
                used.add(index)
                result[name] = APP_HUES[index]
                break
        else:
            result[name] = APP_HUES[start]
    return result


def accent(hue: int) -> str:
    """カラーチップなどに使う代表色。ヒートマップの一番濃い段階に合わせる。"""
    return _hex(hue, *_LEVELS[-1])


def app_heat(name: str) -> list:
    """そのアプリ用のヒートマップ配色(記録なし + 4段階)。"""
    return heat_ramp(app_hue(name))


def app_accent(name: str) -> str:
    """一覧のカラーチップなどに使う、そのアプリの代表色。"""
    return accent(app_hue(name))


# --- フォント ---------------------------------------------------------------
# Claude CodeのUIと同じ見え方にするため Segoe UI を最優先にする。日本語の
# 字形はTkが自動で代替フォントに落とすので、和文込みでも破綻しない。
_FAMILIES = ["Segoe UI", "Yu Gothic UI", "Meiryo UI", "MS UI Gothic", "TkDefaultFont"]


class Fonts:
    """tkのroot生成後に呼び出して初期化する。"""

    def __init__(self):
        available = set(tkfont.families())
        family = next((f for f in _FAMILIES if f in available), _FAMILIES[-1])
        self.family = family
        self.tiny = (family, 8)
        self.small = (family, 9)
        self.body = (family, 10)
        self.label = (family, 9)
        self.value = (family, 19, "bold")
        self.value_sm = (family, 13, "bold")
        self.section = (family, 11, "bold")
        self.seg = (family, 9)
        self.tab = (family, 10)
