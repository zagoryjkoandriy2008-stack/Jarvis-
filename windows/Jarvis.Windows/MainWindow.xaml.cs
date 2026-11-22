using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Jarvis.Windows.Core;

namespace Jarvis.Windows;

public partial class MainWindow : Window {
    private readonly StateStore _store=new();
    private readonly JarvisState _state;
    private readonly VoiceService _voice=new();
    private readonly VoiceWakeService _wake=new();
    private int _wakeGeneration;
    private async Task ResumeWakeAsync() {
        var generation=++_wakeGeneration;
        try {
            for(int n=0;n<150;n++) {
                await Task.Delay(200,_lifetime.Token);
                if(generation!=_wakeGeneration||!_state.Settings.EnableWakeWord)return;
                if(!_busy&&!_voice.IsListening&&!_voice.IsSpeaking) {
                    _wake.Start(_state.Settings.RecognizerCulture);
                    return;
                }
            }
            Status("Wake word: очікування голосового модуля перевищило 30 с.");
        }catch(OperationCanceledException){}
    }
    private void WakeDetected() {
        if(_automationPaused||_busy||_voice.IsListening||_voice.IsSpeaking)return;
        _voice.Start(_state.Settings.RecognizerCulture);
        MicButton.Content=_voice.IsListening?"■ Слухаю…":"🎙 Слухати";
        _tray?.SetListening(_voice.IsListening);
        if(!_voice.IsListening)_=ResumeWakeAsync();
    }

    private readonly AiClient _ai=new();
    private readonly WindowsFileExchange _fileExchange=new();
    private TrayStartupService? _tray;
    private bool _allowExit;
    private bool _automationPaused;
    public bool StartMinimizedAfterLogin=>_state.Settings.LaunchAtSignIn;
    public void HideFloatingForBackground()=>_floating?.Hide();
    public void RestoreFromTray() { Show();WindowState=WindowState.Normal;Activate(); }
    private void QuitFromTray() { _allowExit=true;Close();System.Windows.Application.Current.Shutdown(); }
    private readonly FinanceService _finance;
    private DeviceRelayClient? _relay;
    private readonly WindowsScreenPublisher _screenPublisher;
    private string? _pendingControlSession;
    private FloatingOrb? _floating;
    private JarvisDevice _target=JarvisDevice.Computer;
    private readonly CancellationTokenSource _lifetime=new();
    private CancellationTokenSource? _operation;
    private HwndSource? _source;
    private bool _busy;
    private bool _runningMacro;
    private bool _hotkeyRegistered;
    private const int HotkeyId=0x4A51;
    private const int WmHotkey=0x0312;
    private const uint ModControl=0x0002,ModShift=0x0004;
    [DllImport("user32.dll",SetLastError=true)]
    private static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd,int id);

    public MainWindow() {
        InitializeComponent();
        try { _state=_store.Load(); }
        catch(InvalidDataException ex) {
            System.Windows.MessageBox.Show(ex.Message,"JARVIS Windows",MessageBoxButton.OK,MessageBoxImage.Error);
            throw;
        }
        _finance=new FinanceService(_state,_store);
        _screenPublisher=new WindowsScreenPublisher(_state.Settings,_store);
        _screenPublisher.ShareStatus+=(active,detail)=>{
            Dispatcher.BeginInvoke(new Action(()=>{
                if(active) {
                    ScreenShareIndicator.Visibility=Visibility.Visible;
                    StopScreenShareButton.Visibility=Visibility.Visible;
                } else {
                    ScreenShareIndicator.Visibility=Visibility.Collapsed;
                    StopScreenShareButton.Visibility=Visibility.Collapsed;
                }
                _floating?.SetStreaming(active);
                Status(detail);
            }));
        };
        _screenPublisher.ControlRequested+=session=>Dispatcher.BeginInvoke(
            new Action(()=>{
                if(!_state.Settings.EnableRemoteControl||
                   !_state.Settings.EnableScreenShare)return;
                _pendingControlSession=session;
                ControlRequestPanel.Visibility=Visibility.Visible;
                ControlActivePanel.Visibility=Visibility.Collapsed;
                // This is the single visible user-consent gate. Only
                // clicking Allow on the Windows machine enables input.
                Show();
                WindowState=WindowState.Normal;
                Activate();
                Status("📱 Pixel просить керувати мишею і клавіатурою. "+
                    "Підтверди або відхили на Windows.");
            }));
        _screenPublisher.ControlStatus+=(active,detail)=>Dispatcher.BeginInvoke(
            new Action(()=>{
                ControlActivePanel.Visibility=active
                    ?Visibility.Visible:Visibility.Collapsed;
                if(!active) {
                    ControlRequestPanel.Visibility=Visibility.Collapsed;
                    _pendingControlSession=null;
                }
                Status(detail);
            }));
        _=_screenPublisher.RunAsync(_lifetime.Token);
        try {
            _relay=new DeviceRelayClient(_store,_state.Settings);
            _relay.Status+=text=>Dispatcher.BeginInvoke(new Action(()=>Status(text)));
            _relay.CommandReceived+=HandleRemoteOnUiAsync;
            _=_relay.ListenAsync(_lifetime.Token);
        } catch(InvalidDataException invalidReceipts) {
            // A corrupt at-most-once journal must disable REMOTE execution,
            // not prevent users from using all local JARVIS functions.
            _relay=null;
            _state.Settings.EnableDeviceRelay=false;
            System.Windows.MessageBox.Show(invalidReceipts.Message+
                "\nЛокальний JARVIS продовжить роботу. Для дистанційного "+
                "керування віднови remote-receipts.json із резервної копії.",
                "JARVIS — журнал дистанційних дій",MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        WorkerUrl.Text=_state.Settings.WorkerUrl;
        SpeakReplies.IsChecked=_state.Settings.SpeakReplies;
        StayOnTop.IsChecked=_state.Settings.OnTop;
        EnableDeviceRelay.IsChecked=_state.Settings.EnableDeviceRelay;
        EnableScreenShare.IsChecked=_state.Settings.EnableScreenShare;
        EnableRemoteControl.IsChecked=_state.Settings.EnableRemoteControl;
        ShowFloatingOrb.IsChecked=_state.Settings.ShowFloatingOrb;
        LaunchAtSignIn.IsChecked=_state.Settings.LaunchAtSignIn;
        MinimizeToTray.IsChecked=_state.Settings.MinimizeToTray;
        EnableWakeWord.IsChecked=_state.Settings.EnableWakeWord;
        Topmost=_state.Settings.OnTop;
        NotesText.Text=_state.Notes;
        VoiceChoices.ItemsSource=_voice.Voices;
        VoiceChoices.SelectedItem=_state.Settings.VoiceName.Length>0
            ?_state.Settings.VoiceName:_voice.Voices.FirstOrDefault();
        RecognizerChoices.ItemsSource=_voice.Recognizers;
        RecognizerChoices.SelectedItem=_state.Settings.RecognizerCulture.Length>0
            ?_state.Settings.RecognizerCulture:_voice.Recognizers.FirstOrDefault();
        _voice.Recognized+=text=>Dispatcher.BeginInvoke(new Action(async ()=>{
            Append("🎙 Ви",text);
            await HandleUtteranceAsync(text);
        }));
        _voice.Status+=text=>Dispatcher.BeginInvoke(new Action(()=>Status(text)));
        _voice.RecognitionEnded+=()=>Dispatcher.BeginInvoke(new Action(async()=>await ResumeWakeAsync()));
        _wake.Status+=text=>Dispatcher.BeginInvoke(new Action(()=>Status(text)));
        _wake.Detected+=()=>Dispatcher.BeginInvoke(new Action(WakeDetected));
        RefreshFinance();
        RefreshMacros();
        Append("JARVIS","Windows-версія запущена. Для голосу натисни 🎙 або Ctrl+Shift+J. Якщо українського розпізнавача Windows немає, користуйся текстом.");
        _floating=new FloatingOrb();
        _floating.ListenRequested+=ToggleListening;
        _floating.TargetRequested+=SetTarget;
        _floating.HideRequested+=()=>_floating.Hide();
        _floating.StopSharingRequested+=()=>StopScreenShare_Click(this,new RoutedEventArgs());
        _floating.ShowRequested+=()=>{Show();WindowState=WindowState.Normal;Activate();};
        _floating.SetTarget(_target);
        if(_state.Settings.ShowFloatingOrb)_floating.Show();
        if(_state.Settings.EnableWakeWord)_=ResumeWakeAsync();
        CurrentDeviceLabel.Text=DeviceRouting.Label(_target);
        SourceInitialized+=OnSourceInitialized;
        Closing+=OnClosing;
        _tray=new TrayStartupService();
        _tray.ShowRequested+=()=>Dispatcher.BeginInvoke(new Action(RestoreFromTray));
        _tray.ListenRequested+=()=>Dispatcher.BeginInvoke(new Action(ToggleListening));
        _tray.DevicesRequested+=()=>Dispatcher.BeginInvoke(new Action(()=>{
            RestoreFromTray();CheckDevices_Click(this,new RoutedEventArgs());
        }));
        _tray.PauseChanged+=paused=>Dispatcher.BeginInvoke(new Action(()=>{
            _automationPaused=paused;
            if(paused)_operation?.Cancel();
            Status(paused?"Автоматику призупинено.":"Автоматику відновлено.");
        }));
        _tray.ExitRequested+=()=>Dispatcher.BeginInvoke(new Action(QuitFromTray));
    }

    private void OnSourceInitialized(object? sender,EventArgs e) {
        _source=PresentationSource.FromVisual(this) as HwndSource;
        if(_source==null)return;
        _source.AddHook(HotkeyHook);
        _hotkeyRegistered=RegisterHotKey(_source.Handle,HotkeyId,
            ModControl|ModShift,(uint)KeyInterop.VirtualKeyFromKey(Key.J));
        if(!_hotkeyRegistered)Status("Ctrl+Shift+J зайнята іншою програмою. Можна натискати кнопку мікрофона.");
    }
    private IntPtr HotkeyHook(IntPtr hwnd,int msg,IntPtr wParam,IntPtr lParam,ref bool handled) {
        if(msg==WmHotkey && wParam.ToInt32()==HotkeyId) {
            handled=true;
            Dispatcher.BeginInvoke(new Action(ToggleListening));
        }
        return IntPtr.Zero;
    }
    private void OnClosing(object? sender,CancelEventArgs e) {
        if(!_allowExit && _state.Settings.MinimizeToTray) {
            e.Cancel=true;Hide();return;
        }
        _wakeGeneration++;
        _wake.Dispose();
        _tray?.Dispose();_tray=null;
        if(_source!=null) {
            if(_hotkeyRegistered)UnregisterHotKey(_source.Handle,HotkeyId);
            _source.RemoveHook(HotkeyHook);
        }
        _lifetime.Cancel();
        _operation?.Cancel();
        _voice.Dispose();
        _floating?.Close();
        _relay?.Dispose();
        _screenPublisher.Dispose();
        _ai.Dispose();
        _fileExchange.Dispose();
        _lifetime.Dispose();
    }

    private void ToggleListening() {
        _wakeGeneration++;
        _wake.Stop();
        if(_voice.IsListening) {
            _voice.StopListening(); MicButton.Content="🎙 Слухати";_tray?.SetListening(false);
        } else {
            _voice.Start(RecognizerChoices.SelectedItem as string);
            MicButton.Content=_voice.IsListening?"■ Слухаю…":"🎙 Слухати";
            _tray?.SetListening(_voice.IsListening);
        }
        _floating?.SetListening(_voice.IsListening);
    }
    private void MicButton_Click(object sender,RoutedEventArgs e)=>ToggleListening();
    private void StopButton_Click(object sender,RoutedEventArgs e) {
        _voice.StopListening();_voice.StopVoice();_operation?.Cancel();
        MicButton.Content="🎙 Слухати";
        _floating?.SetListening(false);
        Status("Зупинено.");
    }
    private void CommandInput_KeyDown(object sender,KeyEventArgs e) {
        if(e.Key==Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift)==0) {
            e.Handled=true;
            _=SubmitInputAsync();
        }
    }
    private async void RunButton_Click(object sender,RoutedEventArgs e)=>
        await SubmitInputAsync();
    private async Task SubmitInputAsync() {
        var text=CommandInput.Text.Trim();
        if(text.Length==0)return;
        CommandInput.Clear();
        Append("Ви",text);
        await HandleUtteranceAsync(text);
    }
    private void Append(string by,string text) {
        ChatHistory.AppendText($"[{DateTime.Now:HH:mm:ss}] {by}: {text}\n\n");
        ChatHistory.ScrollToEnd();
    }
    private void Status(string detail)=>StatusText.Text=detail;
    private async void ReceiveFileFromPixel_Click(object sender,RoutedEventArgs e) {
        try {
            FileExchangeStatus.Text="Перевіряю файли з Pixel…";
            var selected=await _fileExchange.GetFromPixelAsync(
                _state.Settings,_store,_lifetime.Token);
            if(selected==null) {
                FileExchangeStatus.Text="Файла з Pixel немає. Спочатку передай його з телефону.";
                return;
            }
            var picker=new SaveFileDialog {
                Title="JARVIS — зберегти файл з Pixel",FileName=selected.Name,
                OverwritePrompt=true,Filter="Усі файли|*.*"
            };
            if(picker.ShowDialog(this)!=true) {
                FileExchangeStatus.Text="Збереження скасовано. Файл може залишатися у Relay до 2 хвилин.";
                return;
            }
            await File.WriteAllBytesAsync(picker.FileName,selected.Bytes,_lifetime.Token);
            FileExchangeStatus.Text="✅ Файл з Pixel збережено: "+selected.Name;
            try {
                await _fileExchange.ConfirmSavedAsync(
                    selected.Id,_state.Settings,_store,_lifetime.Token);
            }catch(Exception) {
                FileExchangeStatus.Text+=" (Relay сам очистить тимчасову копію).";
            }
        }catch(Exception error) {
            FileExchangeStatus.Text="Не вдалося отримати файл: "+error.Message;
        }
    }
    private async void SendFileToPixel_Click(object sender,RoutedEventArgs e) {
        if(!_state.Settings.EnableDeviceRelay) {
            FileExchangeStatus.Text="Спочатку ввімкни приватний Relay у налаштуваннях.";
            return;
        }
        var picker=new OpenFileDialog {
            Title="JARVIS — вибери файл для Pixel (до 1 МіБ)",
            Multiselect=false,CheckFileExists=true,Filter="Усі файли|*.*"
        };
        if(picker.ShowDialog(this)!=true)return;
        try {
            FileExchangeStatus.Text="Надсилаю вибраний файл в тимчасовий Relay…";
            FileExchangeStatus.Text=await _fileExchange.SendToPixelAsync(
                picker.FileName,_state.Settings,_store,_lifetime.Token);
        }catch(Exception error) {
            FileExchangeStatus.Text="Передавання не виконано: "+error.Message;
        }
    }
    private async void RefreshSystemStatus_Click(object sender,RoutedEventArgs e) {
        try {
            SystemStatusReport.Text="Зчитую навантаження Windows…";
            SystemStatusReport.Text=await WindowsSystemMonitor.SnapshotAsync(_lifetime.Token);
        }catch(OperationCanceledException) {
            SystemStatusReport.Text="Перевірку скасовано.";
        }catch(Exception error) {
            SystemStatusReport.Text="Не вдалося отримати показники: "+error.Message;
        }
    }
    private async void AnalyzeScreenButton_Click(object sender,RoutedEventArgs e) {
        await HandleUtteranceAsync("проаналізуй екран",forceComputer:true);
    }
    private void Reply(string detail,bool speak=true) {
        Append("JARVIS",detail);
        Status(detail.Length<=180?detail:detail[..177]+"…");
        if(speak && SpeakReplies.IsChecked==true)_voice.Say(detail[..Math.Min(1200,detail.Length)]);
    }

    private async Task HandleUtteranceAsync(string raw,bool forceComputer=false) {
        if(_automationPaused){Reply("Автоматику призупинено.",false);return;}
        if(_busy) {Status("Попередня команда ще виконується.");return;}
        _busy=true;
        using var operation=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operation=operation;
        try {
            var change=DeviceRouting.TargetChange(raw);
            if(change.HasValue) {
                SetTarget(change.Value);
                Reply("Обрано режим "+DeviceRouting.Label(change.Value)+".");
            } else {
            var macro=MacroEngine.Match(_state,raw);
            if(macro!=null && _target==JarvisDevice.Computer) {
                if(_runningMacro)throw new InvalidOperationException("Макрос уже виконується.");
                await RunMacroAsync(macro,operation.Token);
                Reply("Макрос «"+macro.Trigger+"» виконано.");
            } else {
                var cmd=CommandRouter.Parse(raw);
                var destination=forceComputer || cmd.Kind==CommandKind.AnalyzeScreen
                    ?JarvisDevice.Computer:DeviceRouting.EffectiveTarget(_target,cmd.Kind);
                if(destination!=JarvisDevice.Computer) {
                    if(!_state.Settings.EnableDeviceRelay || _relay==null)
                        throw new InvalidOperationException("Увімкни Cloudflare Relay у Налаштуваннях для керування іншими пристроями.");
                    Status("Передаю команду: "+DeviceRouting.Label(destination)+"…");
                    var response=await _relay.SendCommandAsync(JarvisDevice.Computer,
                        destination,raw,operation.Token);
                    if(cmd.Kind is CommandKind.FinanceAdd or
                       CommandKind.FinanceReport or CommandKind.FinanceRemote)
                        FinanceReport.Text=response.Ok ? response.Detail :
                            "Pixel не підтвердив фінансову команду: "+response.Detail;
                    if(cmd.Kind is CommandKind.NoteAdd or
                       CommandKind.NoteRead or CommandKind.NoteDelete)
                        PhoneNotesReport.Text=response.Ok ? response.Detail :
                            "Нотатки Pixel не оновлено: "+response.Detail;
                    Reply((response.Ok?"Підтверджено: ":"Не виконано: ")+response.Detail);
                } else await ExecuteAsync(cmd,operation.Token);
            }
            }
        } catch(OperationCanceledException) { Status("Команду скасовано."); }
          catch(Exception ex) { Reply("Не вдалося виконати: "+ex.Message,false); }
        finally {
            _busy=false;_operation=null;MicButton.Content="🎙 Слухати";
            _floating?.SetListening(false);
        }
    }

    private async Task ExecuteAsync(ParsedCommand command,CancellationToken token) {
        switch(command.Kind) {
            case CommandKind.FinanceAdd:
                if(command.Finance==null)throw new InvalidOperationException("Немає даних транзакції.");
                Reply(_finance.Add(command.Finance));
                RefreshFinance();break;
            case CommandKind.FinanceReport:
                Reply(_finance.Report(),false);RefreshFinance();break;
            case CommandKind.DeviceList:
                if(_relay==null||!_state.Settings.EnableDeviceRelay) {
                    Reply("Зв'язок пристроїв вимкнений. Windows працює локально.",false);break;
                }
                var phonePresence=await _relay.GetPresenceAsync(JarvisDevice.Phone,token);
                var watchPresence=await _relay.GetPresenceAsync(JarvisDevice.Watch,token);
                var statusDevices="Windows: активний; Pixel: "+FormatPresence(phonePresence)+
                    "; Watch7: "+FormatPresence(watchPresence);
                DeviceStatusReport.Text=statusDevices;Reply(statusDevices,false);break;
            case CommandKind.LockWindows:
            case CommandKind.MonitorOff:
                RestoreFromTray();
                var commandLabel=command.Kind==CommandKind.LockWindows?"заблокувати Windows":"вимкнути монітор";
                if(System.Windows.MessageBox.Show("Підтвердити на цьому комп'ютері: "+commandLabel+"?",
                    "JARVIS — дозвіл на системну дію",MessageBoxButton.YesNo,
                    MessageBoxImage.Warning)!=MessageBoxResult.Yes)
                    throw new OperationCanceledException("Власник відхилив дію.");
                if(command.Kind==CommandKind.LockWindows)WindowsActions.LockCurrentSession();
                else WindowsActions.TurnOffMonitor();
                Reply("Windows: "+commandLabel+".",false);break;
            case CommandKind.MacroRun:
                var called=MacroEngine.Match(_state,command.Argument)
                    ?? throw new InvalidOperationException("Збережений макрос не знайдено.");
                await RunMacroAsync(called,token);
                Reply("Макрос виконано: "+called.Trigger,false);break;
            case CommandKind.SystemStatus:
                var systemReport=await WindowsSystemMonitor.SnapshotAsync(token);
                SystemStatusReport.Text=systemReport;
                Reply(systemReport,false);break;
            case CommandKind.Wait:
                await Task.Delay(TimeSpan.FromSeconds(command.Value),token);
                Reply("Пауза завершилась.",false);break;
            case CommandKind.AnalyzeScreen:
                if(string.IsNullOrWhiteSpace(_state.Settings.WorkerUrl))
                    throw new InvalidOperationException("Налаштуй приватний Cloudflare Worker.");
                var approval=System.Windows.MessageBox.Show(
                    "Надіслати поточний знімок головного монітора у твій Cloudflare Worker та ШІ? На ньому можуть бути особисті дані.",
                    "JARVIS — разовий дозвіл на аналіз екрана",
                    MessageBoxButton.YesNo,MessageBoxImage.Warning);
                if(approval!=MessageBoxResult.Yes) {
                    Reply("Аналіз екрана скасовано.",false);break;
                }
                var secret=_store.ReadWorkerToken();
                if(string.IsNullOrWhiteSpace(secret))
                    throw new InvalidOperationException("Немає коду Worker.");
                var screenshot=WindowsScreenPublisher.CaptureForAi();
                Status("Надсилаю разовий знімок на аналіз ШІ…");
                Reply(await _ai.AskAsync(_state.Settings.WorkerUrl,secret,
                    "Опиши, що видно на знімку екрана Windows. Якщо є помилка, поясни її. Нічого не натискай і не запускай.",
                    "analysis",token,screenshot));break;
            case CommandKind.ClickNamed:
                Reply(WindowsScreenControl.ClickNamed(command.Argument));
                break;
            case CommandKind.ScrollPage:
                Reply(WindowsScreenControl.ScrollPage(command.Value==1));
                break;
            case CommandKind.NoteAdd:
            case CommandKind.NoteRead:
            case CommandKind.NoteDelete:
            case CommandKind.FinanceRemote:
                throw new InvalidOperationException(
                    "Ця команда потребує Pixel. Активуй приватний JARVIS Relay "+
                    "та повтори команду. Windows не створює дублікати даних.");
            case CommandKind.OpenApp:
                WindowsActions.OpenKnownApp(command.Argument);
                Reply("Відкриваю: "+command.Argument);break;
            case CommandKind.OpenSettings:
                WindowsActions.OpenTarget("ms-settings:");
                Reply("Відкриваю параметри Windows.");break;
            case CommandKind.WebSearch:
                WindowsActions.WebSearch(command.Argument);
                Reply("Відкрив пошук у браузері.");break;
            case CommandKind.SpotifySearch:
            case CommandKind.SpotifyPlaylist:
                WindowsActions.SpotifySearch(command.Argument);
                Reply("Відкрив пошук «"+command.Argument+
                    "» у Spotify. Автоматичний Play на Windows ще не реалізований.");break;
            case CommandKind.Volume:
                WindowsVolume.SetPercent(command.Value);
                Reply("Гучність Windows: "+command.Value+"%.");break;
            case CommandKind.Speak:
                _voice.Say(command.Argument);Status("Промовляю текст.");break;
            case CommandKind.Timer:
                var seconds=command.Value;
                _=TimerAsync(seconds,_lifetime.Token);
                Reply("Таймер поставлено на "+seconds+" секунд.");break;
            case CommandKind.MediaToggle:
                WindowsActions.MediaKey(WindowsActions.PlayPause);
                Reply("Медіа: відтворення / пауза.",false);break;
            case CommandKind.MediaNext:
                WindowsActions.MediaKey(WindowsActions.NextTrack);
                Reply("Наступна пісня.",false);break;
            case CommandKind.MediaPrevious:
                WindowsActions.MediaKey(WindowsActions.PreviousTrack);
                Reply("Попередня пісня.",false);break;
            case CommandKind.TypeIntoActiveWindow:
                WindowsActions.TypeIntoForeground(command.Argument);
                Reply("Текст введено в активне вікно.",false);break;
            case CommandKind.Ai:
                if(string.IsNullOrWhiteSpace(_state.Settings.WorkerUrl))
                    throw new InvalidOperationException("Підключи Cloudflare AI Worker у Налаштуваннях.");
                var tokenText=_store.ReadWorkerToken();
                if(string.IsNullOrEmpty(tokenText))
                    throw new InvalidOperationException("Немає захищеного коду доступу до Worker.");
                var mode=(AiMode.SelectedItem as ComboBoxItem)?.Content?.ToString()??"chat";
                Status("JARVIS думає…");
                var response=await _ai.AskAsync(_state.Settings.WorkerUrl,tokenText,
                    command.Argument,mode,token);
                Reply(response);break;
        }
    }
    private async Task TimerAsync(int seconds,CancellationToken token) {
        try {
            await Task.Delay(TimeSpan.FromSeconds(seconds),token);
            await Dispatcher.InvokeAsync(()=>Reply("Таймер завершено."));
        }catch(OperationCanceledException) { }
    }
    private async Task RunMacroAsync(MacroDefinition macro,CancellationToken token) {
        if(_runningMacro)throw new InvalidOperationException("Макрос уже виконується.");
        _runningMacro=true;
        try {
            await MacroEngine.RunAsync(macro,cmd=>ExecuteAsync(cmd,token),token);
        } finally { _runningMacro=false; }
    }

    private void RefreshFinance()=>FinanceReport.Text=
        "Локальні записи ранньої Preview (не синхронізовані, лише перегляд):\n"+
        _finance.Report()+"\nДля єдиного JARVIS Finance використовуй з'єднання з Pixel.";
    private void RefreshMacros() {
        MacroList.ItemsSource=null;
        MacroList.ItemsSource=_state.Macros.Select(m=>
            m.Trigger+" → "+string.Join(" → ",m.Actions)).ToList();
    }
    private async void AddFinanceButton_Click(object sender,RoutedEventArgs e) {
        var parsed=CommandRouter.Parse(FinanceInput.Text);
        if(parsed.Kind is not (CommandKind.FinanceAdd or
           CommandKind.FinanceRemote or CommandKind.FinanceReport)) {
            Reply("Приклад: витратив 150 готівка транспорт автобус; "+
                "або «залишок бюджету на місяць».",false);return;
        }
        // Preserve the typed amount if the remote operation fails.
        await HandleUtteranceAsync(FinanceInput.Text);
    }
    private async void RefreshFinanceButton_Click(object sender,RoutedEventArgs e) {
        if(!_state.Settings.EnableDeviceRelay || _relay==null) {
            FinanceReport.Text="JARVIS Finance на Pixel недоступний: увімкни "+
                "Cloudflare Relay на обох пристроях. Локальні попередні записи "+
                "не синхронізуються з Pixel.";
            return;
        }
        try {
            FinanceReport.Text="📱 Запитую фінансовий звіт у Pixel…";
            var result=await _relay.SendCommandAsync(JarvisDevice.Computer,
                JarvisDevice.Phone,"звіти",_lifetime.Token);
            FinanceReport.Text=result.Ok?result.Detail:
                "Pixel не повернув звіт: "+result.Detail;
        }catch(Exception failure){
            FinanceReport.Text="JARVIS Finance недоступний: "+failure.Message;
        }
    }
    private async void AddPhoneNote_Click(object sender,RoutedEventArgs e) {
        var note=PhoneNoteInput.Text.Trim();
        if(note.Length is <1 or >180 || note.Any(char.IsControl)) {
            Reply("Нотатка має бути 1–180 символів без керувальних знаків.",false);
            return;
        }
        await HandleUtteranceAsync("запиши нотатку "+note);
        // Do not clear the input automatically: if Pixel is offline the
        // user can retry, rather than silently lose the note text.
    }
    private async void LoadPhoneNotes_Click(object sender,RoutedEventArgs e) {
        await HandleUtteranceAsync("покажи нотатки");
    }
    private void SaveMacroButton_Click(object sender,RoutedEventArgs e) {
        try {
            var macro=MacroEngine.New(MacroTrigger.Text,MacroActions.Text);
            _state.Macros.RemoveAll(m=>m.Trigger.Equals(macro.Trigger,StringComparison.OrdinalIgnoreCase));
            _state.Macros.Add(macro);_store.Save(_state);
            RefreshMacros();Status("Макрос збережено.");
        }catch(Exception ex) {Reply(ex.Message,false);}
    }
    private async void RunMacroButton_Click(object sender,RoutedEventArgs e) {
        var macro=MacroEngine.Match(_state,MacroTrigger.Text);
        if(macro==null) {Reply("Спочатку збережи макрос.",false);return;}
        await HandleUtteranceAsync(macro.Trigger);
    }
    private void StayOnTop_Changed(object sender,RoutedEventArgs e) {
        if(IsLoaded)Topmost=StayOnTop.IsChecked==true;
    }
    private async void AllowControl_Click(object sender,RoutedEventArgs e) {
        string? session=_pendingControlSession;
        if(session==null)return;
        ControlRequestPanel.Visibility=Visibility.Collapsed;
        try {
            bool ok=await _screenPublisher.DecideControlAsync(session,true);
            if(!ok)Status("Запит на керування застарів або дозвіл вимкнений.");
        }catch(Exception ex){
            Status("Не вдалося дозволити керування: "+ex.Message);
        }finally {_pendingControlSession=null;}
    }
    private async void DenyControl_Click(object sender,RoutedEventArgs e) {
        string? session=_pendingControlSession;
        ControlRequestPanel.Visibility=Visibility.Collapsed;
        _pendingControlSession=null;
        if(session==null)return;
        try {await _screenPublisher.DecideControlAsync(session,false);}
        catch(Exception ex){Status("Відхилення: "+ex.Message);}
    }
    private async void StopControl_Click(object sender,RoutedEventArgs e) {
        _pendingControlSession=null;
        ControlRequestPanel.Visibility=Visibility.Collapsed;
        ControlActivePanel.Visibility=Visibility.Collapsed;
        await _screenPublisher.DisableControlAsync();
    }
    private void StopScreenShare_Click(object sender,RoutedEventArgs e) {
        // This is an immediate local revocation; the publisher checks the
        // same settings flag before every capture and every upload.
        _state.Settings.EnableScreenShare=false;
        _state.Settings.EnableRemoteControl=false;
        EnableScreenShare.IsChecked=false;
        EnableRemoteControl.IsChecked=false;
        ControlRequestPanel.Visibility=Visibility.Collapsed;
        ControlActivePanel.Visibility=Visibility.Collapsed;
        _pendingControlSession=null;
        _store.Save(_state);
        _=_screenPublisher.RevokeAsync();
        ScreenShareIndicator.Visibility=Visibility.Collapsed;
        StopScreenShareButton.Visibility=Visibility.Collapsed;
        _floating?.SetStreaming(false);
        Status("Передавання екрана заборонено на Windows.");
    }
    private void SaveSettingsButton_Click(object sender,RoutedEventArgs e) {
        try {
            var url=WorkerUrl.Text.Trim();
            if(url.Length>0 && !AiClient.ValidEndpoint(url))
                throw new InvalidOperationException("Адреса ШІ має бути HTTPS із /chat без параметрів.");
            if(WorkerToken.Password.Length>0)_store.SaveWorkerToken(WorkerToken.Password);
            _state.Settings.WorkerUrl=url;
            _state.Settings.SpeakReplies=SpeakReplies.IsChecked==true;
            _state.Settings.OnTop=StayOnTop.IsChecked==true;
            if(EnableScreenShare.IsChecked==true && EnableDeviceRelay.IsChecked!=true)
                throw new InvalidOperationException("Спочатку дозволь зв'язок пристроїв через Cloudflare.");
            if(EnableRemoteControl.IsChecked==true &&
               (EnableScreenShare.IsChecked!=true || EnableDeviceRelay.IsChecked!=true))
                throw new InvalidOperationException(
                    "Для дистанційної миші й клавіатури потрібно спочатку дозволити "+
                    "перегляд екрана та зв'язок пристроїв.");
            bool previouslySharingAllowed=_state.Settings.EnableScreenShare;
            bool previouslyControlAllowed=_state.Settings.EnableRemoteControl;
            _state.Settings.EnableDeviceRelay=EnableDeviceRelay.IsChecked==true;
            _state.Settings.EnableScreenShare=EnableScreenShare.IsChecked==true;
            _state.Settings.EnableRemoteControl=EnableRemoteControl.IsChecked==true;
            if(previouslyControlAllowed && !_state.Settings.EnableRemoteControl) {
                _pendingControlSession=null;
                ControlRequestPanel.Visibility=Visibility.Collapsed;
                ControlActivePanel.Visibility=Visibility.Collapsed;
                _=_screenPublisher.DisableControlAsync();
            }
            if(previouslySharingAllowed && !_state.Settings.EnableScreenShare)
                _=_screenPublisher.RevokeAsync();
            TrayStartupService.ConfigureAutostart(LaunchAtSignIn.IsChecked==true);
            _state.Settings.LaunchAtSignIn=LaunchAtSignIn.IsChecked==true;
            _state.Settings.MinimizeToTray=MinimizeToTray.IsChecked==true;
            _state.Settings.EnableWakeWord=EnableWakeWord.IsChecked==true;
            if(!_state.Settings.EnableWakeWord){_wakeGeneration++;_wake.Stop();}
            else if(!_voice.IsListening)_=ResumeWakeAsync();
            _state.Settings.ShowFloatingOrb=ShowFloatingOrb.IsChecked==true;
            _state.Settings.VoiceName=VoiceChoices.SelectedItem as string??"";
            _state.Settings.RecognizerCulture=RecognizerChoices.SelectedItem as string??"";
            _voice.SelectVoice(_state.Settings.VoiceName);
            Topmost=_state.Settings.OnTop;
            _store.Save(_state);
            if(_state.Settings.ShowFloatingOrb)_floating?.Show();
            else _floating?.Hide();
            WorkerToken.Clear();
            _ai.ClearHistory();
            Status("Параметри збережено. Код Worker захищений DPAPI.");
        }catch(Exception ex){Reply("Налаштування: "+ex.Message,false);}
    }
    private async void TestAiButton_Click(object sender,RoutedEventArgs e) {
        SaveSettingsButton_Click(sender,e);
        Append("Ви","[Тест підключення AI]");
        await HandleUtteranceAsync("Відповідай одним словом: OK");
    }
    private void TestSpeechButton_Click(object sender,RoutedEventArgs e) {
        _voice.SelectVoice(VoiceChoices.SelectedItem as string);
        _voice.Say("Джарвіс для Windows готовий до роботи.");
    }
    private void ClearConversationButton_Click(object sender,RoutedEventArgs e) {
        _ai.ClearHistory();ChatHistory.Clear();
        Status("Контекст розмови очищено.");
    }

    private void SetTarget(JarvisDevice selected) {
        _target=selected;
        CurrentDeviceLabel.Text=DeviceRouting.Label(selected);
        _floating?.SetTarget(selected);
        Status("Активна ціль: "+DeviceRouting.Label(selected)+
            ". Наступні команди передаються вибраному пристрою.");
    }
    private void OrbToggle_Click(object sender,RoutedEventArgs e) {
        if(_floating==null)return;
        if(_floating.IsVisible)_floating.Hide();
        else _floating.Show();
    }
    private void TargetPhone_Click(object sender,RoutedEventArgs e)=>SetTarget(JarvisDevice.Phone);
    private void TargetWatch_Click(object sender,RoutedEventArgs e)=>SetTarget(JarvisDevice.Watch);
    private void TargetComputer_Click(object sender,RoutedEventArgs e)=>SetTarget(JarvisDevice.Computer);
    private static string FormatPresence(DeviceRelayClient.DevicePresence p) =>
        p.Online?"online, останній heartbeat "+(p.AgeMs??0)/1000+" с тому":
        p.AgeMs.HasValue?"offline, останній heartbeat "+p.AgeMs.Value/1000+" с тому":"немає даних";
    private async void CheckDevices_Click(object sender,RoutedEventArgs e) {
        if(_relay==null || !_state.Settings.EnableDeviceRelay) {
            DeviceStatusReport.Text="Включи авторизований Relay у Налаштуваннях, збережи токен Worker.";
            return;
        }
        try {
            var phone=await _relay.GetPresenceAsync(JarvisDevice.Phone,_lifetime.Token);
            var watch=await _relay.GetPresenceAsync(JarvisDevice.Watch,_lifetime.Token);
            DeviceStatusReport.Text="🖥 Windows: активний\n📱 Pixel: "+FormatPresence(phone)+
                "\n⌚ Watch7: "+FormatPresence(watch)+
                "\nБезпека: старий спільний ключ, криптографічне pairing ще не завершене.";
        }catch(Exception error){DeviceStatusReport.Text="Relay: "+error.Message;}
    }

    /** Execute remote commands only from the authenticated private relay,
     * not from AI output. Only predefined safe macros; never type into another app,
     * launch arbitrary programs, or mutate finance on this Windows machine. */
    private async Task<DeviceRelayClient.RemoteResult> HandleRemoteOnUiAsync(
        DeviceRelayClient.RemoteCommand request) {
        var work=await Dispatcher.InvokeAsync(()=>ExecuteRemoteAsync(request));
        return await work;
    }
    private async Task<DeviceRelayClient.RemoteResult> ExecuteRemoteAsync(
        DeviceRelayClient.RemoteCommand request) {
        if(!_state.Settings.EnableDeviceRelay || _automationPaused)
            return new(false,"Дистанційне керування або автоматику призупинено.");
        if(_busy)return new(false,"JARVIS Windows зайнятий.");
        if(request.Origin is not ("phone" or "watch") ||
           request.Target!="computer" || request.Command.Length is <1 or >250 ||
           request.Command.Any(char.IsControl))
            return new(false,"Джерело або команда не дозволені.");

        // Evaluate only preconfigured owner macros or narrow built-in actions.
        // A voice relay cannot create macros, type text, click UI or invoke AI.
        var command=CommandRouter.Parse(request.Command);
        var remoteMacro=MacroEngine.Match(_state,request.Command);
        if(remoteMacro==null && command.Kind==CommandKind.MacroRun)
            remoteMacro=MacroEngine.Match(_state,command.Argument);
        if(remoteMacro==null && !WindowsRemoteCommandPolicy.IsAllowed(command.Kind))
            return new(false,"Команда не дозволена для дистанційного Windows. "+
                "Фінанси завжди виконуються на Pixel.");

        // Local and remote operations share the same busy gate, including
        // remote macros. Release it even on cancellation or unexpected failure.
        _busy=true;
        try {
            if(remoteMacro!=null) {
                await RunMacroAsync(remoteMacro,_lifetime.Token);
                return new(true,"Windows виконав макрос: "+remoteMacro.Trigger);
            }
            if(command.Kind==CommandKind.SystemStatus) {
                var report=await WindowsSystemMonitor.SnapshotAsync(_lifetime.Token);
                SystemStatusReport.Text=report;
                return new(true,report);
            }
            await ExecuteAsync(command,_lifetime.Token);
            return new(true,"Windows обробив: "+request.Command);
        }catch(OperationCanceledException) {
            return new(false,"Windows: дистанційну дію скасовано.");
        }catch(Exception failure) {
            return new(false,"Windows: "+failure.Message);
        }finally {
            _busy=false;
        }
    }
}
