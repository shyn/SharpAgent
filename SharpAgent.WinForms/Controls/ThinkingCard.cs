using System.Drawing.Drawing2D;

namespace SharpAgent.WinForms.Controls;

/// <summary>
/// A collapsible card that displays the model's thinking/reasoning process
/// </summary>
public class ThinkingCard : Control
{
    private readonly Panel _headerPanel;
    private readonly Label _headerLabel;
    private readonly Label _contentLabel;
    private readonly Panel _contentPanel;
    private readonly int _maxWidth;
    private bool _isExpanded = false;
    private bool _isHeaderHovered = false;
    private string _thinkingContent = "";

    private static readonly Color CardBackgroundStart = Theme.BackgroundTertiary;
    private static readonly Color CardBackgroundEnd = Theme.BackgroundSecondary;
    private static readonly Color BorderColor = Theme.BorderSubtle;
    private static readonly Color HeaderColor = Theme.TextSecondary;
    private static readonly Color HeaderHoverColor = Theme.ButtonHover;

    public ThinkingCard(int maxWidth = 600)
    {
        _maxWidth = maxWidth;

        SetStyle(ControlStyles.SupportsTransparentBackColor |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint, true);
        DoubleBuffered = true;
        BackColor = Color.Transparent;
        Padding = new Padding(0);
        Margin = new Padding(0);

        _headerPanel = new Panel
        {
            BackColor = Color.Transparent,
            Location = new Point(Theme.SpacingXs, Theme.SpacingXs),
            Size = new Size(_maxWidth - Theme.SpacingSmall, 36),
            Cursor = Cursors.Hand
        };
        _headerPanel.Click += (s, e) => ToggleExpanded();
        _headerPanel.MouseEnter += (s, e) => { _isHeaderHovered = true; _headerPanel.Invalidate(); };
        _headerPanel.MouseLeave += (s, e) => { _isHeaderHovered = false; _headerPanel.Invalidate(); };
        _headerPanel.Paint += (s, e) =>
        {
            if (_isHeaderHovered)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = new Rectangle(0, 0, _headerPanel.Width - 1, _headerPanel.Height - 1);
                using var path = CreateRoundedRectangle(rect, Theme.CornerRadiusSmall);
                using var brush = new SolidBrush(HeaderHoverColor);
                e.Graphics.FillPath(brush, path);
            }
        };

        _headerLabel = new Label
        {
            Text = "💭 Thinking...",
            Font = Theme.FontRegular,
            ForeColor = HeaderColor,
            AutoSize = false,
            Cursor = Cursors.Hand,
            BackColor = Color.Transparent,
            Padding = new Padding(Theme.GutterSmall, 10, Theme.GutterSmall, 10),
            Location = new Point(0, 0),
            Size = new Size(_maxWidth - Theme.SpacingSmall, 36)
        };
        _headerLabel.Click += (s, e) => ToggleExpanded();
        _headerLabel.MouseEnter += (s, e) => { _isHeaderHovered = true; _headerPanel.Invalidate(); };
        _headerLabel.MouseLeave += (s, e) => { _isHeaderHovered = false; _headerPanel.Invalidate(); };
        _headerPanel.Controls.Add(_headerLabel);


        _contentPanel = new Panel
        {
            BackColor = Color.Transparent,
            Location = new Point(Theme.SpacingXs, 40),
            Size = new Size(_maxWidth - Theme.SpacingSmall, 0),
            AutoScroll = true,
            Visible = false
        };

        _contentLabel = new Label
        {
            Font = Theme.FontSmall,
            ForeColor = Theme.TextSecondary,
            AutoSize = false, // We'll manage size manually for better control
            BackColor = Color.Transparent,
            Location = new Point(Theme.GutterSmall, Theme.SpacingSmall),
            Text = ""
        };
        _contentPanel.Controls.Add(_contentLabel);

        Controls.Add(_headerPanel);
        Controls.Add(_contentPanel);

        UpdateSize();
    }

    public void AppendThinking(string text)
    {
        _thinkingContent += text;
        _contentLabel.Text = _thinkingContent;
        
        if (_isExpanded)
        {
            UpdateContentPanelSize();
            UpdateSize();
        }
        
        // Update header with character count
        var charCount = _thinkingContent.Length;
        var displayCount = charCount > 1000 ? $"{charCount / 1000.0:F1}k" : charCount.ToString();
        _headerLabel.Text = _isExpanded ? $"💭 Thinking ({displayCount} chars) ▼" : $"💭 Thinking ({displayCount} chars) ▶";
    }

    public void CompleteThinking()
    {
        var charCount = _thinkingContent.Length;
        var displayCount = charCount > 1000 ? $"{charCount / 1000.0:F1}k" : charCount.ToString();
        _headerLabel.Text = _isExpanded ? $"💭 Thought ({displayCount} chars) ▼" : $"💭 Thought ({displayCount} chars) ▶";
    }

    private void ToggleExpanded()
    {
        _isExpanded = !_isExpanded;
        _contentPanel.Visible = _isExpanded;
        
        var charCount = _thinkingContent.Length;
        var displayCount = charCount > 1000 ? $"{charCount / 1000.0:F1}k" : charCount.ToString();
        var verb = string.IsNullOrEmpty(_thinkingContent) ? "Thinking" : "Thought";
        _headerLabel.Text = _isExpanded ? $"💭 {verb} ({displayCount} chars) ▼" : $"💭 {verb} ({displayCount} chars) ▶";

        if (_isExpanded)
        {
            UpdateContentPanelSize();
        }
        else
        {
            _contentPanel.Height = 0;
        }

        UpdateSize();
    }

    private void UpdateContentPanelSize()
    {
        // Measure the text to determine required height
        var availableWidth = _maxWidth - 60;
        Size textSize;
        using (var g = CreateGraphics())
        {
            textSize = TextRenderer.MeasureText(g, _thinkingContent, _contentLabel.Font,
                new Size(availableWidth, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        }
        
        // Set the label size
        _contentLabel.Size = new Size(availableWidth, textSize.Height + Theme.SpacingSmall);
        
        // Calculate panel height (max 300px, then scroll)
        var desiredHeight = textSize.Height + Theme.Gutter;
        var maxHeight = 300;
        
        _contentPanel.Size = new Size(_maxWidth - Theme.SpacingSmall, Math.Min(maxHeight, desiredHeight));
    }

    private void UpdateSize()
    {
        var headerHeight = 44;
        var contentHeight = _isExpanded ? _contentPanel.Height : 0;
        Size = new Size(_maxWidth, headerHeight + contentHeight + 8);
        
        // Trigger parent layout update
        Parent?.PerformLayout();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = CreateRoundedRectangle(rect, Theme.CornerRadius);
        using var gradientBrush = new LinearGradientBrush(rect, CardBackgroundStart, CardBackgroundEnd, LinearGradientMode.Vertical);
        using var borderPen = new Pen(BorderColor, 1);

        e.Graphics.FillPath(gradientBrush, path);
        e.Graphics.DrawPath(borderPen, path);

        base.OnPaint(e);
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (bounds.Width < 1 || bounds.Height < 1) return path;

        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));

        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();

        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _headerLabel.Dispose();
            _headerPanel.Dispose();
            _contentLabel.Dispose();
            _contentPanel.Dispose();
        }
        base.Dispose(disposing);
    }
}
