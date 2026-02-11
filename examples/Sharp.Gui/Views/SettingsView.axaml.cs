using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Sharp.Gui.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ViewModels.SettingsViewModel vm)
        {
            vm.BrowseFileAsync = BrowseForConfigFileAsync;
        }
    }

    private async Task<string?> BrowseForConfigFileAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Config File",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("JSON Files") { Patterns = ["*.json"] },
                new FilePickerFileType("All Files") { Patterns = ["*"] }
            ]
        });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }
}
