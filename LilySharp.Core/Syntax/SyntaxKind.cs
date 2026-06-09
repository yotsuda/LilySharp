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
    None = 0,
    EndOfFile,
    BadToken,

    // === Trivia ===
    WhitespaceTrivia,
    LineCommentTrivia,
    BlockCommentTrivia,
    EndOfLineTrivia,

    // === Literals ===
    IntegerLiteral,
    StringLiteral,

    // === Identifiers ===
    Identifier,

    // === Pitch Names ===
    PitchC,
    PitchD,
    PitchE,
    PitchF,
    PitchG,
    PitchA,
    PitchB,

    // === Accidentals (suffixes, not separate tokens) ===
    // Handled as part of pitch token

    // === Duration ===
    DurationNumber,     // 1, 2, 4, 8, 16, 32, 64, 128
    Dot,                // .

    // === Structure Keywords (no backslash) ===
    SectionKeyword,     // section
    StructureKeyword,   // structure
    RenderKeyword,      // render
    ScoreKeyword,       // score (legacy)
    PartKeyword,        // part (legacy)
    StaffKeyword,       // staff
    GrandStaffKeyword,  // grandStaff
    TabKeyword,         // tab
    OssiaKeyword,       // ossia
    VoiceKeyword,       // voice
    PhraseKeyword,      // phrase
    RepeatKeyword,      // repeat (legacy)
    VoltaKeyword,       // volta (legacy)
    AlternativeKeyword, // alternative (legacy)
    LetKeyword,         // let (legacy)
    UseKeyword,         // use (legacy)
    TitleKeyword,       // title
    ComposerKeyword,    // composer
    TempoKeyword,       // tempo
    TimeKeyword,        // time
    KeyKeyword,         // key
    ClefKeyword,        // clef
    MajorKeyword,       // major
    MinorKeyword,       // minor
    TupletKeyword,      // tuplet
    TrebleKeyword,      // treble
    BassKeyword,        // bass
    AltoKeyword,        // alto
    TenorKeyword,       // tenor
    Treble8Keyword,     // treble_8
    GraceKeyword,       // grace
    AcciaccaturaKeyword,
    AppogiaturaKeyword,
    LyricsKeyword,      // lyrics
    TabStaffKeyword,    // tabStaff (legacy)
    TuningKeyword,      // tuning
    TransposeKeyword,   // transpose
    OctaveKeyword,      // octave
    InstrumentKeyword,  // instrument
    ChannelKeyword,     // channel
    BreakKeyword,       // break (line break)


    // === Navigation Keywords (structure block) ===
    SegnoKeyword,       // segno
    FineKeyword,        // fine
    CodaKeyword,        // coda
    DcKeyword,          // dc
    DsKeyword,          // ds
    AlKeyword,          // al
    ToKeyword,          // to

    // === Mode Keywords ===
    DorianKeyword,      // dorian
    PhrygianKeyword,    // phrygian
    LydianKeyword,      // lydian
    MixolydianKeyword,  // mixolydian
    AeolianKeyword,     // aeolian
    LocrianKeyword,     // locrian

    // === Rest ===
    RestR,              // r
    RestS,              // s (spacer)
    RestR_Full,         // R (full measure)

    // === String Number (for tablature) ===
    StringNumber,       // \1, \2, \3, \4, \5, \6

    // === Punctuation ===
    OpenBrace,          // {
    CloseBrace,         // }
    OpenParen,          // (
    CloseParen,         // )
    OpenAngle,          // <
    CloseAngle,         // >
    OpenBracket,        // [
    CloseBracket,       // ]
    Bar,                // |
    DoubleBar,          // ||
    FinalBar,           // |.
    RepeatStartBar,     // |:
    RepeatEndBar,       // :|
    // LineBreakBar removed - use BreakKeyword instead
    Tilde,              // ~
    Asterisk,           // * (multi-measure rest count: R1*N)
    Colon,              // :
    TremoloSuffix,      // :8, :16, :32 (tremolo beams)
    Equals,             // =
    Slash,              // /
    At,                 // @
    Underscore,         // _
    Backslash,          // \
    Comma,              // ,
    Minus,              // -
    Apostrophe,         // '
    Dollar,             // $
    DoubleOpenAngle,    // <<
    DoubleCloseAngle,   // >>

    // === Articulation Names ===
    StaccatoKeyword,
    AccentKeyword,
    TenutoKeyword,
    MarcatoKeyword,
    FermataKeyword,
    PortatoKeyword,

    // === Ornament Names ===
    TrillKeyword,       // @trill
    MordentKeyword,     // @mordent
    PrallKeyword,       // @prall (inverted mordent)
    TurnKeyword,        // @turn
    InvertedTurnKeyword, // @invertedturn
    PrallTrillKeyword,  // @pralltriller (short trill)
    TremoloKeyword,     // @tremolo (stem tremolo)

    // === Override/Revert Keywords (no backslash, follows LilySharp convention) ===
    OverrideKeyword,    // override
    RevertKeyword,      // revert
    OnceKeyword,        // once

    // === Dynamics (with backslash) ===
    DynamicPPP,         // \ppp
    DynamicPP,          // \pp
    DynamicP,           // \p
    DynamicMP,          // \mp
    DynamicMF,          // \mf
    DynamicF,           // \f
    DynamicFF,          // \ff
    DynamicFFF,         // \fff
    CrescKeyword,       // \cresc
    DecrescKeyword,     // \decresc
    DimKeyword,         // \dim

    // === Nodes: Top Level ===
    CompilationUnit,
    MetadataDeclaration,
    VariableDeclaration,    // (legacy) name = { ... }
    PhraseDeclaration,      // phrase name { ... }
    PartDeclaration,        // part name { props }
    VariableReference,

    // === Nodes: New Structure ===
    SectionDeclaration,         // section Name { ... }
    StructureDeclaration,       // structure { ... }
    RenderDeclaration,          // render Name "file.svg" { ... }
    PartBlock,                  // guitar { ... } inside section
    LyricsBlock,                // lyrics { ... } inside section
    LyricMeasure,               // syllable syllable | inside lyrics
    LyricSyllable,              // single lyric syllable
    StaffRender,                // staff { guitar } inside render
    GrandStaffRender,           // grandStaff { staff staff } inside render
    TabRender,                  // tab guitar { guitar } inside render
    OssiaRender,                // ossia treble { alternative } inside render
    MidiPartRender,             // guitar channel:1 inside render
    // === Nodes: Structure Block Items ===
    SectionStartMarker,         // marker to reset pitch resolver at section boundaries
    SectionReference,           // section name in structure
    SilentSectionReference,     // ~section name (no label)
    CustomText,                 // _"text" in structure
    RepeatCount,                // x3 after repeat block
    StructureRepeatBlock,       // |: ... :| in structure
    StructureAlternative,       // 1. A, 2. B in structure
    NavigationMark,             // segno, fine, coda, dc, ds (legacy)
    MusicMark,                  // @segno, @fine, @ds.al.fine (new)

    // === Nodes: Legacy Structure ===
    ScoreDeclaration,
    StaffDeclaration,
    VoiceDeclaration,

    // === Nodes: Music Content ===
    MusicBlock,

    // === Nodes: Notes and Rests ===
    Note,
    Rest,
    Chord,
    Pitch,
    Duration,

    // === Nodes: Articulations ===
    Articulation,
    Dynamic,

    // === Nodes: Other Music ===
    Barline,
    Break,              // line break
    Tie,
    Slur,
    BeamMarker,

    // === Nodes: Repeat and Parallel ===
    RepeatExpression,
    AlternativeClause,
    ParallelExpression,
    InlineVolta,                // [1. ...] inline volta ending in a |: :| repeat

    // === Nodes: Properties ===
    PropertyAssignment,
    TimeSignature,
    TempoDeclaration,
    KeySignature,
    ClefDeclaration,

    // === Nodes: Tuplet and Grace ===
    TupletExpression,
    GraceExpression,

    // === Nodes: Tablature ===
    TabStaffDeclaration,
    TuningDeclaration,
    StringNumberAnnotation,

    // === Nodes: Override/Revert ===
    OverrideDeclaration,    // override Grob.property = value
    RevertDeclaration,      // revert Grob.property
    OnceModifier,           // once override/revert ...
}