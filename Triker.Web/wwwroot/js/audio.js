window.trikerAudio = (() => {
    let ctx = null;
    let enabled = true;

    // Versión síncrona: crea/reactiva el ctx dentro del evento DOM (user gesture real)
    function getCtxSync() {
        if (!ctx) ctx = new (window.AudioContext || window.webkitAudioContext)();
        if (ctx.state === 'suspended') ctx.resume();
        return ctx;
    }

    // Versión async: para sonidos disparados desde JS interop (no user gesture)
    async function getCtxAsync() {
        if (!ctx) ctx = new (window.AudioContext || window.webkitAudioContext)();
        if (ctx.state === 'suspended') await ctx.resume();
        return ctx;
    }

    function toneSync(freq, dur, type, gain, delay) {
        if (!enabled) return;
        const c = getCtxSync();
        if (c.state !== 'running') return;
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

    function sweepSync(f0, f1, dur, gain, delay) {
        if (!enabled) return;
        const c = getCtxSync();
        if (c.state !== 'running') return;
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

    async function toneAsync(freq, dur, type, gain, delay) {
        if (!enabled) return;
        const c = await getCtxAsync();
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

    // ── Sonidos de interacción: listeners nativos del DOM (user gesture real) ──
    document.addEventListener('pointerdown', (e) => {
        if (e.target.closest('.card:not(.face-down)')) {
            toneSync(900, 0.06, 'triangle', 0.18);
        }
        if (e.target.closest('.btn-discard')) {
            sweepSync(700, 220, 0.22, 0.28);
            toneSync(220, 0.15, 'sine', 0.12, 0.18);
        }
    }, { passive: true });

    // ── Sonidos de estado: llamados desde JS interop (Blazor) ──
    return {
        setEnabled(val) { enabled = val; },

        yourTurn: async () => {
            await toneAsync(523, 0.18, 'sine', 0.22);
            await toneAsync(659, 0.22, 'sine', 0.26, 0.14);
        },

        winRound: () => Promise.all(
            [523, 659, 784, 1047].map((f, i) => toneAsync(f, 0.28, 'sine', 0.22, i * 0.1))
        ),

        loseRound: async () => {
            await toneAsync(392, 0.2, 'sine', 0.18);
            await toneAsync(294, 0.3, 'sine', 0.14, 0.18);
        },

        champion: () => {
            const seq = [523, 659, 784, 659, 1047];
            return Promise.all([
                ...seq.map((f, i) => toneAsync(f, 0.35, 'sine', 0.28, i * 0.13)),
                toneAsync(1047, 0.7, 'sine', 0.22, seq.length * 0.13)
            ]);
        }
    };
})();
