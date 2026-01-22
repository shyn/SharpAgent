using SharpAgent.Core.Configuration;
using System.Drawing.Drawing2D;

namespace SharpAgent.WinForms;

public sealed partial class ConfigDialog : Form
{
    private readonly ConfigurationService _configService;

    public ConfigDialog(ConfigurationService configService)
    {
        _configService = configService;

        InitializeComponent();
        
        // Wire up button events
        _saveButton.Click += SaveButton_Click;
        _cancelButton.Click += (_, _) => Close();
        
        // Add button panel border paint
        _buttonPanel.Paint += (s, e) =>
        {
            using var borderPen = new Pen(Theme.BorderSubtle, 1);
            e.Graphics.DrawLine(borderPen, 0, 0, _buttonPanel.Width, 0);
        };
        
        // Build the main panel content with dynamic layout
        BuildMainPanelContent();
        
        // Populate models and load config
        PopulateModels();
        LoadConfig();
    }

    private void BuildMainPanelContent()
    {
        var y = Theme.Gutter;

        AddLabel(_mainPanel, "Default Model:", ref y);
        _defaultModelCombo.Location = new Point(Theme.GutterLarge + Theme.SpacingSmall, y);
        _mainPanel.Controls.Add(_defaultModelCombo);
        y += Theme.GutterLarge * 2;

        AddSectionHeader(_mainPanel, "OpenAI Settings", ref y);

        AddLabel(_mainPanel, "API Key:", ref y);
        _openAiKeyBox.Location = new Point(Theme.GutterLarge + Theme.SpacingSmall, y);
        _mainPanel.Controls.Add(_openAiKeyBox);
        y += Theme.ButtonSize.Height;

        AddLabel(_mainPanel, "Base URL:", ref y);
        _openAiBaseUrlBox.Location = new Point(Theme.GutterLarge + Theme.SpacingSmall, y);
        _mainPanel.Controls.Add(_openAiBaseUrlBox);
        y += Theme.ButtonSize.Height;

        AddSectionHeader(_mainPanel, "Anthropic Settings", ref y);

        AddLabel(_mainPanel, "API Key:", ref y);
        _anthropicKeyBox.Location = new Point(Theme.GutterLarge + Theme.SpacingSmall, y);
        _mainPanel.Controls.Add(_anthropicKeyBox);
        y += Theme.ButtonSize.Height;

        AddLabel(_mainPanel, "Base URL:", ref y);
        _anthropicBaseUrlBox.Location = new Point(Theme.GutterLarge + Theme.SpacingSmall, y);
        _mainPanel.Controls.Add(_anthropicBaseUrlBox);
    }

    private void PopulateModels()
    {
        foreach (var provider in _configService.Config.Providers)
        {
            foreach (var model in provider.Models)
            {
                _defaultModelCombo.Items.Add($"{provider.Id}/{model.Id}");
            }
        }
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

    private void LoadConfig()
    {
        var config = _configService.Config;

        // Set default model
        _defaultModelCombo.Text = config.DefaultModel;

        // Load provider settings
        var openAiProvider = config.Providers.FirstOrDefault(p => p.Id == "openai");
        if (openAiProvider != null)
        {
            _openAiKeyBox.Text = openAiProvider.ApiKey ?? "";
            _openAiBaseUrlBox.Text = openAiProvider.BaseUrl;
        }

        var anthropicProvider = config.Providers.FirstOrDefault(p => p.Id == "anthropic");
        if (anthropicProvider != null)
        {
            _anthropicKeyBox.Text = anthropicProvider.ApiKey ?? "";
            _anthropicBaseUrlBox.Text = anthropicProvider.BaseUrl;
        }
    }

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        _configService.Update(config =>
        {
            config.DefaultModel = _defaultModelCombo.Text;

            var openAiProvider = config.Providers.FirstOrDefault(p => p.Id == "openai");
            if (openAiProvider != null)
            {
                openAiProvider.ApiKey = string.IsNullOrWhiteSpace(_openAiKeyBox.Text) ? null : _openAiKeyBox.Text;
                openAiProvider.BaseUrl = _openAiBaseUrlBox.Text;
            }

            var anthropicProvider = config.Providers.FirstOrDefault(p => p.Id == "anthropic");
            if (anthropicProvider != null)
            {
                anthropicProvider.ApiKey = string.IsNullOrWhiteSpace(_anthropicKeyBox.Text) ? null : _anthropicKeyBox.Text;
                anthropicProvider.BaseUrl = _anthropicBaseUrlBox.Text;
            }
        });

        DialogResult = DialogResult.OK;
        Close();
    }
}
