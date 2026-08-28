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
    /// <summary>The anchorless offset-free reset (a parallel span's fresh frame).</summary>
    public static readonly RelativeResetMarker Instance = new(0, null);

    /// <summary>Net octave shift applied to the reset frame (' = +1, , = -1).</summary>
    public int OctaveOffset { get; }

    /// <summary>The phrase's anchor step (see <see cref="Music.PhraseAnchor"/>):
    /// the written step the paired <see cref="PhraseEndMarker"/> hands back to
    /// the relative chain — the chord rule, so a reference propagates its first
    /// note's bare letter, never its interior. <see cref="Music.PhraseAnchor.Tonic"/>
    /// marks a degree-opened body (resolved to the AMBIENT tonic at the
    /// reference); null = pitchless body, nothing to hand off.</summary>
    public int? AnchorStep { get; }

    /// <summary>Reuses <see cref="Instance"/> for the (common) bare anchorless case.</summary>
    public static RelativeResetMarker For(int octaveOffset, int? anchorStep = null)
        => octaveOffset == 0 && anchorStep == null
            ? Instance
            : new RelativeResetMarker(octaveOffset, anchorStep);

    private RelativeResetMarker(int octaveOffset, int? anchorStep)
        : base(MarkerGreen.Shared, parent: null, position: 0)
    {
        OctaveOffset = octaveOffset;
        AnchorStep = anchorStep;
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
