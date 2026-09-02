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
        // (LYS6009/LYS6010 are RETIRED with the `with lyrics` clause — LYS0031.
        // The spellings that used to trip them are legal by construction now: an
        // unbound row after a staff is the lead-sheet row, and a row bound to
        // another part is an independent band at its written place. The one
        // surviving refusal is the GROUP case, where no band exists to fall
        // back to.)
        Assert.Contains(Validate("""
            section A { m { c4 d | } v { e4 f | } lyrics w sings v { la la | } }
            form main { A }
            score main { grandStaff { staff m  lyrics w  staff v } }
            """), d => d.Code == DiagnosticCodes.GroupRowNotBoundToStaffAbove);

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
            score main { staff m  lyrics w }
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
            score main { staff m  lyrics w }
            """,
            // sings + the bound ROW (the melody is not engraved).
            PartSheet,
            // The voice rule IS a binding: lyrics named after a voice.
            """
            section A { m { voice sop { c'4 d' | } { e4 f | } } lyrics sop { la la | } }
            form main { A }
            score main { staff m  lyrics sop }
            """,
            // A track named after the part itself.
            """
            section A { m { c4 d | } lyrics m { la la | } }
            form main { A }
            score main { staff m  lyrics m }
            """,
            // An unbound row stays the even-spread lead sheet.
            """
            section A { chords prog { C | } lyrics words { la la | } }
            form main { A }
            score main { chords prog lyrics words }
            """,
        ];
        foreach (var src in clean)
            Assert.DoesNotContain(Validate(src), d =>
                d.Code is DiagnosticCodes.GroupRowNotBoundToStaffAbove
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
            score main { staff m  lyrics w }
            """;
        Assert.DoesNotContain(Validate(src), d => d.Code == DiagnosticCodes.SingsConflict);
    }

    // ── the ROW spelling: the score row states the same track property ──

    [Fact]
    public void RowSpelledBinding_BindsLikeTheDefinitions()
    {
        // `score { … lyrics ja sings vocal }` — the parser used to hand `sings`
        // to the next render item and report a bogus "Undefined part: 'sings'".
        // Same rhythm claim as BoundRow_PlacesSyllablesAtTheMelodysRhythm: the
        // row carries the VOCAL's onsets, not the even spread, so the binding
        // resolved — through the row spelling alone.
        var tree = SyntaxTree.Parse("""
            time 4/4
            section Chorus {
              sax { c4 d e f | g2 g | }
              vocal { g8 g a4 a8 a a4 | g2 f | }
              lyrics ja { Sing it loud and clear now | ev- ery | }
            }
            form main { Chorus }
            score main { staff sax  lyrics ja sings vocal }
            """);
        Assert.DoesNotContain(SemanticValidation.Run(tree), d => d.Severity == DiagnosticSeverity.Error);

        var spec = RenderSpecParser.FindFirst(tree);
        var score = new MeasureCollector().CollectMultiStaff(tree, spec!);
        var bar1 = score.Lyrics.Where(l => l.IsLyricsRow && l.MeasureIndex == 0)
            .OrderBy(l => l.Timing).ToList();
        Assert.Equal(
            new[] { new Fraction(0, 1), new Fraction(1, 8), new Fraction(1, 4),
                    new Fraction(1, 2), new Fraction(5, 8), new Fraction(3, 4) },
            bar1.Select(l => l.Timing).ToArray());
    }

    [Fact]
    public void RowSpelledBinding_GoesThroughTheSameNets()
    {
        // Unknown target on the row → LYS7004, same as the definition's.
        Assert.Contains(Validate("""
            section A { m { c4 d | } lyrics w { la la | } }
            form main { A }
            score main { staff m  lyrics w sings ghost }
            """), d => d.Code == DiagnosticCodes.SingsTargetUnknown);

        // A row naming a DIFFERENT target than the definition is NOT a conflict
        // (user decision, 2026-09-02): the row's `sings` is that placement's own
        // melody. Until then this spelling was LYS7005 — "a track sings ONE part".
        Assert.DoesNotContain(Validate("""
            section A { m { c4 d | } v { e4 f | } lyrics w sings m { la la | } }
            form main { A }
            score main { staff m  lyrics w sings v }
            """), d => d.Code == DiagnosticCodes.SingsConflict);

        // Two DEFINITION blocks naming different targets still conflict: the
        // definition states the track's ONE default.
        Assert.Contains(Validate("""
            section A { m { c4 d | } v { e4 f | }
              lyrics w sings m { la la | }
              lyrics w sings v { lo lo | } }
            form main { A }
            score main { staff m  lyrics w }
            """), d => d.Code == DiagnosticCodes.SingsConflict);

        // A row repeating the definition's target identically is silent.
        Assert.DoesNotContain(Validate("""
            section A { m { c4 d | } lyrics w sings m { la la | } }
            form main { A }
            score main { staff m  lyrics w sings m }
            """), d => d.Code is DiagnosticCodes.SingsConflict
                             or DiagnosticCodes.SingsTargetUnknown);
    }

    // ── a row's `sings` is ITS OWN placement's melody (user decision, 2026-09-02) ──

    [Fact]
    public void RowSings_OverridesTheDefinitionsDefault_ForThatPlacement()
    {
        // The definition binds `ja` to the SAX (four quarters); the row says it
        // sings the VOCAL. The row therefore does not fold under the sax staff
        // (it is not the sax's verse) and its syllables sit at the vocal's
        // eighth-note onsets — the row's own target won, not the default.
        var tree = SyntaxTree.Parse("""
            time 4/4
            section Chorus {
              sax { c4 d e f | g2 g | }
              vocal { g8 g a4 a8 a a4 | g2 f | }
              lyrics ja sings sax { Sing it loud and clear now | ev- ery | }
            }
            form main { Chorus }
            score main { staff sax  lyrics ja sings vocal }
            """);
        Assert.DoesNotContain(SemanticValidation.Run(tree), d => d.Severity == DiagnosticSeverity.Error);

        var spec = RenderSpecParser.FindFirst(tree);
        Assert.Equal(2, spec!.Items.Length);
        Assert.IsType<LyricsRowSpec>(spec.Items[1]);
        var score = new MeasureCollector().CollectMultiStaff(tree, spec);
        var bar1 = score.Lyrics.Where(l => l.IsLyricsRow && l.MeasureIndex == 0)
            .OrderBy(l => l.Timing).ToList();
        Assert.Equal(
            new[] { new Fraction(0, 1), new Fraction(1, 8), new Fraction(1, 4),
                    new Fraction(1, 2), new Fraction(5, 8), new Fraction(3, 4) },
            bar1.Select(l => l.Timing).ToArray());

        // Positive control: the same book with the row's `sings` removed folds
        // under the sax (the default) and places no independent row at all.
        var folded = SyntaxTree.Parse("""
            time 4/4
            section Chorus {
              sax { c4 d e f | g2 g | }
              vocal { g8 g a4 a8 a a4 | g2 f | }
              lyrics ja sings sax { Sing it loud and clear now | ev- ery | }
            }
            form main { Chorus }
            score main { staff sax  lyrics ja }
            """);
        var foldedSpec = RenderSpecParser.FindFirst(folded);
        Assert.Equal(1, foldedSpec!.Items.Length);
        Assert.Contains("ja", ((SingleStaffSpec)foldedSpec.Items[0]).Staff.WithLyrics);
    }

    private const string Chorale = """
        time 4/4  key g major  octave absolute
        part sop { clef treble }
        part alt { clef treble }
        part bas { clef bass octave 3 }
        section Chorale {
          sop { b4 b c' d' | d'4 c' b a | }
          alt { g4. g8 g4 g | g4 g g fis | }
          bas { g,4 g, c g, | g,8 g, c4 g, d | }
          lyrics verse sings sop { Freu- de, schö- ner | Göt- ter- fun- ken, | }
        }
        form main { Chorale }
        score main {
          choirStaff {
            staff sop
            lyrics verse
            staff alt
            lyrics verse sings alt
            staff bas
            lyrics verse sings bas
          }
        }
        """;

    [Fact]
    public void OneTrack_PlacedUnderEveryStaffOfAChorale_EachRowSingsItsOwnStaff()
    {
        // The showcase shape that used to fail with LYS7005 × 2 and LYS6012 × 2:
        // one verse, every staff. Each row folds into the staff above it as that
        // staff's verse, so the group has three staves and no loose row.
        var tree = SyntaxTree.Parse(Chorale);
        Assert.DoesNotContain(SemanticValidation.Run(tree), d => d.Severity == DiagnosticSeverity.Error);

        var spec = RenderSpecParser.FindFirst(tree);
        var group = Assert.IsType<GrandStaffRenderSpec>(Assert.Single(spec!.Items));
        Assert.Equal(3, group.GrandStaff.Staves.Length);
        Assert.All(group.GrandStaff.Staves, s => Assert.Equal(new[] { "verse" }, s.WithLyrics));

        // …and each staff's copy of the words sits at THAT staff's onsets: the
        // alto's dotted first bar (0, 3/8, 1/2, 3/4) and the bass's eighth-note
        // second bar (0, 1/8, 1/4, 1/2) — neither is the soprano's four quarters.
        var score = new MeasureCollector().CollectMultiStaff(tree, spec);
        var byStaff = score.Lyrics.GroupBy(l => l.StaffIndex).ToDictionary(g => g.Key, g => g.ToList());
        Assert.Equal(new[] { 0, 1, 2 }, byStaff.Keys.OrderBy(k => k).ToArray());
        Assert.All(byStaff.Values, ls => Assert.Equal(8, ls.Count));

        static Fraction[] Timings(IEnumerable<Core.Svg.Model.LyricItem> items, int bar)
            => items.Where(l => l.MeasureIndex == bar).OrderBy(l => l.Timing).Select(l => l.Timing).ToArray();
        Assert.Equal(
            new[] { new Fraction(0, 1), new Fraction(1, 4), new Fraction(1, 2), new Fraction(3, 4) },
            Timings(byStaff[0], 0));
        Assert.Equal(
            new[] { new Fraction(0, 1), new Fraction(3, 8), new Fraction(1, 2), new Fraction(3, 4) },
            Timings(byStaff[1], 0));
        Assert.Equal(
            new[] { new Fraction(0, 1), new Fraction(1, 8), new Fraction(1, 4), new Fraction(1, 2) },
            Timings(byStaff[2], 1));
    }

    [Fact]
    public void GroupRow_WithoutItsOwnSings_StillNeedsTheStaffAbove()
    {
        // Positive control for LYS6012: drop the alto row's `sings` and the row
        // falls back to the definition's default (sop), which is not the staff
        // above it — the group refusal is still live.
        var src = Chorale.Replace("lyrics verse sings alt", "lyrics verse");
        Assert.Contains(Validate(src), d => d.Code == DiagnosticCodes.GroupRowNotBoundToStaffAbove);
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
            score main { staff m  lyrics w }
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
