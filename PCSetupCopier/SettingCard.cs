namespace PCSetupCopier;

// Compact native WinForms card. Controls are never recreated on resize.
internal sealed class SettingCard : Panel
{
    private static readonly Color Ink = Color.FromArgb(30, 44, 64);
    private static readonly Color Muted = Color.FromArgb(100, 113, 132);
    private static readonly Color Edge = Color.FromArgb(222, 228, 236);
    private readonly bool supported;
    private readonly Label descriptionLabel;
    private readonly Label nameLabel;
    private readonly TableLayoutPanel content;

    internal CheckBox Selected { get; } = new()
    {
        Text = "適用する", AutoSize = true, Checked = false,
        ForeColor = Color.FromArgb(38, 82, 142), Anchor = AnchorStyles.Right
    };
    internal Label Current { get; } = new()
    {
        AutoSize = false, Dock = DockStyle.Fill,
        ForeColor = Color.FromArgb(68, 85, 111),
        AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft
    };
    internal FlowLayoutPanel Editor { get; } = new()
    {
        Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
        WrapContents = false, AutoScroll = false,
        Margin = new Padding(0, 3, 0, 0)
    };

    internal SettingCard(string name, string description, bool supported)
    {
        this.supported = supported;
        BackColor = supported ? Color.White : Color.FromArgb(246, 248, 251);
        BorderStyle = BorderStyle.None;
        Height = supported ? 160 : 96;
        Width = 840;
        Margin = new Padding(4, 4, 4, 10);
        Padding = new Padding(17, 10, 17, 9);

        content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1,
            RowCount = supported ? 4 : 2,
            BackColor = BackColor,
            Margin = Padding.Empty
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        nameLabel = new Label
        {
            Text = name, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Yu Gothic UI", 12f, FontStyle.Bold),
            ForeColor = Ink
        };
        header.Controls.Add(nameLabel, 0, 0);
        header.Controls.Add(Selected, 1, 0);
        descriptionLabel = new Label
        {
            Text = description, Dock = DockStyle.Fill,
            ForeColor = Muted, AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Yu Gothic UI", 9f)
        };
        content.Controls.Add(header, 0, 0);
        content.Controls.Add(descriptionLabel, 0, 1);

        if (supported)
        {
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            content.Controls.Add(Current, 0, 2);
            content.Controls.Add(Editor, 0, 3);
        }
        else
        {
            Selected.Checked = false;
            Selected.Enabled = false;
            Selected.Text = "準備中";
            Selected.ForeColor = Muted;
        }
        Controls.Add(content);

        // Windows Forms classic borders are too visually prominent; draw a thin neutral edge.
        Paint += (_, e) =>
        {
            using var pen = new Pen(Edge);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        };
    }

    internal void SetCardWidth(int availableWidth)
    {
        Width = Math.Clamp(availableWidth, 320, 900);
    }
}
