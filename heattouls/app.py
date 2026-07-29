"""ダッシュボードのメインウィンドウ。"""

import datetime as dt
import tkinter as tk

from . import db, fmt, stats, theme, widgets

REFRESH_MS = 5000

RANGES = [("all", "すべて"), ("30", "30日"), ("7", "7日")]
TABS = [("home", "ホーム"), ("overview", "概要"), ("apps", "ランキング")]

RANGE_DAYS = {"all": None, "30": 30, "7": 7}

# 期間ごとのヒートマップの行×列。行×列がそのまま表示日数になるので、
# 期間の日数で割り切れる組を選んでいる(cols=Noneは幅いっぱいに詰める)。
# ホームと概要は同じ形にして、並べたときに見比べられるようにする。
OVERVIEW_HEATMAP_GRID = {"all": (7, None), "30": (3, 10), "7": (1, 7)}
HOME_HEATMAP_GRID = OVERVIEW_HEATMAP_GRID


class Dashboard:
    """tkinterのウィンドウとその状態。"""

    def __init__(self, conn=None, minimize_on_close=False, title="HeatTouls"):
        self.conn = conn or db.connect()
        self.minimize_on_close = minimize_on_close
        self._after_id = None
        # 一覧を作り直したかどうかの判定に使う。5秒ごとの自動更新で毎回組み直すと
        # スクロール位置が飛び、画像の描き直しも無駄になるため。
        self._home_signature = None
        self._apps_signature = None
        self._overview_signature = None
        self.selected_app = None   # 概要タブで見ているアプリ

        self.root = tk.Tk()
        self.root.title(title)
        self.root.configure(bg=theme.BG)
        self.root.geometry("1000x680")
        self.root.minsize(860, 560)

        self.fonts = theme.Fonts()
        self._build()
        self.refresh()

        self.root.protocol("WM_DELETE_WINDOW", self.hide)
        self.root.bind("<Control-r>", lambda _e: self.refresh())
        self.root.bind("<Escape>", lambda _e: self.hide())
        self._schedule()

    # --- 組み立て ---------------------------------------------------------
    def _build(self) -> None:
        outer = tk.Frame(self.root, bg=theme.BG)
        outer.pack(fill="both", expand=True, padx=18, pady=16)

        header = tk.Frame(outer, bg=theme.BG)
        header.pack(fill="x", pady=(0, 14))

        self.tabs = widgets.SegmentedControl(header, self.fonts, TABS, self._on_tab, padx=13)
        self.tabs.pack(side="left")
        self.ranges = widgets.SegmentedControl(
            header, self.fonts, RANGES, self._on_range
        )
        self.ranges.pack(side="right")

        self.content = tk.Frame(outer, bg=theme.BG)
        self.content.pack(fill="both", expand=True)

        self.pages = {
            "home": self._build_home(self.content),
            "overview": self._build_overview(self.content),
            "apps": self._build_apps(self.content),
        }
        self.pages["home"].pack(fill="both", expand=True)

    def _build_home(self, master) -> tk.Frame:
        """ホームタブ: アプリごとのヒートマップだけを縦に並べた一覧。"""
        page = tk.Frame(master, bg=theme.BG)
        self.home_scroll = widgets.ScrollFrame(page)
        self.home_scroll.pack(fill="both", expand=True)
        return page

    def _build_overview(self, master) -> tk.Frame:
        """概要タブ: アプリを1つ選んで、その詳細を見る画面。"""
        page = tk.Frame(master, bg=theme.BG)

        # 上部: 対象アプリの選択
        picker = tk.Frame(page, bg=theme.BG)
        picker.pack(fill="x", pady=(0, 12))
        tk.Label(
            picker, text="対象", bg=theme.BG, fg=theme.MUTED,
            font=self.fonts.small, anchor="w",
        ).pack(side="left", padx=(0, 8))

        self._app_var = tk.StringVar(value="—")
        self.app_picker = tk.Menubutton(
            picker, textvariable=self._app_var, bg=theme.CARD, fg=theme.TEXT,
            activebackground=theme.CARD_HOVER, activeforeground=theme.TEXT,
            font=self.fonts.body, relief="flat", bd=0, padx=12, pady=4,
            highlightthickness=1, highlightbackground=theme.BORDER,
            cursor="hand2", direction="below",
        )
        self.app_picker.menu = tk.Menu(
            self.app_picker, tearoff=0, bg=theme.CARD, fg=theme.TEXT,
            activebackground=theme.SEG_ACTIVE_BG, activeforeground=theme.TEXT,
            bd=0, font=self.fonts.body,
        )
        self.app_picker.configure(menu=self.app_picker.menu)
        self.app_picker.pack(side="left")

        self.app_period = tk.Label(
            picker, text="", bg=theme.BG, fg=theme.FAINT,
            font=self.fonts.small, anchor="e",
        )
        self.app_period.pack(side="right")

        grid = tk.Frame(page, bg=theme.BG)
        grid.pack(fill="x")
        for col in range(4):
            grid.columnconfigure(col, weight=1, uniform="cards")

        specs = [
            ("total", "合計稼働時間", False),
            ("share", "全体に占める割合", False),
            ("sessions", "セッション", False),
            ("active_days", "アクティブ日数", False),
            ("average", "1日あたり", False),
            ("streak", "現在の連続日数", False),
            ("longest", "最長連続日数", False),
            ("peak", "ピーク時間", False),
        ]
        self.cards = {}
        for index, (key, label, small) in enumerate(specs):
            card = widgets.StatCard(grid, self.fonts, label, small=small)
            card.grid(
                row=index // 4, column=index % 4, sticky="nsew",
                padx=(0 if index % 4 == 0 else 5, 0),
                pady=(0 if index < 4 else 8, 0),
            )
            self.cards[key] = card

        # 見出しも凡例も置かず、背景に直接ヒートマップを敷く(スクショに合わせる)
        self.heatmap = widgets.Heatmap(
            page, self.fonts, rows=7, bg=theme.BG, axis=True, legend=True,
            weekdays=True,
        )
        self.heatmap.pack(fill="x", pady=(20, 0))

        # そのアプリを1日のどの時間帯に使っているか
        self.hourly = widgets.HourlyChart(page, self.fonts, height=104)
        self.hourly.pack(fill="x", pady=(16, 0))

        self.footer = tk.Label(
            page, text="", bg=theme.BG, fg=theme.MUTED,
            font=self.fonts.small, anchor="w", justify="left",
        )
        self.footer.pack(fill="x", pady=(12, 0))
        return page

    def _build_apps(self, master) -> tk.Frame:
        """ランキングタブ: よく使ったアプリの一覧だけ。"""
        page = tk.Frame(master, bg=theme.BG)

        self.apps_heading = tk.Label(
            page, text="よく使ったアプリ", bg=theme.BG, fg=theme.TEXT,
            font=self.fonts.section, anchor="w",
        )
        self.apps_heading.pack(fill="x", pady=(0, 9))

        self.apps_scroll = widgets.ScrollFrame(page)
        self.apps_scroll.pack(fill="both", expand=True)
        self.app_rows = self.apps_scroll.body
        return page

    # --- 状態 -------------------------------------------------------------
    def _on_tab(self, key: str) -> None:
        for name, page in self.pages.items():
            if name != key:
                page.pack_forget()
        self.pages[key].pack(fill="both", expand=True)
        self.refresh()

    def _on_range(self, _key: str) -> None:
        # 期間が変わればどのタブも作り直す必要がある
        self._home_signature = None
        self._apps_signature = None
        self._overview_signature = None
        self.refresh()

    def _open_app(self, app: dict) -> None:
        """アプリ一覧の行から、そのアプリの詳細(概要タブ)へ飛ぶ。"""
        self.selected_app = app["name"]
        self._overview_signature = None
        self.tabs.select("overview")

    def refresh(self) -> None:
        """現在表示中のタブだけを再構築する。

        ホームタブはアプリ数ぶん画像を描くため、毎回すべてのタブを更新すると重い。
        表示中のタブに応じて必要な分だけ集計・描画する。
        """
        range_key = self.ranges.value
        current = self.tabs.value
        if current == "home":
            self._refresh_home(range_key)
        elif current == "overview":
            self._refresh_overview(range_key)
        else:
            self._refresh_apps(range_key)

    # --- ホーム -----------------------------------------------------------
    def _refresh_home(self, range_key: str) -> None:
        apps = stats.app_daily(self.conn, RANGE_DAYS[range_key])
        signature = (range_key, tuple((a["name"], round(a["seconds"])) for a in apps))
        if signature == self._home_signature:
            return  # 中身が変わっていないので、スクロール位置を保ったまま何もしない
        previous_range = self._home_signature[0] if self._home_signature else None
        self._home_signature = signature

        self.home_scroll.clear()
        if not apps:
            tk.Label(
                self.home_scroll.body, text="まだ記録がありません。",
                bg=theme.BG, fg=theme.MUTED, font=self.fonts.body, anchor="w",
            ).pack(fill="x", pady=20)
            self.home_scroll.to_top()
            return

        end = dt.date.today()
        rows, cols = HOME_HEATMAP_GRID[range_key]
        # 一覧全体で色がかぶらないように色相を割り当てる
        hues = theme.assign_hues([a["name"] for a in apps])
        for app in apps:
            row = widgets.AppHeatmapRow(
                self.home_scroll.body, self.fonts, app, end,
                rows=rows, cols=cols, hue=hues[app["name"]],
                on_click=self._open_app,
            )
            row.pack(fill="x", pady=(0, 14))
        # 期間を切り替えた時だけ先頭に戻す。計測による更新では今の位置を保つ。
        if previous_range != range_key:
            self.home_scroll.to_top()
        self.home_scroll.wake_visible()

    # --- 概要(アプリ詳細) --------------------------------------------------
    def _refresh_overview(self, range_key: str) -> None:
        days = RANGE_DAYS[range_key]
        names = stats.app_names(self.conn, days)

        # 選択中のアプリがこの期間に無ければ、一番使っているアプリに寄せる
        if self.selected_app not in names:
            self.selected_app = names[0] if names else None

        signature = (range_key, self.selected_app, tuple(names))
        if signature != self._overview_signature:
            self._overview_signature = signature
            self._rebuild_app_menu(names)

        self._app_var.set(fmt.truncate(self.selected_app, 30) if self.selected_app else "—")
        if not self.selected_app:
            for card in self.cards.values():
                card.set_value("—")
            self.heatmap.set_data({}, *OVERVIEW_HEATMAP_GRID[range_key])
            self.hourly.set_data({})
            self.app_period.configure(text="")
            self.footer.configure(text="まだ記録がありません。")
            return

        detail = stats.app_detail(self.conn, self.selected_app, days)
        self.cards["total"].set_value(fmt.duration(detail["total_seconds"]))
        self.cards["share"].set_value(f"{detail['share'] * 100:.0f}%")
        self.cards["sessions"].set_value(fmt.count(detail["session_count"]))
        self.cards["active_days"].set_value(fmt.days(detail["active_days"]))
        self.cards["average"].set_value(fmt.duration(detail["average_seconds"]))
        self.cards["streak"].set_value(fmt.days(detail["current_streak"]))
        self.cards["longest"].set_value(fmt.days(detail["longest_streak"]))
        self.cards["peak"].set_value(fmt.hour_label(detail["peak_hour"]))

        rows, cols = OVERVIEW_HEATMAP_GRID[range_key]
        self.heatmap.set_data(
            detail["daily"], rows=rows, cols=cols,
            palette=theme.heat_ramp(theme.app_hue(self.selected_app)),
        )
        self.hourly.set_data(detail["hourly"])
        self.app_period.configure(text=self._period_text(detail))
        self.footer.configure(text=self._footer_text(detail))

    def _rebuild_app_menu(self, names) -> None:
        menu = self.app_picker.menu
        menu.delete(0, "end")
        for name in names:
            menu.add_command(
                label=fmt.truncate(name, 40),
                command=lambda n=name: self._select_app(n),
            )
        if not names:
            menu.add_command(label="(記録なし)", state="disabled")

    def _select_app(self, name: str) -> None:
        self.selected_app = name
        self.refresh()

    def _period_text(self, detail: dict) -> str:
        first, last = detail["first_day"], detail["last_day"]
        if not first:
            return ""
        if first == last:
            return f"{first.replace('-', '/')}"
        return f"{first.replace('-', '/')} 〜 {last.replace('-', '/')}"

    def _footer_text(self, detail: dict) -> str:
        total = detail["total_seconds"]
        if total <= 0:
            return f"{detail['name']} はこの期間に記録がありません。"
        movies = total / (2 * 3600)
        return (
            f"{detail['name']} を合計 {fmt.duration_long(total)} — "
            f"長編映画（2時間）約{movies:,.0f}本分。"
            f" 全体の{detail['share'] * 100:.0f}%を占めています。"
        )

    # --- ランキング --------------------------------------------------------
    def _refresh_apps(self, range_key: str) -> None:
        apps = stats.app_breakdown(self.conn, RANGE_DAYS[range_key])
        self.apps_heading.configure(
            text=f"よく使ったアプリ · {len(apps)}" if apps else "よく使ったアプリ"
        )

        signature = (
            range_key,
            tuple((a["name"], round(a["seconds"]), a["sessions"]) for a in apps),
        )
        if signature == self._apps_signature:
            return
        self._apps_signature = signature

        for child in self.app_rows.winfo_children():
            child.destroy()
        if not apps:
            tk.Label(
                self.app_rows, text="まだ記録がありません。",
                bg=theme.BG, fg=theme.MUTED, font=self.fonts.body, anchor="w",
            ).pack(fill="x", pady=16)
            return

        peak = max(app["seconds"] for app in apps) or 1.0
        for rank, app in enumerate(apps, start=1):
            widgets.AppUsageRow(
                self.app_rows, self.fonts, app, peak,
                rank=rank, on_click=self._open_app,
            ).pack(fill="x", pady=(0, 6))

    # --- 更新ループ --------------------------------------------------------
    def _schedule(self) -> None:
        self._after_id = self.root.after(REFRESH_MS, self._tick)

    def _tick(self) -> None:
        try:
            self.refresh()
        except Exception:
            pass  # 一時的なDBロック等で更新ループを止めない
        self._schedule()

    # --- ウィンドウ制御 ----------------------------------------------------
    def show(self) -> None:
        self.root.deiconify()
        self.root.lift()
        self.root.focus_force()
        self.refresh()

    def hide(self) -> None:
        """閉じるボタン。トレイ常駐中は隠すだけ、そうでなければ終了する。"""
        if self.minimize_on_close:
            self.root.withdraw()
        else:
            self.quit()

    # トレイは別スレッドから呼ばれるので、必ずafter()経由でUIスレッドに戻す。
    def request_show(self) -> None:
        self.root.after(0, self.show)

    def request_quit(self) -> None:
        self.root.after(0, self.quit)

    def quit(self) -> None:
        if self._after_id:
            try:
                self.root.after_cancel(self._after_id)
            except tk.TclError:
                pass
            self._after_id = None
        try:
            self.conn.close()
        except Exception:
            pass
        self.root.quit()
        self.root.destroy()

    def run(self) -> None:
        self.root.mainloop()
