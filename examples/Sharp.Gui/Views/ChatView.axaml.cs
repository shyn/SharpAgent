using Avalonia.Controls;
using Avalonia.Input;
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
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        // Shift+Enter sends the message
        if (e.KeyModifiers == KeyModifiers.Shift)
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
