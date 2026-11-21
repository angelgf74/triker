using Microsoft.AspNetCore.SignalR.Client;

namespace Triker.Web.Services;

// Reconexion del hub sin rendirse. La politica por defecto de SignalR hace
// cuatro intentos (a los 0, 2, 10 y 30 s) y cierra la conexion: bastaba un
// tunel o el movil bloqueado un minuto para acabar en «Conexion perdida» y
// tener que recargar la app entera, con el turno corriendo.
//
// Aqui los primeros intentos van seguidos y despues se repite cada pocos
// segundos mientras la pagina siga abierta. El tope es corto a proposito:
// SignalR no deja adelantar un intento ya programado, asi que es tambien lo
// maximo que se tarda en volver cuando la red reaparece.
public sealed class ReintentoSinFin : IRetryPolicy
{
    private static readonly TimeSpan[] Primeros =
        [TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3)];

    private static readonly TimeSpan Tope = TimeSpan.FromSeconds(5);

    public static TimeSpan Espera(long intento)
        => intento < Primeros.Length ? Primeros[intento] : Tope;

    public TimeSpan? NextRetryDelay(RetryContext retryContext)
        => Espera(retryContext.PreviousRetryCount);

    // La reconexion automatica de SignalR solo cubre una conexion que llego a
    // abrirse. Si falla la primera (se entra en la mesa justo sin red), esto la
    // sigue intentando con las mismas esperas. Devuelve false si se cancela o
    // la conexion se descarta antes de conseguirlo.
    public static async Task<bool> Arrancar(HubConnection hub, CancellationToken cancelar, long desdeIntento = 0)
    {
        for (var intento = desdeIntento; ; intento++)
        {
            try
            {
                await Task.Delay(Espera(intento), cancelar);
                await hub.StartAsync(cancelar);
                return true;
            }
            catch (OperationCanceledException) when (cancelar.IsCancellationRequested) { return false; }
            catch (ObjectDisposedException) { return false; }
            catch { /* sigue sin red o el servidor no responde: al siguiente */ }
        }
    }
}
