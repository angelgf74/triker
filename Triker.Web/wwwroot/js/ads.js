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

    // Pide un intersticial a la app. Devuelve true si se ha pedido; la app
    // puede no enseñar nada (sin anuncio cargado o sin consentimiento resuelto).
    function interstitial() {
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

    // El anuncio se abre en otra pantalla de la app, encima del WebView: la
    // página pasa a oculta al abrirse (unos 50 ms después de pedirlo) y vuelve
    // a visible al cerrarse. Así se sabe cuándo ha terminado sin que la app
    // tenga que avisar.
    var SIN_ANUNCIO_MS = 1500;   // si en este tiempo no se ha tapado la página, no hubo anuncio
    var TOPE_MS = 90000;         // por si el jugador deja la app con el anuncio abierto

    function interstitialAndWait() {
        return new Promise(function (resolve) {
            if (!interstitial()) { resolve(false); return; }
            var tapada = document.hidden, hecho = false;
            function acabar(huboAnuncio) {
                if (hecho) return;
                hecho = true;
                document.removeEventListener('visibilitychange', alCambiar);
                clearTimeout(sinAnuncio);
                clearTimeout(tope);
                resolve(huboAnuncio);
            }
            function alCambiar() {
                if (document.hidden) tapada = true;
                else if (tapada) acabar(true);
            }
            document.addEventListener('visibilitychange', alCambiar);
            var sinAnuncio = setTimeout(function () { if (!tapada) acabar(false); }, SIN_ANUNCIO_MS);
            var tope = setTimeout(function () { acabar(true); }, TOPE_MS);
        });
    }

    return {
        isAndroidApp: isAndroidApp,

        // Al salir de la mesa desde los resultados: se pide y no se espera.
        interstitial: interstitial,

        // Al pulsar «Siguiente ronda»: se resuelve cuando el anuncio se ha
        // cerrado (o cuando queda claro que no lo hubo), para que la ronda
        // nueva no empiece a correr con el anuncio delante.
        interstitialAndWait: interstitialAndWait,

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
