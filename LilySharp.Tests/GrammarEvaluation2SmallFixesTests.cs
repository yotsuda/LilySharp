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

    // ── The leftovers of the same evaluation ────────────────────────────────────────────────

    /// <summary>The notes of a warning about a singer's staff pointed into the part Lily# writes
    /// after the file's text: an editor showed them at lines past the file's end. They point at
    /// the singer line that made the part — the command line's and the editor's alike.</summary>
    [Fact]
    public void TheNotesOfAWarningAboutASingersStaff_PointAtTheSingerLine()
    {
        string source = "octave absolute\nlanguage english\n"
            + "part lead { lyrics words\n  section I { r1 | r1 | }\n  section A { e'4 e' d' c' | e'1 | }\n}\n"
            + "part b { clef bass\n  section I { c1 | c1 | c1 | }\n  section A { c1 | c1 | c1 | }\n}\n"
            + "lyrics words { section A { Take the long way | round | } }\nsinger carol female on lead up 3 in A\n"
            + "form { ~I ~A }\nscore { staff lead  lyrics words  staff carol  staff b }\n";
        int carol = source.IndexOf("carol female", StringComparison.Ordinal);
        string made = LilySharp.Core.Editing.SingerParts.Expand(source, SyntaxTree.Parse(source), out _);
        Assert.True(made.Length > source.Length);

        // As written they stand past the file's end…
        var raw = Said(made).Where(d => d.Code == DiagnosticCodes.SectionBarCountMismatch).ToList();
        Assert.Contains(raw.SelectMany(d => d.Related), r => r.Span.Start >= source.Length);
        // …and what a reader is handed has none there: carol's name on her line, for her staff and her words.
        foreach (var said in new[]
                 {
                     LilySharp.Core.Editing.SingerParts.OwnOf(Said(made), made),
                     LilySharp.Lsp.LilySharpLanguageServer.DocumentDiagnostics(source, SyntaxTree.Parse(source), "", _ => null),
                 })
        {
            var warnings = said.Where(d => d.Code == DiagnosticCodes.SectionBarCountMismatch).ToList();
            Assert.Equal(2, warnings.Count);
            Assert.All(warnings.SelectMany(d => d.Related), r => Assert.True(r.Span.End <= source.Length, r.Message));
            Assert.Equal([carol, carol, carol],
                warnings.SelectMany(d => d.Related).Where(r => r.Message.Contains("singer 'carol'")).Select(r => r.Span.Start));
            Assert.Equal(raw.Sum(d => d.Related.Count), warnings.Sum(d => d.Related.Count));
        }
        // A file with no singer's staff is handed its diagnostics as they are.
        const string noSinger = "part a { }\npart b { }\nsection A { a { c1 | } b { c1 | c1 | } }\nform { A }\nscore { staff a  staff b }\n";
        var plain = Said(noSinger);
        Assert.Contains(plain, d => d.Related.Count > 0);
        Assert.Equal(plain, LilySharp.Core.Editing.SingerParts.OwnOf(plain, noSinger), ReferenceEqualityComparer.Instance);
    }

    private static string KitBook(string kit, string melody = "c'1 | c'1 | c'1 |")
        => "octave absolute\ntime 4/4\npart m { }\npart kit { clef percussion }\n"
            + "phrase ride { cymr4 cymr cymr cymr | }\nphrase foot { r4 hhp r hhp | }\n"
            + "section A { m { " + melody + " } kit { " + kit + " } }\nform { A }\nscore { staff m  staff kit }\n";

    /// <summary>A chart's inline line with its own bar line, then the bar line after the chart:
    /// the section was a bar too long (LYS2007) and the melody padded by a bar, where the same
    /// chart of one-bar phrases was right. The page, the bar counts and the sound agree.</summary>
    [Theory]
    [InlineData("drums { { cymr4 cymr cymr cymr | } { r4 hhp r hhp | } } | % | % |")]
    [InlineData("drums { { cymr4 cymr cymr cymr | } { r4 hhp r hhp | } } | sn1 | sn1 |")]
    [InlineData("drums { { cymr4 cymr cymr cymr } { r4 hhp r hhp } } | sn1 | sn1 |")]
    [InlineData("drums { ride foot } | sn1 | sn1 |")]
    // A chart the merge does not take (a tuplet in a tuplet) is drawn a voice a line: the same.
    [InlineData("drums { { cymr4 cymr cymr cymr | } { tuplet 3/2 { tuplet 3/2 { bd8 bd bd } bd4 bd4 } r2 | } } | sn1 | sn1 |")]
    public void TheBarLineAfterADrumChart_ConfirmsTheBarItsLinesClosed(string kit)
    {
        string source = KitBook(kit);
        Assert.Empty(Said(source).Select(d => d.Message));
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree);
        var score = SvgGenerator.CollectScore(tree, spec);
        Assert.All(score.EnumerateStaves(), s => Assert.Equal(3, s.Staff.Voices[0].Measures.Length));
        // The third bar's snare (or the mark's last cymbal) sounds in the third bar.
        var midi = new LilySharp.Core.Midi.MidiExporter { Form = spec?.Form, Score = spec }.Export(tree);
        int bar = midi.TicksPerQuarterNote * 4;
        Assert.All(midi.Tracks.Where(t => t.Notes.Count > 0), t => Assert.InRange(t.Notes.Max(n => n.StartTick), 2 * bar, 3 * bar - 1));
    }

    /// <summary>What the chart's exit does not touch: a second bar line after the confirming one
    /// is an empty bar still, and a voice span's own bar lines pair with the one after it.</summary>
    [Fact]
    public void AfterTheConfirmingBarLine_APairIsStillAnEmptyBar()
    {
        string source = KitBook("drums { { cymr4 cymr cymr cymr | } { r4 hhp r hhp | } } | | sn1 |");
        Assert.Empty(Said(source).Select(d => d.Message));
        var kit = Condensed(source).EnumerateStaves().Last().Staff.Voices[0].Measures;
        Assert.Equal(3, kit.Length);
        Assert.True(kit[1].IsEmptyPlaceholder);

        string span = "octave absolute\ntime 4/4\npart m { }\npart n { }\n"
            + "section A { m { c'1 | c'1 | c'1 | c'1 | } n { voice { c''1 | } { e'1 | } | c''1 | c''1 | } }\nform { A }\nscore { staff m  staff n }\n";
        Assert.Empty(Said(span).Select(d => d.Message));
        Assert.Equal(4, Condensed(span).EnumerateStaves().Last().Staff.Voices[0].Measures.Length);
    }

    /// <summary>A voice span whose first voice ends in a bar line, then a bar line: the second
    /// of a pair, an empty bar on the page — and the MIDI played none, a bar ahead of the page
    /// and of the other parts from there. Each spelling sounds as the one an author would type
    /// with <c>s1</c> for the empty bar, in the page's bars; a span that leaves its bar open, a
    /// phrase (its exit is confirmed, a span inside it too) and a plain pair are as they were.</summary>
    [Theory]
    [InlineData("voice { c'1 | } { e'1 | } | d'1 | g'1 |", "voice { c'1 } { e'1 } | s1 | d'1 | g'1 |", 4)]
    [InlineData("voice { c'1 | d'1 | } { e'1 | f'1 | } | | g'1 |", "voice { c'1 | d'1 } { e'1 | f'1 } | s1 | s1 | g'1 |", 5)]
    [InlineData("voice { c'1 | } { e'1 | } || d'1 | g'1 |", "voice { c'1 } { e'1 } || d'1 | g'1 |", 3)]
    [InlineData("voice { c'1 | } { e'1 | } d'1 | g'1 |", "voice { c'1 } { e'1 } | d'1 | g'1 |", 3)]
    [InlineData("two | d'1 | g'1 |", "voice { c'1 } { e'1 } | d'1 | g'1 |", 3)]
    [InlineData("one | d'1 | g'1 |", "c'1 | d'1 | g'1 |", 3)]
    [InlineData("one | | d'1 | g'1 |", "c'1 | s1 | d'1 | g'1 |", 4)]
    [InlineData("c'1 | | d'1 | g'1 |", "c'1 | s1 | d'1 | g'1 |", 4)]
    public void TheBarLineAfterAVoiceSpan_SoundsTheBarThePageDraws(string written, string spelled, int bars)
    {
        string Book(string music) => "octave absolute\ntime 4/4\nphrase one { c'1 | }\nphrase two { voice { c'1 | } { e'1 | } }\n"
            + "part m { }\npart b { clef bass }\nsection A { m { " + music + " } b { " + string.Concat(Enumerable.Repeat("c1 | ", bars))
            + "} }\nform { A }\nscore { staff m  staff b }\n";
        static List<(int, int)> Sounds(string source)
        {
            var tree = SyntaxTree.Parse(source);
            var spec = RenderSpecParser.FindFirst(tree);
            return [.. new LilySharp.Core.Midi.MidiExporter { Form = spec?.Form, Score = spec }.Export(tree)
                .Tracks.SelectMany(t => t.Notes).Select(n => (n.Pitch, n.StartTick)).Order()];
        }
        // The page: the part is as many bars as the part beside it, which writes them plainly.
        Assert.DoesNotContain(Said(Book(written)), d => d.Code == DiagnosticCodes.SectionBarCountMismatch);
        Assert.Equal(bars, Condensed(Book(written)).EnumerateStaves().First().Staff.Voices[0].Measures.Length);
        Assert.Equal(Sounds(Book(spelled)), Sounds(Book(written)));
    }

    /// <summary>…and the MusicXML wrote none either (a leftover of the same day): three measures
    /// for the page's four, the file a bar short from there on. It writes the bar the page draws,
    /// the bar of rest a plain <c>| |</c> is — and, the other way round, the bar line after a
    /// phrase that ends in its own confirms that bar, where a bar of rest was written after every
    /// such phrase. The shapes that gain no bar gain none, and the file read back is the page's bars.</summary>
    [Theory]
    [InlineData("voice { c'1 | } { e'1 | } | d'1 | g'1 |", 4, 1)]
    [InlineData("voice { c'1 | } { e'1 | } | | d'1 | g'1 |", 5, 2)]
    [InlineData("voice { c'1 | d'1 | } { e'1 | f'1 | } | g'1 | a'1 |", 5, 1)]
    [InlineData("voice { c'1 | d'1 | } { e'1 | } | g'1 | a'1 |", 5, 1)]
    [InlineData("d'1 | voice { c'1 | } { e'1 | } |", 3, 1)]
    [InlineData("voice { c'1 } { e'1 } | d'1 | g'1 |", 3, 0)]
    [InlineData("voice { c'1 | d'1 } { e'1 | f'1 } | g'1 | a'1 |", 4, 0)]
    [InlineData("voice { c'1 } { e'1 | } | d'1 | g'1 |", 3, 0)]
    [InlineData("voice { c'1 | } { e'1 | } d'1 | g'1 |", 3, 0)]
    [InlineData("voice { c'1 | } { e'1 | } || d'1 | g'1 |", 3, 0)]
    [InlineData("one | d'1 | g'1 |", 3, 0)]
    [InlineData("one one | d'1 | g'1 |", 4, 0)]
    [InlineData("one | | d'1 | g'1 |", 4, 1)]
    [InlineData("two | d'1 | g'1 |", 3, 0)]
    [InlineData("voice { one } { e'1 | } | d'1 | g'1 |", 3, 0)]
    [InlineData("c'1 | | d'1 | g'1 |", 4, 1)]
    public void TheBarLineAfterAVoiceSpan_IsTheBarThePageDraws_InMusicXml(string written, int bars, int empty)
    {
        string source = "octave absolute\ntime 4/4\nphrase one { c'1 | }\nphrase two { voice { c'1 | } { e'1 | } }\n"
            + "part m { }\nsection A { m { " + written + " } }\nform { A }\nscore { staff m }\n";
        Assert.Empty(Said(source).Select(d => d.Message));
        static int PageBars(string s) => Condensed(s).EnumerateStaves().First().Staff.Voices[0].Measures.Length;
        Assert.Equal(bars, PageBars(source));

        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree);
        var document = new LilySharp.Core.MusicXml.MusicXmlExporter { Form = spec?.Form, Score = spec }.Export(tree);
        var measures = Assert.Single(document.Parts).Measures;
        Assert.Equal(Enumerable.Range(1, bars), measures.Select(m => m.Number));
        // An empty bar is one whole-bar rest and nothing else, as `c'1 | | d'1` writes it.
        Assert.Equal(empty, measures.Count(m => m.Notes.Count == 1 && m.Notes[0] is { IsRest: true, Type: "whole" }));
        Assert.Equal(bars - empty, measures.Count(m => m.Notes.Any(n => !n.IsRest && !n.IsBackup)));

        var (lys, _) = new LilySharp.Core.MusicXmlImport.MusicXmlImporter().Import(document.ToXml().ToString());
        Assert.Empty(Said(lys).Select(d => d.Message));
        Assert.Equal(bars, PageBars(lys));
    }

    /// <summary>A drum chart is no voice span: the bar line after it confirms the bar its lines
    /// closed, in the MusicXML as on the page — no bar of rest.</summary>
    [Fact]
    public void TheBarLineAfterADrumChart_IsNoBarInMusicXml()
    {
        string source = KitBook("drums { { cymr4 cymr cymr cymr | } { r4 hhp r hhp | } } | sn1 | sn1 |");
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree);
        var document = new LilySharp.Core.MusicXml.MusicXmlExporter { Form = spec?.Form, Score = spec }.Export(tree);
        Assert.All(document.Parts, p => Assert.Equal(3, p.Measures.Count));
        Assert.All(document.Parts[1].Measures, m => Assert.Contains(m.Notes, n => !n.IsRest && !n.IsBackup));
    }
}
