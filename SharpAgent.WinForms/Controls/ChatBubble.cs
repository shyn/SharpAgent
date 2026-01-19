using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;

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
    private readonly int _maxWidth;
    private readonly FlowLayoutPanel _contentPanel;

    public BubbleType Type => _type;
    
    private readonly List<Control> _blockControls = [];

    private static readonly Color UserBubbleStart = Theme.AccentPrimary;
    private static readonly Color UserBubbleEnd = Theme.AccentPrimaryHover;
    private static readonly Color AssistantBubbleStart = Theme.BackgroundTertiary;
    private static readonly Color AssistantBubbleEnd = Theme.BackgroundSecondary;
    private static readonly Color SystemBubbleColor = Theme.BackgroundTertiary;
    private static readonly Color CodeBlockColor = Theme.Background;
    private static readonly Color CodeBlockBorder = Theme.BorderSubtle;

    public ChatBubble(BubbleType type, string role, string content, int maxWidth = 600)
    {
        _type = type;
        _displayText = content;
        _maxWidth = maxWidth;

        SetStyle(ControlStyles.SupportsTransparentBackColor | 
                 ControlStyles.OptimizedDoubleBuffer | 
                 ControlStyles.AllPaintingInWmPaint, true);
        DoubleBuffered = true;
        AutoSize = false;
        
        BackColor = Color.Transparent;
        Padding = new Padding(0);
        Margin = new Padding(0);

        _contentPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.Transparent,
            MaximumSize = new Size(maxWidth, 0),
            Padding = new Padding(0),
            Margin = new Padding(0)
        };

        Controls.Add(_contentPanel);
        _contentPanel.Resize += (s, e) => UpdateOwnSize();

        if (!string.IsNullOrEmpty(content))
        {
            RenderContent();
        }
    }

    public void AppendText(string text)
    {
        _displayText += text;
        RenderContent();
    }

    private void UpdateOwnSize()
    {
        Size = new Size(_contentPanel.Width + 12, _contentPanel.Height + 12);
    }

    private void RenderContent()
    {
        var parts = Regex.Split(_displayText, @"(```[\s\S]*?```)");

        if (_blockControls.Count > 0 && parts.Length == _blockControls.Count)
        {
            UpdateControl(_blockControls.Last(), parts.Last());
            DoLayout();
            return;
        }
        
        _contentPanel.SuspendLayout();
        _contentPanel.Controls.Clear();
        _blockControls.Clear();

        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part)) continue;

            Control ctrl;
            if (part.StartsWith("```"))
            {
                var content = part.TrimStart('`');
                if (part.EndsWith("```")) content = content.TrimEnd('`');

                var firstLineEnd = content.IndexOf('\n');
                if (firstLineEnd > 0 && firstLineEnd < 20)
                {
                     var firstLine = content.Substring(0, firstLineEnd).Trim();
                     if (!firstLine.Contains(' '))
                     {
                         content = content.Substring(firstLineEnd + 1);
                     }
                }

                ctrl = CreateCodePanel(content);
            }
            else
            {
                ctrl = CreateTextLabel(part);
            }

            _contentPanel.Controls.Add(ctrl);
            _blockControls.Add(ctrl);
        }
        
        _contentPanel.ResumeLayout(true);
        DoLayout();
    }

    private Control CreateTextLabel(string text)
    {
        var panel = new Panel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(14, Theme.GutterSmall, 14, Theme.GutterSmall),
            BackColor = Color.Transparent,
            Margin = new Padding(0, 2, 0, 2)
        };
        
        var innerLabel = new Label
        {
            Text = text,
            Font = Theme.FontMedium,
            ForeColor = Theme.TextPrimary,
            AutoSize = true,
            MaximumSize = new Size(_maxWidth - 32, 0),
            BackColor = Color.Transparent,
            Location = new Point(14, Theme.GutterSmall)
        };
        panel.Controls.Add(innerLabel);
        
        panel.Paint += (s, e) =>
        {
            var p = (Panel)s!;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            
            var rect = new Rectangle(0, 0, p.Width - 1, p.Height - 1);
            using var path = CreateRoundedRectangle(rect, Theme.CornerRadius);
            
            if (_type == BubbleType.User)
            {
                using var gradientBrush = new LinearGradientBrush(
                    rect, UserBubbleStart, UserBubbleEnd, LinearGradientMode.Horizontal);
                e.Graphics.FillPath(gradientBrush, path);
            }
            else if (_type == BubbleType.System)
            {
                using var brush = new SolidBrush(SystemBubbleColor);
                e.Graphics.FillPath(brush, path);
                using var borderPen = new Pen(Theme.BorderSubtle, 1);
                e.Graphics.DrawPath(borderPen, path);
            }
            else
            {
                using var gradientBrush = new LinearGradientBrush(
                    rect, AssistantBubbleStart, AssistantBubbleEnd, LinearGradientMode.Vertical);
                e.Graphics.FillPath(gradientBrush, path);
                using var borderPen = new Pen(Theme.BorderSubtle, 1);
                e.Graphics.DrawPath(borderPen, path);
            }
        };
        
        return panel;
    }

    private Control CreateCodePanel(string code)
    {
        var panel = new Panel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0),
            BackColor = Color.Transparent,
            Margin = new Padding(Theme.SpacingXs, Theme.SpacingSmall, Theme.SpacingXs, Theme.SpacingSmall)
        };
        
        var label = new Label
        {
            Text = code,
            Font = Theme.FontMono,
            ForeColor = Theme.TextSecondary,
            AutoSize = true,
            MaximumSize = new Size(_maxWidth - 40, 0),
            BackColor = Color.Transparent,
            Location = new Point(14, 14)
        };
        
        panel.Controls.Add(label);
        
        panel.Paint += (s, e) =>
        {
            var p = (Panel)s!;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            
            var rect = new Rectangle(0, 0, p.Width - 1, p.Height - 1);
            using var path = CreateRoundedRectangle(rect, Theme.CornerRadiusSmall);
            using var brush = new SolidBrush(CodeBlockColor);
            using var borderPen = new Pen(CodeBlockBorder, 1);
            
            e.Graphics.FillPath(brush, path);
            e.Graphics.DrawPath(borderPen, path);
        };
        
        return panel;
    }

    private void UpdateControl(Control ctrl, string newText)
    {
         if (ctrl is Panel p && p.Controls.Count > 0 && p.Controls[0] is Label l)
         {
             if (l.Font.Name.StartsWith("Cascadia", StringComparison.OrdinalIgnoreCase) || 
                 l.Font.Name == "Consolas")
             {
                var content = newText.TrimStart('`');
                if (newText.EndsWith("```")) content = content.TrimEnd('`');
                
                var firstLineEnd = content.IndexOf('\n');
                if (firstLineEnd > 0 && firstLineEnd < 20)
                {
                     var firstLine = content.Substring(0, firstLineEnd).Trim();
                     if (!firstLine.Contains(' '))
                     {
                         content = content.Substring(firstLineEnd + 1);
                     }
                }
                l.Text = content;
             }
             else
             {
                l.Text = newText;
             }
         }
    }

    private void DoLayout()
    {
        _contentPanel.Location = new Point(6, 6);
        UpdateOwnSize();
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
            foreach (var ctrl in _blockControls)
            {
                ctrl.Dispose();
            }
            _blockControls.Clear();
            _contentPanel.Dispose();
        }
        base.Dispose(disposing);
    }
}
