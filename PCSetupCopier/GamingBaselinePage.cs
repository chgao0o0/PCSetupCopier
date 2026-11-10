using System.Text;
using System.Text.Json;

namespace PCSetupCopier;

// Read-only settings diff: never imports a configuration as an apply plan.
internal sealed class GamingBaselinePage : UserControl
{
    private static string BaselineFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCSetupCopier", "Diagnostics", "baseline.json");
    private readonly DataGridView differences = new() {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = Color.White
    };
    private readonly Label state = new() {
        Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = Color.FromArgb(48, 74, 105),
        TextAlign = ContentAlignment.MiddleLeft
    };
    private PcSnapshot? baseline;
    private string lastReport = "";

    internal GamingBaselinePage()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4,
            Padding = new Padding(16), BackColor = Color.FromArgb(248, 250, 253) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 75));
        root.Controls.Add(new Label {
            Text = "ゲーミング設定の変更前・変更後を比較",
            Font = new Font("Yu Gothic UI", 15, FontStyle.Bold),
            Dock = DockStyle.Fill
        }, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        AddButton(actions, "現在の設定を基準として保存", SaveBaseline);
        AddButton(actions, "最新の設定と比較", CompareNow);
        AddButton(actions, "別PC・過去のJSONを読み込む", ImportBaseline);
        AddButton(actions, "差分レポートを保存", ExportDiff);
        root.Controls.Add(actions, 0, 1);
        differences.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "項目",
            FillWeight = 120 });
        differences.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "過去の値",
            FillWeight = 240 });
        differences.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "現在値",
            FillWeight = 240 });
        differences.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "判定",
            FillWeight = 75 });
        root.Controls.Add(differences, 0, 2);
        state.Text = "現在の設定を保存しておけば、ゲーミングPCで設定を変更した前後の差を確認できます。\n" +
            "比較・JSON読み込みは診断専用で、WindowsやNVIDIAの設定を一切変更しません。";
        root.Controls.Add(state, 0, 3);
        Controls.Add(root);
        try { if (File.Exists(BaselineFile)) baseline = Read(BaselineFile); }
        catch (Exception ex) { state.Text = "保存済み基準ファイルの読み込みに失敗: " + ex.Message; }
    }

    private static void AddButton(FlowLayoutPanel target, string label, Action action)
    {
        var button = new Button { Text = label, AutoSize = true, Height = 37,
            Margin = new Padding(3, 3, 7, 3) };
        button.Click += (_, _) => action();
        target.Controls.Add(button);
    }

    private static PcSnapshot Read(string path)
    {
        if (new FileInfo(path).Length is < 2 or > 1_000_000)
            throw new InvalidDataException("診断ファイルのサイズが不正です。");
        var snapshot = JsonSerializer.Deserialize<PcSnapshot>(File.ReadAllText(path))
            ?? throw new InvalidDataException("診断JSONの内容が不正です。");
        if (snapshot.Settings == null || snapshot.Settings.Count is < 1 or > 100 ||
            snapshot.Settings.Any(x => string.IsNullOrWhiteSpace(x.Id)) ||
            snapshot.Settings.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != snapshot.Settings.Count)
            throw new InvalidDataException("診断項目が不正です。");
        return snapshot;
    }

    private void SaveBaseline()
    {
        try
        {
            var captured = SettingsCollector.Collect();
            var directory = Path.GetDirectoryName(BaselineFile)!;
            Directory.CreateDirectory(directory);
            string temp = BaselineFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(captured,
                    new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temp, BaselineFile, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            baseline = captured;
            lastReport = "";
            state.Text = "基準を保存しました（" + captured.CapturedAt.ToString("yyyy/MM/dd HH:mm") +
                "）。設定の変更は行っていません。";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "基準保存失敗"); }
    }

    private void ImportBaseline()
    {
        using var picker = new OpenFileDialog {
            Filter = "PC Setup Copier診断JSON (*.json)|*.json",
            CheckFileExists = true
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            baseline = Read(picker.FileName);
            lastReport = "";
            state.Text = "診断JSONを比較基準として読み込みました（" +
                baseline.CapturedAt.ToString("yyyy/MM/dd HH:mm") +
                "）。Windows設定は変更していません。";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "診断JSON読込失敗"); }
    }

    private void CompareNow()
    {
        if (baseline == null)
        {
            MessageBox.Show(this, "先に基準を保存するか、診断JSONを読み込んでください。");
            return;
        }
        try
        {
            var now = SettingsCollector.Collect();
            var before = baseline.Settings.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
            var current = now.Settings.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
            var lines = new StringBuilder();
            lines.AppendLine("PC Setup Copier / 設定変更比較");
            lines.AppendLine("基準: " + baseline.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            lines.AppendLine("現在: " + now.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            lines.AppendLine("注意: 比較結果は取得可能なWindowsの関連値であり、性能やGPU実効状態の測定ではありません。");
            lines.AppendLine();
            differences.Rows.Clear();
            int changes = 0, unknown = 0;
            foreach (string id in before.Keys.Union(current.Keys, StringComparer.OrdinalIgnoreCase))
            {
                before.TryGetValue(id, out var a);
                current.TryGetValue(id, out var b);
                string oldValue = a?.Value ?? "取得不可";
                string newValue = b?.Value ?? "取得不可";
                bool uncertain = a == null || b == null ||
                    a.Value == null || b.Value == null ||
                    a.Status is "未対応" or "未取得" or "取得失敗" ||
                    b.Status is "未対応" or "未取得" or "取得失敗";
                bool changed = !string.Equals(oldValue, newValue, StringComparison.Ordinal);
                string verdict = uncertain ? "比較不可" : changed ? "変更あり" : "同じ";
                if (uncertain) unknown++;
                else if (changed) changes++;
                int index = differences.Rows.Add(b?.DisplayName ?? a?.DisplayName ?? id,
                    oldValue, newValue, verdict);
                if (changed && !uncertain)
                    differences.Rows[index].DefaultCellStyle.BackColor =
                        Color.FromArgb(255, 245, 218);
                lines.AppendLine((b?.DisplayName ?? a?.DisplayName ?? id) + ": " + verdict);
                lines.AppendLine("  基準: " + oldValue);
                lines.AppendLine("  現在: " + newValue);
            }
            lastReport = lines.ToString();
            state.Text = "比較完了: 変更あり " + changes + "件 / 比較不可 " + unknown +
                "件。差分があっても自動では適用・復元しません。";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "比較失敗"); }
    }

    private void ExportDiff()
    {
        if (lastReport.Length == 0)
        {
            MessageBox.Show(this, "先に比較を実行してください。");
            return;
        }
        using var dialog = new SaveFileDialog {
            FileName = "PCSetupCopier-settings-diff.txt",
            Filter = "テキスト (*.txt)|*.txt", DefaultExt = "txt"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllText(dialog.FileName, lastReport, Encoding.UTF8); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "レポート保存失敗"); }
    }

    internal static bool VerifyContract() =>
        typeof(PcSnapshot).GetProperty(nameof(PcSnapshot.Settings)) != null &&
        typeof(SettingResult).GetProperty(nameof(SettingResult.Id)) != null;
}
