# Lily# Grammar Specification
# Version: 0.4.0
# Date: 2025-12-09

## Design Principles

1. Single-pass compilation  - No forward references, immediate error detection
2. Explicit over implicit   - No hidden state, no optional structure
3. Locality                 - Each element independently parsable
4. Visual correspondence    - Corresponds to sheet music visually
5. LilyPond inspiration     - Inherit practical conventions, not Scheme complexity
6. Section-oriented         - Organize by musical sections, not just by parts

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

(* Base pitch *)
PitchBase      = 'c' | 'd' | 'e' | 'f' | 'g' | 'a' | 'b' ;

(* Accidentals *)
Accidental     = 'is' | 'ees' | 'isis' | 'eeses'
               | 'aes'                            (* a-flat *)
               | 'bes'                            (* b-flat *)
               ;

(* Octave marks *)
OctaveUp       = { '\'' }+ ;                      (* ' = 1 octave up *)
OctaveDown     = { ',' }+ ;                       (* , = 1 octave down *)
Octave         = OctaveUp | OctaveDown | ε ;

(* Complete pitch token *)
PitchToken     = PitchBase , [ Accidental ] , Octave ;

### Duration

DurationBase   = '1' | '2' | '4' | '8' | '16' | '32' | '64' | '128'
               | 'breve' | 'longa' ;
Dots           = { '.' }+ ;
DurationToken  = DurationBase , [ Dots ] ;

### Keywords

(* Structure keywords - no backslash *)
StructureKeyword = 'section' | 'structure' | 'render'
                 | 'staff' | 'tab' | 'voice'
                 | 'title' | 'composer' | 'tempo' | 'time' | 'key' | 'clef'
                 | 'transpose' | 'octave' | 'instrument' | 'channel'
                 | 'major' | 'minor' | 'dorian' | 'phrygian' | 'lydian'
                 | 'mixolydian' | 'aeolian' | 'locrian'
                 | 'treble' | 'bass' | 'alto' | 'tenor' | 'percussion'
                 | 'r' | 's'                      (* rests *)
                 ;

(* Dynamic keywords - with backslash *)
DynamicKeyword = '\p' | '\pp' | '\ppp' | '\mp'
               | '\f' | '\ff' | '\fff' | '\mf'
               | '\fp' | '\sf' | '\sfz' | '\rfz'
               | '\cresc' | '\decresc' | '\dim'
               ;

(* Structure control *)
StructureControl = 'segno' | 'fine' | 'coda' | 'dc' | 'ds' | 'al' | 'to' ;

### Operators & Punctuation

Punctuation    = '{' | '}' | '(' | ')' | '<' | '>' | '[' | ']'
               | '|' | '~' | ':' | '=' | '/' | '@' | '\' | '-'
               | '|:' | ':|' | ':|:'              (* repeat signs *)
               ;

================================================================================
## 2. File Structure (All Required)
================================================================================

### 2.1 Top-Level Structure

(* A valid file MUST contain: at least one section, one structure, one render *)

File           = { TopLevelItem } ;

TopLevelItem   = MetadataDecl                     (* title, composer, etc. *)
               | GlobalSetting                    (* tempo, time, key *)
               | VariableDecl                     (* reusable music fragments *)
               | SectionDecl                      (* musical sections - REQUIRED *)
               | StructureDecl                    (* song form - REQUIRED *)
               | RenderDecl                       (* output definitions - REQUIRED *)
               ;

(* Validation: File must contain at least one SectionDecl, one StructureDecl, one RenderDecl *)

### 2.2 Metadata

MetadataDecl   = MetadataKey , String ;
MetadataKey    = 'title' | 'composer' | 'arranger' | 'copyright' | 'tagline' ;

(* Example:
   title "My Song"
   composer "John Doe"
*)

### 2.3 Global Settings

GlobalSetting  = TempoDecl | TimeDecl | KeyDecl ;

TempoDecl      = 'tempo' , Integer ;              (* BPM *)
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
## 3. Variables
================================================================================

### 3.1 Variable Definition

(* Reusable music fragments - must be defined before use *)
(* Variables may NOT contain key/tempo/time/clef changes - pure music only *)

VariableDecl   = Identifier , '=' , MusicBlock ;

(* Example:
   guitar_riff = { c4 d e f | g a b c' }
   bass_line = { c,4 g, c, g, }
*)

(* Error: Variables cannot contain key/tempo/time/clef changes
   bad_var = { c4 d | key g major | e4 f }   // ERROR
*)

### 3.2 Variable Reference

VariableRef    = Identifier ;

(* Variables can be referenced in section part blocks *)

================================================================================
## 4. Section-Oriented Structure
================================================================================

### 4.1 Section Definition

(* Musical sections with rehearsal marks - at least one required *)
SectionDecl    = 'section' , Identifier , '{' , { SectionItem } , '}' ;

SectionItem    = SectionSetting                   (* key, tempo at section start *)
               | PartBlock                        (* instrument parts *)
               ;

SectionSetting = KeyDecl | TempoDecl | TimeDecl ;

PartBlock      = Identifier , [ PartOptions ] , MusicBlock ;

PartOptions    = { PartOption } ;
PartOption     = 'transpose' , ':' , TransposeValue
               | 'octave' , ':' , Integer
               | 'instrument' , ':' , InstrumentName
               | 'clef' , ':' , ClefValue
               ;

TransposeValue = PitchToken                       (* transpose:bes for Bb instruments *)
               | Integer                          (* transpose:-2 for semitones *)
               ;

InstrumentName = Identifier | String ;

ClefValue      = 'treble' | 'bass' | 'alto' | 'tenor' | 'percussion' ;

(* Example:
   section Intro {
     key c major
     tempo 120
     guitar { c4 d e f | g a b c' }
     bass { c,4 g, c, g, | c, g, c, g, }
   }

   section A {
     key g major
     guitar { g4 a b c' | d' e' fis' g' }
     bass { g,4 d, g, d, | g, d, g, d, }
   }
*)

### 4.2 Mid-Section Changes

(* Key, tempo, time, clef can change mid-section inside part blocks *)
(* Changes affect all parts from that point - parts should have matching bar positions *)
(* Warning issued if key/tempo changes occur at different bar positions across parts *)

MidSectionChange = 'key' , PitchToken , Mode
                 | 'tempo' , Integer
                 | 'time' , Integer , '/' , Integer
                 | 'clef' , ClefValue
                 ;

(* Example - correct usage (changes at same bar position):
   section A {
     key c major
     tempo 120
     
     guitar {
       c4 d e f | g a b c' |
       key g major                    // bar 3: key change
       tempo 140                      // bar 3: tempo change
       g4 a b c' | d' e' fis' g' |
     }
     
     bass {
       c,4 g, c, g, | c, g, c, g, |
       key g major                    // bar 3: must match guitar
       tempo 140
       g,4 d, g, d, | g, d, g, d, |
     }
   }
*)

(* Warning example - mismatched bar positions:
   section A {
     guitar { c4 d e f | key g major | g4 a b c' | }    // key at bar 2
     bass   { c,4 g, | c,4 g, | key g major | g,4 d, | } // key at bar 3
   }
   // Warning: Key change in 'guitar' at bar 2, but 'bass' at bar 3
*)

### 4.3 Structure Definition (Required)

(* Song form with repeat signs - 1:1 correspondence with music notation *)
(* Exactly one structure block required per file *)

StructureDecl  = 'structure' , '{' , { StructureItem } , '}' ;

StructureItem  = SectionRef                       (* section name *)
               | RepeatBlock                      (* |: ... :| *)
               | NavigationMark                   (* segno, fine, coda, etc. *)
               ;

SectionRef     = Identifier ;

RepeatBlock    = '|:' , { StructureItem } , ':|'
               | '|:' , { StructureItem } , '|' , Alternatives , ':|' , AlternativeEnd
               ;

Alternatives   = Alternative , { Alternative } ;
Alternative    = Integer , '.' , SectionRef ;     (* 1. A, 2. B *)
AlternativeEnd = Integer , '.' , SectionRef ;     (* final alternative *)

NavigationMark = 'segno'                          (* 𝄋 sign *)
               | 'fine'                           (* Fine *)
               | 'coda'                           (* ⊕ Coda *)
               | 'to' , 'coda'                    (* To Coda *)
               | 'dc'                             (* D.C. *)
               | 'dc' , 'al' , 'fine'             (* D.C. al Fine *)
               | 'dc' , 'al' , 'coda'             (* D.C. al Coda *)
               | 'ds'                             (* D.S. *)
               | 'ds' , 'al' , 'fine'             (* D.S. al Fine *)
               | 'ds' , 'al' , 'coda'             (* D.S. al Coda *)
               ;

(* Example:
   structure {
     Intro
     |: A :|
     |: B | 1. A :| 2. C
     fine
   }
*)

================================================================================
## 5. Output Definition (Required)
================================================================================

### 5.1 Render Declaration

(* At least one render block required per file *)

RenderDecl     = 'render' , Identifier , String , '{' , { RenderItem } , '}' ;

(* Identifier = render name for --render command line option *)
(* String = output filename, extension determines format *)

RenderItem     = StaffRender | TabRender | MidiPart ;

StaffRender    = 'staff' , [ ClefOption ] , '{' , PartRef , '}' ;
TabRender      = 'tab' , TuningOption , '{' , PartRef , '}' ;
MidiPart       = PartRef , [ MidiOptions ] ;

ClefOption     = Identifier                       (* bass, treble, etc. *)
               | 'clef' , ':' , ClefValue
               ;

TuningOption   = Identifier                       (* guitar, bass, ukulele *)
               | 'tuning' , ':' , TuningValue
               ;

TuningValue    = 'guitar' | 'bass' | 'bass5' | 'ukulele' ;

PartRef        = Identifier ;                     (* part name from sections *)

MidiOptions    = { MidiOption } ;
MidiOption     = 'channel' , ':' , Integer        (* 1-16 *)
               | 'instrument' , ':' , Integer     (* GM program 1-128 *)
               | 'octave' , ':' , Integer         (* octave shift for playback *)
               ;

(* Examples:

   // Full band score
   render full "mysong-full.svg" {
     staff { guitar }
     tab guitar { guitar }
     staff bass { bass }
   }

   // Guitar part only
   render guitarPart "mysong-guitar.pdf" {
     staff { guitar }
     tab guitar { guitar }
   }

   // MIDI output
   render audio "mysong.mid" {
     guitar channel:1 instrument:25
     bass channel:2 instrument:33 octave:-1
   }
*)

### 5.2 Output Formats

(* Determined by file extension *)

| Extension | Format | Description           |
|-----------|--------|-----------------------|
| .svg      | SVG    | Web display, preview  |
| .pdf      | PDF    | Print                 |
| .png      | PNG    | Image embedding       |
| .mid      | MIDI   | Playback, DAW         |

================================================================================
## 6. Music Expression
================================================================================

### 6.1 Music Expression Types

MusicExpr      = SequentialExpr
               | ParallelExpr
               | VariableRef
               ;

SequentialExpr = MusicBlock ;
ParallelExpr   = '<<' , MusicExpr , { '\\' , MusicExpr } , '>>' ;

### 6.2 Implicit Relative Mode

(* All music is in relative mode. Reference pitch is determined by clef. *)
(* Absolute pitch mode is NOT supported - all pitches are relative. *)

| Clef       | Reference Pitch | Description        |
|------------|-----------------|-------------------|
| treble     | c' (middle C)   | Standard treble   |
| bass       | c, (octave below)| Standard bass    |
| alto       | c (middle C)    | Viola range       |
| tenor      | c (middle C)    | Tenor range       |
| percussion | (N/A)           | Unpitched         |

(* Octave marks ' and , are relative to previous note, as in LilyPond *)
(* The interval to the next note is always the smallest possible. *)
(* Use ' to force up, , to force down when the interval is a 4th or more. *)

MusicBlock     = '{' , { MusicItem } , '}' ;

MusicItem      = Note
               | Rest
               | Chord
               | Barline
               | MidSectionChange                 (* key, tempo, time, clef *)
               | Slur
               | Tie
               | VariableRef
               ;

### 6.2 Notes, Rests, Chords

(* Note *)
Note           = Pitch , [ Duration ] , { Articulation } ;

Pitch          = PitchToken ;

Duration       = DurationToken
               | '*' , Fraction ;
Fraction       = Integer , [ '/' , Integer ] ;

(* Rest *)
Rest           = RestType , [ Duration ] ;
RestType       = 'r'                              (* normal rest *)
               | 's'                              (* spacer rest - invisible *)
               | 'R'                              (* full measure rest *)
               ;

(* Chord *)
Chord          = '<' , Pitch , { Pitch } , '>' , [ Duration ] , { Articulation } ;

(* Barline - REQUIRED, not optional *)
(* Unlike LilyPond where barlines are hints, LilySharp requires explicit barlines. *)
(* Parser will error if measure duration doesn't match time signature. *)
Barline        = '|'                              (* normal *)
               | '||'                             (* double bar *)
               | '|.'                             (* final bar *)
               ;

### 6.3 Articulations & Dynamics

Articulation   = '@' , ArticulationName
               | DynamicMark
               ;

ArticulationName = 'staccato' | 'stac'
                 | 'accent' | 'acc'
                 | 'tenuto' | 'ten'
                 | 'marcato' | 'marc'
                 | 'fermata' | 'ferm'
                 | 'portato'
                 | 'staccatissimo' | 'stacc'
                 ;

(* Dynamics use backslash - visually distinct from structure *)
DynamicMark    = '\' , DynamicLevel
               | '\' , DynamicChange
               ;
DynamicLevel   = 'ppp' | 'pp' | 'p' | 'mp' | 'mf' | 'f' | 'ff' | 'fff'
               | 'fp' | 'sf' | 'sfz' | 'rfz' ;
DynamicChange  = 'cresc' | 'decresc' | 'dim' ;

### 6.4 Slurs, Ties, Beams

Slur           = '(' | ')' ;
Tie            = '~' ;
BeamControl    = '[' | ']' ;

================================================================================
## 7. Backslash Usage Summary
================================================================================

(* Only dynamics use backslash - everything else is plain keywords *)

| Category         | Syntax              | Example                |
|------------------|---------------------|------------------------|
| Dynamics         | \p \f \ff \mf etc.  | c4\f d\p e\cresc       |
| Key signature    | key                 | key g major            |
| Time signature   | time                | time 3/4               |
| Tempo            | tempo               | tempo 120              |
| Clef             | clef                | clef bass              |

================================================================================
## 8. Complete Example
================================================================================

```lilysharp
// Metadata
title "Rock Song"
composer "John Doe"

// Global settings
tempo 120
time 4/4
key c major

// Reusable variables (pure music only, no key/tempo changes)
guitar_riff = { e4 f g a | b c' d' e' }
bass_groove = { c,4 g, c, g, | c, g, c, g, }

// Sections (required - at least one)
section Intro {
  guitar { c4 d e f | g a b c' }
  bass { bass_groove }
}

section A {
  guitar { 
    guitar_riff |
    key g major                       // mid-section key change
    tempo 140                         // mid-section tempo change
    g4\f a b c' | d' e' fis' g' |
  }
  bass { 
    e,4 b, e, b, | e, b, e, b, |
    key g major                       // must match guitar's bar position
    tempo 140
    g,4 d, g, d, | g, d, g, d, |
  }
}

section B {
  guitar { g8\ff a b c' d' e' fis' g' | a' g' fis' e' d' c' b a | }
  bass { g,4 d, g, d, | g, d, g, d, | }
}

section Outro {
  tempo 100
  guitar { c'1\p }
  bass { c,1 }
}

// Song structure (required - exactly one)
structure {
  Intro
  |: A :|
  |: B | 1. A :| 2. Outro
  fine
}

// Output definitions (required - at least one)
render full "rocksong-full.svg" {
  staff { guitar }
  tab guitar { guitar }
  staff bass { bass }
}

render guitarPart "rocksong-guitar.pdf" {
  staff { guitar }
  tab guitar { guitar }
}

render bassPart "rocksong-bass.pdf" {
  staff bass { bass }
}

render bassTab "rocksong-bass-tab.pdf" {
  tab bass { bass }
}

render audio "rocksong.mid" {
  guitar channel:1 instrument:25 octave:-1
  bass channel:2 instrument:33 octave:-1
}
```

================================================================================
## 9. Error Detection
================================================================================

### Required Elements

| Error | Description |
|-------|-------------|
| No section | File must contain at least one `section` block |
| No structure | File must contain exactly one `structure` block |
| No render | File must contain at least one `render` block |

### Compile-time Errors

| Error | Description |
|-------|-------------|
| Undefined section | Section referenced in structure but not defined |
| Undefined variable | Variable referenced but not defined |
| Undefined part | Part referenced in render but not in sections |
| Forward reference | Variable/section used before definition |
| Variable with key/tempo | Variables cannot contain key/tempo/time/clef changes |
| Missing fine | `dc al fine` without `fine` |
| Missing segno | `ds` without `segno` |
| Missing coda | `to coda` without `coda` |
| Duplicate fine | Multiple `fine` marks |
| Duplicate segno | Multiple `segno` marks (unnamed) |
| Invalid alternative | Alternative outside repeat block |
| Unreachable section | Section after `dc al fine` (except coda) |

### Warnings

| Warning | Description |
|---------|-------------|
| Unused section | Section defined but not in structure |
| Unused variable | Variable defined but never referenced |
| Incomplete measure | Measure duration doesn't match time signature |
| Missing alternative | `1.` without `2.` |
| Mismatched key/tempo | Key/tempo change at different bar positions across parts |

================================================================================
## 10. Future Considerations
================================================================================

| Item | Status | Notes |
|------|--------|-------|
| Named segno/coda | Future | For complex navigation |
| Custom tunings | Future | User-defined string tunings |
| Lyrics | Planned | Verse/chorus lyrics alignment |
| Chord symbols | Planned | Lead sheet style |
| Drum notation | Future | Percussion staff |
| Multi-language pitch | Future | do re mi support |