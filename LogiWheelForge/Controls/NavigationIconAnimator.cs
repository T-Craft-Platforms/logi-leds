using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LogiWheelForge.Controls;

public enum NavigationIconMotion
{
    Wiggle,
    FullTurn
}

public static class NavigationIconAnimator
{
    public static void PlayWiggle(FrameworkElement icon) => Play(icon, NavigationIconMotion.Wiggle);

    public static void PlayFullTurn(FrameworkElement icon) => Play(icon, NavigationIconMotion.FullTurn);

    public static void Play(FrameworkElement icon, NavigationIconMotion motion)
    {
        var rotation = new RotateTransform();
        var scale = new ScaleTransform(1, 1);
        var transform = new TransformGroup();
        transform.Children.Add(scale);
        transform.Children.Add(rotation);
        icon.RenderTransformOrigin = new System.Windows.Point(.5, .5);
        icon.RenderTransform = transform;

        var duration = TimeSpan.FromMilliseconds(motion == NavigationIconMotion.FullTurn ? 480 : 340);
        AnimationTimeline rotationAnimation;
        var scaleAnimation = CreateScaleAnimation(duration);

        if (motion == NavigationIconMotion.FullTurn)
        {
            rotationAnimation = new DoubleAnimation(0, 360, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
        }
        else
        {
            var wiggle = new DoubleAnimationUsingKeyFrames { Duration = duration };
            wiggle.KeyFrames.Add(new EasingDoubleKeyFrame(-12, KeyTime.FromPercent(0), new CubicEase { EasingMode = EasingMode.EaseOut }));
            wiggle.KeyFrames.Add(new EasingDoubleKeyFrame(4, KeyTime.FromPercent(.55), new CubicEase { EasingMode = EasingMode.EaseOut }));
            wiggle.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), new CubicEase { EasingMode = EasingMode.EaseOut }));
            rotationAnimation = wiggle;
        }

        rotation.BeginAnimation(RotateTransform.AngleProperty, rotationAnimation, HandoffBehavior.SnapshotAndReplace);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation, HandoffBehavior.SnapshotAndReplace);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, (DoubleAnimationUsingKeyFrames)scaleAnimation.Clone(),
            HandoffBehavior.SnapshotAndReplace);
    }

    private static DoubleAnimationUsingKeyFrames CreateScaleAnimation(Duration duration)
    {
        var animation = new DoubleAnimationUsingKeyFrames { Duration = duration };
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(1.10, KeyTime.FromPercent(.35), new CubicEase { EasingMode = EasingMode.EaseOut }));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1), new CubicEase { EasingMode = EasingMode.EaseOut }));
        return animation;
    }
}
