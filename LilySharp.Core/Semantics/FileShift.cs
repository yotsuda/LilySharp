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

using LilySharp.Core.Music;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// The file's <c>shift N</c> (owner's request 2026-10-17): the whole piece N semitones up, or
/// down for N &lt; 0, the way a karaoke machine or a band chart moves a song "half a step up" —
/// every pitched part with its key signatures, and every chord row, on the page, in the MIDI,
/// in the MusicXML and in the LilyPond twin. It composes OUTSIDE everything a part says for
/// itself (the <c>fileShift</c> argument of <see cref="PartTranspose"/>'s <c>Read</c>: the
/// part's own <c>transpose</c>, the file default, a transposing instrument's shift), where a
/// top-level <c>transpose</c> is only the default of the parts that write none. Drum notes are
/// not pitches and stay where they are.
/// </summary>
/// <remarks>
/// <para>
/// THE SPELLING. A number leaves the spelling open (+1 from C major is D♭ or C♯), so the shift
/// is first turned into ONE c→target interval (<see cref="Interval"/>) and the music is then
/// transposed by that interval the way LilyPond's <c>\transpose</c> does — name-based, so every
/// note, accidental and key signature agrees. The target key is the file's first <c>key</c>
/// moved N semitones and brought into five flats … six sharps
/// (<see cref="ChordStructure.PressedKeySharps"/>, the rule a capo's pressed name already
/// follows, sharp at the tritone), and a chord row's names go down that same pressed-name road,
/// so they spell as the notes do. C major +1 is D♭ major (c→des), −1 is B major (c→b,), +6 is
/// F♯ major; G major −1 is F♯ major; E major +1 is F major.
/// </para>
/// <para>
/// A WRITTEN chord shape is a shape of the written chord: under a shift the row's diagrams draw
/// each chord's usual shape on its new root instead (ChordNameCollector.DiagramOf) — the
/// "refret" a band takes when it moves a song. A capo is the writer's own choice
/// (<c>chordDiagrams … capo N</c>; owner's decision 2026-10-17: no automatic capo).
/// <c>@chord</c> annotations on notes are not moved (they are not moved for a transposing
/// staff either).
/// </para>
/// <para>LILYSHARP-OWN: LilyPond has <c>\transpose FROM TO</c> and no number of semitones.</para>
/// </remarks>
public static class FileShift
{
    /// <summary>The most a shift moves either way: eleven semitones (an octave is <c>octave N</c>'s).</summary>
    public const int Max = 11;

    /// <summary>Whether <paramref name="prop"/> is a <c>shift</c> property (case-sensitive, as every name).</summary>
    public static bool IsShift(PropertyAssignmentSyntax prop) => prop.NameToken.Text == "shift";

    /// <summary>
    /// Whether <paramref name="prop"/> is THE site the file's shift is read from — a top-level
    /// <c>shift</c> outside every part header and score block — and, when it is, its semitones
    /// (0 for a value the parser refused: the site still ends the search). One predicate for
    /// the whole-tree reader and for the collector's definitions walk, as
    /// <see cref="PartTranspose.TryReadScoreDefault"/> is for <c>transpose</c>.
    /// </summary>
    public static bool TryRead(PropertyAssignmentSyntax prop, out int semitones)
    {
        if (IsShift(prop) && !prop.IsInside<PartDeclarationSyntax>() && !prop.IsInside<RenderDeclarationSyntax>())
        {
            semitones = Parse(prop) ?? 0;
            return true;
        }
        semitones = 0;
        return false;
    }

    /// <summary>The written number — an optional <c>+</c> or <c>-</c> and an integer — or null.</summary>
    public static int? Parse(PropertyAssignmentSyntax prop)
    {
        int sign = 1;
        bool signed = false;
        int? value = null;
        foreach (var v in prop.Values)
        {
            if (v is not SyntaxTokenNode tok || tok.Kind == SyntaxKind.Colon)
                continue;
            if (value == null && !signed && tok.Kind is SyntaxKind.Plus or SyntaxKind.Minus)
            {
                signed = true;
                sign = tok.Kind == SyntaxKind.Minus ? -1 : 1;
                continue;
            }
            if (value == null && int.TryParse(tok.Text, out int n))
            {
                value = n;
                continue;
            }
            return null;
        }
        return value is int x ? sign * x : null;
    }

    /// <summary>The file's shift in semitones, 0 for none: the first top-level site, in the
    /// pre-order the collector's definitions walk meets it.</summary>
    public static int ReadSemitones(SyntaxNode root)
    {
        foreach (var prop in root.GreenSites(
                     static g => (g.Kind == SyntaxKind.PropertyAssignment, Descend: true)))
            if (prop is PropertyAssignmentSyntax pa && TryRead(pa, out int n))
                return n;
        return 0;
    }

    /// <summary>The file's shift as the c→target interval every reader composes, or null for none.</summary>
    public static (int step, int alt, int oct)? Read(SyntaxNode root)
        => Interval(ReadSemitones(root), FirstKeySharps(root));

    /// <summary>
    /// The c→target interval that moves music by <paramref name="semitones"/>, spelled from the
    /// key of <paramref name="keySharps"/> (the remarks): the target key is
    /// <see cref="KeySharpsAfter"/>, the interval the one whose key-signature shift reaches it
    /// (<see cref="PitchTransposer.IntervalFromFifths"/>), its octave marks making the semitone
    /// count exact. Null for 0.
    /// </summary>
    public static (int step, int alt, int oct)? Interval(int semitones, int keySharps)
    {
        if (semitones == 0)
            return null;
        int fifths = KeySharpsAfter(keySharps, semitones) - keySharps;
        var (step, alt) = PitchTransposer.IntervalFromFifths(fifths);
        // Seven fifths are a semitone (mod 12), so the interval's size agrees with the shift
        // mod 12, and the octave marks make up the difference exactly.
        int within = RelativeOctave.StepSemitoneOf(step) + alt;
        return (step, alt, (semitones - within) / 12);
    }

    /// <summary>The key signature after the shift: <paramref name="keySharps"/> moved
    /// <paramref name="semitones"/>, brought into five flats … six sharps
    /// (<see cref="ChordStructure.PressedKeySharps"/>); unmoved for 0.</summary>
    public static int KeySharpsAfter(int keySharps, int semitones)
        => semitones == 0 ? keySharps : ChordStructure.PressedKeySharps(keySharps, -semitones);

    /// <summary>The sharps of the file's first <c>key</c> in document order (a part header's
    /// counts); 0 — C major — with none, or a <c>key custom</c>.</summary>
    public static int FirstKeySharps(SyntaxNode root)
    {
        foreach (var node in root.GreenSites(
                     static g => (g.Kind == SyntaxKind.KeySignature, Descend: true)))
            if (node is KeySignatureSyntax key && !key.IsCustom && key.GetChild(2) is PitchSyntax tonic
                && key.GetChild(3) is SyntaxTokenNode { Text.Length: > 0 } mode
                && KeySpelling.SharpsFor(tonic.PitchName, mode.Text.ToLowerInvariant()) is int sharps)
                return sharps;
        return 0;
    }

    /// <summary>A chord row's drop with the shift folded in — semitones DOWN, 0..11, the number
    /// <see cref="ChordStructure.Pressed"/> takes: a shift of N up is a drop of 12 − N.</summary>
    public static int RowDrop(int drop, int semitones) => (((drop - semitones) % 12) + 12) % 12;
}
