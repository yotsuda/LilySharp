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

using System.IO;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using LilySharp.Tests.LpFidelity;
using Xunit;
using Diagnostic = LilySharp.Core.Syntax.Diagnostic;
using DiagnosticSeverity = LilySharp.Core.Syntax.DiagnosticSeverity;

namespace LilySharp.Tests;

/// <summary>
/// The guitar marks a large guitar score written to try the grammar asked for, and the owner
/// approved (2026-11-13, grammar evaluation 3): the palm mute, the let ring and the barre — three
/// lines over a run of notes. Each is held from the text to the page, the sound and the exports.
/// </summary>
public class GrammarEvaluation3FeaturesTests
{
    private static List<Diagnostic> Said(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return [.. tree.Diagnostics, .. SemanticValidation.Run(tree)];
    }

    private static void Clean(string source) => Assert.Empty(Said(source).Select(d => d.Message));

    private static void RoundTrips(string source)
        => Assert.Equal(source, SyntaxTree.Parse(source).GetRoot().ToFullString());

    private static LilySharp.Core.Midi.MidiFile Midi(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree);
        return new LilySharp.Core.Midi.MidiExporter { Form = spec?.Form, Score = spec }.Export(tree);
    }

    private static string Ly(string source) => new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(source));

    private static string Xml(string source)
        => new LilySharp.Core.MusicXml.MusicXmlExporter().Export(SyntaxTree.Parse(source)).ToXml()
            .ToString(System.Xml.Linq.SaveOptions.DisableFormatting);

    private static readonly Uri DocUri = new("file:///guitar.lys");

    private static char GlyphOf(LilySharp.Core.Svg.MusicGlyph glyph) => LilySharp.Core.Svg.Layout.MusicFont.Current.Codepoint(glyph);

    /// <summary>The completion rows at <c>‸</c>, in the order the editor shows them.</summary>
    private static string[] Offered(string marked)
    {
        int offset = marked.IndexOf('‸');
        string text = marked.Remove(offset, 1);
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = DocUri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, offset);
        return [.. (server.Completion(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocUri },
            Position = new Position(line, character),
        })?.Items ?? [])
            .OrderBy(i => (i.SortText ?? i.Label!).ToLowerInvariant(), StringComparer.Ordinal)
            .Select(i => i.Label!)];
    }

    /// <summary>A guitar part with one section of <paramref name="music"/>, on the staves
    /// <paramref name="score"/> names.</summary>
    private static string Guitar(string music, string score = "staff gtr")
        => "octave absolute\ntime 4/4\npart gtr \"Guitar\" { instrument guitar }\n"
            + "section A { gtr { " + music + " } }\nform { A }\nscore { " + score + " }\n";

    // ───────────────────────── 1–3. @palmMute, @letRing, @barre(7) ─────────────────────────

    private const string ThreeLines =
        "e,8@palmMute e, e, e,@!palmMute e,8@letRing b, e g@!letRing | b,8@barre(7) fis b d'@!barre b8@barre(7 half) d' fis' b'@!barre |";

    /// <summary>Each line is its word, a dashed rule from the word to the centre of the note
    /// carrying the end, and a hook from the rule's end one staff space toward the staff.</summary>
    [Fact]
    public void AGuitarLine_IsItsWordADashedRuleAndAHook()
    {
        string source = Guitar(ThreeLines);
        Clean(source);
        RoundTrips(source);
        var g = RenderedGeometry.Render(source);
        foreach (string word in new[] { "P.M.", "let ring", "CVII", "½CVII" })
        {
            var label = Assert.Single(g.Texts, t => t.Text == word);
            Assert.Equal(LilySharp.Core.Rendering.TextRole.Text, label.Role);
            // The rule of this line: dashed, at the label's baseline, right of the label.
            var rule = Assert.Single(g.Lines, l => l.IsDashed && Math.Abs(l.Y1 - label.Y) < 1e-9 && l.X1 > label.X);
            // The hook: a solid stroke down from the rule's end, HookDepth long.
            var hook = Assert.Single(g.Lines, l => !l.IsDashed && Math.Abs(l.X1 - rule.X2) < 1e-9 && Math.Abs(l.X2 - rule.X2) < 1e-9
                && Math.Abs(l.Y1 - rule.Y1) < 1e-9);
            Assert.Equal(LilySharp.Core.Svg.Layout.TextSpannerEngraver.HookDepth, hook.Y2 - hook.Y1, 9);
            Assert.Equal(rule.StrokeWidth, hook.StrokeWidth, 9);
        }
        // Above the staff: every label over the top staff line.
        double top = g.Lines.Where(l => !l.IsDashed && l.X2 - l.X1 > 20).Min(l => l.Y1);
        Assert.All(g.Texts.Where(t => t.Text is "P.M." or "let ring" or "CVII" or "½CVII"), t => Assert.True(t.Y < top));
    }

    /// <summary>The lines of one voice run together: a palm mute under a barre, each closed by
    /// its own name, both drawn, the outer one clear of the inner — and a <c>rit.</c> beside them.</summary>
    [Fact]
    public void GuitarLines_RunUnderOneAnother()
    {
        string source = Guitar("e,8@palmMute@rit e,@barre(2) fis, fis,@!barre e,@!palmMute r8 a4@!rit |");
        Clean(source);
        var g = RenderedGeometry.Render(source);
        var palm = Assert.Single(g.Texts, t => t.Text == "P.M.");
        var barre = Assert.Single(g.Texts, t => t.Text == "CII");
        Assert.Single(g.Texts, t => t.Text == "rit.");
        Assert.True(Math.Abs(palm.Y - barre.Y) > 1.0, $"P.M. at {palm.Y}, CII at {barre.Y}");
        Assert.Equal(3, g.Lines.Count(l => l.IsDashed));
    }

    /// <summary>A barre with no <c>@!barre</c> after it is one note's: its label, no rule, no
    /// hook and nothing said — also when the next <c>@barre</c> comes before any end.</summary>
    [Fact]
    public void ABarreWithNoEnd_IsItsLabelAlone()
    {
        string source = Guitar("a4@barre(5) b4@barre(7) d'2 |");
        Clean(source);
        var g = RenderedGeometry.Render(source);
        Assert.Single(g.Texts, t => t.Text == "CV");
        Assert.Single(g.Texts, t => t.Text == "CVII");
        Assert.DoesNotContain(g.Lines, l => l.IsDashed);

        var plain = RenderedGeometry.Render(Guitar("a4 b4 d'2 |"));
        Assert.Equal(plain.Lines.Count, g.Lines.Count);
    }

    /// <summary>Beside its staff a tab repeats no line (it is numbers only); standing alone it
    /// carries them itself, above its strings.</summary>
    [Fact]
    public void GuitarLines_AreOnTheStaff_AndOnATabThatStandsAlone()
    {
        Assert.Single(RenderedGeometry.Render(Guitar(ThreeLines, "staff gtr  tab gtr")).Texts, t => t.Text == "P.M.");
        var alone = RenderedGeometry.Render(Guitar(ThreeLines, "tab gtr"));
        var label = Assert.Single(alone.Texts, t => t.Text == "P.M.");
        // (A line shorter than its word is the word and the hook: the tab's notes stand closer.)
        Assert.InRange(alone.Lines.Count(l => l.IsDashed), 2, 4);
        Assert.True(label.Y < alone.TabFrets.Min(f => f.Y));
    }

    /// <summary>The Roman numeral of every fret, and the half barre's ½.</summary>
    [Theory]
    [InlineData(1, false, "CI")]
    [InlineData(4, false, "CIV")]
    [InlineData(9, true, "½CIX")]
    [InlineData(12, false, "CXII")]
    [InlineData(19, false, "CXIX")]
    [InlineData(24, false, "CXXIV")]
    public void ABarre_PrintsItsFretInRomanNumerals(int fret, bool half, string printed)
    {
        Assert.Equal(printed, LilySharp.Core.Svg.Model.MusicMarkItem.BarreText(fret, half));
        string source = Guitar($"a4@barre({fret}{(half ? " half" : "")}) b4 d'2 |");
        Clean(source);
        Assert.Contains(RenderedGeometry.Render(source).Texts, t => t.Text == printed);
    }

    /// <summary>A barre that names none is an error that says what it lacks, and draws nothing.</summary>
    [Theory]
    [InlineData("@barre", "no fret")]
    [InlineData("@barre()", "no fret")]
    [InlineData("@barre(0)", "outside 1 to 24")]
    [InlineData("@barre(25)", "outside 1 to 24")]
    [InlineData("@barre(seven)", "'seven' is not a fret")]
    [InlineData("@barre(7 full)", "'full' is not a word of a barre")]
    public void ABarreThatNamesNone_IsAnError(string barre, string says)
    {
        string source = Guitar($"a4{barre} b4 d'2 |");
        var said = Assert.Single(Said(source));
        Assert.Equal(DiagnosticCodes.BarreMalformed, said.Code);
        Assert.Equal(DiagnosticSeverity.Error, said.Severity);
        Assert.Contains(says, said.Message);
        Assert.Contains("@barre(7 half)", said.Message);
        Assert.Equal(barre, source.Substring(said.Span.Start, said.Span.Length));
        Assert.DoesNotContain(RenderedGeometry.Render(source).Texts, t => t.Text.StartsWith('C') || t.Text.StartsWith('½'));
    }

    /// <summary>A palm mute or a let ring nobody ends is an error in its own words, an end with
    /// nothing open a warning, and an end of another name closes nothing of this one.</summary>
    [Fact]
    public void AGuitarLineWithNoEnd_AndAnEndWithNoLine_AreSaid()
    {
        var open = Assert.Single(Said(Guitar("e,4@palmMute e, e, e, |")));
        Assert.Equal((DiagnosticCodes.UnpairedSpan, DiagnosticSeverity.Error), (open.Code, open.Severity));
        Assert.Contains("a palm mute is never closed", open.Message);
        Assert.Contains("'@!palmMute'", open.Message);

        var ring = Assert.Single(Said(Guitar("e,4@letRing e, e, e, |")));
        Assert.Contains("a let ring is never closed", ring.Message);

        var stray = Assert.Single(Said(Guitar("e,4 e, e, e,@!barre |")));
        Assert.Equal((DiagnosticCodes.UnpairedSpan, DiagnosticSeverity.Warning), (stray.Code, stray.Severity));
        Assert.Contains("no barre is open", stray.Message);

        // '@!letRing' does not close the palm mute: two faults, neither a misnamed end (LYS4032).
        var crossed = Said(Guitar("e,4@palmMute e, e, e,@!letRing |"));
        Assert.Equal(2, crossed.Count);
        Assert.All(crossed, d => Assert.Equal(DiagnosticCodes.UnpairedSpan, d.Code));
        // …and interleaved with a rit., each end is its own line's.
        Clean(Guitar("e,4@palmMute e,@rit e,@!palmMute e,@!rit |"));
    }

    /// <summary>The sound: under a palm mute each note is half its length, both ends included;
    /// under a let ring each note sounds to the end of the line's last note, or to where its own
    /// pitch is struck again. The barre changes nothing.</summary>
    [Fact]
    public void APalmMuteIsPlayedShort_AndALetRingRingsOn()
    {
        var plain = Midi(Guitar("e,4 e, b, e |")).Tracks.SelectMany(t => t.Notes).OrderBy(n => n.StartTick).ToList();
        int quarter = plain[1].StartTick - plain[0].StartTick;
        Assert.All(plain, n => Assert.Equal(quarter, n.DurationTicks));

        var muted = Midi(Guitar("e,4@palmMute e, b,@!palmMute e |")).Tracks.SelectMany(t => t.Notes).OrderBy(n => n.StartTick).ToList();
        Assert.Equal([quarter / 2, quarter / 2, quarter / 2, quarter], muted.Select(n => n.DurationTicks));

        var ringing = Midi(Guitar("e,4@letRing e, b, e@!letRing | e,1 |")).Tracks.SelectMany(t => t.Notes).OrderBy(n => n.StartTick).ToList();
        // The first e, rings to the second e, (its own pitch again); the rest to the bar's end.
        Assert.Equal([quarter, 3 * quarter, 2 * quarter, quarter, 4 * quarter], ringing.Select(n => n.DurationTicks));

        var barred = Midi(Guitar("e,4@barre(2) e, b, e@!barre |")).Tracks.SelectMany(t => t.Notes).OrderBy(n => n.StartTick).ToList();
        Assert.Equal(plain.Select(n => (n.StartTick, n.DurationTicks)), barred.Select(n => (n.StartTick, n.DurationTicks)));
    }

    /// <summary>MusicXML has no element for the three; each is its word and a dashed bracket
    /// whose stop turns down, numbered so they run together, and a one-note barre its word alone.</summary>
    [Fact]
    public void GuitarLines_GoToMusicXmlAsWordsAndADashedBracket()
    {
        string xml = Xml(Guitar(ThreeLines + " a4@barre(5) r4 r2 |"));
        foreach (var (word, number) in new[] { ("P.M.", 1), ("let ring", 2), ("CVII", 3), ("½CVII", 3) })
            Assert.Contains($"<direction-type><words font-style=\"italic\">{word}</words></direction-type>"
                + $"<direction-type><bracket type=\"start\" number=\"{number}\" line-end=\"none\" line-type=\"dashed\" /></direction-type>", xml);
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(xml, "<bracket type=\"stop\" number=\"\\d\" line-end=\"down\"").Count);
        Assert.Contains("<words font-style=\"italic\">CV</words></direction-type></direction>", xml);
    }

    /// <summary>The LilyPond twin writes each as LilyPond's users do: a text spanner whose left
    /// text is the word and whose right text is the hook; a one-note barre is a markup.</summary>
    [Fact]
    public void GuitarLines_GoToLilyPondAsTextSpannersWithAHook()
    {
        string ly = Ly(Guitar(ThreeLines + " a4@barre(5) r4 r2 |"));
        const string hook = " \\once \\override TextSpanner.bound-details.right.text = \\markup { \\draw-line #'(0 . -1) }";
        foreach (string word in new[] { "P.M.", "let ring", "CVII", "½CVII" })
            Assert.Contains($"\\once \\override TextSpanner.bound-details.left.text = \"{word}\"{hook}", ly);
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(ly, @"\\startTextSpan").Count);
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(ly, @"\\stopTextSpan").Count);
        Assert.Contains("a4^\\markup { \\italic \"CV\" }", ly);
        // A rit. keeps its plain line.
        Assert.DoesNotContain("draw-line", Ly(Guitar("e,4@rit e, e, e,@!rit |")));
    }

    /// <summary>While a line is open an <c>@</c> offers its end first — the innermost first when
    /// one runs inside another — and <c>@!</c> lists the open ones before every other name.</summary>
    [Fact]
    public void WhileALineIsOpen_ItsEndIsOfferedFirst()
    {
        var at = Offered(Guitar("b,8@barre(7) fis@palmMute b d'@‸ |"));
        Assert.Equal(["!palmMute", "!barre", "!"], at.Take(3));
        var ends = Offered(Guitar("b,8@barre(7) fis@palmMute b d'@!‸ |"));
        Assert.Equal(["palmMute", "barre"], ends.Take(2));
        // Closed, it is offered no more; the text spanner and the pedal are offered the same way.
        Assert.Equal("!", Offered(Guitar("b,8@palmMute fis@!palmMute b d'@‸ |"))[0]);
        Assert.Equal(["!sustain", "!textSpan", "!"], Offered(Guitar("b,8@textSpan(\"sul pont.\") fis@sustain b d'@‸ |")).Take(3));
        // The three names are in the list, and the end's list.
        var names = Offered(Guitar("b,8@‸ |"));
        Assert.Contains("palmMute", names);
        Assert.Contains("letRing", names);
        Assert.Contains("barre", names);
        Assert.Contains("letRing", Offered(Guitar("b,8@!‸ |")));
    }

    /// <summary>Inside <c>@barre(</c> the frets in order, then <c>half</c>.</summary>
    [Fact]
    public void InsideABarresParentheses_TheFretsAndHalfAreOffered()
    {
        var offered = Offered(Guitar("b,8@barre(‸) |"));
        Assert.Equal(["1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "half"], offered);
    }

    // ───────────────────────── 4. c4@vibrato on a fretted instrument ─────────────────────────

    private static string TwoParts(string guitar, string flute, string score = "staff gtr  staff fl")
        => "octave absolute\ntime 4/4\npart gtr \"Guitar\" { instrument guitar }\npart fl \"Flute\" { instrument flute }\n"
            + "section A { gtr { " + guitar + " } fl { " + flute + " } }\nform { A }\nscore { " + score + " }\n";

    /// <summary>On a guitar's note <c>@vibrato</c> is the wavy line above the note, from the
    /// note to the next one, with no "tr" — on the staff and on the tab — and the sound is the
    /// plain note's.</summary>
    [Fact]
    public void AFrettedInstrumentsVibrato_IsAWaveOverItsNote()
    {
        string source = Guitar("e'2@vibrato b4 g4 |");
        Clean(source);
        RoundTrips(source);
        char wave = GlyphOf(LilySharp.Core.Svg.MusicGlyph.WiggleTrill);
        char tr = GlyphOf(LilySharp.Core.Svg.MusicGlyph.OrnamentTrill);
        var g = RenderedGeometry.Render(source);
        var waves = g.Glyphs.Where(x => x.Glyph == wave).OrderBy(x => x.X).ToList();
        Assert.NotEmpty(waves);
        Assert.DoesNotContain(g.Glyphs, x => x.Glyph == tr);
        var heads = g.Noteheads.OrderBy(h => h.X).ToList();
        // Over the staff, beginning at its note and ending before the next.
        double top = g.Lines.Where(l => !l.IsDashed && l.X2 - l.X1 > 10).Min(l => l.Y1);
        Assert.All(waves, w => Assert.True(w.Y < top));
        Assert.InRange(waves[0].X, heads[0].X - 0.5, heads[0].X + 1.5);
        Assert.True(waves[^1].X < heads[1].X);
        Assert.DoesNotContain(RenderedGeometry.Render(Guitar("e'2 b4 g4 |")).Glyphs, x => x.Glyph == wave);

        // The tab draws it too, beside the staff and alone.
        Assert.Equal(2 * waves.Count, RenderedGeometry.Render(Guitar("e'2@vibrato b4 g4 |", "staff gtr  tab gtr")).Glyphs.Count(x => x.Glyph == wave));
        Assert.NotEmpty(RenderedGeometry.Render(Guitar("e'2@vibrato b4 g4 |", "tab gtr")).Glyphs.Where(x => x.Glyph == wave));

        static IEnumerable<(int, int, int)> Notes(LilySharp.Core.Midi.MidiFile midi)
            => midi.Tracks.SelectMany(t => t.Notes).Select(n => (n.Pitch, n.StartTick, n.DurationTicks));
        Assert.Equal(Notes(Midi(Guitar("e'2 b4 g4 |"))), Notes(Midi(source)));
    }

    /// <summary>On any other instrument it is the singer's mark still, and said (LYS1008); a
    /// sung part's takes its numbers; a guitar's with numbers is an error.</summary>
    [Fact]
    public void Vibrato_IsTheSingersMarkEverywhereElse()
    {
        var onFlute = Assert.Single(Said(TwoParts("e'1 |", "c''1@vibrato |")));
        Assert.Equal(DiagnosticCodes.UnknownAnnotation, onFlute.Code);
        Assert.Contains("singer's mark", onFlute.Message);
        char wave = GlyphOf(LilySharp.Core.Svg.MusicGlyph.WiggleTrill);
        Assert.DoesNotContain(RenderedGeometry.Render(TwoParts("e'1 |", "c''1@vibrato |")).Glyphs, x => x.Glyph == wave);

        var numbered = Assert.Single(Said(Guitar("e'1@vibrato(60, 6) |")));
        Assert.Equal((DiagnosticCodes.VibratoNumbersOnAnInstrument, DiagnosticSeverity.Error), (numbered.Code, numbered.Severity));
        Assert.Contains("write '@vibrato' alone", numbered.Message);
        Assert.DoesNotContain(RenderedGeometry.Render(Guitar("e'1@vibrato(60, 6) |")).Glyphs, x => x.Glyph == wave);

        // A bass and a ukulele are fretted too.
        foreach (string instrument in new[] { "bass", "ukulele" })
            Clean(Guitar("e'1@vibrato |").Replace("instrument guitar", "instrument " + instrument).Replace("e'1", instrument == "bass" ? "e,1" : "e'1"));
    }

    /// <summary>The wave goes to MusicXML as a wavy-line started and stopped on its note, and to
    /// LilyPond as a trill spanner with no "tr", stopped where the note ends.</summary>
    [Fact]
    public void AFrettedInstrumentsVibrato_GoesToTheExports()
    {
        string source = Guitar("e'2@vibrato b4 g4 |");
        Assert.Contains("<ornaments><wavy-line type=\"start\" number=\"1\" /><wavy-line type=\"stop\" number=\"1\" /></ornaments>", Xml(source));
        Assert.Contains("\\once \\override TrillSpanner.bound-details.left.text = ##f e'2\\startTrillSpan <>\\stopTrillSpan", Ly(source));
        Assert.DoesNotContain("wavy-line", Xml(TwoParts("e'1 |", "c''1@vibrato |")));
    }

    // ───────────────────────── 5. a'4@bend(full)@release ─────────────────────────

    /// <summary>The arrowheads of a page: each a V of two strokes of the head's width meeting at
    /// its tip, up (the bend) or down (the release).</summary>
    private static List<(double X, double Y, bool Down)> ArrowHeads(RenderedGeometry g)
    {
        var heads = new List<(double, double, bool)>();
        var strokes = g.Lines.Where(l => Math.Abs(l.StrokeWidth - 0.16) < 1e-9).ToList();
        for (int i = 0; i + 1 < strokes.Count; i += 2)
            // The tip is the point the two strokes share; in device Y an up arrow's tip is above its feet.
            heads.Add((strokes[i].X2, strokes[i].Y2, strokes[i].Y1 < strokes[i].Y2));
        return heads;
    }

    /// <summary>On the bent note the release is a second arrow, down, a bend's reach right of the
    /// first and back at the note's height; the amount stays over the arrow up.</summary>
    [Fact]
    public void AReleaseOnTheBentNote_IsUpAndBackWithinTheNote()
    {
        string source = Guitar("a'4@bend(full)@release r4 r2 |");
        Clean(source);
        RoundTrips(source);
        var g = RenderedGeometry.Render(source);
        var bent = ArrowHeads(RenderedGeometry.Render(Guitar("a'4@bend(full) r4 r2 |")));
        var up = Assert.Single(bent);
        Assert.False(up.Down);

        var heads = ArrowHeads(g);
        Assert.Equal(2, heads.Count);
        Assert.Equal((up.X, up.Y, false), heads[0]);
        Assert.True(heads[1].Down);
        Assert.Equal(LilySharp.Core.Svg.Layout.ArticulationEngraver.GuitarBendReach, heads[1].X - heads[0].X, 9);
        Assert.Equal(LilySharp.Core.Svg.Layout.ArticulationEngraver.GuitarBendRise, heads[1].Y - heads[0].Y, 9);
        Assert.Single(g.Texts, t => t.Text == "full");
        // The tab draws the same pair off its fret.
        Assert.Equal(4, ArrowHeads(RenderedGeometry.Render(Guitar("a'4@bend(full)@release r4 r2 |", "staff gtr  tab gtr"))).Count);
    }

    /// <summary>On the note after a bent one the release is the arrow down to that note, its
    /// curve leaving the top of the bend's arrow; the bar makes room for the run.</summary>
    [Fact]
    public void AReleaseOnTheNextNote_ComesDownFromTheBendsArrow()
    {
        string source = Guitar("a'4@bend(full) a'4@release r2 |");
        Clean(source);
        var g = RenderedGeometry.Render(source);
        var heads = ArrowHeads(g);
        Assert.Equal(2, heads.Count);
        var (up, down) = (heads[0], heads[1]);
        Assert.False(up.Down);
        Assert.True(down.Down);
        // The arrow's tip stands at the second note's left, at the note's height.
        var notes = g.Noteheads.OrderBy(h => h.X).ToList();
        Assert.InRange(down.X, notes[0].X + 2, notes[1].X);
        Assert.Equal(LilySharp.Core.Svg.Layout.ArticulationEngraver.GuitarBendRise, down.Y - up.Y, 9);
        // Its curve starts at the up arrow's tip.
        Assert.Contains(g.Lines, l => Math.Abs(l.StrokeWidth - 0.13) < 1e-9 && Math.Abs(l.X1 - up.X) < 1e-9 && Math.Abs(l.Y1 - up.Y) < 1e-9 && l.X2 > up.X);
        // Room: the two notes stand further apart than with the bend alone.
        var alone = RenderedGeometry.Render(Guitar("a'4@bend(full) a'4 r2 |")).Noteheads.OrderBy(h => h.X).ToList();
        Assert.True(notes[1].X - notes[0].X > alone[1].X - alone[0].X + 1.0);
        // Over the bar line too.
        Assert.Equal(2, ArrowHeads(RenderedGeometry.Render(Guitar("r2 r4 a'4@bend(full) | a'4@release r4 r2 |"))).Count);
    }

    /// <summary>A release with no bend on its note or on the note before it is an error that
    /// says so; a rest between breaks the pair.</summary>
    [Theory]
    [InlineData("a'4@release r4 r2 |")]
    [InlineData("a'4@bend(full) g'4 a'4@release r4 |")]
    [InlineData("a'4@bend(full) r4 a'4@release r4 |")]
    public void AReleaseWithNoBend_IsAnError(string music)
    {
        var error = Assert.Single(Said(Guitar(music)));
        Assert.Equal((DiagnosticCodes.ReleaseWithoutBend, DiagnosticSeverity.Error), (error.Code, error.Severity));
        Assert.Contains("lets back no bend", error.Message);
        Assert.Contains("a'4@bend(full)@release", error.Message);
        Assert.DoesNotContain(ArrowHeads(RenderedGeometry.Render(Guitar(music))), h => h.Down);
    }

    /// <summary>A bend is silent in the MIDI, and so is its release; MusicXML takes the bend and
    /// the release as its bend elements, up and then back.</summary>
    [Fact]
    public void ARelease_IsSilent_AndGoesToMusicXml()
    {
        static IEnumerable<(int, int, int)> Notes(LilySharp.Core.Midi.MidiFile midi)
            => midi.Tracks.SelectMany(t => t.Notes).Select(n => (n.Pitch, n.StartTick, n.DurationTicks));
        Assert.Equal(Notes(Midi(Guitar("a'4 a'4 r2 |"))), Notes(Midi(Guitar("a'4@bend(full)@release a'4 r2 |"))));
        Assert.Equal(Notes(Midi(Guitar("a'4 a'4 r2 |"))), Notes(Midi(Guitar("a'4@bend(full) a'4@release r2 |"))));

        Assert.Contains("<bend><bend-alter>2</bend-alter></bend><bend><bend-alter>-2</bend-alter><release /></bend>",
            Xml(Guitar("a'4@bend(full)@release a'4 r2 |")));
        Assert.Contains("<bend><bend-alter>2</bend-alter></bend><bend><bend-alter>-2</bend-alter><release /></bend>",
            Xml(Guitar("a'4@release@bend(full) a'4 r2 |")));
        string next = Xml(Guitar("a'4@bend(half) a'4@release r2 |"));
        Assert.Contains("<bend><bend-alter>1</bend-alter></bend>", next);
        Assert.Contains("<bend><bend-alter>-1</bend-alter><release /></bend>", next);
    }

    /// <summary>After the <c>@</c> of a bent note, and of the note after one, <c>release</c> is
    /// the first row; elsewhere it stands in its alphabetical place.</summary>
    [Fact]
    public void AfterABend_ReleaseIsOfferedFirst()
    {
        Assert.Equal("release", Offered(Guitar("a'4@bend(full)@‸ r4 r2 |"))[0]);
        Assert.Equal("release", Offered(Guitar("a'4@bend(full) a'4@‸ r2 |"))[0]);
        Assert.Equal("release", Offered(Guitar("r2 r4 a'4@bend(half) | a'4@‸ r4 r2 |"))[0]);
        Assert.Equal("!", Offered(Guitar("a'4@bend(full)@release@‸ r4 r2 |"))[0]);
        Assert.Equal("!", Offered(Guitar("a'4@bend(full) g'4 a'4@‸ r4 |"))[0]);
        Assert.Contains("release", Offered(Guitar("a'4@‸ r4 r2 |")));
        // …and before the end of a line open there.
        Assert.Equal(["release", "!letRing", "!"], Offered(Guitar("a'4@letRing@bend(full) a'4@‸ r2 |")).Take(3));
        Assert.Equal(["half", "full"], Offered(Guitar("a'4@bend(‸) r4 r2 |")).Take(2));
    }
}
