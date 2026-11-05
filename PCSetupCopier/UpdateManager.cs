using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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

        var installed = JsonDocument.Parse(await File.ReadAllTextAsync(installedMarker, cancellationToken));
        var oldSha = installed.RootElement.GetProperty("commit").GetString() ?? "";
        progress.Report("GitHubの認証状態を確認しています…");
        await GhAsync(["auth", "status", "-h", "github.com"], cancellationToken);

        progress.Report("最新版のビルドを確認しています…");
        string json = await GhAsync(
            ["run", "list", "-R", Repository, "--workflow", "build-windows.yml",
             "--branch", "main", "--status", "success", "--limit", "1",
             "--json", "databaseId,headSha"], cancellationToken);
        using var runs = JsonDocument.Parse(json);
        if (runs.RootElement.GetArrayLength() == 0)
            throw new InvalidOperationException("成功済みのWindowsビルドが見つかりません。");

        var run = runs.RootElement[0];
        var latestSha = run.GetProperty("headSha").GetString() ?? "";
        var runId = run.GetProperty("databaseId").GetInt64();
        if (latestSha.Length != 40)
            throw new InvalidDataException("GitHubから正しいコミットIDを取得できませんでした。");
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
            progress.Report("Windows版をダウンロードしています…");
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

            // The updater is a separate short-lived cmd process; it waits until this app exits,
            // retains the previous EXE, swaps verified artifact files and relaunches.
            LaunchSwap(stage);
            return "更新ファイルを取得しました。アプリを終了して更新します。";
        }
        catch
        {
            try { Directory.Delete(stage, true); } catch { }
            throw;
        }
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

    private static void LaunchSwap(string stage)
    {
        string installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        string destination = Path.Combine(installDir, ExeName);
        if (!File.Exists(destination)) throw new FileNotFoundException("配布版exeが見つかりません。", destination);

        // Controlled paths only, all produced by the app; batch quoting includes Windows cmd metacharacter defenses.
        foreach (var p in new[] { installDir, stage })
            if (p.IndexOfAny(['"', '%', '!', '^', '&', '|', '<', '>', '\r', '\n']) >= 0)
                throw new InvalidOperationException("更新先のパスに未対応の文字が含まれています。");

        string scriptPath = Path.Combine(stage, "apply-update.cmd");
        string script = $"""
            @echo off
            setlocal DisableDelayedExpansion
            set "DEST={installDir}"
            set "STAGE={stage}"
            set /a TRIES=0
            :wait
            tasklist /FI "PID eq {Environment.ProcessId}" /FO CSV /NH | findstr /C:"{Environment.ProcessId}" >nul
            if errorlevel 1 goto swap
            set /a TRIES+=1
            if %TRIES% GEQ 30 goto failed
            timeout /t 1 /nobreak >nul
            goto wait
            :swap
            if exist "%DEST%\{ExeName}.previous" del /q "%DEST%\{ExeName}.previous"
            if exist "%DEST%\build-info.json.previous" del /q "%DEST%\build-info.json.previous"
            if exist "%DEST%\build-info.json" copy /Y "%DEST%\build-info.json" "%DEST%\build-info.json.previous" >nul
            move /Y "%DEST%\{ExeName}" "%DEST%\{ExeName}.previous" >nul
            if errorlevel 1 goto failed
            copy /Y "%STAGE%\{ExeName}" "%DEST%\{ExeName}" >nul
            if errorlevel 1 goto rollback
            copy /Y "%STAGE%\build-info.json" "%DEST%\build-info.json" >nul
            if errorlevel 1 goto rollback
            start "" "%DEST%\{ExeName}"
            if errorlevel 1 goto rollback
            goto done
            :rollback
            if exist "%DEST%\{ExeName}.previous" move /Y "%DEST%\{ExeName}.previous" "%DEST%\{ExeName}" >nul
            if exist "%DEST%\build-info.json.previous" copy /Y "%DEST%\build-info.json.previous" "%DEST%\build-info.json" >nul
            :failed
            echo Update failed. Check installed files and backups.
            pause
            :done
            endlocal
            """; 
        File.WriteAllText(scriptPath, script, new UTF8Encoding(false));
        var psi = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = true,
            CreateNoWindow = false,
            WorkingDirectory = installDir
        };
        psi.ArgumentList.Add("/c");
        psi.ArgumentList.Add(scriptPath);
        Process.Start(psi);
    }
}
