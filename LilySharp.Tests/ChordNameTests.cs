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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;
using LilySharp.Core.Rendering;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class ChordNameTests
{
    // --- The @chord annotation, read from its argument ---
    //
    // ⚠️ These parse the SPELLING; they used to hand the reader a dotted MarkName
    // ("chord.c.:.m7") directly, which asserts about a string rather than about
    // anything anyone can write — and one of them ("chord.") named a MarkName no
    // source produces at all. VALUE_SITE_AUDIT §7 (第167) is where that habit cost a
    // session: a remark's claimed spelling had never once parsed.

    private static MusicMarkSyntax Mark(string music)
        => SyntaxTree.Parse("melody { " + music + " }")
            .GetRoot().DescendantNodes().OfType<MusicMarkSyntax>().First();

    private static string? Chord(string music)
        => LilySharp.Core.Semantics.AnnotationValues.Chord(
            Mark(music), LilySharp.Core.Semantics.ChordSpelling.Default, out _)?.Text;

    [Fact]
    public void ParseChordName_SimpleChord()
    {
        // @chord(C) — a Lily# root pitch, major triad.
        Assert.Equal("C", Chord("c4@chord(C) |"));
    }

    [Fact]
    public void ParseChordName_MinorSeventh()
    {
        // The sub-language: 'c' arrives as a PITCH token, ':' and 'm7' as their own,
        // and the three are ONE argument because they were written adjacent.
        Assert.Equal("Cm7", Chord("c4@chord(Cm7) |"));
    }

    [Fact]
    public void ParseChordName_FlatRoot()
    {
        // @chord(Bb7) — the flat root spells B-flat in the symbol.
        Assert.Equal("B\u266D7", Chord("c4@chord(Bb7) |"));  // B♭7
    }

    [Fact]
    public void ParseChordName_FlatRoot_Eb()
    {
        Assert.Equal("E\u266Dmaj7", Chord("c4@chord(Ebmaj7) |"));  // E♭maj7
    }

    [Fact]
    public void ParseChordName_NaturalB()
    {
        // @chord(B) — B natural major, not B-flat.
        Assert.Equal("B", Chord("c4@chord(B) |"));
    }

    [Theory]
    [InlineData("c4@chord(Csus4) |", "Csus4")]
    [InlineData("c4@chord(Cdim) |", "Cdim")]
    [InlineData("c4@chord(Caug) |", "Caug")]
    public void ParseChordName_Qualities(string music, string symbol)
        => Assert.Equal(symbol, Chord(music));

    [Fact]
    public void ParseChordName_MultiPartJoined()
    {
        // Several tokens, ONE run: the argument's text is the written "g:7/b", with
        // no reassembly. (MarkName spells the same thing "chord.g.:.7./.b".)
        Assert.Equal("G7/B", Chord("c4@chord(G7/B) |"));  // slash bass
    }

    [Fact]
    public void ParseChordName_SharpRoot()
    {
        // The sharp root spells C-sharp.
        Assert.Equal("C♯m7", Chord("c4@chord(C#m7) |"));  // C-sharp m7
    }

    [Fact]
    public void ParseChordName_QuotedFreeText_ForAlteredChords()
    {
        // An altered chord outside the diatonic vocabulary (e.g. "7#9") is no longer
        // a valid bare chord; it goes in the quoted free-text escape and prints as
        // written (@chord("G7#9")).
        Assert.Null(Chord("c4@chord(G7#9) |"));                 // bare 7#9: rejected
        Assert.Equal("G7#9", Chord("c4@chord(\"G7#9\") |"));    // quoted: verbatim
    }

    /// <summary>
    /// The quoted escape prints what is inside the quotes, DOTS INCLUDED — the one
    /// spelling the old reader had to catch before it removed the dots of the joined
    /// MarkName, or "N.C." would have printed as "NC".
    /// </summary>
    [Fact]
    public void ParseChordName_QuotedFreeText_KeepsItsDots()
        => Assert.Equal("N.C.", Chord("c4@chord(\"N.C.\") |"));

    [Fact]
    public void ParseChordName_SharpRootWithFlatTension()
    {
        // @chord(F#m7-5) resolves to the half-diminished quality; the canonical
        // symbol spells both accidentals (root sharp, the b5 as flat).
        Assert.Equal("F♯m7♭5", Chord("c4@chord(F#m7-5) |"));  // half-diminished
    }

    [Theory]
    [InlineData("c4@segno |")]
    [InlineData("c4@fig(6) |")]
    [InlineData("c4@mark(\"A\") |")]
    [InlineData("c4@Chord(c) |")]   // the name gate is case-SENSITIVE, as it always was
    public void ParseChordName_NotChord_ReturnsNull(string music)
        => Assert.Null(Chord(music));

    /// <summary>
    /// A bare <c>@chord</c> derives its symbol from the notes it sits on, and
    /// <c>@chord()</c> is what the completion leaves behind. Neither names a chord
    /// here, and neither is an UNKNOWN annotation — which is what the empty string,
    /// as against null, says.
    /// </summary>
    [Theory]
    [InlineData("<c e g>4@chord |")]
    [InlineData("<c e g>4@chord() |")]
    public void ParseChordName_NoArgument_IsEmptyNotNull(string music)
        => Assert.Equal("", Chord(music));

    /// <summary>
    /// ★ The equivalence the collector now relies on (VALUE_SITE_AUDIT §9.5.3 ⑶): asking
    /// the reader "does this name nothing itself?" answers exactly what comparing the
    /// dotted MarkName to "chord" answered. Not self-evident — the two could part
    /// company on a spelling whose dotted NAME has parts but whose argument list is
    /// empty — so every such spelling is pinned here. <c>@chord.c</c> is the legacy
    /// dotted form (its '.c' stays outside the node, where it is LYS0023 since 第170第1
    /// 便) and <c>@chord.up</c> is the shape that could have disagreed: it parses to no
    /// music mark at all.
    /// </summary>
    [Theory]
    [InlineData("<c e g>4@chord |")]
    [InlineData("<c e g>4@chord() |")]
    [InlineData("<c e g>4@chord.c |")]
    public void ABareChord_IsTheSameQuestionAskedOfTheNameOrOfTheReader(string music)
    {
        var mark = Mark(music);
        Assert.Equal("chord", mark.MarkName);          // what the collector used to ask
        Assert.Equal("", Chord(music));                // what it asks now
    }

    [Fact]
    public void APlacementQualifierOnChord_ParsesToNoMarkAtAll()
        => Assert.Empty(SyntaxTree.Parse("melody { c4@chord.up }")
            .GetRoot().DescendantNodes().OfType<MusicMarkSyntax>());

    /// <summary>
    /// ⚠️ A behaviour change, declared (VALUE_SITE_AUDIT §9.5.3 ⑴). A '.' WRITTEN
    /// inside the parentheses used to vanish: MarkName joined the tokens with dots and
    /// the chord parser then removed every dot, so <c>@chord(b.es:7)</c> printed B♭7
    /// and <c>@chord(.c:m7)</c> printed Cm7. The run keeps what was written, so these
    /// name no chord and are reported unknown. No book writes one — measured by
    /// spelling all 299 books back out of their trees — and nothing ever documented
    /// the swallowing; it was an artefact of the round trip this reading removes.
    /// </summary>
    [Theory]
    [InlineData("c4@chord(b.es:7) |")]
    [InlineData("c4@chord(c:m.7) |")]
    [InlineData("c4@chord(.c:m7) |")]
    public void ADotWrittenInsideTheParentheses_IsNoLongerSwallowed(string music)
        => Assert.Null(Chord(music));

    /// <summary>
    /// ★ Positive control for the change above, and the measurement that decided the
    /// reader's shape: an argument written with a SPACE is still accepted, because ALL
    /// the runs are joined and not just the first. (Every <c>@chord(</c> in the corpus
    /// is a single run, so the spaced form is unexercised, not impossible.) A comma
    /// joins the same way — the reader concatenates every argument's text.
    /// </summary>
    [Theory]
    [InlineData("c4@chord(C m7) |", "Cm7")]
    [InlineData("c4@chord(C, m7) |", "Cm7")]
    [InlineData("c4@chord(x, m7) |", null)]   // "xm7" is no chord entry
    public void ArgumentsWrittenApart_AreJoined(string music, string? symbol)
        => Assert.Equal(symbol, Chord(music));

    // --- ChordNameEngraver ---

    [Fact]
    public void ChordNameEngraver_Calculate_EmptyInput()
    {
        var result = ChordNameEngraver.Calculate(ScoreTextMetrics.Bundled, 
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

        var result = ChordNameEngraver.Calculate(ScoreTextMetrics.Bundled, 
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

        var result = ChordNameEngraver.Calculate(ScoreTextMetrics.Bundled, 
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
        var source = "c4 @chord(C) d e f";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.ChordNames);
        var cn = score.ChordNames[0];
        Assert.Equal(0, cn.MeasureIndex);
        Assert.Equal(0, cn.ItemIndex);
        Assert.Equal("C", cn.ChordText);
    }

    /// <summary>An inline symbol carries its note's onset (the column the spacing prices
    /// it on) while staying item-placed — see MeasureCollector.CollectChordNames.</summary>
    [Fact]
    public void Collector_ChordName_CarriesTheNoteOnset()
    {
        var source = "c4 d e @chord(C) f | <c e g>2 r4 <d f a>@chord";
        var tree = SyntaxTree.Parse(source);
        var score = new MeasureCollector().Collect(tree);

        Assert.Equal(2, score.ChordNames.Length);
        var first = score.ChordNames[0];
        Assert.False(first.UseTiming);
        Assert.Equal(2, first.ItemIndex);
        Assert.Equal(new Fraction(1, 2), first.Timing);
        var second = score.ChordNames[1];
        Assert.Equal(1, second.MeasureIndex);
        Assert.Equal(new Fraction(3, 4), second.Timing);
    }

    [Fact]
    public void Collector_ChordName_KeptInMultiVoiceScore()
    {
        // Regression: a single-staff score with voice { } polyphony used to drop
        // chord names (BuildMultiVoiceScore omitted them). It must keep them, just
        // like the single-voice case above.
        var source = "c4 @chord(C) voice { d e } { d e } f";
        var tree = MusicSource.Parse(source);
        Assert.Empty(tree.Diagnostics); // supported syntax, no rejection

        var score = new MeasureCollector().Collect(tree);

        Assert.True(score.Voices.Length >= 2); // reconstructed as multiple voices
        Assert.Single(score.ChordNames);
        Assert.Equal("C", score.ChordNames[0].ChordText);
    }

    [Fact]
    public void ChordRow_KeptInMultiVoiceScore()
    {
        // Regression (kept in row spelling): a chords row above a multi-voice
        // single staff used to drop the whole progression (Collect returned to
        // BuildMultiVoiceScore before the chords were collected). Both single-
        // and multi-voice must surface the chords. Uses the real render path.
        string Doc(string body) => $@"
part m {{ clef treble }}
chords prog {{ C | D | }}
section A {{ m {{ {body} }} }}
form main {{ A }}
score main {{ chords prog  staff m }}
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
        var source = "c4 @chord(Cm7) d e f";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.ChordNames);
        Assert.Equal("Cm7", score.ChordNames[0].ChordText);
    }

    [Fact]
    public void Collector_ChordName_FlatRoot()
    {
        var source = "c4 @chord(Bb7) d e f";
        var tree = SyntaxTree.Parse(source);
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.ChordNames);
        Assert.Equal("B\u266D7", score.ChordNames[0].ChordText);
    }

    [Fact]
    public void Collector_ChordName_SharpChord()
    {
        var source = "c4 @chord(C#m7) d e f";
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
        var source = "c4 @chord(C) d @chord(Am) e @chord(F) f";
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
        var source = "c4 @chord(C) @fig(6) d e f";
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
            "chords prog { C | }\n" +
            "section Main {\n  hi { b4 b b b | }\n" +
            $"  lo {{ voice {{ {firstVoice} }} {{ b4 b b b }} | }}\n}}\n" +
            "form main { Main }\n" +
            "score main \"o\" { staff hi  chords prog  staff lo }\n";
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

    /// <summary>
    /// A NUMBERS-ONLY tab prints no note-attached <c>@chord</c>: the notation staff above it
    /// is already printing that name over the same note, and it books no room for one either.
    /// </summary>
    /// <remarks>
    /// The same reading <see cref="LilySharp.Core.Svg.Layout.TabStaffStencils"/> applies to
    /// the scripts and the markup families — a numbers-only tab carries the fret digits
    /// BECAUSE the staff above carries the rest — extended to the chord name (reader,
    /// 2026-09-07). LILYSHARP-OWN: LilyPond names chords only in a <c>ChordNames</c> context,
    /// so its TabStaff block has no ChordName line to port.
    /// <para>
    /// ⚠️ THREE LEGS, one book: the numbers tab (the quantity), the SAME tab written
    /// <c>as full</c> (the control — the writer asked for a complete tab, and a complete tab
    /// carries its own markup, so the name prints twice), and the ROOM (a numbers tab with
    /// the chord sits exactly where one whose part has no chord at all does — the band that
    /// used to be booked under an empty line is gone with the symbol).
    /// </para>
    /// </remarks>
    [Fact]
    public void ANumbersOnlyTab_PrintsNoAttachedChord_AndBooksNoRoomForOne()
    {
        static (int Symbols, double TabY) Read(string scoreBlock, bool withChord = true)
        {
            var tree = SyntaxTree.Parse(
                $"part melody {{ section A {{ c1\\3{(withChord ? "@chord(Cmaj7)" : "")} }} }}\n"
                + "form main { A }\n"
                + scoreBlock + "\n");
            Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
            var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
            var layout = new LayoutEngine().Layout(score);
            double tabY = layout.Systems[0].StaffGroups.SelectMany(g => g.Staves)
                .OrderBy(s => s.StaffIndex).Last().Y;
            return (layout.ChordNameLayouts.Count(c => c.ChordText == "Cmaj7"), tabY);
        }

        var numbers = Read("score main { staff melody  tab melody }");
        var full = Read("score main { staff melody  tab melody as full }");
        var noChord = Read("score main { staff melody  tab melody }", withChord: false);

        // The control first: a tab the writer asked to be COMPLETE carries its own markup,
        // so the name is over the staff AND over the tab. That is the premise the quantity
        // is a departure from — without it, "one symbol" could just mean the book has one.
        Assert.Equal(2, full.Symbols);
        Assert.Equal(1, numbers.Symbols);

        // ...and the room went with it: the tab sits where it does when nothing on it
        // carries a chord at all.
        Assert.Equal(noChord.TabY, numbers.TabY, 9);
        Assert.True(full.TabY < numbers.TabY - 0.1,
            "the full tab must still book the band its own symbol stands in: "
            + $"full {full.TabY:F6}, numbers {numbers.TabY:F6} (up-positive: lower is smaller)");
    }

    /// <summary>
    /// A note-attached <c>@chord</c> on a TAB staff stands over ITS OWN top line, the same
    /// distance a notation staff's does — so its ink stays out of the staff above it.
    /// </summary>
    /// <remarks>
    /// The placement is <c>0.6 + the protrusion of this staff's own up-skyline</c>, and that
    /// skyline is built about the staff's REFERENCE POINT and reflected once, at the edge, to
    /// "above the top line" (<c>LayoutEngine.LayoutChordNames</c>). The reflection subtracted
    /// the SCORE's nominal half-staff (2.000000) from a TAB staff that spans 7.500000, so
    /// 1.750000 of it was left undone and the tab's chord floated that much too high — through
    /// the bottom line of the staff above, while the room reserved for it below stood empty
    /// (owner report 2026-09-06, scratch/ベースタブLy/tab-chord.lys: <c>staff back</c> over
    /// <c>tab melody</c>, Cmaj7 crossing the staff line above it).
    /// <para>
    /// ⚠️ THE ASSERTION IS A COMPARISON, NOT A CONSTANT: the same book carries a chord on the
    /// notation staff, so the tab's distance is measured against the one the corpus already
    /// engraves rather than against a number written here. Two legs, as the family above:
    /// the quantity (both stand the same distance over their own top line) and the consequence
    /// the reader saw (the tab's ink is below the staff above, measured through the engraver's
    /// own ink, not a guessed cap height).
    /// </para>
    /// <para>
    /// ⚠️ ONE OBSERVER. Sweeping the tracked corpus and the owner's 323 bass-tab books
    /// (920 books, base against head) MOVED exactly this one: no other book puts an
    /// <c>@chord</c> on a tab staff, which is why the frame stayed wrong.
    /// </para>
    /// </remarks>
    [Fact]
    public void ChordOnATabStaff_StandsOverItsOwnTopLine_NotTheStaffAbove()
    {
        var tree = SyntaxTree.Parse(
            "part melody { section A { c1\\3@chord(Cmaj7) } }\n"
            + "part back { section A { e1\\3@chord(Dm7) } }\n"
            + "form main { A }\n"
            + "score main { staff back  tab melody }\n");
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine().Layout(score);

        var staves = layout.Systems[0].StaffGroups.SelectMany(g => g.Staves)
            .ToDictionary(s => s.StaffIndex);
        // Y is the staff's TOP LINE and YUp the symbol's baseline, both up-positive from the
        // system origin, so the difference is "above this staff's own top line".
        double OverItsTopLine(string text)
        {
            var chord = layout.ChordNameLayouts.Single(c => c.ChordText == text);
            var owner = score.ChordNames.Single(c => c.ChordText == text);
            return chord.YUp - staves[owner.StaffIndex].Y;
        }

        double onNotation = OverItsTopLine("Dm7");   // staff back, the control
        double onTab = OverItsTopLine("Cmaj7");      // tab melody

        Assert.Equal(onNotation, onTab, 9);

        // ...and therefore the consequence: the tab's symbol is BELOW the bottom line of the
        // staff above it. The ink is the engraver's own measurement of this very string.
        var above = staves[0];
        double inkTop = layout.ChordNameLayouts.Single(c => c.ChordText == "Cmaj7").YUp
            + ChordNameEngraver.SymbolInk(score.TextMetrics, "Cmaj7").Top;
        Assert.True(inkTop < above.Y - above.Height,
            $"the tab's chord ink reaches {inkTop:F6}, the staff above ends at "
            + $"{above.Y - above.Height:F6} (up-positive: smaller is lower)");
    }
}
