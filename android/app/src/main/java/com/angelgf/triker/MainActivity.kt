package com.angelgf.triker

import android.annotation.SuppressLint
import android.os.Bundle
import android.webkit.WebChromeClient
import android.webkit.WebSettings
import android.webkit.WebView
import android.webkit.WebViewClient
import androidx.activity.ComponentActivity
import androidx.activity.OnBackPressedCallback
import com.google.android.gms.ads.MobileAds

/**
 * Actividad única que encapsula la app web de Triker en un WebView y muestra
 * intersticiales de AdMob entre partidas (disparados desde Blazor vía window.AndroidAds).
 */
class MainActivity : ComponentActivity() {

    private lateinit var webView: WebView
    private lateinit var ads: InterstitialAdManager

    @SuppressLint("SetJavaScriptEnabled")
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        // Inicializa el SDK de Google Mobile Ads (AdMob).
        MobileAds.initialize(this) {}

        ads = InterstitialAdManager(this, getString(R.string.admob_interstitial_id))
        ads.preload()

        webView = WebView(this).apply {
            settings.apply {
                javaScriptEnabled = true
                domStorageEnabled = true           // localStorage que usa Blazor
                cacheMode = WebSettings.LOAD_DEFAULT
                mediaPlaybackRequiresUserGesture = false  // permite los sonidos del juego
            }
            // Mantiene la navegación dentro del WebView (no abre el navegador externo).
            webViewClient = WebViewClient()
            webChromeClient = WebChromeClient()
            // Expone window.AndroidAds y window.AndroidShare a la web.
            addJavascriptInterface(AdsBridge(this@MainActivity, ads), "AndroidAds")
            addJavascriptInterface(ShareBridge(this@MainActivity), "AndroidShare")
        }
        setContentView(webView)

        webView.loadUrl(getString(R.string.app_url))

        // El botón "atrás" navega en el historial del WebView; si no hay, sale de la app.
        onBackPressedDispatcher.addCallback(this, object : OnBackPressedCallback(true) {
            override fun handleOnBackPressed() {
                if (webView.canGoBack()) {
                    webView.goBack()
                } else {
                    isEnabled = false
                    onBackPressedDispatcher.onBackPressed()
                }
            }
        })
    }

    override fun onPause() {
        super.onPause()
        webView.onPause()
    }

    override fun onResume() {
        super.onResume()
        webView.onResume()
    }

    override fun onDestroy() {
        webView.destroy()
        super.onDestroy()
    }
}
