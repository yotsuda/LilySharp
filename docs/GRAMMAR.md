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
        | 'part' | 'phrase' | 'section' | 'structure' | 'score'
        | 'staff' | 'grandStaff' | 'tab' | 'ossia' | 'voice'
        | 'lyrics' | 'chords' | 'tuning' | 'instrument'
        | 'transpose' | 'octave' | 'using' | 'use' | 'let' | 'break' | 'partial'
        | 'tuplet' | 'grace' | 'acciaccatura' | 'appoggiatura'
        | 'repeat' | 'volta' | 'alternative'
        | 'override' | 'revert' | 'once' | 'with'
        | 'major' | 'minor' | 'ionian' | 'dorian' | 'phrygian' | 'lydian' | 'mixolydian'
        | 'aeolian' | 'locrian'
        | 'treble' | 'bass' | 'alto' | 'tenor' | 'treble_8'
        | 'segno' | 'fine' | 'coda' | 'dc' | 'ds' | 'al' | 'to'
        | 'ppp' | 'pp' | 'p' | 'mp' | 'mf' | 'ff' | 'fff'
        ;

(* The four clef-name words (treble bass alto tenor) ARE allowed as part / section /
   phrase names. Single letters a-g are pitches ('f' is a pitch, not a keyword — @f
   resolves the dynamic from text); r / R / s are rests. The reserved dynamic words
   above (p, pp, mp, …) cannot be identifiers. 'swing'/'shuffle' are NOT reserved
   (tempo value words). Articulation, ornament, dynamic-text and mark NAMES
   (staccato, tr, sfz, cresc, dim, …) are resolved from the '@name' text and are
   NOT reserved. 'volta'/'alternative' are reserved only to reject the removed
   LilyPond-style forms; 'using' is reserved for multi-file support. *)

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

TopLevelItem   = VersionDecl                      (* optional language-version marker *)
               | MetadataDecl                     (* title, composer *)
               | GlobalSetting                    (* tempo, time, key *)
               | PartDecl                         (* part definitions *)
               | PhraseDecl                       (* reusable music fragments *)
               | SectionDecl                      (* musical sections - REQUIRED *)
               | StructureDecl                    (* song form - optional *)
               | ScoreDecl                        (* output definitions - REQUIRED *)
               | OverrideDecl                     (* engraving overrides *)
               ;

### 2.2 Version (optional)

VersionDecl    = 'version' , Integer ;
                 (* optional, recommended first line: the language version the
                    file targets, a bare number e.g. version 1 (not quoted).
                    Recorded so future grammar revisions can branch on it;
                    omitting it = current grammar. *)

### 2.3 Metadata

MetadataDecl   = MetadataKey , String ;
MetadataKey    = 'title' | 'composer' ;

### 2.4 Global Settings

GlobalSetting  = TempoDecl | TimeDecl | KeyDecl | PartialDecl | OctaveDecl ;

PartialDecl    = 'partial' , DurationToken ;
                 (* the piece-opening pickup, declared ONCE for every part; an
                    in-music 'partial' declares it per voice (or mid-piece).
                    A bare underfull first bar gets a warning suggesting this. *)
OctaveDecl     = 'octave' , ( 'absolute' | 'relative' ) ;

TempoDecl      = 'tempo' , [ String ] , [ DurationBase , '=' ] , Integer ,
                 [ ( 'swing' | 'shuffle' ) , [ Integer ] ] ;
                 (* tempo 120 / tempo "Allegro" 120 / tempo "Andante" 4 = 96 —
                    the string is tempo text, `duration =` picks the beat unit.
                    'tempo 120 swing' draws a shuffle-feel equation; 'swing 16' = 16th swing *)
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

================================================================================
## 3. Part Definition
================================================================================

(* Parts declare instruments/voices. Header attributes are written BARE — the same
   command form as the top-level commands (NO colon, NO '='). *)

PartDecl       = 'part' , Identifier , [ PartBody ] ;
PartBody       = '{' , { PartProperty } , '}' ;
PartProperty   = 'clef'        , ClefName
               | 'instrument'  , ( Identifier , [ String ] | String )
               | 'transpose'   , PitchToken
               | 'tuning'      , Identifier
               | 'name'        , String                      (* display name *)
               | 'octave'      , ( 'absolute' | 'relative' | Integer )
               | 'removeEmpty' , ( 'true' | 'all' | 'false' ) ;

ClefName       = 'treble' | 'bass' | 'alto' | 'tenor' | 'treble_8' ;

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
LyricSyllable  = LyricText | '~' | '_' ;          (* '-' suffix joins one word's syllables *)

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
               | StructureVolta                     (* a repeat volta ending *)
               ;

(* A repeat volta ending inside a |: … :| repeat, referencing a section:
   form main { |: A [1. D] :| [2. O] }
   The '[' is REQUIRED; the closing ']' is OPTIONAL — present draws the right cap
   (closed ending), absent leaves it open. *)
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

ScoreDecl      = 'score' , [ String ] , '{' , [ StructureDecl ] , { ScoreItem } , '}' ;

ScoreItem      = StaffRender                        (* staff partName — BARE, no braces *)
               | 'grandStaff' , '{' , { StaffRender } , '}'
               | 'tab' , PartRef                     (* tablature: tab partName *)
               | 'ossia' , [ ClefName ] , PartRef       (* ossia partName — BARE, like staff *)
               | 'chords' , PartRef                  (* independent chord ROW (lead sheet) *)
               | 'lyrics' , PartRef                  (* independent lyrics ROW (lead sheet) *)
               ;

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

   score main "practice" { form main { Intro } staff melody }
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
   form main { Main }
   score main "sheet" { chords prog lyrics words }
*)

================================================================================
## 8. Music Expression
================================================================================

### 8.1 Music Block

MusicBlock     = '{' , { MusicItem } , '}' ;

MusicItem      = Note | Rest | Chord | Arpeggio | Barline | InlineVolta | PhraseRef
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
     still shift the whole group. *)

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
   This reuses '<< … >>' (LilyPond's parallel-voice form, which Lily# writes as
   'voice { }'); a '\\' inside is reported as the removed-polyphony form, not an arpeggio. *)
ArpMember      = PitchToken | ScaleDegree | Chord | Rest ;   (* no DurationToken on a member *)
ScaleDegree    = Integer , [ 'is' | 'isis' | 'es' | 'eses' ] , { "'" | ',' } ;
                 (* anchor-relative degree: 1 = root/tonic, 3 = third, 8 = octave; also the '<c 3 5>' chord form *)
Arpeggio       = '<<' , ArpMember , { ArpMember } , '>>' , { "'" | ',' } , [ DurationToken ] ;

Barline        = '|' | '||' | '|.' | '|:' | RepeatEnd ;
RepeatEnd      = ':|' , [ '*' , Integer ] ;          (* :|*N plays the span N times, default 2 *)

(* BARE-BARLINE SEMANTICS (music): a bare '|' after music closes that bar. On an empty
   span a SINGLE bare '|' merely anchors the boundary it sits on — the section start
   (a leading '|'), the section end (a trailing '|'), or a just-auto-filled bar — and
   creates nothing, so `{ | c1 | c1 | }` == `{ c1 | c1 }`. An EMPTY MEASURE is always
   an explicit `| |` PAIR: two written barlines with nothing between (leading, mid, or
   trailing; `| | |` is two). It holds a slot to keep parts aligned, renders as an
   empty bar, and warns (LYS2008) until filled — an empty measure is thus always
   visible in the source. A TYPED barline on an empty span decorates the previous
   bar's end. LYRICS differ BY DESIGN: they carry no durations, so their barlines ARE
   the structure — a lone leading '|' there means "bar 1 has no syllables". *)

(* First/second-time endings inside a |: … :| repeat. '[' followed by an integer is a
   volta; otherwise '[' … ']' is a manual beam group. The '[' is REQUIRED; the closing
   ']' is OPTIONAL — present draws the right cap (closed ending), absent leaves it open. *)
InlineVolta    = '[' , Integer , [ ( '-' | ',' ) , Integer ] , '.' , { MusicItem } , [ ']' ] ;
Beam           = '[' | ']' ;
PhraseRef      = '$' , Identifier ;

### 8.3 Ties, Slurs, Beams

Tie            = '~' ;            (* same pitch across notes/barline: c4~ | c4. A tie binds to
                                     the IMMEDIATELY following note/chord, which must repeat the
                                     tied pitch — a different pitch or a rest there ties nothing
                                     and warns (LYS4007); different pitches connect with a slur. *)
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
   - Chord name:    c4@chord(C) , d4@chord(Dm)
   - Fingering:     <c@finger(1) e@finger(3)>4
   - Rehearsal mark: c4@mark("A")   (label is a quoted string)
   - Free text:      c4@text("dolce") , c4@text("pizz.").up   (italic; below by default)
   - Half ties:     c4@laissezVibrer (l.v. into silence) , c4@repeatTie (from a repeat)
   - Cue/effects:   @cue (small cue note) , @cross / @dead (x notehead) ,
                    @fall @doit (jazz bends) , @breath @caesura
   - Feathered beam: c16@feather(right) … (accel) / @feather(left) (rit)
   - Marks/spanners: @segno @coda @fine @dc @ds @rit @accel
                     @ottava(…) @quindicesima(…) … @loco ,   [labels: 8va/15ma; @…(bassa) = down]
                     @startTrillSpan … @stopTrillSpan , @ped … @ped(off) , @sost … @sost(off) , @una(corda) … @tre(corde) *)

(* Example: c4@staccato.up d4@accent@p <e g>4@arpeggio | *)

### 8.5 Tuplets and Grace notes

Tuplet         = 'tuplet' , Integer , '/' , Integer , MusicBlock ;   (* nesting allowed *)
Grace          = ( 'grace' | 'acciaccatura' | 'appoggiatura' ) , MusicBlock ;
Repeat         = 'repeat' , ( 'percent' | 'unfold' | 'tremolo' ) , [ Integer ] , MusicBlock ;
                 (* repeat percent 2 { … } = percent-repeat the measure; volta repeats
                    use the symbolic |: … :| form, NOT a 'repeat' keyword *)

================================================================================
## 9. Override / Revert (engraving properties)
================================================================================

OverrideDecl   = [ 'once' ] , 'override' , Grob , '.' , Property , '=' , OverrideValue
               | 'revert' , Grob , '.' , Property ;
OverrideValue  = Integer | '-' Integer | Identifier | String ;
                 (* the value form fits the property: a length/position is an
                    integer, a direction/symbol an identifier (e.g. up, red), a
                    colour a string ("red"). Stored as text and reparsed per property. *)

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
