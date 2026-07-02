# Lily# Grammar Specification
# Version: 1.0.0
# Date: 2026-07-01

> The canonical, always-current single-file spec is [`GRAMMAR_FOR_LLM.md`](GRAMMAR_FOR_LLM.md)
> (compressed, every example parse-verified). This file is the formal EBNF; when the two
> disagree, `GRAMMAR_FOR_LLM.md` and the parser are authoritative.

## Design Principles

1. Single-pass compilation  - No forward references, immediate error detection
2. Explicit over implicit   - No hidden state, clear structure
3. Locality                 - Each element independently parsable
4. Visual correspondence    - Corresponds to sheet music visually
5. LilyPond inspiration     - Inherit practical conventions, not Scheme complexity
6. Section-oriented         - Organize by musical sections, not just by parts
7. AI-friendly              - Unambiguous grammar for both human and AI authoring

Lily# is **not** LilyPond. Backslash constructs are rejected — `\relative`,
`\repeat volta`, `\new Staff`, `\version`, `<< … \\ … >>`, `\p`/`\f` dynamics, etc.
The one annotation prefix is `@`; backslash is reserved for tablature only
(`\3` string numbers, `\tuning`).

================================================================================
## 1. Lexical Grammar
================================================================================

### Whitespace & Comments

Whitespace     = { ' ' | '\t' | '\r' | '\n' } ;
LineComment    = '//' , { any except '\n' } , '\n' ;
BlockComment   = '/*' , { any except '*/' } , '*/' ;
Trivia         = { Whitespace | LineComment | BlockComment } ;

### Literals

Integer        = Digit , { Digit } ;
Digit          = '0' | '1' | '2' | '3' | '4' | '5' | '6' | '7' | '8' | '9' ;
String         = '"' , { StringChar } , '"' ;

### Identifiers

Identifier     = IdentStart , { IdentCont } ;
IdentStart     = UnicodeLetter | '_' ;          (* any Unicode letter, e.g. 動機 *)
IdentCont      = IdentStart | Digit | '-' ;

### Pitch Names

PitchBase      = 'c' | 'd' | 'e' | 'f' | 'g' | 'a' | 'b' ;
Accidental     = 'is' | 'es' | 'isis' | 'eses' ;   (* sharp / flat / double *)
OctaveUp       = { '\'' }+ ;
OctaveDown     = { ',' }+ ;
Octave         = OctaveUp | OctaveDown | ε ;
PitchToken     = PitchBase , [ Accidental ] , Octave ;

(* Octaves are always RELATIVE: each bare pitch lands in the octave nearest the
   previous pitch (an interval of a fourth or less), then any '/',' marks shift it.
   There is NO absolute-octave mode and no 'relative'/'absolute' keyword; the octave
   resets to the part's base at each section boundary and at each phrase call. *)

### Duration

DurationBase   = '1' | '2' | '4' | '8' | '16' | '32' | '64' | '128' ;
Dots           = { '.' }+ ;
Tremolo        = ':' , ( '8' | '16' | '32' ) ;    (* stem tremolo: 1-3 beams *)
DurationToken  = DurationBase , [ Dots ] , [ Tremolo ] ;

### Keywords

Keyword = 'title' | 'composer' | 'tempo' | 'time' | 'key' | 'clef'
        | 'part' | 'phrase' | 'section' | 'structure' | 'score'
        | 'staff' | 'grandStaff' | 'tab' | 'tabStaff' | 'ossia' | 'voice'
        | 'lyrics' | 'chords' | 'chordnames' | 'tuning' | 'instrument' | 'channel'
        | 'transpose' | 'octave' | 'include' | 'use' | 'let' | 'break' | 'partial'
        | 'tuplet' | 'grace' | 'acciaccatura' | 'appoggiatura'
        | 'repeat' | 'volta' | 'alternative' | 'swing'
        | 'override' | 'revert' | 'once'
        | 'major' | 'minor' | 'dorian' | 'phrygian' | 'lydian' | 'mixolydian'
        | 'aeolian' | 'locrian'
        | 'treble' | 'bass' | 'alto' | 'tenor' | 'treble_8'
        | 'segno' | 'fine' | 'coda' | 'dc' | 'ds' | 'al' | 'to'
        | 'ppp' | 'pp' | 'p' | 'mp' | 'mf' | 'f' | 'ff' | 'fff'
        ;

(* The four clef-name words (treble bass alto tenor) ARE allowed as part / section /
   phrase names. Single letters a-g are pitches; r / R / s are rests. Articulation,
   ornament, dynamic-text and mark NAMES (staccato, tr, mordent, cresc, dim, …) are
   resolved from the '@name' text and are NOT reserved. *)

### Operators & Punctuation

Punctuation    = '{' | '}' | '(' | ')' | '<' | '>' | '[' | ']'
               | '|' | '~' | ':' | '=' | '/' | '@' | '_' | '\' | '-' | '.' | '$'
               | '|:' | ':|' | ':|:' | '||' | '|.'
               ;

================================================================================
## 2. File Structure
================================================================================

### 2.1 Top-Level Structure

File           = { TopLevelItem } ;

TopLevelItem   = MetadataDecl                     (* title, composer *)
               | GlobalSetting                    (* tempo, time, key *)
               | PartDecl                         (* part definitions *)
               | PhraseDecl                       (* reusable music fragments *)
               | SectionDecl                      (* musical sections - REQUIRED *)
               | StructureDecl                    (* song form - optional *)
               | ScoreDecl                        (* output definitions - REQUIRED *)
               | OverrideDecl                     (* engraving overrides *)
               ;

### 2.2 Metadata

MetadataDecl   = MetadataKey , String ;
MetadataKey    = 'title' | 'composer' ;

### 2.3 Global Settings

GlobalSetting  = TempoDecl | TimeDecl | KeyDecl ;

TempoDecl      = 'tempo' , Integer , [ 'swing' , [ Integer ] ] ;
                 (* 'tempo 120 swing' draws a shuffle-feel equation; 'swing 16' = 16th swing *)
TimeDecl       = 'time' , Integer , '/' , Integer ;
KeyDecl        = 'key' , PitchBase , [ Accidental-text ] , Mode ;

Mode           = 'major' | 'minor' | 'dorian' | 'phrygian'
               | 'lydian' | 'mixolydian' | 'aeolian' | 'locrian' ;

(* Example:
   title "My Song"
   composer "Jane Doe"
   tempo 120
   time 4/4
   key c major
*)

================================================================================
## 3. Part Definition
================================================================================

(* Parts declare instruments/voices. Header attributes are written BARE — the same
   command form as the top-level commands (NO colon, NO '='). *)

PartDecl       = 'part' , Identifier , [ PartBody ] ;
PartBody       = '{' , { PartProperty } , '}' ;
PartProperty   = 'clef'        , ClefName
               | 'instrument'  , ( Identifier | String )
               | 'channel'     , Integer
               | 'octave'      , ( 'absolute' | 'relative' )
               | 'transpose'   , PitchToken
               | 'tuning'      , Identifier
               | 'removeEmpty' , ( 'true' | 'all' | 'false' ) ;

ClefName       = 'treble' | 'bass' | 'alto' | 'tenor' | 'treble_8' ;

(* removeEmpty (hara-kiri): hide this part's staff in systems where it only
   rests. 'true' keeps the FIRST system (LilyPond \RemoveEmptyStaves);
   'all' hides the first system too (\RemoveAllEmptyStaves). A system stays
   visible if ANY voice of the staff plays. Default: never hide. *)

(* Examples:

   part melody                        // minimal
   part melody { clef treble }        // bare attribute, no colon
   part bass   { clef bass  instrument "Cello" channel 2 }
   part fill   { clef bass  removeEmpty all }   // hara-kiri staff
*)

================================================================================
## 4. Phrase Definition
================================================================================

(* Reusable music fragments, referenced as $name. Defined before use. A phrase body
   evaluates in a fresh frame (default octave/pitch/duration), so $name means the same
   notes at every call. *)

PhraseDecl     = 'phrase' , Identifier , MusicBlock ;

(* Example:
   phrase theme { c4 d e f | g a b c' | }
   section Main { melody { $theme g2 g | } }
*)

================================================================================
## 5. Section Definition
================================================================================

(* Musical sections bind music to each part by name. At least one is required. *)

SectionDecl    = 'section' , Identifier , '{' , { SectionItem } , '}' ;

SectionItem    = SectionSetting
               | PartBlock                        (* partName MusicBlock *)
               | VoiceBlock                       (* multi-voice on one staff *)
               | LyricsBlock                      (* note-bound OR named row *)
               | ChordsBlock                      (* note-aligned OR named chord row *)
               ;

SectionSetting = KeyDecl | TempoDecl | TimeDecl ;

PartBlock      = Identifier , MusicBlock ;

(* Note-bound lyrics align to the SAME part's notes. A NAMED lyrics/chords block
   (with an identifier before the brace) is an independent ROW placed in a score with
   'lyrics NAME' / 'chords NAME' — see §7 (lead sheets). *)
LyricsBlock    = 'lyrics' , [ Identifier ] , '{' , { LyricMeasure } , '}' ;
LyricMeasure   = { LyricSyllable } , '|' ;
LyricSyllable  = LyricText | '~' | '_' ;          (* '-' suffix joins one word's syllables *)

ChordsBlock    = ( 'chordnames' | 'chords' ) , [ Identifier ] , '{' , { ChordEntry | Barline } , '}' ;
ChordEntry     = PitchBase , [ Accidental-text ] , [ DurationToken ] , [ ':' , Quality ] , [ '/' , PitchBase ] ;
                 (* c=C, a:m=Am, g:7=G7, g:m7.5-=Gm7b5, c/g=C over a G bass *)

(* Example:
   section Verse {
     key g major
     melody { c4 d e f | g2 g | }
     lyrics { Twin- kle twin- kle | lit- tle star | }
   }
*)

### 5.1 Multi-voice (one staff)

VoiceBlock     = 'voice' , [ Identifier ] , MusicBlock , { 'voice' , [ Identifier ] , MusicBlock } ;

(* Example (each voice { } is a simultaneous voice; NOT the LilyPond '<< \\ >>' form):
   section Main { piano { voice { c'2 d } voice { e2 f } } }
   // A named voice binds its own lyrics: voice sop { … }  +  lyrics sop { … }
*)

================================================================================
## 6. Structure Definition
================================================================================

(* Song form: print/playback order of sections. Optional — omitting it plays sections
   in declaration order. Only section references and navigation marks (no inline music). *)

StructureDecl  = 'structure' , '{' , { StructureItem } , '}' ;

StructureItem  = SectionRef                        (* Identifier — shows the section label *)
               | '~' , Identifier                  (* same section, no label *)
               | String                            (* custom label for the preceding/section *)
               | NavMark                            (* segno / coda / fine / dc / ds / to coda *)
               | '_' , String                       (* custom text directive *)
               ;

NavMark        = 'segno' | 'coda' | 'fine' | 'to' 'coda'
               | 'dc' [ 'al' ( 'fine' | 'coda' ) ]
               | 'ds' [ 'al' ( 'fine' | 'coda' ) ] ;

(* Section reuse and a custom label:
   structure { Intro Verse Verse "Verse (reprise)" Coda } *)

(* Navigation: signs (segno/coda) engrave at the START of the following section; text
   directives (fine, to coda, dc/ds, dc al fine, ds al coda) engrave at the END of the
   section just played:
   structure { A segno  B to coda  C ds al coda  coda D } *)

================================================================================
## 7. Score (Output) Definition
================================================================================

(* A printable layout. 'score' is the keyword; the optional string is the output
   basename. Multiple 'score' blocks emit multiple files. MIDI has NO source block —
   it is a CLI output: `lysc midi song.lys song.mid`. *)

ScoreDecl      = 'score' , [ String ] , '{' , [ StructureDecl ] , { ScoreItem } , '}' ;

ScoreItem      = StaffRender                        (* staff partName — BARE, no braces *)
               | 'grandStaff' , '{' , { StaffRender } , '}'
               | 'tab' , PartRef                     (* tablature: tab partName *)
               | 'ossia' , '{' , PartRef , '}'       (* ossia { partName } *)
               | 'chords' , PartRef                  (* independent chord ROW (lead sheet) *)
               | 'lyrics' , PartRef                  (* independent lyrics ROW (lead sheet) *)
               ;

StaffRender    = 'staff' , [ ClefName ] , PartRef ;
PartRef        = Identifier ;

(* A 'score' may carry its OWN 'structure { … }' to render a different arrangement
   (e.g. a practice excerpt); it overrides the top-level structure for that score only. *)

(* Examples:

   score "full" {
     grandStaff { staff rightHand  staff leftHand }
   }

   score practice { structure { Intro } staff melody }
*)

### 7.1 Lead sheets (chords and/or lyrics, no staff)

(* A 'chords NAME { … }' / 'lyrics NAME { … }' part placed in a score with
   'chords NAME' / 'lyrics NAME' (instead of 'staff NAME') renders WITHOUT a staff:
   a grid of measure barlines, chord symbols between the bars (at their timing), and
   lyrics below. Source barlines ( | |: :| || |. ) are drawn. *)

(* Example:
   section Main {
     chords prog  { c2 g:7 | a:m f | c1 :| }
     lyrics words { Twin- kle | lit- tle | star | }
   }
   structure { Main }
   score "sheet" { chords prog lyrics words }
*)

================================================================================
## 8. Music Expression
================================================================================

### 8.1 Music Block

MusicBlock     = '{' , { MusicItem } , '}' ;

MusicItem      = Note | Rest | Chord | Barline | InlineVolta | PhraseRef
               | Slur | Tie | Beam | Tuplet | Grace | MidMusicCommand ;

(* Mid-music commands change context here. clef/key/time use the bare COMMAND form
   (no colon) — distinct from a part header which uses the same bare form to set the
   INITIAL value. *)
MidMusicCommand = 'clef' , ClefName
               | 'key' , PitchBase , [ Accidental-text ] , Mode
               | 'time' , Integer , '/' , Integer
               | 'partial' , DurationToken
               | 'break' ;

### 8.2 Notes, Rests, Chords

Note           = PitchToken , [ DurationToken ] , { Annotation } ;
Rest           = ( 'r' | 's' | 'R' ) , [ DurationToken ] , { Annotation } ;
                 (* r = plain rest, s = invisible spacer, R = full-measure rest *)
Chord          = '<' , ChordNote , { ChordNote } , '>' , [ DurationToken ] , { Annotation } ;
ChordNote      = PitchToken , { Annotation } ;

Barline        = '|' | '||' | '|.' | '|:' | RepeatEnd ;
RepeatEnd      = ':|' , [ '*' , Integer ] ;          (* :|*N plays the span N times, default 2 *)

(* First/second-time endings inside a |: … :| repeat. '[' followed by an integer is a
   volta; otherwise '[' … ']' is a manual beam group. *)
InlineVolta    = '[' , Integer , [ ( '-' | ',' ) , Integer ] , '.' , { MusicItem } , ']' ;
Beam           = '[' | ']' ;
PhraseRef      = '$' , Identifier ;

### 8.3 Ties, Slurs, Beams

Tie            = '~' ;            (* same pitch across notes/barline: c4~ | c4 *)
Slur           = '(' | ')' ;      (* over notes OR chords: c4( d e) , <c e>4( <d f>) *)
Beam           = '[' | ']' ;      (* manual; beaming is automatic otherwise *)

### 8.4 Annotations (@name, attached to a note or chord)

Annotation     = '@' , AnnotationName , [ '(' , Arg , { ( ' ' | ',' ) , Arg } , ')' ] , [ Placement ] ;
Placement      = '.up' | '.down' ;   (* force above / below; default is automatic *)
(* A value-bearing annotation puts its argument(s) in parentheses (space- or
   comma-separated); '.' is reserved for the .up / .down placement suffix. *)

(* The '@' prefix is the ONLY annotation prefix; '\name' is rejected (backslash is
   tablature-only). AnnotationName is resolved from text — it is NOT a reserved keyword,
   so names like 'tr' remain usable as identifiers. Categories: *)

(* - Articulations: @staccato @accent @tenuto @marcato @fermata @portato  (.up/.down ok)
   - Ornaments:     @trill @mordent @prall @turn @invertedturn
   - Dynamics:      @ppp @pp @p @mp @mf @f @ff @fff   (default below; @f.up forces side)
   - Hairpins:      @cresc @decresc @dim  (start note → next dynamic; .up/.down REJECTED)
   - Stem:          @stemUp @stemDown      (a beam's shared direction wins)
   - Accidental:    @courtesy (cautionary) @editorial (musica ficta)
   - Arpeggio:      <c e g>4@arpeggio
   - Glissando:     c4@glissando d
   - Figured bass:  c4@fig(6) , d4@fig(6 4)
   - Chord name:    c4@chord(C) , d4@chord(Dm)
   - Fingering:     <c@finger(1) e@finger(3)>4
   - Rehearsal mark: c4@mark(A)
   - Marks/spanners: @segno @coda @fine @dc @ds @rit @accel
                     @ottava … @loco , @startTrillSpan … @stopTrillSpan ,
                     @ped … @ped(off) *)

(* Example: c4@staccato.up d4@accent@p <e g>4@arpeggio | *)

### 8.5 Tuplets and Grace notes

Tuplet         = 'tuplet' , Integer , '/' , Integer , MusicBlock ;   (* nesting allowed *)
Grace          = ( 'grace' | 'acciaccatura' | 'appoggiatura' ) , MusicBlock ;

================================================================================
## 9. Override / Revert (engraving properties)
================================================================================

OverrideDecl   = [ 'once' ] , 'override' , Grob , '.' , Property , '=' , Integer
               | 'revert' , Grob , '.' , Property ;

(* Example:
   override Stem.length = 7
   c4 d e f |
   revert Stem.length
   once override Stem.length = 9     // applies to the next note only
*)

================================================================================
## 10. Complete Example
================================================================================

```lilysharp
title "Demo"
composer "Jane Doe"
tempo 120
time 4/4
key c major

part melody { clef treble }
phrase riff { c4 d e f | }

section Verse {
  melody { $riff | g4@p a( b c') | }
  lyrics { Sing a song now | one two three four | }
}

// A staff-less lead sheet (chords + lyrics, no notes):
section Sheet {
  chords prog  { c2 a:m |: f2 g:7 | c1 :| }
  lyrics words { Twin- kle twin- kle | lit- tle | star | }
}

structure { Verse }

score "demo"  { staff melody }
score "sheet" { structure { Sheet } chords prog lyrics words }
```

MIDI export: `lysc midi demo.lys demo.mid` (no score block needed).

================================================================================
## 11. Error Detection
================================================================================

### Required / structural

| Error              | Description                                              |
|--------------------|----------------------------------------------------------|
| No section         | File must contain at least one `section` block           |
| No score           | File must contain at least one `score` block             |
| Multiple structure | At most one top-level `structure` block                  |
| Inline music       | `{ }` music in `structure` (not allowed — section refs only) |
| Undefined ref      | Section / phrase / part referenced but not defined       |
| Forward reference  | Phrase used before its definition                        |
| Missing fine/segno/coda | `dc al fine` without `fine`, `ds` without `segno`, `to coda` without `coda` |

### Warnings

| Warning            | Description                                               |
|--------------------|-----------------------------------------------------------|
| Unused section     | Section defined but not in structure                      |
| Unused phrase      | Phrase defined but never referenced                       |
| Incomplete measure | Measure duration doesn't match the time signature         |
| Excess lyrics      | More lyric syllables than notes in a bar (extras dropped) |
