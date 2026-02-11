using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sharp.AI;
using Sharp.Core;
using Sharp.Core.Configuration;
using Sharp.Core.Sessions;

namespace Sharp.Gui.ViewModels;

public partial class ChatViewModel : ViewModelBase
{
    private AgentSession? _session;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _inputText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _isProcessing;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private string _tokenInfo = "";

    [ObservableProperty]
    private bool _hasHistory;

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public async Task InitializeAsync(AgentRuntimeOptions runtimeOptions, CancellationToken ct = default)
    {
        StatusText = "Initializing session...";
        _session = await AgentSession.CreateAsync(runtimeOptions, ct: ct);
        StatusText = $"Model: {_session.Model.ProviderId}/{_session.Model.ModelId}";
        RefreshCommandStates();
    }

    public async Task InitializeWithSessionAsync(AgentRuntimeOptions runtimeOptions, string sessionFilePath, CancellationToken ct = default)
    {
        StatusText = "Loading session...";

        var sessionManager = await SessionManager.LoadAsync(sessionFilePath, ct);
        _session = await AgentSession.CreateAsync(runtimeOptions, sessionId: sessionManager.SessionId, ct: ct);

        // Restore history from session entries
        await LoadHistoryFromSessionAsync(sessionManager);

        StatusText = $"Model: {_session.Model.ProviderId}/{_session.Model.ModelId}";
        RefreshCommandStates();
    }

    private Task LoadHistoryFromSessionAsync(SessionManager sessionManager)
    {
        var branch = sessionManager.GetCurrentBranch();

        foreach (var entry in branch)
        {
            switch (entry.Type)
            {
                case "message":
                {
                    var payload = entry.Payload.Deserialize<MessageEntryPayload>(JsonDefaults.Options);
                    if (payload?.Message == null) continue;

                    switch (payload.Message.Role)
                    {
                        case LlmMessageRole.User:
                        {
                            // User messages - show text content
                            var text = GetTextContent(payload.Message);
                            if (!string.IsNullOrWhiteSpace(text))
                                Messages.Add(new ChatMessageViewModel(ChatMessageRole.User, text));
                            break;
                        }

                        case LlmMessageRole.Assistant:
                        {
                            // Assistant messages - show only text, filter out tool_calls
                            var text = GetTextContent(payload.Message);
                            if (!string.IsNullOrWhiteSpace(text))
                                Messages.Add(new ChatMessageViewModel(ChatMessageRole.Assistant, text));
                            break;
                        }

                        case LlmMessageRole.Tool:
                        {
                            // Tool results - show with actual state from session
                            var toolResult = payload.Message.Content.OfType<ToolResultContentBlock>().FirstOrDefault();
                            if (toolResult != null)
                            {
                                // Determine real state: Error if IsError flag is set, otherwise Completed
                                var state = toolResult.IsError ? ToolStatus.Error : ToolStatus.Completed;
                                var toolMsg = new ChatMessageViewModel(ChatMessageRole.Tool, toolResult.ContentText)
                                {
                                    ToolName = toolResult.ToolName,
                                    ToolCallId = toolResult.ToolCallId,
                                    ToolStatus = state
                                };
                                Messages.Add(toolMsg);
                            }
                            break;
                        }
                    }
                    break;
                }

                case "compaction":
                {
                    var payload = entry.Payload.Deserialize<CompactionEntryPayload>(JsonDefaults.Options);
                    if (payload != null)
                    {
                        Messages.Add(new ChatMessageViewModel(ChatMessageRole.Tool,
                            $"⟳ Compacted ({payload.TokensBefore} tokens)\n{(payload.Summary.Length > 200 ? payload.Summary[..200] + "..." : payload.Summary)}"));
                    }
                    break;
                }
            }
        }

        HasHistory = Messages.Count > 0;
        return Task.CompletedTask;
    }

    private static string GetTextContent(LlmMessage message)
    {
        return string.Join("", message.Content.OfType<TextContentBlock>().Select(t => t.Text));
    }

    private bool CanSend() => _session != null && !IsProcessing && !string.IsNullOrWhiteSpace(InputText);

    private void RefreshCommandStates()
    {
        SendCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        if (_session == null || string.IsNullOrWhiteSpace(InputText))
            return;

        var prompt = InputText.Trim();
        InputText = string.Empty;

        Messages.Add(new ChatMessageViewModel(ChatMessageRole.User, prompt));

        await RunAgentStreamAsync(_session.PromptAsync(prompt, (_cts = new CancellationTokenSource()).Token));
    }

    private async Task RunAgentStreamAsync(IAsyncEnumerable<AgentEvent> eventStream)
    {
        IsProcessing = true;
        StatusText = "Thinking...";

        ChatMessageViewModel? currentAssistant = null;
        ChatMessageViewModel? currentThinking = null;
        ChatMessageViewModel? currentTool = null;

        try
        {
            await foreach (var evt in eventStream)
            {
                switch (evt)
                {
                    case AgentThinkingStartedEvent:
                        currentThinking = new ChatMessageViewModel(ChatMessageRole.Thinking) { IsStreaming = true };
                        await Dispatcher.UIThread.InvokeAsync(() => Messages.Add(currentThinking));
                        break;

                    case AgentThinkingDeltaEvent thinkingDelta:
                        if (currentThinking != null)
                            await Dispatcher.UIThread.InvokeAsync(() => currentThinking.AppendContent(thinkingDelta.Delta));
                        break;

                    case AgentThinkingCompletedEvent:
                        if (currentThinking != null)
                        {
                            // Remove empty thinking messages from UI
                            if (currentThinking.IsEmpty)
                            {
                                await Dispatcher.UIThread.InvokeAsync(() => Messages.Remove(currentThinking));
                            }
                            else
                            {
                                await Dispatcher.UIThread.InvokeAsync(() => currentThinking.Complete());
                            }
                        }
                        currentThinking = null;
                        break;

                    case AgentTextDeltaEvent textDelta:
                        if (currentAssistant == null)
                        {
                            currentAssistant = new ChatMessageViewModel(ChatMessageRole.Assistant) { IsStreaming = true };
                            await Dispatcher.UIThread.InvokeAsync(() => Messages.Add(currentAssistant));
                        }
                        await Dispatcher.UIThread.InvokeAsync(() => currentAssistant.AppendContent(textDelta.Delta));
                        break;

                    case AgentToolUseStartedEvent toolUseStarted:
                        // LLM返回需要调用工具 - 创建Waiting状态的工具卡片
                        var waitingTool = new ChatMessageViewModel(ChatMessageRole.Tool, "")
                        {
                            ToolName = toolUseStarted.ToolName,
                            ToolCallId = toolUseStarted.ToolCallId,
                            ToolStatus = ToolStatus.Waiting,
                            IsStreaming = true
                        };
                        await Dispatcher.UIThread.InvokeAsync(() => Messages.Add(waitingTool));
                        break;

                    case AgentToolExecutionStartedEvent toolStarted:
                        // 工具实际开始执行 - 更新为Running状态
                        var existingTool = Messages.FirstOrDefault(m =>
                            m.ToolCallId == toolStarted.ToolCallId && m.ToolStatus == ToolStatus.Waiting);
                        if (existingTool != null)
                        {
                            currentTool = existingTool;
                            await Dispatcher.UIThread.InvokeAsync(() => currentTool.SetToolRunning());
                        }
                        else
                        {
                            // 如果没有找到Waiting状态的工具卡片，创建一个新的
                            currentTool = new ChatMessageViewModel(ChatMessageRole.Tool, "")
                            {
                                ToolName = toolStarted.ToolName,
                                ToolCallId = toolStarted.ToolCallId,
                                ToolStatus = ToolStatus.Running,
                                IsStreaming = true
                            };
                            await Dispatcher.UIThread.InvokeAsync(() => Messages.Add(currentTool));
                        }
                        StatusText = $"Tool: {toolStarted.ToolName}";
                        break;

                    case AgentToolExecutionCompletedEvent toolCompleted:
                        var resultText = toolCompleted.Result.ContentAsText;
                        // 通过ToolCallId找到对应的工具卡片（优先使用currentTool，否则按ToolCallId查找）
                        var toolToComplete = (currentTool?.ToolCallId == toolCompleted.ToolCallId ? currentTool : null)
                            ?? Messages.FirstOrDefault(m =>
                                m.ToolCallId == toolCompleted.ToolCallId &&
                                (m.ToolStatus == ToolStatus.Waiting || m.ToolStatus == ToolStatus.Running));
                        if (toolToComplete != null)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                toolToComplete.Content = resultText;
                                toolToComplete.SetToolCompleted();
                            });
                        }
                        currentTool = null;
                        break;

                    case AgentCompletedEvent:
                        if (currentAssistant != null)
                            await Dispatcher.UIThread.InvokeAsync(() => currentAssistant.Complete());
                        currentAssistant = null;
                        break;

                    case AgentCompactionRequiredEvent compactionEvt:
                        await Dispatcher.UIThread.InvokeAsync(() =>
                            TokenInfo = $"⚠ Tokens: {compactionEvt.TokenCount}/{compactionEvt.Threshold}");
                        break;

                    case AgentErrorEvent errorEvent:
                        // 尝试找到当前相关的工具卡片
                        var toolInError = currentTool ?? Messages.LastOrDefault(m =>
                            m.Role == ChatMessageRole.Tool &&
                            (m.ToolStatus == ToolStatus.Waiting || m.ToolStatus == ToolStatus.Running));
                        if (toolInError != null)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                toolInError.Content = errorEvent.Message;
                                toolInError.SetToolError();
                            });
                            currentTool = null;
                        }
                        else
                        {
                            var errorMsg = new ChatMessageViewModel(ChatMessageRole.Error, $"Error: {errorEvent.Message}");
                            await Dispatcher.UIThread.InvokeAsync(() => Messages.Add(errorMsg));
                        }
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Messages.Add(new ChatMessageViewModel(ChatMessageRole.Error, "Cancelled."));
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessageViewModel(ChatMessageRole.Error, $"Error: {ex.Message}"));
        }
        finally
        {
            currentAssistant?.Complete();
            currentThinking?.Complete();
            IsProcessing = false;
            HasHistory = Messages.Count > 0;
            StatusText = "Ready";
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _session?.Abort();
        _cts?.Cancel();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _session?.Dispose();
        _session = null;
        HasHistory = false;
        RefreshCommandStates();
    }
}
