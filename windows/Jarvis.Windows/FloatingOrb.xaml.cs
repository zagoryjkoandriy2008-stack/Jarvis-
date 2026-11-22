using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Jarvis.Windows.Core;
namespace Jarvis.Windows;

public partial class FloatingOrb : Window {
    private const int GwlExstyle=-20;
    private const int WsExNoActivate=0x08000000;
    [DllImport("user32.dll",EntryPoint="GetWindowLongW",SetLastError=true)]
    private static extern int GetWindowLong(IntPtr hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongW",SetLastError=true)]
    private static extern int SetWindowLong(IntPtr hwnd,int index,int style);

    protected override void OnSourceInitialized(EventArgs e) {
        base.OnSourceInitialized(e);
        // Clicking the floating microphone must NOT steal foreground focus
        // from Spotify, Explorer, or the app whose button JARVIS will click.
        var hwnd=new WindowInteropHelper(this).Handle;
        int style=GetWindowLong(hwnd,GwlExstyle);
        SetWindowLong(hwnd,GwlExstyle,style|WsExNoActivate);
    }

    public event Action? ListenRequested;
    public event Action? ShowRequested;
    public event Action? HideRequested;
    public event Action? StopSharingRequested;
    public event Action<JarvisDevice>? TargetRequested;
    private bool _dragging;
    private Point _pressed;
    private Point _windowAtPress;
    public FloatingOrb() {
        InitializeComponent();
        Left=Math.Max(10,SystemParameters.WorkArea.Right-Width-24);
        Top=Math.Max(10,SystemParameters.WorkArea.Bottom-Height-36);
    }
    public void SetTarget(JarvisDevice selected) {
        TargetLabel.Text=DeviceRouting.Label(selected);
        PhoneButton.Opacity=selected==JarvisDevice.Phone?1:.58;
        WatchButton.Opacity=selected==JarvisDevice.Watch?1:.58;
        PcButton.Opacity=selected==JarvisDevice.Computer?1:.58;
    }
    public void SetListening(bool listening) {
        OrbButton.Content=listening?"◉  ◉\n▁▁":"◼  ◼\n▔▔";
        OrbButton.Foreground=listening?
            System.Windows.Media.Brushes.Orange:
            new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(83,215,234));
    }
    public void SetStreaming(bool streaming) {
        ScreenCastBadge.Visibility=streaming?Visibility.Visible:Visibility.Collapsed;
        OrbRing.BorderBrush=streaming
            ?System.Windows.Media.Brushes.OrangeRed
            :new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(83,215,234));
    }
    private void OrbButton_Click(object sender,RoutedEventArgs e)=>ListenRequested?.Invoke();
    private void Phone_Click(object sender,RoutedEventArgs e)=>TargetRequested?.Invoke(JarvisDevice.Phone);
    private void Watch_Click(object sender,RoutedEventArgs e)=>TargetRequested?.Invoke(JarvisDevice.Watch);
    private void Pc_Click(object sender,RoutedEventArgs e)=>TargetRequested?.Invoke(JarvisDevice.Computer);
    private void ShowMain_Click(object sender,RoutedEventArgs e)=>ShowRequested?.Invoke();
    private void HideOrb_Click(object sender,RoutedEventArgs e)=>HideRequested?.Invoke();
    private void StopScreen_Click(object sender,RoutedEventArgs e)=>StopSharingRequested?.Invoke();
    private void Orb_Drag(object sender,MouseButtonEventArgs e) {
        if(e.ChangedButton!=MouseButton.Left)return;
        _dragging=true;
        _pressed=PointToScreen(e.GetPosition(this));
        _windowAtPress=new Point(Left,Top);
        Mouse.Capture((IInputElement)sender);
    }
    private void Orb_DragEnd(object sender,MouseButtonEventArgs e) {
        if(!_dragging)return;
        var current=PointToScreen(e.GetPosition(this));
        if(Math.Abs(current.X-_pressed.X)>8 || Math.Abs(current.Y-_pressed.Y)>8) {
            Left=Math.Clamp(_windowAtPress.X+current.X-_pressed.X,
                SystemParameters.WorkArea.Left,SystemParameters.WorkArea.Right-Width);
            Top=Math.Clamp(_windowAtPress.Y+current.Y-_pressed.Y,
                SystemParameters.WorkArea.Top,SystemParameters.WorkArea.Bottom-Height);
        }
        _dragging=false;
        Mouse.Capture(null);
    }
}
