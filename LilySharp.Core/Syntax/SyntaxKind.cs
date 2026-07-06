// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

namespace LilySharp.Core.Syntax;

/// <summary>
/// Defines all syntax kinds (tokens and nodes) in LilySharp.
/// </summary>
public enum SyntaxKind : ushort
{
    // === Special ===
    /// <summary>The absence of any syntax kind.</summary>
    None = 0,
    /// <summary>The end-of-file marker token.</summary>
    EndOfFile,
    /// <summary>An unrecognized or invalid token.</summary>
    BadToken,

    // === Trivia ===
    /// <summary>Whitespace trivia between tokens.</summary>
    WhitespaceTrivia,
    /// <summary>A single-line comment trivia.</summary>
    LineCommentTrivia,
    /// <summary>A block comment trivia.</summary>
    BlockCommentTrivia,
    /// <summary>An end-of-line trivia.</summary>
    EndOfLineTrivia,

    // === Literals ===
    /// <summary>An integer literal token.</summary>
    IntegerLiteral,
    /// <summary>A string literal token.</summary>
    StringLiteral,

    // === Identifiers ===
    /// <summary>An identifier token.</summary>
    Identifier,

    // === Pitch Names ===
    /// <summary>The pitch-name token <c>c</c>.</summary>
    PitchC,
    /// <summary>The pitch-name token <c>d</c>.</summary>
    PitchD,
    /// <summary>The pitch-name token <c>e</c>.</summary>
    PitchE,
    /// <summary>The pitch-name token <c>f</c>.</summary>
    PitchF,
    /// <summary>The pitch-name token <c>g</c>.</summary>
    PitchG,
    /// <summary>The pitch-name token <c>a</c>.</summary>
    PitchA,
    /// <summary>The pitch-name token <c>b</c>.</summary>
    PitchB,

    // === Accidentals (suffixes, not separate tokens) ===
    // Handled as part of pitch token

    // === Duration ===
    /// <summary>A duration number (1, 2, 4, 8, 16, 32, 64, 128).</summary>
    DurationNumber,
    /// <summary>A duration dot (<c>.</c>).</summary>
    Dot,

    // === Structure Keywords (no backslash) ===
    /// <summary>The <c>section</c> keyword.</summary>
    SectionKeyword,
    /// <summary>The <c>structure</c> keyword.</summary>
    StructureKeyword,
    /// <summary>The <c>include</c> keyword.</summary>
    IncludeKeyword,
    /// <summary>The <c>version</c> keyword (optional language-version directive).</summary>
    VersionKeyword,
    /// <summary>The <c>score</c> keyword (legacy).</summary>
    ScoreKeyword,
    /// <summary>The <c>part</c> keyword (legacy).</summary>
    PartKeyword,
    /// <summary>The <c>staff</c> keyword.</summary>
    StaffKeyword,
    /// <summary>The <c>grandStaff</c> keyword.</summary>
    GrandStaffKeyword,
    /// <summary>The <c>tab</c> keyword.</summary>
    TabKeyword,
    /// <summary>The <c>ossia</c> keyword.</summary>
    OssiaKeyword,
    /// <summary>The <c>voice</c> keyword.</summary>
    VoiceKeyword,
    /// <summary>The <c>phrase</c> keyword.</summary>
    PhraseKeyword,
    /// <summary>The <c>repeat</c> keyword (legacy).</summary>
    RepeatKeyword,
    /// <summary>The <c>volta</c> keyword (legacy).</summary>
    VoltaKeyword,
    /// <summary>The <c>alternative</c> keyword (legacy).</summary>
    AlternativeKeyword,
    /// <summary>The <c>let</c> keyword (legacy).</summary>
    LetKeyword,
    /// <summary>The <c>use</c> keyword (legacy).</summary>
    UseKeyword,
    /// <summary>The <c>title</c> keyword.</summary>
    TitleKeyword,
    /// <summary>The <c>composer</c> keyword.</summary>
    ComposerKeyword,
    /// <summary>The <c>tempo</c> keyword.</summary>
    TempoKeyword,
    /// <summary>The <c>time</c> keyword.</summary>
    TimeKeyword,
    /// <summary>The <c>key</c> keyword.</summary>
    KeyKeyword,
    /// <summary>The <c>clef</c> keyword.</summary>
    ClefKeyword,
    /// <summary>The <c>major</c> keyword.</summary>
    MajorKeyword,
    /// <summary>The <c>minor</c> keyword.</summary>
    MinorKeyword,
    /// <summary>The <c>tuplet</c> keyword.</summary>
    TupletKeyword,
    /// <summary>The <c>treble</c> clef keyword.</summary>
    TrebleKeyword,
    /// <summary>The <c>bass</c> clef keyword.</summary>
    BassKeyword,
    /// <summary>The <c>alto</c> clef keyword.</summary>
    AltoKeyword,
    /// <summary>The <c>tenor</c> clef keyword.</summary>
    TenorKeyword,
    /// <summary>The <c>treble_8</c> clef keyword (G clef sounding an octave lower).</summary>
    Treble8Keyword,
    /// <summary>The <c>soprano</c> clef keyword (C clef on line 1).</summary>
    SopranoKeyword,
    /// <summary>The <c>mezzoSoprano</c> clef keyword (C clef on line 2).</summary>
    MezzoSopranoKeyword,
    /// <summary>The <c>baritone</c> clef keyword (C clef on line 5).</summary>
    BaritoneKeyword,
    /// <summary>The <c>bass_8</c> clef keyword (F clef sounding an octave lower).</summary>
    Bass8Keyword,
    /// <summary>The percussion clef keyword (unpitched percussion clef).</summary>
    PercussionKeyword,
    /// <summary>The <c>treble^8</c> clef keyword (G clef sounding an octave higher).</summary>
    Treble8UpKeyword,
    /// <summary>The <c>grace</c> keyword.</summary>
    GraceKeyword,
    /// <summary>The <c>acciaccatura</c> grace-note keyword.</summary>
    AcciaccaturaKeyword,
    /// <summary>The <c>appoggiatura</c> grace-note keyword.</summary>
    AppogiaturaKeyword,
    /// <summary>The <c>lyrics</c> keyword.</summary>
    LyricsKeyword,
    /// <summary>The <c>chords</c> keyword (independent chord part: <c>chords name { ... }</c> plus a score row).</summary>
    ChordsKeyword,
    /// <summary>The <c>with</c> keyword (staff modifier: <c>staff NAME with chords CHORDPART</c>).</summary>
    WithKeyword,
    /// <summary>The <c>tuning</c> keyword.</summary>
    TuningKeyword,
    /// <summary>The <c>transpose</c> keyword.</summary>
    TransposeKeyword,
    /// <summary>The <c>octave</c> keyword.</summary>
    OctaveKeyword,
    /// <summary>The <c>instrument</c> keyword.</summary>
    InstrumentKeyword,
    /// <summary>The <c>channel</c> keyword.</summary>
    ChannelKeyword,
    /// <summary>The <c>break</c> keyword (line break).</summary>
    BreakKeyword,
    /// <summary>The <c>partial</c> keyword (anacrusis / pickup measure).</summary>
    PartialKeyword,


    // === Navigation Keywords (structure block) ===
    /// <summary>The <c>segno</c> keyword.</summary>
    SegnoKeyword,
    /// <summary>The <c>fine</c> keyword.</summary>
    FineKeyword,
    /// <summary>The <c>coda</c> keyword.</summary>
    CodaKeyword,
    /// <summary>The <c>dc</c> (da capo) keyword.</summary>
    DcKeyword,
    /// <summary>The <c>ds</c> (dal segno) keyword.</summary>
    DsKeyword,
    /// <summary>The <c>al</c> keyword.</summary>
    AlKeyword,
    /// <summary>The <c>to</c> keyword.</summary>
    ToKeyword,

    // === Mode Keywords ===
    /// <summary>The <c>ionian</c> mode keyword (equivalent to major).</summary>
    IonianKeyword,
    /// <summary>The <c>dorian</c> mode keyword.</summary>
    DorianKeyword,
    /// <summary>The <c>phrygian</c> mode keyword.</summary>
    PhrygianKeyword,
    /// <summary>The <c>lydian</c> mode keyword.</summary>
    LydianKeyword,
    /// <summary>The <c>mixolydian</c> mode keyword.</summary>
    MixolydianKeyword,
    /// <summary>The <c>aeolian</c> mode keyword.</summary>
    AeolianKeyword,
    /// <summary>The <c>locrian</c> mode keyword.</summary>
    LocrianKeyword,

    // === Rest ===
    /// <summary>The rest token <c>r</c>.</summary>
    RestR,
    /// <summary>The spacer-rest token <c>s</c>.</summary>
    RestS,
    /// <summary>The full-measure rest token <c>R</c>.</summary>
    RestR_Full,

    // === String Number (for tablature) ===
    /// <summary>A tablature string number (<c>\1</c> through <c>\6</c>).</summary>
    StringNumber,

    // === Punctuation ===
    /// <summary>An opening brace <c>{</c> token.</summary>
    OpenBrace,
    /// <summary>A closing brace <c>}</c> token.</summary>
    CloseBrace,
    /// <summary>An opening parenthesis <c>(</c> token.</summary>
    OpenParen,
    /// <summary>A closing parenthesis <c>)</c> token.</summary>
    CloseParen,
    /// <summary>An opening angle bracket <c>&lt;</c> token.</summary>
    OpenAngle,
    /// <summary>A closing angle bracket <c>&gt;</c> token.</summary>
    CloseAngle,
    /// <summary>An opening square bracket <c>[</c> token.</summary>
    OpenBracket,
    /// <summary>A closing square bracket <c>]</c> token.</summary>
    CloseBracket,
    /// <summary>A bar-line <c>|</c> token.</summary>
    Bar,
    /// <summary>A double bar-line <c>||</c> token.</summary>
    DoubleBar,
    /// <summary>A final bar-line <c>|.</c> token.</summary>
    FinalBar,
    /// <summary>A repeat-start bar-line <c>|:</c> token.</summary>
    RepeatStartBar,
    /// <summary>A repeat-end bar-line <c>:|</c> token.</summary>
    RepeatEndBar,
    // LineBreakBar removed - use BreakKeyword instead
    /// <summary>A tilde <c>~</c> token (tie).</summary>
    Tilde,
    /// <summary>A plus <c>+</c> token (additive meters, e.g. <c>time 3+2/8</c>).</summary>
    Plus,
    /// <summary>A dashed bar-line <c>!</c> token (LilyPond <c>\bar "!"</c>).</summary>
    DashedBar,
    /// <summary>An asterisk <c>*</c> token (multi-measure rest count, e.g. <c>R1*N</c>).</summary>
    Asterisk,
    /// <summary>A colon <c>:</c> token.</summary>
    Colon,
    /// <summary>A tremolo-beam suffix (<c>:8</c>, <c>:16</c>, <c>:32</c>).</summary>
    TremoloSuffix,
    /// <summary>An equals <c>=</c> token.</summary>
    Equals,
    /// <summary>A slash <c>/</c> token.</summary>
    Slash,
    /// <summary>An at-sign <c>@</c> token.</summary>
    At,
    /// <summary>An underscore <c>_</c> token.</summary>
    Underscore,
    /// <summary>A backslash <c>\</c> token.</summary>
    Backslash,
    /// <summary>A comma <c>,</c> token.</summary>
    Comma,
    /// <summary>A minus <c>-</c> token.</summary>
    Minus,
    /// <summary>An apostrophe <c>'</c> token.</summary>
    Apostrophe,
    /// <summary>A dollar-sign <c>$</c> token.</summary>
    Dollar,
    /// <summary>A double opening angle <c>&lt;&lt;</c> token (parallel-music start).</summary>
    DoubleOpenAngle,
    /// <summary>A double closing angle <c>&gt;&gt;</c> token (parallel-music end).</summary>
    DoubleCloseAngle,

    // Articulation/ornament names (staccato, tr, mordent, cresc, dim, …) are no
    // longer reserved keywords: they are written as '@name' and resolved from text
    // by ArticulationRegistry / the mark registry. Only the dynamic level marks
    // below remain distinct tokens (some share spelling with pitch letters).

    // === Override/Revert Keywords (no backslash, follows LilySharp convention) ===
    /// <summary>The <c>override</c> keyword.</summary>
    OverrideKeyword,
    /// <summary>The <c>revert</c> keyword.</summary>
    RevertKeyword,
    /// <summary>The <c>once</c> keyword.</summary>
    OnceKeyword,

    // === Dynamics (with backslash) ===
    /// <summary>The <c>\ppp</c> dynamic token (pianississimo).</summary>
    DynamicPPP,
    /// <summary>The <c>\pp</c> dynamic token (pianissimo).</summary>
    DynamicPP,
    /// <summary>The <c>\p</c> dynamic token (piano).</summary>
    DynamicP,
    /// <summary>The <c>\mp</c> dynamic token (mezzo-piano).</summary>
    DynamicMP,
    /// <summary>The <c>\mf</c> dynamic token (mezzo-forte).</summary>
    DynamicMF,
    /// <summary>The <c>\f</c> dynamic token (forte).</summary>
    DynamicF,
    /// <summary>The <c>\ff</c> dynamic token (fortissimo).</summary>
    DynamicFF,
    /// <summary>The <c>\fff</c> dynamic token (fortississimo).</summary>
    DynamicFFF,

    // === Nodes: Top Level ===
    /// <summary>The compilation-unit root node.</summary>
    CompilationUnit,
    /// <summary>A metadata declaration node.</summary>
    MetadataDeclaration,
    /// <summary>A variable declaration node (legacy, <c>name = { ... }</c>).</summary>
    VariableDeclaration,
    /// <summary>A phrase declaration node (<c>phrase name { ... }</c>).</summary>
    PhraseDeclaration,
    /// <summary>A part declaration node (<c>part name { props }</c>).</summary>
    PartDeclaration,
    /// <summary>A variable reference node.</summary>
    VariableReference,

    // === Nodes: New Structure ===
    /// <summary>A section declaration node (<c>section Name { ... }</c>).</summary>
    SectionDeclaration,
    /// <summary>A structure declaration node (<c>structure { ... }</c>).</summary>
    StructureDeclaration,
    /// <summary>An include directive node (<c>include "file.lys"</c>).</summary>
    IncludeDirective,
    /// <summary>An optional language-version directive node (<c>version "1"</c>) —
    /// a soft top-level marker that lets future grammar revisions branch behavior
    /// without breaking documents that declare their version.</summary>
    VersionDeclaration,
    /// <summary>A render declaration node (<c>render Name "file.svg" { ... }</c>).</summary>
    RenderDeclaration,
    /// <summary>A part block node (e.g. <c>guitar { ... }</c> inside a section).</summary>
    PartBlock,
    /// <summary>A lyrics block node (<c>lyrics { ... }</c> inside a section).</summary>
    LyricsBlock,
    /// <summary>A lyric measure node (<c>syllable syllable |</c> inside lyrics).</summary>
    LyricMeasure,
    /// <summary>A single lyric syllable node.</summary>
    LyricSyllable,
    /// <summary>A chord-names block node (<c>chordnames { ... }</c> inside a section).</summary>
    ChordNamesBlock,
    /// <summary>A chord part block node (<c>chords name { ... }</c> inside a section; an independent chord part).</summary>
    ChordPartBlock,
    /// <summary>A chord entry node (<c>root[dur][:quality][/bass]</c> inside chordnames / chords).</summary>
    ChordEntry,
    /// <summary>A staff render node (<c>staff { guitar }</c> inside render).</summary>
    StaffRender,
    /// <summary>A grand-staff render node (<c>grandStaff { staff staff }</c> inside render).</summary>
    GrandStaffRender,
    /// <summary>A tablature render node (<c>tab guitar { guitar }</c> inside render).</summary>
    TabRender,
    /// <summary>An ossia render node (<c>ossia treble { alternative }</c> inside render).</summary>
    OssiaRender,
    /// <summary>A chord-row render node (<c>chords name</c> inside score; places a chord part as a row).</summary>
    ChordRowRender,
    /// <summary>A lyrics-row render node (<c>lyrics name</c> inside score; places a lyrics part as a row).</summary>
    LyricsRowRender,
    /// <summary>A MIDI part render node (<c>guitar channel:1</c> inside render).</summary>
    MidiPartRender,
    // === Nodes: Structure Block Items ===
    /// <summary>A marker node that resets the pitch resolver at section boundaries.</summary>
    SectionStartMarker,
    /// <summary>A section reference node (a section name in a structure).</summary>
    SectionReference,
    /// <summary>A silent section reference node (<c>~section name</c>, no label).</summary>
    SilentSectionReference,
    /// <summary>A custom-text node (<c>_"text"</c> in a structure).</summary>
    CustomText,
    /// <summary>A repeat-count node (<c>x3</c> after a repeat block).</summary>
    RepeatCount,
    /// <summary>A structure repeat block node (<c>|: ... :|</c> in a structure).</summary>
    StructureRepeatBlock,
    /// <summary>A structure alternative node (<c>1. A, 2. B</c> in a structure).</summary>
    StructureAlternative,
    /// <summary>A navigation mark node (<c>segno</c>, <c>fine</c>, <c>coda</c>, <c>dc</c>, <c>ds</c>; legacy).</summary>
    NavigationMark,
    /// <summary>A music mark node (<c>@segno</c>, <c>@fine</c>, <c>@ds.al.fine</c>; new).</summary>
    MusicMark,

    // === Nodes: Legacy Structure ===
    /// <summary>A staff declaration node (legacy).</summary>
    StaffDeclaration,
    /// <summary>A voice declaration node (legacy).</summary>
    VoiceDeclaration,

    // === Nodes: Music Content ===
    /// <summary>A music block node.</summary>
    MusicBlock,

    // === Nodes: Notes and Rests ===
    /// <summary>A note node.</summary>
    Note,
    /// <summary>A drum-kit note node (e.g. <c>bd4</c>, <c>sn8</c>, <c>hh</c>; resolved via DrumNameRegistry).</summary>
    DrumNote,
    /// <summary>A nested-voice recovery node (an erroneous <c>voice{}</c> inside a voice body whose content is inlined).</summary>
    NestedVoiceRecovery,
    /// <summary>A drum-map declaration node (<c>drummap { hh: position 6 notehead x … }</c>).</summary>
    DrummapDeclaration,
    /// <summary>The <c>drummap</c> keyword.</summary>
    DrummapKeyword,
    /// <summary>A rest node.</summary>
    Rest,
    /// <summary>A chord node.</summary>
    Chord,
    /// <summary>A pitch node.</summary>
    Pitch,
    /// <summary>A duration node.</summary>
    Duration,

    // === Nodes: Articulations ===
    /// <summary>An articulation node.</summary>
    Articulation,
    /// <summary>A dynamic node.</summary>
    Dynamic,

    // === Nodes: Other Music ===
    /// <summary>A bar-line node.</summary>
    Barline,
    /// <summary>A line-break node.</summary>
    Break,
    /// <summary>A tie node.</summary>
    Tie,
    /// <summary>A slur node.</summary>
    Slur,
    /// <summary>A beam marker node.</summary>
    BeamMarker,

    // === Nodes: Repeat and Parallel ===
    /// <summary>A repeat expression node.</summary>
    RepeatExpression,
    /// <summary>An alternative (volta) clause node.</summary>
    AlternativeClause,
    /// <summary>A parallel expression node.</summary>
    ParallelExpression,
    /// <summary>An inline volta node (<c>[1. ...]</c> inline volta ending inside a <c>|: :|</c> repeat).</summary>
    InlineVolta,

    // === Nodes: Properties ===
    /// <summary>A property assignment node.</summary>
    PropertyAssignment,
    /// <summary>A time-signature node.</summary>
    TimeSignature,
    /// <summary>A tempo declaration node.</summary>
    TempoDeclaration,
    /// <summary>A partial (anacrusis / pickup measure) declaration node.</summary>
    PartialDeclaration,
    /// <summary>A key-signature node.</summary>
    KeySignature,
    /// <summary>A clef declaration node.</summary>
    ClefDeclaration,
    /// <summary>An octave directive node.</summary>
    OctaveDirective,

    // === Nodes: Tuplet and Grace ===
    /// <summary>A tuplet expression node.</summary>
    TupletExpression,
    /// <summary>A grace expression node.</summary>
    GraceExpression,

    // === Nodes: Tablature ===
    /// <summary>A tablature staff declaration node.</summary>
    TabStaffDeclaration,
    /// <summary>A tuning declaration node.</summary>
    TuningDeclaration,
    /// <summary>A string-number annotation node.</summary>
    StringNumberAnnotation,

    // === Nodes: Override/Revert ===
    /// <summary>An override declaration node (<c>override Grob.property = value</c>).</summary>
    OverrideDeclaration,
    /// <summary>A revert declaration node (<c>revert Grob.property</c>).</summary>
    RevertDeclaration,
    /// <summary>A once modifier node (<c>once override/revert ...</c>).</summary>
    OnceModifier,
}
