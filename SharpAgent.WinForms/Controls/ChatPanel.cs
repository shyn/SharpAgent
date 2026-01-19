namespace SharpAgent.WinForms.Controls;

public class ChatPanel : Panel
{
    private readonly FlowLayoutPanel _messageContainer;
    private readonly Panel _emptyStatePanel;
    private readonly List<Control> _messages = [];
    private ChatBubble? _currentAssistantBubble;
    private ToolCallCard? _currentToolCard;
    private ThinkingCard? _currentThinkingCard;

    private int _bubbleMaxWidth = 600;
    private bool _isDisposed;

    public bool HasMessages => _messages.Count > 0;
    public event EventHandler? MessageCountChanged;

    public ChatPanel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | 
                 ControlStyles.AllPaintingInWmPaint, true);
        DoubleBuffered = true;
        BackColor = Theme.Background;
        AutoScroll = true;
        Padding = new Padding(0);

        _messageContainer = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(Theme.GutterLarge + Theme.SpacingSmall / 2, Theme.GutterLarge, Theme.GutterLarge + Theme.SpacingSmall / 2, Theme.GutterLarge),
            BackColor = Color.Transparent,
            Dock = DockStyle.Top
        };

        // Empty state panel - shown when no messages
        _emptyStatePanel = CreateEmptyStatePanel();

        Controls.Add(_messageContainer);
        Controls.Add(_emptyStatePanel);

        Resize += (_, _) => OnPanelResize();
        UpdateEmptyState();
    }

    private Panel CreateEmptyStatePanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Visible = true
        };

        var iconLabel = new Label
        {
            Text = "🤖",
            Font = new Font("Segoe UI Emoji", 48),
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            BackColor = Color.Transparent
        };

        var titleLabel = new Label
        {
            Text = "Welcome to SharpAgent",
            Font = new Font("Segoe UI", 18, FontStyle.Bold),
            ForeColor = Theme.TextSecondary,
            AutoSize = true,
            BackColor = Color.Transparent
        };

        var subtitleLabel = new Label
        {
            Text = "Type a message below to start a conversation",
            Font = Theme.FontMedium,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            BackColor = Color.Transparent
        };

        panel.Controls.Add(iconLabel);
        panel.Controls.Add(titleLabel);
        panel.Controls.Add(subtitleLabel);

        // Center the labels on resize
        panel.Resize += (s, e) =>
        {
            iconLabel.Location = new Point((panel.Width - iconLabel.Width) / 2, panel.Height / 2 - 100);
            titleLabel.Location = new Point((panel.Width - titleLabel.Width) / 2, iconLabel.Bottom + 16);
            subtitleLabel.Location = new Point((panel.Width - subtitleLabel.Width) / 2, titleLabel.Bottom + 8);
        };

        return panel;
    }

    private void UpdateEmptyState()
    {
        _emptyStatePanel.Visible = _messages.Count == 0;
        _messageContainer.Visible = _messages.Count > 0;
        MessageCountChanged?.Invoke(this, EventArgs.Empty);
    }


    private void OnPanelResize()
    {
        _bubbleMaxWidth = Math.Max(300, (int)(Width * 0.72));
        ReflowAllBubbles();
    }

    private void ReflowAllBubbles()
    {
        if (_isDisposed || _messages.Count == 0) return;

        var containerWidth = GetWrapperWidth();

        _messageContainer.SuspendLayout();

        foreach (var wrapper in _messages.OfType<Panel>())
        {
            wrapper.Width = containerWidth;

            if (wrapper.Controls.Count == 0) continue;

            var bubble = wrapper.Controls[0];
            RepositionBubble(wrapper, bubble);
        }

        _messageContainer.ResumeLayout(true);
    }

    private void RepositionBubble(Panel wrapper, Control bubble)
    {
        var bubbleType = GetBubbleType(bubble);

        switch (bubbleType)
        {
            case BubbleAlignment.Right:
                bubble.Location = new Point(Math.Max(0, wrapper.Width - bubble.Width - Theme.GutterSmall), Theme.SpacingSmall / 2 + Theme.SpacingXs / 2);
                break;
            case BubbleAlignment.Center:
                bubble.Location = new Point((wrapper.Width - bubble.Width) / 2, Theme.SpacingSmall / 2 + Theme.SpacingXs / 2);
                break;
            case BubbleAlignment.Left:
            default:
                bubble.Location = new Point(Theme.GutterSmall, Theme.SpacingSmall / 2 + Theme.SpacingXs / 2);
                break;
        }
    }

    private static BubbleAlignment GetBubbleType(Control bubble)
    {
        return bubble switch
        {
            ChatBubble cb => cb.Type switch
            {
                BubbleType.User => BubbleAlignment.Right,
                BubbleType.System => BubbleAlignment.Center,
                _ => BubbleAlignment.Left
            },
            ToolCallCard => BubbleAlignment.Left,
            ThinkingCard => BubbleAlignment.Left,
            _ => BubbleAlignment.Left
        };
    }

    private int GetWrapperWidth() => Math.Max(100, ClientSize.Width - Theme.GutterLarge * 1 - Theme.SpacingSmall); // Reduced gutter to 1x to ensure full width usage

    private enum BubbleAlignment { Left, Right, Center }

    private Panel CreateWrapper()
    {
        var containerWidth = GetWrapperWidth();
        var wrapper = new Panel
        {
            AutoSize = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, Theme.SpacingSmall / 2 + Theme.SpacingXs / 2, 0, Theme.SpacingSmall / 2 + Theme.SpacingXs / 2),
            Width = containerWidth,
            Height = 48
        };
        return wrapper;
    }

    public void AddUserMessage(string role, string content)
    {
        if (_isDisposed) return;
        FinalizeCurrentBubble();

        var wrapper = CreateWrapper();
        var bubble = new ChatBubble(BubbleType.User, role, content, _bubbleMaxWidth);
        
        bubble.Location = new Point(Math.Max(0, wrapper.Width - bubble.Width - Theme.GutterSmall), Theme.SpacingSmall / 2 + Theme.SpacingXs / 2);
        bubble.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        wrapper.Controls.Add(bubble);
        wrapper.Height = bubble.Height + Theme.GutterSmall;

        bubble.Resize += (s, e) => {
            if (_isDisposed) return;
            wrapper.Height = bubble.Height + Theme.GutterSmall;
            RepositionBubble(wrapper, bubble);
            _messageContainer.PerformLayout();
        };

        _messageContainer.Controls.Add(wrapper);
        _messages.Add(wrapper);
        UpdateEmptyState();
        ScrollToBottom();
    }

    public void StartAssistantMessage(string role)
    {
        if (_isDisposed) return;
        FinalizeCurrentBubble();

        var wrapper = CreateWrapper();
        _currentAssistantBubble = new ChatBubble(BubbleType.Assistant, role, "", _bubbleMaxWidth);
        _currentAssistantBubble.Location = new Point(Theme.GutterSmall, Theme.SpacingSmall / 2 + Theme.SpacingXs / 2);
        _currentAssistantBubble.Anchor = AnchorStyles.Top | AnchorStyles.Left;

        wrapper.Controls.Add(_currentAssistantBubble);
        wrapper.Height = _currentAssistantBubble.Height + Theme.GutterSmall;

        _currentAssistantBubble.Resize += (s, e) => {
            if (_isDisposed || _currentAssistantBubble == null) return;
            wrapper.Height = _currentAssistantBubble.Height + Theme.GutterSmall;
            _messageContainer.PerformLayout();
        };

        wrapper.Tag = _currentAssistantBubble;
        _messageContainer.Controls.Add(wrapper);
        _messages.Add(wrapper);
        UpdateEmptyState();
        ScrollToBottom();
    }

    public void AppendAssistantText(string text)
    {
        if (_isDisposed) return;
        
        // Finalize thinking card when text starts
        if (_currentThinkingCard != null)
        {
            _currentThinkingCard.CompleteThinking();
            _currentThinkingCard = null;
        }
        
        if (_currentAssistantBubble == null)
        {
            StartAssistantMessage("Agent");
        }

        _currentAssistantBubble!.AppendText(text);
        ScrollToBottom();
    }

    public void StartThinking()
    {
        if (_isDisposed) return;
        FinalizeCurrentBubble();

        var wrapper = CreateWrapper();
        _currentThinkingCard = new ThinkingCard(_bubbleMaxWidth);
        _currentThinkingCard.Location = new Point(Theme.GutterSmall, Theme.SpacingSmall / 2 + Theme.SpacingXs / 2);
        _currentThinkingCard.Anchor = AnchorStyles.Top | AnchorStyles.Left;

        wrapper.Controls.Add(_currentThinkingCard);
        wrapper.Height = _currentThinkingCard.Height + Theme.GutterSmall;

        _currentThinkingCard.Resize += (s, e) =>
        {
            if (_isDisposed || _currentThinkingCard == null) return;
            wrapper.Height = _currentThinkingCard.Height + Theme.GutterSmall;
            _messageContainer.PerformLayout();
        };

        wrapper.Tag = _currentThinkingCard;
        _messageContainer.Controls.Add(wrapper);
        _messages.Add(wrapper);
        UpdateEmptyState();
        ScrollToBottom();
    }

    public void AppendThinking(string text)
    {
        if (_isDisposed) return;

        if (_currentThinkingCard == null)
        {
            StartThinking();
        }

        _currentThinkingCard!.AppendThinking(text);
        ScrollToBottom();
    }

    public void CompleteThinking()
    {
        if (_isDisposed || _currentThinkingCard == null) return;
        _currentThinkingCard.CompleteThinking();
        // Note: Keep _currentThinkingCard reference so it finalizes when text starts
    }

    public void AddToolCall(string toolName, string arguments)
    {
        if (_isDisposed) return;
        FinalizeCurrentBubble();

        var wrapper = CreateWrapper();
        _currentToolCard = new ToolCallCard(toolName, arguments, _bubbleMaxWidth);
        _currentToolCard.Location = new Point(Theme.GutterSmall, Theme.SpacingSmall / 2 + Theme.SpacingXs / 2);
        _currentToolCard.Anchor = AnchorStyles.Top | AnchorStyles.Left;

        wrapper.Controls.Add(_currentToolCard);
        wrapper.Height = _currentToolCard.Height + Theme.GutterSmall;

        _currentToolCard.Resize += (s, e) => 
        {
            if (_isDisposed || _currentToolCard == null) return;
            wrapper.Height = _currentToolCard.Height + Theme.GutterSmall;
            _messageContainer.PerformLayout(); 
        };

        wrapper.Tag = _currentToolCard;

        _messageContainer.Controls.Add(wrapper);
        _messages.Add(wrapper);
        UpdateEmptyState();

        ScrollToBottom();
    }

    public void CompleteToolCall(string result, bool isError)
    {
        if (_isDisposed || _currentToolCard == null) return;

        _currentToolCard.SetResult(result, isError);
        _currentToolCard = null;
        ScrollToBottom();
    }

    public void AddSystemMessage(string content)
    {
        if (_isDisposed) return;
        FinalizeCurrentBubble();

        var wrapper = CreateWrapper();
        var bubble = new ChatBubble(BubbleType.System, "System", content, _bubbleMaxWidth);
        
        bubble.Location = new Point((wrapper.Width - bubble.Width) / 2, Theme.SpacingSmall / 2 + Theme.SpacingXs / 2);
        bubble.Anchor = AnchorStyles.Top;

        wrapper.Controls.Add(bubble);
        wrapper.Height = bubble.Height + Theme.GutterSmall;

        bubble.Resize += (s, e) => {
            if (_isDisposed) return;
            wrapper.Height = bubble.Height + Theme.GutterSmall;
            RepositionBubble(wrapper, bubble);
            _messageContainer.PerformLayout();
        };

        _messageContainer.Controls.Add(wrapper);
        _messages.Add(wrapper);
        UpdateEmptyState();
        ScrollToBottom();
    }

    public void AddErrorMessage(string content)
    {
        if (_isDisposed) return;
        FinalizeCurrentBubble();

        var wrapper = new Panel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Padding = new Padding(0, Theme.SpacingSmall / 2 + Theme.SpacingXs / 2, 0, Theme.SpacingSmall / 2 + Theme.SpacingXs / 2),
            Width = GetWrapperWidth()
        };

        var label = new Label
        {
            Text = $"⚠️ {content}",
            Font = Theme.FontMedium,
            ForeColor = Color.FromArgb(255, 130, 130),
            AutoSize = true,
            BackColor = Color.FromArgb(55, 28, 28),
            Padding = new Padding(Theme.Gutter - 2, Theme.SpacingSmall + 2, Theme.Gutter - 2, Theme.SpacingSmall + 2)
        };
        label.Location = new Point(0, 0);

        wrapper.Controls.Add(label);
        wrapper.Height = label.Height + Theme.GutterSmall;

        _messageContainer.Controls.Add(wrapper);
        _messages.Add(wrapper);
        UpdateEmptyState();

        ScrollToBottom();
    }

    public void FinalizeCurrentBubble()
    {
        _currentAssistantBubble = null;
        _currentToolCard = null;
        _currentThinkingCard = null;
    }

    public void Clear()
    {
        _messageContainer.Controls.Clear();
        _messages.Clear();
        _currentAssistantBubble = null;
        _currentToolCard = null;
        _currentThinkingCard = null;
        UpdateEmptyState();
    }

    private void ScrollToBottom()
    {
        if (_isDisposed || !IsHandleCreated) return;
        
        try
        {
            BeginInvoke(() =>
            {
                if (_isDisposed || !IsHandleCreated) return;
                VerticalScroll.Value = VerticalScroll.Maximum;
                PerformLayout();
            });
        }
        catch (ObjectDisposedException)
        {
            // Form was disposed during async operation
        }
        catch (InvalidOperationException)
        {
            // Handle not created yet
        }
    }

    protected override void Dispose(bool disposing)
    {
        _isDisposed = true;
        if (disposing)
        {
            foreach (var msg in _messages)
            {
                msg.Dispose();
            }
            _messages.Clear();
            _messageContainer.Dispose();
        }
        base.Dispose(disposing);
    }
}
