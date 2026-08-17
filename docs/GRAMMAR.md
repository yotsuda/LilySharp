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
Decimal        = Digit , { Digit } , '.' , Digit , { Digit } ;
Digit          = '0' | '1' | '2' | '3' | '4' | '5' | '6' | '7' | '8' | '9' ;
String         = '"' , { StringChar } , '"' ;

(* A Decimal REQUIRES a digit after the point, and that is what keeps it out of every
   dot the grammar already spells: the augmentation dot (c4. / R2.*3 / partial 2. /
   tempo 4. = 116) is followed by a space, a '*', an '=' or end-of-line, and the grob
   property separator by a letter. So `4.` is Integer + '.', while `4.5` is one Decimal.
   A leading point is not a number either: `.5` is '.' followed by Integer.
   A Decimal is only accepted in a VALUE position (see OverrideValue and the part
   properties) — a duration, a tuplet ratio, a volta number and a repeat count are whole
   by construction, and writing `c4.5` is LYS0021 rather than a silent dotted quarter. *)

### Identifiers

Identifier     = IdentStart , { IdentCont } ;
IdentStart     = UnicodeLetter | '_' ;          (* any Unicode letter, e.g. 動機 *)
IdentCont      = IdentStart | Digit | '-' ;
(* A name may CONTAIN or END with digits (melody2, foo2bar) but must not START
   with one: a leading digit is a duration (c4) or scale degree (<1 3 5>), so
   'phrase 2foo { }' is rejected with LYS0017 "a name cannot start with a digit". *)

### Pitch Names

PitchBase      = 'c' | 'd' | 'e' | 'f' | 'g' | 'a' | 'b' ;
Accidental     = 'is' | 'es' | 'isis' | 'eses' ;   (* sharp / flat / double *)
OctaveUp       = { '\'' }+ ;
OctaveDown     = { ',' }+ ;
Octave         = OctaveUp | OctaveDown | ε ;
PitchToken     = PitchBase , [ Accidental ] , Octave ;

(* Octaves default to RELATIVE: each bare pitch lands in the octave nearest the
   previous pitch (an interval of a fourth or less), then any '/',' marks shift it;
   the frame resets to the part's base at each section boundary and phrase call.
   `octave absolute` (top-level, part header, or mid-music) switches to ABSOLUTE
   mode: bare c = C4, and '/',' are absolute offsets from that anchor (c' = C5,
   c, = C3) with NO carry between notes — octave mistakes cannot cascade, which
   is the recommended mode for AI-generated scores. `part X { octave N }`
   re-anchors the absolute base (e.g. a bass part with `octave 2`). Section
   boundaries restore the file-level mode. *)

### Duration

DurationBase   = '1' | '2' | '4' | '8' | '16' | '32' | '64' | '128' ;
Dots           = { '.' }+ ;
Tremolo        = ':' , ( '8' | '16' | '32' ) ;    (* stem tremolo: 1-3 beams *)
DurationToken  = DurationBase , [ Dots ] , [ Tremolo ] ;

### Keywords

Keyword = 'title' | 'composer' | 'tempo' | 'time' | 'key' | 'clef'
        | 'part' | 'phrase' | 'section' | 'form' | 'score'
        | 'staff' | 'grandStaff' | 'staffGroup' | 'choirStaff'
        | 'condensedStaff' | 'combinedStaff' | 'tab' | 'ossia' | 'voice'
        | 'lyrics' | 'chords' | 'tuning' | 'instrument' | 'percussion' | 'drummap'
        | 'transpose' | 'octave' | 'using' | 'break' | 'nobreak' | 'partial'
        | 'tuplet' | 'grace' | 'acciaccatura' | 'appoggiatura' | 'cue'
        | 'repeat' | 'volta' | 'alternative' | 'embedded' | 'font'
        | 'override' | 'revert' | 'once' | 'with'
        | 'major' | 'minor' | 'ionian' | 'dorian' | 'phrygian' | 'lydian' | 'mixolydian'
        | 'aeolian' | 'locrian'
        | 'treble' | 'bass' | 'alto' | 'tenor' | 'treble_8' | 'bass_8'
        | 'soprano' | 'mezzosoprano' | 'baritone'
        | 'segno' | 'fine' | 'coda' | 'dc' | 'ds' | 'al' | 'to' | 'tocoda'
        | 'ppp' | 'pp' | 'p' | 'mp' | 'mf' | 'ff' | 'fff'
        ;

(* The four clef-name words (treble bass alto tenor) ARE allowed as part / section /
   phrase names. Single letters a-g are pitches ('f' is a pitch, not a keyword — @f
   resolves the dynamic from text); r / R / s are rests. The reserved dynamic words
   above (p, pp, mp, …) cannot be identifiers. 'swing'/'shuffle' are NOT reserved
   (tempo value words). Articulation, ornament, dynamic-text and mark NAMES
   (staccato, tr, sfz, cresc, dim, …) are resolved from the '@name' text and are
   NOT reserved. 'volta'/'alternative' are reserved only to reject the removed
   LilyPond-style forms; 'using' is reserved for multi-file support.

   ⚠️ This list is one table in the implementation (Lexer.GetKeywordKind) and was
   MEASURED against it on 2026-08-16, word by word, by asking whether each can name a
   part: 'structure', 'use' and 'let' were listed here and are NOT reserved (they name
   a part fine — 'structure' has not been a keyword since it was renamed to 'form'),
   and sixteen words that ARE reserved were missing. The words a-g reach that table
   only through the '@name' path, which is why 'f' is in it and not here. *)

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
               | FontDecl                         (* text fonts, per role *)
               | GlobalSetting                    (* tempo, time, key *)
               | PartDecl                         (* part definitions *)
               | PhraseDecl                       (* reusable music fragments *)
               | SectionDecl                      (* musical sections - REQUIRED *)
               | StructureDecl                    (* song form - optional *)
               | ScoreDecl                        (* output definitions - REQUIRED *)
               | OverrideDecl                     (* engraving overrides *)
               ;

(* MUSIC IS NOT A TopLevelItem, and is rejected with LYS0020: a note stream, a bare
   '{ … }' block, a grace/tuplet group, a 'break', and a '$phrase' reference all belong
   inside a part, reached through a section. The parser accepted a headerless note stream
   for a long time without the grammar ever listing it; that permissiveness is closed.

   This is what makes GlobalSetting unambiguous. A top-level clef/key/time/tempo is ALWAYS
   the file default, because no music can stand beside it to make the same spelling mean a
   mid-music change instead — which is exactly what it used to mean when written after a
   top-level note, and what each of the four directives was independently got wrong. *)

### 2.2 Metadata

MetadataDecl   = MetadataKey , String ;
MetadataKey    = 'title' | 'composer' ;

### 2.3 Global Settings

GlobalSetting  = TempoDecl | TimeDecl | KeyDecl | PartialDecl | OctaveDecl ;

PartialDecl    = 'partial' , DurationToken ;
                 (* the piece-opening pickup, declared ONCE for every part; an
                    in-music 'partial' declares it per voice (or mid-piece).
                    A bare underfull first bar gets a warning suggesting this. *)
OctaveDecl     = 'octave' , ( 'absolute' | 'relative' ) ;

TempoDecl      = 'tempo' , [ Marking ] , [ DurationBase , [ Dots ] , '=' ] , [ Integer ] ,
                 [ FeelWord , [ Integer ] ] ;
Marking        = String | Identifier ;      (* a bare word only in the FIRST position *)
FeelWord       = 'swing' | 'shuffle' ;      (* contextual, NOT reserved words *)
                 (* tempo 120 / tempo "Allegro" / tempo "Allegro" 120 /
                    tempo "Andante" 4 = 96 / tempo "Lively" 4. = 116 /
                    tempo Comodo 4 = 84 — a bare word is a marking only where a marking
                    can start, so a trailing feel word is never swallowed by it.
                    'tempo 120 swing' draws a shuffle-feel equation; 'swing 16' = 16th
                    swing; a bare feel word means eighths. *)
                 (* HOW THE RUN IS READ (one pass, LysValue's neighbour TempoValue):
                    - bpm        = the LAST integer, stopping at a feel word, so the 16
                                   of `swing 16` is not the tempo. Last, not first,
                                   because `4 = 120` puts the beat unit first.
                    - beat unit  = the last integer BEFORE the '=', a quarter if the '='
                                   has none before it, and ABSENT with no '=' at all —
                                   `tempo 140` is a speed, not a 140th-note beat.
                    - beat dots  = the dots after that unit.
                    - marking    = the bare word in the first position, else the string.
                    Every one of those is pinned in TempoValueTests; changing one changes
                    what existing scores mean. A Decimal anywhere in the run is LYS0022 —
                    a metronome mark is whole and a beat unit is a note value. *)
TimeDecl       = 'time' , Integer , '/' , Integer ;
KeyDecl        = 'key' , PitchBase , [ Accidental-text ] , Mode ;

Mode           = 'major' | 'minor' | 'ionian' | 'dorian' | 'phrygian'
               | 'lydian' | 'mixolydian' | 'aeolian' | 'locrian' ;

(* Example:
   title "My Song"
   composer "Jane Doe"
   tempo 120
   time 4/4
   key c major
*)

### 2.4 Text Fonts

FontDecl       = 'font' , ( String , [ 'embedded' ] | FontBlock ) ;
FontBlock      = '{' , { FontEntry } , '}' ;
FontEntry      = FontKey , ( String , { String } | GenericFamily )
               | 'embedded' ;
FontKey        = GenericFamily | RoleGroup | Role ;
GenericFamily  = 'serif' | 'sans' | 'sans-serif' ;
RoleGroup      = 'header' | 'lyrics' | 'chords' | 'marks' | 'numbers' | 'notation' ;
Role           = 'title' | 'composer' | 'instrument'          (* header  *)
               | 'lyricText' | 'stanza'                       (* lyrics  *)
               | 'chordName' | 'fretFrame' | 'figuredBass'    (* chords  *)
               | 'tempo' | 'mark' | 'pedal' | 'navigation'
               | 'text' | 'dynamics' | 'partCombine'          (* marks   *)
               | 'barNumber' | 'fingering' | 'tuplet' | 'volta'
               | 'ottava' | 'bend' | 'tabTechnique'           (* numbers *)
               | 'clefOctave' | 'meter' | 'tabFret' ;         (* notation *)

(* WHICH FACE A STRING IS DRAWN IN is decided in this order, first hit winning:
     1. the role's own binding          font { lyricText "Charis SIL" }
     2. its group's binding             font { lyrics    "Charis SIL" }
     3. the generic family it belongs to    font { serif "Georgia" }
     4. the bundled face                (TeX Gyre Schola / Heros)
   The NARROWER spelling wins wherever both are written, in either source order, so
   `marks "Georgia"  tempo "Playfair Display"` needs no special case. Case-insensitive:
   `lyrictext` binds `lyricText`.

   `font "NAME" [embedded]` is the whole-document shorthand and means step 3 for BOTH
   generic families — unchanged from before the block form existed, with one exception
   below.

   ⚠️ NOTATION IS OUTSIDE STEPS 3 AND 4. The octave digit under `treble_8`, a compound
   meter's '+', and tab fret numbers are notation that happens to be drawn as text, so
   `font "NAME"` and a `serif`/`sans` binding do NOT restyle them; they follow a face only
   when `notation` or the leaf itself is named. `font "NAME"` reached all three before
   2026-08-18.

   SEVERAL NAMES ARE A FALLBACK CHAIN, most preferred first — a Latin face for the words
   and a CJK face for the syllables it has no glyph for. SVG hands the whole list to the
   viewer; PNG and PDF take the first name that resolves on this machine.

   A ROLE MAY POINT AT A GENERIC FAMILY instead of naming a face (`chordName serif`),
   which also moves what the LAYOUT measures it against — the only way to do that, since
   both sides are faces this engine ships. A generic family itself takes only quoted
   names: `serif sans` would be a re-classification, not a face choice, and is refused.

   `mono` is NOT a key: no text in this engine is monospace, and a binding that reaches
   nothing looks exactly like one that works. An unknown key is an ERROR for the same
   reason; a key bound twice in one block is a warning and the last one wins.

   A NAMED FACE THIS MACHINE DOES NOT HAVE is a WARNING, with or without `embedded` —
   whether a font is installed is a property of the machine and not of the source, so an
   error would let a runner's contents fail an author's score. (Until 2026-08-18 only the
   `embedded` spelling was checked, so `font "NoSuchFontFace"` was accepted in silence.)

   ⚠️ WHAT A BOUND FACE DOES NOT CHANGE: the layout still reserves space using the bundled
   face of the role's family, because measuring a system font by name would make the same
   .lys lay out differently on different machines. A named face is therefore DRAWN but not
   MEASURED, and the two disagree by −2.05 to +3.61 staff spaces on ordinary strings
   (measured 2026-08-18 at 2.2 ss). Weight and slant are the engraving's, not the score's:
   there is no way to ask for italic here. *)

(* Example:
   font {
     serif     "Georgia"
     lyricText "Charis SIL" "Noto Serif CJK JP"
     chordName serif
     title     "Cormorant"
     embedded
   }
*)

================================================================================
## 3. Part Definition
================================================================================

(* Parts declare instruments/voices. Header attributes are written BARE — the same
   command form as the top-level commands (NO colon, NO '='). *)

PartDecl       = 'part' , Identifier , [ String ] , [ PartBody ] ;  (* String = display name *)
PartBody       = '{' , { PartProperty } , '}' ;
PartProperty   = 'clef'        , ClefName
               | 'instrument'  , ( Identifier , [ String ] | String )
               | 'transpose'   , PitchToken
               | 'tuning'      , Identifier
               | 'octave'      , ( 'absolute' | 'relative' | Integer )
               | 'removeEmpty' , ( 'true' | 'all' | 'false' ) ;

ClefName       = 'treble' | 'bass' | 'alto' | 'tenor' | 'treble_8' ;

(* Display name: an optional quoted string right after the part name is the part's
   default printed label (the staff-left name), shared by every score that renders
   the part — `part vln1 "Violin I" { … }`. Same `symbol "label"` idiom as a
   structure section (`A "A2"`) and a staff render (`staff X "…"`). A score's
   `staff X "…"` overrides it for that score. Priority for the shown label:
   `staff X "…"` (per-score) > part display name > `instrument` label > the
   capitalized part identifier. (There is no `name` property — the inline string
   replaced it.) *)

(* instrument: a bare preset word (violin, cello, piano-right, …) drives the
   default clef/octave/tuning and the MIDI timbre, and is shown as the staff name.
   An optional trailing quoted string overrides just the shown name, keeping the
   preset's defaults: `instrument cello "Cello I"` = cello defaults, label "Cello I".
   A quoted string alone is a free-text name with no preset (default clef). *)

(* removeEmpty (hara-kiri): hide this part's staff in systems where it only
   rests. 'true' keeps the FIRST system (LilyPond \RemoveEmptyStaves);
   'all' hides the first system too (\RemoveAllEmptyStaves). A system stays
   visible if ANY voice of the staff plays. Default: never hide. *)

(* Examples:

   part melody                        // minimal
   part melody { clef treble }        // bare attribute, no colon
   part bass   { clef bass  instrument "Cello" }
   part fill   { clef bass  removeEmpty all }   // hara-kiri staff
*)

================================================================================
## 4. Phrase Definition
================================================================================

(* Reusable music fragments, referenced as $name. Defined before use. A phrase body
   evaluates in a fresh frame (default octave/pitch/duration), so $name means the same
   notes at every call. A phrase body MAY reference other phrases (phrase x { y }); the
   reference expands in place (its own fresh frame). What it must NOT do is reference
   itself, directly or around a ring (x -> y -> x, x -> y -> z -> x): a cycle would never
   expand to a finite piece and is rejected with LYS1027. *)

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
               | LyricsBlock                      (* named lyrics track; attach via 'with lyrics NAME' or a 'lyrics NAME' row *)
               | ChordsBlock                      (* note-aligned OR named chord row *)
               ;

(* A section-level setting applies to the WHOLE section — its key / meter / tempo / pickup
   prints on every part of the section, not just one voice. A section whose body is ONLY
   settings (no part blocks) is a standalone header: in part-major layout it states a
   section's key/meter/tempo once, parallel to the 'part' blocks, e.g.
     part melody { section A { c d e f } }
     section A { key g major }              (* applies to every part playing A *) *)
SectionSetting = KeyDecl | TempoDecl | TimeDecl | PartialDecl ;

PartBlock      = Identifier , MusicBlock ;

(* A lyrics track is written in a section next to the part it sings, and named so a score
   can reference it: attach it under a staff with 'staff X with lyrics NAME' (aligned to
   that part's notes), or place it as an independent 'lyrics NAME' ROW — see §7 (lead
   sheets). *)
LyricsBlock    = 'lyrics' , Identifier , '{' , { LyricMeasure } , '}' ;
LyricMeasure   = { LyricSyllable } , '|' ;
LyricSyllable  = LyricText , [ '-' ] | '--' | '-' | '~' | '_' ;
                 (* MEASURED 2026-08-16 — and the spacing is part of the rule, because
                    the two arms that fuse tokens keep only the pair's OUTER trivia:
                      GLUED to the word   "Hap- py"   continues that word (one syllable);
                      DETACHED  "la -- la" / "la - la" is a separate connector syllable.
                    Both spellings put the same hyphen on the same syllable — Classify
                    folds them — so the difference is only which node holds the text.
                    '~' GLUED on both sides ("va~ga") is an elision, otherwise a melisma. *)

ChordsBlock    = 'chords' , [ Identifier ] , '{' , { ChordEntry | Barline } , '}' ;
                 (* WITH a name: an independent chord part for a score row (lead sheet).
                    WITHOUT a name: symbols align above the co-written part's staff. *)
ChordEntry     = PitchBase , [ Accidental-text ] , [ DurationToken ] , [ ':' , Quality ] , [ '/' , PitchBase ] ;
                 (* c=C, a:m=Am, g:7=G7, g:m7.5-=Gm7b5, c/g=C over a G bass *)

(* Example (the score attaches the named track under the staff):
   section Verse {
     key g major
     melody { c4 d e f | g2 g | }
     lyrics words { Twin- kle twin- kle | lit- tle star | }
   }
   form main { Verse }
   score main { staff melody with lyrics words }
*)

### 5.1 Multi-voice (one staff)

VoiceBlock     = 'voice' , VoicePart , { VoicePart } ;
VoicePart      = [ Identifier ] , MusicBlock ;
(* 'voice' opens the span ONCE; every further voice is another block. Repeating the
   keyword ('voice { … } voice { … }') is LYS0019 — it would open a SECOND span, and
   two one-voice spans play in sequence rather than together. A voice NAME is an
   ordinary identifier, told apart from a phrase reference by the '{' after it. *)

(* Example (each voice { } is a simultaneous voice; NOT the LilyPond '<< \\ >>' form):
   section Main { piano { voice { c'2 d } { e2 f } } }
   // Named voices bind their own lyrics — the name goes before each block:
   //   voice sop { … } alt { … }   +   lyrics sop { … }  lyrics alt { … } *)

(* The FIRST voice carries the staff's timeline: it is engraved inline, its barlines are
   the staff's barlines, and music written after the span continues in the bar it left
   off in. The other voices sound alongside it, starting from the instant the span opened
   (mid-bar spans included), and add no bars of their own. The bar check counts them the
   same way, so both 'voice { c d e f } e f g a' and the same music without the braces
   report the one overfull bar they engrave.

   ONE voice { } is transparent — up/down stem forcing needs a second voice — so it
   engraves exactly like the music without it, and an UNNAMED lone voice warns (LYS4011).
   A named one does not: its name is what a 'lyrics NAME { }' block binds to. *)

================================================================================
## 6. Structure Definition
================================================================================

(* Song form: print/playback order of sections. Optional — omitting it plays sections
   in declaration order. Only section references and navigation marks (no inline music). *)

(* ⚠️ The keyword is 'form'. It was 'structure' once, and this production still said so
   until 2026-08-16 — a production is not an Example, so DocExamplesParseTests never read
   it, and 'structure' now parses as an ordinary identifier (measured: "Undefined variable
   or phrase: 'structure'"). The NAME is required; omitting it is LYS1016. The whole item
   list below was measured the same day by putting each spelling in a form and running
   `lysc check`. *)

StructureDecl  = 'form' , Identifier , '{' , { StructureItem } , '}' ;

StructureItem  = SectionRef                        (* Identifier [ String ] — the string is
                                                      this occurrence's display label *)
               | '~' , Identifier , [ String ]     (* same section, label hidden (LYS0012) *)
               | NavMark                            (* segno / coda / fine / dc / ds / to coda —
                                                      BARE; the '@' form is rejected (LYS1022) *)
               | '_' , String                       (* custom text — the string is GLUED:
                                                      _"text" is taken, _ "text" is not *)
               | StructureRepeat                    (* |: … :| *)
               | StructureVolta                     (* a repeat volta ending *)
               | Barline                            (* '||' '|.' '!' ':|' ':|:' engrave; a plain
                                                      '|' is kept as an inert divider *)
               | ( 'break' | 'nobreak' )            (* force / forbid a system break here *)
               ;

(* A repeat block. The endings go BETWEEN the barlines — |: … [1. D] :| [2. O] — and the
   play count rides on the closing bar the way the music stream spells it. *)
StructureRepeat = '|:' , { StructureItem } , ':|' , [ '*' , Integer ] ;

(* A repeat volta ending inside a |: … :| repeat, referencing a section:
   form main { |: A [1. D] :| [2. O] }
   The '[' is REQUIRED; the closing ']' is OPTIONAL — present draws the right cap
   (closed ending), absent leaves it open.

   An ending that NO repeat opens — form main { A [1. B] } — is accepted and engraves as
   the plain reference B: no bracket, no number, played once. That is LilyPond's answer
   (measured, 2.26.0: an \alternative with no \repeat in front renders byte-identically to
   the bare music), and it is warned about (LYS6008) because the number prints nothing.
   ⚠️ "Opened by a repeat" is about the TREE, not the text: in |: A [1. D] :| [2. O] the
   ending after the ':|' belongs to the repeat block, while in |: A :| B [1. B] the ending
   does not — that second one warns even though the form has a repeat in it. *)
StructureVolta = '[' , Integer , [ ( '-' | ',' ) , Integer ] , '.' , [ '~' ] , Identifier , [ ']' ] ;

NavMark        = 'segno' | 'coda' | 'fine' | 'to' 'coda'
               | 'dc' [ 'al' ( 'fine' | 'coda' ) ]
               | 'ds' [ 'al' ( 'fine' | 'coda' ) ] ;

(* Section reuse and a custom label:
   form main { Intro Verse Verse "Verse (reprise)" Coda } *)

(* Navigation: signs (segno/coda) engrave at the START of the following section; text
   directives (fine, to coda, dc/ds, dc al fine, ds al coda) engrave at the END of the
   section just played:
   form main { A segno  B to coda  C ds al coda  coda D } *)

================================================================================
## 7. Score (Output) Definition
================================================================================

(* A printable layout. 'score' is the keyword; the optional string is the output
   basename. Multiple 'score' blocks emit multiple files. MIDI has NO source block —
   it is a CLI output: `lysc midi song.lys song.mid`. *)

ScoreDecl      = 'score' , Identifier , [ String ] , '{' , { ScoreItem } , '}' ;
                 (* The Identifier NAMES THE FORM this score renders and is REQUIRED —
                    'score "out" { … }' is refused with "A 'score' must name the form it
                    renders". The optional String is the output basename.
                    A score body holds ScoreItems and nothing else: a form is declared at
                    the top level and referred to by that name, never written inside the
                    braces. At least one ScoreItem is required — a score with an empty body
                    engraves a page with no music, so it is an error (LYS6002). *)

ScoreItem      = StaffRender                        (* staff partName — BARE, no braces *)
               | 'grandStaff' , '{' , { StaffRender } , '}'
               | CondensedStaff                     (* several parts on ONE staff *)
               | CombinedStaff                      (* two parts on one staff, MERGED *)
               | 'tab' , [ TuningName ] , PartRef    (* tablature: tab partName, or
                                                        tab bass5 partName to override the
                                                        part header's own `tuning` *)
               | 'ossia' , [ ClefName ] , PartRef       (* ossia partName — BARE, like staff *)
               | 'chords' , PartRef                  (* independent chord ROW (lead sheet) *)
               | 'lyrics' , PartRef                  (* independent lyrics ROW (lead sheet) *)
               ;

CondensedStaff = 'condensedStaff' , '{' , PartRef , PartRef , { PartRef } , '}' ;
                 (* A CONDENSED score: the named parts, each of which would otherwise be its
                    own staff, share ONE staff — one voice each, in source order, so the
                    first part is voice 1 (stems up) exactly as the first block of a
                    `voice { … } { … }` span. Two or more parts are required (one part on one
                    staff is what `staff NAME` already is — LYS6003), and the members are
                    BARE part names: everything inside becomes a voice, and a `staff` item or
                    a braced group of staves is not a voice (LYS6004).

                    Because it is a SCORE-level item, one source can print both forms:
                      score full  { condensedStaff { fl1 fl2 } }
                      score parts { staff fl1  staff fl2 }

                    This is plain condensation. Unisons are NOT merged into one notehead and
                    no "a2"/"Solo" text is printed — that is `combinedStaff`, below. *)

CombinedStaff  = 'combinedStaff' , '{' , PartRef , PartRef , '}' ;
                 (* The part COMBINER: exactly two parts on one staff, merged wherever they
                    agree. At each moment the two are compared —

                      the same notes                  -> one notehead, marked "a2"
                      different notes, same rhythm,
                        within a ninth                -> ONE voice of two-note chords
                      only part one sounding          -> one voice, marked "Solo";
                                                         part two's rests are not engraved
                      only part two sounding          -> one voice, marked "Solo II"
                      anything else                   -> two voices, stems up and down

                    Exactly two, because combining is defined pairwise and every label it
                    prints names one of two parts (LYS6005 — for more, use `condensedStaff`).
                    Members are bare part names, as for `condensedStaff` (LYS6006).

                    ⚠️ The chord case is the common one, not the exception: two parts a third
                    or a sixth apart with the same rhythm come out as chords in a single
                    voice, which is what an orchestral part looks like. Use `condensedStaff`
                    when the two lines must stay visibly separate.

                    Score-level like `condensedStaff`, so one source prints both:
                      score full  { combinedStaff { fl1 fl2 } }
                      score parts { staff fl1  staff fl2 } *)

StaffRender    = 'staff' , [ ClefName ] , PartRef , { 'with' ( 'chords' PartRef | 'lyrics' PartRef ) } ;
                 (* Each 'with' clause ADDS to the staff: 'with chords NAME' aligns the
                    named chord part's symbols ABOVE the staff; 'with lyrics NAME' aligns
                    the named lyrics track's syllables BELOW it (repeat 'with lyrics' to
                    stack verses). The same chord part can also feed a lead-sheet row
                    ('chords NAME'), so a progression is written once. *)
PartRef        = Identifier ;

(* A 'score' may carry its OWN 'form main { … }' to render a different arrangement
   (e.g. a practice excerpt); it overrides the top-level structure for that score only. *)

(* Examples:

   score main "full" {
     grandStaff { staff rightHand  staff leftHand }
   }

   score practice { staff melody }     // a second form, rendered to practice.svg
*)

### 7.1 Lead sheets (chords and/or lyrics, no staff)

(* A 'chords NAME { … }' / 'lyrics NAME { … }' part placed in a score with
   'chords NAME' / 'lyrics NAME' (instead of 'staff NAME') renders WITHOUT a staff:
   a grid of measure barlines, chord symbols between the bars (at their timing), and
   lyrics below. Source barlines ( | |: :| || |. ) are drawn, and follow the
   bare-barline rule (below): a lone leading '|' only anchors the run's start
   ('| c1 | f1 |' == 'c1 | f1 |'), an empty bar is the explicit '| |' pair. *)

(* Example:
   section Main {
     chords prog  { c2 g:7 | a:m f | c1 :| }
     lyrics words { Twin- kle | lit- tle | star | }
   }
   form main { Main }
   score main "sheet" { chords prog lyrics words }
*)

================================================================================
## 8. Music Expression
================================================================================

### 8.1 Music Block

MusicBlock     = '{' , { MusicItem } , '}' ;

MusicItem      = Note | Rest | Chord | Arpeggio | Barline | InlineVolta | PhraseRef
               | Slur | Tie | Beam | Tuplet | Grace | Cue | MidMusicCommand | NavMark ;

(* NavMark (see §6) is the SAME bare token in a section's music as in a form — it is a
   landmark, never a note modifier, so it takes no '@' (c4@segno is LYS1022 and
   `segno c4` is the spelling). Written mid-measure it engraves but warns (LYS4003);
   put it at a barline boundary. *)

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

(* A PITCHED rest is a Note carrying the '@rest' annotation: it prints as a rest, at
   the height the written pitch would have had. The pitch is a POSITION and nothing
   else — it does not sound, prints no accidental, and does not enter the measure's
   accidental memory. Everywhere else a rest finds its own height (the middle line,
   or the voiced position inside a voice span, moved clear of the notes sounding with
   it); '@rest' replaces that whole calculation, and no collision moves it afterwards.
   '@rest' on anything but a note is an error — there is no pitch there to read.
   Example: two voices whose rests would collide, each rest placed by hand:

     voice { g'8 g' g' r8 r2 | } { a,4@rest c r2 | } *)

(* ADJACENCY RULE: a DurationToken (number + dots) is GLUED to what it lengthens —
   c4, r2., <c e g>4, << c e g >>2 — never spaced. A spaced number is a detached
   duration (LYS0016: 'c 4' is the note c and a meaningless 4), and a glued number
   on a chord/arpeggio MEMBER is a misplaced duration (LYS0015: members share one,
   written after the bracket). A SPACED number inside brackets is a scale degree —
   the adjacency is what tells <c e g2> (mistake) from <c e g 2> (degree). *)
(* A chord is EITHER letter mode — a pitch anchor followed by any mix of pitches and
   scale degrees ('<c e g>', '<c 3 5>', '<c 3 g>'; every degree measures from the
   ANCHOR, so '<c e 5>' == '<c e g>') — or degree mode: degrees only, anchored on the
   key TONIC ('<1 3 5>', '<2 4 6>'). A named pitch inside a DEGREE-anchored chord is an
   error (LYS1019): the degrees move with the key, the letter would not, so the chord
   would half-transpose. Octave marks may follow the closing '>', BEFORE the duration:
   <c e g>'4 . *)
Chord          = '<' , ( ChordNote , { ChordNote | ScaleDegree }
                       | ScaleDegree , { ScaleDegree } )
               , '>' , { "'" | ',' } , [ DurationToken ] , { Annotation } ;
ChordNote      = PitchToken , { Annotation } ;

(* Chord/arpeggio OCTAVES — the anchor model. One rule: a mark moves only what it is
   attached to.
   - ANCHOR: letter mode anchors on the FIRST member's bare LETTER, resolved nearest in
     the incoming relative frame; degree mode anchors on the key TONIC (degree 1),
     resolved the same way. The note AFTER the group is relative to the anchor.
   - MEMBERS place themselves at-or-above the anchor: a letter takes the same-letter
     pitch in the octave at/above it; degree N sits N−1 diatonic steps above it (8/9/13
     carry upward, no special case). A member's own '/, marks shift THAT ONE note only —
     the first member's included: <c' e g> = C5 E4 G4, and the next bare c is still C4.
     Letter mode is order-independent except the first slot (<c e g> = <c g e>, but
     <g c e> anchors on g); degree mode is FULLY order-independent (<2 4 6> = <6 2 4>
     = D F A in C major, and degrees follow the key: Dm in C, D-major shapes in D).
   - Marks AFTER '>' / '>>' move the WHOLE group an octave each, anchor included, so
     they DO propagate: <c e g>' c = C5 E5 G5 then C5, whereas <c' e' g'> sounds the
     same close-position chord but the next bare c stays C4. (A deliberate Lily#
     divergence from LilyPond's per-member relative chain.) In 'octave absolute' mode
     every member is a fixed pitch — no stacking, no frame — and the trailing marks
     STILL shift the whole group, so <c e g>' is C5 E5 G5 and <c e g>, is C3 E3 G3 there
     too. Read off the page: noteheads at y 13.85/12.85/11.85 and 20.85/19.85/18.85
     against treble staff lines 12.35…16.35, and each absolute book's SVG is identical
     to its relative twin's (data-pos masked).
     ⚠️ This paragraph said the opposite between 2026-08-16 and 2026-08-16 — "they do
     not … measured … the mark is parsed and then dropped" — and the word "measured"
     was true of the instrument, not of the engine: the reading came from
     'lysc check --pitches', which answered C4 E4 G4 for all three spellings while the
     page drew three different chords. The trace entry was written by
     ResolveAbsolutePitch as it resolved, and absolute mode applied the group shift to
     the value that call had already returned, so the drawn note moved and the reported
     one did not. Fixed by folding the shift into the octave BEFORE resolution, which is
     what relative mode had always done (it adds the same shift into the chord's anchor).
     ⇒ A claim about the engine that was measured only through a report is a claim about
     the report. The page is the arbiter, and reading it costs one hash. *)

(* Arpeggio: a written-out broken chord. Members carry NO duration of their own — they play
   in SEQUENCE and EQUALLY SUBDIVIDE the group's total, so a bare number is always a scale
   degree (never a duration): '<< c 3 5 >>' = c e g. The share becomes an auto-tuplet when it
   is not a plain note value (3 members in a beat = a triplet, 5 = a quintuplet, 9 = a
   nonuplet). A trailing DurationToken sets the total; without one the group inherits the
   running duration and acts like a single note. Octaves follow the chord anchor model
   (above): the anchor is the first pitched member's bare letter — or the key tonic when
   the group opens with degrees, so a descending figure needs no marks: << 8 5 3 1 >> =
   C5 G4 E4 C4. Each member's own marks are local; marks after '>>' shift the whole group
   and propagate. Members may be pitches, scale degrees, chords or rests.
   Annotations after '>>': a dynamic (@f) applies to the whole group and a chord name
   (@chord / @chord(a:m7)) labels it; any other annotation on the group or a bare member is not
   applied yet and warns (LYS4008) — nothing is dropped silently. A nested chord member
   keeps its own annotation handling ('<< <c e>@arpeggio g >>' is fine).
   This reuses '<< … >>' (LilyPond's parallel-voice form, which Lily# writes as
   'voice { }'); a '\\' inside is reported as the removed-polyphony form, not an arpeggio. *)
ArpMember      = PitchToken | ScaleDegree | Chord | Rest ;   (* no DurationToken on a member *)
ScaleDegree    = Integer , [ 'is' | 'isis' | 'es' | 'eses' ] , { "'" | ',' } ;
                 (* anchor-relative degree: 1 = root/tonic, 3 = third, 8 = octave; also the '<c 3 5>' chord form *)
Arpeggio       = '<<' , ArpMember , { ArpMember } , '>>' , { "'" | ',' } , [ DurationToken ] ;

Barline        = '|' | '||' | '|.' | '|:' | '!' | RepeatEnd ;
RepeatEnd      = ':|' , [ '*' , Integer ] ;          (* :|*N plays the span N times, default 2 *)

(* ONE-SIDED REPEAT BARLINES. The two halves are not symmetric:
     - ':|' with no '|:' open REPEATS FROM THE BEGINNING OF THE PIECE. Not an error.
     - '|:' that no ':|' closes IS AN ERROR (LYS4017) — where it ends is undefined.
   The pair MAY CROSS LAYERS: a '|:' in a section's music can be closed by a ':|' the
   form writes, because a section is not a piece of music on its own. So the pairing
   is decided on the LAID-OUT score (the collector's expanded measure stream), never
   on one layer alone — no scan of a section's text could be right about it.
   The mirror does NOT work: a '|:' in a form opens a FormRepeatBlock (below), and
   that block must close in the form.
   A repeat barline belongs to the SCORE, not to one part: written in one part it is
   drawn on every staff. *)

(* '!' is the DASHED barline (LilyPond's \bar "!"), and like every other barline it
   CLOSES THE BAR it follows. Write it spaced ('c4 d e f ! g4 …'). Glued to a note
   ('cis!') it is still the dashed barline, but LilyPond spells the FORCED ACCIDENTAL
   that way, so that form is warned about (LYS4009): Lily# has no forced-accidental
   shorthand — use '@courtesy' (parenthesized) or '@editorial' (small, above the head). *)

(* BARE-BARLINE SEMANTICS (music): a bare '|' after music closes that bar. On an empty
   span a SINGLE bare '|' merely anchors the boundary it sits on — the section start
   (a leading '|'), the section end (a trailing '|'), or a just-auto-filled bar — and
   creates nothing, so `{ | c1 | c1 | }` == `{ c1 | c1 }`. An EMPTY MEASURE is always
   an explicit `| |` PAIR: two written barlines with nothing between (leading, mid, or
   trailing; `| | |` is two). It holds a slot to keep parts aligned, renders as an
   empty full-width bar, and carries the ordinary underfull warning (LYS2001,
   "duration 0", squiggled between the barlines) until filled — an empty measure is
   thus always visible in the source. A TYPED barline on an empty span decorates the previous
   bar's end. A PHRASE REFERENCE is one item whose boundary re-arms this rule like a section
   start: a barline at the edge of the phrase body does not pair with an adjacent outer
   barline, so 'phrase x { c d e f | }' used as 'x | x' is two content bars, not two + a gap
   (an EXPLICIT '| |' after the reference still makes an empty bar). LYRICS follow the SAME rule: a lone leading '|' merely anchors the
   run's start ('| きら | ひかる |' == 'きら | ひかる'), so the fenced style aligns
   with the melody above instead of shifting the verse; "bar 1 has no syllables"
   is the explicit leading '| |' pair, and a mid-run '| |' is an empty bar. *)

(* First/second-time endings inside a |: … :| repeat. '[' followed by an integer is a
   volta; otherwise '[' … ']' is a manual beam group. The '[' is REQUIRED; the closing
   ']' is OPTIONAL — present draws the right cap (closed ending), absent leaves it open. *)
InlineVolta    = '[' , Integer , [ ( '-' | ',' ) , Integer ] , '.' , { MusicItem } , [ ']' ] ;
Beam           = '[' | ']' ;
PhraseRef      = [ '$' ] , Identifier , { "'" | ',' } , [ '(' , Integer , ')' ] ;
                 (* ⚠️ The '$' is OPTIONAL and the two spellings are the same thing —
                    measured 2026-08-16 over eight forms in both octave modes ($theme,
                    theme, theme', theme'(3), … all identical). This production named
                    only the bare form while SYNTAX_REFERENCE.md and GRAMMAR_FOR_LLM.md
                    teach '$name' and every example here writes '$'. Both compile, so
                    DocExamplesParseTests cannot see three documents disagreeing; WHICH
                    one to teach is a decision, filed in HANDOFF §2F. This line says what
                    the parser accepts, which is not the same question.
                    A movable phrase: it lands in the AMBIENT key at the reference
                    site; trailing marks shift whole octaves (Chorus' / Chorus,).
                    The marks shift in BOTH octave modes: relative moves the running
                    frame, absolute moves the anchor a bare 'c' is measured from, and the
                    two spellings resolve identically (measured over plain / ' / , — same
                    page, same MIDI, same MusicXML, same reported pitches; the LilyPond
                    twin writes '\fixed c''' where the relative one writes
                    '\relative c''', and LilyPond 2.26.0 renders those two byte-identically).
                    ⚠️ Absolute mode DROPPED them until 2026-08-16, in silence in three of
                    the four outputs — only the LilyPond twin said anything, warning that
                    the body was "exported UNSHIFTED". This paragraph and
                    GRAMMAR_FOR_LLM.md both taught the shift the whole time; the
                    implementation and one code comment (MeasureCollector.EnterDefaultFrame,
                    which declared the absence deliberate) disagreed with them, and no book
                    in the tree writes the spelling, so nothing ever brought the two
                    together. A
                    GLUED '(N)' after at least one mark is a DIATONIC interval:
                    Melody'(3) plays the phrase a third UP in the ambient key (the
                    third's quality follows the scale), Motif,(2) a second down —
                    sequences and parallel-third harmonies in one token. 1-based
                    like a degree, so '(8) == ' and '(1) is a unison; extra marks
                    add octaves (''(3) = an octave plus a third). The adjacency is
                    what separates it from a slur: a SPACED ' (' still opens one.
                    A reference is ONE item to the relative chain — the chord rule:
                    the note that follows is relative to the phrase's ANCHOR (its
                    first note's bare letter, shifted with the reference's marks
                    and '(N)'), never to its interior, so how the body ends — or is
                    later edited — cannot move the music after a reference, and
                    '(8) == ' holds after the phrase too. A body evaluates in the
                    default frame; a pitchless body (rests only) hands nothing off. *)

### 8.3 Ties, Slurs, Beams

Tie            = '~' ;            (* same pitch across notes/barline: c4~ | c4. A tie binds to
                                     the IMMEDIATELY following note/chord, which must repeat the
                                     tied pitch — a different pitch or a rest there ties nothing
                                     and warns (LYS4007); different pitches connect with a slur. *)
Slur           = '(' | ')' ;      (* over notes OR chords: c4( d e) , <c e>4( <d f>) . A slur mark
                                     goes AFTER the note it belongs to, so a '(' written before any
                                     note belongs to none. Marks pair innermost-first and do not
                                     carry into another voice; one that pairs with nothing draws no
                                     slur and warns (LYS4010). *)
Beam           = '[' | ']' ;      (* manual; beaming is automatic otherwise *)

### 8.4 Annotations (@name, attached to a note or chord)

Annotation     = '@' , AnnotationName , [ '(' , Arg , { ( ' ' | ',' ) , Arg } , ')' ] , [ Placement ] ;
Placement      = '.up' | '.down' ;   (* force above / below; default is automatic *)
(* A value-bearing annotation puts its argument(s) in parentheses (space- or
   comma-separated); '.' is reserved for the .up / .down placement suffix. *)

(* The '@' prefix is the ONLY annotation prefix; '\name' is rejected (backslash is
   tablature-only). AnnotationName is resolved from text — it is NOT a reserved keyword,
   so names like 'tr' remain usable as identifiers. Categories: *)

(* - Articulations: @staccato @staccatissimo @accent @tenuto @marcato @fermata @portato  (.up/.down ok)
   - Strings:       @upbow @downbow @flageolet  (always above)
   - Ornaments:     @trill @mordent @prall @turn @invertedturn
   - Dynamics:      @ppp @pp @p @mp @mf @f @ff @fff @sfz @sf @fp @rfz @fz   (default below; @f.up forces side)
   - Hairpins:      @cresc @decresc @dim  (start note → next dynamic; .up/.down REJECTED)
   - Stem:          @stemUp @stemDown      (a beam's shared direction wins)
   - Accidental:    @courtesy (cautionary) @editorial (musica ficta)
   - Arpeggio:      <c e g>4@arpeggio
   - Glissando:     c4@glissando d
   - Figured bass:  c4@fig(6) , d4@fig(6 4)
   - Chord name:    c4@chord(c) , d4@chord(d:m)   (* Lily# pitch spelling, lower case:
                    the quality follows a ':' (a:m7, g:7). '@chord(C)' / '@chord(Dm)' are
                    LilyPond's spelling and are NOT recognised — LYS1008 warns and the
                    symbol is not engraved. A bare '@chord' derives it from the notes. *)
   - Fingering:     <c@finger(1) e@finger(3)>4
   - Rehearsal mark: c4@mark("A")   (label is a quoted string)
   - Free text:      c4@text("dolce") , c4@text("pizz.").up   (italic; below by default)
   - Half ties:     c4@laissezVibrer (l.v. into silence) , c4@repeatTie (from a repeat)
   - Effects:       @cross / @dead (x notehead) ,
                    @fall @doit (jazz bends) , @breath @caesura
                    (* a CUE is not an annotation — it is a region, 'cue { … }', below *)
   - Feathered beam: c16@feather(right) … (accel) / @feather(left) (rit)
   - Spanners:       @rit @accel
                     @ottava(…) @quindicesima(…) … @loco ,   [labels: 8va/15ma; @…(bassa) = down]
                     @startTrillSpan … @stopTrillSpan ,
                     @sustainOn … @sustainOff , @sostenutoOn … @sostenutoOff , @unaCorda … @treCorde *)

(* Example: c4@staccato.up d4@accent@p <e g>4@arpeggio | *)

### 8.5 Tuplets and Grace notes

Tuplet         = 'tuplet' , Integer , '/' , Integer , MusicBlock ;   (* nesting allowed *)
Grace          = ( 'grace' | 'acciaccatura' | 'appoggiatura' ) , MusicBlock ;
Repeat         = 'repeat' , ( 'percent' | 'unfold' | 'tremolo' ) , [ Integer ] , MusicBlock ;
                 (* repeat percent 2 { … } = percent-repeat the measure; volta repeats
                    use the symbolic |: … :| form, NOT a 'repeat' keyword *)
Cue            = 'cue' , [ ClefName ] , MusicBlock ;
                 (* Small cue notes. A cue is a REGION, not a note annotation — it maps
                    onto LilyPond's CueVoice context, whose size is a context property,
                    so there is no '@cue': write 'c4 d cue { e4 f } g4 |'.
                    The optional ClefName is the clef the QUOTED instrument reads in —
                    LilyPond's \cueClef / \cueClefUnset, which the twin emits as a pair.
                    Its notes are written in that clef and the staff's own clef returns
                    after the region. *)
                 (* ⚠️ A slur or tie may not cross the region's edge: a cue is a voice of
                    its own, so LilyPond drops such a span entirely (LYS4012). Close it
                    inside the cue, or keep both ends outside — a slur passing OVER a whole
                    cue is fine. Two cue blocks written side by side are two voices, so a
                    span may not run from one into the next either, both ends being cued. *)

(* Example: c4 d cue { e4 f } g4 | *)

(* Example: c4 d cue bass { e4 f } g4 | *)

================================================================================
## 9. Override / Revert (engraving properties)
================================================================================

OverrideDecl   = [ 'once' ] , 'override' , Grob , '.' , Property , '=' , OverrideValue
               | 'revert' , Grob , '.' , Property ;
OverrideValue  = Number | '-' Number | Identifier | String ;
Number         = Integer | Decimal ;
                 (* the value form fits the property: a length/position is a number,
                    whole or fractional (LilyPond's own grob values are routinely
                    fractional — padding 0.5, thickness 0.45); a direction/symbol is an
                    identifier (up, red); a colour is a string ("red"). *)

(* The value is carried as a TYPE, not as text: LysValue.Int / Real / Str / Symbol,
   built once where the value is collected. A quoted value is therefore NOT a number —
   `= "10"` is the string "10" and answers nothing to a consumer asking for a double. *)

(* The SYNTAX above accepts any Grob.property, but the consumed vocabulary is three pairs —
   NoteHead.transparent, Stem.transparent, NoteColumn.force-hshift. Anything else is refused
   (LYS1029, "not supported in this version") rather than silently doing nothing, so the
   examples below are limited to the three; the list grows, and each addition removes one
   error. *)

(* Example:
   override NoteHead.transparent = true
   override NoteColumn.force-hshift = 1.5      // fractional
   override NoteColumn.force-hshift = -3       // negative
   c4 d e f |
   revert NoteHead.transparent
   once override Stem.transparent = true       // applies to the next note only
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
  lyrics words { Sing a song now | one two three four | }
}

// A staff-less lead sheet (chords + lyrics, no notes):
section Sheet {
  chords prog  { c2 a:m |: f2 g:7 | c1 :| }
  lyrics words { Twin- kle twin- kle | lit- tle | star | }
}

form main  { Verse }
form sheet { Sheet }

score main  "demo"  { staff melody with lyrics words }
score sheet "sheet" { chords prog lyrics words }
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
| Unnamed form       | `form` written without a name (LYS1016) — `form main { … }` |
| Duplicate form     | Two `form`s share a name (LYS1017). SEVERAL forms are fine — one score each |
| Inline music       | `{ }` music in a `form` (not allowed — section refs only) |
| Undefined ref      | Section / phrase / part referenced but not defined       |
| Forward reference  | Phrase used before its definition                        |
| Missing fine/segno/coda | `dc al fine` without `fine`, `ds` without `segno`, `to coda` without `coda` |

### Warnings

| Warning            | Description                                               |
|--------------------|-----------------------------------------------------------|
| Unused section     | Section defined but named by no form                      |
| Unused phrase      | Phrase defined but never referenced                       |
| Incomplete measure | Measure duration doesn't match the time signature         |
| Excess lyrics      | More lyric syllables than notes in a bar (extras dropped) |
