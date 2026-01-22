using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;

namespace SharpAgent.WinForms.Controls;

/// <summary>
/// A textbox with rounded corners and focus border effects
/// </summary>
public class RoundedTextBox : Control
{
    private readonly TextBox _innerTextBox = null!;
    
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public Color BorderColor { get; set; } = Theme.BorderSubtle;
    
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public Color FocusBorderColor { get; set; } = Theme.FocusRing;
    
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public int CornerRadius { get; set; } = Theme.CornerRadius;
    
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public string PlaceholderText { get; set; } = "";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public bool Multiline
    {
        get => _innerTextBox?.Multiline ?? false;
        set { if (_innerTextBox != null) { _innerTextBox.Multiline = value; UpdateInnerTextBoxLayout(); } }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public bool AcceptsReturn
    {
        get => _innerTextBox?.AcceptsReturn ?? false;
        set { if (_innerTextBox != null) _innerTextBox.AcceptsReturn = value; }
    }

    private bool _isFocused;

    public RoundedTextBox()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | 
                 ControlStyles.AllPaintingInWmPaint | 
                 ControlStyles.UserPaint, true);
        DoubleBuffered = true;

        _innerTextBox = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = BackColor,
            ForeColor = ForeColor,
            Font = Font,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom
        };

        _innerTextBox.GotFocus += (s, e) => { _isFocused = true; Invalidate(); };
        _innerTextBox.LostFocus += (s, e) => { _isFocused = false; Invalidate(); };
        _innerTextBox.TextChanged += (s, e) => { OnTextChanged(e); Invalidate(); };

        Controls.Add(_innerTextBox);
        
        Height = 48;
        UpdateInnerTextBoxLayout();
    }

    [AllowNull]
    public override string Text
    {
        get => _innerTextBox?.Text ?? string.Empty;
        set { if (_innerTextBox != null) _innerTextBox.Text = value ?? string.Empty; }
    }

    [AllowNull]
    public override Font Font
    {
        get => base.Font;
        set
        {
            base.Font = value!;
            if (_innerTextBox != null)
            {
                _innerTextBox.Font = value!;
                UpdateInnerTextBoxLayout();
            }
        }
    }

    public override Color BackColor
    {
        get => base.BackColor;
        set
        {
            base.BackColor = value;
            if (_innerTextBox != null) _innerTextBox.BackColor = value;
        }
    }

    public override Color ForeColor
    {
        get => base.ForeColor;
        set
        {
            base.ForeColor = value;
            if (_innerTextBox != null) _innerTextBox.ForeColor = value;
        }
    }

    public void Clear() => _innerTextBox?.Clear();

    public new event KeyEventHandler? KeyDown
    {
        add => _innerTextBox.KeyDown += value;
        remove => _innerTextBox.KeyDown -= value;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateInnerTextBoxLayout();
    }

    private void UpdateInnerTextBoxLayout()
    {
        if (_innerTextBox == null) return;
        
        var padding = Theme.SpacingSmall;
        if (_innerTextBox.Multiline)
        {
            _innerTextBox.Location = new Point(CornerRadius + padding, padding);
            _innerTextBox.Size = new Size(
                Math.Max(1, Width - (CornerRadius * 2) - (padding * 2)),
                Math.Max(1, Height - (padding * 2)));
        }
        else
        {
            var verticalPadding = (Height - _innerTextBox.PreferredHeight) / 2;
            _innerTextBox.Location = new Point(CornerRadius + padding, Math.Max(1, verticalPadding));
            _innerTextBox.Width = Math.Max(1, Width - (CornerRadius * 2) - (padding * 2));
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = CreateRoundedRectangle(rect, CornerRadius);
        using var brush = new SolidBrush(BackColor);
        
        e.Graphics.FillPath(brush, path);

        using var borderPen = new Pen(_isFocused ? FocusBorderColor : BorderColor, _isFocused ? 2f : 1f);
        e.Graphics.DrawPath(borderPen, path);

        if (string.IsNullOrEmpty(_innerTextBox.Text) && !_isFocused && !string.IsNullOrEmpty(PlaceholderText))
        {
            var placeholderRect = new Rectangle(CornerRadius + Theme.SpacingSmall, 0, Width - CornerRadius * 2 - Theme.Gutter, Height);
            TextRenderer.DrawText(e.Graphics, PlaceholderText, Font, placeholderRect,
                Theme.TextMuted, _innerTextBox.Multiline ? TextFormatFlags.Default : TextFormatFlags.VerticalCenter);
        }
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
