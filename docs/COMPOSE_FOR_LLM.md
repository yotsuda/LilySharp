# Composing in Lily# — a guide for LLMs

Sent with Compose with AI, after `docs/GRAMMAR_FOR_LLM.md` (the language itself). It says what
makes a new piece music for its players; the grammar says how each thing is written. Kept apart
from the grammar so that Ask AI, which edits a score, is not sent it (owner's decision 2026-10-29).

**"Compose a piece" / "write it for a string quartet, …"** — a new piece is a whole file, and
it should sound like the ensemble asked for.

**These are a composer's habits, not a form to fill** (owner's wish 2026-10-29: rules held too
tight make every piece the same). Any of them may be broken where the piece is better for it — the
plan says which and why. Compose with pleasure: write a piece you would enjoy hearing, with
choices of your own — a key, a meter, a colour, a turn the listener does not expect.

**Start in absolute octaves**: a new piece begins with `octave absolute` (and `pitch concert`) at
the top of the file, before anything else — every pitch then says where it sounds (`c'` is middle
C), and a slip stays one note instead of carrying every note after it away. Without that line every
pitch is read relative to the one before it, and pitches written as absolute fly off by octaves
(owner's report 2026-10-29: a piece composed in Copilot's chat did just that).

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
  a bar: 26 bars, under a minute): an A–B–A with a short coda (2 to 4 bars), or
  a song's form — complete and shapely rather than long, never a bar added to reach a length. A
  song counts its WRITTEN bars: a verse or a chorus sung again is named again in the form (its new
  words as verses, `[1. …] [2. …]`), not written twice. Phrases often run 8 bars — two halves of
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
- **A theme**: a motif of a bar or two that the piece is made of — a melody's motif, a song's hook,
  a band's riff, a jazz head's opening figure — with a rhythm of its own, not even quarters —
  stated, repeated, sequenced, varied, and back near the end. Decide it in notes
  before writing the piece: a theme that is only words ("a rising broken chord") is not one yet.
- **A section that returns is named again, not copied**: one that comes back as it was is written
  once and played twice by the form (`form { A B A Coda }`) — never a second section with the same
  notes. A return that changes (the tune an octave higher, a new accompaniment, another ending) is a
  section of its own (`A2`), and the plan says what changes; the bars that do not change are
  written as they were.
- **One home key, one tempo**: a section may move to a related key (`key g major` at its head —
  the relative, the dominant) and the piece comes home to end. A section in the RELATIVE key (B
  minor in a D major piece, A minor in C) keeps the signature and writes no `key` line: the same
  sharps or flats would only be printed again. The tempo stays unless the
  character changes with it (a slow middle section, `tempo "Adagio" 4 = 60`), never a new tempo
  for every section.
- **The end**: the last phrase closes in the home key — V (or V7) to I, the tonic chord held
  through the final bar in every part, the bass on the tonic.
- **Registers**: every part where its player plays (see *Instrument ranges* — a piano's left hand
  round C3, its right hand round C5).
- **A band** (rock, pop, funk, metal, … — any request naming an electric guitar, an electric
  bass, drums or band keyboards): the whole rhythm section plays, whichever members were named —
  **drums** (a `clef percussion` part: a groove, fills at the section ends), an **electric
  bass** (`instrument bass` with `midiInstrument "electric bass (finger)"`, locked to the
  kick drum) and a **guitar or keyboards** for the chords (`instrument guitar` with
  `midiInstrument "overdriven guitar"` or `"electric guitar (clean)"`; keyboards on
  `instrument piano-right` with `"rock organ"`, `"electric piano 1"` …). "Guitar and drums"
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
  guitar — doubled by the bass, the drums driving with the snare on 2 and 4; verse and chorus in a
  guitar-friendly key (E, A, D, G), an instrumental break (a guitar solo over the verse's chords)
  before the last chorus.
- **Gospel**: a piano (or an organ: `midiInstrument "drawbar organ"`) under a solo voice, a choir
  (`choirStaff`, soprano–alto–tenor–bass in close harmony), or both in call and response — the
  soloist sings a line, the choir answers it. Rich chords (added 9ths and 7ths, a dominant with a
  sus4 before it resolves, passing diminished chords, the bass walking up into the next chord), a
  slow 12/8 or a swinging fast 4/4, and the IV–I "amen" at the end.
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
  cadence), not rocking between a chord's root and fifth.
- **A song with words** (a voice part — `instrument soprano`, `alto`, `tenor` or `voice-bass`, the
  vocal line of a pop song too — and its lyrics row under it, see *Lyrics*):
  - **One sung unit a note, each written apart**: the unit is the language's own beat of speech —
    a syllable in most languages (English `Hap- py`, a word's syllables joined by `-`), a mora in
    Japanese (`き ら き ら`: a small kana goes with the one before it, `きゃ`; `っ`, `ん` and `ー`
    are units of their own), a character in Chinese. Lily# splits nothing: text written together
    is ONE unit on one note, with no warning. A melisma (one unit over several notes) is a slur,
    and is rare.
  - **The melody follows the words' own shape**: their stressed syllables on the strong beats and
    the weak ones off them (English, German, Spanish …); the rise and fall of a pitch accent or a
    tone where the language has one (Japanese 雨 falls from its first mora — a line that rises
    there sings 飴; a Chinese word's tone is not sung against). A long or high note on an open
    vowel (a, o, e), not on i or u or a closed syllable.
  - A line of words is a phrase of music: the line ends where the phrase does, on a longer note, a
    rest after it to breathe; a comma in the words is a breath in the line.
  - **Write the words first, whole, then set them**: each line as the sentence it is, its units
    counted, and its phrase given that many notes — the notes fit the words, never the words cut
    to fit the notes. Every word is sung whole: a line whose notes run out inside a word
    (`to- mor-` for "tomorrow", `も や` for 燃やす) gets another note or other words. A long note or
    a phrase's last note ends a word; a bar line may fall inside a word only where the phrase runs
    on through it.
  - Count every bar: as many units as notes, less the notes a slur or a tie carries over.
  - Words the request gives are set as they stand. Words written for it tell their story in the
    verses and come back unchanged in the chorus, its hook the title.
- **A piano piece** is written for two hands that fill the keyboard between them: the left
  hand's figure — broken chords, an octave bass, a root-and-fifth beat, an ostinato — reaching
  up toward middle C, the right hand's melody round C5 with its harmony under it (thirds, sixths,
  chords on the strong beats), never an octave of empty keyboard between the hands for long. A
  left hand may keep one note value through a section: its figure is the rhythm; the melody is
  where the rhythm varies. A contrasting section takes a figure of its own. A broken-chord figure
  stays within an octave and a half (a twelfth), climbing from its bass by the chord's tones and
  turning back — not leaping two octaves from a low bass to the treble.
- **Texture**: one voice carries the melody while the others accompany — broken chords,
  repeated eighths, a moving bass — and the roles pass between the parts. Not every part in the
  same rhythm; not whole notes everywhere. **A rest in one part is covered by another**: the
  accompanying parts do not stop together bar after bar (three lower strings all resting on beat
  4 of every bar leave the tune alone there, and the music hiccups) — while one breathes, another
  holds a note or moves on; they rest together only at a phrase's end, or for an effect.
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
