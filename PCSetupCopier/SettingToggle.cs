using System.Drawing.Drawing2D;

namespace PCSetupCopier;

// Accessible, keyboard-operable iOS-style toggle. Changes only pending UI state.
internal sealed class SettingToggle : Control
{
    private bool value;
    internal bool IsOn
    {
        get => value;
        set
        {
            if (this.value == value) return;
            this.value = value;
            Invalidate();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
    internal event EventHandler? Changed;

    internal SettingToggle()
    {
        Size = new Size(58, 34);
        MinimumSize = Size;
        MaximumSize = Size;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.CheckButton;
        AccessibleName = "マウス加速";
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (Enabled) IsOn = !IsOn;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            IsOn = !IsOn;
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new Rectangle(1, 1, Width - 2, Height - 2);
        using var path = Rounded(track, track.Height / 2);
        using var brush = new SolidBrush(!Enabled ? Color.FromArgb(204, 209, 217)
            : IsOn ? Color.FromArgb(52, 199, 89) : Color.FromArgb(170, 178, 189));
        g.FillPath(brush, path);

        int diameter = Height - 8;
        int x = IsOn ? Width - diameter - 4 : 4;
        using var shadow = new SolidBrush(Color.FromArgb(80, 100, 115));
        g.FillEllipse(shadow, x, 5, diameter, diameter);
        using var knob = new SolidBrush(Color.White);
        g.FillEllipse(knob, x, 4, diameter, diameter);
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(g, new Rectangle(0, 0, Width - 1, Height - 1));
    }

    private static GraphicsPath Rounded(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 90, 180);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 180);
        path.CloseFigure();
        return path;
    }
}
