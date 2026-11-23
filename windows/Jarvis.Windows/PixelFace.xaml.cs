using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Jarvis.Windows;

public enum PixelFaceState { Idle, Listening, Thinking, Speaking }

/// <summary>
/// Shared face for the Windows hero and floating assistant.
/// Geometry/color proportions follow Android MainActivity.PixelFace.
/// Audio/listening states are set by the real command lifecycle, never inferred from animation.
/// </summary>
public partial class PixelFace : System.Windows.Controls.UserControl {
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private PixelFaceState _state = PixelFaceState.Idle;

    public PixelFace() {
        InitializeComponent();
        _timer.Tick += (_, _) => Animate();
    }

    private void PixelFace_Loaded(object sender, RoutedEventArgs e) {
        Animate();
        _timer.Start();
    }

    private void PixelFace_Unloaded(object sender, RoutedEventArgs e) {
        _timer.Stop();
    }

    public void SetState(PixelFaceState state) {
        _state = state;
        IdleMouth.Visibility = state == PixelFaceState.Idle ? Visibility.Visible : Visibility.Collapsed;
        ListeningMouth.Visibility = state == PixelFaceState.Listening ? Visibility.Visible : Visibility.Collapsed;
        ThinkingMouth.Visibility = state == PixelFaceState.Thinking ? Visibility.Visible : Visibility.Collapsed;
        SpeakingMouth.Visibility = state == PixelFaceState.Speaking ? Visibility.Visible : Visibility.Collapsed;
        FaceRing.Stroke = new SolidColorBrush(state == PixelFaceState.Listening
            ? System.Windows.Media.Color.FromRgb(107, 232, 191) : System.Windows.Media.Color.FromRgb(114, 235, 255));
        Animate();
    }

    private void Animate() {
        long now = Environment.TickCount64;
        bool blink = now % 5000 > 4570;
        EyesNormal.Visibility = blink ? Visibility.Collapsed : Visibility.Visible;
        EyesBlink.Visibility = blink ? Visibility.Visible : Visibility.Collapsed;
        // Subtle ring breathing, with distinct active-state feedback.
        double pulse = (Math.Sin(now / 650.0) + 1) * .5;
        OuterRing.Opacity = .44 + .27 * pulse;
        MiddleRing.Opacity = .48 + .20 * (1 - pulse);
        if (_state == PixelFaceState.Speaking) {
            double height = 5.6 * (1 + (int)(now / 160 % 3));
            SpeakingMouth.Height = height;
            Canvas.SetTop(SpeakingMouth, 127.4 - height);
        }
    }
}
