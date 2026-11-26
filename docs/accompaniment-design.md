# Arrange an Accompaniment…

The fourth step of the way from a song to a dance (Shosa's `docs/ROADMAP.md` §2): **song → singing →
dance → video**. A score read from a recording (Orecchiante) plays that recording under the song. Its
sound cannot be published, and taking the voice out of a recording spoils it. This step writes the
score's own accompaniment from its chords, so the recording can go silent. Started 2026-11-26
(session 1397).

## The owner's decisions (2026-11-26)

- **Copilot's chat writes the notes**, as in Write a Song over These Chords… (not a fixed pattern,
  not patterns plus AI).
- **The instruments**: the ones the request names; with none, a band (piano, bass, drums). A part
  the score has (a bass read from the recording) stays, and only the missing instruments are added.
- **The recording**: it is not taken off. Its bars still time the score, the song and the dance.
  When the arrangement is accepted, **its sound alone goes silent**, so the musician can still find it:
  1. The file beside the chart keeps it: `"silent": true` in `X.orecchiante.json`. Shosa reads the
     same file.
  2. Right after Accept a message says the recording is silent, with **Play the Recording Too**.
  3. The preview's mixer keeps the Recording line: its button says `(silent)`, and pressing it turns
     the recording on again. The file then forgets `"silent"`.
  4. The exports (WAV, MP3, MP4) follow the mixer. Their message says the recording is not in the file.

## What the musician does

Right-click a `.lys` (Explorer, the editor tab, the editor), or run **Lily#: Arrange an
Accompaniment…** (with no score open, a score is picked). One question: the instruments and the
style, or nothing for a band. Copilot's chat (agent mode) is opened with the request. The panel
beside the score shows the parts to play, and the musician accepts them or not.

## What the chat is told (`accompanimentCore.ts`)

- `accompanimentRequest`: the file's absolute path, the parts it has (they stay), that a recording
  plays under it (the accompaniment takes its place), to read the guide `lilysharp_readGuide` topic
  `accompaniment`, and the request (or "a band in the style the tempo, chords and title suggest").
- The guide, topic `accompaniment` (`accompanimentGuide`): the steps (`ACCOMPANIMENT_STEPS`), then
  the composing guide's items *A band* and *A style named is its rhythm first*. They are copied from
  `docs/COMPOSE_FOR_LLM.md` when read, so the text stays in one place.
- The steps: read the score and its chords, and leave its parts alone; plan the style, the
  instruments and each section's role; fill every section the form plays, every bar, on its chords;
  stay under a sung part; write it all in **one** `lilysharp_writePart` call (new parts:
  `instrument piano-right`, `instrument bass` with an electric bass's sound, `clef percussion`).
  A real VS Code found that `instrument piano` is no preset, so the guide names `piano-right`.

## The silent recording

| Where | Before | Silent |
|---|---|---|
| The file beside the chart (`recordingCore.ts`) | — | `"silent": true`, read into `Recording.silent`, written back with the rest as it was; heard again, the item is removed |
| The preview (`previewHtml.ts`) | the recording off: the score on its own written seconds | `recSilent`: not heard, and the score still on the recording's bars (`recTimesOnly` → `warpNow`); the button says `(silent)` and turns it on (→ `recordingSilent` → `setRecordingSilent(false)`) |
| The practice state sent to the extension | `recording` only while it sounds | also while silent: its bars, `level: 0`, `silent: true` — the voices are sung on its bars (`recordingWarpOf`) |
| The exports (`recordedExportPlan`) | mixed in while it sounds | the notes on its seconds, `recording: null`; the message says it is not in the file |
| The stems (`stems.ts`) | `recording` and `recordingPart` stems, `timeline: 'recording'` | `timeline: 'recording'`, no recording stems, a note saying why |

When an arranged proposal is accepted (`proposeScore`: the score was asked for by Arrange an
Accompaniment…, and the change adds parts, `addedParts`), `setRecordingSilent(true)` writes the file.
The preview hears of it through the file's watcher, which only covers files inside the workspace.

## Checked

- `test/accompanimentCore.test.ts`: the parts kept and added, the request, the guide and its items,
  the command and topic, `"silent"` read and written, an export timed by the recording and not
  mixed in, the mixer's way back, the export's message, the stems.
- A real VS Code 1.147 (Lab `sessions/p1397/vscode-e2e/run2.js`), on a chart with a 5-second
  recording:
  - Arrange with no score open picks the score, asks, and hands the chat a request that keeps `bass`.
  - The guide topic answers (6.4 KB, one part, with *A band*).
  - `lilysharp_writePart` adds a piano. The candidate panel's Accept writes it in, and the file beside
    the chart then says `"silent": true` (bars and `tempoBpm` kept), and the message is shown.
  - The preview is sent `silent`, and the page answers with its bars, `level: 0`, `silent: true`.
  - The page's `recordingSilent` makes the file forget it.

## Not done yet

- The owner's check in a real VS Code (`editors/vscode/MANUAL-CHECKS.md`, "Arrange an Accompaniment").
  It covers the chat's arrangement in a real song, the mixer's `(silent)`, Play on the recording's
  bars, and an export without the recording.
- Shosa: its preview plays the recording itself when the stems are not there, and so does its MP4.
  Both are to honour `"silent"` (Shosa's `docs/repro/lilysharp-report-2026-11-26-silent-recording.md`).
- A chart outside the workspace: the preview hears of the change only at its next refresh.
