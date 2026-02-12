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

public partial class MainWindowViewModel : ViewModelBase
{
    private AgentConfigurationService? _configService;
    private string _configFilePath;

    [ObservableProperty]
    private string _title = "Sharp Agent";

    [ObservableProperty]
    private bool _isInitialized;

    [ObservableProperty]
    private string? _initError;

    // --- Page navigation ---
    [ObservableProperty]
    private bool _isSettingsVisible;

    [ObservableProperty]
    private SettingsViewModel? _settings;

    // --- Model selector ---
    [ObservableProperty]
    private ObservableCollection<string> _availableModels = [];

    [ObservableProperty]
    private string? _selectedModel;

    // --- Thinking level ---
    public ThinkingLevel[] ThinkingLevels { get; } = Enum.GetValues<ThinkingLevel>();

    [ObservableProperty]
    private ThinkingLevel _selectedThinkingLevel = ThinkingLevel.Off;

    // --- Session sidebar ---
    [ObservableProperty]
    private ObservableCollection<SessionItemViewModel> _sessionItems = [];

    [ObservableProperty]
    private SessionItemViewModel? _selectedSession;

    [ObservableProperty]
    private bool _isSidebarVisible = true;

    // --- Chat ---
    public ChatViewModel Chat { get; } = new();

    public MainWindowViewModel()
    {
        _configFilePath = Path.Combine(Directory.GetCurrentDirectory(), "config.json");
        if (!File.Exists(_configFilePath))
        {
            _configFilePath = Path.Combine(AgentConfigurationService.DefaultAgentDirectory(), "config.json");
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            _configService = AgentConfigurationService.LoadFromFile();

            // Populate model list
            var models = _configService.GetAvailableModels();
            AvailableModels = new ObservableCollection<string>(models);
            SelectedModel = _configService.Config.DefaultModel;

            await CreateSessionAsync();
            await LoadSessionListAsync();

            IsInitialized = true;
        }
        catch (Exception ex)
        {
            InitError = $"Failed to initialize: {ex.Message}";
        }
    }

    // --- Settings ---

    [RelayCommand]
    private void OpenSettings()
    {
        Settings = new SettingsViewModel(_configFilePath, OnSettingsApplied);
        IsSettingsVisible = true;
    }

    [RelayCommand]
    private void CloseSettings()
    {
        IsSettingsVisible = false;
    }

    private void OnSettingsApplied(string newConfigPath)
    {
        _configFilePath = newConfigPath;
        IsSettingsVisible = false;
        IsInitialized = false;
        InitError = null;
        _ = InitializeAsync();
    }

    private async Task CreateSessionAsync()
    {
        if (_configService == null || SelectedModel == null)
            return;

        Chat.Dispose();

        var runtimeOptions = _configService.BuildRuntimeOptions(
            modelString: SelectedModel,
            thinkingLevel: SelectedThinkingLevel,
            workingDirectory: Chat.WorkspacePath);

        await Chat.InitializeAsync(runtimeOptions);
        Title = $"Sharp Agent — {runtimeOptions.Model.ProviderId}/{runtimeOptions.Model.ModelId}";
    }

    partial void OnSelectedModelChanged(string? value)
    {
        if (!IsInitialized || value == null) return;
        _ = SwitchModelAsync();
    }

    partial void OnSelectedThinkingLevelChanged(ThinkingLevel value)
    {
        if (!IsInitialized) return;
        _ = SwitchModelAsync();
    }

    partial void OnSelectedSessionChanged(SessionItemViewModel? value)
    {
        if (!IsInitialized || value == null) return;
        _ = LoadSession(value);
    }

    private async Task SwitchModelAsync()
    {
        try
        {
            InitError = null;
            await CreateSessionAsync();
        }
        catch (Exception ex)
        {
            InitError = $"Switch failed: {ex.Message}";
        }
    }

    // --- Session management ---

    [RelayCommand]
    private async Task NewSession()
    {
        try
        {
            InitError = null;
            Chat.Messages.Clear();
            await CreateSessionAsync();
            await LoadSessionListAsync();
        }
        catch (Exception ex)
        {
            InitError = $"Failed to create session: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task LoadSession(SessionItemViewModel? item)
    {
        if (item == null || _configService == null || SelectedModel == null)
            return;

        try
        {
            InitError = null;
            Chat.Dispose();
            Chat.Messages.Clear();

            var runtimeOptions = _configService.BuildRuntimeOptions(
                modelString: SelectedModel,
                thinkingLevel: SelectedThinkingLevel);

            await Chat.InitializeWithSessionAsync(runtimeOptions, item.FilePath);
            Title = $"Sharp Agent — {runtimeOptions.Model.ProviderId}/{runtimeOptions.Model.ModelId}";
        }
        catch (Exception ex)
        {
            InitError = $"Failed to load session: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarVisible = !IsSidebarVisible;
    }

    private async Task LoadSessionListAsync()
    {
        var sessionDir = AgentConfigurationService.DefaultSessionDirectory();
        if (!Directory.Exists(sessionDir))
            return;

        var items = new List<SessionItemViewModel>();

        foreach (var file in Directory.GetFiles(sessionDir, "*.jsonl").OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                var firstLine = (await File.ReadAllLinesAsync(file)).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(firstLine)) continue;

                var header = JsonSerializer.Deserialize<SessionHeader>(firstLine, JsonDefaults.Options);
                if (header == null) continue;

                items.Add(new SessionItemViewModel
                {
                    SessionId = header.SessionId,
                    FilePath = file,
                    Timestamp = header.TimestampUtc,
                    WorkingDirectory = header.WorkingDirectory
                });
            }
            catch
            {
                // Skip malformed session files
            }
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            SessionItems = new ObservableCollection<SessionItemViewModel>(items);
        });
    }
}

public class SessionItemViewModel
{
    public string SessionId { get; set; } = "";
    public string FilePath { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; }
    public string WorkingDirectory { get; set; } = "";

    public string DisplayName => $"{SessionId[..Math.Min(8, SessionId.Length)]}";
    public string DisplayTime => Timestamp.LocalDateTime.ToString("MM-dd HH:mm");
    public string DisplayDir => Path.GetFileName(WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar));
}
