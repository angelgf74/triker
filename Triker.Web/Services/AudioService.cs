using Microsoft.JSInterop;

namespace Triker.Web.Services;

public class AudioService(IJSRuntime js)
{
    public bool Enabled { get; set; } = true;

    public async Task PlayCardClick()  => await Play("cardClick");
    public async Task PlayDiscard()    => await Play("discard");
    public async Task PlayYourTurn()   => await Play("yourTurn");
    public async Task PlayWinRound()   => await Play("winRound");
    public async Task PlayLoseRound()  => await Play("loseRound");
    public async Task PlayChampion()   => await Play("champion");

    private async Task Play(string fn)
    {
        if (!Enabled) return;
        try { await js.InvokeVoidAsync($"trikerAudio.{fn}"); }
        catch { }
    }
}
