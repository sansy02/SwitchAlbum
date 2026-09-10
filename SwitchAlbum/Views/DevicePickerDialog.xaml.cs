using System.Windows;
using System.Windows.Controls;
using SwitchAlbum.Models;

namespace SwitchAlbum.Views;

public partial class DevicePickerDialog : Window
{
    public DevicePickerDialog(IReadOnlyList<MediaDeviceInfo> devices)
    {
        InitializeComponent();
        Devices = devices;
        DeviceList.ItemsSource = Devices;
        NoneHint.Visibility = Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (Devices.Count > 0)
        {
            DeviceList.SelectedIndex = 0;
        }
    }

    public IReadOnlyList<MediaDeviceInfo> Devices { get; }

    public MediaDeviceInfo? SelectedDevice { get; private set; }

    private void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectedDevice = DeviceList.SelectedItem as MediaDeviceInfo;
        OkButton.IsEnabled = SelectedDevice != null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDevice != null)
        {
            DialogResult = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
