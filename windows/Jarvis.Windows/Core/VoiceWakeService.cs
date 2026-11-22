using System.Speech.Recognition;
using System.Globalization;
namespace Jarvis.Windows.Core;

/// <summary>
/// Opt-in OFFLINE SAPI keyword listener, not cloud streaming. Requires a
/// Windows recognizer supporting the chosen language and pronunciation.
/// This is a fallback, not a substitute for Ukrainian whisper.cpp models.
/// </summary>
public sealed class VoiceWakeService : IDisposable {
    private SpeechRecognitionEngine? _engine;
    private bool _triggered;
    public bool IsArmed { get; private set; }
    public event Action? Detected;
    public event Action<string>? Status;
    public bool Start(string? culture) {
        if(IsArmed)return true;
        try {
            var recognizers=SpeechRecognitionEngine.InstalledRecognizers();
            var match=recognizers.FirstOrDefault(x=>x.Culture.Name.Equals(culture,
                 StringComparison.OrdinalIgnoreCase))
                ?? recognizers.FirstOrDefault(x=>x.Culture.Name.StartsWith("uk",
                 StringComparison.OrdinalIgnoreCase));
            if(match==null) {
                Status?.Invoke("Wake word OFF: потрібен офлайн-розпізнавач вибраної мови. "+
                    "Без мовного пакета Windows постійне слухання не вмикається.");
                return false;
            }
            var engine=new SpeechRecognitionEngine(match.Id);
            var words=new GrammarBuilder {Culture=match.Culture};
            words.Append(new Choices("джарвіс","джарвис","jarvis"));
            engine.LoadGrammar(new Grammar(words));
            engine.SetInputToDefaultAudioDevice();
            _triggered=false;_engine=engine;IsArmed=true;
            engine.SpeechRecognized+=(_,e)=>{
                if(e.Result.Confidence<0.60)return;
                var said=e.Result.Text.Trim().ToLowerInvariant();
                if(said is not ("джарвіс" or "джарвис" or "jarvis"))return;
                _triggered=true;
                try{engine.RecognizeAsyncCancel();}catch(InvalidOperationException){}
            };
            engine.RecognizeCompleted+=(_,_)=>{
                var fired=_triggered;
                _triggered=false;IsArmed=false;
                if(ReferenceEquals(_engine,engine))_engine=null;
                engine.Dispose();
                if(fired)Detected?.Invoke();
            };
            engine.RecognizeAsync(RecognizeMode.Multiple);
            Status?.Invoke("Очікую слово «Джарвіс» офлайн.");
            return true;
        }catch(Exception e) {
            IsArmed=false;
            try{_engine?.Dispose();}catch(Exception){}
            _engine=null;
            Status?.Invoke("Wake word не активний: "+e.Message);
            return false;
        }
    }
    public void Stop() {
        _triggered=false;
        var engine=_engine;
        _engine=null;IsArmed=false;
        if(engine==null)return;
        try{engine.RecognizeAsyncCancel();}catch(InvalidOperationException){}
        // The RecognizeCompleted event disposes the actual engine.
    }
    public void Dispose()=>Stop();
}
