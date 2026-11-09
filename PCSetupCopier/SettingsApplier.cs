using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PCSetupCopier;

// Only the explicitly allowlisted Windows mouse-acceleration setting can be written.
internal static class SettingsApplier
{
    private const uint GetMouse = 0x0003;
    private const uint SetMouse = 0x0004;
    private const uint PersistAndNotify = 0x0001 | 0x0002;
    private static readonly Regex Format = new(
        @"Threshold1=(\d+)\s*/\s*Threshold2=(\d+)\s*/\s*Accel=(\d+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private sealed record MouseBackup(DateTimeOffset CapturedAt, int[] Values);
    private static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCSetupCopier", "Backups");
    private static string LatestBackup => Path.Combine(Folder, "mouse-acceleration-latest.json");

    internal static bool CanRestore => File.Exists(LatestBackup);

    internal static bool TryParse(string? value, out int[] result)
    {
        result = [];
        if (value == null) return false;
        Match m = Format.Match(value);
        if (!m.Success) return false;
        if (!int.TryParse(m.Groups[1].Value, out int a) ||
            !int.TryParse(m.Groups[2].Value, out int b) ||
            !int.TryParse(m.Groups[3].Value, out int c)) return false;
        // Windows mouse settings: guard against unexpected or malicious JSON values.
        if (a is < 0 or > 20 || b is < 0 or > 20 || c is < 0 or > 2) return false;
        result = [a, b, c];
        return true;
    }

    internal static int[] Read()
    {
        int[] result = new int[3];
        if (!SystemParametersInfo(GetMouse, 0, result, 0))
            throw new InvalidOperationException("Windowsのマウス加速設定を取得できません。");
        return result;
    }

    private static void Write(int[] values)
    {
        if (values.Length != 3 || !SystemParametersInfo(SetMouse, 0, values, PersistAndNotify))
            throw new InvalidOperationException("マウス加速設定を変更できませんでした。");
        if (!Read().SequenceEqual(values))
            throw new InvalidOperationException("適用後のマウス加速設定が一致しません。");
    }

    internal static string Apply(int[] requested)
    {
        int[] before = Read();
        if (before.SequenceEqual(requested)) return "現在の設定と同じため変更は不要です。";
        Directory.CreateDirectory(Folder);
        // Preserve the last recovery point unless/until a successful change is requested.
        // Never touch other Windows settings.
        var backup = new MouseBackup(DateTimeOffset.Now, before);
        string unique = Path.Combine(Folder, $"mouse-acceleration-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        string json = JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(unique, json);
        File.WriteAllText(LatestBackup, json);
        try { Write(requested); }
        catch
        {
            try { Write(before); }
            catch (Exception rollback)
            {
                throw new InvalidOperationException(
                    "設定変更に失敗し、自動復元も失敗しました。バックアップ: " + unique, rollback);
            }
            throw;
        }
        return "マウス加速を適用し、再取得で確認しました。元の値はバックアップ済みです。";
    }

    // Used only for rollback within the same confirmed batch.
    internal static void RestoreExact(int[] original)
    {
        if (original.Length != 3 || original[0] is < 0 or > 20 ||
            original[1] is < 0 or > 20 || original[2] is < 0 or > 2)
            throw new InvalidDataException("復元用のマウス設定が不正です。");
        Write(original);
    }

    internal static string Restore()
    {
        if (!File.Exists(LatestBackup))
            throw new FileNotFoundException("復元用バックアップが見つかりません。");
        var backup = JsonSerializer.Deserialize<MouseBackup>(File.ReadAllText(LatestBackup));
        if (backup?.Values is not { Length: 3 } || backup.Values[0] is < 0 or > 20 ||
            backup.Values[1] is < 0 or > 20 || backup.Values[2] is < 0 or > 2)
            throw new InvalidDataException("バックアップの内容が不正です。");
        int[] before = Read();
        try { Write(backup.Values); }
        catch
        {
            try { Write(before); } catch { }
            throw;
        }
        // Keep backup on disk for recovery/audit, rather than discarding it.
        return "バックアップのマウス加速設定に復元しました。";
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint param, [In, Out] int[] values, uint flags);
}
