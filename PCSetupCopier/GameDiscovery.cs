using Microsoft.Win32;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PCSetupCopier;

// Read-only detection. Candidate executables require explicit user confirmation.
// Steam/Epic manifests may include third-party metadata; never execute their contents.
internal static class GameDiscovery
{
    internal sealed record Candidate(string Title, string Exe, string Store, bool Ambiguous);

    internal static IReadOnlyList<Candidate> Find()
    {
        var all = new List<Candidate>();
        try { Steam(all); } catch { }
        try { Epic(all); } catch { }
        try { all.AddRange(GeneralGameDiscovery.Find()); } catch { }
        return all.Where(x => File.Exists(x.Exe))
            .GroupBy(x => x.Exe, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderBy(y => y.Ambiguous).First()).Take(300)
            .OrderBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static string Unescape(string s) => s.Replace(@"\\", @"\");

    private static string? GetVdf(string text, string key)
    {
        var match = Regex.Match(text, "\"" + Regex.Escape(key) +
            "\"\\s*\"((?:\\\\.|[^\"])*)\"", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        return match.Success ? Unescape(match.Groups[1].Value) : null;
    }

    private static IEnumerable<string> SteamRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? value = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath")?.ToString();
        if (!string.IsNullOrWhiteSpace(value)) roots.Add(value);
        value = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamExe")?.ToString();
        if (!string.IsNullOrWhiteSpace(value)) roots.Add(Path.GetDirectoryName(value)!);
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        foreach (string root in roots)
            if (Directory.Exists(root)) yield return root;
    }

    private static void Steam(List<Candidate> result)
    {
        foreach (string root in SteamRoots())
        {
            var libs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
            string folderFile = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (File.Exists(folderFile) && new FileInfo(folderFile).Length < 1_000_000)
            {
                string vdf = File.ReadAllText(folderFile);
                foreach (Match match in Regex.Matches(vdf, @"""path""\s*""((?:\\.|[^""])*)""",
                    RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)))
                {
                    string path = Unescape(match.Groups[1].Value);
                    if (Directory.Exists(path)) libs.Add(path);
                }
            }
            foreach (string lib in libs)
            {
                string apps = Path.Combine(lib, "steamapps");
                if (!Directory.Exists(apps)) continue;
                foreach (string manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf").Take(800))
                {
                    try
                    {
                        if (new FileInfo(manifest).Length > 150_000) continue;
                        string raw = File.ReadAllText(manifest);
                        string? title = GetVdf(raw, "name"), dir = GetVdf(raw, "installdir");
                        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(dir) ||
                            Path.IsPathRooted(dir) || dir.Contains("..")) continue;
                        string installed = Path.Combine(apps, "common", dir);
                        var candidates = FindExecutables(installed);
                        foreach (var exe in candidates.Take(1))
                            result.Add(new Candidate(title, exe, "Steam", candidates.Count > 1));
                    }
                    catch { }
                }
            }
        }
    }

    private sealed record EpicItem(string? DisplayName, string? InstallLocation, string? LaunchExecutable);
    private static void Epic(List<Candidate> result)
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(folder)) return;
        foreach (string manifest in Directory.EnumerateFiles(folder, "*.item").Take(600))
        {
            try
            {
                if (new FileInfo(manifest).Length > 500_000) continue;
                using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
                var root = doc.RootElement;
                string? title = Read(root, "DisplayName");
                string? installed = Read(root, "InstallLocation");
                string? launch = Read(root, "LaunchExecutable");
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(installed) ||
                    !Path.IsPathFullyQualified(installed) || !Directory.Exists(installed)) continue;
                if (launch != null && !Path.IsPathRooted(launch) && !launch.Contains(".."))
                {
                    string path = Path.GetFullPath(Path.Combine(installed, launch));
                    if (path.StartsWith(Path.GetFullPath(installed) + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase) && File.Exists(path) &&
                        path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(new Candidate(title, path, "Epic", false));
                        continue;
                    }
                }
                var candidates = FindExecutables(installed);
                foreach (var exe in candidates.Take(1))
                    result.Add(new Candidate(title, exe, "Epic", candidates.Count > 1));
            }
            catch { }
        }
    }

    private static string? Read(JsonElement obj, string property) =>
        obj.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() : null;

    private static List<string> FindExecutables(string root)
    {
        var found = new List<string>();
        if (!Directory.Exists(root)) return found;
        try
        {
            // Deliberately bounded to familiar Windows game build paths.
            var dirs = new List<string> { root };
            foreach (string name in new[] { "Binaries", "Binaries\\Win64", "bin", "bin\\x64",
                                              "Win64", "x64", "Game\\Binaries\\Win64" })
            {
                string sub = Path.Combine(root, name);
                if (Directory.Exists(sub)) dirs.Add(sub);
            }
            foreach (string sub in Directory.EnumerateDirectories(root).Take(15))
            {
                string bins = Path.Combine(sub, "Binaries", "Win64");
                if (Directory.Exists(bins)) dirs.Add(bins);
            }
            foreach (string dir in dirs.Distinct(StringComparer.OrdinalIgnoreCase))
                foreach (string exe in Directory.EnumerateFiles(dir, "*.exe").Take(35))
                    if (!IsHelper(Path.GetFileNameWithoutExtension(exe))) found.Add(exe);
        }
        catch { }
        return found.OrderByDescending(x => Path.GetFileNameWithoutExtension(x)
                .Equals(Path.GetFileName(root), StringComparison.OrdinalIgnoreCase))
            .ThenBy(x => x.Count(c => c == Path.DirectorySeparatorChar))
            .ThenBy(x => x.Length).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();
    }

    private static bool IsHelper(string name)
    {
        string n = name.ToLowerInvariant();
        return new[] { "unins", "setup", "installer", "crash", "report", "anticheat",
            "easyanticheat", "eac", "battleye", "redistributable", "dxsetup",
            "unitycrashhandler", "vc_redist", "launcher", "updater", "repair" }
            .Any(n.Contains);
    }

    internal static bool VerifyContract()
    {
        const string vdf = "\"name\" \"Example\" \"installdir\" \"ExampleGame\"";
        return GetVdf(vdf, "name") == "Example" &&
            GetVdf(vdf, "installdir") == "ExampleGame" &&
            IsHelper("EasyAntiCheat_Setup") && !IsHelper("game-win64-shipping") &&
            GeneralGameDiscovery.VerifyContract();
    }
}
