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
using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// The concert-pitch convention, read from its two spellings and turned into the one
/// thing every consumer wants: a <c>transpose</c>-shaped interval to compose onto a part.
/// </summary>
/// <remarks>
/// <para>
/// Two conventions exist for a transposing instrument. WRITTEN pitch (the default, and what
/// every book on disk means): the note letters are what the player reads, the part prints
/// exactly that, and only playback moves — <c>InstrumentDefaults.GetTransposition</c>
/// answers the sounding shift. CONCERT pitch (<c>pitch concert</c> at the top level): the
/// letters are what SOUNDS, and a transposing part is printed the way its player reads it —
/// an E♭ alto saxophone's <c>c'</c> prints as <c>a'</c>, in A major when the piece is in C.
/// </para>
/// <para>
/// A part header takes the same words to say it for THAT part alone (own &gt; file default,
/// as <c>transpose</c> and <c>octave</c> do), so a saxophone copied from a transposed
/// part-sheet and a piano copied from a concert-pitch score can share one book.
/// The second spelling is on a score header — <c>score full pitch concert { … }</c> — and
/// says how THAT score prints: at sounding pitch, the conductor's score, whichever
/// convention the file is written in. The two compose to one page shift per part:
/// </para>
/// <list type="table">
/// <item><term>file written, score written</term><description>nothing moves (the default,
/// and every existing book)</description></item>
/// <item><term>file written, score concert</term><description>+T: a written part shown
/// at what it sounds</description></item>
/// <item><term>file concert, score written</term><description>−T: the part printed for
/// its player</description></item>
/// <item><term>file concert, score concert</term><description>nothing moves</description></item>
/// </list>
/// <para>
/// where T is the part's chromatic transposition (<see cref="PartHeaderDefaults.ConcertShiftSemitones"/>:
/// an octave-only transposer keeps its notation in a concert score, so its T is 0).
/// </para>
/// <para>
/// ⚠️ THE SHIFT RIDES THE <c>transpose</c> CHANNEL AND NOTHING ELSE. <see cref="PartTranspose.Read(SyntaxNode, string)"/>
/// composes the file-level half onto the part's transpose, so the page, the MIDI, the
/// MusicXML and the LilyPond twin all move the SAME way a hand-written <c>transpose a</c>
/// would move them — pitches, key signature and accidentals together, which is the
/// machinery <c>transpose</c> already has (OctaveContext.TransposeKeySharps). The
/// score-level half is composed where the score's own <c>transpose</c> is composed. There
/// is no second spelling of "shift a part" anywhere, and no second reader of the sounding
/// shift: playback still adds <see cref="PartHeaderDefaults.SoundingShiftSemitones"/> to the
/// PRINTED pitch, so a concert-pitch file's alto saxophone prints <c>a'</c> and sounds
/// <c>c'</c> — the two shifts cancel exactly once, which <c>ConcertPitchTests</c> holds with
/// a non-transposing part as the control.
/// </para>
/// </remarks>
public static class ConcertPitch
{
    /// <summary>The property name, in both positions.</summary>
    public const string Property = "pitch";

    /// <summary>The value that says the letters are sounding pitches.</summary>
    public const string Concert = "concert";

    /// <summary>The value that says the letters are what the player reads — the default.</summary>
    public const string Written = "written";

    /// <summary>The two words <c>pitch</c> takes, beside the parser that refuses a third.</summary>
    public static readonly IReadOnlyList<string> Modes = [Written, Concert];

    /// <summary>
    /// Reads a <c>pitch</c> property: true for <c>concert</c>, false for <c>written</c>, null
    /// when the node is not a pitch property or carries a value outside the two (the parser
    /// has reported that one already).
    /// </summary>
    public static bool? ReadProperty(PropertyAssignmentSyntax prop)
    {
        if (!string.Equals(prop.NameToken.Text, Property, StringComparison.OrdinalIgnoreCase))
            return null;
        return prop.ValueText switch
        {
            Concert => true,
            Written => false,
            _ => null,
        };
    }

    /// <summary>
    /// Whether the file's music is written at concert pitch: the first top-level
    /// <c>pitch</c> directive says so. A directive on a score header is that score's own
    /// (<see cref="ScoreIsConcert"/>) and is not the file's.
    /// </summary>
    /// <remarks>
    /// Green finder rather than a red walk, for the reason <see cref="PartTranspose.ReadScoreDefault"/>
    /// gives: this is asked per part per keystroke.
    /// </remarks>
    public static bool FileIsConcert(SyntaxNode root)
    {
        foreach (var prop in root.GreenSites(
                     static g => (g.Kind == SyntaxKind.PropertyAssignment, Descend: true)))
            if (prop is PropertyAssignmentSyntax pa
                && !pa.IsInside<PartDeclarationSyntax>() && !pa.IsInside<RenderDeclarationSyntax>()
                && ReadProperty(pa) is { } mode)
                return mode;
        return false;
    }

    /// <summary>Whether <paramref name="render"/> asks to be printed at concert pitch.</summary>
    public static bool ScoreIsConcert(RenderDeclarationSyntax? render)
        => render?.PitchMode is { } p && ReadProperty(p) == true;

    /// <summary>
    /// The part header's own <c>pitch</c>, or null when it writes none: a part that copies a
    /// transposed part-sheet beside one that copies a concert-pitch score can say so, each
    /// for itself. Own &gt; file default, the rule <c>transpose</c> and <c>octave</c> follow.
    /// </summary>
    public static bool? PartMode(PartDeclarationSyntax? part)
    {
        if (part == null) return null;
        foreach (var prop in part.Properties)
            if (ReadProperty(prop) is { } mode)
                return mode;
        return null;
    }

    /// <summary>
    /// The written-side shift a concert-pitch part gives itself — sounding to written, −T —
    /// or null when the part is written-pitch or does not transpose chromatically. The
    /// part's own <c>pitch</c> decides; <paramref name="fileIsConcert"/> is the default for a
    /// part that writes none.
    /// </summary>
    public static (int step, int alt, int oct)? InputShift(bool fileIsConcert, PartDeclarationSyntax? part)
        => (PartMode(part) ?? fileIsConcert)
            ? Interval(-PartHeaderDefaults.Read(part).ConcertShiftSemitones)
            : null;

    /// <summary>
    /// The shift a score printed AT concert pitch gives <paramref name="part"/> — written to
    /// sounding, +T — or null when the score is not, or the part does not transpose
    /// chromatically. Composed onto the part's transpose where the score's own
    /// <c>transpose</c> is, so with <see cref="InputShift"/> also armed the two cancel.
    /// </summary>
    public static (int step, int alt, int oct)? OutputShift(bool scoreIsConcert, PartDeclarationSyntax? part)
        => scoreIsConcert ? Interval(PartHeaderDefaults.Read(part).ConcertShiftSemitones) : null;

    /// <summary>The declared part named <paramref name="partName"/>, or null: part
    /// declarations are top-level only (Parser.ParseTopLevelItem).</summary>
    public static PartDeclarationSyntax? FindPart(SyntaxNode root, string partName)
        => root.ChildNodes().OfType<PartDeclarationSyntax>()
            .FirstOrDefault(p => p.Name.Text == partName);

    /// <summary>Null for no shift, so a part that does not transpose stays "untransposed"
    /// to every reader that tests the interval for null.</summary>
    private static (int step, int alt, int oct)? Interval(int semitones)
        => semitones == 0 ? null : PitchTransposer.IntervalFromSemitones(semitones);
}
