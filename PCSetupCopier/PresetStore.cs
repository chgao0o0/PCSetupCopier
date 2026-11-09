using System.Text.Json;

namespace PCSetupCopier;

// Presets are pending preferences, never system-write commands.
// Optional fields preserve compatibility with existing mouse-only M2.x presets.
internal sealed record UserPreset(
    string Name, bool MouseAcceleration, DateTimeOffset SavedAt,
    bool? IncludeMouse = null, bool? IncludePower = null,
    Guid? PowerPlanId = null, string? ComputerName = null,
    bool? IncludeGameMode = null, bool? GameModeDesired = null,
    bool? IncludeCapture = null, bool? CaptureDesired = null,
    bool? IncludeGameBarCapture = null, bool? GameBarCaptureDesired = null);

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
            if (preset.PowerPlanId == Guid.Empty || preset.ComputerName?.Length > 255)
                throw new InvalidDataException("プリセットの電源プラン情報が不正です。");
        }
        if (list.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != list.Count)
            throw new InvalidDataException("プリセット名が重複しています。");
        return list.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    internal static void Save(string name, bool mouseAcceleration) =>
        Save(name, mouseAcceleration, true, false, null, null);

    internal static void Save(
        string name, bool mouseAcceleration, bool includeMouse,
        bool includePower, Guid? powerPlanId, string? computerName,
        bool includeGameMode = false, bool gameModeDesired = false,
        bool includeCapture = false, bool captureDesired = false,
        bool includeGameBarCapture = false, bool gameBarCaptureDesired = false)
    {
        if (includePower && (powerPlanId == null || powerPlanId == Guid.Empty ||
            string.IsNullOrWhiteSpace(computerName)))
            throw new ArgumentException("電源プランの保存に必要なPC情報が不足しています。");
        if (computerName?.Length > 255)
            throw new ArgumentException("PC名が長すぎます。");
        name = name.Trim();
        ValidateName(name);
        var list = Load().ToList();
        int existing = list.FindIndex(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        var entry = new UserPreset(name, mouseAcceleration, DateTimeOffset.Now,
            includeMouse, includePower, powerPlanId, computerName,
            includeGameMode, gameModeDesired, includeCapture, captureDesired,
            includeGameBarCapture, gameBarCaptureDesired);
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

    internal static void Export(string name, string destination)
    {
        var preset = Load().FirstOrDefault(x =>
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException("選択したプリセットが見つかりません。");
        if (!destination.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("JSON形式で書き出してください。");
        File.WriteAllText(destination, JsonSerializer.Serialize(preset, JsonOptions));
    }

    internal static string Import(string source)
    {
        if (new FileInfo(source).Length is < 2 or > 50_000)
            throw new InvalidDataException("プリセットファイルのサイズが不正です。");
        var item = JsonSerializer.Deserialize<UserPreset>(File.ReadAllText(source))
            ?? throw new InvalidDataException("プリセットファイルの形式が不正です。");
        ValidateName(item.Name);
        if (item.PowerPlanId == Guid.Empty || item.ComputerName?.Length > 255 ||
            item.SavedAt == default)
            throw new InvalidDataException("プリセットの内容が不正です。");
        var list = Load().ToList();
        if (list.Any(x => string.Equals(x.Name, item.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("同名のプリセットが既にあります。名前を変えてから読み込んでください。");
        if (list.Count >= 100) throw new InvalidOperationException("プリセットは100件まで保存できます。");
        list.Add(item);
        Commit(list);
        return item.Name;
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
            var sample = new UserPreset("Gaming", false, DateTimeOffset.UnixEpoch,
                true, true, Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e"), "Test-PC",
                true, true, false, false, true, false);
            if (JsonSerializer.Deserialize<UserPreset>(JsonSerializer.Serialize(sample)) != sample)
                return false;
            // Old mouse-only presets must remain loadable and never auto-select power.
            const string legacy = "{\"Name\":\"Old\",\"MouseAcceleration\":true,\"SavedAt\":\"2025-01-01T00:00:00+00:00\"}";
            var old = JsonSerializer.Deserialize<UserPreset>(legacy);
            return old?.MouseAcceleration == true && old.IncludeMouse == null &&
                   old.IncludePower == null && old.PowerPlanId == null &&
                   old.IncludeGameMode == null && old.IncludeCapture == null &&
                   old.IncludeGameBarCapture == null;
        }
        catch { return false; }
    }
}
