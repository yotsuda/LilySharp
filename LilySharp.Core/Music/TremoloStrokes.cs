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

namespace LilySharp.Core.Music;

/// <summary>
/// The strokes a stem tremolo plays — <c>&lt;fis b dis' fis'&gt;2.:8</c> six eighths,
/// <c>c2:16</c> eight sixteenths — the one count the sound (Midi.MidiExporter.Restrike) and
/// the portato over the strokes (ArticulationItem.TremoloStrokes, one dot a stroke) read
/// (owner's decisions 2026-11-20, Calamo request 33).
/// </summary>
public static class TremoloStrokes
{
    /// <summary>The most strokes counted; a longer roll is not one a dot can stand for.</summary>
    public const int Most = 1024;

    /// <summary>The stem tremolo written on a note, chord, <c>q</c>, bare duration or drum
    /// note, or null.</summary>
    public static SyntaxTokenNode? TremoloOf(SyntaxNode node) => node switch
    {
        NoteSyntax n => n.Tremolo,
        ChordSyntax c => c.Tremolo,
        ChordRepetitionSyntax q => q.Tremolo,
        BareDurationSyntax b => b.Tremolo,
        DrumNoteSyntax d => d.Tremolo,
        _ => null,
    };

    /// <summary>The written value over the tremolo's, or 0: no tremolo, or one that does not
    /// divide the note into two strokes or more (<c>c8:8</c>, <c>c4.:8</c> is three).</summary>
    public static int Of(SyntaxTokenNode? tremolo, Fraction written)
    {
        if (tremolo is null || tremolo.Text.Length < 2
            || !int.TryParse(tremolo.Text.AsSpan(1), out int value) || value <= 0 || written.Numerator <= 0)
            return 0;
        long over = written.Numerator * value, under = written.Denominator;
        if (over % under != 0)
            return 0;
        long strokes = over / under;
        return strokes is >= 2 and <= Most ? (int)strokes : 0;
    }
}
