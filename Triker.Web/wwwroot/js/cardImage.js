// Genera una imagen PNG (data URL) con la jugada obtenida, para compartirla
// junto al texto (ver js/share.js). Dibuja directamente sobre un <canvas>
// fuera de pantalla, sin depender del DOM ni de librerías externas.
window.TrikerHandImage = (function () {
    function roundRect(ctx, x, y, w, h, r) {
        ctx.beginPath();
        ctx.moveTo(x + r, y);
        ctx.arcTo(x + w, y, x + w, y + h, r);
        ctx.arcTo(x + w, y + h, x, y + h, r);
        ctx.arcTo(x, y + h, x, y, r);
        ctx.arcTo(x, y, x + w, y, r);
        ctx.closePath();
    }

    // cards: [{ letter, figure, color, groupColor }] — groupColor es null si la carta no está agrupada.
    function render(cards, handName, score, playerName) {
        const cw = 130, ch = 190, gap = 16, pad = 36;
        const headerH = 120, footerH = 46;
        const n = Math.max(cards.length, 1);
        const width = pad * 2 + n * cw + (n - 1) * gap;
        const height = pad * 2 + headerH + ch + footerH;

        const canvas = document.createElement('canvas');
        canvas.width = width;
        canvas.height = height;
        const ctx = canvas.getContext('2d');

        const bg = ctx.createLinearGradient(0, 0, 0, height);
        bg.addColorStop(0, '#1a1a2e');
        bg.addColorStop(1, '#16213e');
        ctx.fillStyle = bg;
        ctx.fillRect(0, 0, width, height);

        ctx.textAlign = 'center';
        ctx.fillStyle = '#f0c040';
        ctx.font = '700 30px system-ui, sans-serif';
        ctx.fillText('🎴 Triker', width / 2, pad + 34);

        ctx.fillStyle = '#ffffff';
        ctx.font = '700 26px system-ui, sans-serif';
        ctx.fillText(handName || '', width / 2, pad + 72);

        ctx.fillStyle = '#9aa0aa';
        ctx.font = '600 19px system-ui, sans-serif';
        const sub = playerName ? `${playerName} · ${score} pts` : `${score} pts`;
        ctx.fillText(sub, width / 2, pad + 100);

        let x = pad;
        const y = pad + headerH;
        (cards || []).forEach(function (c) {
            if (c.groupColor) {
                ctx.save();
                ctx.shadowColor = c.groupColor;
                ctx.shadowBlur = 14;
                roundRect(ctx, x - 3, y - 3, cw + 6, ch + 6, 16);
                ctx.strokeStyle = c.groupColor;
                ctx.lineWidth = 5;
                ctx.stroke();
                ctx.restore();
            }

            roundRect(ctx, x, y, cw, ch, 14);
            ctx.fillStyle = '#ffffff';
            ctx.fill();
            ctx.strokeStyle = '#dddddd';
            ctx.lineWidth = 2;
            ctx.stroke();

            ctx.fillStyle = c.color;
            ctx.textAlign = 'left';
            ctx.font = '900 22px system-ui, sans-serif';
            ctx.fillText(c.letter, x + 12, y + 30);

            ctx.textAlign = 'center';
            ctx.font = '64px system-ui, sans-serif';
            ctx.fillText(c.figure, x + cw / 2, y + ch / 2 + 22);

            x += cw + gap;
        });

        ctx.fillStyle = '#9aa0aa';
        ctx.font = '600 17px system-ui, sans-serif';
        ctx.textAlign = 'center';
        ctx.fillText('triker-app.angelgf.com.es', width / 2, height - pad / 2 + 4);

        return canvas.toDataURL('image/png');
    }

    return { render: render };
})();
