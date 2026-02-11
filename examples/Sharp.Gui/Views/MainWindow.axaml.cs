using Avalonia.Controls;

namespace Sharp.Gui.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (DataContext is ViewModels.MainWindowViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }
}