// node render-audio.mjs <soundbank.sf2|.sf3> <in.mid> <out.wav>
//
// Plays a MIDI file through a sound bank with spessasynth_core, the synthesizer the VS Code
// preview plays through, and writes 48 kHz stereo WAV with 4 seconds after the end to ring out.
// Unlike `lysc wav` (MeltySynth), it reads the compressed .sf3 the extension ships.
// Needs `npm install` in editors/vscode, where spessasynth_core is installed.
import * as fs from "node:fs/promises";
import { fileURLToPath, pathToFileURL } from "node:url";
import * as path from "node:path";

const here = path.dirname(fileURLToPath(import.meta.url));
const corePath = path.join(here, "..", "..", "editors", "vscode", "node_modules", "spessasynth_core", "dist", "index.js");
const { audioToWav, BasicMIDI, SoundBankLoader, SpessaSynthProcessor, SpessaSynthSequencer } =
  await import(pathToFileURL(corePath).href);

const [sfPath, midPath, wavPath] = process.argv.slice(2);
if (!wavPath) {
  console.error("usage: node render-audio.mjs <soundbank> <in.mid> <out.wav>");
  process.exit(2);
}
const ab = (b) => b.buffer.slice(b.byteOffset, b.byteOffset + b.byteLength);
const midi = BasicMIDI.fromArrayBuffer(ab(await fs.readFile(midPath)));
const bank = SoundBankLoader.fromArrayBuffer(ab(await fs.readFile(sfPath)));

const sampleRate = 48000;
const synth = new SpessaSynthProcessor(sampleRate, { eventsEnabled: false });
synth.soundBankManager.addSoundBank(bank, "main");
await synth.processorInitialized;
synth.setSystemParameter("autoAllocateVoices", true);

const seq = new SpessaSynthSequencer(synth);
seq.loadNewSongList([midi]);
seq.play();

const sampleCount = Math.ceil(sampleRate * (midi.duration + 4));
const L = new Float32Array(sampleCount), R = new Float32Array(sampleCount);
for (let filled = 0; filled < sampleCount; ) {
  seq.processTick();
  const n = Math.min(128, sampleCount - filled);   // small blocks: modulators and LFOs update per block
  synth.process(L, R, filled, n);
  filled += n;
}
await fs.writeFile(wavPath, new Uint8Array(audioToWav([L, R], sampleRate)));
console.log(`audio: ${midi.duration.toFixed(2)} s of music -> ${wavPath}`);
