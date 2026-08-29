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
IdentCont      = IdentStart | Digit ;
(* A name may CONTAIN or END with digits (melody2, foo2bar) but must not START
   with one: a leading digit is a duration (c4) or scale degree (<1 3 5>), so
   'phrase 2foo { }' is rejected with LYS0017 "a name cannot start with a digit". *)
(* ⚠️ IdentCont listed '-' until 2026-08-19 and that was never true: the lexer scans
   letters, digits and '_' and stops at a hyphen (Lexer.ScanWord). Measured the same day —
   'part foo-bar { }' errors at every one of the four positions it is written in, so no
   name in the language has ever contained a hyphen. The ONE place a '-' joins two words
   is InstrumentPreset below, where the parser stitches them deliberately.
   Held by IdentifierCannotContainAHyphen in DocKeywordListTests. *)

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
        | 'repeat' | 'volta' | 'alternative' | 'embedded' | 'fonts' | 'paper'
        | 'override' | 'revert' | 'once'
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
   NOT reserved. 'alternative' is reserved only to reject the removed LilyPond-style
   form; 'using' is reserved for multi-file support.

   ⚠️ 'volta' was in that sentence too, and stopped belonging there when the fonts
   block landed: 'repeat volta 2 { … }' is removed (LYS0006) but
   'fonts { volta "TeX Gyre Schola" }' binds the volta-bracket face and compiles.
   The word has a DEAD spelling and a LIVE one, which is why the editor's grammar no
   longer paints it — or anything — as an error: a per-line regular expression cannot
   tell the two apart, and only a diagnostic knows where a word stands
   (EditorColouringTests, 2026-08-18).

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
               | PaperDecl                        (* page dimensions *)
               | GlobalSetting                    (* tempo, time, key *)
               | PartDecl                         (* part definitions *)
               | PhraseDecl                       (* reusable music fragments *)
               | SectionDecl                      (* musical sections - REQUIRED *)
               | StructureDecl                    (* song form - optional *)
               | ScoreDecl                        (* output definitions - REQUIRED *)
               | OverrideDecl                     (* engraving overrides *)
               ;

(* MUSIC IS NOT A TopLevelItem, and is rejected with LYS0020: a note stream, a bare
   '{ … }' block, a grace/tuplet group, a 'break', and a phrase reference all belong
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

FontDecl       = 'fonts' , [ Identifier ] , FontBlock ;
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
     1. the role's own binding          fonts { lyricText "Charis SIL" }
     2. its group's binding             fonts { lyrics    "Charis SIL" }
     3. the generic family it belongs to    fonts { serif "Georgia" }
     4. the bundled face                (TeX Gyre Schola / Heros)
   The NARROWER spelling wins wherever both are written, in either source order, so
   `marks "Georgia"  tempo "Playfair Display"` needs no special case. Case-insensitive:
   `lyrictext` binds `lyricText`.

   THE WHOLE DOCUMENT is step 3 for BOTH generic families — the two bound together:
     fonts { serif "Georgia"  sans "Georgia" }

   ⚠️ THE KEYWORD IS `fonts`, PLURAL, AND IT TAKES A BLOCK. The block is an alist of
   family -> face, which is what LilyPond calls `fonts` too
   (define-grob-properties.scm:395, paper-defaults-init.ly:169-178
   property-defaults.fonts.serif). There is no `font` keyword — that word is free, and a
   part may be named it. A bare value, `fonts "Georgia"`, is LYS8008, whose message quotes
   the writer's own face name back inside the block to write instead.

   ⚠️ NOTATION IS OUTSIDE STEPS 3 AND 4. The octave digit under `treble_8`, a compound
   meter's '+', and tab fret numbers are notation that happens to be drawn as text, so a
   `serif`/`sans` binding does NOT restyle them; they follow a face only when `notation`
   or the leaf itself is named.

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
   (LilyPond's third family IS `typewriter`; the two layers above are otherwise LilyPond's
   own — `\paper { property-defaults.fonts.serif = "…" }` for the broad one and
   `\override GROB.font-name` for the narrow one. The GROUPS are this language's addition.
   Verified against the 2.26 tree on 2026-08-18: input/regression/font-family-override.ly
   and font-name.ly.)

   A NAMED FACE THIS MACHINE DOES NOT HAVE is a WARNING, with or without `embedded` —
   whether a font is installed is a property of the machine and not of the source, so an
   error would let a runner's contents fail an author's score. (Until 2026-08-18 only a
   directive that also said `embedded` was checked, so a face nobody had was accepted in
   silence when it did not.)

   A BOUND FACE IS MEASURED, not only drawn — since 2026-08-18. The layout reserves space
   with the same file the string is drawn in, so a title in a wide face gets a wide box.
   Before that the reservation always used the bundled face and the two disagreed by −2.05
   to +3.61 staff spaces on ordinary strings (measured 2026-08-18 at 2.2 ss).

   ⚠️ THIS MAKES THE PAGE DEPEND ON THE MACHINE, for a score that names a face: where the
   face is not installed the reservation falls back to the bundled one, and the page is
   spaced differently. That is LilyPond's own exposure and for the same reason — a
   `font-name` there goes to fontconfig — and it is why a missing face WARNS rather than
   passing in silence. A score that names no face is unaffected: the bundled files ship.

   ⚠️ `embedded` DOES ONE THING: it subsets the named faces into an exported PDF. It is not
   a switch on how anything is measured or drawn.

   Weight and slant are the engraving's, not the score's: there is no way to ask for
   italic here. *)

(* NAMED BLOCKS, AND THE PER-SCORE REFERENCE (fonts and paper share this shape).
   Without a name the block is the FILE DEFAULT — one per file, every score that
   references nothing inherits it. With a name it is a DECLARATION and binds nothing
   by itself: a score references it as a ScoreItem, optionally overriding part of it —

     fonts house { serif "Georgia"  lyricText "Charis SIL" }
     score main  { fonts house  staff melody }
     score parts { fonts house { lyricText "Noto Serif CJK JP" }  staff melody }

   The reference REPLACES the file default (resolved = built-in defaults + the named
   block + the override block, never a hidden chain through the unnamed default), and
   the override block reads as if its entries were written at the END of the named
   block: the last same-key entry wins, with no duplicate warning across the two
   blocks — overriding a key is the block's purpose. ⚠️ The narrower-spelling rule
   keeps winning WHICHEVER block a binding came from: a house block's role binding
   (lyricText) survives a score overriding the group (lyrics), deliberately — a house
   style's deliberate role choices outlive a score swapping the broad base, and a
   role is overridden by writing the same or a narrower key.

   An unknown reference name is an error naming the declared blocks, a reference
   that resolves to nothing binds nothing (the score keeps the file default), two
   declarations sharing a name is an error, a named block no score references is a
   warning, and a second reference in one score warns — the last wins. *)

(* Example:
   fonts {
     serif     "Georgia"
     lyricText "Charis SIL" "Noto Serif CJK JP"
     chordName serif
     title     "Cormorant"
     embedded
   }
*)

### 2.5 Paper

PaperDecl      = 'paper' , [ Identifier ] , PaperBlock ;
PaperBlock     = '{' , { PaperEntry } , '}' ;
PaperEntry     = 'size' , ( SizeName | String ) (* a whole page by name - see below *)
               | PaperScalarKey , Length
               | 'raggedRight'
               | SpacingKey , SpacingBlock ;
SizeName       = Word-run ;                     (* the GLUED tokens after 'size' read
                                                   as one word - b5 lexes as a pitch
                                                   and a duration, 17x11 as a number
                                                   and a word, and adjacency joins
                                                   them back into the name they spell *)
PaperScalarKey = 'paperWidth' | 'paperHeight'
               | 'leftMargin' | 'rightMargin' | 'topMargin' | 'bottomMargin'
               | 'indent' | 'shortIndent'
               | 'topSystemPadding' | 'spacingIncrement' ;
SpacingKey     = 'systemSystemSpacing' | 'scoreSystemSpacing' | 'markupSystemSpacing'
               | 'scoreMarkupSpacing' | 'markupMarkupSpacing' | 'topSystemSpacing'
               | 'lastBottomSpacing'
               | 'staffStaffSpacing' | 'staffGroupStaffSpacing'
               | 'defaultStaffStaffSpacing'
               | 'nonStaffRelatedStaffSpacing' | 'nonStaffUnrelatedStaffSpacing'
               | 'nonStaffNonStaffSpacing' ;
SpacingBlock   = '{' , { SpacingEntry } , '}' ;
SpacingEntry   = ( 'basicDistance' | 'minimumDistance' | 'padding' ) , Length
               | 'stretchability' , SignedNumber ;
Length         = SignedNumber , [ LengthUnit ] ;
LengthUnit     = 'mm' | 'cm' | 'in' ;
SignedNumber   = [ '-' ] , ( Integer | Decimal ) ;

(* THE PAGE'S DIMENSIONS — paper size, margins, indents, and the vertical spacing
   specs. One UNNAMED block per file (a second one warns and the last wins, like
   every repeated global). A NAMED block is a per-score declaration — the reference
   shape, the replace-the-default rule and the override reading are the fonts
   block's, spelled out in the note at the end of 2.4:

     paper wide { paperWidth 250mm }
     score main  { paper wide  staff melody }
     score parts { paper wide { topMargin 12mm }  staff melody }

   The vocabulary is LilyPond's \paper variables camelCased, and every
   default equals LilyPond's a4 default.

   SIZE IS A WHOLE PAGE BY NAME: `size b5` sets the width, the height, AND the four
   margins, scaled the way LilyPond's set-paper-size scales them (scm/paper.scm
   set-paper-dimensions) — each margin default multiplied by the size's ratio to a4
   (horizontal by width, vertical by height) and rounded to whole millimetres. So
   `size a4` is the identity, and `size b5` gives 13mm sides and 8mm top/bottom.
   The name is BARE — a closed vocabulary's values are bare in this language, like a
   clef's or a tuning's — and the quoted form is the escape for the few names that
   carry a SPACE (`size "ansi a"`), the lyric syllable's rule (a quoted single word
   is accepted the same way). The names are LilyPond's documented-paper-alist,
   transcribed whole (a4..a10, b0..b10, c0..c10, letter, legal, tabloid, and the
   rest), PLUS `jisb5` (182 x 257) which is Lily#-OWN: the ISO b5 is not the
   Japanese B5, Japanese sheet music commonly uses JIS B5, and LilyPond has no JIS
   entries. An unknown name is an error listing the table.

   `size` reads at its position like every other key — a later `topMargin` overrides
   its margin, a later `size` overrides an earlier margin. That second half is a
   deliberate divergence from LilyPond, whose set-paper-size preserves an earlier
   left-margin while clobbering an earlier top-margin (an artifact of its module
   mechanics): Lily# keeps the block's one rule, later wins, for every key alike.
   Write `size` first and the two engines agree everywhere.

     paper { size jisb5 }
     paper concert { size b4  raggedRight }
     score parts { paper concert { size a4 } }

   A BARE NUMBER IS STAFF SPACES — the unit everything else in this language is
   measured in. A physical unit is a word GLUED to its number, one quantity:
   210mm, 29.7cm, 8.5in (LilyPond spells the same thing 210\mm). A spaced
   `210 mm` reads as a key named mm and is refused with the glued spelling in the
   message. The conversion is 1 staff space = 5 TeX points = 127/72.27 mm, rounded
   to six decimals — exactly how the engine's own defaults were computed, so a book
   that states a default IS the default, byte for byte.

   THE STAFF-SPACING FAMILY LIVES HERE, not in override, although LilyPond keeps it
   on grobs (StaffGrouper.staff-staff-spacing) and contexts: every one of these
   quantities is applied score-wide in one pass, and paper is the spelling whose
   meaning IS score-wide, while an override would drag in a scope machinery (once,
   staff tags) that would parse and then silently not apply (user decision
   2026-08-23, GRAMMAR_AUDIT 2.1/2.2).

   NOT HERE, deliberately: a staff-size knob (the staff space is the unit itself —
   scaling it is a different feature, LilyPond's set-global-staff-size), and the
   line/page-breaking algorithm switches (engine tuning, not a dimension of the
   picture). `raggedRight` is a bare flag — writing it turns it on.

   `stretchability` is unitless (a spring flexibility), so a physical unit on it is
   refused. paperHeight 0 keeps the single content-driven page.

   An unknown key is an ERROR, the fonts block's reasoning: a setting nobody reads
   looks exactly like one that works. A key set twice in one block warns and the
   last one wins. *)

(* Example:
   paper {
     paperWidth 210mm
     paperHeight 297mm
     leftMargin 15mm
     raggedRight
     systemSystemSpacing { basicDistance 12  stretchability 60 }
   }
*)

================================================================================
## 3. Part Definition
================================================================================

(* Parts declare instruments/voices. Header attributes are written BARE — the same
   command form as the top-level commands (NO colon, NO '='). *)

PartDecl       = 'part' , Identifier , [ String ] , [ PartBody ] ;  (* String = display name *)
PartBody       = '{' , { PartProperty } , '}' ;
PartProperty   = 'clef'          , PartClefName
               | 'instrument'    , ( InstrumentPreset , [ String ] | String )
               | 'transpose'     , PitchToken
               | 'transposition' , TranspositionMarker
               | 'tuning'        , TuningName
               | 'octave'        , Integer
               | 'removeEmpty'   , RemoveEmptyValue
               | 'pedal'         , PedalStyleName ;
               (* 'lines' left this list 2026-08-19 (user decision): the staff-line
                  count is presentation, not music, so the SCORE item that renders
                  the part carries it — 'staff m as lines 1' (StaffRender, §7). The
                  same part can print five-lined in the full score and one-lined in
                  a lead sheet, which one part-global number could not spell. *)
               (* 'key' is per-part too, but the parser takes it as a KeySignature rather
                  than a PartProperty, so it is not an alternative here. *)

(* A preset is ONE word that may be spelled with hyphens (piano-right, voice-soprano).
   The tail after each '-' is any BARE WORD, whatever else that word is reserved for:
   until 2026-08-19 the parser gated it on "may this word name a part?", which admitted
   an identifier and the four clef words bass/treble/alto/tenor — so voice-alto and
   voice-tenor were writable and voice-soprano was not, for no reason anybody chose. *)
InstrumentPreset = BareWord , { '-' , BareWord } ;
BareWord         = ( UnicodeLetter | Digit | '_' ) , { UnicodeLetter | Digit | '_' } ;
                   (* i.e. SyntaxFacts.IsBareWord — a KEYWORD qualifies, which is the point *)

ClefName       = 'treble' | 'bass' | 'alto' | 'tenor' | 'treble_8' ;

PartClefName   = ClefName
               | 'treble^8' | 'bass_8'
               | 'soprano' | 'mezzosoprano' | 'baritone' | 'percussion' ;

TuningName     = 'standard' | 'guitar' | 'bass' | 'bass5' | 'bass6'
               | 'ukulele' | 'uke' ;

RemoveEmptyValue = 'true' | 'all' | 'false' ;

PedalStyleName = 'bracket' | 'text' | 'mixed' ;

TranspositionMarker = '8va' | '8vb' | '15ma' | '15mb' ;

(* ⚠️ Every one of these value words is case-SENSITIVE, `removeEmpty` included. It was the
   one exception until 2026-08-19 — and it was an exception because nobody checked it at
   all: `removeEmpty banana` compiled and was read as off, and so did `lines banana` (five
   lines), `octave banana`, `transpose banana` and `transposition banana` (no shift). The
   value each of those five names was silently the default. They are refused now.
   ⚠️ The document is a SECOND reader of these lists, so RemoveEmptyValue, PedalStyleName,
   TuningName, PartClefName and TranspositionMarker are all held to LilySharp.Core by
   DocKeywordListTests. The `octave` line above is why: it read
   `( 'absolute' | 'relative' | Integer )` for a long time, and the two words are the
   OctaveDecl directive's, not this property's. Written in a part header they parsed as a
   value no reader could read and changed nothing at all (measured 2026-08-19 against a
   control that proves the instrument can see `octave`). Nothing bound that alternation
   to anything, so nothing said so. *)

(* ⚠️ ClefName and PartClefName are two vocabularies and were one production until
   2026-08-19, when the difference was measured in both directions. A PART HEADER takes
   all eleven; `clef` INSIDE MUSIC, and `staff`/`ossia` in a score, take the five of
   ClefName and refuse the other six ("Expected clef name (treble, treble_8, alto,
   tenor, bass)"). Writing the eleven as ClefName told a reader that `staff percussion X`
   is legal, and writing the five told the editor to leave `clef treble^8` uncoloured in
   the one place it is legal — both happened. *)

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

(* transposition: the part's written->sounding shift, BEYOND whatever octave the clef
   word already carries. 'transpose' moves the written pitches; 'transposition' states
   that the written pitches sound elsewhere. *)

(* pedal: how a sustain span is drawn - the 'Ped.' text, a bracket, or 'mixed'
   (text at the start, bracket for the hold). *)

(* Examples:

   part melody                        // minimal
   part melody { clef treble }        // bare attribute, no colon
   part bass   { clef bass  instrument "Cello" }
   part fill   { clef bass  removeEmpty all }   // hara-kiri staff
*)

================================================================================
## 4. Phrase Definition
================================================================================

(* Reusable music fragments, referenced by bare name. Defined before use. A phrase body
   evaluates in a fresh frame (default octave/pitch/duration), so a reference means the same
   notes at every call. A phrase body MAY reference other phrases (phrase x { y }); the
   reference expands in place (its own fresh frame). What it must NOT do is reference
   itself, directly or around a ring (x -> y -> x, x -> y -> z -> x): a cycle would never
   expand to a finite piece and is rejected with LYS1027. *)

PhraseDecl     = 'phrase' , Identifier , MusicBlock ;

(* Example:
   phrase theme { c4 d e f | g a b c' | }
   section Main { melody { theme g2 g | } }
*)

================================================================================
## 5. Section Definition
================================================================================

(* Musical sections bind music to each part by name. At least one is required. *)

SectionDecl    = 'section' , Identifier , '{' , { SectionItem } , '}' ;

SectionItem    = SectionSetting
               | PartBlock                        (* partName MusicBlock *)
               | VoiceBlock                       (* multi-voice on one staff *)
               | LyricsBlock                      (* named lyrics track; a score places it as a 'lyrics NAME' row *)
               | ChordsBlock                      (* named chord part; a score places it as a 'chords NAME' row *)
               ;

(* A section-level setting applies to the WHOLE section — its key / meter / tempo / pickup
   prints on every part of the section, not just one voice. A section whose body is ONLY
   settings (no part blocks) is a standalone header: in part-major layout it states a
   section's key/meter/tempo once, parallel to the 'part' blocks, e.g.
     part melody { section A { c d e f } }
     section A { key g major }              (* applies to every part playing A *) *)
SectionSetting = KeyDecl | TempoDecl | TimeDecl | PartialDecl ;

PartBlock      = Identifier , MusicBlock ;

(* A lyrics track BINDS TO ITS OWN MELODY AT THE DEFINITION (user decision,
   2026-08-19, closed before the first tag): 'lyrics ja sings vocal { ... }' says the
   track ja sings the part vocal, and the SCORE only PLACES its row (§7) —
     - directly BELOW the staff engraving the part it sings, the row IS that
       staff's verse: the syllables sit under the engraved melody, and a run of
       such rows stacks as verses in written order;
     - anywhere else, it is an independent ROW at the melody's rhythm WITHOUT
       engraving the melody (a part sheet carrying the chorus words —
       LilyPond's shape is \lyricsto over a NullVoice: the moments join the
       spacing, the notes print nothing).
   The binding is a property of the TRACK NAME, stated once; later same-name blocks
   may repeat it identically or omit it (a different target is LYS7005; an unknown
   one LYS7004). With no 'sings' anywhere the NAME can be the binding — the part
   itself, or one of that part's voices ('voice sop { }' + 'lyrics sop { }') —
   and a track that binds to nothing placed as a row keeps the even-spread
   lead-sheet reading (§7). Placement cannot re-decide the association: a row
   after a staff it does not sing simply stays an independent band. *)
LyricsBlock    = 'lyrics' , Identifier , [ 'sings' , PartRef ] , '{' , { LyricMeasure } , '}' ;
LyricMeasure   = { LyricSyllable } , '|' ;
LyricSyllable  = LyricText , [ '-' ] | '--' | '-' | '~' | '_' ;
                 (* MEASURED 2026-08-16 — and the spacing is part of the rule, because
                    the two arms that fuse tokens keep only the pair's OUTER trivia:
                      GLUED to the word   "Hap- py"   continues that word (one syllable);
                      DETACHED  "la -- la" / "la - la" is a separate connector syllable.
                    Both spellings put the same hyphen on the same syllable — Classify
                    folds them — so the difference is only which node holds the text.
                    '~' GLUED on both sides ("va~ga") is an elision, otherwise a melisma. *)

ChordsBlock    = 'chords' , Identifier , '{' , { ChordEntry | ChordExtend | Rest | Barline } , '}' ;
                 (* A named chord part; a score places it as a 'chords NAME' row —
                    directly above a staff, or as a lead-sheet row on its own.
                    The NAMELESS form (auto-attach above "the co-written part's
                    staff") was removed before the first tag (LYS0032): its
                    association was stated nowhere and broke down the moment a
                    section held two parts. *)
ChordEntry     = ChordRoot , [ Quality ] , [ '/' , ChordRoot ] ;
ChordRoot      = ( 'A' | 'B' | 'C' | 'D' | 'E' | 'F' | 'G' ) , [ '#' | 'b' ] ;
ChordExtend    = '.' ;
Quality        = { 'm' | 'maj' | 'dim' | 'aug' | 'sus' | 'add' | Integer
                 | ( '+' | '-' ) , Integer | '+' | '-' } ;
                 (* THE ENTRY IS THE SYMBOL AS IT PRINTS (decided 2026-08-21,
                    GRAMMAR_AUDIT 8.1): C, Am, G7, F#m, Bb7, Gm7-5, Cmaj7/E. The
                    case is the grammar — an UPPERCASE letter is a root, so 'R'
                    (the rest) never collides, and 'b' after a root is a FLAT,
                    which is why every altered tension spells '+'/'-' (Bb5 is
                    B-flat's power chord; B's flat five is B-5). Placement is
                    MEASURE-RELATIVE, no durations: a bar's written slots —
                    entries, rests, '.' — divide it on the meter's own beat grid
                    (the beams' grid). One slot takes the bar. Slots = beats sit
                    one per beat, an integer multiple splits each beat equally,
                    a divisor groups whole beats — anything else warns (LYS2009)
                    and divides equally. '.' holds the previous chord one more
                    slot ('| C . . G7 |' in 4/4 = C for three beats) and never
                    crosses a barline (a bar-head '.' is LYS2010). r/R print
                    N.C. in their slot, s prints nothing — each occupies one. *)

(* Example (the track sings its melody; the score attaches it under that staff —
   or, as 'lyrics words' among the score items, shows ONLY the words at the
   melody's rhythm, e.g. a horn part carrying the chorus lyrics):
   section Verse {
     key g major
     melody { c4 d e f | g2 g | }
     lyrics words sings melody { Twin- kle twin- kle | lit- tle star | }
   }
   form main { Verse }
   score main { staff melody  lyrics words }
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
               | 'grandStaff' , StaffGroupBody      (* a BRACE  — one instrument, two staves *)
               | 'staffGroup' , StaffGroupBody      (* a BRACKET, bar lines drawn through *)
               | 'choirStaff' , StaffGroupBody      (* a BRACKET, bar lines NOT drawn through *)
               | CondensedStaff                     (* several parts on ONE staff *)
               | CombinedStaff                      (* two parts on one staff, MERGED *)
               | 'tab' , [ TuningName ] , PartRef , [ 'as' , TabStyle ]
                                                     (* tablature: tab partName, or
                                                        tab bass5 partName to override the
                                                        part header's own `tuning`.
                                                        The style word is TabStyle, below.
                                                        With no clause the SCORE answers.
                                                        ⚠️ NO SEMICOLONS IN HERE — the doc
                                                        tests cut the block at the first
                                                        one, and this comment cost three
                                                        red tests on 2026-08-29 *)
               | 'ossia' , [ ClefName ] , PartRef , [ 'as' , 'lines' , Integer ]
                                                     (* ossia partName — BARE, like staff,
                                                        with the same line-count selector.
                                                        ⚠️ keep semicolons out of comments
                                                        inside this production — the doc
                                                        tests cut the block at the first *)
               | 'chords' , PartRef                  (* independent chord ROW (lead sheet) *)
               | 'lyrics' , PartRef , [ 'sings' , PartRef ]
                                                     (* lyrics ROW. The optional 'sings' states
                                                        or repeats the track's melody binding
                                                        on the row - the SAME property the
                                                        definition spells, resolved together
                                                        (a different target is LYS7005, an
                                                        unknown one LYS7004). Unbound with no
                                                        'sings' anywhere - the even-spread
                                                        lead-sheet row. *)
               | ( 'title' | 'composer' ) , String   (* THIS score's own header — see below *)
               | 'fonts' , Identifier , [ FontBlock ] (* THIS score's faces: a reference to a
                                                        named top-level block, the optional
                                                        block overriding part of it *)
               | 'paper' , Identifier , [ PaperBlock ] (* THIS score's page, same shape *)
               | PartRef                            (* a bare part name: MIDI only — see below *)
               ;

TabStyle       = 'numbers' | 'full' ;
                 (* How much of the rhythm a tab staff draws.
                    'numbers' is LilyPond's plain TabStaff: fret DIGITS only, with no time
                    signature, stems, flags, beams, dots, rests, tuplet brackets or ties.
                    The tie's TARGET still drops its fret number, which is how a numbers tab
                    says "keep holding" -- the absent number, not a bow.
                    'full' is LilyPond's \tabFullNotation, less the key signature: a tab has
                    no note letters for a key signature to alter, and LilyPond removes the
                    Key_engraver from the context outright rather than hiding its stencil,
                    so no LilyPond mode prints one either. Slurs print in BOTH styles, which
                    is also LilyPond's answer -- the same block that hides the tie keeps the
                    slur.
                    WITH NO CLAUSE THE SCORE ANSWERS (user decision, 2026-08-29): a tab
                    beside a notation staff of the same part is 'numbers', because that staff
                    already carries the rhythm; a tab standing alone is 'full', because it
                    has to carry the rhythm itself. A condensed, combined or grand staff
                    counts as a notation staff; an ossia does not.
                    NOT YET DRAWN in 'full': beams. Stems, flags, dots, rests, the time
                    signature and ties are all there. *)


StaffGroupBody = '{' , { StaffRender | 'lyrics' PartRef [ 'sings' PartRef ] } , '}' ;
                 (* Several staves engraved as ONE GROUP. All three take `staff` items
                    with `lyrics NAME` rows between them — inside the braces as outside,
                    a bound row directly below the staff it sings is that staff's verse
                    (the chorale writes its words between the sopranos and the altos),
                    and a row that sings no adjacent staff is LYS6012, anything else
                    LYS6011. They differ only in what is drawn down the left edge, and
                    each is the LilyPond context of the same name (engraver-init.ly):

                      grandStaff    a BRACE, and bar lines drawn through the gap between
                                    the staves — the piano/harp reading of two staves as
                                    one instrument (LilyPond `GrandStaff`)
                      staffGroup    a BRACKET, bar lines drawn through — an orchestral
                                    family, e.g. the woodwinds (LilyPond `StaffGroup`)
                      choirStaff    a BRACKET, bar lines NOT drawn through: each staff
                                    keeps its own, so singers read independent lines
                                    (LilyPond `ChoirStaff`)

                    ⚠️ The word order of `staffGroup` is deliberate and is NOT a slip for
                    `groupStaff`. The other four `…Staff` items each PRODUCE a staff, or are
                    the established name of one; a staff group produces a GROUP OF STAVES and
                    says so. LilyPond spells its own contexts the same way, and for the same
                    reason: of its seventeen staff contexts, `StaffGroup` is the only one that
                    is not a musical term. *)

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

StaffRender    = 'staff' , [ ClefName ] , PartRef , [ DisplayName ] ,
                 [ 'as' , 'lines' , Integer ] ;
                 (* 'as lines N' (1..5) is THIS staff's line count — presentation
                    belongs to the rendering, so a lead sheet can write
                    'staff melody as lines 1' while the full score keeps five.
                    'as' and 'lines' are matched by text — 'as' also lexes as the
                    Dutch A-flat, 'lines' is an ordinary word. Ossia takes the
                    same selector. *)
                 (* A staff renders ONE part; everything that used to hang off it by
                    clause hangs by ORDER instead — score = a vertical stack of bands
                    (user decision, 2026-08-19, before the first tag). A bound
                    'lyrics NAME' row directly below the staff is its verse; a
                    'chords NAME' row directly above it aligns the symbols over it.
                    The old 'with chords NAME' / 'with lyrics NAME' clauses are
                    gone and 'with' is not a keyword at all (its migration error
                    LYS0031 retired with it; DiagnosticCodes records the number).
                    The same chord part can also feed a lead-sheet row, so a
                    progression is written once. *)
                 (* ⚠️ THE CLEF IS OPTIONAL, THE PART IS NOT — so a LONE clef word is
                    the PART NAME, not a clef with the name left off: 'staff bass'
                    renders a part literally named 'bass' (whose clef then comes from
                    its own definition), and reports LYS1007 when no such part is
                    declared. The four words this can happen to are treble, bass, alto
                    and tenor, which are also legal part names (SyntaxFacts.
                    IsPartNameKind); the other seven part-header clefs are not, so
                    'staff percussion' is a syntax error instead. The reading is
                    RenderSpecParser.ParseStaff's and ParseOssia's; until 2026-08-28 the
                    REFERENCE scan disagreed with both and collected nothing, so
                    'score main { staff bass }' over a part named 'bassline' engraved a
                    blank staff and 'lysc check' said "No errors found". *)
PartRef        = Identifier ;
DisplayName    = String ;
                 (* 'staff flute "津田さん"': overrides the instrument label for THIS
                    score only. QUOTED ONLY (2026-08-23, user decision — the bare form
                    was position-dependent: a ScoreItem may also be a bare MIDI-only
                    part name, so 'staff flute click' silently ate the click track as
                    a label. A trailing bare identifier is now always that part
                    reference; GRAMMAR_AUDIT §3.1. Also uniform with 'part X "Violin I"',
                    which was already quoted-only.) *)

(* THIS SCORE'S OWN HEADER: 'title' / 'composer' written inside a score restate the file's
   metadata for that score alone — the same two words as the top-level MetadataDecl, in a
   score body. A part-extract score can be headed with the part's name while the full score
   keeps the work's title. *)

(* A BARE PART NAME renders that part to MIDI ONLY: it is played and not engraved, which is
   how a click track or a cue part is carried without appearing on the page. Because it puts
   nothing on the page, a score of nothing but bare names has nothing to engrave and is the
   empty-body error (LYS6002) — a bare name accompanies staves, it does not replace them.
   A bare word after 'staff NAME' is this too — 'staff flute click' is flute's staff plus
   the click track. (Until 2026-08-23 that word was read as the staff's display name and
   the click track silently stopped playing; a paragraph here taught word order as the
   workaround. Display names are quoted-only now, so position no longer matters.) *)

(* A 'score' may carry its OWN 'form main { … }' to render a different arrangement
   (e.g. a practice excerpt); it overrides the top-level structure for that score only. *)

(* Examples:

   score main "full" {
     grandStaff { staff rightHand  staff leftHand }
   }

   score practice { staff melody }     // a second form, rendered to practice.svg

   score choral "satb" {               // a bracket, each staff keeping its own bar lines
     choirStaff { staff sop  staff alt  staff ten  staff bas }
   }                                   // (not 'soprano': of the clef names, only
                                       //  treble/bass/alto/tenor may also name a part)

   score winds "winds" {               // a bracket with bar lines drawn through
     staffGroup { staff flute  staff oboe  staff clarinet }
     title "Woodwinds"                 // this score's own header
     click                             // played, never engraved
   }
*)

### 7.1 Lead sheets (chords and/or lyrics, no staff)

(* A 'chords NAME { … }' / 'lyrics NAME { … }' part placed in a score with
   'chords NAME' / 'lyrics NAME' (instead of 'staff NAME') renders WITHOUT a staff:
   a grid of measure barlines, chord symbols between the bars (at their timing), and
   lyrics below. Source barlines ( | |: :| || |. ) are drawn, and follow the
   bare-barline rule (below): every written '|' closes one bar, the one that OPENS
   the run included, so '| C | F |' is an empty bar and then two. *)

(* Example:
   section Main {
     chords prog  { C G7 | Am F | C :| }
     lyrics words { Twin- kle | lit- tle | star | }
   }
   form main { Main }
   score main "sheet" { chords prog lyrics words }
*)

(* WRITING THE CHORDS AS DEGREES. An entry may be an absolute symbol (C, Am, G7, F#m,
   Bb7/D) or a ROMAN DEGREE of the key at that bar:

     chords prog { section A { Imaj7 | V7 | IIm7 | bVII | } }

   In C that is Cmaj7 G7 Dm7 B♭; in E♭ the same source is E♭maj7 B♭7 Fm7 D♭. The two
   spellings cannot collide — an absolute root is A-G and a numeral is I or V — and both
   resolve to the same chord, so the WRITTEN form and the DISPLAYED form are independent:
   a degree chart prints names by default and degrees under 'as roman', and a name chart
   does the same. A mid-piece 'key' rebases every degree after it.
   The degree grammar: an optional accidental 'b'/'#' (as many as needed), a numeral
   I II III IV V VI VII, the ordinary quality (Imaj7, IIm7, V7, VIIdim, Vaug, IIm7-5),
   and an optional '/' bass written as a degree too (V7/VII).
   ⚠️ Written with the ASCII 'b' and '#'. The PRINTED accidentals (♭ ♯) and the printed
   roman quality symbols (° ø) are refused by the lexer, so a degree cannot be pasted
   back into the source from the score it came out of — write 'bVII', not '♭VII', and
   'VIIdim', not 'VII°'. *)

(* SHOWING ONE TRACK TWO WAYS. A chord row takes 'as roman' (degrees for the key) or
   'as names' (the default). There is no third mode: to show BOTH, place the track twice —

     score main "sheet" { chords prog as roman  chords prog as names  lyrics words }

   which is two rows, in the order written, each its own band. 'as both' — one symbol with
   the degree stacked above the name — was retired 2026-08-23. ⚠️ The two are not quite the
   same and the difference is worth knowing: a slot with no chord (an 'r' printing N.C.)
   has no degree, so a roman row shows its NAME there. Stacked, that slot reads once per
   row. Anything else after 'as' is an error (LYS2012). *)

(* WHERE A TRACK'S CELLS GO. The example above is SECTION-major: each track block sits
   inside the section whose bars it fills, so the binding is where it is written. The
   PART-major spelling puts the track at the top level and names the sections inside it:

     chords prog  { section A { C G7 | } section B { Am F | } }
     lyrics words { section A { Twin- kle | } section B { lit- tle | } }

   In a part-major file a top-level track MUST be written that way. A flat top-level
   track has no section to anchor to, so its cells would run from bar 0 across whatever
   the form plays and every section after the first would get nothing. That is an error:
   LYS4002 for lyrics, LYS2011 for chords. A section-major or structureless file is not
   affected, and neither is a track block written inside a part or a section. *)

================================================================================
## 8. Music Expression
================================================================================

### 8.1 Music Block

MusicBlock     = '{' , { MusicItem } , '}' ;

MusicItem      = Note | Rest | Chord | Arpeggio | Barline | InlineVolta | PhraseRef
               | SlashNote | BareDuration
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

(* RHYTHM (COMPING) NOTATION, 2026-08-19. A SLASH NOTE is a pitchless note drawn
   as a slash head on the MIDDLE staff line — the token depicts the printed ink,
   the way '|' depicts a barline (HANDOFF §3 records the sigil-rule decision).
   Duration carry, stems, beams, ties and post-events behave as on an ordinary
   note; playback is SILENT; on a one-line staff (part property `lines 1`) the
   head sits on the line. The other spellings of '/' are untouched: `time 4/4`,
   `tuplet 3/2` and a chord entry's `c/g` never reach the note position.
   LILYPOND-REF: ly/property-init.ly improvisationOn — LilyPond's spelling of
   the same page (slash heads, no accidentals) on a written pitch; the twin
   exports exactly that form on the clef's middle-line pitch. *)
SlashNote      = '/' , [ DurationToken ] , { Annotation } ;

(* A BARE DURATION repeats the previous note, chord or slash at the new length:
   `bes8 8 8 8` is the electric-bass pump written once. The number is a WRITTEN
   duration — it sets the running default like any other. Rests and the empty
   chord <> are transparent to the run; a `q` threads as the chord it resolves
   to; an ARPEGGIO breaks the run (its members subdivide a total, so "again at
   a new length" has no single answer — LYS0016, loudly, instead of a guess);
   a bare duration with nothing before it to repeat is LYS0016. The repeated
   event carries the ORIGINAL's absolute pitches (expansion happens after
   relative resolution, like `q`), re-derives accidentals through the measure's
   own state, and takes only its OWN post-events.
   LILYPOND-REF: lily/parser.yy music_embedded — "duration post_events" builds a
   NoteEvent with no pitch; MEASURED against 2.26.0 (byte-identical SVGs): the
   pitch of a note, the FULL chord of a chord, and reads through rests.
   ⚠️ What this spelling trades away, decided knowingly (HANDOFF §3): `c 4` is
   no longer the detached-duration error — a dropped pitch letter (`4 g f e`
   meant as `a4 g f e`) now compiles as a repeat. The extra-time class still
   trips the bar check; the duration-preserving class is the price LilyPond
   pays for the same spelling. Since 2026-08-23 the CROSS-BARLINE shape of
   that class warns (LYS1031): a repeat reaching back across a barline is the
   shape the dropped letter takes, so a measure opening on a bare number is
   named — a warning, because the repeat is legal. Within-measure runs are
   the idiom and stay silent (GRAMMAR_AUDIT 3.3).
   ⚠️ '[' , Integer , '.' stays an INLINE VOLTA: a beamed run that opens on a
   DOTTED bare duration spells the slash or the pitch (`[/4. 8]`, `[bes4. 8]`). *)
BareDuration   = DurationToken , { Annotation } ;
                 (* spaced — a GLUED number is the previous item's duration *)

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
   c4, r2., <c e g>4, << c e g >>2. A glued number on a chord/arpeggio MEMBER is a
   misplaced duration (LYS0015: members share one, written after the bracket). A
   SPACED number inside brackets is a scale degree — the adjacency is what tells
   <c e g2> (mistake) from <c e g 2> (degree). A spaced number OUTSIDE brackets is
   a BARE DURATION (above; until 2026-08-19 it was the detached-duration error,
   whose code LYS0016 survives for a bare duration with nothing to repeat). *)
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
   (@chord / @chord(Am7)) labels it; any other annotation on the group or a bare member is not
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

(* BARE-BARLINE SEMANTICS (music). ONE SENTENCE: a written '|' CLOSES EXACTLY ONE
   MEASURE, and a measure with nothing in it is an empty one. So '|' after music closes
   that bar; a '|' on an empty span closes an EMPTY bar — the one that OPENS a block
   included, which is what makes `{ | | | | }` four bars and `{ | c1 }` two. An empty bar
   holds a slot to keep parts aligned and renders as an empty full-width bar; IT IS NOT
   DIAGNOSED, because the engine fills it with one full-measure SPACER of the meter in
   force — the `s1` (or `s2.`, …) the author would otherwise have had to type — so `| |`
   and `| s1 |` are the same music, on the page and in playback alike. Any zero-duration
   DIRECTIVE written in that span (a 'time' or 'key' change) belongs to the bar and goes
   with it.
   (Owner's decision, 2026-08-28, in two parts. First: the empty bar stopped being built
   with no contents and a duration of ZERO carrying the underfull warning LYS2001 — the
   zero was audible, since a gap written in one part pulled everything after it a whole
   bar early against the others, the engraver walking BARS and the MIDI exporter
   DURATIONS. Then: a leading '|' stopped being an ANCHOR that created nothing. Under
   that older reading `{ | c1 | c1 | }` == `{ c1 | c1 }` and an empty bar had to be
   spelled as the PAIR `| |`; it cost a bar wherever an author fenced a block, measured
   on the author's own books — a lead sheet written four bars to the line came out three
   in the two blocks that opened with '|', and a chord row that fenced its pickup bar
   printed every chord one bar early with none on the last.)
   THREE BARLINES CLOSE NOTHING, and each for its own reason. A TYPED barline ('||',
   '|.', ':|') on an empty span DECORATES the bar behind it — it retro-types that bar's
   end and creates nothing (with no bar behind it there is nothing to decorate, so it
   closes its own span like any other written bar: `{ || }` is one empty bar). A '|:'
   decorates nothing either — it OPENS the bar in front of it — so it makes an empty bar
   only when a written bar OPENED the span it leaves behind: `c1 | |: d1 :|` is three
   bars, `c1 |: d1 :|` (one barline doing both jobs) is two, and a '|:' at a block's head
   is just the opener, so `{ |: d1 :| }` is one. THIS IS THE ONE PLACE '|' AND '|:' PART
   COMPANY, and `{ | |: d1 :| }` is accordingly two bars. And a '|' landing on a boundary
   something else JUST CLOSED merely confirms it: the auto-fill that closes a full bar at
   the meter (which is what keeps a trailing 'c1 |' one bar, not two), a form's own
   '|:' / ':|', and the EXIT of a phrase reference — so 'phrase x { c d e f | }' used as
   'x | x' is two content bars, not two + a gap. ENTERING a phrase body changes nothing,
   so a '|' at the body's head closes an empty bar exactly as one at a section's head
   does; otherwise extracting a section into a phrase would silently lose a bar.
   LYRICS AND CHORD ROWS FOLLOW THE SAME RULE, counting every written bar: '| きら |
   ひかる |' is "bar 1 has no syllables" followed by two sung bars, one bar longer than
   'きら | ひかる'. That is how a verse skips a rest bar the melody opens with. *)

(* First/second-time endings inside a |: … :| repeat. '[' followed by an integer is a
   volta; otherwise '[' … ']' is a manual beam group. The '[' is REQUIRED; the closing
   ']' is OPTIONAL — present draws the right cap (closed ending), absent leaves it open. *)
InlineVolta    = '[' , Integer , [ ( '-' | ',' ) , Integer ] , '.' , { MusicItem } , [ ']' ] ;
Beam           = '[' | ']' ;
PhraseRef      = Identifier , { "'" | ',' } ;
                 (* ⚠️ The '$' sigil was REMOVED 2026-08-22. The two spellings had been
                    measured identical 2026-08-16 (eight forms, both octave modes), so the
                    sigil distinguished nothing — except three name families, each now
                    closed on its own terms: drum vocabulary and 'q' are refused as
                    phrase names at the DECLARATION (LYS1030, since a bare reference
                    would read as a drum note and silently turn the staff into a
                    DrumStaff); dynamics (p, f, mf, …) were already reserved words
                    there; and the clef words (bass, treble, alto, tenor) are
                    reachable bare because 'clef bass' reaches its clef through its
                    own keyword, so the music stream reads them as references.
                    Unreleased, so no migration diagnostic ('$theme' is now an
                    unexpected character followed by a reference — LYS0018 names it).
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
                    together.
                    ⚠️ A GLUED '(N)' after the marks WAS a diatonic interval —
                    Melody'(3) a third up in the ambient key — and was REMOVED
                    2026-08-28 (user decision). It asked a reader to hold two
                    non-obvious rules to read one token: the mark stopped meaning
                    'an octave' and started meaning 'upwards' as soon as (N) was
                    attached, and the degree was 1-based on top of that ('(8) == ').
                    And a single SPACE turned the whole construct into a reference
                    followed by a slur. No book in the tree wrote it. There is now no
                    per-reference transposition at all: 'transpose' is a part property
                    and chromatic, so a motif quoted a third higher is written out.
                    The spelling needs no migration diagnostic — '3' is not a valid
                    duration, so Melody'(3) stops at two hard errors on the number.
                    A reference is ONE item to the relative chain — the chord rule:
                    the note that follows is relative to the phrase's ANCHOR (its
                    first note's bare letter, shifted with the reference's marks),
                    never to its interior, so how the body ends — or is
                    later edited — cannot move the music after a reference. A body
                    evaluates in the
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

Annotation     = '@' , [ '!' ] , AnnotationName , [ '(' , Arg , { ( ' ' | ',' ) , Arg } , ')' ] , [ Placement ] ;
Placement      = '.up' | '.down' ;   (* force above / below; default is automatic *)
(* A value-bearing annotation puts its argument(s) in parentheses (space- or
   comma-separated); '.' is reserved for the .up / .down placement suffix. *)
(* '@!X' is the TERMINATOR: it ends what '@X' opened, and it reports the SAME name, so the
   vocabulary and the "did you mean" list stay ONE list. Only families that HAVE an end
   accept it — today the text spanner (@!rit, @!accel, @!rall, @!textSpan); any other name
   written with '!' is reported rather than dropped. A span nobody ends draws NOTHING
   (LYS4018) — LilyPond's own answer, not a shortening. *)

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
   - Chord name:    c4@chord(C) , d4@chord(Dm)   (* the SYMBOL as it prints — the same
                    ChordEntry format as a chords row: Am7, G7, F#m, Bb7/D. The retired
                    lowercase ':' entry ('@chord(a:m)') is NOT recognised — LYS1008 warns
                    and the symbol is not engraved. A bare '@chord' derives it from the notes. *)
   - Fingering:     <c@finger(1) e@finger(3)>4
   - Rehearsal mark: c4@mark("A")   (label is a quoted string)
   - Free text:      c4@text("dolce") , c4@text("pizz.").up   (italic; below by default)
   - Half ties:     c4@laissezVibrer (l.v. into silence) , c4@repeatTie (from a repeat)
   - Effects:       @cross / @dead (x notehead) ,
                    @fall @doit (jazz bends) , @breath @caesura
                    (* a CUE is not an annotation — it is a region, 'cue { … }', below *)
   - Feathered beam: c16@feather(right) … (accel) / @feather(left) (rit)
   - Spanners:       @textSpan("poco rit.") … @!textSpan   [sugar: @rit @accel @rall,
                     each ended by @!rit / @!accel / @!rall — an end is REQUIRED]
                     @ottava(…) @quindicesima(…) … @!ottava ,  [labels: 8va/15ma; @…(bassa) = down]
                     (* an end is REQUIRED here too; '@loco' is retired — it named a
                        mark nothing printed, and LilyPond has no such command either *)
                     @startTrillSpan … @stopTrillSpan ,
                     @sustainOn … @sustainOff , @sostenutoOn … @sostenutoOff , @unaCorda … @treCorde *)

(* Example: c4@staccato.up d4@accent@p <e g>4@arpeggio | *)

### 8.5 Tuplets and Grace notes

Tuplet         = 'tuplet' , Integer , '/' , Integer , MusicBlock ;   (* nesting allowed *)
Grace          = ( 'grace' | 'acciaccatura' | 'appoggiatura' ) , MusicBlock ;
Repeat         = 'repeat' , ( 'percent' | 'unfold' | 'tremolo' ) , [ Integer ] , MusicBlock ;
                 (* repeat percent 2 { … } = percent-repeat the measure; volta repeats
                    use the symbolic |: … :| form, NOT a 'repeat' keyword *)
                 (* WHICH SIGN a percent repeat prints is the BODY'S LENGTH, not the
                    repeat count, and LilyPond decides it once, on the second iteration
                    (percent-repeat-iterator.cc's next_element). There are TWO tests and
                    one else — no more:
                      one measure   -> the single % in each repeated measure
                      two measures  -> ONE double-% on the bar line between the pair,
                                       so 'repeat percent 4 { r1 | r1 }' is three double
                                       signs, not six single ones
                      anything else -> ONE repeat slash where the repetition starts, and
                                       the rest of that repetition's measures are BLANK.
                                       'repeat percent 2 { c1 | c1 | c1 }' is three written
                                       measures, one slash, and two empty bars.
                    How many slashes that last sign carries comes from the body's WRITTEN
                    durations (calc-repeat-slash-count): all equal gives a plain slash,
                    unequal gives the double slash. *)
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

(* The SYNTAX above accepts any Grob.property, but the consumed vocabulary is four pairs —
   NoteHead.transparent, Stem.transparent, NoteHead.color, Stem.color. Anything else is
   refused (LYS1029, "not supported in this version") rather than silently doing nothing,
   so the examples below are limited to the four; the list grows, and each addition removes
   one error. ALL FOUR TAKE EFFECT — the vocabulary and the engine agree in both
   directions since 2026-08-23: color's live reader gained its rows (it had been refused
   while working, GRAMMAR_AUDIT §4.2), and NoteColumn.force-hshift left the vocabulary
   (its reader sits disabled behind ElementCoordinator's ForceHshiftEnabled = false, so
   the spelling was accepted and then silently ignored — GRAMMAR_AUDIT §4.3; it errs
   honestly until the per-voice implementation lands, and then returns). *)

(* Example:
   override NoteHead.color = red               // named colour, or a "#rrggbb" string
   override Stem.color = "#8000ff"
   c4 d e f |
   revert NoteHead.color
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
  melody { riff | g4@p a( b c') | }
  lyrics words sings melody { Sing a song now | one two three four | }
}

// A staff-less lead sheet (chords + lyrics, no notes). Its lyric row is its
// own track (no `sings`): with no melody to sing, the syllables spread evenly.
section Sheet {
  chords prog  { C Am |: F G7 | C :| }
  lyrics sheetWords { Twin- kle twin- kle | lit- tle | star | }
}

form main  { Verse }
form sheet { Sheet }

score main  "demo"  { staff melody  lyrics words }
score sheet "sheet" { chords prog lyrics sheetWords }
```

MIDI export: `lysc midi demo.lys demo.mid` (no score block needed).

================================================================================
## 11. Error Detection
================================================================================

⚠️ **The two tables below are a SAMPLE, not the set** (recorded 2026-08-21).
`Diagnostic.cs` declares **131 codes**; these twelve rows are the structural ones a reader
meets first and have never been the whole list — so a rule absent here is not a rule the
language lacks. The set itself is `LilySharp.Core/Syntax/Diagnostic.cs`, which carries each
code's reason and the retired numbers.

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
