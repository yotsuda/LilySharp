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
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A section reference's trailing octave marks (<c>~B'</c> / <c>B,</c> / <c>[1. B']</c>)
/// move the frame THAT PLAY opens in — and all four readers of the music have to say so.
/// </summary>
/// <remarks>
/// <para>
/// A section boundary reopens the relative frame at the part's anchor and reverts the
/// octave mode; the reset stays and the CARRY is given by notation (user decision,
/// 2026-08-31, HANDOFF §3), so a section whose music belongs an octave away says so on the
/// REFERENCE. It is per play: the same section quoted twice can open at two octaves, and
/// the declaration never moves — a shift written on the declaration would change B at every
/// call site, which is the bug the reset was introduced to fix.
/// </para>
/// <para>
/// ⚠️ ONE PAIR OF BOOKS, READ FOUR WAYS, IN ONE FILE — and one test METHOD per reader, the
/// shape <c>SectionBoundaryMeterRevertTests</c> settled on (RULES §5.4). Splitting the file
/// per exporter is what lets the next reader added drift; splitting the METHODS is what lets
/// a poison say WHICH reader it cut, since the red set is read by test name.
/// </para>
/// <para>
/// ⚠️ The shape of each is an IDENTITY PAIR: <c>~B'</c> against a control that writes B an
/// octave up and quotes it plainly. The two must agree, and both must DIFFER from the
/// unshifted play — without that second half every assertion here would pass on a reader
/// that ignores the marks entirely.
/// </para>
/// <para>
/// ⚠️ AND EVERY CASE RUNS IN BOTH OCTAVE MODES, because the modes read different anchors:
/// relative resolves nearest to the running octave, absolute measures from a fixed base.
/// Measured 2026-08-31 before the notation was built: 283 of the author's 326 books are
/// written <c>octave absolute</c>, and 52 of the 75 that have more than one section — so a
/// mark that worked only in relative mode would be a silent drop in the majority of the
/// books it exists for.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class SectionReferenceOctaveTests
{
    /// <param name="mode">"" for relative (the default), "octave absolute" for the other.</param>
    /// <param name="bodyB">Section B's music — the control writes the octave into it.</param>
    /// <param name="form">The form body, where the marks are written.</param>
    private static string Book(string mode, string bodyB, string form) => $$"""
        {{mode}}
        time 4/4
        part m {
          section A { c'4 d e f | }
          section B { {{bodyB}} }
          section C { e'4 d c b | }
        }
        form main { {{form}} }
        score main { staff m }
        """;

    /// <summary>Section B, written an octave up in the source instead of at the reference.
    /// Relative resolves the bare letters nearest the reset anchor; absolute measures each
    /// from a fixed base, so every letter carries the mark. Same music, twice.</summary>
    private static string UpOctave(string mode, int octaves)
    {
        string m = new string('\'', octaves);
        return mode == "" ? $"g{m}4 a b c |" : $"g{m}4 a{m} b{m} c{m} |";
    }

    private static string DownOctave(string mode) =>
        mode == "" ? "g,4 a b c |" : "g,4 a, b, c, |";

    // ===== the four readings =====

    /// <summary>Every note's clef-relative staff position, in diatonic steps. An octave is
    /// seven of them, so this is the page's own witness to the shift.</summary>
    private static int[] PageSteps(string lys) =>
        new LilySharp.Core.Svg.Collector.MeasureCollector()
            .Collect(SyntaxTree.Parse(lys), "m")
            .Voice.Measures.SelectMany(m => m.Items)
            .OfType<LilySharp.Core.Svg.Model.NoteItem>()
            .Select(n => n.StaffPosition).ToArray();

    private static string Twin(string lys) => new LilyPondExporter().Export(SyntaxTree.Parse(lys));

    private static int[] MidiPitches(string lys) =>
        new MidiExporter().Export(SyntaxTree.Parse(lys))
            .Tracks.SelectMany(t => t.Notes).OrderBy(n => n.StartTick).ThenBy(n => n.Pitch)
            .Select(n => n.Pitch).ToArray();

    private static string[] XmlPitches(string lys) =>
        new MusicXmlExporter().Export(SyntaxTree.Parse(lys)).ToXml()
            .Descendants("pitch")
            .Select(p => (string)p.Element("step")! + (string)p.Element("octave")!)
            .ToArray();

    // ===== reader 1: the page =====

    [Theory]
    [InlineData("")]                  // relative — the default
    [InlineData("octave absolute")]
    public void ThePageEngravesTheMarkedPlayAnOctaveUp(string mode)
    {
        var marked = PageSteps(Book(mode, "g4 a b c |", "~A ~B'"));
        Assert.Equal(PageSteps(Book(mode, UpOctave(mode, 1), "~A ~B")), marked);
        Assert.NotEqual(PageSteps(Book(mode, "g4 a b c |", "~A ~B")), marked);
    }

    // ===== reader 2: the LilyPond twin =====

    [Theory]
    [InlineData("")]
    [InlineData("octave absolute")]
    public void TheLilyPondTwinReopensAtTheShiftedAnchor(string mode)
    {
        // A twin that compiles and is a different piece is the defect class this exporter's
        // warning channel exists for, so the bar is byte equality with the control's .ly.
        var marked = Twin(Book(mode, "g4 a b c |", "~A ~B'"));
        Assert.Equal(Twin(Book(mode, UpOctave(mode, 1), "~A ~B")), marked);
        Assert.NotEqual(Twin(Book(mode, "g4 a b c |", "~A ~B")), marked);
    }

    // ===== reader 3: the sound =====

    [Theory]
    [InlineData("")]
    [InlineData("octave absolute")]
    public void TheMarkedPlaySoundsAnOctaveUp(string mode)
    {
        var marked = MidiPitches(Book(mode, "g4 a b c |", "~A ~B'"));
        Assert.Equal(MidiPitches(Book(mode, UpOctave(mode, 1), "~A ~B")), marked);
        Assert.NotEqual(MidiPitches(Book(mode, "g4 a b c |", "~A ~B")), marked);
    }

    // ===== reader 4: the MusicXML =====

    [Theory]
    [InlineData("")]
    [InlineData("octave absolute")]
    public void TheMusicXmlWritesTheMarkedPlayAnOctaveUp(string mode)
    {
        var marked = XmlPitches(Book(mode, "g4 a b c |", "~A ~B'"));
        Assert.Equal(XmlPitches(Book(mode, UpOctave(mode, 1), "~A ~B")), marked);
        Assert.NotEqual(XmlPitches(Book(mode, "g4 a b c |", "~A ~B")), marked);
    }

    // ===== the rest of the spelling, read by all four at once =====

    /// <summary>The whole assertion, once, for the cases whose question is the SPELLING
    /// rather than a reader: all four outputs agree with the control, and none of them
    /// agrees with the unshifted play.</summary>
    private static void AllFourReadersAgree(string marked, string control, string unshifted)
    {
        Assert.Equal(PageSteps(control), PageSteps(marked));
        Assert.Equal(Twin(control), Twin(marked));
        Assert.Equal(MidiPitches(control), MidiPitches(marked));
        Assert.Equal(XmlPitches(control), XmlPitches(marked));

        Assert.NotEqual(PageSteps(unshifted), PageSteps(marked));
        Assert.NotEqual(MidiPitches(unshifted), MidiPitches(marked));
        Assert.NotEqual(XmlPitches(unshifted), XmlPitches(marked));
    }

    [Theory]
    [InlineData("")]
    [InlineData("octave absolute")]
    public void AComma_OpensThePlayAnOctaveDown(string mode)
        => AllFourReadersAgree(
            marked: Book(mode, "g4 a b c |", "~A ~B,"),
            control: Book(mode, DownOctave(mode), "~A ~B"),
            unshifted: Book(mode, "g4 a b c |", "~A ~B"));

    [Theory]
    [InlineData("")]
    [InlineData("octave absolute")]
    public void TwoMarksAreTwoOctaves(string mode)
        => AllFourReadersAgree(
            marked: Book(mode, "g4 a b c |", "~A ~B''"),
            control: Book(mode, UpOctave(mode, 2), "~A ~B"),
            unshifted: Book(mode, "g4 a b c |", "~A ~B"));

    /// <summary>The tilde hides the LABEL, never the music — so the marks mean the same on
    /// both spellings, and the only difference between these two books is the mark.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("octave absolute")]
    public void APlainReferenceTakesTheSameMarksAsASilentOne(string mode)
    {
        string plain = Book(mode, "g4 a b c |", "~A B'");
        string silent = Book(mode, "g4 a b c |", "~A ~B'");
        Assert.Equal(PageSteps(silent), PageSteps(plain));
        Assert.Equal(MidiPitches(silent), MidiPitches(plain));
        Assert.Equal(XmlPitches(silent), XmlPitches(plain));
        Assert.Contains("\\mark", Twin(plain));
    }

    /// <summary>An ending IS a section reference with a bracket around it, marks included.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("octave absolute")]
    public void AMarkedEnding_OpensItsPlayAnOctaveUp(string mode)
        => AllFourReadersAgree(
            marked: Book(mode, "g4 a b c |", "|: ~A [1. B' ] :| [2. C ]"),
            control: Book(mode, UpOctave(mode, 1), "|: ~A [1. B ] :| [2. C ]"),
            unshifted: Book(mode, "g4 a b c |", "|: ~A [1. B ] :| [2. C ]"));

    /// <summary>
    /// An ending's RANGE SEPARATOR is a comma too, and it is not an octave mark.
    /// </summary>
    /// <remarks>
    /// <c>[1,3. B]</c> holds a Comma TOKEN as a direct child of the ending node, standing
    /// before the section name. The shared counter scans a node's direct <c>'</c>/<c>,</c>
    /// children, which is right for the other six reference shapes and would read this one
    /// as "an octave down" — so the ending counts from the section-name slot instead
    /// (<c>SyntaxFacts.NetOctaveMarksFrom</c>). Without that, every ranged ending in the
    /// tree would have dropped an octave the day the notation landed.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("octave absolute")]
    public void ARangedEndingIsNotShifted_TheSeparatorCommaIsNotAMark(string mode)
    {
        string ranged = Book(mode, "g4 a b c |", "|: ~A [1,3. B ] :| [2. C ]");
        string plain = Book(mode, "g4 a b c |", "|: ~A [1. B ] :| [2. C ]");
        Assert.Equal(MidiPitches(plain), MidiPitches(ranged));
        Assert.Equal(XmlPitches(plain), XmlPitches(ranged));
        Assert.Equal(PageSteps(plain), PageSteps(ranged));

        // …and a ranged ending can still be marked, which is the other half of the same slot
        // question: the separator is at a fixed index, the marks come after the name.
        Assert.NotEqual(MidiPitches(ranged),
            MidiPitches(Book(mode, "g4 a b c |", "|: ~A [1,3. B' ] :| [2. C ]")));
    }

    /// <summary>
    /// A DOUBLY marked ending — <c>[1. B'']</c> — is two octaves, not a range.
    /// </summary>
    /// <remarks>
    /// The case that made <c>HasSeparator</c> stop counting slots. Without a separator an
    /// ending holds seven slots plus its marks, so <c>[1. B'']</c> is the FIRST spelling to
    /// reach nine without one — and the old <c>SlotCount == 9</c> test then read the <c>.</c>
    /// as the range separator and the second apostrophe as the section name. Nothing else in
    /// this file reaches nine slots that way, so without this case the repair had no observer
    /// at all (measured: poisoning it back to <c>SlotCount == 9</c> left the whole suite green).
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("octave absolute")]
    public void ADoublyMarkedEndingIsTwoOctaves_NotARange(string mode)
        => AllFourReadersAgree(
            marked: Book(mode, "g4 a b c |", "|: ~A [1. B'' ] :| [2. C ]"),
            control: Book(mode, UpOctave(mode, 2), "|: ~A [1. B ] :| [2. C ]"),
            unshifted: Book(mode, "g4 a b c |", "|: ~A [1. B ] :| [2. C ]"));

    /// <summary>The label still reads after the marks — it is found by KIND now, not at a
    /// fixed slot, on the reference and on the ending alike.</summary>
    [Fact]
    public void AMarkedReferenceAndEndingStillCarryTheirLabel()
    {
        Assert.Contains("reprise", Twin(Book("", "g4 a b c |", "~A B' \"reprise\"")));
        Assert.Contains("late", Twin(Book("", "g4 a b c |", "|: ~A [1. B, \"late\" ] :| [2. C ]")));
    }

    /// <summary>
    /// <c>~B ~B'</c> is one section at two octaves, and <c>~B' ~B</c> puts the second play
    /// back where it started.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE SECOND HALF IS THE ONE THAT NEEDED A FIELD, and it is the PAGE's. In absolute
    /// mode the shift moves the base a bare letter is measured from, and nothing used to
    /// restore that base at a boundary — every mutation of it was balanced, so the reset was
    /// an identity and was never written. A marked reference is the first unbalanced one:
    /// without <c>OctaveContext.InitialOctaveBase</c>, <c>~B'</c> leaves every LATER section
    /// an octave high. The exporters cannot show it — they re-arm their anchors from the part
    /// header at every play — so the page reading below is the whole of the observation.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("octave absolute")]
    public void TheShiftBelongsToThePlay_AndTheNextPlayIsBackAtTheAnchor(string mode)
    {
        var twice = MidiPitches(Book(mode, "g4 a b c |", "~B ~B'"));
        Assert.Equal(8, twice.Length);
        for (int i = 0; i < 4; i++)
            Assert.Equal(twice[i] + 12, twice[i + 4]);

        var back = MidiPitches(Book(mode, "g4 a b c |", "~B' ~B"));
        Assert.Equal(8, back.Length);
        for (int i = 0; i < 4; i++)
            Assert.Equal(back[i] - 12, back[i + 4]);

        // Seven staff steps to the octave; this pair is what catches a base that leaks.
        var pageTwice = PageSteps(Book(mode, "g4 a b c |", "~B ~B'"));
        Assert.Equal(8, pageTwice.Length);
        for (int i = 0; i < 4; i++)
            Assert.Equal(pageTwice[i] + 7, pageTwice[i + 4]);

        var pageBack = PageSteps(Book(mode, "g4 a b c |", "~B' ~B"));
        Assert.Equal(8, pageBack.Length);
        for (int i = 0; i < 4; i++)
            Assert.Equal(pageBack[i] - 7, pageBack[i + 4]);

        // …and a THIRD, DIFFERENT section after the marked play opens at the anchor too.
        Assert.Equal(MidiPitches(Book(mode, "g4 a b c |", "~B ~C")).Skip(4).ToArray(),
                     MidiPitches(Book(mode, "g4 a b c |", "~B' ~C")).Skip(4).ToArray());
        Assert.Equal(PageSteps(Book(mode, "g4 a b c |", "~B ~C")).Skip(4).ToArray(),
                     PageSteps(Book(mode, "g4 a b c |", "~B' ~C")).Skip(4).ToArray());
    }

    /// <summary>
    /// A section whose music is a PHRASE REFERENCE moves with the mark too — writing the
    /// notes out and quoting a phrase are the same section.
    /// </summary>
    /// <remarks>
    /// ⚠️ MEASURED 2026-08-31, AND THE TWO MODES HAD ALREADY PARTED. A phrase body opens a
    /// FRESH frame at the voice's anchor so that a phrase means the same notes at every call
    /// site — and that anchor has to be the SECTION's, not the part's, or <c>~B'</c> would
    /// mean "an octave up, unless the section happens to be written as a reference". Absolute
    /// mode moved it already (the reference pushes <c>OctaveBase</c> on top of the shifted
    /// base); relative did not (<c>ResetToInitial</c> went back to <c>InitialOctave</c> and
    /// dropped the play's shift). ONE BOOK, TWO ANSWERS, decided by <c>octave absolute</c> —
    /// which is why the fix is one field read by one line
    /// (<c>OctaveContext.SectionOctaveOffset</c>) rather than an arm per mode.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("octave absolute")]
    public void APhraseBodyInsideAMarkedSectionMovesWithIt(string mode)
    {
        static string WithPhrase(string mode, string form) => $$"""
            {{mode}}
            time 4/4
            phrase P { g4 a b c | }
            part m {
              section A { c'4 d e f | }
              section B { P }
              section C { e'4 d c b | }
            }
            form main { {{form}} }
            score main { staff m }
            """;

        // Written out, and quoted: the same section, so the same pitches — with the mark and
        // without it.
        Assert.Equal(MidiPitches(Book(mode, "g4 a b c |", "~A ~B")),
                     MidiPitches(WithPhrase(mode, "~A ~B")));
        Assert.Equal(MidiPitches(Book(mode, "g4 a b c |", "~A ~B'")),
                     MidiPitches(WithPhrase(mode, "~A ~B'")));
        Assert.Equal(PageSteps(Book(mode, "g4 a b c |", "~A ~B'")),
                     PageSteps(WithPhrase(mode, "~A ~B'")));
        Assert.Equal(XmlPitches(Book(mode, "g4 a b c |", "~A ~B'")),
                     XmlPitches(WithPhrase(mode, "~A ~B'")));
        // …and the mark actually did something, so the equalities are not vacuous.
        Assert.NotEqual(MidiPitches(WithPhrase(mode, "~A ~B")),
                        MidiPitches(WithPhrase(mode, "~A ~B'")));
        // ⚠️ The TWIN is asserted separately and only as "it moved". Its two books are not
        // byte-comparable — one inlines the notes, the other emits a nested block — and the
        // ONE line that decides the block's octave is its reference pitch: measured
        // 2026-08-31, shifting the buffer's two frames by the same amount cancels in every
        // written mark, so the frame assignment beside it is bookkeeping and this is what
        // actually observes the reference pitch.
        Assert.NotEqual(Twin(WithPhrase(mode, "~A ~B")), Twin(WithPhrase(mode, "~A ~B'")));
    }

    /// <summary>
    /// A SLASH NOTE does not move: it stands on the clef's middle line and carries no pitch
    /// to shift.
    /// </summary>
    /// <remarks>
    /// A deliberate narrowing rather than an oversight, and one both sides make: the
    /// collector reads the clef for a slash, never the octave frame, and the twin writes
    /// <c>\improvisationOn</c> around the same middle-line pitch. Measured 2026-08-31 —
    /// <c>section B { /4 4 4 4 | }</c> played as <c>~B</c> and as <c>~B'</c> gives the same
    /// page (data-pos suppressed) and the same <c>b,4</c> in the .ly. The line exists so
    /// that a future reader who adds <c>_absoluteSectionOctave</c> to the twin's slash arm
    /// finds out that the page does not agree.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("octave absolute")]
    public void ASlashNoteDoesNotMove_ItStandsOnTheClefsMiddleLine(string mode)
    {
        Assert.Equal(PageSteps(Book(mode, "/4 4 4 4 |", "~A ~B")),
                     PageSteps(Book(mode, "/4 4 4 4 |", "~A ~B'")));
        Assert.Equal(Twin(Book(mode, "/4 4 4 4 |", "~A ~B")),
                     Twin(Book(mode, "/4 4 4 4 |", "~A ~B'")));
    }

    /// <summary>An unmarked form is what it was before the notation existed: the offset is
    /// zero and every reader's arm is a no-op.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("octave absolute")]
    public void AnUnmarkedFormIsUntouched(string mode)
    {
        string silent = Book(mode, "g4 a b c |", "~A ~B ~C");
        string bare = Book(mode, "g4 a b c |", "A B C");
        Assert.Equal(MidiPitches(bare), MidiPitches(silent));
        Assert.Equal(PageSteps(bare), PageSteps(silent));
        Assert.Equal(XmlPitches(bare), XmlPitches(silent));
    }
}
