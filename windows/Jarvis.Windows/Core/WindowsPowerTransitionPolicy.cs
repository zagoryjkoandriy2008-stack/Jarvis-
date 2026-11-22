namespace Jarvis.Windows.Core;
/// <summary>Pure idempotent suspend/resume gate. Does not infer online
/// availability; network and signed heartbeat recovery are checked elsewhere.
/// </summary>
public sealed class WindowsPowerTransitionPolicy {
    public bool IsSuspended {get;private set;}
    public bool Suspend(){
        if(IsSuspended)return false;
        IsSuspended=true;return true;
    }
    public bool Resume(){
        if(!IsSuspended)return false;
        IsSuspended=false;return true;
    }
}
