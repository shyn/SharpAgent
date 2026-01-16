using System.Drawing.Drawing2D;

namespace SharpAgent.WinForms.Controls;

public class ToolCallCard : Control
{
    private readonly string _toolName;
    private readonly string _arguments;
    private string? _result;
    private bool? _isError;
    private bool _isExpanded = true;

    private readonly Label _headerLabel;
    private readonly Label _statusLabel;
    private readonly Panel _contentPanel;
    private readonly Label _argsLabel;
    private readonly Label _resultTitleLabel;
    private readonly Label _resultLabel;
    private readonly Button _toggleButton;
    private readonly int _maxWidth;

    private static readonly Color CardBackground = Color.FromArgb(45, 45, 50);
    private static readonly Color HeaderColor = Color.FromArgb(255, 180, 70);
    private static readonly Color RunningColor = Color.FromArgb(100, 180, 255);
    private static readonly Color SuccessColor = Color.FromArgb(80, 200, 120);
    private static readonly Color ErrorColor = Color.FromArgb(255, 100, 100);

    public ToolCallCard(string toolName, string arguments, int maxWidth = 600)
    {
        _toolName = toolName;
        _arguments = arguments;
        _maxWidth = maxWidth;

        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        DoubleBuffered = true;
        BackColor = Color.Transparent;

        _toggleButton = new Button
        {
            Text = "▼",
            Size = new Size(24, 24),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8),
            ForeColor = Color.Gray,
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };
        _toggleButton.FlatAppearance.BorderSize = 0;
        _toggleButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 60, 65);
        _toggleButton.Click += (_, _) => ToggleExpand();

        _headerLabel = new Label
        {
            Text = $"🔧 {toolName}",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = HeaderColor,
            AutoSize = true,
            BackColor = Color.Transparent,
            Location = new Point(12, 10)
        };

        _statusLabel = new Label
        {
            Text = "Running...",
            Font = new Font("Segoe UI", 9),
            ForeColor = RunningColor,
            AutoSize = true,
            BackColor = Color.Transparent
        };

        _contentPanel = new Panel
        {
            BackColor = Color.Transparent,
            Location = new Point(12, 38),
            AutoSize = false
        };

        _argsLabel = new Label
        {
            Text = FormatJson(arguments),
            Font = new Font("Consolas", 9),
            ForeColor = Color.FromArgb(180, 180, 185),
            AutoSize = false,
            BackColor = Color.FromArgb(35, 35, 40),
            Padding = new Padding(8, 6, 8, 6),
            MaximumSize = new Size(maxWidth - 40, 0)
        };

        _resultTitleLabel = new Label
        {
            Text = "Result:",
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(150, 150, 155),
            AutoSize = true,
            BackColor = Color.Transparent,
            Visible = false
        };

        _resultLabel = new Label
        {
            Font = new Font("Consolas", 9),
            ForeColor = Color.FromArgb(180, 180, 185),
            AutoSize = false,
            BackColor = Color.FromArgb(35, 35, 40),
            Padding = new Padding(8, 6, 8, 6),
            MaximumSize = new Size(maxWidth - 40, 0),
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
        _resultLabel.ForeColor = isError ? Color.FromArgb(255, 180, 180) : Color.FromArgb(180, 200, 180);

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
        _statusLabel.Location = new Point(_headerLabel.Right + 12, _headerLabel.Top + 2);
        _toggleButton.Location = new Point(_maxWidth - 40, 8);

        if (!_isExpanded)
        {
            Size = new Size(_maxWidth - 16, 40);
            Invalidate();
            return;
        }

        using var g = CreateGraphics();

        var argsSize = TextRenderer.MeasureText(g, _argsLabel.Text, _argsLabel.Font,
            new Size(_maxWidth - 56, int.MaxValue), TextFormatFlags.WordBreak);
        _argsLabel.Size = new Size(Math.Min(argsSize.Width + 20, _maxWidth - 40), argsSize.Height + 16);
        _argsLabel.Location = new Point(0, 0);

        var contentHeight = _argsLabel.Bottom;

        if (_resultLabel.Visible)
        {
            _resultTitleLabel.Location = new Point(0, _argsLabel.Bottom + 10);

            var resultSize = TextRenderer.MeasureText(g, _resultLabel.Text, _resultLabel.Font,
                new Size(_maxWidth - 56, int.MaxValue), TextFormatFlags.WordBreak);
            _resultLabel.Size = new Size(Math.Min(resultSize.Width + 20, _maxWidth - 40), Math.Min(resultSize.Height + 16, 200));
            _resultLabel.Location = new Point(0, _resultTitleLabel.Bottom + 4);

            contentHeight = _resultLabel.Bottom;
        }

        _contentPanel.Size = new Size(_maxWidth - 24, contentHeight);

        Size = new Size(_maxWidth - 16, _contentPanel.Bottom + 12);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = CreateRoundedRectangle(rect, 8);
        using var brush = new SolidBrush(CardBackground);
        using var pen = new Pen(Color.FromArgb(70, 70, 75), 1);

        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(pen, path);
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
}
