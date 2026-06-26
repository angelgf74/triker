package com.angelgf.triker

import android.app.Activity
import android.webkit.JavascriptInterface

/**
 * Puente expuesto a la web como `window.AndroidAds`. Blazor lo invoca entre
 * partidas a través de js/ads.js (window.TrikerAds.interstitial()).
 */
class AdsBridge(
    private val activity: Activity,
    private val ads: InterstitialAdManager,
) {
    @JavascriptInterface
    fun showInterstitial() {
        // addJavascriptInterface ejecuta en un hilo propio; saltamos al hilo de UI.
        activity.runOnUiThread { ads.show() }
    }
}
