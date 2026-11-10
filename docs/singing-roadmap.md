# Singing — the roadmap

Where the singing of lyrics goes from here, long term and short term. Written 2026-11-08 at the
owner's request, after stages 1–4 of `docs/singing-design.md` §7 had shipped in one day (sessions
1124–1131). The design itself stays in `singing-design.md`; this page is the order of work and
the reasons. What is being worked on *now* is `HANDOFF.md` §1.0, as always.

The score spellings here (`lyrics NAME sings PART`, `singer` on a track) were replaced on 2026-11-10:
`docs/lyrics-singer-design.md` and `GRAMMAR.md` say what is written now.

## 0. The goal

**Lily# as the notation editor that sings — through whichever engine the user has.** The owner's
aim (2026-11-08): grow Lily# into a product that integrates several singing engines. Few score
editors can play a score with its lyrics at all; done well, this gives Lily# a place of its own
among open-source notation programs.

What that means, measurably:

1. **A score with words sings on Play**, in time with the instruments, in the preview and in
   every export. *(Shipped for Japanese through NEUTRINO on Windows; the rest of this page widens it.)*
2. **More than one engine, behind one contract.** Adding an engine is one backend in Cantoria;
   Lily# does not change. The user picks engine and voice the way they pick a sound font.
3. **More than one language and script.** Kana today; kanji with a reading, then English and
   others as voices for them exist.
4. **Every platform Lily# runs on** can sing something: Windows, macOS, Linux; with a GPU or
   without one.
5. **A new user hears a voice within minutes of installing**, without reading a licence page
   first — and never a voice whose terms Lily# or Cantoria may not pass on.

The division of labour does not move: **Lily# knows the music, Cantoria knows the voice, the
editor plays both** (`singing-design.md` §2). Everything below sorts into those three.

## 1. Where things stand (2026-11-08)

| Piece | State |
|---|---|
| Contract (`SungScore`: seconds, MIDI pitch, syllable, melisma, breath) | Shipped, Cantoria `README.md` |
| `neutrino` backend (NEUTRINO Tau v3.2.x, Japanese, Windows) | Sings; warm render ≈ 0.1× real time on a GPU |
| `diffsinger` backend (in-process ONNX; OpenUtau-format voices; kana, and English through a voice's `dsdict-en.yaml`, session 1190) | Sings (sessions 1134–1136); on Windows through DirectML on any DirectX 12 GPU, the CPU where there is none (session 1139, Cantoria 0.4.0) |
| `voicevox` backend (a running VOICEVOX over its local HTTP API; Japanese) | Sings: run against VOICEVOX 0.26.2 (engine 0.26.1) on the owner's machine with three voices (session 1194); the playing in the editor is left to MANUAL-CHECKS |
| `cantoria serve` (resident JSON-lines engine) and the CLI | Shipped |
| Cantoria VS Code extension `yotsuda.cantoria` (settings, pickers, API) | Built, VSIX packed, **not yet published**; repository **not yet on GitHub** |
| `lilysharp/sung`, the editor's voice playback, the practice menu's `♪ part` | Shipped; the owner heard it synchronised in the real VS Code |
| Export (WAV/MP3/MP4) with the voice mixed in | Shipped (session 1131); manual checks open (`MANUAL-CHECKS.md` §3.7) |
| Rules: a sung part is a singer, one sound a staff, the instrument stands in | Decided and built (sessions 1129–1130) |
| `lysc wav --singer <cantoria>` (the command line sings through the same engine) | Shipped (session 1137); heard on a real voice is the owner's |
| Kana for kanji: a reading glued to the word, `漢[かん]` | Shipped (session 1138; `singing-design.md` §3) |
| Open questions | A choir (every part with words, or the first) |

## 2. Long term — the phases

Phases, not dates: each has a *done when*. They overlap where the work is in different repos.

### Phase A — Out of the door *(short term, §3)*

Publish what exists, close the small leftovers, and settle the contract's one gap before a
second engine arrives.

- Cantoria repository public; the extension on the Marketplace; Lily# release notes say the
  preview sings.
- A voice is named `engine/voice` everywhere (`neutrino/MERROW`), so a second engine needs no
  new setting; the extension's API lists what is installed (`voices()`).
- The leftovers: a span's voice stops at the span's end; the loop sings; `lysc wav --singer`.
- The owner's decision on **kana for kanji** (§4), built.

*Done when* a user with NEUTRINO installs two extensions from the Marketplace, opens a kana
score, and hears it — and every item of `MANUAL-CHECKS.md` §3.7 is ticked.

### Phase B — The second engine: DiffSinger in-process

The biggest lever on goals 2–4 at once. DiffSinger voices exported for OpenUtau exist in many
languages, run on the CPU (seconds, not sub-second) or on a GPU (CUDA, DirectML), and are a
folder the user installs — the same shape as a sound font. The C# inference exists under MIT
(OpenUtau `OpenUtau.Core/DiffSinger/`), to be adapted with its notice, as Cantoria's
`THIRD-PARTY-NOTICES.md` already plans.

Cantoria's side (its `docs/roadmap.md` has the detail):

- Read a voice folder (`dsconfig.yaml`, dictionaries, the linguistic / duration / pitch /
  variance / acoustic models, the vocoder); phonemise per the voice's language; run through
  `Microsoft.ML.OnnxRuntime`, GPU provider when present.
- The same `ISinger`; `serve` serves both engines; `voices` lists both.
- Builds for `osx-arm64` and `linux-x64` beside `win-x64` (self-contained .NET, as now); the
  extension carries them per platform target. *Built 2026-11-08 (session 1140, Cantoria 0.4.0:
  three VSIX targets, CI on the three platforms); not yet heard off Windows.*

Lily#'s side: nothing in Core. The extension shows the engine's name in its status line and
passes `language` from the lyrics block (Phase C) — that is all.

*Done when* a kana score sings through a DiffSinger voice on a machine with no NEUTRINO and
no GPU, on Windows and on one of macOS / Linux, and the same `.lys` sings through either
engine by changing one setting.

### Phase C — Languages and scripts

Phonemisation stays the engine's (it is voice-specific); what Lily# owes is **the text as
sung** and **the language it is in**.

- **Kanji lyrics** (decided in Phase A, §4): a reading travels with the syllable; the page
  prints the kanji, the voice sings the kana. Romaji lyrics are read as kana for a Japanese
  voice (a small table; Lily# or Cantoria — decide with the kanji answer).
- **A language on the lyrics block** — *built 2026-11-09, session 1167* (owner: the language
  belongs to the words, before any singer's; names, not codes, lower-case, for the editor's
  completion): `language english` for the file, `lyrics en language english sings melody { … }`
  for a track, `japanese` when nothing is written (`Music.LyricLanguage`, LYS4038). The sung
  score carries the tag, and a voice that says it sings another language refuses in one
  sentence, the instrument playing (session 1168). **Left for Cantoria:** the DiffSinger
  dictionary is still fixed at `ja`, so no voice sings another language yet.
- **English** sings through a DiffSinger voice's dictionary (session 1190): the hyphens Lily# already
  prints mark a word's syllables, the engine looks the word up whole and deals its phonemes back.
  **Others** (a voice's `es`, `fr`, `it`, `pt`, `zh` dictionaries) still need their syllable rules.
- **Elisions, diphthongs, consonant clusters**: what the page treats as one syllable is sent
  as one; the voice's dictionary decides the rest. Where a language needs a hint the page
  cannot give (stress, a split the dictionary gets wrong), a per-note `phonemes` override in
  the contract — advanced, optional, unknown fields are already ignored by older engines.

*Done when* one `.lys` with a Japanese verse and an English verse sings both, each through a
voice for its language, with no change to the page.

### Phase D — Expression

What a singer does beyond pitch and length, as **optional hints** on the contract (the
contract stays minimal; an engine maps what it understands and ignores the rest):

- Dynamics (`@p` … `@ff`, hairpins) → a loudness curve; slurs → legato (today a slur only makes
  melismas); `@breath` → already a breath; fermatas and tempo changes are already seconds.
- Vibrato and portamento: engine defaults first; a per-part or per-note hint only if the owner
  wants control (`singing-design.md` §1: "tuning is out of scope until a caller needs it").
- Playback parity: the stand-in instrument and the voice should follow the same dynamics so
  switching `♪` does not change the balance.

*Done when* a crescendo written on a sung phrase is heard in the voice through at least one
engine, and the mix with the instruments needs no manual gain.

### Phase E — The choir and the ensemble

- **A voice per part** in the score (`sings soprano` names its voice as `midiInstrument` names
  an instrument — syntax to be designed), the practice menu overriding per session.
- SATB each in its own voice at once; a part sung by two voices (unison divisi) later.
- Natural ensemble: small per-voice timing and pitch offsets (an engine or a Lily# option),
  the count-in and the loop per voice as per instrument.
- Per-voice gain and pan in the practice menu, saved with the file or not (owner).

*Done when* a four-part hymn sings in four voices and the balance can be set from the menu.

### Phase F — More engines, and a voice on every machine

Candidates, in the order their value / cost looks today (each is a Cantoria backend; none
needs Lily# changes once Phase A's naming is in):

| Engine | How | Languages | Why / why not |
|---|---|---|---|
| **DiffSinger** (Phase B) | in-process ONNX | many (per voice) | open, cross-platform, CPU-capable — the main one |
| **NEUTRINO** (done) | invoked | ja | quality; Windows; closed freeware |
| **VOICEVOX** (song mode) | invoked, local HTTP | ja | free, popular in Japan, has an API; verify its song endpoints and terms |
| **Sinsy / HTS voices** | in-process (C++ via a thin shim) or invoked | ja (en research) | BSD, no GPU, dated quality — a candidate for the "always sings something" fallback |
| **CeVIO AI** | COM (Windows) | ja, en | commercial, has an external API; later, on request |
| **A built-in guide voice** (vowel/formant synth, no model) | in-process | any (vowels) | zero setup, choir-rehearsal pitch guide; owner's call whether its sound is worth shipping |
| Synthesizer V, VOCALOID, ACE Studio | — | — | no external API; not planned |
| Python-hosted engines (NNSVS/ENUNU, DiffSinger-in-PyTorch) | — | — | not as a product dependency; a research path only |

Goal 5 ("hears a voice within minutes") is settled here: **a first-run voice**. Cantoria
ships no voice, so the extension offers to *download* one whose terms allow it, shows those
terms, and installs it into the user's voice folder. Which voice is research (licence first);
until one is found, the Setup page points at where voices are and what their terms say.

*Done when* a fresh machine with neither NEUTRINO nor a GPU can be singing within ten minutes
of installing Lily#, and the voices list shows each voice's engine and licence.

### Phase G — Beyond the preview

- `lysc wav --singer` and `lysc mp4` in CI: a GitHub Pages lesson or a repository README can
  carry a sung rendering made by the build.
- Cantoria used by others: the CLI and the extension API are enough for another notation
  extension; a MuseScore plugin or a command-line user needs nothing from Lily#. This is what
  earns the attention the owner is after — the engine is the part others can adopt.
- The learning set: a lesson on lyrics that sings (`HANDOFF-LEARNING.md`).

### Not on the roadmap (decided or deferred)

- **Training voices** in this project: not until recordings made for it exist (consent,
  rights). Public singing datasets are mostly research-only.
- **Bundling any voice or engine** with Lily# or Cantoria: never (licence, size).
- **A piano-roll or tuning editor**: not Lily#'s job; the score is the source.
- **Voice conversion** of any kind: NEUTRINO's terms forbid it and it is not the product.

## 3. Short term — the next sessions

In order; each is a session or two. The owner's items are marked.

| # | Work | Repo | Done when |
|---|---|---|---|
| 1 | **Publish Cantoria** (owner): `gh repo create yotsuda/cantoria --public --source . --push`; the VSIX to the Marketplace; `MANUAL-CHECKS.md` §3.7 ticked | cantoria | the README's links resolve; a second PC installs both extensions and sings |
| 2 | **The two leftovers**: the span's voice stops at the span's end; the loop sings (restart the buffer with the notes) | LilySharp (editor) | §3.7 items for span and loop tick; `npm test` green |
| 3 | **`engine/voice` naming and `voices()`**: the extension's API lists installed voices as `{ id: 'neutrino/MERROW', engine, language, licence? }`; `cantoria.defaultVoice` accepts and shows the prefixed form; Lily#'s status line names it | cantoria, LilySharp (editor) | the same setting shape will take a DiffSinger voice unchanged; `apiVersion` stays 1 (additive) |
| 4 | **Kana for kanji** — *done 2026-11-08, session 1138*: `漢[かん]` (owner's decision ⒜ of §4), the page unchanged, `SungVoices` sings the reading, the kana warning goes away for read syllables, the `.mid` keeps the kanji | LilySharp (Core, GRAMMAR.md) | a kanji verse sings; a kanji verse without readings still warns once |
| 5 | **`lysc wav --singer <cantoria>`**: the CLI's offline render asks a `cantoria` program (`sing`, once per voice) and mixes as the editor does — *done 2026-11-08, session 1137* (`singing-design.md` §5) | LilySharp (Cli/Core) | `lysc wav 雪やこんこ.lys --singer …` carries the voice |
| 6 | **DiffSinger spike** (Cantoria `docs/roadmap.md` B1): load one voice folder, sing one phrase on the CPU, measure; decide the vocoder and provider choices | cantoria | a WAV from a DiffSinger voice; a note of times and the licence of the voice used |
| 7 | **The demo**: a 30-second shot "type the words, press Play, hear it" for the site (`site/videos/SHOTLIST.md`; owner shoots); a `Sing` paragraph on the site and in the Lily# README | LilySharp (site) | the video is on the page; the README's feature list says *sings* |
| 8 | **Lily# release** with singing in the notes (v0.16.0 when the owner says), `OMR_MIN_VERSION` unchanged | LilySharp | the CHANGELOG's "The preview sings the lyrics" is under a version |

Then Phase B proper (the full DiffSinger backend, the platform builds), then Phase C.

## 4. Decisions wanted from the owner

Each blocks the row named; a default is proposed so work can start.

1. **Kana for kanji** (row 4). Options: ⒜ a reading in the syllable (prints the kanji, sings the
   kana — one row, one place, the 2026-11-04 lesson); ⒝ a second hidden track
   (`lyrics ja sings melody { … }`); ⒞ kana only, as now. **Decided 2026-11-08: ⒜ as `漢[かん]`**
   (`[` was free once the retired `[1. … ]` reading went; `|` is the barline), the `.mid`'s lyric
   events keep the kanji, no ruby is printed — with ⒝ not excluded later for a translation that
   is both printed and sung.
2. **A choir** (Phase E, affects nothing short term). Today every part with words sings, each in
   the same voice. **Proposed: keep**, and add a voice per part when a second voice exists.
   **Owner, 2026-11-09 (session 1163): settle the grammar and the settings before building
   several voices** — "a voice is like an instrument, so its name belongs in the .lys; but a
   score is something anyone may sing, so a voice's name in the score feels wrong too."
   **Owner's direction, 2026-11-09 (session 1166): both may be written, and the `.lys` must be
   able to say who sings** — "many people will want the music to sound as they intend". A role
   or a voice's own name goes in the file with one word; the listener's settings have the last
   say. **Built 2026-11-09, session 1168**, one voice a part still (two tracks with different
   singers, and `lysc wav --cast`, are not):
   - **One word, `singer`, with a string**, in three places, the nearest winning — the same
     order `sings` already has (a score row over its definition):

     ```
     part sop { singer "neutrino/MERROW" }            // the part's singer, by the voice's name
     part hero { singer "A" }                         // or by a role, a cast to fill in
     lyrics en sings melody singer "diffsinger/TIGER" { … }   // this track's singer
     score { lyrics verse singer "neutrino/MERROW" sings melody }  // this placement's (the owner's shape)
     ```

     ```
     PartProperty = … | 'singer' , String ;
     LyricsClause = 'sings' , PartRef | 'language' , LanguageName | 'singer' , String ;   (* any order *)
     ScoreItem    = … | 'lyrics' , [ PartRef ] , { 'sings' , PartRef | 'singer' , String } ;
     ```

     `singer` is not reserved: a part or a track may keep that name (GRAMMAR's own examples have
     `staff singer`). In the two `lyrics` forms it is the clause only when a String follows, so
     `lyrics singer { … }` stays a track named singer. Not `voice`: that word is the music's
     `voice sop { }`. An empty string is an error on the string; the compiler checks no name
     against an installation (the file is the same on every machine).
   - **A name with a `/` is a voice** (`engine/name`, as `cantoria.defaultVoice` writes it); **without
     one it is a role** (`"soprano"`, `"A"`).
   - **Superseded by the owner's decision of session 1187**: no cast. Two settings, a default female
     and a default male voice, and the part says which (docs/singing-design.md §3). What follows is
     the earlier plan.
   - **What sings** (resolved by Cantoria, so the editor and the command line agree), for the
     string written, or for `default` when nothing is written:
     1. `cantoria.voices[string]` when the settings bind it — a role, or a voice's name the
        listener replaces (`"neutrino/MERROW": "diffsinger/TIGER"`): the listener has the last say;
     2. the string itself when it names an installed voice;
     3. the default voice (today's `cantoria.defaultVoice`, the `default` entry), said once: *"soprano is
        not cast; singing with neutrino/MERROW"* or *"neutrino/MERROW is not installed; …"*. A
        missing voice never stops the music.
   - The API grows `sing(score, { singer })` (v2, additive); `lysc wav` keeps `--voice` as the
     default and gains `--cast role=voice`.
   - Then: two lyrics tracks on one part both sing when their singers differ (a translation, a
     duet on one line) and the first words win as today when they do not; SATB is four parts
     with four singers. The practice menu's rows become `♪ part (track · singer)`. A unison
     chorus (`singer "a", "b"`, one line by several voices, slightly spread in time and pitch)
     is left for later: the list form can be added to the string without a change of meaning.
   - The order of work: the grammar and its nets (nothing sounds different), Cantoria's
     `voices` and `sing(score, { singer })`, the practice menu. Session 1163 built the part that does
     not depend on it: the practice menu lists every singable lyrics of the file (one voice per
     part, the score's sung parts on by default, the others off until pressed).
3. **A built-in guide voice** (Phase F). Ship a model-free vowel synth so every install can sing
   a pitch line, or wait for a downloadable DiffSinger voice? **Proposed: wait** until the
   first-run download is tried; revisit if no voice with usable terms is found.
4. **Platform order** after Windows (Phase B): macOS first or Linux first? **Proposed: Linux**
   (CI can test it; the owner has no Mac at hand as far as the record says). *Moot since
   session 1140: both are built from Windows and CI runs the program on both; what is left is
   hearing a phrase there, which needs a machine and a voice.*
5. **Where romaji → kana lives** (Phase C): Lily# (it owns the text) or Cantoria (it owns the
   voice's needs)? **Proposed: Cantoria**, as part of the Japanese phonemiser, so every caller
   gets it.

## 5. Risks

- **Voice licences** decide the reputation of the whole. Every voice the extension can name
  shows its terms; nothing is downloaded without showing them; nothing is bundled.
- **NEUTRINO changes its layout** between versions (it did once). The command lines are
  configuration by design (`design.md` §3.1); a settings-only fix must stay possible.
- **GPU-less machines**: DiffSinger on the CPU is seconds per phrase. Rendering ahead and the
  cache (already the design) carry it; the status bar says `Voice: rendering…`, never a Play that waits.
- **Platform spread**: three runtimes and two GPU providers to test. CI renders one phrase per
  platform with a small voice, or the platform is marked *untested* in the README.
- **Scope creep into tuning**: a piano roll would eat the project. The score is the source;
  expression comes from what is written (Phase D), not from a curve editor.

## 6. Where things are

| What | Where |
|---|---|
| The design and the rules | `docs/singing-design.md` |
| The engine's roadmap (backends, contract changes, platforms) | Cantoria `docs/roadmap.md` |
| The contract and the backends | Cantoria `README.md`, `docs/design.md` |
| Licences | Cantoria `THIRD-PARTY-NOTICES.md`; `singing-design.md` §6 |
| Manual checks | `editors/vscode/MANUAL-CHECKS.md` §3.7 |
| The proposal's entry | `docs/ROADMAP.md` "Singing the lyrics" |
