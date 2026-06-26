# El SDK de Google Mobile Ads trae sus propias reglas consumer; no hace falta añadir nada.
# Conservamos el puente JavaScript expuesto al WebView para que R8 no lo elimine/ofusque.
-keepclassmembers class com.angelgf.triker.AdsBridge {
    @android.webkit.JavascriptInterface <methods>;
}
