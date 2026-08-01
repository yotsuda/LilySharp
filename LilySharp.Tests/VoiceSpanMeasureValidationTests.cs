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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A <c>voice { … } { … }</c> span holds SIMULTANEOUS music, and the bar check must
/// count it the way the collector renders it: voice 1 walks INLINE in the enclosing stream
/// (barlines and all) and voices 2..N are their own tracks over the same bars. Before this,
/// the span was one item of zero duration, which broke the check in both directions —
/// a bar holding a span was never checked at all, and every voice was separately checked as
/// a stream starting on a barline, so a span opened mid-bar reported phantom short measures.
/// </summary>
public sealed class VoiceSpanMeasureValidationTests
{
    private static IReadOnlyList<Diagnostic> Diagnose(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var validator = new MeasureValidator();
        validator.Validate(tree);
        return validator.Diagnostics;
    }

    private static bool AnyFullness(IReadOnlyList<Diagnostic> diags) => diags.Any(d =>
        d.Code == DiagnosticCodes.MeasureIncomplete
        || d.Code == DiagnosticCodes.MeasureOverflow
        || d.Code == DiagnosticCodes.PickupWithoutPartial
        || d.Code == DiagnosticCodes.MeasureDurationMismatch);

    [Fact]
    public void LeadVoiceIsInlined_SoAnOverfullBarWarnsLikeTheBareSpelling()
    {
        // `voice { c d e f } e f g a` engraves eight quarters in one 4/4 bar — exactly what
        // the bare spelling engraves, which warns LYS2002. The span must not hide it.
        var diags = Diagnose("time 4/4\npart mel {\n  section A { voice { c4 d e f } e f g a | }\n}\n");
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.MeasureOverflow);
    }

    [Fact]
    public void BareSpellingOfTheSameBar_WarnsTheSameWay()
    {
        // The control: without the braces the bar is the same music and the same warning.
        var diags = Diagnose("time 4/4\npart mel {\n  section A { c4 d e f e f g a | }\n}\n");
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.MeasureOverflow);
    }

    [Fact]
    public void SpanOpenedMidBar_FillsTheBarExactly_AndIsSilent()
    {
        // `c2 voice { d2 } { e2 }` is ONE full 4/4 bar (the renderer lays out exactly
        // one). It used to report THREE short "first measures": the enclosing bar (which
        // counted only `c2`), and each voice block validated as its own opening stream.
        var diags = Diagnose("time 4/4\npart mel {\n  section A { c2 voice { d2 } { e2 } | }\n}\n");
        Assert.False(AnyFullness(diags));
    }

    [Fact]
    public void SecondVoiceStartsFromTheSpansLeadIn_NotFromTheBarline()
    {
        // Same shape, but voice 2 is a quarter too short for the half bar left to fill.
        // The lead-in is what makes that visible — without it `e4` reads as a 1/4 opening
        // bar (a pickup nudge), with it as a 3/4 bar that misses a beat.
        var diags = Diagnose("time 4/4\npart mel {\n  section A { c2 voice { d2 } { e4 } | }\n}\n");
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.MeasureIncomplete);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.PickupWithoutPartial);
    }

    [Fact]
    public void BarlinesInsideTheLeadVoice_SplitTheEnclosingStream()
    {
        // The corpus shape (08-chorale): the span carries the bars, and the music after it
        // continues in the last one — the trailing `a` completes the LEAD voice's second
        // bar, which is why voice 2 has to write its own fourth beat. Two full bars, nothing
        // to report.
        var diags = Diagnose(
            "time 4/4\npart mel {\n" +
            "  section A { voice { c4 d e f | c4 d e } { e4 f g a | e4 f g a } a | }\n}\n");
        Assert.False(AnyFullness(diags));
    }

    [Fact]
    public void AShortBarInsideTheLeadVoice_StillWarns()
    {
        // Inlining must not swallow a real miscount: bar 2 of the lead voice is 3/4.
        var diags = Diagnose(
            "time 4/4\npart mel {\n" +
            "  section A { voice { c4 d e f | c4 d e | } { e4 f g a | e4 f g a | } }\n}\n");
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.MeasureIncomplete);
    }

    [Fact]
    public void AShortBarInASecondVoice_StillWarns()
    {
        // …and neither must it swallow one in a voice that is not the lead.
        var diags = Diagnose(
            "time 4/4\npart mel {\n" +
            "  section A { voice { c4 d e f | c4 d e f | } { e4 f g a | e4 f g | } }\n}\n");
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.MeasureIncomplete);
    }

    [Fact]
    public void CrossPart_ReadsOneStaffsBarOnce_NotOncePerVoice()
    {
        // The `beam-under-staves` shape. Reading every voice in document order made the
        // two-voice staff's bar last 2 while its partner's lasted 1 — a mismatch warning
        // for a pair of bars that align perfectly.
        var diags = Diagnose(
            "time 4/4\npart rh { clef treble }\npart lh { clef bass }\n" +
            "section S {\n  rh { voice { b1 } { g,8 g, g, g, g, g, g, g, } | }\n" +
            "  lh { d,1 | }\n}\n");
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.MeasureDurationMismatch);
    }

    [Fact]
    public void CrossPart_BarCountIsTheLeadVoices_NotTheSumOfAllVoices()
    {
        // Concatenating the voices doubled the staff's bar count, so a two-voice part read
        // as twice as long as the single-voice part beside it (the `hara-kiri` shape).
        var diags = Diagnose(
            "time 4/4\npart rh { clef treble }\npart lh { clef bass }\n" +
            "section S {\n  rh { voice { b1 | b1 | } { g,1 | g,1 | } }\n" +
            "  lh { d,1 | d,1 | }\n}\n");
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.SectionBarCountMismatch);
    }
}
