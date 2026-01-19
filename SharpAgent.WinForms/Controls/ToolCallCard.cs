using System.Drawing.Drawing2D;

namespace SharpAgent.WinForms.Controls;

public class ToolCallCard : Control
{
    private readonly string _toolName;
    private readonly string _arguments;
    private string? _result;
    private bool? _isError;
    private bool _isExpanded = false;

    private readonly Label _headerLabel;
    private readonly Label _statusLabel;
    private readonly Panel _contentPanel;
    private readonly Label _argsLabel;
    private readonly Label _resultTitleLabel;
    private readonly Label _resultLabel;
    private readonly Button _toggleButton;
    private readonly int _maxWidth;

    private static readonly Color CardBackgroundStart = Theme.BackgroundTertiary;
    private static readonly Color CardBackgroundEnd = Theme.BackgroundSecondary;
    private static readonly Color CardBorder = Theme.BorderSubtle;
    private static readonly Color HeaderColor = Color.FromArgb(200, 120, 0); // Darker Orange
    private static readonly Color RunningColor = Theme.FocusRing;
    private static readonly Color SuccessColor = Color.FromArgb(0, 150, 60); // Darker Green
    private static readonly Color ErrorColor = Theme.Error;
    private static readonly Color CodeBackground = Theme.Background;
    private static readonly Color CodeBorder = Theme.BorderSubtle;

    public ToolCallCard(string toolName, string arguments, int maxWidth = 600)
    {
        _toolName = toolName;
        _arguments = arguments;
        _maxWidth = maxWidth;

        SetStyle(ControlStyles.SupportsTransparentBackColor | 
                 ControlStyles.OptimizedDoubleBuffer | 
                 ControlStyles.AllPaintingInWmPaint, true);
        DoubleBuffered = true;
        BackColor = Color.Transparent;

        _toggleButton = new Button
        {
            Text = "▶",
            Size = new Size(28, 28),
            FlatStyle = FlatStyle.Flat,
            Font = Theme.FontSmall,
            ForeColor = Theme.TextMuted,
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };
        _toggleButton.FlatAppearance.BorderSize = 0;
        _toggleButton.FlatAppearance.MouseOverBackColor = Theme.ButtonHover;
        _toggleButton.FlatAppearance.MouseDownBackColor = Theme.ButtonPressed;
        _toggleButton.Click += (_, _) => ToggleExpand();

        _headerLabel = new Label
        {
            Text = $"🔧 {toolName}",
            Font = Theme.FontMedium,
            ForeColor = HeaderColor,
            AutoSize = true,
            BackColor = Color.Transparent,
            Location = new Point(14, Theme.GutterSmall),
            Cursor = Cursors.Hand
        };
        _headerLabel.Click += (_, _) => ToggleExpand();

        _statusLabel = new Label
        {
            Text = "Running...",
            Font = Theme.FontSmall,
            ForeColor = RunningColor,
            AutoSize = true,
            BackColor = Color.Transparent
        };

        _contentPanel = new Panel
        {
            BackColor = Color.Transparent,
            Location = new Point(14, 44),
            AutoSize = false,
            Visible = false
        };

        _argsLabel = new Label
        {
            Text = FormatJson(arguments),
            Font = Theme.FontMono,
            ForeColor = Theme.TextSecondary,
            AutoSize = false, // Manual size for better control
            BackColor = CodeBackground,
            Padding = new Padding(10, Theme.SpacingSmall, 10, Theme.SpacingSmall)
        };

        _resultTitleLabel = new Label
        {
            Text = "Result:",
            Font = Theme.FontSmall,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            BackColor = Color.Transparent,
            Visible = false
        };

        _resultLabel = new Label
        {
            Font = Theme.FontMono,
            ForeColor = Theme.TextSecondary,
            AutoSize = false, // Manual size for better control
            BackColor = CodeBackground,
            Padding = new Padding(10, Theme.SpacingSmall, 10, Theme.SpacingSmall),
            Visible = false
        };

        _contentPanel.Controls.Add(_argsLabel);
        _contentPanel.Controls.Add(_resultTitleLabel);
        _contentPanel.Controls.Add(_resultLabel);

        Controls.Add(_toggleButton);
        Controls.Add(_headerLabel);
        Controls.Add(_statusLabel);
        Controls.Add(_contentPanel);

        UpdateLayout();
    }

    public void SetResult(string result, bool isError)
    {
        _result = result;
        _isError = isError;

        _statusLabel.Text = isError ? "Error" : "Completed";
        _statusLabel.ForeColor = isError ? ErrorColor : SuccessColor;

        _resultTitleLabel.Visible = true;
        _resultTitleLabel.ForeColor = isError ? ErrorColor : SuccessColor;
        _resultTitleLabel.Text = isError ? "Error:" : "Result:";

        _resultLabel.Visible = true;
        _resultLabel.Text = TruncateResult(result, 1000);
        _resultLabel.ForeColor = isError ? Color.FromArgb(255, 190, 190) : Color.FromArgb(190, 210, 190);

        UpdateLayout();
    }

    private void ToggleExpand()
    {
        _isExpanded = !_isExpanded;
        _toggleButton.Text = _isExpanded ? "▼" : "▶";
        _contentPanel.Visible = _isExpanded;
        UpdateLayout();
    }

    private void UpdateLayout()
    {
        _statusLabel.Location = new Point(_headerLabel.Right + 14, _headerLabel.Top + 2);
        _toggleButton.Location = new Point(_maxWidth - 46, 10);

        if (!_isExpanded)
        {
            Size = new Size(_maxWidth - 16, 48);
            Invalidate();
            return;
        }

        var availableWidth = _maxWidth - 60;
        
        // Measure args text
        Size argsSize;
        using (var g = CreateGraphics())
        {
            argsSize = TextRenderer.MeasureText(g, _argsLabel.Text, _argsLabel.Font,
                new Size(availableWidth, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        }
        
        var argsHeight = Math.Min(argsSize.Height + 20, 300); // Max 300px for args
        _argsLabel.Size = new Size(availableWidth, argsHeight);
        _argsLabel.Location = new Point(0, 0);

        var contentHeight = argsHeight;

        if (_resultLabel.Visible)
        {
            _resultTitleLabel.Location = new Point(0, contentHeight + 14);

            // Measure result text
            Size resultSize;
            using (var g = CreateGraphics())
            {
                resultSize = TextRenderer.MeasureText(g, _resultLabel.Text, _resultLabel.Font,
                    new Size(availableWidth, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            }
            
            var resultHeight = Math.Min(resultSize.Height + 20, 200); // Max 200px for results
            _resultLabel.Size = new Size(availableWidth, resultHeight);
            _resultLabel.Location = new Point(0, _resultTitleLabel.Bottom + 6);
            
            contentHeight = _resultLabel.Bottom;
        }

        _contentPanel.Size = new Size(_maxWidth - 28, contentHeight + 8);
        Size = new Size(_maxWidth - 16, 44 + contentHeight + 24);
        
        // Trigger parent layout update
        Parent?.PerformLayout();
        
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = CreateRoundedRectangle(rect, Theme.CornerRadius);
        using var gradientBrush = new LinearGradientBrush(
            rect, CardBackgroundStart, CardBackgroundEnd, LinearGradientMode.Vertical);
        using var borderPen = new Pen(CardBorder, 1);

        e.Graphics.FillPath(gradientBrush, path);
        e.Graphics.DrawPath(borderPen, path);
        
        using var highlightPen = new Pen(Color.FromArgb(30, 255, 255, 255), 1);
        e.Graphics.DrawLine(highlightPen, Theme.CornerRadius, 1, Width - Theme.CornerRadius - 1, 1);
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

    private static string FormatJson(string json)
    {
        try
        {
            var obj = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
            return System.Text.Json.JsonSerializer.Serialize(obj, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch
        {
            return json;
        }
    }

    private static string TruncateResult(string result, int maxLength)
    {
        if (result.Length <= maxLength) return result;
        return result[..maxLength] + "\n... (truncated)";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _headerLabel.Dispose();
            _statusLabel.Dispose();
            _argsLabel.Dispose();
            _resultTitleLabel.Dispose();
            _resultLabel.Dispose();
            _toggleButton.Dispose();
            _contentPanel.Dispose();
        }
        base.Dispose(disposing);
    }
}
