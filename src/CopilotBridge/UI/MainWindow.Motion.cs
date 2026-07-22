using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CopilotBridge.UI;

public partial class MainWindow
{
    private bool _motionInteractionsInitialized;

    private void InitializeMotionInteractions()
    {
        if (_motionInteractionsInitialized) return;
        _motionInteractionsInitialized = true;

        foreach (var button in VisualDescendants<Button>(this))
        {
            button.MouseEnter += MotionButton_MouseEnter;
            button.MouseLeave += MotionButton_MouseLeave;
            button.PreviewMouseLeftButtonDown += MotionButton_PreviewMouseLeftButtonDown;
            button.PreviewMouseLeftButtonUp += MotionButton_PreviewMouseLeftButtonUp;
            button.PreviewKeyDown += MotionButton_PreviewKeyDown;
            button.PreviewKeyUp += MotionButton_PreviewKeyUp;
            button.IsEnabledChanged += MotionButton_IsEnabledChanged;
        }

        foreach (var toggle in VisualDescendants<CheckBox>(this))
        {
            toggle.Checked += MotionToggle_Changed;
            toggle.Unchecked += MotionToggle_Changed;
            UpdateToggleThumb(toggle, animate: false);
        }

        ResetNoticeMotion();
    }

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in VisualDescendants<T>(child)) yield return descendant;
        }
    }

    private static ScaleTransform ButtonScale(Button button)
    {
        button.RenderTransformOrigin = new Point(0.5, 0.5);
        if (button.RenderTransform is ScaleTransform scale) return scale;
        scale = new ScaleTransform(1, 1);
        button.RenderTransform = scale;
        return scale;
    }

    private static void MotionButton_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not Button { IsEnabled: true } button) return;
        if (NativeMotionPolicy.IsEnabledFor(SystemParameters.ClientAreaAnimation))
            AnimateMotionValue(button, OpacityProperty, 0.96, NativeMotionPolicy.ButtonHoverDurationMs);
        else
            SetMotionValue(button, OpacityProperty, 1);
    }

    private static void MotionButton_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is not Button button) return;
        AnimateMotionValue(button, OpacityProperty, 1, NativeMotionPolicy.ButtonReleaseDurationMs);
        AnimateButtonScale(button, 1, NativeMotionPolicy.ButtonReleaseDurationMs);
    }

    private static void MotionButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button { IsEnabled: true } button && NativeMotionPolicy.IsEnabledFor(SystemParameters.ClientAreaAnimation))
            AnimateButtonScale(button, 0.98, NativeMotionPolicy.ButtonPressDurationMs);
    }

    private static void MotionButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button button) AnimateButtonScale(button, 1, NativeMotionPolicy.ButtonReleaseDurationMs);
    }

    private static void MotionButton_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is Button { IsEnabled: true } button &&
            e.Key is Key.Space or Key.Enter &&
            NativeMotionPolicy.IsEnabledFor(SystemParameters.ClientAreaAnimation))
            AnimateButtonScale(button, 0.98, NativeMotionPolicy.ButtonPressDurationMs);
    }

    private static void MotionButton_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (sender is Button button && e.Key is Key.Space or Key.Enter)
            AnimateButtonScale(button, 1, NativeMotionPolicy.ButtonReleaseDurationMs);
    }

    private static void MotionButton_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not Button button || button.IsEnabled) return;
        SetMotionValue(button, OpacityProperty, 1);
        SetButtonScale(button, 1);
    }

    private static void AnimateButtonScale(Button button, double value, int durationMilliseconds)
    {
        var scale = ButtonScale(button);
        AnimateMotionValue(scale, ScaleTransform.ScaleXProperty, value, durationMilliseconds);
        AnimateMotionValue(scale, ScaleTransform.ScaleYProperty, value, durationMilliseconds);
    }

    private static void SetButtonScale(Button button, double value)
    {
        var scale = ButtonScale(button);
        SetMotionValue(scale, ScaleTransform.ScaleXProperty, value);
        SetMotionValue(scale, ScaleTransform.ScaleYProperty, value);
    }

    private static void MotionToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox toggle) UpdateToggleThumb(toggle, animate: true);
    }

    private static void UpdateToggleThumb(CheckBox toggle, bool animate)
    {
        toggle.ApplyTemplate();
        if (toggle.Template?.FindName("Thumb", toggle) is not FrameworkElement thumb) return;
        var translate = thumb.RenderTransform as TranslateTransform ?? new TranslateTransform();
        thumb.RenderTransform = translate;
        var target = toggle.IsChecked == true ? NativeMotionPolicy.ToggleTravel : 0;
        if (animate)
            AnimateMotionValue(translate, TranslateTransform.XProperty, target, NativeMotionPolicy.ToggleDurationMs);
        else
            SetMotionValue(translate, TranslateTransform.XProperty, target);
    }

    private void AnimateNoticeEntrance()
    {
        var translate = NoticeBorder.RenderTransform as TranslateTransform ?? new TranslateTransform();
        NoticeBorder.RenderTransform = translate;
        AnimateMotionFrom(NoticeBorder, OpacityProperty, 0, 1, NativeMotionPolicy.NoticeDurationMs);
        AnimateMotionFrom(translate, TranslateTransform.YProperty, -6, 0, NativeMotionPolicy.NoticeDurationMs);
    }

    private void ResetNoticeMotion()
    {
        var translate = NoticeBorder.RenderTransform as TranslateTransform ?? new TranslateTransform();
        NoticeBorder.RenderTransform = translate;
        SetMotionValue(NoticeBorder, OpacityProperty, 1);
        SetMotionValue(translate, TranslateTransform.YProperty, 0);
    }

    private static void AnimateMotionValue(DependencyObject target, DependencyProperty property, double targetValue, int durationMilliseconds)
    {
        var current = (double)target.GetValue(property);
        StopMotion(target, property);
        target.SetValue(property, targetValue);
        if (!NativeMotionPolicy.IsEnabledFor(SystemParameters.ClientAreaAnimation) || Math.Abs(current - targetValue) < 0.001) return;
        BeginMotion(target, property, new DoubleAnimation(current, targetValue, TimeSpan.FromMilliseconds(durationMilliseconds))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        });
    }

    private static void AnimateMotionFrom(DependencyObject target, DependencyProperty property, double from, double targetValue, int durationMilliseconds)
    {
        StopMotion(target, property);
        target.SetValue(property, targetValue);
        if (!NativeMotionPolicy.IsEnabledFor(SystemParameters.ClientAreaAnimation)) return;
        BeginMotion(target, property, new DoubleAnimation(from, targetValue, TimeSpan.FromMilliseconds(durationMilliseconds))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        });
    }

    private static void SetMotionValue(DependencyObject target, DependencyProperty property, double value)
    {
        StopMotion(target, property);
        target.SetValue(property, value);
    }

    private static void BeginMotion(DependencyObject target, DependencyProperty property, AnimationTimeline animation)
    {
        if (target is UIElement element) element.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
        else if (target is Animatable animatable) animatable.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void StopMotion(DependencyObject target, DependencyProperty property)
    {
        if (target is UIElement element) element.BeginAnimation(property, null);
        else if (target is Animatable animatable) animatable.BeginAnimation(property, null);
    }
}

internal static class NativeMotionPolicy
{
    internal const int ButtonPressDurationMs = 90;
    internal const int ButtonHoverDurationMs = 120;
    internal const int ButtonReleaseDurationMs = 140;
    internal const int ToggleDurationMs = 160;
    internal const int NoticeDurationMs = 180;
    internal const double ToggleTravel = 20;

    internal static bool IsEnabledFor(bool clientAreaAnimation) => clientAreaAnimation;
}
