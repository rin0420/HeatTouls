"""設定値とパス解決。"""

import os
from pathlib import Path

APP_NAME = "HeatTouls"
LEGACY_APP_NAME = "toulstudio"   # 旧名。保存先の引き継ぎに使う

# --- 計測パラメータ -------------------------------------------------------
POLL_SECONDS = 1.0        # 前面ウィンドウを見に行く間隔
FLUSH_SECONDS = 20.0      # メモリ上の集計をDBへ書き出す間隔
IDLE_THRESHOLD = 180.0    # 何秒無操作ならアイドル扱いにするか
SESSION_GAP = 300.0       # この秒数以上間隔が空いたらセッションを切る
MIN_SESSION_SECONDS = 10.0  # これ未満のセッションは記録しない
MIN_ACTIVE_DAY_SECONDS = 60.0  # 「アクティブな日」とみなす1日の最低秒数

# 計測対象から除外する実行ファイル名(小文字)。
# ロック画面やIME、シェル自身など「ユーザーが使っているアプリ」ではないもの。
IGNORED_EXES = {
    "lockapp.exe",
    "logonui.exe",
    "textinputhost.exe",
    "shellexperiencehost.exe",
    "searchhost.exe",
    "searchapp.exe",
    "startmenuexperiencehost.exe",
    "applicationframehost.exe",  # UWPホスト。中身のPIDに解決できなかった場合のみ該当
    "idle",
}


def _env(name: str):
    """HEATTOULS_* を見る。旧名の TOULSTUDIO_* も引き続き受け付ける。"""
    return os.environ.get(f"HEATTOULS_{name}") or os.environ.get(f"TOULSTUDIO_{name}")


def data_dir() -> Path:
    """データ保存先。HEATTOULS_DATA_DIR で上書きできる。"""
    override = _env("DATA_DIR")
    if override:
        path = Path(override)
        path.mkdir(parents=True, exist_ok=True)
        return path

    base = Path(os.environ.get("LOCALAPPDATA") or str(Path.home()))
    path = base / APP_NAME
    if not path.exists():
        # 旧名(toulstudio)で貯めた記録があれば、そのまま引き継ぐ
        legacy = base / LEGACY_APP_NAME
        if legacy.is_dir():
            try:
                legacy.rename(path)
            except OSError:
                path = legacy   # 使用中などで移せない場合は旧フォルダを使い続ける
    path.mkdir(parents=True, exist_ok=True)
    return path


def db_path() -> Path:
    """DBファイルのパス。HEATTOULS_DB で上書きできる。"""
    override = _env("DB")
    if override:
        path = Path(override)
        path.parent.mkdir(parents=True, exist_ok=True)
        return path
    return data_dir() / "usage.db"
