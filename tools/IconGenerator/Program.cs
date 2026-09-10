using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

// IconGenerator：生成像素风 SwitchAlbum 图标（橙色大写 N + 白色像素描边），
// 输出多尺寸 ICO（16/32/48/64/128/256，PNG 压缩条目）。
// 用法: IconGenerator <输出.ico>

if (args.Length != 1)
{
    Console.Error.WriteLine("用法: IconGenerator <输出.ico>");
    return 1;
}

const int grid = 16; // 基础像素网格
var orange = Color.FromArgb(255, 109, 0); // #FF6D00
var white = Color.White;

// 1. 定义 N 字形（16x16 网格）
bool InShape(int x, int y)
{
    // 左右竖杠
    if (x is 2 or 3 or 12 or 13)
    {
        return y is >= 2 and <= 13;
    }

    // 斜杠（右上到左下，约 2 像素宽）
    if (y is >= 2 and <= 13)
    {
        var sum = x + y;
        return sum is >= 14 and <= 17 && x is >= 3 and <= 12;
    }

    return false;
}

bool IsOutline(int x, int y)
{
    if (InShape(x, y))
    {
        return false;
    }

    for (var dy = -1; dy <= 1; dy++)
    {
        for (var dx = -1; dx <= 1; dx++)
        {
            var nx = x + dx;
            var ny = y + dy;
            if (nx >= 0 && nx < grid && ny >= 0 && ny < grid && InShape(nx, ny))
            {
                return true;
            }
        }
    }

    return false;
}

// 2. 渲染基础网格并放大到各尺寸
Bitmap Render(int size)
{
    var scale = size / grid;
    var bitmap = new Bitmap(size, size);
    using var g = Graphics.FromImage(bitmap);
    g.SmoothingMode = SmoothingMode.None;
    g.InterpolationMode = InterpolationMode.NearestNeighbor;
    g.PixelOffsetMode = PixelOffsetMode.Half;

    for (var y = 0; y < grid; y++)
    {
        for (var x = 0; x < grid; x++)
        {
            Color color;
            if (InShape(x, y))
            {
                color = orange;
            }
            else if (IsOutline(x, y))
            {
                color = white;
            }
            else
            {
                continue; // 透明
            }

            using var brush = new SolidBrush(color);
            g.FillRectangle(brush, x * scale, y * scale, scale, scale);
        }
    }

    return bitmap;
}

// 3. 组装多尺寸 ICO（PNG 条目）
var sizes = new[] { 256, 128, 64, 48, 32, 16 };
var pngs = new List<byte[]>();
foreach (var size in sizes)
{
    using var bitmap = Render(size);
    using var ms = new MemoryStream();
    bitmap.Save(ms, ImageFormat.Png);
    pngs.Add(ms.ToArray());
}

using (var output = File.Create(args[0]))
using (var writer = new BinaryWriter(output))
{
    // ICONDIR
    writer.Write((ushort)0);            // reserved
    writer.Write((ushort)1);            // type: icon
    writer.Write((ushort)sizes.Length); // count

    // ICONDIRENTRY
    var offset = 6 + 16 * sizes.Length;
    for (var i = 0; i < sizes.Length; i++)
    {
        writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); // width
        writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); // height
        writer.Write((byte)0);   // colors
        writer.Write((byte)0);   // reserved
        writer.Write((ushort)1); // planes
        writer.Write((ushort)32);// bitcount
        writer.Write(pngs[i].Length);
        writer.Write(offset);
        offset += pngs[i].Length;
    }

    foreach (var png in pngs)
    {
        writer.Write(png);
    }
}

Console.WriteLine($"已生成 {args[0]}（{sizes.Length} 个尺寸）");
return 0;
