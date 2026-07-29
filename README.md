# HeatTouls

PC上のどんなソフトウェアでも、**アクティブに使っていた時間**を自動で計測してダッシュボードに可視化するツール。
Windows 11 / .NET 9 + WinUI 3 で動作。

![UI](https://img.shields.io/badge/UI-WinUI%203-informational) ![描画](https://img.shields.io/badge/描画-Win2D-informational) ![計測](https://img.shields.io/badge/API-Win32%20via%20P%2FInvoke-informational)

## 何ができるか

- 前面ウィンドウを1秒ごとに見て、そのアプリのアクティブ時間を積算する
- 3分以上の無操作はアイドルとみなして計上しない（つけっぱなしで数字が膨らまない）
- アプリごとに 合計稼働時間 / 全体に占める割合 / セッション数 / アクティブ日数 / 連続日数 / ピーク時間
- アプリごとの日別ヒートマップ、時間帯別の棒グラフ
- 期間切り替え（すべて / 30日 / 7日）
- タスクトレイ常駐。ウィンドウを閉じてもバックグラウンドで計測を続ける

計測対象を登録する必要はない。前面に出たアプリが自動で一覧に増えていく。

画面は3つのタブに分かれる。

| タブ | 内容 |
| --- | --- |
| ホーム | アプリごとの日別ヒートマップを一覧表示する。アプリごとに色が変わる |
| 概要 | アプリを1つ選び、その詳細（統計カード8枚・ヒートマップ・時間帯別）を見る |
| ランキング | よく使ったアプリを稼働時間順に並べる |

ホームやランキングでアプリ名をクリックすると、概要タブでそのアプリの詳細が開く。
ホームと概要のヒートマップは同じ行×列なので、並べて見比べられる。

### ヒートマップの読み方

1列が1週間（日曜〜土曜）。行は上から **日月火水木金土** で、左端に月・水・金だけ
ラベルを出す。左上が最も古い日、右下が今週の土曜で、列を上から下へ埋めてから
次の列へ進む。上には月の変わり目（`3月` `4月` …）を出している。

**使用時間が長い日ほど色が濃くなる**（右下の「少 〜 多」が目安）。段階が上がる
ごとに彩度を上げているので、一番よく使った日が最もくっきり出る。同時に明度も
上げてあるため、暗い背景に沈むこともない。

今週のまだ来ていない日は、週の区切りを揃えるために記録なしのマスとして描かれる。

### 期間の切り替え

右上の `すべて / 30日 / 7日` は、集計範囲とヒートマップの形をまとめて変える。

| 期間 | ヒートマップ（ホーム・概要 共通） |
| --- | --- |
| 7日 | 1行 × 7列 |
| 30日 | 3行 × 10列 |
| すべて | 7行 × 幅いっぱい |

## 使う（推奨）

[Releases](https://github.com/rin0420/HeatTouls/releases/latest) から
`HeatTouls-win-x64.zip` をダウンロードして展開し、`HeatTouls.exe` を
**ダブルクリックするだけ**で動く。.NET ランタイムも Windows App SDK も同梱しているので、
利用者側のインストールは一切不要。

署名していないので、初回起動時に SmartScreen の警告が出ることがある
（「詳細情報」→「実行」で起動できる）。

## 自分でビルドする

[.NET 9 SDK](https://dotnet.microsoft.com/download) を入れて:

```powershell
.\build.ps1                      # dist\HeatTouls\HeatTouls.exe
.\build.ps1 -Runtime win-arm64   # ARM64 向け
.\build.ps1 -SkipIcon            # アイコンを作り直さない
```

`build.ps1` はアイコン（[src/HeatTouls/Assets/HeatTouls.ico](src/HeatTouls/Assets/HeatTouls.ico)）を
[tools/IconGen](tools/IconGen/Program.cs) で生成してから `dotnet publish` を呼ぶ。
Visual Studio は不要で、.NET SDK だけでビルドできる。

exe にはコマンドライン引数をそのまま渡せる。ショートカットのプロパティで
リンク先の末尾に `--minimized` などを足せばよい。
`--autostart` の結果はコンソールが無い場合ダイアログで表示される。

## ソースから起動する

```powershell
dotnet run --project src\HeatTouls                    # 計測しながらダッシュボードを表示
dotnet run --project src\HeatTouls -- --minimized     # ウィンドウを出さずトレイのみで起動
dotnet run --project src\HeatTouls -- --track-only    # GUIなしで計測だけ（Ctrl+Cで終了）
dotnet run --project src\HeatTouls -- --no-tray       # トレイに常駐しない
dotnet run --project src\HeatTouls -- --demo          # サンプルデータでUIを確認（別DBなので実データに影響なし）
dotnet run --project src\HeatTouls -- --db <PATH>     # 使用するDBファイルを指定
```

ログオン時の自動起動:

```powershell
.\dist\HeatTouls\HeatTouls.exe --autostart on       # 自分自身を登録
.\dist\HeatTouls\HeatTouls.exe --autostart status   # 状態確認
.\dist\HeatTouls\HeatTouls.exe --autostart off      # 解除
```

登録先は `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`。
`--minimized`（ウィンドウを出さずトレイのみ）で登録される。

ショートカット: `Ctrl+R` で再集計、`Esc` でウィンドウを閉じる（トレイへ）。
ヒートマップと棒グラフはマウスを乗せると内訳が出る。

## データの保存先

`%LOCALAPPDATA%\HeatTouls\usage.db`（SQLite）。
`HEATTOULS_DB` でファイルを、`HEATTOULS_DATA_DIR` でフォルダを上書きできる。
外部への送信は一切行わない。

| テーブル | 内容 |
| --- | --- |
| `apps` | 実行ファイルのパスと表示名（exeのFileDescriptionから取得） |
| `usage` | (日, 時, アプリ) ごとのアクティブ秒数 |
| `sessions` | 連続して使っていた区間 |

## 構成

| ファイル | 役割 |
| --- | --- |
| [src/HeatTouls/Program.cs](src/HeatTouls/Program.cs) | エントリポイント、CLI |
| [src/HeatTouls/App.xaml.cs](src/HeatTouls/App.xaml.cs) | 計測スレッド・ウィンドウ・トレイの起動と終了 |
| [src/HeatTouls/MainWindow.xaml.cs](src/HeatTouls/MainWindow.xaml.cs) | メインウィンドウと3タブの組み立て |
| [src/HeatTouls/TrayIcon.cs](src/HeatTouls/TrayIcon.cs) | タスクトレイ常駐（Shell_NotifyIcon） |
| [src/HeatTouls/Theme.cs](src/HeatTouls/Theme.cs) | 配色とフォント |
| [src/HeatTouls/Core/WinApi.cs](src/HeatTouls/Core/WinApi.cs) | Win32 API（前面ウィンドウ / 無操作時間 / exe表示名） |
| [src/HeatTouls/Core/Tracker.cs](src/HeatTouls/Core/Tracker.cs) | 1秒ポーリングして集計するバックグラウンドスレッド |
| [src/HeatTouls/Core/Database.cs](src/HeatTouls/Core/Database.cs) | SQLiteスキーマと書き込み |
| [src/HeatTouls/Core/Stats.cs](src/HeatTouls/Core/Stats.cs) | ダッシュボード用の集計クエリ |
| [src/HeatTouls/Core/Palette.cs](src/HeatTouls/Core/Palette.cs) | ヒートマップの配色計算（アイコン生成と共用） |
| [src/HeatTouls/Core/Config.cs](src/HeatTouls/Core/Config.cs) | 設定値とパス解決 |
| [src/HeatTouls/Core/Autostart.cs](src/HeatTouls/Core/Autostart.cs) | ログオン時自動起動（HKCUのRunキー） |
| [src/HeatTouls/Core/Demo.cs](src/HeatTouls/Core/Demo.cs) | UI確認用サンプルデータ |
| [src/HeatTouls/Controls/](src/HeatTouls/Controls/) | ヒートマップ / カード / 時間帯別グラフ / 行ウィジェット |
| [tools/IconGen/](tools/IconGen/Program.cs) | アプリアイコン(.ico)の生成 |
| [build.ps1](build.ps1) | アイコン生成 + `dotnet publish` |

ヒートマップと時間帯別グラフは [Win2D](https://github.com/microsoft/Win2D) で直接描き、
角丸カードやバーは WinUI の `Border` にそのまま任せている。

## 調整できる値

[src/HeatTouls/Core/Config.cs](src/HeatTouls/Core/Config.cs) の定数:

| 定数 | 既定 | 意味 |
| --- | --- | --- |
| `PollSeconds` | 1.0 | 前面ウィンドウを見に行く間隔 |
| `IdleThreshold` | 180.0 | 何秒無操作でアイドル扱いにするか |
| `SessionGap` | 300.0 | セッションを切る間隔 |
| `FlushSeconds` | 20.0 | DBへ書き出す間隔 |
| `IgnoredExes` | — | 計測から除外する実行ファイル名 |

## 制限

- Windows専用（Win32 APIを直接使うため）
- 配布物は1ファイルではなくフォルダ形式（約 218MB）。WinUI 3 は多数のネイティブDLLを
  伴うため単一 exe にまとめられない。.NET と Windows App SDK を同梱しない
  framework-dependent 発行にすれば小さくなるが、利用者側に両方のランタイムが要る
- 署名していないexeなので、初回起動時にSmartScreenの警告が出ることがある
  （「詳細情報」→「実行」で起動できる）
- 「アクティブ時間」＝前面ウィンドウの時間。バックグラウンドで動いているだけのアプリは計上されない
- 管理者権限で動いているアプリはパスを取得できず、記録されないことがある
- スリープ復帰直後の1区間は計上せず、セッションを切る

## ライセンス

[MIT License](LICENSE)
