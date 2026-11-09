using Microsoft.Win32;
using System.Text.Json;

namespace PCSetupCopier;

// Writes only the current user's HistoricalCaptureEnabled preference.
// It does not claim to verify the effective Windows Game Mode state.
internal static class CapturePreferenceManager
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string ValueName = "HistoricalCaptureEnabled";
    private sealed record Backup(bool KeyExisted, int? Original, DateTimeOffset CapturedAt);

    private static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCSetupCopier", "Backups");
    private static string Latest => Path.Combine(Folder, "background-capture-preference-latest.json");
    internal static bool CanRestore => File.Exists(Latest);

    internal static int? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
        if (key == null) return null;
        var v = key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (v == null) return null;
        if (key.GetValueKind(ValueName) != RegistryValueKind.DWord || v is not int number || number is < 0 or > 1)
            throw new InvalidDataException("バックグラウンド録画関連値が標準の0/1ではありません。変更しません。");
        return number;
    }

    private static void Write(int? target)
    {
        if (target is not null and not (0 or 1))
            throw new ArgumentOutOfRangeException(nameof(target));
        if (target is null)
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        else
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true)
                ?? throw new InvalidOperationException("バックグラウンド録画関連設定を開けません。");
            key.SetValue(ValueName, target.Value, RegistryValueKind.DWord);
        }
        if (Read() != target)
            throw new InvalidOperationException("バックグラウンド録画関連値の再取得が一致しません。");
    }

    internal static string Apply(bool desired)
    {
        int? original = Read();
        int target = desired ? 1 : 0;
        if (original == target) return "指定値と一致しています。";
        bool keyExisted;
        using (var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false))
            keyExisted = key != null;
        Directory.CreateDirectory(Folder);
        var backup = new Backup(keyExisted, original, DateTimeOffset.Now);
        string json = JsonSerializer.Serialize(backup);
        string archive = Path.Combine(Folder, "background-capture-preference-" +
            DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + ".json");
        File.WriteAllText(archive, json);
        File.WriteAllText(Latest, json);
        try { Write(target); }
        catch
        {
            try { Write(original); }
            catch (Exception ex)
            {
                throw new InvalidOperationException("変更と自動復元に失敗しました。バックアップ: " + archive, ex);
            }
            throw;
        }
        return "バックグラウンド録画のユーザー設定値を変更しました。録画機能の実効状態は未検証です。";
    }

    internal static void RestoreExact(int? original) => Write(original);

    internal static string Restore()
    {
        if (!File.Exists(Latest))
            throw new FileNotFoundException("バックグラウンド録画のバックアップがありません。");
        var backup = JsonSerializer.Deserialize<Backup>(File.ReadAllText(Latest))
            ?? throw new InvalidDataException("バックアップが不正です。");
        if (backup.Original is not null and not (0 or 1))
            throw new InvalidDataException("バックアップの値が不正です。");
        int? now = Read();
        try { Write(backup.Original); }
        catch
        {
            try { Write(now); } catch { }
            throw;
        }
        return "バックグラウンド録画関連値を変更前の状態に戻しました。";
    }

    internal static bool VerifyContract()
    {
        // Pure shape check; never touches registry during package verification.
        var sample = new Backup(true, null, DateTimeOffset.UnixEpoch);
        return JsonSerializer.Deserialize<Backup>(JsonSerializer.Serialize(sample)) == sample &&
            KeyPath == @"Software\Microsoft\Windows\CurrentVersion\GameDVR" && ValueName == "HistoricalCaptureEnabled";
    }
}
