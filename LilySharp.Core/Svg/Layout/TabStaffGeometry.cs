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
/// A sloped tab beam that follows the fret-digit contour (LilyPond-like) instead of
/// sitting horizontal above the highest digit — otherwise a chord's high string pins
/// the beam up and the lower melody digits grow very long stems. The line is the
/// first→last stem-head slope, shifted so the SHORTEST stem equals the target length
/// and every stem clears it. Shared by the renderer's beam pass and the articulation
/// engraver (a forced-above script must clear the same beam).
/// </summary>
internal static class TabBeamMath
{
    /// <param name="xs">Each member's stem x (must be ascending, physical device X).</param>
    /// <param name="headYs">Each member's stem-head Y (digit string line minus the gap).</param>
    /// <param name="stemUp">Beam above the digits (up-stems) vs below.</param>
    /// <param name="tabBeamStem">The shortest stem length (on the outermost digit).</param>
    public static (double Slope, double InterceptY, double FirstX) Line(
        double[] xs, double[] headYs, bool stemUp, double tabBeamStem)
    {
        int n = xs.Length;
        // LilyPond's beam slope: a LEAST-SQUARES fit through the stem-heads (so one
        // outlier digit — e.g. a chord's high string — doesn't pin the slope), then
        // damped by 0.6*tanh(slope)/damping so it stays gentle. damping = 1 (the
        // Beam grob default); concaveness is 0 for the ordinary contours here.
        // LILYPOND-REF: lily/beam-quanting.cc least_squares_positions + slope_damping;
        // scm/define-grobs.scm Beam.damping = 1, details.round-to-zero-slope = 0.02.
        double slope = 0.0;
        if (n > 1)
        {
            double mx = 0, my = 0;
            for (int i = 0; i < n; i++) { mx += xs[i]; my += headYs[i]; }
            mx /= n; my /= n;
            double num = 0, den = 0;
            for (int i = 0; i < n; i++)
            {
                double dx = xs[i] - mx;
                num += dx * (headYs[i] - my);
                den += dx * dx;
            }
            double ls = den != 0.0 ? num / den : 0.0;
            const double damping = 1.0;
            slope = 0.6 * System.Math.Tanh(ls) / damping;
            if (System.Math.Abs(slope) < 0.02) slope = 0.0; // round-to-zero-slope
        }
        // Shift the line out so the SHORTEST stem is tabBeamStem and every stem clears.
        double b = stemUp ? double.PositiveInfinity : double.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            double proj = headYs[i] - slope * (xs[i] - xs[0]);
            b = stemUp ? System.Math.Min(b, proj) : System.Math.Max(b, proj);
        }
        b += stemUp ? -tabBeamStem : tabBeamStem;
        return (slope, b, xs[0]);
    }

    public static double At((double Slope, double InterceptY, double FirstX) line, double x)
        => line.Slope * (x - line.FirstX) + line.InterceptY;
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
