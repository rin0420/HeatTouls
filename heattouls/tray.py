"""タスクトレイ常駐アイコン。pystrayが無い環境では無効化される。"""

try:
    import pystray
    from PIL import Image  # noqa: F401  icon.make_image が必要とする

    AVAILABLE = True
except ImportError:  # pystray / Pillow 未インストール
    AVAILABLE = False

from . import icon as icon_image


def create(on_show, on_quit):
    """pystray.Icon を返す。利用できない場合は None。"""
    if not AVAILABLE:
        return None

    def show(_icon=None, _item=None):
        on_show()

    def quit_(icon, _item=None):
        icon.stop()
        on_quit()

    menu = pystray.Menu(
        pystray.MenuItem("ダッシュボードを開く", show, default=True),
        pystray.Menu.SEPARATOR,
        pystray.MenuItem("終了", quit_),
    )
    return pystray.Icon(
        "HeatTouls", icon_image.make_image(), "HeatTouls — 稼働時間を計測中", menu
    )
