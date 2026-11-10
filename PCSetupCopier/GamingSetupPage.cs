namespace PCSetupCopier;

// Guided setup: visually consistent cards with a fixed action footer.
// All buttons stage preferences; applying Windows and NVIDIA remains separately confirmed.
internal sealed class GamingSetupPage : UserControl
{
    private static readonly Color Canvas = Color.FromArgb(245, 247, 251);
    private static readonly Color Ink = Color.FromArgb(27, 39, 61);
    private static readonly Color Muted = Color.FromArgb(102, 116, 138);
    private static readonly Color Accent = Color.FromArgb(57, 102, 220);
    private static readonly Color Border = Color.FromArgb(224, 230, 240);

    private readonly Action<GamingTransferBundle.WindowsPreferences> stageWindows;
    private readonly Func<IReadOnlyDictionary<string, uint>, string> stageNvidia;
    private readonly Action<string> preset;
    private readonly Action showWindows, showNvidia;
    private readonly TextBox details = new() {
        Dock = DockStyle.Fill, ReadOnly = true, Multiline = true,
        ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None,
        Font = new Font("Yu Gothic UI", 9.6f), BackColor = Color.White,
        ForeColor = Color.FromArgb(72, 87, 110)
    };
    private readonly Label status = new() {
        Dock = DockStyle.Fill, ForeColor = Color.FromArgb(61, 82, 120),
        TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
        Font = new Font("Yu Gothic UI", 9.5f)
    };
    private readonly Panel scroller = new() { Name = "SetupScrollArea", Dock = DockStyle.Fill, AutoScroll = true, BackColor = Canvas };
    private readonly TableLayoutPanel content = new() {
        Name = "SetupContent", ColumnCount = 1, RowCount = 5, AutoSize = false,
        Size = new Size(920, 655), BackColor = Canvas,
        Margin = Padding.Empty, Padding = new Padding(18, 14, 18, 16)
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
        BackColor = Canvas;

        var shell = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Canvas
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));

        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 152));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 195));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 134));

        var heading = new TableLayoutPanel { Dock = DockStyle.Fill,
            RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
        heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        heading.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        heading.Controls.Add(new Label {
            Text = "ゲームPCのセットアップ", Dock = DockStyle.Fill,
            Font = new Font("Yu Gothic UI", 20, FontStyle.Bold),
            ForeColor = Ink, TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        heading.Controls.Add(new Label {
            Text = "設定を選んで準備。確認してから適用。変更内容はいつでも見直せます。",
            Dock = DockStyle.Fill, ForeColor = Muted,
            Font = new Font("Yu Gothic UI", 9.6f)
        }, 0, 1);
        content.Controls.Add(heading, 0, 0);

        content.Controls.Add(SectionTitle("QUICK PRESETS", "おすすめの設定を選択"), 0, 1);
        var presetGrid = new TableLayoutPanel {
            Name = "SetupPresets", Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        for (int i = 0; i < 3; i++)
            presetGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3f));
        presetGrid.Controls.Add(PresetCard("01", "FPS重視",
            "パフォーマンスを優先", "fps", true), 0, 0);
        presetGrid.Controls.Add(PresetCard("02", "バランス",
            "速度と品質のバランス", "balanced", false), 1, 0);
        presetGrid.Controls.Add(PresetCard("03", "画質重視",
            "映像の品質を優先", "visual", false), 2, 0);
        content.Controls.Add(presetGrid, 0, 2);

        var transfer = Card();
        transfer.Name = "SetupTransfer";
        transfer.Margin = new Padding(0, 12, 0, 0);
        var transferContent = new TableLayoutPanel {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 2,
            Margin = Padding.Empty, Padding = new Padding(18, 12, 18, 10)
        };
        transferContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        transferContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        transferContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        transferContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        transferContent.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var transferTitle = new Label {
            Text = "PC間の設定移植", Dock = DockStyle.Fill, ForeColor = Ink,
            Font = new Font("Yu Gothic UI", 13.2f, FontStyle.Bold)
        };
        transferContent.Controls.Add(transferTitle, 0, 0);
        transferContent.SetColumnSpan(transferTitle, 2);
        var save = ActionButton("自分のPC設定を保存", true);
        save.Click += (_, _) => ExportAll();
        var load = ActionButton("友達の設定を読み込む", false);
        load.Click += (_, _) => ImportAll();
        save.Margin = new Padding(0, 0, 9, 5);
        load.Margin = new Padding(9, 0, 0, 5);
        transferContent.Controls.Add(save, 0, 1);
        transferContent.Controls.Add(load, 1, 1);
        transferContent.Controls.Add(Description(
            "STEP 1  /  Windows + NVIDIAの対応設定を1つのJSONファイルに保存"), 0, 2);
        transferContent.Controls.Add(Description(
            "STEP 2  /  対応状況を確認し、適用候補として読み込み"), 1, 2);
        transfer.Controls.Add(transferContent);
        content.Controls.Add(transfer, 0, 3);

        var help = Card();
        help.Margin = new Padding(0, 12, 0, 0);
        var helpLayout = new TableLayoutPanel {
            Dock = DockStyle.Fill, RowCount = 2, Padding = new Padding(16, 8, 16, 8)
        };
        helpLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        helpLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        helpLayout.Controls.Add(new Label {
            Text = "操作ガイド・実行結果", Dock = DockStyle.Fill,
            Font = new Font("Yu Gothic UI", 10.3f, FontStyle.Bold),
            ForeColor = Ink
        }, 0, 0);
        helpLayout.Controls.Add(details, 0, 1);
        help.Controls.Add(helpLayout);
        content.Controls.Add(help, 0, 4);

        details.Text = "① 目的に合わせてプリセットを選ぶか、別PCの設定ファイルを読み込む。\r\n" +
            "② 各設定画面で希望値を確認し、必要な項目だけ一括適用。\r\n" +
            "※ この画面ではPCの設定を変更しません。NVIDIAの実機での適用・復元は未検証です。";

        scroller.Controls.Add(content);
        scroller.ClientSizeChanged += (_, _) => FitContent();
        shell.Controls.Add(scroller, 0, 0);

        var footer = new TableLayoutPanel {
            Name = "SetupFooter", Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
            BackColor = Color.White, Padding = new Padding(16, 10, 16, 10)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 177));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 177));
        status.Padding = new Padding(0, 4, 0, 0);
        status.Text = "準備のみ。確認するまでは変更されません。";
        footer.Controls.Add(status, 0, 0);
        var windows = ActionButton("Windows設定を確認", false);
        windows.Margin = new Padding(4, 0, 6, 0);
        windows.Click += (_, _) => showWindows();
        footer.Controls.Add(windows, 1, 0);
        var nvidia = ActionButton("NVIDIA設定を確認", true);
        nvidia.Margin = new Padding(6, 0, 0, 0);
        nvidia.Click += (_, _) => showNvidia();
        footer.Controls.Add(nvidia, 2, 0);
        shell.Controls.Add(footer, 0, 1);
        Controls.Add(shell);
        FitContent();
    }

    private void FitContent()
    {
        // TableLayoutPanel.AutoSize=GrowAndShrink previously ignored the Width
        // assignment and collapsed the entire 3-column card grid to ~200px.
        // Use an explicitly sized root so all percentage-width child columns
        // expand to the available viewport. Only this inner panel scrolls.
        int viewport = scroller.ClientSize.Width;
        if (viewport <= 0) return;
        int width = Math.Max(630, viewport - SystemInformation.VerticalScrollBarWidth - 2);
        if (content.Width != width) content.Width = width;
        if (content.Height != 655) content.Height = 655;
    }

    internal static string VerifyVisualGeometry()
    {
        // A real WinForms layout probe, not a compile-only test.
        // Must run in an STA on Windows. No settings are changed.
        foreach (var width in new[] { 980, 1260, 1600 })
        {
            using var form = new Form {
                ClientSize = new Size(width, 720),
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-2000, -2000),
                AutoScaleMode = AutoScaleMode.Dpi
            };
            using var page = new GamingSetupPage(_ => {}, _ => "",
                _ => {}, () => {}, () => {}) { Dock = DockStyle.Fill };
            form.Controls.Add(page);
            form.Show();
            Application.DoEvents();
            page.PerformLayout();
            page.scroller.PerformLayout();
            page.content.PerformLayout();
            Application.DoEvents();

            int usable = page.scroller.ClientSize.Width;
            // CI runners may clamp larger windows to the virtual desktop.
            // Verify the actual client area rather than the requested frame size.
            if (usable < page.ClientSize.Width - 30 || usable < 590)
                throw new InvalidOperationException("セットアップ画面のスクロール領域が狭すぎます: " +
                    usable + " / " + page.ClientSize.Width);
            if (page.content.Width < usable - 35)
                throw new InvalidOperationException("設定カードが横幅いっぱいに展開されていません: " +
                    page.content.Width + " / " + usable);
            var presets = page.content.Controls.Find("SetupPresets", true).Single();
            var cards = presets.Controls.Cast<Control>().ToArray();
            if (cards.Length != 3 || cards.Any(card => card.Width < 175 || card.Height < 100))
                throw new InvalidOperationException("プリセットカードのサイズが不正です: " +
                    string.Join(",", cards.Select(card => card.Width + "x" + card.Height)));
            if (cards.Any(card => card.Controls.OfType<TableLayoutPanel>()
                .SelectMany(layout => layout.Controls.OfType<Button>())
                .Any(button => button.Width < 115 || button.Height < 28)))
                throw new InvalidOperationException("プリセットの操作ボタンが切れています。");
            var transfer = page.content.Controls.Find("SetupTransfer", true).Single();
            if (transfer.Width < 500 || transfer.Height < 150)
                throw new InvalidOperationException("移植カードが小さすぎます: " +
                    transfer.Width + "x" + transfer.Height);
            var footer = page.Controls.Find("SetupFooter", true).Single();
            if (footer.Width < usable - 10 || footer.Height < 55)
                throw new InvalidOperationException("画面下の操作エリアが表示されません。");
            form.Close();
        }
        return "980 / 1260 / 1600 px: setup cards, buttons and footer passed";
    }

    private static Label SectionTitle(string overline, string caption) => new() {
        Dock = DockStyle.Fill, Text = overline + "     " + caption,
        ForeColor = Muted, Font = new Font("Yu Gothic UI", 9.5f, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static Panel Card()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White,
            Margin = new Padding(0, 0, 10, 8) };
        panel.Paint += (_, e) => {
            using var pen = new Pen(Border);
            e.Graphics.DrawRectangle(pen, 0, 0,
                Math.Max(0, panel.Width - 1), Math.Max(0, panel.Height - 1));
        };
        return panel;
    }

    private Panel PresetCard(string number, string name, string description,
        string id, bool highlighted)
    {
        var card = Card();
        card.Name = "SetupPreset_" + id;
        card.Margin = new Padding(0, 0, 11, 7);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill,
            RowCount = 3, Padding = new Padding(13, 10, 13, 12) };
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.Controls.Add(new Label {
            Text = number + "   " + name, Dock = DockStyle.Fill,
            Font = new Font("Yu Gothic UI", 12.3f, FontStyle.Bold),
            ForeColor = Ink
        }, 0, 0);
        body.Controls.Add(Description(description), 0, 1);
        var button = ActionButton("この設定を準備", highlighted);
        button.Click += (_, _) => StagePreset(id);
        body.Controls.Add(button, 0, 2);
        card.Controls.Add(body);
        return card;
    }

    private static Label Description(string text) => new() {
        Text = text, Dock = DockStyle.Fill,
        Font = new Font("Yu Gothic UI", 9.1f), ForeColor = Muted,
        AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft
    };

    private static Button ActionButton(string title, bool primary) => new() {
        Text = title, Dock = DockStyle.Fill,
        FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
        Font = new Font("Yu Gothic UI", 9.4f, FontStyle.Bold),
        BackColor = primary ? Accent : Color.FromArgb(240, 244, 251),
        ForeColor = primary ? Color.White : Color.FromArgb(44, 72, 117),
        UseVisualStyleBackColor = false,
        FlatAppearance = { BorderSize = 0 }
    };

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
