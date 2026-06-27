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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Bass tab support: the <c>\N</c> string-number annotation forces the fret's
/// string, bass tunings sound 8vb, and the note carries a clef-independent MIDI
/// pitch for fret calculation. LILYPOND-REF: lily/tab-note-heads-engraver.cc.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TabStringNumberTests
{
    private static NoteItem FirstNote(string body)
    {
        var src = "part bl { clef bass }\nsection Main {\n  bl {\n" + body + "\n  }\n}\n" +
                  "structure { Main }\nscore \"x\" { staff { bl } }\n";
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(src));
        return score.Voice.Measures[0].Items.OfType<NoteItem>().First();
    }

    [Fact]
    public void StringNumberAnnotation_IsParsedOntoTheNote()
    {
        // The \4 must reach the note (it used to be silently dropped).
        var note = FirstNote("a4\\4 b c d |");
        Assert.Equal(4, note.StringNumber);
    }

    [Fact]
    public void NoteCarriesClefIndependentMidi()
    {
        // The first 'a' resolves to A3 (MIDI 57); the note carries that absolute
        // pitch (NOT the clef-relative StaffPosition the tab must avoid for pitch).
        var note = FirstNote("a4 b c d |");
        Assert.Equal(57, note.Midi);
        Assert.Null(note.StringNumber);
    }

    [Theory]
    [InlineData(TuningType.Bass, -12)]
    [InlineData(TuningType.Bass5, -12)]
    [InlineData(TuningType.Bass6, -12)]
    [InlineData(TuningType.Guitar, 0)]
    [InlineData(TuningType.Ukulele, 0)]
    public void BassTuningsSound8vb(TuningType tuning, int expectedShift)
    {
        Assert.Equal(expectedShift, Tunings.OctaveShift(tuning));
    }

    [Fact]
    public void Bass6Tuning_IsSixStringsLowBHighC()
    {
        Assert.Equal(6, Tunings.GetStringCount(TuningType.Bass6));
        // B0 E1 A1 D2 G2 C3
        Assert.Equal(new[] { 23, 28, 33, 38, 43, 48 }, Tunings.GetTuning(TuningType.Bass6));
    }

    [Fact]
    public void CalculateFret_HonoursPreferredString()
    {
        // A1 (33) forced onto string 4 (E1=28) of a 4-string bass = fret 5,
        // even though the open A string (string 3) would be fret 0.
        var (str, fret) = Tunings.CalculateFret(33, Tunings.Bass, preferredString: 4);
        Assert.Equal(4, str);
        Assert.Equal(5, fret);
    }

    // ---- Tab tie behaviour ----

    private static (Score Score, MeasureCollector Collector) CollectBody(string body)
    {
        var src = "part bl { clef bass }\nsection Main {\n  bl {\n" + body + "\n  }\n}\n" +
                  "structure { Main }\nscore \"x\" { staff { bl } }\n";
        var collector = new MeasureCollector();
        var score = collector.Collect(SyntaxTree.Parse(src));
        return (score, collector);
    }

    private static List<NoteItem> Notes(Score score) =>
        score.Voice.Measures.SelectMany(m => m.Items).OfType<NoteItem>().ToList();

    [Fact]
    public void TieDestination_IsFlaggedTieTarget()
    {
        // The held note (tie destination) is flagged so the tab hides its fret;
        // the struck note (source) is not.
        var notes = Notes(CollectBody("a4\\4~ a4 b4 |").Score);
        Assert.False(notes[0].IsTieTarget);
        Assert.True(notes[1].IsTieTarget);
    }

    [Fact]
    public void TieSource_AdoptsDestinationString_WhenSourceUnspecified()
    {
        // Source has no \N but the destination names string 3 → source adopts 3
        // (so the struck note sits on the held string). Not a conflict.
        var (score, collector) = CollectBody("a4~ a4\\3 b4 |");
        var notes = Notes(score);
        Assert.Equal(3, notes[0].StringNumber);
        Assert.True(notes[1].IsTieTarget);
        Assert.Empty(collector.TabTieWarnings);
    }

    [Fact]
    public void TieWithConflictingStrings_Warns_AndKeepsSourceString()
    {
        var (score, collector) = CollectBody("a4\\4~ a4\\3 b4 |");
        var w = Assert.Single(collector.TabTieWarnings);
        Assert.Equal(4, w.PreviousString);
        Assert.Equal(3, w.FollowingString);
        Assert.Equal(4, Notes(score)[0].StringNumber); // source string kept
    }

    [Fact]
    public void StringNumberToken_KeepsFollowingSourcePositionsAligned()
    {
        // Regression: the \N token text must span BOTH chars ("\4", not "4"); a
        // token's width comes from its text, so a 1-char text under a 2-char span
        // drifts every following note's SourcePosition by 1 per \N, which silently
        // broke the editor<->preview note mapping on tab scores.
        var src = "part bl { clef bass }\nsection Main {\n  bl {\n r4 a4\\4 b4\\3 c4 |\n  }\n}\n" +
                  "structure { Main }\nscore \"x\" { staff { bl } }\n";
        var notes = Notes(new MeasureCollector().Collect(SyntaxTree.Parse(src)));
        Assert.Equal(src.IndexOf("b4") - src.IndexOf("a4"),
            notes[1].SourcePosition - notes[0].SourcePosition);
        Assert.Equal(src.IndexOf("c4") - src.IndexOf("b4"),
            notes[2].SourcePosition - notes[1].SourcePosition);
    }

    [Fact]
    public void TabTieStringValidator_SurfacesConflict()
    {
        var src = "part bl { clef bass }\nsection Main {\n  bl {\n a4\\4~ a4\\3 b4 |\n  }\n}\n" +
                  "structure { Main }\nscore \"x\" { staff { bl } }\n";
        var validator = new TabTieStringValidator();
        validator.Validate(SyntaxTree.Parse(src));
        var d = Assert.Single(validator.Diagnostics);
        Assert.Equal(DiagnosticCodes.TabTieStringConflict, d.Code);
    }
}
