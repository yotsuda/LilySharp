namespace Lilysharp.Core.Syntax;

/// <summary>
/// Defines all syntax kinds (tokens and nodes) in Lilysharp.
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
    
    // === Keywords ===
    ScoreKeyword,
    PartKeyword,
    StaffKeyword,
    VoiceKeyword,
    RelativeKeyword,
    AbsoluteKeyword,
    FixedKeyword,
    RepeatKeyword,
    VoltaKeyword,
    AlternativeKeyword,
    LetKeyword,
    UseKeyword,
    TitleKeyword,
    ComposerKeyword,
    TempoKeyword,
    TimeKeyword,
    KeyKeyword,
    ClefKeyword,
    MajorKeyword,
    MinorKeyword,
    
    // === Rest ===
    RestR,              // r
    RestS,              // s (spacer)
    RestR_Full,         // R (full measure)
    
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
    
    // === Dynamics ===
    DynamicPPP,         // ppp
    DynamicPP,          // pp
    DynamicP,           // p
    DynamicMP,          // mp
    DynamicMF,          // mf
    DynamicF,           // f
    DynamicFF,          // ff
    DynamicFFF,         // fff
    CrescKeyword,       // cresc
    DecrescKeyword,     // decresc
    DimKeyword,         // dim
    
    // === Nodes ===
    CompilationUnit,
    
    // Music structure
    ScoreDeclaration,
    PartDeclaration,
    StaffDeclaration,
    VoiceDeclaration,
    
    // Music content
    MusicBlock,
    RelativeExpression,
    AbsoluteExpression,
    
    // Notes and rests
    Note,
    Rest,
    Chord,
    Pitch,
    Duration,
    
    // Articulations
    Articulation,
    Dynamic,
    
    // Other
    Barline,
    Tie,
    Slur,
    VariableDeclaration,
    VariableReference,
    
    // Properties
    PropertyAssignment,
    MetadataDeclaration,
}