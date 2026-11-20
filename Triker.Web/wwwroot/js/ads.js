// Puente de anuncios. En la app Android nativa, window.AndroidAds es inyectado
// por el WebView (ver android/). En la web normal no existe y todo es no-op.
window.TrikerAds = (function () {
    var lastShown = 0;
    var MIN_INTERVAL_MS = 60000; // como máximo un intersticial por minuto

    function tiene(metodo) {
        return typeof window.AndroidAds !== 'undefined'
            && typeof window.AndroidAds[metodo] === 'function';
    }

    function isAndroidApp() {
        return tiene('showInterstitial');
    }

    // Donde el consentimiento es obligatorio (EEE, Reino Unido, Suiza) hay que
    // dejar a mano una forma de cambiarlo. Solo la app sabe si lo es; las
    // versiones anteriores a la 1.4.0 no tienen este método.
    function privacyOptionsRequired() {
        try { return tiene('privacyOptionsRequired') && window.AndroidAds.privacyOptionsRequired() === true; }
        catch (e) { return false; }
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
        },

        // Enseña u oculta los elementos [data-ads-privacy] (el enlace del
        // lobby). Lo llama el lobby al pintarse y la app cuando cambia el
        // estado del consentimiento: al arrancar aún no se conoce.
        refreshPrivacyLink: function () {
            var visible = privacyOptionsRequired();
            document.querySelectorAll('[data-ads-privacy]').forEach(function (el) {
                el.hidden = !visible;
            });
            return visible;
        },

        showPrivacyOptions: function () {
            try { if (tiene('showPrivacyOptions')) window.AndroidAds.showPrivacyOptions(); }
            catch (e) { console.warn('AdMob bridge error', e); }
        }
    };
})();
