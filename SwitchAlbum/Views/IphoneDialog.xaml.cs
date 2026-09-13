using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using QRCoder;

namespace SwitchAlbum.Views;

public partial class IphoneDialog : Window
{
    private readonly string _url;

    public IphoneDialog(string url)
    {
        InitializeComponent();
        _url = url;
        UrlText.Text = url;
        QrImage.Source = GenerateQr(url);
    }

    private static BitmapImage GenerateQr(string content)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        using var qr = new PngByteQRCode(data);
        var png = qr.GetGraphic(12);

        var image = new BitmapImage();
        using var stream = new MemoryStream(png);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_url);
        }
        catch
        {
            // 剪贴板占用时忽略
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
