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

using LilySharp.Core.Syntax;
using LilySharp.Core.Syntax.InternalSyntax;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Zero-width sentinel injected into the flattened music-node stream at each
/// phrase-reference expansion: the collector resets its relative pitch and
/// duration state when it encounters one, so every phrase body evaluates in
/// the default frame regardless of call site. A reference's trailing octave
/// marks (<c>Chorus'</c> / <c>Chorus,</c>) travel on <see cref="OctaveOffset"/>,
/// shifting the fresh frame up or down before the phrase body runs.
/// </summary>
internal sealed class RelativeResetMarker : SyntaxNode
{
    /// <summary>The offset-free reset used by the overwhelming common case.</summary>
    public static readonly RelativeResetMarker Instance = new(0, 0);

    /// <summary>Net octave shift applied to the reset frame (' = +1, , = -1).</summary>
    public int OctaveOffset { get; }

    /// <summary>Diatonic scale-step shift from the reference's interval argument
    /// (<c>Melody'(3)</c> = +2 steps), applied to the phrase body's pitches after
    /// relative resolution (see <see cref="OctaveContext.DiatonicShiftSteps"/>).</summary>
    public int DiatonicSteps { get; }

    /// <summary>Reuses <see cref="Instance"/> for the (common) unmarked case.</summary>
    public static RelativeResetMarker For(int octaveOffset, int diatonicSteps = 0)
        => octaveOffset == 0 && diatonicSteps == 0
            ? Instance
            : new RelativeResetMarker(octaveOffset, diatonicSteps);

    private RelativeResetMarker(int octaveOffset, int diatonicSteps)
        : base(MarkerGreen.Shared, parent: null, position: 0)
    {
        OctaveOffset = octaveOffset;
        DiatonicSteps = diatonicSteps;
    }

    private sealed class MarkerGreen : GreenNode
    {
        public static readonly MarkerGreen Shared = new();
        private MarkerGreen() : base(SyntaxKind.None, fullWidth: 0) { }
    }
}

/// <summary>
/// Zero-width sentinel closing a phrase-reference expansion: paired with the
/// leading <see cref="RelativeResetMarker"/>, it lets the collector restore the
/// pitch transpose it armed for the movable phrase, so inline notes that follow
/// the reference stay at their written (absolute) pitch.
/// </summary>
internal sealed class PhraseEndMarker : SyntaxNode
{
    public static readonly PhraseEndMarker Instance = new();

    private PhraseEndMarker()
        : base(MarkerGreen.Shared, parent: null, position: 0)
    {
    }

    private sealed class MarkerGreen : GreenNode
    {
        public static readonly MarkerGreen Shared = new();
        private MarkerGreen() : base(SyntaxKind.None, fullWidth: 0) { }
    }
}
