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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The chord display selector on a chord row: <c>chords NAME as roman | names</c>.
/// Absolute names by default, Roman-numeral degrees for the key. To show one track BOTH
/// ways, place it twice (<c>StackedTrackRowTests</c>) — which is what replaced the
/// retired <c>as both</c>.
/// </summary>
public class ChordDisplayModeTests
{
    private const string Doc = """
        octave absolute
        time 4/4
        key c major
        part melody { clef treble }
        section A { melody { e'4 e' f' g' | a' g' e' d' | } }
        chords harmony { C | Am | }
        form main { A }
        score main "names" { chords harmony  staff melody }
        score main "roman" { chords harmony as roman  staff melody }
        score main "row"   { chords harmony as roman }
        """;

    private static ChordDisplayMode StaffMode(string score)
        => RenderSpecParser.FindByName(SyntaxTree.Parse(Doc), score)!
            .GetVoiceBindings().First().ChordDisplay;

    [Fact]
    public void AsSelector_ParsesEachMode()
    {
        // `as` doubles as the Dutch A-flat pitch, so it is matched by text — this
        // guards that the selector still reads correctly.
        Assert.Equal(ChordDisplayMode.Names, StaffMode("names"));
        Assert.Equal(ChordDisplayMode.Roman, StaffMode("roman"));
    }

    /// <summary>
    /// There are TWO displays. <c>both</c> — the degree stacked above the name as one
    /// symbol — was retired 2026-08-23 (user decision): a track shown both ways is placed
    /// twice, which is two rows the writer can see and order (<c>StackedTrackRowTests</c>).
    /// The word is now rejected by name, not silently read as <c>names</c>.
    /// </summary>
    [Fact]
    public void Both_IsRetired_AndSaysWhatToWriteInstead()
    {
        var tree = SyntaxTree.Parse(Doc.Replace(
            "score main \"row\"   { chords harmony as roman }",
            "score main \"row\"   { chords harmony as both }"));
        var d = LilySharp.Core.Semantics.SemanticValidation.Run(tree)
            .Single(x => x.Code == DiagnosticCodes.UnknownChordDisplayMode);

        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains("chords harmony as roman", d.Message);
        Assert.Contains("chords harmony as names", d.Message);
    }

    [Fact]
    public void AnUnknownDisplay_IsRejectedRatherThanReadAsNames()
    {
        // The pre-existing hole the retirement had to close first: ParseChordMode's `_`
        // arm meant any unrecognised word drew absolute names and reported nothing.
        var tree = SyntaxTree.Parse(Doc.Replace(
            "score main \"row\"   { chords harmony as roman }",
            "score main \"row\"   { chords harmony as romn }"));

        Assert.Contains(LilySharp.Core.Semantics.SemanticValidation.Run(tree),
            x => x.Code == DiagnosticCodes.UnknownChordDisplayMode
              && x.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ChordRow_AsRoman_Parses()
    {
        var rs = RenderSpecParser.FindByName(SyntaxTree.Parse(Doc), "row")!;
        var row = rs.Items.OfType<ChordRowSpec>().Single();
        Assert.Equal(ChordDisplayMode.Roman, row.DisplayMode);
    }

    [Fact]
    public void Collect_Roman_StampsDegreeAndMode()
    {
        var score = new MeasureCollector()
            .Collect(SyntaxTree.Parse(Doc), "melody", null, "harmony", ChordDisplayMode.Roman);
        // C major: c1 -> I, a1:m -> VIm.
        Assert.Equal(new[] { "I", "VIm" }, score.ChordNames.Select(c => c.RomanText).ToArray());
        Assert.All(score.ChordNames, c => Assert.Equal(ChordDisplayMode.Roman, c.DisplayMode));
        // The absolute name is still carried, so the SAME track placed again `as names`
        // shows names from the same items.
        Assert.Equal(new[] { "C", "Am" }, score.ChordNames.Select(c => c.ChordText).ToArray());
    }

    [Fact]
    public void InlineChord_FollowsTheStaffsRomanMode()
    {
        // A staff with `as roman` shows its inline @chord as a degree too, so it does
        // not clash with the track's Roman symbol (both render "Imaj7").
        var tree = SyntaxTree.Parse("""
            octave absolute
            time 4/4
            key c major
            part melody { clef treble }
            section A { melody { c'4@chord(Cmaj7) c' g' g' | } }
            chords harmony { Cmaj7 | }
            form main { A }
            score main { chords harmony as roman  staff melody }
            """);
        var score = new MeasureCollector()
            .Collect(tree, "melody", null, "harmony", ChordDisplayMode.Roman);
        Assert.Equal(2, score.ChordNames.Length); // inline + track, same slot
        Assert.All(score.ChordNames, c => Assert.Equal("Imaj7", c.RomanText));
        Assert.All(score.ChordNames, c => Assert.Equal(ChordDisplayMode.Roman, c.DisplayMode));
    }

    [Fact]
    public void MidPieceKeyChange_RebasesRomanDegrees()
    {
        // Modulate C -> G mid-section: a G chord is V in C but I in G.
        var tree = SyntaxTree.Parse("""
            octave absolute
            time 4/4
            key c major
            part melody { clef treble }
            section A { melody { c'4 c' c' c' | key g major d' d' d' d' | } }
            chords harmony { C | G | }
            form main { A }
            score main { chords harmony as roman  staff melody }
            """);
        var score = new MeasureCollector()
            .Collect(tree, "melody", null, "harmony", ChordDisplayMode.Roman);
        Assert.Equal(new[] { "I", "I" },
            score.ChordNames.OrderBy(c => c.MeasureIndex).Select(c => c.RomanText).ToArray());
    }

    [Fact]
    public void Collect_DefaultNames_HasNoModeButStillComputesDegree()
    {
        var score = new MeasureCollector()
            .Collect(SyntaxTree.Parse(Doc), "melody", null, "harmony");
        Assert.All(score.ChordNames, c => Assert.Equal(ChordDisplayMode.Names, c.DisplayMode));
    }
}
