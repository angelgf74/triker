using Microsoft.JSInterop;

namespace Triker.Web.Services;

public class AudioService(IJSRuntime js)
{
    private bool _enabled = true;
    private bool _initialized;

    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; _ = SetEnabledJs(value); }
    }

    // Lee el estado persistido en localStorage (una sola vez). El setter ya
    // se encarga de guardar los cambios posteriores.
    public async Task EnsureInitializedAsync()
    {
        if (_initialized) return;
        _initialized = true;
        try { _enabled = await js.InvokeAsync<bool>("trikerAudio.getEnabled"); }
        catch { }
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
