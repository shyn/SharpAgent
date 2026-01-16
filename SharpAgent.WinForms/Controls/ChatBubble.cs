using System.Drawing.Drawing2D;

namespace SharpAgent.WinForms.Controls;

public enum BubbleType
{
    User,
    Assistant,
    System
}

public class ChatBubble : Control
{
    private readonly BubbleType _type;
    private string _displayText = "";
    private readonly Label _roleLabel;
    private readonly Label _contentLabel;
    private readonly int _maxWidth;

    private static readonly Color UserBubbleColor = Color.FromArgb(0, 122, 255);
    private static readonly Color AssistantBubbleColor = Color.FromArgb(55, 55, 60);
    private static readonly Color SystemBubbleColor = Color.FromArgb(40, 40, 45);

    public ChatBubble(BubbleType type, string role, string content, int maxWidth = 600)
    {
        _type = type;
        _displayText = content;
        _maxWidth = maxWidth;

        DoubleBuffered = true;
        BackColor = Color.Transparent;
        Padding = new Padding(12, 8, 12, 8);

        _roleLabel = new Label
        {
            Text = role,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = type == BubbleType.User ? Color.FromArgb(180, 210, 255) : Color.FromArgb(150, 150, 155),
            AutoSize = true,
            BackColor = Color.Transparent,
            Location = new Point(16, 10)
        };

        _contentLabel = new Label
        {
            Text = content,
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.White,
            AutoSize = false,
            BackColor = Color.Transparent,
            MaximumSize = new Size(maxWidth - 32, 0),
            AutoEllipsis = false
        };

        Controls.Add(_roleLabel);
        Controls.Add(_contentLabel);

        UpdateLayout();
    }

    public void AppendText(string text)
    {
        _displayText += text;
        _contentLabel.Text = _displayText;
        UpdateLayout();
    }

    public void SetText(string text)
    {
        _displayText = text;
        _contentLabel.Text = text;
        UpdateLayout();
    }

    private void UpdateLayout()
    {
        using var g = CreateGraphics();
        var contentSize = TextRenderer.MeasureText(g, _displayText, _contentLabel.Font,
            new Size(_maxWidth - 32, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);

        _contentLabel.Size = new Size(Math.Min(contentSize.Width + 4, _maxWidth - 32), contentSize.Height + 4);
        _contentLabel.Location = new Point(16, _roleLabel.Bottom + 4);

        var bubbleWidth = Math.Max(_roleLabel.Width, _contentLabel.Width) + 32;
        var bubbleHeight = _contentLabel.Bottom + 12;

        Size = new Size(bubbleWidth, bubbleHeight);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var bubbleColor = _type switch
        {
            BubbleType.User => UserBubbleColor,
            BubbleType.Assistant => AssistantBubbleColor,
            _ => SystemBubbleColor
        };

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = CreateRoundedRectangle(rect, 12);
        using var brush = new SolidBrush(bubbleColor);
        e.Graphics.FillPath(brush, path);
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
