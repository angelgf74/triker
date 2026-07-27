// Puente para compartir la jugada obtenida (texto + imagen PNG opcional). En
// la app Android nativa, window.AndroidShare es inyectado por el WebView
// (ver android/) y abre el selector nativo de Android con la imagen adjunta.
// En la web normal usa la Web Share API (con archivos si el navegador los
// soporta), y si no, copia el texto al portapapeles y descarga la imagen.
window.TrikerShare = (function () {
    function isAndroidApp() {
        return typeof window.AndroidShare !== 'undefined'
            && typeof window.AndroidShare.share === 'function';
    }

    function dataUrlToFile(dataUrl, filename) {
        const parts = dataUrl.split(',');
        const mimeMatch = /data:(.*?);base64/.exec(parts[0]);
        const mime = mimeMatch ? mimeMatch[1] : 'image/png';
        const bin = atob(parts[1]);
        const bytes = new Uint8Array(bin.length);
        for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
        return new File([bytes], filename, { type: mime });
    }

    function downloadImage(dataUrl, filename) {
        const a = document.createElement('a');
        a.href = dataUrl;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        a.remove();
    }

    return {
        // imageDataUrl es opcional (data URL PNG con la jugada, ver js/cardImage.js).
        // Devuelve 'shared', 'copied', 'copied+downloaded', 'cancelled' o 'unsupported'.
        share: async function (text, imageDataUrl) {
            if (isAndroidApp()) {
                try {
                    const base64 = imageDataUrl ? imageDataUrl.split(',')[1] : null;
                    window.AndroidShare.share(text, base64);
                    return 'shared';
                } catch (e) {
                    console.warn('AndroidShare bridge error', e);
                }
            }

            if (navigator.share) {
                try {
                    if (imageDataUrl && navigator.canShare) {
                        const file = dataUrlToFile(imageDataUrl, 'triker-jugada.png');
                        if (navigator.canShare({ files: [file] })) {
                            await navigator.share({ text: text, files: [file] });
                            return 'shared';
                        }
                    }
                    await navigator.share({ text: text });
                    return 'shared';
                } catch (e) {
                    if (e && e.name === 'AbortError') return 'cancelled';
                    console.warn('navigator.share error', e);
                }
            }

            if (navigator.clipboard && navigator.clipboard.writeText) {
                try {
                    await navigator.clipboard.writeText(text);
                    if (imageDataUrl) {
                        try {
                            downloadImage(imageDataUrl, 'triker-jugada.png');
                            return 'copied+downloaded';
                        } catch (e) {
                            console.warn('download error', e);
                        }
                    }
                    return 'copied';
                } catch (e) {
                    console.warn('clipboard error', e);
                }
            }
            return 'unsupported';
        },

        // Copia texto sin abrir el selector de compartir (para el codigo privado).
        // navigator.clipboard no existe fuera de contextos seguros ni en algunos
        // WebView, de ahi el fallback con textarea + execCommand.
        copy: async function (text) {
            if (navigator.clipboard && navigator.clipboard.writeText) {
                try {
                    await navigator.clipboard.writeText(text);
                    return 'copied';
                } catch (e) {
                    console.warn('clipboard error', e);
                }
            }

            try {
                const ta = document.createElement('textarea');
                ta.value = text;
                ta.setAttribute('readonly', '');
                ta.style.position = 'fixed';
                ta.style.opacity = '0';
                document.body.appendChild(ta);
                ta.select();
                const ok = document.execCommand('copy');
                ta.remove();
                if (ok) return 'copied';
            } catch (e) {
                console.warn('execCommand copy error', e);
            }
            return 'unsupported';
        }
    };
})();
