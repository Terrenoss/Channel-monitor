using Avalonia.Controls;
using Strivea.ViewModels;
using Avalonia.Interactivity;

namespace Strivea.Views;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void ToggleTempSectionVisibility(object sender, RoutedEventArgs e)
    {
        if (DataContext is Strivea.ViewModels.MainViewModel vm)
        {
            vm.ToggleTempSectionVisibilityCommand.Execute(null);
        }
    }
}
