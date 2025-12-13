namespace LilySharp.Core.Semantics.Symbols;

/// <summary>
/// Kinds of symbols in LilySharp.
/// </summary>
public enum SymbolKind
{
    /// <summary>A section definition (section A { ... }).</summary>
    Section,
    
    /// <summary>A phrase definition (phrase name = { ... }).</summary>
    Phrase,
    
    /// <summary>A part definition (part name { ... }).</summary>
    Part,
    
    /// <summary>A variable definition (name = expression).</summary>
    Variable,
    
    /// <summary>A structure definition (structure { ... }).</summary>
    Structure
}
