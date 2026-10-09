# Lily# Syntax Reference

Complete reference for the `.lys` music notation language.

## Strings

Quoted text — a title, `@text("…")`, a label, a lyric syllable — follows **C#'s string
grammar**:

| Form | Example | Reads as |
|---|---|---|
| Regular `"…"` | `"say \"hi\""` | say "hi" |
| | `"a\\b"`, `"one\ntwo"`, `"\u00e9"` | a\b, a line break, é |
| Verbatim `@"…"` | `@"C:\music"` | C:\music (a backslash as written) |
| | `@"say ""hi"""` | say "hi" (`""` is one quote) |

A regular string decodes `\" \' \\ \0 \a \b \f \n \r \t \v`, `\uXXXX`, `\UXXXXXXXX` and
`\x` + 1–4 hex digits. Any other backslash is an error (LYS0036) — write `\\`, or use the
verbatim form, which is the easy spelling for text that is full of backslashes.

## Pitches

### Basic Pitch Names

| Pitch | Name |
|-------|------|
| `c` | C |
| `d` | D |
| `e` | E |
| `f` | F |
| `g` | G |
| `a` | A |
| `b` | B |

### Accidentals

| Suffix | Meaning | Example |
|--------|---------|---------|
| `is` | Sharp | `cis` = C# |
| `es` | Flat | `ees` = Eb |
| `isis` | Double sharp | `cisis` = C## |
| `eses` | Double flat | `deses` = Dbb |

Special forms: `ees` (Eb), `aes` (Ab), `bes` (Bb). LilyPond's contractions are accepted too: `es` = `ees`, `as` = `aes`, `eses` = `eeses`, `ases` = `aeses`.

Annotations:

```
a4@courtesy    // Courtesy (cautionary) accidental — parenthesized, left of the note
c4@editorial   // Editorial (suggestion) accidental — small, above the note (musica ficta)
```

### Octave Marks

| Mark | Meaning |
|------|---------|
| `'` | One octave up |
| `''` | Two octaves up |
| `,` | One octave down |
| `,,` | Two octaves down |

Default starting octave is C4 (middle C). Each pitch takes the octave
closest to the previous note (an interval of a fourth or less); `'` and `,`
shift octaves on top of that.

> **Octave mode — relative (default) vs absolute.** By default Lily# resolves
> octaves *relative* to the previous note (the "nearest octave" rule above). It is
> compact, but a wide leap can leave the line an octave from where you meant it,
> and a repeated figure like `c c g g` can walk steadily downward. To write
> **fixed** octaves instead — bare `c` always means C4, and `'` / `,` are absolute
> per-note offsets — put **`octave absolute`** at the top of your file (return to
> the default with `octave relative`):
>
> ```
> octave absolute
> c' d' e' c'       // always C5 D5 E5 C5 — no drift, whatever the leaps
> ```
>
> A finished file can be switched either way without moving a note: the editor commands
> **Convert Octaves to Absolute** / **Convert Octaves to Relative**, or
> `lysc octave --absolute|--relative`, rewrite the directives and every note's marks.

```
c d e f g a b c   // C4 D4 E4 F4 G4 A4 B4 C5 — bare c after b is already C5
c' c,             // C6 C5 — marks shift from the nearest octave
```

Each **phrase body** evaluates in a fresh frame — the default octave, pitch
and duration — regardless of where the phrase is referenced, so a phrase
always means the same notes at every call site. What flows out is the
phrase's **anchor** — its first note's bare letter, shifted with the
reference's own marks — exactly like a chord: a note written after the
reference is relative to that anchor, never to how the body ends. A body that opens
with a `chord(…)` item anchors on the item's lowest note, as the item itself hands on —
absolute, so the reference's marks do not move it.
**Section boundaries** also reset the frame.

## Durations

| Value | Name | Beats (in 4/4) |
|-------|------|-----------------|
| `1` | Whole | 4 |
| `2` | Half | 2 |
| `4` | Quarter | 1 |
| `8` | Eighth | 1/2 |
| `16` | Sixteenth | 1/4 |
| `32` | Thirty-second | 1/8 |
| `64` | Sixty-fourth | 1/16 |
| `128` | One-twenty-eighth | 1/32 |

### Dotted Notes

| Notation | Effect |
|----------|--------|
| `4.` | Dotted quarter (1.5 beats) |
| `2.` | Dotted half (3 beats) |
| `4..` | Double-dotted quarter (1.75 beats) |

### Default Duration

If no duration is specified, the previous note's duration is used:

```
c4 d e f   // All quarter notes
c8 d e f   // All eighth notes
```

### Bare Durations (repeat the previous event)

A duration standing **alone** repeats the previous note, chord or slash at the
new length — the same reading LilyPond gives an isolated duration:

```
bes8 8 8 8 8 8 8 8    // an eighth-note bass pump, written once
<c e g>4 4 2          // the chord again, quarter then half
c'4 r4 4              // rests are transparent: the last 4 is c' again
```

The bare number is a written duration, so it also sets the running default.
Rests and the empty chord `<>` are transparent to the run; an arpeggio breaks
it; a bare duration with nothing before it to repeat is an error (LYS0016).
The repeat keeps the original's absolute pitch (it is transparent to the
relative frame, like `q`) and takes only its own post-events.

A repeat that reaches back **across a barline** (`c4 d e f | 4 g f e`) is legal
but warns (LYS1031): a measure opening on a bare number is also what a dropped
pitch letter looks like (`4 g f e` meant as `a4 g f e`). Write the event itself
at the measure head when the repeat is meant; within-measure runs never warn.

## Notes, Rests, and Chords

### Notes

```
c4         // C quarter note
fis8       // F# eighth note
bes2.      // Bb dotted half note
```

### Slash Notes (rhythm notation)

`/` in note position is a pitchless note drawn as a **slash head on the middle
staff line** — comping rhythm. Duration carry, stems and beams behave as on an
ordinary note; playback is silent. Combine with a one-line staff
(`staff comp as lines 1` in the score — the line count is a property of the
rendering, so the same part can keep five lines elsewhere) for a rhythm chart:

```
/4 / / /              // four beat slashes
/8 8 /4 8 8 /4        // a comping figure (bare durations continue the run)
/4 4 g8 g /4 /        // ensemble kicks mix with pitched notes
```

`time 4/4`, `tuplet 3/2` and a chord entry's `c/g` keep their own `/` — only
the note position reads it as a slash.

### Rests

| Syntax | Meaning |
|--------|---------|
| `r4` | Quarter rest |
| `r2` | Half rest |
| `s4` | Spacer rest (invisible) |
| `R` | Full-measure rest — lasts its bar, whatever the meter |
| `R*3` | One rest over three bars (the count is printed above it) |
| `R1`, `R2.*4` | A full-measure rest of a written duration, as in LilyPond |
| `a4@rest` | Quarter rest placed where the note `a` would sit |

A full-measure rest written with no duration lasts the bar it opens: four quarters in 4/4,
five in 5/4, the pickup's length in a pickup bar. It is the one spelling for a 5/4 or 5/8 bar,
which no single note value fills (`R1` is four quarters; `R4*5` would be five bars). It must
open its bar, and there must be a bar to last — one after other music in the bar, or under
`time none`, is an error (LYS2016). It does not change the running duration: the note after
it takes the value the note before it had. `R | R | R` is three one-bar rests and `R*3` one
three-bar rest, as LilyPond engraves `R1 | R1 | R1` and `R1*3`. The LilyPond twin writes the
bar's own duration (`R1`, `R2.`, `R4*5`), and the MusicXML import writes every whole-measure
rest this way.

A rest normally finds its own height: on the middle line alone, up or down inside a
`voice { } { }` span, and out of the way of whatever the other voices are playing at
that moment. **Writing a pitch with `@rest` decides that height instead** — the rest
sits where that note would, and nothing moves it afterwards. Use it when two voices'
rests would otherwise land on top of each other, which is the one case the automatic
placement leaves alone:

```
octave absolute
part v { }
section Main {
  v {
    voice
    { g'8 g' g' r8 r2 | }
    { a,4@rest c r2 | }    // this rest sits at a — clear of the other voice's
    { c'4 c' f'2@rest | }  // and this one at f, on a staff line
    { r2 g | }
  }
}
form { ~Main }
score { staff ~v }
```

The pitch is only a height: it never sounds, never prints an accidental, and never
takes a ledger line of its own (a whole or half rest that lands off a staff line
carries a short one inside its own glyph, as it does anywhere else).

### Chords

Notes enclosed in angle brackets share a duration (written after the `>`):

```
<c e g>4       // C major triad, quarter note
<d fis a>2     // D major triad, half note
<c 3 5>4       // the same triad by scale DEGREES: root + 3rd + 5th of the key
<1 3 5>2       // degrees only — anchored on the key's tonic (C E G in C major)
<2 4 6>2       // the ii triad (D F A in C major); degrees follow the key
```

**Octaves — the anchor model.** Two rules: *a mark moves only what it is attached to*,
and *a chord is one item of the relative chain — the next note reads its anchor.*

- The chord's **anchor** — where the chord *sits* — is the first member's bare **letter**
  (or the key **tonic** when the chord is degrees-only), resolved nearest to the running
  frame. The note after the chord is relative to that anchor, just as after a single
  note, so editing a note two items back cannot move it: in `g1 | <c e g>1 | <f a c>1`
  the f reads the chord's c, and rewriting the `g1` as `f1` moves neither chord.
  A run of chords climbs or falls the way a run of single notes does —
  `<c e g> | <d f a> | <g b d> | <c e g>` ends an octave up, like `c d g c`; write
  `<g b d>,` to come back down.
- Every member sits at-or-above the anchor, so the written order doesn't matter
  (`<c e g>` = `<c g e>`); only the first slot does (`<g c e>` anchors on g).
  Degrees are fully order-independent (`<2 4 6>` = `<6 2 4>`).
- A `'` / `,` **on a member** moves *that one note* only — the first member's
  included: `<c' e g>` = C5 E4 G4, and the next bare `c` is still C4. Because the
  anchor is the bare letter, `<c, e g> <c, e g>` repeats the same C3 E4 G4 however many
  times it is pasted.
- A `'` / `,` **after the `>`** (before the duration) moves the **whole chord** and its
  anchor, so it *propagates*: after `<c e g>'4` (C5 E5 G5) a bare `c` continues at C5.
- A degree chord hands on the **tonic**, not the first degree written: the degrees are
  stacked upward from the tonic, so `<1 3 5> <5 7 2> <1 3 5>` returns to the same I.

```
<c e g>4       // C4 E4 G4
<c g,>4        // C4 G3 — a member ',' drops that one note
<c' e g>4      // C5 E4 G4 — the root's mark is local; next bare c = C4
<c e g>'4      // C5 E5 G5 — whole chord up, and the frame with it; next bare c = C5
<g' b' d'>4    // marking every member sounds the same chord as <g b d>' — but LOCALLY:
               // the anchor stays on the bare g, so the next bare c does not follow
```

### Arpeggios (`<< … >>`)

An arpeggio is a *written-out* broken chord: the members play in **sequence** and
**equally subdivide** the group's total duration (members carry no durations of their
own — a bare number is always a scale degree). Octaves follow the chord anchor model — the
note after the group reads the group's anchor (not its last member), and a mark after `>>`
moves the group and its anchor:

```
<< c e g >>         // c, then e and g stacked above it (E4, G4) — an ascending arpeggio
<< c g >>           // g is a fifth ABOVE c, exactly like the chord <c g>
<< c g e >>         // same pitches as << c e g >>, only the play order differs
<< c 3 5 >>         // by degrees: c e g
<< 8 5 3 1 >>       // degrees-only anchors on the TONIC: C5 G4 E4 C4 — descending, no marks
<< c e g >>'        // the whole group an octave up; the next bare note follows it there
```

Members may be **chords** or **rests**:

```
<< <c e> g >>       // a chord member, then g — an arpeggio of stacked members
<< c r e g >>       // the rest is a gap (an equal share of the total)
```

A member carries what a note carries — a script, a fingering, a dynamic, a string
number, a slur mark — written on the member. A mark written after `>>` belongs to the
group: a dynamic sounds from its first note, a chord name labels it, a string number is
every member's, and a tie or slur mark hangs on its **last** member.

```
<< c@accent e\3 g@finger(1) >>   // per-member marks
<< c( e g) >>                    // a slur over the group, on the members
<< c e g >>4\2                   // every member on the second string
<< c e g >>4~ g                  // the tie is the last member's
<< c e g >>4( d)                 // …and so is a bow started after '>>'
```

A **spaced dot** holds the member before it one more share of the total (not the 1.5×
of a duration dot — inside `<< >>` there is no duration for a dot to belong to). It is
written as its own token, never glued: `c.` would read as a duration dot and `3.` as a
decimal, and the parser reports both. Any whole-number ratio is written this way, and
the tuplet is spelled by the rule above from the total number of shares:

```
<< c . d >>4        // 2 : 1 in a quarter → tuplet 3/2 { c4 d8 }  (the swing figure)
<< c . . d >>4      // 3 : 1 → c8. d16, no bracket
<< c . d . e >>4    // 2 : 2 : 1 → a quintuplet of sixteenths, c and d as eighths
<< r . d >>4        // a rest holds shares too
<< c 3 . 5 >>       // after a degree, spaced
```

A member no single note can spell (five sixteenths) is written as tied notes.

Without a trailing duration the group takes the running duration and acts like one
note; a **duration after `>>`** sets the group's total. Either way the members split
it equally, becoming an automatic tuplet when needed. The tuplet is spelled the way
engraving convention spells it: the members take the plain note value that fills the
total **with the largest power of two not above their count**, and the bracket is that
count against it (Gould, *Behind Bars*). In a dotted total the frames are 3, 6, 12 …
and the nearest one is taken — compound metre's duplet and quadruplet:

```
<< c e g >>         // after c4: three in a quarter → eighths under 3:2
                    //   (the same picture as `tuplet 3/2 { c8 e g }`)
<< r c cis >>4      // an eighth rest, then two eighths, under 3:2
<< c e g >>2        // three in a half → quarters under 3:2
<< c e g a >>4      // four in a quarter → four plain sixteenths, no bracket
<< c d e f g >>4    // five in a quarter → sixteenths under 5:4 (six → 6:4, seven → 7:4)
<< c e >>4.         // two in a dotted quarter → eighths under 2:3 (a duplet)
<< c e g >>4.       // three in a dotted quarter → three plain eighths, no bracket
<< c e g a >>4.     // four in a dotted quarter → eighths under 4:3
```

The group must fit within one measure (otherwise it crosses the barline and the measure
overflows its meter).

> **Note:** this reuses `<< … >>`, which in LilyPond means simultaneous voices. Lily#
> writes parallel voices as `voice { … } { … }` (§ Voices), so `<< … >>` is free to
> mean an arpeggio here. A `\\` inside still reports the removed-polyphony hint.

## Articulations

Articulations are attached to notes with the `@` prefix. Names are resolved from
text, not reserved keywords, so a word like `tr` or `accent` stays usable as an
ordinary identifier (say, a phrase name) elsewhere:

```
c4@staccato    // Staccato
d4@accent      // Accent
e4@tenuto      // Tenuto
f4@marcato     // Marcato
g4@fermata     // Fermata
a4@portato     // Portato (tenuto + staccato)
```

Every annotation name is case-sensitive and has one spelling: a name of several words is
camelCase (`@upBow`, `@shortFermata`, `@reverseTurn`, `@laissezVibrer`, `@hammerOn`,
`@figuredBass`) whatever LilyPond spells it (`\upbow`, `\shortfermata`), and a one-word
name is lowercase (`@staccato`, `@pralltriller`). A name written in another case (`@hammeron`, `@Staccato`)
is an unknown annotation: it draws and exports nothing, and the warning (LYS1008) names
the spelling to write. The same holds for the VALUE words inside the parentheses, which
are all lowercase: `@notehead(triangle)`, `@bend(full)`, `@pluck(p)`, `@feather(right)`,
`@arpeggio(bracket)`, `@diagram(x32010)` (`x` and `o`), `@figuredBass(6 s)` —
`@notehead(TRIANGLE)` is refused with "write '@notehead(triangle)'". Free text keeps its
case (`@text("Dolce")`, `@mark("A")`), and so does a chord symbol, whose case is its
meaning (`@chord(Dm)`).

### Placement (`.up` / `.down`)

By default an articulation sits opposite the stem. Append `.up` or `.down` to force it
above or below the note:

```
c4@staccato.up     // staccato forced above
d4@accent.down     // accent forced below
```

### Left to check (`@todo`)

`@todo` marks a note, rest or chord to come back to — a reading you are unsure of, a bar to
review. It draws nothing and plays nothing: the compiler reports each one as a warning
(LYS4026, `TODO: memo`), the preview draws the marked head red (the
`lilysharp.preview.highlightTodos` setting turns that off), and the quick fix *Resolve this
TODO* deletes it. A quoted argument is a memo; a bare one is a key — a letter, then letters,
digits, `_` and `-` — which a tool (an OMR reader) links its own data to.

```
c'4 fis'8@todo("F# or F natural? the sharp is faint") e'8 g'2 |
r2@todo("bar 12 is only 7/8") d'2 |
c'4@todo(o1203 "smudged") e'4@todo g'2 |
```

`lysc check --todo-as-error` fails while any is left; `--no-todo` stops reporting them.
When an OMR reader wrote the file, its side file `x.omr.json` (beside `x.lys`) may list
candidates for a keyed mark; each is a quick fix that writes it over the marked item,
the mark with it.

## Ornaments

```
c4@trill         // Trill
d4@mordent       // Mordent
e4@prall         // Inverted mordent (pralltriller)
f4@turn          // Turn
g4@reverseTurn   // Reverse (inverted) turn — LilyPond's \reverseturn
```

## Dynamics

Dynamics use `@` prefix (or `\` for LilyPond compatibility):

```
c4@ppp   // Pianississimo
c4@pp    // Pianissimo
c4@p     // Piano
c4@mp    // Mezzo piano
c4@mf    // Mezzo forte
c4@f     // Forte
c4@ff    // Fortissimo
c4@fff   // Fortississimo
```

Dynamics sit below the staff by default. Append `.up` / `.down` to force the side:

```
c4@f.up      // forte above the staff
d4@p.down    // piano below (the default)
```

A note takes one dynamic: in `c4@f@sfz` the `f` is printed and the `sfz` is not, and the
second warns (LYS4022) — LilyPond keeps the first as well. A hairpin beside a dynamic is
fine (`c4@p@cresc`).

### Hairpins (Crescendo/Decrescendo)

```
c4@p @cresc d e f |
g4@f @decresc a b c |
```

A hairpin runs to the next dynamic (or the next hairpin) on its staff.

#### Niente (al niente / dal niente)

`@niente` is the silent dynamic. A hairpin whose thin end touches it gets a small circle at
its tip and the word is not printed:

```
c4@mf@decresc d e f | g1@niente |     // decrescendo al niente: fades to nothing
c4@niente@cresc d e f | g1@f |        // crescendo dal niente: grows from nothing
c4@p@decresc d e@niente@cresc f | g1@f |   // both on one note share one circle
```

Alone, `@niente` prints the word *niente* in italic. At a hairpin's thick end (a crescendo
ending on it, a decrescendo starting from it) it warns (LYS4029) and the wedge is drawn
plain. In MIDI it is the quietest level (velocity 1); in MusicXML it is the wedge's
`niente="yes"` or the `<n/>` dynamic, and LilyPond writes the hairpin with `circled-tip`.

`.up` / `.down` cannot be applied to `@cresc` / `@decresc` / `@dim` — a hairpin is
always engraved below the staff, so a placement suffix there is rejected as an error
rather than silently ignored. (Placement works on dynamic *levels* like `@f.up`.)

## Ties and Slurs

### Ties

Connect notes of the same pitch with `~`:

```
c4~ | c4 d e f   // C tied across barline
```

### Slurs

Connect different pitches with `(` and `)`:

```
c4( d e f)        // Slur over four notes
c4( d) e( f)      // Two separate slurs
```

A slur mark written INSIDE a chord's brackets, after one of its pitches, binds the bow to
that note head instead of the whole chord — LilyPond's `<c e( g>`:

```
<c e( g>4 <d f) a>      // from the e's head to the f's head
<c( e g>4 <d f a>)      // from the c's head to the second chord
```

The bow then leaves the head's inner edge and stays close to it, as LilyPond draws it. The
mark goes after a pitch member (after a scale degree or a drum name it is reported); a mark
after `>` is the chord's own.

### Phrasing Slurs

A phrasing slur — the long curve over a musical sentence, LilyPond's `\(` … `\)` — is a span,
written like the other spans with a start and a `@!` end:

```
c4@phrasingSlur d( e) f | g( a) b c@!phrasingSlur |
```

It is drawn over the ordinary slurs inside it, and clears them the way LilyPond's does (it
is the only curve that avoids a slur; a slur never avoids another slur). One phrasing slur is
open per voice at a time — a second `@phrasingSlur` before the first is closed is ignored and
warned about, and **the end is required**: an unclosed one draws nothing and is an error
(LYS4018). A note that ends one phrase and begins the next carries both marks,
`e@!phrasingSlur@phrasingSlur`, and closes before it opens. `@phrasingSlur.up` /
`@phrasingSlur.down` fixes its side (LilyPond's `^\(` / `_\(`); otherwise it takes a slur's
rule.

### Across a section boundary

A slur, a phrasing slur, a tie or a hairpin that is still open when a section ends is carried
into the section the form plays **next**, and must end there:

```
part vc {
  section C { g4 a b c( || }
  section D { d4) e f g~ || }
  section E { g'1 | }   // the frame reset at E: g' is the tied G
}
form { C D E }
score { staff vc }
```

The carry follows the form, play by play and part by part, so the same section can be followed
by different sections in different places (`form { C D C E }` carries C's slur into D the
first time and into E the second). Every form a score plays is checked. A span that breaks the
rule is not drawn (a hairpin is cut at the end of its own section) and is reported (**LYS4023**):

- carried into the next section and not ended there — a span may cross one boundary, not run
  through a whole section (warning);
- a `)` or `@!phrasingSlur` at a section's start with nothing carried in from the section played
  before it (warning);
- carried into a section the part plays nothing in — its bars there are padding (warning);
- a slur, phrasing slur or hairpin carried over a repeat sign (`|:` `:|` `:|:`), into or out
  of a volta ending, or over a jump mark (segno, coda, D.S., D.C., fine) — what is played before
  that section differs from pass to pass, so this is **an error**.

A **tie** may cross all of those. It is carried to the first note of every section that is
*played* after the tied note's section — the way the MIDI plays the form: the body again at
each pass, that pass's ending, whatever follows the block — and each of those notes must repeat
the tied pitch (otherwise LYS4007, as for any tie):

```
part vn {
  section I { c''1~ || }
  section A { c''1 | e1~ || }
  section B { e''1 | }
}
form { I |: A [1. B] :| [2. B] }
score { staff vn }
```

Where the section played next is also the one printed next, the tie is an ordinary arc (I into
A, A into the first ending). Where it is not — back to the `|:`, into a later ending — the tied
note gets a hanging tie and the note the music arrives at a repeat tie, drawn as if
`@laissezVibrer` / `@repeatTie` were written there (once, however many ties arrive at it). The
MIDI sustains the note on each pass the tie is carried on and re-attacks it on the others;
MusicXML writes the tie's start on the tied note and its stop on every note it reaches; the
LilyPond twin writes `\repeatTie`. Jumps (D.S., D.C.) are followed too: a tie at the end of
the section before a jump text is carried to every section the route plays next — the segno's
(or the piece's first) on the replay, the one after the `coda` sign when the replay leaves at
`to coda` — exactly where the MIDI sustains it (see Navigation marks). A jump text written in a
section's music, or inside a `|: … :|` block, is drawn and not followed by any of them.

Everything else a section starts from still resets at the boundary (the relative frame, the note
value, the meter, the key, the clef, overrides), so a tie's target states its octave. A span
opened in the last section and never closed is still the ordinary unclosed slur (LYS4010) or
phrasing slur (LYS4018). Text spanners, ottavas, pedals and trill spanners are paired as before.

### Restating: `key!`, `time!`, `clef!`

A `time`, `key` or `clef` that changes nothing **draws nothing**, wherever it is written. At a
section's start — in its header or before its first note — "nothing" means what was in force
when the section before it ended: the meter, the key and the clef reset at a section boundary,
so a section that continues in what the section before it left has to say so again, and
neither that restatement nor the boundary's reset is engraved. A different value is drawn as
usual.

To draw one that changes nothing — a courtesy key at a new movement, a clef restated after a
long rest — write `!` right after the keyword: `key! ees major`, `time! 3/4`, `clef! bass`
(a space before the `!` is allowed). It is drawn whether or not it changes anything. The `!`
goes after the keyword because after the value it is the dashed barline: `key ees major !` is
a key followed by a dashed bar.

```
time 4/4
part m { clef treble }
section A { m { c4 d e f | time 3/4 key ees major c2. | key ees major c2. | } }  // 2nd key: nothing
section B { m { time 3/4 key ees major c2. | } }   // nothing drawn at B
section C { m { key! ees major c2. | } }           // E♭ major drawn again, and 4/4 (the reset)
form { A B C }
score { staff m }
```

The key compares its tonic too: `key g major` after `key e minor` is drawn. The LilyPond twin
writes what the page draws: it omits the same restatements (LilyPond engraves every `\time` and
`\key`), and writes `clef!` with `\set Staff.forceClef = ##t`. The MusicXML export likewise
writes no `<attributes>` for a restatement and writes them again for `key!`, `time!` and
`clef!`. This is what lets the MusicXML
import and the editor's Split Sections cut a section wherever the music in force is not the
file's.

The MusicXML import reads it back the same way. A `<key>`, `<time>` or `<clef>` that changes
nothing is in the file on purpose (MusicXML prints what `<attributes>` states), so it comes
back as `key!`, `time!` or `clef!`, and a section opens on its bar. One on a bar that opens a
system (`<print new-system="yes">` or `new-page`) is the courtesy one a line's head prints —
a writer copying the printed page restates the clef and key on every line — and is dropped. In
a piece with no rehearsal mark, a section also opens at every change of key, time or clef;
where there are marks, the sections are the marks'.

## Barlines

| Syntax | Type | Where |
|--------|------|-------|
| `\|` | Single barline | music, chord row, lyric row, form |
| `\|\|` | Double barline | music, chord row, lyric row, form |
| `\|.` | Final barline | music, chord row, lyric row, form |
| `!` | Dashed barline | music |
| `\|:` | Repeat start | **`form` only** (LYS1034) |
| `:\|` | Repeat end | **`form` only** (LYS1034) |
| `:\|:` | Back-to-back repeat (`:\|` then `\|:`) | **`form` only** (LYS1034) |

The three repeat barlines change the order the music plays in, so they are written where
the order is — see [Volta Repeats](#volta-repeats).

### Line and Page Breaks

| Syntax | Effect | Where |
|--------|--------|-------|
| `break` | Force a system break here | music, form |
| `noBreak` | Forbid a system break here | music, form |
| `pageBreak` | Force a page break here (and the system break with it) | music, form |
| `noPageBreak` | Forbid a page break here | music, form |

At a bar line — `c4 d e f break | …` or `… | break g1` — the break ends the system with that
bar. **Inside a bar, with notes on both sides of it** — `c4 d break e f |` — the bar itself is
broken across the two systems, as LilyPond's `\break` does: the first half ends the system
with no bar line, the second half opens the next with no bar line and no bar number (the bar
number counts bars, so the next full bar is numbered as if nothing had happened), and every
part, chord row, lyrics row and empty `| |` gap of the score is cut at the same beat. The bar
stays whole — and the break falls to the next bar line — where some part holds a note, chord
or rest **sounding across** the break, or a beam, tuplet or percent repeat runs across it, or
the bar is unmetered (`time none`), or it is a second break in the same bar; LYS1037 names the
part and the reason. Tie the note across the break (`c2~ break c2`) or move the break to a beat
nothing crosses. The `.ly` twin writes `\break` where it stands either way, so a book carrying
LYS1037 breaks differently in LilyPond.

## Key Signature

```
key c major      // C major (no accidentals)
key g major      // G major (1 sharp)
key f major      // F major (1 flat)
key a minor      // A minor (no accidentals)
key d minor      // D minor (1 flat)
```

## Clef

```
clef treble      // Treble clef (G clef)
clef bass        // Bass clef (F clef)
clef alto        // Alto clef (C clef, line 3)
clef tenor       // Tenor clef (C clef, line 4)
```

## Time Signature

```
time 4/4         // Common time
time 3/4         // Waltz time
time 6/8         // Compound duple
time 2/2         // Cut time
```

## Tempo

```
tempo 120             // Quarter = 120 BPM
tempo "Allegro" 4 = 120  // With text marking
tempo 120 swing       // + swing/shuffle feel equation beside the mark
tempo 120 swing 16    // sixteenth-note swing (double-beamed)
tempo swing           // the swing equation alone, no metronome mark
```

Adding `swing` (or `shuffle`) after the tempo draws the swing equation — two beamed
straight notes = a quarter and an eighth under a triplet `3` bracket, LilyPond's own
`\rhythm { 8[ 8] } = \rhythm { \tuplet 3/2 { 4 8 } }` — to the right of the metronome
mark (or of the marking, or alone when neither is written), the way shuffle charts are
headed. A trailing number picks the note value that swings: `swing` (= `swing 8`) for
eighths, `swing 16` for sixteenths (`16[ 16] = \tuplet 3/2 { 8 16 }`); any other number
is reported (LYS0034). The words are contextual, not reserved, so `swing` / `shuffle`
stay usable as your own names.

## Metadata

```
title "Sonata in C"
subtitle "K. 545"
composer "W.A. Mozart"
poet "Anonymous"
```

The four words are LilyPond's `\header` fields of the same names, and the page draws them
where LilyPond's title block does: the title centred; the subtitle centred on the line below
it, bold and a little smaller; the poet at the left end and the composer at the right end of
the next line, at text size. Each is optional.

## Text Fonts

The whole document in one face — that is the two generic families, bound together:

```
fonts {
  serif "Georgia"
  sans  "Georgia"
  embedded          // also subset-embed it in the PDF
}
```

⚠️ **`fonts` is plural, and takes a BLOCK.** There is no `font` keyword and no one-line
form; a bare value (`fonts "Georgia"`) is an error that quotes your face name back inside
the block to write instead. Completing `fonts` in the editor inserts the block pre-filled
with the faces already in use, so accepting it changes nothing until you edit a face.

Or a face per kind of text:

```
fonts {
  serif     "Georgia"                       // everything serif, unless overridden below
  sans      "Verdana"                       // chord symbols are the engine's one sans

  lyrics    "Charis SIL" "Noto Serif CJK JP"  // a fallback chain, most preferred first
  title     "Cormorant" size 3.8 bold       // a face, an absolute em, a weight
  chord     as sans                         // point a role at a bundled family
  marks     "Georgia"                       // a whole group at once
  tempo     "Playfair Display" italic       // ...and one member of it, overriding the group
  mark      step +1                         // one LilyPond font-size step larger
  numbers   step -1                         // the whole group one step smaller

  embedded
}
```

**The music font** — the glyphs rather than the text — is the `music` entry:

```
fonts {
  music "Bravura"                 // Emmentaler (LilyPond's own) when not written
  music "Petaluma" "Bravura"      // a per-glyph fallback chain, most preferred first
}
```

Bundled: Emmentaler, Bravura, Petaluma, Leland (the last three are SMuFL fonts; the
name is compared without regard to case). Another SMuFL font is found where the SMuFL
specification says its metadata lives. `music` takes quoted names only — a glyph's size
is its grob's (`layout { NoteHead.scale 1.2 }`), so `step`, `size`, `as` and a style on it
are errors. A name found nowhere **warns** (LYS8019, naming every place looked) and the
next name, or Emmentaler, is used. Each glyph comes from the first named font that has
it; one that none has (Leland lacks the figured-bass digits, for one) is drawn in
Emmentaler and the render warns once per glyph. The first font's own line thicknesses
(staff line, stem, ledger line, beam, bar lines) are used where `layout { }` and `--set` say
nothing; whatever they say wins. `lysc svg --set music=Bravura` lays a name over the
file's. The LilyPond twin stays in Emmentaler (LilyPond 2.26 reads no SMuFL font) and warns.

A **chord symbol** follows the music font too: its ♭ ♯ △ ° ø + are the font's own
chord-symbol glyphs where it has them, and under Petaluma its letters are set in Petaluma
Script, the handwritten face bundled beside it — unless `chord` (or `chords`, or the `sans`
family) names a face, or `chord as serif` / `as sans` says which family to follow.

An entry is a **key followed by attributes**, in any order, and it ends where the next key
begins — there is no separator. The attributes:

| Attribute | Meaning |
|---|---|
| `"Face"` … | the face, or a fallback chain when several |
| `as serif` / `as sans` | follow a generic family instead of naming a face |
| `step ±n` | size relative to the role's default, in LilyPond `font-size` steps (six steps double); the twin writes it as `\override Grob.font-size = #n` |
| `size n` | an absolute em in staff spaces (0.5..20) — not reproduced by the LilyPond twin, so prefer `step` |
| `bold` `italic` `regular` | weight and slant; `bold italic` combine, `regular` clears, the last word decides |

A written style **replaces** the engraving's own: `text bold` sets a text script bold and
upright, `tempo italic` sets the marking italic, not bold-italic. One entry takes `step` or
`size`, not both.

⚠️ **`chord serif` — a bare family word after a key — is refused** since 2026-09-08:
a bare word after a key is the next key, so that line opens an empty `serif` entry. Write
`chord as serif`. A generic family (`serif`, `sans`) takes quoted faces only; a size
or a style on it is an error.

**Size and style reach these roles**: `title subtitle composer poet instrument stanza chord
diagram tempo mark pedal navigation text dynamics partCombineText barNumbers tuplet volta ottava
bend tabTechnique clefOctave tab time` — every role, and for each one the drawing and the
reserved space read the plan together (the `lyrics` group reaches the syllables themselves,
which have no role of their own). `finger` and `figuredBass` are Emmentaler digit
runs: a `step` moves the glyph's font-size (its design, em and box together), and a style has
nothing to act on and **warns** (LYS8018). `tab step` moves the fret digit and everything
measured from it — its column, the bite out of the string line, the stem's near end, a tie's
clearance — and not the string spacing (as LilyPond's `TabNoteHead.font-size` leaves
`staff-space` alone). `time` is the compound numerator's `+` alone — the signature's digits
are Emmentaler glyphs — and its step widens the column the signature is booked at. A group
warns only when none of its roles follows.

**The narrower spelling wins**, in either source order and for each attribute on its own:
`role` beats `group` beats `serif`/`sans` beats the bundled face (size and style have no
family layer). So the `marks`/`tempo` pair above needs no special case, and `lyrics step -1`
with `stanza bold` gives small syllables under a small bold stanza number.

The keys, by group. A key is spelled as the source writes what it styles (`@chord` →
`chord`, `time 6/8` → `time`, a `tab` staff → `tab`); a key with no single source word names
the family (`pedal`, `navigation`, `dynamics`, `tabTechnique`) or the printed text (`stanza`,
`volta`, `clefOctave`).

| Group | Roles it covers |
|---|---|
| `header` | `title` `subtitle` `composer` `poet` `instrument` |
| `lyrics` | the syllables (no key of their own) and `stanza` |
| `chords` | `chord` `diagram` `figuredBass` |
| `marks` | `tempo` `mark` `pedal` `navigation` `text` `dynamics` `partCombineText` |
| `numbers` | `barNumbers` `finger` `tuplet` `volta` `ottava` `bend` `tabTechnique` |
| `notation` | `clefOctave` `time` `tab` |

**`diagram` sizes the whole chord diagram**, not only its "5fr" label. `fonts { diagram step +3 }`
draws the grid, the dots and the o/x marks about 1.4× larger, and `step +6` draws them twice
as large. With no entry, the diagram is LilyPond's default size: one staff space between
strings and between frets. Chord diagrams stand side by side over their notes. When a bar is
too narrow for its diagrams, the bar gets wider instead of the diagrams stacking (LilyPond's
`\textLengthOn`, which the `.ly` export writes on each diagram).

⚠️ **`notation` is not reached by a `serif`/`sans` binding.** The
octave digit under a `treble_8` clef, a compound time signature's `+`, and tab fret numbers are
notation that happens to be drawn as text — restyling them changes the notation rather
than the words — so they follow a face only when you name `notation` or the role itself.
A size or a style has no family layer, so for those the named entry is the only door:
`notation step +1` or `tab bold` is always deliberate.

**A named face is measured, not only drawn** (since 2026-08-18). The layout reserves space
with the same file the string is drawn in, so a title in a wide face gets a wide box.
Before this the reservation always used the bundled face, and on ordinary strings the drawn
width ran −2.05 to +3.61 staff spaces from the reserved one.

⚠️ **So a score that names a face lays out differently on a machine that does not have it**
— there the reservation falls back to the bundled face. LilyPond has the same exposure for
the same reason (its `font-name` goes to fontconfig), and it is why a missing face warns
rather than passing quietly. A score that names nothing is unaffected: the bundled faces
ship with the engine.

⚠️ **`embedded` does one thing**: it subsets the named faces into an exported PDF. It does
not change how anything is measured or drawn.

Unknown keys are an error (a binding that reaches nothing looks exactly like one that
works), a key bound twice in one block is a warning and the last wins, and `mono` is not a
key because no text in this engine is monospace.

A face this machine does not have is a **warning**, with or without `embedded` — whether a
font is installed is a property of the machine and not of the source, so a score that is
right on your box must not fail to compile on a runner that has no fonts.

**A named block is per-score.** `fonts NAME { … }` at the top level declares a reusable
block that binds nothing by itself; a score references it as `fonts NAME`, or overrides
part of it with `fonts NAME { lyrics "…" }`:

```
fonts house { serif "Georgia"  lyrics "Charis SIL" }

score  { fonts house  staff melody }
score parts { fonts house { lyrics "Noto Serif CJK JP" }  staff melody }
```

The reference **replaces** the file's unnamed default for that score, and the override
block reads as if its entries were written at the end of the named block — the same key
written again wins, with no duplicate warning across the two blocks. ⚠️ **The
narrower-spelling rule keeps winning whichever block a binding came from**: a house
block's `stanza` (a role) beats a score's `lyrics` (its group) — deliberately, so a
house style's role choices survive a score swapping the broad base. Override a role with
the same or a narrower key. An unknown reference name is an error naming the declared
blocks; a named block no score references is a warning; a second reference in one score
warns and the last wins.

## Paper

The page's dimensions — paper size, margins, indents, and the vertical spacing specs.
One block per file. Every default equals LilyPond's a4 default, so an absent block, an
empty one, and one that states the defaults all lay out identically:

```
paper {
  paperWidth 210mm
  paperHeight 297mm            // 0 = one content-driven page
  leftMargin 15mm  rightMargin 15mm
  topMargin 10mm  bottomMargin 10mm
  indent 15mm  shortIndent 0
  raggedRight                  // bare flag: lines keep their ideal width
  raggedBottom                 // bare flag: every page keeps its natural system spacing
                               // (default: only the last page does, as in LilyPond)
  breaksOnly                   // bare flag: break lines and pages only at break / pageBreak
  spacingIncrement 1.2         // horizontal note-spacing unit
  shortestDurationSpace 2      // the shortest note's space, in spacing increments
  staffSpace 1.757299mm        // the staff's size: the distance between two staff lines
  systemsPerPage 4             // exactly 4 systems on every page (or min/maxSystemsPerPage)
  measuresPerSystem 4          // exactly 4 bars on every system
  systemSystemSpacing { basicDistance 12  minimumDistance 8  padding 1  stretchability 60 }
  staffStaffSpacing   { basicDistance 9 }
}
```

**A bare number is staff spaces** — the unit everything else in this language is measured
in. A physical unit is a word **glued** to its number, one quantity: `210mm`, `29.7cm`,
`8.5in` (LilyPond spells the same thing `210\mm`). A spaced `210 mm` is an error naming
the glued spelling, and so is a unit in another case (`210MM` → "write 'mm'"). The conversion is the one the engine's defaults were computed with
(1 staff space = 5 TeX points), rounded the same way — so writing a default out **is**
the default, byte for byte.

**A whole page by name**: `size b5` sets the width, the height **and** the four
margins, scaled the way LilyPond's `set-paper-size` scales them — each margin default by
the size's ratio to a4, rounded to whole millimetres, so `size a4` is the identity and
`size b5` gives 13mm sides and 8mm top/bottom. The name is **bare** and lowercase, like
every closed vocabulary's values (`clef treble`, `tuning guitar`) — `size A4` is refused
with "write 'a4'"; quote only a name that carries a
space (`size "ansi a"`) — the lyric syllable's rule. The names are LilyPond's paper
table — `a0`…`a10`, `b0`…`b10`, `c0`…`c10`, `letter`, `legal`, `tabloid`, `ledger`, and
the rest — plus **`jisb5`** (182 × 257 mm), which is Lily#-own: ISO `b5` (176 × 250) is
not the Japanese B5, and Japanese sheet music commonly uses JIS B5. `size` reads at its
position like every other key — write it first, then refine
(`size b5  topMargin 12mm`).

The spacing blocks, each taking `basicDistance` / `minimumDistance` / `padding` /
`stretchability` lines (`stretchability` is unitless):

| Key | The pair it spaces |
|---|---|
| `systemSystemSpacing` | two consecutive systems |
| `scoreSystemSpacing` | a score boundary, then the next system |
| `markupSystemSpacing` | a title/markup, then the next system |
| `scoreMarkupSpacing` | a system, then the next title/markup |
| `markupMarkupSpacing` | consecutive titles/markups |
| `topSystemSpacing` | the page top and the first system |
| `lastBottomSpacing` | the last element and the page bottom |
| `staffStaffSpacing` | two staves of a group |
| `staffGroupStaffSpacing` | a group's staff and the next group's |
| `defaultStaffStaffSpacing` | ungrouped staves |
| `nonStaffRelatedStaffSpacing` | a lyrics/chord row and its own staff |
| `nonStaffUnrelatedStaffSpacing` | a lyrics/chord row and an unrelated staff |
| `nonStaffNonStaffSpacing` | two lyrics/chord rows |

⚠️ **The staff-spacing family lives here, not in `override`**, although LilyPond keeps it
on grobs (`StaffGrouper.staff-staff-spacing`): these quantities are applied score-wide in
one pass, and `paper { }` is the spelling whose meaning is score-wide — an override would
parse a scope (`once`, staff tags) and then silently not apply it.

**The staff's size, the page counts and the shortest note's space** (in the language since
2026-10-05; `lysc --set` takes the same keys):

- `staffSpace 1.5mm` — the distance between two staff lines on the paper (LilyPond's
  `set-global-staff-size`; the default 20pt staff is `1.757299mm`, `#(set-global-staff-size 17)`
  is `1.493704mm`). The page keeps its millimetres — the paper, the margins, the indents and
  every length written in mm / cm / in — so a smaller staff puts more music on the page, and
  the SVG, PNG and PDF keep the paper's size with the staff smaller on it. It needs its unit
  (a bare number would be staff spaces, the unit it sets) and applies to the whole block
  wherever it is written. Lengths written as bare numbers are staff spaces and scale with it.
- `systemsPerPage 4` — exactly 4 systems on every page: the lines are re-broken so the pages
  fill, as LilyPond's `systems-per-page` does. `minSystemsPerPage` / `maxSystemsPerPage` are a
  floor (not binding the last page) and a cap. One block may not write `systemsPerPage` beside
  either of the others; a later block (a score's, a `--set`) writing one clears the other.
  More systems than a page can hold are pressed together on it, with a warning.
- `measuresPerSystem 4` — exactly 4 bars on every system (the lead-sheet layout); written
  `break` / `pageBreak` give way, and a pickup counts as a bar. Lily#-own (LilyPond's spelling
  is a `\break` every N bars). Bars that cannot fit run past the margin, with a warning.
- `shortestDurationSpace 2` — the space the score's shortest note gets, in spacing increments
  (LilyPond's `SpacingSpanner.shortest-duration-space`); no unit.

There is deliberately **no algorithm switch** (line/page-breaking strategy is engine
tuning, not a dimension of the picture).

Unknown keys are an error, a key set twice in one block is a warning and the last wins,
and a second unnamed `paper { }` block warns like every repeated global setting.

**A named block is per-score**, the same shape as a named fonts block: declare
`paper wide { paperWidth 250mm }` at the top level, reference it as `paper wide` inside
a score, or override part of it there (`paper wide { topMargin 12mm }`). The reference
replaces the file's unnamed default for that score; a spacing block's unwritten lines
keep the named block's values. One file can then carry a wide conductor page and
default part pages.

## Grace Notes

```
grace { d16 e } f4          // Grace notes before F
acciaccatura { a16 } b4     // Slashed grace (takes no time)
appoggiatura { c8 } d4      // Unslashed grace
```

**A grace body is parsed as a full music block, and engraved much more narrowly than that.**
The engraver reads a **bare note's pitch and its duration VALUE** out of the body and nothing
else. Everything else written inside is not drawn, and Lily# says so at what was written
(LYS4020):

```
grace { d16@staccato } c4     // the staccato is not drawn      (LYS4020)
grace { d16. } c4             // the dot is not drawn           (LYS4020)
grace { d16 r16 } c4          // the rest is not drawn          (LYS4020)
grace { <d f>16 } c4          // no grace at all is drawn       (LYS4020)
```

**Two annotations ARE carried, and the line between them and the rest is whether they want a
column of their own on the page:**

* `grace { d16@mark("A") } c4` — the rehearsal mark. It is not the note's mark: its grob
  belongs to the bar (LilyPond consists `Mark_engraver` in the `Score` context), so a grace
  note never had to carry it.
* `grace { a,16\2 } b,8` — the string number. It is not drawn at all; it is what the tab's
  fret resolver reads, so a grace note on a `tab` staff takes the string you asked for.

A grace body with at least one bare note keeps its grace; only the parts of the body that are
not bare notes go missing.

**A slur IS drawn wherever it is written in grace time**, as in LilyPond: inside the body
(`grace { d16( e16) } c4`), from a grace note to the main note (`grace { d16( } c4)`), or from
a main note into the body (`b4( grace { d16) } c4`). One that starts in the body bends down,
unless the voice fixes the side (`voice { … } { … }`).

LilyPond draws all of the spellings above, so each warning says "not drawn yet", not "do not
write this".

## Tuplets

```
tuplet 3/2 { c8 d e }       // Triplet: 3 eighth notes in time of 2
tuplet 5/4 { c16 d e f g }  // Quintuplet
```

Nested tuplets are supported:

```
tuplet 3/2 {
  c8 d tuplet 3/2 { e16 f g } |
}
```

## Cue Notes

A cue quotes another instrument in small type. It is a **region**, not a mark on a note —
there is no `@cue` — because that is what it is in LilyPond too: `cue { … }` becomes a
`CueVoice` context, whose size is a property of the context and not of any note in it.

```
c4 d cue { e4 f } g4 |      // The two notes inside are cue-sized
c4 d cue bass { e4 f } g4 | // Read in the quoted instrument's clef
```

Naming a clef writes it before the region and restores the staff's own clef after it, so
the following notes are unaffected. Any clef name works: `treble`, `bass`, `alto`, `tenor`,
`treble_8`.

A cue is a **voice of its own**, which decides what may cross its edge:

```
c4 cue { e4( f) } g4 |      // A slur closing inside the cue
c4( cue { e4 f } g4) |      // A slur passing OVER the cue - both ends outside it
```

A slur or a tie with one end inside the cue and the other outside is rejected (**LYS4012**).
LilyPond cannot engrave such a span at all — it drops it, in one direction without even a
warning — so close the span inside the region, or move the note it reaches for out of it.
The same applies between **two cue regions written side by side** — `cue { … } cue { … }` is
two voices, not one — so `c4 cue { e4( f } cue { g4) }` is rejected for the same reason, even
though both ends of that slur are cue notes.

Two other shapes are closed while the feature is young: a `cue` nested in a `cue`
(**LYS4013**) and a `voice { … } voice { … }` span inside one (**LYS4014**).

## Repeats

### Volta Repeats

Volta repeats are written symbolically with `|: … :|` repeat barlines and volta
endings `[1. Section] [2. Section]` — **in the `form`, never in the music**
(**LYS1034**, user decision 2026-08-31). A repeat changes the ORDER the music plays
in, and a book's order is written in its form; that is the whole line the rule draws.
With endings, **the numbers are the passes**: the repeat plays as many passes as the highest
number, and each pass from 1 up plays the ending that names it — `|: A [1-2. B] :| [3. C]` is
A B A B A C. A pass no ending names, or two name, is an error (**LYS1043**), and so is a count
beside endings (`|: A [1. B] :|*3 [2. C]`, **LYS1042**) — write the pass as a number instead
(owner's decision 2026-09-30). Without endings the body plays twice, or `|: A :|*N` N times.

```
part m { clef treble }
section A { m { c4 d e f | } }
section B { m { g2 g | } }
section C { m { a2 a | } }
form { |: A [1. ~B] :| [2. ~C] }
score { staff m }
```

An ending NAMES a section — the music lives in the section, and the bracket goes round
the reference. It may name several, played in order under one bracket:
`|: A [1. B C] :| [2. D]` plays A B C, then A D. Each is written as in the form body
(`[1. ~B C']` — `~` hides that play's label, a trailing mark shifts its octave). Endings
accept ranges and lists: `[1-2. B]`, `[1,3. B]`, `[1,3,5. B]` (a list as long as you like; a
range is two numbers). The bracket prints its passes as LilyPond
does — `[1-2.` prints "1. 2.", `[1-3.` "1.–3.", `[1,3.` "1. 3." (a run of three or more
passes as a range). The passes are written with `,` or `-` only — `[1.3. B]`, the printed
points, is an error (**LYS0037**) that names both spellings. The first ending is the last thing before
the `:|`; a third and later ending is written by repeating the same shape: `:| [3. D]`.

**Where an ending ends, how its bracket ends, and how far the bracket reaches** are three
separate settings (2026-09-28):

- **Range** — the `]` ends the ending. It may be left off only right before a `:|`:
  `|: A [1. B C :| [2. D]` — the `:|` closes it. Anywhere else a missing `]` is an ordinary
  syntax error ("Expected 'CloseBracket'"); an unclosed *last* ending is no longer accepted.
- **End shape** — `]` hooks the bracket's right end down ("the ending ends here"); `-]` leaves
  it straight, the open look: `|: A [1. B] :| [2. C D -]`. An ending its `:|` closes hooks.
- **Length** — `layout { voltaBracket all | line | N }` (see *Display switches*), or one
  ending's own `@voltaBracket(…)` glued to its `]` / `-]`, which wins:
  `|: A [1. B C]@voltaBracket(2) :| [2. D]`. `all` (the default) covers every bar of the
  ending, `line` stops at the end of the system the bracket starts in, and `N` covers the
  ending's first N bars (all of it when the ending is shorter; across a system break when N
  reaches past it). **A bracket cut short always ends straight**, whatever `]` / `-]` says.

| Written | Bracket |
|---|---|
| `[2. C D]` | over C D, right end hooked |
| `[2. C D -]` | over C D, right end straight |
| `[1. B C :\|` | over B C, hooked (the `:\|` closes it) |
| `[1. B C]@voltaBracket(1)` | over B's first bar only, straight |
| `[2. D]@voltaBracket(line)` | up to the end of the line it starts on (then straight) |

The page, the `.ly` twin (`VoltaBracket.edge-height` / `musical-length`) and MusicXML
(`<ending type="stop">` for a hook, `"discontinue"` for a straight end or at the cut) all
follow; MIDI is unaffected. A MusicXML file carries no system breaks, so `line` writes the
whole ending there.

A repeat needs music every pass plays: `|: [1. B] :| [2. C]` (nothing before the first
ending), `|: :|` and an empty run after a `:|:` are errors (**LYS1041**). Without endings, a bare `|: A :|` simply repeats the body (twice by
default, or `|: A :|*N` times).

An ending needs a repeat to be an ending *of*. An ending that no repeat opens —
`form { A [1. B] }` — draws no bracket and no number: it engraves as the plain
reference `B`, played once, and **LYS6008** warns that the `1.` prints nothing.
This is LilyPond's behaviour for the same shape. Note that it is the *tree* that
decides, not the reading order: in `|: A [1. D] :| [2. O]` the ending written after the
`:|` still belongs to that repeat, while in `|: A :| B [1. B]` it does not.

> Note: `repeat volta` / `alternative` are LilyPond's spellings, **not** Lily#'s — the
> parser refuses `repeat volta` with a hint to the symbolic form above, and `alternative`
> is an ordinary word. The `repeat` keyword is for `unfold` / `percent` / `tremolo` (see
> below), which stay in the music because they abbreviate notes rather than reorder them.

#### One-sided repeat barlines

The two halves are **not** symmetric.

- A `:|` with no `|:` open **repeats from the beginning of the piece** — the
  ordinary reading of a one-sided end-repeat. It is not an error.
- A `|:` that no `:|` ever closes **is an error** (LYS4017): where the repeat ends
  is undefined. A form's own `|: … :|` block is closed by the parser, so what is left
  to reach this is a bare `:|:` standing in a form body — one written divider that
  means `:|` then `|:`.

> ⚠️ **The cross-layer pair is gone.** Until 2026-08-31 a `|:` written in a section's
> music could be closed by a `:|` the `form` wrote, and books in the tree were spelled
> that way; the reverse never worked, because a form repeat is bracketed and closes in
> the form. That one-sidedness is what made the ban worth doing — an author moving
> structure into the form always hit the direction that does not work first.

A repeat barline belongs to the **score**, not to one part: it is drawn on every staff,
and since the only place to write one is the form, it is now **played** on every staff
too. (Written in the music it expanded only the part it stood in — 8 notes over 4 in a
two-part book, measured — so the picture and the sound could disagree.)

> ⚠️ One gap, stated so nothing looks decided that is not: the LilyPond twin cannot
> express "repeat from the beginning" at all (`\bar ":|."` only draws the barline) —
> `lysc ly` warns when it emits one.

### Percent Repeats

Use the percent repeat syntax to repeat the previous measure:

```
c4 d e f |
repeat percent 2 { c4 d e f | }
```

## Beaming

Beams are automatic for eighth notes and shorter. Manual beam control:

```
c8[ d e f]    // Beam these four notes together
c8[ d r e]    // A manual beam runs over a rest
r8[ c d e]    // …and may open (or close) on one
```

A rest a manual beam covers keeps its place under the beam: it draws no stem, but the beam
reaches it, so `r8[ c d e]` beams from the rest. Automatic beams still end at every rest —
only a written bracket spans one.

## Stem Direction

A stem points up or down automatically from the note's staff position. Force it with
`@stemUp` / `@stemDown`:

```
c''4@stemUp d''4@stemDown e''4   // first up, second down, third automatic
```

On a beamed note the beam's shared direction wins (a beam carries one direction for the
whole group).

## Parallel Voices (Multi-Voice)

```
voice { c'2 d } { e2 f }
```

```
voice sop { c'2 d } alt { e2 f }     // named — binds lyrics sop / lyrics alt
```

`voice` opens the span **once**; each `{ … }` after it is one simultaneous voice on the
same staff. Repeating the keyword (`voice { … } voice { … }`) is an error (LYS0019): it
would open a *second* span, and two one-voice spans play one after the other rather than
together. A single voice is transparent and warns (LYS4011) unless it is named — a name
is what a `lyrics NAME { … }` block binds to.

(The LilyPond `<< … \\ … >>` form is **not** Lily# — the parser rejects it with a hint.)

## Named Music (Phrases)

Named music is declared with the `phrase` keyword. (The earlier `name = { … }`
and `let name = …` forms have been removed — the parser rejects them with a hint
to use `phrase`.)

### Phrase Declaration

```
phrase theme {
  c4 d e f | g2 g |
}
```

### Phrase Reference

```
theme              // Insert the phrase's music here (bare name)
```

## Sections and Parts

### Part Declaration

Header attributes are written bare (no colon), the same as the top-level
`clef` / `key` / `time` / `tempo` commands:

```
part rightHand {
  clef treble
}

part leftHand {
  clef bass
  octave 3        // a clef only draws; octave N (or an instrument preset) sets the register
}
```

### One part, no names

A piece with one part may leave the part, its chords and its lyrics unnamed — the score
then names them with the bare keyword:

```
part {
  clef treble
  section A { c'4 d e f | g2 g | }
}
chords { section A { C | G } }
lyrics { section A { Twin- kle twin- kle | lit- tle | } }

form { A }

score {
  chords
  staff
  lyrics
}
```

An unnamed `part` must be the file's only part, and an unnamed `lyrics` sings it unless it
says `sings`. With no `part` at all, music written straight into a section is the one
part's, and a bare `staff` renders it. A bare word right after `staff`, `tab`, `chords` or
`lyrics` is always a name.

### Section with Parts

```
section Main {
  rightHand { c4 d e f | }
  leftHand { c2 c | }
}
```

### Section-level key, meter, tempo, and pickup

A section may state its own `key`, `time`, `tempo`, or `partial` at the top of its body.
These apply to the **whole section** — they print on every part of it, and revert to the
score level at the next section (tempo persists):

```
section A {
  key g major
  time 3/4
  melody { g4 a b | }
}
```

In a file **grouped by part**, where each part holds its own inner sections, a section's key/
meter/tempo can be stated once in a standalone **header** — a `section` block with only
those settings — placed alongside the `part` blocks:

```
part melody { section A { c4 c g' g | } }
part bass   { section A { c2 e | } }
section A { key g major }       // applies to every part playing A
```

The editor command **Regroup (by part ⇄ by section)** rewrites a file from one grouping to
the other.

**Splitting the other parts to match.** When one part has cut a section into several
(`part vn1 { section A { … 16 bars } section B { … 121 bars } }`) while the others still write
the whole passage in one `section A`, the section is not the same length everywhere
(LYS2007). The editor command **Split Sections to Match a Part** — also offered as that
warning's quick fix — cuts the other parts' `A`, and the section's chord rows and lyrics
tracks, at the same bars into the same sections, and every form plays `A B` where it played
`A` (`|: A :|` becomes `|: A B :|`). When two parts subdivide the section differently, it asks
which one to follow. It does not stop at that section: a part that still holds several of the
followed part's sections in one (the double bass writing all of `A`…`H` in `A` while the others
write `B`…`H`) is cut too, whichever section's warning it was started from, and the whole plan
is refused if any part of it is — a split that would leave a section long in one part is never
offered. Whatever is still not the same length afterwards is named in the plan. A section boundary resets the relative frame, the note value, the meter,
the key and the clef, so at each cut the new section's first note is given the octave marks
and the note value that keep it where it was, and the meter / key / clef in force are
restated. The rewrite is checked before it is offered — every part it cuts must sound exactly
as before (the MIDI, part by part) and write as many bars, and the warning must be gone — and
applied as one edit. A slur, phrasing slur, tie or hairpin running across a cut is kept as
written: every form plays the new section right after the one it was cut from, so the span is
carried into it ([Across a section boundary](#across-a-section-boundary)) — unless it is still
open at the end of that new section, which is reported. A manual beam, a pedal or any other
span running across a cut, a cut that falls mid-bar, or the section played as a repeat ending
(`[1. A]`) is reported instead, with where. It works on files grouped by part; regroup a file grouped by section first.

**Those four are the whole list.** A setting that belongs to ONE part — `clef`,
`instrument`, `transpose`, `octave` — is refused beside a section's part cells
(**LYS1035** for `clef` / `octave`, **LYS0030** for the other two), because written there it
belongs to no cell and nothing reads it. Put it on the part, or — for a clef — in the
music where the change happens:

```
part melody { clef bass }                       // the part's clef
section A { melody { clef bass c4 d e f | } }   // a change mid-piece
```

⚠️ **The position is the rule, not the keyword.** Where a section's body *is* a music
stream, the same `clef` is ordinary music and engraves: `part m { section A { clef bass c4 … } }`
(grouped by part) and `section A { clef bass c4 … }` (a single-part piece writing bare music) are
both correct. Only a section holding *cells* has nowhere to put a loose one.

### Structure (Playback Order)

```
form { Intro Main Main Coda }
```

A reused section prints the same section mark each time. Give an
occurrence its own display label with a string after the name; an empty
string suppresses the mark (like `~Name`):

```
form { Intro Main Main "Main (reprise)" Coda }
```

A label that every play of a section prints — text with spaces or a `'`, say — is written
once on the section's top-level declaration, so several forms need not repeat it. In a file
grouped by part the declaration's body may be empty:

```
section A2 "A'" { }

part p1 { section A2 { c d e r } }
part p2 { section A2 { e f g r } }

form { A2 }              // prints A'
form other { A2 "A''" }  // the reference's own label wins: prints A''
```

Only a top-level section takes a label: one on a section inside a part, a lyrics track or a
chords track is an error (**LYS0039**), because every part plays the same section. Two
top-level declarations that label one section differently warn (**LYS1045**), and the first
one wins.

**Hiding a label.** A section prints its name as a rehearsal label, and a `~` on the
form reference hides it for that play. A section that only carries STRUCTURE — one cut
to hold a repeat edge, say — is referenced with the tilde:

```
form { A |: B [1. ~B1] :| [2. ~B2] C }
```

The tilde belongs to the reference, never to the declaration: `section ~B1 { … }` is an
error (LYS0033). A label written on a `~` play is reported (LYS0012).

**Octave marks on a reference.** A section boundary reopens the relative frame at the
part's anchor (and reverts the octave mode), so a section written for one register plays
in that register wherever it is quoted. A trailing `'` or `,` on the REFERENCE moves the
frame that play opens in — one octave per mark, the same spelling a phrase reference
carries:

```
form { Intro Main ~Main' Coda }        // the reprise sounds an octave higher
```

An ending takes them too:

```
form { |: A [1. B' ] :| [2. C ] }
```

The shift belongs to the occurrence, never to the declaration: `~Main ~Main'` is one
section played at two octaves, and the reference after it is back at the part's anchor.
Both spellings take the marks (`Main'` and `~Main'` — the tilde hides the label, not the
music), and they mean the same thing under `octave absolute`, where they move the base a
bare letter is measured from.

Identifiers (sections, parts, phrases) may use any Unicode letters:

```
section イントロ { メロディ { 動機 } }
form { イントロ イントロ "イントロ(再現)" }
```

### Navigation marks

The form may carry repeat-navigation marks between sections. The *signs*
`segno` and `coda` engrave at the start of the following section (the jump
target); the *text* directives `fine`, `to coda`, `dc`/`ds` (optionally
`dc al fine`, `ds al coda`, …) engrave at the end of the section just played:

```
form {
  A segno
  B  to coda
  C  ds al coda
  coda  D
}
```

The MIDI **follows** a form's jump texts, the way a player reads them: `dc` goes back to the
beginning, `ds` to the item after the last `segno` before it; `al fine` plays to the first
`fine` in the replayed stretch and ends the piece there (nothing written after the jump
sounds); `al coda` plays to the first `to coda` in it, then goes on from the `coda` sign after
the jump; a bare `dc` / `ds` plays up to the jump and goes on after it. On the replayed stretch
a repeat block plays once, as its last pass (`|: A [1. B] :| [2. C]` replays A C), and a
one-sided `:|` rewinds nothing. The form above plays A B C B D. A `ds` with no `segno` before
it, and a mark written in the music or inside a `|: … :|` block, are drawn and not followed.
The page draws the marks where they stand, MusicXML writes each with its `<sound>` attribute,
and the LilyPond twin writes `\jump`, whose MIDI does not follow it.

## Render Block

Controls output layout. Each `staff partName` names the part to draw (a bare
name, no braces); the clef comes from the part declaration, not the render block.

A score's name is optional. The unnamed `score { … }` is the file's default: it writes to
the input file's name (`song.lys` → `song.svg`), and the preview shows it first, listed as
**(Default)** in its score picker. `score another { … }` writes `song-another.svg` and is
`another` in the picker and in `lysc --score`. A file has at most one unnamed score.

```
score {
  grandStaff {
    staff rightHand
    staff leftHand
  }
}

score right {
  staff rightHand
}
```

A score's name is a bare word, like a part's or a form's. `score "tab"` is an error
(**LYS0038**) that names the word to write instead — a quoted string in Lily# is text
printed on the page, and a name with spaces or hyphens is written in one word
(`score guitarChart`).

### Staff groups — `grandStaff`, `staffGroup`, `choirStaff`

Three ways to engrave several staves as one group. All three take `staff` items and
nothing else, and they differ only in what is drawn down the left edge:

| | left edge | bar lines | reads as |
|---|---|---|---|
| `grandStaff` | brace | drawn **through** the gap | one instrument on two staves (piano, harp) |
| `staffGroup` | bracket | drawn **through** the gap | one family (the woodwinds) |
| `choirStaff` | bracket | **not** drawn through — each staff keeps its own | independent lines (voices) |

```
score satb {
  choirStaff { staff sop  staff alt  staff ten  staff bas }
}

score winds {
  staffGroup { staff flute  staff oboe  staff clarinet }
}
```

Each is the LilyPond context of the same name, so a `.ly` export of the three is
`\new GrandStaff` / `\new StaffGroup` / `\new ChoirStaff`.

⚠️ `staffGroup` reads in the other order on purpose, and is **not** a slip for
`groupStaff`. The other four `…Staff` items in a score body each *produce* a staff
(`condensedStaff` and `combinedStaff` put several parts on one) or are the established
name of one; a staff group produces a **group of staves**, and says so. LilyPond spells
its own contexts the same way for the same reason: of its seventeen staff contexts,
`StaffGroup` is the only one that is not a musical term.

### This score's own header, and parts that only play

A `title` / `subtitle` / `composer` / `poet` inside a score restates the file's metadata for **that score
alone** — a part extract can be headed with the part's name while the full score keeps
the work's title. A **bare part name** renders that part to MIDI only: played, never
engraved, which is how a click track or a cue part rides along without appearing on the
page.

```
score winds {
  staffGroup { staff flute  staff oboe }
  title "Woodwinds"    // this score only
  click                // played, never engraved
}
```

A staff's display name is a quoted string (`staff flute "Piccolo"`) — a bare word after
`staff NAME` is always another score item (`staff flute click` is flute's staff plus the
`click` MIDI-only part), so position never changes what a word means. And a score of
nothing but bare names has nothing to engrave, which is the empty-body error.

### This score's own page and faces

A score may write its own `fonts` / `paper` / `layout` block, which overrides the file's
unnamed default for that score alone — or reference a **named** block (declared at the
top level — see those sections) and override part of it in place:

```
fonts { music "Bravura" }                           // the file's default
paper wide  { paperWidth 250mm }
fonts house { serif "Georgia"  lyrics "Charis SIL" }

score       { paper wide  fonts house  staff melody }       // the conductor page
score parts { paper wide { topMargin 12mm }  staff melody } // same paper, wider top
score sketch { fonts { music "Petaluma" }  staff melody }   // the default, in Petaluma
```

A bare block reads as if its entries were written at the end of the file's unnamed
default. A reference replaces the default for that score alone; its override block reads
as if its entries were written at the end of the named block.

The display switches work the same way — `layout { barNumbers every 4 }` in a score, or
`layout chart` naming a top-level `layout chart { markTempo beside  barNumbers every 4 }`
block (see *Display switches* under Music Marks).

### Which form a score plays (excerpts)

The unnamed `form { … }` is the file's default: every score that picks no form plays
it (and with no form at all the sections play in the order they are declared). A score
picks another with a `form` item — a named top-level form, or its own written in place:

```
form          { Intro Verse Outro }   // the default
form practice { Intro }

score              { staff melody }   // → song.svg, plays the default
score practice     { form practice  staff melody }        // → song-practice.svg
score reprise      { form { Verse Verse Outro }  staff melody }  // its own form
```

`form A` is always a **reference** to a form named `A` — to play the section `A` alone,
write `form { A }`. MIDI plays the form of the score it is made from: the preview plays
the score it shows, and `lysc midi` writes one file per score (`--score NAME` picks one).

## Override/Revert

Modify engraving properties. The vocabulary is four properties —
`NoteHead.transparent`, `Stem.transparent`, `NoteHead.color`, `Stem.color`. The syntax
accepts any `Grob.property`, but anything outside that list is refused (LYS1029, "not
supported in this version") rather than silently doing nothing; the list grows, and each
addition removes one error. All four take effect (`NoteColumn.force-hshift` left the
vocabulary 2026-08-23 — its implementation is disabled for the initial release, so it is
refused honestly until the per-voice implementation lands).

```
override NoteHead.color = red    // named colour, or a "#rrggbb" string
c4 d e f |
revert NoteHead.color

once override Stem.transparent = true
c4 d e f |       // 'once' applies to the next note only
```

## Lyrics

Lyrics bind to their **default melody at the definition** — `lyrics NAME sings PART`
— and the score places them, by ORDER: a `lyrics NAME` row directly below
the staff engraving PART is that staff's verse (a run of rows stacks as
verses); anywhere else it draws **only the words, at the melody's rhythm**,
without engraving the melody — a part sheet carrying the chorus words. A score
row may name **its own** melody — `lyrics NAME sings OTHER` — which overrides
the default for that placement: one verse written once and placed under every
staff of a chorale (`staff alt  lyrics verse sings alt`). Several tracks may
sing the same part (Japanese and English words, a parody). A track whose name
matches the part (or one of its voices) is bound by the name alone; a track
with no binding is the even-spread lead-sheet row.

```
part melody
section Main {
  melody { c4 d e f | g2 g | }
  lyrics words sings melody {
    Hap- py birth- day |
    to you |
  }
}
form { Main }
score { staff melody  lyrics words }
```

Barlines in a lyrics block follow the music rule: every written `|` closes one
bar, the one that OPENS the run included — so `| きら | ひかる |` is one bar
longer than `きら | ひかる`, its first bar carrying no syllables. That leading
`|` is how a verse skips the rest bar the melody opens with.

**Melismas** follow LilyPond's `\lyricsto`. A **slur** or a **tie** makes one on its own:
the syllable on the slur's first note is sung over every note up to the one that closes
it (and a tied note takes no syllable), so the next syllable lands after the slur.
`__` draws the extender line and takes **no** note; `_` takes one note without a
syllable, holding the previous one over it.

```
part melody
section Main {
  melody { c4( d e) f | c4 d e f | }
  lyrics words sings melody {
    la __ lu |       // lu on f
    la _ lu li |     // la on c, d held, lu on e, li on f
  }
}
form { Main }
score { staff melody  lyrics words }
```

**Verses** — different words for each pass of a section — are written as a verse header,
`[1. syll syll | … ]`: the number names the pass (a play of the section in the form), and
prints as the stanza number `1.` before the words. A list or a range covers several passes
(`[1,3. …]`, `[1-2. …]`); a leading `~` keeps the words and hides the number (`[~1. …]`).
Under a `|: B :|` repeat the section is printed once, so its
verses stack under it as verses 1 and 2; a section the form plays twice written out takes
each verse at its own play.

```
part melody
section A { melody { c4 d e f | } }
section B {
  melody { g4 a b c | }
  lyrics words sings melody {
    [1. up up up up |]
    [2. down down down down |]
  }
}
form { A |: B :| }
score { staff melody  lyrics words }
```

## Music Marks

### Rehearsal Marks

```
c4@mark("A") d e f |
```

A `@mark` is the score's, not the note's: it stands at the bar its note is in, whichever
note carries it. One mark is engraved at a bar, so a `@mark` written at the bar a `form`
section's label opens is not printed — the label is — and the mark warns (LYS4021), as
LilyPond keeps the first `\mark` of a moment and discards the second. Drop the `@mark`, or
hide the label with `~Name` in the form (or `layout { sectionLabels none }`).

### Navigation Marks

A navigation mark is **bare** and **form-only**: it is written between the section names of
a `form`, never in the music (LYS1034, the same rule as the repeat barlines — user decision
2026-10-04) and never as a note modifier (`c4@segno` is LYS1022). The route a jump takes is
read off the form alone, so a mark in the music was drawn and never followed; a landmark that
falls inside a section is written by cutting the section there (`~` hides the label). A mark
stands at the section boundary it is written at — a text (`fine`, `dc`, `ds`, `to coda`, the
`al` forms) hangs to the bar's left, at the end of the section just played; a sign (`segno`,
`coda`) sits to its right, at the start of the next — as LilyPond aligns `JumpScript` and
`SegnoMark`/`CodaMark`. A text after the last section is drawn at the final bar.

```
form { segno A B to coda C ds al coda coda D fine }
```

The words are how a `form` names the route: `form { A segno B to coda C ds al
coda coda D }`. A jump text in a form whose landmark is missing warns (LYS4025) and says what
the MIDI does instead: a `ds` with no `segno` before it is not followed, an `al fine` with no
`fine` on the replayed stretch replays to the jump and ends there, an `al coda` with no
`to coda` on it replays to the jump, and one with no `coda` after the jump resumes right
after it.

### Display switches — `layout { }`

```
layout {
  markTempo stacked        // the default: LilyPond's arrangement
  barNumbers lines         // the default: a number at the start of every line but the first
  accidentals default      // the default: the 18th-century style
  sectionLabels boxed      // the default: the section's name in a frame
  partCombineText true     // the default: a combinedStaff prints a2 / Solo
  chordQualities symbols   // the default: C°, C+, Cø, C°7 — LilyPond's own
  minorChords upper        // the default: Am, Am7
  chordDiagrams guitar     // written chord shapes draw as guitar diagrams (unset: the part's instrument)
                           // (`chordDiagrams guitar all` / `chordDiagrams all`: EVERY chord draws one)
  voltaBracket all         // the default: an ending's bracket covers every bar of it
  Stem.thickness 1.3       // the default: the line thicknesses and stem length (below)
}
layout chart {
  markTempo beside         // the chart's: "[Chorus] ♩ = 132" on one line
  barNumbers every 4       // a number on every fourth bar, wherever it stands
  accidentals modern       // Kurt Stone's: cancelled in other octaves and the next measure
  sectionLabels plain      // the name with no frame (LilyPond's own picture)
  partCombineText false    // no a2 / Solo words
  chordQualities words     // Cdim, Caug, Cm7♭5, Cdim7 — spelled out
  minorChords lower        // a, a7 — a lowercase root, no m
  chordDiagrams none       // no chord diagrams at all, even for written shapes
}
score  { staff melody }                 // the file's default
score parts { layout chart  staff melody }   // this score's own
```

The score-wide **display switches**: closed vocabularies that pick one of a few drawings,
with no unit and no grob scope. The block takes the two tiers `fonts` / `paper` take — one
unnamed block per file is the default, a named block is a per-score declaration a score
references (and may override in part: `layout chart { barNumbers none }` inside the score).
It is not an `override` (which reads a `once` / section scope a whole-score switch would
silently ignore) and not `paper` (a quantity with a unit — a length, `raggedRight` — is the
page's and stays there).

**The line thicknesses and the stem length** (2026-10-05) — the block's one family of
NUMBERS: how the score is engraved rather than which drawing it picks, score-wide like
LilyPond's `\layout { \context { \Score \override … } }`. The dotted keys name a grob and
its property, glued (`Stem.thickness`, not `Stem . thickness`); `lysc --set` takes the same
keys (`--set Stem.thickness=1.5`, and `LedgerLine.thickness=1.0,0.1` with a comma).

| Key | Default | Means |
|-----|---------|-------|
| `lineThickness` | 0.1 | the line every other one is a multiple of, in staff spaces |
| `StaffLine.thickness` | 1.0 | the staff's lines, in line thicknesses — and, as in LilyPond, every line stated in them follows: stems, ledger lines, ties, slurs, hairpins, brackets. The bar lines read `lineThickness` alone and stay |
| `LedgerLine.thickness` | 1.0 0.1 | ledger lines: staff line thicknesses, plus staff spaces |
| `LedgerLine.lengthFraction` | 0.25 | how far a ledger line reaches past its note head on each side, in that head's widths (a black head's ledger is 1.96 staff spaces long, at 0.4 it is 2.35). The notes are spaced as before, as in LilyPond |
| `Stem.thickness` | 1.3 | stems, in staff line thicknesses |
| `Stem.lengthFraction` | 1.0 | every stem's length, beamed or not, times this (a grace note's and a cue's keep their own) |
| `Beam.thickness` | 0.48 | beams, in staff spaces — the beams are placed for it, as LilyPond places them |
| `Beam.damping` | 1 | how much a beam's slope is flattened, as LilyPond's: the slope stays under 0.6 / this (staff spaces per staff space); 0 leaves it undamped, 10000 or more lays every beam flat |
| `Dots.padding` | one dot's width (0.45) | the gap between a note or rest and its first dot, in staff spaces; set, it is the same for a grace note's smaller dots. The notes make room for it, as in LilyPond |
| `Accidental.rightPadding` | 0.15 | the gap an accidental keeps from its note head beyond a fixed 0.2, in staff spaces (0.35 in all by default); chords' and two voices' accidentals too. The notes make room for it |
| `NoteHead.scale` | 1.0 | the note heads' size, a factor (1.15 is 15% larger). Everything that hangs on a head follows it, as in LilyPond: stems stand on its edge, ledger lines reach past it, dots, accidentals, ties and slurs keep their gaps from it, and the notes make room for it. A grace note's head keeps its own size; a cue's is both smaller and scaled. Stems, flags, beams, dots, accidentals and rests keep their size |
| `BarLine.thinThickness` | 1.9 | thin bar lines, in line thicknesses |
| `BarLine.thickThickness` | 6.0 | thick bar lines, in line thicknesses |

Each takes a positive number with no unit (`LedgerLine.thickness` two, not both 0;
`Beam.damping`, `Dots.padding` and `Accidental.rightPadding` may be 0). The
`.ly` twin writes them as those overrides (`\override Stem.thickness = #1.5`,
`line-thickness = 0.6\pt`; `NoteHead.scale 1.1` as
`\override NoteHead.font-size = #(magnification->font-size 1.1)`).

**`markTempo`** — a boxed section label (a `form` section's name, a `@mark`) and the metronome
mark standing at the **same bar** are arranged one of two ways. `stacked` is LilyPond's:
the label break-aligns to the key/clef column and the tempo to the meter column, each on
its own anchor, and where their inks meet the label stacks over the tempo. `beside` is the
chart's one line: the label's box stands at the line-start edge and the tempo sits to its
right, its digits on the label's own baseline — a Lily#-own arrangement, chosen as an
option (2026-09-02). A mid-line label is centred on its bar and a mid-measure `tempo` keeps
its note column under either. The `.ly` twin has no spelling for `beside` and warns; the
page is the reference. (The key names the pair it arranges, the mark and the tempo; the
font *group* that faces them is `fonts { marks "Georgia" }`.)

**`barNumbers`** — which bars carry a printed number, in LilyPond's own vocabulary.
`lines` (the default) is LilyPond's: the first bar of every line after the first. `none`
prints no numbers (`\remove Bar_number_engraver`). `every N` prints every bar whose number
is a multiple of N wherever it stands in the line, and **only** those — under `every 2` a
line opening on bar 3 opens with no number, exactly as LilyPond's
`every-nth-bar-number-visible` prints it. N is a whole number of at least 1 (`every 1`
numbers every bar, the first included). The twin writes the same LilyPond words into its
`\layout` block.

**`accidentals`** — which notes carry a printed accidental: LilyPond's `\accidentalStyle`
table, for the styles whose context is the staff.

| word | LilyPond | what it does |
|---|---|---|
| `default` | `default` | 18th-century: an alteration holds to the bar line, in its own octave |
| `modern` | `modern` | Kurt Stone's: also cancelled in other octaves and in the next measure, and no restore-natural |
| `modernCautionary` | `modern-cautionary` | `modern`, with the accidentals it **adds** printed in parentheses |
| `forget` | `forget` | nothing is remembered — every note is read against the key signature |
| `noReset` | `no-reset` | the bar line resets nothing; an accidental holds until overridden |

LilyPond's `voice`, `piano` and `choral` families name a Voice / GrandStaff / ChoirStaff
**context**, which a score-wide switch has no way to name, and its `neo-modern`,
`teaching` and dodecaphonic families need rules of a different kind — they are absent
rather than approximated, and a style the table does not hold is refused at the word. The
`.ly` twin writes `\accidentalStyle modern` at the head of each part's music, where the
function's own default context is the staff.

**`sectionLabels`** — how a `form` section's name is drawn. `boxed` (the default) is the
frame Lily# has always drawn, and it is Lily#-own: LilyPond's `SectionLabel` grob draws the
bare string, and the twin reaches Lily#'s picture by writing `\mark \markup \box`. `plain`
drops the frame and engraves the name alone — which **is** LilyPond's own picture — and the
twin then writes `\mark \markup` with no `\box`; the label narrows by the frame's padding on
both sides and sits on its own baseline, so a neighbouring symbol moves in with it. `none`
engraves no section names at all — the part sheet's answer — and the twin then writes no
`\mark` either. It is a **display** switch: the form still plays the section, and MIDI /
MusicXML are untouched.

**`partCombineText`** — whether a `combinedStaff` prints `a2` / `Solo` / `Solo II`. `true`
is the default and LilyPond's; `false` is its `printPartCombineTexts = ##f`, which the twin
writes. With the words off no text item is made at all, so nothing is drawn and nothing is
reserved. The **combining** is unchanged — this switch is the words, not the merge.

**`chordQualities`** — *not* the same question as `chords NAME as names|roman`. That clause
says **which quantity** a row shows (the absolute chord, or its degree in the key) and is
written per row, because one score writes both at once — `chords prog as roman` above
`chords prog as names` is how a track is shown two ways. This key says how the **quality**
of whatever the row shows is spelled, for the whole score. The two compose: a degrees row
is unchanged by it, since a Roman degree already spells those qualities its own way.

How a chord's **quality** is spelled after the root. `symbols` is the **default** and is
LilyPond's own picture: the four qualities its exception table names print `C°`, `C+`,
`Cø`, `C°7`, and a major seventh prints its drawn triangle. Every other quality keeps its
word, because LilyPond spells the rest with digits too. `words` spells the qualities out
instead — `Cdim`, `Caug`, `Cm7♭5`, `Cdim7`, `Cmaj7` — the lead-sheet convention, and what
every Lily# book printed before 2026-09-12.

**`minorChords`** — whether a chord with a **minor third** prints an uppercase root with
its `m` (`upper`, the default and LilyPond's) or a lowercase root with the `m` dropped
(`lower` = LilyPond's `chordNameLowercaseMinor`, which the twin writes on the `ChordNames`
context). The test is the third, so a diminished chord lowercases too and a `sus` chord
never does; the slash **bass** keeps its capital, as LilyPond's does. Measured with both
keys set, Lily# spells `c°` `C+` `cø` `c°7` `a7/C` — character for character what LilyPond
prints for the same chords, and with the default vocabulary the chord row lands on
LilyPond's own distance from the staff to the digit. Neither key reaches MIDI or MusicXML —
a `<harmony>` carries the chord as data, and asks for the words.

**`chordDiagrams`** — the tuning the score's **chord diagrams** draw on: a tuning word — the
words a tab's `tuning` takes (`guitar`, `ukulele`, `mandolin`, `guitardropd`, `bass`, …) — or
`none`. A diagram draws only where a chord WRITES its shape (`G(320003)`, `@chord(G 320003)`,
`@chord(x32010)`; *Chord Diagrams*); this key says which of the written shapes applies. Unset,
each diagram takes the instrument of its part (the part an `@chord`'s note is in, the staff a
row stands over), else the guitar. `none` draws no diagram at all, written shapes included —
so the same source makes a piano score and a guitar score. `@diagram(…)` is not a chord symbol
and always draws. The scope word **`all`**, after the tuning (`chordDiagrams guitar all`,
`chordDiagrams ukulele all`) or alone (`chordDiagrams all` — the tuning then resolved as when
unset), makes EVERY chord name draw a diagram: its written shape, else the usual one. `none all`
is refused (`none` draws nothing), as are `all guitar` (the tuning comes first) and a word
written twice. A **shape table** in braces may follow the words (`chordDiagrams guitar { Cm7
x35343  G  section Chorus { C x35553 } }`): the chords it lists draw a diagram wherever they are
named, with the shape written after each (a name alone: the usual shape), and a `section NAME
{ … }` block's entries apply in that section only — see *Listed chords* under *Chord
Diagrams*. `none` takes no table. **`capo N`** after the tuning word (before `all`) puts a capo
on fret N: the shapes are the shapes pressed above it, the names the pressed chords', and
"Capo N" stands at the score's head — see *A capo* under *Chord Diagrams*.

**`chordNames`** — what a chord name shows in a score with a capo: `shape` (the default) the
name of the shape the player presses (`C` for a sounding E♭ at capo 3), `sounding` the sounding
chord's name (`E♭`), `both` the sounding name with the pressed one in brackets (`E♭ (C)`).
Without a capo the three print the same name.

**`chordList`** — `true` puts the chords the score uses, each with its diagram, at the head
of the score under the title: every chord once in order of first appearance, in centred rows
of even counts — see *The chord list* under *Chord Diagrams*. `false` (the default) draws none.

**`voltaBracket`** — how far a form ending's volta bracket reaches: `all` (the default)
covers every bar of the ending; `line` stops at the end of the system the bracket starts in;
a whole number `N` covers the ending's first N bars (all of it when the ending is shorter,
continuing across a system break when N reaches past it). One ending overrides it with
`@voltaBracket(…)` after its `]`: `[1. B C]@voltaBracket(2)`. A bracket cut short always ends
straight — the hook means "the ending ends here" (see *Volta Repeats*). A bad value is the
ordinary layout error (`'Line' is not a value of 'voltaBracket'. Values are case-sensitive:
write 'line'.`); on an ending it is the ordinary annotation warning (LYS1008) and the layout's
value applies.

The keys are case-sensitive — `barnumbers` is refused with "write 'barNumbers'" — and so
are the value words. Neither
is a reserved word (`part markTempo { … }` compiles). An unknown key is an error, a key set
twice warns (the last wins), and a brace inside the block is refused.

### Text Spanners

A text spanner runs from its start to the `@!` that ends it, and **the end is required**:
a spanner nobody closes draws nothing at all — not its dashed line and not its word — and
says so (LYS4018). That is LilyPond's own answer (a `\startTextSpan` with no
`\stopTextSpan` is dropped, never shortened), and it is what makes the length on the page
always a length you wrote.

```
c4@rit d e f@!rit |                 // Ritardando over four notes
c4@accel d e f | g@!accel a b c |   // Accelerando over five
c4@rall d e f@!rall |               // Rallentando

c4@textSpan("poco rit.") d e f@!textSpan |   // any word you like
c4@textSpan d e f@!textSpan |                // no word: a bare dashed line
```

`@rit`, `@accel` and `@rall` are shorthand for `@textSpan("rit.")` and its siblings —
nothing else about them differs, so `@!rit` and `@!textSpan` are the same mark and either
closes either start. One spanner is open at a time in a voice, and a spanner does not
carry from one voice into another.

### Ottava Brackets

Like a text spanner, an ottava **must be closed** — an unclosed one draws no bracket at
all, and the notes under it are not transposed, and it says so (LYS4018). One
`@!ottava` closes whichever of the family was opened.

```
c4@ottava d e f@!ottava |            // 8va bracket
c4@ottava(bassa) d e f@!ottava |     // 8vb
c4@quindicesima d e f@!ottava |      // 15ma - the same terminator
```

⚠️ `@loco` is **retired**. It named a mark that printed nothing — writing it only moved
where the bracket stopped — and LilyPond has no `loco` command either (the word is in
its glossary and nowhere else). Write `@!ottava`.

### Pedal Markings

Each pedal is **one span**, opened by its name and closed by `@!` — the same rule as a text
spanner and an ottava. A pedal nobody releases draws nothing at all, and says so (LYS4018).

A **pedal change** — release and press again on the same note, LilyPond's
`\sustainOff\sustainOn` — is simply the pedal's name again while it is down: a second
`@sustain` releases and re-engages, and the bracket draws its notch there. The span stays
open until the one `@!sustain` that ends it. (Both marks on one note, `@!sustain@sustain`,
mean the same and engrave the same.)

The drawing is a part property: `part lh { clef bass pedal text }` picks "Ped. … *",
`pedal bracket` (what a part without the property draws) or `pedal mixed` (text at the
start, bracket for the hold).

```
c4@sustain d e f@!sustain |           // Sustain pedal
c4@sustain d e f | g4@sustain a b c@!sustain |   // pedal change on g
c4@sostenuto d@!sostenuto |           // Sostenuto pedal
c4@unaCorda d@!unaCorda |             // Una corda pedal
c4@unaCorda d@treCorde |              // the same release, written as the word it prints
```

### Trill Spanners

```
c4@startTrillSpan d e@stopTrillSpan f |
```

## Glissando

```
c4@glissando d |      // Glissando from C to D
```

## Arpeggio

```
<c e g>4@arpeggio     // Arpeggiate chord
```

## Figured Bass

```
c4@figuredBass(6) d@figuredBass(6 4) e@figuredBass(5 3) |
```

## Chord Names

Written as they print — an UPPERCASE root, `#`/`b`, a bare quality (`C`, `Am`,
`G7`, `F#m`, `Bb7/D`; altered tensions spell `+`/`-`: `Gm7-5`):

```
c4@chord(C) d@chord(Dm) e@chord(Em) f@chord(F) |
```

The symbol is the FIRST word of the argument: the words after it are the shapes of its chord
diagram (`@chord(Cm7 x3x546)`, see *Chord Diagrams*). A space therefore separates words —
`@chord(C 7)` is C with a one-character shape (a warning), not C7 (write `@chord(C7)`). A bare
`@chord` names the chord from the notes it sits on.

A chord symbol belongs to the beat, not to a note: it may sit on a rest, a spacer or a
multi-measure rest, and draws there exactly as on a note — the name at that moment, and its
diagram under it by the usual rules (*Chord Diagrams*). An intro over silence:

```
s1@chord(C) | s1@chord(G 320003) | r2@chord(Am) c'2 |
```

A bare `@chord` on a rest or a spacer has no notes to name, so it draws nothing and warns
(LYS1020): write the name.

In a `chords NAME { }` row the same symbols place themselves on the bar's beat
grid (no durations): one entry takes the bar, two in 4/4 are halves, and `.`
holds the previous chord one more beat:

```
chords prog { C | F G | C . . G7 | }
```

An entry may carry its chord diagram's shapes in parentheses glued to the symbol —
`F(133211)`, `F(133211 2010)`, `F(guitar 133211 ukulele 2010)`, `F/A(x03211)` — and only an
entry that writes one draws a diagram under its name (*Chord Diagrams*).

An entry may also be a **Roman degree of the key** at that bar, which is how a
progression is written once and follows the key:

```
chords prog { section A { Imaj7 | V7 | IIm7 | bVII | } }
```

In C that is `Cmaj7 G7 Dm7 B♭`; in E♭ the same source is `E♭maj7 B♭7 Fm7 D♭`. A degree is
an optional `b`/`#`, a numeral `I`–`VII`, the ordinary quality (`Imaj7`, `IIm7`, `V7`,
`VIIdim`, `Vaug`, `IIm7-5`) and an optional `/` bass written as a degree too (`V7/VII`).
Degrees and absolute names may not collide — a root is `A`–`G`, a numeral is `I` or `V` —
and both resolve to the same chord, so the written form and the displayed form stay
independent: a degree chart prints names by default and degrees under `as roman`.
⚠️ Use the ASCII `b`/`#`: the printed `♭ ♯ ° ø` are refused by the lexer, so write
`bVII` and `VIIdim`, not `♭VII` and `VII°`.

That fragment is the row's *contents*. Where it may sit depends on how the file is grouped:
inside the section whose bars it fills (`section A { chords prog { … } }`), or — in a
file grouped by part, where the parts carry their own sections — at the top level with the
sections named inside it (`chords prog { section A { … } }`). A **flat top-level track
in a file grouped by part is an error** (LYS2011 for chords, LYS4002 for lyrics): it has no
section to anchor to, so its bars would run from bar 0 across whatever the form plays,
and every section after the first would get nothing.

**In the MIDI**, a row the score places (`chords NAME` in the score) sounds on a track of its own, `NAME (chords)`, at 70% of the velocity in force; a row
no score places is silent. Each symbol sounds over exactly the span it prints over — `.`
holds it, `r` is silence, and every written symbol strikes again. A symbol names a chord
but voices none, so Lily# picks one voicing, the same everywhere: every tone takes its one
pitch from G3 up to (not including) G4, and a slash bass its pitch an octave below that —
`G7/B` sounds B2 G3 B3 D4 F4. Hovering the symbol in the editor lists those pitches.

## Chord Diagrams

`@diagram(…)` draws a guitar chord diagram (a fret diagram) over its note. The argument is a
position string, one character per string from the lowest to the highest: a digit is the
fret (`0` or `o` open), `x` a muted string — and, for frets 10–15, a `-` on each side of each
two-digit fret (`@diagram(xx-10-12-13-11)`, *A shape* below); 4 to 8 strings. Anything else is not a diagram:
it warns as an unknown annotation and is ignored.

```
c4@diagram(x32010) d@diagram(xx0232) e@diagram(022100).down f |   // C, D, E (the E below)
```

A diagram stands above its note whatever the stem; `.down` puts it below. The `.ly` twin
writes `\fret-diagram-terse`; MusicXML writes a `<frame>`, inside the `<harmony>` of an
`@chord(…)` on the same note. Its size is the `fonts` key `diagram` (see *Text Fonts*).
A shape that reaches past the 4th fret is drawn from its lowest fretted fret and labelled
`Nfr` (`x35343` is `3fr`), as LilyPond draws it.

### Diagrams under the chord names: a written shape

A chord draws a diagram UNDER its name only where its SHAPE is written — `G(320003)` in a
`chords` row, `@chord(G 320003)` on a note, or `@chord(x32010)` (the name derived from the
shape) — between the name and the staff, side by side, and the bars widen to fit them. A
name alone (`G`, `@chord(G)`, a bare `@chord`) draws no diagram — save in a score whose
layout writes `all` (below). The editor writes shapes for you: `Ctrl+Shift+Up` on a chord adds
its usual shape (below).

**Every chord: `chordDiagrams … all`.** In a score whose layout says `chordDiagrams all`
(or `chordDiagrams guitar all`, `chordDiagrams ukulele all`, …) EVERY chord name draws a diagram
— the `chords` rows' entries and every `@chord`, a bare `@chord` by the name it derives: the
shape written for the tuning when there is one, else the usual shape (below). A chord with no
shape at all on the tuning (C13 on the ukulele) draws none and warns once per chord and tuning
in the file (write its shape). Without `all`, only written shapes draw.

```
layout uke   { chordDiagrams ukulele }
layout piano { chordDiagrams none }
layout book  { chordDiagrams guitar all }
part melody { clef treble }
section A {
  melody { c'2@chord(Cm7) c'2@chord(Cm7 x3x546) | c'1 | }
  chords prog { C(x32010) F(133211 2010) | G(guitar 320003 ukulele 0232) Am | }
}
form { A }
score { chords prog  staff melody }                     // guitar diagrams: C F G, x3x546
score uke { layout uke  chords prog  staff melody }   // ukulele diagrams: F G
score piano { layout piano  chords prog  staff melody }  // none
score book { layout book  chords prog  staff melody }    // every chord: C F G Am, x35343 x3x546
```

**Listed chords: a shape table.** Between "only what is written" and "everything", the layout
can LIST the chords that draw: a table in braces after the `chordDiagrams` words —
`chordDiagrams guitar { Cm7 x35343  G  section Chorus { C x35553 } }`. Each entry is a chord
symbol and the shape it draws, written as a row writes them after its symbol (`F 133211 2010`,
`F guitar 133211 ukulele 2010` — several shapes route by string count or by a tuning word); a
name alone (`G`) draws the usual shape. A listed chord draws wherever it is named — in a
`chords` row, in an `@chord`, a bare `@chord` by the name it derives — and the shape it draws
is decided strongest first: the shape WRITTEN at the chord, the entry of the `section` the
chord is written in, the song's entry, then (in an `all` score) the usual shape. A
`section NAME { … }` block's entries apply to the chords written in that section — an
`@chord`'s note, a row's bar, a by-part row's inner `section NAME { }` — and the keyword keeps
a section named `A` or `C` from reading as a chord; a phrase outside every section takes the
song's entries alone. An entry whose shapes fit no tuning the score draws on is not used there
(a table can carry a guitar shape and a ukulele shape as a row can). The table follows `all`
(`chordDiagrams all { F xx3211 }`: the listed shape for F, the usual shape for every other
chord) or stands alone (`chordDiagrams { C }`, the tuning as when unset); `none` takes none.

```
layout { chordDiagrams guitar { C  F xx3211  section B { F 133211  G } } }
section A { melody { c'1@chord(F) | c'1@chord(G) | }  chords prog { C | F | } }   // F xx3211, G none; C x32010, F xx3211
section B { melody { c'1@chord(F) | c'1@chord(G) | }  chords prog { C | F | } }   // F 133211, G 320003; C x32010, F 133211
```

A symbol that is no chord, a shape that is none, a shape before any symbol (the row's LYS1038
words) and a chord listed twice in one scope (the last wins) are warnings and the rest of the
table stands; a section nothing declares warns (its entries apply nowhere); a table shape that
disagrees with its chord is LYS1039 (below). A brace where none belongs, a `section` with no
name or block and a word after the closing `}` refuse the whole `chordDiagrams` entry. A chord
listed by name alone that has no shape at all on the tuning (C13 on the ukulele) warns as in an
`all` score. The editor treats a listed chord as an `all` score treats every chord: the hover
shows the shape the name draws (`guitar: xx3211 (layout)`), and the step counts from it.

**A capo.** `layout { chordDiagrams guitar capo 3 }` puts a capo on the third fret. The music
still writes the SOUNDING chords — a row's `Eb`, `@chord(Eb)` — and everything a player
reads follows the capo:

- every shape is the shape PRESSED above the capo, drawn capo-relative: `Eb(x32010)` is the C
  shape; the usual shape of a chord is its pressed chord's (`Eb` at capo 3 draws `x32010`
  under `all`); the table's shapes are pressed shapes; a written shape is checked (LYS1039)
  as pressed above the capo — the warning speaks in sounding names ("'320003' sounds A C♯ E
  with the capo on fret 2, which is A, not G … write chord(A 320003), or chord(G 133211) for
  G under the capo") and offers the symbol's own pressed shape as the fix that keeps it;
- the printed NAME is the pressed chord's: `C` for `Eb`, `G` for `Bb`, spelled in the key that
  many semitones below the key at the bar (the key's own letter, else a natural, else the key's
  side of the accidental: in E major at capo 3, the pressed key is D♭, so a sounding `G#m`
  prints `Fm`; at the tritone the sharp side, F♯). `chordNames sounding` prints the sounding
  names instead, `chordNames both` both — `E♭m7 (Cm7)`, each name with its own raised quality.
  A Roman degree is the sounding key's and is not moved;
- "Capo 3" stands at the score's head, on the header's instrument line (the middle of the
  poet / composer row);
- a `chord(Eb x32010)` item's strings sound three semitones higher (E♭ major), on the staff,
  in the MIDI and in the twin.

`capo 0` is refused (leave the word out), as is a fret above 11, `none capo 3` and `all capo
3` (the capo comes first). The `.ly` twin writes the pressed chords into `\chordmode` (LilyPond
then prints the same names) and `instrument = "Capo 3"` in its `\header`; under `chordNames
sounding` it writes the sounding chords, and `both` it cannot spell (it names the sounding
chord and warns). MusicXML's `<harmony>` stays the sounding chord, its `<frame>` the pressed
shape.

**The chord list.** `layout { chordList true }` puts every chord the score names at its head,
under the title rows and above the first system — the songbook's "chords used" row: each
chord once, in order of first appearance across the `chords` rows and the `@chord`s (a bare
`@chord` by the name it derives, a degree by the chord it resolves to), as the name the score
prints (a capo's pressed name) over the diagram it draws there. A chord that draws no diagram
in the score shows its usual shape in the list — the list is where a shape is looked up — on
the layout's tuning, else the guitar; under `chordDiagrams none` the names stand alone. The
cells stand 3 staff spaces apart in the fewest rows that fit the line with as nearly equal
counts as those rows allow (16 chords where 12 would fit a row make two rows of 8), and each
row is centred on the page. The `.ly` twin writes the rows as `\markup` lines of
`\center-column { "NAME" \fret-diagram-terse … }` before the score, the names as plain text.

```
layout { chordDiagrams guitar  chordList true }
section A { melody { c'1 | c'1 | c'1 | }  chords prog { C | F(xx3211) | G | } }   // the head: C x32010, F xx3211, G 320003
```

**The capo suggestion.** After `capo ` the completion lists the frets 0 to 7 ranked by how
many of the file's chords would be played with a barre there (on their usual shapes; fewest
first — `capo 3: 0 barre chords of 3`, `capo 0: 2 barre chords of 3 (F 133211, Bb x13331)`),
and hovering `capo` or its fret shows the same ranking. Pick a fret and write it: there is no
`capo auto`.

**Which tuning.** A diagram draws on ONE tuning, strongest first:

1. the score's `layout { chordDiagrams TUNING }` (with or without `all`) — `none` draws no
   diagram at all, written shapes included;
2. else the instrument of the PART, when it is fretted (its `tuning`, else its `instrument`
   preset's — the tuning its tab would use: guitar, ukulele, mandolin, bass, banjo, …; the
   bowed strings, a piano, a voice fret nothing): for an `@chord`, the part whose note carries
   it; for a `chords` row, the staff the row stands directly above in the score (a tab staff:
   its own tuning);
3. else the guitar — a lead-sheet row with no staff under it, a staff that frets nothing.

There is no staff-level override; a score that needs another tuning says so in its `layout`.

**A shape** is one character per string from the LOW string: `x` muted, `o` or `0` open, a
digit the fret. For frets 10 to 15, put a `-` on each side of each two-digit fret:
`Cm(8xx88-11)`, `Cm(xx-10-12-13-11)`, `@chord(Cm 8-10-10-888)`, `@diagram(x-15-13-12-13-x)`.
A shape holding `-` is read in SEGMENTS, the parts between the dashes: a segment of exactly
two digits is ONE fret, 10–15; any other segment is one character per string. So the
chord-chart spelling with a `-` between every string reads the same (`x-x-10-12-13-11`,
`x-3-5-5-4-3` is `x35543`). ⚠️ A two-digit segment is always one fret: frets 10, 9, 9 are
`10-9-9` — `10-99` is fret 99 and warns, as does `09` (write `0-9`). Lower case only; a
leading, trailing or doubled `-` and a fret above 15 warn, naming the fix. An unnamed shape
goes to the tuning with as MANY STRINGS as it has — characters, a two-digit fret counting one
(`8xx88-11` is six) — `F(133211 2010)` and `F(1-3-3-2-1-1 2-0-1-0)` are the guitar's and
the ukulele's; a shape the diagram's tuning cannot take is simply not used. The editor writes
a shape one character per string when every fret is 9 or less, else with a `-` only around
the two-digit frets (and between two lone single digits: `10-9-9`). When a file uses two
tunings of one string count, a tuning word binds the next shape by name:
`F(guitar 133211 guitardropd 333211)` (the words of `chordDiagrams`) — it applies only where
that tuning is the diagram's. `@chord` takes the same words after its symbol:
`@chord(F guitar 133211 ukulele 2010)`. A written shape is checked against its chord symbol
(below, LYS1039).

- `@chord(x32010)` — a shape with no symbol — draws that shape and names the chord from its
  notes (C), on the diagram's tuning, else on the part's tuning.
- A bare `@chord` on a chord names it from its notes; it writes no shape, so draws no diagram
  (in an `all` score, the usual shape of the chord it names).
- `@chord` takes no `.up`/`.down`: the diagram is always above, under its name.
- `@diagram(x32010)` is not a chord symbol: it always draws, with no name.

**The usual shape** — what `Ctrl+Shift+Up` writes first on a chord with none, what the
hover offers, and what an `all` score draws for a name alone — is LilyPond's predefined shape for the tuning (its tables for the guitar: 136
chords and 17 ninth chords; the ukulele: 306; the mandolin: 204 — less nine, below — C `x32010`, F `133211`,
Cm7 `x35343`, ukulele C `0003`, F `2010`; a table serves only the tuning it was made for, and
holds no slash chord), else the first shape of Lily#'s order (below); without a stretch when
there is one (F11 in open G, `333047`, has only stretch shapes). A chord no rule can voice
(C13 on the ukulele's four strings) has no usual shape: write it.

**Warnings (LYS1038)** — about WRITTEN shapes; the shape is not used and the name still draws:
a shape of the wrong length (`@chord(C 7)`: one character, which no tuning has strings for; a
named shape whose tuning has another count), a word that is neither a shape nor a tuning word
(`@chord(C m7)`), two unnamed shapes of one length (name the tuning each is for), a tuning
given two shapes, a miswritten dash-separated shape (`x-x-16-12-13-11`, `x--3-5-5-4-3`, a
leading or trailing `-`, a two-digit segment that is no fret: `10-99-988`, `8xx88-09`), a shape in upper case (`X-3-5-5-4-3`: values are case-sensitive).
A symbol-less shape whose notes name no chord draws with no name and warns: write the
chord name first (`@chord(NAME 808081)`), or use `@diagram(808081)` for a diagram with no
name. A name with no shape is not warned about — save in an `all` score,
where a chord with no shape at all on the score's tuning (none written, no usual shape) warns
ONCE per chord and tuning in the file, at its first appearance, naming the fix (write the
shape); a Roman degree, a bare `@chord` and a symbol-less shape are not checked there (their
chord is the page's to resolve). An unknown chord symbol keeps `@chord`'s own warning (LYS1008).

**The check (LYS1039)** — a WRITTEN shape that disagrees with the symbol it is written for
(`@chord(SYMBOL shape)`, a row's `SYMBOL(shape)`) warns; the shape still draws. The notes it
sounds are each unmuted string's open pitch plus its fret, and two rules are asked (looser
than Lily#'s order, below, so every shape of that order passes):

1. a note that is not a chord tone (for X/Y, X's tones plus Y): `@chord(C x02210)` —
   *'x02210' sounds A C E, which is Am, not C (A is not a tone of C) - write @chord(Am x02210)
   or another shape*; the chord the notes do name is suggested (a slash chord when its root is
   not the lowest note), else the foreign notes are listed;
2. a required tone missing — every tone but the ROOT and the PERFECT FIFTH (the 3rd, the 7th,
   an altered fifth such as m7-5's, the tensions); for X/Y, Y when it is neither X's root nor
   its perfect fifth (`@chord(C/F# x32010)` lacks F♯): `@chord(C7 x3201x)` — *'x3201x' for C7
   lacks B♭ (the 7th) - fret B♭ or write another shape*.

The bass is not checked: an inversion (`@chord(C 032010)`, LilyPond's own C7 `032310`) is an
ordinary shape. One warning per shape lists every problem. A shape is checked on the tuning it is routed to
in each score that draws it (layout, else the part's instrument, else the guitar); a shape
no score uses (a four-string shape in a guitar-only score) and a shape under
`chordDiagrams none` are not. A symbol-less `@chord(x32010)` (its name comes from the shape),
`@diagram` and a Roman degree in a row (its chord is the key's at its bar) are not checked.
The notes are named as the chord spells them; any other in the key its root suggests.
Nine of LilyPond's predefined shapes would warn and are left out of Lily#'s tables — seven
sound a note outside their chord (guitar D♯m/E♭m `xx4341`, Faug `xx1443`, Baug `x3200x`;
ukulele Bsus2 `5122`; mandolin C♯aug/D♭aug `x630`), two lack the diminished fifth (mandolin
C♯dim7/D♭dim7 `3210`, only B♭ and E): those chords take the first shape of Lily#'s order (the
ukulele's Bsus2 has none). Every other predefined shape passes.

**Lily#'s order.** For the usual shape of the chords LilyPond's tables lack, and for the
editor's `Ctrl+Shift+Up`/`Down` (which rewrites a chord's shape to the next / previous: the
usual shape first, then this order), Lily# lists a chord's shapes on every tuning.
A shape assigns each string muted or a fret. A shape is VALID when:

- **V1.** at least 3 strings sound; frets 0..15;
- **V2.** only chord tones sound — the pitch classes of the chord; for a slash chord X/Y, X's
  tones plus Y;
- **V3.** every REQUIRED tone sounds: all of the chord's tones except the perfect fifth,
  which may be omitted; altered fifths (dim, aug, m7-5, 7-5, 7+5) are required; for X/Y, Y is
  required;
- **V4.** the LOWEST-PITCHED sounding note is the root (for X/Y: Y) — asked only on a tuning
  whose strings rise in pitch; on a re-entrant tuning (the ukulele's high G, a banjo's drone)
  the lowest string is not the lowest note, so V4 is not asked and the other rules alone list
  the shapes — which puts LilyPond's own ukulele shape first for every chord tried (C `0003`
  of 39 shapes, Am `2000` of 38, F `2010` of 23, G7 `0212` of 19);
- **V5.** span: among fretted strings (fret > 0), max − min ≤ 3 (four frets); open strings
  are not counted. A shape with max − min = 4 (five frets) is a STRETCH shape — hard to play,
  so left out unless stretch shapes are asked for (below);
- **V6.** at most 4 fingers: one per fretted string, except that if the lowest fretted fret f
  is used on two or more strings and every string between its first and last use is fretted
  at ≥ f (none open or muted), those strings at f count as ONE finger (barre).

The order lists the MAXIMAL valid shapes — valid, and no muted string can be given any fret
0..15 with the result still valid (every playable shape is one of them with strings muted) —
by (a) POSITION = the lowest fretted fret, 0 if none, ascending; (b) FINGER COUNT (V6's),
ascending; (c) the frets from the LOWEST string, muted = −1, lexicographic ascending. On
standard tuning C has 38 (`x32010` first), Cm7 33, D 36. With stretch shapes (V5 at
max − min ≤ 4) there are more — C 57, Cm7 52, D 64 — and the maximal shapes are those of that
wider rule (G7's `35340x` is maximal without stretch, but grows into `353407` with it). The
editor steps through the order without stretch shapes unless the VS Code setting
`lilysharp.chordShapes.includeStretch` is on; a written stretch shape steps to the next
(previous) shape by where it would sort. The order is the editor's, NOT the language's: no
source names a shape by its place in it, so it may be improved without moving any book's
diagram.

**Stepping.** `Ctrl+Shift+Up` on a chord — an `@chord(G)` or a row entry `G` — with no shape
writes the usual shape (`@chord(G 320003)`, `G(320003)`) and the diagram appears; further Ups
walk the order — frets 10–15 included, a `-` around each two-digit fret (Cm goes on past
`8xx888` to `8xx88-11` … `x-15-13-x-13-15`, its 29th); `Down` back AT the usual shape removes it again (the diagram goes, a row's
empty `( )` with it); `Down` on a name alone does nothing. With several shapes written, the
one for the chord's tuning steps and the others stay. That tuning is the rule above read in
the FIRST score that renders the chord (for a row, the first score placing it); when another
score draws it on another tuning, the status bar says so. When that first score writes
`chordDiagrams … all`, a name alone already SHOWS the usual shape, so the step counts from it:
`Up` on the name writes the NEXT shape after the usual one, and `Down` at the usual shape —
written or not — does nothing (the status bar says why). The same where that score's layout
table lists the chord (*Listed chords*): the name alone shows the table's shape, `Up` writes
the shape after it, and `Down` at the written table shape removes it (the page keeps showing it).

**Hover** a `@chord` or a `chords` row entry with no shape: one line says how to add a diagram
and what it would be — `Ctrl+Shift+↑ adds a chord diagram (guitar: 320003)`; in an `all` score,
the usual shape it draws instead — `guitar: 320003 (default) — shape 1 of N`; for a chord the
layout table lists, the table's shape — `guitar: xx3211 (layout)`. With a shape
written: the shape each tuning its scores draw on shows — `guitar: x3x546 (written)`,
`ukulele: no diagram` — and the stepping tuning's line also says where the shape stands in the
editor's order — `shape n of N (M with stretch)` (the hover carries no setting, so it gives
both counts), or `stretch shape n of M` for a stretch shape.

**Exports.** Under a `chords` row some entry of which draws a diagram the `.ly` twin writes a
`FretBoards` context over the same chord music — each drawn shape (written, or the layout
table's) as a one-shape table set on that chord, a silent `s` for every chord with none
(LilyPond would compute a diagram for it), `stringTunings` for a tuning other than the
guitar's — so LilyPond stacks the diagrams under the `ChordNames` line as the page does; a row
none of whose chords draws gets no context. In an `all` score every row gets the context and
every chord its one-shape table (the usual shape, with LilyPond's fingers when it is theirs) —
not LilyPond's own tables, which would draw a shape of LilyPond's where they hold none. An
`@chord`'s diagram is the note's `\fret-diagram-terse` markup under its name. MusicXML nests an
`@chord`'s diagram's `<frame>` in its `<harmony>` (a `chords` row is not exported to MusicXML
yet, nor is a bare `@chord`'s name); on a rest or a spacer the `<harmony>` stands before the
rest, at its moment. A diagram does not sound in the MIDI.

### Chords from a shape — `chord(…)`

`chord(SYMBOL SHAPE)` is a music item that WRITES THE NOTES of a shape: each unmuted
string's open pitch plus its fret, as a chord on the staff, every note with its string number.
It takes what a `<…>` chord takes after it — duration, dots, tremolo, ties, slurs, beams,
articulations, dynamics, a bare duration or `q` repeating it, a tuplet or a grace body around it.

```
chord(C x32013)1                 // C3 E3 G3 C4 G4 on a guitar, a whole note
chord(Cm7 x3x546)2@chord         // + the name and the diagram (the bare @chord takes the item's words)
chord(C xx-10-12-13-12)4.~       // frets 10-15 in the dash form; duration, dots, tie as on <…>
chord(x32010)2@chord             // no symbol: the shape alone gives the notes; @chord names them (C)
```

- **The words** are an `@chord(…)` argument's: a symbol then a shape (or a shape alone), either
  shape form, a tuning word binding a shape to one tuning (`chord(F guitar 133211 ukulele 2010)`).
  Every shape warning applies (LYS1038: length, dashes, case), and a shape that disagrees with its
  symbol warns (LYS1039, the fix written `chord(X shape)`).
- **The shape is required** (for now): `chord(C)` warns (LYS1040) naming the fix —
  `write a shape: chord(C x32010)` — and keeps its time as a SPACER, so the bar still adds up;
  so does a shape no string count of the part's tuning takes.
- **The tuning** is the PART's: its fretted instrument (its `tuning`, else its `instrument`
  preset's — the tuning its tab frets on), else the standard guitar. The shape gives SOUNDING
  pitches, and the staff writes them the way the part writes any sounding pitch: a guitar part
  (`treble_8`) writes x32013 an octave up, as C4 E4 G4 C5 G5, and plays C3 E3 G3 C4 G4; a part
  with no instrument writes them where they sound. The ukulele's re-entrant G string sounds where
  it is (`chord(C 0003)` = G4 C4 E4 C5).
- **Absolute.** Octave marks, the relative frame and `octave absolute` do not move the item;
  marks after its `)` are an error (LYS0035) and ignored — write a shape higher on the neck.
- **The frame after it** is its LOWEST sounding note (as a `<…>` chord hands on its anchor): in
  relative mode the next note is read from it; absolute mode changes nothing. A phrase that
  opens with the item hands the same note on after its reference (not moved by the
  reference's marks).
- **In `<< >>`** the item is SPREAD: its notes, lowest first, each one member of the broken
  chord — `<< chord(C x32010) >>2` plays the five strings in turn under 5:4, and
  `<< c chord(G 320003) e >>` plays c, G's six strings, then e. A share dot after it holds its
  last note, a `(` after it starts the bow on its first; first in the group, it is the root.
- **Spelling:** a note that is a tone of the symbol (or its slash bass) is spelled as the chord
  spells it — Cm7's E♭ and B♭, never D♯ and A♯; any other note in the key.
- **String numbers:** every note carries its string, so a `tab` of the part shows exactly the
  shape; a muted string gives no note.
- **Notes only.** The item draws no name and no diagram of its own. A bare `@chord` on it names
  the item's symbol and draws its shape (the usual diagram rules: the diagram's tuning is layout,
  else the part's instrument, else the guitar); an `@chord(Other …)` on it is its own.
- **Exports.** The MIDI plays the notes; MusicXML writes the chord with each note's
  `<technical><string>`; the `.ly` twin writes the notes out as a chord, lowest first, each with
  its string number (`<c\5 e\4 g\3 c'\2 g'\1>1`), and `\omit StringNumber` on the staff.
- **Editor.** Hover lists the notes it sounds on each tuning that plays it —
  `guitar: x32013 — C3 E3 G3 C4 G4 — shape n of N`; `Ctrl+Shift+Up`/`Down` on the item steps its
  shape through the order above on the PART's tuning (from `chord(C)`, Up writes the usual shape),
  sounding each, and Down stops at the usual shape (the item keeps a shape); inside `chord(` the
  completion offers the chord symbols `@chord(` does.
- `chord` is reserved in music only: a phrase cannot be named it (a part can).

## Guitar Bends and Technique Letters

`@bend(half|full|N)` draws a bend-up: a rising arrow off the note's right, labelled in
steps. `half` is one semitone, `full` two, and `N` is 1–12 semitones (`@bend(3)` prints
`1½`). It is drawn on the page only — the `.ly` twin drops it with a warning, and MusicXML
does not carry it.

`@hammerOn`, `@pullOff` and `@tap` print the tab letters H, P and T, small and italic,
opposite the stem; `.up` / `.down` force the side. They are drawn on a `tab` staff as well.
The `.ly` twin writes each as a text script (`-\markup { \italic "H" }`); MusicXML writes
`<hammer-on>` / `<pull-off>` from the previous note, and `<tap/>`.

```
c4@hammerOn d@pullOff e@tap f@bend(full) |
```

## Comments

```
// This is a line comment
/* This is a
   block comment */
```

## Reserved Words

The following words are keywords. A **part or section name may still be any of them**
except the structural ones (2026-10-03): the words that open a block of their own where a
name could stand — `voice` `lyrics` `chords` `section` `part` `phrase` `form` `score` `tab`
`staff` `ossia` `grandStaff` `staffGroup` `choirStaff` `condensedStaff` `combinedStaff`
`layout` `paper` `fonts` `grace` `acciaccatura` `appoggiatura` `cue` — plus the section
settings and item-head directives `key` `time` `tempo` `partial` `override` `revert` `once`
`using` `title` `subtitle` `composer` `poet`, and the words a form body reads as items of
its own, `segno` `fine` `coda` `dc` `ds` `al` `to` `break` `noBreak` `pageBreak`
`noPageBreak` (a section is named by the same rule). Everything else names a part: `part p`,
`part bass`, `part percussion`, `part q`, `part c` all compile (the list is
`SyntaxFacts.PartNameReservedVocabulary`, and the error for a reserved name prints it).
A **phrase name** is narrower, because a phrase is referenced *bare in a music stream*:
an identifier, the four clef words `treble` `bass` `alto` `tenor`, or a dynamic word
`ppp` `pp` `p` `mp` `mf` `ff` `fff` (a bare `p` in music was an error, so it now plays the
phrase). Any other keyword — and `f`, which is the note F — is refused at the declaration
(LYS1030). A variable name is an identifier.

| Group | Words |
|-------|-------|
| Structure | `section` `form` `using` `tab` `ossia` `transpose` `octave` `pitch` `instrument` `percussion` `drummap` |
| Score / layout | `score` `part` `staff` `grandStaff` `staffGroup` `choirStaff` `condensedStaff` `combinedStaff` `voice` `phrase` `repeat` `break` `noBreak` `pageBreak` `noPageBreak` `partial` `embedded` `fonts` `paper` `layout` |
| Metadata | `title` `subtitle` `composer` `poet` `tempo` `time` `key` `clef` |
| Modes | `major` `minor` `ionian` `dorian` `phrygian` `lydian` `mixolydian` `aeolian` `locrian` |
| Clef names | `treble` `bass` `alto` `tenor` `treble_8` `bass_8` `soprano` `mezzosoprano` `baritone` |
| Notation | `tuplet` `grace` `acciaccatura` `appoggiatura` `cue` `lyrics` `chords` `tuning` |
| Overrides | `override` `revert` `once` |
| Navigation (form block) | `segno` `fine` `coda` `dc` `ds` `al` `to` |
| Dynamics | `ppp` `pp` `p` `mp` `mf` `f` `ff` `fff` |

⚠️ The `fonts { }` keys (`serif` `header` `stanza` `chord` `barNumbers` …) are **not**
reserved words — they are read inside that block only, against the role vocabulary, so
they stay free as part / section / phrase names. Several of them (`title`, `lyrics`,
`chords`, `tempo`, `instrument`, `tuplet`, `time`, `tab`) are reserved for other reasons and
appear above. The `paper { }` keys and units (`paperWidth`, `mm`, `basicDistance`, …)
are free the same way.

⚠️ Measured word by word against `Lexer.GetKeywordKind` on 2026-08-16, by asking whether
each can name a part. Five words this table listed are **not** reserved and name a part
fine — `include`, `let`, `use`, `chordnames`, `tabStaff` — and the sixteen added above are.
`structure` and `render` left the language when they became `form` and `score`.

Notes:

- Single letters `a`–`g` are pitch names; `r`/`R` are rests, `s` is a spacer rest.
- **Reserved in music only:** `q` (repeats the previous chord), `chord` (a chord from a
  shape, `chord(C x32010)` — 2026-09-28) and the drum-kit names (`bd`, `sn`, `hh`, …). A music
  stream reads them as music items, so a **phrase** cannot be named any of them (LYS1030); a
  part, a section or a `fonts` key still can (`part chord { … }` compiles).
- **`tab X Y` reads a declared part first** (2026-10-03): with a part named `bass` in the
  file, `tab bass click` is part `bass`'s tab plus `click` played to MIDI only — the same
  bare-part-name item that follows a staff — and LYS1044 says so when `bass` is a tuning
  word too. With no such part — or with the same word twice, `tab bass bass` — it is the
  `bass` tuning over the part, as before. For the tuning over a part that shares a tuning
  word's name, write the synonym: `tab bass4 click`.
- Articulation, ornament, dynamic-text and mark **names** (`staccato`, `tr`, `mordent`,
  `cresc`, `dim`, `segno`, …) are resolved from the `@name` text and are **not** reserved
  as identifiers — `tr`, `acc`, `ten`, `dim` etc. remain usable as your own names.
- **Keyword spelling is exact, including case.** `Lexer.GetKeywordKind` matches whole
  strings and the lexer has no case-folding path at all, so `grandstaff` is not a spelling
  of `grandStaff` — it is an ordinary identifier, and names a part fine (measured). Written
  where a keyword belongs it is refused exactly like any other unknown word.
  ⚠️ This line used to claim the opposite for `grandstaff` and `tabstaff`. It also outlived
  the measurement nine lines above, which had already found that `tabStaff` is not a keyword
  in the first place.
