using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
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

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Auto-focus input when view loads
        this.FindControl<TextBox>("InputTextBox")?.Focus();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Unsubscribe from previous VM
        if (_currentVm != null)
        {
            _currentVm.Messages.CollectionChanged -= OnMessagesChanged;
            _currentVm.PropertyChanged -= OnVmPropertyChanged;
        }

        if (DataContext is ChatViewModel vm)
        {
            _currentVm = vm;
            _currentVm.Messages.CollectionChanged += OnMessagesChanged;
            _currentVm.PropertyChanged += OnVmPropertyChanged;

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

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // When processing starts (IsProcessing becomes true), the user has just sent a message.
        // We should focus the input box so they can immediately type the next message.
        if (e.PropertyName == nameof(ChatViewModel.IsProcessing))
        {
            if (_currentVm?.IsProcessing == true)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    this.FindControl<TextBox>("InputTextBox")?.Focus();
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
