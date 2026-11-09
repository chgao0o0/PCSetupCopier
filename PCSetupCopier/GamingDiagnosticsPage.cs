using System.Text;
using System.Text.Json;

namespace PCSetupCopier;

// Read-only diagnostics summary for sharing between PCs and checking prerequisites.
// No executable paths, process lists or user names are included.
internal sealed class GamingDiagnosticsPage : UserControl
{
    private readonly TextBox output = new() {
        Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Both,
        WordWrap = false, Font = new Font("Consolas", 10),
        BackColor = Color.White
    };
    private readonly Label note = new() {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.FromArgb(65, 81, 106)
    };
    private bool scanned;

    internal GamingDiagnosticsPage()
    {
        BackColor = Color.FromArgb(248, 250, 253);
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, Padding = new Padding(16),
            ColumnCount = 1, RowCount = 4
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        root.Controls.Add(new Label {
            Text = "ゲーミングPC 診断・引き継ぎレポート",
            Dock = DockStyle.Fill,
            Font = new Font("Yu Gothic UI", 15, FontStyle.Bold)
        }, 0, 0);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        AddButton(actions, "診断レポートを作成", RefreshReport);
        AddButton(actions, "内容をコピー", () => {
            if (!string.IsNullOrWhiteSpace(output.Text)) Clipboard.SetText(output.Text);
        });
        AddButton(actions, "テキストに保存", SaveReport);
        root.Controls.Add(actions, 0, 1);
        root.Controls.Add(output, 0, 2);
        note.Text = "診断は読み取り専用です。保存前に内容を確認してください。ゲームexeのフルパスやユーザー名は出力しません。";
        root.Controls.Add(note, 0, 3);
        Controls.Add(root);
    }

    private static void AddButton(FlowLayoutPanel actions, string label, Action action)
    {
        var button = new Button {
            Text = label, AutoSize = true, Height = 35,
            Margin = new Padding(2, 3, 8, 3)
        };
        button.Click += (_, _) => action();
        actions.Controls.Add(button);
    }

    private void RefreshReport()
    {
        try
        {
            var snapshot = SettingsCollector.Collect();
            var buffer = new StringBuilder();
            buffer.AppendLine("PC Setup Copier / ゲーミング診断レポート");
            buffer.AppendLine("診断日時: " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            buffer.AppendLine("アプリ版: " + snapshot.ToolVersion);
            buffer.AppendLine();
            buffer.AppendLine("【Windowsとゲーム関連設定】");
            foreach (var item in snapshot.Settings)
            {
                buffer.AppendLine("- " + item.DisplayName);
                buffer.AppendLine("  取得状況: " + item.Status);
                buffer.AppendLine("  現在値: " + (item.Value ?? "取得不可"));
                buffer.AppendLine("  注意点: " + item.Description);
            }

            buffer.AppendLine();
            buffer.AppendLine("【モニターとHz】");
            try
            {
                foreach (var monitor in DisplayRefreshManager.Read())
                {
                    buffer.AppendLine("- " + monitor.Label + ": " + monitor.CurrentHz + "Hz");
                    buffer.AppendLine("  同一解像度・色深度で列挙された候補: " +
                        (monitor.Choices.Count == 0 ? "取得不可" :
                        string.Join(", ", monitor.Choices.Select(x => x.Hz + "Hz"))));
                }
            }
            catch (Exception ex) { buffer.AppendLine("モニター取得失敗: " + ex.Message); }

            buffer.AppendLine();
            buffer.AppendLine("【NVIDIA グローバル設定】");
            try
            {
                var states = NvidiaGlobalDrs.ReadAll().ToDictionary(x => x.Key);
                foreach (var setting in NvidiaGlobalDrs.Settings)
                {
                    var state = states[setting.Key];
                    buffer.AppendLine("- " + setting.Title + ": " +
                        NvidiaGlobalDrs.Label(setting, state.Value) +
                        (state.IsExplicit ? "（明示設定）" : "（既定/継承）"));
                }
            }
            catch (Exception ex)
            {
                buffer.AppendLine("未取得: " + ex.Message);
                buffer.AppendLine("※ NVIDIA以外のPCでは正常に取得できない場合があります。");
            }
            buffer.AppendLine();
            buffer.AppendLine("【保存済み設定】");
            try
            {
                buffer.AppendLine("- Windowsプリセット数: " + PresetStore.Load().Count);
                buffer.AppendLine("- NVIDIAプリセット数: " + NvidiaProfileStore.Load().Count);
            }
            catch (Exception ex) { buffer.AppendLine("プリセット確認失敗: " + ex.Message); }
            buffer.AppendLine("- NVIDIA復元用バックアップ: " +
                (NvidiaGlobalDrs.CanRestore ? "あり" : "なし"));
            buffer.AppendLine("- ゲーム別GPU復元用バックアップ: " +
                (GameGpuPreferences.CanRestore ? "あり" : "なし"));
            buffer.AppendLine();
            buffer.AppendLine("注: NVIDIA設定値はドライバーにより意味・対応状況が変わります。");
            buffer.AppendLine("このレポートは性能測定やゲーム内FPSを保証するものではありません。");
            buffer.AppendLine("診断時にWindowsやNVIDIAの設定は変更していません。");
            output.Text = buffer.ToString();
            scanned = true;
            note.Text = "読み取り専用診断が完了しました。保存・コピー前に内容を確認してください。";
        }
        catch (Exception ex)
        {
            note.Text = "診断レポートの生成に失敗しました: " + ex.Message;
        }
    }

    private void SaveReport()
    {
        if (!scanned || string.IsNullOrWhiteSpace(output.Text))
        {
            MessageBox.Show(this, "先に診断レポートを作成してください。");
            return;
        }
        using var dialog = new SaveFileDialog {
            Filter = "テキストレポート (*.txt)|*.txt",
            DefaultExt = "txt",
            FileName = "PCSetupCopier-gaming-report.txt",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dialog.FileName, output.Text, Encoding.UTF8);
            note.Text = "レポートを書き出しました。PC設定は変更していません。";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "保存失敗"); }
    }
}
