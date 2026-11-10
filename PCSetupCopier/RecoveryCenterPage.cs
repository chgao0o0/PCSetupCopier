namespace PCSetupCopier;

// Explicit best-effort recovery of last backups by setting type.
// Independent backups cannot be atomically restored together; failures are reported per item.
internal sealed class RecoveryCenterPage : UserControl
{
    private sealed record Entry(string Name, Func<bool> Available, Func<string> Restore);
    private readonly List<(CheckBox Box, Entry Item)> entries = [];
    private readonly TextBox output = new() {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
        ScrollBars = ScrollBars.Vertical, BackColor = Color.White
    };

    internal RecoveryCenterPage()
    {
        BackColor = Color.FromArgb(248, 250, 253);
        var items = new List<Entry> {
            new("NVIDIAグローバル", () => NvidiaGlobalDrs.CanRestore, NvidiaGlobalDrs.Restore),
            new("ゲーム別GPU設定", () => GameGpuPreferences.CanRestore, GameGpuPreferences.Restore),
            new("マウス加速", () => SettingsApplier.CanRestore, SettingsApplier.Restore),
            new("電源プラン", () => PowerPlanManager.CanRestore, PowerPlanManager.Restore),
            new("ゲームモード関連値", () => GameModeManager.CanRestore, GameModeManager.Restore),
            new("バックグラウンド録画関連値", () => CapturePreferenceManager.CanRestore, CapturePreferenceManager.Restore),
            new("Game Bar録画関連値", () => GameBarCaptureManager.CanRestore, GameBarCaptureManager.Restore)
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5,
            Padding = new Padding(18) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 260));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 51));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label {
            Text = "まとめてバックアップから復元", Dock = DockStyle.Fill,
            Font = new Font("Yu Gothic UI", 16, FontStyle.Bold)
        }, 0, 0);
        layout.Controls.Add(new Label {
            Text = "復元する項目だけチェックしてください。最新のバックアップを使います。" +
                   "\nNVIDIAとWindowsの復元は独立しており、一括処理全体の原子的な復元は保証しません。",
            Dock = DockStyle.Fill, ForeColor = Color.FromArgb(82, 101, 124)
        }, 0, 1);
        var selector = new FlowLayoutPanel { Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
            AutoScroll = true, BackColor = Color.White };
        foreach (var item in items)
        {
            var box = new CheckBox { Text = item.Name, Width = 440, Height = 34,
                Margin = new Padding(9, 3, 3, 3), AutoSize = false };
            selector.Controls.Add(box);
            entries.Add((box, item));
        }
        layout.Controls.Add(selector, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var refresh = new Button { Text = "バックアップ状況を更新", AutoSize = true, Height = 37 };
        refresh.Click += (_, _) => Refresh();
        actions.Controls.Add(refresh);
        var restore = new Button {
            Text = "チェックした設定だけ復元", AutoSize = true, Height = 37,
            BackColor = Color.FromArgb(39, 100, 180), ForeColor = Color.White
        };
        restore.Click += (_, _) => RestoreChecked();
        actions.Controls.Add(restore);
        layout.Controls.Add(actions, 0, 3);
        layout.Controls.Add(output, 0, 4);
        Controls.Add(layout);
        Refresh();
    }

    private void Refresh()
    {
        int total = 0;
        foreach (var (box, item) in entries)
        {
            try
            {
                box.Enabled = item.Available();
                if (!box.Enabled) box.Checked = false;
                if (box.Enabled) total++;
            }
            catch { box.Enabled = false; box.Checked = false; }
        }
        output.Text = total + "項目に復元用バックアップがあります。\r\n" +
            "チェックを入れない限り復元されません。";
    }

    private void RestoreChecked()
    {
        var selected = entries.Where(x => x.Box.Enabled && x.Box.Checked).ToArray();
        if (selected.Length == 0)
        {
            MessageBox.Show(this, "復元する項目をチェックしてください。"); return;
        }
        if (MessageBox.Show(this,
            string.Join("\n", selected.Select(x => "・" + x.Item.Name)) +
            "\n\nこれらの項目を最後の保存済みバックアップへ戻します。\n" +
            "現在の設定が上書きされる可能性があります。続行しますか？",
            "復元する設定を確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
            != DialogResult.Yes) return;
        var results = new List<string>();
        int failed = 0;
        foreach (var (_, item) in selected)
        {
            try
            {
                string result = item.Restore();
                results.Add("成功: " + item.Name + " — " + result);
                DiagnosticLog.Record("Recovery/" + item.Name, "Success");
            }
            catch (Exception ex)
            {
                failed++;
                results.Add("失敗: " + item.Name + " — " + ex.Message);
                DiagnosticLog.Record("Recovery/" + item.Name, "Failed", ex);
            }
        }
        output.Text = string.Join("\r\n\r\n", results);
        RefreshAvailabilityOnly();
        MessageBox.Show(this, selected.Length + "件中 " + (selected.Length - failed) +
            "件成功、" + failed + "件失敗しました。詳細は下の結果欄と操作ログで確認できます。",
            "復元結果", MessageBoxButtons.OK,
            failed == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void RefreshAvailabilityOnly()
    {
        foreach (var (box, item) in entries)
        {
            try { box.Enabled = item.Available(); }
            catch { box.Enabled = false; }
            box.Checked = false;
        }
    }
}
