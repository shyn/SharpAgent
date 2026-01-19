using System.Drawing.Drawing2D;
using System.ComponentModel;
using SharpAgent.Core.Configuration;

namespace SharpAgent.WinForms.Controls;

public class ModernInputArea : Control
{
    private readonly TextBox _textBox;
    private readonly ModernButton _btnAttach;
    private readonly ModernButton _btnImage;
    private readonly ModernButton _btnThinking;
    private readonly ModernButton _btnSend;
    private readonly ToolTip _toolTip;
    
    private ThinkingLevel _currentLevel = ThinkingLevel.Off;
    private bool _isFocused;
    
    // Container bounds for painting and layout
    private Rectangle _containerRect;
    private const int ContainerPadding = 12;
    private const int InnerPadding = 10;
    
    public event EventHandler? SendClicked;
    public event EventHandler<ThinkingLevel>? ThinkingLevelChanged;
    public event EventHandler? FileAttachClicked;
    public event EventHandler? ImageAttachClicked;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string TextContent
    {
        get => _textBox.Text;
        set => _textBox.Text = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public string PlaceholderText { get; set; } = "Type a message...";

    public ModernInputArea()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | 
                 ControlStyles.AllPaintingInWmPaint | 
                 ControlStyles.UserPaint |
                 ControlStyles.SupportsTransparentBackColor, true);
        DoubleBuffered = true;
        
        BackColor = Color.Transparent;
        Size = new Size(800, 110);

        _toolTip = new ToolTip
        {
            AutoPopDelay = 3000,
            InitialDelay = 500,
            ReshowDelay = 200
        };

        // Use a borderless TextBox that we control
        _textBox = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Font = Theme.FontMedium,
            BackColor = Theme.BackgroundSecondary,
            ForeColor = Theme.TextPrimary,
            Multiline = true,
            AcceptsReturn = true
        };
        _textBox.KeyDown += (s, e) => {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                SendClicked?.Invoke(this, EventArgs.Empty);
            }
        };
        _textBox.GotFocus += (s, e) => { _isFocused = true; Invalidate(); };
        _textBox.LostFocus += (s, e) => { _isFocused = false; Invalidate(); };
        _textBox.TextChanged += (s, e) => Invalidate();

        _btnAttach = CreateIconButton("➕", "Attach File");
        _btnAttach.Click += (s, e) => FileAttachClicked?.Invoke(this, EventArgs.Empty);

        _btnImage = CreateIconButton("🖼️", "Attach Image");
        _btnImage.Click += (s, e) => ImageAttachClicked?.Invoke(this, EventArgs.Empty);

        _btnThinking = CreateThinkingToggle();
        _btnThinking.Click += (s, e) => CycleThinkingLevel();

        _btnSend = CreateSendButton();
        _btnSend.Click += (s, e) => SendClicked?.Invoke(this, EventArgs.Empty);

        Controls.Add(_textBox);
        Controls.Add(_btnAttach);
        Controls.Add(_btnImage);
        Controls.Add(_btnThinking);
        Controls.Add(_btnSend);

        UpdateLayout();
    }

    private ModernButton CreateIconButton(string icon, string tooltip)
    {
        var btn = new ModernButton
        {
            Text = icon,
            Size = new Size(32, 32),
            BackgroundColor = Theme.ButtonDefault,
            HoverColor = Theme.ButtonHover,
            PressedColor = Theme.ButtonPressed,
            CornerRadius = Theme.CornerRadiusSmall,
            Font = new Font("Segoe UI Emoji", 10),
            ForeColor = Theme.TextPrimary
        };
        _toolTip.SetToolTip(btn, tooltip);
        return btn;
    }

    private ModernButton CreateThinkingToggle()
    {
        var btn = new ModernButton
        {
            Text = "THINK: OFF",
            Size = new Size(90, 32),
            BackgroundColor = Theme.ButtonDefault,
            HoverColor = Theme.ButtonHover,
            PressedColor = Theme.ButtonPressed,
            CornerRadius = Theme.CornerRadiusSmall,
            Font = new Font("Segoe UI Semibold", 8),
            ForeColor = Theme.TextMuted
        };
        _toolTip.SetToolTip(btn, "Toggle thinking level (Off → Low → Medium → High)");
        return btn;
    }

    private ModernButton CreateSendButton()
    {
        var btn = new ModernButton
        {
            Text = "▲",
            Size = new Size(32, 32),
            BackgroundColor = Theme.AccentPrimary,
            HoverColor = Theme.AccentPrimaryHover,
            PressedColor = Theme.AccentPrimaryPressed,
            CornerRadius = 16, // Circle
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.White
        };
        _toolTip.SetToolTip(btn, "Send message (Enter)");
        return btn;
    }

    private void CycleThinkingLevel()
    {
        _currentLevel = _currentLevel switch
        {
            ThinkingLevel.Off => ThinkingLevel.Low,
            ThinkingLevel.Low => ThinkingLevel.Middle,
            ThinkingLevel.Middle => ThinkingLevel.High,
            ThinkingLevel.High => ThinkingLevel.Off,
            _ => ThinkingLevel.Off
        };

        _btnThinking.Text = $"THINK: {_currentLevel.ToString().ToUpper()}";
        _btnThinking.ForeColor = _currentLevel switch
        {
            ThinkingLevel.Off => Theme.TextMuted,
            ThinkingLevel.High => Color.DarkOrange,
            _ => Color.MediumPurple
        };
        
        ThinkingLevelChanged?.Invoke(this, _currentLevel);
    }
    public void SetRunningState(bool running)
    {
        _btnThinking.Enabled = !running;
        _btnAttach.Enabled = !running;
        _btnImage.Enabled = !running;
        _btnSend.Enabled = !running;
        _btnSend.Visible = running;
    }
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateLayout();
    }

    private void UpdateLayout()
    {
        if (_textBox == null) return;

        // Calculate container bounds (the visual rounded rectangle)
        _containerRect = new Rectangle(
            ContainerPadding, 
            ContainerPadding, 
            Width - (ContainerPadding * 2), 
            Height - (ContainerPadding * 2)
        );

        var buttonSize = 32;
        var spacing = 8;
        
        // TextBox: takes top portion of the container
        var textBoxHeight = _containerRect.Height - buttonSize - InnerPadding - (InnerPadding * 2);
        _textBox.Location = new Point(
            _containerRect.X + InnerPadding + 4,
            _containerRect.Y + InnerPadding
        );
        _textBox.Size = new Size(
            _containerRect.Width - (InnerPadding * 2) - 8,
            Math.Max(24, textBoxHeight)
        );
        
        // Buttons: bottom row inside the container
        var buttonY = _containerRect.Bottom - InnerPadding - buttonSize;
        var x = _containerRect.X + InnerPadding;
        
        _btnAttach.Location = new Point(x, buttonY);
        x += buttonSize + spacing;
        
        _btnImage.Location = new Point(x, buttonY);
        x += buttonSize + spacing;
        
        _btnThinking.Location = new Point(x, buttonY);
        
        // Send button on the right
        _btnSend.Location = new Point(
            _containerRect.Right - InnerPadding - buttonSize,
            buttonY
        );
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        
        // Draw the container background
        using var path = CreateRoundedRectangle(_containerRect, Theme.CornerRadiusLarge);
        using var brush = new SolidBrush(Theme.BackgroundSecondary);
        e.Graphics.FillPath(brush, path);
        
        // Draw border (thicker when focused)
        var borderColor = _isFocused ? Theme.FocusRing : Theme.BorderSubtle;
        var borderWidth = _isFocused ? 2f : 1f;
        using var borderPen = new Pen(borderColor, borderWidth);
        e.Graphics.DrawPath(borderPen, path);
        
        // Draw placeholder text if textbox is empty and not focused
        if (string.IsNullOrEmpty(_textBox.Text) && !_isFocused && !string.IsNullOrEmpty(PlaceholderText))
        {
            var placeholderRect = new Rectangle(
                _textBox.Left + 2,
                _textBox.Top,
                _textBox.Width,
                _textBox.Height
            );
            using var placeholderBrush = new SolidBrush(Theme.TextMuted);
            e.Graphics.DrawString(PlaceholderText, Theme.FontMedium, placeholderBrush, placeholderRect);
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

    public void Clear() => _textBox.Clear();
}
