# Composing in Lily# — a guide for LLMs

Read through the `lilysharp_readGuide` tool (topic `composing`) when a piece is composed in
Copilot's chat, after `docs/GRAMMAR_FOR_LLM.md` (the language itself). It says what
makes a new piece music for its players; the grammar says how each thing is written. Kept apart
from the grammar so that Ask AI, which edits a score, is not sent it (owner's decision 2026-10-29).

**"Compose a piece" / "write it for a string quartet, …"** — a new piece is a whole file, and
it should sound like the ensemble asked for.

**Answer each reply within five minutes, thinking included.** A long think can be cut off by the
connection, and the whole reply is lost — the musician waits and gets nothing.
Five minutes is roughly 30,000 tokens of thinking: think about this step only, and leave the next
step to its own reply. (Owner's trials 2026-11-08: a choir piece thought over whole before its plan was
cut off three times running, half an hour lost each time it was asked.)

**These are a composer's habits, not a form to fill** (owner's wish 2026-10-29: rules held too
tight make every piece the same). Any of them may be broken where the piece is better for it — the
plan says which and why. Compose with pleasure: write a piece you would enjoy hearing, with
choices of your own — a key, a meter, a colour, a turn the listener does not expect.

**Start in absolute octaves**: a new piece begins with `octave absolute` (and `pitch concert`) at
the top of the file, before anything else — every pitch then says where it sounds (`c'` is middle
C), and a slip stays one note instead of carrying every note after it away. Inside a chord too each
note says its own octave, not its distance from the note before: F♯1 F♯2 A♯2 is `<fis,, fis, ais,>`
(`<fis,, fis, ais>` would put the A♯ an octave higher, out of the hand's reach). Without that line every
pitch is read relative to the one before it, and pitches written as absolute fly off by octaves
(owner's report 2026-10-29: a piece composed in Copilot's chat did just that).

**Plan before the first note.** Write the plan in your reply before you propose the piece, every
heading below filled in and in this order — a heading left vague is a decision not made. The
musician may read it; it is your working notes, short lines, not prose. The headings say what to
decide; how, the rest of this guide says. (Owner's trials 2026-10-29: a piece planned only in words
padded itself out to 32 bars, copied its "A′" from A, never wrote its theme into a bar, had phrases
that did not answer and no high point, and planned its coda quiet and wrote it loud; the plan below
was what mended them, and it went missing from the chat's composing until 2026-11-07.)
The plan is the piece's shape, a page — not the piece: do not compose its sections in your head
before you write it. Each section's detail is decided in its own step, its section notes (below).
(Owner's trials 2026-11-08: asked to plan every bar's chord, every phrase's notes and every line
of the words up front, a choir piece was composed whole in thought before its plan — ten minutes
of thinking, a reply all but cut off.)
- **LENGTH AND FORM** — the sections and their bars (about 24 to 32 bars unless the request gives a
  length, or what the piece needs); a section that comes back as it was is named again in the form
  (`form { A B A Coda }`), never copied.
- **KEY, METER, TEMPO** — and a key change, if any: its bar, the chord that prepares it, the way home.
- **HARMONY** — each section's progression in one line and its cadence (half, full, deceptive); where it is coloured (a
  secondary dominant, a borrowed chord, a deceptive cadence) — or that the style keeps it plain. The bars
  of each chord are the section notes'.
- **THEME** — its one or two bars WRITTEN OUT in this file's notes, its rhythm with them
  (`fis''2 e''8 d''8 | d''4 b'4 g''4 |`), and the bars where it comes back.
  In a piece whose parts imitate: each entry's bar, part and key, and the countersubject written out.
- **PHRASES** — how the phrases pair (which asks, which answers, as *Phrases that answer* says) and
  the range the melody climbs to in each section.
- **CLIMAX** — its bar and note, and how the dynamics lead there and away.
- **PARTS** — what each part does in each section (melody, bass line, inner parts, the accompaniment
  figure and its rhythm) and its register, as sounding pitches (C4 = middle C) AND as you will write
  them (a right hand round C5, D4–A5, is written `d'` – `a''`); a countermelody, if any, and the
  bar of its own peak.
- **DYNAMICS** — the level each section starts at and where it swells or falls.
- **CHARACTER** — each device of the character asked for, how it is written and the bars it is in
  (`@sf` in bar 13, a tremolo `d'2:32` in bars 14–15, …).
- **WORDS** (a song only) — what each section's words say, in a line, and the chorus's first line
  written out WHOLE: the hook the song is remembered by.
- **ENDING** — in the home key, on a held tonic chord, at the dynamic planned for it.

**Section notes, then the section.** Write the piece a section at a time, each in its own step. Just
before you write a section's music, put its section notes in your reply, short lines: its chords bar by
bar and its cadence; for each phrase its bars, the note it starts on, its highest note and the note
it ends on over its cadence; its dynamics phrase by phrase; and in a song each line of its words
written out WHOLE first, its sung units counted, then the phrase that sings it, given that many notes
(*A song with words*). Then write that section to them, and think of the next section only in its own
step.

Write the piece to the plan: the theme in the bars the plan names, the climax where it says,
the dynamics as planned. Where the piece turns out better another way, change the plan, not only the
notes.

What makes it music:
- **A title of its own**: `title "…"` at the top, words that fit its music and mood — never a
  placeholder (`New Piece`, `Untitled`). **No `composer` line** unless the request asks for one
  (a piece composed in Copilot's chat signed itself `composer "Lily#"`).
- **Instruments**: every part gets its `instrument` (`violin`, `viola`, `cello`, `flute`, …) —
  without one it plays a piano. The octaves are LilyPond's whatever the instrument (`c'` is
  middle C, so a violin's open A is `a'`, a cello's open C is `c,`). Name the parts as a score prints them,
  by the instrument (`part vn1 "Violin I" "Vn. I"`). A piece for piano alone names no part (its
  two staves need no label); a piano in an ensemble is named once, on its upper part (`part rh
  "Piano" "Pno."`), the lower part unnamed. A staff is never labelled "Right hand", "L.H.", 右手
  or 左手.
- **Layout**: an ensemble is a `staffGroup { … }` (a bracket); `grandStaff` is one instrument
  on two staves (piano, harp) — never four players.
- **A form, at the length it needs**: unless the request gives a length, about 24 to 32 bars
  of 4/4 suits a piece (a piano piece then fits one printed page), a minute and a half to three
  minutes played — a shorter bar or a quicker beat takes more bars (6/8 at ♩. = 60 is two seconds
  a bar: 26 bars, under a minute) — never a slower tempo than the character asks for, to fill the
  time: an A–B–A with a short coda (2 to 4 bars), or
  a song's form — complete and shapely rather than long, never a bar added to reach a length. A
  song counts its WRITTEN bars: a verse or a chorus sung again is named again in the form (its new
  words as verses, `verse 1 { … } verse 2 { … }`), not written twice. Phrases often run 8 bars — two halves of
  4 that answer each other — and a section is given room to say something: one of a single 4-bar
  phrase seldom does, and its dynamics turn every four bars. Each phrase closes on a cadence: a
  half cadence (on V) to go on, an authentic one (V–I) to close a section. Check each one bar by
  bar: the chord just before a closing I is V (or V7), with IV or ii before it — never V, then IV,
  then I, which goes back before it arrives (a blues's or a rock song's V–IV–I is their own).
- **Phrases that answer**: the second phrase of a pair starts as the first did and ends otherwise
  — the first stops open (on V, or on the chord that leads back: a jazz turnaround, a rock IV or
  ♭VII), the second closes on I. The first half's last melody note is NOT the tonic: a tune
  that lands on its home note at bar 4 has ended, and its second half only ends again. Each
  phrase rises to a high note and falls to its cadence — by steps: a leap wider than a fifth is
  followed by a step back the other way, above all from the high note, which a ninth's drop
  (`d'''2 | c''4`) breaks off instead of leaving. A middle section that leads back to the
  first (the B of A–B–A, a bridge) ends on the home key's V, so the return is heard arriving —
  not closed in its own key (vi, a relative minor) with the return simply starting after it.
- **One climax**: the piece rises to one high point — a bar and a note, the loudest place — and the
  dynamics lead there and away. In a song it is the chorus's high note, strongest its last time.
- **Harmony first**: each phrase has its chord progression in the key, and every part is
  written from it — the bass on the chords' roots and inversions, the inner parts on their
  tones, the melody's strong beats on chord tones and its passing notes between them. A chord
  sounds its third — the accompaniment has it, or the melody does on the beat: a left hand of bare
  fifths (`b, fis`) is hollow and leaves the chord unsaid (major or minor? V of the relative
  minor needs its raised third, `ais` under B minor), unless a drone or a power chord is meant.
- **Harmony that moves**: past the first phrase, colour the progression where the music asks — a
  secondary dominant leading into the next chord (V/V, V/vi), a chord borrowed from the minor (iv,
  ♭VI in a major key), an inversion that lets the bass walk by step, a deceptive cadence (V–vi)
  where a phrase would close too soon — and let the chords change faster toward a cadence. **The
  chord changes with the note**: a melody's note from outside the key belongs to the accompaniment's
  chord as well, written into it (a flute's A♮ for V/vi in D♭ major is an F major chord in the
  piano, not F minor's A♭ under it — owner's trial 2026-11-07: the two clashed for a beat). A pop
  song's loop of four chords, a blues's twelve bars and a hymn's plain triads are their own idiom:
  there the colour is in the melody, the voicing and the rhythm, not in new chords.
- **A theme**: a motif of a bar or two that the piece is made of — a melody's motif, a song's hook,
  a band's riff, a jazz head's opening figure — with a rhythm of its own, not even quarters —
  stated, repeated, sequenced, varied, and back near the end. Decide it in notes
  before writing the piece: a theme that is only words ("a rising broken chord") is not one yet.
- **The tune grows from its motif**: every phrase of the melody is made of the theme's motif —
  repeated, sequenced a step higher or lower, inverted, its rhythm kept under new notes, lengthened
  to a cadence — so the ear half-knows what comes next and is pleased when it comes, and once or
  twice surprised. One feature makes it remembered: a leap the tune keeps, a rhythm, a note leaned
  on against its chord (an appoggiatura on the strong beat, resolving a step down). A tune of notes
  that only follow the chords is not yet a theme. **The motif's shape is kept, its rhythm is not
  frozen**: its own rhythm in one bar of two at most, longer notes at a phrase's peak and end, a
  phrase that runs on over the bar line, a section that moves in other values (owner's trials
  2026-11-07: a waltz and a rain piece, two models, poured the motif's one-bar rhythm into half
  their bars or more, and the tune became a pattern). (Owner's goals 2026-11-07, with the five that
  follow: a melody that is remembered, chords that are more than a stock pattern, a countermelody
  of its own, a key change with a story, an orchestra's instruments written as they sound, and one
  piece from its first bar to its last.)
- **A section that returns is named again, not copied**: one that comes back as it was is written
  once and played twice by the form (`form { A B A Coda }`) — never a second section with the same
  notes. A return that changes (the tune an octave higher, a new accompaniment, another ending) is a
  section of its own (`A2`), and the plan says what changes; the bars that do not change are
  written as they were. Going round the form does not make a piece longer: a section plays twice at most.
- **A variation is of its theme**: it keeps the theme's chords or the outline of its tune, so the
  theme is heard in it, and changes its rhythm, its figure or its register. A new tune over new chords is
  a new section, and the theme's rhythm kept under new notes is not yet a variation. Each variation
  changes more than the rhythm: the accompaniment's figure, the register, or the tune's own shape
  (ornamented, inverted, in the other hand), and no two take the same accompaniment.
- **One home key, one tempo**: a section may move to a related key (`key g major` at its head —
  the relative, the dominant) and the piece comes home to end. A section in the RELATIVE key (B
  minor in a D major piece, A minor in C) keeps the signature and writes no `key` line: the same
  sharps or flats would only be printed again. The tempo stays unless the
  character changes with it (a slow middle section, `tempo "Adagio" 4 = 60`), never a new tempo
  for every section. **A key change is an event**: it comes where the piece turns — the middle
  section, the last chorus up a step — prepared by a chord the two keys share or by the new key's V,
  and the way home is heard arriving (the home key's V before the return). A development takes the
  theme apart — a fragment of it sequenced through the keys — rather than bringing new tunes.
- **The contrast is made of the theme too**: a middle section takes the theme's motif inverted,
  augmented or in the minor, rather than a tune from nowhere, and a coda recalls the opening's first
  bars. The accompaniment's figures, the character's devices and the plan of the dynamics run through
  the piece, so it is one piece from its first bar to its last.
- **The end**: the last phrase closes in the home key — V (or V7) to I, the tonic chord held
  through the final bar in every part, the bass on the tonic.
- **Registers**: every part where its player plays (see *Instrument ranges* — a piano's left hand
  round C3, its right hand round C5).
- **A band** (rock, pop, funk, metal, … — any request naming an electric guitar, an electric
  bass, drums or band keyboards): the whole rhythm section plays, whichever members were named —
  **drums** (a `clef percussion` part: a groove that changes with the section — closed hi-hat in
  the verse, the ride or crashes and open hi-hat in the chorus, half-time or toms in a quiet bridge —
  fills at the section ends, and levels of its own that follow the song's), an **electric
  bass** (`instrument bass` with `midiInstrument "electric bass (finger)"`, locked to the
  kick drum) and a **guitar or keyboards** for the chords (`instrument guitar` with
  `midiInstrument "overdriven guitar"` or `"electric guitar (clean)"`; keyboards on
  `instrument piano-right` with `"rock organ"`, `"electric piano 1"` …). A distorted sound
  (overdriven, distortion) blurs a full chord: on it strike two or three notes — the root and its
  fifth, the octave above (`<e, b,>`, `<e, b, e>`), low strings included. Full chords are for a clean
  sound, and there on the upper strings, three or four notes from the fourth string up (`<f a c' f'>`;
  `lilysharp_chordVoicings` with `strings: "1-4"` lists them): the bass has the low ones. "Guitar and drums"
  has a bass too; "guitar and bass" has drums too; keyboards are welcome. **One tempo** from start
  to end — a single `tempo` at the top, none in the sections. The form is the song's: intro,
  verse, chorus, bridge, outro; separate staves, no bracket.
- **One staff only when it is asked for**: a lead sheet, a melody alone ("a melody for these
  words"), or a solo for one instrument that plays one line ("a flute solo", "unaccompanied
  cello"). Anything else has its accompaniment — its own parts, on staves of their own.
- **Jazz** not asked for as a lead sheet is a **combo**: a rhythm section — piano or guitar for the
  chords, a bass (`instrument bass` with `midiInstrument "acoustic bass"`), drums swinging on the
  ride cymbal (`cymr`) and hi-hat — under a melody instrument (`instrument tenor-sax`, `trumpet`,
  `alto-sax`, …), with the harmony and the feel of a jazz lead sheet (next), at one tempo — or a
  piano trio, the piano's right hand the melody. How the rhythm section plays:
  - **Drums**: the ride's swing figure, `cymr4 cymr8 cymr cymr4 cymr8 cymr`, the hi-hat pedal on
    2 and 4 (`hhp`), the bass drum light or silent, the snare an accent now and then — never a
    backbeat of bass drum on 1 and 3 and snare on 2 and 4 (that is rock); a fill into a new chorus.
  - **Bass**: walking quarter notes, the chord's root on beat 1, and the note on beat 4 leading a
    step or a half step into the next root (`f, a, c e | f,`) — not the chord's notes in a row.
  - **Piano comping**: rootless voicings — the 3rd and 7th with a 9th or a 13th (the bass has the
    root) — short and off the beat, anticipating the next chord, around middle C; not on every beat.
  - **A blues head**: a riff stated, stated again over the IV, answered in its last four bars
    (AAB), with the blue notes (the ♭3, ♭5, ♭7) bent against the chord — and no 7th of the scale
    held against the dominant 7th (an E♮ sustained over F7).
- **A jazz lead sheet** (asked for as "a lead sheet", "a tune to jam on"): ONE staff — the
  melody, no instrument needed (a lead sheet is concert pitch) — with a `chords NAME` row
  directly above it, and nothing else. The harmony is jazz's: seventh chords throughout
  (`Fmaj7 Gm7 C7 Am7-5`), ii–V–I in the key and ii–Vs into other chords (`Am7 D7` → `Gm7`), a
  ♭9 or 13 where it colours (`D7(b9)`, `C13`), the backdoor ii–V (`Bbm7 Eb7` → `Fmaj7`), a
  turnaround at the end of a chorus (`Fmaj7 D7 | Gm7 C7`). The feel: `tempo "Medium Swing" 4 = 152
  swing`. The melody: eighth-note lines with rests, anticipations tied over the bar
  (`e'8~ | e'4`), chord tones on the strong beats and the colour tones (9ths, 13ths, the ♭9) for
  tension. The head in sections of 8 bars (AABA, ABAC — 32 bars, or a 16-bar or 12-bar blues). The
  sheet is written `score { chords NAME  staff lead }`, the row above its staff.
- **A solo instrument with piano** ("a violin piece", "for flute and piano", a sax ballad): the
  solo sings the melody on its own staff, above the piano's `grandStaff` (`score { staff vn
  grandStaff { staff rh  staff lh } }`), and the piano accompanies — it does not double the tune
  bar after bar; it takes the line where the solo rests. The solo lies where it sings: a violin
  round D5–A5 (its low G string for warmth, the E string for brilliance), a flute round A4–A6, a
  cello round C3–A4. Slurs are a string player's bowings — a slur of a few notes, not a whole
  phrase; a wind player breathes at the rests between phrases; a double stop on a violin now and
  then, never a run of them. The piano's chords lie BELOW the solo, not in its register and never
  over it; a dance's figure is one pair, not two — a waltz's bass on beat 1 and its chords on 2
  and 3 round middle C, the two hands sharing that one figure, not each playing it — and the
  right hand's own line comes where the solo holds or rests, an answer to it.
- **A pop song**: intro, verse, pre-chorus, chorus (bridge before the last chorus) — the verse low
  and narrow, the chorus higher and wider, its hook the song's title words on its highest, most
  repeated phrase. A chord loop of four (I–V–vi–IV, vi–IV–I–V) that changes between verse and
  chorus. A syncopated melody — anticipations tied over the beat, eighths and sixteenths — over a
  backbeat (drums with the snare on 2 and 4) and bass, keys or guitar, or a piano alone. **At least
  half the bars of the sung line have a syncopation**: a note that starts off the beat and is held
  across the next one (`r8 g'8 b'8 d''8~ d''4 b'4 |` — the D comes an eighth early and rings over
  beat 3), or a phrase's last note sung an eighth before the bar line and tied into the next bar
  (`… a'8 g'8~ | g'2 r2 |`). A line of plain quarter notes is a nursery song, not pop.
- **Rock**: a riff the song is built on — power chords (`<e, b,>`) or a single-note figure on the
  guitar — doubled by the bass, the drums driving with the snare on 2 and 4; the bass drives eighth
  notes on the chord's root with the kick (an octave or a fifth now and then, a run at a fill), never
  leaping more than an octave from note to note; a guitar solo is a line with a shape of its own —
  long notes held and bent (`@bend`), a motif repeated higher, a run climbing to its peak — not the
  chord's tones broken in eighths bar after bar; any key (a power chord is one shape on every
  fret); an instrumental break (a guitar solo over the verse's chords) before the last chorus.
- **Gospel**: a piano (or an organ: `midiInstrument "drawbar organ"`) under a solo voice, a choir
  (`choirStaff`, soprano–alto–tenor–bass in close harmony), or both in call and response — the
  soloist sings a line, the choir answers it. Rich chords (added 9ths and 7ths, a dominant with a
  sus4 before it resolves, passing diminished chords, the bass walking up into the next chord), a
  slow 12/8 or a swinging fast 4/4, and the IV–I "amen" at the end.
- **A style named is its rhythm first**: its chords alone do not make it, and a request for one is
  answered in the figure the style is known by, written into every section. A tango's bass is
  `4. 8 4 4`; reggae's chords stand on the off-beats, its bass answering them. **A bossa nova**: ♩ =
  120 to 140, major sevenths and minor sevenths, and two bars that go round. The rhythm is the bass's
  (and the drums', straight eighths, no swing): root, rest, fifth, then root, rest, root
  (`e,4 r4 b,2 | e,4 r4 e,2 |`) — a tune from a four-beat standard takes `e,4 r8 e,8 b,4. b,8 |`
  instead. The piano or guitar fills the gaps round the melody, long chords and a few off-beat ones,
  not one figure from start to end; here and there it takes the batida, eighths counted the chords on
  1, 4, 7 of the first bar and 3, 6 of the second (`<…>4. <…>4. <…>4 | r4 <…>4 r8 <…>4. |`). The melody
  comes in off the beat and is held over the bar line, above the chords. Over a chords row (a song to
  sing and play, a lead sheet) the batida is a `strokes` part the row plays, `%2` repeating the two bars
  before it — `strokes bossa { section A { 4. 4. 4 | r4 4 r8 4. | %2 | %2 | %2 | } }`, as many bars as
  the section, and `chords harmony plays bossa` in the score. **A guitar alone** plays all three at once,
  in three voices on one part: the tune above the chords, a voice of its own that comes in between the
  strokes and is held over the bar line, not one note squeezed into each gap of the chords; the fingers
  the batida on three neighbouring strings (Am7 `<g c' e'>`, D7(9) `<fis c' e'>`, written here with
  `octave absolute`); the thumb on beats 1 and 3, the root, or the fifth. One hand plays them: four notes
  struck at once at most, the chord two notes where the tune strikes with it, and the hand within a few
  frets from chord to chord, since a chord that no hand can hold is counted with the notes of the other
  voices sounding with it. A dynamic goes in one voice only, or the page prints it once for each:
  ```
  part gtr { instrument guitar midiInstrument "acoustic guitar (nylon)" }
  section A { gtr { voice {
    r4 c''4 b'8 a'4.~ | a'2 fis'2 |
  } {
    <g c' e'>4. <g c' e'>4. <g c' e'>4 | r4 <fis c' e'>4 r8 <fis c' e'>4. |
  } {
    a,2 a,2 | d2 d2 |
  } } }
  ```
- **A piece for a beginner** ("for a beginner", "an easy exercise", "for a child learning the
  piano"): the hand stays in one place — five notes under five fingers (C to G for the right
  hand from middle C), moving by step with a few skips of a third, no leap out of the five, no
  sharp or flat the key does not have; quarter and half notes, a whole note at the end. Fingering
  on the first note and wherever the hand must think (`c'4@finger(1)`); a slur over each phrase
  and a dynamic at its start, so the piece teaches them too. The last note is the tonic, reached
  by step (`d'4 c'1`, or `b4 c'1` — the B below middle C, not `b'` an octave above).
- **A hymn or chorale for choir** (SATB, `choirStaff`): the four voices move together, mostly in
  quarter notes, a longer note at each phrase's end, where the harmony makes a cadence; a fermata
  on the last chord. Each voice in its own range — soprano C4–G5, alto G3–D5, tenor C3–G4, bass
  E2–C4 — the alto ABOVE the tenor, never crossing it, the upper three within an octave of each
  other. Each inner voice is a line of its own, by step where it can, holding a common tone from
  chord to chord; the bass carries the harmony with steps and leaps of its own (to the root at a
  cadence), not rocking between a chord's root and fifth, and sings the words with the others, not
  one long note a bar under them.
- **A chorus for a school choir** (a contest or class piece; 合唱コンクール, "for our class to sing"):
  a piano part under the voices (`instrument piano-right` / `piano-left` in a `grandStaff`, below the
  `choirStaff`) carries the introduction and the interludes, and the voices sing words whenever they
  sing. Younger voices sit in the middle of the staff: soprano about C4–E5, alto A3–C5, a mixed
  choir's men B♭2–D4 (`instrument baritone`, below the alto, not a tenor above it), whatever the
  preset allows. Every part is a line a singer can
  learn by ear, by step where it can, sharing a common tone from chord to chord — the alto and the
  men above all — and two parts do not double at the octave unless the words ask for that strength.
  About three minutes, two verses and a chorus that returns.
- **A song with words** (a voice part — `instrument soprano`, `alto`, `tenor`, `baritone` or `voice-bass`, the
  vocal line of a pop song too — and its lyrics row under it, see *Lyrics*):
  - **One sung unit a note, each written apart**: the unit is the language's own beat of speech —
    a syllable in most languages (English `Hap- py`, a word's syllables joined by `-`), a mora in
    Japanese (`き ら き ら`: a small kana goes with the one before it, `きゃ`; `っ`, `ん` and `ー`
    are units of their own), a character in Chinese. Lily# splits nothing: text written together
    is ONE unit on one note, with no warning. A melisma (one unit over several notes) is a slur,
    and is rare.
  - **A Japanese particle is sung as it is said, and a voice reads the letter**: the particles は and
    へ are わ and え, so each takes its reading glued to it, `ぼ く は[わ] ず ん だ`, `う み へ[え]`. Left
    bare they are sung ha and he. The page still prints は. Only the particle: the は of `は じ ま る`
    and the へ of `へ や` stay as they are. In the same way a long お written う takes its reading:
    `あ り が と う[お]`, `じ こ しょ う[お] か い`. Not where the う is a sound of its own, as in `お も う`.
  - **The melody follows the words' own shape**: their stressed syllables on the strong beats and
    the weak ones off them (English, German, Spanish …) — a line that opens on a weak syllable (*O GOD of
    LIGHT*) opens on an upbeat, with `partial`, so that GOD and LIGHT fall on the beats, and a long note
    goes to a stressed word, not to the small word after it; the rise and fall of a pitch accent or a
    tone where the language has one (Japanese 雨 falls from its first mora — a line that rises
    there sings 飴; a Chinese word's tone is not sung against). A long or high note on an open
    vowel (a, o, e), not on i or u or a closed syllable, and on a word that carries the meaning,
    not on a little word that only joins others. A long note is its chord's own tone, or resolves
    to one by step before the chord changes.
  - A line of words is a phrase of music: the line ends where the phrase does, on a longer note, a
    rest after it to breathe; a comma in the words is a breath in the line.
  - **Write the words first, whole, then set them**: each line as the sentence it is, its units
    counted, and its phrase given that many notes — the notes fit the words, never the words cut
    to fit the notes. Every word is sung whole: a line whose notes run out inside a word
    (`to- mor-` for "tomorrow", `も や` for 燃やす) gets another note or other words. A long note or
    a phrase's last note ends a word; a bar line may fall inside a word only where the phrase runs
    on through it.
  - **Then the chorus's hook, before any other note of the tune**: two bars on the chorus's first
    line, with the song's highest note in them and a rhythm of its own, that can be hummed after one
    hearing. Then the verse — lower, in another rhythm, ending open — and the rest around them. A
    chorus that opens with the verse's phrase, or stays below the verse, is no chorus; a
    tune whose every two-bar phrase has one rhythm (`8 8 4 2 | 4 2 r4` thirteen times) is a formula,
    not a tune.
  - **The chorus comes back after every verse**: verse 1, chorus, verse 2, chorus — the form names
    the chorus again each time (`form { Intro A B A B Coda }`, A's two verses as `verse 1` and
    `verse 2`), and its last time is the strongest: louder, an octave fuller in the piano, or its
    last line sung twice (a section of its own, `B2`, when its notes change). A song does not end on
    a verse, nor on its hook said once in a coda: the whole chorus is the last thing sung before the
    coda (owner's trial 2026-11-07: a spring song sang its chorus once, between its two verses, and
    went from the second verse to a coda that hummed its hook).
  - Count every bar: as many units as notes, less the notes a slur or a tie carries over.
  - Words the request gives are set as they stand. Words written for it tell their story in the
    verses and come back unchanged in the chorus, its hook the title.
  - **Who sings**: a top-level `singer` line sings the words: `singer on sop sings words`. A `lyrics`
    row only prints them, and words with no `singer` line are not sung: the part plays its instrument.
    A song meant to be heard sung has the line, for every part with words to sing. With no voice named
    this machine's default voice of the part's gender sings, and the name is left out unless the
    request names a voice or a kind of voice, or several parts should sound different.
    To name a voice, call `lilysharp_listVoices` first and copy a name it answers; never guess one.
    Called with nothing it lists every voice; narrow it with `language` (the words': `japanese` or
    `english`, `ja` or `en`), `gender` (`male` or `female`; a voice that does not state one is kept and
    marked so) or `provider` (`neutrino`, `diffsinger` or `voicevox`). Each line is one voice:
    the name as a score writes it (`engine/name`) before the colon, then what it calls itself, its
    gender and the languages it sings. Take a voice that sings the words' language. If it answers
    that no voice can be listed or none is available, name none: `singer sings words`.
    The line stands at the top level, and a score's `lyrics` row is not needed for it:
    ```
    part sop { instrument soprano }
    lyrics words { … }
    singer "neutrino/MERROW" on sop sings words
    score { staff sop  lyrics words }
    ```
    A choir's voices each take their own line, one a part. Two lines with the same words on one part
    sing it together. A line inside a score replaces the top level's for its part there, which is how
    one score sings Japanese words and another English ones. `singer male sings
    words` or `singer female sings words` asks for that gender's default voice and needs no list.
    `singer male "neutrino/SOMA" sings words` is a voice with its gender said, the gender narrowing
    what the editor offers. `staff sop sounds both` plays the instrument with the voice;
    `staff sop hidden` sings without drawing the staff.
    Where parts have different words, name each part's in its header, `part ten { instrument
    baritone  lyrics mw }`, and `singer male on ten` sings them: a row or a line that gives
    that part other words is then warned about.
- **A song to sing and play yourself** (弾き語り; "a song with chords and lyrics", "for guitar and
  voice"): a lead sheet with the guitar's chord diagrams, not a written guitar staff.
  - **The voice**: `instrument alto` (F3–F5) for a woman's song and when the request names no
    voice; `instrument baritone` (G2–G4) for a man's — the singer is the player, not a trained
    voice, and those are the ranges such a voice has; Lily# holds every note to the range
    (LYS1052). A voice the request names (a tenor, a soprano) is written as named. Choose the key
    so the tune lies in the range and the chords are the open shapes (C, D, E, G, A, Am, Dm, Em,
    with a `capo N` line for another key).
  - **The score**: `score { chords NAME plays rhythm  staff singer  lyrics words }` — the chords
    row above the singer's staff, the words under it, and nothing else. No guitar staff: the row
    IS the guitar part. Write one only when a figure is asked for (a picking pattern, a riff),
    and then that figure, never the row's chords struck again as notes.
  - **Eight bars a section**: the verse and the chorus are two lines of words or more — eight bars,
    two phrases that answer each other — never one four-bar phrase; the second verse sings new
    words. A song of 24 bars is over in a minute: give it the minutes a song has.
  - **The chords row in every section** (`chords harmony { D | A | Bm G | }`), one bar a chord,
    two chords in a bar written together (`Bm G`).
  - **The strum, always**: a `strokes` part — the rhythm the chords are struck in, values alone
    and no pitch — `strokes rhythm { section A { 4 8 8 4 4 | % | % | % | } }`, one pattern a
    section (`%` repeats the bar before), and the row plays it (`chords harmony plays rhythm`):
    the row then sounds its chords at every stroke. Without it a row holds each chord for a
    whole bar, which is not how a guitar is played. `plays` draws nothing; when the request asks
    for the strum on the page, add `strokes rhythm` as the score's last item (a one-line staff
    of slashes) and move the row above it.
  - **The verses**: one lyrics track, `verse 1 { … } verse 2 { … }` inside it (see *Lyrics*), and
    the form sings the section again — `|: Verse :|` stacks the verses under it, `Verse Verse`
    prints each at its own time, numbered. Not a track a verse, never `[1. … ]`.
  - **The bridge is a change**: a chord the verse and the chorus do not use (Am, Bm, D7, a borrowed
    chord), a tune of its own, or the strum's pattern changed there — not the same four chords
    walked from another one of them under the verse's tune again.
  - **Who sings**: as *A song with words* says: `singer "…" on melody sings words`, a name
    `lilysharp_listVoices` gave. A part with no words to sing can hum (`singer "…" sings hum on
    harmony`) or sing the note names (`sings solfege on harmony`), with no lyrics block.
  - **The singer breathes**: every line of words ends on a longer note with a rest after it (a
    bar's last beat, at least), never four quarters in every bar from the first word to the last.
  - **The diagrams, always**: `layout { chordDiagrams all  chordList true }` — every chord draws
    its diagram where it changes and at each line's head, and the chords the song uses are listed
    with their diagrams under the title. The usual shapes draw by themselves (C `x32010`, G
    `320003`, D `xx0232`, Am `x02210` …); a chord with no usual shape takes one written at its
    name (`Bm(x24432)`, `F(133211)`), and a capo's shapes are the pressed ones.
- **A piano piece** is written for two hands that fill the keyboard between them: the left
  hand's figure — broken chords, an octave bass, a root-and-fifth beat, an ostinato — reaching
  up toward middle C, the right hand's melody round C5 with its harmony under it (thirds, sixths,
  chords on the strong beats), never an octave of empty keyboard between the hands for long. A
  left hand may keep one note value through a section: its figure is the rhythm; the melody is
  where the rhythm varies. A contrasting section takes a figure of its own. A broken-chord figure
  stays within an octave and a half (a twelfth), climbing from its bass by the chord's tones and
  turning back — not leaping two octaves from a low bass to the treble. A chord one hand strikes
  lies within an octave, a ninth at most: a wider sound is shared between the hands, or its bass
  struck first and the chord after it. Broken chords and a slow
  or quiet tune are held in the pedal, or each note stops where it is written: `@sustain` on the
  harmony's first bass note, the same mark again at each change of harmony, `@!sustain` on the
  last note (`d,4@sustain a, d a, | g,4@sustain d g d | a,1@!sustain |`).
- **Texture**: one voice carries the melody while the others accompany — broken chords,
  repeated eighths, a moving bass — and the roles pass between the parts. Not every part in the
  same rhythm; not whole notes everywhere. **A rest in one part is covered by another**: the
  accompanying parts do not stop together bar after bar (three lower strings all resting on beat
  4 of every bar leave the tune alone there, and the music hiccups) — while one breathes, another
  holds a note or moves on; they rest together only at a phrase's end, or for an effect.
- **A countermelody is a second tune**, not a harmony part — an accompaniment's top line, an inner
  string part, a piano's right hand under a solo when it is not striking chords: it moves when the
  melody holds and holds when it moves, goes against it (contrary or oblique motion more than
  parallel), peaks in another bar, and could be sung alone. Thirds or sixths under the tune for a
  phrase are a colour; for a whole section they are a doubling. Between the melody and the bass, and
  the melody and a countermelody, no parallel fifths or octaves unless a doubling is meant. An
  accompaniment of one figure from the first bar to the last (a bass on 1, chords on 2 and 3) leaves
  the tune alone: let a line answer it where it holds or rests.
- **A fugue, a canon, any piece whose parts imitate** keeps every part a line of its own to the
  last bar. In a fugue, a subject that opens tonic to dominant is answered dominant to tonic, and
  the countersubject comes back with the later entries. An entry stays in one key, or changes at a
  bar the plan names. The climax is a higher register, a stretto, a louder level, never one part
  doubling another, whatever the character asks: a doubled part is a voice lost. Read every pair
  of parts, the inner ones too and across each bar line: no parallel fifths or octaves, and no
  strong beat with all the parts on one note before a cadence.
- **An orchestra or a string ensemble** writes each instrument where it sounds well, not only where
  it can play: a flute's low octave is weak (its tune lies above G5), an oboe carries a tune in its
  middle, a horn sings round C4–C5 and rests between phrases, a cello's tenor register sings. Strings
  play `@text("pizz.").up` and back `@text("arco").up` (the preview still plays them bowed: the
  marks are for the players), a tremolo `c'2:32` for tension, sustained notes under a moving tune.
  Layers: one tune, one countermelody, a harmony held, a bass — doubled at the octave for weight at
  the climax, thinned to a few players for a quiet passage. Winds and brass breathe: no part plays
  every bar. The key suits the players: a clarinet in B♭ reads two sharps more than it sounds, a horn
  in F one more, so winds and brass live in the flat keys (F, B♭, E♭) and their sharp neighbours
  (C, G, D) — no part's written key past four or five sharps or flats.
- **Rhythm and articulation**: long and short notes mixed, rests that let a line breathe,
  slurs over a phrase, `@staccato`, `@accent`, and dynamics with hairpins
  (`@p … @cresc … @f`) that shape each section — every mark for a reason, the same level never
  written twice in a row, the ending at the level it was planned. The rhythm keeps the meter's
  beats: in 6/8 (9/8, 12/8) a beat is three eighths, the melody grouped `4.`, `4 8`, `8 8 8` —
  `8 8 4 4`, a quarter across the beat, is 3/4's hemiola, written only where it is meant.
- **The character asked for** is heard through devices, each written into the piece where it
  acts — not only planned: "dramatic" — `@sf` and `@p.subito` after a loud bar, a tremolo
  (`d'2:32`), every part in unison at the climax, the low register; "cool" — a driving rhythm,
  syncopation and `@accent`, octaves on the strong beats, a run into the climax; "lovely" — a
  singing line with long notes at its peaks, a gentle flowing accompaniment. **The devices run
  through the piece**, not spent in one place: each comes back in more than one section (a
  quartet's tremolo and `@sf` only in two bars of its middle section left its opening merely
  loud and soft), growing toward the climax, where they are strongest and come together.

**The first step, once this guide is read**: give the piece its title and call `lilysharp_checkName`
for its file — nothing more in that reply, and no music thought out yet. Its answer says the next
step, and each answer after it says the one after that: the plan, then one section at a time. (Owner's
trials 2026-11-08: a model that read the whole way at once composed the whole piece in thought before
writing a word, and its reply was cut off three times over.)

