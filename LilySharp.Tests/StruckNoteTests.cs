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
using System.Text.RegularExpressions;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>@struck</c> — a chord member struck with the chord and let go, the small black head on the
/// chord's stem (owner's decision 2026-11-19, Calamo request 31: Kücken Op.47-3 bar 70).
/// </summary>
[Trait("Category", "Unit")]
public class StruckNoteTests
{
    private static string Book(string music, string time = "3/4") => $$"""
        octave absolute
        time {{time}}
        part p { clef treble }
        section A { p { {{music}} } }
        form { ~A }
        score { staff p }
        """;

    private static MusicItem[] Items(string text)
    {
        var tree = SyntaxTree.Parse(text);
        Assert.Empty(SemanticValidation.Run(tree).Where(d => d.Severity != DiagnosticSeverity.Info));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.Choose(RenderSpecParser.FindAll(tree), null));
        return [.. score.EnumerateStaves().Single().Staff.Voices[0].Measures[0].Items];
    }

    /// <summary>The music glyphs of a page, in drawing order: x, y, font size, source offset.</summary>
    private static (double X, double Y, string Size, int Pos)[] Glyphs(string svg) =>
        [.. Regex.Matches(svg, "<text class=\"music\"[^>]* x=\"([-\\d.]+)\" y=\"([-\\d.]+)\" font-size=\"([\\d.]+)\" data-pos=\"(\\d+)\"")
            .Select(m => (double.Parse(m.Groups[1].Value), double.Parse(m.Groups[2].Value), m.Groups[3].Value, int.Parse(m.Groups[4].Value)))];

    [Fact]
    public void TheMember_IsStruck_AndTheOthersAreNot()
    {
        var chord = Assert.IsType<ChordItem>(Items(Book("<gis' cis'' gis''@struck>2. |"))[0]);
        Assert.Equal(new[] { false, false, true }, chord.Notes.Select(n => n.IsStruck));
    }

    [Fact]
    public void TheSmallHead_StandsWhereLilyPondPutsIt()
    {
        // MEASURED on LilyPond 2.26.0 (Lab sessions/p1317/lp/s1.ly), the small head's left less
        // the full heads': stem down 0 (bar 3, all at 34.3403); stem up 0.5621, the two right
        // edges together (bar 5: 48.2037 against 47.6416; bar 6 the same). The head is the
        // thirteen's at magstep(-4): 4.00 → 2.52.
        string text = Book("<gis' cis'' gis''@struck>2. | <a@struck c' e'>2. | <c' e' bes'@struck>2. |");
        var glyphs = Glyphs(SvgGenerator.Generate(SyntaxTree.Parse(text)));
        var small = glyphs.Where(g => g.Size == "2.52").ToArray();
        Assert.Equal(3, small.Length);
        double[] delta = [.. small.Select(s => s.X - glyphs.Last(g => g.Size == "4.00" && g.Pos != s.Pos
            && System.Math.Abs(g.X - s.X) < 1 && g.X <= s.X + 0.001).X)];
        // (The page writes two places: each x is within 0.005.)
        Assert.InRange(delta[0], -0.011, 0.011);
        Assert.InRange(delta[1], 0.5621 - 0.011, 0.5621 + 0.011);
        Assert.InRange(delta[2], 0.5621 - 0.011, 0.5621 + 0.011);
    }

    [Fact]
    public void ItsAccidental_IsFullSize_AndStandsBesideTheSmallHead()
    {
        // MEASURED (the same book, bar 6 `<c' e' \struck bes'>2.`): the flat at the staff's own
        // size, its left 0.5879 left of the full heads' (54.3711 against 54.9590) — 1.1500 left
        // of the small head it belongs to, not of a full head standing there.
        string text = Book("<c' e' bes'@struck>2. |");
        var glyphs = Glyphs(SvgGenerator.Generate(SyntaxTree.Parse(text)));
        var ofTheMember = glyphs.Where(g => g.Pos == text.IndexOf("bes'")).OrderBy(g => g.X).ToArray();
        Assert.Equal(new[] { "4.00", "2.52" }, ofTheMember.Select(g => g.Size));
        double fullHeads = glyphs.Single(g => g.Pos == text.IndexOf("e'")).X;
        Assert.InRange(fullHeads - ofTheMember[0].X, 0.5879 - 0.011, 0.5879 + 0.011);
    }

    [Fact]
    public void TheSmallHead_TakesNoDot_AndAShortLedger()
    {
        // MEASURED (the same book): two dots on `<gis' cis'' \struck gis''>2.`, where the tweak
        // without Dots.stencil has three; on `<\struck a c' e'>2.` the a's own ledger is 1.2230
        // long and the c's 2.0661.
        string dotted = SvgGenerator.Generate(SyntaxTree.Parse(Book("<gis' cis'' gis''@struck>2. |")));
        string plain = SvgGenerator.Generate(SyntaxTree.Parse(Book("<gis' cis'' gis''>2. |")));
        // The dots are the page's rightmost column of glyphs.
        static int Dots(string svg) => Glyphs(svg).GroupBy(g => g.X).OrderBy(g => g.Key).Last().Count();
        Assert.Equal(3, Dots(plain));
        Assert.Equal(2, Dots(dotted));

        string ledgers = SvgGenerator.Generate(SyntaxTree.Parse(Book("<a@struck c' e'>2. |")));
        // The level lines shorter than a bar: the ledgers.
        var lengths = Regex.Matches(ledgers, "<line x1=\"([-\\d.]+)\" y1=\"([-\\d.]+)\" x2=\"([-\\d.]+)\" y2=\"\\2\"")
            .Select(m => double.Parse(m.Groups[3].Value) - double.Parse(m.Groups[1].Value))
            .Where(length => length < 3).OrderBy(x => x).ToArray();
        Assert.Equal(2, lengths.Length);
        Assert.InRange(lengths[0], 1.2230 - 0.011, 1.2230 + 0.011);
        Assert.InRange(lengths[1], 2.0661 - 0.011, 2.0661 + 0.011);
    }

    /// <summary>A beamed stem standing on a struck member begins where the small head's attachment
    /// is, as the unbeamed one does; until session 1332 the beam drew it from a full head's.</summary>
    [Fact]
    public void ABeamedStem_StandsOnTheSmallHead()
    {
        // MEASURED on LilyPond 2.26.0 (Lab sessions/p1332/beam/b1.ly): the stem's foot 0.0948 from
        // the small head's centre, stem up (bar 2) and down (bar 4); a full head's foot stays 0.15
        // on Lily#'s side (LilyPond 0.1862 — the beamed filled head's own question, not this one).
        string text = Book("<a@struck c' e'>8 <a c' e'>8 r2 | <e'' g'' c'''@struck>8 <e'' g'' c'''>8 r2 |");
        string svg = SvgGenerator.Generate(SyntaxTree.Parse(text));
        var stems = Regex.Matches(svg, "<line x1=\"([-\\d.]+)\" y1=\"([-\\d.]+)\" x2=\"\\1\" y2=\"([-\\d.]+)\"[^>]*stroke-width=\"0\\.130\"")
            .Select(m => (X: double.Parse(m.Groups[1].Value), Foot: double.Parse(m.Groups[2].Value))).ToArray();
        Assert.Equal(4, stems.Length);
        var glyphs = Glyphs(svg);
        // The foot's head: the first glyph at the source offset of the text (a unique spelling).
        double FootGap(int stem, string at) =>
            System.Math.Abs(stems[stem].Foot - glyphs.First(g => g.Pos == text.IndexOf(at, StringComparison.Ordinal)).Y);
        // (The page writes two places: each y is within 0.005.)
        Assert.InRange(FootGap(0, "a@struck"), 0.0948 - 0.011, 0.0948 + 0.011);
        Assert.InRange(FootGap(1, "a c' e'>8 r2"), 0.15 - 0.011, 0.15 + 0.011);
        Assert.InRange(FootGap(2, "c'''@struck"), 0.0948 - 0.011, 0.0948 + 0.011);
        Assert.InRange(FootGap(3, "c'''>8 r2"), 0.15 - 0.011, 0.15 + 0.011);
    }

    /// <summary>On a tab the struck member's fret number is smaller, centred where LilyPond's smaller
    /// reference "8" puts it; until session 1333 it was a full one.</summary>
    [Fact]
    public void OnATab_ItsNumberIsSmaller_AndStandsAFullHeadOffsetLeft()
    {
        // MEASURED on LilyPond 2.26.0 (Lab sessions/p1332/beam/tab1.ly): the struck "3" at 1.3862
        // against 1.7459 (0.794); its centre 3/5 × (the full "8"'s width) × (1 − 0.794) left of a
        // full number's (scm/tablature.scm tab-note-head::print), its baseline centred on the string.
        const string text = """
            octave absolute
            part g { clef treble_8 }
            section A { g { <c' e' g'>2 <c' e' g'@struck>2 | } }
            form { ~A }
            score { staff g tab g }
            """;
        // The tab's numbers (written from the chords; the clef's 8 is not one): two chords of three.
        int music = text.IndexOf("<c'", StringComparison.Ordinal);
        var digits = Regex.Matches(SvgGenerator.Generate(SyntaxTree.Parse(text)),
                "<text x=\"([-\\d.]+)\" y=\"([-\\d.]+)\" font-size=\"([\\d.]+)\"[^>]* data-pos=\"(\\d+)\">(\\d+)</text>")
            .Where(m => int.Parse(m.Groups[4].Value) >= music)
            .Select(m => (X: double.Parse(m.Groups[1].Value), Y: double.Parse(m.Groups[2].Value),
                Size: double.Parse(m.Groups[3].Value), Text: m.Groups[5].Value))
            .ToArray();
        Assert.Equal(6, digits.Length);
        // Each chord's "3" (the g', top string) against its bottom number (the c', string 3: the
        // column's own place, no zigzag).
        (double Dx, double Dy, double Ratio) TopAgainstBottom(int chord)
        {
            var three = digits.Skip(3 * chord).Take(3).Single(d => d.Text == "3");
            var bottom = digits.Skip(3 * chord).Take(3).MaxBy(d => d.Y);
            return (bottom.X - three.X, three.Y, three.Size / bottom.Size);
        }
        var full = TopAgainstBottom(0);
        var small = TopAgainstBottom(1);
        Assert.InRange(full.Dx, -0.006, 0.006);
        Assert.InRange(small.Ratio, 0.794 - 0.01, 0.794 + 0.01);
        // 0.6 × w8 × 0.206 at Lily#'s digit size: 0.16 at 2.30.
        Assert.InRange(small.Dx, 0.16 - 0.015, 0.16 + 0.015);
        Assert.True(small.Dy < full.Dy, "the smaller number's baseline is higher, centred on the string");
    }

    /// <summary><c>lysc import</c> reads the struck member back from the MusicXML Lily# writes (a
    /// sounding chord member at the cue size); until session 1330 it came back a plain member.
    /// A cue-size member that is a <c>&lt;cue/&gt;</c> is not one.</summary>
    [Fact]
    public void TheImport_ReadsItBack()
    {
        string xml = new LilySharp.Core.MusicXml.MusicXmlExporter()
            .Export(SyntaxTree.Parse(Book("<c' e' g'@struck>2. | <c'' d''@struck>2. |"))).ToXml().ToString();
        string back = new LilySharp.Core.MusicXmlImport.MusicXmlImporter().Import(xml).Item1;
        Assert.Contains("<c' e' g'@struck>2.", back);
        Assert.Contains("<c'' d''@struck>2.", back);
        string cue = xml.Replace("<type size=\"cue\">", "<cue /><type size=\"cue\">");
        Assert.DoesNotContain("@struck", new LilySharp.Core.MusicXmlImport.MusicXmlImporter().Import(cue).Item1);
        // Nor a single note at the cue size: nothing is held under it.
        string single = new LilySharp.Core.MusicXml.MusicXmlExporter()
            .Export(SyntaxTree.Parse(Book("c'2. |"))).ToXml().ToString();
        Assert.Contains("<type>half</type>", single);
        single = single.Replace("<type>half</type>", "<type size=\"cue\">half</type>");
        Assert.DoesNotContain("@struck", new LilySharp.Core.MusicXmlImport.MusicXmlImporter().Import(single).Item1);
    }

    /// <summary>Written first, the struck member is moved behind the held ones in MusicXML; the
    /// chord's marks stay on its first &lt;note&gt; (until session 1330 they went with the struck
    /// one onto a &lt;chord/&gt; note), and an arpeggio is still every member's.</summary>
    [Fact]
    public void InMusicXml_TheChordsMarks_StayOnItsFirstNote()
    {
        static string[] Notes(string music)
        {
            var doc = System.Xml.Linq.XDocument.Parse(new LilySharp.Core.MusicXml.MusicXmlExporter()
                .Export(SyntaxTree.Parse(Book(music))).ToXml().ToString());
            return [.. doc.Descendants("note").Select(n => n.Element("pitch")!.Element("step")!.Value
                + (n.Element("chord") != null ? "+" : "")
                + "[" + string.Join(",", n.Descendants().Where(d => d.Parent?.Name.LocalName is "articulations" or "notations"
                    && d.Name.LocalName is not "articulations").Select(d => d.Name.LocalName)) + "]")];
        }
        Assert.Equal(new[] { "C[accent]", "E+[]", "G+[]" }, Notes("<g'@struck c'' e''>2.@accent |"));
        Assert.Equal(new[] { "C[arpeggiate]", "E+[arpeggiate]", "G+[arpeggiate]" }, Notes("<g'@struck c'' e''>2.@arpeggio |"));
        // An empty chord's script before it is the chord's too, on that first note.
        Assert.Equal(new[] { "C[]", "C[accent]", "E+[]", "G+[]" }, Notes("c'2 <>@accent <g'@struck c'' e''>4 |"));
    }

    /// <summary>A tie that reaches the struck member is said (LYS4086, owner's decision 2026-11-20):
    /// the page draws it as LilyPond does, but the member is let go, so it does not sound and
    /// MusicXML does not tie it. Ties on the held members alone are not.</summary>
    [Theory]
    [InlineData("<c' e' g'@struck>2~ <c' e' g'>4 |", 1)]
    [InlineData("<c' e' g'@struck~>2 <c' e' g'>4 |", 1)]
    [InlineData("<c'~ e'~ g'@struck>2 <c' e' g'>4 |", 0)]
    [InlineData("<c' e' g'>2~ <c' e' g'>4 |", 0)]
    public void ATieFromTheStruckMember_IsSaid(string music, int expected)
        => Assert.Equal(expected, SemanticValidation.Run(SyntaxTree.Parse(Book(music))).Count(d => d.Code == "LYS4086"));

    [Fact]
    public void TheDot_StandsRightOfTheSmallHead_OnTheStemsFarSide()
    {
        // MEASURED (the same book, bar 7 `<c'' \struck d''>2.`): a second, the small d'' on the
        // stem's far side, the c'''s dot 2.578 right of the c'''s head — right of the small head's
        // own ink. Until session 1330 the dot stood right of a full head there (3.14).
        string text = Book("<c'' d''@struck>2. |");
        var glyphs = Glyphs(SvgGenerator.Generate(SyntaxTree.Parse(text)));
        var ofTheChord = glyphs.Where(g => g.Pos == text.IndexOf('<')).ToArray();
        double head = glyphs.Single(g => g.Pos == text.IndexOf("c''")).X;
        double dot = Assert.Single(ofTheChord).X;
        Assert.InRange(dot - head, 2.578 - 0.011, 2.578 + 0.011);
    }

    [Fact]
    public void TheNextColumn_IsMeasuredFromTheSmallHead_WhereItIsTheStemsFirst()
    {
        // MEASURED (the same book): stem down with the small head on top, the bar line stands
        // 0.562 nearer the heads than the unmarked chord's (4.165 against 4.727).
        static double BarAfterHead(string music)
        {
            // The chord's cis head (right of its own sharp) to the next bar's note (left of its dot).
            string text = Book(music + " c''2. |");
            var glyphs = Glyphs(SvgGenerator.Generate(SyntaxTree.Parse(text)));
            double chord = glyphs.Where(g => g.Pos == text.IndexOf("cis'")).Max(g => g.X);
            double next = glyphs.Where(g => g.Pos == text.IndexOf("c''2.")).Min(g => g.X);
            return next - chord;
        }
        Assert.InRange(BarAfterHead("<gis' cis'' gis''>2. |") - BarAfterHead("<gis' cis'' gis''@struck>2. |"),
            0.562 - 0.011, 0.562 + 0.011);
        // Stem up with the small head at the foot, the two right edges meet: nothing moves.
        Assert.Equal(BarAfterHead("<a cis' e'>2. |"), BarAfterHead("<a@struck cis' e'>2. |"), 2);
    }

    private static (int Key, int Start, int Length)[] Sounds(string text)
    {
        var midi = new LilySharp.Core.Midi.MidiExporter().Export(SyntaxTree.Parse(text));
        return [.. midi.Tracks.SelectMany(t => t.Notes).OrderBy(n => n.StartTick).ThenBy(n => n.Pitch)
            .Select(n => (n.Pitch, n.StartTick, n.DurationTicks))];
    }

    [Fact]
    public void ItSounds_AQuarterAtMost_AndTheRestIsHeld()
    {
        var notes = Sounds(Book("<c' e' g'@struck>2. | <c' e' g'@struck>8 r8 r2 |"));
        int quarter = notes[0].Length / 3;
        Assert.Equal(new[] { (60, 0, 3 * quarter), (64, 0, 3 * quarter), (67, 0, quarter) }, notes.Take(3));
        // In a chord shorter than a quarter it is as long as the chord.
        Assert.Equal(3, notes.Skip(3).Select(n => n.Length).Count());
        Assert.Single(notes.Skip(3).Select(n => n.Length).Distinct());
    }

    [Fact]
    public void InATremolo_ItSoundsAtTheFirstStrokeOnly()
    {
        // Kücken Op.47-3 bar 70, the right hand: 24 strokes of the chord and the note, and the
        // gis' of the chord once, at the head.
        var notes = Sounds(Book("repeat tremolo 24 { <gis cis' gis'@struck>64 gis'64 } |", "6/8"));
        Assert.Equal(24, notes.Count(n => n.Key == 56));
        Assert.Equal(24, notes.Count(n => n.Key == 61));
        Assert.Equal(24 + 1, notes.Count(n => n.Key == 68));
        Assert.Single(notes, n => n.Start == 0 && n.Key == 68);
        Assert.Equal(notes.First(n => n.Key == 56).Length, notes.Single(n => n.Start == 0 && n.Key == 68).Length);
    }

    [Fact]
    public void TheTwins_SayItLilyPondsWay_AndMusicXmlsWay()
    {
        var tree = SyntaxTree.Parse(Book("<gis'@struck cis'' gis''>2. |"));
        string ly = new LilySharp.Core.LilyPond.LilyPondExporter().Export(tree);
        Assert.Contains("<\\tweak font-size #-4 \\tweak duration-log #2 \\tweak Dots.stencil ##f gis' cis'' gis''>2.", ly);

        // A sounding note of the cue size — no <cue/> — a quarter long, after the held members.
        string xml = new LilySharp.Core.MusicXml.MusicXmlExporter().Export(tree).ToXml().ToString();
        Assert.DoesNotContain("<cue", xml);
        var notes = Regex.Matches(xml, "<note>[\\s\\S]*?</note>").Select(m => m.Value).ToArray();
        Assert.Equal(3, notes.Length);
        Assert.DoesNotContain("<chord", notes[0]);
        Assert.Contains("<octave>5</octave>", notes[0]);   // cis'', the first held member
        Assert.Contains("<chord", notes[2]);
        Assert.Contains("<type size=\"cue\">quarter</type>", notes[2]);
        Assert.DoesNotContain("<dot", notes[2]);
        static int Duration(string note) => int.Parse(Regex.Match(note, "<duration>(\\d+)</duration>").Groups[1].Value);
        Assert.Equal(Duration(notes[0]), 3 * Duration(notes[2]));
    }

    /// <summary>A <c>q</c> and a bare duration repeat the struck member as struck: the small head
    /// again, struck again and let go, the cue-size note behind the held ones. Until session 1332 a
    /// repetition drew it a full head, left it out of the MIDI and wrote it a held note.</summary>
    [Theory]
    [InlineData("<c' e' g'@struck>2 q2 |")]
    [InlineData("<c' e' g'@struck>2 2 |")]
    [InlineData("<g'@struck c' e'>2 q2 |")]
    public void ARepetition_StrikesItAgain(string music)
    {
        // MEASURED on LilyPond 2.26.0 (Lab sessions/p1332/beam/lp-q1.ly): `<c' e' \struck g'>2 q4`
        // draws the small head on the q too (its x 0.489 right of the full heads', stem up).
        // (Halves: the q's struck member sounds a quarter of it, a held one all of it.)
        string text = Book(music, "4/4");
        var glyphs = Glyphs(SvgGenerator.Generate(SyntaxTree.Parse(text)));
        Assert.Equal(2, glyphs.Count(g => g.Size == "2.52"));

        var notes = Sounds(text);
        int quarter = notes.First(n => n.Key == 60).Length / 2;
        Assert.Equal(new[] { (67, 0, quarter), (67, 2 * quarter, quarter) },
            notes.Where(n => n.Key == 67).ToArray());
        Assert.Equal(2, notes.Count(n => n.Key == 60));

        string xml = new LilySharp.Core.MusicXml.MusicXmlExporter().Export(SyntaxTree.Parse(text)).ToXml().ToString();
        var xmlNotes = Regex.Matches(xml, "<note>[\\s\\S]*?</note>").Select(m => m.Value).ToArray();
        Assert.Equal(6, xmlNotes.Length);
        Assert.Contains("<step>G</step>", xmlNotes[5]);
        Assert.Contains("<type size=\"cue\">quarter</type>", xmlNotes[5]);
        Assert.DoesNotContain("<chord", xmlNotes[3]);
    }

    [Theory]
    [InlineData("c'4@struck r2 |", "not a member")]
    [InlineData("<c' e' g'>2.@struck |", "not a member")]
    [InlineData("<c'@struck e'@struck>2. |", "every member")]
    public void WithNothingHeld_ItIsAnError_AndTheNoteIsAsWritten(string music, string why)
    {
        var tree = SyntaxTree.Parse(Book(music));
        // One error a mark.
        var errors = SemanticValidation.Run(tree).Where(d => d.Severity != DiagnosticSeverity.Info).ToList();
        Assert.Equal(Regex.Matches(music, "@struck").Count, errors.Count);
        Assert.All(errors, error =>
        {
            Assert.Equal((DiagnosticCodes.StruckWithNothingHeld, DiagnosticSeverity.Error), (error.Code, error.Severity));
            Assert.Contains(why, error.Message);
        });
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.Choose(RenderSpecParser.FindAll(tree), null));
        foreach (var item in score.EnumerateStaves().Single().Staff.Voices[0].Measures[0].Items.OfType<ChordItem>())
            Assert.All(item.Notes, n => Assert.False(n.IsStruck));
    }
}
