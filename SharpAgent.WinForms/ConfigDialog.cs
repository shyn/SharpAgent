using SharpAgent.Core.Configuration;

namespace SharpAgent.WinForms;

public sealed class ConfigDialog : Form
{
    private readonly ConfigurationService _configService;
    private readonly ComboBox _providerCombo;
    private readonly TextBox _openAiKeyBox;
    private readonly TextBox _openAiBaseUrlBox;
    private readonly TextBox _openAiModelBox;
    private readonly TextBox _anthropicKeyBox;
    private readonly TextBox _anthropicBaseUrlBox;
    private readonly TextBox _anthropicModelBox;
    private readonly NumericUpDown _anthropicMaxTokensBox;

    public ConfigDialog(ConfigurationService configService)
    {
        _configService = configService;

        Text = "Settings";
        Size = new Size(500, 480);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(30, 30, 33);

        var mainPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            AutoScroll = true
        };

        var y = 10;

        AddLabel(mainPanel, "Provider:", ref y);
        _providerCombo = new ComboBox
        {
            Location = new Point(20, y),
            Width = 200,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(50, 50, 55),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _providerCombo.Items.AddRange(["openai", "anthropic"]);
        mainPanel.Controls.Add(_providerCombo);
        y += 35;

        AddSeparator(mainPanel, "OpenAI Settings", ref y);

        AddLabel(mainPanel, "API Key:", ref y);
        _openAiKeyBox = CreateTextBox(mainPanel, ref y, true);

        AddLabel(mainPanel, "Base URL:", ref y);
        _openAiBaseUrlBox = CreateTextBox(mainPanel, ref y);

        AddLabel(mainPanel, "Model:", ref y);
        _openAiModelBox = CreateTextBox(mainPanel, ref y);

        AddSeparator(mainPanel, "Anthropic Settings", ref y);

        AddLabel(mainPanel, "API Key:", ref y);
        _anthropicKeyBox = CreateTextBox(mainPanel, ref y, true);

        AddLabel(mainPanel, "Base URL:", ref y);
        _anthropicBaseUrlBox = CreateTextBox(mainPanel, ref y);

        AddLabel(mainPanel, "Model:", ref y);
        _anthropicModelBox = CreateTextBox(mainPanel, ref y);

        AddLabel(mainPanel, "Max Tokens:", ref y);
        _anthropicMaxTokensBox = new NumericUpDown
        {
            Location = new Point(20, y),
            Width = 150,
            Minimum = 1024,
            Maximum = 200000,
            BackColor = Color.FromArgb(50, 50, 55),
            ForeColor = Color.White
        };
        mainPanel.Controls.Add(_anthropicMaxTokensBox);
        y += 35;

        Controls.Add(mainPanel);

        var buttonPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            BackColor = Color.FromArgb(35, 35, 38)
        };

        var saveButton = new Button
        {
            Text = "Save",
            Size = new Size(80, 32),
            Location = new Point(Width - 200, 9),
            BackColor = Color.FromArgb(0, 122, 255),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Right
        };
        saveButton.FlatAppearance.BorderSize = 0;
        saveButton.Click += SaveButton_Click;

        var cancelButton = new Button
        {
            Text = "Cancel",
            Size = new Size(80, 32),
            Location = new Point(Width - 105, 9),
            BackColor = Color.FromArgb(60, 60, 65),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Right
        };
        cancelButton.FlatAppearance.BorderSize = 0;
        cancelButton.Click += (_, _) => Close();

        buttonPanel.Controls.Add(saveButton);
        buttonPanel.Controls.Add(cancelButton);
        Controls.Add(buttonPanel);

        LoadConfig();
    }

    private void AddLabel(Panel parent, string text, ref int y)
    {
        var label = new Label
        {
            Text = text,
            Location = new Point(20, y),
            AutoSize = true,
            ForeColor = Color.FromArgb(180, 180, 185)
        };
        parent.Controls.Add(label);
        y += 20;
    }

    private void AddSeparator(Panel parent, string title, ref int y)
    {
        y += 10;
        var label = new Label
        {
            Text = title,
            Location = new Point(20, y),
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = Color.FromArgb(100, 180, 255)
        };
        parent.Controls.Add(label);
        y += 25;
    }

    private TextBox CreateTextBox(Panel parent, ref int y, bool isPassword = false)
    {
        var box = new TextBox
        {
            Location = new Point(20, y),
            Width = 420,
            BackColor = Color.FromArgb(50, 50, 55),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        if (isPassword) box.UseSystemPasswordChar = true;
        parent.Controls.Add(box);
        y += 30;
        return box;
    }

    private void LoadConfig()
    {
        var config = _configService.Config;

        _providerCombo.SelectedItem = config.Provider;

        _openAiKeyBox.Text = config.OpenAi.ApiKey ?? "";
        _openAiBaseUrlBox.Text = config.OpenAi.BaseUrl;
        _openAiModelBox.Text = config.OpenAi.Model;

        _anthropicKeyBox.Text = config.Anthropic.ApiKey ?? "";
        _anthropicBaseUrlBox.Text = config.Anthropic.BaseUrl;
        _anthropicModelBox.Text = config.Anthropic.Model;
        _anthropicMaxTokensBox.Value = config.Anthropic.MaxTokens;
    }

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        _configService.Update(config =>
        {
            config.Provider = _providerCombo.SelectedItem?.ToString() ?? "openai";

            config.OpenAi.ApiKey = string.IsNullOrWhiteSpace(_openAiKeyBox.Text) ? null : _openAiKeyBox.Text;
            config.OpenAi.BaseUrl = _openAiBaseUrlBox.Text;
            config.OpenAi.Model = _openAiModelBox.Text;

            config.Anthropic.ApiKey = string.IsNullOrWhiteSpace(_anthropicKeyBox.Text) ? null : _anthropicKeyBox.Text;
            config.Anthropic.BaseUrl = _anthropicBaseUrlBox.Text;
            config.Anthropic.Model = _anthropicModelBox.Text;
            config.Anthropic.MaxTokens = (int)_anthropicMaxTokensBox.Value;
        });

        DialogResult = DialogResult.OK;
        Close();
    }
}
