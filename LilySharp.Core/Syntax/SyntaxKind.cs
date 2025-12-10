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
    TabKeyword,         // tab
    VoiceKeyword,       // voice
    RelativeKeyword,    // relative
    AbsoluteKeyword,    // absolute
    FixedKeyword,       // fixed
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
    Tilde,              // ~
    Colon,              // :
    Equals,             // =
    Slash,              // /
    At,                 // @
    Backslash,          // \
    Comma,              // ,
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
    VariableDeclaration,
    VariableReference,
    
    // === Nodes: New Structure ===
    SectionDeclaration,         // section Name { ... }
    StructureDeclaration,       // structure { ... }
    RenderDeclaration,          // render Name "file.svg" { ... }
    PartBlock,                  // guitar { ... } inside section
    StaffRender,                // staff { guitar } inside render
    TabRender,                  // tab guitar { guitar } inside render
    MidiPartRender,             // guitar channel:1 inside render
    
    // === Nodes: Structure Block Items ===
    SectionReference,           // section name in structure
    StructureRepeatBlock,       // |: ... :| in structure
    StructureAlternative,       // 1. A, 2. B in structure
    NavigationMark,             // segno, fine, coda, dc, ds
    
    // === Nodes: Legacy Structure ===
    ScoreDeclaration,
    PartDeclaration,
    StaffDeclaration,
    VoiceDeclaration,
    
    // === Nodes: Music Content ===
    MusicBlock,
    RelativeExpression,
    AbsoluteExpression,
    
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
    Tie,
    Slur,
    
    // === Nodes: Repeat and Parallel ===
    RepeatExpression,
    AlternativeClause,
    ParallelExpression,
    
    // === Nodes: Properties ===
    PropertyAssignment,
    TimeSignature,
    TempoDeclaration,
    KeySignature,
    ClefDeclaration,
    
    // === Nodes: Tuplet and Grace ===
    TupletExpression,
    GraceExpression,
    LyricsBlock,
    
    // === Nodes: Tablature ===
    TabStaffDeclaration,
    TuningDeclaration,
    StringNumberAnnotation,
}