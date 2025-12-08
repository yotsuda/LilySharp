# Lilysharp Grammar Specification
# Version: 0.1.0
# Date: 2024-12-08

## Design Principles

1. Explicit over implicit     - No hidden state
2. Locality                   - Each element independently parsable
3. Completion-friendly        - Context determines valid tokens
4. Visual correspondence      - Corresponds to sheet music visually
5. LilyPond respect           - Inherit practical conventions

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
IdentCont      = IdentStart | Digit ;

### Pitch Names

(* Base pitch *)
PitchBase      = 'c' | 'd' | 'e' | 'f' | 'g' | 'a' | 'b' ;

(* Accidentals - LilyPond compatible *)
Accidental     = 'is' | 'es' | 'isis' | 'eses'
               | 's'                              (* b -> bes shorthand *)
               | 'as' | 'aes'                     (* a-flat both allowed *)
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

Keyword        = 'score' | 'part' | 'staff' | 'voice'
               | 'relative' | 'absolute' | 'fixed'
               | 'repeat' | 'volta' | 'alternative'
               | 'let' | 'use'
               | 'title' | 'composer' | 'tempo' | 'time' | 'key' | 'clef'
               | 'verse' | 'notes' | 'lyrics' | 'chords'
               | 'major' | 'minor' | 'dorian' | 'phrygian' | 'lydian'
               | 'mixolydian' | 'aeolian' | 'locrian'
               | 'treble' | 'bass' | 'alto' | 'tenor' | 'percussion'
               | 'r'                              (* rest *)
               | 's'                              (* spacer rest *)
               ;

### Operators & Punctuation

Punctuation    = '{' | '}' | '(' | ')' | '<' | '>' | '[' | ']'
               | '|' | '~' | ':' | '=' | '/' | '@' | '\' | '-'
               ;

================================================================================
## 2. Syntactic Grammar
================================================================================

### 2.1 Top-Level Structure

File           = { TopLevelItem } ;

TopLevelItem   = MetadataDecl
               | VariableDecl
               | ScoreDecl
               | PartDecl
               | MusicExpr                        (* implicit single part *)
               ;

### Metadata

MetadataDecl   = MetadataKey , MetadataValue ;
MetadataKey    = 'title' | 'composer' | 'arranger' | 'copyright'
               | 'tagline' | 'dedication' ;
MetadataValue  = String | Integer ;

### Variables

VariableDecl   = 'let' , Identifier , '=' , MusicExpr ;
VariableRef    = 'use' , Identifier
               | '$' , Identifier ;              (* shorthand *)

### 2.2 Score & Part Structure

(* Score *)
ScoreDecl      = 'score' , [ String ] , '{' , { ScoreItem } , '}' ;

ScoreItem      = ScoreProperty
               | PartDecl
               | StaffGroup
               ;

ScoreProperty  = PropertyName , ':' , PropertyValue ;
PropertyName   = 'tempo' | 'time' | 'key' | 'pickup' ;
PropertyValue  = TempoValue | TimeValue | KeyValue | DurationToken ;

TempoValue     = Integer                          (* BPM *)
               | DurationToken , '=' , Integer    (* quarter = 120 *)
               ;
TimeValue      = Integer , '/' , Integer ;        (* 4/4, 3/4, 6/8 *)
KeyValue       = PitchBase , [ Accidental ] , Mode ;
Mode           = 'major' | 'minor' | 'dorian' | 'phrygian'
               | 'lydian' | 'mixolydian' | 'aeolian' | 'locrian' ;

(* Part *)
PartDecl       = 'part' , [ Identifier ] , [ String ] , '{' , { PartItem } , '}' ;

PartItem       = PartProperty
               | StaffDecl
               | VoiceDecl
               | MusicExpr                        (* implicit single staff *)
               ;

PartProperty   = 'clef' , ':' , ClefValue
               | 'instrument' , ':' , String
               | PropertyName , ':' , PropertyValue
               ;
ClefValue      = 'treble' | 'bass' | 'alto' | 'tenor'
               | 'treble_8' | 'bass^8' | 'percussion' ;

(* Staff & Voice *)
StaffDecl      = 'staff' , [ Identifier ] , '{' , { StaffItem } , '}' ;
StaffItem      = PartProperty | VoiceDecl | MusicExpr ;

VoiceDecl      = 'voice' , [ Identifier ] , '{' , MusicExpr , '}' ;

(* Staff Grouping *)
StaffGroup     = GroupType , '{' , { PartDecl | StaffGroup } , '}' ;
GroupType      = 'piano' | 'group' | 'choir' | 'grand' ;

### 2.3 Music Expression

MusicExpr      = RelativeExpr
               | AbsoluteExpr
               | SequentialExpr
               | ParallelExpr
               | RepeatExpr
               | VariableRef
               ;

RelativeExpr   = 'relative' , PitchToken , MusicBlock ;
AbsoluteExpr   = 'absolute' , MusicBlock
               | 'fixed' , PitchToken , MusicBlock ;
SequentialExpr = MusicBlock ;
ParallelExpr   = '<<' , MusicExpr , { '\\' , MusicExpr } , '>>' ;

MusicBlock     = '{' , { MusicItem } , '}' ;

MusicItem      = Note
               | Rest
               | Chord
               | Barline
               | InlineProperty
               | Slur
               | Tie
               | RepeatExpr
               | VariableRef
               | VerseBlock
               ;

### 2.4 Notes, Rests, Chords

(* Note *)
Note           = Pitch , [ Duration ] , { Articulation } , [ LyricAttach ] ;

Pitch          = PitchToken ;                     (* c, cis', des,, etc. *)

Duration       = DurationToken                    (* 4, 8., 16.. etc. *)
               | '*' , Fraction ;                 (* duration multiplier *)
Fraction       = Integer , [ '/' , Integer ] ;

(* Rest *)
Rest           = RestType , [ Duration ] ;
RestType       = 'r'                              (* normal rest *)
               | 's'                              (* spacer rest - invisible *)
               | 'R'                              (* full measure rest *)
               ;

(* Chord *)
Chord          = '<' , Pitch , { Pitch } , '>' , [ Duration ] , { Articulation } ;

(* Barline *)
Barline        = '|'                              (* normal *)
               | '||'                             (* double bar *)
               | '|.'                             (* final bar *)
               | '.|'                             (* opening double bar *)
               | ':|'                             (* left repeat *)
               | '|:'                             (* right repeat *)
               | ':|:'                            (* bidirectional repeat *)
               ;

### 2.5 Articulations & Ornaments

(* Articulation *)
Articulation   = '@' , ArticulationName
               | DynamicMark
               | OrnamentMark
               ;

ArticulationName = 'staccato' | 'stac'            (* . *)
                 | 'accent' | 'acc'               (* > *)
                 | 'tenuto' | 'ten'               (* - *)
                 | 'marcato' | 'marc'             (* ^ *)
                 | 'fermata' | 'ferm'             (* fermata symbol *)
                 | 'portato'                      (* -. *)
                 | 'staccatissimo' | 'stacc'      (* ' *)
                 | 'downbow' | 'upbow'
                 | 'trill' | 'turn' | 'mordent'
                 | 'prall' | 'prallmordent'
                 ;

(* Dynamics *)
DynamicMark    = '\' , DynamicLevel
               | '\' , DynamicChange
               ;
DynamicLevel   = 'ppp' | 'pp' | 'p' | 'mp' | 'mf' | 'f' | 'ff' | 'fff'
               | 'fp' | 'sf' | 'sfz' | 'rfz' ;
DynamicChange  = 'cresc' | 'decresc' | 'dim'
               | '<' | '>' ;                      (* hairpin *)

(* Ornaments - grace notes *)
OrnamentMark   = GraceNote | Tremolo | Glissando ;
GraceNote      = '\grace' , ( Note | Chord | MusicBlock )
               | '\acciaccatura' , ( Note | Chord )
               | '\appoggiatura' , ( Note | Chord )
               ;
Tremolo        = ':' , TremoloDuration ;          (* c4:32 = 32nd tremolo *)
TremoloDuration = '8' | '16' | '32' | '64' ;
Glissando      = '\gliss' ;

### 2.6 Slurs, Ties, Beams

(* Slur *)
Slur           = '('                              (* slur start *)
               | ')'                              (* slur end *)
               | '\(' | '\)'                      (* phrasing slur *)
               ;

(* Tie *)
Tie            = '~' ;                            (* after note: c4~ c4 *)

(* Beam *)
BeamControl    = '['                              (* beam start *)
               | ']'                              (* beam end *)
               ;

(* Connectors attached after Note *)
NoteWithConnectors = Note , [ Tie ] , [ Slur ] , [ BeamControl ] ;

### 2.7 Repeat Structure

RepeatExpr     = 'repeat' , RepeatType , Integer , MusicBlock , [ Alternative ] ;

RepeatType     = 'volta'                          (* with repeat signs *)
               | 'unfold'                         (* expand *)
               | 'percent'                        (* % sign *)
               | 'tremolo'
               ;

Alternative    = 'alternative' , '{' , { MusicBlock } , '}' ;

(* Example: repeat volta 2 { c4 d e f } alternative { { g2 } { g4 a } } *)

### 2.8 Lyrics & Chord Names

(* Inline Lyrics *)
LyricAttach    = '(' , String , ')' ;             (* c4("Hel") d("lo") *)

(* Verse Block - Table Format *)
VerseBlock     = 'verse' , [ Integer | Identifier ] , '{' , { VerseLine } , '}' ;

VerseLine      = 'notes' , ':' , MusicBlock
               | 'lyrics' , ':' , LyricLine
               | 'chords' , ':' , ChordLine
               ;

LyricLine      = '|' , { LyricSyllable } , '|' , { '|' , { LyricSyllable } , '|' } ;
LyricSyllable  = String                           (* "Hel" *)
               | '-'                              (* continuation of previous *)
               | '_'                              (* melisma *)
               | '___'                            (* long melisma *)
               ;

(* Chord Symbols *)
ChordLine      = '|' , { ChordSymbol } , '|' , { '|' , { ChordSymbol } , '|' } ;
ChordSymbol    = '@' , ChordRoot , [ ChordQuality ] , [ ChordExtension ] , [ ChordBass ]
               | '-'                              (* continuation *)
               ;

ChordRoot      = 'C' | 'D' | 'E' | 'F' | 'G' | 'A' | 'B'
               | PitchBase , [ '#' | 'b' ]        (* uppercase preferred *)
               ;
ChordQuality   = 'm' | 'min' | 'maj' | 'dim' | 'aug' | 'sus2' | 'sus4' ;
ChordExtension = '6' | '7' | 'maj7' | '9' | '11' | '13'
               | 'add9' | 'add11' ;
ChordBass      = '/' , ChordRoot ;                (* C/G = C over G bass *)

(* Examples: @Cmaj7, @Dm7, @G7sus4, @C/E *)

### 2.9 Inline Property Changes

InlineProperty = KeyChange | TimeChange | TempoChange | ClefChange ;

KeyChange      = '\key' , PitchBase , [ Accidental ] , Mode ;
TimeChange     = '\time' , Integer , '/' , Integer ;
TempoChange    = '\tempo' , TempoValue ;
ClefChange     = '\clef' , ClefValue ;

(* Note: These use backslash for mid-piece changes *)
(* File-level declarations use property: value format *)

================================================================================
## 3. Sample Files
================================================================================

### Simple Song

```lilysharp
// Simple song
title "Happy Birthday"
composer "Traditional"
tempo 120
time 3/4
key g major

part Vocal {
    clef: treble

    relative d' {
        \partial 8
        d8 |
        d4 e d | g2 fis4 |
        d4 d e | d2 a'4 |
        d4 d d' | b2 g4 |
        a4 a g | d2. |
    }
}
```

### With Inline Lyrics

```lilysharp
part Vocal {
    relative c' {
        | c4("Hap") c("py") d("birth") | c2("day") f4("to") | e2.("you") |
    }
}
```

### With Table Format Lyrics

```lilysharp
part Vocal {
    verse 1 {
        notes:  | c'4   c     d     | c2    f4    | e2.         |
        lyrics: | "Hap" "py"  "birth"| "day" "to"  | "you___"    |
        chords: | @C    -     -     | @F    -     | @C          |
    }
}
```

### Piano Score

```lilysharp
score "Fur Elise" {
    tempo: 76
    time: 3/8
    key: a minor

    part Piano {
        staff RH {
            clef: treble
            relative c'' {
                | e8 dis e dis e b d c | a4. r8 c e |
                | a8 b c e, gis b | c4. r8 e, e' |
            }
        }
        staff LH {
            clef: bass
            relative c {
                | r4. a8 e' a | c4. e,8 e' gis |
                | a4. e,8 e' e | a4. r4. |
            }
        }
    }
}
```

### With Variables

```lilysharp
let theme = relative c'' {
    | c4 d e f | g2 g |
}

let variation = relative c'' {
    | c8 d c d e f e f | g4 a g2 |
}

score {
    part Violin {
        use theme
        use variation
        use theme
    }
}
```

### Repeat with Alternatives

```lilysharp
part {
    relative c' {
        repeat volta 2 {
            | c4 d e f | g2 g |
        }
        alternative {
            { | a4 b c2 | }
            { | a4 g f2 | }
        }
    }
}
```

### Articulations and Dynamics

```lilysharp
part {
    relative c' {
        | c4@staccato d@accent e@tenuto f@fermata |
        | c4\p d e\cresc f |
        | g4\f a b\dim c |
        | c1\pp |
    }
}
```

### Parallel Voices

```lilysharp
part {
    staff {
        <<
            relative c'' { c2 d | e2 f }
        \\
            relative c'  { e2 f | g2 a }
        >>
    }
}
```

================================================================================
## 4. Undecided Items
================================================================================

| Item                | Proposal    | Reason                              |
|---------------------|-------------|-------------------------------------|
| File extension      | .lys        | LilySharp abbreviation, distinct from .ly |
| Duration inheritance| Allow       | LilyPond compatible, efficient input |
| Dynamics prefix     | \p \f       | Visually distinct, LilyPond compatible |
| Multi-language pitch| Future      | do re mi switchable via settings    |

================================================================================
## 5. Completion Context Mapping
================================================================================

[Top level]        -> score, part, title, composer, tempo, time, key
[score block]      -> part, title, tempo, time, key
[part block]       -> clef, staff, relative, absolute
[relative after]   -> base pitch: c, c', c'', c,
[music block]      -> note names, r, <, (, repeat, variables
[after @]          -> staccato, accent, fermata, ...
[after \]          -> p, pp, f, ff, mp, mf, cresc, dim
[key: after]       -> c, d, e, f, g, a, b
[pitch after]      -> major, minor, dorian, phrygian, ...
[clef: after]      -> treble, bass, alto, tenor

================================================================================
## 6. Real-time Validation
================================================================================

Measure checking example:

    time: 4/4

    | c4 d e f |  <- [████████████] 4/4 ✓
    | g4 a b |    <- [████████░░░░] 3/4 ⚠ remaining 1/4
    | c4 d e |    <- [████████░░░░] 3/4 (typing...)
                  ^ cursor position

LSP Diagnostics:
- Measure incomplete: warning with remaining duration
- Measure overflow: error with excess duration
- Undefined time signature: error
- Real-time feedback as user types
