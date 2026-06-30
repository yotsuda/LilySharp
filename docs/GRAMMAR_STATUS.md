# Lily# Grammar — Feature Status

> Quick implemented-feature checklist. The canonical syntax reference is
> [`GRAMMAR_FOR_LLM.md`](GRAMMAR_FOR_LLM.md); the formal EBNF is [`GRAMMAR.md`](GRAMMAR.md).
> Updated 2026-07-01.

### 1. Header / metadata
| Feature | Syntax | Status |
|------|------|------|
| Title / composer | `title "..."` / `composer "..."` | ✅ |
| Tempo | `tempo 120` (`tempo 120 swing` / `swing 16` shuffle feel) | ✅ |
| Time / key | `time 4/4` / `key c major` | ✅ |

### 2. Structure declarations
| Feature | Syntax | Status | Notes |
|------|------|------|------|
| Part | `part name { clef treble }` | ✅ | bare attributes (no colon); `instrument`/`octave`/`channel`/`transpose` |
| Phrase | `phrase name { notes }` referenced as `$name` | ✅ | |
| Section | `section Name { partName { … } }` | ✅ | |
| Structure | `structure { Section … }` | ✅ | optional; navigation marks; per-score override |

### 3. Render targets
| Feature | Syntax | Status | Notes |
|------|------|------|------|
| Score | `score "name" { … }` | ✅ | one or more; optional per-score `structure` |
| Staff | `staff name` (bare, no braces) | ✅ | optional clef: `staff bass name` |
| Grand staff | `grandStaff { staff a  staff b }` | ✅ | |
| Tab | `tab name` | ✅ | Guitar/Bass/Bass5/Ukulele |
| Ossia | `ossia { name }` | ✅ | reduced-size alternative passage |
| Lead sheet | `chords name` / `lyrics name` (no staff) | ✅ | barline grid, chords between bars, lyrics below |
| MIDI | CLI: `lysc midi file.lys file.mid` | ✅ | no source block |

### 4. Music elements
| Feature | Syntax | Status |
|------|------|------|
| Notes / rests / spacer | `c4 d8 e16` / `r4` / `s4` / `R1` | ✅ |
| Chords | `<c e g>4` | ✅ |
| Octave / accidentals / dots | `c' c,` / `cis ees` / `c4. c4..` | ✅ |
| Tie / slur | `c4~ c4` / `c4( d e)` — slurs bind chords too: `<c e>4( <d f>)` | ✅ |
| Tuplets / grace | `tuplet 3/2 { c8 d e }` / `grace { c16 d } e4` | ✅ |
| Beams | automatic, or manual `c8[ d e f]` | ✅ |
| Multi-voice | `voice { … } voice { … }` (NOT `<< \\ >>`) | ✅ |

### 5. Annotations (`@name`, with optional `.up` / `.down`)
| Feature | Syntax | Status |
|------|------|------|
| Articulations | `@staccato @accent @tenuto @marcato @fermata @portato` | ✅ |
| Ornaments | `@trill @mordent @prall @turn @invertedturn` | ✅ |
| Dynamics | `@p @f @mf …` (`@f.up` forces side) | ✅ |
| Hairpins | `@cresc @decresc @dim` (no `.up`/`.down`) | ✅ |
| Stem direction | `@stemUp` / `@stemDown` | ✅ |
| Accidental style | `@courtesy` / `@editorial` | ✅ |
| Arpeggio / glissando | `<c e g>4@arpeggio` / `c4@glissando d` | ✅ |
| Figured bass / chord name | `c4@fig.6` / `c4@chord.C` | ✅ |

### 6. Repeats / navigation
| Feature | Syntax | Status |
|------|------|------|
| Repeat | `|: … :|` (count `:|*N`) | ✅ |
| Volta endings | `[1. A] [2. B]` | ✅ |
| Marks / spanners | `segno coda fine dc ds`, `@rit @accel`, `@ottava … @loco`, trill spanner, pedals | ✅ |

### 7. Other
| Feature | Syntax | Status |
|------|------|------|
| Lyrics | `lyrics { Hap- py birth- day | to you | }` | ✅ |
| System break | `break` | ✅ |
| Custom text | `_"text"` | ✅ |
| Phrase / variable ref | `$name` | ✅ |
| Override / revert | `override Stem.length = 7` / `revert …` / `once override …` | ✅ |
