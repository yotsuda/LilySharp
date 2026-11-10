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

using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// SCORE = A VERTICAL STACK OF BANDS (user decision, 2026-08-19, closed before
/// the first tag): a bound <c>lyrics NAME</c> row standing directly BELOW the
/// staff whose part it sings IS that staff's attachment — the association is
/// written at the definition (<c>sings</c>, or the name-is-binding voice rule),
/// the score only orders the bands. These tests pin the fold
/// (RenderSpecParser.FoldAdjacentRows) at the spec level, and the last one pins
/// the identity that makes the fold the whole port: the folded row renders
/// byte-identically (modulo source positions) to the attachment it folds into.
/// </summary>
[Trait("Category", "Unit")]
public class ScoreRowFoldingTests
{
    private static RenderSpec SpecOf(string src)
    {
        var spec = RenderSpecParser.FindFirst(SyntaxTree.Parse(src));
        Assert.NotNull(spec);
        return spec!;
    }

    private const string BoundBody = """
        time 4/4
        section A {
          vocal { c''4 d' e' f' | g'2 g | }
          lyrics ja { la la la la | ho ho | }
          lyrics en { na na na na | go go | }
        }
        form { A }
        """;

    [Fact]
    public void BoundRowAfterItsStaff_FoldsIntoAnAttachedVerse()
    {
        var spec = SpecOf(BoundBody + "score { staff vocal  lyrics ja on vocal }");

        var staff = Assert.IsType<SingleStaffSpec>(Assert.Single(spec.Items)).Staff;
        Assert.Equal(new[] { "ja" }, staff.WithLyrics);
    }

    [Fact]
    public void ARunOfBoundRows_StacksAsVersesInWrittenOrder()
    {
        var spec = SpecOf(BoundBody + "score { staff vocal  lyrics ja on vocal  lyrics en on vocal }");

        var staff = Assert.IsType<SingleStaffSpec>(Assert.Single(spec.Items)).Staff;
        Assert.Equal(new[] { "ja", "en" }, staff.WithLyrics);
    }

    /// <summary>
    /// The part sheet carrying another part's words (test/sings-chorus-row): the
    /// row's binding is NOT the adjacent staff's part, so it keeps its place as an
    /// independent band — placement cannot re-decide the association.
    /// </summary>
    [Fact]
    public void RowSingingAnotherPart_KeepsItsPlaceAsABand()
    {
        var spec = SpecOf("""
            time 4/4
            section A {
              sax { c'4 d e f | }
              vocal { g'8 g a4 a8 a a4 | }
              lyrics ja { la la la la la la | }
            }
            form { A }
            score { staff sax  lyrics ja on vocal }
            """);

        Assert.Equal(2, spec.Items.Length);
        Assert.IsType<LyricsRowSpec>(spec.Items[1]);
        Assert.Empty(Assert.IsType<SingleStaffSpec>(spec.Items[0]).Staff.WithLyrics);
    }

    [Fact]
    public void UnboundRow_StaysTheEvenSpreadLeadSheetRow()
    {
        var spec = SpecOf("""
            time 4/4
            section A {
              melody { c''4 d' e' f' | }
              lyrics words { la la | }
            }
            form { A }
            score { lyrics words  staff melody }
            """);

        Assert.Equal(2, spec.Items.Length);
        Assert.IsType<LyricsRowSpec>(spec.Items[0]);
    }

    /// <summary>The pre-<c>sings</c> voice rule, kept because the name IS the
    /// binding: <c>voice sop { } + lyrics sop { }</c> — a row named after one of
    /// the staff part's voices folds under that staff.</summary>
    [Fact]
    public void RowSingingAVoiceOfTheStaffsPart_Folds()
    {
        var spec = SpecOf("""
            time 4/4
            section A {
              m { voice sop { c''4 d' e' f' | } alt { e'4 f g a | } }
              lyrics w { la la la la | }
            }
            form { A }
            score { staff m  lyrics w on sop }
            """);

        var staff = Assert.IsType<SingleStaffSpec>(Assert.Single(spec.Items)).Staff;
        Assert.Equal(new[] { "w" }, staff.WithLyrics);
    }

    /// <summary>
    /// A LEADING chords row (nothing staff-like above it) is NOT folded: the row
    /// already is the LilyPond-ported adhesion for the system-opening regime.
    /// <c>lyrics.chord-row.between-systems.*</c> measured LilyPond putting the
    /// row INTO the lyric loose chain (page-layout-problem.cc, ported
    /// 2026-07-27, residuals are font terms only); folding it into the
    /// attached-chords engraver measurably moved that geometry AWAY from
    /// LilyPond (residual −0.002157 → +0.030400, 2026-08-19).
    /// </summary>
    [Fact]
    public void LeadingChordRow_IsAlreadyTheAdhesion_NotFolded()
    {
        var spec = SpecOf("""
            time 4/4
            section A {
              melody { c''4 d' e' f' | }
              chords prog { C | }
            }
            form { A }
            score { chords prog  staff melody }
            """);

        Assert.Equal(2, spec.Items.Length);
        Assert.IsType<ChordRowSpec>(spec.Items[0]);
        Assert.Null(Assert.IsType<SingleStaffSpec>(spec.Items[1]).Staff.WithChords);
    }

    /// <summary>
    /// An INTERIOR chords row (a staff above, a staff below) folds into the
    /// staff below as its attached symbols — the between-staves band placement
    /// reads no up-skyline, and the attached engraver's reservation is the
    /// machinery that was measured to clear the staff-below's pushed-out ink
    /// (ChordRowOnALowerStaff_ClearsARestAnotherVoicePushedOutOfIt).
    /// </summary>
    [Fact]
    public void InteriorChordRow_FoldsIntoTheStaffBelow()
    {
        var spec = SpecOf("""
            time 4/4
            section A {
              hi { c''4 d' e' f' | }
              lo { c'4 d e f | }
              chords prog { C | }
            }
            form { A }
            score { staff hi  chords prog  staff lo }
            """);

        Assert.Equal(2, spec.Items.Length);
        Assert.Null(Assert.IsType<SingleStaffSpec>(spec.Items[0]).Staff.WithChords);
        Assert.Equal("prog", Assert.IsType<SingleStaffSpec>(spec.Items[1]).Staff.WithChords);
    }

    [Fact]
    public void BoundRowAfterAGroup_FoldsIntoTheGroupsLastStaff()
    {
        var spec = SpecOf("""
            time 4/4
            section A {
              rh { c''4 d' e' f' | }
              lh { c'4 d e f | }
              lyrics words { la la la la | }
            }
            form { A }
            score { grandStaff { staff rh  staff lh }  lyrics words on lh }
            """);

        var group = Assert.IsType<GrandStaffRenderSpec>(Assert.Single(spec.Items)).GrandStaff;
        Assert.Empty(Assert.IsType<SingleStaffSpec>(group.Members[0]).Staff.WithLyrics);
        Assert.Equal(new[] { "words" }, Assert.IsType<SingleStaffSpec>(group.Members[1]).Staff.WithLyrics);
    }

    private const string ChoraleBody = """
        time 4/4
        part sop { clef treble }
        part alt { clef treble }
        section A {
          sop { c''4 d' e' f' | }
          alt { e'4 f g a | }
          lyrics words { la la la la | }
        }
        form { A }
        """;

    /// <summary>Score = a vertical stack of bands INSIDE a group too: the chorale
    /// writes its words between the staves, and the row folds into the staff
    /// directly above it (⑵ of the with-clause removal plan).</summary>
    [Fact]
    public void BoundRowInsideAGroup_FoldsIntoTheStaffAbove()
    {
        var spec = SpecOf(ChoraleBody
            + "score { choirStaff { staff sop  lyrics words on sop  staff alt } }");

        var group = Assert.IsType<GrandStaffRenderSpec>(Assert.Single(spec.Items)).GrandStaff;
        Assert.Equal(2, group.StaffCount);
        Assert.Equal(new[] { "words" }, Assert.IsType<SingleStaffSpec>(group.Members[0]).Staff.WithLyrics);
        Assert.Empty(Assert.IsType<SingleStaffSpec>(group.Members[1]).Staff.WithLyrics);
    }

    /// <summary>A row with its own <c>on</c> stands where it is written, on a band of its own,
    /// at that part's rhythm (docs/lyrics-singer-design.md §9.3): LilyPond's Lyrics context
    /// between the staves.</summary>
    [Fact]
    public void RowInAGroupOnAnotherPart_StandsOnItsOwnBand()
    {
        var src = ChoraleBody + "score { choirStaff { staff sop  lyrics words on alt  staff alt } }";
        var tree = SyntaxTree.Parse(src);
        Assert.DoesNotContain(LilySharp.Core.Semantics.SemanticValidation.Run(tree), d => d.Code == "LYS6012");

        var spec = SpecOf(src);
        var group = Assert.IsType<GrandStaffRenderSpec>(Assert.Single(spec.Items)).GrandStaff;
        Assert.Equal(2, group.StaffCount);
        Assert.Equal(new LyricsRowSpec("words", "alt"), group.Members[1]);
        Assert.Empty(Assert.IsType<SingleStaffSpec>(group.Members[0]).Staff.WithLyrics);

        var score = new MeasureCollector().CollectMultiStaff(tree, spec);
        var staves = score.StaffGroups.SelectMany(g => g.Staves).ToList();
        Assert.Equal(new[] { false, true, false }, staves.Select(s => s.IsTextRow).ToArray());
        Assert.All(score.StaffGroups, g => Assert.NotNull(g.Outer));
        Assert.Equal(new[] { 1 }, score.Lyrics.Select(l => l.StaffIndex).Distinct().ToArray());
    }

    /// <summary>The twin writes the standing row between the staves, once, inside the group.</summary>
    [Fact]
    public void TheTwinWritesAStandingRowInsideTheGroup_BetweenTheStaves()
    {
        string ly = new LilySharp.Core.LilyPond.LilyPondExporter().Export(
            SyntaxTree.Parse(ChoraleBody + "score { choirStaff { staff sop  lyrics words on alt  staff alt } }"));

        int group = ly.IndexOf(@"\new ChoirStaff", StringComparison.Ordinal);
        int row = ly.IndexOf(@"\new Lyrics", StringComparison.Ordinal);
        int end = ly.IndexOf(">>", row, StringComparison.Ordinal);
        Assert.True(group >= 0 && group < row, ly);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(ly, @"\\new Lyrics"));
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(ly[..row], @"\\new Staff").Count);
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(ly[row..end], @"\\new Staff").Count);
    }

    /// <summary>A singer line reads a row standing in a group the way it reads a loose one:
    /// the row says whose notes the words follow, and the words are read once, on that part.</summary>
    [Fact]
    public void ASingerLineReadsTheRowStandingInAGroup()
    {
        var src = ChoraleBody + "singer female sings words\nscore { choirStaff { staff sop  lyrics words on alt  staff alt } }";
        var root = SyntaxTree.Parse(src).GetRoot();

        Assert.Equal("alt", Assert.Single(LilySharp.Core.Music.LyricSinger.LinesOf(root)).Part);
        var read = LilySharp.Core.Music.LyricSinger.WithSungWords(root, SpecOf(src));
        Assert.True(read.GetVoiceBindings().Single(b => b.VoiceName == "sop").WithLyrics.IsDefaultOrEmpty);
        Assert.Equal(["words"], read.GetVoiceBindings().Single(b => b.VoiceName == "alt").WithLyrics);
        Assert.DoesNotContain(((GrandStaffRenderSpec)read.Items[0]).GrandStaff.Members, m => m is LyricsRowSpec);
    }

    [Fact]
    public void RowInAGroupWithNoStaffAboveAndNoOn_IsRefused()
    {
        var src = ChoraleBody + "score { choirStaff { lyrics words  staff sop  staff alt } }";
        var diags = LilySharp.Core.Semantics.SemanticValidation.Run(SyntaxTree.Parse(src));

        var d = Assert.Single(diags, d => d.Code == "LYS6012");
        Assert.Contains("before any staff", d.Message);
    }

    /// <summary>A second row under a standing row no longer stands directly below its staff,
    /// so it stands too — it used to fold nowhere and vanish.</summary>
    [Fact]
    public void RowWithNoOnUnderAStandingRow_StandsOnTheStaffAbovesPart()
    {
        var src = ChoraleBody.Replace("lyrics words { la la la la | }", "lyrics words { la la la la | }\n  lyrics more { lo lo lo lo | }")
            + "score { choirStaff { staff sop  lyrics words on alt  lyrics more  staff alt } }";
        var group = Assert.IsType<GrandStaffRenderSpec>(Assert.Single(SpecOf(src).Items)).GrandStaff;

        Assert.Equal(new LyricsRowSpec("more", "sop"), group.Members[2]);
    }

    [Fact]
    public void TheSameWordsOnOnePartTwiceInAGroup_AreWarned()
    {
        var src = ChoraleBody + "score { choirStaff { staff sop  lyrics words on alt  staff alt  lyrics words on alt } }";
        var diags = LilySharp.Core.Semantics.SemanticValidation.Run(SyntaxTree.Parse(src));

        var d = Assert.Single(diags, d => d.Code == "LYS6015");
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
    }

    /// <summary>One track on two parts is two rows, each at its own part's rhythm.</summary>
    [Fact]
    public void OneTrackOnTwoPartsInAGroup_EachRowKeepsItsOwnRhythm()
    {
        const string src = """
            time 4/4
            part sop { clef treble }
            part alt { clef treble }
            section A {
              sop { c''2 d | }
              alt { e'4 f g a | }
              lyrics w { la la | }
            }
            form { A }
            score { staffGroup { staff sop  lyrics w on alt  staff alt  lyrics w on sop } }
            """;
        var tree = SyntaxTree.Parse(src);
        var score = new MeasureCollector().CollectMultiStaff(tree, SpecOf(src));

        Fraction[] Timings(int staff) => score.Lyrics.Where(l => l.StaffIndex == staff)
            .OrderBy(l => l.Timing).Select(l => l.Timing).ToArray();
        Assert.Equal(new[] { new Fraction(0, 1), new Fraction(1, 4) }, Timings(1));
        Assert.Equal(new[] { new Fraction(0, 1), new Fraction(1, 2) }, Timings(3));
    }

    [Fact]
    public void NonStaffNonRowMemberInAGroup_IsReportedAtTheMember()
    {
        var tree = SyntaxTree.Parse(ChoraleBody
            + "score { grandStaff { staff sop  tab alt } }");

        var d = Assert.Single(tree.Diagnostics, d => d.Code == "LYS6011");
        Assert.Contains("cannot contain 'tab'", d.Message);
    }

    /// <summary>
    /// THE DOOR IS GONE, NOT JUST CLOSED: `with` stopped being a keyword when
    /// its clause-removal error LYS0031 retired (2026-08-19, user decision —
    /// still before the first tag; the DiagnosticCodes remarks record the
    /// number). And since the bare display name retired too (2026-08-23, user
    /// decision — GRAMMAR_AUDIT section 3.1: display names are quoted-only, so
    /// a trailing bare identifier is always the MIDI-only part reference), the
    /// old clause spelling now reads as a staff, a reference to an undeclared
    /// part `with` — which is diagnosed, not silently swallowed as a label —
    /// and a `lyrics ja` row, which still folds onto the staff.
    /// </summary>
    [Fact]
    public void TheRetiredWithSpelling_ReadsAsAPartRefAndARow()
    {
        var source = BoundBody + "score { staff vocal with lyrics ja on vocal }";
        var spec = SpecOf(source);
        var staff = Assert.IsType<SingleStaffSpec>(Assert.Single(spec.Items)).Staff;
        Assert.Null(staff.InstrumentName);
        Assert.Equal(new[] { "ja" }, staff.WithLyrics);

        // The undeclared `with` is named by a diagnostic — before 2026-08-23 it
        // was eaten as vocal's display label, in silence.
        var tree = SyntaxTree.Parse(source);
        Assert.Contains(LilySharp.Core.Semantics.SemanticValidation.Run(tree),
            d => d.Message.Contains("with"));
    }

    /// <summary>
    /// The unnamed <c>chords { }</c> block is the file's unnamed track (2026-10-09), and a
    /// bare <c>chords</c> row places it — a row the score writes, never the co-writing
    /// association LYS0032 refused until then (stated nowhere, hard-coded to staff 0).
    /// </summary>
    [Fact]
    public void TheUnnamedChordsBlock_IsPlacedByABareRow()
    {
        string source = """
            time 4/4
            section A {
              melody { c''4 d' e' f' | }
              chords { C | }
            }
            form { A }
            score { chords  staff melody }
            """;
        Assert.Empty(SyntaxTree.Parse(source).Diagnostics);
        var row = Assert.IsType<ChordRowSpec>(SpecOf(source).Items[0]);
        Assert.Equal(SyntaxFacts.UnnamedChordsName, row.PartName);
    }
}
