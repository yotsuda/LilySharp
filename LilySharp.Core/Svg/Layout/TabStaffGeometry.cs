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
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;

using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Sizes shared by every tab-staff site. Previously the fret font size lived as a
/// private const in SharedRenderer AND a hard-coded literal in ElementCoordinator
/// (a silent desync if either was tuned); this is now the single source.
/// </summary>
internal static class TabConstants
{
    /// <summary>Font size of a tab fret number, in staff spaces.</summary>
    public const double FretFontSize = 2.6;

    /// <summary>Grace fret digits relative to the main fret size (just slightly smaller).</summary>
    public const double GraceFretScale = 0.8;

    /// <summary>Height (staff spaces) of a fret digit's occluding box — the digit's
    /// visual extent, centred on its string line. Shared by the renderer (the
    /// white background) and the articulation engraver (clearing a digit that
    /// protrudes past the outer staff line).</summary>
    public const double FretDigitHeight = 0.6875 * FretFontSize;
}

/// <summary>
/// A straight tab beam line in device Y, produced by <see cref="TabBeamQuant"/>.
/// <see cref="At"/> evaluates it at any x. Shared by the renderer's beam pass and
/// the articulation engraver (a forced-above script must clear the same beam).
/// </summary>
internal static class TabBeamMath
{
    public static double At((double Slope, double InterceptY, double FirstX) line, double x)
        => line.Slope * (x - line.FirstX) + line.InterceptY;
}

/// <summary>
/// Quants a TAB beam through LilyPond's ported beam quanter
/// (<see cref="BeamScoringProblem"/>) by feeding the notes' STRING lines as the
/// stem positions — so tab beams get the same least-squares → damping →
/// concaveness → quanting treatment as notation beams (a chord/outlier flattens,
/// a monotonic run slopes), instead of an ad-hoc slope. Returns the beam line in
/// DEVICE Y (evaluate with <see cref="TabBeamMath.At"/>).
/// </summary>
internal static class TabBeamQuant
{
    public static (double Slope, double InterceptY, double FirstX) Compute(
        BeamGroup group, double[] memberStemXs, TabStaffGeometry geom)
    {
        int n = group.Members.Length;
        // A string line is 1.5 staff-spaces = 3 half-space positions; string 1 (the
        // top line) is position 0, lower strings negative. The quanter reads
        // itemXPositions by ItemIndex, so build that sparse array from the stems.
        var stemPos = new int[n];
        int maxIdx = 0;
        for (int i = 0; i < n; i++)
        {
            int str = geom.StemHeadString(group.Members[i].Item, group.StemUp);
            stemPos[i] = -(str - 1) * 3;
            if (group.Members[i].ItemIndex > maxIdx) maxIdx = group.Members[i].ItemIndex;
        }
        var xById = new double[maxIdx + 1];
        for (int i = 0; i < n; i++)
            xById[group.Members[i].ItemIndex] = memberStemXs[i];

        var (leftPos, rightPos) =
            new BeamScoringProblem(group, xById, stemPositions: stemPos).Solve();

        // Quanter Y is in half-space positions (position 0 = the top string line);
        // map back to device Y at the outer stems.
        double leftX = memberStemXs[0], rightX = memberStemXs[n - 1];
        double leftY = geom.StaffY - leftPos * 0.5;
        double rightY = geom.StaffY - rightPos * 0.5;
        double slope = rightX > leftX ? (rightY - leftY) / (rightX - leftX) : 0.0;
        return (slope, leftY, leftX);
    }
}

/// <summary>
/// The geometry of one tab staff: its tuning-derived string spacing / octave shift,
/// and the string→Y and midi→fret conversions used by the renderer, the tie/grace
/// layout, and the articulation engraver. Consolidates the chain
/// (<c>OctaveShift + GetTuning + CalculateFret + TabStringSpace(GetStringCount)</c>)
/// and the <c>Y = staffY + (stringNum-1)*stringSpace</c> formula that were inlined
/// at half a dozen sites.
/// </summary>
internal readonly struct TabStaffGeometry
{
    private readonly int[] _tuning;
    private readonly int _octaveShift;

    /// <summary>Device-Y of the top tab line (string 1).</summary>
    public double StaffY { get; }
    /// <summary>Vertical distance between adjacent string lines, in staff spaces.</summary>
    public double StringSpace { get; }
    /// <summary>Number of strings for this tuning.</summary>
    public int StringCount { get; }

    public TabStaffGeometry(TuningType tuning, double staffY, ClefType clef = ClefType.Treble)
    {
        StaffY = staffY;
        _tuning = Tunings.GetTuning(tuning);
        _octaveShift = Tunings.OctaveShift(tuning, clef);
        StringCount = Tunings.GetStringCount(tuning);
        StringSpace = EngravingDefaults.TabStringSpace(StringCount);
    }

    /// <summary>Device-Y of a string's line (string 1 = top line).</summary>
    public double StringY(int stringNum) => StaffY + (stringNum - 1) * StringSpace;

    /// <summary>
    /// Resolves the (string, fret) for a written MIDI pitch, honouring a preferred
    /// string (0 = automatic). The tuning's 8vb octave shift is applied here.
    /// </summary>
    public (int stringNum, int fret) Fret(int writtenMidi, int? preferredString = null)
        => Tunings.CalculateFret(writtenMidi + _octaveShift, _tuning, preferredString ?? 0);

    /// <summary>Device-Y of the fret digit row for a written MIDI pitch.</summary>
    public double DigitY(int writtenMidi, int? preferredString = null)
        => StringY(Fret(writtenMidi, preferredString).stringNum);

    /// <summary>
    /// Device-Y of ONE chord note's fret digit, resolved through the chord's
    /// EXCLUSIVE string allocation — the same <c>CalculateChordFrets</c> the
    /// digits are drawn with — rather than a per-note auto-fret. A per-note
    /// <see cref="DigitY"/> ignores the other notes and can hand several chord
    /// notes the same low string, which is why a chord's per-string ties used to
    /// pile up at the bottom of the staff instead of hugging their own digits.
    /// Matches the note by STAFF POSITION — a chord tie's synthesized start note
    /// carries no MIDI, only its staff position — and returns that string's line.
    /// </summary>
    public double ChordNoteDigitY(ChordItem chord, int staffPosition)
    {
        int shift = _octaveShift;
        var alloc = Tunings.CalculateChordFrets(
            chord.Notes.Select(n => (n.Midi + shift, n.StringNumber)).ToList(), _tuning);
        for (int i = 0; i < chord.Notes.Length; i++)
            if (chord.Notes[i].StaffPosition == staffPosition && alloc[i].stringNum >= 1)
                return StringY(alloc[i].stringNum);
        return StaffY;
    }

    /// <summary>
    /// The string a stem meets on this item: the TOP digit (smallest string number)
    /// for an up-stem, the BOTTOM for a down-stem. Chords use the same exclusive
    /// allocation the drawn chord does. Mirrors <c>TabStemHeadY</c> in the renderer.
    /// </summary>
    public int StemHeadString(MusicItem item, bool stemUp)
    {
        switch (item)
        {
            case NoteItem n:
                return Fret(n.Midi, n.StringNumber).stringNum;
            case ChordItem c when c.Notes.Length > 0:
                int shift = _octaveShift;
                var alloc = Tunings.CalculateChordFrets(
                    c.Notes.Select(x => (x.Midi + shift, x.StringNumber)).ToList(), _tuning);
                int head = alloc[0].stringNum;
                foreach (var a in alloc)
                    head = stemUp ? System.Math.Min(head, a.stringNum) : System.Math.Max(head, a.stringNum);
                return head;
            default:
                return 1;
        }
    }
}
