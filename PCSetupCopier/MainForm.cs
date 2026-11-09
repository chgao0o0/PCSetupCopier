using System.Text;
using System.Text.Json;

namespace PCSetupCopier;

internal sealed class MainForm : Form
{
    private readonly DataGridView grid = new();
    private readonly ListView diagnosticList = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
        MultiSelect = false, GridLines = true, HideSelection = false
    };
    private readonly TextBox presetName = new() { Width = 220, MaxLength = 40 };
    private readonly ComboBox presetChoices = new() { Width = 230, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button presetLoad = new() { Text = "選択内容を読み込む", AutoSize = true };
    private readonly Button presetDelete = new() { Text = "削除", AutoSize = true };
    private readonly Label freeCurrent = new();
    private readonly Label powerCurrent = new() { AutoSize = true };
    private readonly ComboBox powerChoices = new() { Width = 420, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button powerApplyButton = new() { Text = "電源プランを適用", AutoSize = true };
    private readonly Button powerRestoreButton = new() { Text = "電源プランを復元", AutoSize = true };
    private readonly RadioButton mouseOff = new() { Text = "オフ", AutoSize = true, Checked = true };
    private readonly RadioButton mouseOn = new() { Text = "オン", AutoSize = true };
    private readonly Button freeApplyButton = new() { Text = "マウス加速を適用", AutoSize = true, MinimumSize = new Size(190, 38) };
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
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
        Text = "PC Setup Copier — M2.5 (診断・自由設定・電源プラン)";
        Size = new Size(1250, 720);
        MinimumSize = new Size(760, 500);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Yu Gothic UI", 10f);
        var title = new Label { Text = "PC Setup Copier", Dock = DockStyle.Top, Height = 55,
            Font = new Font("Yu Gothic UI", 19f, FontStyle.Bold), Padding = new Padding(20, 10, 0, 0) };
        var notice = new Label { Text = "ゲーム関連11項目を診断。M2ではマウス加速と電源プランを個別に適用・復元できます。",
            Dock = DockStyle.Top, Height = 35, ForeColor = Color.DarkGreen, Padding = new Padding(20, 4, 0, 0) };
        device.Dock = DockStyle.Top;
        device.Height = 52;
        device.Padding = new Padding(20, 4, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 55, Padding = new Padding(15, 3, 0, 0) };
        AddAction(actions, "JSONを書き出し", Export);
        AddAction(actions, "コピー元JSONを選ぶ", Import);
        AddAction(actions, "すべて選択", () => SelectRows(true));
        AddAction(actions, "選択解除", () => SelectRows(false));
        applyButton.Text = "選択項目を適用";
        applyButton.AutoSize = true;
        applyButton.MinimumSize = new Size(135, 36);
        applyButton.Click += (_, _) => ApplySelected();
        actions.Controls.Add(applyButton);
        actions.WrapContents = true;
        actions.Height = 58;

        updateButton.Text = "更新を確認";
        updateButton.AutoSize = true;
        updateButton.MinimumSize = new Size(130, 36);
        updateButton.Click += async (_, _) => await UpdateAsync();
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 49, Padding = new Padding(15, 2, 0, 0) };
        AddAction(toolbar, "PCを診断", Scan);
        toolbar.Controls.Add(updateButton);

        var diagnosticPage = new TabPage("診断");
        diagnosticList.Columns.Add("設定", 225);
        diagnosticList.Columns.Add("取得状況", 110);
        diagnosticList.Columns.Add("現在値", 265);
        diagnosticList.Columns.Add("説明", 480);
        diagnosticList.Resize += (_, _) =>
        {
            int width = diagnosticList.ClientSize.Width;
            if (width > 650) diagnosticList.Columns[3].Width = Math.Max(220, width - 625);
        };
        diagnosticPage.Controls.Add(diagnosticList);

        var freePage = new TabPage("自由設定");
        var freeLayout = new FlowLayoutPanel {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(22, 18, 15, 15)
        };
        freeLayout.Controls.Add(new Label { Text = "マウス加速", AutoSize = true,
            Font = new Font("Yu Gothic UI", 16f, FontStyle.Bold) });
        freeLayout.Controls.Add(new Label { Text = "Windows 10/11対応。現在値のバックアップと適用後の検証を行います。",
            AutoSize = true, Padding = new Padding(0, 5, 0, 14) });
        freeCurrent.AutoSize = true;
        freeCurrent.Padding = new Padding(0, 0, 0, 12);
        freeLayout.Controls.Add(freeCurrent);
        freeLayout.Controls.Add(mouseOff);
        freeLayout.Controls.Add(mouseOn);
        freeApplyButton.Margin = new Padding(0, 18, 0, 10);
        freeApplyButton.Click += (_, _) => ApplyFreeMouse();
        freeLayout.Controls.Add(freeApplyButton);
        freeLayout.Controls.Add(new Label { Text = "電源プラン", AutoSize = true,
            Font = new Font("Yu Gothic UI", 14f, FontStyle.Bold),
            Margin = new Padding(0, 22, 0, 8) });
        freeLayout.Controls.Add(new Label {
            Text = "このPCに存在するプランだけを選択可能。Windowsの電源モードとは別の設定です。",
            AutoSize = true, MaximumSize = new Size(710, 0)
        });
        powerCurrent.Padding = new Padding(0, 8, 0, 10);
        freeLayout.Controls.Add(powerCurrent);
        freeLayout.Controls.Add(powerChoices);
        powerApplyButton.Margin = new Padding(0, 10, 0, 10);
        powerApplyButton.Click += (_, _) => ApplyPowerPlan();
        freeLayout.Controls.Add(powerApplyButton);
        freeLayout.Controls.Add(new Label { Text = "プリセット", AutoSize = true,
            Font = new Font("Yu Gothic UI", 14f, FontStyle.Bold),
            Margin = new Padding(0, 22, 0, 8) });
        freeLayout.Controls.Add(new Label { Text = "名前を付けて現在選択中のON/OFFを保存できます。",
            AutoSize = true });
        var saveRow = new FlowLayoutPanel { Width = 590, Height = 43, FlowDirection = FlowDirection.LeftToRight };
        saveRow.Controls.Add(presetName);
        var savePreset = new Button { Text = "保存", AutoSize = true, MinimumSize = new Size(90, 32) };
        savePreset.Click += (_, _) => SavePreset();
        saveRow.Controls.Add(savePreset);
        freeLayout.Controls.Add(saveRow);
        var loadRow = new FlowLayoutPanel { Width = 660, Height = 43, FlowDirection = FlowDirection.LeftToRight };
        loadRow.Controls.Add(presetChoices);
        presetLoad.Click += (_, _) => LoadPreset();
        presetDelete.Click += (_, _) => DeletePreset();
        loadRow.Controls.Add(presetLoad);
        loadRow.Controls.Add(presetDelete);
        freeLayout.Controls.Add(loadRow);
        freeLayout.Controls.Add(new Label { Text = "読み込みだけではWindowsの設定は変わりません。適用ボタンで確認後に変更します。",
            AutoSize = true, ForeColor = Color.DimGray });
        freeLayout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(780, 0),
            Text = "ゲームモードなど残り9項目は検証済みの変更方法が実装されるまで診断・比較専用です。",
            ForeColor = Color.DimGray });
        freePage.Controls.Add(freeLayout);

        var copyPage = new TabPage("PCからコピー");
        var copyContent = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        copyContent.Controls.Add(grid);
        copyContent.Controls.Add(actions);
        copyPage.Controls.Add(copyContent);

        var backupPage = new TabPage("バックアップ・復元");
        var backupLayout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(22, 22, 10, 10) };
        backupLayout.Controls.Add(new Label { Text = "マウス設定の復元", AutoSize = true,
            Font = new Font("Yu Gothic UI", 16f, FontStyle.Bold) });
        backupLayout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(780, 0),
            Text = "変更直前の状態に戻します。バックアップは復元後も保持されます。",
            Padding = new Padding(0, 10, 0, 14) });
        restoreButton.Text = "マウス設定を復元";
        restoreButton.AutoSize = true;
        restoreButton.MinimumSize = new Size(200, 38);
        restoreButton.Click += (_, _) => RestoreMouse();
        backupLayout.Controls.Add(restoreButton);
        backupLayout.Controls.Add(new Label { Text = "電源プランの復元",
            Font = new Font("Yu Gothic UI", 16f, FontStyle.Bold),
            AutoSize = true, Margin = new Padding(0, 25, 0, 10) });
        powerRestoreButton.MinimumSize = new Size(200, 38);
        powerRestoreButton.Click += (_, _) => RestorePowerPlan();
        backupLayout.Controls.Add(powerRestoreButton);
        backupPage.Controls.Add(backupLayout);

        tabs.TabPages.Add(diagnosticPage);
        tabs.TabPages.Add(freePage);
        tabs.TabPages.Add(copyPage);
        tabs.TabPages.Add(backupPage);
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
        Controls.Add(tabs);
        Controls.Add(status);
        Controls.Add(toolbar);
        Controls.Add(device);
        Controls.Add(notice);
        Controls.Add(title);
        Scan();
        RefreshPresetChoices();
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
            RefreshDiagnostic();
        }
        catch (Exception ex)
        {
            current = null;
            MessageBox.Show("診断失敗: " + ex.Message);
        }
    }

    private void RefreshDiagnostic()
    {
        if (current == null) return;
        diagnosticList.BeginUpdate();
        try
        {
            diagnosticList.Items.Clear();
            foreach (var item in current.Settings)
            {
                var row = new ListViewItem(item.DisplayName);
                row.SubItems.Add(item.Status);
                row.SubItems.Add(item.Value ?? "—");
                row.SubItems.Add(item.Description);
                row.ToolTipText = item.Description;
                if (item.Status != "取得済み") row.ForeColor = Color.DimGray;
                diagnosticList.Items.Add(row);
            }
        }
        finally { diagnosticList.EndUpdate(); }
        RefreshPowerPlan();
        try
        {
            int[] mouse = SettingsApplier.Read();
            freeCurrent.Text = $"現在: {(mouse[2] == 0 ? "オフ" : "オン")}  / Threshold1={mouse[0]} / Threshold2={mouse[1]} / Accel={mouse[2]}";
            mouseOn.Checked = mouse[2] != 0;
            mouseOff.Checked = mouse[2] == 0;
            freeApplyButton.Enabled = SettingEngine.Capability("mouse_acceleration").Supported;
        }
        catch (Exception ex)
        {
            freeCurrent.Text = "マウス設定の取得失敗: " + ex.Message;
            freeApplyButton.Enabled = false;
        }
    }

    private void RefreshPowerPlan()
    {
        try
        {
            Guid active = PowerPlanManager.ReadActive();
            var available = PowerPlanManager.Installed();
            powerChoices.BeginUpdate();
            try
            {
                powerChoices.Items.Clear();
                foreach (var plan in available) powerChoices.Items.Add(plan);
                int index = available.ToList().FindIndex(x => x.Id == active);
                if (index >= 0) powerChoices.SelectedIndex = index;
            }
            finally { powerChoices.EndUpdate(); }
            powerCurrent.Text = "現在の電源プラン: " +
                (available.FirstOrDefault(x => x.Id == active)?.Label ?? active.ToString("D"));
            powerApplyButton.Enabled = powerChoices.SelectedItem is PowerPlanManager.Plan;
        }
        catch (Exception ex)
        {
            powerChoices.Items.Clear();
            powerApplyButton.Enabled = false;
            powerCurrent.Text = "電源プラン取得不可: " + ex.Message;
        }
    }

    private void ApplyPowerPlan()
    {
        if (powerChoices.SelectedItem is not PowerPlanManager.Plan choice) return;
        try
        {
            Guid before = PowerPlanManager.ReadActive();
            if (before == choice.Id)
            {
                MessageBox.Show(this, "現在と同じプランです。", "電源プラン"); return;
            }
            if (MessageBox.Show(this,
                "電源プランを変更します。\\n\\n現在: " + before +
                "\\n変更後: " + choice.Label +
                "\\n\\n事前バックアップ後に検証します。続行しますか？",
                "電源プラン変更", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            string result = PowerPlanManager.Apply(choice.Id);
            Scan();
            MessageBox.Show(this, result, "電源プラン", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Scan();
            MessageBox.Show(this, ex.Message, "電源プラン変更失敗",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RestorePowerPlan()
    {
        if (MessageBox.Show(this, "以前の電源プランに戻しますか？", "電源プラン復元",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            string result = PowerPlanManager.Restore();
            Scan();
            MessageBox.Show(this, result, "電源プラン", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Scan();
            MessageBox.Show(this, ex.Message, "電源プラン復元失敗",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplyFreeMouse()
    {
        if (current == null) { MessageBox.Show(this, "先に診断してください。"); return; }
        try
        {
            int[] previous = SettingsApplier.Read();
            int[] desired = mouseOn.Checked ? [6, 10, 1] : [0, 0, 0];
            var choice = new SettingResult("mouse_acceleration", "Gaming", "マウス加速",
                $"Threshold1={desired[0]} / Threshold2={desired[1]} / Accel={desired[2]}",
                "取得済み", "自由設定");
            PcSnapshot snapshot = current with { Settings = [choice] };
            SettingPlan plan = SettingEngine.BuildPlan(snapshot, ["mouse_acceleration"]);
            if (!plan.CanApply)
            {
                MessageBox.Show(this, plan.Blocked.Count > 0 ? string.Join("\n", plan.Blocked) :
                    "すでに指定された設定です。", "設定確認");
                return;
            }
            var change = plan.Changes[0];
            if (MessageBox.Show(this,
                $"マウス加速を変更します。\n\n現在: {change.CurrentValue}\n変更後: {change.TargetValue}\n\n変更前の値を保存して続けますか？",
                "自由設定の適用", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            string result = SettingEngine.Apply(plan);
            Scan();
            MessageBox.Show(this, result, "設定完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Scan();
            MessageBox.Show(this, ex.Message, "設定失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshPresetChoices(string? chosen = null)
    {
        try
        {
            string? previous = chosen ?? presetChoices.SelectedItem as string;
            presetChoices.Items.Clear();
            foreach (var item in PresetStore.Load()) presetChoices.Items.Add(item.Name);
            if (previous != null && presetChoices.Items.Contains(previous))
                presetChoices.SelectedItem = previous;
            else if (presetChoices.Items.Count > 0) presetChoices.SelectedIndex = 0;
            presetLoad.Enabled = presetChoices.SelectedItem != null;
            presetDelete.Enabled = presetChoices.SelectedItem != null;
        }
        catch (Exception ex)
        {
            presetLoad.Enabled = false;
            presetDelete.Enabled = false;
            MessageBox.Show(this, "プリセット読み込み失敗: " + ex.Message,
                "プリセット", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SavePreset()
    {
        try
        {
            string name = presetName.Text.Trim();
            var existing = PresetStore.Load().FirstOrDefault(x =>
                string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing != null && MessageBox.Show(this,
                $"「{existing.Name}」を上書きしますか？", "プリセット保存",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            PresetStore.Save(name, mouseOn.Checked);
            RefreshPresetChoices(name);
            MessageBox.Show(this, "プリセットを保存しました。Windows設定は変更していません。",
                "プリセット", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "プリセット保存失敗: " + ex.Message,
                "プリセット", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadPreset()
    {
        if (presetChoices.SelectedItem is not string name) return;
        try
        {
            var preset = PresetStore.Load().FirstOrDefault(x => x.Name == name)
                ?? throw new FileNotFoundException("選択されたプリセットが見つかりません。");
            mouseOn.Checked = preset.MouseAcceleration;
            mouseOff.Checked = !preset.MouseAcceleration;
            presetName.Text = preset.Name;
            MessageBox.Show(this, "選択内容を読み込みました。Windows設定はまだ変更していません。",
                "プリセット", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "プリセット読み込み失敗: " + ex.Message,
                "プリセット", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DeletePreset()
    {
        if (presetChoices.SelectedItem is not string name) return;
        if (MessageBox.Show(this, $"「{name}」を削除しますか？",
            "プリセット削除", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            PresetStore.Delete(name);
            RefreshPresetChoices();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "プリセット削除失敗: " + ex.Message,
                "プリセット", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        powerRestoreButton.Enabled = PowerPlanManager.CanRestore;
        status.Text = $"診断: {current.Settings.Count}項目 / 選択: {count} / 比較可能: {ready}（コピー適用: マウス加速 / 自由設定: マウス加速・電源）" +
            (source == null ? " / コピー元JSON未選択" : $" / コピー元: {source.ComputerName}") +
            " / その他の項目は読み取り・比較専用";
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
