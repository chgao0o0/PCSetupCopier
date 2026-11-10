using System.Text.Json;

namespace PCSetupCopier;

// One portable, explicitly staged transfer for supported Windows and NVIDIA preferences.
// The file is never an instruction to apply changes, and never contains game executable paths.
internal static class GamingTransferBundle
{
    private const string Format = "PCSetupCopier.GamingTransfer";
    private const int Version = 1;

    internal sealed record WindowsPreferences(
        bool? MouseAcceleration, bool? GameMode,
        bool? BackgroundCapture, bool? GameBarCapture, Guid? PowerScheme);
    internal sealed record Bundle(
        string Format, int Version, DateTimeOffset CapturedAt,
        WindowsPreferences Windows, Dictionary<string, uint> Nvidia);
    internal sealed record CaptureResult(Bundle Data, string[] Warnings);
    internal sealed record ValidationResult(Bundle Data, string[] Warnings);

    internal static CaptureResult Capture()
    {
        var notes = new List<string>();
        T? TryRead<T>(string label, Func<T> read) where T : struct
        {
            try { return read(); }
            catch (Exception ex)
            {
                notes.Add(label + "を取得できません: " + ex.Message);
                return null;
            }
        }

        int[]? mouse = null;
        try { mouse = SettingsApplier.Read(); }
        catch (Exception ex) { notes.Add("マウス設定: " + ex.Message); }

        bool? ToBool(int? value) => value is 0 ? false : value is 1 ? true : null;

        int? game = ReadPreference("ゲームモード", GameModeManager.Read, notes);
        int? capture = ReadPreference("バックグラウンド録画", CapturePreferenceManager.Read, notes);
        int? gameBar = ReadPreference("Game Bar録画", GameBarCaptureManager.Read, notes);

        var windows = new WindowsPreferences(
            mouse == null ? null : mouse[2] != 0,
            ToBool(game), ToBool(capture), ToBool(gameBar),
            TryRead("電源プラン", PowerPlanManager.ReadActive));

        var nvidia = new Dictionary<string, uint>(StringComparer.Ordinal);
        try
        {
            var states = NvidiaGlobalDrs.ReadAll().ToDictionary(x => x.Key, StringComparer.Ordinal);
            foreach (var setting in NvidiaGlobalDrs.Settings)
                if (states.TryGetValue(setting.Key, out var state) && state.Available &&
                    state.Value is uint value && setting.Options.Any(o => o.Value == value))
                    nvidia[setting.Key] = value;
        }
        catch (Exception ex)
        {
            notes.Add("NVIDIA: " + ex.Message);
        }
        if (mouse is null && game is null && capture is null && gameBar is null &&
            windows.PowerScheme is null && nvidia.Count == 0)
            throw new InvalidOperationException("移植に使える設定値を取得できませんでした。");

        return new CaptureResult(new Bundle(Format, Version, DateTimeOffset.Now, windows, nvidia),
            notes.ToArray());
    }

    private static int? ReadPreference(string label, Func<int?> getter, List<string> warnings)
    {
        try { return getter(); }
        catch (Exception ex)
        {
            warnings.Add(label + ": " + ex.Message);
            return null;
        }
    }

    internal static void Export(string path, Bundle bundle)
    {
        Validate(bundle);
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("JSONファイルを選択してください。");
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(bundle,
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static ValidationResult Import(string path)
    {
        if (new FileInfo(path).Length is < 20 or > 120_000)
            throw new InvalidDataException("設定移植ファイルが大きすぎるか無効です。");
        var bundle = JsonSerializer.Deserialize<Bundle>(File.ReadAllText(path))
            ?? throw new InvalidDataException("移植ファイルを解析できません。");
        return Validate(bundle);
    }

    private static ValidationResult Validate(Bundle bundle)
    {
        if (bundle.Format != Format || bundle.Version != Version ||
            bundle.CapturedAt == default || bundle.Windows == null ||
            bundle.Nvidia == null || bundle.Nvidia.Count > 100)
            throw new InvalidDataException("対応していない設定移植ファイルです。");
        var warnings = new List<string>();
        var settings = NvidiaGlobalDrs.Settings.ToDictionary(x => x.Key, StringComparer.Ordinal);
        var accepted = new Dictionary<string, uint>(StringComparer.Ordinal);
        foreach (var (key, value) in bundle.Nvidia)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > 80)
                throw new InvalidDataException("設定名が不正です。");
            if (settings.TryGetValue(key, out var item) &&
                item.Options.Any(x => x.Value == value))
                accepted.Add(key, value);
            else warnings.Add("非対応NVIDIA設定は除外: " + key);
        }
        if (bundle.Windows.PowerScheme == Guid.Empty)
            throw new InvalidDataException("電源プランIDが不正です。");
        return new ValidationResult(bundle with { Nvidia = accepted }, warnings.ToArray());
    }

    internal static bool VerifyContract()
    {
        try
        {
            var sample = new Bundle(Format, Version, DateTimeOffset.UnixEpoch,
                new WindowsPreferences(false, true, null, false,
                    Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e")),
                new() { ["power"] = 1, ["unknown"] = 800 });
            var restored = JsonSerializer.Deserialize<Bundle>(JsonSerializer.Serialize(sample))!;
            var validated = Validate(restored);
            return validated.Data.Nvidia.Count == 1 &&
                validated.Warnings.Length == 1 && validated.Data.Windows.MouseAcceleration == false;
        }
        catch { return false; }
    }
}
