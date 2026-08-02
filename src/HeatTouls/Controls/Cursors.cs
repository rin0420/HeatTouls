using Microsoft.UI.Input;

namespace HeatTouls.Controls;

/// <summary>
/// ホバーで差し替えるカーソル。
///
/// InputSystemCursor.Create は呼ぶたびに新しい WinRT オブジェクトを作る。行の出入り
/// ごとに作っていると、一覧をなぞるだけで捨てられないハンドルが溜まっていくので、
/// 使い回す2種類をここに置いておく。
/// </summary>
internal static class Cursors
{
    public static readonly InputSystemCursor Hand =
        InputSystemCursor.Create(InputSystemCursorShape.Hand);

    public static readonly InputSystemCursor Arrow =
        InputSystemCursor.Create(InputSystemCursorShape.Arrow);
}
