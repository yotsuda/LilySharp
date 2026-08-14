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

namespace LilySharp.Core.Syntax;

/// <summary>
/// What a <c>tempo</c> declaration's value run MEANS, read once.
/// </summary>
/// <remarks>
/// <para>
/// The run — <c>tempo ["marking"] [unit[.]] [=] [bpm] [swing|shuffle [n]]</c> — used to be
/// re-walked by a separate state machine per question. There were SIX of them:
/// <c>Marking</c>, <c>SwingSubdivision</c>, <c>Bpm</c>, <c>BeatUnit</c> and
/// <c>BeatDots</c> on the syntax node, plus a sixth, independent walk in
/// <c>MeasureCollector.CollectTempo</c> that found the beat unit by stepping back from
/// the <c>=</c> over the dots and matching a regex. Six spellings of one reading is the
/// shape §5.2.1② names, and the two beat-unit spellings did disagree — see
/// <see cref="BeatUnit"/>.
/// </para>
/// <para>
/// ⚠️ The rules below are NOT a tidied-up version of what those machines did. They are
/// what they did, transcribed, because each one is load-bearing for some written form in
/// the corpus and the point of this type is to have one copy of them, not a better set.
/// Any change to them is a change to what a <c>.lys</c> file means, and belongs in its
/// own commit with its own reason.
/// </para>
/// </remarks>
public sealed record TempoValue(
    string? Marking,
    int? BeatUnit,
    int BeatDots,
    int? Bpm,
    int SwingSubdivision)
{
    /// <summary>A run with nothing readable in it (<c>tempo</c> alone).</summary>
    public static readonly TempoValue Empty = new(null, null, 0, null, 0);

    /// <summary>
    /// Reads the value run left to right, in ONE pass.
    /// </summary>
    /// <remarks>
    /// The five readings and their priorities, each of which some written form depends on:
    /// <list type="bullet">
    /// <item><b>Marking</b> — a bare word, but only in the FIRST position
    /// (<c>tempo Comodo 4 = 84</c>), so a trailing feel word or the music after the
    /// declaration is never swallowed; otherwise the first quoted string anywhere. A
    /// leading <c>swing</c>/<c>shuffle</c> is a feel word, not a marking.</item>
    /// <item><b>Bpm</b> — the LAST integer, stopping at a feel word, so the <c>16</c> of
    /// <c>swing 16</c> is not mistaken for the tempo. Last, not first, because
    /// <c>4 = 120</c> puts the beat unit first.</item>
    /// <item><b>BeatUnit</b> — the last integer BEFORE the <c>=</c>, or a quarter when
    /// there is an <c>=</c> with no integer before it. NULL when there is no <c>=</c> at
    /// all: <c>tempo 140</c> is a bpm, and reading its 140 as a beat unit printed a
    /// 140th-note metronome glyph once already.</item>
    /// <item><b>BeatDots</b> — the dots after that unit, reset by each new integer.</item>
    /// <item><b>SwingSubdivision</b> — the first integer after a feel word, 8 for a bare
    /// feel word, 0 for none.</item>
    /// </list>
    /// A <see cref="SyntaxKind.DecimalLiteral"/> is not a tempo value and is skipped here;
    /// the parser has already reported it (LYS0022) at the point it consumed it.
    /// </remarks>
    public static TempoValue FromTokens(IEnumerable<SyntaxTokenNode> tokens)
    {
        string? marking = null;
        int? bpm = null;
        int? beatCandidate = null;
        int? beatUnit = null;
        int? subdivision = null;
        int dots = 0, beatDots = 0;
        bool sawUnit = false, sawEquals = false, sawFeelWord = false, first = true;

        foreach (var token in tokens)
        {
            switch (token.Kind)
            {
                case SyntaxKind.Identifier when IsFeelWord(token.Text):
                    sawFeelWord = true;
                    break;

                case SyntaxKind.Identifier when first:
                    marking ??= token.Text;
                    break;

                case SyntaxKind.StringLiteral:
                    marking ??= token.Text.Trim('"');
                    break;

                case SyntaxKind.IntegerLiteral:
                    // The unit tracking runs even for a number too big to parse: the
                    // dots after it still belong to it, and dropping that would move
                    // them onto an earlier unit.
                    if (!sawEquals)
                    {
                        sawUnit = true;
                        dots = 0;
                    }
                    if (!int.TryParse(token.Text, out int n))
                        break;
                    if (sawFeelWord)
                        subdivision ??= n;   // the FIRST number after the feel word
                    else
                        bpm = n;             // the LAST number before it
                    if (!sawEquals)
                        beatCandidate = n;
                    break;

                case SyntaxKind.Dot when sawUnit && !sawEquals:
                    dots++;
                    break;

                case SyntaxKind.Equals when !sawEquals:
                    sawEquals = true;
                    beatUnit = beatCandidate ?? 4;
                    beatDots = dots;
                    break;
            }
            first = false;
        }

        // A bare feel word swings the eighths. `swing 0` is NOT that case — it asked
        // for a subdivision and named zero, and the reading this replaces answered 0.
        int swing = subdivision ?? (sawFeelWord ? 8 : 0);

        return new TempoValue(marking, beatUnit, beatDots, bpm, swing);
    }

    /// <summary>
    /// The words that ask for a swing feel. Deliberately NOT reserved words — they stay
    /// usable as part names and markings everywhere else, which is why every reading of
    /// the run has to know them rather than the lexer.
    /// </summary>
    public static bool IsFeelWord(string text) => text is "swing" or "shuffle";
}
