// This file is part of Lily#.
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Lily# is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// Lily# is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The small defects the four large scores of 2026-11-12 found beside the ones
/// <see cref="GrammarEvaluation2Tests"/> holds. Each test is one finding, as it was met.
/// </summary>
public class GrammarEvaluation2SmallFixesTests
{
    private static List<Diagnostic> Said(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return [.. tree.Diagnostics, .. SemanticValidation.Run(tree)];
    }

    private const string Choir = "octave absolute\npart sop { instrument soprano }\npart alt { instrument alto }\n";

    private static LilySharp.Core.Svg.Model.MultiStaffScore Condensed(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
    }

    /// <summary>Both parts of a condensed staff wrote <c>@mp</c> on one note: "mp" was printed
    /// twice, one under the other, and the hairpin and the <c>@f</c> they shared twice on one place.</summary>
    [Fact]
    public void ACondensedStaff_PrintsADynamicBothPartsWrite_Once()
    {
        var score = Condensed(Choir
            + "section A { sop { c''4@mp d''@cresc e'' f'' | g''1@f | } alt { e'4@mp f'@cresc g' a' | b'1@f | } }\n"
            + "form { ~A }\nscore { condensedStaff { sop alt } }\n");
        Assert.Equal(["mp", "f"], score.Dynamics.Select(d => d.Text));
        Assert.Single(LilySharp.Core.Svg.Layout.HairpinEngraver.DetectHairpins(score.MusicMarks, score.Dynamics));
    }

    /// <summary>The same moment, not the same place in the bar's text: the parts' rhythms differ,
    /// a section changes the meter — and what one part alone writes, or writes otherwise, stays.</summary>
    [Fact]
    public void ACondensedStaff_KeepsTheDynamicsItsPartsDoNotShare()
    {
        var score = Condensed(Choir
            + "section A { sop { c''4@mp d'' e''@p f'' | g''1@f | } alt { e'2@mp g'4@p a'@mf | b'1@ff | } }\n"
            + "section B { time 3/4  sop { c''4@pp d'' e''@f | } alt { e'2@pp g'4@f | } }\n"
            + "form { ~A ~B }\nscore { condensedStaff { sop alt } }\n");
        Assert.Equal(["f", "f", "ff", "mf", "mp", "p", "pp"], score.Dynamics.Select(d => d.Text).Order());
        // Two staves of their own each keep theirs.
        var apart = Condensed(Choir + "section A { sop { c''1@mp | } alt { e'1@mp | } }\nform { ~A }\nscore { staff sop  staff alt }\n");
        Assert.Equal(2, apart.Dynamics.Length);
    }

    /// <summary>A part with nothing in a section was exported with a full bar's rest in the short
    /// bar the next section's pickup completes: the file imported again did not align.</summary>
    [Fact]
    public void APartSilentInASection_RestsItsShortBarsAsLongAsTheOthersPlayThem()
    {
        string source = "octave absolute\ntime 3/4\npart a { }\npart b { clef bass }\n"
            + "section I { partial 4  a { c''4 | d''2. | e''2 | } }\n"
            + "section V { partial 4  a { c''4 | d''2. | e''2 | } b { c4 | d2. | e2 | } }\n"
            + "form { ~I ~V }\nscore { staff a  staff b }\n";
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree);
        var document = new LilySharp.Core.MusicXml.MusicXmlExporter { Form = spec?.Form, Score = spec }.Export(tree);
        static int[] Lengths(LilySharp.Core.MusicXml.MusicXmlPart part)
            => [.. part.Measures.Select(m => m.Notes.Where(n => !n.IsChord && !n.IsBackup).Sum(n => n.Duration))];
        Assert.Equal(Lengths(document.Parts[0]), Lengths(document.Parts[1]));
        Assert.Equal(document.Parts[0].Measures.Select(m => (m.Number, m.Implicit)), document.Parts[1].Measures.Select(m => (m.Number, m.Implicit)));

        var (lys, _) = new LilySharp.Core.MusicXmlImport.MusicXmlImporter().Import(document.ToXml().ToString());
        Assert.DoesNotContain(Said(lys), d => d.Code == DiagnosticCodes.MeasureDurationMismatch);
    }

    /// <summary><c>barNumbers every 4</c> printed "0" over a pickup: 0 is a multiple of every N.</summary>
    [Fact]
    public void BarNumbersEveryN_PrintNoneOverAPickup()
    {
        var g = RenderedGeometry.Render("octave absolute\ntime 4/4\nlayout { barNumbers every 4 }\npart m { }\n"
            + "section A { partial 4  m { g'4 | c''1 | d''1 | e''1 | f''1 | g''1 | a''1 | b''1 | c'''1 | d''1 | } }\n"
            + "form { A }\nscore { staff m }\n");
        Assert.Equal(["4", "8"], g.BarNumbers.OrderBy(n => n.X).Select(n => n.Text));
    }

    private const string Anthem = "octave absolute\nlanguage english\ntime 3/4\ntempo 4 = 88\n"
        + "part sop { instrument soprano  lyrics w }\npart org { clef treble  midiInstrument \"church organ\" }\n"
        + "phrase line { a'4@mp | d''2 e''4 | fis''2 d''4 | e''4 d'' cis'' | b'2BREATH }\n"
        + "section V { partial 4  sop { line line line line line line a'4 | } org { r4 | R*24 | } }\n"
        + "section R { tempo 4 = 100  sop { d''2.@f | d''2. | } org { d'2.@f | d'2. | } }\n"
        + "form { V R }\nscore { staff sop  staff org }\n";

    /// <summary>A choir anthem with a church organ was read as a band piece: told to keep one
    /// tempo, and to add drums and an electric bass.</summary>
    [Fact]
    public void AChurchOrgan_DoesNotMakeABand()
    {
        var found = LilySharp.Core.Editing.CompositionChecks.Findings(Anthem.Replace("BREATH", "@breath"));
        Assert.DoesNotContain(found, f => f.Id.StartsWith("band.", StringComparison.Ordinal));
        // A band's organ still does.
        Assert.Contains(LilySharp.Core.Editing.CompositionChecks.Findings(Anthem.Replace("BREATH", "@breath").Replace("church organ", "rock organ")),
            f => f.Id == "band.tempo-changes");
    }

    /// <summary>A breath mark at each phrase's end, and the singer was told of 24 bars without a rest.</summary>
    [Fact]
    public void AWrittenBreath_IsAPlaceToBreathe()
    {
        Assert.Contains(LilySharp.Core.Editing.CompositionChecks.Findings(Anthem.Replace("BREATH", "")), f => f.Id == "singers.no-breath");
        Assert.DoesNotContain(LilySharp.Core.Editing.CompositionChecks.Findings(Anthem.Replace("BREATH", "@breath")), f => f.Id == "singers.no-breath");
    }

    /// <summary>A singer's staff is a part of a name no file writes (<c>carol__0</c>), and a
    /// warning named it: it names the singer.</summary>
    [Fact]
    public void AMessageAboutASingersStaff_NamesTheSinger()
    {
        string source = "octave absolute\nlanguage english\n"
            + "part lead { lyrics words\n  section I { r1 | r1 | }\n  section A { e'4 e' d' c' | e'1 | }\n}\n"
            + "part b { clef bass\n  section I { c1 | c1 | c1 | }\n  section A { c1 | c1 | c1 | }\n}\n"
            + "lyrics words { section A { Take the long way | round | } }\nsinger carol female on lead up 3 in A\n"
            + "form { ~I ~A }\nscore { staff lead  lyrics words  staff carol  staff b }\n";
        string made = LilySharp.Core.Editing.SingerParts.Expand(source, SyntaxTree.Parse(source), out _);
        var said = Said(made).Where(d => d.Code == DiagnosticCodes.SectionBarCountMismatch).ToList();
        Assert.Equal(2, said.Count);
        Assert.Contains("3 bar(s) in part 'b' and the staff of singer 'carol'; 2 bar(s) in part 'lead'", said[0].Message);
        Assert.Contains("the words of singer 'carol'", said[1].Message);
        Assert.DoesNotContain(said.SelectMany(d => d.Related.Select(r => r.Message).Append(d.Message)), m => m.Contains("__"));
        // A part the file writes with such a name is that part.
        Assert.Equal("part 'carol__0'", LilySharp.Core.Music.LyricSinger.NameInMessages(SyntaxTree.Parse("part carol__0 { }\n").GetRoot(), "part", "carol__0"));
    }

    /// <summary>A line of dashes written as a divider was one identical error for each dash.</summary>
    [Theory]
    [InlineData("--------\n", "'--------' is not something a file can hold")]
    [InlineData("section A { m { c'1 | } ==== }\n", "'====' is not something a section can hold")]
    public void ARunOfOneStrayCharacter_IsOneError(string stray, string says)
    {
        string source = "time 4/4\npart m { }\n" + stray + "section B { m { c'1 | } }\nform { B }\nscore { staff m }\n";
        var tree = SyntaxTree.Parse(source);
        var error = Assert.Single(tree.Diagnostics, d => d.Code == DiagnosticCodes.StrayItemToken);
        Assert.Contains(says, error.Message);
        Assert.Equal(stray.Trim().Split(' ')[^1].Length is var n && stray.StartsWith('-') ? 8 : 4, error.Span.Length);
        Assert.Equal(source, tree.GetRoot().ToFullString());
        // Two runs are two mistakes; so are two tokens with a space between.
        Assert.Equal(2, SyntaxTree.Parse("---\ntime 4/4\n---\n").Diagnostics.Count(d => d.Code == DiagnosticCodes.StrayItemToken));
        Assert.Equal(2, SyntaxTree.Parse("- -\n").Diagnostics.Count(d => d.Code == DiagnosticCodes.StrayItemToken));
    }

    /// <summary>With <c>pedal text</c>, a form's "D.S. al Coda" under the system was printed over
    /// the release star (the bracket style was held clear the same day).</summary>
    [Fact]
    public void AJumpTextUnderTheSystem_StandsClearOfThePedalsReleaseStar()
    {
        var g = RenderedGeometry.Render("""
            octave absolute
            time 4/4
            part rh { clef treble }
            part lh { clef bass  pedal text }
            section A { rh { c''1 | d''1 | } lh { c4@sustain e g e | c4 e g e@!sustain | } }
            section B { rh { e''1 | f''1 | } lh { c4@sustain e g e | c4 e g2@!sustain | } }
            section C { rh { g''1 | } lh { c1 | } }
            form { A segno B to coda  ds al coda  coda C }
            score { grandStaff { staff rh  staff lh } }
            """);
        var jump = g.Texts.Single(t => t.Text == "D.S. al Coda");
        // The stars: the pedal's release glyph, drawn on its baseline (device Y grows down).
        const char star = LilySharp.Core.Svg.EmmentalerGlyphs.PedalStar;
        var stars = g.Glyphs.Where(s => s.Glyph == star).ToList();
        Assert.Equal(2, stars.Count);
        // The word's ink top, under-read on purpose (0.6 em is below any face's cap height).
        double jumpTop = jump.Y - 0.6 * jump.FontSize;
        Assert.True(jumpTop > stars[^1].Y,
            $"\"D.S. al Coda\" reaches up to {jumpTop:F3} and the release star down to {stars[^1].Y:F3}: the text has to stand under it.");
    }
}
