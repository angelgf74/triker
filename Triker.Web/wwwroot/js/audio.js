window.trikerAudio = (() => {
    let ctx = null;

    function ac() {
        if (!ctx) ctx = new (window.AudioContext || window.webkitAudioContext)();
        if (ctx.state === 'suspended') ctx.resume();
        return ctx;
    }

    function tone(freq, duration, type = 'sine', gain = 0.25, startDelay = 0) {
        const c = ac();
        const osc = c.createOscillator();
        const g = c.createGain();
        osc.connect(g);
        g.connect(c.destination);
        osc.type = type;
        osc.frequency.value = freq;
        const t = c.currentTime + startDelay;
        g.gain.setValueAtTime(gain, t);
        g.gain.exponentialRampToValueAtTime(0.0001, t + duration);
        osc.start(t);
        osc.stop(t + duration + 0.01);
    }

    function sweep(freqStart, freqEnd, duration, gain = 0.2, startDelay = 0) {
        const c = ac();
        const osc = c.createOscillator();
        const g = c.createGain();
        osc.connect(g);
        g.connect(c.destination);
        osc.type = 'sine';
        const t = c.currentTime + startDelay;
        osc.frequency.setValueAtTime(freqStart, t);
        osc.frequency.exponentialRampToValueAtTime(freqEnd, t + duration);
        g.gain.setValueAtTime(gain, t);
        g.gain.exponentialRampToValueAtTime(0.0001, t + duration);
        osc.start(t);
        osc.stop(t + duration + 0.01);
    }

    return {
        // Clic rápido al seleccionar/deseleccionar carta
        cardClick() {
            tone(900, 0.055, 'triangle', 0.18);
        },

        // Barrido descendente al enviar descarte
        discard() {
            sweep(700, 220, 0.22, 0.28);
            tone(220, 0.15, 'sine', 0.12, 0.18);
        },

        // Dos notas ascendentes: es tu turno
        yourTurn() {
            tone(523, 0.18, 'sine', 0.22);        // C5
            tone(659, 0.22, 'sine', 0.26, 0.14);  // E5
        },

        // Arpeggio ascendente: ganaste la ronda
        winRound() {
            const notes = [523, 659, 784, 1047];  // C5 E5 G5 C6
            notes.forEach((f, i) => tone(f, 0.28, 'sine', 0.22, i * 0.1));
        },

        // Dos notas descendentes: perdiste la ronda
        loseRound() {
            tone(392, 0.2, 'sine', 0.18);         // G4
            tone(294, 0.3, 'sine', 0.14, 0.18);   // D4
        },

        // Fanfarria completa: campeón de la partida
        champion() {
            const seq = [523, 659, 784, 659, 1047];
            seq.forEach((f, i) => tone(f, 0.35, 'sine', 0.28, i * 0.13));
            tone(1047, 0.7, 'sine', 0.22, seq.length * 0.13);
        }
    };
})();
