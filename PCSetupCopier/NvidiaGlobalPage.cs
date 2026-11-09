namespace PCSetupCopier;

// This tab is an actual NVIDIA NVAPI DRS global-settings editor.
// All edits remain in memory until the explicit Apply button + confirmation dialog.
internal sealed class NvidiaGlobalPage : UserControl
{
    private readonly DataGridView grid = new() {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        RowHeadersVisible = false, BackgroundColor = Color.White,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
    };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft };
    private readonly ComboBox templates = new() {
        DropDownStyle = ComboBoxStyle.DropDownList, Width = 215
    };
    private readonly TextBox templateName = new() { Width = 160 };
    private readonly Button apply = new() {
        Text = "NVIDIAグローバル設定を一括適用", AutoSize = true,
        Height = 42, BackColor = Color.FromArgb(34, 97, 176), ForeColor = Color.White
    };
    private readonly Button restore = new() { Text = "前回の設定を復元", AutoSize = true, Height = 38 };
    private bool loaded;

    internal NvidiaGlobalPage()
    {
        BackColor = Color.FromArgb(248, 250, 253);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1,
            RowCount = 5, Padding = new Padding(14) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 95));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));

        layout.Controls.Add(new Label {
            Text = "NVIDIA グローバル3D設定（NVAPI）", Dock = DockStyle.Fill,
            Font = new Font("Yu Gothic UI", 15, FontStyle.Bold)
        }, 0, 0);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        AddButton(actions, "現在のNVIDIA設定を取得", RefreshDriver);
        AddButton(actions, "FPS重視", () => StageProfile("fps"));
        AddButton(actions, "バランス重視", () => StageProfile("balanced"));
        AddButton(actions, "画質重視", () => StageProfile("visual"));
        AddButton(actions, "現在値を希望値に取り込む", CaptureCurrent);
        layout.Controls.Add(actions, 0, 1);

        grid.Columns.Add(new DataGridViewCheckBoxColumn {
            HeaderText = "適用対象", Name = "Apply", FillWeight = 48
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn {
            HeaderText = "設定", Name = "Name", FillWeight = 130, ReadOnly = true
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn {
            HeaderText = "現在値", Name = "Current", FillWeight = 135, ReadOnly = true
        });
        var desired = new DataGridViewComboBoxColumn {
            Name = "Desired", HeaderText = "希望値（変更予定）",
            FillWeight = 160, FlatStyle = FlatStyle.Flat
        };
        // Row-specific dropdown lists are set per-cell below.
        grid.Columns.Add(desired);
        grid.CurrentCellDirtyStateChanged += (_, _) => {
            if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        grid.DataError += (_, e) => { e.ThrowException = false; };
        layout.Controls.Add(grid, 0, 2);

        var personal = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        personal.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        personal.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        var saveLine = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        saveLine.Controls.Add(new Label { Text = "自分のNVIDIAプリセット", AutoSize = true,
            Margin = new Padding(0, 10, 8, 0) });
        saveLine.Controls.Add(templateName);
        AddButton(saveLine, "選択値を保存", SaveTemplate);
        personal.Controls.Add(saveLine, 0, 0);
        var loadLine = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        loadLine.Controls.Add(templates);
        AddButton(loadLine, "自作設定を読み込む", LoadTemplate);
        loadLine.Controls.Add(new Label {
            Text = "読み込みだけではNVIDIA設定を変更しません。",
            ForeColor = Color.DimGray, AutoSize = true,
            Margin = new Padding(12, 10, 0, 0)
        });
        personal.Controls.Add(loadLine, 0, 1);
        layout.Controls.Add(personal, 0, 3);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 265));
        status.Text = "NVIDIAドライバーがあるゲーミングPCで「現在の設定を取得」を押してください。";
        bottom.Controls.Add(status, 0, 0);
        restore.Dock = DockStyle.Fill;
        restore.Click += (_, _) => RestoreDriver();
        bottom.Controls.Add(restore, 1, 0);
        apply.Dock = DockStyle.Fill;
        apply.Click += (_, _) => ApplyDriver();
        bottom.Controls.Add(apply, 2, 0);
        layout.Controls.Add(bottom, 0, 4);
        Controls.Add(layout);

        foreach (var setting in NvidiaGlobalDrs.Settings)
        {
            int idx = grid.Rows.Add(false, setting.Title, "未取得", null);
            grid.Rows[idx].Tag = setting;
            var combo = new DataGridViewComboBoxCell();
            foreach (var option in setting.Options) combo.Items.Add(option.Label);
            combo.Value = setting.Options[0].Label;
            grid.Rows[idx].Cells["Desired"] = combo;
        }
        RefreshTemplates();
        restore.Enabled = NvidiaGlobalDrs.CanRestore;
        apply.Enabled = false;
    }

    private static void AddButton(FlowLayoutPanel panel, string title, Action callback)
    {
        var button = new Button { Text = title, AutoSize = true,
            Height = 36, Margin = new Padding(2, 3, 7, 3) };
        button.Click += (_, _) => callback();
        panel.Controls.Add(button);
    }

    private void RefreshTemplates()
    {
        templates.Items.Clear();
        foreach (var item in NvidiaProfileStore.Load())
            templates.Items.Add(item.Name);
        if (templates.Items.Count > 0) templates.SelectedIndex = 0;
    }

    private void RefreshDriver()
    {
        try
        {
            var states = NvidiaGlobalDrs.ReadAll().ToDictionary(x => x.Key);
            foreach (DataGridViewRow row in grid.Rows)
            {
                var item = (NvidiaGlobalDrs.Setting)row.Tag!;
                var state = states[item.Key];
                string current = NvidiaGlobalDrs.Label(item, state.Value);
                row.Cells["Current"].Value = current;
                row.Cells["Current"].ToolTipText = state.IsExplicit
                    ? "グローバル設定に明示指定あり" : "プリセット／既定値";
                if (!loaded)
                {
                    var preferred = item.Options.FirstOrDefault(x => x.Value == state.Value);
                    row.Cells["Desired"].Value = preferred?.Label ?? item.Options[0].Label;
                }
            }
            loaded = true;
            apply.Enabled = true;
            status.Text = "NVAPIからグローバル設定を取得しました。適用対象だけ変更します。";
        }
        catch (Exception ex)
        {
            loaded = false;
            apply.Enabled = false;
            status.Text = "NVIDIA設定を取得できません: " + ex.Message;
        }
        restore.Enabled = NvidiaGlobalDrs.CanRestore;
    }

    private void StageProfile(string type)
    {
        if (!loaded)
        {
            RefreshDriver();
            if (!loaded) return;
        }
        var targets = type switch
        {
            "fps" => new Dictionary<string, uint> {
                ["power"] = 1, ["vsync"] = 0x08416747, ["texture"] = 20,
                ["fps"] = 0 // no arbitrary global limit
            },
            "balanced" => new Dictionary<string, uint> {
                ["power"] = 5, ["vsync"] = 0x60925292, ["texture"] = 0,
                ["fps"] = 0
            },
            _ => new Dictionary<string, uint> {
                ["power"] = 5, ["vsync"] = 0x60925292, ["texture"] = 0xfffffff6,
                ["fps"] = 0
            }
        };
        Stage(targets);
        status.Text = "プリセットを希望値にセットしました。まだNVIDIA設定は変更していません。";
    }

    private void CaptureCurrent()
    {
        try
        {
            var states = NvidiaGlobalDrs.ReadAll().ToDictionary(x => x.Key);
            var values = new Dictionary<string, uint>();
            foreach (var item in NvidiaGlobalDrs.Settings)
            {
                if (states[item.Key].Value is uint value &&
                    item.Options.Any(x => x.Value == value))
                    values[item.Key] = value;
            }
            if (values.Count == 0)
                throw new InvalidOperationException("取り込める既知のNVIDIA設定値がありません。");
            loaded = true;
            Stage(values);
            status.Text = "現在のNVIDIA設定を取り込みました。必要なら名前を付けて保存できます。適用はしていません。";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "設定取込失敗"); }
    }

    private void Stage(IReadOnlyDictionary<string, uint> values)
    {
        foreach (DataGridViewRow row in grid.Rows)
        {
            var item = (NvidiaGlobalDrs.Setting)row.Tag!;
            row.Cells["Apply"].Value = false;
            if (!values.TryGetValue(item.Key, out uint value)) continue;
            var option = item.Options.FirstOrDefault(x => x.Value == value);
            if (option == null) continue;
            row.Cells["Desired"].Value = option.Label;
            row.Cells["Apply"].Value = true;
        }
        apply.Enabled = loaded;
    }

    private Dictionary<string, uint> Selected()
    {
        grid.EndEdit();
        var values = new Dictionary<string, uint>();
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.Cells["Apply"].Value is not true) continue;
            var item = (NvidiaGlobalDrs.Setting)row.Tag!;
            string? text = row.Cells["Desired"].Value as string;
            var option = item.Options.FirstOrDefault(x => x.Label == text)
                ?? throw new InvalidDataException(item.Title + "の希望値が不正です。");
            values.Add(item.Key, option.Value);
        }
        return values;
    }

    private void SaveTemplate()
    {
        try
        {
            var values = Selected();
            NvidiaProfileStore.Save(templateName.Text, values);
            RefreshTemplates();
            templates.SelectedItem = templateName.Text.Trim();
            status.Text = "自作NVIDIAプリセットを保存しました。Windowsやドライバーは変更していません。";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "保存失敗"); }
    }

    private void LoadTemplate()
    {
        try
        {
            string name = templates.SelectedItem as string
                ?? throw new InvalidOperationException("読み込むプリセットを選択してください。");
            if (!loaded) RefreshDriver();
            if (!loaded) return;
            var entry = NvidiaProfileStore.Load().First(x => x.Name == name);
            Stage(entry.Values);
            templateName.Text = entry.Name;
            status.Text = "「" + name + "」を希望値に読み込みました。ドライバーは未変更です。";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "読込失敗"); }
    }

    private void ApplyDriver()
    {
        try
        {
            if (!loaded) throw new InvalidOperationException("まずNVIDIA設定を取得してください。");
            var changes = NvidiaGlobalDrs.Preview(Selected().Select(x => (x.Key, x.Value)));
            if (changes.Count == 0)
            {
                MessageBox.Show(this, "選択項目は現在のNVIDIA設定と同じです。"); return;
            }
            var lines = changes.Select(x => {
                var item = NvidiaGlobalDrs.Settings.Single(s => s.Key == x.Key);
                return item.Title + ": " + NvidiaGlobalDrs.Label(item, x.Before) +
                    " → " + NvidiaGlobalDrs.Label(item, x.After);
            });
            if (MessageBox.Show(this,
                "以下のNVIDIAグローバル設定を変更します。\n" +
                string.Join("\n", lines) +
                "\n\n変更前の値をバックアップし、ドライバー保存後に再取得して確認します。" +
                "\nグローバル設定はゲーム別プロファイルで上書きされる場合があります。続行しますか？",
                "NVIDIAグローバル設定の一括適用",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            apply.Enabled = false;
            var result = NvidiaGlobalDrs.Apply(changes);
            RefreshDriver();
            MessageBox.Show(this, result, "NVIDIA設定", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            RefreshDriver();
            MessageBox.Show(this, ex.Message, "NVIDIA設定適用失敗",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RestoreDriver()
    {
        if (MessageBox.Show(this, "前回のNVIDIAグローバル設定を復元しますか？",
            "復元確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            var result = NvidiaGlobalDrs.Restore();
            RefreshDriver();
            MessageBox.Show(this, result, "NVIDIA設定復元", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "NVIDIA復元失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
