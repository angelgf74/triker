package com.angelgf.triker

import android.annotation.SuppressLint
import android.content.ActivityNotFoundException
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.webkit.WebChromeClient
import android.webkit.WebResourceRequest
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
            // Solo la web de Triker se abre dentro del WebView. Cualquier otra
            // dirección va al navegador: dentro de la app tendría a su alcance
            // window.AndroidAds y window.AndroidShare, que se exponen a la
            // página que esté cargada, sea cual sea.
            val appHost = Uri.parse(getString(R.string.app_url)).host
            webViewClient = object : WebViewClient() {
                override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean {
                    if (!request.isForMainFrame) return false
                    val url = request.url
                    if (url.scheme == "https" && url.host == appHost) return false
                    try {
                        startActivity(Intent(Intent.ACTION_VIEW, url))
                    } catch (_: ActivityNotFoundException) {
                        // Nada que la abra: mejor no navegar que abrirla aquí.
                    }
                    return true
                }
            }
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
