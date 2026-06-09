# Lily# Grammar Specification
# Version: 0.5.0
# Date: 2025-12-13

## Design Principles

1. Single-pass compilation  - No forward references, immediate error detection
2. Explicit over implicit   - No hidden state, clear structure
3. Locality                 - Each element independently parsable
4. Visual correspondence    - Corresponds to sheet music visually
5. LilyPond inspiration     - Inherit practical conventions, not Scheme complexity
6. Section-oriented         - Organize by musical sections, not just by parts
7. AI-friendly              - Unambiguous grammar for both human and AI authoring

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
StringChar     = any except '"' and '\'
               | '\' , EscapeChar ;
EscapeChar     = '"' | '\' | 'n' | 'r' | 't' ;

### Identifiers

Identifier     = IdentStart , { IdentCont } ;
IdentStart     = 'a'..'z' | 'A'..'Z' | '_' ;
IdentCont      = IdentStart | Digit | '-' ;

### Pitch Names

PitchBase      = 'c' | 'd' | 'e' | 'f' | 'g' | 'a' | 'b' ;
Accidental     = 'is' | 'es' | 'isis' | 'eses'
               | 'aes' | 'bes' ;
OctaveUp       = { '\'' }+ ;
OctaveDown     = { ',' }+ ;
Octave         = OctaveUp | OctaveDown | ε ;
PitchToken     = PitchBase , [ Accidental ] , Octave ;

(* Octaves are always RELATIVE: each pitch lands in the octave nearest the previous
   pitch, then any '/',' marks shift it. There is no absolute-octave mode and no
   'relative'/'absolute' keyword; the octave resets to the part's base at each
   section boundary. The ' and , marks adjust relative to that reckoning. *)

### Duration

DurationBase   = '1' | '2' | '4' | '8' | '16' | '32' | '64' | '128'
               | 'breve' | 'longa' ;
Dots           = { '.' }+ ;
Tremolo        = ':' , ( '8' | '16' | '32' ) ;    // Stem tremolo: 1-3 beams
DurationToken  = DurationBase , [ Dots ] , [ Tremolo ] ;

### Keywords

StructureKeyword = 'part' | 'phrase' | 'section' | 'structure' | 'render'
                 | 'staff' | 'grandStaff' | 'tab' | 'midi'
                 | 'title' | 'composer' | 'tempo' | 'time' | 'key' | 'clef'
                 | 'instrument' | 'channel'
                 | 'major' | 'minor' | 'dorian' | 'phrygian' | 'lydian'
                 | 'mixolydian' | 'aeolian' | 'locrian'
                 | 'treble' | 'bass' | 'alto' | 'tenor' | 'percussion'
                 | 'r' | 's'
                 ;

DynamicKeyword = '\p' | '\pp' | '\ppp' | '\mp'
               | '\f' | '\ff' | '\fff' | '\mf'
               | '\fp' | '\sf' | '\sfz' | '\rfz'
               | '\cresc' | '\decresc' | '\dim'
               ;

### Operators & Punctuation

Punctuation    = '{' | '}' | '(' | ')' | '<' | '>' | '[' | ']'
               | '|' | '~' | ':' | '=' | '/' | '@' | '_' | '\' | '-' | '.'
               | '|:' | ':|' | ':|:'
               ;

================================================================================
## 2. File Structure
================================================================================

### 2.1 Top-Level Structure

File           = { TopLevelItem } ;

TopLevelItem   = MetadataDecl                     (* title, composer, etc. *)
               | GlobalSetting                    (* tempo, time, key *)
               | PartDecl                         (* part definitions *)
               | PhraseDecl                       (* reusable music fragments *)
               | SectionDecl                      (* musical sections - REQUIRED *)
               | StructureDecl                    (* song form - REQUIRED *)
               | RenderDecl                       (* output definitions - REQUIRED *)
               ;

### 2.2 Metadata

MetadataDecl   = MetadataKey , String ;
MetadataKey    = 'title' | 'composer' | 'arranger' | 'copyright' ;

(* Example:
   title "My Song"
   composer "John Doe"
*)

### 2.3 Global Settings

GlobalSetting  = TempoDecl | TimeDecl | KeyDecl ;

TempoDecl      = 'tempo' , Integer ;
TimeDecl       = 'time' , Integer , '/' , Integer ;
KeyDecl        = 'key' , PitchToken , Mode ;

Mode           = 'major' | 'minor' | 'dorian' | 'phrygian'
               | 'lydian' | 'mixolydian' | 'aeolian' | 'locrian' ;

(* Example:
   tempo 120
   time 4/4
   key c major
*)

================================================================================
## 3. Part Definition
================================================================================

(* Parts define instruments/voices with their properties *)

PartDecl       = 'part' , Identifier , [ PartBody ] ;

PartBody       = '{' , { PartProperty } , '}' ;

(* Every part-header attribute is colon-form ('name: value'). time and tempo keep
   their richer value grammars but still take the colon, like the simple keys. *)
PartProperty   = SimpleKey , ':' , PropertyValue
               | 'time'  , ':' , TimeValue            (* e.g. time: 4/4 *)
               | 'tempo' , ':' , TempoValue ;         (* e.g. tempo: 120 *)

SimpleKey      = 'clef' | 'instrument' | 'channel' | 'tuning' | 'octave' ;

PropertyValue  = Identifier | String | Integer ;

(* Examples:

   // Minimal
   part melody

   // With properties
   part melody {
     clef: treble
     time: 4/4
     tempo: 120
     instrument: "Violin"
   }

   part bass {
     clef: bass
     instrument: "Cello"
     channel: 2
   }

   part guitar {
     clef: treble
     instrument: "Acoustic Guitar"
     tuning: standard
   }
*)

================================================================================
## 4. Phrase Definition
================================================================================

(* Reusable music fragments - must be defined before use *)
(* Phrases may NOT contain key/tempo/time/clef changes - pure music only *)

PhraseDecl     = 'phrase' , Identifier , MusicBlock ;

(* Example:
   phrase theme { c4 d e f | g a b c' | }
   phrase ending { g4 f e d | c1 | }
*)

================================================================================
## 5. Section Definition
================================================================================

(* Musical sections - at least one required *)

SectionDecl    = 'section' , Identifier , '{' , { SectionItem } , '}' ;

SectionItem    = SectionSetting
               | PartBlock
               | LyricsBlock
               ;

SectionSetting = KeyDecl | TempoDecl | TimeDecl ;

PartBlock      = Identifier , MusicBlock ;

(* Lyrics blocks - multiple allowed for verses *)
LyricsBlock    = 'lyrics' , '{' , { LyricMeasure } , '}' ;
LyricMeasure   = { LyricSyllable } , '|' ;
LyricSyllable  = LyricText
               | '~' | '～'      (* melisma continuation *)
               | '_'               (* rest/skip *)
               ;
LyricText      = { any except whitespace | '|' | '{' | '}' } , [ '-' ] ;

(* Example:
   section Intro {
     melody { c4 d e f | g a b c' | }
     bass { c2 g2 | c2 g2 | }
   }

   section Verse {
     key g major
     tempo 140
     melody { c4 d4 e4 f4 | g2 g2 | a1 | }
     lyrics { き ら き ら | ひ か | る | }
     lyrics { ま ば た き | し て | は | }
   }

   (* Lyric notation: *)
   (* - space separates syllables (each maps to next note) *)
   (* - hyphen at end connects syllables: "twi-" "nkle" *)
   (* - ~ starts melisma (syllable extends to next note) *)
   (* - ～ continues melisma *)
   (* - _ skips a note (rest or no lyric) *)
*)

================================================================================
## 6. Structure Definition
================================================================================

(* Song form with repeat signs - REQUIRED, exactly one per file *)
(* Structure defines the visual appearance of the score *)
(* Only section references allowed - no inline music { } *)

StructureDecl  = 'structure' , '{' , { StructureItem } , '}' ;

StructureItem  = SectionRef
               | SilentSectionRef
               | RepeatBlock
               | VoltaBlock
               | MusicMark
               | CustomText
               ;

### 6.1 Section References

SectionRef       = Identifier ;                   (* displays section label *)
SilentSectionRef = '~' , Identifier ;             (* no label displayed *)

(* Example:
   structure {
     Intro                // "Intro" label displayed
     Verse                // "Verse" label displayed
     ~Verse               // same section, no label
   }
*)

### 6.2 Repeat Blocks

RepeatBlock    = '|:' , { StructureItem } , ':|' , [ RepeatCount ] ;

RepeatCount    = 'x' , Integer ;                  (* default: x2 *)

(* Example:
   |: Verse Chorus :|           // repeat 2 times (default)
   |: Verse Chorus :| x3        // repeat 3 times
*)

### 6.3 Volta Blocks

VoltaBlock     = '[' , VoltaSpec , '.' , StructureItem , ']' ;

VoltaSpec      = VoltaNumber , { ',' , VoltaNumber }
               | VoltaRange
               ;

VoltaNumber    = Integer ;
VoltaRange     = Integer , '-' , Integer ;

(* Example:
   |: Verse [1. Bridge] [2. Chorus] :|
   |: Verse [1,3. Bridge] [2,4. Coda] :| x4
   |: Verse [1-3. Bridge] [4. Coda] :| x4
*)

### 6.4 Music Marks

(* Predefined music symbols - position determined automatically *)

MusicMark      = '@' , MarkName ;

MarkName       = 'segno'                          (* 𝄋 - beginning, above *)
               | 'coda'                           (* Coda - beginning, above *)
               | 'fine'                           (* Fine - end, above *)
               | 'ds'                             (* D.S. - end, above *)
               | 'dc'                             (* D.C. - end, above *)
               | 'ds.al" .fine'                    (* D.S. al Fine - end, above *)
               | 'ds.al.coda'                     (* D.S. al Coda - end, above *)
               | 'dc.al.fine'                     (* D.C. al Fine - end, above *)
               | 'dc.al.coda'                     (* D.C. al Coda - end, above *)
               | 'rit'                            (* rit. - end, below *)
               | 'accel'                          (* accel. - end, below *)
               | 'cresc'                          (* cresc. - end, below *)
               | 'decresc'                        (* decresc. - end, below *)
               | 'dim'                            (* dim. - end, below *)
               ;

(* Predefined mark positions:

   | Mark           | Horizontal | Vertical |
   |----------------|------------|----------|
   | @segno         | beginning  | above    |
   | @coda          | beginning  | above    |
   | @fine          | end        | above    |
   | @ds, @dc, etc. | end        | above    |
   | @rit, @accel   | end        | below    |
   | @cresc, etc.   | end        | below    |
*)

### 6.5 Custom Text

(* Custom text with explicit position *)

CustomText     = '_' , String ;                   (* end, below *)

(* Example:
   structure {
     Intro
     @segno
     |: Verse [1. Bridge] [2. Chorus @fine] :|
     Interlude _"molto rit."
     @ds.al.fine
   }
*)

================================================================================
## 7. Render Definition
================================================================================

(* Output definitions - at least one required *)

RenderDecl     = 'render' , RenderType , String , RenderBody ;

RenderType     = 'score' | 'midi' ;

RenderBody     = '{' , { RenderItem } , '}' ;

### 7.1 Score Render

RenderItem     = StaffRender | GrandStaffRender | TabRender | MidiPart ;

StaffRender    = 'staff' , '{' , PartRef , '}' ;

GrandStaffRender = 'grandStaff' , '{' , { StaffRender } , '}' ;

TabRender      = 'tab' , '{' , PartRef , '}' ;

PartRef        = Identifier ;

(* Example:
   render score "full-score.pdf" {
     staff { melody }
     staff { bass }
   }

   render score "piano.pdf" {
     grandStaff {
       staff { rightHand }
       staff { leftHand }
     }
   }
*)

### 7.2 MIDI Render

MidiPart       = PartRef , [ MidiOptions ] ;

MidiOptions    = { MidiOption } ;
MidiOption     = 'channel' , ':' , Integer
               | 'instrument' , ':' , Integer
               ;

(* Example:
   render midi "song.mid" {
     melody channel: 1 instrument: 41
     bass channel: 2 instrument: 33
   }
*)

================================================================================
## 8. Music Expression
================================================================================

### 8.1 Music Block

MusicBlock     = '{' , { MusicItem } , '}' ;

MusicItem      = Note
               | Rest
               | Chord
               | Barline
               | InlineVolta
               | PhraseRef
               | ParallelExpr
               | Slur
               | Tie
               | Beam
               | MidMusicCommand
               ;

(* Mid-music commands change context at this point in the stream. clef/key/time
   here are the bare COMMAND form (no colon) — DISTINCT from the part-header
   attribute form 'clef: treble' (with colon). The header form sets a part's
   initial value; the command form changes it mid-music. Both are intentional and
   each is consistent within its context (header = 'name: value', music = command). *)
MidMusicCommand = 'clef' , Identifier                 (* clef bass *)
               | 'key' , PitchBase , ( 'major' | 'minor' )
               | 'time' , Integer , '/' , Integer     (* time 4/4 *)
               | Tuplet | Grace | 'break' ;

### 8.2 Notes, Rests, Chords

Note           = Pitch , [ Duration ] , { Articulation } ;

Pitch          = PitchToken ;

Duration       = DurationToken ;

Rest           = RestType , [ Duration ] ;
RestType       = 'r' | 's' | 'R' ;

Chord          = '<' , Pitch , { Pitch } , '>' , [ Duration ] , { Articulation } ;

Barline        = '|' | '||' | '|.' | '|:' | RepeatEnd ;
RepeatEnd      = ':|' , [ '*' , Integer ] ;           (* :|*N plays the span N times, default 2 *)

(* First/second-time endings inside a |: … :| repeat. '[' followed by an integer is
   a volta; otherwise '[' … ']' is a Beam group. *)
InlineVolta    = '[' , Integer , [ ( '-' | ',' ) , Integer ] , '.' , { MusicItem } , ']' ;
Beam           = '[' | ']' ;

PhraseRef      = Identifier ;

### 8.3 Parallel Voices

ParallelExpr   = '<<' , MusicBlock , { '\\' , MusicBlock } , '>>' ;

(* Example:
   section Verse {
     piano {
       << { c'4 e' g' c'' } \\ { c4 e g c' } >>
     }
   }
*)

### 8.4 Articulations & Dynamics

Articulation   = '@' , ArticulationName
               | DynamicMark
               ;

(* ArticulationName is any identifier; it is resolved from text (not reserved as a
   keyword), so abbreviations and full names both work and names like 'tr' stay
   usable as ordinary identifiers. Known articulations/ornaments: *)
ArticulationName = 'staccato' ('stac') | 'accent' ('acc') | 'tenuto' ('ten')
               | 'marcato' ('marc') | 'fermata' ('ferm') | 'portato'
               | 'trill' ('tr') | 'mordent' | 'prall' | 'turn'
               | 'invertedturn' | 'pralltriller' ;

(* Dynamics take '@' (preferred, consistent with articulations) or '\' *)
DynamicMark    = ( '@' | '\' ) , DynamicLevel
               | ( '@' | '\' ) , DynamicChange
               ;

DynamicLevel   = 'ppp' | 'pp' | 'p' | 'mp' | 'mf' | 'f' | 'ff' | 'fff' ;
DynamicChange  = 'cresc' | 'decresc' | 'dim' ;

### 8.5 Slurs & Ties

Slur           = '(' | ')' ;
Tie            = '~' ;

================================================================================
## 9. Complete Example
================================================================================

```lilysharp
// Metadata
title "Rock Song"
composer "John Doe"

// Global settings
tempo 120
time 4/4
key c major

// Part definitions
part guitar {
  clef: treble
  instrument: "Electric Guitar"
}

part bass {
  clef: bass
  instrument: "Bass Guitar"
}

// Reusable phrases
phrase riff { e4 f g a | b c' d' e' | }
phrase groove { c,4 g, c, g, | c, g, c, g, | }

// Sections
section Intro {
  guitar { c4 d e f | g a b c' | }
  bass { groove }
}

section Verse {
  guitar { riff | g4 a b c' | d' e' f' g' | }
  bass { groove | e,4 b, e, b, | e, b, e, b, | }
}

section Chorus {
  guitar { c'4\f b a g | f e d c | }
  bass { c,4 g, c, g, | c,1 | }
}

section Bridge {
  key g major
  guitar { g4 a b c' | d' e' fis' g' | }
  bass { g,4 d, g, d, | g,4 d, g, d, | }
}

section Outro {
  tempo 100
  guitar { c'1\p~ | c'1 | }
  bass { c,1~ | c,1 | }
}

// Song structure
structure {
  Intro
  @segno
  |: Verse [1. Bridge] [2. Chorus @fine] :|
  ~Verse
  Outro _"molto rit."
  @ds.al.fine
}

// Output definitions
render score "rocksong-full.svg" {
  staff { guitar }
  staff { bass }
}

render score "guitar-part.pdf" {
  staff { guitar }
}

render midi "rocksong.mid" {
  guitar channel: 1 instrument: 27
  bass channel: 2 instrument: 33
}
```

================================================================================
## 10. Symbol Summary
================================================================================

### Structure Symbols

| Symbol     | Meaning                          | Example                |
|------------|----------------------------------|------------------------|
| `|: :|`    | Repeat block                     | `|: Verse :|`          |
| `x3`       | Repeat count                     | `|: Verse :| x3`       |
| `[1. ]`    | Volta (1st time)                 | `[1. Bridge]`          |
| `[1,3. ]`  | Volta (1st and 3rd)              | `[1,3. Bridge]`        |
| `[1-3. ]`  | Volta (1st through 3rd)          | `[1-3. Bridge]`        |
| `@`        | Music mark                       | `@segno` `@fine`       |
| `~`        | Silent section (no label)        | `~Verse`               |
| `_"..."`   | Custom text (end, below)         | `_"molto rit."`        |

### Music Mark Positions

| Mark             | Horizontal | Vertical |
|------------------|------------|----------|
| `@segno`         | beginning  | above    |
| `@coda`          | beginning  | above    |
| `@fine`          | end        | above    |
| `@ds` `@dc` etc. | end        | above    |
| `@rit` `@accel`  | end        | below    |
| `@cresc` etc.    | end        | below    |

================================================================================
## 11. Error Detection
================================================================================

### Required Elements

| Error        | Description                                    |
|--------------|------------------------------------------------|
| No section   | File must contain at least one `section` block |
| No structure | File must contain exactly one `structure` block|
| No render    | File must contain at least one `render` block  |

### Compile-time Errors

| Error                | Description                                    |
|----------------------|------------------------------------------------|
| Undefined section    | Section referenced in structure but not defined|
| Undefined phrase     | Phrase referenced but not defined              |
| Undefined part       | Part referenced in render but not in sections  |
| Inline music         | `{ }` in structure (not allowed)               |
| Forward reference    | Phrase/section used before definition          |
| Missing fine         | `@dc.al.fine` without `@fine`                  |
| Missing segno        | `@ds` without `@segno`                         |
| Missing coda         | `to coda` without `@coda`                      |

### Warnings

| Warning            | Description                                      |
|--------------------|--------------------------------------------------|
| Unused section     | Section defined but not in structure             |
| Unused phrase      | Phrase defined but never referenced              |
| Incomplete measure | Measure duration doesn't match time signature    |