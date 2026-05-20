// Bridge entre Blazor y la capa nativa de AdMob.
//
// El wrapper Android (WebView) debe exponer window.Android con:
//   - showInterstitial()   → carga y muestra un intersticial de AdMob
//   - getBannerHeight()    → devuelve la altura del banner en píxeles CSS
//
// El wrapper Android también debe inyectar JS al cargar la página para
// registrar la altura del banner:
//   document.documentElement.style.setProperty('--ad-banner-height', h + 'px');

export function showInterstitial() {
    if (window.Android?.showInterstitial) {
        window.Android.showInterstitial();
    }
}
