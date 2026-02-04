using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace LilySharp.Core.Semantics.Symbols;

/// <summary>
/// Manages symbols collected from the syntax tree.
/// </summary>
/// <remarks>
/// Design inspired by Roslyn's symbol tables.
/// The SymbolTable is built by SymbolCollector and provides
/// lookup services for the Binder.
/// </remarks>
public sealed class SymbolTable
{
    private readonly Dictionary<string, SectionSymbol> _sections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PhraseSymbol> _phrases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PartSymbol> _parts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, VariableSymbol> _variables = new(StringComparer.Ordinal);
    private StructureSymbol? _structure;

    /// <summary>
    /// Gets all section symbols.
    /// </summary>
    public IReadOnlyDictionary<string, SectionSymbol> Sections => _sections;

    /// <summary>
    /// Gets all phrase symbols.
    /// </summary>
    public IReadOnlyDictionary<string, PhraseSymbol> Phrases => _phrases;

    /// <summary>
    /// Gets all part symbols.
    /// </summary>
    public IReadOnlyDictionary<string, PartSymbol> Parts => _parts;

    /// <summary>
    /// Gets all variable symbols.
    /// </summary>
    public IReadOnlyDictionary<string, VariableSymbol> Variables => _variables;

    /// <summary>
    /// Gets the structure symbol, if defined.
    /// </summary>
    public StructureSymbol? Structure => _structure;

    /// <summary>
    /// Adds a section symbol.
    /// </summary>
    /// <returns>True if added, false if a section with the same name already exists.</returns>
    public bool AddSection(SectionSymbol symbol)
    {
        if (_sections.ContainsKey(symbol.Name))
            return false;
        _sections[symbol.Name] = symbol;
        return true;
    }

    /// <summary>
    /// Adds a phrase symbol.
    /// </summary>
    /// <returns>True if added, false if a phrase with the same name already exists.</returns>
    public bool AddPhrase(PhraseSymbol symbol)
    {
        if (_phrases.ContainsKey(symbol.Name))
            return false;
        _phrases[symbol.Name] = symbol;
        return true;
    }

    /// <summary>
    /// Adds a part symbol.
    /// </summary>
    /// <returns>True if added, false if a part with the same name already exists.</returns>
    public bool AddPart(PartSymbol symbol)
    {
        if (_parts.ContainsKey(symbol.Name))
            return false;
        _parts[symbol.Name] = symbol;
        return true;
    }

    /// <summary>
    /// Adds a variable symbol.
    /// </summary>
    /// <returns>True if added, false if a variable with the same name already exists.</returns>
    public bool AddVariable(VariableSymbol symbol)
    {
        if (_variables.ContainsKey(symbol.Name))
            return false;
        _variables[symbol.Name] = symbol;
        return true;
    }

    /// <summary>
    /// Sets the structure symbol.
    /// </summary>
    /// <returns>True if set, false if a structure already exists.</returns>
    public bool SetStructure(StructureSymbol symbol)
    {
        if (_structure != null)
            return false;
        _structure = symbol;
        return true;
    }

    /// <summary>
    /// Tries to get a section by name.
    /// </summary>
    public bool TryGetSection(string name, [NotNullWhen(true)] out SectionSymbol? symbol)
        => _sections.TryGetValue(name, out symbol);

    /// <summary>
    /// Tries to get a phrase by name.
    /// </summary>
    public bool TryGetPhrase(string name, [NotNullWhen(true)] out PhraseSymbol? symbol)
        => _phrases.TryGetValue(name, out symbol);

    /// <summary>
    /// Tries to get a part by name.
    /// </summary>
    public bool TryGetPart(string name, [NotNullWhen(true)] out PartSymbol? symbol)
        => _parts.TryGetValue(name, out symbol);

    /// <summary>
    /// Tries to get a variable by name.
    /// </summary>
    public bool TryGetVariable(string name, [NotNullWhen(true)] out VariableSymbol? symbol)
        => _variables.TryGetValue(name, out symbol);

    /// <summary>
    /// Looks up a symbol by name, searching all symbol kinds.
    /// </summary>
    /// <returns>The symbol if found, null otherwise.</returns>
    public Symbol? Lookup(string name)
    {
        if (_sections.TryGetValue(name, out var section))
            return section;
        if (_phrases.TryGetValue(name, out var phrase))
            return phrase;
        if (_parts.TryGetValue(name, out var part))
            return part;
        if (_variables.TryGetValue(name, out var variable))
            return variable;
        return null;
    }

    /// <summary>
    /// Gets all symbols as a flat collection.
    /// </summary>
    public IEnumerable<Symbol> GetAllSymbols()
    {
        foreach (var s in _sections.Values) yield return s;
        foreach (var s in _phrases.Values) yield return s;
        foreach (var s in _parts.Values) yield return s;
        foreach (var s in _variables.Values) yield return s;
        if (_structure != null) yield return _structure;
    }

    /// <summary>
    /// Creates an immutable snapshot of this symbol table.
    /// </summary>
    public ImmutableSymbolTable ToImmutable() => new(
        _sections.ToImmutableDictionary(),
        _phrases.ToImmutableDictionary(),
        _parts.ToImmutableDictionary(),
        _variables.ToImmutableDictionary(),
        _structure);
}

/// <summary>
/// An immutable snapshot of a symbol table.
/// </summary>
public sealed record ImmutableSymbolTable(
    ImmutableDictionary<string, SectionSymbol> Sections,
    ImmutableDictionary<string, PhraseSymbol> Phrases,
    ImmutableDictionary<string, PartSymbol> Parts,
    ImmutableDictionary<string, VariableSymbol> Variables,
    StructureSymbol? Structure)
{
    /// <summary>
    /// An empty symbol table.
    /// </summary>
    public static ImmutableSymbolTable Empty { get; } = new(
        ImmutableDictionary<string, SectionSymbol>.Empty,
        ImmutableDictionary<string, PhraseSymbol>.Empty,
        ImmutableDictionary<string, PartSymbol>.Empty,
        ImmutableDictionary<string, VariableSymbol>.Empty,
        null);

    /// <summary>
    /// Looks up a symbol by name, searching all symbol kinds.
    /// </summary>
    public Symbol? Lookup(string name)
    {
        if (Sections.TryGetValue(name, out var section))
            return section;
        if (Phrases.TryGetValue(name, out var phrase))
            return phrase;
        if (Parts.TryGetValue(name, out var part))
            return part;
        if (Variables.TryGetValue(name, out var variable))
            return variable;
        return null;
    }
}
