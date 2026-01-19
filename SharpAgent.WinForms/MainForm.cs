using SharpAgent.Core;
using SharpAgent.Core.Configuration;
using SharpAgent.Core.Streaming;
using SharpAgent.Core.Tools;
using SharpAgent.WinForms.Controls;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;

namespace SharpAgent.WinForms;

public partial class MainForm : Form
{
    private readonly ChatPanel _chatPanel;
    private readonly ModernButton _settingsButton;
    private readonly ModernButton _clearButton;
    private readonly ComboBox _providerCombo;
    private readonly Panel _headerPanel;
    private readonly Panel _inputPanel;
    private readonly Label _statusLabel;
    private readonly ModernInputArea _inputArea;
    private readonly ModernButton _stopButton;

    private readonly ConfigurationService _configService;
    private Agent? _agent;
    private HttpClient? _httpClient;
    private CancellationTokenSource? _cts;
    private bool _isRunning;
    private bool _isDisposed;
    private ThinkingConfig _thinkingConfig = ThinkingConfig.Disabled;

    public MainForm()
    {
        Text = "SharpAgent";
        Size = new Size(1000, 800);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(650, 500);
        BackColor = Theme.Background;
        
        // Enable double buffering for smooth rendering
        SetStyle(ControlStyles.OptimizedDoubleBuffer | 
                 ControlStyles.AllPaintingInWmPaint, true);

        _configService = new ConfigurationService();
        _configService.Load();
        _configService.ConfigChanged += (_, _) => ResetAgent();

        // Modern header with gradient
        _headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = Theme.HeaderHeight,
            Padding = new Padding(Theme.Gutter, 0, Theme.Gutter, 0),
            BackColor = Theme.HeaderStart
        };

        var titleLabel = new Label
        {
            Text = "🤖 SharpAgent",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            ForeColor = Theme.TextPrimary,
            AutoSize = true,
            BackColor = Color.Transparent
        };
        var titleCenterY = (Theme.HeaderHeight - titleLabel.PreferredHeight) / 2;
        titleLabel.Location = new Point(Theme.Gutter, titleCenterY);

        _providerCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            DrawMode = DrawMode.OwnerDrawFixed,
            Width = 130,
            DropDownWidth = 150,
            Font = new Font("Segoe UI", 10),
            ItemHeight = 24
        };
        _providerCombo.Items.Add("OpenAI");
        _providerCombo.Items.Add("Anthropic");
        _providerCombo.DrawItem += (sender, e) =>
        {
            // Handle the case when drawing the selected item in the edit area (e.Index == -1)
            string text;
            if (e.Index < 0)
            {
                // Drawing the selected item in the main combo area
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
        var comboCenterY = (Theme.HeaderHeight - _providerCombo.Height) / 2;
        _providerCombo.Location = new Point(titleLabel.Right +100 + Theme.SpacingSmall, comboCenterY);
        _providerCombo.SelectedIndexChanged += ProviderCombo_SelectedIndexChanged;

        _settingsButton = new ModernButton
        {
            Text = "⚙",
            Font = new Font("Segoe UI", 13),
            Size = new Size(40, 34),
            BackgroundColor = Theme.ButtonDefault,
            HoverColor = Theme.ButtonHover,
            ForeColor = Theme.TextPrimary,
            CornerRadius = 8
        };
        var settingsCenterY = (Theme.HeaderHeight - _settingsButton.Height) / 2;
        _settingsButton.Location = new Point(_providerCombo.Right + Theme.SpacingSmall, settingsCenterY);
        _settingsButton.Click += SettingsButton_Click;

        _clearButton = new ModernButton
        {
            Text = "🗑",
            Font = new Font("Segoe UI", 13),
            Size = new Size(40, 34),
            BackgroundColor = Theme.ButtonDefault,
            HoverColor = Theme.ButtonHover,
            ForeColor = Theme.TextPrimary,
            CornerRadius = 8,
            Enabled = false // Initially disabled when no messages
        };
        var clearCenterY = (Theme.HeaderHeight - _clearButton.Height) / 2;
        _clearButton.Location = new Point(_settingsButton.Right + Theme.SpacingSmall, clearCenterY);
        _clearButton.Click += ClearButton_Click;

        _statusLabel = new Label
        {
            Text = "Ready",
            Font = new Font("Segoe UI", 10),
            ForeColor = Theme.TextSecondary,
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = Color.Transparent
        };
        var statusCenterY = (Theme.HeaderHeight - _statusLabel.PreferredHeight) / 2;
        _statusLabel.Location = new Point(_headerPanel.ClientSize.Width - _statusLabel.PreferredWidth - Theme.Gutter, statusCenterY);

        _headerPanel.Controls.Add(titleLabel);
        _headerPanel.Controls.Add(_providerCombo);
        _headerPanel.Controls.Add(_settingsButton);
        _headerPanel.Controls.Add(_clearButton);
        _headerPanel.Controls.Add(_statusLabel);
        
        // Set selected index after adding to parent to ensure proper text display
        _providerCombo.SelectedIndex = _configService.Config.Provider.Equals("anthropic", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        
        _headerPanel.Resize += (_, _) =>
        {
            var centerY = (Theme.HeaderHeight - _statusLabel.PreferredHeight) / 2;
            _statusLabel.Location = new Point(_headerPanel.ClientSize.Width - _statusLabel.PreferredWidth - Theme.Gutter, centerY);
        };

        // Modern input panel
        _inputPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 120,
            BackColor = Theme.Background,
            Padding = new Padding(Theme.SpacingSmall, Theme.SpacingSmall, Theme.SpacingSmall, Theme.SpacingSmall)
        };

        _inputArea = new ModernInputArea
        {
            Dock = DockStyle.Fill
        };
        _inputArea.SendClicked += (s, e) => SendMessage();
        _inputArea.ThinkingLevelChanged += (s, level) => {
            _thinkingConfig = new ThinkingConfig { Level = level };
            ResetAgent();
            if (level != ThinkingLevel.Off)
                _chatPanel.AddSystemMessage($"Thinking set to {level} ({_thinkingConfig.BudgetTokens} tokens)");
        };
        _inputArea.FileAttachClicked += (s, e) => _chatPanel.AddSystemMessage("File selection coming soon...");
        _inputArea.ImageAttachClicked += (s, e) => _chatPanel.AddSystemMessage("Image selection coming soon...");

        _stopButton = new ModernButton
        {
            Text = "Stop",
            Font = new Font("Segoe UI Semibold", 10),
            BackgroundColor = Theme.Error,
            HoverColor = Theme.ErrorHover,
            PressedColor = Color.FromArgb(200, 50, 50),
            ForeColor = Color.White,
            Size = new Size(70, 34),
            CornerRadius = 10,
            Visible = false,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        _stopButton.Click += StopButton_Click;

        _inputPanel.Controls.Add(_inputArea);
        _inputPanel.Controls.Add(_stopButton);
        
        // Position stop button to overlay the send button area
        _inputPanel.Resize += (s, e) => 
        {
            // Position at the right side of the input panel, vertically centered in the bottom toolbar area
            var rightMargin = 20;
            var bottomMargin = 16;
            _stopButton.Location = new Point(
                _inputPanel.Width - _stopButton.Width - rightMargin,
                _inputPanel.Height - _stopButton.Height - bottomMargin
            );
        };

        _chatPanel = new ChatPanel
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None
        };
        _chatPanel.MessageCountChanged += (s, e) => _clearButton.Enabled = _chatPanel.HasMessages;

        Controls.Add(_chatPanel);
        Controls.Add(_inputPanel);
        Controls.Add(_headerPanel);
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
            //_cts?.Dispose();
            //_cts = null;
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
            _chatPanel.AddErrorMessage($"No API key configured for {_configService.Config.Provider}. Click ⚙ to add one.");
            return;
        }

        (_httpClient, var llmClient) = _configService.CreateLlmClient(_thinkingConfig);
        
        var thinkingStatus = _thinkingConfig.Enabled ? " + Thinking" : "";
        _statusLabel.Text = $"{_configService.Config.Provider} ({_configService.GetCurrentModelName()}){thinkingStatus}";

        var tools = new ITool[] { new CalculatorTool(), new ReadFileTool(), new ListFilesTool(), new BashTool(), new GlobTool(), new GrepTool() };
        _agent = new Agent(llmClient, tools);
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
        _httpClient?.Dispose();
        base.OnFormClosing(e);
    }
}

// Modern button with hover effects and rounded corners
internal class ModernButton : Control
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public Color BackgroundColor { get; set; } = Theme.AccentPrimary;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public Color HoverColor { get; set; } = Theme.AccentPrimaryHover;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public Color PressedColor { get; set; } = Theme.AccentPrimaryPressed;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public int CornerRadius { get; set; } = Theme.CornerRadiusSmall;

    private bool _isHovering;
    private bool _isPressed;

    public ModernButton()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.Selectable, true);
        DoubleBuffered = true;
        TabStop = true;
        Cursor = Cursors.Hand;
        Size = Theme.ButtonSize;
    }

    protected override bool IsInputKey(Keys keyData)
    {
        return keyData is Keys.Space or Keys.Enter || base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Enabled && e.KeyCode is Keys.Space or Keys.Enter)
        {
            _isPressed = true;
            Invalidate();
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (Enabled && e.KeyCode is Keys.Space or Keys.Enter)
        {
            _isPressed = false;
            Invalidate();
            OnClick(EventArgs.Empty);
        }
        base.OnKeyUp(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var color = _isPressed ? PressedColor : (_isHovering ? HoverColor : BackgroundColor);
        if (!Enabled) color = Theme.BackgroundTertiary;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = CreateRoundedRectangle(rect, CornerRadius);
        using var brush = new SolidBrush(color);

        e.Graphics.FillPath(brush, path);

        // Draw focus ring when focused
        if (Focused && Enabled)
        {
            var focusRect = new Rectangle(1, 1, Width - 3, Height - 3);
            using var focusPath = CreateRoundedRectangle(focusRect, CornerRadius - 1);
            using var focusPen = new Pen(Theme.FocusRing, 2);
            e.Graphics.DrawPath(focusPen, focusPath);
        }

        // Draw text
        var textColor = Enabled ? ForeColor : Theme.TextDisabled;
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _isHovering = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _isHovering = false;
        _isPressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (Enabled)
        {
            _isPressed = true;
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _isPressed = false;
        Invalidate();
        base.OnMouseUp(e);
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

internal class RoundedTextBox : Control
{
    private readonly TextBox _innerTextBox = null!;
    
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public Color BorderColor { get; set; } = Theme.BorderSubtle;
    
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public Color FocusBorderColor { get; set; } = Theme.FocusRing;
    
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public int CornerRadius { get; set; } = Theme.CornerRadius;
    
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public string PlaceholderText { get; set; } = "";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public bool Multiline
    {
        get => _innerTextBox?.Multiline ?? false;
        set { if (_innerTextBox != null) { _innerTextBox.Multiline = value; UpdateInnerTextBoxLayout(); } }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [Browsable(false)]
    public bool AcceptsReturn
    {
        get => _innerTextBox?.AcceptsReturn ?? false;
        set { if (_innerTextBox != null) _innerTextBox.AcceptsReturn = value; }
    }

    private bool _isFocused;

    public RoundedTextBox()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | 
                 ControlStyles.AllPaintingInWmPaint | 
                 ControlStyles.UserPaint, true);
        DoubleBuffered = true;

        _innerTextBox = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = BackColor,
            ForeColor = ForeColor,
            Font = Font,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom
        };

        _innerTextBox.GotFocus += (s, e) => { _isFocused = true; Invalidate(); };
        _innerTextBox.LostFocus += (s, e) => { _isFocused = false; Invalidate(); };
        _innerTextBox.TextChanged += (s, e) => { OnTextChanged(e); Invalidate(); };

        Controls.Add(_innerTextBox);
        
        Height = 48;
        UpdateInnerTextBoxLayout();
    }

    [AllowNull]
    public override string Text
    {
        get => _innerTextBox?.Text ?? string.Empty;
        set { if (_innerTextBox != null) _innerTextBox.Text = value ?? string.Empty; }
    }

    [AllowNull]
    public override Font Font
    {
        get => base.Font;
        set
        {
            base.Font = value!;
            if (_innerTextBox != null)
            {
                _innerTextBox.Font = value!;
                UpdateInnerTextBoxLayout();
            }
        }
    }

    public override Color BackColor
    {
        get => base.BackColor;
        set
        {
            base.BackColor = value;
            if (_innerTextBox != null) _innerTextBox.BackColor = value;
        }
    }

    public override Color ForeColor
    {
        get => base.ForeColor;
        set
        {
            base.ForeColor = value;
            if (_innerTextBox != null) _innerTextBox.ForeColor = value;
        }
    }

    public void Clear() => _innerTextBox?.Clear();

    public new event KeyEventHandler? KeyDown
    {
        add => _innerTextBox.KeyDown += value;
        remove => _innerTextBox.KeyDown -= value;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateInnerTextBoxLayout();
    }

    private void UpdateInnerTextBoxLayout()
    {
        if (_innerTextBox == null) return;
        
        var padding = Theme.SpacingSmall;
        if (_innerTextBox.Multiline)
        {
            _innerTextBox.Location = new Point(CornerRadius + padding, padding);
            _innerTextBox.Size = new Size(
                Math.Max(1, Width - (CornerRadius * 2) - (padding * 2)),
                Math.Max(1, Height - (padding * 2)));
        }
        else
        {
            var verticalPadding = (Height - _innerTextBox.PreferredHeight) / 2;
            _innerTextBox.Location = new Point(CornerRadius + padding, Math.Max(1, verticalPadding));
            _innerTextBox.Width = Math.Max(1, Width - (CornerRadius * 2) - (padding * 2));
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = CreateRoundedRectangle(rect, CornerRadius);
        using var brush = new SolidBrush(BackColor);
        
        e.Graphics.FillPath(brush, path);

        using var borderPen = new Pen(_isFocused ? FocusBorderColor : BorderColor, _isFocused ? 2f : 1f);
        e.Graphics.DrawPath(borderPen, path);

        if (string.IsNullOrEmpty(_innerTextBox.Text) && !_isFocused && !string.IsNullOrEmpty(PlaceholderText))
        {
            var placeholderRect = new Rectangle(CornerRadius + Theme.SpacingSmall, 0, Width - CornerRadius * 2 - Theme.Gutter, Height);
            TextRenderer.DrawText(e.Graphics, PlaceholderText, Font, placeholderRect,
                Theme.TextMuted, _innerTextBox.Multiline ? TextFormatFlags.Default : TextFormatFlags.VerticalCenter);
        }
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
