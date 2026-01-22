namespace SharpAgent.WinForms;

partial class MainForm
{
    /// <summary>
    /// Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    /// Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        this.components = new System.ComponentModel.Container();
        
        _headerPanel = new Panel();
        _titleLabel = new Label();
        _providerCombo = new ComboBox();
        _settingsButton = new Controls.ModernButton();
        _clearButton = new Controls.ModernButton();
        _statusLabel = new Label();
        _inputPanel = new Panel();
        _inputArea = new Controls.ModernInputArea();
        _stopButton = new Controls.ModernButton();
        _chatPanel = new Controls.ChatPanel();
        
        _headerPanel.SuspendLayout();
        _inputPanel.SuspendLayout();
        SuspendLayout();
        
        //
        // _headerPanel
        //
        _headerPanel.BackColor = Theme.HeaderStart;
        _headerPanel.Controls.Add(_titleLabel);
        _headerPanel.Controls.Add(_providerCombo);
        _headerPanel.Controls.Add(_settingsButton);
        _headerPanel.Controls.Add(_clearButton);
        _headerPanel.Controls.Add(_statusLabel);
        _headerPanel.Dock = DockStyle.Top;
        _headerPanel.Height = Theme.HeaderHeight;
        _headerPanel.Padding = new Padding(Theme.Gutter, 0, Theme.Gutter, 0);
        
        //
        // _titleLabel
        //
        _titleLabel.AutoSize = true;
        _titleLabel.BackColor = Color.Transparent;
        _titleLabel.Font = new Font("Segoe UI", 16, FontStyle.Bold);
        _titleLabel.ForeColor = Theme.TextPrimary;
        _titleLabel.Text = "🤖 SharpAgent";
        
        //
        // _providerCombo
        //
        _providerCombo.DrawMode = DrawMode.OwnerDrawFixed;
        _providerCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _providerCombo.DropDownWidth = 150;
        _providerCombo.Font = new Font("Segoe UI", 10);
        _providerCombo.ItemHeight = 24;
        _providerCombo.Width = 130;
        
        //
        // _settingsButton
        //
        _settingsButton.BackgroundColor = Theme.ButtonDefault;
        _settingsButton.CornerRadius = 8;
        _settingsButton.Font = new Font("Segoe UI", 13);
        _settingsButton.ForeColor = Theme.TextPrimary;
        _settingsButton.HoverColor = Theme.ButtonHover;
        _settingsButton.Size = new Size(40, 34);
        _settingsButton.Text = "⚙";
        
        //
        // _clearButton
        //
        _clearButton.BackgroundColor = Theme.ButtonDefault;
        _clearButton.CornerRadius = 8;
        _clearButton.Enabled = false;
        _clearButton.Font = new Font("Segoe UI", 13);
        _clearButton.ForeColor = Theme.TextPrimary;
        _clearButton.HoverColor = Theme.ButtonHover;
        _clearButton.Size = new Size(40, 34);
        _clearButton.Text = "🗑";
        
        //
        // _statusLabel
        //
        _statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _statusLabel.AutoSize = true;
        _statusLabel.BackColor = Color.Transparent;
        _statusLabel.Font = new Font("Segoe UI", 10);
        _statusLabel.ForeColor = Theme.TextSecondary;
        _statusLabel.Text = "Ready";
        
        //
        // _inputPanel
        //
        _inputPanel.BackColor = Theme.Background;
        _inputPanel.Controls.Add(_inputArea);
        _inputPanel.Controls.Add(_stopButton);
        _inputPanel.Dock = DockStyle.Bottom;
        _inputPanel.Height = 120;
        _inputPanel.Padding = new Padding(Theme.SpacingSmall);
        
        //
        // _inputArea
        //
        _inputArea.Dock = DockStyle.Fill;
        
        //
        // _stopButton
        //
        _stopButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _stopButton.BackgroundColor = Theme.Error;
        _stopButton.CornerRadius = 10;
        _stopButton.Font = new Font("Segoe UI Semibold", 10);
        _stopButton.ForeColor = Color.White;
        _stopButton.HoverColor = Theme.ErrorHover;
        _stopButton.PressedColor = Color.FromArgb(200, 50, 50);
        _stopButton.Size = new Size(70, 34);
        _stopButton.Text = "Stop";
        _stopButton.Visible = false;
        
        //
        // _chatPanel
        //
        _chatPanel.BorderStyle = BorderStyle.None;
        _chatPanel.Dock = DockStyle.Fill;
        
        //
        // MainForm
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Theme.Background;
        ClientSize = new Size(1000, 800);
        Controls.Add(_chatPanel);
        Controls.Add(_inputPanel);
        Controls.Add(_headerPanel);
        MinimumSize = new Size(650, 500);
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "SharpAgent";
        
        _headerPanel.ResumeLayout(false);
        _headerPanel.PerformLayout();
        _inputPanel.ResumeLayout(false);
        ResumeLayout(false);
    }

    #endregion

    private Panel _headerPanel;
    private Label _titleLabel;
    private ComboBox _providerCombo;
    private Controls.ModernButton _settingsButton;
    private Controls.ModernButton _clearButton;
    private Label _statusLabel;
    private Panel _inputPanel;
    private Controls.ModernInputArea _inputArea;
    private Controls.ModernButton _stopButton;
    private Controls.ChatPanel _chatPanel;
}
