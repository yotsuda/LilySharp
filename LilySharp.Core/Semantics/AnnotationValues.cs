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

using System;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// What the value-carrying annotations MEAN, read once from their arguments.
/// </summary>
/// <remarks>
/// <para>
/// These four families — <c>@finger(N)</c>, <c>@pluck(p|i|m|a)</c>,
/// <c>@bend(half|full|N)</c>, <c>@notehead(style)</c> — are the ones
/// <c>docs/VALUE_SITE_AUDIT.md</c> §9.5 ⑵ names as the first to move, because their
/// argument IS a value: nothing is lost by reading it as one. (The families whose
/// spelling is their meaning — <c>@frame</c>'s position string — and the sub-language
/// one — <c>@chord</c> — are deliberately last.)
/// </para>
/// <para>
/// Each is read HERE and only here. Before this type, every one of them was spelled
/// twice: once by the consumer that acts on it, and once again by
/// <see cref="AnnotationNameValidator.IsKnownCompoundName"/> deciding whether anything
/// consumes it — the second copy being the tenth restatement §9.3 counted. Two copies
/// of one predicate is the shape HANDOFF §5.2.1② names, and these two DID disagree; see
/// <see cref="Finger"/>.
/// </para>
/// <para>
/// ⚠️ Each reads the ARGUMENT, not the dotted <see cref="MusicMarkSyntax.MarkName"/>.
/// The gates below are the ones the string-slicing versions applied, transcribed rather
/// than tidied, with the one exception recorded on <see cref="Finger"/>.
/// </para>
/// </remarks>
public static class AnnotationValues
{
    /// <summary>
    /// The finger number of <c>@finger(N)</c>, or null when this is not one.
    /// </summary>
    /// <remarks>
    /// Any non-negative integer, which is the set LilyPond's own grammar takes (measured
    /// on 2.26.0: <c>-0</c>, <c>-5</c>, <c>-6</c> and <c>-12</c> all engrave a Fingering).
    /// <para>
    /// ⚠️ <b>A behaviour change, declared:</b> the two former copies disagreed about
    /// <c>@finger("3")</c>. The collector's read (<c>int.TryParse</c> on the dotted tail)
    /// rejected the quotes, so Lily# drew nothing; the LilyPond exporter's read trimmed
    /// them first and emitted <c>-3</c> anyway — a twin that states music the source does
    /// not. Its own comment claimed the two read "the SAME set". They now do, on the
    /// collector's rule, which makes that claim true. No book writes the quoted form
    /// (measured over the 80-book corpus and 219 fixtures: every written finger argument
    /// is a bare digit), so this converges an unexercised divergence rather than changing
    /// any book.
    /// </para>
    /// </remarks>
    public static int? Finger(MusicMarkSyntax mark)
        => Named(mark, "finger") && Sole(mark) is { Value: LysValue.Int { V: >= 0 and <= int.MaxValue } n }
            ? (int)n.V
            : null;

    /// <summary>
    /// The right-hand finger letter of <c>@pluck(p|i|m|a)</c> in lower case, or null.
    /// </summary>
    /// <remarks>LILYPOND-REF: the p-i-m-a fingering, printed below the note.</remarks>
    public static string? Pluck(MusicMarkSyntax mark)
        => Named(mark, "pluck") && Sole(mark) is { Text.Length: 1 } argument
           && char.ToLowerInvariant(argument.Text[0]) is 'p' or 'i' or 'm' or 'a'
            ? argument.Text.ToLowerInvariant()
            : null;

    /// <summary>
    /// The semitones of a guitar bend-up, <c>@bend(half|full|N)</c>, or null.
    /// </summary>
    /// <remarks>LILYPOND-REF: MusicXML &lt;bend&gt;&lt;bend-alter&gt; semantics.</remarks>
    public static int? Bend(MusicMarkSyntax mark)
    {
        if (!Named(mark, "bend") || Sole(mark) is not { } argument)
            return null;
        return argument.Text.ToLowerInvariant() switch
        {
            "half" => 1,
            "full" => 2,
            _ => argument.Value?.AsInt is { } n && n is > 0 and <= 12 ? n : null,
        };
    }

    /// <summary>
    /// The notehead style word of <c>@notehead(style)</c> in lower case, or null when
    /// the annotation is not one or names no style Lily# draws.
    /// </summary>
    /// <remarks>
    /// The WORD, not a style enum: its two consumers map it to different things (Lily#'s
    /// own <c>NoteheadStyle</c> and MusicXML's <c>&lt;notehead&gt;</c> vocabulary), and
    /// those mappings are genuinely two. What was three copies of the ACCEPTED SET is one.
    /// </remarks>
    public static string? Notehead(MusicMarkSyntax mark)
        => Named(mark, "notehead") && Sole(mark)?.Text.ToLowerInvariant() is
           ("x" or "cross" or "diamond" or "triangle" or "slash" or "xcircle") and var style
            ? style
            : null;

    private static bool Named(MusicMarkSyntax mark, string name)
        => string.Equals(mark.Name, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The single argument, or null when the annotation has none or several. All four
    /// families take exactly one — a second argument was never accepted by the string
    /// forms either, because the extra dotted part broke every one of their parses.
    /// </summary>
    private static MarkArgument? Sole(MusicMarkSyntax mark)
        => mark.Arguments is [var only] ? only : null;
}
