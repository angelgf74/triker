package com.angelgf.triker

import android.app.Activity
import android.os.SystemClock
import android.util.Log
import com.google.android.gms.ads.AdError
import com.google.android.gms.ads.AdRequest
import com.google.android.gms.ads.FullScreenContentCallback
import com.google.android.gms.ads.LoadAdError
import com.google.android.gms.ads.interstitial.InterstitialAd
import com.google.android.gms.ads.interstitial.InterstitialAdLoadCallback

/**
 * Carga y muestra anuncios intersticiales, precargando siempre el siguiente y
 * aplicando un intervalo mínimo entre apariciones para no saturar al jugador.
 */
class InterstitialAdManager(
    private val activity: Activity,
    private val adUnitId: String,
) {
    private var ad: InterstitialAd? = null
    private var loading = false
    private var lastShownAt = 0L

    companion object {
        private const val TAG = "TrikerAds"
        private const val MIN_INTERVAL_MS = 60_000L
    }

    /** Precarga un intersticial si no hay uno listo ni una carga en curso. */
    fun preload() {
        if (loading || ad != null) return
        loading = true
        InterstitialAd.load(
            activity,
            adUnitId,
            AdRequest.Builder().build(),
            object : InterstitialAdLoadCallback() {
                override fun onAdLoaded(loaded: InterstitialAd) {
                    ad = loaded
                    loading = false
                }

                override fun onAdFailedToLoad(error: LoadAdError) {
                    Log.w(TAG, "Intersticial no cargado: ${error.message}")
                    ad = null
                    loading = false
                }
            }
        )
    }

    /** Muestra el intersticial si está listo y ha pasado el cooldown. Hilo de UI. */
    fun show() {
        val now = SystemClock.elapsedRealtime()
        if (lastShownAt != 0L && now - lastShownAt < MIN_INTERVAL_MS) return

        val current = ad
        if (current == null) {
            preload() // no había anuncio listo; lo dejamos listo para la próxima vez
            return
        }

        current.fullScreenContentCallback = object : FullScreenContentCallback() {
            override fun onAdShowedFullScreenContent() {
                lastShownAt = SystemClock.elapsedRealtime()
            }

            override fun onAdDismissedFullScreenContent() {
                ad = null
                preload() // precarga el siguiente
            }

            override fun onAdFailedToShowFullScreenContent(e: AdError) {
                Log.w(TAG, "Intersticial no mostrado: ${e.message}")
                ad = null
                preload()
            }
        }
        current.show(activity)
    }
}
