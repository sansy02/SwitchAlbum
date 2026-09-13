namespace SwitchAlbum.Services;

/// <summary>窗口位置自愈：判断保存的窗口边界在当前虚拟屏上是否仍有可交互的可见区域。</summary>
public static class WindowBounds
{
    /// <summary>
    /// 要求窗口与虚拟屏至少有 minVisibleWidth×minVisibleHeight 的交集
    /// （保证标题栏可见可拖动；显示器布局变化后越界窗口由此判定为不可见）。
    /// </summary>
    public static bool IsVisibleOnScreen(
        double left, double top, double width, double height,
        double virtualLeft, double virtualTop, double virtualWidth, double virtualHeight,
        double minVisibleWidth = 100, double minVisibleHeight = 40)
    {
        if (width <= 0 || height <= 0 || virtualWidth <= 0 || virtualHeight <= 0)
        {
            return false;
        }

        var overlapW = Math.Min(left + width, virtualLeft + virtualWidth) - Math.Max(left, virtualLeft);
        var overlapH = Math.Min(top + height, virtualTop + virtualHeight) - Math.Max(top, virtualTop);
        return overlapW >= minVisibleWidth && overlapH >= minVisibleHeight;
    }
}
