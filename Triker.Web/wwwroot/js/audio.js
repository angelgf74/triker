window.trikerAudio = (() => {
    let ctx = null;
    let enabled = true;

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

    // Sonidos de interacción: pointerdown nativo → resume().then(play)
    // Así el audio se ejecuta cuando el contexto está garantizadamente 'running'
    document.addEventListener('pointerdown', (e) => {
        if (!enabled) return;
        const isCard    = !!e.target.closest('.card:not(.face-down)');
        const isDiscard = !!e.target.closest('.btn-discard');
        if (!isCard && !isDiscard) return;

        const c = getCtx();
        c.resume().then(() => {
            if (isCard)    playTone(c,  900, 0.06, 'triangle', 0.18);
            if (isDiscard) { playSweep(c, 700, 220, 0.22, 0.28); playTone(c, 220, 0.15, 'sine', 0.12, 0.18); }
        });
    }, { passive: true });

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
