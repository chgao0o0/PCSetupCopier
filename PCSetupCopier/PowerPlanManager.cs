using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PCSetupCopier;

// Switches only to an existing local power scheme. Never creates/imports schemes.
internal static class PowerPlanManager
{
    internal sealed record Plan(Guid Id, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record Backup(Guid Previous, DateTimeOffset CapturedAt);
    private static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCSetupCopier", "Backups");
    private static string Latest => Path.Combine(Folder, "power-plan-latest.json");
    internal static bool CanRestore => File.Exists(Latest);

    internal static Guid ReadActive()
    {
        IntPtr pointer = IntPtr.Zero;
        try
        {
            uint status = PowerGetActiveScheme(IntPtr.Zero, out pointer);
            if (status != 0 || pointer == IntPtr.Zero)
                throw new InvalidOperationException("電源プランを取得できません。エラー: " + status);
            return Marshal.PtrToStructure<Guid>(pointer);
        }
        finally
        {
            if (pointer != IntPtr.Zero) LocalFree(pointer);
        }
    }

    internal static IReadOnlyList<Plan> Installed()
    {
        string output = Powercfg("/list");
        var plans = new List<Plan>();
        var pattern = new Regex(
            @"([a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12})",
            RegexOptions.Compiled);
        foreach (string line in output.Split('\n'))
        {
            var match = pattern.Match(line);
            if (!match.Success || !Guid.TryParse(match.Value, out var id)) continue;
            string label = id.ToString("D");
            int start = line.IndexOf('(', match.Index + match.Length);
            int end = start < 0 ? -1 : line.IndexOf(')', start + 1);
            if (start >= 0 && end > start)
                label = line.Substring(start + 1, end - start - 1).Trim();
            if (label.Length == 0 || label.Length > 80) label = id.ToString("D");
            if (!plans.Any(x => x.Id == id)) plans.Add(new Plan(id, label + "  [" + id.ToString("D")[..8] + "]"));
        }
        if (plans.Count == 0)
            throw new InvalidOperationException("利用可能な電源プランを検出できませんでした。");
        return plans;
    }

    private static string Powercfg(string command, string? argument = null)
    {
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "powercfg.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        psi.ArgumentList.Add(command);
        if (argument != null) psi.ArgumentList.Add(argument);
        using var process = Process.Start(psi) ??
            throw new InvalidOperationException("powercfg.exeを開始できません。");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("電源設定の確認が時間切れになりました。");
        }
        if (process.ExitCode != 0)
            throw new InvalidOperationException("電源設定コマンド失敗 (終了コード " + process.ExitCode + "): " +
                stderr.GetAwaiter().GetResult()[..Math.Min(200, stderr.GetAwaiter().GetResult().Length)]);
        return stdout.GetAwaiter().GetResult();
    }

    private static void Set(Guid id)
    {
        Powercfg("/setactive", id.ToString("D"));
        if (ReadActive() != id)
            throw new InvalidOperationException("電源プランの変更後検証に失敗しました。");
    }

    internal static string Apply(Guid target)
    {
        if (!Installed().Any(p => p.Id == target))
            throw new InvalidOperationException("このPCに存在しない電源プランは適用できません。");
        Guid previous = ReadActive();
        if (previous == target) return "現在と同じ電源プランです。";
        Directory.CreateDirectory(Folder);
        string json = JsonSerializer.Serialize(new Backup(previous, DateTimeOffset.Now));
        string archive = Path.Combine(Folder, "power-plan-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + ".json");
        File.WriteAllText(archive, json);
        File.WriteAllText(Latest, json);
        try { Set(target); }
        catch
        {
            try { Set(previous); }
            catch (Exception e)
            {
                throw new InvalidOperationException("電源プラン変更と自動復元に失敗しました。バックアップ: " + archive, e);
            }
            throw;
        }
        return "電源プランを変更し、再取得で確認しました。変更前のプランはバックアップ済みです。";
    }

    // Used only for rollback within the same confirmed batch.
    internal static void RestoreExact(Guid original)
    {
        if (!Installed().Any(x => x.Id == original))
            throw new InvalidOperationException("元の電源プランを復元できません。");
        Set(original);
    }

    internal static string Restore()
    {
        if (!CanRestore) throw new FileNotFoundException("電源プランのバックアップがありません。");
        var backup = JsonSerializer.Deserialize<Backup>(File.ReadAllText(Latest)) ??
            throw new InvalidDataException("バックアップが不正です。");
        if (!Installed().Any(p => p.Id == backup.Previous))
            throw new InvalidOperationException("元の電源プランがこのPCに存在しません。");
        Guid before = ReadActive();
        try { Set(backup.Previous); }
        catch
        {
            try { Set(before); } catch { }
            throw;
        }
        return "バックアップの電源プランに復元しました。";
    }

    // Runs on the Windows CI runner without changing its power settings.
    internal static bool VerifyContract()
    {
        try
        {
            var plans = Installed();
            var active = ReadActive();
            return plans.Count > 0 && plans.Any(x => x.Id == active) &&
                   plans.All(x => x.Id != Guid.Empty);
        }
        catch { return false; }
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr userRoot, out IntPtr policy);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr pointer);
}
