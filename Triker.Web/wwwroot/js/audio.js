window.trikerAudio = (() => {
    let ctx = null;
    let enabled = true;
    let lastInteractionAt = 0;

    function getCtx() {
        if (!ctx) ctx = new (window.AudioContext || window.webkitAudioContext)();
        return ctx;
    }

    function playTone(c, freq, dur, type, gain, delay) {
        const osc = c.createOscillator();
        const g = c.createGain();
        osc.connect(g); g.connect(c.destination);
        osc.type = type || 'sine';
        osc.frequency.value = freq;
        const t = c.currentTime + (delay || 0);
        g.gain.setValueAtTime(gain || 0.22, t);
        g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
        osc.start(t); osc.stop(t + dur + 0.05);
    }

    function playSweep(c, f0, f1, dur, gain, delay) {
        const osc = c.createOscillator();
        const g = c.createGain();
        osc.connect(g); g.connect(c.destination);
        osc.type = 'sine';
        const t = c.currentTime + (delay || 0);
        osc.frequency.setValueAtTime(f0, t);
        osc.frequency.exponentialRampToValueAtTime(f1, t + dur);
        g.gain.setValueAtTime(gain || 0.2, t);
        g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
        osc.start(t); osc.stop(t + dur + 0.05);
    }

    // Desbloquear el AudioContext en la primera interacción (cualquier toque/clic)
    function unlockCtx() {
        const c = getCtx();
        if (c.state !== 'running') c.resume().catch(() => {});
        document.removeEventListener('pointerdown', unlockCtx, true);
        document.removeEventListener('keydown',     unlockCtx, true);
    }
    document.addEventListener('pointerdown', unlockCtx, { capture: true, passive: true });
    document.addEventListener('keydown',     unlockCtx, { capture: true });

    // Sonidos de interacción: cartas y botón de descarte.
    // Se escuchan en fase de captura (no se puede stop-propagar antes de nosotros)
    // y en pointerdown + click para máxima fiabilidad. Antirrebote para evitar doble sonido.
    function tryPlayInteraction(e) {
        if (!enabled) return;
        const target = e.target;
        if (!target || !target.closest) return;
        const isCard    = !!target.closest('.card');
        const isDiscard = !!target.closest('.btn-discard');
        if (!isCard && !isDiscard) return;

        const now = (typeof performance !== 'undefined' ? performance.now() : Date.now());
        if (now - lastInteractionAt < 150) return;
        lastInteractionAt = now;

        const c = getCtx();
        const doPlay = () => {
            if (isCard)    playTone(c, 1200, 0.09, 'square', 0.28);
            if (isDiscard) { playSweep(c, 700, 220, 0.22, 0.28); playTone(c, 220, 0.15, 'sine', 0.12, 0.18); }
        };

        if (c.state === 'running') {
            doPlay();
        } else {
            c.resume().then(doPlay).catch(() => {});
        }
    }
    document.addEventListener('pointerdown', tryPlayInteraction, { capture: true, passive: true });
    document.addEventListener('click',       tryPlayInteraction, { capture: true, passive: true });

    // Sonidos de estado: desde JS interop (SignalR), el contexto ya está activo
    async function interopTone(freq, dur, type, gain, delay) {
        if (!enabled) return;
        const c = getCtx();
        if (c.state === 'suspended') await c.resume();
        playTone(c, freq, dur, type, gain, delay);
    }

    return {
        setEnabled(val) { enabled = val; },

        yourTurn: async () => {
            await interopTone(523, 0.18, 'sine', 0.22);
            await interopTone(659, 0.22, 'sine', 0.26, 0.14);
        },

        winRound: () => Promise.all(
            [523, 659, 784, 1047].map((f, i) => interopTone(f, 0.28, 'sine', 0.22, i * 0.1))
        ),

        loseRound: async () => {
            await interopTone(392, 0.2, 'sine', 0.18);
            await interopTone(294, 0.3, 'sine', 0.14, 0.18);
        },

        champion: () => {
            const seq = [523, 659, 784, 659, 1047];
            return Promise.all([
                ...seq.map((f, i) => interopTone(f, 0.35, 'sine', 0.28, i * 0.13)),
                interopTone(1047, 0.7, 'sine', 0.22, seq.length * 0.13)
            ]);
        }
    };
})();
