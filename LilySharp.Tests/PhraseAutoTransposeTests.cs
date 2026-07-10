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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A phrase is a movable motif: written once in the score's home key, it is
/// auto-transposed to whatever key is in effect where it is referenced (by the
/// nearest octave). Inline notes stay at their written pitch. Verified through
/// the collected note staff positions (treble: middle line B4 = 0, one diatonic
/// step per unit).
/// </summary>
[Trait("Category", "Unit")]
public sealed class PhraseAutoTransposeTests
{
    private static int[] Positions(string source) =>
        new MeasureCollector().Collect(SyntaxTree.Parse(source), "m").Voice.Measures
            .SelectMany(m => m.Items.OfType<NoteItem>())
            .Select(n => n.StaffPosition)
            .ToArray();

    // c d e c in C major, resolved on the treble staff: C4 D4 E4 C4.
    private static readonly int[] Home = { -6, -5, -4, -6 };

    [Fact]
    public void ReferenceInHomeKey_IsNotTransposed()
    {
        // Ambient key equals home (C) → exact no-op, the phrase plays as written.
        var pos = Positions("""
            key c major
            phrase Lick { c d e c }
            part m {
              section A { Lick }
            }
            form main { A }
            score main { staff m }
            """);
        Assert.Equal(Home, pos);
    }

    [Fact]
    public void ReferenceInModulatedKey_TransposesUpToAmbient()
    {
        // Section B modulates to G; the phrase written in C moves to g a b g by
        // the NEAREST octave — down a fourth (5 semitones) beats up a fifth (7) —
        // landing on G3 A3 B3 G3. Section A (home) stays put; the shift is scoped
        // to B. (Lick' would nudge it up an octave — see the octave-mark step.)
        var pos = Positions("""
            key c major
            phrase Lick { c d e c }
            part m {
              section A { Lick }
              section B { key g major Lick }
            }
            form main { A B }
            score main { staff m }
            """);
        Assert.Equal(new[] { -6, -5, -4, -6, /* G3 A3 B3 G3 */ -9, -8, -7, -9 }, pos);
    }

    [Fact]
    public void NearestOctave_ChoosesTheSmallerShift()
    {
        // Home C → ambient Bb is a whole tone DOWN (2 semitones), not a major
        // seventh up: C4 D4 E4 C4 becomes Bb3 C4 D4 Bb3.
        var pos = Positions("""
            key c major
            phrase Lick { c d e c }
            part m {
              section A { key bes major Lick }
            }
            form main { A }
            score main { staff m }
            """);
        Assert.Equal(new[] { -7, -6, -5, -7 }, pos);
    }

    [Fact]
    public void InlineNotesAfterReference_StayAtWrittenPitch()
    {
        // The phrase transposes; the inline `c` that follows it does not. In G,
        // Lick → G3 A3 B3 G3 (nearest octave). The written c then resolves relative
        // to the phrase's last WRITTEN note (C4) as C4 = -6 — untransposed (a shift
        // would land it on G3 = -9 instead).
        var pos = Positions("""
            key c major
            phrase Lick { c d e c }
            part m {
              section A { key g major Lick c }
            }
            form main { A }
            score main { staff m }
            """);
        Assert.Equal(new[] { -9, -8, -7, -9, /* inline c = C4, not transposed */ -6 }, pos);
    }
}
