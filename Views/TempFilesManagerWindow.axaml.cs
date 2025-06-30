using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Threading.Tasks;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using System;
using Strivea.ViewModels;

namespace Strivea.Views
{
    public partial class TempFilesManagerWindow : Window
    {
        public TempFilesManagerWindow()
        {
            InitializeComponent();
        }

        private void OnChannelButtonClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string folderPath)
            {
                if (DataContext is MainViewModel vm && vm.DeleteTempFilesForFolderCommand != null)
                {
                    var param = (folderPath, this);
                    if (vm.DeleteTempFilesForFolderCommand.CanExecute(param))
                        vm.DeleteTempFilesForFolderCommand.Execute(param);
                }
            }
        }

        private void OnCloseClick(object? sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
} 