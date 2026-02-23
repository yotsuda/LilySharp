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

using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics.BoundTree;

/// <summary>
/// A bound note with resolved pitch and duration.
/// </summary>
public sealed record BoundNote : BoundMusic
{
    /// <summary>
    /// Creates a bound note.
    /// </summary>
    public BoundNote(
        SyntaxNode? syntax,
        Pitch pitch,
        Fraction duration,
        int dots,
        bool hasTieStart,
        bool hasTieEnd,
        bool hasSlurStart = false,
        bool hasSlurEnd = false,
        int tremoloBeams = 0)
    {
        _syntax = syntax;
        Pitch = pitch;
        BaseDuration = duration;
        Dots = dots;
        HasTieStart = hasTieStart;
        HasTieEnd = hasTieEnd;
        HasSlurStart = hasSlurStart;
        HasSlurEnd = hasSlurEnd;
        TremoloBeams = tremoloBeams;
    }

    private readonly SyntaxNode? _syntax;

    /// <inheritdoc/>
    public override SyntaxNode? Syntax => _syntax;

    /// <summary>The absolute pitch (relative mode resolved).</summary>
    public Pitch Pitch { get; }

    /// <summary>The base duration without dots.</summary>
    public Fraction BaseDuration { get; }

    /// <summary>Number of dots.</summary>
    public int Dots { get; }

    /// <summary>Whether this note starts a tie.</summary>
    public bool HasTieStart { get; }

    /// <summary>Whether this note ends a tie.</summary>
    public bool HasTieEnd { get; }

    /// <summary>Whether this note starts a slur.</summary>
    public bool HasSlurStart { get; }

    /// <summary>Whether this note ends a slur.</summary>
    public bool HasSlurEnd { get; }

    /// <summary>Number of tremolo beams (0 = no tremolo, 1-3 = tremolo strokes).</summary>
    public int TremoloBeams { get; }
    /// <summary>The total duration including dots.</summary>
    public Fraction Duration => Dots > 0 ? BaseDuration.Dotted(Dots) : BaseDuration;

    /// <summary>The staff position for rendering.</summary>
    public int StaffPosition => Pitch.StaffPosition;

    /// <summary>The accidental string, or null if natural.</summary>
    public string? Accidental => Pitch.AccidentalString;
}

/// <summary>
/// A bound rest with resolved duration.
/// </summary>
public sealed record BoundRest : BoundMusic
{
    /// <summary>
    /// Creates a bound rest.
    /// </summary>
    public BoundRest(SyntaxNode? syntax, Fraction duration, int dots)
    {
        _syntax = syntax;
        BaseDuration = duration;
        Dots = dots;
    }

    private readonly SyntaxNode? _syntax;

    /// <inheritdoc/>
    public override SyntaxNode? Syntax => _syntax;

    /// <summary>The base duration without dots.</summary>
    public Fraction BaseDuration { get; }

    /// <summary>Number of dots.</summary>
    public int Dots { get; }

    /// <summary>The total duration including dots.</summary>
    public Fraction Duration => Dots > 0 ? BaseDuration.Dotted(Dots) : BaseDuration;
}

/// <summary>
/// Information about a single note within a bound chord.
/// </summary>
public readonly record struct BoundChordNote(
    Pitch Pitch,
    bool HasTieStart,
    bool HasTieEnd)
{
    /// <summary>The staff position for rendering.</summary>
    public int StaffPosition => Pitch.StaffPosition;

    /// <summary>The accidental string, or null if natural.</summary>
    public string? Accidental => Pitch.AccidentalString;
}

/// <summary>
/// A bound chord with resolved pitches and duration.
/// </summary>
public sealed record BoundChord : BoundMusic
{
    /// <summary>
    /// Creates a bound chord.
    /// </summary>
    public BoundChord(
        SyntaxNode? syntax,
        IReadOnlyList<BoundChordNote> notes,
        Fraction duration,
        int dots,
        int tremoloBeams = 0)
    {
        _syntax = syntax;
        Notes = notes;
        BaseDuration = duration;
        Dots = dots;
        TremoloBeams = tremoloBeams;
    }

    private readonly SyntaxNode? _syntax;

    /// <inheritdoc/>
    public override SyntaxNode? Syntax => _syntax;

    /// <summary>The notes in this chord.</summary>
    public IReadOnlyList<BoundChordNote> Notes { get; }

    /// <summary>The base duration without dots.</summary>
    public Fraction BaseDuration { get; }

    /// <summary>Number of dots.</summary>
    public int Dots { get; }

    /// <summary>Number of tremolo beams (0 = no tremolo, 1-3 = tremolo strokes).</summary>
    public int TremoloBeams { get; }

    /// <summary>The total duration including dots.</summary>
    public Fraction Duration => Dots > 0 ? BaseDuration.Dotted(Dots) : BaseDuration;
}
