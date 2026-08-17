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
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Music written in a <c>section</c> with no <c>partName { }</c> block around it belongs to
/// the part the <c>score</c> gives it to — its clef anchor, its sounding shift, and its
/// section-boundary reset.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ THE SCORE IS THE ONLY STATEMENT THAT SAYS WHOSE IT IS. The page has always read it
/// that way; the MIDI never read <c>score</c> at all, so a bare section played with no part
/// header — no anchor, no 8vb, and no frame reset at the section boundary. Measured
/// 2026-08-17: `part bl { clef bass }` with bare sections printed C3 and played C4, and a
/// second section printed G4 and played G6.
/// </para>
/// <para>
/// ⚠️ THE PAIR IS "SAME MUSIC, WITH AND WITHOUT THE BLOCK". Every case here has a twin that
/// wraps the identical notes in <c>bl { … }</c>; the twin was already right, so it is the
/// control that says this is attribution and not a pitch rule. A control that also changed
/// would mean the defect was somewhere else entirely.
/// </para>
/// <para>
/// ⚠️ TWO PARTS HAVE NO SINGLE ANSWER and are deliberately left alone: the page draws a bare
/// section on every staff the score names, in each staff's own register, and one MIDI line
/// cannot be two registers. No book in the corpus does it (23 of 566 write bare sections, 0
/// give one to more than one part — measured 2026-08-17), so this is a definition rather
/// than a repair, and the last case here is what pins it.
/// </para>
/// </remarks>
public class BareSectionPartTests
{
    private static int[] Play(string lys)
        => new MidiExporter().Export(SyntaxTree.Parse(lys))
            .Tracks.SelectMany(t => t.Notes).Select(n => n.Pitch).ToArray();

    // Bare: the notes sit directly in the section. Blocked: the same notes inside `bl { }`.
    private static string Bare(string header, string score = "score main { staff bl }") => $$"""
        time 4/4
        part bl { {{header}} }
        section A { c'4 d e f | }
        section B { g'4 f e d | }
        form main { A B }
        {{score}}
        """;

    private static string Blocked(string header) => $$"""
        time 4/4
        part bl { {{header}} }
        section A { bl { c'4 d e f | } }
        section B { bl { g'4 f e d | } }
        form main { A B }
        score main { staff bl }
        """;

    /// <summary>The clef the part reads in sets the octave a bare letter anchors to.</summary>
    [Fact]
    public void ABareSectionOpensInItsPartsRegister()
    {
        Assert.Equal(Play(Blocked("clef bass")), Play(Bare("clef bass")));
        // …and that register is the low one: reading in bass clef the part anchors an
        // octave below treble, so `c'` is C4 (60) where a treble part gives C5 (72).
        Assert.Equal(60, Play(Bare("clef bass"))[0]);
        Assert.Equal(72, Play(Bare("clef treble"))[0]);
    }

    /// <summary>
    /// The section boundary reopens the frame at the part's anchor — the second symptom,
    /// and the one that stayed hidden while both books ran off the top of the MIDI range.
    /// </summary>
    [Fact]
    public void EachBareSectionReopensTheFrameAtThePartsAnchor()
    {
        int[] played = Play(Bare("clef treble"));

        Assert.Equal(8, played.Length);
        // Section B opens with `g'`: from the part's anchor (C4) the nearest g is G3, and
        // the mark lifts it to G4. Continuing from section A's last note (F5) would give G6.
        Assert.Equal(67, played[4]);
        Assert.Equal(Play(Blocked("clef treble")), played);
    }

    /// <summary>What a transposing part SOUNDS, which is a third thing the part header
    /// carries and the bare section never got.</summary>
    [Fact]
    public void ABareSectionSoundsItsPartsTransposition()
    {
        // A bass guitar prints in bass clef and sounds an octave below it.
        int[] played = Play(Bare("clef bass tuning bass"));

        Assert.Equal(Play(Blocked("clef bass tuning bass")), played);
        Assert.Equal(48, played[0]);       // printed C4, sounding C3
        // The shift is the whole of the difference from the same part without the tuning.
        Assert.Equal(Play(Bare("clef bass"))[0] - 12, played[0]);
    }

    /// <summary>A file with no <c>score</c> block has nobody to ask, and plays as it
    /// always did.</summary>
    [Fact]
    public void WithNoScoreNothingIsAttributed()
    {
        string lys = """
            time 4/4
            part bl { clef bass }
            section A { c'4 d e f | }
            form main { A }
            """;

        Assert.Equal(72, Play(lys)[0]);    // the default anchor, not the bass clef's
    }

    /// <summary>
    /// Two parts on one bare section: no single register, so it stays as it was. The
    /// definition, not a repair — nothing in the corpus reaches it.
    /// </summary>
    [Fact]
    public void TwoPartsLeaveItUnattributed()
    {
        string lys = """
            time 4/4
            part bl { clef bass }
            part tr { clef treble }
            section A { c'4 d e f | }
            form main { A }
            score main { staff bl  staff tr }
            """;

        Assert.Equal(72, Play(lys)[0]);    // neither part's register: the default one
    }

    /// <summary>
    /// One part on two staves is still ONE part — the case that makes the rule "distinct
    /// parts", not "staff count" (`staff bl  tab bl` is the shape two fixtures use).
    /// </summary>
    [Fact]
    public void OnePartOnTwoStavesIsStillOnePart()
    {
        int[] played = Play(Bare("clef bass tuning bass", "score main { staff bl  tab bl }"));

        Assert.Equal(48, played[0]);
    }

    /// <summary>
    /// The MusicXML reads the score the same way, and had the same hole in a different
    /// half: it reset the frame at each section but emitted a bare section under the name
    /// "Part 1", which no declaration answers — so it kept the default anchor and no
    /// transposition whatever the part it is drawn on says.
    /// </summary>
    [Theory]
    [InlineData("clef bass", "C4")]                    // the clef's own register
    [InlineData("clef treble", "C5")]                  // …and the control that is not it
    [InlineData("clef bass tuning bass", "C4")]        // written pitch: the shift is <transpose>
    public void TheMusicXmlAttributesABareSectionTheSameWay(string header, string firstPitch)
    {
        static (string Pitch, string Part) Read(string lys)
        {
            var doc = new MusicXmlExporter().Export(SyntaxTree.Parse(lys));
            var first = doc.Parts.SelectMany(p => p.Measures).SelectMany(m => m.Notes)
                .First(n => n.Step != null);
            return (first.Step + first.Octave!.Value.ToString(), doc.Parts[0].Name);
        }

        var bare = Read(Bare(header));
        Assert.Equal(firstPitch, bare.Pitch);
        // Same music inside `bl { }` blocks — the control that says this is attribution.
        Assert.Equal(Read(Blocked(header)), bare);
        // …and the document now says whose part it is, rather than inventing "Part 1".
        Assert.Equal("bl", bare.Part);
    }

    /// <summary>A transposing part states its shift once, in <c>&lt;transpose&gt;</c>, so
    /// the written pitch above is the same with and without the tuning — the sounding one
    /// is not.</summary>
    [Fact]
    public void TheBareSectionsTranspositionReachesTheDocument()
    {
        var doc = new MusicXmlExporter().Export(
            SyntaxTree.Parse(Bare("clef bass tuning bass")));

        var transpose = doc.Parts.SelectMany(p => p.Measures)
            .Select(m => m.Attributes?.TransposeSemitones)
            .FirstOrDefault(t => t != null);
        Assert.Equal(-12, transpose);
    }
}
