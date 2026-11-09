using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace PCSetupCopier;

// Read-only launcher-independent discovery sources.
// Treat every executable as an untrusted candidate, not a verified game.
internal static class GeneralGameDiscovery
{
    private static string RootsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCSetupCopier", "Games", "scan-folders.json");

    internal static IReadOnlyList<string> LoadFolders()
    {
        if (!File.Exists(RootsFile)) return [];
        if (new FileInfo(RootsFile).Length > 20_000)
            throw new InvalidDataException("フォルダー登録ファイルが大きすぎます。");
        string[] paths = JsonSerializer.Deserialize<string[]>(File.ReadAllText(RootsFile)) ?? [];
        return paths.Where(IsSafeRoot).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray();
    }

    internal static void AddFolder(string path)
    {
        if (!IsSafeRoot(path)) throw new ArgumentException(
            "ゲームを置いたフォルダーを選んでください。ドライブ全体やWindowsフォルダーは対象外です。");
        var roots = LoadFolders().Append(Path.GetFullPath(path))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (roots.Length > 12) throw new InvalidOperationException("追加できるフォルダーは12件までです。");
        Directory.CreateDirectory(Path.GetDirectoryName(RootsFile)!);
        string temp = RootsFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(roots));
            File.Move(temp, RootsFile, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static bool IsSafeRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 512 || !Path.IsPathFullyQualified(path))
            return false;
        try
        {
            string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            if (!Directory.Exists(full) || full.StartsWith(@"\\", StringComparison.Ordinal))
                return false;
            string root = Path.GetPathRoot(full)!.TrimEnd(Path.DirectorySeparatorChar);
            if (full.Equals(root, StringComparison.OrdinalIgnoreCase)) return false;
            if (full.Equals(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                StringComparison.OrdinalIgnoreCase)) return false;
            if (full.Equals(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }
        catch { return false; }
    }

    internal static IReadOnlyList<GameDiscovery.Candidate> Find()
    {
        var result = new List<GameDiscovery.Candidate>();
        InstalledApps(result);
        Shortcuts(result);
        Folders(result);
        return result;
    }

    // Windows uninstall registrations cover games installed by EA, Ubisoft, GOG,
    // Battle.net and standalone installers when the publisher registers them.
    private static void InstalledApps(List<GameDiscovery.Candidate> output)
    {
        const string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive == Registry.CurrentUser ?
                        RegistryHive.CurrentUser : RegistryHive.LocalMachine, view);
                    using var uninstall = baseKey.OpenSubKey(path, false);
                    if (uninstall == null) continue;
                    foreach (string entry in uninstall.GetSubKeyNames().Take(2000))
                    {
                        try
                        {
                            using var key = uninstall.OpenSubKey(entry, false);
                            if (key == null) continue;
                            string title = key.GetValue("DisplayName") as string ?? "";
                            if (title.Length is < 2 or > 150 || !MayBeGame(title)) continue;
                            string? install = key.GetValue("InstallLocation") as string;
                            string? icon = key.GetValue("DisplayIcon") as string;
                            if (icon != null)
                            {
                                string pathGuess = icon.Trim().Trim('"');
                                int exe = pathGuess.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                                if (exe >= 0) pathGuess = pathGuess[..(exe + 4)];
                                AddCandidate(output, title, pathGuess, "インストール情報", true);
                            }
                            if (!string.IsNullOrWhiteSpace(install))
                            {
                                foreach (var exe in Executables(install, 45).Take(2))
                                    AddCandidate(output, title, exe, "インストール情報", true);
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }
    }

    private static bool MayBeGame(string title)
    {
        string low = title.ToLowerInvariant();
        if (new[] { "redistributable", "runtime", "driver", "visual c++", "microsoft .net",
            "update", "security", "framework", "sdk", "uninstall", "anti-cheat",
            "anticheat", "directx", "openal", "physx", "webview", "language pack" }
            .Any(low.Contains)) return false;
        return true;
    }

    // Windows Start Menu and Desktop launch shortcuts; protocol and web links are excluded.
    private static void Shortcuts(List<GameDiscovery.Candidate> output)
    {
        string commonStart = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
        var folders = new[] {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            commonStart,
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        };
        foreach (string folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(folder)) continue;
            // Bounded breadth-first traversal; never crawl arbitrary folders.
            var queue = new Queue<(string Path, int Depth)>();
            queue.Enqueue((folder, 0));
            int seen = 0;
            while (queue.Count > 0 && seen < 1800)
            {
                var (dir, depth) = queue.Dequeue();
                try
                {
                    foreach (string shortcut in Directory.EnumerateFiles(dir, "*.lnk").Take(180))
                    {
                        if (++seen > 1800) break;
                        try
                        {
                            string? target = ReadShortcut(shortcut);
                            if (target != null)
                                AddCandidate(output, Path.GetFileNameWithoutExtension(shortcut),
                                    target, "ショートカット", true);
                        }
                        catch { }
                    }
                    if (depth >= 3) continue;
                    foreach (string sub in Directory.EnumerateDirectories(dir).Take(120))
                        queue.Enqueue((sub, depth + 1));
                }
                catch { }
            }
        }
    }

    private static string? ReadShortcut(string file)
    {
        object? shell = null, shortcut = null;
        try
        {
            Type? type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return null;
            shell = Activator.CreateInstance(type);
            if (shell == null) return null;
            shortcut = type.InvokeMember("CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod, null, shell, [file]);
            if (shortcut == null) return null;
            var target = shortcut.GetType().InvokeMember("TargetPath",
                System.Reflection.BindingFlags.GetProperty, null, shortcut, null) as string;
            return target;
        }
        finally
        {
            if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
            if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }

    private static void Folders(List<GameDiscovery.Candidate> output)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in LoadFolders()) roots.Add(path);
        foreach (var drive in DriveInfo.GetDrives().Where(x => x.DriveType == DriveType.Fixed))
        {
            foreach (string name in new[] { "Games", "Game", "Installed Games", "GOG Games",
                "Battle.net", "Riot Games", "Ubisoft", "EA Games", "Electronic Arts",
                "XboxGames" })
            {
                string path = Path.Combine(drive.RootDirectory.FullName, name);
                if (Directory.Exists(path)) roots.Add(path);
            }
        }
        foreach (string root in roots.Take(30))
        {
            try
            {
                foreach (var exe in Executables(root, 150))
                    AddCandidate(output, Path.GetFileNameWithoutExtension(exe), exe,
                        "ゲームフォルダー", true);
                foreach (var sub in Directory.EnumerateDirectories(root).Take(65))
                    {
                    foreach (var exe in Executables(sub, 50))
                        AddCandidate(output, Path.GetFileName(sub), exe, "ゲームフォルダー", true);
                    // Common game subdirectories (e.g. VALORANT/live/ShooterGame/Binaries/Win64).
                    // Restrict traversal to known executable locations and two nested levels.
                    foreach (string childName in new[] { "live", "Game", "Client", "ShooterGame",
                        "Bin", "bin", "Retail", "_retail_" })
                    {
                        string child = Path.Combine(sub, childName);
                        if (!Directory.Exists(child)) continue;
                        foreach (var exe in Executables(child, 25))
                            AddCandidate(output, Path.GetFileName(sub), exe, "ゲームフォルダー", true);
                        string binaries = Path.Combine(child, "ShooterGame", "Binaries", "Win64");
                        if (Directory.Exists(binaries))
                            foreach (var exe in Executables(binaries, 25))
                                AddCandidate(output, Path.GetFileName(sub), exe, "ゲームフォルダー", true);
                    }
                }
            }
            catch { }
        }
    }

    private static IEnumerable<string> Executables(string folder, int max)
    {
        if (!Directory.Exists(folder)) yield break;
        var paths = new List<string> { folder };
        foreach (string relative in new[] { "Binaries", @"Binaries\Win64", @"Game\Binaries\Win64",
            "bin", @"bin\x64", "x64", "Win64", @"Engine\Binaries\Win64",
            @"ShooterGame\Binaries\Win64", @"live\ShooterGame\Binaries\Win64" })
        {
            string sub = Path.Combine(folder, relative);
            if (Directory.Exists(sub)) paths.Add(sub);
        }
        int scanned = 0;
        foreach (string path in paths)
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(path, "*.exe").Take(max).ToArray(); }
            catch { continue; }
            foreach (string exe in files)
            {
                if (++scanned > max) yield break;
                if (NotGameExe(exe)) continue;
                yield return exe;
            }
        }
    }

    private static bool NotGameExe(string exe)
    {
        string name = Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();
        return new[] { "unins", "uninstall", "setup", "installer", "crash", "report",
            "anticheat", "easyanticheat", "battleye", "redistributable",
            "dxsetup", "unitycrashhandler", "vc_redist", "updater", "repair",
            "bootstrapper", "cef", "webview", "helper", "service" }.Any(name.Contains);
    }

    private static void AddCandidate(List<GameDiscovery.Candidate> output,
        string title, string? exe, string source, bool uncertain)
    {
        if (output.Count >= 700 || string.IsNullOrWhiteSpace(exe) ||
            !Path.IsPathFullyQualified(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            NotGameExe(exe)) return;
        try
        {
            string full = Path.GetFullPath(exe);
            if (!full.StartsWith(@"\\", StringComparison.Ordinal) && File.Exists(full))
                output.Add(new GameDiscovery.Candidate(title, full, source, uncertain));
        }
        catch { }
    }

    internal static bool VerifyContract() =>
        !MayBeGame("Microsoft Visual C++ Redistributable") &&
        MayBeGame("Example Game") &&
        NotGameExe("EasyAntiCheat.exe") && !NotGameExe("ExampleGame-Win64-Shipping.exe");
}
