# The preview's sound — the SoundFont synth, and the road to a VST

The preview's Play button, Alt+P / Alt+M and the auditions (the step keys, the caret, a typed
note) sound through the preview webview. Until session 901 (2026-10-11) the webview voiced
every note with a WebAudio oscillator — ten hand-tuned patches — which the owner called "a poor
sound", and a dense score stuttered and dropped out. This page records what plays now, how the
events are scheduled, where the sound font comes from, and stage B (session 911, 2026-10-18):
the language server sending MIDI to a port of the machine — a DAW or a VST host — instead of
hosting a VST ourselves. LILYSHARP-OWN throughout — LilyPond writes a `.mid` and plays nothing.

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
  `media/soundfont/` (a developer's local build only — a release carries none), else the
  General MIDI bank the OS ships (`osSoundFonts`: Windows' `gm.dls`, macOS's
  `gs_instruments.dls`). A missing file is logged and the default used. Changing the
  setting re-roots the open previews' webviews and swaps the font in place
  (`previewResourceRoots`, `synthUrls`, the `setSoundFont` message). No font at all: the
  oscillator patches, and the output channel says so (`Preview synth none`). The setting's
  description links to **Lily#: Choose Sound Font…** (`chooseSoundFont`), a picker: the
  bundled font, the General MIDI bank the OS ships for its own synth where present
  (Windows' `gm.dls`, macOS's `gs_instruments.dls` — Roland GS, DLS, small and dated; read
  in place, never copied), the current file, or a file dialog filtered to the three formats.
- **Which one — DECIDED 2026-10-12 (owner): none is bundled.** "Windows ships one, small as it
  is; whoever wants better installs it — document the recommended fonts and the steps." So the
  default is the OS's bank (Windows' `gm.dls`: 3.4 MB, 235 presets with the GS drum kits,
  parsed by spessasynth_core 4.3.22 in 45 ms — measured), simple tones on Linux, and
  `site/editor-body.html` `#soundfonts` names the three below with their publishers' links and
  the steps (Choose Sound Font… → Browse). `media/soundfont/` is in `.gitignore` and
  `.vscodeignore`. The three considered for bundling, now the recommendations:
  - *MuseScore_General.sf3* (MuseScore 3's default; S. Christian Collins' adaptation of
    FluidR3Mono; MIT; 39.9 MB as SF3) — fuller, stereo.
  - *FluidR3Mono_GM.sf3* (MuseScore 2's default; Michael Cowgill's mono conversion of Frank
    Wen's FluidR3; MIT; 14.5 MB as SF3) — lighter.
  - *GeneralUser GS* (S. Christian Collins; its own permissive licence; ~30 MB as SF2) — a
    third option spessasynth is tuned against.
  Bundling one would have meant its licence beside it and a THIRD-PARTY-NOTICES entry (all
  three permit it; 15–40 MB more per VSIX was a size question, not a licence one). Not
  bundling also leaves fetching on first use unbuilt — the user downloads from the publisher.
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
  `a~ | a` strikes the flute's a and not a piano's (owner's report 2026-10-11) — shifted as
  the part's struck notes are (`SoundIndex.PartShift`: the most common exported − written over
  the positions both the export and the collector filed, which composes the preset's
  transposition, `transpose`, the concert-pitch input and the clef's octave without
  re-deriving them), so a guitar's tied note sounds its octave below and a clarinet's its two
  semitones (the same report: an octave high, wrong on a transposing instrument).
- A held key re-arms a watchdog as before; the strike is a `noteOn` and the release a
  `noteOff`, so the font's own release plays.

## 6. Limits, and MIDI out (stage B — owner's decision 2026-10-18, session 911)

- **Latency and polyphony are the worklet's**: a 128-sample quantum at the context's rate, and
  spessasynth's voice cap. Fine for playback and auditions; not a performance instrument.
- **One wheel per channel** (§2).
- **No VST, by design.** Hosting VST3 or CLAP in-process needs native code, an audio device,
  a window for the plugin's editor (the language server is headless and a VS Code extension
  cannot own a native window) and per-OS packaging — MuseScore 4's scale of work. The road to
  a user's own instruments is **MIDI out from the language server**, below. Web MIDI from the
  webview is not the route — VS Code is not expected to grant it the `midi` permission.
- **Offline rendering** (`lysc wav`) would reuse the same font through MeltySynth (pure C#,
  MIT) in Core, after the bundling decision.

### MIDI out

`lilysharp.playback.output` names a MIDI output port of the machine (empty: the preview's own
synth, §1–§5 unchanged); **Lily#: Choose MIDI Output…** lists what the machine has, and the
setting's description links to it. Windows then plays through the Microsoft GS Wavetable Synth
with no setup, and a virtual port — loopMIDI on Windows, an IAC bus on a Mac, the ALSA
sequencer's own graph on Linux — carries the stream to a DAW, Cantabile, Carla or any VST host,
where the user's instruments are.

- **Who decides what, and who holds the port.** The webview still decides WHAT plays — the
  note list it fetched (`lilysharp/playback`), the offset (Play from the highlighted note, a
  seek, Alt+M's measure), an audition's pitches — and **the server is the port**:
  `lilysharp/midiOut/play` takes the list and the offset, `/stop` stops, `/strike` and
  `/release` are the auditions, `/ports` is the picker's list
  (`LilySharpLanguageServer.MidiOut.cs`, DTOs in `MidiOut/MidiOutDtos.cs`). The list is passed,
  not recomputed, so what sounds and what the highlight follows are one list, and the measure
  Alt+M cuts out plays as it does on the synth here. Every request names the port, so a changed
  setting takes effect on the next sound; the host sends the requests one after another
  (`midiOutRequest`), because the server dispatches concurrently and a Stop must not overtake
  the Play it follows.
- **The events are the same.** `MidiOutEvents.From` is the server-side twin of `toEvents`
  (§2): each pitched channel's program once at the start, a quarter tone as the key below bent
  up by the wheel and centred after, at one instant a note's off before its re-strike and the
  programs and bends before the notes — so a DAW hears what the preview's synth hears. The
  nets mirror `test/playbackCore.test.ts` case for case (`MidiOutTests`).
- **The clock.** `MidiOutPlayer` runs one playback thread: it sleeps to about 2 ms before each
  event and spins the rest (a timer's grain is 1–16 ms, and a note 10 ms late is heard); on
  Windows the timer period is raised to 1 ms for the playback (`timeBeginPeriod`, as every MIDI
  player does). The play's answer carries the wall-clock time (Unix ms) of the playback's zero,
  and the webview's follow-along highlight runs from it — `Date.now()` is the same clock, so no
  estimate of the round trip is needed (`followAlong` takes a clock function: the audio
  context's for the synth here, this for the port). A stop wakes the thread at once and sends
  every channel the playback touched All Notes Off, All Sound Off and a centred wheel; the
  server does the same on its way down (`Program.cs`), so a window reload leaves nothing ringing
  on a DAW.
- **The ports, OS by OS, through P/Invoke only** — no package, no native binary of ours,
  nothing for THIRD-PARTY-NOTICES. (The RtMidi bindings on NuGet date from 2023, cover Windows
  and Mac only and carry Serilog; the one maintained pure-managed library reaches Linux through
  rawmidi, which does not see the software synths. Each OS's own API is a hundred lines.)
  `WinMmPort` (winmm, `midiOut*`); `CoreMidiPort` (CoreMIDI: one client and output port, a
  destination by display name, the packet list built by `MIDIPacketListInit` / `Add` so its
  layout — which differs between Intel and Apple silicon — is never spelled); `AlsaSeqPort` (the
  ALSA **sequencer**, not rawmidi: FluidSynth, TiMidity and a DAW's input are sequencer
  clients; a port is `client: port`; libasound2 must be installed, else the list is empty).
  ⚠️ **Windows is verified on the machine** — `MidiOutTests.TheMachinesOwnSynth_…` opens the GS
  synth, plays and closes it, on every Windows run; **the Mac and Linux ports are written
  against the documented API and not yet run** (MANUAL-CHECKS §3.5).
- **Failures are answers.** A port that is not there (unplugged, renamed, another machine's
  setting) comes back as `Error`; the status bar says so once per port, the output channel every
  time, and the preview plays its own synth for that Play. Nothing throws across the wire.
- **What stays as it was.** Nothing sounds while no preview is open (the webview is still the
  conductor; the auditions are its); one wheel per channel; the audition strikes on channel 16
  (15 0-based) and the drums on 10 (9). A part's own channel (`midiChannel`, to pin a DAW
  template) is the next step if a book needs it.

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
| The font | none bundled — `soundFontFile` / `osSoundFonts` in `extension.ts`; the advice for users: `site/editor-body.html` `#soundfonts` |
| MIDI out: the ports, the events, the player | `LilySharp.Lsp/MidiOut/` — `MidiOutPorts` (+ `WinMmPort`, `CoreMidiPort`, `AlsaSeqPort`), `MidiOutEvents`, `MidiOutPlayer`; the requests `LilySharpLanguageServer.MidiOut.cs`, the DTOs `MidiOut/MidiOutDtos.cs`; the close on exit `Program.cs` |
| MIDI out: the setting, the picker, the relay, the page | `extension.ts`: `midiOutputSetting`, `chooseMidiOutput`, `midiOutRequest`, `reportMidiOutError`, the `midiPlay` / `midiStop` / `midiStrike` / `midiRelease` messages; the page script's `setMidiOutput`, `midiStarted`, `startPlayback`'s port branch, `holdNotes` / `stopHeldNote` |
| The nets | `PlaybackRequestTests`, `StepRequestTests.TheAudition_NamesTheProgramThePartPlays`, `MidiOutTests`, `test/playbackCore.test.ts`, `test/midiOutputSetting.test.ts`, `test/webviewScript.test.ts` (the page script still parses) |
