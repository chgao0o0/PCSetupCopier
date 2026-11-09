using System.Text.Json;

namespace PCSetupCopier;

// Staged NVIDIA global settings and local user templates.
// Loading/saving a template never writes to NVIDIA.
internal static class NvidiaProfileStore
{
    internal sealed record Template(string Name, Dictionary<string, uint> Values);
    private static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCSetupCopier", "Presets");
    private static string FileName => Path.Combine(Folder, "nvidia-personal.json");

    internal static IReadOnlyList<Template> Load()
    {
        if (!File.Exists(FileName)) return [];
        if (new FileInfo(FileName).Length > 80_000)
            throw new InvalidDataException("NVIDIAプリセットファイルが大きすぎます。");
        var items = JsonSerializer.Deserialize<List<Template>>(File.ReadAllText(FileName))
            ?? throw new InvalidDataException("NVIDIAプリセットが不正です。");
        if (items.Count > 50) throw new InvalidDataException("NVIDIAプリセットは50件までです。");
        foreach (var item in items) Validate(item);
        return items;
    }

    private static void Validate(Template item)
    {
        if (string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 40 ||
            item.Name.Any(x => char.IsControl(x) || x is '/' or '\\') ||
            item.Values.Count is < 1 or > 4)
            throw new InvalidDataException("NVIDIAプリセットの内容が不正です。");
        foreach (var (key, value) in item.Values)
        {
            var setting = NvidiaGlobalDrs.Settings.FirstOrDefault(x => x.Key == key);
            if (setting == null || !setting.Options.Any(x => x.Value == value))
                throw new InvalidDataException("未対応のNVIDIA設定値です。");
        }
    }

    internal static void Save(string name, IReadOnlyDictionary<string, uint> values)
    {
        var item = new Template(name.Trim(), new(values));
        Validate(item);
        var items = Load().ToList();
        int ix = items.FindIndex(x => string.Equals(x.Name, item.Name, StringComparison.OrdinalIgnoreCase));
        if (ix < 0) items.Add(item); else items[ix] = item;
        if (items.Count > 50) throw new InvalidDataException("NVIDIAプリセットは50件までです。");
        Directory.CreateDirectory(Folder);
        string temp = Path.Combine(Folder, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(items,
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, FileName, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal static bool VerifyContract()
    {
        try
        {
            var test = new Template("Gaming", new() { ["power"] = 1, ["fps"] = 165 });
            Validate(test);
            var json = JsonSerializer.Serialize(test);
            return JsonSerializer.Deserialize<Template>(json)?.Values["power"] == 1;
        }
        catch { return false; }
    }
}
