# Singing the lyrics — the preview's voice through Cantilena

The preview plays every part through a sound font (`docs/playback-design.md`); a part that
carries lyrics sounds as the instrument its preset names, and the words are only read. The
owner asked (2026-11-08, session 1124) that the preview sing them. This page records what was
found, what was decided, and the design: Lily# resolves the sung part to a score in seconds,
a separate engine turns it into a voice, and the editor plays that voice beside the synth.
LILYSHARP-OWN throughout — LilyPond writes no sound.

## 1. What was found (2026-11-08)

- **OpenUtau** (MIT, C#) has the best open singing stack — its DiffSinger inference is C# over
  ONNX Runtime — but no command line: it renders from its GUI only (a CLI pull request,
  #2162, is unmerged), and it ships no voice. Not a backend today.
- **NEUTRINO** (Tau, v3.2.x; closed-source freeware; Japanese) has one: a MusicXML goes
  through `musicXMLtoLabel` to Sinsy labels, a resident `neutrino_server` renders them, and
  `neutrino_client` talks to it. It needs hiragana or katakana on every note, `ー` for a
  melisma, inserts a bar of rest when the score opens on a note, and ignores slurs, dynamics
  and tempo marks. A warm render is about a tenth of real time on this machine (RTX 5070 Ti),
  a cold one 3–50 s (the CUDA kernel cache). Its EULA forbids voice-conversion use; it is
  invoked, never included.
- **Cantilena** (`C:\MyProj\cantilena`, Apache-2.0, the owner's) was started for this: a
  `.NET` library with one contract — a *sung score* in, mono audio out (`ISinger`) — a CLI
  (`cantilena sing score.json -o out.wav`), and a `neutrino` backend that sings today; a
  `diffsinger` backend (in-process ONNX, after OpenUtau) is planned. It ships no voice. **A
  separate repository on purpose** (owner's decision 2026-11-08): Lily# is GPL-3.0 and
  Cantilena Apache-2.0, so Lily# may use Cantilena, Cantilena must never depend on Lily#, and
  any other notation program may use it.

## 2. The division of labour

- **Lily# knows the music.** Which note each syllable stands on is already answered once for
  every file (`Svg.Collector.SungSyllables`: the page's placement, used by the MIDI's lyric
  events and the MusicXML's `<lyric>`), and the notes' seconds are the playback's
  (`MidiExporter` with the tempo map, as `lilysharp/playback` flattens them). The sung score
  is one more reading of the same two answers — no counting of its own (the 2026-11-04
  lesson: a file that counted syllables itself drifted from the page).
- **Cantilena knows the voice.** Phonemes, pitch curves, the engine's quirks (NEUTRINO's
  lead-in bar, its `ー`, its label format) stay there, behind the score contract. Lily#
  never sees a label or a model.
- **The editor plays both.** The webview owns the clock (the AudioContext); the voice is an
  `AudioBuffer` started at the playback's zero, through the same limiter as the synth.

## 3. The sung score (Cantilena's contract, as Lily# fills it)

```json
{ "sampleRate": 48000, "language": "ja",
  "notes": [ { "start": 0.0, "duration": 0.682, "pitch": 60, "lyric": "ゆ" },
             { "start": 0.682, "duration": 0.227, "pitch": 62, "melisma": true },
             { "start": 0.909, "duration": 0.682, "pitch": 60, "lyric": "き", "breath": true } ] }
```

- **Seconds, not ticks**: tempo, meter, repeats and the practice speed are resolved before
  the engine sees anything. Gaps are rests. `pitch` is MIDI (fractional for a quarter tone).
- **A syllable, not phonemes**, in the language's own script. `lyric` is the page's text
  with no connector (the hyphen and the extender are `LyricItem.ConnectorType`, not text);
  the elision `‿` (`va~ga`) stays one syllable.
- **`melisma: true`** on a note that carries the previous syllable's vowel on: a note the
  page gave no syllable (inside a slur, the end of a tie that the MIDI did not merge, a
  `_` or a detached `~` holder, `__`'s span). A chord in a sung part sings its top note.
- **`breath: true`** on the last note before a rest (a gap in the part), before `@breath`,
  and on the part's last note — where a singer breathes; the engine shapes the phrase end.
- **Which parts sing**: every part a placed lyrics row sings (the same set the MIDI gives
  lyric events to), one voice per (part, lyrics track); verses sing on their own pass as the
  MIDI already does (`SingLyrics`' pass rule). A part with no words is not a voice.
  **A sung part is a singer, whatever its `instrument` or `midiInstrument` says** (owner's
  decision 2026-11-08: "if the score sings the part, the voice sounds"): its instrument is the
  **stand-in** — what the `.mid` plays, and what the preview plays while there is no voice (no
  engine, syllables not kana, not rendered yet, the menu's ♪ switched off). A singer that names
  no instrument and no preset stands in with the General MIDI choir (`GeneralMidi.ChoirAahs`,
  `MidiExporter.SplitIntoPartTracks`), not LilyPond's acoustic grand. **One sound a staff**
  (owner's decision 2026-11-08, evening): the words belong to the staff they are printed
  under. A staff row with a lyrics row under it that sings its part is a singer's staff — the
  voice; a staff row of the same part with no words under it is an instrument's — so
  `staff melody  staff melody  lyrics verse sings melody  staff melody` sounds the melody
  both ways, once each, while `staff melody  lyrics verse sings melody` is voice alone. A
  lyrics row belongs to the nearest staff of its part above it (the page draws it there), else
  the first such staff below; words of a part no staff shows give no voice (the page writes
  no syllable for them). `Svg.Collector.SungStaves` reads this off the score's rows for
  `lilysharp/playback` (`Sung`, `Played`); the preview lets the voice stand in for the
  instrument only where the part is sung and not played.
- **Language**: `ja` when the syllables are kana; otherwise the score is passed as it is and
  the engine answers that it cannot sing it (NEUTRINO: kanji, romaji). The editor shows the
  engine's sentence once per score and plays the instruments alone. A kana reading of kanji
  lyrics (a second track, or furigana in the syllable) is an open question for the owner (§7).

## 4. The server: `lilysharp/sung`

- **Params**: `textDocument`, `renderName` (the score, as `lilysharp/playback`).
- **Answer**: `Voices[]` — `{ Part, Track, Language, Notes[] }` with `Notes` in the contract's
  shape (`T`, `D`, `P`, `L`, `M`, `B`, and `S` the source offset so the voice's notes can be
  told apart in the playback list) — and `Error`.
- **How it is built** (`LilySharp.Core/Midi/SungVoices.cs`, session 1125): the same
  `MidiExporter.Export` the playback uses, and the same clock (`MidiClock.SecondsOf`, which
  `PlaybackOf` now reads too). The lyric events carry the part that sings them
  (`LyricEvent.Part`, written by `SingLyrics`; the events all land on the first part track), so
  a voice is a part's events over that part's notes. The rules, as built:
  - At each onset the highest note of the voice the words stand on most (a chord sings its
    top; a part in two voices sings the one its syllables are on); grace notes and drums are
    not sung; a note is cut where the next begins.
  - A note whose onset carries words takes them. A note with no words that begins exactly
    where the sung note before it ends is a melisma (a slur's; `_` and a detached `~`, for
    which the page files no syllable, come here too). ⚠️ So are the notes of a phrase whose
    words ran out before it did, up to the next rest — the events cannot tell a slur from no
    words; a note after a rest with no words is not sung, a `_` after a rest included
    (measured, Lab `sessions/p1125/e2e/held.lys`).
  - A breath after the last note before a gap, and after the last note.
  - The first words at an onset win: a second row on the same part (the verse printed twice,
    a translation) is not a second voice.
  - `Warnings`: the syllables that are not kana (hiragana, katakana, the long-vowel mark), which
    the Japanese voice will not sing; the notes are still in the list.
- **The practice menu**: speed scales the seconds (a voice cannot be resampled without a
  pitch change, so a speed change re-renders), muted parts drop their voice, the loop and the
  count-in move it with the notes — all in the editor, on the list the server gave, as the
  notes are handled now (`playbackCore.ts` `practised`).

## 5. The editor

- **The engine is the Cantilena extension** (`yotsuda.cantilena`, owner's decision 2026-11-08,
  session 1127 — the shape the OMR reader took: a separate extension that carries the program,
  holds its own settings, and is called through its API). It holds the voice
  (`cantilena.voice`) and NEUTRINO's folder (`cantilena.neutrinoFolder`), with pickers and a
  **Check Setup**; its `activate` returns `{ version, apiVersion: 1, status(), sing(score),
  release() }` (its README, "For extension authors"; `singerCore.cantilenaApiOf` checks the
  shape). Lily# finds it with `vscode.extensions.getExtension`, activates it (no process
  starts), and asks `sing` per voice; the extension keeps one resident `cantilena serve` and
  the WAVs under its own storage. Installed or removed later, `extensions.onDidChange` makes
  Lily# look again. Without it the lyrics are not sung and nothing is said.
- **Setting `lilysharp.playback.singer`** (machine-overridable) is usually empty. A path names
  a `cantilena` program to run here instead — a developer's build — as **one resident
  `cantilena serve`** per extension host (`singer.ts` `SingerProcess`: JSON lines on
  stdin/stdout, `{ id, score, output }` in, `{ id, wav, duration }` or `{ id, error }` out,
  the WAV under the OS temp folder; the protocol is Cantilena's `docs/design.md` §4.1). Both
  ways are a `VoiceBackend` behind the `VoiceRenderer`, whose cache key takes the backend's
  voice name (`voiceName()`: the extension's setting now, the program's default), so a changed
  voice sings anew.
- **The voice takes the instrument's place** (owner's decision 2026-11-08, later the same
  day: hearing both together, then "that is unnatural — one sound a part"; a `singerReplacesPart`
  setting existed for a day in between). While a part's voice plays, its notes leave the
  instruments' list (`played` in `startPlayback`, and the list the MIDI port gets); the
  highlight follows the whole list. No voice yet, or the ♪ off: the instrument — the
  stand-in of §3 — plays.
- **Rendered ahead, not at Play.** When the preview fetches its note list after a compile
  (`refreshPlayback`), the extension asks `lilysharp/sung`, hashes each voice's score with
  the voice name and the speed, and renders what the cache (a Map of hash → wav path, the
  files under the temp folder) does not hold. Play uses what is ready; a voice still
  rendering is skipped this time and the status bar says `Voice: rendering…` (the shape the
  sound font's loading has: never a Play that waits).
- **The webview plays the buffer.** The host reads the WAV and posts its bytes
  (`voice` message: part, bytes, the score's offset of the buffer's first sample — 0); the
  page decodes once (`decodeAudioData`), and `startPlayback` starts `AudioBufferSourceNode`s
  at `t0` through the limiter, where `playbackCore.voiceCues` says (session 1132): the whole
  piece from `offset` into the buffer (a seek, Play from Here), to the buffer's end; a selected
  stretch cut to the score's times it plays (`playSpanWindow`'s `pieces` — one piece, or several
  when the loop closed a repeat up), so the voice stops where the stretch ends; a loop with one
  cue per round laid out (`loopRounds`), so each round sings the stretch again; and after a
  count-in the voice waits for the music instead of running its lead ahead of it. Stop stops
  them all.
- **The practice menu lists each voice** beside the parts, as `♪ part` (owner's request
  2026-11-08): on, the part is sung; off, its instrument plays in the voice's place — a switch
  between the two sounds, never both. `Practice.voicesOff`, read by `sungTracks()`; the host
  lists the voices from `lilysharp/sung` when an engine is there (`requestParts`). The part's
  own toggle silences it altogether.
- **MIDI out** (`lilysharp.playback.midiOutput` set): the port plays the instruments, the
  voice plays here, started from the server's wall-clock zero (`midiStarted`'s `t0Ms`). The
  alignment is the round trip's jitter, some tens of milliseconds; acceptable for a preview,
  noted here.
- **Export** (built 2026-11-08, session 1131): `singJobs` in the host asks `lilysharp/sung`
  for each file's score, has the engine sing every part the score sings (not muted, its ♪
  on) at the practice speed, and hands the WAVs to the render as `AudioJob.Voices`
  (`VoiceTrack`: the file, where it starts — the count-in's lead, `exportPlan` — and its
  length); where the score does not also play the part, its notes leave the instruments'
  list (a video still lights them: `AudioJob.Lit`). `renderAudioFiles` reads each WAV
  (`decodeWav`: 16-bit PCM and the other PCM widths, channels averaged), resamples it to
  `WAV_SAMPLE_RATE` and adds it to both channels from its offset (`mixIn`, the file grown when
  the voice outlasts the notes) before `fileLevel`; a video's length counts the voices
  (`fileSeconds`). So a WAV, an MP3 and an MP4 carry the voice as Play does. No engine, or a
  voice that fails: the instruments, and a line in the output channel.
- **`lysc wav --singer <cantilena> [--voice engine/name]`** (built 2026-11-08, session 1137;
  `LILYSHARP_SINGER` names the program once, as `LILYSHARP_SOUNDFONT` names the font): the
  command line's offline render, the same rules in Core. `ScoreExport.SingTheSungParts` reads
  the voices off the export's MIDI and clock (`SungVoices`, `MidiClock`) and the sung and played
  parts off the score's rows (`SungStaves`); each sung part's score goes as Cantilena's JSON
  (`Audio.SungScoreJson`, the shape `singerCore.cantilenaScore` sends) to the engine —
  `Audio.CantilenaProgram`, one `cantilena sing score.json -o voice.wav` per voice, a separate
  process and nothing linked (§6); the WAV is read back (`Audio.WavReader`: PCM 8–32 and float,
  channels averaged) and `WavRenderer.Render` adds it to both channels from the score's zero,
  resampled by linear interpolation at the engine's own level, the file grown when the voice
  outlasts the notes, as the editor's `mixIn` does. Where the score does not also play the
  part, its notes leave the MIDI before the synth plays it. A voice the engine cannot sing
  leaves the instrument and a warning (`melody is not sung (…); its instrument plays`); the
  voice's kana warning comes along. A file with no `score` block has no sung staff, so nothing
  is sung there, as in the preview. Each voice is one `cantilena sing`, so NEUTRINO's cold
  start is paid once per sung part; `serve` would save nothing (its engines are one server per
  voice too). Nets: `WavExportTests` with a stand-in engine (`FakeSinger`).

## 6. Licensing boundaries

- Lily# **invokes** Cantilena as a separate process and links nothing: no dependency in a
  csproj or package.json, nothing new in THIRD-PARTY-NOTICES. The setting names the exe as
  `lilysharp.playback.soundFont` names a font; the user installs both.
- Nothing of NEUTRINO or of any voice is read, copied or shipped by Lily#. Cantilena's
  README and THIRD-PARTY-NOTICES carry that side.
- Should a backend ever be linked in-process (Cantilena.Core as a NuGet package), Apache-2.0
  is GPLv3-compatible: a notice entry and the NOTICE file's lines, nothing else.

## 7. Stages, and the owner's questions

> The stages below are the first release's. What follows them — the second engine, languages,
> expression, the choir, more engines and platforms — and the short-term order of work is
> `docs/singing-roadmap.md` (2026-11-08).

1. **Cantilena `serve`** (the resident JSON-lines process) — done 2026-11-08 in the
   Cantilena repo (its `docs/design.md` §4).
2. **The server's `lilysharp/sung`** — done 2026-11-08 (session 1125): `SungVoices`,
   `MidiClock`, `LyricEvent.Part`, the request and its DTOs; nets `SungVoicesTests` (the
   held note, the slur, the chord's top, the words running out, the kana warning, two parts,
   two rows on one part) and `SungRequestTests` (the voice's seconds are the playback's).
   End to end: the owner's `雪やこんこ.lys` through Core to Cantilena's JSON and NEUTRINO's WAV
   (Lab `sessions/p1125/e2e`).
3. **The editor** — done 2026-11-08 (session 1126), as §5 says, with these notes: the host
   renders when the preview fetches its notes (`requestPlayback`) and when its tempo changes
   (`practiceState`); a voice is keyed by its score at the speed and the voice name
   (`singerCore.voiceKey`) and the WAV named by the key under the temp folder
   (`lilysharp-voice/`); the page plays it from `voiceOrigin` (the score time of the played
   list's 0: 0 for a plain Play, the stretch's start for a span, null for a loop or a candidate's
   notes, where the voice stays out) and after a count-in (`countInFix`); a span's voice was cut
   where the stretch ends only by the instruments' end timer — the buffer played on to the piece's
   end if the stretch was shorter, until session 1132 (§5, `voiceCues`). Nets:
   `test/singerCore.test.ts` (the score, the key, the wire, the manifest); the engine itself was
   driven from node against the real `cantilena serve` (Lab `sessions/p1126/probe-singer.js`).
   The real VS Code is the owner's to check (MANUAL-CHECKS §3.7). Session 1127 (owner's
   decision): the engine became the **Cantilena extension** (`yotsuda.cantilena`, in the
   Cantilena repository's `editors/vscode`, packed by its `tools/pack-extension.ps1`), and
   Lily#'s `singerVoice` setting and **Choose Singing Voice…** went away — the voice is
   Cantilena's; `lilysharp.playback.singer` stayed as the developer's override.
4. **Export mixing** — done 2026-11-08 (session 1131, §5 "Export"; nets `test/voiceMix.test.ts`).
   **A span's voice stopping at the span's end, the voice in a loop, and the count-in's lead** —
   done 2026-11-08 (session 1132: `voiceCues`, `playSpanWindow.pieces`, `loopRounds`; nets in
   `test/playbackCore.test.ts`). **`lysc wav --singer`** — done 2026-11-08 (session 1137, §5).
   Then MIDI-out alignment if the jitter is heard (`docs/singing-roadmap.md` §3).

Open for the owner before stage 3:
- **Kana for kanji lyrics**: a second track (`lyrics ja sings melody { … }` kept hidden), a
  reading in the syllable (`漢字|かんじ`), or kana-only for now (the engine's sentence).
- **A choir**: several parts with words sing the same voice each, or only the first.
- **Replace or double** the part's instrument (the setting's default).

## 8. Where things are

| What | Where |
|---|---|
| The engine, its contract, the backends | `C:\MyProj\cantilena` — `README.md`, `docs/design.md`, `src/Cantilena.Core` (`SungScore`, `ISinger`, `Neutrino/`), `src/Cantilena.Cli` |
| Which note a syllable stands on | `LilySharp.Core/Svg/Collector/SungSyllables.cs`; the MIDI's events `MidiExporter.SingLyrics`; the MusicXML's `MusicXmlExporter` (`syllabic`, `extend`) |
| The connector and the holders | `Svg/Model/LyricItem.cs` (`ConnectorType`), `Svg/Collector/LyricSyllableReader.cs` (`Marker`) |
| The seconds | `LilySharp.Lsp/LilySharpLanguageServer.Commands.cs` (`PlaybackOf`, `SecondsAt`) |
| The preview's clock, Play, the limiter, MIDI out | `editors/vscode/src/extension.ts` (`startPlayback`, `midiStarted`), `src/webview/previewSynth.ts` |
| The practice menu on a list | `editors/vscode/src/webview/playbackCore.ts` (`practised`) |
| The offline render | `editors/vscode/src/audioRender.ts`, `audioRenderCore.ts` |
| The probes of 2026-11-08 | Lab `sessions/p1124/utau/` (MIDI and MusicXML exports, NEUTRINO's labels and WAVs, the first Cantilena WAV) |
