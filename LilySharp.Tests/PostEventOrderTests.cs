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

using System.Text.RegularExpressions;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Order-free post-events: slur parens / ties / beam brackets may interleave
/// with <c>@</c>-articulations after a note (<c>g4(@cresc</c> ≡
/// <c>g4@cresc(</c>), matching LilyPond's unordered post_events.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/lily-parser.yy post_events.
/// <para>
/// ⚠️ THE TWO SPELLINGS DO NOT PARSE TO THE SAME TREE, AND SINCE 2026-08-30 THAT IS
/// DELIBERATE. The tree stands in the order the characters were typed (HANDOFF §2F ⑺), so
/// a marker written BEFORE another post-event — <c>c4~@mark("A")</c> — is a CHILD of its
/// host, while the same marker written last is the next ITEM. Every reader of markers
/// therefore has two places to look, and one that looks in only one is green on half the
/// corpus. <see cref="TheTwoOrdersOfOnePostEventRun_AreTheSameMusic"/> is what makes that
/// failure loud; the older facts below still pin the individual flags.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class PostEventOrderTests
{
    // Every case here is a bare note stream, which is no longer a legal top level —
    // it rides in the minimal document instead.
    private static SyntaxTree Parse(string music) => MusicSource.Parse(music);

    [Theory]
    [InlineData("g4(@cresc a b c) |")]      // marker before articulation
    [InlineData("g4@cresc( a b c) |")]      // articulation before marker
    [InlineData("g2~@cresc g2 |")]          // tie + dynamic
    [InlineData("g8[@cresc a b c] d4 e |")] // beam start + dynamic
    [InlineData("g4( a b c)@f |")]          // slur end + dynamic
    public void MarkerAndArticulation_AnyOrder_ParsesClean(string source)
    {
        var tree = Parse(source);
        Assert.Empty(tree.Diagnostics);
    }

    /// <summary>Every source offset stripped: what is left is the drawn page.</summary>
    private static string Ink(string svg) => Regex.Replace(svg, " data-pos=\"\\d+\"", "");

    private static string Book(string music) =>
        $"part bass\nsection A {{ bass {{ {music} }} }}\nform main {{ ~A }}\n"
        + "score main { staff bass }\n";

    private static string Page(string music) => Ink(LilySharp.Core.Svg.SvgGenerator.Generate(
        SyntaxTree.Parse(Book(music)),
        new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false }));

    /// <summary>
    /// The exported MusicXML, SERIALIZED. ⚠️ <c>Export</c> returns a document MODEL, so
    /// <c>.ToString()</c> on it is the type name — the first draft of this helper compared
    /// that constant with itself and the whole theory passed vacuously. The poison caught
    /// it: removing the marker kinds from <c>ChordSyntax.Articulations</c> demonstrably
    /// drops <c>&lt;slur type="stop"/&gt;</c> from the empty-chord book, and this theory
    /// stayed green until the serialization went in. (RULES §5.4: a checker is proved by
    /// making it fail first.)
    /// </summary>
    private static string Xml(string music)
        => new MusicXmlExporter().Export(SyntaxTree.Parse(Book(music))).ToXml().ToString();

    /// <summary>What the two books SOUND like. <c>MidiNote.SourcePos</c> is deliberately
    /// left out: it is a source offset, the one thing the two spellings must differ in,
    /// and it is never written to a .mid file either.</summary>
    private static string Midi(string music)
        => string.Join("|", new MidiExporter().Export(SyntaxTree.Parse(Book(music)))
            .Tracks.SelectMany(t => t.Notes)
            .Select(n => $"{n.Channel}/{n.Pitch}/{n.Velocity}@{n.StartTick}+{n.DurationTicks}"
                + $"/{n.QuarterBend}"));

    /// <summary>
    /// Whether ANY tie/slur/beam marker in the book stands INSIDE its host's post-event
    /// list rather than beside it — the difference the two spellings make.
    /// </summary>
    /// <remarks>
    /// ⚠️ ANY, not the first. Several of the pairs below open a slur early and close it on
    /// the note that carries the moved marker (<c>g4( a b c)@f</c>), so the FIRST marker in
    /// the book is the <c>(</c>, which is a plain sequence item in both spellings — asking
    /// it says "outside" about a book whose <c>)</c> is inside, and the premise assert
    /// fails on a correct engine. It did, on the first run.
    /// </remarks>
    private static bool AnyMarkerIsInsideItsHost(string music)
        => SyntaxTree.Parse(Book(music)).GetRoot().DescendantNodes()
            .Any(n => n is TieSyntax or SlurSyntax or BeamMarkerSyntax
                && n.Parent is NoteSyntax or ChordSyntax or RestSyntax
                    or ChordRepetitionSyntax or SlashNoteSyntax or BareDurationSyntax
                    or DrumNoteSyntax or ArpeggioSyntax);

    /// <summary>
    /// The claim itself, stated where it can fail: the two orders of one post-event run
    /// are the SAME MUSIC, so they must engrave, export and sound identically. Compared on
    /// the outputs rather than on the tree, because the trees legitimately differ.
    /// </summary>
    /// <remarks>
    /// ⚠️ EACH OF THE LAST TWO CASES CAUGHT A REAL SILENT DROP on the day the tree became
    /// faithful (2026-08-30), and each fell through a DIFFERENT hole — which is why they
    /// are here and not folded into the plain cases above:
    /// <list type="bullet">
    /// <item>the TUPLET pair, through a container body walking its DIRECT children
    /// (<c>MeasureCollector.MusicWalk</c>): measured on audit/lpreg/tupnumss.lys, which
    /// began warning "a slur ')' has no '(' open" — the same containers that swallowed
    /// rehearsal marks in session 293, a different thing falling through.</item>
    /// <item>the EMPTY-CHORD pair, through <c>ChordSyntax.Articulations</c> being a TYPE
    /// FILTER that did not name markers, so the green tree held a node no accessor handed
    /// out: measured on audit/lp-regression/lys/empty-chord.lys, which lost its slur close
    /// from the MusicXML AND the LilyPond twin while the page stayed correct.</item>
    /// </list>
    /// ⚠️ The page is compared with <c>data-pos</c> masked, because the two spellings
    /// really do put their characters in different places and that offset is what the
    /// change exists to correct. MusicXML and MIDI carry no source offsets, so they are
    /// compared whole — and on the 1630-book sweep neither moved on any book.
    /// </remarks>
    [Theory]
    // marker first (it lands INSIDE the host)   |   marker last (it is the next item)
    [InlineData("c4~@mark(\"A\") c4 |", "c4@mark(\"A\")~ c4 |")]
    [InlineData("g4(@cresc a b c |", "g4@cresc( a b c |")]
    [InlineData("g4( a b c)@f |", "g4( a b c@f) |")]
    [InlineData("c8[@accent d e f] g4 |", "c8@accent[ d e f] g4 |")]
    [InlineData("tuplet 3/2 { e8(@accent e8 e8) } r4 r2 |",
                "tuplet 3/2 { e8@accent( e8 e8) } r4 r2 |")]
    [InlineData("r4 e8( g <>)@f c8 c c |", "r4 e8( g <>@f) c8 c c |")]
    public void TheTwoOrdersOfOnePostEventRun_AreTheSameMusic(string markerFirst, string markerLast)
    {
        // The premise, so a pass cannot be an accident of the two spellings parsing alike:
        // the marker really does land in a different place in the two trees.
        Assert.True(AnyMarkerIsInsideItsHost(markerFirst),
            $"[{markerFirst}] was expected to put a marker inside its host");
        Assert.False(AnyMarkerIsInsideItsHost(markerLast),
            $"[{markerLast}] was expected to leave every marker beside its host");

        Assert.Equal(Page(markerLast), Page(markerFirst));
        Assert.Equal(Xml(markerLast), Xml(markerFirst));
        Assert.Equal(Midi(markerLast), Midi(markerFirst));
    }

    /// <summary>
    /// The reader-visible half, stated as the quantity it corrupts: a rehearsal letter's
    /// <c>data-pos</c> is the offset of the <c>@</c> the author typed, not of the tie in
    /// front of it. That attribute is what the preview highlights from and what a
    /// score-side selection turns into a text range, so one character early is a target
    /// that lights the wrong thing.
    /// </summary>
    /// <remarks>
    /// Reported as HANDOFF §2 U9 and measured 2026-08-30 on the author's own library:
    /// 40 of 326 books write this shape, 63 times — <c>~@mark</c> 53, <c>~@trill</c> 1,
    /// <c>)@fall</c> 3, and six string numbers after a slur close. The control is the same
    /// mark with no marker in front of it, which was never wrong: without it a test that
    /// pins an offset passes just as well when nothing maps correctly.
    /// </remarks>
    [Theory]
    [InlineData("c4~@mark(\"A\") c4 |")]   // the author's own idiom
    [InlineData("c4@mark(\"A\") c4 |")]    // control: no marker in front
    public void ARehearsalLetter_ReportsTheOffsetOfTheAtSignThatWroteIt(string music)
    {
        var book = Book(music);
        var svg = LilySharp.Core.Svg.SvgGenerator.Generate(
            SyntaxTree.Parse(book),
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });

        int at = book.IndexOf("@mark", StringComparison.Ordinal);
        Assert.True(at > 0, "the fixture must contain the mark it is about");

        var positions = Regex.Matches(svg, "<text[^>]*data-pos=\"(\\d+)\"[^>]*>A</text>")
            .Select(m => int.Parse(m.Groups[1].Value)).ToList();
        Assert.NotEmpty(positions);
        Assert.All(positions, p => Assert.Equal(at, p));
    }

    [Fact]
    public void SlurThenCresc_AttachesCrescToTheNote()
    {
        var score = new MeasureCollector().Collect(Parse("g4(@cresc a b c)@f |"));

        // The cresc mark lands in measure 0 and the f dynamic on the last note.
        Assert.Contains(score.MusicMarks, m => m.Type == MusicMarkType.Cresc);
        var f = Assert.Single(score.Dynamics);
        Assert.Equal(3, f.ItemIndex);

        var first = Assert.IsType<NoteItem>(score.Voice.Measures[0].Items[0]);
        Assert.True(first.HasSlurStart, "slur start must survive the reordering");
        var last = Assert.IsType<NoteItem>(score.Voice.Measures[0].Items[3]);
        Assert.True(last.HasSlurEnd, "slur end must survive @f after ')'");
    }

    [Fact]
    public void TieThenDynamic_TieSurvives()
    {
        var score = new MeasureCollector().Collect(Parse("g2~@cresc g2 |"));
        var first = Assert.IsType<NoteItem>(score.Voice.Measures[0].Items[0]);
        Assert.True(first.HasTieStart);
    }

    [Fact]
    public void BareMarkers_WithoutArticulation_Unchanged()
    {
        var tree = Parse("b4( c d e) | g2~ g4 a | c8[ d e f] g4 a |");
        Assert.Empty(tree.Diagnostics);
        var score = new MeasureCollector().Collect(tree);
        Assert.True(((NoteItem)score.Voice.Measures[0].Items[0]).HasSlurStart);
        Assert.True(((NoteItem)score.Voice.Measures[1].Items[0]).HasTieStart);
        Assert.True(((NoteItem)score.Voice.Measures[2].Items[0]).HasBeamStart);
    }

    [Fact]
    public void CompoundMarkThenBeamOrTie_MarkerSurvives()
    {
        // A @name(...) mark rides its note as a MusicMarkSyntax CHILD, and the
        // flattened walk used to list that child between the note and the beam/
        // tie marker written after it — the one-node lookahead read the mark,
        // the flag never got set, and the manual beam silently fell to the
        // autobeamer (found via LP regression beaming.ly, the beam over the bar
        // line: c'8^"over bar line"[ c c]). @cresc/@accent never hit this: only
        // MusicMarkSyntax is a collectable flat node.
        var score = new MeasureCollector().Collect(
            Parse("c8@text(\"x\")[ d8] e4 g2@text(\"y\")~ | g1 |"));
        var first = Assert.IsType<NoteItem>(score.Voice.Measures[0].Items[0]);
        Assert.True(first.HasBeamStart, "beam start must survive a @text(...) mark");
        var tied = Assert.IsType<NoteItem>(score.Voice.Measures[0].Items[3]);
        Assert.True(tied.HasTieStart, "tie must survive a @text(...) mark");
    }

    [Fact]
    public void InlineVoltaBracket_NotMistakenForBeamMarker()
    {
        // '[1.' after a note is an inline volta ending, not a beam bracket —
        // the marker-run lookahead must not consume it.
        var tree = Parse("c4 d e f |: g4 a b c [1. d1 |] [2. e1 |] :|");
        Assert.Empty(tree.Diagnostics);
    }
}
