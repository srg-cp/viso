using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CapturaTutor;

public partial class MainWindow
{
    private int visibilityVersion;
    private ScaleTransform windowScale = new(1, 1);
    private TranslateTransform windowOffset = new();
    private void PrepareTransitions()
    {
        var group = new TransformGroup();
        group.Children.Add(windowScale); group.Children.Add(windowOffset);
        ShellContent.RenderTransform = group;
        ShellContent.RenderTransformOrigin = new Point(0.5, 0.8);
    }
    private void RestoreWindowVisuals()
    {
        visibilityVersion++;
        BeginAnimation(OpacityProperty, null); Opacity = 1;
        windowScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        windowScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        windowOffset.BeginAnimation(TranslateTransform.YProperty, null);
        windowScale.ScaleX = windowScale.ScaleY = 1; windowOffset.Y = 0;
    }
    private void AnimateWindowOpen()
    {
        RestoreWindowVisuals();
        if (!SystemParameters.ClientAreaAnimation) return;
        var duration = TimeSpan.FromMilliseconds(180);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = easing });
        windowScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, 1, duration) { EasingFunction = easing });
        windowScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.97, 1, duration) { EasingFunction = easing });
        windowOffset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, duration) { EasingFunction = easing });
    }
    private async Task HideWindowAnimatedAsync()
    {
        if (!IsVisible || exiting) return;
        int version = ++visibilityVersion;
        if (SystemParameters.ClientAreaAnimation)
        {
            var duration = TimeSpan.FromMilliseconds(160);
            var easing = new CubicEase { EasingMode = EasingMode.EaseIn };
            BeginAnimation(OpacityProperty, new DoubleAnimation(Opacity, 0, duration) { EasingFunction = easing });
            windowScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(windowScale.ScaleX, 0.94, duration) { EasingFunction = easing });
            windowScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(windowScale.ScaleY, 0.94, duration) { EasingFunction = easing });
            windowOffset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(windowOffset.Y, 16, duration) { EasingFunction = easing });
            await Task.Delay(175);
        }
        if (version != visibilityVersion || exiting) return;
        Hide(); RestoreWindowVisuals();
    }
}
