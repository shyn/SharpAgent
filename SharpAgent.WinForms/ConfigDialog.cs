using SharpAgent.Core.Configuration;
using System.Drawing.Drawing2D;

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
        Size = new Size(520, 540);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Theme.Background;

        var mainPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(Theme.GutterLarge + Theme.SpacingSmall),
            AutoScroll = true,
            BackColor = Color.Transparent
        };

        var y = Theme.Gutter;

        AddLabel(mainPanel, "Provider:", ref y);
        _providerCombo = new ComboBox
        {
            Location = new Point(Theme.GutterLarge + Theme.SpacingSmall, y),
            Width = 220,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Theme.BackgroundTertiary,
            ForeColor = Theme.TextPrimary,
            FlatStyle = FlatStyle.Flat,
            Font = Theme.FontRegular
        };
        _providerCombo.Items.AddRange(["openai", "anthropic"]);
        mainPanel.Controls.Add(_providerCombo);
        y += Theme.GutterLarge * 2;

        AddSectionHeader(mainPanel, "OpenAI Settings", ref y);

        AddLabel(mainPanel, "API Key:", ref y);
        _openAiKeyBox = CreateTextBox(mainPanel, ref y, true);

        AddLabel(mainPanel, "Base URL:", ref y);
        _openAiBaseUrlBox = CreateTextBox(mainPanel, ref y);

        AddLabel(mainPanel, "Model:", ref y);
        _openAiModelBox = CreateTextBox(mainPanel, ref y);

        AddSectionHeader(mainPanel, "Anthropic Settings", ref y);

        AddLabel(mainPanel, "API Key:", ref y);
        _anthropicKeyBox = CreateTextBox(mainPanel, ref y, true);

        AddLabel(mainPanel, "Base URL:", ref y);
        _anthropicBaseUrlBox = CreateTextBox(mainPanel, ref y);

        AddLabel(mainPanel, "Model:", ref y);
        _anthropicModelBox = CreateTextBox(mainPanel, ref y);

        AddLabel(mainPanel, "Max Tokens:", ref y);
        _anthropicMaxTokensBox = new NumericUpDown
        {
            Location = new Point(Theme.GutterLarge + Theme.SpacingSmall, y),
            Width = 180,
            Minimum = 1024,
            Maximum = 200000,
            BackColor = Theme.BackgroundTertiary,
            ForeColor = Theme.TextPrimary,
            Font = Theme.FontRegular,
            BorderStyle = BorderStyle.FixedSingle
        };
        mainPanel.Controls.Add(_anthropicMaxTokensBox);
        y += Theme.GutterLarge * 2;

        Controls.Add(mainPanel);

        var buttonPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 60,
            BackColor = Theme.BackgroundSecondary
        };
        
        buttonPanel.Paint += (s, e) =>
        {
            using var borderPen = new Pen(Theme.BorderSubtle, 1);
            e.Graphics.DrawLine(borderPen, 0, 0, buttonPanel.Width, 0);
        };

        var saveButton = CreateModernButton("Save", Theme.AccentPrimary, Width - 210, Theme.GutterSmall);
        saveButton.Click += SaveButton_Click;

        var cancelButton = CreateModernButton("Cancel", Theme.ButtonDefault, Width - 110, Theme.GutterSmall);
        cancelButton.Click += (_, _) => Close();

        buttonPanel.Controls.Add(saveButton);
        buttonPanel.Controls.Add(cancelButton);
        Controls.Add(buttonPanel);

        LoadConfig();
    }

    private static Button CreateModernButton(string text, Color bgColor, int x, int y)
    {
        var btn = new Button
        {
            Text = text,
            Size = Theme.ButtonSize,
            Location = new Point(x, y),
            BackColor = bgColor,
            ForeColor = Theme.TextPrimary,
            FlatStyle = FlatStyle.Flat,
            Font = Theme.FontMedium,
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }

    private void AddLabel(Panel parent, string text, ref int y)
    {
        var label = new Label
        {
            Text = text,
            Location = new Point(Theme.GutterLarge + Theme.SpacingSmall, y),
            AutoSize = true,
            ForeColor = Theme.TextSecondary,
            Font = Theme.FontRegular
        };
        parent.Controls.Add(label);
        y += 22;
    }

    private void AddSectionHeader(Panel parent, string title, ref int y)
    {
        y += Theme.SpacingSmall;

        var headerPanel = new Panel
        {
            Location = new Point(Theme.GutterSmall, y),
            Size = new Size(parent.Width - Theme.GutterLarge * 2, 32),
            BackColor = Theme.BackgroundSecondary
        };
        headerPanel.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(Theme.BackgroundSecondary);
            using var path = CreateRoundedRectPath(0, 0, headerPanel.Width, headerPanel.Height, Theme.CornerRadiusSmall);
            e.Graphics.FillPath(brush, path);
        };

        var label = new Label
        {
            Text = title,
            Location = new Point(Theme.GutterSmall, 6),
            AutoSize = true,
            Font = Theme.FontMedium,
            ForeColor = Theme.FocusRing,
            BackColor = Color.Transparent
        };
        headerPanel.Controls.Add(label);
        parent.Controls.Add(headerPanel);
        y += 32 + Theme.Gutter;
    }

    private static GraphicsPath CreateRoundedRectPath(int x, int y, int width, int height, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(x, y, diameter, diameter, 180, 90);
        path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
        path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
        path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private TextBox CreateTextBox(Panel parent, ref int y, bool isPassword = false)
    {
        var box = new TextBox
        {
            Location = new Point(Theme.GutterLarge + Theme.SpacingSmall, y),
            Width = 440,
            BackColor = Theme.BackgroundTertiary,
            ForeColor = Theme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = Theme.FontRegular
        };
        if (isPassword) box.UseSystemPasswordChar = true;
        parent.Controls.Add(box);
        y += Theme.ButtonSize.Height;
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
        
        // Safely set MaxTokens value within valid range
        var maxTokens = config.Anthropic.MaxTokens;
        if (maxTokens < _anthropicMaxTokensBox.Minimum)
            maxTokens = (int)_anthropicMaxTokensBox.Minimum;
        if (maxTokens > _anthropicMaxTokensBox.Maximum)
            maxTokens = (int)_anthropicMaxTokensBox.Maximum;
        _anthropicMaxTokensBox.Value = maxTokens;
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
