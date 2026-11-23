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
using System.Xml.Linq;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.MusicXmlImport;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The scripts written on an empty chord (<c>c''2 &lt;&gt;@accent d''2</c>) stand at its moment,
/// on the note that begins it, as the page draws them — and at the end of a piece a fermata
/// stands over the closing bar line (session 1315). Until session 1328 neither MusicXML nor
/// MIDI had any of them (Lab sessions/p1328/end e6 to e10).
/// </summary>
[Trait("Category", "Unit")]
public class EmptyChordScriptsExportTests
{
    private static string Book(string bars)
        => "octave absolute\ntime 4/4\npart m { clef treble }\nsection A { m { " + bars + " } }\nform { ~A }\nscore { staff m }\n";

    /// <summary>Each note's notations and the closing bar lines, bar by bar.</summary>
    private static string Xml(string bars)
    {
        var doc = XDocument.Parse(new MusicXmlExporter().Export(SyntaxTree.Parse(Book(bars))).ToXml().ToString());
        var said = new List<string>();
        foreach (var measure in doc.Descendants("measure"))
            foreach (var e in measure.Elements())
                if (e.Name == "note")
                    said.Add("note" + (e.Element("notations") is { } n
                        ? "[" + string.Join(",", n.Elements().Select(x => x.Name.LocalName
                            + (x.HasElements ? "(" + string.Join(",", x.Elements().Select(y => y.Name.LocalName)) + ")" : ""))) + "]"
                        : ""));
                else if (e.Name == "barline")
                    said.Add("barline[" + string.Join(",", e.Elements().Select(x => x.Name.LocalName
                        + (x.Attribute("type") is { } t ? ":" + t.Value : ""))) + "]");
        return string.Join(" ", said);
    }

    [Theory]
    // On the note that begins at its moment, as the note's own marks are.
    [InlineData("c''2 <>@fermata d''2 | c''1 |", "note note[fermata] note")]
    [InlineData("c''2 d''2 <>@fermata | c''1 |", "note note note[fermata]")]
    [InlineData("c''2 <>@accent@staccato@trill d''2 | c''1 |", "note note[articulations(accent,staccato),ornaments(trill-mark)] note")]
    // At the end of the piece a fermata stands over the closing bar line — a full bar or not.
    [InlineData("c''2 d''2 | c''1 <>@fermata |", "note note note barline[fermata:upright]")]
    [InlineData("c''2 d''2 | c''2 <>@fermata |", "note note note barline[fermata:upright]")]
    [InlineData("c''2 d''2 | c''1 <>@fermata.down |", "note note note barline[fermata:inverted]")]
    // …the other scripts nowhere, as on the page.
    [InlineData("c''2 d''2 | c''1 <>@accent |", "note note note")]
    // A grace or a rest that begins at its moment takes them, as the page and LilyPond draw them
    // (session 1329, Lab sessions/p1329/g s2 r1 r2 c1): the main note after a grace took them.
    [InlineData("c''2 <>@accent grace { e''16 } d''2 | c''1 |", "note note[articulations(accent)] note note")]
    [InlineData("c''2 <>@accent grace { e''16 f''16 } d''2 | c''1 |", "note note[articulations(accent)] note note note")]
    [InlineData("c''2 <>@staccato grace { <e'' g''>16 } d''2 | c''1 |", "note note[articulations(staccato)] note note note")]
    [InlineData("c''2 <>@accent grace { r16 e''16 } d''2 | c''1 |", "note note[articulations(accent)] note note note")]
    [InlineData("c''2 <>@accent r4 d''4 | c''1 |", "note note[articulations(accent)] note note")]
    [InlineData("c''1 | <>@accent R1 | d''1 |", "note note[articulations(accent)] note")]
    [InlineData("c''1 | <>@fermata R1 | d''1 |", "note note[fermata] note")]
    // …but not a spacer, which is no note: the next note takes them, as the page draws them
    // (owner's decision 2026-11-20; LYS4085 says so). Until session 1331 the invisible rest did.
    [InlineData("c''2 <>@accent s4 d''4 | c''1 |", "note note note[articulations(accent)] note")]
    [InlineData("c''2 <>@accent grace { s16 e''16 } d''2 | c''1 |", "note note note[articulations(accent)] note note")]
    public void InMusicXml(string bars, string expected) => Assert.Equal(expected, Xml(bars));

    /// <summary>The fermata over the closing bar line comes back from <c>lysc import</c> as the
    /// empty chord after the last note it was written as (until session 1329 the import did not
    /// read <c>&lt;barline&gt;&lt;fermata/&gt;</c> and the round trip lost it); one over a bar line
    /// within the piece stays out — that empty chord would put it on the next bar's first note.</summary>
    [Theory]
    [InlineData("c''2 d''2 | c''1 <>@fermata |", "c''1 <>@fermata")]
    [InlineData("c''2 d''2 | c''1 <>@fermata.down |", "c''1 <>@fermata.down")]
    public void TheClosingBarLinesFermata_ComesBackFromTheImport(string bars, string expected)
    {
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(Book(bars))).ToXml().ToString();
        Assert.Contains(expected, new MusicXmlImporter().Import(xml).Item1);
        string within = xml.Replace("</measure>", "<barline location=\"right\"><fermata type=\"upright\"/></barline></measure>");
        string back = new MusicXmlImporter().Import(within).Item1;
        Assert.Equal(1, back.Split("<>@fermata").Length - 1);
    }

    /// <summary>A note's own fermata is a &lt;notations&gt; child, not one of &lt;articulations&gt;:
    /// until session 1328 it was written in there, where a reader keeping to the schema finds none.</summary>
    [Fact]
    public void ANotesFermata_IsANotationsChild()
        => Assert.Equal("note note[fermata,articulations(accent)] note", Xml("c''2 d''2@fermata@accent | c''1 |"));

    private const string TwoSections = "octave absolute\ntime 4/4\npart m { clef treble }\npart n { clef bass }\n"
        + "section A {\n  m { c''2 d''2 <>@fermata | }\n  n { c2 d2 | }\n}\nsection B {\n  m { e''1 | }\n  n { e1 | }\n}\n"
        + "form { A B }\nscore { staff m staff n }\n";

    /// <summary>An empty chord ending a section's block: its scripts are its part's next onset's,
    /// the next section's first — the page's next bar's head (Lab sessions/p1328/end s1) — and the
    /// other part, whose block is written between the two, takes none.</summary>
    [Fact]
    public void AtABlocksEnd_TheScriptsAreThePartsNextOnsets()
    {
        var doc = XDocument.Parse(new MusicXmlExporter().Export(SyntaxTree.Parse(TwoSections)).ToXml().ToString());
        string Part(int k) => string.Join(" ", doc.Descendants("part").ElementAt(k).Descendants("note")
            .Select(n => "note" + (n.Element("notations") is { } x ? "[" + string.Join(",", x.Elements().Select(e => e.Name.LocalName)) + "]" : "")));
        Assert.Equal("note note note[fermata]", Part(0));
        Assert.Equal("note note note", Part(1));
        Assert.DoesNotContain(doc.Descendants("barline"), b => b.Element("fermata") != null);
        var midi = new MidiExporter().Export(SyntaxTree.Parse(TwoSections));
        var notes = midi.Tracks.SelectMany(t => t.Notes).ToList();
        // The fermata holds the next section's first bar's time half again, its note as written
        // (docs/expressive-tempo-design.md, 2026-11-23; until then the note rang 2880).
        Assert.Equal(new[] { 960, 960, 1920 }, notes.Where(n => n.Part == "m").OrderBy(n => n.StartTick).Select(n => n.DurationTicks));
        var tempos = midi.Tracks[0].TempoChanges;
        Assert.Equal(tempos[0].MicrosecondsPerBeat * 3 / 2, tempos.Last(c => c.Tick <= 1920).MicrosecondsPerBeat);
        Assert.Equal(new[] { 960, 960, 1920 }, notes.Where(n => n.Part == "n").OrderBy(n => n.StartTick).Select(n => n.DurationTicks));
    }

    [Fact]
    public void InMidi()
    {
        List<(int Tick, int Length, int Velocity)> Notes(string bars) =>
            new MidiExporter().Export(SyntaxTree.Parse(Book(bars))).Tracks.SelectMany(t => t.Notes)
                .OrderBy(n => n.StartTick).Select(n => (n.StartTick, n.DurationTicks, n.Velocity)).ToList();
        // The empty chord's scripts sound on the next note as written on it.
        foreach (var (onChord, onNote) in new[]
        {
            ("c''2 <>@accent d''2 | c''1 |", "c''2 d''2@accent | c''1 |"),
            ("c''2 <>@fermata d''2 | c''1 |", "c''2 d''2@fermata | c''1 |"),
            ("c''2 <>@staccato@accent d''2 | c''1 |", "c''2 d''2@staccato@accent | c''1 |"),
            ("c''2 d''2 <>@fermata | c''1 |", "c''2 d''2 | c''1@fermata |"),
        })
            Assert.Equal(Notes(onNote), Notes(onChord));
        // …once: the note after it is played as written.
        var once = Notes("c''2 <>@accent d''4 e''4 | c''1 |");
        Assert.Equal(new[] { 90, 110, 90, 90 }, once.Select(n => n.Velocity));
    }

    /// <summary>A grace sounds the scripts the page draws on it — its own and those of an empty
    /// chord before it, whose moment it begins — and the main note after it none of them; a rest
    /// at that moment takes them and sounds nothing. Until session 1329 a grace sounded no script
    /// and the main note took the empty chord's (Lab sessions/p1329/g).</summary>
    [Fact]
    public void InMidi_AGraceOrARestAtItsMomentTakesThem()
    {
        List<(int Tick, int Length, int Velocity)> Notes(string bars) =>
            new MidiExporter().Export(SyntaxTree.Parse(Book(bars))).Tracks.SelectMany(t => t.Notes)
                .OrderBy(n => n.StartTick).ThenBy(n => n.Pitch).Select(n => (n.StartTick, n.DurationTicks, n.Velocity)).ToList();
        var own = Notes("c''2 grace { e''16@accent } d''2 | c''1 |");
        Assert.Equal(new[] { 90, 110, 90, 90 }, own.Select(n => n.Velocity));
        Assert.Equal(own, Notes("c''2 <>@accent grace { e''16 } d''2 | c''1 |"));
        // The first grace of the group, once.
        Assert.Equal(new[] { 90, 110, 90, 90, 90 }, Notes("c''2 <>@accent grace { e''16 f''16 } d''2 | c''1 |").Select(n => n.Velocity));
        // A grace chord's staccato halves both heads' length (27 ticks of an eighth's 9/40 → 13).
        Assert.Equal(new[] { 960, 13, 13, 933, 1920 }, Notes("c''2 <>@staccato grace { <e'' g''>16 } d''2 | c''1 |").Select(n => n.Length));
        Assert.Equal(new[] { 960, 13, 13, 933, 1920 }, Notes("c''2 grace { <e'' g''>16@staccato } d''2 | c''1 |").Select(n => n.Length));
        Assert.Equal(new[] { 90, 110, 110, 90, 90 }, Notes("c''2 <>@accent grace { <e'' g''>16 } d''2 | c''1 |").Select(n => n.Velocity));
        // A rest, a grace's or a full one, takes them: nothing after it is accented.
        Assert.Equal(new[] { 90, 90, 90, 90 }, Notes("c''2 <>@accent grace { r16 e''16 } d''2 | c''1 |").Select(n => n.Velocity));
        Assert.Equal(new[] { 90, 90, 90 }, Notes("c''2 <>@accent r4 d''4 | c''1 |").Select(n => n.Velocity));
        // …a full-bar rest too, as the page draws them over it and MusicXML writes them on it:
        // until session 1359 the next bar's first note took them (Lab sessions/p1359/mmr a1, a4).
        Assert.Equal(new[] { 90, 90 }, Notes("c''1 | <>@accent R1 | d''1 |").Select(n => n.Velocity));
        Assert.Equal(new[] { 90, 90 }, Notes("c''1 | <>@accent R1*2 | d''1 |").Select(n => n.Velocity));
        Assert.Equal(new[] { 90, 95 }, Notes("c''1 | <>@f R1 | d''1 |").Select(n => n.Velocity));
        // A spacer does not: the page draws them on the note after it (LilyPond hangs them on the
        // spacer's moment with no support; Lab sessions/p1329/g sp1).
        Assert.Equal(new[] { 90, 110, 90 }, Notes("c''2 <>@accent s4 d''4 | c''1 |").Select(n => n.Velocity));
        // …nor a grace spacer (owner's decision 2026-11-20): the grace after it.
        Assert.Equal(new[] { 90, 110, 90, 90 }, Notes("c''2 <>@accent grace { s16 e''16 } d''2 | c''1 |").Select(n => n.Velocity));
    }

    private static string[] Warnings(string bars, string code)
        => [.. LilySharp.Core.Semantics.SemanticValidation.Run(SyntaxTree.Parse(Book(bars)))
            .Where(d => d.Code == code).Select(d => d.Message)];

    /// <summary>A script on an empty chord before a spacer is said where it is written (LYS4085,
    /// owner's decision 2026-11-20): it marks no note there and goes on to the next one. A level,
    /// words or a line at a spacer's moment is the ordinary way to place one, and is not.</summary>
    [Theory]
    [InlineData("c''2 <>@accent s4 d''4 | c''1 |", 1)]
    [InlineData("c''2 <>@fermata s2 | c''1 |", 1)]
    [InlineData("c''2 <>@accent@staccato s4 d''4 | c''1 |", 2)]
    [InlineData("c''2. <>@accent | s4 d''2. |", 1)]
    [InlineData("c''2 <>@p s4 d''4 | c''1 |", 0)]
    [InlineData("c''2 <>@segno s4 d''4 | c''1 |", 0)]
    [InlineData("c''2 <>@sustain s4 d''4@!sustain | c''1 |", 0)]
    [InlineData("c''2 <>@accent r4 d''4 | c''1 |", 0)]
    [InlineData("c''2 <>@accent d''2 | c''1 |", 0)]
    public void AScriptBeforeASpacer_IsSaid(string bars, int expected)
        => Assert.Equal(expected, Warnings(bars, "LYS4085").Length);

    /// <summary>A piece's closing <c>&lt;&gt;@fermata</c> is written in the <c>.ly</c> as LilyPond's
    /// fermata over the closing bar line (owner's decision 2026-11-20): <c>&lt;&gt;-\fermata</c>
    /// there draws nothing in LilyPond 2.26.0 (Lab sessions/p1331/ly). Within the piece the empty
    /// chord's fermata stays as it was (LilyPond puts it on the next note), and the end mark is
    /// the score's, written once.</summary>
    [Fact]
    public void TheClosingFermata_IsTheEndMarkInTheLy()
    {
        static string Ly(string text) => new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(text));
        const string Mark = "\\once \\override Score.TextMark.self-alignment-X = #CENTER \\textEndMark \\markup { \\musicglyph \"scripts.ufermata\" }";
        string up = Ly(Book("c''2 d''2 | c''1 <>@fermata |"));
        Assert.Contains("c''1 " + Mark + " |", up);
        Assert.DoesNotContain("<>-\\fermata", up);
        string down = Ly(Book("c''2 d''2 | c''1 <>@fermata.down@accent |"));
        Assert.Contains("<>-\\accent \\once \\override Score.TextMark.self-alignment-X = #CENTER \\once \\override Score.TextMark.direction = #DOWN "
            + "\\textEndMark \\markup { \\musicglyph \"scripts.dfermata\" }", down);
        // Within the piece, and in a second part ending the same way: as before.
        string two = Ly(TwoSections.Replace("e''1 |", "e''1 <>@fermata |").Replace("e1 |", "e1 <>@fermata |"));
        Assert.Contains("d''2 <>-\\fermata", two);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(two, "textEndMark"));
        Assert.Contains("e1 <>-\\fermata", two);
    }
}
