using System.Globalization;
using System.Speech.Recognition;
using System.Speech.Synthesis;

namespace Jarvis.Windows.Core;

/** Microphone starts ONLY after a visible button or Ctrl+Shift+J. */
public sealed class VoiceService : IDisposable {
    private readonly SpeechSynthesizer _speaker = new();
    private SpeechRecognitionEngine? _listener;
    private int _speaking;
    public VoiceService() {
        _speaker.SpeakStarted+=(_,_)=>{System.Threading.Volatile.Write(ref _speaking,1);SpeakingChanged?.Invoke(true);};
        _speaker.SpeakCompleted+=(_,_)=>{System.Threading.Volatile.Write(ref _speaking,0);SpeakingChanged?.Invoke(false);};
    }
    public bool IsListening { get; private set; }
    public bool IsSpeaking => System.Threading.Volatile.Read(ref _speaking)!=0;
    public event Action<bool>? SpeakingChanged;
    public event Action? RecognitionEnded;
    public event Action<string>? Recognized;
    public event Action<string>? Status;

    public IReadOnlyList<string> Voices =>
        _speaker.GetInstalledVoices()
            .Where(v=>v.Enabled).Select(v=>v.VoiceInfo.Name).ToArray();

    public IReadOnlyList<string> Recognizers {
        get {
            try { return SpeechRecognitionEngine.InstalledRecognizers()
                .Select(r=>r.Culture.Name).Distinct().ToArray(); }
            catch (Exception) { return Array.Empty<string>(); }
        }
    }

    public void SelectVoice(string? name) {
        if(!string.IsNullOrWhiteSpace(name) && Voices.Contains(name))
            _speaker.SelectVoice(name);
    }

    public void Say(string text) {
        if(text.Length is <1 or >12000)return;
        try {
            _speaker.SpeakAsyncCancelAll();
            _speaker.SpeakAsync(text);
        } catch(Exception ex) {
            Status?.Invoke("TTS: "+ex.Message);
        }
    }
    public void StopVoice() => _speaker.SpeakAsyncCancelAll();

    public void Start(string? selectedCulture) {
        if(IsListening)return;
        try {
            var installed=SpeechRecognitionEngine.InstalledRecognizers();
            var selected=installed.FirstOrDefault(r=>
                r.Culture.Name.Equals(selectedCulture,StringComparison.OrdinalIgnoreCase))
                ?? installed.FirstOrDefault(r=>r.Culture.Name.StartsWith("uk",StringComparison.OrdinalIgnoreCase))
                ?? installed.FirstOrDefault();
            if(selected==null) {
                Status?.Invoke("Windows не має встановленого розпізнавача мовлення. Можна вводити команди текстом.");
                return;
            }
            _listener?.Dispose();
            _listener=new SpeechRecognitionEngine(selected.Id);
            _listener.LoadGrammar(new DictationGrammar());
            _listener.SetInputToDefaultAudioDevice();
            _listener.SpeechRecognized+=(sender,args)=>{
                if(args.Result.Confidence>=0.60f &&
                    !string.IsNullOrWhiteSpace(args.Result.Text))
                    Recognized?.Invoke(args.Result.Text);
                else Status?.Invoke("Не вдалося впевнено розпізнати фразу.");
            };
            _listener.RecognizeCompleted+=(sender,args)=>{
                IsListening=false;
                Status?.Invoke("Мікрофон вимкнений.");
                RecognitionEnded?.Invoke();
            };
            IsListening=true;
            _listener.RecognizeAsync(RecognizeMode.Single);
            Status?.Invoke("Слухаю ("+selected.Culture.Name+")…");
        } catch(Exception ex) {
            IsListening=false;
            _listener?.Dispose();_listener=null;
            Status?.Invoke("Мікрофон: "+ex.Message);
        }
    }
    public void StopListening() {
        if(!IsListening)return;
        try { _listener?.RecognizeAsyncCancel(); }
        catch(InvalidOperationException) { }
        IsListening=false;
        Status?.Invoke("Мікрофон вимкнений.");
    }
    public void Dispose() {
        StopListening();_listener?.Dispose();_speaker.Dispose();
    }
}
