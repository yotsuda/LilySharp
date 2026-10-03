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

using System;
using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Editing;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A part (or section) may be named any bare word outside
/// <see cref="SyntaxFacts.PartNameReservedVocabulary"/>; a phrase may be named for a
/// dynamic word; and <c>tab X Y</c> reads a declared part X first (owner's decisions
/// 2026-10-03, HANDOFF §1.0 ⑽ and ⑼ ⒜).
/// </summary>
/// <remarks>
/// Until then <c>part p</c> and <c>phrase p</c> were "Expected a name, found 'p', a reserved
/// word" (session 762 hit it writing a big-band book), and <c>tab bass click</c> beside a
/// part named <c>bass</c> drew the CLICK TRACK's notes as the bass tab — nothing in
/// <c>lysc check</c> or the LilyPond twin said so; it was seen by eye.
/// </remarks>
[Trait("Category", "Unit")]
public class PartNameVocabularyTests
{
    private static IReadOnlyList<Diagnostic> AllDiagnostics(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return [.. tree.Diagnostics, .. SemanticValidation.Run(tree)];
    }

    private static IEnumerable<Diagnostic> Errors(string source) =>
        AllDiagnostics(source).Where(d => d.Severity == DiagnosticSeverity.Error);

    private static string Book(string partName) =>
        $"part {partName} {{ clef treble }}\n"
        + $"section A {{ {partName} {{ c4 d e f | }} }}\n"
        + "form main { A }\n"
        + $"score main {{ staff {partName} }}";

    // ── part and section names ──

    [Theory]
    [InlineData("p")]           // a dynamic
    [InlineData("mp")]
    [InlineData("f")]           // the pitch F — a name never stands in a music stream
    [InlineData("c")]
    [InlineData("r")]           // a rest letter
    [InlineData("q")]           // the chord repeat (a music-stream word only)
    [InlineData("bd")]          // a drum name
    [InlineData("chord")]
    [InlineData("bass")]        // the four clef words were legal before
    [InlineData("percussion")]  // the other seven were not
    [InlineData("treble_8")]
    [InlineData("tuplet")]      // a keyword that takes arguments before its brace
    [InlineData("repeat")]
    [InlineData("instrument")]  // a part property
    [InlineData("major")]
    public void APart_MayBeNamedForAlmostAnyWord(string name)
    {
        var errors = Errors(Book(name)).ToList();
        Assert.True(errors.Count == 0,
            $"part {name}: " + string.Join(" | ", errors.Select(e => $"{e.Code} {e.Message}")));
        // …and it is the part the score renders, not a clef override or a stray.
        var render = SyntaxTree.Parse(Book(name)).GetRoot()
            .DescendantNodes<RenderDeclarationSyntax>().Single();
        var spec = RenderSpecParser.Parse(render)!;
        Assert.Equal(name, spec.Items.OfType<SingleStaffSpec>().Single().Staff.VoiceName);
    }

    [Fact]
    public void TheReservedListCanFail()
    {
        // The deny-list is words the lexer knows; a free word or a misspelling in it would
        // deny nothing and the test above would still be green.
        foreach (string word in SyntaxFacts.PartNameReservedVocabulary)
            Assert.NotEqual(SyntaxKind.Identifier, new LilySharp.Core.Parser.Lexer(word).ScanAllTokens().First().Kind);
        Assert.False(SyntaxFacts.IsPartNameToken(SyntaxKind.VoiceKeyword, "voice"));
        Assert.True(SyntaxFacts.IsPartNameToken(SyntaxKind.DynamicP, "p"));
        Assert.False(SyntaxFacts.IsPartNameToken(SyntaxKind.Treble8UpKeyword, "treble^8")); // not a bare word
    }

    [Fact]
    public void EveryReservedWord_IsRefusedAsAPartAndAsASection_NamingTheList()
    {
        foreach (string word in SyntaxFacts.PartNameReservedVocabulary)
        {
            foreach (string decl in new[] { $"part {word} {{ clef treble }}", $"section {word} {{ c4 }}" })
            {
                var error = SyntaxTree.Parse(decl).Diagnostics
                    .FirstOrDefault(d => d.Code == DiagnosticCodes.ExpectedToken && d.Message.Contains("reserved word"));
                Assert.True(error != null, $"{decl}: not refused");
                Assert.Contains($"'{word}'", error!.Message);
                Assert.Contains("voice, lyrics", error.Message);   // the list is printed
            }
        }
    }

    [Theory]
    [InlineData("p")]
    [InlineData("bass")]
    [InlineData("q")]
    public void ASection_MayBeNamedTheSameWay_AndAFormReferencesIt(string name)
    {
        string src = $"part m {{ clef treble }}\n"
            + $"section {name} {{ m {{ c4 d e f | }} }}\n"
            + $"section B {{ m {{ g4 a b c' | }} }}\n"
            + $"form main {{ {name} |: B [1. {name}] :| [2. ~{name}] {name}' }}\n"
            + "score main { staff m }";
        var errors = Errors(src).ToList();
        Assert.True(errors.Count == 0, string.Join(" | ", errors.Select(e => $"{e.Code} {e.Message}")));
    }

    [Fact]
    public void AKeywordNamedCell_NeedsItsBraceDirectly_BareMusicStillReads()
    {
        // `p { … }` is part p's cell; a bare `p` in a single-part section is the stray item
        // it always was (LYS0030 — no cell, no reference), not a cell with a missing brace.
        var cell = SyntaxTree.Parse("part p { clef treble }\nsection A { p { c4 } }");
        Assert.DoesNotContain(cell.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Single(cell.GetRoot().DescendantNodes<PartBlockSyntax>());

        var bare = SyntaxTree.Parse("section A { c4 p d4 }");
        Assert.Empty(bare.GetRoot().DescendantNodes<PartBlockSyntax>());
    }

    [Fact]
    public void ARejectedBodyInsideACondensedStaff_YieldsNoPartNames()
    {
        // The kept body `{ staff c }` holds a pitch letter, which is a legal part name now;
        // it must not come back as a member and be reported undefined on top of LYS6004.
        string src = "section A { m { c4 } }\nform main { A }\n"
            + "score main { condensedStaff { m grandStaff { staff c } } }";
        var render = SyntaxTree.Parse(src).GetRoot().DescendantNodes<CondensedStaffRenderSyntax>().Single();
        Assert.Equal(["m"], render.PartNames);
        Assert.DoesNotContain(AllDiagnostics(src), d => d.Code == DiagnosticCodes.UndefinedPart);
    }

    // ── phrase names ──

    [Theory]
    [InlineData("p")]
    [InlineData("ppp")]
    [InlineData("mf")]
    [InlineData("fff")]
    public void APhrase_MayBeNamedForADynamicWord_AndABareWordPlaysIt(string name)
    {
        string src = $"phrase {name} {{ c4 d e f | }}\n"
            + "part m { clef treble }\n"
            + $"section A {{ m {{ {name} {name}' | }} }}\n"
            + "form main { A }\nscore main { staff m }";
        var errors = Errors(src).ToList();
        Assert.True(errors.Count == 0, string.Join(" | ", errors.Select(e => $"{e.Code} {e.Message}")));
        var refs = SyntaxTree.Parse(src).GetRoot().DescendantNodes<VariableReferenceSyntax>().ToList();
        Assert.Equal(2, refs.Count);
        Assert.All(refs, r => Assert.Equal(name, r.Name.Text));
    }

    [Theory]
    [InlineData("f", "note")]              // f lexes as the pitch F
    [InlineData("c", "note")]
    [InlineData("treble_8", "reserved")]   // a clef the music stream does not read bare
    [InlineData("major", "reserved")]
    [InlineData("tuplet", "reserved")]
    [InlineData("q", "repeats")]           // the three families refused before
    [InlineData("chord", "shape")]
    [InlineData("sn", "drum")]
    public void APhrase_NamedForAWordTheStreamCannotReadBack_IsRefusedAtTheDeclaration(string name, string why)
    {
        var d = SyntaxTree.Parse($"phrase {name} {{ c4 }}").Diagnostics
            .SingleOrDefault(x => x.Code == DiagnosticCodes.PhraseNameUnreachable);
        Assert.True(d != null, $"phrase {name}: no LYS1030");
        Assert.Contains(why, d!.Message);
    }

    [Theory]
    [InlineData("bass")]
    [InlineData("theme")]
    public void APhrase_NamedForAReachableWord_IsNotRefused(string name) =>
        Assert.DoesNotContain(SyntaxTree.Parse($"phrase {name} {{ c4 }}").Diagnostics,
            d => d.Code == DiagnosticCodes.PhraseNameUnreachable);

    // ── `tab X Y` ──

    private static string TabBook(string tabItem, bool declareBass = true) =>
        (declareBass ? "part bass { instrument bass }\n" : "")
        + "part click { instrument \"Click\" }\npart melody { clef treble }\n"
        + "section A { " + (declareBass ? "bass { c4 d e f | } " : "")
        + "click { c4 c c c | } melody { c'4 d' e' f' | } }\n"
        + "form main { A }\n"
        + $"score main {{ staff melody  {tabItem} }}";

    private static RenderSpec Spec(string src) =>
        RenderSpecParser.Parse(SyntaxTree.Parse(src).GetRoot().DescendantNodes<RenderDeclarationSyntax>().Single())!;

    [Fact]
    public void TabOfADeclaredPart_ThenABareWord_IsThatPartsTabPlusAMidiOnlyPart()
    {
        string src = TabBook("tab bass click");
        var spec = Spec(src);
        var tab = spec.Items.OfType<TabStaffSpec>().Single();
        Assert.Equal("bass", tab.Staff.VoiceName);
        Assert.Equal(TuningType.Bass, tab.Tuning);          // from the part's instrument
        Assert.Contains("click", spec.MidiOnlyParts);
        Assert.Contains("click", spec.SoundingPartNames);

        // The word is also a tuning, so the writer who meant it is told the synonym —
        // a WARNING, since the book is well-formed under the declared-part reading.
        var all = AllDiagnostics(src);
        Assert.DoesNotContain(all, d => d.Severity == DiagnosticSeverity.Error);
        var w = Assert.Single(all, d => d.Code == DiagnosticCodes.TabPartNameIsAlsoATuning);
        Assert.Equal(DiagnosticSeverity.Warning, w.Severity);
        Assert.Contains("tab bass4 click", w.Message);
        Assert.Equal(src.LastIndexOf("bass click", StringComparison.Ordinal), w.Span.Start);

        // Both words are part references: the rename sees `click` on the tab item too
        // (declaration, cell, tab), and an undefined one would be reported.
        var root = SyntaxTree.Parse(src).GetRoot();
        Assert.Equal(3, PartReferenceFinder.Occurrences(root, "click").Count);
        Assert.Equal(3, PartReferenceFinder.Occurrences(root, "bass").Count);
        Assert.Contains(AllDiagnostics(TabBook("tab bass clack")),
            d => d.Code == DiagnosticCodes.UndefinedPart && d.Message.Contains("'clack'"));
    }

    [Fact]
    public void TabWithTheTuningsSynonym_IsTheTuningOverThePart_AndSilent()
    {
        string src = TabBook("tab bass4 click");
        var tab = Spec(src).Items.OfType<TabStaffSpec>().Single();
        Assert.Equal("click", tab.Staff.VoiceName);
        Assert.Equal(TuningType.Bass, tab.Tuning);
        Assert.Empty(Spec(src).MidiOnlyParts);
        Assert.DoesNotContain(AllDiagnostics(src), d => d.Code == DiagnosticCodes.TabPartNameIsAlsoATuning);
    }

    [Theory]
    [InlineData("tab bass")]
    [InlineData("tab bass as numbers")]
    public void TabOfADeclaredPartAlone_IsThatPart(string item)
    {
        var spec = Spec(TabBook(item));
        Assert.Equal("bass", spec.Items.OfType<TabStaffSpec>().Single().Staff.VoiceName);
        Assert.Empty(spec.MidiOnlyParts);
    }

    [Fact]
    public void TheSameWordTwice_KeepsTheTuningReading_AndIsSilent()
    {
        // `tab bass bass` is how the 2026-10-02 big-band book got the bass tuning onto its
        // part named bass; both readings draw that, so the old one stands without a warning.
        string src = TabBook("tab bass bass");
        var spec = Spec(src);
        var tab = spec.Items.OfType<TabStaffSpec>().Single();
        Assert.Equal("bass", tab.Staff.VoiceName);
        Assert.Equal(TuningType.Bass, tab.Tuning);
        Assert.Empty(spec.MidiOnlyParts);
        Assert.DoesNotContain(AllDiagnostics(src), d => d.Code == DiagnosticCodes.TabPartNameIsAlsoATuning);
        Assert.Equal(3, PartReferenceFinder.Occurrences(SyntaxTree.Parse(src).GetRoot(), "bass").Count);
    }

    [Fact]
    public void TabWithNoPartOfThatName_ReadsTheTuningAsBefore()
    {
        // The reading every book before 2026-10-03 had — and the one every book that
        // compiled then still gets, since none declares a part named for its tuning word.
        string src = TabBook("tab bass click", declareBass: false);
        var spec = Spec(src);
        var tab = spec.Items.OfType<TabStaffSpec>().Single();
        Assert.Equal("click", tab.Staff.VoiceName);
        Assert.Equal(TuningType.Bass, tab.Tuning);
        Assert.Empty(spec.MidiOnlyParts);
        Assert.DoesNotContain(AllDiagnostics(src), d => d.Code == DiagnosticCodes.TabPartNameIsAlsoATuning);
    }

    [Fact]
    public void TheTwinReadsTheSamePart()
    {
        // The LilyPond exporter asks the same node, so `tab bass click` writes a TabStaff
        // of the bass part, not of the click track.
        var ly = new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(TabBook("tab bass click")));
        Assert.Contains("TabStaff", ly);
        Assert.DoesNotContain("click", ly.Substring(ly.IndexOf("TabStaff", StringComparison.Ordinal)).Split('\n')[0]);
    }
}
