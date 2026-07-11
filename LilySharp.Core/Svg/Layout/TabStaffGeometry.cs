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
