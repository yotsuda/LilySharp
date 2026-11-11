// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

/**
 * The plain tones — oscillator patches a page plays with while no sound font is loaded — as the
 * script a page includes: PATCHES (one a timbre family, PlaybackNote.I: a waveform and an envelope,
 * `pluck` an exponential decay; the last the drums, filtered noise), oscillatorOutput (the sum
 * through a limiter: many voices at once used to clip) and startOscillators(ctx, notes, offset,
 * t0), which builds a node pair a note up front, pushes each source onto the page's own
 * `playingOscs` for a Stop to end, and answers when the last note ends, in seconds from the
 * list's start.
 *
 * ONE text for the two pages that play notes (owner's decision 2026-11-11: the candidate panel
 * plays itself, with no sound font too — Linux with none set): the preview's fallback and the
 * panel's are the same tones. It is page script, not extension code: no template substitution
 * stands in it, and the page that includes it declares `let playingOscs = [];`.
 */
export const PLAIN_TONES_JS = `        const PATCHES = [
            { w: 'triangle', a: 0.010, s: 0.55, r: 0.05, g: 1.00, pluck: true,  tau: 0.9  }, // 0 piano-ish
            { w: 'sine',     a: 0.050, s: 0.90, r: 0.06, g: 1.15, pluck: false, tau: 0    }, // 1 flute
            { w: 'square',   a: 0.030, s: 0.75, r: 0.05, g: 0.45, pluck: false, tau: 0    }, // 2 clarinet/reed
            { w: 'sawtooth', a: 0.080, s: 0.85, r: 0.09, g: 0.55, pluck: false, tau: 0    }, // 3 strings
            { w: 'triangle', a: 0.005, s: 0.40, r: 0.04, g: 1.00, pluck: true,  tau: 0.35 }, // 4 guitar
            { w: 'sine',     a: 0.008, s: 0.45, r: 0.05, g: 1.30, pluck: true,  tau: 0.5  }, // 5 bass
            { w: 'sawtooth', a: 0.020, s: 0.90, r: 0.06, g: 0.50, pluck: false, tau: 0    }, // 6 brass
            { w: 'square',   a: 0.040, s: 1.00, r: 0.05, g: 0.35, pluck: false, tau: 0    }, // 7 organ
            { w: 'sine',     a: 0.060, s: 0.90, r: 0.08, g: 1.10, pluck: false, tau: 0    }, // 8 voice
            { drum: true }                                                                     // 9 drums (noise)
        ];
        function oscillatorOutput(ctx, gain) {
            const master = ctx.createGain();
            master.gain.value = gain;
            const limiter = ctx.createDynamicsCompressor();
            limiter.threshold.value = -10;
            limiter.knee.value = 12;
            limiter.ratio.value = 12;
            limiter.attack.value = 0.002;
            limiter.release.value = 0.12;
            master.connect(limiter);
            limiter.connect(ctx.destination);
            return master;
        }
        function startOscillators(ctx, notes, offset, t0) {
            const master = oscillatorOutput(ctx, 0.25);
            let end = 0;
            // Shared white-noise buffer for the drum patch (timbre 9).
            let noiseBuf = null;
            const getNoise = () => {
                if (!noiseBuf) {
                    noiseBuf = ctx.createBuffer(1, ctx.sampleRate, ctx.sampleRate);
                    const d = noiseBuf.getChannelData(0);
                    for (let i = 0; i < d.length; i++) { d[i] = Math.random() * 2 - 1; }
                }
                return noiseBuf;
            };
            for (const n of notes) {
                if (n.P < 0) { continue; }   // the pedal: the plain tones have none
                const p = PATCHES[n.I] || PATCHES[0];
                if (p.drum) {
                    // GM percussion approximation: filtered noise burst.
                    // Kick (<45): lowpass thump; snare-ish (45-47 + 38-40):
                    // bandpass; cymbals/hats (42+, 49+): highpass sizzle.
                    const src = ctx.createBufferSource();
                    src.buffer = getNoise();
                    const filt = ctx.createBiquadFilter();
                    let tau = 0.06;
                    if (n.P <= 36) { filt.type = 'lowpass'; filt.frequency.value = 180; tau = 0.10; }
                    else if (n.P === 38 || n.P === 39 || n.P === 40) { filt.type = 'bandpass'; filt.frequency.value = 1800; filt.Q.value = 0.8; tau = 0.09; }
                    else if (n.P >= 49 && n.P !== 51) { filt.type = 'highpass'; filt.frequency.value = 5000; tau = 0.45; } // crash etc ring
                    else if (n.P === 46) { filt.type = 'highpass'; filt.frequency.value = 6500; tau = 0.25; } // open hat
                    else if (n.P >= 41 && n.P <= 48 && n.P !== 42 && n.P !== 44) { filt.type = 'bandpass'; filt.frequency.value = 300 + (n.P - 41) * 90; filt.Q.value = 1.2; tau = 0.12; } // toms
                    else { filt.type = 'highpass'; filt.frequency.value = 7500; tau = 0.05; } // closed/pedal hat, ride tick
                    const dg = ctx.createGain();
                    const dat = t0 + n.T - offset;
                    const dpk = 0.9 * (n.V / 127);
                    dg.gain.setValueAtTime(0, dat);
                    dg.gain.linearRampToValueAtTime(dpk, dat + 0.004);
                    dg.gain.setTargetAtTime(0.0001, dat + 0.004, tau);
                    src.connect(filt); filt.connect(dg); dg.connect(master);
                    src.start(dat);
                    const dstop = dat + Math.min(n.D, tau * 6) + 0.05;
                    src.stop(dstop);
                    playingOscs.push(src);
                    if (n.T + n.D > end) end = n.T + n.D;
                    continue;
                }
                const osc = ctx.createOscillator();
                osc.type = p.w;
                osc.frequency.value = 440 * Math.pow(2, (n.P - 69) / 12);
                const g = ctx.createGain();
                const at = t0 + n.T - offset;
                const rel = at + n.D;
                const peak = 0.9 * (n.V / 127) * p.g;
                g.gain.setValueAtTime(0, at);
                g.gain.linearRampToValueAtTime(peak, at + p.a);
                if (p.pluck) {
                    // Plucked/struck: exponential decay through the note,
                    // then a faster decay as the release.
                    g.gain.setTargetAtTime(peak * 0.15, at + p.a, p.tau);
                    g.gain.setTargetAtTime(0.0001, rel, 0.03);
                } else {
                    // Sustained: hold, ease to the sustain level, release.
                    g.gain.setValueAtTime(peak, Math.max(at + p.a, rel - 0.06));
                    g.gain.linearRampToValueAtTime(peak * p.s, Math.max(at + p.a, rel - 0.02));
                    g.gain.linearRampToValueAtTime(0.0001, rel + p.r);
                }
                osc.connect(g);
                g.connect(master);
                osc.start(at);
                osc.stop(rel + p.r + 0.05);
                playingOscs.push(osc);
                if (n.T + n.D > end) end = n.T + n.D;
            }
            return end;
        }
`;
