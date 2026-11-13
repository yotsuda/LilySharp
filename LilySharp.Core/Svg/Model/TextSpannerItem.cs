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

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Style of the text spanner line.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm:3523 (style . dashed-line)
/// </remarks>
public enum TextSpannerStyle
{
    /// <summary>Dashed line (default for most text spanners).</summary>
    DashedLine,
    /// <summary>Solid continuous line.</summary>
    Line,
    /// <summary>No line (text only).</summary>
    None
}

/// <summary>
/// Which line of its voice a text spanner is: the general one (<c>@rit</c>, <c>@accel</c>,
/// <c>@rall</c>, <c>@textSpan("…")</c>), or one of the three guitar lines the owner approved on
/// 2026-11-13 (grammar evaluation 3) — <c>@palmMute … @!palmMute</c> ("P.M."),
/// <c>@letRing … @!letRing</c> ("let ring") and <c>@barre(7) … @!barre</c> ("CVII").
/// </summary>
/// <remarks>
/// Each family is paired on its own (<c>TextSpannerEngraver.PairTextSpanners</c>): a palm mute
/// runs under a barre and beside a <c>rit.</c>, where two general spanners in one voice cannot
/// both be open.
/// LILYSHARP-OWN: LilyPond has ONE text spanner per voice (lily/text-spanner-engraver.cc
/// Text_spanner_engraver holds one <c>span_</c>) and no palm-mute, let-ring or barre spanner at
/// all; its users write each with <c>\startTextSpan</c> and an override of the bound texts,
/// which is the picture these follow (the LilyPond twin writes exactly that).
/// </remarks>
public enum TextSpanFamily
{
    /// <summary>The general text spanner.</summary>
    Text,
    /// <summary>"P.M." — the palm of the picking hand on the strings.</summary>
    PalmMute,
    /// <summary>"let ring" — the notes sound on to the end of the line.</summary>
    LetRing,
    /// <summary>"CVII" / "½CVII" — a finger laid across the strings at a fret.</summary>
    Barre,
}

/// <summary>
/// Represents a text spanner (text label with extending dashed/solid line).
/// Used for rit., accel., and similar expression markings that span a duration.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/text-spanner-engraver.cc TextSpanner engraver
/// LILYPOND-REF: scm/define-grobs.scm:3504-3535 TextSpanner grob
/// LILYPOND-REF: scm/define-grobs.scm:1331-1385 DynamicTextSpanner grob
///
/// Text spanners display text at the start point with a dashed/solid line
/// extending to the end point. Common uses:
/// - rit. --------
/// - accel. --------
/// - cresc. --------  (text alternative to hairpin wedge)
/// </remarks>
public sealed record TextSpannerItem(
    // The display text (e.g., "rit.", "accel.").
    string Text,
    // Measure index of the start point.
    int StartMeasureIndex,
    // Item index within the start measure.
    int StartItemIndex,
    // Measure index of the end point.
    int EndMeasureIndex,
    // Item index within the end measure.
    int EndItemIndex,
    // Line style (dashed, solid, none).
    TextSpannerStyle Style,
    // Source position for click-to-source mapping.
    int SourcePosition,
    // F3/B: index of the originating rit/accel mark in score.MusicMarks,
    // so a reused layout re-derives data-pos from the live score. -1 = unresolved.
    int SourceIndex = -1,
    // The staff this spanner belongs to (0 = first/only staff).
    int StaffIndex = 0,
    // The voice of that staff the start mark was written in: the item indices count THAT
    // voice's items (Text_spanner_engraver is a Voice-context engraver), so both bounds are
    // resolved through it (see TextSpannerEngraver.BoundX).
    int VoiceIndex = 0,
    // Which line it is (TextSpanFamily): the guitar lines end in a hook, and a barre with no
    // end is its label alone (Style None).
    TextSpanFamily Family = TextSpanFamily.Text
)
{
    /// <summary>Whether the line ends in a hook turned to the staff: the three guitar lines do
    /// (owner-approved 2026-11-13, grammar evaluation 3), the general spanner does not.</summary>
    public bool EndHook => Family != TextSpanFamily.Text && Style != TextSpannerStyle.None;

    // Identity, not value equality: see ModelIdentity.
    public bool Equals(TextSpannerItem? other) => ReferenceEquals(this, other);

    /// <inheritdoc/>
    public override int GetHashCode() => ModelIdentity.HashOf(this);
}
