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

using System.Linq;
using LilySharp.Core.Music;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// LYS1053: a <c>chords</c> row entry that is no chord Lily# reads — <c>C7b9</c>, <c>C-5</c>,
/// a misspelled quality. The row prints it as written and plays its root alone
/// (<c>ChordNameCollector.StructureOf</c> keeps the raw suffix on a root that reads), so the
/// slip is heard as a bare root; this says so, with the fix the spelling grammar names
/// (<see cref="ChordStructure.TryParseChordEntry(string, out ChordStructure, out string?)"/>)
/// — the same words the symbol gets in an <c>@chord</c>, where it is LYS1008.
/// </summary>
/// <remarks>
/// Owner's decision 2026-10-26 ("警告を出す"): the printing and the playback stay as they were;
/// only the silence ends. A Roman degree (<c>V7</c>, <c>bVII</c>) reads against the key at its
/// bar, which this pass does not track — it is let through when it reads in any key, since the
/// numeral and its quality, not the key, decide whether it is one. <c>.</c> and <c>r</c> are
/// other nodes (<see cref="ChordExtendSyntax"/>, a rest) and never reach here.
/// </remarks>
internal sealed class ChordRowSymbolValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        foreach (var entry in tree.GetRoot().DescendantNodes<ChordEntrySyntax>())
        {
            string symbol = entry.SymbolText;
            if (symbol.Length == 0
                || ChordStructure.TryParseChordEntry(symbol, out _, out string? refusal)
                || ChordStructure.TryParseRomanEntry(symbol, 0, 0, out _))
                continue;

            // (an entry reaches here with a root: one that is no A–G is the parser's error)
            int slash = symbol.IndexOf('/');
            ChordStructure.TryParseSymbolPitch(slash >= 0 ? symbol[..slash] : symbol, out _, out _, out string quality);
            _diagnostics.Warning(entry.Span, DiagnosticCodes.ChordSymbolNotRead,
                $"'{symbol}' is not a chord Lily# reads, so the row prints it as written and plays only its root. "
                + Hint(refusal, quality));
        }
    }

    /// <summary>What to write instead: what the spelling grammar names, else the alteration rule
    /// for a quality with a <c>b</c> or <c>#</c> in it, else where the names are — and, for such a
    /// quality, its <c>-</c> / <c>+</c> spelling when that one reads, as <c>@chord</c> offers it.</summary>
    private static string Hint(string? refusal, string quality)
    {
        bool altering = quality.IndexOfAny(['b', '#']) >= 0;
        string hint = refusal ?? (altering
            ? "An altered tension is spelled with '+' or '-' (C7-9, C7+11) or in parentheses "
                + "(C7(b9), C7(#11)) - '#' and 'b' after a letter belong to the root and the bass alone, "
                + "which is what keeps 'Bb9' unambiguous."
            : $"'{quality}' is no chord quality Lily# knows - Ctrl+Space in the row lists the key's chords.");
        string altered = quality.Replace('b', '-').Replace('#', '+');
        return altering && ChordQualityRegistry.TryResolve(altered, out _) ? hint + $" Did you mean '{altered}'?" : hint;
    }
}
