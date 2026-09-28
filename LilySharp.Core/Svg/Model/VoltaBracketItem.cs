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

using System.Collections.Immutable;

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Represents a volta bracket (first/second ending bracket).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/volta-bracket.cc:1-170 Volta_bracket_interface
/// LILYPOND-REF: scm/define-grobs.scm:4292-4317 VoltaBracket grob
///
/// Volta brackets show which measures to play on each repeat:
/// - [1. ] = first ending (play on first time through)
/// - [2. ] = second ending (play on second time through)
/// - [1, 3. ] = play on first and third time
/// - [1-3. ] = play on first through third time
/// </remarks>
public sealed record VoltaBracketItem(
    // Starting measure index (inclusive).
    int StartMeasureIndex,

    // Ending measure index (inclusive) — the last bar the BRACKET covers: an ending's
    // `voltaBracket N` is already applied here (Semantics.VoltaBracketLength.LastBar).
    int EndMeasureIndex,

    // Volta number text (e.g., "1.", "2.", "1, 3.", "1-3.").
    string VoltaText,

    // Whether the bracket's right end hooks down: the ending's `]` (not `-]`), and not cut
    // short by its length setting.
    bool IsClosed,

    // Source position for click-to-source mapping.
    int SourcePosition,

    // `voltaBracket line`: only the piece on the system the bracket starts in is drawn — a
    // bracket the system break cuts ends straight (VoltaBracketEngraver.Calculate).
    bool FirstSystemOnly = false,

    // The ENDING's last bar when the bracket stops before it (`voltaBracket N`), else -1 —
    // for the readers that ask where the ending is, not where its ink is (MeasureCollector
    // LaterEndingPredecessors pairs a repeat's endings by adjacency).
    int CutEndingLastMeasureIndex = -1
)
{
    /// <summary>The ending's last bar — the bracket's own, unless the bracket was cut short.</summary>
    public int EndingLastMeasureIndex => CutEndingLastMeasureIndex >= 0 ? CutEndingLastMeasureIndex : EndMeasureIndex;

    // Identity, not value equality: see ModelIdentity.
    public bool Equals(VoltaBracketItem? other) => ReferenceEquals(this, other);

    /// <inheritdoc/>
    public override int GetHashCode() => ModelIdentity.HashOf(this);
}
