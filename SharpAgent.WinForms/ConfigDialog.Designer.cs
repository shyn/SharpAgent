namespace SharpAgent.WinForms;

partial class ConfigDialog
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
        
        _mainPanel = new Panel();
        _buttonPanel = new Panel();
        _saveButton = new Button();
        _cancelButton = new Button();
        _defaultModelCombo = new ComboBox();
        _openAiKeyBox = new TextBox();
        _openAiBaseUrlBox = new TextBox();
        _anthropicKeyBox = new TextBox();
        _anthropicBaseUrlBox = new TextBox();
        
        _mainPanel.SuspendLayout();
        _buttonPanel.SuspendLayout();
        SuspendLayout();
        
        //
        // _mainPanel
        //
        _mainPanel.AutoScroll = true;
        _mainPanel.BackColor = Color.Transparent;
        _mainPanel.Dock = DockStyle.Fill;
        _mainPanel.Padding = new Padding(Theme.GutterLarge + Theme.SpacingSmall);
        
        //
        // _buttonPanel
        //
        _buttonPanel.BackColor = Theme.BackgroundSecondary;
        _buttonPanel.Controls.Add(_saveButton);
        _buttonPanel.Controls.Add(_cancelButton);
        _buttonPanel.Dock = DockStyle.Bottom;
        _buttonPanel.Height = 60;
        
        //
        // _saveButton
        //
        _saveButton.BackColor = Theme.AccentPrimary;
        _saveButton.Cursor = Cursors.Hand;
        _saveButton.FlatAppearance.BorderSize = 0;
        _saveButton.FlatStyle = FlatStyle.Flat;
        _saveButton.Font = Theme.FontMedium;
        _saveButton.ForeColor = Theme.TextPrimary;
        _saveButton.Location = new Point(310, Theme.GutterSmall);
        _saveButton.Size = Theme.ButtonSize;
        _saveButton.Text = "Save";
        
        //
        // _cancelButton
        //
        _cancelButton.BackColor = Theme.ButtonDefault;
        _cancelButton.Cursor = Cursors.Hand;
        _cancelButton.FlatAppearance.BorderSize = 0;
        _cancelButton.FlatStyle = FlatStyle.Flat;
        _cancelButton.Font = Theme.FontMedium;
        _cancelButton.ForeColor = Theme.TextPrimary;
        _cancelButton.Location = new Point(410, Theme.GutterSmall);
        _cancelButton.Size = Theme.ButtonSize;
        _cancelButton.Text = "Cancel";
        
        //
        // _defaultModelCombo
        //
        _defaultModelCombo.BackColor = Theme.BackgroundTertiary;
        _defaultModelCombo.DropDownStyle = ComboBoxStyle.DropDown;
        _defaultModelCombo.FlatStyle = FlatStyle.Flat;
        _defaultModelCombo.Font = Theme.FontRegular;
        _defaultModelCombo.ForeColor = Theme.TextPrimary;
        _defaultModelCombo.Width = 300;
        
        //
        // _openAiKeyBox
        //
        _openAiKeyBox.BackColor = Theme.BackgroundTertiary;
        _openAiKeyBox.BorderStyle = BorderStyle.FixedSingle;
        _openAiKeyBox.Font = Theme.FontRegular;
        _openAiKeyBox.ForeColor = Theme.TextPrimary;
        _openAiKeyBox.UseSystemPasswordChar = true;
        _openAiKeyBox.Width = 440;
        
        //
        // _openAiBaseUrlBox
        //
        _openAiBaseUrlBox.BackColor = Theme.BackgroundTertiary;
        _openAiBaseUrlBox.BorderStyle = BorderStyle.FixedSingle;
        _openAiBaseUrlBox.Font = Theme.FontRegular;
        _openAiBaseUrlBox.ForeColor = Theme.TextPrimary;
        _openAiBaseUrlBox.Width = 440;
        
        //
        // _anthropicKeyBox
        //
        _anthropicKeyBox.BackColor = Theme.BackgroundTertiary;
        _anthropicKeyBox.BorderStyle = BorderStyle.FixedSingle;
        _anthropicKeyBox.Font = Theme.FontRegular;
        _anthropicKeyBox.ForeColor = Theme.TextPrimary;
        _anthropicKeyBox.UseSystemPasswordChar = true;
        _anthropicKeyBox.Width = 440;
        
        //
        // _anthropicBaseUrlBox
        //
        _anthropicBaseUrlBox.BackColor = Theme.BackgroundTertiary;
        _anthropicBaseUrlBox.BorderStyle = BorderStyle.FixedSingle;
        _anthropicBaseUrlBox.Font = Theme.FontRegular;
        _anthropicBaseUrlBox.ForeColor = Theme.TextPrimary;
        _anthropicBaseUrlBox.Width = 440;
        
        //
        // ConfigDialog
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Theme.Background;
        ClientSize = new Size(520, 480);
        Controls.Add(_mainPanel);
        Controls.Add(_buttonPanel);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "ConfigDialog";
        StartPosition = FormStartPosition.CenterParent;
        Text = "Settings";
        
        _mainPanel.ResumeLayout(false);
        _buttonPanel.ResumeLayout(false);
        ResumeLayout(false);
    }

    #endregion

    private Panel _mainPanel;
    private Panel _buttonPanel;
    private Button _saveButton;
    private Button _cancelButton;
    private ComboBox _defaultModelCombo;
    private TextBox _openAiKeyBox;
    private TextBox _openAiBaseUrlBox;
    private TextBox _anthropicKeyBox;
    private TextBox _anthropicBaseUrlBox;
}
