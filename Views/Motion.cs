using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;

namespace TraySport.Views;

/// <summary>
/// Small composition animations for loading and arriving content: a staggered skeleton pulse, a
/// fade-and-rise when content appears, a fade when a placeholder leaves, a spinning glyph, and a
/// card flip. All are short and independent (no UI-thread timers), and all are skipped when the
/// user turned off animation effects in Windows.
/// </summary>
/// <remarks>
/// A public DependencyObject rather than a static class: attached properties set to literals in
/// XAML ("True") go through the XAML parser, which only finds types it has metadata for.
/// </remarks>
public sealed partial class Motion : DependencyObject
{
    private static readonly UISettings s_uiSettings = new();

    private static readonly TimeSpan PulseDuration = TimeSpan.FromMilliseconds(1400);
    private static readonly TimeSpan PulseStagger = TimeSpan.FromMilliseconds(140);
    private static readonly TimeSpan RevealDuration = TimeSpan.FromMilliseconds(320);
    private static readonly TimeSpan PlaceholderFadeDuration = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan SpinDuration = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan FlipHalf = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan FlipSettle = TimeSpan.FromMilliseconds(380);
    private const float RevealRise = 8;

    /// <summary>Settings > Accessibility > Visual effects > Animation effects.</summary>
    public static bool IsEnabled => s_uiSettings.AnimationsEnabled;

    // Pulse: a placeholder breathes while it's visible, offset by its index so a list of them ripples.
    public static readonly DependencyProperty PulseIndexProperty = DependencyProperty.RegisterAttached(
        "PulseIndex", typeof(int), typeof(Motion), new PropertyMetadata(-1, OnPulseIndexChanged));

    public static int GetPulseIndex(UIElement element) => (int)element.GetValue(PulseIndexProperty);

    public static void SetPulseIndex(UIElement element, int value) => element.SetValue(PulseIndexProperty, value);

    // RevealOnShow: content fades in and rises a few pixels each time it goes from collapsed to visible.
    public static readonly DependencyProperty RevealOnShowProperty = DependencyProperty.RegisterAttached(
        "RevealOnShow", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnRevealOnShowChanged));

    public static bool GetRevealOnShow(UIElement element) => (bool)element.GetValue(RevealOnShowProperty);

    public static void SetRevealOnShow(UIElement element, bool value) => element.SetValue(RevealOnShowProperty, value);

    // FadeOnHide: a placeholder fades out instead of vanishing, so it crossfades with what replaces it.
    public static readonly DependencyProperty FadeOnHideProperty = DependencyProperty.RegisterAttached(
        "FadeOnHide", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnFadeOnHideChanged));

    public static bool GetFadeOnHide(UIElement element) => (bool)element.GetValue(FadeOnHideProperty);

    public static void SetFadeOnHide(UIElement element, bool value) => element.SetValue(FadeOnHideProperty, value);

    /// <summary>Spins an element (a refresh glyph) until <see cref="StopSpin"/>.</summary>
    public static void StartSpin(FrameworkElement element)
    {
        if (!IsEnabled)
        {
            return;
        }

        Visual visual = ElementCompositionPreview.GetElementVisual(element);

        // The first refresh can start before layout, when the size is still 0 and the glyph would
        // orbit its corner; keep the pivot centered whenever the size is known or changes.
        if (!element.IsLoaded || element.ActualWidth == 0)
        {
            element.SizeChanged -= OnSpinTargetSizeChanged;
            element.SizeChanged += OnSpinTargetSizeChanged;
        }

        CenterPivot(element);

        ScalarKeyFrameAnimation spin = visual.Compositor.CreateScalarKeyFrameAnimation();
        spin.InsertExpressionKeyFrame(0, "this.StartingValue", visual.Compositor.CreateLinearEasingFunction());
        spin.InsertExpressionKeyFrame(1, "this.StartingValue + 360", visual.Compositor.CreateLinearEasingFunction());
        spin.Duration = SpinDuration;
        spin.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation(nameof(Visual.RotationAngleInDegrees), spin);
    }

    /// <summary>Lets a spinning element ease to the end of its turn instead of stopping mid-angle.</summary>
    public static void StopSpin(FrameworkElement element)
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation(nameof(Visual.RotationAngleInDegrees));

        float angle = visual.RotationAngleInDegrees % 360;
        visual.RotationAngleInDegrees = angle;
        if (angle == 0 || !IsEnabled)
        {
            visual.RotationAngleInDegrees = 0;
            return;
        }

        Compositor compositor = visual.Compositor;
        ScalarKeyFrameAnimation settle = compositor.CreateScalarKeyFrameAnimation();
        settle.InsertKeyFrame(1, 360, compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f)));
        settle.Duration = TimeSpan.FromMilliseconds(250 + (360 - angle));
        visual.StartAnimation(nameof(Visual.RotationAngleInDegrees), settle);
    }

    /// <summary>
    /// Flips an element over on its horizontal axis, starting after <paramref name="delay"/>: it
    /// tips away to edge-on, then comes back around from the other side (so text never shows
    /// mirrored) and settles with a slight overshoot. PlaneProjection supplies the perspective.
    /// </summary>
    public static void Flip(UIElement element, TimeSpan delay)
    {
        if (!IsEnabled)
        {
            return;
        }

        PlaneProjection projection = element.Projection as PlaneProjection ?? new PlaneProjection();
        element.Projection = projection;

        DoubleAnimationUsingKeyFrames rotate = new() { BeginTime = delay };
        rotate.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 0 });
        rotate.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = FlipHalf,
            Value = 90,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        });
        rotate.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = FlipHalf + TimeSpan.FromMilliseconds(1), Value = -90 });
        rotate.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = FlipHalf + FlipSettle,
            Value = 0,
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 },
        });
        Storyboard.SetTarget(rotate, projection);
        Storyboard.SetTargetProperty(rotate, nameof(PlaneProjection.RotationX));

        Storyboard storyboard = new();
        storyboard.Children.Add(rotate);
        storyboard.Completed += (_, _) =>
        {
            // Drop the projection so the settled card renders flat, as before.
            if (element.Projection == projection)
            {
                element.Projection = null;
            }
        };
        storyboard.Begin();
    }

    private static void OnSpinTargetSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is FrameworkElement element && e.NewSize.Width > 0)
        {
            element.SizeChanged -= OnSpinTargetSizeChanged;
            CenterPivot(element);
        }
    }

    private static void CenterPivot(FrameworkElement element) =>
        ElementCompositionPreview.GetElementVisual(element).CenterPoint =
            new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);

    private static void OnPulseIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || (int)e.OldValue != -1)
        {
            return; // Hooked once; a recycled container only changes the index.
        }

        element.Loaded += (_, _) => UpdatePulse(element);
        element.Unloaded += (_, _) => StopPulse(element);
        element.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => UpdatePulse(element));
        if (element.IsLoaded)
        {
            UpdatePulse(element);
        }
    }

    private static void UpdatePulse(FrameworkElement element)
    {
        if (!element.IsLoaded || element.Visibility != Visibility.Visible || !IsEnabled)
        {
            StopPulse(element);
            return;
        }

        Visual visual = ElementCompositionPreview.GetElementVisual(element);
        Compositor compositor = visual.Compositor;
        CubicBezierEasingFunction ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.45f, 0), new Vector2(0.55f, 1));

        ScalarKeyFrameAnimation pulse = compositor.CreateScalarKeyFrameAnimation();
        pulse.InsertKeyFrame(0, 1, ease);
        pulse.InsertKeyFrame(0.5f, 0.45f, ease);
        pulse.InsertKeyFrame(1, 1, ease);
        pulse.Duration = PulseDuration;
        pulse.DelayTime = PulseStagger * Math.Max(0, GetPulseIndex(element));
        pulse.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        pulse.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation(nameof(Visual.Opacity), pulse);
    }

    private static void StopPulse(FrameworkElement element)
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation(nameof(Visual.Opacity));
        visual.Opacity = 1;
    }

    private static void OnRevealOnShowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element || e.OldValue is true || e.NewValue is not true)
        {
            return;
        }

        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        element.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) =>
        {
            if (element.Visibility == Visibility.Visible && IsEnabled)
            {
                Reveal(element);
            }
        });
    }

    private static void Reveal(UIElement element)
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(element);
        Compositor compositor = visual.Compositor;
        CubicBezierEasingFunction decelerate = compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));

        ScalarKeyFrameAnimation fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, decelerate);
        fade.Duration = RevealDuration;

        Vector3KeyFrameAnimation rise = compositor.CreateVector3KeyFrameAnimation();
        rise.InsertKeyFrame(0, new Vector3(0, RevealRise, 0));
        rise.InsertKeyFrame(1, Vector3.Zero, decelerate);
        rise.Duration = RevealDuration;

        visual.StartAnimation(nameof(Visual.Opacity), fade);
        visual.StartAnimation("Translation", rise);
    }

    private static void OnFadeOnHideChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        if (e.NewValue is not true || !IsEnabled)
        {
            ElementCompositionPreview.SetImplicitHideAnimation(element, null);
            return;
        }

        Compositor compositor = ElementCompositionPreview.GetElementVisual(element).Compositor;
        ScalarKeyFrameAnimation fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Target = nameof(Visual.Opacity);
        fade.InsertExpressionKeyFrame(0, "this.StartingValue");
        fade.InsertKeyFrame(1, 0);
        fade.Duration = PlaceholderFadeDuration;
        ElementCompositionPreview.SetImplicitHideAnimation(element, fade);
    }
}
