using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
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
    private readonly AiClient _ai=new();
    private readonly FinanceService _finance;
    private DeviceRelayClient? _relay;
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
            MessageBox.Show(ex.Message,"JARVIS Windows",MessageBoxButton.OK,MessageBoxImage.Error);
            throw;
        }
        _finance=new FinanceService(_state,_store);
        _relay=new DeviceRelayClient(_store,_state.Settings);
        _relay.Status+=text=>Dispatcher.BeginInvoke(new Action(()=>Status(text)));
        _relay.CommandReceived+=HandleRemoteOnUiAsync;
        _=_relay.ListenAsync(_lifetime.Token);
        WorkerUrl.Text=_state.Settings.WorkerUrl;
        SpeakReplies.IsChecked=_state.Settings.SpeakReplies;
        StayOnTop.IsChecked=_state.Settings.OnTop;
        EnableDeviceRelay.IsChecked=_state.Settings.EnableDeviceRelay;
        ShowFloatingOrb.IsChecked=_state.Settings.ShowFloatingOrb;
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
        RefreshFinance();
        RefreshMacros();
        Append("JARVIS","Windows-версія запущена. Для голосу натисни 🎙 або Ctrl+Shift+J. Якщо українського розпізнавача Windows немає, користуйся текстом.");
        _floating=new FloatingOrb();
        _floating.ListenRequested+=ToggleListening;
        _floating.TargetRequested+=SetTarget;
        _floating.HideRequested+=()=>_floating.Hide();
        _floating.ShowRequested+=()=>{Show();WindowState=WindowState.Normal;Activate();};
        _floating.SetTarget(_target);
        if(_state.Settings.ShowFloatingOrb)_floating.Show();
        CurrentDeviceLabel.Text=DeviceRouting.Label(_target);
        SourceInitialized+=OnSourceInitialized;
        Closing+=OnClosing;
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
        if(_source!=null) {
            if(_hotkeyRegistered)UnregisterHotKey(_source.Handle,HotkeyId);
            _source.RemoveHook(HotkeyHook);
        }
        _lifetime.Cancel();
        _operation?.Cancel();
        _voice.Dispose();
        _floating?.Close();
        _relay?.Dispose();
        _ai.Dispose();
        _lifetime.Dispose();
    }

    private void ToggleListening() {
        if(_voice.IsListening) {
            _voice.StopListening(); MicButton.Content="🎙 Слухати";
        } else {
            _voice.Start(RecognizerChoices.SelectedItem as string);
            MicButton.Content=_voice.IsListening?"■ Слухаю…":"🎙 Слухати";
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
    private void Reply(string detail,bool speak=true) {
        Append("JARVIS",detail);
        Status(detail.Length<=180?detail:detail[..177]+"…");
        if(speak && SpeakReplies.IsChecked==true)_voice.Say(detail[..Math.Min(1200,detail.Length)]);
    }

    private async Task HandleUtteranceAsync(string raw) {
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
                var destination=DeviceRouting.EffectiveTarget(_target,cmd.Kind);
                if(destination!=JarvisDevice.Computer) {
                    if(!_state.Settings.EnableDeviceRelay || _relay==null)
                        throw new InvalidOperationException("Увімкни Cloudflare Relay у Налаштуваннях для керування іншими пристроями.");
                    Status("Передаю команду: "+DeviceRouting.Label(destination)+"…");
                    var response=await _relay.SendCommandAsync(JarvisDevice.Computer,
                        destination,raw,operation.Token);
                    if(cmd.Kind is CommandKind.FinanceAdd or CommandKind.FinanceReport &&
                       response.Ok)FinanceReport.Text=response.Detail;
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
            case CommandKind.NoteAdd:
                if(command.Argument.Length>2000)throw new InvalidOperationException("Нотатка надто довга.");
                _state.Notes+=(_state.Notes.Length>0?"\r\n":"")+
                    DateTime.Now.ToString("dd.MM HH:mm")+" · "+command.Argument;
                _store.Save(_state);NotesText.Text=_state.Notes;
                Reply("Нотатку збережено локально.");break;
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
        "Локальні записи ранньої Preview (не синхронізовані, лише перегляд):\\n"+
        _finance.Report()+"\\nДля єдиного JARVIS Finance використовуй з'єднання з Pixel.";
    private void RefreshMacros() {
        MacroList.ItemsSource=null;
        MacroList.ItemsSource=_state.Macros.Select(m=>
            m.Trigger+" → "+string.Join(" → ",m.Actions)).ToList();
    }
    private async void AddFinanceButton_Click(object sender,RoutedEventArgs e) {
        var parsed=CommandRouter.Parse(FinanceInput.Text);
        if(parsed.Kind!=CommandKind.FinanceAdd) {
            Reply("Приклад: витратив 150 готівка транспорт автобус",false);return;
        }
        await HandleUtteranceAsync(FinanceInput.Text);
        FinanceInput.Clear();
    }
    private void RefreshFinanceButton_Click(object sender,RoutedEventArgs e)=>RefreshFinance();
    private void SaveNotesButton_Click(object sender,RoutedEventArgs e) {
        _state.Notes=NotesText.Text;_store.Save(_state);
        Status("Нотатки збережено.");
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
    private void SaveSettingsButton_Click(object sender,RoutedEventArgs e) {
        try {
            var url=WorkerUrl.Text.Trim();
            if(url.Length>0 && !AiClient.ValidEndpoint(url))
                throw new InvalidOperationException("Адреса ШІ має бути HTTPS із /chat без параметрів.");
            if(WorkerToken.Password.Length>0)_store.SaveWorkerToken(WorkerToken.Password);
            _state.Settings.WorkerUrl=url;
            _state.Settings.SpeakReplies=SpeakReplies.IsChecked==true;
            _state.Settings.OnTop=StayOnTop.IsChecked==true;
            _state.Settings.EnableDeviceRelay=EnableDeviceRelay.IsChecked==true;
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
    private async void CheckDevices_Click(object sender,RoutedEventArgs e) {
        if(_relay==null || !_state.Settings.EnableDeviceRelay) {
            DeviceStatusReport.Text="Включи авторизований Relay у Налаштуваннях, збережи токен Worker.";
            return;
        }
        try {
            var phone=await _relay.IsOnlineAsync(JarvisDevice.Phone,_lifetime.Token);
            var watch=await _relay.IsOnlineAsync(JarvisDevice.Watch,_lifetime.Token);
            DeviceStatusReport.Text="🖥 Windows: працює\n📱 Pixel: "+
                (phone?"з'єднаний":"офлайн")+"\n⌚ Watch7: "+
                (watch?"з'єднаний":"офлайн")+
                "\nЗв'язок потребує активного Android-агента та оновленого Cloudflare Worker.";
        }catch(Exception error){DeviceStatusReport.Text="Relay: "+error.Message;}
    }

    /** Execute remote commands only from the authenticated private relay,
     * not from AI output. Never remotely type into another app, run macros,
     * launch arbitrary programs, or mutate finance on this Windows machine. */
    private async Task<DeviceRelayClient.RemoteResult> HandleRemoteOnUiAsync(
        DeviceRelayClient.RemoteCommand request) {
        var work=await Dispatcher.InvokeAsync(()=>ExecuteRemoteAsync(request));
        return await work;
    }
    private async Task<DeviceRelayClient.RemoteResult> ExecuteRemoteAsync(
        DeviceRelayClient.RemoteCommand request) {
        if(!_state.Settings.EnableDeviceRelay)
            return new(false,"Користувач Windows вимкнув дистанційне керування.");
        if(_busy)return new(false,"JARVIS Windows зайнятий.");
        if(request.Origin is not ("phone" or "watch") ||
           request.Target!="computer" || request.Command.Length>250)
            return new(false,"Джерело або команда не дозволені.");
        var command=CommandRouter.Parse(request.Command);
        if(command.Kind is not (CommandKind.OpenApp or CommandKind.OpenSettings
           or CommandKind.Volume or CommandKind.MediaToggle or CommandKind.MediaNext
           or CommandKind.MediaPrevious or CommandKind.Speak or CommandKind.Timer
           or CommandKind.WebSearch or CommandKind.SpotifySearch
           or CommandKind.SpotifyPlaylist))
            return new(false,"Команда не дозволена для дистанційного Windows. "+
                "Фінанси завжди виконуються на Pixel.");
        try {
            await ExecuteAsync(command,_lifetime.Token);
            return new(true,"Windows обробив: "+request.Command);
        }catch(Exception failure) {
            return new(false,"Windows: "+failure.Message);
        }
    }

}
