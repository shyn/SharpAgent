using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace SharpAgent.WinForms.Controls;

/// <summary>
/// Modern button with hover effects and rounded corners
/// </summary>
public class ModernButton : Control
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public Color BackgroundColor { get; set; } = Theme.AccentPrimary;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public Color HoverColor { get; set; } = Theme.AccentPrimaryHover;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public Color PressedColor { get; set; } = Theme.AccentPrimaryPressed;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public int CornerRadius { get; set; } = Theme.CornerRadiusSmall;

    private bool _isHovering;
    private bool _isPressed;

    public ModernButton()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.Selectable, true);
        DoubleBuffered = true;
        TabStop = true;
        Cursor = Cursors.Hand;
        Size = Theme.ButtonSize;
    }

    protected override bool IsInputKey(Keys keyData)
    {
        return keyData is Keys.Space or Keys.Enter || base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Enabled && e.KeyCode is Keys.Space or Keys.Enter)
        {
            _isPressed = true;
            Invalidate();
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (Enabled && e.KeyCode is Keys.Space or Keys.Enter)
        {
            _isPressed = false;
            Invalidate();
            OnClick(EventArgs.Empty);
        }
        base.OnKeyUp(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var color = _isPressed ? PressedColor : (_isHovering ? HoverColor : BackgroundColor);
        if (!Enabled) color = Theme.BackgroundTertiary;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = CreateRoundedRectangle(rect, CornerRadius);
        using var brush = new SolidBrush(color);

        e.Graphics.FillPath(brush, path);

        // Draw focus ring when focused
        if (Focused && Enabled)
        {
            var focusRect = new Rectangle(1, 1, Width - 3, Height - 3);
            using var focusPath = CreateRoundedRectangle(focusRect, CornerRadius - 1);
            using var focusPen = new Pen(Theme.FocusRing, 2);
            e.Graphics.DrawPath(focusPen, focusPath);
        }

        // Draw text
        var textColor = Enabled ? ForeColor : Theme.TextDisabled;
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _isHovering = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _isHovering = false;
        _isPressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (Enabled)
        {
            _isPressed = true;
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _isPressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
