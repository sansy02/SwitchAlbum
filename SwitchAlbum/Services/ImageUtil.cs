using System.Drawing;
using System.Drawing.Drawing2D;

namespace SwitchAlbum.Services;

/// <summary>图片工具（仅 Windows，用于缩略图降采样与演示数据生成）。</summary>
internal static class ImageUtil
{
    public static Image Downscale(Image source, int maxDimension)
    {
        var scale = Math.Min(1.0, (double)maxDimension / Math.Max(source.Width, source.Height));
        var width = Math.Max(1, (int)(source.Width * scale));
        var height = Math.Max(1, (int)(source.Height * scale));

        var bitmap = new Bitmap(width, height);
        using var g = Graphics.FromImage(bitmap);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(source, 0, 0, width, height);
        return bitmap;
    }
}
