using SharpAgent.Core;
using SharpAgent.Core.Configuration;
using SharpAgent.Core.Streaming;
using SharpAgent.Core.Tools;
using SharpAgent.WinForms.Controls;

namespace SharpAgent.WinForms;

public partial class MainForm : Form
{
    private readonly ChatPanel _chatPanel;
    private readonly TextBox _inputBox;
    private readonly Button _sendButton;
    private readonly Button _stopButton;
    private readonly Button _settingsButton;
    private readonly ComboBox _providerCombo;
    private readonly Panel _headerPanel;
    private readonly Panel _inputPanel;
    private readonly Label _statusLabel;

    private readonly ConfigurationService _configService;
    private Agent? _agent;
    private HttpClient? _httpClient;
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    public MainForm()
    {
        Text = "SharpAgent";
        Size = new Size(950, 750);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(600, 450);
        BackColor = Color.FromArgb(30, 30, 33);

        _configService = new ConfigurationService();
        _configService.Load();
        _configService.ConfigChanged += (_, _) => ResetAgent();

        _headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 50,
            BackColor = Color.FromArgb(35, 35, 38),
            Padding = new Padding(15, 0, 15, 0)
        };

        var titleLabel = new Label
        {
            Text = "🤖 SharpAgent",
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(15, 12)
        };

        _providerCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 120,
            Font = new Font("Segoe UI", 9),
            BackColor = Color.FromArgb(50, 50, 55),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(160, 13)
        };
        _providerCombo.Items.AddRange(["OpenAI", "Anthropic"]);
        _providerCombo.SelectedIndex = _configService.Config.Provider.Equals("anthropic", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _providerCombo.SelectedIndexChanged += ProviderCombo_SelectedIndexChanged;

        _settingsButton = new Button
        {
            Text = "⚙",
            Font = new Font("Segoe UI", 12),
            Size = new Size(36, 28),
            Location = new Point(290, 11),
            BackColor = Color.FromArgb(50, 50, 55),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        _settingsButton.FlatAppearance.BorderSize = 0;
        _settingsButton.Click += SettingsButton_Click;

        _statusLabel = new Label
        {
            Text = "Ready",
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.FromArgb(120, 120, 125),
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(Width - 120, 16)
        };

        _headerPanel.Controls.Add(titleLabel);
        _headerPanel.Controls.Add(_providerCombo);
        _headerPanel.Controls.Add(_settingsButton);
        _headerPanel.Controls.Add(_statusLabel);
        _headerPanel.Resize += (_, _) => _statusLabel.Location = new Point(_headerPanel.Width - _statusLabel.Width - 20, 16);

        _inputPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 70,
            BackColor = Color.FromArgb(35, 35, 38),
            Padding = new Padding(15, 12, 15, 12)
        };

        _inputBox = new TextBox
        {
            Font = new Font("Segoe UI", 11),
            BackColor = Color.FromArgb(50, 50, 55),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            Location = new Point(15, 15),
            Height = 32
        };
        _inputBox.KeyDown += InputBox_KeyDown;

        _sendButton = new Button
        {
            Text = "Send",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            BackColor = Color.FromArgb(0, 122, 255),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(80, 38),
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
            Cursor = Cursors.Hand
        };
        _sendButton.FlatAppearance.BorderSize = 0;
        _sendButton.Click += SendButton_Click;

        _stopButton = new Button
        {
            Text = "Stop",
            Font = new Font("Segoe UI", 10),
            BackColor = Color.FromArgb(200, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(60, 38),
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
            Cursor = Cursors.Hand,
            Visible = false
        };
        _stopButton.FlatAppearance.BorderSize = 0;
        _stopButton.Click += StopButton_Click;

        _inputPanel.Controls.Add(_inputBox);
        _inputPanel.Controls.Add(_sendButton);
        _inputPanel.Controls.Add(_stopButton);
        _inputPanel.Resize += InputPanel_Resize;

        _chatPanel = new ChatPanel
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None
        };

        Controls.Add(_chatPanel);
        Controls.Add(_inputPanel);
        Controls.Add(_headerPanel);

        InputPanel_Resize(null, EventArgs.Empty);

        var welcomeMsg = _configService.HasApiKey()
            ? $"Welcome to SharpAgent! Using {_configService.Config.Provider} ({_configService.GetCurrentModelName()})"
            : "Welcome to SharpAgent! Click ⚙ to configure your API keys.";
        //_chatPanel.AddSystemMessage(welcomeMsg);
    }

    private void ProviderCombo_SelectedIndexChanged(object? sender, EventArgs e)
    {
        var newProvider = _providerCombo.SelectedIndex == 1 ? "anthropic" : "openai";
        if (!_configService.Config.Provider.Equals(newProvider, StringComparison.OrdinalIgnoreCase))
        {
            _configService.Update(c => c.Provider = newProvider);
        }
    }

    private void SettingsButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new ConfigDialog(_configService);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _providerCombo.SelectedIndex = _configService.Config.Provider.Equals("anthropic", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            _chatPanel.AddSystemMessage($"Configuration updated. Using {_configService.Config.Provider} ({_configService.GetCurrentModelName()})");
        }
    }

    private void ResetAgent()
    {
        _httpClient?.Dispose();
        _httpClient = null;
        _agent = null;
    }

    private void InputPanel_Resize(object? sender, EventArgs e)
    {
        _inputBox.Width = _inputPanel.Width - 190;
        _sendButton.Location = new Point(_inputPanel.Width - 175, 15);
        _stopButton.Location = new Point(_inputPanel.Width - 85, 15);
    }

    private void InputBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && !e.Shift)
        {
            e.SuppressKeyPress = true;
            SendMessage();
        }
    }

    private void SendButton_Click(object? sender, EventArgs e)
    {
        SendMessage();
    }

    private void StopButton_Click(object? sender, EventArgs e)
    {
        _cts?.Cancel();
    }

    private async void SendMessage()
    {
        var message = _inputBox.Text.Trim();
        if (string.IsNullOrEmpty(message) || _isRunning) return;

        _inputBox.Clear();
        SetRunningState(true);

        _chatPanel.AddUserMessage(message);

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
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void SetRunningState(bool running)
    {
        _isRunning = running;
        _sendButton.Visible = !running;
        _stopButton.Visible = running;
        _providerCombo.Enabled = !running;
        _settingsButton.Enabled = !running;
        _statusLabel.Text = running ? "Processing..." : "Ready";
        _statusLabel.ForeColor = running ? Color.FromArgb(100, 180, 255) : Color.FromArgb(120, 120, 125);
    }

    private void EnsureAgentInitialized()
    {
        if (_agent != null) return;

        if (!_configService.HasApiKey())
        {
            _chatPanel.AddErrorMessage($"No API key configured for {_configService.Config.Provider}. Click ⚙ to add one.");
            return;
        }

        (_httpClient, var llmClient) = _configService.CreateLlmClient();
        _statusLabel.Text = $"{_configService.Config.Provider} ({_configService.GetCurrentModelName()})";

        var tools = new ITool[] { new CalculatorTool(), new ReadFileTool(), new ListFilesTool(), new BashTool() };
        _agent = new Agent(llmClient, tools);
    }

    private async Task ProcessAgentResponseAsync(string message, CancellationToken ct)
    {
        var textStarted = false;
        var pendingToolCalls = new Dictionary<string, string>();

        await foreach (var evt in _agent!.RunStreamingAsync(message, ct))
        {
            if (ct.IsCancellationRequested) break;

            switch (evt)
            {
                case AgentTextDeltaEvent delta:
                    if (!textStarted)
                    {
                        _chatPanel.StartAssistantMessage();
                        textStarted = true;
                    }
                    Invoke(() => _chatPanel.AppendAssistantText(delta.Text));
                    break;

                case AgentToolCallStartedEvent toolStart:
                    if (textStarted)
                    {
                        _chatPanel.FinalizeCurrentBubble();
                        textStarted = false;
                    }
                    pendingToolCalls[toolStart.ToolCallId] = toolStart.ToolName;
                    Invoke(() =>
                    {
                        _chatPanel.AddToolCall(toolStart.ToolName, toolStart.Arguments);
                        _statusLabel.Text = $"🔧 {toolStart.ToolName}";
                    });
                    break;

                case AgentToolCallCompletedEvent toolComplete:
                    Invoke(() => _chatPanel.CompleteToolCall(toolComplete.Result, toolComplete.IsError));
                    pendingToolCalls.Remove(toolComplete.ToolCallId);
                    break;

                case AgentCompletedEvent:
                    Invoke(() => _chatPanel.FinalizeCurrentBubble());
                    break;

                case AgentErrorEvent error:
                    Invoke(() => _chatPanel.AddErrorMessage(error.Message));
                    break;
            }
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cts?.Cancel();
        _httpClient?.Dispose();
        base.OnFormClosing(e);
    }
}
