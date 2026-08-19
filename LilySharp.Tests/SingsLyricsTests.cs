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

using LilySharp.Core.Editing;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The <c>sings</c> lyric binding (user decision, 2026-08-19): lyrics bind to
/// their OWN melody at the definition — <c>lyrics ja sings vocal { … }</c> —
/// and the score only places them. A bound track placed as a ROW puts its
/// syllables at the melody's rhythm WITHOUT engraving the melody (the part-sheet
/// chorus-words case; LilyPond's shape is <c>\lyricsto</c> over a NullVoice).
/// An unbound row keeps the even-spread lead-sheet reading; attaching an
/// unbound track to a staff is the closed door (LYS6009/6010).
/// </summary>
[Trait("Category", "Unit")]
public class SingsLyricsTests
{
    private const string PartSheet = """
        time 4/4
        section Chorus {
          sax { c4 d e f | g2 g | }
          vocal { g8 g a4 a8 a a4 | g2 f | }
          lyrics ja sings vocal { Sing it loud and clear now | ev- ery | }
        }
        form main { Chorus }
        score main { staff sax lyrics ja }
        """;

    private static IReadOnlyList<Diagnostic> Validate(string src)
        => SemanticValidation.Run(SyntaxTree.Parse(src));

    [Fact]
    public void BoundRow_PlacesSyllablesAtTheMelodysRhythm()
    {
        var tree = SyntaxTree.Parse(PartSheet);
        var spec = RenderSpecParser.FindFirst(tree);
        var score = new MeasureCollector().CollectMultiStaff(tree, spec!);

        var row = score.Lyrics.Where(l => l.IsLyricsRow).ToList();
        Assert.Equal(8, row.Count);
        // Bar 1 carries the VOCAL's onsets (8th 8th 4th 8th 8th 4th), not six
        // even sixths of the bar, and not the sax's four quarters.
        var bar1 = row.Where(l => l.MeasureIndex == 0).OrderBy(l => l.Timing).ToList();
        Assert.Equal(
            new[] { new Fraction(0, 1), new Fraction(1, 8), new Fraction(1, 4),
                    new Fraction(1, 2), new Fraction(5, 8), new Fraction(3, 4) },
            bar1.Select(l => l.Timing).ToArray());
        Assert.Equal("Sing", bar1[0].Text);
        Assert.Equal("now", bar1[5].Text);
    }

    [Fact]
    public void BoundRow_DoesNotEngraveTheMelody()
    {
        var tree = SyntaxTree.Parse(PartSheet);
        var spec = RenderSpecParser.FindFirst(tree);
        var score = new MeasureCollector().CollectMultiStaff(tree, spec!);

        // Two staves total (sax + the lyric row band); the row's measures hold
        // only invisible spacers — the vocal's notes are nowhere on the page.
        var allStaves = score.StaffGroups.SelectMany(g => g.Staves).ToList();
        Assert.Equal(2, allStaves.Count);
        var rowStaff = allStaves[1];
        Assert.All(rowStaff.Voices[0].Measures.SelectMany(m => m.Items),
            item => Assert.True(item is Core.Svg.Model.RestItem { IsSpacer: true }));
    }

    [Fact]
    public void TheDecidedErrors_FireWhereTheDesignSaysTheyDo()
    {
        // Unbound track attached to a staff: the closed door.
        Assert.Contains(Validate("""
            section A { m { c4 d | } lyrics words { la la | } }
            form main { A }
            score main { staff m with lyrics words }
            """), d => d.Code == DiagnosticCodes.LyricsAttachmentUnbound);

        // Bound to a DIFFERENT part than the staff it is attached to.
        Assert.Contains(Validate("""
            section A { m { c4 d | } v { e4 f | } lyrics w sings v { la la | } }
            form main { A }
            score main { staff m with lyrics w }
            """), d => d.Code == DiagnosticCodes.LyricsAttachmentWrongStaff);

        // sings target that names nothing.
        Assert.Contains(Validate("""
            section A { m { c4 d | } lyrics w sings ghost { la la | } }
            form main { A }
            score main { staff m }
            """), d => d.Code == DiagnosticCodes.SingsTargetUnknown);

        // Two blocks of one track naming different targets.
        Assert.Contains(Validate("""
            section A { m { c4 d | } v { e4 f | }
              lyrics w sings m { la la | }
              lyrics w sings v { lo lo | } }
            form main { A }
            score main { staff m with lyrics w }
            """), d => d.Code == DiagnosticCodes.SingsConflict);
    }

    [Fact]
    public void TheLegalShapes_ValidateClean()
    {
        string[] clean =
        [
            // sings + attach to the singing staff.
            """
            section A { m { c4 d | } lyrics w sings m { la la | } }
            form main { A }
            score main { staff m with lyrics w }
            """,
            // sings + the bound ROW (the melody is not engraved).
            PartSheet,
            // The voice rule IS a binding: lyrics named after a voice.
            """
            section A { m { voice sop { c'4 d' | } { e4 f | } } lyrics sop { la la | } }
            form main { A }
            score main { staff m with lyrics sop }
            """,
            // A track named after the part itself.
            """
            section A { m { c4 d | } lyrics m { la la | } }
            form main { A }
            score main { staff m with lyrics m }
            """,
            // An unbound row stays the even-spread lead sheet.
            """
            section A { chords prog { c1 | } lyrics words { la la | } }
            form main { A }
            score main { chords prog lyrics words }
            """,
        ];
        foreach (var src in clean)
            Assert.DoesNotContain(Validate(src), d =>
                d.Code is DiagnosticCodes.LyricsAttachmentUnbound
                       or DiagnosticCodes.LyricsAttachmentWrongStaff
                       or DiagnosticCodes.SingsTargetUnknown
                       or DiagnosticCodes.SingsConflict);
    }

    [Fact]
    public void SecondBlockOfATrack_MayOmitOrRepeatTheBinding()
    {
        var src = """
            section A { m { c4 d | e4 f | }
              lyrics w sings m { la la | }
              lyrics w { lo lo | }
              lyrics w sings m { le le | } }
            form main { A }
            score main { staff m with lyrics w }
            """;
        Assert.DoesNotContain(Validate(src), d => d.Code == DiagnosticCodes.SingsConflict);
    }

    [Fact]
    public void TheLayoutConverter_CarriesTheBinding_BothWays()
    {
        const string sectionMajor = """
            part m { clef treble }
            section A {
              m { c4 d e f | }
              lyrics w sings m { la la la la | }
            }
            form main { A }
            score main { staff m with lyrics w }
            """;
        var pm = PartSectionLayoutConverter.Convert(sectionMajor);
        Assert.NotNull(pm);
        Assert.Contains("lyrics w sings m", pm);

        var back = PartSectionLayoutConverter.Convert(pm!);
        Assert.NotNull(back);
        Assert.Contains("lyrics w sings m", back);
        Assert.DoesNotContain(SemanticValidation.Run(SyntaxTree.Parse(back!)),
            d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void RoundTrips_WithNoDiagnostics()
    {
        var tree = SyntaxTree.Parse(PartSheet);
        Assert.Empty(tree.Diagnostics);
        Assert.Equal(PartSheet, tree.GetRoot().ToFullString());
    }
}
