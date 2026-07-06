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
/// The running "Timing / Clef / Key" context that crosses barlines — the state
/// LilyPond threads left-to-right through a voice. This is the F3 design's
/// <c>entry_context</c> / <c>exit_context</c> payload (F3 design notes
/// §2 Layer 2): each measure's <c>entry</c> is the previous measure's <c>exit</c>,
/// and a measure's semantics depend only on its green subtree plus this context.
/// </summary>
/// <remarks>
/// A <c>readonly record struct</c> on purpose: value equality is exactly the
/// early-cutoff comparison the query DAG needs — when a measure's
/// <c>exit_context</c> is unchanged after an edit, the running-state cascade to
/// later measures stops (the F3 incremental design notes §3-4).
///
/// SCOPE: this is the Timing/Clef/Key backbone LilyPond's three context
/// engravers carry. The initial values come from the score header / part clef
/// (<c>Score.KeySignature</c> / <c>Score.TimeSignature</c> / <c>Score.Clef</c>
/// are all the bar-1 values — the collector preserves <c>_initialClef</c> /
/// <c>_initialKeySharps</c> before music processing), and mid-piece changes fold
/// in from the zero-duration Key/Clef/Time change items.
///
/// Deliberately deferred to later stages, where they are consumed or can be
/// derived faithfully from the right source:
/// <list type="bullet">
/// <item>ottava (octave-shift span);</item>
/// <item>the relative-octave reference pitch — post-collection notes are already
/// resolved to staff positions, so this only becomes meaningful once S3 (F3b)
/// normalizes relative→absolute and the chain is driven from the walk/green;</item>
/// <item>pending ties and open cross-measure spanners (slur/hairpin), which need
/// span tracking rather than a scalar carry.</item>
/// </list>
/// Accidental state is intentionally absent: it resets at every barline
/// (MeasureCollector clears it on MeasureCompleted), so only the key crosses.
/// </remarks>
public readonly record struct MeasureContext(
    KeySignature Key,
    TimeSignature Time,
    ClefType Clef)
{
    /// <summary>Returns a compact key/time/clef description for debugging.</summary>
    public override string ToString() => $"key={Key.Sharps} time={Time} clef={Clef}";
}
