using NAudio.CoreAudioApi;

namespace Jarvis.Windows.Core;

public static class WindowsVolume {
    public static void SetPercent(int percent) {
        if(percent<0 || percent>100)throw new ArgumentOutOfRangeException(nameof(percent));
        using var enumerator=new MMDeviceEnumerator();
        using var device=enumerator.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia);
        device.AudioEndpointVolume.MasterVolumeLevelScalar=percent/100f;
    }
    public static int GetPercent() {
        using var enumerator=new MMDeviceEnumerator();
        using var device=enumerator.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia);
        return (int)Math.Round(device.AudioEndpointVolume.MasterVolumeLevelScalar*100);
    }
}
