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
- **A lead of 0.3 s before the score's 0** (`VOICE_LEAD_S`, `SungScoreJson.Lead`; session 1147):
  every note is sent 0.3 s later than the score says, and the WAV's 0.3 s is the score's 0. The
  room is for the first syllable's consonant, which a singer places BEFORE the beat: rendered
  from the score's 0 it was cut away — NEUTRINO's さ at 0 had its s from −0.103 to +0.011 s, and
  the 11 ms left sounded as た (owner's report 2026-11-08). Inside the score a consonant lies in
  the previous syllable or a rest and was never cut; only the piece's first syllable was. The
  page keeps `lead` with each voice and measures `pre`, how much sound the lead holds (the
  first sample over 0.02, `soundStart`): a Play from the score's 0 with no count-in starts the
  instruments `pre` later and the voice from its consonant (`withVoiceLead`; a count-in gives
  the room itself; a start inside the score, or on a MIDI output, pulls nothing). An export
  moves the whole file — notes, lit notes, the count-in's lead, the voices — by `pre`;
  `lysc wav --singer` the same (`WavRenderer.RenderWithVoices`, "Voice lead: 0.10 s" in its
  notes). Cantilena is not changed: the lead is a rest to it.
  ⚠️ Rounded at the note's ENDS (`start` and `start + duration` each rounded, the duration
  their difference), never start and duration apart: sixteenths at 132 are repeating
  decimals, and the engine refuses notes that overlap — 雪やこんこ's melody was refused with
  "note 22 at 6.364 s starts before note 21 ends (6.364 s)" until session 1144
  (`singerCore.cantilenaScore`, `Audio.SungScoreJson`; Cantilena's `ScoreJson.Read` also trims
  an overlap under a millisecond as a caller's rounding).
- **A syllable, not phonemes**, in the language's own script. `lyric` is the page's text
  with no connector (the hyphen and the extender are `LyricItem.ConnectorType`, not text);
  the elision `‿` (`va~ga`) stays one syllable.
- **`melisma: true`** on a note that carries the previous syllable's vowel on: a note the
  page gave no syllable (inside a slur, the end of a tie that the MIDI did not merge, a
  `_` or a detached `~` holder, `__`'s span). A chord in a sung part sings its top note.
- **`breath: true`** on the last note before a rest (a gap in the part), on a note marked
  `@breath`, and on the part's last note — where a singer breathes; the engine shapes the phrase
  end. An `@breath` with a sung note right after it takes the room out of its own note (a quarter
  of a second, at most a third of the note), so the next note still starts on its beat.
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
  the first such staff below. **A row that sings a part no staff shows makes that part an
  unseen singer** (owner's decision 2026-11-09, 雪やこんこ: `staff m2  lyrics verse sings
  melody` — "the melody's notes are not drawn, and the song must be sung"): the page already
  places such a row by the melody's rhythm (`MeasureCollector.CollectMelodyFor`, LilyPond's
  `\lyricsto` over a NullVoice) and now keeps that melody on the model
  (`MultiStaffScore.RowMelodies`); `SungSyllables` puts the syllables on its notes, the MIDI
  export sounds the part beside the parts the score engraves (`SungStaves.UnseenSungParts`,
  the stand-in sound — so the `.mid` carries it too), and `SungStaves` lists it as sung and
  not played, after the staves. The preview then sings it with nothing to light; the practice
  menu shows `♪ melody` and no italic `melody`. Until then the words of a part no staff
  showed gave no voice. The MusicXML has no part to write those words on and leaves them out.
  `Svg.Collector.SungStaves` reads all this off the score's rows for `lilysharp/playback`
  (`Sung`, `Played`); the preview lets the voice stand in for the instrument only where the
  part is sung and not played.
- **Language**: a fact of the words (session 1167): `language NAME` on the file or a lyrics
  track, `japanese` when nothing is written (`Music.LyricLanguage`); the voice's `Language` is
  its first words' tag, and only Japanese words are checked for kana. Otherwise the score is
  passed as it is and the engine answers that it cannot sing it (NEUTRINO: kanji, romaji).
- **Singer** (session 1168): `singer "…"` on the part, the track or the score's row, the nearest
  winning (`Music.LyricSinger`, `SungVoice.Singer`). Lily# carries the string; the Cantilena
  extension resolves it (`resolveSinger`: `cantilena.voices`, then the installed voices, then
  the default with a sentence), and the resolved voice is the cache key's. Between the quotes the
  editor asks the server for the words' language (`lilysharp/singerSlot`) and offers the voices
  that sing it and the roles cast to one (`singerChoices`); a voice not saying what it sings is
  left out. `lysc wav --singer` sings a written `engine/name` and takes a role as the default. The editor shows the
  engine's sentence once per score and plays the instruments alone.
- **Kanji with a reading** (owner's decision 2026-11-08, session 1138): the reading is glued to
  the word in the syllable — `漢[かん]` prints 漢 and sings かん, `大[おお]空[ぞら]` prints 大空
  and sings おおぞら (`Music.LyricReading`; GRAMMAR.md `LyricText`). One row, one place: the file
  that prints the kanji says how it is sung, and nothing is counted twice — the page's syllable
  carries the printed word, `SungSyllables` reads the sung word off the same token by its source
  offset, `LyricEvent.Sung` carries it to `SungVoices`. The `.mid`'s lyric events, the MusicXML
  `<text>` and the LilyPond twin keep the PRINTED word (a karaoke display shows the words as
  written; neither format has a reading); the VSQX sings the reading. No ruby is printed (the
  page is unchanged; LilyPond prints none). A malformed reading is LYS4036, one that is not kana
  LYS4037; a kanji with no reading is still the engine's one sentence.

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
  own toggle is the instrument's alone (session 1164, owner's report: the part off and its voice
  on gave silence — `startPlayback` returned on an empty note list before the voices started;
  it plays a list with no note when a voice sings, the end being the voices', and the export's
  `singJobs` no longer drops a muted part's voice). Until then the part's toggle silenced the
  voice too. **What sounds is what is on, each on its own** (session 1165, owner's report: the
  default score with `melody`, `♪ melody` and `m2` all on played no melody instrument — the
  voice still took the instrument's place): the instruments whose buttons are on, plus the
  voices whose notes are on. The score's intent is the **default**: a part the score only sings
  (`Sung` and not `Played` — a singer's staff with no staff without words; `partsData.sungOnly`)
  has its instrument button off, the voice on; pressed, `Practice.instrumentsOn` names it and
  both sound (Play: `instrumentSounds`; exports: `singJobs` silences a sung-only part's track
  unless `instrumentsOn`). The stand-in survives: a sung-only part's instrument plays while its
  voice is not there (not ready, failed, or its fader at 0); with its `♪` switched off and its
  button off nothing sounds, and this replaces the session-1129 reading of `♪` off above as
  "its instrument plays". A part the score plays keeps its
  instrument on by default, and a voice turned on for it (`voicesOn`) sounds beside it. **Every singable lyrics of the file is listed** (owner's
  request 2026-11-09, session 1163: "whatever score is selected, show every part and every
  singable lyrics"): `lilysharp/sung` with `allLyrics` adds, to the score's rows, a row for each
  lyrics track the score does not place, singing the track's default part (`LyricBindings.
  TargetOf`, or the file's one part), over that part's melody (`MidiExporter.AllParts`, the
  unseen-singer machinery of session 1151); `SungVoiceDto.InScore` says which voices the score
  sings itself (`SungStaves`). The menu lists the score's own first (on unless `voicesOff`),
  then the others in italics (off unless `Practice.voicesOn`); pressed, the host renders the
  voice at once and it sings in the part's instrument's place (the instrument sounds beside a
  voice only where the score both sings and plays the part — two staves). One voice a part
  still: two tracks on one part give one voice, the first words at an onset winning. The fader to its right (owner's request 2026-11-09,
  docs/playback-design.md "A fader beside each part") is the voice's gain: a `GainNode` in the
  page (`startVoices`), `VoiceTrack.gain` into `mixIn` in the exports (`singJobs` reads
  `Practice.voiceLevels`); at 0 the voice is as switched off.
- **The pane says where the voices stand** (owner's request 2026-11-08, session 1141): a small
  **microphone over the corner of ▶** — hidden with no voice; grey and pulsing while the engine
  sings (`preparing`); blue when every voice is decoded and a Play will use it (`ready`); the
  error colour when one will not be sung (`failed`, the engine's sentence in the title) — and a
  sign after each `♪ part` toggle (`…`, `✓`, `⚠`). The first shape was a `♪` button beside ⏹;
  the owner's look (session 1142): it looked like a button and was none, and a note does not
  say *singing* when the instruments are notes too — so a status, not a button, over the Play
  it speaks of, drawn as what sings. The host posts `voiceState`
  (`preparing` before the engine is asked, `failed` with the reason); `ready` is the page's
  own, when `setVoice` has decoded the WAV, so it means playable; a speed change starts the
  cycle again. One badge for all voices: `playbackCore.voiceBadge` (failed if any, else
  preparing if any, else ready). Each time the host asks `lilysharp/sung` it also posts
  `voiceParts`, the parts the score sings now, and the page drops the voices and states of any
  other — so a score or a text with no lyrics shows no microphone (owner's report, session 1143:
  it stayed on). Another document or another score (`followVoices`: `voiceDocs`, `uri\nrender`)
  empties the page's voices as the preview turns to it (`updatePreviewContent`) and again ahead
  of its notes (`clearVoices` before `playbackData`, in order): the voices are kept by part
  name, and a Play right after a tab switch sang the previous file's `melody` over the new notes
  (owner's report, session 1145). The emptying leaves a nameless `preparing` placeholder, so the
  microphone turns grey, not blue or gone, until `voiceParts` names the new voices (owner's
  report, session 1146: it stayed blue), and the new voices are asked for at once. A Play still
  never waits: the badge says why the instrument stood in.
- **When the voice is prepared** (owner's request 2026-11-08, session 1148: the microphone said
  ready over a voice of the old words, pulsed while nothing was being sung, and "if the voice
  can be prepared as soon as the lyrics are edited, better — unless the whole machine slows").
  The host's `prepareVoices(panel, uri, reason)` runs after **every render** (`edit`), at Play
  (`play`) and at a speed change (`speed`): it asks `lilysharp/sung`, posts `voiceParts`, and
  for each part compares the key its score and the engine's voice would give
  (`VoiceRenderer.key`, no engine call) with the key of the voice the page holds
  (`postedVoices`). Equal: nothing. Different: the page drops the old voice (a Play meanwhile
  plays the instrument, not the old words) and the part is shown **`waiting`** — a grey, still
  microphone, a fourth state between none and `preparing` — and the render is queued
  (`singerCore.PrepareQueue`, one job per preview and part, the newest text winning, one in
  flight): at once after a Play, a speed change or a turn to another document, or when the
  engine has the voice already; after `PREPARE_IDLE_MS` = 1.5 s of rest when the text was
  edited — typing restarts the clock, so the engine never works under the keystrokes and sings
  each phrase at most once per pause; and not before Play with
  `lilysharp.playback.singerPrepare` = `onPlay` (for the CPU's DiffSinger, where every pause
  would cost seconds; the default is `afterEdit`). `preparing` (pulsing) is posted only when the
  engine is actually asked; `PREPARE_TIMEOUT_MS` = 120 s without an answer is `failed`. When
  the server cannot answer (a syntax error) the voices the page has stay as they are and the
  nameless placeholder of a turn is settled (`voiceSettled`), so nothing pulses over nothing;
  with no engine everything is dropped. A part the score sings no more (its lyrics row
  commented out) is forgotten on the host too (`sungParts`, the last `voiceParts`): the
  engine's answer to the earlier text, had it still been singing, lands nowhere — it used to
  arrive after the page had emptied and light the microphone again (owner's report, session
  1152). The host checks the same before it posts `preparing` too (session 1160: a switch to a
  file with no lyrics while the engine was being asked — the `preparing` landed after the empty
  `voiceParts` and pulsed for ever), and the page keeps the last `voiceParts` (`sungNow`) and
  refuses a `voiceState` or a `voice` of any other part, so a file with no lyrics shows no
  microphone whatever arrives late. The nameless placeholder itself expires after 10 s
  (`PLACEHOLDER_MS`, session 1161: Untitled-9.lys, no lyrics, opened with the microphone
  pulsing for ever — the server answers in milliseconds, so an older placeholder is a lost
  answer, not a slow one; the page says so in the output channel). `prepareVoices` writes one
  line per ask in the output channel (`singer: prepare (edit) file: no sung part`), and makes
  the page's key agree with its own first (`followVoices`), so a render that corrected the
  score's name cannot leave the placeholder unanswered. **The actual cause** (session 1162,
  read off the owner's output channel, where every ask ended in `no sung part`): the badge's
  own `display: inline-flex` beat the `hidden` attribute, so from session 1142 on the badge
  never hid and kept the look of its last state — the pulsing placeholder — on a file with no
  lyrics; `.voice-badge[hidden] { display: none }` as the other panels have, and the state
  attribute is removed when there is nothing to say. Nets: `test/singerCore.test.ts` (the queue, the mode),
  `test/playbackCore.test.ts` (the badge's `waiting`).
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
  voice that fails: the instruments, and a line in the output channel. **Both doors** sing
  (session 1153, owner's report "the MP4 export carries no voice"): the preview's Export
  button (`exportMedia`) and the Explorer's / palette's folder export (`exportBatch.ts`,
  `deps.singJobs`), which had rendered the instruments alone since session 1131; for a file
  not open, `lilysharp/sung` takes its `path` as `lilysharp/export` does. The engine's WAV
  begins `VOICE_LEAD_S` before the score's 0, so a voice with no count-in starts at a negative
  offset: `mixIn` leaves that much of its head out and starts the rest at 0 (it moved the whole
  voice to 0 from session 1147 to 1153 — 0.3 s late).
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

Open for the owner before stage 3 (as decided since):
- **Kana for kanji lyrics**: a second track (`lyrics ja sings melody { … }` kept hidden), a
  reading in the syllable, or kana-only for now (the engine's sentence). **Decided 2026-11-08
  (session 1138): a reading in the syllable, `漢[かん]` (§3); the `.mid` keeps the kanji; no
  ruby is printed.**
- **A choir**: several parts with words sing the same voice each, or only the first.
- **Replace or double** the part's instrument (the setting's default). **Replace (session 1129).**

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
