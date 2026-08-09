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

using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class ChordNameTests
{
    // --- ParseChordName ---

    [Fact]
    public void ParseChordName_SimpleChord()
    {
        // @chord(c) — a Lily# root pitch, major triad.
        var result = ChordNameItem.ParseChordName("chord.c");
        Assert.NotNull(result);
        Assert.Equal("C", result);
    }

    [Fact]
    public void ParseChordName_MinorSeventh()
    {
        // @chord(c:m7) → MarkName "chord.c.:.m7"
        var result = ChordNameItem.ParseChordName("chord.c.:.m7");
        Assert.NotNull(result);
        Assert.Equal("Cm7", result);
    }

    [Fact]
    public void ParseChordName_FlatRoot()
    {
        // @chord(bes:7) — the flat root spells B-flat in the symbol.
        var result = ChordNameItem.ParseChordName("chord.bes.:.7");
        Assert.NotNull(result);
        Assert.Equal("B\u266D7", result);  // B♭7
    }

    [Fact]
    public void ParseChordName_FlatRoot_Eb()
    {
        // @chord(ees:maj7)
        var result = ChordNameItem.ParseChordName("chord.ees.:.maj7");
        Assert.NotNull(result);
        Assert.Equal("E\u266Dmaj7", result);  // E♭maj7
    }

    [Fact]
    public void ParseChordName_NaturalB()
    {
        // @chord(b) — B natural major, not B-flat.
        var result = ChordNameItem.ParseChordName("chord.b");
        Assert.NotNull(result);
        Assert.Equal("B", result);
    }

    [Fact]
    public void ParseChordName_SuspendedFourth()
    {
        var result = ChordNameItem.ParseChordName("chord.c.:.sus4");
        Assert.NotNull(result);
        Assert.Equal("Csus4", result);
    }

    [Fact]
    public void ParseChordName_Diminished()
    {
        var result = ChordNameItem.ParseChordName("chord.c.:.dim");
        Assert.NotNull(result);
        Assert.Equal("Cdim", result);
    }

    [Fact]
    public void ParseChordName_Augmented()
    {
        var result = ChordNameItem.ParseChordName("chord.c.:.aug");
        Assert.NotNull(result);
        Assert.Equal("Caug", result);
    }

    [Fact]
    public void ParseChordName_MultiPartJoined()
    {
        // Tokenized as separate parts: @chord(g:7/b) → MarkName "chord.g.:.7./.b",
        // rejoined without dots to the entry "g:7/b".
        var result = ChordNameItem.ParseChordName("chord.g.:.7./.b");
        Assert.NotNull(result);
        Assert.Equal("G7/B", result);  // slash bass
    }

    [Fact]
    public void ParseChordName_SharpRoot()
    {
        // @chord(cis:m7) → MarkName "chord.cis.:.m7"; the sharp root spells C-sharp.
        var result = ChordNameItem.ParseChordName("chord.cis.:.m7");
        Assert.NotNull(result);
        Assert.Equal("C♯m7", result);  // C-sharp m7
    }

    [Fact]
    public void ParseChordName_QuotedFreeText_ForAlteredChords()
    {
        // An altered chord outside the diatonic vocabulary (e.g. "7#9") is no longer
        // a valid bare chord; it goes in the quoted free-text escape and prints as
        // written (@chord("G7#9")).
        Assert.Null(ChordNameItem.ParseChordName("chord.G7.#.9"));  // bare 7#9: rejected
        Assert.Equal("G7#9", ChordNameItem.ParseChordName("chord.\"G7#9\""));  // quoted: verbatim
    }

    [Fact]
    public void ParseChordName_SharpRootWithFlatTension()
    {
        // @chord(fis:m7b5) resolves to the half-diminished quality; the canonical
        // symbol spells both accidentals (root sharp, the b5 as flat).
        var result = ChordNameItem.ParseChordName("chord.fis.:.m7b5");
        Assert.NotNull(result);
        Assert.Equal("F♯m7♭5", result);  // half-diminished
    }

    [Fact]
    public void ParseChordName_NotChord_ReturnsNull()
    {
        Assert.Null(ChordNameItem.ParseChordName("segno"));
        Assert.Null(ChordNameItem.ParseChordName("fig.6"));
        Assert.Null(ChordNameItem.ParseChordName("mark.A"));
    }

    [Fact]
    public void ParseChordName_Empty_ReturnsNull()
    {
        Assert.Null(ChordNameItem.ParseChordName("chord."));
    }

    // --- ChordNameEngraver ---

    [Fact]
    public void ChordNameEngraver_Calculate_EmptyInput()
    {
        var result = ChordNameEngraver.Calculate(
            ImmutableArray<ChordNameItem>.Empty,
            ImmutableArray<SystemLayout>.Empty,
            ImmutableArray<MeasureLayout>.Empty);
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void ChordNameEngraver_Calculate_ProducesLayout()
    {
        var chordNames = ImmutableArray.Create(
            new ChordNameItem("Cm7", 0, 0, 0));

        var itemLayout = new ItemLayout(0, 2.0, 1.0);
        var measureLayout = new MeasureLayout(0, 5.0, 10.0, ImmutableArray.Create(itemLayout));
        var systemLayout = new SystemLayout(0, 20.0, 50.0, 5.0, ImmutableArray.Create(measureLayout));

        var result = ChordNameEngraver.Calculate(
            chordNames,
            ImmutableArray.Create(systemLayout),
            ImmutableArray.Create(measureLayout));

        Assert.Single(result);
        Assert.Equal(0, result[0].MeasureIndex);
        Assert.Equal(7.0, result[0].X, 1);  // measureX(5.0) + itemX(2.0)
        Assert.Equal("Cm7", result[0].ChordText);
    }

    [Fact]
    public void ChordNameEngraver_Calculate_YIsAboveStaff()
    {
        var chordNames = ImmutableArray.Create(
            new ChordNameItem("C", 0, 0, 0));

        var itemLayout = new ItemLayout(0, 0, 1.0);
        var measureLayout = new MeasureLayout(0, 0, 10.0, ImmutableArray.Create(itemLayout));
        var systemLayout = new SystemLayout(0, 20.0, 50.0, 5.0, ImmutableArray.Create(measureLayout));

        var result = ChordNameEngraver.Calculate(
            chordNames,
            ImmutableArray.Create(systemLayout),
            ImmutableArray.Create(measureLayout));

        Assert.Single(result);
        // Y-up (frame B): above the system top means a positive value.
        Assert.True(result[0].YUp > 0, "YUp should be positive (above the staff/system top)");
    }

    // --- MeasureCollector integration ---

    [Fact]
    public void Collector_ChordName_SingleChord()
    {
        var source = "c4 @chord(c) d e f";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.ChordNames);
        var cn = score.ChordNames[0];
        Assert.Equal(0, cn.MeasureIndex);
        Assert.Equal(0, cn.ItemIndex);
        Assert.Equal("C", cn.ChordText);
    }

    [Fact]
    public void Collector_ChordName_KeptInMultiVoiceScore()
    {
        // Regression: a single-staff score with voice { } polyphony used to drop
        // chord names (BuildMultiVoiceScore omitted them). It must keep them, just
        // like the single-voice case above.
        var source = "c4 @chord(c) voice { d e } { d e } f";
        var tree = MusicSource.Parse(source);
        Assert.Empty(tree.Diagnostics); // supported syntax, no rejection

        var score = new MeasureCollector().Collect(tree);

        Assert.True(score.Voices.Length >= 2); // reconstructed as multiple voices
        Assert.Single(score.ChordNames);
        Assert.Equal("C", score.ChordNames[0].ChordText);
    }

    [Fact]
    public void WithChords_KeptInMultiVoiceScore()
    {
        // Regression: `staff NAME with chords PART` on a multi-voice single staff
        // used to drop the whole chord progression (Collect returned to
        // BuildMultiVoiceScore before CollectAttached ran). Both single- and
        // multi-voice must surface the attached chords. Uses the real render path.
        string Doc(string body) => $@"
part m {{ clef treble }}
chords prog {{ c1 | d1 | }}
section A {{ m {{ {body} }} }}
form main {{ A }}
score main {{ staff m with chords prog }}
";
        var sTree = SyntaxTree.Parse(Doc("c'4 d' e' f' | g'4 a' b' c'' |"));
        var mTree = SyntaxTree.Parse(Doc("voice { c'4 d' e' f' | } { c4 d e f | }"));
        Assert.Empty(sTree.Diagnostics);
        Assert.Empty(mTree.Diagnostics);

        var single = LilySharp.Core.Svg.SvgGenerator.CollectScore(sTree, RenderSpecParser.FindFirst(sTree));
        var multi = LilySharp.Core.Svg.SvgGenerator.CollectScore(mTree, RenderSpecParser.FindFirst(mTree));

        Assert.Equal(2, single.ChordNames.Length);  // control
        Assert.Equal(2, multi.ChordNames.Length);   // was 0 before the fix
    }

    [Fact]
    public void Collector_ChordName_MinorSeventh()
    {
        var source = "c4 @chord(c:m7) d e f";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.ChordNames);
        Assert.Equal("Cm7", score.ChordNames[0].ChordText);
    }

    [Fact]
    public void Collector_ChordName_FlatRoot()
    {
        var source = "c4 @chord(bes:7) d e f";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.ChordNames);
        Assert.Equal("B\u266D7", score.ChordNames[0].ChordText);
    }

    [Fact]
    public void Collector_ChordName_SharpChord()
    {
        var source = "c4 @chord(cis:m7) d e f";
        var tree = MusicSource.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));

        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.ChordNames);
        Assert.Equal("C♯m7", score.ChordNames[0].ChordText);  // C-sharp m7
    }

    [Fact]
    public void Collector_ChordName_QuotedFreeText()
    {
        // An altered chord (not in the diatonic vocabulary) prints verbatim via the
        // quoted free-text escape.
        var source = "c4 @chord(\"G7#9\") d e f";
        var tree = MusicSource.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));

        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.ChordNames);
        Assert.Equal("G7#9", score.ChordNames[0].ChordText);  // verbatim
    }

    [Fact]
    public void Collector_ChordName_MultipleChords()
    {
        var source = "c4 @chord(c) d @chord(a:m) e @chord(f) f";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Equal(3, score.ChordNames.Length);
        Assert.Equal("C", score.ChordNames[0].ChordText);
        Assert.Equal("Am", score.ChordNames[1].ChordText);
        Assert.Equal("F", score.ChordNames[2].ChordText);
    }

    [Fact]
    public void Collector_ChordName_NoChords()
    {
        var source = "c4 d e f";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.True(score.ChordNames.IsEmpty);
    }

    [Fact]
    public void Collector_ChordName_WithFiguredBass_BothCollected()
    {
        // Chord name and figured bass on the same note
        var source = "c4 @chord(c) @fig(6) d e f";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.ChordNames);
        Assert.Single(score.FiguredBasses);
        Assert.Equal("C", score.ChordNames[0].ChordText);
        Assert.Equal(6, score.FiguredBasses[0].Figures[0].Number);
    }

    // How far the chord row rides above ITS OWN staff — the lower one of two — when that
    // staff's FIRST voice holds the given item. Measured against the staff rather than
    // against the page because the room moves the staff itself for the same rest, and the
    // question here is the CLEARANCE the row was given.
    private static (double Clearance, double RestShift) LowerStaffChordClearance(string firstVoice)
    {
        var src =
            "octave absolute\n" +
            "part hi { clef treble }\npart lo { clef treble }\n" +
            "chords prog { c1 | }\n" +
            "section Main {\n  hi { b4 b b b | }\n" +
            $"  lo {{ voice {{ {firstVoice} }} {{ b4 b b b }} | }}\n}}\n" +
            "form main { Main }\n" +
            "score main \"o\" { staff hi staff lo with chords prog }\n";
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors,
            string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine().Layout(score);
        double staffY = layout.Systems[0].StaffGroups.SelectMany(g => g.Staves)
            .Single(s => s.StaffIndex == 1).Y;
        return (layout.ChordNameLayouts.Select(c => c.YUp).Min() - staffY,
                layout.GetRestShift(measureIndex: 0, voiceIndex: 0, itemIndex: 0));
    }

    /// <summary>
    /// A chord row over a NON-TOP staff clears the rest another voice pushed UP out of that
    /// staff — the per-(system, staff) up-skyline the row is placed against holds that rest
    /// where <c>Rest_collision</c> put it.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:914-950 <c>skyline_spacing</c> — a Rest is
    /// inside-staff ink and there is one of it; LilyPond translates the grob
    /// (lily/rest-collision.cc:211-290 <c>calc_positioning_done</c>) and everything placed
    /// against the group sees the result.
    /// <para>
    /// ⚠️ THE THIRD OF THE FOUR CALL SITES that build their own profile from
    /// <c>SkylineBuilder.BuildStaffSkylines</c>. The PATH does have a fixture —
    /// <c>test/figbass-chordname-lower-staff</c> reaches it through an inline
    /// <c>@chord(...)</c> on the second staff, which is the only spelling in the corpus that
    /// does (COUNTED: every <c>with chords</c> in Fixtures and samples is on its score's ONLY
    /// staff). What no book has is the path AND a rest pushed out of that staff, which is why
    /// the whole suite stayed green through this fix. MEASURED: clearance
    /// 0.650000 with the rests as spacers, 3.184000 with them printed — but only once the
    /// table reached this call; before it BOTH read 0.650000 and the row was engraved on the
    /// rest while the room below had already made space for it. The 2.534000 between them is
    /// LilyPond's own contribution for a rest pushed UP out of a staff (audit/lp-geometry
    /// <c>staff.staff.rest-over-notes</c> against its control), and it is a DIFFERENT number
    /// from the 2.230000 the downward case gives — which is why a port fitted to one side
    /// does not close the other.
    /// </para>
    /// <para>
    /// ⚠️ THREE LEGS, as in the two mirror books
    /// (<c>DynamicPlacementTests.BelowDynamic_ClearsARestAnotherVoicePushedOutOfTheStaff</c>,
    /// <c>FiguredBassTests.FiguredBass_DropsBelowARestAnotherVoicePushedOutOfTheStaff</c>):
    /// a premise, a control that the placement reads this staff's profile at all, and the
    /// quantity.
    /// </para>
    /// </remarks>
    [Fact]
    public void ChordRowOnALowerStaff_ClearsARestAnotherVoicePushedOutOfIt()
    {
        var moved = LowerStaffChordClearance("r4 r r r");
        var spacer = LowerStaffChordClearance("s4 s s s");
        var highNotes = LowerStaffChordClearance("g''4 g'' g'' g''");

        Assert.True(moved.RestShift >= 5.0,
            "premise: Rest_collision must push this rest up out of the staff, "
            + $"got {moved.RestShift:F6} staff positions");

        Assert.True(highNotes.Clearance > spacer.Clearance + 0.1,
            "control: the row must respond to ink in its own staff's up-skyline: "
            + $"high notes {highNotes.Clearance:F6}, spacer control {spacer.Clearance:F6}");

        Assert.True(moved.Clearance > spacer.Clearance + 0.1,
            "the row must clear the rest pushed up out of its own staff: "
            + $"printed rests {moved.Clearance:F6}, spacer control {spacer.Clearance:F6}");
    }
}
