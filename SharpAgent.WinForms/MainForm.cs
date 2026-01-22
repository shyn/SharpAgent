using SharpAgent.Core;
using SharpAgent.Core.Configuration;
using SharpAgent.Core.Streaming;
using SharpAgent.Core.Tools;
using SharpAgent.WinForms.Controls;

namespace SharpAgent.WinForms;

public partial class MainForm : Form
{
    private readonly ConfigurationService _configService;
    private Agent? _agent;
    private ILlmClient? _llmClient;
    private CancellationTokenSource? _cts;
    private bool _isRunning;
    private bool _isDisposed;
    private ThinkingConfig _thinkingConfig = ThinkingConfig.Disabled;

    public MainForm()
    {
        // Enable double buffering for smooth rendering
        SetStyle(ControlStyles.OptimizedDoubleBuffer | 
                 ControlStyles.AllPaintingInWmPaint, true);

        _configService = new ConfigurationService();
        _configService.Load();
        _configService.ConfigChanged += (_, _) => ResetAgent();

        InitializeComponent();
        
        // Position header controls after layout
        var titleCenterY = (Theme.HeaderHeight - _titleLabel.PreferredHeight) / 2;
        _titleLabel.Location = new Point(Theme.Gutter, titleCenterY);
        
        var comboCenterY = (Theme.HeaderHeight - _providerCombo.Height) / 2;
        _providerCombo.Location = new Point(_titleLabel.Right + 100 + Theme.SpacingSmall, comboCenterY);
        
        var settingsCenterY = (Theme.HeaderHeight - _settingsButton.Height) / 2;
        _settingsButton.Location = new Point(_providerCombo.Right + Theme.SpacingSmall, settingsCenterY);
        
        var clearCenterY = (Theme.HeaderHeight - _clearButton.Height) / 2;
        _clearButton.Location = new Point(_settingsButton.Right + Theme.SpacingSmall, clearCenterY);
        
        var statusCenterY = (Theme.HeaderHeight - _statusLabel.PreferredHeight) / 2;
        _statusLabel.Location = new Point(_headerPanel.ClientSize.Width - _statusLabel.PreferredWidth - Theme.Gutter, statusCenterY);
        
        // Set up provider combo items and custom draw
        _providerCombo.Items.Add("OpenAI");
        _providerCombo.Items.Add("Anthropic");
        _providerCombo.DrawItem += (sender, e) =>
        {
            string text;
            if (e.Index < 0)
            {
                text = _providerCombo.SelectedItem?.ToString() ?? "";
            }
            else
            {
                text = _providerCombo.Items[e.Index]?.ToString() ?? "";
            }
            
            e.DrawBackground();
            using var brush = new SolidBrush(e.ForeColor);
            var textRect = e.Bounds;
            textRect.Inflate(-4, 0);
            e.Graphics.DrawString(text, e.Font ?? _providerCombo.Font, brush, textRect, 
                new StringFormat { LineAlignment = StringAlignment.Center });
            if (e.Index >= 0) e.DrawFocusRectangle();
        };
        _providerCombo.SelectedIndexChanged += ProviderCombo_SelectedIndexChanged;
        
        // Set selected index after adding to parent to ensure proper text display
        var (providerId, _) = ConfigurationService.ParseModelString(_configService.Config.DefaultModel);
        _providerCombo.SelectedIndex = providerId.Equals("anthropic", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        
        // Header resize handler
        _headerPanel.Resize += (_, _) =>
        {
            var centerY = (Theme.HeaderHeight - _statusLabel.PreferredHeight) / 2;
            _statusLabel.Location = new Point(_headerPanel.ClientSize.Width - _statusLabel.PreferredWidth - Theme.Gutter, centerY);
        };
        
        // Wire up button events
        _settingsButton.Click += SettingsButton_Click;
        _clearButton.Click += ClearButton_Click;
        _stopButton.Click += StopButton_Click;
        
        // Wire up input area events
        _inputArea.SendClicked += (s, e) => SendMessage();
        _inputArea.ThinkingLevelChanged += (s, level) => {
            _thinkingConfig = new ThinkingConfig { Level = level };
            ResetAgent();
            if (level != ThinkingLevel.Off)
                _chatPanel.AddSystemMessage($"Thinking set to {level} ({_thinkingConfig.BudgetTokens} tokens)");
        };
        _inputArea.FileAttachClicked += (s, e) => _chatPanel.AddSystemMessage("File selection coming soon...");
        _inputArea.ImageAttachClicked += (s, e) => _chatPanel.AddSystemMessage("Image selection coming soon...");
        
        // Position stop button on input panel resize
        _inputPanel.Resize += (s, e) => 
        {
            var rightMargin = 20;
            var bottomMargin = 16;
            _stopButton.Location = new Point(
                _inputPanel.Width - _stopButton.Width - rightMargin,
                _inputPanel.Height - _stopButton.Height - bottomMargin
            );
        };
        
        // Wire up chat panel events
        _chatPanel.MessageCountChanged += (s, e) => _clearButton.Enabled = _chatPanel.HasMessages;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Pre-initialize agent to avoid first-request latency
        // Use a small delay to allow UI to fully render first
        Task.Delay(100).ContinueWith(_ => SafeInvoke(EnsureAgentInitialized));
    }

    private void ProviderCombo_SelectedIndexChanged(object? sender, EventArgs e)
    {
        var newProvider = _providerCombo.SelectedIndex == 1 ? "anthropic" : "openai";
        var (currentProvider, currentModel) = ConfigurationService.ParseModelString(_configService.Config.DefaultModel);
        if (!currentProvider.Equals(newProvider, StringComparison.OrdinalIgnoreCase))
        {
            // Switch to default model for the new provider
            var providerConfig = _configService.GetProviderConfig(newProvider);
            var firstModel = providerConfig?.Models.FirstOrDefault()?.Id ?? currentModel;
            _configService.Update(c => c.DefaultModel = $"{newProvider}/{firstModel}");
        }
    }

    private void SettingsButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new ConfigDialog(_configService);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            var (providerId, _) = ConfigurationService.ParseModelString(_configService.Config.DefaultModel);
            _providerCombo.SelectedIndex = providerId.Equals("anthropic", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            _chatPanel.AddSystemMessage($"Configuration updated. Using {_configService.GetCurrentModelName()}");
        }
    }

    private void ResetAgent()
    {
        _llmClient?.Dispose();
        _llmClient = null;
        _agent = null;
    }

    private void ClearButton_Click(object? sender, EventArgs e)
    {
        if (_isRunning) return; // Don't clear while processing
        
        _chatPanel.Clear();
        ResetAgent();
        _statusLabel.Text = "Ready";
    }

    private void StopButton_Click(object? sender, EventArgs e)
    {
        _cts?.Cancel();
    }

    private async void SendMessage()
    {
        var message = _inputArea.TextContent.Trim();
        if (string.IsNullOrEmpty(message) || _isRunning) return;

        _inputArea.Clear();
        SetRunningState(true);

        _chatPanel.AddUserMessage("You", message);

        try
        {
            EnsureAgentInitialized();
            if (_agent == null)
            {
                _chatPanel.AddErrorMessage("Agent not initialized. Check your API key in settings.");
                return;
            }

            _cts = new CancellationTokenSource();
            await ProcessAgentResponseAsync(message, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            _chatPanel.FinalizeCurrentBubble();
            _chatPanel.AddSystemMessage("Request cancelled.");
        }
        catch (Exception ex)
        {
            _chatPanel.AddErrorMessage(ex.Message);
        }
        finally
        {
            SetRunningState(false);
        }
    }

    private void SetRunningState(bool running)
    {
        _isRunning = running;
        _inputArea.Enabled = !running;
        
        if (running)
        {
            _stopButton.Visible = true;
            _stopButton.BringToFront();
            // Initially disable to prevent accidental double-click triggering cancel immediately
            _stopButton.Enabled = false;
            
            // Re-enable after a short safety delay (e.g., 500ms)
            System.Windows.Forms.Timer safetyTimer = new System.Windows.Forms.Timer { Interval = 500 };
            safetyTimer.Tick += (s, e) =>
            {
                if (_isRunning && !_isDisposed) _stopButton.Enabled = true;
                safetyTimer.Stop();
                safetyTimer.Dispose();
            };
            safetyTimer.Start();
        }
        else
        {
            _stopButton.Visible = false;
            _stopButton.Enabled = true; // Reset for next time
        }

        _providerCombo.Enabled = !running;
        _settingsButton.Enabled = !running;
        _statusLabel.Text = running ? "Processing..." : "Ready";
        _statusLabel.ForeColor = running ? Theme.FocusRing : Theme.TextMuted;
    }

    private void EnsureAgentInitialized()
    {
        if (_agent != null) return;

        if (!_configService.HasApiKey())
        {
            var (providerId, _) = ConfigurationService.ParseModelString(_configService.Config.DefaultModel);
            _chatPanel.AddErrorMessage($"No API key configured for {providerId}. Click ⚙ to add one.");
            return;
        }

        _llmClient = _configService.CreateLlmClient(null, _thinkingConfig);
        
        var thinkingStatus = _thinkingConfig.Enabled ? " + Thinking" : "";
        _statusLabel.Text = $"{_configService.GetCurrentModelName()}{thinkingStatus}";

        var tools = new ITool[] { new CalculatorTool(), new ReadFileTool(), new ListFilesTool(), new BashTool(), new GlobTool(), new GrepTool() };
        _agent = new Agent(_llmClient, tools, new AgentOptions());
    }

    private async Task ProcessAgentResponseAsync(string message, CancellationToken ct)
    {
        var textStarted = false;
        var thinkingStarted = false;
        var pendingToolCalls = new Dictionary<string, string>();
        ct.ThrowIfCancellationRequested();
        await foreach (var evt in _agent!.RunStreamingAsync(message, ct))
        {
            if (ct.IsCancellationRequested) break;

            switch (evt)
            {
                case AgentThinkingDeltaEvent thinkingDelta:
                    if (!thinkingStarted)
                    {
                        SafeInvoke(() => _statusLabel.Text = "💭 Thinking...");
                        thinkingStarted = true;
                    }
                    SafeInvoke(() => _chatPanel.AppendThinking(thinkingDelta.Thinking));
                    break;

                case AgentThinkingCompletedEvent:
                    SafeInvoke(() => _chatPanel.CompleteThinking());
                    break;

                case AgentTextDeltaEvent delta:
                    if (!textStarted)
                    {
                        if (thinkingStarted)
                        {
                            SafeInvoke(() => _statusLabel.Text = "Processing...");
                        }
                        _chatPanel.StartAssistantMessage("Agent");
                        textStarted = true;
                    }
                    SafeInvoke(() => _chatPanel.AppendAssistantText(delta.Text));
                    break;

                case AgentToolCallStartedEvent toolStart:
                    if (textStarted)
                    {
                        _chatPanel.FinalizeCurrentBubble();
                        textStarted = false;
                    }
                    thinkingStarted = false; // Reset thinking state for next iteration
                    pendingToolCalls[toolStart.ToolCallId] = toolStart.ToolName;
                    SafeInvoke(() =>
                    {
                        _chatPanel.AddToolCall(toolStart.ToolName, toolStart.Arguments);
                        _statusLabel.Text = $"🔧 {toolStart.ToolName}";
                    });
                    break;

                case AgentToolCallCompletedEvent toolComplete:
                    SafeInvoke(() => _chatPanel.CompleteToolCall(toolComplete.Result, toolComplete.IsError));
                    pendingToolCalls.Remove(toolComplete.ToolCallId);
                    break;

                case AgentCompletedEvent:
                    SafeInvoke(() => _chatPanel.FinalizeCurrentBubble());
                    break;

                case AgentErrorEvent error:
                    SafeInvoke(() => _chatPanel.AddErrorMessage(error.Message));
                    break;
            }
        }
    }

    private void SafeInvoke(Action action)
    {
        if (_isDisposed || !IsHandleCreated) return;
        try
        {
            Invoke(action);
        }
        catch (ObjectDisposedException)
        {
            // Form was disposed during async operation
        }
        catch (InvalidOperationException)
        {
            // Handle not created
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _isDisposed = true;
        _cts?.Cancel();
        _llmClient?.Dispose();
        base.OnFormClosing(e);
    }
}
