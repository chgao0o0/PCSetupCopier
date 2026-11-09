using System.Text;
using System.Text.Json;

namespace PCSetupCopier;

internal sealed class MainForm : Form
{
    private readonly DataGridView grid = new();
    private readonly Label device = new();
    private readonly Label status = new();
    private PcSnapshot? current;
    private PcSnapshot? source;
    private readonly Button updateButton = new();
    private readonly Button applyButton = new();
    private readonly Button restoreButton = new();
    private readonly HashSet<string> selectedIds = new(StringComparer.Ordinal);

    public MainForm()
    {
        Text = "PC Setup Copier — M2.2 (診断・比較・共通設定基盤)";
        Size = new Size(1250, 720);
        MinimumSize = new Size(940, 540);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Yu Gothic UI", 10f);
        var title = new Label { Text = "PC Setup Copier", Dock = DockStyle.Top, Height = 55,
            Font = new Font("Yu Gothic UI", 19f, FontStyle.Bold), Padding = new Padding(20, 10, 0, 0) };
        var notice = new Label { Text = "ゲーム関連11項目を診断。M2ではマウス加速のみ選択適用・復元可能です。",
            Dock = DockStyle.Top, Height = 35, ForeColor = Color.DarkGreen, Padding = new Padding(20, 4, 0, 0) };
        device.Dock = DockStyle.Top;
        device.Height = 52;
        device.Padding = new Padding(20, 4, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 55, Padding = new Padding(15, 3, 0, 0) };
        AddAction(actions, "PCを診断", Scan);
        AddAction(actions, "JSONを書き出し", Export);
        AddAction(actions, "コピー元JSONを選ぶ", Import);
        AddAction(actions, "すべて選択", () => SelectRows(true));
        AddAction(actions, "選択解除", () => SelectRows(false));
        updateButton.Text = "更新を確認";
        updateButton.AutoSize = true;
        updateButton.MinimumSize = new Size(130, 36);
        updateButton.Click += async (_, _) => await UpdateAsync();
        actions.Controls.Add(updateButton);
        applyButton.Text = "選択項目を適用";
        applyButton.AutoSize = true;
        applyButton.MinimumSize = new Size(135, 36);
        applyButton.Click += (_, _) => ApplySelected();
        actions.Controls.Add(applyButton);
        restoreButton.Text = "マウス設定を復元";
        restoreButton.AutoSize = true;
        restoreButton.MinimumSize = new Size(155, 36);
        restoreButton.Click += (_, _) => RestoreMouse();
        actions.Controls.Add(restoreButton);
        actions.WrapContents = true;
        actions.Height = 94;
        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.RowHeadersVisible = false;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.BackgroundColor = Color.White;
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "選択", FillWeight = 38 });
        grid.Columns.Add("Name", "設定名");
        grid.Columns.Add("Current", "このPCの値");
        grid.Columns.Add("Source", "コピー元の値");
        grid.Columns.Add("Status", "取得・比較結果");
        grid.Columns.Add("Description", "説明");
        grid.Columns[1].FillWeight = 145;
        grid.Columns[2].FillWeight = 135;
        grid.Columns[3].FillWeight = 135;
        grid.Columns[4].FillWeight = 90;
        grid.Columns[5].FillWeight = 185;
        foreach (DataGridViewColumn c in grid.Columns)
            if (c.Name != "Selected") c.ReadOnly = true;
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        grid.CellValueChanged += (_, e) =>
        {
            if (e.ColumnIndex != 0 || e.RowIndex < 0 || grid.Rows[e.RowIndex].Tag is not string id) return;
            if (grid.Rows[e.RowIndex].Cells[0].Value is true) selectedIds.Add(id);
            else selectedIds.Remove(id);
            UpdateStatus();
        };
        status.Dock = DockStyle.Bottom;
        status.Height = 36;
        status.Padding = new Padding(20, 5, 0, 0);
        Controls.Add(grid);
        Controls.Add(status);
        Controls.Add(actions);
        Controls.Add(device);
        Controls.Add(notice);
        Controls.Add(title);
        Scan();
    }

    private static void AddAction(FlowLayoutPanel panel, string label, Action action)
    {
        var button = new Button { Text = label, AutoSize = true, MinimumSize = new Size(130, 36) };
        button.Click += (_, _) => action();
        panel.Controls.Add(button);
    }

    private async Task UpdateAsync()
    {
        updateButton.Enabled = false;
        try
        {
            var progress = new Progress<string>(message => status.Text = message);
            string result = await UpdateManager.CheckAndStageAsync(progress, CancellationToken.None);
            if (result.Contains("アプリを終了して更新します。", StringComparison.Ordinal))
            {
                // The staged helper waits for this process to exit. Never block here with a dialog.
                Close();
                return;
            }
            MessageBox.Show(this, result, "更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "更新エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            updateButton.Enabled = true;
            if (!IsDisposed) UpdateStatus();
        }
    }

    private void Scan()
    {
        try
        {
            current = SettingsCollector.Collect();
            string screens = current.Displays.Count == 0 ? "未取得" :
                string.Join(" / ", current.Displays.Select(d => $"{d.Bounds} @ {d.RefreshRateHz ?? "?"}Hz"));
            device.Text = $"PC: {current.ComputerName}    OS: {current.OsDescription}\n画面: {screens}";
            RefreshGrid();
        }
        catch (Exception ex)
        {
            current = null;
            MessageBox.Show("診断失敗: " + ex.Message);
        }
    }

    private void RefreshGrid()
    {
        grid.Rows.Clear();
        if (current == null) return;
        var sourceById = source?.Settings.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var item in current.Settings)
        {
            SettingResult? other = sourceById != null && sourceById.TryGetValue(item.Id, out var found) ? found : null;
            bool comparable = other != null && item.Status == "取得済み" &&
                other.Status == "取得済み" && item.Value != null && other.Value != null;
            string state = other == null ? item.Status : !comparable ? "比較不可" :
                item.Value == other.Value ? "一致" : "差分あり";
            int index = grid.Rows.Add(selectedIds.Contains(item.Id), item.DisplayName, item.Value ?? "—",
                other?.Value ?? "—", state, item.Description);
            var capability = SettingEngine.Capability(item.Id);
            grid.Rows[index].DefaultCellStyle.BackColor = capability.Supported ? Color.White : Color.FromArgb(247, 247, 247);
            // Non-writable rows are still selectable for comparisons but are excluded from apply.
            grid.Rows[index].Tag = item.Id;
            grid.Rows[index].Cells[0].ToolTipText = !capability.Supported
                ? capability.Reason
                : other == null ? "先にコピー元JSONを選んでください。" :
                  !comparable ? "比較できない値です。" : "適用前に必ず確認画面を表示します。";
        }
        UpdateStatus();
    }

    private void SelectRows(bool selected)
    {
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.Tag is not string id) continue;
            row.Cells[0].Value = selected;
            if (selected) selectedIds.Add(id);
            else selectedIds.Remove(id);
        }
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (current == null) return;
        int count = grid.Rows.Cast<DataGridViewRow>().Count(r => r.Cells[0].Value is true);
        int ready = grid.Rows.Cast<DataGridViewRow>().Count(r =>
            r.Cells[0].Value is true && r.Cells[4].Value is string comparison && comparison is "一致" or "差分あり");
        SettingPlan plan = SettingEngine.BuildPlan(source, selectedIds);
        applyButton.Enabled = plan.CanApply;
        restoreButton.Enabled = SettingEngine.CanRestore("mouse_acceleration");
        status.Text = $"診断: {current.Settings.Count}項目 / 選択: {count} / 比較可能: {ready}（適用対応: マウス加速のみ）" +
            (source == null ? " / コピー元JSON未選択" : $" / コピー元: {source.ComputerName}") +
            " / ほかの10項目は読み取り・比較専用";
    }

    private void ApplySelected()
    {
        var plan = SettingEngine.BuildPlan(source, selectedIds);
        if (!plan.CanApply)
        {
            MessageBox.Show(this, string.Join("\n", plan.Blocked.Count > 0 ? plan.Blocked :
                ["変更の必要がないか、対応していない項目です。"]), "適用できません");
            return;
        }
        string changes = string.Join("\n\n", plan.Changes.Select(c =>
            $"{c.Name}\n現在: {c.CurrentValue}\n変更後: {c.TargetValue}"));
        if (MessageBox.Show(this,
            "次の設定だけを変更します。\n\n" + changes +
            "\n\n変更前にバックアップし、適用後に再取得します。続けますか？",
            "設定の適用確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            string result = SettingEngine.Apply(plan);
            Scan();
            MessageBox.Show(this, result, "適用結果", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Scan();
            MessageBox.Show(this, ex.Message, "適用失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RestoreMouse()
    {
        if (MessageBox.Show(this, "保存済みのマウス設定に戻しますか？",
            "復元確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            string result = SettingEngine.Restore("mouse_acceleration");
            Scan();
            MessageBox.Show(this, result, "復元", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Scan();
            MessageBox.Show(this, ex.Message, "復元失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Import()
    {
        using var dialog = new OpenFileDialog { Title = "コピー元JSONを選択",
            Filter = "JSONファイル (*.json)|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 2_000_000)
                throw new InvalidDataException("ファイルが2MBを超えています。");
            var other = JsonSerializer.Deserialize<PcSnapshot>(File.ReadAllText(dialog.FileName));
            if (other == null || other.Settings == null || other.Displays == null ||
                other.SchemaVersion == null || !other.SchemaVersion.StartsWith("1.", StringComparison.Ordinal) ||
                other.Settings.Count > 200 || other.Settings.Any(s => string.IsNullOrWhiteSpace(s.Id)))
                throw new InvalidDataException("診断JSONではありません。");
            source = other;
            RefreshGrid();
        }
        catch (Exception ex) { MessageBox.Show("JSON読み込み失敗: " + ex.Message); }
    }

    private void Export()
    {
        if (current == null) return;
        using var dialog = new SaveFileDialog { Filter = "JSONファイル (*.json)|*.json",
            DefaultExt = "json", AddExtension = true,
            FileName = $"pc-settings-{DateTime.Now:yyyyMMdd-HHmmss}.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dialog.FileName,
                JsonSerializer.Serialize(current, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        }
        catch (Exception ex) { MessageBox.Show("JSON保存失敗: " + ex.Message); }
    }
}
