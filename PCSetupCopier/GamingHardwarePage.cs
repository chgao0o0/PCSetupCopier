using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PCSetupCopier;

// Gaming-PC workspace: per-game GPU preferences and per-monitor refresh diagnostics.
// Display mode changes remain in Windows Settings to avoid black-screen failures.
internal sealed class GamingHardwarePage : UserControl
{
    private readonly DataGridView games = new() {
        Dock = DockStyle.Fill, BackgroundColor = Color.White,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
    };
    private readonly ListView displays = new() {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
        GridLines = true, HideSelection = false
    };
    private readonly ComboBox monitorChoice = new() {
        Width = 195, DropDownStyle = ComboBoxStyle.DropDownList
    };
    private readonly ComboBox refreshChoice = new() {
        Width = 95, DropDownStyle = ComboBoxStyle.DropDownList
    };
    private readonly Button changeRefresh = new() {
        Text = "選択したHzに変更", AutoSize = true
    };
    private readonly Button restore = new() { Text = "前回のGPU設定を復元", AutoSize = true };
    private bool _discovering;
    private readonly Label info = new() {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.FromArgb(68, 88, 117), AutoEllipsis = true
    };

    internal GamingHardwarePage()
    {
        BackColor = Color.FromArgb(248, 250, 253);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1,
            Padding = new Padding(16, 10, 16, 10) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 165));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.Controls.Add(new Label {
            Text = "ディスプレイ診断（接続モニター別）", Dock = DockStyle.Fill,
            Font = new Font("Yu Gothic UI", 13, FontStyle.Bold),
            ForeColor = Color.FromArgb(35, 48, 67)
        }, 0, 0);

        displays.Columns.Add("画面", 115);
        displays.Columns.Add("現在の解像度 / Hz", 250);
        displays.Columns.Add("同じ解像度で選べるHz", 450);
        displays.Columns.Add("補足", 290);
        layout.Controls.Add(displays, 0, 1);

        var commands = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2,
            ColumnCount = 1 };
        commands.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        commands.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        var gameActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        AddButton(gameActions, "ゲームをまとめて自動検出", DiscoverGames);
        AddButton(gameActions, "検索フォルダー追加", AddSearchFolder);
        AddButton(gameActions, "exeを手動追加", AddGames);
        AddButton(gameActions, "選択行を削除", RemoveSelected);
        AddButton(gameActions, "診断を更新", RefreshAll);
        commands.Controls.Add(gameActions, 0, 1);
        var monitorActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        monitorActions.Controls.Add(new Label { Text = "画面", AutoSize = true,
            Margin = new Padding(4, 10, 5, 0) });
        monitorActions.Controls.Add(monitorChoice);
        monitorActions.Controls.Add(new Label { Text = "変更後", AutoSize = true,
            Margin = new Padding(14, 10, 5, 0) });
        monitorActions.Controls.Add(refreshChoice);
        monitorActions.Controls.Add(changeRefresh);
        AddButton(monitorActions, "Windowsの詳細ディスプレイ設定", OpenAdvancedDisplay);
        monitorChoice.SelectedIndexChanged += (_, _) => UpdateRefreshChoices();
        changeRefresh.Click += (_, _) => ApplyRefresh();
        commands.Controls.Add(monitorActions, 0, 0);
        layout.Controls.Add(commands, 0, 2);

        games.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Apply", HeaderText = "適用", FillWeight = 40 });
        games.Columns.Add(new DataGridViewTextBoxColumn { Name = "Game", HeaderText = "ゲーム", ReadOnly = true, FillWeight = 120 });
        games.Columns.Add(new DataGridViewTextBoxColumn { Name = "Path", HeaderText = "exeのパス", ReadOnly = true, FillWeight = 250 });
        games.Columns.Add(new DataGridViewTextBoxColumn { Name = "Current", HeaderText = "現在の指定", ReadOnly = true, FillWeight = 115 });
        var desired = new DataGridViewComboBoxColumn {
            Name = "Desired", HeaderText = "変更後のGPU優先設定", FillWeight = 160,
            FlatStyle = FlatStyle.Flat
        };
        desired.Items.AddRange("Windowsに任せる", "省電力を優先", "高パフォーマンスを優先");
        games.Columns.Add(desired);
        games.CurrentCellDirtyStateChanged += (_, _) => {
            if (games.IsCurrentCellDirty)
                games.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        games.DataError += (_, e) => { e.ThrowException = false; info.Text = "不正なGPU設定が選択されています。"; };
        layout.Controls.Add(games, 0, 3);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 195));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));
        info.Text = "ゲームのexeを選び、適用欄にチェックしてから変更します。GPU優先指定は実際のGPU利用を保証しません。";
        bottom.Controls.Add(info, 0, 0);
        restore.Dock = DockStyle.Fill;
        restore.Margin = new Padding(6);
        restore.Click += (_, _) => RestoreGames();
        bottom.Controls.Add(restore, 1, 0);
        var apply = new Button { Text = "選択ゲームをまとめて適用", Dock = DockStyle.Fill,
            Margin = new Padding(6), BackColor = Color.FromArgb(34, 97, 176),
            ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        apply.FlatAppearance.BorderSize = 0;
        apply.Click += (_, _) => ApplyGames();
        bottom.Controls.Add(apply, 2, 0);
        layout.Controls.Add(bottom, 0, 4);
        Controls.Add(layout);
        try
        {
            foreach (string path in GameGpuPreferences.LoadGameList())
                AddGameRow(path);
        }
        catch (Exception ex) { info.Text = "保存したゲーム一覧の読み込みに失敗しました: " + ex.Message; }
        RefreshAll();
    }

    private void AddGameRow(string path)
    {
        if (games.Rows.Cast<DataGridViewRow>().Any(x =>
            string.Equals(x.Cells["Path"].Value as string, path, StringComparison.OrdinalIgnoreCase)))
            return;
        string? value = GameGpuPreferences.Read(path);
        int row = games.Rows.Add(false, Path.GetFileNameWithoutExtension(path), path,
            GameGpuPreferences.Label(value), GameGpuPreferences.Label(value));
        games.Rows[row].Cells["Path"].ToolTipText = path;
    }

    private void SaveGameRows()
    {
        GameGpuPreferences.SaveGameList(games.Rows.Cast<DataGridViewRow>()
            .Select(x => x.Cells["Path"].Value as string ?? ""));
    }

    private static void AddButton(FlowLayoutPanel parent, string text, Action action)
    {
        var b = new Button { Text = text, AutoSize = true, Height = 34 };
        b.Click += (_, _) => action();
        parent.Controls.Add(b);
    }

    private static void OpenAdvancedDisplay()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:display-advanced") { UseShellExecute = true }); }
        catch (Exception e) { MessageBox.Show("Windowsのディスプレイ設定を開けません: " + e.Message); }
    }

    private void UpdateRefreshChoices()
    {
        refreshChoice.Items.Clear();
        changeRefresh.Enabled = false;
        if (monitorChoice.SelectedItem is not DisplayMonitorItem item) return;
        foreach (var hz in item.Value.Choices.Select(x => x.Hz))
            refreshChoice.Items.Add(hz);
        int index = refreshChoice.Items.IndexOf(item.Value.CurrentHz);
        refreshChoice.SelectedIndex = index >= 0 ? index : refreshChoice.Items.Count > 0 ? 0 : -1;
        changeRefresh.Enabled = refreshChoice.SelectedItem is int;
    }

    private sealed record DisplayMonitorItem(DisplayRefreshManager.Monitor Value)
    {
        public override string ToString() => Value.Label + " (" + Value.Id + ")";
    }

    private void ApplyRefresh()
    {
        if (monitorChoice.SelectedItem is not DisplayMonitorItem monitor ||
            refreshChoice.SelectedItem is not int hz) return;
        try
        {
            if (MessageBox.Show(this,
                monitor.Value.Label + " のリフレッシュレートを " + hz +
                "Hzに一時変更します。\n画面が正常に映らない場合は15秒後に自動復元します。\n" +
                "ただしPCのフリーズや電源断では復元できない可能性があります。続行しますか？",
                "ディスプレイ設定の変更", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
                != DialogResult.Yes) return;
            string result = DisplayRefreshManager.ApplyWithConfirmation(this, monitor.Value, hz);
            RefreshAll();
            MessageBox.Show(this, result, "リフレッシュレート", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            RefreshAll();
            MessageBox.Show(this, ex.Message, "リフレッシュレート変更失敗",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RefreshAll()
    {
        try
        {
            string? previouslyChosen = (monitorChoice.SelectedItem as DisplayMonitorItem)?.Value.Id;
            monitorChoice.Items.Clear();
            var activeMonitors = DisplayRefreshManager.Read();
            foreach (var item in activeMonitors)
                monitorChoice.Items.Add(new DisplayMonitorItem(item));
            int monitorIndex = activeMonitors.ToList().FindIndex(x => x.Id == previouslyChosen);
            if (monitorIndex < 0) monitorIndex = 0;
            if (monitorChoice.Items.Count > 0) monitorChoice.SelectedIndex = monitorIndex;
            displays.Items.Clear();
            foreach (var screen in Screen.AllScreens)
            {
                if (!TryMode(screen.DeviceName, -1, out var mode)) continue;
                var hz = new SortedSet<int>();
                for (int i = 0; i < 1024; i++)
                {
                    if (!TryMode(screen.DeviceName, i, out var candidate)) break;
                    if (candidate.dmPelsWidth == mode.dmPelsWidth &&
                        candidate.dmPelsHeight == mode.dmPelsHeight &&
                        candidate.dmBitsPerPel == mode.dmBitsPerPel &&
                        candidate.dmDisplayFrequency is > 1 and <= 1000)
                        hz.Add((int)candidate.dmDisplayFrequency);
                }
                var entry = new ListViewItem(screen.DeviceName);
                entry.SubItems.Add(mode.dmPelsWidth + "×" + mode.dmPelsHeight +
                    " / " + mode.dmDisplayFrequency + "Hz");
                entry.SubItems.Add(hz.Count == 0 ? "取得不可" : string.Join(", ", hz.Select(x => x + "Hz")));
                entry.SubItems.Add(screen.Primary ? "メイン画面" : "サブ画面");
                displays.Items.Add(entry);
            }
            foreach (DataGridViewRow row in games.Rows)
                RefreshGameRow(row);
            restore.Enabled = GameGpuPreferences.CanRestore;
        }
        catch (Exception ex)
        {
            info.Text = "診断更新で問題が発生しました: " + ex.Message;
        }
    }

    private void AddSearchFolder()
    {
        using var picker = new FolderBrowserDialog {
            Description = "インストールしたゲームが入っているフォルダーを選んでください",
            UseDescriptionForTitle = true, ShowNewFolderButton = false
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            GeneralGameDiscovery.AddFolder(picker.SelectedPath);
            MessageBox.Show(this,
                "検索フォルダーを登録しました。ゲームの自動検出を実行すると、このフォルダーも調べます。\n" +
                "フォルダーの登録だけではWindowsの設定を変更しません。",
                "検索対象を追加", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "検索フォルダーの追加に失敗",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async void DiscoverGames()
    {
        if (_discovering) return;
        _discovering = true;
        UseWaitCursor = true;
        info.Text = "インストール情報・ゲームフォルダーを検索しています…";
        try
        {
            var detected = await Task.Run(() => GameDiscovery.Find());
            if (IsDisposed) return;
            var found = detected.Where(x => !games.Rows.Cast<DataGridViewRow>().Any(
                row => string.Equals(row.Cells["Path"].Value as string, x.Exe,
                    StringComparison.OrdinalIgnoreCase))).Take(300).ToArray();
            if (found.Length == 0)
            {
                MessageBox.Show(this, "インストール情報・ショートカット・ゲームフォルダーから新しいexe候補が見つかりませんでした。検索フォルダー追加や手動登録を利用できます。",
                    "ゲームの自動検出", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using var dialog = new Form {
                Text = "ゲーム自動検出 — 登録するexeを確認",
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(980, 640),
                MinimumSize = new Size(600, 380)
            };
            var list = new DataGridView {
                Dock = DockStyle.Fill, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToAddRows = false, RowHeadersVisible = false,
                BackgroundColor = Color.White
            };
            list.Columns.Add(new DataGridViewCheckBoxColumn {
                HeaderText = "登録", Name = "Register", FillWeight = 45
            });
            list.Columns.Add(new DataGridViewTextBoxColumn {
                HeaderText = "ゲーム名", ReadOnly = true, FillWeight = 130
            });
            list.Columns.Add(new DataGridViewTextBoxColumn {
                HeaderText = "検出元", ReadOnly = true, FillWeight = 65
            });
            list.Columns.Add(new DataGridViewTextBoxColumn {
                HeaderText = "exe候補（確認して選択）", ReadOnly = true, FillWeight = 300
            });
            foreach (var item in found)
            {
                int row = list.Rows.Add(!item.Ambiguous, item.Title, item.Store, item.Exe);
                list.Rows[row].Tag = item;
                list.Rows[row].Cells[3].ToolTipText =
                    item.Ambiguous ? "ゲーム本体のexeか確認が必要です。自動で適用対象にはしません。" : item.Exe;
            }
            var heading = new Label {
                Dock = DockStyle.Top, Height = 56,
                Text = "Steam・Epic・他のランチャー・インストール情報・ショートカット・ゲームフォルダーから検索。\nゲーム以外のexeも含む可能性があります。正しいexeを確認し、登録するものだけチェックしてください。",
                Padding = new Padding(12, 8, 8, 5)
            };
            var actions = new FlowLayoutPanel {
                Dock = DockStyle.Bottom, Height = 54,
                FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 8, 12, 6)
            };
            var accept = new Button { Text = "チェックしたゲームを登録", Width = 210, Height = 34,
                DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "キャンセル", Width = 105, Height = 34,
                DialogResult = DialogResult.Cancel };
            actions.Controls.Add(accept);
            actions.Controls.Add(cancel);
            list.CurrentCellDirtyStateChanged += (_, _) => {
                if (list.IsCurrentCellDirty) list.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            dialog.Controls.Add(list);
            dialog.Controls.Add(actions);
            dialog.Controls.Add(heading);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            int added = 0;
            foreach (DataGridViewRow row in list.Rows)
            {
                if (row.Cells[0].Value is not true || row.Tag is not GameDiscovery.Candidate item) continue;
                try { AddGameRow(item.Exe); added++; } catch { }
            }
            SaveGameRows();
            info.Text = added + "件のゲームを登録しました。GPU優先設定はまだ変更していません。";
        }
        catch (Exception ex)
        {
            if (!IsDisposed) MessageBox.Show(this, ex.Message, "ゲーム自動検出に失敗", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            _discovering = false;
            if (!IsDisposed) UseWaitCursor = false;
        }
    }

    private void AddGames()
    {
        using var picker = new OpenFileDialog {
            Filter = "ゲーム実行ファイル (*.exe)|*.exe",
            Title = "ゲーム本体の実行ファイルを選択", Multiselect = true,
            CheckFileExists = true
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        foreach (string path in picker.FileNames)
        {
            try
            {
                AddGameRow(path);
            }
            catch (Exception ex) { MessageBox.Show(this, Path.GetFileName(path) + ": " + ex.Message, "追加できません"); }
        }
        try { SaveGameRows(); }
        catch (Exception ex) { info.Text = "ゲーム一覧の保存に失敗しました: " + ex.Message; return; }
        info.Text = games.Rows.Count + "件のゲームを登録中。ゲーム一覧はこのPC内に保存されます。";
    }

    private void RemoveSelected()
    {
        foreach (DataGridViewRow row in games.SelectedRows.Cast<DataGridViewRow>().ToArray())
            games.Rows.Remove(row);
        try { SaveGameRows(); }
        catch (Exception ex) { info.Text = "ゲーム一覧の保存に失敗しました: " + ex.Message; }
    }

    private void RefreshGameRow(DataGridViewRow row)
    {
        if (row.Cells["Path"].Value is not string path) return;
        try
        {
            string? actual = GameGpuPreferences.Read(path);
            row.Cells["Current"].Value = GameGpuPreferences.Label(actual);
            row.Cells["Current"].ToolTipText = actual ?? "未登録";
        }
        catch (Exception ex)
        {
            row.Cells["Current"].Value = "取得失敗";
            row.Cells["Current"].ToolTipText = ex.Message;
        }
    }

    private void ApplyGames()
    {
        try
        {
            games.EndEdit();
            var selections = new List<GameGpuPreferences.Request>();
            foreach (DataGridViewRow row in games.Rows)
            {
                if (row.Cells["Apply"].Value is not true) continue;
                string path = row.Cells["Path"].Value as string ?? "";
                int target = (row.Cells["Desired"].Value as string) switch
                {
                    "Windowsに任せる" => 0,
                    "省電力を優先" => 1,
                    "高パフォーマンスを優先" => 2,
                    _ => throw new InvalidOperationException("GPU優先設定を選択してください。")
                };
                selections.Add(new(path, target));
            }
            var plan = GameGpuPreferences.Preview(selections);
            if (plan.Count == 0)
            {
                MessageBox.Show(this, "指定したゲームは現在の設定と一致しています。", "GPU設定");
                return;
            }
            string details = string.Join("\n", plan.Select(x => Path.GetFileName(x.ExePath) +
                " : " + GameGpuPreferences.Label(x.Before) + " → " + GameGpuPreferences.Label(x.After)));
            if (MessageBox.Show(this,
                "選択した" + plan.Count + "件のゲームだけGPU優先設定を変更します。\n\n" + details +
                "\n\n事前バックアップ・適用後検証・失敗時の復元を行います。ゲームは再起動してください。続行しますか？",
                "ゲーム別GPU設定の確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                != DialogResult.Yes) return;
            string outcome = GameGpuPreferences.Apply(plan);
            RefreshAll();
            MessageBox.Show(this, outcome, "GPU設定", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            RefreshAll();
            MessageBox.Show(this, ex.Message, "GPU設定失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RestoreGames()
    {
        if (MessageBox.Show(this,
            "前回まとめて変更したゲーム別GPU設定を元に戻します。\nその後に設定を手動変更していた場合は復元を中止します。よろしいですか？",
            "GPU設定の復元", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
            != DialogResult.Yes) return;
        try
        {
            string result = GameGpuPreferences.Restore();
            RefreshAll();
            MessageBox.Show(this, result, "GPU設定の復元", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "復元失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private static bool TryMode(string name, int index, out DEVMODE mode)
    {
        mode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
        return EnumDisplaySettings(name, index, ref mode);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE mode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2;
        public uint dmPanningWidth, dmPanningHeight;
    }
}
