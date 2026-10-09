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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The meter the LilyPond twin keeps across a section boundary and across a nested body —
/// two rules stage C4 (REFACTOR_PLAN, p745) found observed by no test: the HOME meter keeps
/// its additive text, so a section that reverts to it restates <c>\time #'((3 2) . 8)</c> and
/// not <c>\time 5/8</c>; and a nested body hands the meter it changed back, so the section
/// after it restates the home meter LilyPond would otherwise never see restored.
/// </summary>
[Trait("Category", "Unit")]
public class LilyPondExporterMeterNetsTests
{
    private static string Export(string lys)
    {
        var tree = SyntaxTree.Parse(lys);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        return new LilyPondExporter().Export(tree);
    }

    /// <summary>The home is `3+2/8`; A changes to 4/4 mid-section; B states no meter and
    /// so reopens at the home — written as LilyPond's pair, numerator groups and all.</summary>
    [Fact]
    public void ASectionRevertingToAnAdditiveHome_RestatesItWithItsGroups()
    {
        var ly = Export("""
            octave absolute
            time 3+2/8
            part m { clef treble }
            section A { m { c'8 d' e' f' g' | time 4/4 a'1 | } }
            section B { m { b'8 a' g' f' e' | } }
            form { A B }
            score { staff m }
            """);
        Assert.Contains("\\time #'((3 2) . 8) \\mark \\markup \\box \"B\"", ly);
        Assert.DoesNotContain("\\time 5/8", ly);
    }

    /// <summary>The home meter's groups are what a `time 3+2/8` at the FIRST section's head
    /// is compared with — the part opens at the home (BarContext.RevertToHome, no \time node
    /// re-read) — so the restatement writes nothing: ONE `\time #'((3 2) . 8)`, the part's.
    /// A home remembered as a bare 5/8 would read it as a change and write it again (p745 ①:
    /// the one place the home's own text, not the \time node's, is what is compared).</summary>
    [Fact]
    public void ARestatementOfTheAdditiveHome_AtTheFirstSectionsHead_WritesNothing()
    {
        var ly = Export("""
            octave absolute
            time 3+2/8
            part m { clef treble }
            section A { m { time 3+2/8 c'8 d' e' f' g' | } }
            form { A }
            score { staff m }
            """);
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(ly, @"\\time #'\(\(3 2\) \. 8\)").Count);
    }

    /// <summary>A `time` at B's head that states the HOME the head restores is the restore
    /// said twice: LilyPond draws a TimeSignature for every \time, so the twin writes the
    /// restore alone. (A `time` restating what the section BEFORE left cancels the restore
    /// instead and writes nothing — the page draws neither; that rule is unchanged.)</summary>
    [Fact]
    public void ATimeAtTheHead_StatingTheHomeTheHeadRestores_IsWrittenOnce()
    {
        var ly = Export("""
            octave absolute
            time 3+2/8
            part m { clef treble }
            section A { m { c'8 d' e' f' g' | time 4/4 a'1 | } }
            section B { m { time 3+2/8 b'8 a' g' f' e' | } }
            form { A B }
            score { staff m }
            """);
        Assert.Contains("\\time #'((3 2) . 8) \\mark \\markup \\box \"B\" b'8", ly);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(ly, @"\\time #'\(\(3 2\) \. 8\)").Count);
    }

    /// <summary>
    /// A phrase body that changes the meter leaves the section in that meter, on the page
    /// (the body is walked inline; the MusicXML writes B's 4/4 again) and in LilyPond (the
    /// body is inlined under a nested \relative, and \time is Timing's, not the block's). So
    /// B, which states no meter, has to restate the home 4/4 — until 2026-10-03 the twin
    /// carried only the body's last note value out, kept 4/4 in its own books, and wrote no
    /// \time at B: LilyPond read B's whole note in 3/4 and failed its bar check.
    /// </summary>
    [Fact]
    public void APhraseBodyThatChangesTheMeter_LeavesTheSectionInIt_AndTheNextSectionRestatesTheHome()
    {
        var ly = Export("""
            octave absolute
            time 4/4
            phrase ph { time 3/4 c'4 d' e' | }
            part m { clef treble }
            section A { m { ph f'4 g' a' | } }
            section B { m { c''1 | } }
            form { A B }
            score { staff m }
            """);
        Assert.Contains("\\time 3/4 c'4 d' e' | f'4 g' a' |", ly);
        Assert.Contains("\\time 4/4 \\mark \\markup \\box \"B\" c''1 |", ly);
    }

    /// <summary>The key the same way: a phrase body's `key g major` is the section's from
    /// there (the MusicXML writes fifths 1 on that bar and fifths 0 again on B's), so B
    /// restates `\key c \major`.</summary>
    [Fact]
    public void APhraseBodyThatChangesTheKey_LeavesTheSectionInIt_AndTheNextSectionRestatesTheHome()
    {
        var ly = Export("""
            octave absolute
            time 4/4
            key c major
            phrase ph { key g major fis'4 g' a' b' | }
            part m { clef treble }
            section A { m { ph fis'4 g' a' b' | } }
            section B { m { c''1 | } }
            form { A B }
            score { staff m }
            """);
        Assert.Contains("\\key g \\major fis'4 g' a' b' | fis'4 g' a' b' |", ly);
        Assert.Contains("\\key c \\major \\mark \\markup \\box \"B\" c''1 |", ly);
    }
}
