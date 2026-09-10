using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SwitchAlbum.Controls;

/// <summary>
/// 内容切换时新内容淡入 + 上移的 ContentControl。
/// 需配合 App 资源中的隐式样式（模板内含 PART_ContentHost）。
/// </summary>
public class TransitionHost : ContentControl
{
    private FrameworkElement? _presenter;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _presenter = Template.FindName("PART_ContentHost", this) as FrameworkElement;
        AnimateIn();
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        AnimateIn();
    }

    private void AnimateIn()
    {
        if (_presenter == null || PresentationSource.FromVisual(this) == null)
        {
            return;
        }

        _presenter.BeginAnimation(OpacityProperty, null);
        var translate = new TranslateTransform { Y = 14 };
        _presenter.RenderTransform = translate;
        _presenter.Opacity = 0;

        var duration = TimeSpan.FromMilliseconds(260);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _presenter.BeginAnimation(OpacityProperty, new DoubleAnimation(1, duration) { EasingFunction = ease });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, duration) { EasingFunction = ease });
    }
}
