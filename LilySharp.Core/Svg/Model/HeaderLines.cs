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

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// The two header lines of 2026-11-13 (owner's decision), beyond the four strings the model
/// has always carried (<see cref="MultiStaffScore.Title"/>, <c>Composer</c>, <c>Subtitle</c>,
/// <c>Poet</c>): the arranger — LilyPond's <c>\header arranger</c>, right-aligned on the line
/// under the composer — and the copyright line — LilyPond's <c>\header copyright</c>, centred
/// at the foot of the first page — each with where its string is written (0 = none).
/// </summary>
/// <remarks>
/// One value, compared by what it holds: it is a key of the incremental compiler's
/// (IncrementalCompiler's global key) and a field of the collector's MetadataState, which
/// compares and shifts it as a whole. A score with neither carries <see cref="None"/>, the
/// one instance, so the keystroke path allocates nothing for it.
/// </remarks>
/// <param name="Arranger">The arranger, or null.</param>
/// <param name="ArrangerPosition">Where the arranger's string is written in the file.</param>
/// <param name="Copyright">The copyright line, or null.</param>
/// <param name="CopyrightPosition">Where the copyright's string is written in the file.</param>
/// <param name="Fences">The header lines written as Markdown (<c>```md title</c> …; owner's
/// decision 2026-11-13), or null for none — the page draws each in place of its plain string.</param>
public sealed record HeaderLines(
    string? Arranger = null, int ArrangerPosition = 0,
    string? Copyright = null, int CopyrightPosition = 0,
    HeaderFences? Fences = null)
{
    /// <summary>A header with no arranger and no copyright line.</summary>
    public static HeaderLines None { get; } = new();

    /// <summary>The Markdown written for <paramref name="place"/> (a word of
    /// <c>FencedText.HeaderPlaceWords</c>), or null.</summary>
    public HeaderFence? FenceOf(string place) => Fences?.Of(place);

    /// <summary>This header with <paramref name="place"/>'s Markdown set to
    /// <paramref name="fence"/> (null takes it away).</summary>
    public HeaderLines WithFence(string place, HeaderFence? fence)
    {
        var fences = (Fences ?? HeaderFences.Empty).With(place, fence);
        var next = this with { Fences = fences.IsEmpty ? null : fences };
        return next == None ? None : next;
    }

    /// <summary>
    /// This header with every source position carried through <paramref name="shift"/> (the
    /// collector's window map after an edit), or null when one of them cannot be.
    /// </summary>
    public HeaderLines? Shifted(Func<int, int?> shift)
    {
        if (ReferenceEquals(this, None))
            return this;
        if (shift(ArrangerPosition) is not { } arranger || shift(CopyrightPosition) is not { } copyright)
            return null;
        HeaderFences? fences = null;
        if (Fences is { } written && (fences = written.Shifted(shift)) is null)
            return null;
        return this with { ArrangerPosition = arranger, CopyrightPosition = copyright, Fences = fences };
    }
}

/// <summary>One header line written as Markdown: the fence's body and where the fence begins.</summary>
/// <param name="Markdown">The text between the fences.</param>
/// <param name="Position">Where the fence's first backtick stands in the file.</param>
public readonly record struct HeaderFence(string Markdown, int Position);

/// <summary>
/// The header lines written as Markdown (owner's decision 2026-11-13): <c>```md title</c>,
/// <c>```md subtitle</c>, <c>```md composer</c>, <c>```md poet</c>, <c>```md arranger</c>,
/// <c>```md copyright</c> — at most one each. Compared by what it holds.
/// </summary>
public sealed record HeaderFences(
    HeaderFence? Title = null, HeaderFence? Subtitle = null, HeaderFence? Composer = null,
    HeaderFence? Poet = null, HeaderFence? Arranger = null, HeaderFence? Copyright = null)
{
    /// <summary>None written.</summary>
    public static HeaderFences Empty { get; } = new();

    /// <summary>Whether none is written.</summary>
    public bool IsEmpty => this == Empty;

    /// <summary>The fence for <paramref name="place"/>, or null.</summary>
    public HeaderFence? Of(string place) => place switch
    {
        "title" => Title,
        "subtitle" => Subtitle,
        "composer" => Composer,
        "poet" => Poet,
        "arranger" => Arranger,
        "copyright" => Copyright,
        _ => null,
    };

    /// <summary>These with <paramref name="place"/>'s set to <paramref name="fence"/>.</summary>
    public HeaderFences With(string place, HeaderFence? fence) => place switch
    {
        "title" => this with { Title = fence },
        "subtitle" => this with { Subtitle = fence },
        "composer" => this with { Composer = fence },
        "poet" => this with { Poet = fence },
        "arranger" => this with { Arranger = fence },
        "copyright" => this with { Copyright = fence },
        _ => this,
    };

    /// <summary>These with every fence's position carried through <paramref name="shift"/>,
    /// or null when one cannot be.</summary>
    public HeaderFences? Shifted(Func<int, int?> shift)
    {
        bool lost = false;
        HeaderFence? Move(HeaderFence? fence)
        {
            if (fence is not { } f)
                return null;
            if (shift(f.Position) is { } to)
                return f with { Position = to };
            lost = true;
            return null;
        }
        var moved = new HeaderFences(Move(Title), Move(Subtitle), Move(Composer), Move(Poet), Move(Arranger), Move(Copyright));
        return lost ? null : moved;
    }
}
