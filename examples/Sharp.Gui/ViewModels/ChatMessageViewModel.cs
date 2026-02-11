using System.Text;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Sharp.Gui.ViewModels;

public enum ChatMessageRole
{
    User,
    Assistant,
    Tool,
    ToolResult,
    Thinking,
    Error
}

public enum ToolStatus
{
    Waiting,
    Running,
    Completed,
    Error
}

public partial class ChatMessageViewModel : ViewModelBase
{
    private readonly StringBuilder _contentBuilder = new();

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private bool _isStreaming;

    [ObservableProperty]
    private string? _toolName;

    [ObservableProperty]
    private string? _toolCallId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolStatusIcon))]
    [NotifyPropertyChangedFor(nameof(ToolStatusColor))]
    [NotifyPropertyChangedFor(nameof(ToolStatusBrush))]
    [NotifyPropertyChangedFor(nameof(ToolStatusText))]
    private ToolStatus _toolStatus = ToolStatus.Waiting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExpanded))]
    private bool _isCollapsed = true;

    public bool IsExpanded => !IsCollapsed;

    public ChatMessageRole Role { get; }

    public bool IsUser => Role == ChatMessageRole.User;
    public bool IsAssistant => Role == ChatMessageRole.Assistant;
    public bool IsTool => Role == ChatMessageRole.Tool || Role == ChatMessageRole.ToolResult;
    public bool IsToolCall => Role == ChatMessageRole.Tool;
    public bool IsToolResult => Role == ChatMessageRole.ToolResult;
    public bool IsThinking => Role == ChatMessageRole.Thinking;
    public bool IsError => Role == ChatMessageRole.Error;

    public bool IsEmpty => string.IsNullOrWhiteSpace(Content);

    public bool ShowAsToolCard => IsToolCall || IsToolResult;

    public string ToolStatusIcon => ToolStatus switch
    {
        ToolStatus.Waiting => "○",
        ToolStatus.Running => "◌",
        ToolStatus.Completed => "✓",
        ToolStatus.Error => "✕",
        _ => "•"
    };

    public Color ToolStatusColor => ToolStatus switch
    {
        ToolStatus.Waiting => Color.Parse("#94a3b8"),
        ToolStatus.Running => Color.Parse("#fbbf24"),
        ToolStatus.Completed => Color.Parse("#34d399"),
        ToolStatus.Error => Color.Parse("#f87171"),
        _ => Color.Parse("#94a3b8")
    };

    public IBrush ToolStatusBrush => ToolStatus switch
    {
        ToolStatus.Waiting => new SolidColorBrush(Color.Parse("#94a3b8")),
        ToolStatus.Running => new SolidColorBrush(Color.Parse("#fbbf24")),
        ToolStatus.Completed => new SolidColorBrush(Color.Parse("#34d399")),
        ToolStatus.Error => new SolidColorBrush(Color.Parse("#f87171")),
        _ => new SolidColorBrush(Color.Parse("#94a3b8"))
    };

    public string ToolStatusText => ToolStatus switch
    {
        ToolStatus.Waiting => "Waiting",
        ToolStatus.Running => "Running",
        ToolStatus.Completed => "Completed",
        ToolStatus.Error => "Error",
        _ => ""
    };

    public string RoleLabel => Role switch
    {
        ChatMessageRole.User => "USER",
        ChatMessageRole.Assistant => "ASSISTANT",
        ChatMessageRole.Tool => "TOOL",
        ChatMessageRole.ToolResult => "RESULT",
        ChatMessageRole.Thinking => "THINKING",
        ChatMessageRole.Error => "ERROR",
        _ => "UNKNOWN"
    };

    public IBrush RoleColor => Role switch
    {
        ChatMessageRole.User => SolidColorBrush.Parse("#60a5fa"),
        ChatMessageRole.Assistant => SolidColorBrush.Parse("#34d399"),
        ChatMessageRole.Tool => SolidColorBrush.Parse("#fbbf24"),
        ChatMessageRole.ToolResult => SolidColorBrush.Parse("#34d399"),
        ChatMessageRole.Thinking => SolidColorBrush.Parse("#a78bfa"),
        ChatMessageRole.Error => SolidColorBrush.Parse("#f87171"),
        _ => SolidColorBrush.Parse("#94a3b8")
    };

    public ChatMessageViewModel(ChatMessageRole role, string? initialContent = null)
    {
        Role = role;
        if (initialContent != null)
        {
            _contentBuilder.Append(initialContent);
            Content = initialContent;
        }
    }

    public void AppendContent(string delta)
    {
        _contentBuilder.Append(delta);
        Content = _contentBuilder.ToString();
    }

    public void Complete()
    {
        IsStreaming = false;
    }

    [RelayCommand]
    private void ToggleCollapse()
    {
        IsCollapsed = !IsCollapsed;
    }

    public void SetToolRunning()
    {
        ToolStatus = ToolStatus.Running;
    }

    public void SetToolCompleted()
    {
        ToolStatus = ToolStatus.Completed;
        IsStreaming = false;
    }

    public void SetToolError()
    {
        ToolStatus = ToolStatus.Error;
        IsStreaming = false;
    }
}
