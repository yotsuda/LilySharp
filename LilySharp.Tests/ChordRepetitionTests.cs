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

using LilySharp.Core.LilyPond;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The chord repetition <c>q</c>: the previous <c>&lt;&gt;</c> chord's notes with the
/// repetition's own duration and post-events.
/// LILYPOND-REF: scm/music-functions.scm:854-946 copy-repeat-chord +
/// expand-repeat-chords! — note events only are copied (no articulations or
/// fingerings), the duration is the repetition's, cautionary/forced accidentals are
/// cleared, and the expansion runs AFTER \relative resolution
/// (ly/music-functions-init.ly:2143 toplevel-music-functions), so a q copies the
/// original chord's ABSOLUTE pitches and is transparent to the relative frame.
/// Paired with the LP regression books chord-repetition{,-relative,-script-stack,
/// -times,-accidentals}.ly.
/// </summary>
[Trait("Category", "Unit")]
public class ChordRepetitionTests
{
    private static List<MusicItem> Items(string source)
    {
        var collector = new MeasureCollector();
        var score = collector.Collect(SyntaxTree.Parse(source), null);
        return score.Voice.Measures.SelectMany(m => m.Items).ToList();
    }

    [Fact]
    public void Repetition_CopiesThePreviousChordsPitches()
    {
        var items = Items("<c' e g>4 q q q");
        var chords = items.OfType<ChordItem>().ToList();
        Assert.Equal(4, chords.Count);
        var original = chords[0].Notes.Select(n => n.Midi).ToArray();
        foreach (var copy in chords.Skip(1))
            Assert.Equal(original, copy.Notes.Select(n => n.Midi).ToArray());
    }

    [Fact]
    public void Repetition_TakesItsOwnDuration_AndFeedsTheCarry()
    {
        // LILYPOND-REF: scm/music-functions.scm:890-891 copy-repeat-chord — any
        // duration on the copied notes is replaced with the repetition's.
        var items = Items("<c' e g>8 q q4 q");
        var chords = items.OfType<ChordItem>().ToList();
        Assert.Equal(
            new[] { 8, 8, 4, 4 },
            chords.Select(c => (int)c.Duration.Denominator).ToArray());
    }

    [Fact]
    public void Repetition_DoesNotCopyFingeringsOrScripts()
    {
        // The original's per-pitch fingerings are note-event ARTICULATIONS — LP
        // filters them out of the copy (copy-repeat-chord's keep-element?).
        var items = Items("<c'@finger(1) e@finger(3) g@finger(5)>8 q");
        var chords = items.OfType<ChordItem>().ToList();
        Assert.Equal(2, chords.Count);
        Assert.Equal(new int?[] { 1, 3, 5 }, chords[0].Notes.Select(n => n.Fingering).ToArray());
        Assert.All(chords[1].Notes, n => Assert.Null(n.Fingering));
    }

    [Fact]
    public void Repetition_IsTransparentToTheRelativeFrame()
    {
        // LP expands q AFTER \relative has resolved, so the note following a q
        // is relative to the note BEFORE the q, not to the repeated chord's
        // anchor. The walked-away frame (d, a seventh below the anchor c') makes
        // the two readings land the trailing a in different octaves.
        var withQ = Items("<c' e g>4 g8 d8 q a8");
        var withoutQ = Items("<c' e g>4 g8 d8 a8");
        int aAfterQ = withQ.OfType<NoteItem>().Last().Midi;
        int aPlain = withoutQ.OfType<NoteItem>().Last().Midi;
        Assert.Equal(aPlain, aAfterQ);

        // And the repeated chord itself keeps the ORIGINAL's absolute pitches
        // (regression chord-repetition-relative: same octaves as the original).
        var chords = withQ.OfType<ChordItem>().ToList();
        Assert.Equal(
            chords[0].Notes.Select(n => n.Midi).ToArray(),
            chords[1].Notes.Select(n => n.Midi).ToArray());
    }

    [Fact]
    public void Repetition_RederivesAccidentals_InsteadOfCopyingInk()
    {
        // The copy runs through the normal measure-local accidental state: the
        // original prints the sharp, the repeat in the same measure does not
        // (a verbatim ink copy would print it again).
        var items = Items("<fis' a c>4 q q q");
        var chords = items.OfType<ChordItem>().ToList();
        Assert.Equal("sharp", chords[0].Notes[0].Accidental);
        foreach (var copy in chords.Skip(1))
            Assert.Null(copy.Notes[0].Accidental);
    }

    [Fact]
    public void Repetition_OmitsTheOriginalsCourtesyAccidentals()
    {
        // Regression chord-repetition-accidentals: repeats omit reminder (and
        // forced) accidentals — LP clears cautionary/force-accidental on the
        // copy (copy-repeat-chord :892-895). Lily#'s @courtesy is a mark, and a
        // q copies note events only, so the omission falls out structurally.
        // (LP's bare f! has no Lily# spelling — LYS4009 — so only the f? half
        // of the regression book is written; the corpus twin drops the f!
        // measure from both sides.)
        var items = Items("<f@courtesy a d f'@courtesy>4 q q q");
        var chords = items.OfType<ChordItem>().ToList();
        Assert.Equal(4, chords.Count);
        Assert.True(chords[0].Notes[0].IsCourtesy);
        Assert.Equal("natural", chords[0].Notes[0].Accidental);
        Assert.True(chords[0].Notes[3].IsCourtesy);
        foreach (var copy in chords.Skip(1))
            Assert.All(copy.Notes, n =>
            {
                Assert.False(n.IsCourtesy);
                Assert.Null(n.Accidental);
            });
    }

    [Fact]
    public void Repetition_InsideTuplet_TakesTheTupletScale()
    {
        // Regression chord-repetition-times: repetitions are expanded late, so
        // \tuplet still applies to them.
        var items = Items("time 2/4 <c' e g>4 r4 | tuplet 3/2 { <c' e g>4 q q }");
        var scaled = items.OfType<ChordItem>().Where(c => c.TimeScale != new Fraction(1, 1)).ToList();
        Assert.Equal(3, scaled.Count);
        Assert.All(scaled, c => Assert.Equal(new Fraction(2, 3), c.TimeScale));
        // All three carry the original's pitches.
        var original = items.OfType<ChordItem>().First().Notes.Select(n => n.Midi).ToArray();
        Assert.All(scaled, c => Assert.Equal(original, c.Notes.Select(n => n.Midi).ToArray()));
    }

    [Fact]
    public void BadRepetition_WarnsAndOccupiesItsTimeAsASpacer()
    {
        // LILYPOND-REF: scm/music-functions.scm:940-942 expand-repeat-chords! —
        // warning "Bad chord repetition".
        var tree = SyntaxTree.Parse("q4 c' d e");
        var diagnostics = SemanticValidation.Run(tree);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCodes.BadChordRepetition);

        var items = Items("q4 c' d e");
        var spacer = Assert.IsType<RestItem>(items[0]);
        Assert.True(spacer.IsSpacer);
        Assert.Equal(new Fraction(1, 4), spacer.Duration);
    }

    [Fact]
    public void Exporter_PassesTheRepetitionThroughVerbatim()
    {
        // LilyPond understands q, and both engines expand it after relative
        // resolution — so the twin carries the q unexpanded.
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse("<c' e g>8@p q q4@staccato"));
        Assert.Contains("q", ly);
        Assert.Contains("q4", ly);
        Assert.DoesNotContain("<c' e g>8 <c' e g>", ly);
    }
}
