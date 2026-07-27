using Microsoft.JSInterop;

namespace Triker.Web.Services;

public class ShareService(IJSRuntime js)
{
    // imageDataUrl es opcional (data URL PNG con la jugada, ver ShareResult en ResultsView).
    // Devuelve 'shared', 'copied', 'copied+downloaded', 'cancelled', 'unsupported' o 'error'.
    public async Task<string> Share(string text, string? imageDataUrl = null)
    {
        try { return await js.InvokeAsync<string>("TrikerShare.share", text, imageDataUrl); }
        catch { return "error"; }
    }

    // Copia al portapapeles sin pasar por el selector de compartir.
    // Devuelve 'copied', 'unsupported' o 'error'.
    public async Task<string> Copy(string text)
    {
        try { return await js.InvokeAsync<string>("TrikerShare.copy", text); }
        catch { return "error"; }
    }
}
