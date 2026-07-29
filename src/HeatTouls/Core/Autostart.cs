using Microsoft.Win32;

namespace HeatTouls.Core;

/// <summary>Windowsログオン時の自動起動登録(HKCUのRunキー)。</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HeatTouls";
    private const string LegacyValueName = "toulstudio";   // 旧名で登録済みの分を掃除するため

    /// <summary>コンソールを出さずに起動するコマンドライン。</summary>
    private static string BuildCommand()
    {
        // 発行された exe をそのまま指す。WinExe なのでコンソールは開かない。
        var exe = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0];
        return $"\"{Path.GetFullPath(exe)}\" --minimized";
    }

    /// <summary>登録済みならコマンド文字列、未登録なら null。</summary>
    public static string? Status()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) as string;
    }

    public static string Enable()
    {
        var command = BuildCommand();
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        key.SetValue(ValueName, command, RegistryValueKind.String);
        // 旧名で登録されたままだと二重に起動してしまうので消しておく
        key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
        return command;
    }

    public static bool Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null)
        {
            return false;
        }

        var removed = false;
        foreach (var name in new[] { ValueName, LegacyValueName })
        {
            if (key.GetValue(name) is not null)
            {
                key.DeleteValue(name, throwOnMissingValue: false);
                removed = true;
            }
        }
        return removed;
    }
}
