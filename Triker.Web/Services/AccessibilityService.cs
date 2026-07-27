using Microsoft.JSInterop;

namespace Triker.Web.Services;

// Modo daltónico: el color es 1 de los 3 atributos que puntúan, pero es el
// único que no tiene glifo propio (la letra y la figura sí). Con la paleta
// rojo/verde/naranja/marrón, un jugador con daltonismo rojo-verde no puede
// decidir si tres cartas comparten color, que es justo como se puntúa.
//
// El estado vive en una clase del <body> y no en un parámetro de componente:
// asi CardView no necesita re-renderizarse al cambiar el modo, lo hace el CSS.
public class AccessibilityService(IJSRuntime js)
{
    public const string StorageKey = "triker_colorblind";
    public const string BodyClass = "colorblind";

    private bool _colorBlind;
    private bool _initialized;

    public bool ColorBlind => _colorBlind;

    // index.html ya aplica la clase antes de arrancar Blazor para que no haya
    // parpadeo; aqui solo recuperamos el valor para que el interruptor lo refleje.
    public async Task EnsureInitializedAsync()
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            var stored = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            _colorBlind = stored == "1";
        }
        catch { }
    }

    public async Task SetColorBlind(bool value)
    {
        _colorBlind = value;
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", StorageKey, value ? "1" : "0");
            await js.InvokeVoidAsync("document.body.classList.toggle", BodyClass, value);
        }
        catch { }
    }

    public Task Toggle() => SetColorBlind(!_colorBlind);
}
