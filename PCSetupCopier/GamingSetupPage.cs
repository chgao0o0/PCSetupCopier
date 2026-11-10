namespace PCSetupCopier;

// Guided operations, always staging before writing.
// Windows and NVIDIA remain separate explicitly confirmed apply transactions.
internal sealed class GamingSetupPage : UserControl
{
    private readonly Action<GamingTransferBundle.WindowsPreferences> stageWindows;
    private readonly Func<IReadOnlyDictionary<string, uint>, string> stageNvidia;
    private readonly Action<string> preset;
    private readonly Action showWindows, showNvidia;
    private readonly TextBox details = new() {
        Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical,
        Font = new Font("Yu Gothic UI", 10), BackColor = Color.White
    };
    private readonly Label status = new() {
        Dock = DockStyle.Fill, ForeColor = Color.FromArgb(39, 72, 113),
        TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true
    };

    internal GamingSetupPage(
        Action<GamingTransferBundle.WindowsPreferences> stageWindows,
        Func<IReadOnlyDictionary<string, uint>, string> stageNvidia,
        Action<string> preset, Action showWindows, Action showNvidia)
    {
        this.stageWindows = stageWindows;
        this.stageNvidia = stageNvidia;
        this.preset = preset;
        this.showWindows = showWindows;
        this.showNvidia = showNvidia;

        BackColor = Color.FromArgb(248, 250, 253);
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 49));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 59));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.Controls.Add(new Label {
            Text = "かんたんゲームPC設定", Dock = DockStyle.Fill,
            Font = new Font("Yu Gothic UI", 17, FontStyle.Bold)
        }, 0, 0);

        var presets = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        Button(presets, "FPS重視を準備", () => StagePreset("fps"), true);
        Button(presets, "バランス重視を準備", () => StagePreset("balanced"));
        Button(presets, "画質重視を準備", () => StagePreset("visual"));
        root.Controls.Add(presets, 0, 1);

        var heading = new Label {
            Text = "PC丸ごと設定移植（Windows＋NVIDIAの対応項目）",
            Dock = DockStyle.Fill,
            Font = new Font("Yu Gothic UI", 12, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        root.Controls.Add(heading, 0, 2);

        var transferButtons = new FlowLayoutPanel {
            Dock = DockStyle.Fill, WrapContents = true
        };
        Button(transferButtons, "① 自分のPC設定を1ファイルに保存", ExportAll, true);
        Button(transferButtons, "② 友達の設定ファイルを読み込む", ImportAll);
        root.Controls.Add(transferButtons, 0, 3);

        details.Text = "使い方:\r\n" +
            "1. 自分のPCで「保存」を押して移植用JSONを作る\r\n" +
            "2. 友達のPCで「読み込む」を押して設定候補にする\r\n" +
            "3. Windows自由設定とNVIDIAグローバルを確認し、各画面の「一括適用」で確定\r\n\r\n" +
            "読み込み・プリセット選択だけではPCの設定は変更しません。\r\n" +
            "移植先で存在しない電源プランや、対応しないNVIDIA設定は除外します。\r\n" +
            "NVIDIA実機の読み書きは検証が必要なので、最初は小さな変更で試してください。";
        root.Controls.Add(details, 0, 4);

        var footer = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0),
            BackColor = Color.FromArgb(234, 242, 252)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 187));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        footer.Controls.Add(status, 0, 0);
        var windows = new Button { Text = "Windows設定を確認", Dock = DockStyle.Fill,
            Margin = new Padding(5, 8, 5, 8) };
        windows.Click += (_, _) => showWindows();
        footer.Controls.Add(windows, 1, 0);
        var nvidia = new Button { Text = "NVIDIA設定を確認", Dock = DockStyle.Fill,
            Margin = new Padding(5, 8, 5, 8) };
        nvidia.Click += (_, _) => showNvidia();
        footer.Controls.Add(nvidia, 2, 0);
        root.Controls.Add(footer, 0, 5);
        Controls.Add(root);
        status.Text = "設定候補を準備してから、各画面で内容確認と適用を行います。";
    }

    private static void Button(FlowLayoutPanel host, string title, Action handler, bool primary = false)
    {
        var button = new Button { Text = title, AutoSize = true, Height = 42,
            Margin = new Padding(3, 4, 8, 4),
            BackColor = primary ? Color.FromArgb(39, 100, 180) : SystemColors.Control,
            ForeColor = primary ? Color.White : Color.Black };
        button.Click += (_, _) => handler();
        host.Controls.Add(button);
    }

    private void StagePreset(string id)
    {
        try
        {
            preset(id);
            status.Text = "プリセットを設定候補へ読み込みました。未適用です。";
            details.Text = "「" + id + "」の候補をセットしました。\r\n\r\n" +
                "WindowsとNVIDIAには、それぞれ独立した一括適用ボタンがあります。\r\n" +
                "片方だけ変更したい場合は、不要な側の一括適用を押さないでください。";
            DiagnosticLog.Record("SetupPreset", "Staged: " + id);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Record("SetupPreset", "Failed", ex);
            MessageBox.Show(this, ex.Message, "プリセットを準備できません");
        }
    }

    private void ExportAll()
    {
        using var dialog = new SaveFileDialog {
            Filter = "PC Setup Copier移植ファイル (*.json)|*.json",
            DefaultExt = "json", OverwritePrompt = true,
            FileName = "Gaming-PC-Settings.json"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var captured = GamingTransferBundle.Capture();
            GamingTransferBundle.Export(dialog.FileName, captured.Data);
            int count = captured.Data.Nvidia.Count;
            status.Text = "設定ファイルの保存完了。NVIDIA取得: " + count + "項目";
            details.Text = "保存完了（Windows＋NVIDIA設定）\r\n" +
                "NVIDIA設定 " + count + "項目を含めました。\r\n\r\n" +
                (captured.Warnings.Length == 0 ? "取得エラーなし" :
                "一部を取得できませんでした:\r\n" + string.Join("\r\n", captured.Warnings)) +
                "\r\n\r\nJSONを友達のPCに渡し、「② 読み込む」を押してください。";
            DiagnosticLog.Record("TransferExport", "Success, NVIDIA=" + count);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Record("TransferExport", "Failed", ex);
            MessageBox.Show(this, ex.Message, "まとめて保存できませんでした");
        }
    }

    private void ImportAll()
    {
        using var dialog = new OpenFileDialog {
            Filter = "PC Setup Copier移植ファイル (*.json)|*.json",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var imported = GamingTransferBundle.Import(dialog.FileName);
            // Neither callback applies Windows or NVIDIA settings; they only stage UI values.
            stageWindows(imported.Data.Windows);
            string gpu = stageNvidia(imported.Data.Nvidia);
            status.Text = "移植候補を読み込みました（未適用）。";
            details.Text = "移植設定を読み込みました。\r\n\r\n" +
                "Windows: 自由設定に変更候補をセットしました。\r\n" +
                "NVIDIA: " + gpu + "\r\n" +
                (imported.Warnings.Length == 0 ? "" :
                    "\r\n除外した項目:\r\n" + string.Join("\r\n", imported.Warnings)) +
                "\r\n\r\nWindows・NVIDIAそれぞれの設定一覧を確認してから、一括適用してください。";
            DiagnosticLog.Record("TransferImport", "Staged");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Record("TransferImport", "Failed", ex);
            MessageBox.Show(this, ex.Message, "まとめて読み込めませんでした");
        }
    }
}
