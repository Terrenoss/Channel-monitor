using AutoStreamRec;
using AutoStreamRec.ViewModels;
using System.Windows;


namespace AutoStreamRec;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
