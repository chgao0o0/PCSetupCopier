using System.Text.Json;

namespace PCSetupCopier;

// Presets are preferences, not backup snapshots. Only an explicitly supported
// mouse acceleration choice is accepted; reading a preset never changes Windows.
internal sealed record UserPreset(string Name, bool MouseAcceleration, DateTimeOffset SavedAt);

internal static class PresetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCSetupCopier", "Presets");
    private static string FilePath => Path.Combine(Folder, "presets.json");

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 40 ||
            name.Any(c => char.IsControl(c) || c == '/' || c == '\\'))
            throw new ArgumentException("プリセット名は1～40文字で入力してください。改行やスラッシュは使えません。");
    }

    internal static IReadOnlyList<UserPreset> Load()
    {
        if (!File.Exists(FilePath)) return [];
        if (new FileInfo(FilePath).Length > 200_000)
            throw new InvalidDataException("プリセットファイルが大きすぎます。");
        var list = JsonSerializer.Deserialize<List<UserPreset>>(File.ReadAllText(FilePath))
            ?? throw new InvalidDataException("プリセットファイルが不正です。");
        if (list.Count > 100) throw new InvalidDataException("プリセット数が多すぎます。");
        foreach (var preset in list)
        {
            if (preset is null) throw new InvalidDataException("プリセットが不正です。");
            ValidateName(preset.Name);
        }
        if (list.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != list.Count)
            throw new InvalidDataException("プリセット名が重複しています。");
        return list.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    internal static void Save(string name, bool mouseAcceleration)
    {
        name = name.Trim();
        ValidateName(name);
        var list = Load().ToList();
        int existing = list.FindIndex(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        var entry = new UserPreset(name, mouseAcceleration, DateTimeOffset.Now);
        if (existing >= 0) list[existing] = entry;
        else
        {
            if (list.Count >= 100) throw new InvalidOperationException("プリセットは100件まで保存できます。");
            list.Add(entry);
        }
        Commit(list);
    }

    internal static void Delete(string name)
    {
        var list = Load().ToList();
        if (list.RemoveAll(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)) == 0)
            throw new FileNotFoundException("指定したプリセットがありません。");
        Commit(list);
    }

    private static void Commit(List<UserPreset> presets)
    {
        Directory.CreateDirectory(Folder);
        string temporary = Path.Combine(Folder, "presets-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(presets, JsonOptions));
            if (File.Exists(FilePath))
                File.Move(temporary, FilePath, true);
            else
                File.Move(temporary, FilePath);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    // Pure check for package preflight. No file writes.
    internal static bool VerifyContract()
    {
        try
        {
            ValidateName("Gaming");
            try { ValidateName("../invalid"); return false; }
            catch (ArgumentException) { }
            var sample = new UserPreset("Gaming", false, DateTimeOffset.UnixEpoch);
            return JsonSerializer.Deserialize<UserPreset>(JsonSerializer.Serialize(sample)) == sample;
        }
        catch { return false; }
    }
}
