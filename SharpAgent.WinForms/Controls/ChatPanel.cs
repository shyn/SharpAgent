namespace SharpAgent.WinForms.Controls;

public class ChatPanel : Panel
{
    private readonly FlowLayoutPanel _messageContainer;
    private readonly List<Control> _messages = [];
    private ChatBubble? _currentAssistantBubble;
    private ToolCallCard? _currentToolCard;

    private int _bubbleMaxWidth = 600;

    public ChatPanel()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(25, 25, 28);
        AutoScroll = true;
        Padding = new Padding(0);

        _messageContainer = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(20, 15, 20, 15),
            BackColor = Color.Transparent,
            Dock = DockStyle.Top
        };

        Controls.Add(_messageContainer);

        Resize += (_, _) => UpdateBubbleMaxWidth();
    }

    private void UpdateBubbleMaxWidth()
    {
        _bubbleMaxWidth = Math.Max(300, (int)(Width * 0.7));
    }

    public void AddUserMessage(string content)
    {
        FinalizeCurrentBubble();

        var wrapper = new Panel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 4, 0, 4),
            Width = _messageContainer.Width - 40
        };

        var bubble = new ChatBubble(BubbleType.User, "You", content, _bubbleMaxWidth);
        bubble.Location = new Point(wrapper.Width - bubble.Width - 20, 0);
        bubble.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        wrapper.Controls.Add(bubble);
        wrapper.Height = bubble.Height + 8;

        _messageContainer.Controls.Add(wrapper);
        _messages.Add(wrapper);

        ScrollToBottom();
    }

    public void StartAssistantMessage()
    {
        FinalizeCurrentBubble();

        var wrapper = new Panel
        {
            AutoSize = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 4, 0, 4),
            Width = _messageContainer.Width - 40,
            Height = 60
        };

        _currentAssistantBubble = new ChatBubble(BubbleType.Assistant, "Agent", "", _bubbleMaxWidth);
        _currentAssistantBubble.Location = new Point(0, 0);

        wrapper.Controls.Add(_currentAssistantBubble);
        wrapper.Tag = _currentAssistantBubble;

        _messageContainer.Controls.Add(wrapper);
        _messages.Add(wrapper);

        ScrollToBottom();
    }

    public void AppendAssistantText(string text)
    {
        if (_currentAssistantBubble == null)
        {
            StartAssistantMessage();
        }

        _currentAssistantBubble!.AppendText(text);

        if (_currentAssistantBubble.Parent is Panel wrapper)
        {
            wrapper.Height = _currentAssistantBubble.Height + 8;
        }

        ScrollToBottom();
    }

    public void AddToolCall(string toolName, string arguments)
    {
        FinalizeCurrentBubble();

        var wrapper = new Panel
        {
            AutoSize = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 4, 0, 4),
            Width = _messageContainer.Width - 40
        };

        _currentToolCard = new ToolCallCard(toolName, arguments, _bubbleMaxWidth);
        _currentToolCard.Location = new Point(0, 0);

        wrapper.Controls.Add(_currentToolCard);
        wrapper.Height = _currentToolCard.Height + 8;
        wrapper.Tag = _currentToolCard;

        _messageContainer.Controls.Add(wrapper);
        _messages.Add(wrapper);

        ScrollToBottom();
    }

    public void CompleteToolCall(string result, bool isError)
    {
        if (_currentToolCard == null) return;

        _currentToolCard.SetResult(result, isError);

        if (_currentToolCard.Parent is Panel wrapper)
        {
            wrapper.Height = _currentToolCard.Height + 8;
        }

        _currentToolCard = null;
        ScrollToBottom();
    }

    public void AddSystemMessage(string content)
    {
        FinalizeCurrentBubble();

        var wrapper = new Panel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 4, 0, 4),
            Width = _messageContainer.Width - 40
        };

        var bubble = new ChatBubble(BubbleType.System, "System", content, _bubbleMaxWidth);
        bubble.Location = new Point((wrapper.Width - bubble.Width) / 2, 0);

        wrapper.Controls.Add(bubble);
        wrapper.Height = bubble.Height + 8;

        _messageContainer.Controls.Add(wrapper);
        _messages.Add(wrapper);

        ScrollToBottom();
    }

    public void AddErrorMessage(string content)
    {
        FinalizeCurrentBubble();

        var wrapper = new Panel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 4, 0, 4),
            Width = _messageContainer.Width - 40
        };

        var label = new Label
        {
            Text = $"⚠️ {content}",
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.FromArgb(255, 120, 120),
            AutoSize = true,
            BackColor = Color.FromArgb(60, 30, 30),
            Padding = new Padding(12, 8, 12, 8)
        };
        label.Location = new Point(0, 0);

        wrapper.Controls.Add(label);
        wrapper.Height = label.Height + 8;

        _messageContainer.Controls.Add(wrapper);
        _messages.Add(wrapper);

        ScrollToBottom();
    }

    public void FinalizeCurrentBubble()
    {
        _currentAssistantBubble = null;
        _currentToolCard = null;
    }

    public void Clear()
    {
        _messageContainer.Controls.Clear();
        _messages.Clear();
        _currentAssistantBubble = null;
        _currentToolCard = null;
    }

    private void ScrollToBottom()
    {
        BeginInvoke(() =>
        {
            VerticalScroll.Value = VerticalScroll.Maximum;
            PerformLayout();
        });
    }
}
