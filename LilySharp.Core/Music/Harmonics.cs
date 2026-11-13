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
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Music;

/// <summary>
/// What a harmonic sounds — the one home of the two tables (owner-approved 2026-11-13, grammar
/// evaluation 3). The page, the MIDI, the MusicXML and the LilyPond twin each resolve a note's
/// pitch for themselves, so each asks here what the harmonic adds to it; none keeps the
/// intervals.
/// <list type="bullet">
/// <item>A NATURAL harmonic at a fret, <c>e,4@harmonic(12)</c>: the note written is the open
/// string, and the harmonic sounds a partial of it (<see cref="NaturalInterval"/>).</item>
/// <item>An ARTIFICIAL harmonic, <c>&lt;c' f'@harmonic&gt;4</c>: a stopped note and a touched one
/// above it, sounding one pitch (<see cref="ArtificialSounding"/>).</item>
/// </list>
/// </summary>
public static class Harmonics
{
    /// <summary>The annotation's name: <c>@harmonic</c>.</summary>
    public const string Name = "harmonic";

    /// <summary>The frets a natural harmonic is written at, in the order a guitarist meets them:
    /// the octave, the two fifths, the two double octaves, the three thirds and the high fifth.</summary>
    public static IReadOnlyList<int> Frets { get; } = [12, 7, 19, 5, 24, 4, 9, 16, 3];

    /// <summary>
    /// What a natural harmonic at <paramref name="fret"/> adds to its open string, as the c→target
    /// interval <see cref="PitchTransposer.Transpose"/> takes, or null for a fret that is not one
    /// of <see cref="Frets"/>: 12 an octave; 7 and 19 an octave and a fifth; 5 and 24 two octaves;
    /// 4, 9 and 16 two octaves and a major third; 3 two octaves and a fifth.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/tablature.scm fret-partials — "12" → 1, "7" and "19" → 2,
    ///   "5" and "24" → 3, "4", "9" and "16" → 4, "3" → 5 (its "2.7", "2.3" and "2" are not
    ///   offered here: integers only, and the seventh partial is a quarter tone flat);
    /// LILYPOND-REF: scm/tablature.scm partial-pitch — partial 1 is (1 0 0), 2 is
    ///   (1 4 0), 3 is (2 0 0), 4 is (2 2 0), 5 is (2 4 0): octave, step, alteration, added to
    ///   the note's pitch by calc-harmonic-pitch.
    /// LILYPOND-REF: ly/music-functions-init.ly harmonicByFret — calc-harmonic-pitch
    ///   (fret->pitch fret) music.
    /// </remarks>
    public static (int Step, int Alter, int Octave)? NaturalInterval(int fret) => fret switch
    {
        12 => (0, 0, 1),
        7 or 19 => (4, 0, 1),
        5 or 24 => (0, 0, 2),
        4 or 9 or 16 => (2, 0, 2),
        3 => (4, 0, 2),
        _ => null,
    };

    /// <summary><see cref="NaturalInterval"/> in semitones (12, 19, 24, 28, 31), 0 for a fret
    /// that is not a harmonic's.</summary>
    public static int NaturalSemitones(int fret)
        => NaturalInterval(fret) is { } i ? PitchTransposer.IntervalSemitones(i.Step, i.Alter, i.Octave) : 0;

    /// <summary>
    /// The pitch an artificial harmonic sounds, or null when the two notes are not one of the
    /// three standard ones: the touched note a perfect fourth above the stopped one sounds two
    /// octaves above the stopped note, a perfect fifth above an octave and a fifth, a major
    /// third above two octaves and a major third. Any other interval is left to sound as written.
    /// </summary>
    /// <remarks>LILYSHARP-OWN: LilyPond's <c>\harmonic</c> changes the head and nothing else —
    /// its MIDI plays the two written notes. The touched node divides the stopped string in
    /// 4, 3 or 5, which is the partial that sounds.</remarks>
    public static int? ArtificialSounding(int stoppedMidi, int touchedMidi) => (touchedMidi - stoppedMidi) switch
    {
        5 => stoppedMidi + 24,
        7 => stoppedMidi + 19,
        4 => stoppedMidi + 28,
        _ => null,
    };

    /// <summary>Whether <paramref name="pitch"/> is the STOPPED note of an artificial harmonic,
    /// read off the text (an export that names the two notes, MusicXML's <c>base-pitch</c> and
    /// <c>touching-pitch</c>, asks before any pitch is resolved): an untouched member of a chord
    /// with a touched one that is written right before a touched member, or is the chord's only
    /// untouched member.</summary>
    public static bool IsStoppedMember(PitchSyntax pitch)
    {
        if (pitch.Parent is not ChordSyntax chord || IsTouchedMember(pitch))
            return false;
        bool anyTouched = false, previousIsThis = false, beforeTouched = false;
        int untouched = 0;
        foreach (var member in chord.Pitches)
        {
            bool touched = IsTouchedMember(member);
            anyTouched |= touched;
            if (!touched)
                untouched++;
            if (previousIsThis && touched)
                beforeTouched = true;
            previousIsThis = member.SourceStart == pitch.SourceStart;
        }
        return anyTouched && (beforeTouched || untouched == 1);
    }

    /// <summary>What <see cref="ArtificialSounds"/> writes for a member that sounds nothing of
    /// its own: the stopped note of an artificial harmonic.</summary>
    public const int Silent = int.MinValue;

    /// <summary>
    /// What a chord SOUNDS where it writes artificial harmonics: <paramref name="keys"/> — its
    /// members' written keys, in the members' order — rewritten in place. A touched member
    /// (<see cref="IsTouchedMember"/>) and its stopped note, the nearest member below it that is
    /// not itself touched, become the one pitch <see cref="ArtificialSounding"/> names: the
    /// touched member takes it and the stopped one goes <see cref="Silent"/>. A pair at any
    /// other interval, and every other member, keeps the key written.
    /// </summary>
    public static void ArtificialSounds(IReadOnlyList<PitchSyntax> members, int[] keys)
    {
        for (int t = 0; t < members.Count; t++)
        {
            if (!IsTouchedMember(members[t]))
                continue;
            int stopped = -1;
            for (int s = 0; s < members.Count; s++)
                if (s != t && keys[s] != Silent && !IsTouchedMember(members[s]) && keys[s] < keys[t]
                    && (stopped < 0 || keys[s] > keys[stopped]))
                    stopped = s;
            if (stopped >= 0 && ArtificialSounding(keys[stopped], keys[t]) is { } sounding)
            {
                keys[t] = sounding;
                keys[stopped] = Silent;
            }
        }
    }

    /// <summary>
    /// The string a natural harmonic is played on: the one <paramref name="written"/> names when
    /// it is open at <paramref name="openMidi"/>, else the highest-numbered (lowest) string open
    /// at that pitch — 0 when the pitch is no open string of <paramref name="tuning"/> (lowest
    /// string first, as <see cref="Tablature.Tunings.GetTuning(Tablature.TuningType)"/> gives it).
    /// </summary>
    public static int OpenString(int openMidi, int[] tuning, int? written = null)
    {
        int n = tuning.Length;
        if (written is int w && w >= 1 && w <= n && tuning[n - w] == openMidi)
            return w;
        for (int k = 0; k < n; k++)
            if (tuning[k] == openMidi)
                return n - k;
        return 0;
    }

    /// <summary>The fret of the natural harmonic <paramref name="note"/> plays — the
    /// <c>@harmonic(N)</c> written on it, one of <see cref="Frets"/>, in the music of a fretted
    /// instrument's part (<see cref="FrettedParts.Holds"/>) — or 0: none, or one the validator
    /// refuses (<see cref="AnnotationValues.HarmonicFret"/>; LYS4070, LYS4071), which is drawn
    /// and played as the note written. The one question the page, the MIDI, the MusicXML and the
    /// twin ask, so the four cannot answer differently.</summary>
    public static int FretOn(NoteSyntax note)
    {
        foreach (var a in note.Articulations)
            if (a is MusicMarkSyntax mark && AnnotationValues.HarmonicFret(mark) is { } fret)
                return FrettedParts.Holds(note) ? fret : 0;
        return 0;
    }

    /// <summary>Whether <paramref name="pitch"/>, a member of a <c>&lt;…&gt;</c> chord, carries a
    /// bare <c>@harmonic</c>: the touched note of an artificial harmonic, drawn with the open
    /// diamond head (and no flageolet circle).</summary>
    public static bool IsTouchedMember(PitchSyntax pitch)
    {
        if (pitch.Parent is not ChordSyntax)
            return false;
        foreach (var a in pitch.Articulations)
            if (a is ArticulationSyntax { Type: ArticulationType.Flageolet, Name: Name })
                return true;
        return false;
    }
}
