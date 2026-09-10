using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;
using SwitchAlbum.Resources;
using SwitchAlbum.ViewModels;

namespace SwitchAlbum.Views;

public partial class SettingsDialog : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsDialog(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void ChangePath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = Strings.Settings_PickFolder,
            InitialDirectory = Directory.Exists(_viewModel.SavePath)
                ? _viewModel.SavePath
                : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.SetSavePath(dialog.FolderName);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseWithSave();

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        CloseWithSave();
    }

    private void CloseWithSave()
    {
        if (DialogResult == null)
        {
            _viewModel.Save();
            DialogResult = true;
        }
    }
}
