namespace PCSetupCopier;

internal sealed class ApplyPreviewDialog : Form
{
    internal ApplyPreviewDialog(BatchSettingsApplier.Plan plan, string? powerOld, string? powerNew)
    {
        Text = "一括適用する設定の確認";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(590, 410);
        MinimumSize = new Size(450, 330);
        Font = new Font("Yu Gothic UI", 10);

        var explanation = new Label {
            Dock = DockStyle.Top, Height = 62, Padding = new Padding(18, 15, 15, 0),
            Font = new Font("Yu Gothic UI", 12, FontStyle.Bold),
            Text = "次の変更をまとめて適用します"
        };
        var changes = new List<string>();
        if (plan.ChangeMouse)
            changes.Add("マウス加速\n  現在: " +
                (plan.OriginalMouse[2] == 0 ? "オフ" : "オン") +
                " → 変更後: " + (plan.DesiredMouse[2] == 0 ? "オフ" : "オン"));
        if (plan.ChangePower)
            changes.Add("電源プラン\n  現在: " + (powerOld ?? plan.OriginalPower.ToString()) +
                "\n  変更後: " + (powerNew ?? plan.DesiredPower.ToString()));
        if (plan.ChangeGameMode)
            changes.Add("ゲームモード関連のユーザー設定値\n  現在: " +
                (plan.OriginalGameMode == null ? "未設定" : plan.OriginalGameMode == 1 ? "オン指定" : "オフ指定") +
                "\n  変更後: " + (plan.DesiredGameMode == 1 ? "オン指定" : "オフ指定") +
                "\n  ※ Windows設定画面の実効状態は保証しません。");
        if (plan.ChangeCapture)
            changes.Add("バックグラウンド録画の関連設定値\n  現在: " +
                (plan.OriginalCapture == null ? "未設定" : plan.OriginalCapture == 1 ? "オン指定" : "オフ指定") +
                "\n  変更後: " + (plan.DesiredCapture == 1 ? "オン指定" : "オフ指定") +
                "\n  ※ Game Barの実効録画状態は保証しません。");
        var details = new TextBox {
            Dock = DockStyle.Fill, ReadOnly = true, Multiline = true,
            ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None,
            BackColor = Color.White, Font = new Font("Yu Gothic UI", 11),
            Margin = new Padding(18), Text = string.Join("\r\n\r\n", changes)
        };
        var safety = new Label {
            Dock = DockStyle.Bottom, Height = 63,
            Text = "変更前の値をバックアップし、適用後に再確認します。\n途中で失敗した場合は自動復元を試みます。",
            ForeColor = Color.DimGray, Padding = new Padding(18, 8, 8, 0)
        };
        var buttons = new FlowLayoutPanel {
            Dock = DockStyle.Bottom, Height = 57,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8, 10, 12, 8)
        };
        var confirm = new Button { Text = "適用する", DialogResult = DialogResult.OK, Width = 125, Height = 32 };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Width = 125, Height = 32 };
        buttons.Controls.Add(confirm);
        buttons.Controls.Add(cancel);
        Controls.Add(details);
        Controls.Add(safety);
        Controls.Add(buttons);
        Controls.Add(explanation);
        AcceptButton = confirm;
        CancelButton = cancel;
    }
}
