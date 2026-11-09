namespace PCSetupCopier;

// Reusable setting card with a fixed-width content area that adapts to the available page width.
internal sealed class SettingCard : Panel
{
    internal CheckBox Selected { get; } = new()
    {
        Text = "変更する", AutoSize = true, Checked = false
    };
    internal Label Current { get; } = new() { AutoSize = true, ForeColor = Color.FromArgb(80, 90, 100) };
    internal FlowLayoutPanel Editor { get; } = new()
    {
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        FlowDirection = FlowDirection.TopDown, WrapContents = false,
        Margin = new Padding(0, 8, 0, 0)
    };
    private readonly Label descriptionLabel;
    private readonly TableLayoutPanel layout;

    internal SettingCard(string name, string description, bool supported)
    {
        BackColor = Color.White;
        BorderStyle = BorderStyle.FixedSingle;
        Height = 184;
        Width = 680;
        Margin = new Padding(4, 4, 4, 12);
        Padding = new Padding(14, 12, 14, 8);

        layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            AutoScroll = false
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false,
            AutoSize = true, Height = 38
        };
        var title = new Label { Text = name, AutoSize = true,
            Font = new Font("Yu Gothic UI", 13, FontStyle.Bold),
            Margin = new Padding(0, 4, 16, 2) };
        top.Controls.Add(title);
        Selected.Margin = new Padding(0, 7, 0, 0);
        top.Controls.Add(Selected);
        descriptionLabel = new Label
        {
            Text = description, AutoSize = true,
            ForeColor = Color.DimGray, MaximumSize = new Size(640, 0)
        };
        layout.Controls.Add(top, 0, 0);
        layout.Controls.Add(descriptionLabel, 0, 1);
        layout.Controls.Add(Current, 0, 2);
        layout.Controls.Add(Editor, 0, 3);
        Controls.Add(layout);
        if (!supported)
        {
            Selected.Checked = false;
            Selected.Enabled = false;
            Selected.Text = "未対応";
            BackColor = Color.FromArgb(245, 246, 248);
        }
    }

    internal void SetCardWidth(int width)
    {
        Width = Math.Max(340, width);
        descriptionLabel.MaximumSize = new Size(Math.Max(250, Width - 45), 0);
    }
}
