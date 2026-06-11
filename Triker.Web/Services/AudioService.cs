using Microsoft.JSInterop;

namespace Triker.Web.Services;

public class AudioService(IJSRuntime js)
{
    private bool _enabled = true;
    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; _ = SetEnabledJs(value); }
    }

    public async Task PlayYourTurn()  => await Play("yourTurn");
    public async Task PlayWinRound()  => await Play("winRound");
    public async Task PlayLoseRound() => await Play("loseRound");
    public async Task PlayChampion()  => await Play("champion");

    private async Task Play(string fn)
    {
        if (!_enabled) return;
        try { await js.InvokeVoidAsync($"trikerAudio.{fn}"); }
        catch { }
    }

    private async Task SetEnabledJs(bool val)
    {
        try { await js.InvokeVoidAsync("trikerAudio.setEnabled", val); }
        catch { }
    }
}
