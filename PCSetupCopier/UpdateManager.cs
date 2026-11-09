using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace PCSetupCopier;

/// <summary>Private GitHub repository updates through the user's authenticated GitHub CLI.</summary>
internal static class UpdateManager
{
    private const string Repository = "chgao0o0/PCSetupCopier";
    private const string Artifact = "PCSetupCopier-win-x64";
    private const string ExeName = "PCSetupCopier.exe";

    internal static async Task<string> CheckAndStageAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        // Installed, packaged builds have a build-info marker. Never self-update a dotnet run/dev copy.
        var installedMarker = Path.Combine(AppContext.BaseDirectory, "build-info.json");
        if (!File.Exists(installedMarker))
            throw new InvalidOperationException("開発用起動では更新できません。GitHub Actionsから入手した配布版で実行してください。");

        using var elapsed = new UpdateStopwatch(progress);
        using var installed = JsonDocument.Parse(await File.ReadAllTextAsync(installedMarker, cancellationToken));
        var oldSha = installed.RootElement.GetProperty("commit").GetString() ?? "";

        // Independent GitHub requests are run concurrently. The GitHub CLI reports
        // authentication errors itself; a separate "gh auth status" is redundant.
        elapsed.Report("GitHubで最新版を確認しています…");
        Task<string> runsTask = GhAsync(
            ["run", "list", "-R", Repository, "--workflow", "build-windows.yml",
             "--branch", "main", "--status", "success", "--limit", "1",
             "--json", "databaseId,headSha"], cancellationToken);
        Task<string> branchTask = GhAsync(
            ["api", "repos/chgao0o0/PCSetupCopier/commits/main", "--jq", ".sha"],
            cancellationToken);
        await Task.WhenAll(runsTask, branchTask);
        using var runs = JsonDocument.Parse(await runsTask);
        if (runs.RootElement.GetArrayLength() == 0)
            throw new InvalidOperationException("成功済みのWindowsビルドが見つかりません。");
        string branchSha = (await branchTask).Trim();
        if (branchSha.Length != 40 || !branchSha.All(Uri.IsHexDigit))
            throw new InvalidDataException("mainのコミットIDを取得できませんでした。");

        var run = runs.RootElement[0];
        var latestSha = run.GetProperty("headSha").GetString() ?? "";
        var runId = run.GetProperty("databaseId").GetInt64();
        if (latestSha.Length != 40)
            throw new InvalidDataException("GitHubから正しいコミットIDを取得できませんでした。");
        if (!string.Equals(latestSha, branchSha, StringComparison.OrdinalIgnoreCase))
            return "最新版のビルドはまだ成功していません。古いビルドを最新版として扱わず、GitHub Actionsの完了を確認してください。";
        if (string.Equals(oldSha, latestSha, StringComparison.OrdinalIgnoreCase))
            return "すでに最新版です。";

        var answer = MessageBox.Show(
            $"最新版が見つかりました。\n\n現在: {oldSha[..Math.Min(8, oldSha.Length)]}\n最新: {latestSha[..8]}\n\n更新しますか？",
            "PC Setup Copier", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return "更新をキャンセルしました。";

        // Only the authenticated GitHub Actions artifact is accepted.
        string stage = Path.Combine(Path.GetTempPath(), "PCSetupCopierUpdate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            elapsed.Report("更新パッケージをダウンロードしています…");
            await GhAsync(["run", "download", runId.ToString(), "-R", Repository,
                "-n", Artifact, "-D", stage], cancellationToken);

            string stagedExe = Path.Combine(stage, ExeName);
            string stagedMarker = Path.Combine(stage, "build-info.json");
            if (!File.Exists(stagedExe) || !File.Exists(stagedMarker))
                throw new InvalidDataException("更新ファイルが不足しています。");
            if (new FileInfo(stagedExe).Length < 100_000)
                throw new InvalidDataException("実行ファイルのサイズが不正です。");
            string checksumFile = Path.Combine(stage, ExeName + ".sha256");
            if (!File.Exists(checksumFile))
                throw new InvalidDataException("SHA-256検証ファイルがありません。");
            string expectedHash = (await File.ReadAllTextAsync(checksumFile, cancellationToken)).Trim();
            if (expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit))
                throw new InvalidDataException("SHA-256検証ファイルが不正です。");
            await using (var input = File.OpenRead(stagedExe))
            {
                string computedHash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken));
                if (!computedHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("更新ファイルのSHA-256が一致しません。");
            }

            using var marker = JsonDocument.Parse(await File.ReadAllTextAsync(stagedMarker, cancellationToken));
            string stagedSha = marker.RootElement.GetProperty("commit").GetString() ?? "";
            if (!string.Equals(stagedSha, latestSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("ビルドとコミットIDが一致しません。");

            elapsed.Report("更新パッケージを検証しています…");
            await PreflightAsync(stagedExe, cancellationToken);
            // Execute the staged app itself as a standalone helper; Unicode paths are passed as separate arguments.
            elapsed.Report("旧アプリを終了し、更新を適用します…");
            LaunchSwap(stage);
            return "更新ファイルを取得しました。アプリを終了して更新します。";
        }
        catch
        {
            try { Directory.Delete(stage, true); } catch { }
            throw;
        }
    }

    private sealed class UpdateStopwatch : IDisposable
    {
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        private readonly IProgress<string> progress;
        internal UpdateStopwatch(IProgress<string> progress) => this.progress = progress;
        internal void Report(string message) =>
            progress.Report($"[{stopwatch.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)}秒] {message}");
        public void Dispose() => stopwatch.Stop();
    }

    private static async Task<string> GhAsync(string[] args, CancellationToken ct)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo("gh")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string arg in args) process.StartInfo.ArgumentList.Add(arg);
        try { process.Start(); }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException(
                "GitHub CLI (gh) がありません。最初の1回だけ https://cli.github.com/ からインストールして、gh auth login を実行してください。");
        }
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(ct);
        Task<string> stderr = process.StandardError.ReadToEndAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        string output = await stdout;
        string error = await stderr;
        if (process.ExitCode != 0)
        {
            string hint = args[0] == "auth"
                ? "GitHubへのログインが必要です。ターミナルで gh auth login を実行してください。"
                : "GitHubから取得できませんでした。アクセス権とActionsの実行結果を確認してください。";
            throw new InvalidOperationException(hint + "\n" + error[..Math.Min(error.Length, 500)]);
        }
        return output;
    }

    private static async Task PreflightAsync(string stagedExe, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(stagedExe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(stagedExe)!
            }
        };
        process.StartInfo.ArgumentList.Add("--verify-package");
        if (!process.Start())
            throw new InvalidOperationException("新しいアプリの事前検証を開始できません。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidOperationException("新しいアプリの事前検証が時間内に終わりませんでした。");
        }
        if (process.ExitCode != 0)
            throw new InvalidDataException("新しいアプリの事前検証に失敗しました。現在のアプリは変更していません。");
    }

    internal static int VerifyPackage()
    {
        try
        {
            var marker = Path.Combine(AppContext.BaseDirectory, "build-info.json");
            using var document = JsonDocument.Parse(File.ReadAllText(marker));
            var sha = document.RootElement.GetProperty("commit").GetString();
            return sha is { Length: 40 } && sha.All(Uri.IsHexDigit) && SettingEngine.ValidateSafetyContract() && PresetStore.VerifyContract() && PowerPlanManager.VerifyContract() && BatchSettingsApplier.VerifyContract() && GameGpuPreferences.VerifyContract() && GameDiscovery.VerifyContract() && BuiltInGamingProfiles.VerifyContract() ? 0 : 1;
        }
        catch { return 1; }
    }

    private static void LaunchSwap(string stage)
    {
        string installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        string destination = Path.Combine(installDir, ExeName);
        string stagedExe = Path.Combine(stage, ExeName);
        if (!File.Exists(destination) || !File.Exists(stagedExe))
            throw new FileNotFoundException("更新元または更新先のexeがありません。");

        var psi = new ProcessStartInfo(stagedExe)
        {
            UseShellExecute = false,
            WorkingDirectory = stage,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("--apply-staged-update");
        psi.ArgumentList.Add(installDir);
        psi.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (Process.Start(psi) == null)
            throw new InvalidOperationException("更新用プロセスを開始できませんでした。");
    }

    // Runs inside a separately launched staged executable, not inside the normal UI.
    internal static async Task<int> ApplyStagedAsync(string installDir, int previousPid)
    {
        string stage = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        string sourceExe = Path.Combine(stage, ExeName);
        string sourceMarker = Path.Combine(stage, "build-info.json");
        string targetExe = Path.Combine(installDir, ExeName);
        string targetMarker = Path.Combine(installDir, "build-info.json");
        string backupExe = Path.Combine(installDir, "PCSetupCopier.exe.previous");
        string backupMarker = Path.Combine(installDir, "build-info.json.previous");
        string logPath = Path.Combine(stage, "update.log");
        bool moved = false;
        try
        {
            if (!File.Exists(sourceExe) || !File.Exists(sourceMarker))
                throw new FileNotFoundException("展開済み更新ファイルが不足しています。");
            if (!Directory.Exists(installDir) || !File.Exists(targetExe))
                throw new DirectoryNotFoundException("既存のアプリが見つかりません。");

            // Confirm old app has exited before overwriting files; do not kill it.
            try
            {
                using var old = Process.GetProcessById(previousPid);
                if (old.HasExited) { }
                else if (!string.Equals(Path.GetFullPath(old.MainModule?.FileName ?? ""), Path.GetFullPath(targetExe), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("終了待ち対象が元のアプリと一致しません。");
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                try
                {
                    await old.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                {
                    throw new TimeoutException("旧アプリが45秒以内に終了しませんでした。更新はまだ適用していません。");
                }
            }
            catch (ArgumentException) { } // Already exited.

            // Another instance or antivirus scanner may still hold the old executable.
            // Rename the old binary to its backup before copying, never overwrite an in-use image.
            var blockers = Process.GetProcessesByName("PCSetupCopier")
                .Where(p => p.Id != Environment.ProcessId)
                .ToArray();
            try
            {
                foreach (var blocker in blockers)
                {
                    try
                    {
                        if (!blocker.HasExited &&
                            string.Equals(blocker.MainModule?.FileName, targetExe, StringComparison.OrdinalIgnoreCase))
                            throw new IOException($"別のPC Setup Copierが起動中です (PID {blocker.Id})。すべてのアプリ画面を閉じて再実行してください。");
                    }
                    catch (System.ComponentModel.Win32Exception) { }
                    catch (InvalidOperationException) { }
                }
            }
            finally { foreach (var blocker in blockers) blocker.Dispose(); }

            // Retry temporary sharing violations; never forcibly terminate another process.
            Exception? lastError = null;
            for (int attempt = 0; attempt < 25; attempt++)
            {
                try
                {
                    File.Move(targetExe, backupExe, true);
                    moved = true;
                    lastError = null;
                    break;
                }
                catch (IOException ex)
                {
                    lastError = ex;
                    await Task.Delay(500);
                }
            }
            if (!moved)
                throw new IOException("旧exeが使用中でバックアップへ移動できません。別の起動中インスタンスを閉じてください。", lastError);

            if (File.Exists(targetMarker)) File.Copy(targetMarker, backupMarker, true);
            else if (File.Exists(backupMarker)) File.Delete(backupMarker);
            File.Copy(sourceExe, targetExe, true);
            File.Copy(sourceMarker, targetMarker, true);

            var launch = new ProcessStartInfo(targetExe)
            {
                UseShellExecute = false,
                WorkingDirectory = installDir
            };
            if (Process.Start(launch) == null)
                throw new InvalidOperationException("更新後のアプリを起動できませんでした。");

            await File.WriteAllTextAsync(logPath, "Update applied. Previous executable retained in " + backupExe);
            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                if (moved)
                {
                    if (File.Exists(targetExe)) File.Delete(targetExe);
                    File.Move(backupExe, targetExe);
                    if (File.Exists(backupMarker)) File.Copy(backupMarker, targetMarker, true);
                }
            }
            catch (Exception rollbackError)
            {
                ex = new AggregateException("更新失敗・復元も失敗しました。", ex, rollbackError);
            }
            try { await File.WriteAllTextAsync(logPath, ex.ToString()); } catch { }
            MessageBox.Show("更新に失敗しました。元のアプリを保持または復元しました。\n" +
                ex.Message + "\nログ: " + logPath, "PC Setup Copier", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
