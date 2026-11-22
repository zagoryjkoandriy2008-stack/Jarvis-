using System.IO;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
namespace Jarvis.Windows;

/// <summary>One interactive instance per Windows session, never a background service.</summary>
public partial class App : System.Windows.Application {
    private Mutex? _instance;
    private bool _ownsMutex;
    private CancellationTokenSource? _pipeLife;
    private string PipeName=>"jarvis-windows-"+Process.GetCurrentProcess().SessionId;
    protected override void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);
        _instance=new Mutex(true,@"Local\Jarvis.Windows.OneInstance",out _ownsMutex);
        if(!_ownsMutex) {
            SignalExisting();
            Shutdown();
            return;
        }
        _pipeLife=new CancellationTokenSource();
        _=ServeShowRequestsAsync(_pipeLife.Token);
        var window=new MainWindow();
        MainWindow=window;
        if(e.Args.Any(x=>x.Equals("--minimized",StringComparison.OrdinalIgnoreCase)) &&
           window.StartMinimizedAfterLogin) {
            // Create an HWND for hotkeys and notifications without making
            // the main window visible on automatic startup.
            new WindowInteropHelper(window).EnsureHandle();
            window.HideFloatingForBackground();
        } else window.Show();
    }
    private void SignalExisting() {
        for(int attempt=0;attempt<3;attempt++) {
            try {
                using var pipe=new NamedPipeClientStream(".",PipeName,PipeDirection.Out);
                pipe.Connect(600);
                using var writer=new StreamWriter(pipe,Encoding.UTF8) {AutoFlush=true};
                writer.WriteLine("show");
                return;
            }catch(Exception) when(attempt<2) { Thread.Sleep(150); }
            catch(Exception) { return; }
        }
    }
    private async Task ServeShowRequestsAsync(CancellationToken cancel) {
        while(!cancel.IsCancellationRequested) {
            try {
                using var pipe=new NamedPipeServerStream(PipeName,PipeDirection.In,1,
                    PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancel);
                using var reader=new StreamReader(pipe,Encoding.UTF8);
                if(await reader.ReadLineAsync(cancel)=="show")
                    await Dispatcher.InvokeAsync(()=>{
                        if(MainWindow is MainWindow main)main.RestoreFromTray();
                    });
            }catch(OperationCanceledException){break;}
            catch(Exception){try{await Task.Delay(500,cancel);}catch(OperationCanceledException){break;}}
        }
    }
    protected override void OnExit(ExitEventArgs e) {
        _pipeLife?.Cancel();
        _pipeLife?.Dispose();
        if(_ownsMutex)_instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
