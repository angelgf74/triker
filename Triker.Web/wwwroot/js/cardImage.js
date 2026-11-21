// Genera una imagen PNG (data URL) con la jugada obtenida, para compartirla
// junto al texto (ver js/share.js). Dibuja directamente sobre un <canvas>
// fuera de pantalla, sin depender del DOM ni de librerías externas.
//
// Lleva el aspecto de la app: fondo liso, las cartas sobre el tapete, la
// tipografía del logo y los grupos marcados como en resultados. Sin emojis: en
// un <canvas> los pinta el sistema, distintos en cada móvil.
window.TrikerHandImage = (function () {
    const FUENTE = '"Nunito Sans", "Segoe UI", system-ui, sans-serif';

    // Los colores salen de css/app.css; los de reserva son los mismos valores.
    function color(nombre, reserva) {
        const v = getComputedStyle(document.documentElement).getPropertyValue(nombre).trim();
        return v || reserva;
    }

    function roundRect(ctx, x, y, w, h, r) {
        ctx.beginPath();
        ctx.moveTo(x + r, y);
        ctx.arcTo(x + w, y, x + w, y + h, r);
        ctx.arcTo(x + w, y + h, x, y + h, r);
        ctx.arcTo(x, y + h, x, y, r);
        ctx.arcTo(x, y, x + w, y, r);
        ctx.closePath();
    }

    // El logo, letra a letra: la primera K va en espejo, como en la app.
    function logo(ctx, centro, y) {
        const letras = ['T', 'R', 'I', 'K', 'K', 'E', 'R'], espejo = 3, hueco = 9;
        ctx.font = `900 40px ${FUENTE}`;
        ctx.textAlign = 'left';
        const anchos = letras.map((l) => ctx.measureText(l).width);
        const total = anchos.reduce((a, b) => a + b, 0) + hueco * (letras.length - 1);
        let x = centro - total / 2;
        const degradado = ctx.createLinearGradient(x, 0, x + total, 0);
        degradado.addColorStop(0, '#f0c040');
        degradado.addColorStop(1, '#ff6b35');
        ctx.fillStyle = degradado;
        letras.forEach((l, i) => {
            if (i === espejo) {
                ctx.save();
                ctx.translate(x + anchos[i], 0);
                ctx.scale(-1, 1);
                ctx.fillStyle = '#f8a03a';   // el degradado, visto en espejo, iria al reves
                ctx.fillText(l, 0, y);
                ctx.restore();
            } else {
                ctx.fillText(l, x, y);
            }
            x += anchos[i] + hueco;
        });
    }

    // cards: [{ letter, figure, color, groupColor }] — groupColor es null si la carta no está agrupada.
    // detalle: lo que va junto al nombre del jugador («jugada nº 5 de 16»).
    async function render(cards, handName, detalle, playerName) {
        // La fuente ya está cargada en la página, pero un <canvas> no espera por ella.
        try { await Promise.all([document.fonts.load(`900 40px ${FUENTE}`), document.fonts.load(`700 26px ${FUENTE}`)]); } catch (e) { /* se dibuja con la de reserva */ }

        const fondo = color('--fondo', '#1a1a2e'), tapete = color('--tapete', '#16324a');
        const texto = color('--texto', '#f3f2ec'), suave = color('--texto-suave', '#a8abc6');

        const cw = 130, ch = 190, gap = 16, pad = 36, borde = 20;
        const headerH = 136, barraH = 22, footerH = 50;
        const n = Math.max(cards.length, 1);
        const width = pad * 2 + n * cw + (n - 1) * gap;
        const height = pad + headerH + ch + barraH + borde + footerH;

        const canvas = document.createElement('canvas');
        canvas.width = width;
        canvas.height = height;
        const ctx = canvas.getContext('2d');

        ctx.fillStyle = fondo;
        ctx.fillRect(0, 0, width, height);

        logo(ctx, width / 2, pad + 34);

        ctx.textAlign = 'center';
        ctx.fillStyle = texto;
        ctx.font = `800 28px ${FUENTE}`;
        ctx.fillText(handName || '', width / 2, pad + 80);

        ctx.fillStyle = suave;
        ctx.font = `600 19px ${FUENTE}`;
        const sub = playerName ? `${playerName} · ${detalle}` : `${detalle}`;
        ctx.fillText(sub, width / 2, pad + 108);

        // El tapete, detrás de las cartas.
        const y = pad + headerH;
        roundRect(ctx, pad - borde, y - borde, width - 2 * (pad - borde), ch + barraH + 2 * borde, 24);
        ctx.fillStyle = tapete;
        ctx.fill();

        let x = pad;
        (cards || []).forEach(function (c) {
            roundRect(ctx, x, y, cw, ch, 14);
            ctx.fillStyle = '#ffffff';
            ctx.fill();
            ctx.strokeStyle = '#dddddd';
            ctx.lineWidth = 2;
            ctx.stroke();

            // La marca de grupo de los resultados: contorno y barra debajo. El segundo
            // grupo (blanco) va a trazos, para distinguirse sin depender del color.
            if (c.groupColor) {
                const segundo = c.groupColor.toLowerCase() === '#ffffff';
                ctx.save();
                ctx.strokeStyle = c.groupColor;
                ctx.lineWidth = 4;
                if (segundo) ctx.setLineDash([10, 7]);
                roundRect(ctx, x - 4, y - 4, cw + 8, ch + 8, 17);
                ctx.stroke();
                ctx.restore();
                roundRect(ctx, x, y + ch + 11, cw, 6, 3);
                ctx.fillStyle = c.groupColor;
                ctx.fill();
            }

            ctx.fillStyle = c.color;
            ctx.textAlign = 'left';
            ctx.font = `900 24px ${FUENTE}`;
            ctx.fillText(c.letter, x + 12, y + 32);
            // La misma letra, del revés, en la esquina contraria.
            ctx.save();
            ctx.translate(x + cw - 12, y + ch - 32);
            ctx.rotate(Math.PI);
            ctx.fillText(c.letter, 0, 0);
            ctx.restore();

            // La figura, con la fuente del sistema: no está en el subconjunto de la tipografía.
            ctx.textAlign = 'center';
            ctx.font = '64px system-ui, sans-serif';
            ctx.fillText(c.figure, x + cw / 2, y + ch / 2 + 22);

            x += cw + gap;
        });

        ctx.fillStyle = suave;
        ctx.font = `600 17px ${FUENTE}`;
        ctx.textAlign = 'center';
        ctx.fillText('triker-app.angelgf.com.es', width / 2, height - 20);

        return canvas.toDataURL('image/png');
    }

    return { render: render };
})();
