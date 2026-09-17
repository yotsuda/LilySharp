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
using LilySharp.Core.MusicXml;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// A section's HEADER — its own <c>key</c>, <c>time</c> and <c>partial</c>, written beside
/// the part blocks (section-major) or as a standalone <c>section A { partial 8 }</c> beside
/// the parts' cells (part-major) — reaches every part's play of the section in the MusicXML,
/// the way it reaches the page, the MIDI and the LilyPond twin: through a registry keyed by
/// the section's NAME (HANDOFF §2 R2, session 398).
/// </summary>
/// <remarks>
/// Until session 398 the exporter read the header off the declaration in hand, so the
/// standalone header of a part-major book reached nothing (the cell is a different
/// declaration), and the header declaration itself was emitted as music — an EMPTY
/// <c>&lt;part/&gt;</c> under "Part 1" when no single engraved part owned it, which the schema
/// forbids and the importer cannot read back. The pickup never reached the document at all:
/// <c>test/chord-flag</c> exported its pickup as a full bar 1.
/// </remarks>
public class MusicXmlSectionHeaderTests
{
    private static MusicXmlDocument Export(string source)
        => new MusicXmlExporter().Export(SyntaxTree.Parse(source));

    /// <summary>The differential: the header spelling of a pickup and the inline spelling
    /// of the same music write the same measures — the same implicit bar 0 with the pickup
    /// note alone, the same bar 1 with the four crotchets.</summary>
    [Fact]
    public void HeaderPartial_SectionMajor_WritesTheSameMeasuresAsTheInlineSpelling()
    {
        var inline = Export("octave absolute  time 4/4  partial 8  f'8 | g'4 a' b' c'' |");
        var header = Export("""
            octave absolute
            time 4/4
            section A { partial 8  melody { f'8 | g'4 a' b' c'' | } }
            form main { A }
            score main { staff melody }
            """);

        var inlineMeasures = inline.Parts.Single().Measures.Select(m => m.ToXml().ToString()).ToList();
        var headerMeasures = header.Parts.Single().Measures.Select(m => m.ToXml().ToString()).ToList();
        // The differential is only a claim if the inline spelling itself is the pickup.
        Assert.Equal(2, inlineMeasures.Count);
        Assert.Contains("implicit=\"yes\"", inlineMeasures[0]);
        Assert.Equal(inlineMeasures, headerMeasures);
    }

    /// <summary>A part-major book whose score engraves TWO parts: the standalone header owns
    /// no part, so it must open none — and its pickup is every part's first bar.</summary>
    [Fact]
    public void HeaderPartial_PartMajorStandaloneHeader_ReachesEveryPart_AndOpensNoPartOfItsOwn()
    {
        var doc = Export("""
            key g major
            section A { partial 8 }
            part melody { section A { f'8 | g'4 a' b' c'' | } }
            part bass { clef bass  section A { d8 | g,4 a, b, c | } }
            form main { A }
            score main { staff melody  staff bass }
            """);

        Assert.Equal(new[] { "melody", "bass" }, doc.Parts.Select(p => p.Name));
        Assert.All(doc.Parts, part =>
        {
            Assert.Equal(2, part.Measures.Count);
            Assert.True(part.Measures[0].Implicit);
            Assert.Equal(0, part.Measures[0].Number);
            Assert.Single(part.Measures[0].Notes);       // the pickup quaver alone
            Assert.Equal(1, part.Measures[1].Number);
            Assert.Equal(4, part.Measures[1].Notes.Count);
        });
        // The schema's side of it: no <part> without a measure anywhere in the document.
        var xml = doc.ToXml().ToString();
        Assert.DoesNotContain("<part id=\"P1\" />", xml);
        Assert.DoesNotContain("Part 1", xml);
    }

    /// <summary>The same registry carries the header's key and time to a part-major cell.</summary>
    [Fact]
    public void HeaderKeyAndTime_PartMajorStandaloneHeader_ReachTheCell()
    {
        var doc = Export("""
            key c major
            time 4/4
            section A { key d major  time 3/4 }
            part melody { section A { d'4 e' fis' | g'2. | } }
            form main { A }
            score main { staff melody }
            """);

        var first = doc.Parts.Single().Measures[0].ToXml().ToString();
        Assert.Contains("<fifths>2</fifths>", first);
        Assert.Contains("<beats>3</beats>", first);
        Assert.Contains("<beat-type>4</beat-type>", first);
        Assert.Equal(2, doc.Parts.Single().Measures.Count);
    }

    /// <summary>The header's tempo, by the same registry: the piece's opening tempo when the
    /// section opens the part, a metronome direction at the section's start when it does not.
    /// (Until session 398 it reached the document only because the standalone header was
    /// walked as music — the bass corpus's test.lys, <c>section A { tempo 110 }</c>.)</summary>
    [Fact]
    public void HeaderTempo_PartMajorStandaloneHeader_OpensThePiece_OrMarksTheSectionStart()
    {
        const string book = """
            octave absolute
            section A { tempo 110 }
            part melody { section A { c'4 d' e' f' | }  section B { g'1 | } }
            form main { B A }
            score main { staff melody }
            """;
        var measures = Export(book).Parts.Single().Measures;
        Assert.Equal(2, measures.Count);
        // B opens the piece at the file default; A's start carries its own mark.
        Assert.Contains("<per-minute>120</per-minute>", measures[0].ToXml().ToString());
        Assert.Contains("<per-minute>110</per-minute>", measures[1].ToXml().ToString());

        var opening = Export(book.Replace("form main { B A }", "form main { A B }")).Parts.Single().Measures;
        Assert.Contains("<per-minute>110</per-minute>", opening[0].ToXml().ToString());
        Assert.DoesNotContain("<per-minute>120</per-minute>", opening[0].ToXml().ToString());
    }

    /// <summary>The line between a header and an EMPTY cell: a directives-only section INSIDE
    /// a part is that part's play of the section and still opens the part (an empty chord
    /// chart in the bass corpus is written exactly so); only a top-level one is a header.</summary>
    [Fact]
    public void AnEmptyPartMajorCell_IsNotAHeader_AndStillOpensItsPart()
    {
        var doc = Export("""
            part bassline { clef bass  section Body { } }
            form main { Body }
            score main { staff bassline }
            """);

        var part = Assert.Single(doc.Parts);
        Assert.Equal("bassline", part.Name);
        Assert.Empty(part.Measures);
    }

    /// <summary>A pickup later in the part is implicit but COUNTED, as the page counts it:
    /// only a leading pickup is bar 0 (Measure.IsPickup). Before session 398 every pickup
    /// restarted the numbering, so the bars after a mid-piece <c>partial</c> ran 0, 1, 2 again.</summary>
    [Fact]
    public void MidPiecePickup_KeepsItsNumber_LikeThePage()
    {
        var measures = Export("octave absolute  time 4/4  c'1 | partial 4  d'4 | e'1 |").Parts.Single().Measures;

        Assert.Equal(new[] { 1, 2, 3 }, measures.Select(m => m.Number));
        Assert.Equal(new[] { false, true, false }, measures.Select(m => m.Implicit));
        Assert.Single(measures[1].Notes);
    }
}
