# The preview's sound — the SoundFont synth, and the road to a VST

The preview's Play button, Alt+P / Alt+M and the auditions (the step keys, the caret, a typed
note) sound through the preview webview. Until session 901 (2026-10-11) the webview voiced
every note with a WebAudio oscillator — ten hand-tuned patches — which the owner called "a poor
sound", and a dense score stuttered and dropped out. This page records what plays now, how the
events are scheduled, where the sound font comes from, and the next stage: sending MIDI to a
DAW or a VST host instead of hosting a VST ourselves. LILYSHARP-OWN throughout — LilyPond
writes a `.mid` and plays nothing.

## 1. What plays, and where

- **The server flattens the score** (`lilysharp/playback`, `LilySharpLanguageServer.GetPlayback`):
  the same `MidiExporter` model the `.mid` export writes, with the tempo map applied, one
  `PlaybackNote` per note in seconds — pitch (fractional for a quarter tone), velocity, the
  source position and printed-copy ordinal for the follow-along highlight, the oscillator
  patches' timbre family `I`, and since session 901 the **channel `C` and General MIDI program
  `G`** the `.mid` plays the note with (`MidiExporter.SplitIntoPartTracks` gives each part a
  track, a channel and a program: its `midiInstrument`, else its preset's, else 0; drums are
  channel 9). So the preview sounds the instruments the export does, and a part keeps one
  sound.
- **The webview owns the AudioContext and the UI** (the page script in `extension.ts`,
  `getPreviewHtml`): one context for the page's life, resumed from the first click or key in
  the preview (the browser's autoplay policy; the editor's keys do not count — the status bar
  says "click the preview once" when a strike finds it suspended).
- **The synth is spessasynth_lib** (4.3.x, Apache-2.0: a SoundFont2/3/DLS synthesizer in an
  AudioWorklet, with full generator and modulator support, pitch wheel, GM drums on channel
  9). Its browser half and ours (`src/webview/previewSynth.ts`, exposed as `window.LysSynth`)
  are bundled by `esbuild.js` into `out/synth/preview-synth.js`; its worklet processor is
  copied beside it. The page loads both by webview URI, so the CSP names the extension's
  resources for `script-src` (a worklet module answers to it), `'wasm-unsafe-eval'` for the
  processor's Vorbis decoder, `blob:` for the fallback that re-adds the processor from its
  text when the host refuses a cross-origin worklet module, and `connect-src` for the sound
  font's `fetch()`.
- **The pure half is `src/webview/playbackCore.ts`** — the events, the lookahead and the
  highlight cursor — driven by `test/playbackCore.test.ts` under node, where no WebAudio
  exists.
- **The oscillator patches stay as the fallback**: no bundle, no sound font configured or
  bundled, a font that failed to load, or a Play struck while the font is still loading. They
  now go through a limiter too (§3).

## 2. The events (`toEvents`)

- Notes before the offset (a seek, Play from Here) are dropped whole; the rest are timed from
  the offset.
- Each pitched channel gets one program change at the start, the first program seen on it.
  The drum channel gets none.
- A note is a `noteOn` at its onset and a `noteOff` at onset + duration (30 ms at least, as the
  server already clamps). A re-strike of the same key at the instant the previous one ends is
  ordered off-then-on.
- **A quarter tone** is the key below the pitch plus the wheel bent up by the fraction (4096
  per semitone at the synth's default ±2 range), centred again at the note's end — the shape
  the `.mid` export writes. One wheel per channel: a quarter tone sounding against a plain
  note on the same channel bends both. The export has the same limit; a per-note tuning
  (MIDI Tuning Standard, which spessasynth supports) is the fix if a book ever needs it.
- Velocity 1..127, keys 0..127.

## 3. Why it stuttered, and the scheduling now

The owner's report (2026-10-11): "with many simultaneous voices the sound breaks up and gets
unstable". Two causes, both in the old `startPlayback`:

- **No ceiling on the sum.** One voice peaked at 0.9 × up to 1.3, through a master gain of
  0.25: four full-velocity voices already exceeded 1.0, and the clipping read as distortion and
  dropouts.
- **Every node of the piece built up front.** One Oscillator and one Gain per note, for the
  whole piece, created and scheduled the moment Play was pressed: a long or dense piece put
  thousands of live nodes on the audio thread.

Now:

- **One worklet, fed a little ahead of the clock.** `Lookahead.take` hands the player the
  events whose time falls within the horizon — 0.25 s while the page is visible, pumped every
  50 ms; 1.5 s while it is hidden, where the browser fires a timer about once a second
  (`Player.pump`). The synth holds a few hundred milliseconds of events, never the piece; its
  own voice cap and voice stealing bound the polyphony.
- **A limiter on the output**: the synth's output goes through a gain (0.9) and a
  `DynamicsCompressor` (threshold −10 dB, ratio 12, 2 ms attack) before the destination; the
  oscillator fallback got the same pair.
- **Stop drains the queue.** The synth keeps a time-ordered queue that `stopAll` does not
  empty (`spessasynth_core`, `processMessage`), so a Stop sends `stopAll(true)` now and an All
  Sound Off plus a centred wheel on every channel used, timed just after the last event already
  handed out; the next Play starts no earlier than that (`PreviewSynth.play`, `quietAt`).
- **The highlight walks the onsets once.** `SoundingCursor.at(elapsed)` keeps a cursor into
  the onset order and the set of notes still ringing, so a 50 ms tick costs the notes that
  started since plus the active ones — the old tick scanned the whole list.

## 4. The sound font

- **Where it comes from**: `lilysharp.playback.soundFont` (machine-overridable) names an
  `.sf2`, `.sf3` or `.dls` on this machine; empty, the first such file under the extension's
  `media/soundfont/` plays. A missing file is logged and the bundled one used. Changing the
  setting re-roots the open previews' webviews and swaps the font in place
  (`previewResourceRoots`, `synthUrls`, the `setSoundFont` message). No font at all: the
  oscillator patches, and the output channel says so (`Preview synth none`). The setting's
  description links to **Lily#: Choose Sound Font…** (`chooseSoundFont`), a picker: the
  bundled font, the General MIDI bank the OS ships for its own synth where present
  (Windows' `gm.dls`, macOS's `gs_instruments.dls` — Roland GS, DLS, small and dated; read
  in place, never copied), the current file, or a file dialog filtered to the three formats.
- **Which one** — the owner's decision, pending (session 901 placed MuseScore_General for the
  owner to hear and committed no font):
  - *MuseScore_General.sf3* (MuseScore 3's default; S. Christian Collins' adaptation of
    FluidR3Mono; MIT; 39.9 MB as SF3) — fuller, stereo.
  - *FluidR3Mono_GM.sf3* (MuseScore 2's default; Michael Cowgill's mono conversion of Frank
    Wen's FluidR3; MIT; 14.5 MB as SF3) — lighter.
  - *GeneralUser GS* (S. Christian Collins; its own permissive licence; ~30 MB as SF2) — a
    third option spessasynth is tuned against.
  The licence file ships beside the font (`media/soundfont/<name>-LICENSE.md`) and the font is
  listed in `THIRD-PARTY-NOTICES.md`. Whether the VSIX bundles it or the extension fetches it
  on first use into `globalStorage` is the same decision: the platform VSIXs already carry a
  self-contained .NET server, so 15–40 MB more is a size question, not a licence one.
- **Loading** happens at page boot (a suspended context parses a bank fine), so the first Play
  does not wait; the output channel logs `Preview synth ready: N MB in M ms`. Each preview
  keeps its own synth and bank (the panels retain their context when hidden).

## 5. The auditions

- The step keys, the caret landing on a note and a typed note (`stepAudition.ts` →
  `playPitches`) and Alt+P / Alt+M (`playAtCursor` → the note list) strike the synth on
  **channel 15**, which a score's parts take only past fourteen pitched sounds, with the
  **part's program**: the server's `lilysharp/step` and `lilysharp/auditionAt` answer `Program`
  beside `Timbre` (`NoteStepper.Voice`, read off the export's track per source position —
  `SoundIndex.Program`), and a note from the list carries `G`. A sound no part claims
  (`Program` −1: a voicing on a rest sounds the guitar preset's program; a note the export
  cannot place sounds the timbre family's nearest GM voice, `FAMILY_PROGRAM`). The drum
  family (timbre 9) strikes the drum channel. A note the export filed no onset under — the
  target of a tie, a note in music no form plays — sounds its written pitch in the voice of
  the part whose music holds it (`SoundIndex.PartVoice`, `Written`), so the caret on
  `a~ | a` strikes the flute's a and not a piano's (owner's report 2026-10-11).
- A held key re-arms a watchdog as before; the strike is a `noteOn` and the release a
  `noteOff`, so the font's own release plays.

## 6. Limits, and the next stage

- **Latency and polyphony are the worklet's**: a 128-sample quantum at the context's rate, and
  spessasynth's voice cap. Fine for playback and auditions; not a performance instrument.
- **One wheel per channel** (§2).
- **No VST, by design.** Hosting VST3 or CLAP in-process needs native code, an audio device,
  a window for the plugin's editor (the language server is headless and a VS Code extension
  cannot own a native window) and per-OS packaging — MuseScore 4's scale of work. The road to
  a user's own instruments is **MIDI out from the language server** (stage B, not started):
  `lilysharp.playback.output` naming a MIDI port (RtMidi-based .NET bindings cover Windows,
  macOS and Linux), the server scheduling the same note list to it with the part's channel
  and program change, the webview keeping the highlight clock from the server's start
  acknowledgement. Windows then plays through the Microsoft GS Wavetable Synth with no
  setup, and loopMIDI / IAC carry the stream to any DAW, Cantabile, Carla or a VST host; a
  later `midiChannel` property on a part would pin a DAW template. Web MIDI from the webview
  is not the route — VS Code is not expected to grant it the `midi` permission.
- **Offline rendering** (`lysc wav`) would reuse the same font through MeltySynth (pure C#,
  MIT) in Core, after the bundling decision.

## 7. Where things are

| What | Where |
|---|---|
| The note list, channel and program | `LilySharp.Lsp/LilySharpLanguageServer.Commands.cs` (`GetPlayback`), `LspProtocolDtos.cs` (`PlaybackNote`) |
| The audition's program | `LilySharp.Core/Editing/NoteStepper.cs` (`Voice`, `SoundIndex.Program`), `LilySharpLanguageServer.Step.cs` |
| The events, the lookahead, the highlight cursor | `editors/vscode/src/webview/playbackCore.ts` (+ `test/playbackCore.test.ts`) |
| The synth, the player, the font loading | `editors/vscode/src/webview/previewSynth.ts` → `out/synth/preview-synth.js` |
| The page: context, Play/Stop, follow-along, auditions, oscillator fallback | `editors/vscode/src/extension.ts`, the page script's "Playback and audition" section |
| The URLs, the roots, the setting, the CSP | `extension.ts`: `synthRoot`, `soundFontFile`, `previewResourceRoots`, `synthUrls`, `getPreviewHtml` |
| The bundle | `editors/vscode/esbuild.js` (`copyWorkletProcessor`, the second context) |
| The font | `editors/vscode/media/soundfont/` (+ `<name>-LICENSE.md`), `THIRD-PARTY-NOTICES.md` |
| The nets | `PlaybackRequestTests`, `StepRequestTests.TheAudition_NamesTheProgramThePartPlays`, `test/playbackCore.test.ts`, `test/webviewScript.test.ts` (the page script still parses) |
