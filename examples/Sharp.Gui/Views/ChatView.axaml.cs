using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Sharp.Gui.ViewModels;

namespace Sharp.Gui.Views;

public partial class ChatView : UserControl
{
    private ChatViewModel? _currentVm;

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
        // Unsubscribe from previous VM
        if (_currentVm != null)
        {
            _currentVm.Messages.CollectionChanged -= OnMessagesChanged;
        }

        if (DataContext is ChatViewModel vm)
        {
            _currentVm = vm;
            _currentVm.Messages.CollectionChanged += OnMessagesChanged;

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
        else
        {
            _currentVm = null;
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add || e.Action == NotifyCollectionChangedAction.Reset)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var scrollViewer = this.FindControl<ScrollViewer>("MessageScrollViewer");
                scrollViewer?.ScrollToEnd();
            });
        }
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        // Shift+Enter inserts newline (default behavior)
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            return;

        // Regular Enter (or Ctrl/Meta+Enter) sends the message
        e.Handled = true;

        if (DataContext is ChatViewModel vm && vm.SendCommand.CanExecute(null))
        {
            vm.SendCommand.Execute(null);
        }
    }
}
