using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Sharp.Gui.ViewModels;

namespace Sharp.Gui.Views;

public partial class ChatView : UserControl
{
    public ChatView()
    {
        InitializeComponent();

        var textBox = this.FindControl<TextBox>("InputTextBox");
        if (textBox != null)
        {
            textBox.KeyDown += OnInputKeyDown;
        }

        this.DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ChatViewModel vm)
        {
            if (vm.BrowseWorkspaceInteraction is FolderBrowserInteraction interaction)
            {
                interaction.RegisterHandler(async initialPath =>
                {
                    var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
                    if (storage == null) return null;

                    var folder = await storage.TryGetFolderFromPathAsync(initialPath ?? Directory.GetCurrentDirectory());
                    var result = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
                    {
                        Title = "Select Workspace Folder",
                        SuggestedStartLocation = folder,
                        AllowMultiple = false
                    });

                    return result.Count > 0 ? result[0].Path.LocalPath : null;
                });
            }
        }
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        // Shift+Enter or Command/Ctrl+Enter sends the message
        if (e.KeyModifiers == KeyModifiers.Shift ||
            e.KeyModifiers == KeyModifiers.Meta ||
            e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;

            if (DataContext is ChatViewModel vm && vm.SendCommand.CanExecute(null))
            {
                vm.SendCommand.Execute(null);
            }
        }
        // Regular Enter inserts newline (default behavior with AcceptsReturn=True)
        // No need to handle it specially
    }
}
