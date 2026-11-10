using System.Text;

namespace PCSetupCopier;

// Local bounded diagnostics. No background activity, telemetry or uploads.
internal static class DiagnosticLog
{
    internal static string PathName => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCSetupCopier", "Logs", "operations.log");

    internal static void Record(string action, string outcome, Exception? error = null)
    {
        try
        {
            string dir = Path.GetDirectoryName(PathName)!;
            Directory.CreateDirectory(dir);
            string text = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz") +
                " | " + action.Replace('\n', ' ').Replace('\r', ' ') + " | " +
                outcome.Replace('\n', ' ').Replace('\r', ' ');
            if (error != null)
                text += " | " + error.GetType().Name + ": " +
                    error.Message.Replace('\n', ' ').Replace('\r', ' ');
            // Bound log growth. Keep recent events rather than continuous IO/background tasks.
            if (File.Exists(PathName) && new FileInfo(PathName).Length > 800_000)
            {
                var recent = File.ReadAllLines(PathName).TakeLast(400).ToArray();
                File.WriteAllLines(PathName, recent, Encoding.UTF8);
            }
            File.AppendAllText(PathName, text + Environment.NewLine, Encoding.UTF8);
        }
        catch { /* Logging must never prevent or change the user's operation. */ }
    }

    internal static string Read()
    {
        if (!File.Exists(PathName)) return "操作ログはまだありません。";
        if (new FileInfo(PathName).Length > 2_000_000)
            return "ログが大きすぎるため読み込みを中止しました。";
        return File.ReadAllText(PathName);
    }

    internal static bool VerifyContract() =>
        Path.GetExtension(PathName).Equals(".log", StringComparison.OrdinalIgnoreCase);
}

internal sealed class DiagnosticLogPage : UserControl
{
    private readonly TextBox content = new() {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
        ScrollBars = ScrollBars.Both, WordWrap = false,
        Font = new Font("Consolas", 10)
    };

    internal DiagnosticLogPage()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill,
            Padding = new Padding(16), RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label {
            Text = "動作ログ・エラー診断", Dock = DockStyle.Fill,
            Font = new Font("Yu Gothic UI", 15, FontStyle.Bold)
        }, 0, 0);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        Add(buttons, "ログを更新", RefreshLog);
        Add(buttons, "内容をコピー", () => Clipboard.SetText(content.Text));
        Add(buttons, "ログファイルを保存", Export);
        layout.Controls.Add(buttons, 0, 1);
        layout.Controls.Add(content, 0, 2);
        Controls.Add(layout);
        RefreshLog();
    }

    private static void Add(FlowLayoutPanel panel, string title, Action work)
    {
        var button = new Button { Text = title, Height = 36, AutoSize = true,
            Margin = new Padding(3, 2, 9, 2) };
        button.Click += (_, _) => work();
        panel.Controls.Add(button);
    }

    private void RefreshLog() => content.Text = DiagnosticLog.Read();

    private void Export()
    {
        using var dialog = new SaveFileDialog { DefaultExt = "txt",
            Filter = "テキスト (*.txt)|*.txt", FileName = "PCSetupCopier-diagnostic-log.txt" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dialog.FileName, content.Text, Encoding.UTF8);
            MessageBox.Show(this,
                "保存しました。ログにはローカルのパスやエラーメッセージが含まれる場合があります。共有前に確認してください。");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "保存失敗"); }
    }
}
