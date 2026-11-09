namespace PCSetupCopier;

// Compact two-column card matching the settings mockup: explanation on the left,
// changes on the right. All editing controls are persistent and never recreated.
internal sealed class SettingCard : Panel
{
    private static readonly Color Ink = Color.FromArgb(35, 48, 67);
    private static readonly Color Muted = Color.FromArgb(106, 119, 136);
    private static readonly Color Edge = Color.FromArgb(224, 231, 240);
    private readonly Label descriptionLabel;

    internal SettingToggle ApplySwitch { get; } = new() { AccessibleName = "一括適用の対象" };
    private readonly Label applyLabel = new() {
        Text = "適用対象", AutoSize = true, ForeColor = Color.FromArgb(88, 102, 124),
        Margin = new Padding(0, 8, 7, 0)
    };
    internal Label Current { get; } = new()
    {
        AutoEllipsis = true, Dock = DockStyle.Fill,
        ForeColor = Color.FromArgb(63, 86, 119),
        TextAlign = ContentAlignment.MiddleLeft
    };
    internal FlowLayoutPanel Editor { get; } = new()
    {
        Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
        WrapContents = false, AutoScroll = false,
        Margin = new Padding(0, 2, 0, 0),
        Padding = Padding.Empty
    };

    internal SettingCard(string name, string description, bool supported)
    {
        BackColor = supported ? Color.White : Color.FromArgb(246, 248, 251);
        Height = supported ? 132 : 88;

        Width = 760;
        Margin = new Padding(5, 5, 5, 10);
        Padding = new Padding(18, 10, 18, 10);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = supported ? 3 : 2,
            Margin = Padding.Empty, Padding = Padding.Empty,
            BackColor = BackColor
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        if (supported) content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = name, Dock = DockStyle.Fill,
            Font = new Font("Yu Gothic UI", 12f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft, ForeColor = Ink
        };
        descriptionLabel = new Label
        {
            Text = description, Dock = DockStyle.Fill,
            Font = new Font("Yu Gothic UI", 9.2f),
            ForeColor = Muted, AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        content.Controls.Add(title, 0, 0);
        content.Controls.Add(descriptionLabel, 0, 1);
        if (supported)
        {
            var switchHost = new FlowLayoutPanel {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false, Margin = Padding.Empty
            };
            ApplySwitch.Margin = new Padding(0, 0, 0, 0);
            switchHost.Controls.Add(ApplySwitch);
            switchHost.Controls.Add(applyLabel);
            content.Controls.Add(switchHost, 1, 0);
        }

        if (supported)
        {
            content.Controls.Add(Current, 0, 2);
            content.Controls.Add(Editor, 1, 2);
        }
        else
        {
            var badge = new Label {
                Text = "未対応", AutoSize = true, Anchor = AnchorStyles.Right,
                ForeColor = Muted, BackColor = Color.FromArgb(235, 239, 245),
                Padding = new Padding(9, 5, 9, 5)
            };
            content.Controls.Add(badge, 1, 0);
        }
        Controls.Add(content);

        Paint += (_, e) =>
        {
            using var pen = new Pen(Edge);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        };
    }

    internal void SetCardWidth(int availableWidth)
    {
        Width = Math.Clamp(availableWidth, 340, 800);
    }
}
