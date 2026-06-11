window.trikerAudio = (() => {
    let ctx = null;

    // Crea/desbloquea el AudioContext en el evento DOM nativo,
    // ANTES de que el runtime WASM procese el click.
    // Chrome desktop exige que el AudioContext se cree o desbloquee
    // dentro de un gesto de usuario real del DOM, no desde JS interop.
    ['pointerdown', 'touchstart', 'keydown'].forEach(type =>
        document.addEventListener(type, () => {
            if (!ctx) {
                ctx = new (window.AudioContext || window.webkitAudioContext)();
            } else if (ctx.state === 'suspended') {
                ctx.resume();
            }
        }, { passive: true })
    );

    async function ensureCtx() {
        if (!ctx) ctx = new (window.AudioContext || window.webkitAudioContext)();
        if (ctx.state === 'suspended') await ctx.resume();
        return ctx;
    }

    async function tone(freq, dur, type = 'sine', gain = 0.22, delay = 0) {
        const c = await ensureCtx();
        const osc = c.createOscillator();
        const g = c.createGain();
        osc.connect(g); g.connect(c.destination);
        osc.type = type;
        osc.frequency.value = freq;
        const t = c.currentTime + delay;
        g.gain.setValueAtTime(gain, t);
        g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
        osc.start(t);
        osc.stop(t + dur + 0.05);
    }

    async function sweep(f0, f1, dur, gain = 0.2, delay = 0) {
        const c = await ensureCtx();
        const osc = c.createOscillator();
        const g = c.createGain();
        osc.connect(g); g.connect(c.destination);
        osc.type = 'sine';
        const t = c.currentTime + delay;
        osc.frequency.setValueAtTime(f0, t);
        osc.frequency.exponentialRampToValueAtTime(f1, t + dur);
        g.gain.setValueAtTime(gain, t);
        g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
        osc.start(t);
        osc.stop(t + dur + 0.05);
    }

    return {
        cardClick:  () => tone(900, 0.06, 'triangle', 0.18),

        discard: async () => {
            await sweep(700, 220, 0.22, 0.28);
            await tone(220, 0.15, 'sine', 0.12, 0.18);
        },

        yourTurn: async () => {
            await tone(523, 0.18, 'sine', 0.22);
            await tone(659, 0.22, 'sine', 0.26, 0.14);
        },

        winRound: () => Promise.all(
            [523, 659, 784, 1047].map((f, i) => tone(f, 0.28, 'sine', 0.22, i * 0.1))
        ),

        loseRound: async () => {
            await tone(392, 0.2, 'sine', 0.18);
            await tone(294, 0.3, 'sine', 0.14, 0.18);
        },

        champion: () => {
            const seq = [523, 659, 784, 659, 1047];
            return Promise.all([
                ...seq.map((f, i) => tone(f, 0.35, 'sine', 0.28, i * 0.13)),
                tone(1047, 0.7, 'sine', 0.22, seq.length * 0.13)
            ]);
        }
    };
})();
