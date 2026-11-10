using System.Diagnostics;

namespace PCSetupCopier;

// Inspect only backups created by the application. No automatic restore or deletion.
internal sealed class BackupInventoryPanel : UserControl
{
    private static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCSetupCopier", "Backups");
    private readonly ListView rows = new() {
        Dock = DockStyle.Fill, FullRowSelect = true, GridLines = true,
        View = View.Details, MultiSelect = false
    };
    private readonly Label summary = new() {
        Dock = DockStyle.Fill, AutoEllipsis = true,
        ForeColor = Color.FromArgb(68, 90, 118),
        TextAlign = ContentAlignment.MiddleLeft
    };

    internal BackupInventoryPanel()
    {
        Height = 240;
        MinimumSize = new Size(370, 200);
        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill, RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        var refresh = new Button { Text = "バックアップを再確認", AutoSize = true };
        refresh.Click += (_, _) => RefreshInventory();
        actions.Controls.Add(refresh);
        var open = new Button { Text = "保存フォルダーを開く", AutoSize = true };
        open.Click += (_, _) => OpenFolder();
        actions.Controls.Add(open);
        layout.Controls.Add(actions, 0, 0);
        rows.Columns.Add("バックアップ", 360);
        rows.Columns.Add("更新日", 170);
        rows.Columns.Add("サイズ", 85);
        layout.Controls.Add(rows, 0, 1);
        layout.Controls.Add(summary, 0, 2);
        Controls.Add(layout);
        RefreshInventory();
    }

    private void RefreshInventory()
    {
        rows.BeginUpdate();
        try
        {
            rows.Items.Clear();
            if (!Directory.Exists(Folder))
            {
                summary.Text = "まだバックアップは作成されていません。";
                return;
            }
            var files = Directory.EnumerateFiles(Folder, "*", SearchOption.TopDirectoryOnly)
                .Where(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc).Take(150).ToArray();
            foreach (var file in files)
            {
                var row = new ListViewItem(file.Name);
                row.SubItems.Add(file.LastWriteTime.ToString("yyyy/MM/dd HH:mm"));
                row.SubItems.Add((file.Length / 1024d).ToString("F1") + " KB");
                row.ToolTipText = file.Name;
                rows.Items.Add(row);
            }
            summary.Text = files.Length + "件のJSONバックアップを表示中。復元操作は下の各設定ボタンから行います。";
        }
        catch (Exception ex) { summary.Text = "バックアップ取得失敗: " + ex.Message; }
        finally { rows.EndUpdate(); }
    }

    private void OpenFolder()
    {
        try
        {
            if (!Directory.Exists(Folder))
            {
                MessageBox.Show(this, "バックアップフォルダーはまだありません。");
                return;
            }
            Process.Start(new ProcessStartInfo("explorer.exe") {
                UseShellExecute = true, ArgumentList = { Folder }
            });
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "フォルダー表示失敗"); }
    }
}
