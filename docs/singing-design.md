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

- **Setting `lilysharp.playback.singer`** (machine-overridable): the path of `cantilena`
  (the exe, or a `dotnet tool` on the PATH); empty = no voice, nothing else changes.
  `lilysharp.playback.singerVoice` names the voice (`neutrino/MERROW` by default; `cantilena
  voices` lists them, and **Lily#: Choose Singing Voice…** will show that list). NEUTRINO's
  folder is Cantilena's business (`CANTILENA_NEUTRINO_DIR` or its `cantilena.json`).
- **One resident `cantilena serve`** per extension host, started on the first score with a
  voice and killed on deactivate: JSON lines on stdin/stdout — a request `{ id, score, voice }`
  and an answer `{ id, wav, duration }` or `{ id, error }`, `wav` a file under the OS temp
  folder. Resident because the engine's server is warm only inside one process (NEUTRINO
  binds its server to the owner's pid; a `cantilena sing` per Play would pay the cold start
  every time). The protocol is Cantilena's (`docs/design.md` §4 there); Lily# only speaks it.
- **Rendered ahead, not at Play.** When the preview fetches its note list after a compile
  (`refreshPlayback`), the extension asks `lilysharp/sung`, hashes each voice's score with
  the voice name and the speed, and renders what the cache (a Map of hash → wav path, the
  files under the temp folder) does not hold. Play uses what is ready; a voice still
  rendering is skipped this time and the status bar says `Voice: rendering…` (the shape the
  sound font's loading has: never a Play that waits).
- **The webview plays the buffer.** The host reads the WAV and posts its bytes
  (`voice` message: part, bytes, the score's offset of the buffer's first sample — 0); the
  page decodes once (`decodeAudioData`), and `startPlayback` starts an `AudioBufferSourceNode`
  at `t0` with `offset` into the buffer (a seek, Play from Here) through the limiter. Stop
  stops it; a loop restarts it with the notes.
- **The sung part's instrument is muted** while its voice plays (the notes with that `N`
  are left out of the synth's list; the highlight follows the full list as before):
  `lilysharp.playback.singerReplacesPart`, default on; off doubles the voice with the preset.
- **MIDI out** (`lilysharp.playback.midiOutput` set): the port plays the instruments, the
  voice plays here, started from the server's wall-clock zero (`midiStarted`'s `t0Ms`). The
  alignment is the round trip's jitter, some tens of milliseconds; acceptable for a preview,
  noted here.
- **Export** (`audioRender.ts`, the offline synth): each voice's WAV is read in node
  (16-bit mono, the engine's rate), resampled to `WAV_SAMPLE_RATE` (linear), and added to
  both channels before `fileLevel`, so a WAV, an MP3 and an MP4 carry the voice. `lysc wav`
  (Core, MeltySynth) stays instruments-only until a `--singer <cantilena>` is wanted.

## 6. Licensing boundaries

- Lily# **invokes** Cantilena as a separate process and links nothing: no dependency in a
  csproj or package.json, nothing new in THIRD-PARTY-NOTICES. The setting names the exe as
  `lilysharp.playback.soundFont` names a font; the user installs both.
- Nothing of NEUTRINO or of any voice is read, copied or shipped by Lily#. Cantilena's
  README and THIRD-PARTY-NOTICES carry that side.
- Should a backend ever be linked in-process (Cantilena.Core as a NuGet package), Apache-2.0
  is GPLv3-compatible: a notice entry and the NOTICE file's lines, nothing else.

## 7. Stages, and the owner's questions

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
   notes, where the voice stays out) and after a count-in (`countInFix`); a span's voice is cut
   where the stretch ends only by the instruments' end timer — the buffer plays on to the piece's
   end if the stretch is shorter (noted, not built: a stop at the span's end). Nets:
   `test/singerCore.test.ts` (the score, the key, the wire, the manifest); the engine itself was
   driven from node against the real `cantilena serve` (Lab `sessions/p1126/probe-singer.js`).
   The real VS Code is the owner's to check (MANUAL-CHECKS §3.7).
4. **Export mixing**; then MIDI-out alignment if the jitter is heard; then `lysc wav`.

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
