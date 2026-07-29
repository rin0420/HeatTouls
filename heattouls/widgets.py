"""ダッシュボードを構成するtkinterウィジェット群。

角丸やアンチエイリアスが要るものは render.py 経由でPillowに描かせ、
描いた画像をCanvasへ貼る。Pillowが無い場合は素の矩形へ退避する。
"""

import datetime as dt
import tkinter as tk
from tkinter import font as tkfont

from . import fmt, render, theme

DAY_FMT = "%Y-%m-%d"
WEEKDAY_JA = ("月", "火", "水", "木", "金", "土", "日")   # date.weekday() の並び


def week_row(day: dt.date) -> int:
    """日曜を先頭(0)としたときの行番号。date.weekday()は月曜が0なのでずらす。"""
    return (day.weekday() + 1) % 7

# ウィンドウのリサイズ中は<Configure>が連続で飛んでくる。短い間隔で描き直すと
# 引っかかるので、手が止まってからまとめて1回だけ描く。
RESIZE_DEBOUNCE_MS = 200

# tkfont.Font()の生成はTkとのやり取りが挟まって重い。文字幅を測るためだけに
# 描画のたびに作ると、ヒートマップを何個も並べた画面で如実に遅くなる。
_FONT_CACHE = {}


def measure_font(family: str, size: int, weight: str = "normal"):
    """文字幅の計測用フォント。同じ組み合わせは使い回す。"""
    key = (family, size, weight)
    font = _FONT_CACHE.get(key)
    if font is None:
        font = _FONT_CACHE[key] = tkfont.Font(family=family, size=size, weight=weight)
    return font


class Tooltip:
    """マウス位置に出す小さな黒いラベル。"""

    def __init__(self, master, fonts):
        self._master = master
        self._fonts = fonts
        self._win = None
        self._label = None

    def show(self, text: str, x: int, y: int) -> None:
        if self._win is None:
            self._win = tk.Toplevel(self._master)
            self._win.overrideredirect(True)
            self._win.attributes("-topmost", True)
            self._label = tk.Label(
                self._win, bg="#0f0f0f", fg=theme.TEXT, font=self._fonts.small,
                padx=8, pady=4, bd=0,
            )
            self._label.pack()
        self._label.configure(text=text)
        self._win.geometry(f"+{x + 14}+{y + 18}")
        self._win.deiconify()

    def hide(self) -> None:
        if self._win is not None:
            self._win.withdraw()


class RoundedCanvas(tk.Canvas):
    """角丸の背景を持つCanvas。中身は継承先が draw() で描く。"""

    def __init__(self, master, height=None, bg=theme.BG, card=theme.CARD,
                 border=theme.BORDER, radius=theme.RADIUS, **kwargs):
        options = {"bg": bg, "highlightthickness": 0, "bd": 0}
        if height is not None:
            options["height"] = height
        options.update(kwargs)
        super().__init__(master, **options)
        self._card = card
        self._border = border
        self._radius = radius
        self._bg_image = None
        self._pending = None
        self.bind("<Configure>", self._on_configure)

    def _on_configure(self, _event=None) -> None:
        # 連続したリサイズで何度も再描画しないよう、手が止まるまで待つ
        if self._pending is not None:
            self.after_cancel(self._pending)
        self._pending = self.after(RESIZE_DEBOUNCE_MS, self._render)

    def _render(self) -> None:
        self._pending = None
        width, height = self.winfo_width(), self.winfo_height()
        if width <= 1 or height <= 1:
            return
        self.delete("all")
        self._bg_image = render.rounded_rect(
            width, height, self._radius, self._card, self._border
        )
        if self._bg_image is not None:
            self.create_image(0, 0, image=self._bg_image, anchor="nw")
        else:
            self.create_rectangle(
                0, 0, width - 1, height - 1, fill=self._card, outline=self._border
            )
        self.draw(width, height)

    def refresh(self) -> None:
        self._render()

    def draw(self, width: int, height: int) -> None:
        """継承先が中身を描く。"""


class StatCard(RoundedCanvas):
    """『ラベル + 大きな数値』のカード1枚。"""

    PAD = 13

    def __init__(self, master, fonts, label: str, value: str = "—", small: bool = False):
        super().__init__(master, height=74)
        self._fonts = fonts
        self._label = label
        self._value = value
        self._font = fonts.value_sm if small else fonts.value

    def set_value(self, value: str) -> None:
        if value == self._value:
            return
        self._value = value
        self.refresh()

    def draw(self, width: int, height: int) -> None:
        self.create_text(
            self.PAD, self.PAD - 2, text=self._label, anchor="nw",
            fill=theme.MUTED, font=self._fonts.label,
        )
        self.create_text(
            self.PAD, height - self.PAD + 2, text=self._value, anchor="sw",
            fill=theme.TEXT, font=self._font,
        )


class SegmentedControl(tk.Canvas):
    """『すべて / 30日 / 7日』のような切り替えボタン群。

    選択中の項目だけ角丸の下地を敷く。1枚のCanvasに直接描いている。
    """

    HEIGHT = 26

    def __init__(self, master, fonts, options, on_change, initial=None, padx=12, gap=3):
        super().__init__(master, bg=theme.BG, highlightthickness=0, bd=0, height=self.HEIGHT)
        self._font = tkfont.Font(family=fonts.family, size=fonts.seg[1])
        self._on_change = on_change
        self._items = list(options)
        self._padx = padx
        self._gap = gap
        self._pill = None
        self.value = initial if initial is not None else self._items[0][0]

        self._widths = [self._font.measure(label) + padx * 2 for _key, label in self._items]
        self.configure(width=sum(self._widths) + gap * (len(self._widths) - 1))
        self.bind("<Button-1>", self._on_click)
        self.bind("<Configure>", lambda _e: self._draw())

    def _bounds(self):
        x = 0
        for (key, label), width in zip(self._items, self._widths):
            yield key, label, x, x + width
            x += width + self._gap

    def select(self, key: str, notify: bool = True) -> None:
        if key == self.value:
            return
        self.value = key
        self._draw()
        if notify:
            self._on_change(key)

    def _on_click(self, event) -> None:
        for key, _label, x0, x1 in self._bounds():
            if x0 <= event.x <= x1:
                self.select(key)
                return

    def _draw(self) -> None:
        self.delete("all")
        height = self.winfo_height() or self.HEIGHT
        for key, label, x0, x1 in self._bounds():
            active = key == self.value
            if active:
                self._pill = render.pill(int(x1 - x0), height, theme.SEG_ACTIVE_BG)
                if self._pill is not None:
                    self.create_image(x0, 0, image=self._pill, anchor="nw")
                else:
                    self.create_rectangle(
                        x0, 0, x1, height, fill=theme.SEG_ACTIVE_BG, outline=""
                    )
            self.create_text(
                (x0 + x1) / 2, height / 2, text=label, font=self._font,
                fill=theme.SEG_ACTIVE_FG if active else theme.SEG_IDLE_FG,
            )


class Heatmap(tk.Canvas):
    """日別の稼働時間を、隙間なく詰めたマス目で表すヒートマップ。

    GitHubのように曜日で行を固定すると、先頭と末尾に半端な空きができて
    「どの向きに日付が進むのか」が分かりにくい。ここでは曜日を無視して
    古い順に上から下へ埋め、埋まったら次の列へ進む。左上が最も古く、
    右下が今日。全部のマスが実在する日付なので端から端まで詰まる。
    """

    GAP = 3
    MIN_CELL = 8
    MAX_CELL = 17        # 幅いっぱいに詰めるとき
    MAX_CELL_FIXED = 34  # 表示日数が決まっているとき(マスが少ないので大きめに見せる)

    AXIS_H = 15          # 上に置く月ラベルの高さ
    LEGEND_H = 17        # 下に置く凡例の高さ
    WEEKDAY_W = 28       # 左に置く曜日ラベルの幅
    # 上から 日月火水木金土。全部書くと詰まるので月・水・金だけ出す。
    WEEKDAY_LABELS = {1: "月", 3: "水", 5: "金"}

    def __init__(self, master, fonts, rows: int = 7, bg: str = theme.BG,
                 target_cell: int = 15, tooltips: bool = True,
                 axis: bool = False, legend: bool = False,
                 defer_offscreen: bool = False, weekdays: bool = False):
        super().__init__(master, bg=bg, highlightthickness=0, bd=0, height=rows * 18)
        self._fonts = fonts
        self._rows = rows
        self._target = target_cell
        self._axis = axis        # 上に月の区切りを出すか
        self._legend = legend    # 下に「少〜多」の凡例を出すか
        # 左に曜日を出すか。7行のときだけ「行=曜日」が成り立つので、その時だけ有効。
        self._weekdays = weekdays
        self._daily = {}
        self._cols = None
        self._palette = theme.HEAT
        self._end = dt.date.today()
        self._image = None
        self._geometry = None   # (cell, step, count, start, offset, rows, top)
        self._pending = None
        self._signature = None   # 同じ内容の描き直しを避けるための指紋
        # 画像1枚をTkへ渡すコストが大きいので、一覧の中でスクロールして
        # 画面の外にある行は描かずに後回しにする。
        self._defer_offscreen = defer_offscreen
        self._dirty = False

        self._tooltip = Tooltip(master, fonts) if tooltips else None
        self.bind("<Configure>", self._on_configure)
        if tooltips:
            self.bind("<Motion>", self._on_motion)
            self.bind("<Leave>", lambda _e: self._tooltip.hide())

    def set_data(self, daily: dict, rows: int | None = None, cols: int | None = None,
                 end: dt.date | None = None, palette: list | None = None) -> None:
        """colsを省くと幅いっぱいに詰める(列数は幅から決まる)。

        期間を指定して表示する場合は rows と cols を明示する。行×列がそのまま
        表示日数になるので、7日なら1x7、30日なら10x3 のように割り切れる組にする。
        paletteを渡すとアプリごとの色で塗れる(theme.app_heat)。
        """
        self._daily = daily
        if rows:
            self._rows = rows
        self._cols = cols
        self._palette = palette or theme.HEAT
        self._end = end or dt.date.today()
        self._render()

    def _on_configure(self, _event=None) -> None:
        if self._pending is not None:
            self.after_cancel(self._pending)
        self._pending = self.after(RESIZE_DEBOUNCE_MS, self._render)

    def _level(self, seconds: float, peak: float) -> int:
        if seconds <= 0 or peak <= 0:
            return 0
        ratio = seconds / peak
        if ratio >= 0.75:
            return 4
        if ratio >= 0.5:
            return 3
        if ratio >= 0.25:
            return 2
        return 1

    def _on_screen(self) -> bool:
        """ウィンドウの表示範囲に少しでも入っているか。"""
        if not self.winfo_ismapped():
            return False
        top = self.winfo_toplevel()
        y = self.winfo_rooty() - top.winfo_rooty()
        return y + self.winfo_height() > 0 and y < top.winfo_height()

    def render_if_needed(self) -> None:
        """後回しにしていた描画を、画面に入ってきたところで済ませる。"""
        if self._dirty:
            self._render()

    def _render(self) -> None:
        self._pending = None
        width = self.winfo_width()
        if width <= 1:
            return

        if self._defer_offscreen and not self._on_screen():
            self._dirty = True
            return
        self._dirty = False

        # 曜日ラベルは1列が7日ぶん = 1行が1曜日、のときだけ意味を持つ
        show_weekdays = self._weekdays and self._rows == 7
        label_w = self.WEEKDAY_W if show_weekdays else 0
        avail = max(1, width - label_w)

        if self._cols:
            # 表示日数が決まっている場合。マスが大きくなりすぎないよう頭打ちにし、
            # 幅が余ったら中央に寄せる。
            cols = self._cols
            cell = min(self.MAX_CELL_FIXED, (avail - self.GAP * (cols - 1)) / cols)
        else:
            # 幅いっぱいに詰める場合。目標サイズから列数を決め、そのあとマスを
            # 伸縮させて端から端まできっちり収める。
            cols = max(1, round((avail + self.GAP) / (self._target + self.GAP)))
            cell = (avail - self.GAP * (cols - 1)) / cols
            if cell < self.MIN_CELL and cols > 1:
                cols = max(1, int((avail + self.GAP) // (self.MIN_CELL + self.GAP)))
                cell = (avail - self.GAP * (cols - 1)) / cols
        cell = max(1.0, cell)
        step = cell + self.GAP
        offset = label_w + max(0.0, (avail - (cols * step - self.GAP)) / 2)

        # 行数は固定。高さに合わせて行を増やすと、記録のある期間よりずっと長い
        # 範囲を描くことになり、灰色のマスばかりが増えてしまう。
        rows = self._rows
        top = self.AXIS_H if self._axis else 0
        grid_h = rows * step - self.GAP
        self.configure(height=round(top + grid_h + (self.LEGEND_H if self._legend else 0)))

        count = cols * rows
        end = self._end
        if show_weekdays:
            # 右端を今週の土曜まで伸ばす。マス数が7の倍数になるので、先頭が
            # 必ず日曜になり、全部のマスが埋まったまま「行=曜日」が揃う。
            end = end + dt.timedelta(days=6 - week_row(end))
        start = end - dt.timedelta(days=count - 1)
        peak = max(self._daily.values()) if self._daily else 0.0

        colors = []
        for index in range(count):
            day = start + dt.timedelta(days=index)
            seconds = self._daily.get(day.strftime(DAY_FMT), 0.0)
            colors.append(self._palette[self._level(seconds, peak)])

        self._geometry = (cell, step, count, start, offset, rows, top)

        # 同じ絵を描き直さない。タブ切り替えやリサイズのたびに再生成すると重い。
        signature = (tuple(colors), round(cell, 2), rows, round(offset, 1), top,
                     show_weekdays)
        if signature == self._signature:
            return
        self._signature = signature

        self.delete("all")
        self._image = render.heatmap(
            colors, rows, cell, self.GAP, cell * theme.CELL_RADIUS_RATIO
        )
        if self._image is not None:
            self.create_image(offset, top, image=self._image, anchor="nw")
        else:
            for index, color in enumerate(colors):   # Pillowが無い場合
                x = offset + (index // rows) * step
                y = top + (index % rows) * step
                self.create_rectangle(x, y, x + cell, y + cell, fill=color, outline="")

        if show_weekdays:
            self._draw_weekdays(rows, step, cell, top)
        if self._axis:
            self._draw_axis(cols, rows, step, offset, start)
        if self._legend:
            self._draw_legend(width, top + grid_h)

    def _draw_weekdays(self, rows: int, step: float, cell: float, top: float) -> None:
        """左端に曜日を書く。全部書くと詰まるので月・水・金だけ。"""
        for row, label in self.WEEKDAY_LABELS.items():
            if row >= rows:
                continue
            self.create_text(
                self.WEEKDAY_W - 8, top + row * step + cell / 2,
                text=label, anchor="e", fill=theme.MUTED, font=self._fonts.tiny,
            )

    def _draw_axis(self, cols: int, rows: int, step: float, offset: float,
                   start: dt.date) -> None:
        """列の上に月の変わり目を書く。どのあたりがいつ頃かの目印。

        1列が7日ぶんなので、その列で月が変わったら列の左端に月名を置く。
        隣のラベルと重なる場合は飛ばす。
        """
        label_font = self._fonts.tiny
        measure = measure_font(self._fonts.family, label_font[1])
        # 先頭の列はたいてい月の途中から始まる。ここにラベルを置くと次の月の
        # ラベルと重なって潰れるので、月の記録だけして描かない。
        last_month = (start + dt.timedelta(days=0)).month
        last_x = -999.0
        for col in range(1, cols):
            day = start + dt.timedelta(days=col * rows)
            if day.month == last_month:
                continue
            last_month = day.month
            x = offset + col * step
            text = f"{day.month}月"
            if x - last_x < measure.measure(text) + 8:
                continue
            last_x = x
            self.create_text(
                x, self.AXIS_H / 2, text=text, anchor="w",
                fill=theme.MUTED, font=label_font,
            )

    def _draw_legend(self, width: int, top: float) -> None:
        """右下に「少 ▪▪▪▪ 多」を置く。色の向きを迷わせないため。"""
        size = 9
        gap = 3
        y = top + self.LEGEND_H / 2
        x = width
        self.create_text(x, y, text="多", anchor="e", fill=theme.FAINT,
                         font=self._fonts.tiny)
        x -= 16
        for color in reversed(self._palette):
            self.create_rectangle(x - size, y - size / 2, x, y + size / 2,
                                  fill=color, outline="")
            x -= size + gap
        self.create_text(x - 3, y, text="少", anchor="e", fill=theme.FAINT,
                         font=self._fonts.tiny)

    def _on_motion(self, event) -> None:
        if not self._geometry:
            return
        cell, step, count, start, offset, rows = self._geometry
        local_x = event.x - offset
        col, row = int(local_x // step), int(event.y // step)
        # マスの間の隙間や、グリッドの外に乗っている時は何も出さない
        if local_x < 0 or local_x % step > cell or event.y % step > cell or row >= rows:
            self._tooltip.hide()
            return
        index = col * rows + row
        if not 0 <= index < count:
            self._tooltip.hide()
            return
        day = start + dt.timedelta(days=index)
        if day > dt.date.today():
            # 週の区切りを揃えるため今週の残りまで描いている。まだ来ていない日。
            self._tooltip.hide()
            return
        seconds = self._daily.get(day.strftime(DAY_FMT), 0.0)
        text = f"{day.strftime('%Y/%m/%d')}（{WEEKDAY_JA[day.weekday()]}） · " + (
            fmt.duration_long(seconds) if seconds > 0 else "記録なし"
        )
        self._tooltip.show(text, event.x_root, event.y_root)


class ScrollFrame(tk.Frame):
    """縦スクロールする入れ物。内容が収まる時はスクロールバーを隠す。"""

    BAR_WIDTH = 10
    BAR_GAP = 22     # 中身とバーの間隔。左右の余白としてもこの幅を使う

    def __init__(self, master, bg=theme.BG):
        super().__init__(master, bg=bg)
        self._canvas = tk.Canvas(self, bg=bg, highlightthickness=0, bd=0)
        # 既定のスクロールバーは明るい灰色で、暗い画面から浮いてしまう。
        # trough(溝)を背景と同じ色にし、つまみだけ少し明るくする。
        self._scroll = tk.Scrollbar(
            self, orient="vertical", command=self._canvas.yview,
            width=self.BAR_WIDTH, bd=0, relief="flat", highlightthickness=0,
            elementborderwidth=0, troughcolor=bg,
            bg=theme.BORDER, activebackground=theme.MUTED,
        )
        # バーが出ていないときも同じ幅を空けておく。出入りのたびに中身の幅が
        # 変わると、左右の余白がずれるうえヒートマップが描き直しになる。
        gutter = self.BAR_WIDTH + self.BAR_GAP
        self._spacer = tk.Frame(self, bg=bg, width=gutter)
        # 右はバーのぶんだけ空くので、左にも同じ幅を取って余白を揃える。
        self._left_pad = tk.Frame(self, bg=bg, width=gutter)
        self.body = tk.Frame(self._canvas, bg=bg)

        self._window = self._canvas.create_window((0, 0), window=self.body, anchor="nw")
        self._canvas.configure(yscrollcommand=self._scroll.set)
        # Canvasは expand=True で余白を全部取るので、先に右側の幅を確保してから
        # Canvasを詰める。あとから pack すると場所が残らず中身に重なってしまう。
        self._left_pad.pack(side="left", fill="y")
        self._spacer.pack(side="right", fill="y")
        self._canvas.pack(side="left", fill="both", expand=True)
        self._shown = False

        self.body.bind("<Configure>", self._sync)
        self._canvas.bind("<Configure>", self._on_canvas_configure)
        self._canvas.bind("<Enter>", lambda _e: self._canvas.bind_all("<MouseWheel>", self._wheel))
        self._canvas.bind("<Leave>", lambda _e: self._canvas.unbind_all("<MouseWheel>"))

    def _on_canvas_configure(self, event) -> None:
        self._canvas.itemconfigure(self._window, width=event.width)
        self._sync()

    def _sync(self, _event=None) -> None:
        self._canvas.configure(scrollregion=self._canvas.bbox("all"))
        overflow = self.body.winfo_reqheight() > self._canvas.winfo_height()
        if overflow and not self._shown:
            self._spacer.pack_forget()
            self._scroll.pack(
                side="right", fill="y", padx=(self.BAR_GAP, 0), before=self._canvas
            )
            self._shown = True
        elif not overflow and self._shown:
            self._scroll.pack_forget()
            self._spacer.pack(side="right", fill="y", before=self._canvas)
            self._shown = False

    def _wheel(self, event) -> None:
        self._canvas.yview_scroll(int(-event.delta / 120), "units")
        self.wake_visible()

    def wake_visible(self) -> None:
        """画面に入ってきた行の、後回しにしていた描画を済ませる。"""
        self.after_idle(self._wake)

    def _wake(self) -> None:
        for row in self.body.winfo_children():
            for child in (row,) + tuple(row.winfo_children()):
                if isinstance(child, Heatmap):
                    child.render_if_needed()

    def clear(self) -> None:
        for child in self.body.winfo_children():
            child.destroy()

    def to_top(self) -> None:
        self._canvas.yview_moveto(0)


class AppHeatmapRow(tk.Frame):
    """ホームタブの1行: アプリ名 + 合計時間 + そのアプリのヒートマップ。"""

    def __init__(self, master, fonts, app: dict, end: dt.date,
                 rows: int = 4, cols: int | None = None,
                 hue: int | None = None, on_click=None):
        super().__init__(master, bg=theme.BG)
        if hue is None:
            hue = theme.app_hue(app["name"])
        accent = theme.accent(hue)

        header = tk.Frame(self, bg=theme.BG)
        header.pack(fill="x", pady=(0, 6))
        # アプリの色を示す丸いマーク。ヒートマップの色と対応している。
        chip = tk.Canvas(header, bg=theme.BG, width=10, height=10,
                         highlightthickness=0, bd=0)
        chip.pack(side="left", padx=(0, 7))
        self._chip_image = render.pill(10, 10, accent)   # 正方形なので円になる
        if self._chip_image is not None:
            chip.create_image(0, 0, image=self._chip_image, anchor="nw")
        else:
            chip.create_oval(0, 0, 9, 9, fill=accent, outline="")
        name = tk.Label(
            header, text=fmt.truncate(app["name"], 44), bg=theme.BG, fg=theme.TEXT,
            font=fonts.body, anchor="w",
        )
        name.pack(side="left")
        tk.Label(
            header, text=fmt.duration_long(app["seconds"]), bg=theme.BG, fg=theme.MUTED,
            font=fonts.small, anchor="e",
        ).pack(side="right")

        # マスの大きさは概要タブと揃える(target_cell=15)。一覧なので、
        # 画面の外にある行は描画を後回しにする。
        self.heatmap = Heatmap(
            self, fonts, rows=rows, axis=cols is None, defer_offscreen=True,
            weekdays=True,
        )
        self.heatmap.pack(fill="x")
        # 濃さはアプリごとに正規化する。使用量の少ないアプリでも形が見えるように。
        self.heatmap.set_data(
            app["daily"], rows=rows, cols=cols, end=end, palette=theme.heat_ramp(hue)
        )

        if on_click:
            name.configure(cursor="hand2")
            for widget in (name, chip):
                widget.bind("<Button-1>", lambda _e: on_click(app))
            name.bind("<Enter>", lambda _e: name.configure(fg=accent))
            name.bind("<Leave>", lambda _e: name.configure(fg=theme.TEXT))


class AppUsageRow(RoundedCanvas):
    """ランキング1行。カラーマーク・名前・進捗バー・時間をまとめて描く。"""

    PAD = 13

    def __init__(self, master, fonts, app: dict, peak: float,
                 rank: int | None = None, on_click=None):
        super().__init__(master, height=58)
        self._fonts = fonts
        self._app = app
        self._peak = max(peak, 1.0)
        self._rank = rank
        self._on_click = on_click
        self._bar = None

        if on_click is not None:
            self.configure(cursor="hand2")
            self.bind("<Button-1>", lambda _e: on_click(app))
            self.bind("<Enter>", lambda _e: self._set_card(theme.CARD_HOVER))
            self.bind("<Leave>", lambda _e: self._set_card(theme.CARD))

    def _set_card(self, color: str) -> None:
        self._card = color
        self.refresh()

    def draw(self, width: int, height: int) -> None:
        app = self._app
        color = theme.app_accent(app["name"])

        x = self.PAD
        if self._rank is not None:
            self.create_text(
                x, height / 2, text=str(self._rank), anchor="w",
                fill=theme.FAINT, font=self._fonts.small,
            )
            x += 20

        radius = 5
        center = height / 2
        self.create_oval(
            x, center - radius, x + radius * 2, center + radius,
            fill=color, outline="",
        )
        text_x = x + radius * 2 + 10

        # 右側の数字が入る幅を測ってから、バーの右端を決める
        duration_font = measure_font(
            self._fonts.family, self._fonts.value_sm[1], weight="bold"
        )
        small_font = measure_font(self._fonts.family, self._fonts.small[1])
        duration_text = fmt.duration_long(app["seconds"])
        sessions_text = f"{app['sessions']}セッション"
        right_w = max(duration_font.measure(duration_text), small_font.measure(sessions_text))

        self.create_text(
            width - self.PAD, 19, text=duration_text, anchor="e",
            fill=theme.TEXT, font=self._fonts.value_sm,
        )
        self.create_text(
            width - self.PAD, 39, text=sessions_text, anchor="e",
            fill=theme.MUTED, font=self._fonts.small,
        )
        self.create_text(
            text_x, 19, text=fmt.truncate(app["name"], 34), anchor="w",
            fill=theme.TEXT, font=self._fonts.body,
        )

        track_x0 = text_x
        track_x1 = max(track_x0 + 3, width - self.PAD - right_w - 14)
        y = 36
        self.create_rectangle(track_x0, y, track_x1, y + 5, fill=theme.BAR_TRACK, outline="")

        ratio = max(0.02, min(1.0, app["seconds"] / self._peak))
        bar_w = max(3, round((track_x1 - track_x0) * ratio))
        self._bar = render.rounded_rect(bar_w, 5, 2.5, color)
        if self._bar is not None:
            self.create_image(track_x0, y, image=self._bar, anchor="nw")
        else:
            self.create_rectangle(track_x0, y, track_x0 + bar_w, y + 5, fill=color, outline="")


class HourlyChart(tk.Canvas):
    """0時〜23時の時間帯別の稼働時間を並べた棒グラフ。"""

    GAP = 4
    LABEL_H = 16

    def __init__(self, master, fonts, height: int = 120, bg: str = theme.BG):
        super().__init__(master, bg=bg, highlightthickness=0, bd=0, height=height)
        self._fonts = fonts
        self._hourly = {}
        self._bars = {}
        self._images = []
        self._tooltip = Tooltip(master, fonts)
        self.bind("<Configure>", lambda _e: self._render())
        self.bind("<Motion>", self._on_motion)
        self.bind("<Leave>", lambda _e: self._tooltip.hide())

    def set_data(self, hourly: dict) -> None:
        self._hourly = hourly
        self._render()

    def _render(self) -> None:
        self.delete("all")
        self._bars.clear()
        self._images.clear()

        width, height = self.winfo_width(), self.winfo_height()
        if width <= 1 or height <= 1:
            return

        area = height - self.LABEL_H
        bar_width = max(3, (width - self.GAP * 23) / 24)
        step = bar_width + self.GAP
        peak = max(self._hourly.values(), default=0.0)
        busiest = max(self._hourly, key=self._hourly.get) if self._hourly else None
        radius = min(3.0, bar_width / 2)

        for hour in range(24):
            seconds = self._hourly.get(hour, 0.0)
            x = hour * step
            track = render.rounded_rect(
                max(1, round(bar_width)), area, radius, theme.BAR_TRACK
            )
            if track is not None:
                self._images.append(track)
                self.create_image(x, 0, image=track, anchor="nw")
            else:
                self.create_rectangle(x, 0, x + bar_width, area,
                                      fill=theme.BAR_TRACK, outline="")

            if peak <= 0 or seconds <= 0:
                continue
            bar_height = max(3, round(area * (seconds / peak)))
            color = theme.HEAT[4] if hour == busiest else theme.BAR
            bar = render.rounded_rect(max(1, round(bar_width)), bar_height, radius, color)
            top = area - bar_height
            if bar is not None:
                self._images.append(bar)
                self.create_image(x, top, image=bar, anchor="nw")
            else:
                self.create_rectangle(x, top, x + bar_width, area, fill=color, outline="")
            self._bars[hour] = (x, x + bar_width)

        for hour in range(0, 24, 3):
            self.create_text(
                hour * step + bar_width / 2, area + self.LABEL_H / 2,
                text=str(hour), fill=theme.FAINT, font=self._fonts.tiny,
            )

    def _on_motion(self, event) -> None:
        for hour, (x0, x1) in self._bars.items():
            if x0 <= event.x <= x1:
                self._tooltip.show(
                    f"{hour}時台 · {fmt.duration_long(self._hourly.get(hour, 0.0))}",
                    event.x_root, event.y_root,
                )
                return
        self._tooltip.hide()
