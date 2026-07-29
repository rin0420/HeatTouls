r"""単体で動く HeatTouls.exe をビルドする。

    python build.py            dist\HeatTouls.exe を作る(1ファイル、コンソールなし)
    python build.py --onedir   起動が速い dist\HeatTouls\ 形式で作る

出力された exe はダブルクリックするだけで動く。Pythonのインストールは不要。
"""

import argparse
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
ASSETS = ROOT / "assets"
ICO = ASSETS / "HeatTouls.ico"


def build_icon() -> Path:
    sys.path.insert(0, str(ROOT))
    from heattouls import icon

    ASSETS.mkdir(exist_ok=True)
    icon.write_ico(ICO)
    print(f"アイコンを生成しました: {ICO}")
    return ICO


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="HeatTouls.exe のビルド")
    parser.add_argument("--onedir", action="store_true", help="1ファイルではなくフォルダ形式で出力する")
    parser.add_argument("--keep-console", action="store_true", help="コンソールウィンドウを表示する(デバッグ用)")
    args = parser.parse_args(argv)

    try:
        import PyInstaller.__main__ as pyinstaller
    except ImportError:
        print("PyInstaller が必要です:  python -m pip install pyinstaller", file=sys.stderr)
        return 1

    build_icon()

    # 前回の生成物を消してから作り直す
    for path in (ROOT / "build", ROOT / "dist"):
        shutil.rmtree(path, ignore_errors=True)

    options = [
        str(ROOT / "main.py"),
        "--name", "HeatTouls",
        "--onedir" if args.onedir else "--onefile",
        "--console" if args.keep_console else "--windowed",
        "--icon", str(ICO),
        "--noconfirm",
        "--clean",
        "--distpath", str(ROOT / "dist"),
        "--workpath", str(ROOT / "build"),
        "--specpath", str(ROOT / "build"),
        # pystrayはバックエンドを実行時に選ぶため、自動検出されない
        "--hidden-import", "pystray._win32",
        # PillowのImageTkは静的解析で拾われないことがあり、無いとexe版で
        # ヒートマップやカードの角丸描画(heattouls/render.py)が動かない
        "--hidden-import", "PIL._tkinter_finder",
        "--exclude-module", "numpy",
        "--exclude-module", "matplotlib",
        "--exclude-module", "pytest",
    ]

    print("PyInstaller を実行します...")
    pyinstaller.run(options)

    target = ROOT / "dist" / ("HeatTouls/HeatTouls.exe" if args.onedir else "HeatTouls.exe")
    if not target.exists():
        print("ビルドに失敗しました。", file=sys.stderr)
        return 1
    size_mb = target.stat().st_size / (1024 * 1024)
    print(f"\n完成しました: {target}  ({size_mb:.1f} MB)")
    print("ダブルクリックで起動します。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
