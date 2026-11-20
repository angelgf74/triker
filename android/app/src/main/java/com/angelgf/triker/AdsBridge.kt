package com.angelgf.triker

import android.app.Activity
import android.webkit.JavascriptInterface

/**
 * Puente expuesto a la web como `window.AndroidAds`. Blazor lo invoca entre
 * partidas a través de js/ads.js (window.TrikerAds).
 */
class AdsBridge(
    private val activity: Activity,
    private val ads: InterstitialAdManager,
    private val consent: AdsConsent,
) {
    @JavascriptInterface
    fun showInterstitial() {
        // addJavascriptInterface ejecuta en un hilo propio; saltamos al hilo de UI.
        activity.runOnUiThread { ads.show() }
    }

    /** True si la web debe ofrecer un enlace para cambiar el consentimiento de anuncios. */
    @JavascriptInterface
    fun privacyOptionsRequired(): Boolean = consent.opcionesDePrivacidadObligatorias

    @JavascriptInterface
    fun showPrivacyOptions() {
        activity.runOnUiThread { consent.mostrarOpcionesDePrivacidad() }
    }
}
