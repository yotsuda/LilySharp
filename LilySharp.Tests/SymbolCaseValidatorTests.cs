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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Header symbols are case-sensitive: a wrong-case or unknown property name, or a
/// wrong-case/unknown clef / instrument-preset / tuning value, is an error rather
/// than a silent fallback to a default. Free-text (quoted) values are not symbols.
/// </summary>
[Trait("Category", "Unit")]
public class SymbolCaseValidatorTests
{
    // Wrap a part header body in a minimal complete document. `vln` is a plain
    // identifier part name (p / pp / mf … are reserved dynamics, not names).
    private static string Doc(string header) =>
        $"part vln {{ {header} }}\nsection A {{ vln {{ c4 d e f }} }}\nform main {{ A }}\nscore \"s\" {{ staff vln }}";

    private static bool HasSymbolError(string header) =>
        SemanticValidation.Run(SyntaxTree.Parse(Doc(header)))
            .Any(d => d.Code == DiagnosticCodes.UnknownSymbolCase);

    [Fact]
    public void CanonicalLowercaseSymbols_AreClean()
    {
        Assert.False(HasSymbolError("clef treble  instrument violin"));
    }

    [Fact]
    public void WrongCaseClefValue_IsError()
    {
        Assert.True(HasSymbolError("clef Treble"));
    }

    [Fact]
    public void WrongCaseInstrumentPreset_IsError()
    {
        Assert.True(HasSymbolError("instrument Violin"));
    }

    [Fact]
    public void CapitalizedPropertyName_IsError()
    {
        Assert.True(HasSymbolError("Clef treble"));
    }

    [Fact]
    public void WrongCaseTuningValue_IsError()
    {
        Assert.True(HasSymbolError("tuning Guitar"));
    }

    [Fact]
    public void QuotedInstrumentLabel_IsNotASymbol_NoError()
    {
        // A quoted "…" name is free text, not a preset symbol — no case rule applies.
        Assert.False(HasSymbolError("instrument \"1st Violin\""));
    }

    [Fact]
    public void PresetPlusQuotedLabel_ChecksOnlyThePreset()
    {
        Assert.False(HasSymbolError("instrument cello \"Cello I\""));   // known preset + label
        Assert.True(HasSymbolError("instrument Cello \"Cello I\""));    // wrong-case preset
    }

    // ────────────────────────────────────────────────────────────────────────
    // The five values nobody checked until 2026-08-19.
    //
    // `clef`, `tuning`, `pedal` and `instrument` refused an unknown word from the
    // start; `removeEmpty`, `lines`, `octave`, `transpose` and `transposition` did
    // not, so a wrong word was read as the DEFAULT and the book compiled saying
    // something the writer had not asked for (`lines 9` drew five, `removeEmpty
    // banana` was off, `transposition banana` was zero semitones). Two weights
    // inside one header. Refusing them refuses books that compiled before, so it
    // was a decision and not a tidy-up — taken before 0.3.0 was tagged, measured
    // the same day as costing 0 of the 567 tracked books.
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The claim that generalizes, and the one that would have caught all five at once:
    /// EVERY name a part header takes refuses a value it cannot read. Written over the
    /// published vocabulary rather than over a list of five, so a property added later
    /// without a value check arrives here red instead of silently joining the hole.
    /// </summary>
    [Fact]
    public void EveryPartProperty_RefusesAValueItCannotRead()
    {
        // `key` is in the vocabulary but is parsed as a KeySignature rather than a
        // PropertyAssignment, so it never reaches this validator at all — the ONLY
        // exemption, and it is an exemption of reachability, not of judgement.
        var exempt = new HashSet<string> { "key" };

        var accepted = SymbolCaseValidator.PropertyNameVocabulary
            .Where(name => !exempt.Contains(name))
            .Where(name => !HasSymbolError($"{name} banana"))
            .ToList();

        Assert.True(accepted.Count == 0,
            "these part properties still swallow a word they cannot read, and read it as "
            + "their default instead of refusing it: " + string.Join(", ", accepted));
    }

    [Theory]
    [InlineData("removeEmpty banana")]
    [InlineData("lines banana")]
    [InlineData("octave banana")]
    [InlineData("transpose banana")]
    [InlineData("transposition banana")]
    public void ValueTheReaderCannotRead_IsError(string header)
        => Assert.True(HasSymbolError(header));

    [Theory]
    [InlineData("removeEmpty true")]
    [InlineData("removeEmpty all")]
    [InlineData("removeEmpty false")]   // in the vocabulary; means what leaving it out means
    [InlineData("lines 1")]
    [InlineData("lines 5")]
    [InlineData("octave 3")]
    [InlineData("transpose d")]
    [InlineData("transpose cis")]
    [InlineData("transpose bes,")]      // the octave mark is a token of its own
    [InlineData("transposition 8vb")]
    [InlineData("transposition 15ma")]
    public void EverySpellingTheLanguageMeans_StillCompiles(string header)
        => Assert.False(HasSymbolError(header));

    /// <summary>
    /// `removeEmpty` was the ONE part-header property that ignored case, and it ignored
    /// it because nobody checked it — <c>RenderSpecParser</c> lower-cases before comparing.
    /// It now obeys the Ordinal rule the other symbols in this header always obeyed.
    /// </summary>
    [Fact]
    public void RemoveEmptyValue_IsCaseSensitive_LikeEveryOtherSymbolHere()
    {
        Assert.True(HasSymbolError("removeEmpty TRUE"));
        Assert.False(HasSymbolError("removeEmpty true"));
    }

    /// <summary>
    /// The staff-line counts this validator ACCEPTS are exactly the ones the renderer
    /// USES. Written as a comparison against the renderer rather than against the bound,
    /// because the bound is a shared constant and asserting it against itself would prove
    /// nothing: what can rot is the pair, not the number.
    /// </summary>
    [Fact]
    public void LinesTheValidatorAccepts_AreExactlyTheOnesTheRendererUses()
    {
        for (int n = 0; n <= 7; n++)
        {
            bool accepted = !HasSymbolError($"lines {n}");
            int used = LinesTheRendererUses($"lines {n}");
            Assert.Equal(accepted, used == n);
        }

        // …and the direction a numeric sweep cannot see: a value that is not a number
        // at all also falls back to the default, so it must be refused too.
        Assert.True(HasSymbolError("lines 2.5"));
    }

    private static int LinesTheRendererUses(string header)
    {
        var tree = SyntaxTree.Parse(Doc(header));
        var spec = RenderSpecParser.FindFirst(tree)!;
        return spec.Items.OfType<SingleStaffSpec>().Single().Staff.Lines;
    }
    /// <summary>
    /// `octave` names two different things and only one of them lives in a part header.
    /// <c>octave absolute</c> / <c>octave relative</c> is the OctaveDecl DIRECTIVE, written at
    /// the top level or in a section; a part header's <c>octave</c> takes a number. GRAMMAR.md
    /// listed the two mode words as part-property alternatives, so a reader was told
    /// `part m { octave absolute }` sets that part's octave mode. It never did: measured
    /// 2026-08-19 on the tree before this validator learned to refuse it, the book's MIDI was
    /// byte-identical to one with no octave property at all — against a control (octave 2 vs
    /// octave 5) that differs, so the measurement is of the book and not of a blind instrument.
    /// </summary>
    [Fact]
    public void OctaveModeWords_BelongToTheDirective_NotToAPartHeader()
    {
        Assert.True(HasSymbolError("octave absolute"));
        Assert.True(HasSymbolError("octave relative"));
        Assert.False(HasSymbolError("octave 3"));

        // …and the position where those words ARE the language: a section's own directive.
        const string inSection = """
            part vln { clef treble }
            section A { octave absolute  vln { c4 d e f } }
            form main { A }
            score main { staff vln }
            """;
        Assert.Empty(SemanticValidation.Run(SyntaxTree.Parse(inSection))
            .Where(d => d.Code == DiagnosticCodes.UnknownSymbolCase));
    }


    /// <summary>
    /// A wrong-case ottava marker is refused HERE, and the distinction matters: the reader
    /// (<c>InstrumentDefaults.ParseTranspositionSemitones</c>) lower-cases its argument, so
    /// a validator written to "ask the reader" would ACCEPT <c>8VB</c>. A first draft of the
    /// branch did exactly that and let the spelling through; this holds the door shut.
    /// </summary>
    [Fact]
    public void WrongCaseTranspositionMarker_IsRefused_ThoughTheReaderWouldLowerIt()
    {
        Assert.True(HasSymbolError("transposition 8VB"));
        Assert.False(HasSymbolError("transposition 8vb"));
    }

    /// <summary>
    /// And it is refused as a VALUE. Before the lexer took the suffix whole whatever its
    /// case, <c>8VB</c> split into <c>8</c> and <c>VB</c> and the book got three diagnostics,
    /// two of them about a part property named <c>VB</c> that the writer never wrote — the
    /// value they got wrong was named in none of them.
    /// </summary>
    [Fact]
    public void WrongCaseOttavaMarker_IsOneDiagnosticThatNamesTheValue()
    {
        // A book whose ONLY fault is the marker — `Doc` above names a form the file does
        // not declare, which is a second diagnostic and would hide the count this asserts.
        const string book = """
            part vln { transposition 8VB }
            section A { vln { c4 d e f } }
            form main { A }
            score main { staff vln }
            """;
        var tree = SyntaxTree.Parse(book);
        var all = tree.Diagnostics
            .Concat(SemanticValidation.Run(tree))
            .Select(d => d.Message)
            .ToList();

        Assert.DoesNotContain(all, m => m.Contains("'VB'"));
        Assert.Single(all);
        Assert.Contains("8VB", all[0]);
    }
}
