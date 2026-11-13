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

    // ───────────────────────── 6. <c e g>2@arpeggio(up) / @arpeggio(down) ─────────────────────────

    private const string GrandStaff = "grandStaff { staff rh  staff lh }";

    /// <summary>A right hand and a left hand in a grand staff, one bar each.</summary>
    private static string Piano(string rh, string lh, string score = GrandStaff)
        => "octave absolute\ntime 4/4\npart rh { clef treble }\npart lh { clef bass }\n"
           + $"section A {{\n  rh {{ {rh} }}\n  lh {{ {lh} }}\n}}\nform {{ A }}\nscore {{ {score} }}\n";

    private static LilySharp.Core.Svg.Model.MultiStaffScore Collected(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return LilySharp.Core.Svg.SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
    }

    /// <summary>The glyphs of the page's arpeggios, bottom to top, as (glyph, x, y).</summary>
    private static List<(char Glyph, double X, double Y)> ArpeggioGlyphs(RenderedGeometry g)
    {
        char wiggle = GlyphOf(LilySharp.Core.Svg.MusicGlyph.WiggleArpeggiatoUp);
        char up = GlyphOf(LilySharp.Core.Svg.MusicGlyph.WiggleArpeggiatoUpArrow);
        char down = GlyphOf(LilySharp.Core.Svg.MusicGlyph.WiggleArpeggiatoDownArrow);
        return [.. g.Glyphs.Where(x => x.Glyph == wiggle || x.Glyph == up || x.Glyph == down)
            .OrderBy(x => x.X).ThenByDescending(x => x.Y).Select(x => (x.Glyph, x.X, x.Y))];
    }

    /// <summary>An arrowed arpeggio is the plain one's pile with its end copy an arrow head —
    /// the top one for <c>up</c>, the bottom one for <c>down</c> — at the same places, and the
    /// bar is spaced as the plain one's (LilyPond 2.26's \arpeggioArrowUp / \arpeggioArrowDown:
    /// the arrow glyph has the wiggle's box).</summary>
    [Theory]
    [InlineData("<c' e' g' c''>2")]
    [InlineData("<c' e'>2")]
    [InlineData("g'2")]
    public void AnArrowedArpeggio_IsThePlainPileEndingInAnArrowHead(string chord)
    {
        char wiggle = GlyphOf(LilySharp.Core.Svg.MusicGlyph.WiggleArpeggiatoUp);
        char upArrow = GlyphOf(LilySharp.Core.Svg.MusicGlyph.WiggleArpeggiatoUpArrow);
        char downArrow = GlyphOf(LilySharp.Core.Svg.MusicGlyph.WiggleArpeggiatoDownArrow);
        var plainPage = RenderedGeometry.Render(Guitar($"r2 {chord}@arpeggio |"));
        var plain = ArpeggioGlyphs(plainPage);
        Assert.True(plain.Count >= 2);
        Assert.All(plain, p => Assert.Equal(wiggle, p.Glyph));

        foreach (var (word, arrow, at) in new[] { ("up", upArrow, plain.Count - 1), ("down", downArrow, 0) })
        {
            string source = Guitar($"r2 {chord}@arpeggio({word}) |");
            Clean(source);
            RoundTrips(source);
            var page = RenderedGeometry.Render(source);
            var drawn = ArpeggioGlyphs(page);
            Assert.Equal(plain.Count, drawn.Count);
            for (int i = 0; i < plain.Count; i++)
            {
                Assert.Equal(i == at ? arrow : wiggle, drawn[i].Glyph);
                Assert.Equal(plain[i].X, drawn[i].X, 9);
                Assert.Equal(plain[i].Y, drawn[i].Y, 9);
            }
            // The room is the plain arpeggio's: every head stands where it stood.
            Assert.Equal(plainPage.Noteheads.Select(h => (h.X, h.Y)), page.Noteheads.Select(h => (h.X, h.Y)));
        }
    }

    /// <summary>The model says which: +1 up, −1 down, 0 for the plain line and for a bracket.</summary>
    [Fact]
    public void TheArrowOfAnArpeggio_IsReadByTheOneReader()
    {
        string source = Guitar("<c' e'>4@arpeggio(up) <c' e'>4@arpeggio(down) <c' e'>4@arpeggio <c' e'>4@arpeggio(bracket) |");
        Clean(source);
        Assert.Equal([(1, false), (-1, false), (0, false), (0, true)], Collected(source).Arpeggios.Select(a => (a.Arrow, a.Bracket)));
        var marks = SyntaxTree.Parse(source).GetRoot().DescendantNodes().OfType<MusicMarkSyntax>().ToList();
        Assert.Equal([1, -1, 0], marks.Select(AnnotationValues.ArpeggioArrow));
        Assert.Equal((false, false, 1), AnnotationValues.ArpeggioWords(marks[0]));
    }

    /// <summary>On a tab the arrowed arpeggio is drawn as the plain one is: beside the staff
    /// both carry it, and a tab alone carries it.</summary>
    [Fact]
    public void AnArrowedArpeggio_IsOnTheTabToo()
    {
        char upArrow = GlyphOf(LilySharp.Core.Svg.MusicGlyph.WiggleArpeggiatoUpArrow);
        string music = "<e a d' g'>2@arpeggio(up) r2 |";
        int plainBoth = ArpeggioGlyphs(RenderedGeometry.Render(Guitar(music.Replace("(up)", ""), "staff gtr  tab gtr"))).Count;
        var both = ArpeggioGlyphs(RenderedGeometry.Render(Guitar(music, "staff gtr  tab gtr")));
        Assert.Equal(plainBoth, both.Count);
        Assert.Equal(2, both.Count(x => x.Glyph == upArrow));
        Assert.Single(ArpeggioGlyphs(RenderedGeometry.Render(Guitar(music, "tab gtr"))), x => x.Glyph == upArrow);
    }

    /// <summary><c>(cross up)</c> / <c>(cross down)</c>: the one line through both staves ends
    /// in the arrow — at the upper chord's top, or below the lower chord — and is as long as
    /// the plain cross arpeggio.</summary>
    [Fact]
    public void ACrossArpeggio_TakesAnArrowToo()
    {
        char upArrow = GlyphOf(LilySharp.Core.Svg.MusicGlyph.WiggleArpeggiatoUpArrow);
        char downArrow = GlyphOf(LilySharp.Core.Svg.MusicGlyph.WiggleArpeggiatoDownArrow);
        var plain = ArpeggioGlyphs(RenderedGeometry.Render(Piano("<c' e' g'>1@arpeggio(cross) |", "<c e g>1 |")));
        foreach (var (words, arrow, at, sign) in new[] { ("cross up", upArrow, plain.Count - 1, 1), ("up cross", upArrow, plain.Count - 1, 1), ("cross down", downArrow, 0, -1) })
        {
            string source = Piano($"<c' e' g'>1@arpeggio({words}) |", "<c e g>1 |");
            Clean(source);
            var line = Assert.Single(Collected(source).Arpeggios);
            Assert.Equal((true, sign, 1), (line.Cross, line.Arrow, line.PartnerStaffIndex));
            var drawn = ArpeggioGlyphs(RenderedGeometry.Render(source));
            Assert.Equal(plain.Select(p => (p.X, p.Y)), drawn.Select(p => (p.X, p.Y)));
            Assert.Equal(arrow, drawn[at].Glyph);
            Assert.Single(drawn, x => x.Glyph == arrow);
        }
        // With nothing below it is drawn on its own chord, arrow and all, and said (LYS4064).
        string alone = Guitar("<c' e' g'>1@arpeggio(cross up) |");
        var warned = Assert.Single(Said(alone));
        Assert.Equal(DiagnosticCodes.CrossArpeggioWithoutPartner, warned.Code);
        Assert.Contains("or write '@arpeggio(up)'", warned.Message);
        Assert.Single(ArpeggioGlyphs(RenderedGeometry.Render(alone)), x => x.Glyph == upArrow);
    }

    /// <summary>A bracket is not rolled and takes no direction; both directions, or one twice,
    /// say nothing either. The mark is reported with what the parentheses take, and ignored.</summary>
    [Theory]
    [InlineData("bracket up")]
    [InlineData("down bracket")]
    [InlineData("up down")]
    [InlineData("up up")]
    [InlineData("Up")]
    public void AnArrowOnABracket_OrTwoArrows_IsNotAValue(string words)
    {
        string source = Guitar($"<c' e' g'>1@arpeggio({words}) |");
        var said = Assert.Single(Said(source));
        Assert.Equal(DiagnosticCodes.UnknownAnnotation, said.Code);
        if (words != "Up")
        {
            Assert.Contains($"'{words}' is not a value of '@arpeggio'", said.Message);
            Assert.Contains("'@arpeggio(up)', '@arpeggio(down)'", said.Message);
            Assert.Contains("A bracket takes no direction", said.Message);
        }
        RoundTrips(source);
        Assert.Empty(Collected(source).Arpeggios);
    }

    /// <summary>The exports: MusicXML says the arrow as the direction of every arpeggiate of
    /// the line — the staff below's too — and the LilyPond twin sets the grob's
    /// arpeggio-direction for that chord alone, in the grand staff for a connected line. The
    /// sound is the plain chord's (an <c>@arpeggio</c> is not rolled in the MIDI).</summary>
    [Fact]
    public void AnArrowedArpeggio_GoesToTheExports()
    {
        string source = Guitar("<c' e' g'>2@arpeggio(up) <c' e'>4@arpeggio(down) g'4@arpeggio(up) |");
        string xml = Xml(source);
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(xml, "<arpeggiate number=\"1\" direction=\"up\" />").Count);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(xml, "<arpeggiate number=\"1\" direction=\"down\" />").Count);
        string ly = Ly(source);
        Assert.Contains("\\once \\override Arpeggio.arpeggio-direction = #UP <c' e' g'>2\\arpeggio", ly);
        Assert.Contains("\\once \\override Arpeggio.arpeggio-direction = #DOWN <c' e'>4\\arpeggio", ly);
        Assert.Contains("\\once \\override Arpeggio.arpeggio-direction = #UP g'4\\arpeggio", ly);
        Assert.DoesNotContain("arpeggio-direction", Ly(Guitar("<c' e' g'>2@arpeggio <c' e'>2@arpeggio(bracket) |")));

        string crossed = Piano("<c' e' g'>2@arpeggio(cross down) <c' e' g'>2@arpeggio(cross up) |", "<c e g>2 <c g>2 |");
        var tree = SyntaxTree.Parse(crossed);
        string crossedXml = new LilySharp.Core.MusicXml.MusicXmlExporter { Score = RenderSpecParser.FindFirst(tree) }
            .Export(tree).ToXml().ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
        Assert.Equal(6, System.Text.RegularExpressions.Regex.Matches(crossedXml, "<arpeggiate number=\"2\" direction=\"down\" />").Count);
        Assert.Equal(5, System.Text.RegularExpressions.Regex.Matches(crossedXml, "<arpeggiate number=\"2\" direction=\"up\" />").Count);
        Assert.Contains("\\once \\set GrandStaff.connectArpeggios = ##t \\once \\override GrandStaff.Arpeggio.arpeggio-direction = #DOWN <c' e' g'>2\\arpeggio", Ly(crossed));

        static IEnumerable<(int, int, int)> Notes(LilySharp.Core.Midi.MidiFile midi)
            => midi.Tracks.SelectMany(t => t.Notes).Select(n => (n.Pitch, n.StartTick, n.DurationTicks));
        Assert.Equal(Notes(Midi(Guitar("<c' e' g'>2@arpeggio <c' e'>4@arpeggio g'4@arpeggio |"))), Notes(Midi(source)));
    }

    /// <summary>MusicXML's direction comes back as the word, on one staff and through two.</summary>
    [Fact]
    public void AnArrowedArpeggio_ComesBackFromMusicXml()
    {
        string source = Piano("<c' e' g'>2@arpeggio(up) <d' f'>2@arpeggio(cross down) | g'2@arpeggio(down) <d' f'>2@arpeggio |", "<c e g>2 <d f>2 | <c g>1 |");
        Clean(source);
        var tree = SyntaxTree.Parse(source);
        string xml = new LilySharp.Core.MusicXml.MusicXmlExporter { Score = RenderSpecParser.FindFirst(tree) }
            .Export(tree).ToXml().ToString();
        var (lys, _) = new LilySharp.Core.MusicXmlImport.MusicXmlImporter().Import(xml);
        Assert.Contains("@arpeggio(up) <d' f'>2@arpeggio(cross down) | g'2@arpeggio(down) <d' f'>2@arpeggio |", lys);
        Assert.Contains("<c e g>2 <d f>2 | <c g>1 |", lys);
        Clean(lys);
        var back = SyntaxTree.Parse(lys);
        Assert.Equal(Collected(source).Arpeggios.Select(a => (a.Cross, a.Arrow, a.PartnerStaffIndex)),
            LilySharp.Core.Svg.SvgGenerator.CollectScore(back, RenderSpecParser.FindFirst(back)).Arpeggios.Select(a => (a.Cross, a.Arrow, a.PartnerStaffIndex)));
    }

    /// <summary>Inside an arpeggio's parentheses: up, down, bracket, cross — in that order; and
    /// the '@' list has the two arrowed rows.</summary>
    [Fact]
    public void InsideAnArpeggiosParentheses_UpAndDownAreOfferedFirst()
    {
        Assert.Equal(["up", "down", "bracket", "cross"], Offered(Guitar("<c' e' g'>2@arpeggio(‸) r2 |")));
        var marks = Offered(Guitar("<c' e' g'>2@‸ r2 |"));
        Assert.Contains("arpeggio(up)", marks);
        Assert.Contains("arpeggio(down)", marks);
    }

    // ───────────────────────── 7. tuning "d, a, d g a d'" ─────────────────────────

    /// <summary>One part of <paramref name="header"/> playing <paramref name="music"/>.</summary>
    private static string Strung(string header, string music, string score = "staff gtr  tab gtr")
        => "octave absolute\ntime 4/4\npart gtr { " + header + " }\n"
            + "section A { gtr { " + music + " } }\nform { A }\nscore { " + score + " }\n";

    private static List<(string Text, double X, double Y)> Frets(string source)
        => [.. RenderedGeometry.Render(source).TabFrets.Select(f => (f.Text, f.X, f.Y))];

    /// <summary>A tuning written out — the open strings from the highest string number to
    /// string 1, as they sound — is the tuning of those strings: the page is the page of the
    /// tuning word that names the same strings, to the last fret.</summary>
    [Theory]
    [InlineData("instrument guitar  tuning \"d, a, d g a d'\"", "instrument guitar  tuning guitarDadgad", "d,4 a, d g | a d' <d, a, d g a d'>2 |")]
    [InlineData("tuning \"g' c' e' a'\"", "tuning ukulele", "g'4 c' e' a' | <g' c' e' a'>2 c''4 d'' |")]
    [InlineData("instrument bass  tuning \"d,, a,, d, g,\"", "instrument bass  tuning bassDropD", "d,4 a, d g | d2 a, |")]
    [InlineData("tuning \"e, a, d g b e'\"", "tuning guitar", "e,4 a, d g | b e' g'2 |")]
    public void ATuningWrittenOut_IsTheTuningOfThoseStrings(string written, string named, string music)
    {
        string source = Strung(written, music);
        Clean(source);
        RoundTrips(source);
        Assert.Equal(Frets(Strung(named, music)), Frets(source));
        Assert.NotEmpty(Frets(source));

        var tab = Collected(source).StaffGroups.SelectMany(g => g.Staves).Single(s => s.IsTab);
        var namedTab = Collected(Strung(named, music)).StaffGroups.SelectMany(g => g.Staves).Single(s => s.IsTab);
        Assert.Equal(LilySharp.Core.Tablature.Tunings.GetTuning(namedTab.Tuning!.Value), LilySharp.Core.Tablature.Tunings.GetTuning(tab.Tuning!.Value));
        Assert.Equal(LilySharp.Core.Tablature.Tunings.IsBass(namedTab.Tuning!.Value), LilySharp.Core.Tablature.Tunings.IsBass(tab.Tuning!.Value));
        // A list that spells a named tuning's strings is still its own handle: a custom tuning.
        Assert.True(LilySharp.Core.Tablature.Tunings.IsCustom(tab.Tuning!.Value));
        Assert.False(LilySharp.Core.Tablature.Tunings.IsCustom(namedTab.Tuning!.Value));
    }

    /// <summary>The same strings are the same tuning wherever they are written, and the list
    /// reads back as it was spelled.</summary>
    [Fact]
    public void ACustomTuning_IsOneHandleForItsStrings()
    {
        var a = LilySharp.Core.Tablature.Tunings.Parse("\"d, a, d g a d'\"");
        Assert.Equal(a, LilySharp.Core.Tablature.Tunings.Parse("d,  a, d g a d'"));          // a property's value: unquoted
        Assert.Equal(a, LilySharp.Core.Tablature.Tunings.Custom([38, 45, 50, 55, 57, 62]));
        Assert.NotEqual(a, LilySharp.Core.Tablature.Tunings.Parse("\"d, a, d g b e'\""));
        Assert.Equal([38, 45, 50, 55, 57, 62], LilySharp.Core.Tablature.Tunings.GetTuning(a));
        Assert.Equal("\"d, a, d g a d'\"", LilySharp.Core.Tablature.Tunings.Spell(a));
        Assert.Equal("guitarDropD", LilySharp.Core.Tablature.Tunings.Spell(TuningType.GuitarDropD));
        Assert.Equal("\\stringTuning <d, a, d g a d'>", LilySharp.Core.Tablature.Tunings.LilyPondValue(a));
        Assert.Equal("#guitar-drop-d-tuning", LilySharp.Core.Tablature.Tunings.LilyPondValue(TuningType.GuitarDropD));
        // Flats and sharps; a list that spells none is the guitar, as an unknown word is.
        Assert.Equal([39, 44, 49, 54, 58, 63],
            LilySharp.Core.Tablature.Tunings.GetTuning(LilySharp.Core.Tablature.Tunings.Parse("\"ees, aes, des ges bes ees'\"")));
        Assert.Equal([39, 44, 49, 54, 58, 63],
            LilySharp.Core.Tablature.Tunings.GetTuning(LilySharp.Core.Tablature.Tunings.Parse("\"dis, gis, cis fis ais dis'\"")));
        Assert.Equal(TuningType.Guitar, LilySharp.Core.Tablature.Tunings.Parse("\"d, a, x\""));
        Assert.DoesNotContain(a, Enum.GetValues<TuningType>());
    }

    /// <summary>On a tab item the list stands where the tuning word stands, before the part:
    /// that tab is fretted on it, the part's own tab and range are not.</summary>
    [Fact]
    public void OnATabItem_TheListStandsWhereTheTuningWordStands()
    {
        const string music = "e,4 a, d g | a d' r2 |";
        string source = Strung("instrument guitar", music, "tab gtr  tab \"d, a, d g a d'\" gtr");
        Clean(source);
        RoundTrips(source);
        Assert.Equal(Frets(Strung("instrument guitar", music, "tab gtr  tab guitarDadgad gtr")), Frets(source));
        var tabs = Collected(source).StaffGroups.SelectMany(g => g.Staves).Where(s => s.IsTab).ToList();
        Assert.Equal(TuningType.Guitar, tabs[0].Tuning);
        Assert.Equal([38, 45, 50, 55, 57, 62], LilySharp.Core.Tablature.Tunings.GetTuning(tabs[1].Tuning!.Value));
        var item = SyntaxTree.Parse(source).GetRoot().DescendantNodes<TabRenderSyntax>().Last();
        Assert.Equal("\"d, a, d g a d'\"", item.TuningToken!.Text);
        Assert.Equal("gtr", item.PartToken!.Text);
    }

    /// <summary>Four to eight strings: the tab has a line for each, the string numbers run to
    /// the count (LYS5003 beyond it), and a note under the lowest string is LYS5002.</summary>
    [Theory]
    [InlineData("\"g, d a e'\"", 4, "g,4 d a e' |")]
    [InlineData("\"b,, e, a, d g b e'\"", 7, "b,,4 e, a, d |")]
    [InlineData("\"fis,, b,, e, a, d g b e'\"", 8, "fis,,4 b,, e, e' |")]
    public void ACustomTuning_HasFourToEightStrings(string list, int strings, string music)
    {
        string source = Strung("tuning " + list, music, "tab gtr");
        Clean(source);
        var page = RenderedGeometry.Render(source);
        Assert.All(page.TabFrets, f => Assert.Equal("0", f.Text));
        Assert.Equal(4, page.TabFrets.Count);
        Assert.Equal(strings, LilySharp.Core.Tablature.Tunings.GetStringCount(Collected(source).StaffGroups.SelectMany(g => g.Staves).Single().Tuning!.Value));
        // One staff line a string, each fret on its own: the four open strings stand on four heights.
        Assert.Equal(4, page.TabFrets.Select(f => Math.Round(f.Y, 6)).Distinct().Count());
        Assert.Contains($"<staff-lines>{strings}</staff-lines>", Xml(source));

        var beyond = Assert.Single(Said(Strung("tuning " + list, $"c''4\\{strings + 1} r4 r2 |", "tab gtr")));
        Assert.Equal(DiagnosticCodes.TabStringUnplayable, beyond.Code);
        Assert.Contains($"this tab's tuning has {strings} strings", beyond.Message);
        Assert.Contains(Said(Strung("tuning " + list, "c,,,4 r4 r2 |", "tab gtr")), d => d.Code == DiagnosticCodes.TabOutOfRange);
    }

    /// <summary>A list that spells no tuning is an error on the list that says what is wrong
    /// and shows one that is right — on a part and on a tab item — and the tab is the guitar's.</summary>
    [Theory]
    [InlineData("\"d, a, x g a d'\"", "'x' is not a pitch name")]
    [InlineData("\"D, A, D G A D'\"", "'D,' is not a pitch name")]
    [InlineData("\"d2 a2 d3 g3\"", "'d2' is not a pitch name")]
    [InlineData("\"d,a,d,g\"", "'d,a,d,g' is not a pitch name")]
    [InlineData("\"d a d'\"", "it names 3 strings; a tuning has 4 to 8")]
    [InlineData("\"e\"", "it names 1 string; a tuning has 4 to 8")]
    [InlineData("\"c, d, e, f, g, a, b, c d\"", "it names 9 strings; a tuning has 4 to 8")]
    [InlineData("\"\"", "it names no string")]
    public void AListThatSpellsNoTuning_IsAnError(string list, string says)
    {
        foreach (string source in new[]
                 {
                     Strung("instrument guitar  tuning " + list, "e,4 a, d g |"),
                     Strung("instrument guitar", "e,4 a, d g |", "staff gtr  tab " + list + " gtr"),
                 })
        {
            var said = Assert.Single(Said(source));
            Assert.Equal((DiagnosticCodes.CustomTuningMalformed, DiagnosticSeverity.Error), (said.Code, said.Severity));
            Assert.Equal(list, source.Substring(said.Span.Start, said.Span.Length));
            Assert.Contains($"{list} is not a tuning: {says}.", said.Message);
            Assert.Contains("'tuning \"d, a, d g a d'\"' (DADGAD)", said.Message);
            RoundTrips(source);
            Assert.Equal(Frets(Strung("instrument guitar", "e,4 a, d g |")), Frets(source));
        }
    }

    /// <summary>A bass is written an octave above its sound and a tuning names its strings as
    /// they sound: a bass part's list at the pitch of its notes is warned, with the list an
    /// octave down — which is a bass's tuning, fretted as one.</summary>
    [Fact]
    public void ABassPartsListWrittenAsItsNotes_IsWarnedWithTheListAnOctaveDown()
    {
        string source = Strung("instrument bass  tuning \"d, a, d g\"", "d4 a d' g' |");
        var said = Assert.Single(Said(source));
        Assert.Equal((DiagnosticCodes.CustomTuningMalformed, DiagnosticSeverity.Warning), (said.Code, said.Severity));
        Assert.Contains("write \"d,, a,, d, g,\"", said.Message);
        Clean(Strung("instrument bass  tuning \"d,, a,, d, g,\"", "d,4 a, d g |"));
        // A custom tuning is a bass's when no string sounds above c: the six-string bass's top.
        Assert.True(LilySharp.Core.Tablature.Tunings.IsBass(LilySharp.Core.Tablature.Tunings.Parse("\"b,,, e,, a,, d, g, c\"")));
        Assert.False(LilySharp.Core.Tablature.Tunings.IsBass(LilySharp.Core.Tablature.Tunings.Parse("\"fis,, b,, e, a, d g b e'\"")));
    }

    /// <summary>The part's range moves with its strings, its chord shapes are read on them,
    /// and a fretted mark knows the part is fretted.</summary>
    [Fact]
    public void ACustomTuning_IsThePartsInstrument()
    {
        // The open sixth string, D2, is in range (LYS1052 measures from the tuning's strings).
        Clean(Strung("instrument guitar  tuning \"d, a, d g a d'\"", "d,1 |"));
        Assert.Contains(Said(Strung("instrument guitar", "d,1 |")), d => d.Code == DiagnosticCodes.NoteOutsideInstrumentRange);
        // A shape is the strings': 550000 on DADGAD sounds G D D G A D.
        var shape = Assert.Single(Said(Strung("instrument guitar  tuning \"d, a, d g a d'\"", "diagram(G5 550000)1 |")));
        Assert.Contains("'550000' sounds G A D", shape.Message);
        Clean(Strung("instrument guitar  tuning \"d, a, d g a d'\"", "diagram(Dsus4 000000)1 |"));
        // …and the shape's notes are on the tab, a string each.
        Assert.Equal(["0", "0", "0", "0", "0", "0"], Frets(Strung("instrument guitar  tuning \"d, a, d g a d'\"", "diagram(Dsus4 000000)1 |")).Select(f => f.Text));
        Clean(Strung("tuning \"d, a, d g a d'\"", "d'2@vibrato a2 |"));
    }

    /// <summary>The LilyPond twin writes the strings themselves, and MusicXML the staff's tuning.</summary>
    [Fact]
    public void ACustomTuning_GoesToTheExports()
    {
        string ly = Ly(Strung("instrument guitar  tuning \"d, a, d g a d'\"", "d,4 a, d g |"));
        Assert.Contains("\\new TabStaff \\with { stringTunings = \\stringTuning <d, a, d g a d'> ", ly);
        Assert.DoesNotContain("stringTunings = #", ly);
        Assert.Contains("stringTunings = \\stringTuning <g' c' e' a'>", Ly(Strung("instrument guitar", "g'4 c' e' a' |", "tab \"g' c' e' a'\" gtr")));
        Assert.Contains("stringTunings = #guitar-dadgad-tuning", Ly(Strung("instrument guitar  tuning guitarDadgad", "d,4 a, d g |")));

        string xml = Xml(Strung("tuning \"d, a, d g a d'\"", "d,4 a, d g |", "tab gtr"));
        Assert.Contains("<staff-lines>6</staff-lines>", xml);
        Assert.Equal(["D2", "A2", "D3", "G3", "A3", "D4"],
            System.Text.RegularExpressions.Regex.Matches(xml, "<tuning-step>(.)</tuning-step><tuning-octave>(.)</tuning-octave>")
                .Select(m => m.Groups[1].Value + m.Groups[2].Value));
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(xml, "<fret>0</fret>").Count);
    }

    /// <summary>After a part's <c>tuning</c> the last row writes a tuning out.</summary>
    [Fact]
    public void AfterTuning_TheLastRowWritesATuningOut()
    {
        var rows = Offered("part gtr { instrument guitar  tuning ‸ }\n");
        Assert.Equal("guitar", rows[0]);
        Assert.Equal("\"e, a, d g b e'\"", rows[^1]);
    }

    // ───────────────────────── 8. tab gtr capo 2 ─────────────────────────

    /// <summary>Two guitars and a bass, each playing <paramref name="music"/>, under
    /// <paramref name="top"/> (the file's own lines) on <paramref name="score"/>.</summary>
    private static string Band(string music, string score, string top = "")
        => "octave absolute\ntime 4/4\n" + top + "part lead { instrument guitar }\npart rhy { instrument guitar }\npart b { instrument bass }\n"
            + "section A { lead { " + music + " } rhy { " + music + " } b { " + music + " } }\nform { A }\nscore { " + score + " }\n";

    private static List<string> FretTexts(string source)
    {
        var page = RenderedGeometry.Render(source);
        return [.. page.TabFrets.OrderBy(f => Math.Round(f.Y / 20)).ThenBy(f => f.X).Select(f => f.Text)];
    }

    /// <summary>A tab's own capo is where THAT tab counts its frets from: the page is the page
    /// of the same tab under a file capo of that fret, and the tab beside it does not move.</summary>
    [Fact]
    public void ATabsOwnCapo_IsWhereThatTabCountsFrom()
    {
        const string music = "fis,4 a, d g | b e' fis'2 |";
        string own = Band(music, "tab lead capo 2");
        Clean(own);
        RoundTrips(own);
        Assert.Equal(Frets(Band(music, "tab lead", "capo 2\n")).Select(f => (f.Text, f.X)), Frets(own).Select(f => (f.Text, f.X)));
        Assert.Equal("0", Frets(own)[0].Text);

        // Two guitars, one capoed: each tab its own.
        string two = Band(music, "tab lead capo 2  tab rhy");
        Clean(two);
        var tabs = Collected(two).StaffGroups.SelectMany(g => g.Staves).ToList();
        Assert.Equal([2, 0], tabs.Select(t => t.TabCapo));
        // …the capoed one's numbers and the other's, each as it is alone.
        Assert.Equal(["0", "3", "3", "3", "2", "3", "0"], Frets(own).OrderBy(f => f.X).Select(f => f.Text));
        Assert.Equal(Frets(own).Concat(Frets(Band(music, "tab rhy"))).Select(f => f.Text).Order(), Frets(two).Select(f => f.Text).Order());

        // Beside the other words of the item, in their order.
        Clean(Band(music, "staff lead  tab lead as numbers capo 2 sounds instrument"));
        Clean(Band(music, "tab guitarDropD lead capo 2 hidden  tab rhy"));
        Clean(Band(music, "tab \"d, a, d g a d'\" lead capo 2"));
    }

    /// <summary>The item's capo is over the file's: 0 is no capo on a tab of a capoed file,
    /// another fret is that fret — and a bass's tab, which ignores the file's, takes its own.</summary>
    [Fact]
    public void ATabsOwnCapo_IsOverTheFiles()
    {
        const string music = "fis,4 a, d g | b e' fis'2 |";
        Assert.Equal(Frets(Band(music, "tab lead")).Select(f => (f.Text, f.X)), Frets(Band(music, "tab lead capo 0", "capo 2\n")).Select(f => (f.Text, f.X)));
        Assert.Equal(Frets(Band(music, "tab lead", "capo 2\n")).Select(f => (f.Text, f.X)), Frets(Band(music, "tab lead capo 2", "capo 4\n")).Select(f => (f.Text, f.X)));
        var staves = Collected(Band(music, "tab lead  tab rhy capo 0  tab b  staff b", "capo 2\n")).StaffGroups.SelectMany(g => g.Staves).ToList();
        Assert.Equal([2, 0, 0, 0], staves.Select(t => t.TabCapo));
        Assert.Equal(1, Collected(Band(music, "tab b capo 1", "capo 2\n")).StaffGroups.SelectMany(g => g.Staves).Single().TabCapo);
        // A note under the item's capo is said as the file's is (LYS5002 names the fret).
        var below = Assert.Single(Said(Band("e,4 a, d g | b e' fis'2 |", "tab lead capo 2")));
        Assert.Equal(DiagnosticCodes.TabOutOfRange, below.Code);
        Assert.Contains("cannot be played with the capo on fret 2", below.Message);
    }

    /// <summary>Only the tab's numbers move: the staff, the sound and the head of the page are
    /// the same with it and without.</summary>
    [Fact]
    public void ATabsOwnCapo_MovesNothingElse()
    {
        const string music = "fis,4 a, d g | b e' fis'2 |";
        var with = RenderedGeometry.Render(Band(music, "staff lead  tab lead capo 2"));
        var without = RenderedGeometry.Render(Band(music, "staff lead  tab lead"));
        Assert.Equal(without.Noteheads.Select(h => h.Y), with.Noteheads.Select(h => h.Y));
        Assert.DoesNotContain(with.Texts, t => t.Text.StartsWith("Capo", StringComparison.Ordinal));
        static IEnumerable<(int, int, int)> Notes(LilySharp.Core.Midi.MidiFile midi)
            => midi.Tracks.SelectMany(t => t.Notes).Select(n => (n.Pitch, n.StartTick, n.DurationTicks));
        Assert.Equal(Notes(Midi(Band(music, "staff lead  tab lead"))), Notes(Midi(Band(music, "staff lead  tab lead capo 2"))));
    }

    /// <summary>MusicXML says the tab staff's capo and its frets from it; the LilyPond twin
    /// frets the TabStaff from it.</summary>
    [Fact]
    public void ATabsOwnCapo_GoesToTheExports()
    {
        const string music = "fis,4 a, d g | b e' fis'2 |";
        string source = Band(music, "tab lead capo 2  tab rhy");
        string xml = Xml(source);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(xml, "<capo>2</capo>"));
        Assert.DoesNotContain("<capo>0</capo>", xml);
        string ly = Ly(source);
        Assert.Contains("\\transpose c bes, \\lead", ly);
        Assert.DoesNotContain("\\transpose c bes, \\rhy", ly);
    }

    /// <summary>On a staff item the clause is an error that says a staff reads no capo and
    /// names the tab's spelling and the file's; a fret past 11 is an error on the fret; and a
    /// tab whose capo is not the file's under a part that writes shapes is warned.</summary>
    [Fact]
    public void ACapoOnAStaffItem_IsAnError_AndATabsApartFromItsShapesAWarning()
    {
        const string music = "fis,4 a, d g | b e' fis'2 |";
        string onStaff = Band(music, "staff lead capo 2  tab lead");
        var staff = Assert.Single(Said(onStaff));
        Assert.Equal((DiagnosticCodes.ItemCapo, DiagnosticSeverity.Error), (staff.Code, staff.Severity));
        Assert.Equal("capo 2", onStaff.Substring(staff.Span.Start, staff.Span.Length));
        Assert.Contains("'tab lead capo 2'", staff.Message);
        Assert.Contains("a top-level 'capo 2'", staff.Message);
        RoundTrips(onStaff);
        // The staff is drawn as it was, and the tab reads the file's capo (none).
        Assert.Equal(Frets(Band(music, "staff lead  tab lead")), Frets(onStaff));

        string past = Band(music, "tab lead capo 12");
        var fret = Assert.Single(Said(past));
        Assert.Equal((DiagnosticCodes.ItemCapo, DiagnosticSeverity.Error), (fret.Code, fret.Severity));
        Assert.Equal("12", past.Substring(fret.Span.Start, fret.Span.Length));
        Assert.Contains("0 (no capo, where the file has one) to 11", fret.Message);
        Assert.Equal(Frets(Band(music, "tab lead")), Frets(past));

        static string Shapes(string score)
            => "octave absolute\ntime 4/4\ncapo 2\npart lead { instrument guitar }\n"
                + "section A { lead { chord(F# 022100)2 e'2 | fis,1 | } }\nform { A }\nscore { " + score + " }\n";
        var apart = Assert.Single(Said(Shapes("staff lead  tab lead capo 0")));
        Assert.Equal((DiagnosticCodes.ItemCapo, DiagnosticSeverity.Warning), (apart.Code, apart.Severity));
        Assert.Contains("its own capo (none)", apart.Message);
        Assert.Contains("the file's (fret 2)", apart.Message);
        Clean(Shapes("staff lead  tab lead capo 2"));
        Clean(Shapes("staff lead  tab lead"));

        // The word alone is still a part's name: `staff lead capo` is lead's staff and the part capo.
        Assert.Contains(Said(Band(music, "staff lead capo")), d => d.Message.Contains("Undefined part: 'capo'"));
    }

    /// <summary>After a tab's part the list has the capo, after the style words.</summary>
    [Fact]
    public void AfterATabsPart_TheCapoIsOffered()
    {
        var rows = Offered(Band("e,1 |", "tab lead ‸"));
        Assert.Contains("capo", rows);
        Assert.True(Array.IndexOf(rows, "as numbers") < Array.IndexOf(rows, "capo"));
        Assert.True(Array.IndexOf(rows, "capo") < Array.IndexOf(rows, "staff"));
    }

    // ───────────────────────── 9. <c' f'@harmonic>4 — an artificial harmonic ─────────────────────────

    private static string Violin(string music)
        => "octave absolute\ntime 4/4\npart vn \"Violin\" { instrument violin }\n"
            + "section A { vn { " + music + " } }\nform { A }\nscore { staff vn }\n";

    private static IEnumerable<int> Keys(LilySharp.Core.Midi.MidiFile midi)
        => midi.Tracks.SelectMany(t => t.Notes).OrderBy(n => n.StartTick).ThenBy(n => n.Pitch).Select(n => n.Pitch);

    /// <summary>A chord member's <c>@harmonic</c> is that member's head: the open diamond at
    /// every duration, beside the stopped note's ordinary head, with no circle over the chord —
    /// and <c>@harmonic</c> on a note or a whole chord is the circle it was.</summary>
    [Fact]
    public void AMembersHarmonic_IsTheOpenDiamondOnThatHead()
    {
        char open = GlyphOf(LilySharp.Core.Svg.MusicGlyph.NoteheadDiamondWhite);
        char circle = GlyphOf(LilySharp.Core.Svg.MusicGlyph.StringsHarmonic);
        char black = GlyphOf(LilySharp.Core.Svg.MusicGlyph.NoteheadBlack);
        string source = Violin("<c' f'@harmonic>4 <c' f'@harmonic>2 <d' a'@harmonic>8 <d' a'@harmonic>8 | <c' f'@harmonic>1 |");
        Clean(source);
        RoundTrips(source);
        var g = RenderedGeometry.Render(source);
        // Five chords, five diamonds — the quarter's, the half's, the eighths' and the whole's
        // all the open one — and no circle.
        Assert.Equal(5, g.Glyphs.Count(x => x.Glyph == open));
        Assert.DoesNotContain(g.Glyphs, x => x.Glyph == circle);
        // The diamond stands a fourth above its stopped note: three staff positions, 1.5 spaces.
        var first = g.Glyphs.Where(x => x.Glyph == open || x.Glyph == black).OrderBy(x => x.X).Take(2).OrderBy(x => x.Y).ToList();
        Assert.Equal(open, first[0].Glyph);
        Assert.Equal(1.5, first[1].Y - first[0].Y, 6);

        // On a note, and on the whole chord, the circle — and ordinary heads.
        foreach (string circled in new[] { "e''4@harmonic r4 r2 |", "<c' f'>4@harmonic r4 r2 |", "<c' f'@flageolet>4 r4 r2 |" })
        {
            Clean(Violin(circled));
            var page = RenderedGeometry.Render(Violin(circled));
            Assert.Single(page.Glyphs, x => x.Glyph == circle);
            Assert.DoesNotContain(page.Glyphs, x => x.Glyph == open);
        }
    }

    /// <summary>The heads stand as LilyPond's: at the stem's side on a stemmed chord, the
    /// diamond centred on the whole note of a stemless one; and the diamond takes no dot.</summary>
    /// <remarks>MEASURED, LilyPond 2.26.0 (Lab sessions/p1226/md/eval3/featH/lp3.ly): up-stem
    /// quarter, plain 8.5850 … 9.8892 and diamond 8.5892 … 9.8892; whole, plain 29.415 … 31.377
    /// and diamond 29.746 … 31.046; one Dots grob on the dotted chord.</remarks>
    [Fact]
    public void AMembersHarmonic_StandsAsLilyPondsHeadDoes()
    {
        char open = GlyphOf(LilySharp.Core.Svg.MusicGlyph.NoteheadDiamondWhite);
        char black = GlyphOf(LilySharp.Core.Svg.MusicGlyph.NoteheadBlack);
        char whole = GlyphOf(LilySharp.Core.Svg.MusicGlyph.NoteheadWhole);
        char dot = GlyphOf(LilySharp.Core.Svg.MusicGlyph.AugmentationDot);

        var up = RenderedGeometry.Render(Violin("<c' f'@harmonic>4 r4 r2 |"));
        Assert.Equal(0.0042, up.Glyphs.Single(x => x.Glyph == open).X - up.Glyphs.Single(x => x.Glyph == black).X, 4);
        var down = RenderedGeometry.Render(Violin("<c'' f''@harmonic>4 r4 r2 |"));
        Assert.Equal(0.0, down.Glyphs.Single(x => x.Glyph == open).X - down.Glyphs.Single(x => x.Glyph == black).X, 4);
        var stemless = RenderedGeometry.Render(Violin("<c' f'@harmonic>1 |"));
        Assert.Equal(0.331, stemless.Glyphs.Single(x => x.Glyph == open).X - stemless.Glyphs.Single(x => x.Glyph == whole).X, 4);

        var dotted = RenderedGeometry.Render(Violin("<c' f'@harmonic>4. r8 r2 |"));
        Assert.Single(dotted.Glyphs, x => x.Glyph == dot);
        Assert.Equal(2, RenderedGeometry.Render(Violin("<c' f'>4. r8 r2 |")).Glyphs.Count(x => x.Glyph == dot));
    }

    /// <summary>The three standard artificial harmonics sound one pitch for their two notes —
    /// a fourth above the stopped note two octaves over it, a fifth an octave and a fifth, a
    /// major third two octaves and a third — and any other pair sounds as written.</summary>
    [Theory]
    [InlineData("<c' f'@harmonic>4", new[] { 84 })]
    [InlineData("<c' g'@harmonic>4", new[] { 79 })]
    [InlineData("<c' e'@harmonic>4", new[] { 88 })]
    [InlineData("<f'@harmonic c'>4", new[] { 84 })]
    [InlineData("<c' a'@harmonic>4", new[] { 60, 69 })]
    [InlineData("<c' f'>4@harmonic", new[] { 60, 65 })]
    [InlineData("<g c' f'@harmonic>4", new[] { 55, 84 })]
    [InlineData("<c' f'@harmonic g' c''@harmonic>4", new[] { 84, 91 })]
    public void AnArtificialHarmonic_SoundsItsOnePitch(string chord, int[] keys)
        => Assert.Equal(keys, Keys(Midi(Violin(chord + " r4 r2 |"))));

    /// <summary>MusicXML writes the diamond and names the two notes; the twin is LilyPond's own
    /// <c>\harmonic</c> on the member.</summary>
    [Fact]
    public void AnArtificialHarmonic_GoesToTheExports()
    {
        string xml = Xml(Violin("<c' f'@harmonic>4 r4 r2 |"));
        Assert.Contains("<pitch><step>C</step><octave>4</octave></pitch>", xml);
        Assert.Contains("<pitch><step>F</step><octave>4</octave></pitch>", xml);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(xml, "<notehead>diamond</notehead>"));
        Assert.Contains("<technical><harmonic><artificial /><base-pitch /></harmonic></technical>", xml);
        Assert.Contains("<technical><harmonic><artificial /><touching-pitch /></harmonic></technical>", xml);
        Assert.DoesNotContain("<harmonic", Xml(Violin("<c' f'>4 r4 r2 |")));

        string ly = Ly(Violin("<c' f'@harmonic>4 r4 r2 |"));
        Assert.Contains("<c' f'\\harmonic>4", ly);
        Assert.DoesNotContain("flageolet", ly);
        Assert.Contains("<c' f'>4-\\flageolet", Ly(Violin("<c' f'>4@harmonic r4 r2 |")));
    }

    /// <summary>On a tab the touched member's fret stands in angle brackets, the stopped one's
    /// plain — two bows round one number.</summary>
    [Fact]
    public void AnArtificialHarmonic_OnATab_IsItsFretInAngleBrackets()
    {
        string source = Guitar("<c'\\3 f'\\2@harmonic>4 r4 r2 |", "tab gtr");
        Clean(source);
        var g = RenderedGeometry.Render(source);
        Assert.Equal(["5", "6"], g.TabFrets.Select(f => f.Text).Order());
        Assert.Equal(2, g.Beziers.Count);
        var six = g.TabFrets.Single(f => f.Text == "6");
        Assert.True(g.Beziers.Min(b => b.P0.X) < six.X && six.X < g.Beziers.Max(b => b.P0.X));
        Assert.Equal(six.X, g.Beziers.Average(b => b.P0.X), 1);
        Assert.Empty(RenderedGeometry.Render(Guitar("<c'\\3 f'\\2>4 r4 r2 |", "tab gtr")).Beziers);
    }

    // ───────────────────────── 10. e,4@harmonic(12) — a natural harmonic at a fret ─────────────────────────

    private const string NaturalHarmonics = "e,4@harmonic(12) a,4@harmonic(7) d2@harmonic(5) | g8@harmonic(4) g8@harmonic(9) b4.@harmonic(3) e'8@harmonic(19) e'4@harmonic(24) |";

    /// <summary>The note written is the open string; the staff shows what the harmonic sounds —
    /// the heads where those pitches' heads stand, with their accidentals — under the harmonic
    /// head: open down to the half note, filled from the quarter.</summary>
    [Fact]
    public void ANaturalHarmonic_ShowsWhatItSoundsUnderTheHarmonicHead()
    {
        char open = GlyphOf(LilySharp.Core.Svg.MusicGlyph.NoteheadDiamondWhite);
        char filled = GlyphOf(LilySharp.Core.Svg.MusicGlyph.FetaNoteheadsS2harmonic);
        string source = Guitar(NaturalHarmonics);
        Clean(source);
        RoundTrips(source);
        var g = RenderedGeometry.Render(source);
        var heads = g.Glyphs.Where(x => x.Glyph == open || x.Glyph == filled).OrderBy(x => x.X).ToList();
        Assert.Equal([filled, filled, open, filled, filled, filled, filled, filled], heads.Select(h => h.Glyph));
        // The sounding pitches written out: E3 E4 D5, B5 B5 F#6 B5 E6.
        var sounding = RenderedGeometry.Render(Guitar("e4 e'4 d''2 | b''8 b''8 fis'''4. b''8 e'''4 |"));
        Assert.Equal(sounding.Noteheads.OrderBy(h => h.X).Select(h => Math.Round(h.Y, 6)), heads.Select(h => Math.Round(h.Y, 6)));
        Assert.Equal(sounding.Accidentals.Count, g.Accidentals.Count);
        Assert.Single(g.Accidentals);
        // A dotted harmonic keeps its dot (LilyPond sets harmonicDots round \harmonicByFret).
        Assert.Single(g.Glyphs, x => x.Glyph == GlyphOf(LilySharp.Core.Svg.MusicGlyph.AugmentationDot));
        // …and the relative frame goes on from the note WRITTEN, the open string.
        const string relative = "time 4/4\npart gtr { instrument guitar }\nsection A { gtr { e,4@harmonic(12) a,4 r2 | } }\nform { A }\nscore { staff gtr }\n";
        Assert.Equal(Keys(Midi(relative.Replace("@harmonic(12)", ""))).Last(), Keys(Midi(relative)).Last());
        Assert.Equal(Keys(Midi(relative.Replace("@harmonic(12)", ""))).First() + 12, Keys(Midi(relative)).First());
    }

    /// <summary>The tab shows where it is played: the fret, in angle brackets, on the string of
    /// the open note written — the line that string's 0 stands on.</summary>
    [Fact]
    public void ANaturalHarmonic_OnATab_IsItsFretInAngleBracketsOnItsString()
    {
        string source = Guitar(NaturalHarmonics, "tab gtr");
        Clean(source);
        var g = RenderedGeometry.Render(source);
        // (A fret that begins with 1 is drawn as two runs, its 1 drawn closer: the numbers are
        // read joined, and each run's line is asked for.)
        var frets = g.TabFrets.OrderBy(f => f.X).ToList();
        Assert.Equal("12" + "7" + "5" + "4" + "9" + "3" + "19" + "24", string.Concat(frets.Select(f => f.Text)));
        var open = RenderedGeometry.Render(Guitar("e,4 a,4 d2 | g8 g8 b4. e'8 e'4 |", "tab gtr")).TabFrets.OrderBy(f => f.X).ToList();
        Assert.All(open, f => Assert.Equal("0", f.Text));
        int[] runs = [0, 0, 1, 2, 3, 4, 5, 6, 6, 7];
        Assert.Equal(runs.Select(i => Math.Round(open[i].Y * 2)), frets.Select(f => Math.Round(f.Y * 2)));   // the line, whatever the digit's own baseline
        // Two bows a number, one either side of it, pointed: the control points drawn past one another.
        Assert.Equal(16, g.Beziers.Count);
        Assert.All(g.Beziers, b => Assert.True(Math.Abs(b.C1.Y - b.P0.Y) > Math.Abs(b.C2.Y - b.P0.Y)));
        foreach (var fret in frets.Where(f => f.Text is "7" or "5" or "4" or "9" or "3" or "24"))
        {
            var round = g.Beziers.OrderBy(b => Math.Abs(b.P0.X - fret.X)).Take(2).OrderBy(b => b.P0.X).ToList();
            Assert.True(round[0].P0.X < fret.X && fret.X < round[1].P0.X);
        }
        // Neighbouring harmonics keep their brackets apart.
        var xs = g.Beziers.Select(b => b.P0.X).Order().ToList();
        for (int i = 2; i < xs.Count; i += 2)
            Assert.True(xs[i] - xs[i - 1] > 0.9, $"brackets {xs[i - 1]} and {xs[i]}");
        // Beside a staff the two agree: heads above, numbers below, the same count.
        var both = RenderedGeometry.Render(Guitar(NaturalHarmonics, "staff gtr  tab gtr"));
        Assert.Equal("127549319" + "24", string.Concat(both.TabFrets.OrderBy(f => f.X).Select(f => f.Text)));
        Assert.Equal(16, both.Beziers.Count);
    }

    /// <summary>The harmonic sounds the string's partial: 12 an octave over the open string, 7
    /// and 19 an octave and a fifth, 5 and 24 two octaves, 4, 9 and 16 two octaves and a major
    /// third, 3 two octaves and a fifth.</summary>
    [Theory]
    [InlineData("e,4@harmonic(12)", 52)]
    [InlineData("a,4@harmonic(7)", 64)]
    [InlineData("a,4@harmonic(19)", 64)]
    [InlineData("d4@harmonic(5)", 74)]
    [InlineData("d4@harmonic(24)", 74)]
    [InlineData("g4@harmonic(4)", 83)]
    [InlineData("g4@harmonic(9)", 83)]
    [InlineData("g4@harmonic(16)", 83)]
    [InlineData("b4@harmonic(3)", 90)]
    [InlineData("e'4@harmonic(12)", 76)]
    public void ANaturalHarmonic_SoundsTheStringsPartial(string note, int key)
    {
        string source = Guitar(note + " r4 r2 |", "staff gtr  tab gtr");
        Clean(source);
        Assert.Equal([key], Keys(Midi(source)));
    }

    /// <summary>MusicXML: the sounding pitch, the diamond, a natural harmonic, and on the tab
    /// staff its string and fret. The twin: LilyPond's own <c>\harmonicByFret</c> round the open
    /// string's note, with its string.</summary>
    [Fact]
    public void ANaturalHarmonic_GoesToTheExports()
    {
        string xml = Xml(Guitar("e,4@harmonic(12) a,4@harmonic(7) r2 |", "staff gtr  tab gtr"));
        Assert.Contains("<pitch><step>E</step><octave>3</octave></pitch>", xml);
        Assert.Contains("<pitch><step>E</step><octave>4</octave></pitch>", xml);
        Assert.Contains("<technical><harmonic><natural /><sounding-pitch /></harmonic><string>6</string><fret>12</fret></technical>", xml);
        Assert.Contains("<technical><harmonic><natural /><sounding-pitch /></harmonic><string>5</string><fret>7</fret></technical>", xml);
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(xml, "<notehead>diamond</notehead>").Count);

        string ly = Ly(Guitar("e,4@harmonic(12) a,4@harmonic(7) b2\\2@harmonic(3) |", "staff gtr  tab gtr"));
        Assert.Contains("\\harmonicByFret #12 e,4-\\tweak stencil ##f \\6 \\harmonicByFret #7 a,4-\\tweak stencil ##f \\5 \\harmonicByFret #3 b2\\2 |", ly);
        Assert.DoesNotContain("dropped", string.Join("\n", TwinWarnings(Guitar("e,4@harmonic(12) r4 r2 |"))));
    }

    private static IReadOnlyList<string> TwinWarnings(string source)
    {
        var exporter = new LilySharp.Core.LilyPond.LilyPondExporter();
        exporter.Export(SyntaxTree.Parse(source));
        return [.. exporter.Warnings];
    }

    /// <summary>A written string chooses between two strings open at one pitch; a tuning written
    /// out and a capo move the open strings with them.</summary>
    [Fact]
    public void ANaturalHarmonic_IsOnTheStringsOfThePartsTuning()
    {
        // DADGAD: the sixth string is D2, and its harmonic at 12 sounds D3.
        string dadgad = Strung("instrument guitar  tuning \"d, a, d g a d'\"", "d,4@harmonic(12) a4@harmonic(7) r2 |");
        Clean(dadgad);
        Assert.Equal([50, 76], Keys(Midi(dadgad)));
        Assert.Equal("127", string.Concat(Frets(dadgad).OrderBy(f => f.X).Select(f => f.Text)));
        // A bass, written an octave above its sound: its open E string is written e,.
        string bass = Strung("instrument bass", "e,4@harmonic(12) g4@harmonic(5) r2 |");
        Clean(bass);
        Assert.Equal("125", string.Concat(Frets(bass).OrderBy(f => f.X).Select(f => f.Text)));
        // Under a capo the open strings are the capo's, written as they sound.
        string capo = "octave absolute\ntime 4/4\ncapo 2\npart gtr { instrument guitar }\n"
            + "section A { gtr { fis,4@harmonic(12) r4 r2 | } }\nform { A }\nscore { staff gtr  tab gtr }\n";
        Clean(capo);
        Assert.Equal("12", string.Concat(Frets(capo).OrderBy(f => f.X).Select(f => f.Text)));
        Assert.Equal([54], Keys(Midi(capo)));
        var off = Assert.Single(Said(capo.Replace("fis,4", "e,4")), d => d.Code == DiagnosticCodes.HarmonicNotAnOpenString);
        Assert.Equal(DiagnosticSeverity.Error, off.Severity);
    }

    /// <summary>What is refused, each with its own word: a fret no harmonic is written at, a host
    /// that is not a single note, a part with no strings, a note that is no open string.</summary>
    [Fact]
    public void ANaturalHarmonic_MiswrittenIsSaid()
    {
        foreach (string fret in new[] { "8", "0", "13", "2.7", "12,7" })
        {
            string source = Guitar($"e,4@harmonic({fret}) r4 r2 |", "staff gtr  tab gtr");
            var said = Assert.Single(Said(source), d => d.Code == DiagnosticCodes.HarmonicFretMalformed);
            Assert.Equal(DiagnosticSeverity.Error, said.Severity);
            Assert.Contains("The frets are 12, 7, 19, 5, 24, 4, 9, 16, 3", said.Message);
            RoundTrips(source);
            // Drawn and played as the note written.
            Assert.Equal([40], Keys(Midi(source)));
            Assert.Equal(["0"], Frets(source).Select(f => f.Text));
        }

        var onChord = Assert.Single(Said(Guitar("<e, b,>4@harmonic(12) r4 r2 |")));
        Assert.Equal((DiagnosticCodes.HarmonicFretNotOnANote, DiagnosticSeverity.Error), (onChord.Code, onChord.Severity));
        var onMember = Assert.Single(Said(Guitar("<e, b,@harmonic(12)>4 r4 r2 |")));
        Assert.Equal(DiagnosticCodes.HarmonicFretNotOnANote, onMember.Code);
        Assert.Equal([40, 47], Keys(Midi(Guitar("<e, b,@harmonic(12)>4 r4 r2 |"))));

        string violin = Violin("g4@harmonic(12) r4 r2 |");
        var unfretted = Assert.Single(Said(violin));
        Assert.Equal((DiagnosticCodes.HarmonicFretNotFretted, DiagnosticSeverity.Error), (unfretted.Code, unfretted.Severity));
        Assert.Equal("@harmonic(12)", violin.Substring(unfretted.Span.Start, unfretted.Span.Length));
        Assert.Equal([55], Keys(Midi(violin)));

        string stopped = Guitar("f,4@harmonic(12) r4 r2 |", "staff gtr  tab gtr");
        var offString = Assert.Single(Said(stopped));
        Assert.Equal((DiagnosticCodes.HarmonicNotAnOpenString, DiagnosticSeverity.Error), (offString.Code, offString.Severity));
        Assert.Equal("@harmonic(12)", stopped.Substring(offString.Span.Start, offString.Span.Length));
        Assert.Contains("e,4@harmonic(12)", offString.Message);
    }

    /// <summary>The slur that starts on a note with the circle is still a slur: only a number
    /// glued inside the parentheses is a fret.</summary>
    [Fact]
    public void AHarmonicBeforeASlur_IsStillTheCircleAndTheSlur()
    {
        foreach (string music in new[] { "e'4@harmonic(d' c') r4 |", "e'4@harmonic( d' c') r4 |", "e'4@harmonic (d' c') r4 |" })
        {
            string source = Guitar(music);
            Clean(source);
            Assert.Empty(SyntaxTree.Parse(source).GetRoot().DescendantNodes<MusicMarkSyntax>());
            Assert.Equal([64, 62, 60], Keys(Midi(source)));
        }
    }

    /// <summary>A harmonic above the fretted range is no slip of an octave: LYS1052 lets it be.</summary>
    [Fact]
    public void AHarmonicAboveTheFrettedRange_IsNotAnOctaveSlip()
    {
        Clean(Guitar("b4@harmonic(3) r4 r2 |"));
        Assert.Contains(Said(Guitar("fis''''4 r4 r2 |")), d => d.Code == DiagnosticCodes.NoteOutsideInstrumentRange);
    }

    /// <summary>Inside <c>@harmonic(</c> the frets most written, in the order a guitarist meets them.</summary>
    [Fact]
    public void InsideAHarmonicsParentheses_TheFretsAreOffered()
    {
        Assert.Equal(["12", "7", "5", "4", "9", "3"], Offered(Guitar("e,4@harmonic(‸) r4 r2 |")));
        Assert.Contains("harmonic", Offered(Guitar("e,4@‸ r4 r2 |")));
    }

    // ───────────────────────── 11. the twin says what it drops of a bend ─────────────────────────

    /// <summary>LilyPond's bend spanner runs between two written notes, and Lily#'s bend is one
    /// note and an amount: the twin writes the note plain and says so of the bend and of its
    /// release, each by its own name.</summary>
    [Fact]
    public void TheTwin_SaysWhatItDropsOfABend()
    {
        var said = TwinWarnings(Guitar("a'4@bend(full)@release a'4@bend(half) a'4@release r4 |"));
        Assert.Equal(2, said.Count(w => w.StartsWith("@release dropped with the @bend it lets back", StringComparison.Ordinal)));
        Assert.Single(said, w => w.StartsWith("@bend(full) dropped: LilyPond's bend spanner", StringComparison.Ordinal));
        Assert.Single(said, w => w.StartsWith("@bend(half) dropped: LilyPond's bend spanner", StringComparison.Ordinal));
        Assert.DoesNotContain(said, w => w.Contains("out of scope") || w.Contains("not mapped"));
        Assert.Contains("a'4 a'4 a'4 r4 |", Ly(Guitar("a'4@bend(full)@release a'4@bend(half) a'4@release r4 |")));
    }
}
