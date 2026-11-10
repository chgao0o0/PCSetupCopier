using System.Text.Json;

namespace PCSetupCopier;

// Portable read-only snapshot. Importing it only stages supported desired values.
// It is intentionally not a raw driver-profile backup and contains no personal paths.
internal static class NvidiaQuickTransfer
{
    private const string Format = "PCSetupCopier.NvidiaGlobalTransfer";
    private const int Revision = 1;

    internal sealed record TransferData(
        string Format, int Revision, DateTimeOffset CapturedAt,
        Dictionary<string, uint> Values);

    internal sealed record TransferResult(
        IReadOnlyDictionary<string, uint> Values, int Unrecognized);

    internal static int ExportCurrent(string path)
    {
        var states = NvidiaGlobalDrs.ReadAll().ToDictionary(x => x.Key, StringComparer.Ordinal);
        var values = new Dictionary<string, uint>(StringComparer.Ordinal);
        foreach (var item in NvidiaGlobalDrs.Settings)
        {
            if (!states.TryGetValue(item.Key, out var state) ||
                !state.Available || !state.Value.HasValue) continue;
            uint value = state.Value.Value;
            if (item.Options.Any(x => x.Value == value))
                values.Add(item.Key, value);
        }
        if (values.Count == 0)
            throw new NotSupportedException(
                "このPCのNVIDIAドライバーから移植できる既知の設定値を取得できませんでした。");
        Write(path, new TransferData(Format, Revision, DateTimeOffset.Now, values));
        return values.Count;
    }

    private static void Write(string path, TransferData data)
    {
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("JSON形式で保存してください。");
        string json = JsonSerializer.Serialize(data,
            new JsonSerializerOptions { WriteIndented = true });
        // Write temp next to destination, then rename. Never write a partial export.
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, json);
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal static TransferResult Import(string path)
    {
        if (new FileInfo(path).Length is < 16 or > 100_000)
            throw new InvalidDataException("NVIDIA移植ファイルのサイズが不正です。");
        var data = JsonSerializer.Deserialize<TransferData>(File.ReadAllText(path))
            ?? throw new InvalidDataException("NVIDIA移植ファイルを読み込めません。");
        return Validate(data);
    }

    private static TransferResult Validate(TransferData data)
    {
        if (data.Format != Format || data.Revision != Revision ||
            data.Values == null || data.Values.Count is < 1 or > 100 ||
            data.CapturedAt == default)
            throw new InvalidDataException("対応していないNVIDIA移植ファイルです。");

        var supported = NvidiaGlobalDrs.Settings.ToDictionary(x => x.Key, StringComparer.Ordinal);
        var result = new Dictionary<string, uint>(StringComparer.Ordinal);
        int excluded = 0;
        foreach (var (key, value) in data.Values)
        {
            if (key == null || key.Length > 80)
                throw new InvalidDataException("設定名が不正です。");
            if (supported.TryGetValue(key, out var item) &&
                item.Options.Any(x => x.Value == value))
                result.Add(key, value);
            else excluded++;
        }
        if (result.Count == 0)
            throw new InvalidDataException("この版で取り込めるNVIDIA設定がありません。");
        return new TransferResult(result, excluded);
    }

    internal static bool VerifyContract()
    {
        try
        {
            var test = new TransferData(Format, Revision, DateTimeOffset.UnixEpoch,
                new() { ["power"] = 1, ["vsync"] = 0x60925292, ["unknown"] = 999 });
            var restored = JsonSerializer.Deserialize<TransferData>(
                JsonSerializer.Serialize(test))!;
            var result = Validate(restored);
            if (result.Values.Count != 2 || result.Unrecognized != 1 ||
                result.Values["power"] != 1) return false;
            bool Reject(TransferData invalid)
            {
                try { Validate(invalid); return false; }
                catch (InvalidDataException) { return true; }
            }
            return Reject(test with { Format = "unknown" }) &&
                Reject(test with { Revision = 2 }) &&
                Reject(test with { Values = new Dictionary<string, uint>() }) &&
                Reject(test with { Values = new Dictionary<string, uint> {
                    ["power"] = uint.MaxValue
                } });
        }
        catch { return false; }
    }
}
