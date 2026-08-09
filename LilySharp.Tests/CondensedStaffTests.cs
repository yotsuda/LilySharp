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
using System.Text.RegularExpressions;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>condensedStaff { partA partB … }</c> — several parts, each of which would be its own
/// staff, put onto ONE staff as separate voices (a condensed score). The parts keep their own
/// notes and get the ordinary polyphony treatment; unisons are NOT merged and no a2/Solo text
/// is printed (that is the part combiner, a separate item).
/// </summary>
[Trait("Category", "Unit")]
public class CondensedStaffTests
{
    private const string Defaults = "octave absolute\ntime 4/4\n";

    /// <summary>Two parts of the same music, condensed onto one staff.</summary>
    private static string TwoParts(string render) => Defaults + """
        part fl1 { clef treble }
        part fl2 { clef treble }
        section A {
          fl1 { c'4 d' e' f' | g'2 g' | }
          fl2 { e4 f g a | b2 b | }
        }
        form main { ~A }
        """ + "\nscore main { " + render + " }\n";

    /// <summary>The SAME music written the way it can be written today: one part whose
    /// section holds a two-voice span. This is what a condensed staff must engrave as.</summary>
    private static readonly string TwoVoiceControl = Defaults + """
        part fl { clef treble }
        section A {
          fl { voice { c'4 d' e' f' | g'2 g' | } { e4 f g a | b2 b | } }
        }
        form main { ~A }
        score main { staff fl }
        """ + "\n";

    private static string Svg(string source) => SvgGenerator.Generate(
        SyntaxTree.Parse(source),
        new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });

    /// <summary>Everything the compiler says about this source — PARSE diagnostics as well
    /// as semantic ones. The bad-member rule is reported by the parser (that is where the
    /// offending tokens are), so a semantics-only sweep would miss it.</summary>
    private static IReadOnlyList<Diagnostic> Diagnose(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var all = new List<Diagnostic>(tree.Diagnostics);
        foreach (var v in SemanticValidation.CreateAll())
        {
            v.Validate(tree);
            all.AddRange(v.Diagnostics);
        }
        return all;
    }

    private static List<double> GlyphXs(string svg) =>
        Regex.Matches(svg, "<text class=\"music\" x=\"([-\\d.]+)\"")
            .Select(m => double.Parse(m.Groups[1].Value)).ToList();

    private static List<double> AllYs(string svg) =>
        Regex.Matches(svg, "(?:y|y1|y2)=\"([-\\d.]+)\"")
            .Select(m => double.Parse(m.Groups[1].Value)).ToList();

    /// <summary>Staff-line rows: five per staff, so this counts staves.</summary>
    private static int StaffCount(string svg) =>
        Regex.Matches(svg, "<line x1=\"0.00\"[^>]*stroke-width=\"0.100\"").Count / 5;

    [Fact]
    public void TwoParts_ShareOneStaff()
    {
        // The point of the whole thing: two parts in, ONE staff out.
        Assert.Equal(1, StaffCount(Svg(TwoParts("condensedStaff { fl1 fl2 }"))));
    }

    [Fact]
    public void BothPartsAreEngraved_NotJustTheFirst()
    {
        // Eight noteheads plus the clef and the time signature's two digits — the same
        // glyph count the two-voice control produces. A condensed staff that quietly
        // dropped its second part would still be "one staff" and still look plausible.
        Assert.Equal(GlyphXs(Svg(TwoVoiceControl)).Count,
                     GlyphXs(Svg(TwoParts("condensedStaff { fl1 fl2 }"))).Count);
    }

    [Fact]
    public void VerticalPlacementIsExactlyTheTwoVoiceSpan()
    {
        // ★ The strong claim, and the one that says the parts really became voices 1 and 2:
        // EVERY y — staff lines, notehead rows, stem ends, the barline rects — matches the
        // one-part two-voice control exactly. That covers stem directions (voice 1 up, voice
        // 2 down), stem lengths, and the collision treatment between them.
        Assert.Equal(AllYs(Svg(TwoVoiceControl)),
                     AllYs(Svg(TwoParts("condensedStaff { fl1 fl2 }"))));
    }

    [Fact]
    public void SourceOrderIsVoiceOrder()
    {
        // Swapping the two parts swaps which one gets voice 1, so the Y layout changes.
        // (If order were ignored, these two would engrave identically.)
        Assert.NotEqual(AllYs(Svg(TwoParts("condensedStaff { fl1 fl2 }"))),
                        AllYs(Svg(TwoParts("condensedStaff { fl2 fl1 }"))));
    }

    [Fact]
    public void HorizontalSpacingStillDriftsFromTheTwoVoiceSpelling()
    {
        // ⚠️ KNOWN RESIDUAL, pinned so it cannot grow unnoticed. The same music spelled as
        // two condensed parts spaces the FIRST bar identically to the one-part two-voice
        // span, then drifts: measured 0.08 at bar 2's first note, rising to 0.11 and
        // levelling off (scratch/lpreg/cond3-{probe,ctl}.lys). Vertical placement is exact,
        // so this is the measure spring, not the voice assignment.
        var condensed = GlyphXs(Svg(TwoParts("condensedStaff { fl1 fl2 }")));
        var control = GlyphXs(Svg(TwoVoiceControl));

        Assert.Equal(control[2], condensed[2]);  // bar 1 first notehead: exact
        double drift = condensed[^1] - control[^1];
        Assert.InRange(drift, 0.0, 0.2);
    }

    [Fact]
    public void ThreeOrMoreParts_AreAllowed()
    {
        // ⚠️ Part names avoid the single letters a-g: those are PITCHES.
        string src = Defaults + """
            part hn1 { clef treble }
            part hn2 { clef treble }
            part hn3 { clef treble }
            section A {
              hn1 { c'4 d' e' f' | }
              hn2 { e4 f g a | }
              hn3 { c4 c c c | }
            }
            form main { ~A }
            score main { condensedStaff { hn1 hn2 hn3 } }
            """ + "\n";

        Assert.Empty(Diagnose(src).Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Equal(1, StaffCount(Svg(src)));
    }

    [Fact]
    public void AStaffAfterACondensedStaff_KeepsItsOwnStaffIndex()
    {
        // ⚠️ The condensed parts yield one voice BINDING each but one STAFF, so a caller
        // that counted bindings would tag every later staff one index too high. Two staves
        // must come out, and the second must carry its own part's music.
        string src = Defaults + """
            part fl1 { clef treble }
            part fl2 { clef treble }
            part bass { clef bass }
            section A {
              fl1 { c'4 d' e' f' | }
              fl2 { e4 f g a | }
              bass { c2 g | }
            }
            form main { ~A }
            score main { condensedStaff { fl1 fl2 }  staff bass }
            """ + "\n";

        Assert.Empty(Diagnose(src).Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Equal(2, StaffCount(Svg(src)));
    }

    [Fact]
    public void OnePart_IsReportedAndNamesTheRuleItBroke()
    {
        var errors = Diagnose(TwoParts("condensedStaff { fl1 }"))
            .Where(d => d.Code == DiagnosticCodes.CondensedStaffNeedsTwoParts).ToList();

        var e = Assert.Single(errors);
        // Not "your score declares no staff", which is what the neighbouring grandStaff
        // makes an under-filled group say: the message must name the real mistake and the
        // way out.
        Assert.Contains("staff fl1", e.Message);
    }

    [Fact]
    public void NoParts_IsReported()
        => Assert.Single(Diagnose(TwoParts("condensedStaff { }"))
            .Where(d => d.Code == DiagnosticCodes.CondensedStaffNeedsTwoParts));

    [Fact]
    public void ANestedStaffGroup_IsReported()
    {
        // Everything inside becomes a VOICE of the one staff, and a braced group of staves
        // is not a voice.
        var errors = Diagnose(TwoParts("condensedStaff { grandStaff { staff fl1 staff fl2 } }"))
            .Where(d => d.Code == DiagnosticCodes.CondensedStaffBadMember).ToList();

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void AStaffItemInside_IsReported()
    {
        // `condensedStaff { staff fl1 staff fl2 }` is the shape a grandStaff user will try
        // first; it must say why the members are bare names here.
        var errors = Diagnose(TwoParts("condensedStaff { staff fl1 staff fl2 }"))
            .Where(d => d.Code == DiagnosticCodes.CondensedStaffBadMember).ToList();

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void TheKeywordIsCaseSensitive()
    {
        // Keywords are ordinal-matched, so `condensedstaff` is not the keyword. It must not
        // silently become something else; today it falls through to a part reference and is
        // reported as an undefined part.
        var errors = Diagnose(TwoParts("condensedstaff { fl1 fl2 }"))
            .Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        Assert.NotEmpty(errors);
    }
}
