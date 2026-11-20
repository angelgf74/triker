package com.angelgf.triker

import android.app.Activity
import android.content.pm.ApplicationInfo
import android.util.Log
import com.google.android.ump.ConsentDebugSettings
import com.google.android.ump.ConsentInformation
import com.google.android.ump.ConsentRequestParameters
import com.google.android.ump.UserMessagingPlatform
import java.util.concurrent.atomic.AtomicBoolean

/**
 * Consentimiento de anuncios con la User Messaging Platform de Google.
 *
 * Antes la app iniciaba AdMob y pedía anuncios nada más arrancar. Ahora no se
 * pide ninguno hasta que la UMP dice que se puede: en el EEE, Reino Unido y
 * Suiza eso es después de que el jugador responda al formulario; en el resto,
 * de inmediato. El formulario es el que esté configurado en AdMob (Privacidad
 * y mensajes) para esta app; si no hay ninguno, la UMP no enseña nada.
 *
 * [alPoderPedirAnuncios] se llama una sola vez y [alCambiar] cada vez que el
 * estado se actualiza o se cierra un formulario; las dos, en el hilo de UI. El
 * resto de la clase también se usa solo desde el hilo de UI.
 */
class AdsConsent(
    private val activity: Activity,
    private val alPoderPedirAnuncios: () -> Unit,
    private val alCambiar: () -> Unit,
) {
    private val info: ConsentInformation = UserMessagingPlatform.getConsentInformation(activity)
    private val anunciosIniciados = AtomicBoolean(false)

    // La UMP pide consultar el estado una vez por arranque. Si la consulta
    // falla (se abrió la app sin red), se repite cuando vuelva a pedirse.
    private var actualizado = false
    private var consultando = false

    private val params: ConsentRequestParameters by lazy {
        ConsentRequestParameters.Builder().apply {
            // Las compilaciones de debug se comportan como si estuvieran en el
            // EEE, para poder ver el formulario desde cualquier sitio.
            if (activity.applicationInfo.flags and ApplicationInfo.FLAG_DEBUGGABLE != 0) {
                setConsentDebugSettings(
                    ConsentDebugSettings.Builder(activity)
                        .setDebugGeography(ConsentDebugSettings.DebugGeography.DEBUG_GEOGRAPHY_EEA)
                        .build()
                )
            }
        }.build()
    }

    companion object {
        private const val TAG = "TrikerAds"
    }

    /**
     * Actualiza el estado y, si hace falta, enseña el formulario. Se llama al
     * arrancar y cada vez que la web carga bien: mientras la consulta de este
     * arranque no haya salido, la repite; después no hace nada.
     */
    fun pedir() {
        actualizar {
            UserMessagingPlatform.loadAndShowConsentFormIfRequired(activity) { error ->
                if (error != null) Log.w(TAG, "Formulario de consentimiento: ${error.errorCode} ${error.message}")
                iniciarAnunciosSiSePuede()
                alCambiar()
            }
        }
        // El consentimiento de una sesión anterior permite no esperar a la red.
        iniciarAnunciosSiSePuede()
    }

    private fun actualizar(despues: () -> Unit) {
        if (actualizado || consultando) return
        consultando = true
        info.requestConsentInfoUpdate(
            activity,
            params,
            {
                consultando = false
                actualizado = true
                alCambiar()
                despues()
            },
            { error ->
                // Sin red o sin respuesta: vale lo que se decidió en una sesión anterior.
                consultando = false
                Log.w(TAG, "Estado del consentimiento no actualizado: ${error.errorCode} ${error.message}")
            },
        )
    }

    private fun iniciarAnunciosSiSePuede() {
        if (info.canRequestAds() && anunciosIniciados.compareAndSet(false, true)) {
            activity.runOnUiThread(alPoderPedirAnuncios)
        }
    }

    /** Si hay que ofrecer al jugador una forma de cambiar su decisión. */
    val opcionesDePrivacidadObligatorias: Boolean
        get() = info.privacyOptionsRequirementStatus ==
            ConsentInformation.PrivacyOptionsRequirementStatus.REQUIRED

    /**
     * Vuelve a abrir el formulario para cambiar la decisión. La UMP solo lo
     * abre si el estado se consultó en este arranque, así que antes se
     * asegura de que la consulta haya salido.
     */
    fun mostrarOpcionesDePrivacidad() {
        val abrir = {
            UserMessagingPlatform.showPrivacyOptionsForm(activity) { error ->
                if (error != null) Log.w(TAG, "Opciones de privacidad: ${error.errorCode} ${error.message}")
                alCambiar()
            }
        }
        if (actualizado) abrir() else actualizar(abrir)
    }
}
