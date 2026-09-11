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

namespace LilySharp.Core.Semantics;

/// <summary>
/// What a <c>layout { }</c> block asks for — the score-wide DISPLAY switches: closed
/// vocabularies that say how a class of symbol is drawn or arranged, with no unit and no
/// grob scope. The third block beside <c>fonts</c> (the faces) and <c>paper</c> (the
/// page's dimensions), and it takes the same two tiers: the unnamed top-level block is
/// the file's default, a named block is a per-score declaration a score references.
/// </summary>
/// <remarks>
/// <para>
/// The line against <c>paper</c> (user decision 2026-09-11): a quantity with a unit — a
/// length, a justification flag — is the page's and lives in <c>paper</c>; a switch that
/// picks one of a few drawings is the layout's and lives here. <c>indent</c>,
/// <c>raggedRight</c> and <c>spacingIncrement</c> stay in <c>paper</c> on that rule
/// (LilyPond itself accepts them in <c>\paper</c>). NOT an <c>override</c>: an override
/// reads a <c>once</c> / section scope that a whole-score switch would silently ignore.
/// </para>
/// <para>
/// Compared by value (a record of a bool and a small struct), so the incremental compiler
/// sheds its caches on a real change and not on a trivia edit — <c>LayoutOptions</c>'
/// contract.
/// </para>
/// </remarks>
public sealed record LayoutPlan(
    // `marks beside` — a boxed section label and the bar's tempo on one line (the chart's);
    // false is `marks stacked`, LilyPond's arrangement and the default (MarkArrangement).
    bool MarksBeside,
    // `barNumbers lines|none|every N` — where the bar numbers stand (BarNumberPolicy).
    BarNumberPolicy BarNumbers)
{
    /// <summary>What a book with no <c>layout { }</c> gets: LilyPond's picture on every
    /// switch — labels stacked over the tempo, a number at the start of every line but the
    /// first.</summary>
    public static readonly LayoutPlan Default = new(MarksBeside: false, BarNumberPolicy.Lines);
}

/// <summary>Which bars carry a printed number.</summary>
public enum BarNumberMode
{
    /// <summary>LilyPond's default: the first bar of every line after the first
    /// (<c>first-bar-number-invisible-and-no-parenthesized-bar-numbers</c> with the grob's
    /// <c>begin-of-line-visible</c>).</summary>
    Lines,
    /// <summary>No bar numbers at all (LilyPond's <c>\remove Bar_number_engraver</c>).</summary>
    None,
    /// <summary>Every bar whose number is a multiple of <see cref="BarNumberPolicy.Period"/>,
    /// wherever it stands in the line (LilyPond's <c>every-nth-bar-number-visible</c> with
    /// <c>break-visibility = end-of-line-invisible</c>).</summary>
    Every,
}

/// <summary>
/// The <c>barNumbers</c> key's value: a mode, and for <see cref="BarNumberMode.Every"/> the
/// period. LilyPond's vocabulary, spelled as the chart writer says it.
/// </summary>
/// <param name="Mode">Which bars are numbered.</param>
/// <param name="Period">For <c>every N</c>, the N (≥ 1); 0 otherwise.</param>
public readonly record struct BarNumberPolicy(BarNumberMode Mode, int Period = 0)
{
    /// <summary>The key, as written in the block.</summary>
    public const string Key = "barNumbers";

    /// <summary>The three words the key takes, the default first.</summary>
    public const string LinesWord = "lines";
    /// <inheritdoc cref="LinesWord"/>
    public const string NoneWord = "none";
    /// <inheritdoc cref="LinesWord"/>
    public const string EveryWord = "every";

    /// <summary>The words, in the order the completion offers them (the default first).
    /// <c>every</c> takes an integer after it.</summary>
    public static readonly IReadOnlyList<string> Words = [LinesWord, NoneWord, EveryWord];

    /// <summary>The default: a number at the start of every line after the first.</summary>
    public static readonly BarNumberPolicy Lines = new(BarNumberMode.Lines);

    /// <summary>No numbers.</summary>
    public static readonly BarNumberPolicy None = new(BarNumberMode.None);

    /// <summary>Every <paramref name="period"/>th bar.</summary>
    public static BarNumberPolicy Every(int period) => new(BarNumberMode.Every, period);
}
