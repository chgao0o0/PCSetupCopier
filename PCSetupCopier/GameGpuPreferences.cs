using Microsoft.Win32;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PCSetupCopier;

// Per-game graphics preference. This is a preference, not a guarantee which GPU a driver uses.
// Only exact user-selected local executable paths may be written; never scan/edit unrelated keys.
internal static class GameGpuPreferences
{
    private const string RegistryPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private static readonly Regex Preference = new(
        @"^(?:GpuPreference=[012];)?$", RegexOptions.CultureInvariant);
    internal sealed record Request(string ExePath, int Preference);
    internal sealed record Change(string ExePath, string? Before, string? After);
    private sealed record Snapshot(DateTimeOffset SavedAt, List<Change> Changes);

    private static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCSetupCopier", "Backups");
    private static string LatestPath => Path.Combine(DirectoryPath, "per-game-gpu-latest.json");
    internal static bool CanRestore => File.Exists(LatestPath);
    private static string FavoritesPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCSetupCopier", "Games", "selected-games.json");

    internal static IReadOnlyList<string> LoadGameList()
    {
        if (!File.Exists(FavoritesPath)) return [];
        if (new FileInfo(FavoritesPath).Length > 50_000)
            throw new InvalidDataException("登録ゲーム一覧が大きすぎます。");
        string[] games = JsonSerializer.Deserialize<string[]>(File.ReadAllText(FavoritesPath)) ?? [];
        if (games.Length > 100) throw new InvalidDataException("ゲーム登録数が多すぎます。");
        return games.Where(x => !string.IsNullOrEmpty(x) &&
                Path.IsPathFullyQualified(x) && x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                !x.StartsWith(@"\\", StringComparison.Ordinal) && File.Exists(x))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static void SaveGameList(IEnumerable<string> paths)
    {
        var all = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (all.Length > 100) throw new InvalidOperationException("ゲーム登録数は100件までです。");
        foreach (string path in all) ValidPath(path);
        string folder = Path.GetDirectoryName(FavoritesPath)!;
        Directory.CreateDirectory(folder);
        string temporary = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(all));
            File.Move(temporary, FavoritesPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string ValidPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1024 ||
            !Path.IsPathFullyQualified(path))
            throw new ArgumentException("ゲームのexeの絶対パスを選択してください。");
        string full = Path.GetFullPath(path);
        if (!full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            full.StartsWith(@"\\", StringComparison.Ordinal) || !File.Exists(full))
            throw new ArgumentException("ローカルに存在するゲームのexeを選択してください。");
        return full;
    }

    internal static string? Read(string path)
    {
        string full = ValidPath(path);
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, false);
        if (key == null || !key.GetValueNames().Contains(full, StringComparer.OrdinalIgnoreCase))
            return null;
        if (key.GetValueKind(full) != RegistryValueKind.String)
            throw new InvalidDataException("既存のGPU設定が文字列ではありません。上書きを中止します。");
        var stored = key.GetValue(full) as string;
        if (stored == null || !Preference.IsMatch(stored))
            throw new InvalidDataException("既存のGPU設定に未対応の追加情報があります。上書きを中止します。");
        return stored;
    }

    internal static string Label(string? stored) => stored switch
    {
        null or "" or "GpuPreference=0;" => "Windowsに任せる",
        "GpuPreference=1;" => "省電力を優先",
        "GpuPreference=2;" => "高パフォーマンスを優先",
        _ => "未対応の設定"
    };

    private static string Target(int pref) => pref switch
    {
        0 => "GpuPreference=0;",
        1 => "GpuPreference=1;",
        2 => "GpuPreference=2;",
        _ => throw new ArgumentOutOfRangeException(nameof(pref))
    };

    internal static IReadOnlyList<Change> Preview(IEnumerable<Request> requests)
    {
        var list = requests.ToArray();
        if (list.Length is < 1 or > 40)
            throw new InvalidOperationException("1〜40件のゲームを選択してください。");
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Change>();
        foreach (var req in list)
        {
            string path = ValidPath(req.ExePath);
            if (!unique.Add(path))
                throw new InvalidOperationException("同じゲームが複数回指定されています。");
            string? before = Read(path);
            string after = Target(req.Preference);
            // Unset and explicit Windows default are functionally equivalent, so don't write needlessly.
            if (req.Preference == 0 && before is null or "" or "GpuPreference=0;") continue;
            if (before != after) result.Add(new Change(path, before, after));
        }
        return result;
    }

    private static void WriteExact(Change change, bool forward)
    {
        string path = ValidPath(change.ExePath);
        string? expected = forward ? change.Before : change.After;
        string? next = forward ? change.After : change.Before;
        if (Read(path) != expected)
            throw new InvalidOperationException("確認後にGPU設定が変化しました: " + Path.GetFileName(path));
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, true)
            ?? throw new InvalidOperationException("GPU設定の保存先を開けません。");
        if (next == null) key.DeleteValue(path, false);
        else key.SetValue(path, next, RegistryValueKind.String);
        if (Read(path) != next)
            throw new InvalidOperationException("GPU設定の書き込み検証に失敗: " + Path.GetFileName(path));
    }

    internal static string Apply(IReadOnlyList<Change> plan)
    {
        if (plan.Count == 0) return "変更が必要なゲームはありません。";
        if (plan.Count > 40) throw new InvalidOperationException("一度に変更できるゲームは40件までです。");
        // Recheck all before performing any write.
        foreach (var item in plan)
        {
            if (Read(item.ExePath) != item.Before)
                throw new InvalidOperationException("確認後にGPU設定が変更されています。再取得してください。");
            if (item.After is null || !Preference.IsMatch(item.After))
                throw new InvalidDataException("適用するGPU設定が不正です。");
        }
        Directory.CreateDirectory(DirectoryPath);
        string json = JsonSerializer.Serialize(new Snapshot(DateTimeOffset.Now, plan.ToList()),
            new JsonSerializerOptions { WriteIndented = true });
        string archive = Path.Combine(DirectoryPath, "per-game-gpu-" +
            DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + ".json");
        File.WriteAllText(archive, json);
        File.WriteAllText(LatestPath, json);
        var attempted = new List<Change>();
        try
        {
            foreach (var change in plan)
            {
                attempted.Add(change);
                WriteExact(change, true);
            }
        }
        catch (Exception error)
        {
            var failed = new List<string>();
            foreach (var change in attempted.AsEnumerable().Reverse())
            {
                try
                {
                    // A failed write may have either the old or new value.
                    var now = Read(change.ExePath);
                    if (now == change.Before) continue;
                    if (now != change.After) throw new InvalidOperationException("他の変更を検出しました。");
                    WriteExact(change, false);
                }
                catch (Exception ex) { failed.Add(Path.GetFileName(change.ExePath) + ": " + ex.Message); }
            }
            if (failed.Count > 0)
                throw new InvalidOperationException("GPU設定変更に失敗し、一部を自動復元できませんでした。バックアップを確認してください。\n" +
                    string.Join("\n", failed), error);
            throw new InvalidOperationException("GPU設定変更に失敗したため、自動復元しました: " + error.Message, error);
        }
        return plan.Count + "件のゲーム別GPU優先設定を保存し、再取得で確認しました。ゲームを再起動してください。";
    }

    internal static string Restore()
    {
        if (!CanRestore) throw new FileNotFoundException("GPU設定のバックアップがありません。");
        if (new FileInfo(LatestPath).Length > 300_000)
            throw new InvalidDataException("バックアップのサイズが不正です。");
        var saved = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(LatestPath))
            ?? throw new InvalidDataException("バックアップが不正です。");
        if (saved.Changes.Count is < 1 or > 40)
            throw new InvalidDataException("バックアップの項目数が不正です。");
        var plan = new List<Change>();
        foreach (var item in saved.Changes)
        {
            string path = ValidPath(item.ExePath);
            string? actual = Read(path);
            if (actual != item.After)
                throw new InvalidOperationException("バックアップ後に設定が変更されています。手動確認してください: " + Path.GetFileName(path));
            plan.Add(new Change(path, item.After, item.Before));
        }
        // Restore is also transactional; keep the original archive untouched.
        var applied = new List<Change>();
        try
        {
            foreach (var item in plan)
            {
                applied.Add(item);
                WriteExact(item, true);
            }
        }
        catch (Exception error)
        {
            var errors = new List<string>();
            foreach (var item in applied.AsEnumerable().Reverse())
            {
                try
                {
                    string? now = Read(item.ExePath);
                    if (now == item.Before) continue;
                    if (now != item.After) throw new InvalidOperationException("他の変更を検出しました。");
                    WriteExact(item, false);
                }
                catch (Exception ex) { errors.Add(ex.Message); }
            }
            throw new InvalidOperationException("GPU設定復元に失敗しました: " + error.Message +
                (errors.Count == 0 ? "" : " / 再復元失敗: " + string.Join(", ", errors)), error);
        }
        return plan.Count + "件のゲーム別GPU優先設定をバックアップから復元しました。";
    }

    internal static bool VerifyContract()
    {
        // Test pure validation/round-trip only; no registry access.
        var sample = new Snapshot(DateTimeOffset.UnixEpoch,
            [new Change(@"C:\Games\example.exe", null, "GpuPreference=2;")]);
        var restored = JsonSerializer.Deserialize<Snapshot>(JsonSerializer.Serialize(sample));
        return restored?.Changes.Count == 1 && restored.Changes[0].Before == null &&
               restored.Changes[0].After == "GpuPreference=2;" &&
               Target(0) == "GpuPreference=0;" && Target(2) == "GpuPreference=2;" &&
               !Preference.IsMatch("GpuPreference=2;More=1;");
    }
}
