package com.angelgf.triker

import android.annotation.SuppressLint
import android.content.ActivityNotFoundException
import android.content.Intent
import android.content.res.ColorStateList
import android.graphics.Bitmap
import android.net.Uri
import android.os.Bundle
import android.util.Log
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.ViewGroup.LayoutParams.MATCH_PARENT
import android.view.ViewGroup.LayoutParams.WRAP_CONTENT
import android.webkit.RenderProcessGoneDetail
import android.webkit.WebChromeClient
import android.webkit.WebResourceError
import android.webkit.WebResourceRequest
import android.webkit.WebResourceResponse
import android.webkit.WebSettings
import android.webkit.WebView
import android.webkit.WebViewClient
import android.widget.Button
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.TextView
import androidx.activity.ComponentActivity
import androidx.activity.OnBackPressedCallback
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat
import com.google.android.gms.ads.MobileAds

/**
 * Actividad única que encapsula la app web de Triker en un WebView y muestra
 * intersticiales de AdMob entre partidas (disparados desde Blazor vía window.AndroidAds).
 */
class MainActivity : ComponentActivity() {

    private lateinit var root: FrameLayout
    private lateinit var webView: WebView
    private lateinit var errorView: View
    private lateinit var errorMessage: TextView
    private lateinit var ads: InterstitialAdManager
    private lateinit var consent: AdsConsent
    private lateinit var appUrl: String

    // La carga del documento principal falló: el aviso se queda hasta que una
    // carga posterior termine bien.
    private var mainFrameFailed = false

    companion object {
        private const val TAG = "Triker"
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        appUrl = getString(R.string.app_url)

        // Nada de AdMob hasta que haya consentimiento (ver AdsConsent).
        ads = InterstitialAdManager(this, getString(R.string.admob_interstitial_id))
        consent = AdsConsent(
            activity = this,
            alPoderPedirAnuncios = {
                MobileAds.initialize(this) {}
                ads.enabled = true
                ads.preload()
            },
            // La web decide si enseña el enlace "Privacidad de los anuncios"
            // (js/ads.js); el estado se conoce después de que pinte el lobby.
            alCambiar = {
                webView.evaluateJavascript(
                    "window.TrikerAds && TrikerAds.refreshPrivacyLink && TrikerAds.refreshPrivacyLink()", null
                )
            },
        )

        root = FrameLayout(this).apply { setBackgroundColor(getColor(R.color.background)) }
        webView = createWebView()
        errorView = createErrorView()
        root.addView(webView, MATCH_PARENT, MATCH_PARENT)
        root.addView(errorView, MATCH_PARENT, MATCH_PARENT)
        setContentView(root)

        // Desde Android 15 la app se dibuja de borde a borde: sin este margen
        // la cabecera quedaba debajo de la barra de estado, el final de la
        // página bajo la de navegación y los campos bajo el teclado.
        ViewCompat.setOnApplyWindowInsetsListener(root) { view, insets ->
            val bars = insets.getInsets(
                WindowInsetsCompat.Type.systemBars() or
                    WindowInsetsCompat.Type.displayCutout() or
                    WindowInsetsCompat.Type.ime()
            )
            view.setPadding(bars.left, bars.top, bars.right, bars.bottom)
            WindowInsetsCompat.CONSUMED
        }

        webView.loadUrl(appUrl)
        consent.pedir()

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

    @SuppressLint("SetJavaScriptEnabled")
    private fun createWebView(): WebView = WebView(this).apply {
        setBackgroundColor(getColor(R.color.background))
        settings.apply {
            javaScriptEnabled = true
            domStorageEnabled = true           // localStorage que usa Blazor
            cacheMode = WebSettings.LOAD_DEFAULT
            mediaPlaybackRequiresUserGesture = false  // permite los sonidos del juego
        }
        webViewClient = TrikerWebViewClient()
        webChromeClient = WebChromeClient()
        // Expone window.AndroidAds y window.AndroidShare a la web.
        addJavascriptInterface(AdsBridge(this@MainActivity, ads, consent), "AndroidAds")
        addJavascriptInterface(ShareBridge(this@MainActivity), "AndroidShare")
    }

    private inner class TrikerWebViewClient : WebViewClient() {
        private val appHost = Uri.parse(appUrl).host

        // Solo la web de Triker se abre dentro del WebView. Cualquier otra
        // dirección va al navegador: dentro de la app tendría a su alcance
        // window.AndroidAds y window.AndroidShare, que se exponen a la
        // página que esté cargada, sea cual sea.
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

        override fun onPageStarted(view: WebView, url: String?, favicon: Bitmap?) {
            mainFrameFailed = false
        }

        // Sin red (o con el servidor caído) el WebView enseñaba su página de
        // error en blanco y en inglés, sin forma de reintentar.
        override fun onReceivedError(view: WebView, request: WebResourceRequest, error: WebResourceError) {
            if (request.isForMainFrame) showError(R.string.error_message)
        }

        override fun onReceivedHttpError(view: WebView, request: WebResourceRequest, response: WebResourceResponse) {
            if (request.isForMainFrame && response.statusCode >= 500) showError(R.string.error_message)
        }

        override fun onPageFinished(view: WebView, url: String?) {
            if (mainFrameFailed) return
            hideError()
            // Si la app arrancó sin red, la consulta del consentimiento no
            // salió: que la web cargue es la señal de que ya hay conexión.
            consent.pedir()
        }

        // Android puede matar el proceso del WebView para recuperar memoria
        // (lo normal, con la app en segundo plano) o este puede fallar. Sin
        // atenderlo, la app entera se cerraba. El WebView muerto no se puede
        // reutilizar: se cambia por uno nuevo.
        override fun onRenderProcessGone(view: WebView, detail: RenderProcessGoneDetail): Boolean {
            Log.w(TAG, "Proceso del WebView perdido (fallo=${detail.didCrash()})")
            root.removeView(view)
            view.destroy()
            webView = createWebView()
            root.addView(webView, 0, FrameLayout.LayoutParams(MATCH_PARENT, MATCH_PARENT))
            if (detail.didCrash()) {
                // Si falló por lo que estaba cargado, recargar solo podría
                // repetirlo en bucle: que decida el jugador.
                showError(R.string.error_crash_message)
            } else {
                webView.loadUrl(appUrl)
            }
            return true
        }
    }

    private fun showError(message: Int) {
        mainFrameFailed = true
        errorMessage.setText(message)
        webView.visibility = View.INVISIBLE
        errorView.visibility = View.VISIBLE
    }

    private fun hideError() {
        errorView.visibility = View.GONE
        webView.visibility = View.VISIBLE
    }

    private fun retry() {
        // Tras un fallo de carga el WebView recuerda la dirección que falló;
        // uno recién creado no tiene ninguna.
        if (webView.url.isNullOrEmpty()) webView.loadUrl(appUrl) else webView.reload()
    }

    private fun createErrorView(): View {
        fun dp(value: Int) = TypedValue.applyDimension(
            TypedValue.COMPLEX_UNIT_DIP, value.toFloat(), resources.displayMetrics
        ).toInt()

        val title = TextView(this).apply {
            setText(R.string.error_title)
            setTextColor(getColor(R.color.accent))
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 22f)
            gravity = Gravity.CENTER
        }
        errorMessage = TextView(this).apply {
            setText(R.string.error_message)
            setTextColor(getColor(R.color.text))
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 16f)
            gravity = Gravity.CENTER
            setPadding(0, dp(12), 0, dp(24))
        }
        val button = Button(this).apply {
            setText(R.string.error_retry)
            isAllCaps = false
            setTextColor(getColor(R.color.background))
            backgroundTintList = ColorStateList.valueOf(getColor(R.color.accent))
            minimumHeight = dp(48)
            setPadding(dp(32), 0, dp(32), 0)
            setOnClickListener { retry() }
        }
        return LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER
            setBackgroundColor(getColor(R.color.background))
            setPadding(dp(32), dp(32), dp(32), dp(32))
            isClickable = true   // los toques no pasan al WebView de debajo
            visibility = View.GONE
            addView(title, WRAP_CONTENT, WRAP_CONTENT)
            addView(errorMessage, WRAP_CONTENT, WRAP_CONTENT)
            addView(button, WRAP_CONTENT, WRAP_CONTENT)
        }
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
