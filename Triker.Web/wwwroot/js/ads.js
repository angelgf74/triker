// Puente de anuncios. En la app Android nativa, window.AndroidAds es inyectado
// por el WebView (ver android/). En la web normal no existe y todo es no-op.
window.TrikerAds = (function () {
    var lastShown = 0;
    var MIN_INTERVAL_MS = 60000; // como máximo un intersticial por minuto

    function isAndroidApp() {
        return typeof window.AndroidAds !== 'undefined'
            && typeof window.AndroidAds.showInterstitial === 'function';
    }

    return {
        isAndroidApp: isAndroidApp,

        // Llamado por Blazor entre partidas. Solo muestra anuncio dentro de la app Android.
        interstitial: function () {
            if (!isAndroidApp()) return false;
            var now = Date.now();
            if (now - lastShown < MIN_INTERVAL_MS) return false;
            lastShown = now;
            try {
                window.AndroidAds.showInterstitial();
                return true;
            } catch (e) {
                console.warn('AdMob bridge error', e);
                return false;
            }
        }
    };
})();
