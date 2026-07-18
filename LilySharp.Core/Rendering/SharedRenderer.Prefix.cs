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

using System.Collections.Immutable;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;

namespace LilySharp.Core.Rendering;

/// <summary>
/// Prefix engraving for <see cref="SharedRenderer"/>: the clef, time signature,
/// and key signature drawn at the head of a staff/system (plus their resolution
/// helpers). Split out of the main SharedRenderer file as a partial class; no
/// behavior change.
/// </summary>
internal static partial class SharedRenderer
{
    // ---------- Clef ----------

    /// <summary>
    /// Resolves the active clef at the start of a system by walking previous
    /// measures' ClefChangeItems. Mirrors SvgRenderer.GetActiveClefStringForSystem.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/clef-engraver.cc — clef at system start reflects last clef change.
    /// </remarks>
    private static ClefType ResolveClef(Staff staff, SystemLayout system, MultiStaffScore score)
    {
        if (system.Measures.IsDefaultOrEmpty || system.Measures.Length == 0)
            return staff.Clef;

        var voice = staff.PrimaryVoice;
        int firstMeasureIndex = system.Measures[0].MeasureIndex;
        var activeClef = staff.Clef;

        // Apply clef changes accumulated in earlier measures.
        for (int m = 0; m < firstMeasureIndex && m < voice.Measures.Length; m++)
        {
            foreach (var item in voice.Measures[m].Items)
            {
                if (item is ClefChangeItem cc)
                    activeClef = cc.NewClef;
            }
        }

        // Leading ClefChangeItems in this system's first measure also surface as
        // system-start clefs (LP groups them with the prefix).
        if (firstMeasureIndex < voice.Measures.Length)
        {
            foreach (var item in voice.Measures[firstMeasureIndex].Items)
            {
                if (item is ClefChangeItem cc)
                    activeClef = cc.NewClef;
                else if (item.Duration > Fraction.Zero)
                    break;
            }
        }

        return activeClef;
    }

    /// <summary>
    /// Resolves the active key signature at the start of a system by walking
    /// previous measures' KeySignatureChangeItems. Mirrors ResolveClef so the
    /// key signature repeated at each system head reflects any mid-piece change,
    /// not the initial key.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/key-engraver.cc — the key signature is reprinted at
    /// every line start, showing the meter in force at that point.
    /// </remarks>
    private static KeySignature ResolveKeySignature(Staff staff, SystemLayout system, MultiStaffScore score)
    {
        // A transposed part in a multi-staff score carries its own key; concert
        // staves fall back to the score key.
        var initialKey = staff.PerStaffKeySignature ?? score.KeySignature;

        if (system.Measures.IsDefaultOrEmpty || system.Measures.Length == 0)
            return initialKey;

        var voice = staff.PrimaryVoice;
        int firstMeasureIndex = system.Measures[0].MeasureIndex;
        var activeKey = initialKey;

        // Apply key changes accumulated in measures BEFORE this system. A change
        // that lands inside this system is drawn in place as a measure item, so
        // it is not folded into the system-start prefix here.
        for (int m = 0; m < firstMeasureIndex && m < voice.Measures.Length; m++)
        {
            foreach (var item in voice.Measures[m].Items)
            {
                if (item is KeySignatureChangeItem kc)
                    activeKey = kc.NewKey;
            }
        }

        return activeKey;
    }

    /// <summary>
    /// Returns the time-signature change that OPENS the first measure of a
    /// (non-first) system, or null. Such a change lands exactly at the line
    /// break and is drawn in the system-start prefix (clef, key, THEN time),
    /// like LilyPond — not as a measure item hanging left of the first note.
    /// </summary>
    private static TimeSignatureChangeItem? GetSystemStartTimeChange(Staff staff, SystemLayout system)
    {
        if (system.SystemIndex == 0 || system.Measures.IsDefaultOrEmpty || system.Measures.Length == 0)
            return null;

        var voice = staff.PrimaryVoice;
        int firstMeasureIndex = system.Measures[0].MeasureIndex;
        if (firstMeasureIndex >= voice.Measures.Length)
            return null;

        foreach (var item in voice.Measures[firstMeasureIndex].Items)
        {
            if (item is TimeSignatureChangeItem tc)
                return tc;
            if (item.Duration > Fraction.Zero)
                break; // a note/rest before any change → not a measure-opening meter change
        }
        return null;
    }

    /// <summary>
    /// Returns the key-signature change that OPENS the first measure of a
    /// (non-first) system, or null. Such a change lands exactly at the line
    /// break and prints as the NEW key in the system prefix; the cancellation
    /// belongs to the previous line, not to bar one of the new line.
    /// LILYPOND-REF: lily/break-align-engraver.cc — KeySignature is
    /// break-aligned at every line start.
    /// </summary>
    private static KeySignatureChangeItem? GetSystemStartKeyChange(Staff staff, SystemLayout system)
    {
        if (system.SystemIndex == 0 || system.Measures.IsDefaultOrEmpty || system.Measures.Length == 0)
            return null;

        var voice = staff.PrimaryVoice;
        int firstMeasureIndex = system.Measures[0].MeasureIndex;
        if (firstMeasureIndex >= voice.Measures.Length)
            return null;

        foreach (var item in voice.Measures[firstMeasureIndex].Items)
        {
            if (item is KeySignatureChangeItem kc)
                return kc;
            if (item.Duration > Fraction.Zero)
                break; // a note/rest before any change → not measure-opening
        }
        return null;
    }

    /// <summary>
    /// Returns the key change that opens the measure AFTER this system's last
    /// one (i.e. the first measure of the next line), or null — the trigger
    /// for the end-of-line courtesy cancellation + signature.
    /// </summary>
    private static KeySignatureChangeItem? GetSystemEndKeyChange(Staff staff, SystemLayout system)
    {
        if (system.Measures.IsDefaultOrEmpty || system.Measures.Length == 0)
            return null;
        var voice = staff.PrimaryVoice;
        int nextMeasureIndex = system.Measures[^1].MeasureIndex + 1;
        if (nextMeasureIndex >= voice.Measures.Length)
            return null;
        foreach (var item in voice.Measures[nextMeasureIndex].Items)
        {
            if (item is KeySignatureChangeItem kc)
                return kc;
            if (item.Duration > Fraction.Zero)
                break;
        }
        return null;
    }

    /// <summary>True when this key change is the one folded into its system's
    /// start prefix (see GetSystemStartKeyChange) — the per-measure pass must
    /// not draw it again.</summary>
    private static bool IsSystemStartKeyChange(
        Voice voice, SystemLayout system, int measureIndex, KeySignatureChangeItem kc)
    {
        if (system.SystemIndex == 0 || system.Measures.IsDefaultOrEmpty || system.Measures.Length == 0)
            return false;
        if (measureIndex != system.Measures[0].MeasureIndex || measureIndex >= voice.Measures.Length)
            return false;
        foreach (var item in voice.Measures[measureIndex].Items)
        {
            if (ReferenceEquals(item, kc))
                return true;
            if (item.Duration > Fraction.Zero)
                return false;
        }
        return false;
    }

    /// <summary>True when a clef change is a LEADING change (before the first sounding
    /// item) in a system's first measure. <see cref="ResolveClef"/> folds such a change
    /// into the system-start clef, so drawing it again as a mid-measure change would
    /// double-print it. Unlike the key-signature analog this also applies to the first
    /// system, because a moment-0 clef IS the opening clef there.</summary>
    private static bool IsSystemStartClefChange(
        Voice voice, SystemLayout system, int measureIndex, ClefChangeItem cc)
    {
        if (system.Measures.IsDefaultOrEmpty || system.Measures.Length == 0)
            return false;
        if (measureIndex != system.Measures[0].MeasureIndex || measureIndex >= voice.Measures.Length)
            return false;
        foreach (var item in voice.Measures[measureIndex].Items)
        {
            if (ReferenceEquals(item, cc))
                return true;
            if (item.Duration > Fraction.Zero)
                return false;
        }
        return false;
    }

    private static double DrawClef(ClefType clef, double x, double staffY, IDrawingContext gc)
    {
        char glyph = clef switch
        {
            ClefType.Bass or ClefType.Bass8Below => EmmentalerGlyphs.FClef,
            ClefType.Alto or ClefType.Tenor or ClefType.Soprano
                or ClefType.MezzoSoprano or ClefType.Baritone => EmmentalerGlyphs.CClef,
            ClefType.Percussion => EmmentalerGlyphs.PercussionClef,
            _ => EmmentalerGlyphs.GClef,
        };
        // Y baseline matches LP positioning: the clef glyph anchors on the line
        // it names (G / F / C); percussion centres on the middle line.
        double clefY = clef switch
        {
            ClefType.Bass or ClefType.Bass8Below => staffY - 1,
            ClefType.Alto or ClefType.Percussion => staffY - 2,
            ClefType.Tenor => staffY - 1,
            ClefType.Soprano => staffY - 4,       // C4 on the bottom line
            ClefType.MezzoSoprano => staffY - 3,  // C4 on line 2
            ClefType.Baritone => staffY - 0,      // C4 on the top line
            _ => staffY - 3,
        };
        gc.DrawGlyph(glyph, x + 0.3, clefY, FontSize);
        if (clef is ClefType.Treble8Below or ClefType.Bass8Below)
            DrawClefModifier8(x + 0.3, staffY, change: false, gc);
        else if (clef == ClefType.Treble8Above)
            DrawClefModifier8(x + 0.3, staffY, change: false, gc, above: true);
        return x + 0.3 + 3.0;  // approximate clef width + padding
    }

    /// <summary>
    /// Draws the octavation modifier digit "8" beneath a <c>treble_8</c> clef.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:944-975 (ClefModifier grob) +
    /// scm/output-lib.scm:3972-3989 (clef-modifier::print). LilyPond draws the
    /// modifier as italic text centred on the clef (self-alignment CENTER, with a
    /// small leftward nudge from clef-alignments <c>(G . (-0.2 . 0.1))</c>) and
    /// placed below the staff (direction DOWN, staff-padding 0.7). Lily# has no
    /// glyph-metric measurement, so the horizontal centre and vertical drop are
    /// constants calibrated to LilyPond 2.24 output (staff-space pixel probe of a
    /// treble_8 render): the digit is ~2 ss tall, its top ~0.3 ss below the bottom
    /// staff line, centred ~0.1 ss left of the clef's vertical axis. The mid-music
    /// _change clef uses a smaller glyph, so the digit and its offset shrink to
    /// match (LP applies a font-size dampening for change clefs).
    /// </remarks>
    private static void DrawClefModifier8(double clefGlyphX, double staffY, bool change, IDrawingContext gc, bool above = false)
    {
        double scale = change ? 0.85 : 1.0;
        double centerX = clefGlyphX + 1.1 * scale; // under the clef's descender (slightly left of the stem)
        // Below: clears the clef's lower curl. Above (treble^8): clears the
        // G clef's upper hook symmetrically.
        double centerY = above ? staffY + 3.2 : staffY - 5.6;
        double size = FontSize * 0.80 * scale;     // digit ~2 ss tall, matching LP
        gc.DrawText("8", centerX, centerY, size, "serif",
            FontStyle.Italic, TextAnchor.Middle, Color.Black, VerticalAnchor.Middle);
    }

    // ---------- Time signature ----------

    private static double DrawTimeSignature(TimeSignature ts, double x, double staffY, IDrawingContext gc)
    {
        // Senza misura: unmeasured music prints NO signature.
        if (ts.SenzaMisura)
            return x;
        if (ts.Beats == 4 && ts.BeatType == 4)
        {
            gc.DrawGlyph(EmmentalerGlyphs.TimeSigCommon, x, staffY - 2, FontSize);
            return x + 2.0;
        }
        if (ts.Beats == 2 && ts.BeatType == 2)
        {
            gc.DrawGlyph(EmmentalerGlyphs.TimeSigCutCommon, x, staffY - 2, FontSize);
            return x + 2.0;
        }
        // Stack numerator over denominator, each centered on the staff like
        // LilyPond: numerator centered at staff position +2 (device staffY+1),
        // denominator at -2 (device staffY+3), so the pair is symmetric about
        // the middle line. Unlike the common/cut-common glyphs (vertically
        // centred on their origin), the feta number glyphs are anchored at their
        // BASELINE (= the digit's bottom) and stand ~2 staff-spaces tall, so the
        // DrawGlyph y must be lowered by half the digit height to bring the
        // digit's CENTER onto the target line.
        // LILYPOND-REF: mf/feta-numbers.mf — time-signature numbers sit on the baseline.
        const double digitHalfHeight = 1.0; // feta number glyphs are ~2 ss tall
        // Additive meters print the numerator AS WRITTEN ("3+2" over 8), the
        // rows centered on each other. LILYPOND-REF: \compoundMeter numerator.
        var num = ts.BeatsText ?? ts.Beats.ToString();
        var den = ts.BeatType.ToString();
        const double digitW = 1.4, plusW = 1.1;
        double NumWidth(string s)
        {
            double w = 0;
            foreach (var ch in s) w += ch == '+' ? plusW : digitW;
            return w;
        }
        double numWidth = NumWidth(num), denWidth = NumWidth(den);
        double total = Math.Max(numWidth, denWidth);
        double nx = x + (total - numWidth) / 2;
        foreach (var ch in num)
        {
            if (ch == '+')
            {
                gc.DrawText("+", nx + plusW / 2, staffY - 1 - digitHalfHeight + 0.55,
                    2.4, "serif", FontStyle.Bold, TextAnchor.Middle, Color.Black);
                nx += plusW;
            }
            else
            {
                gc.DrawGlyph(EmmentalerGlyphs.GetTimeSigDigit(ch - '0'),
                    nx, staffY - 1 - digitHalfHeight, FontSize);
                nx += digitW;
            }
        }
        double dnx = x + (total - denWidth) / 2;
        foreach (var ch in den)
        {
            gc.DrawGlyph(EmmentalerGlyphs.GetTimeSigDigit(ch - '0'),
                dnx, staffY - 3 - digitHalfHeight, FontSize);
            dnx += digitW;
        }
        return x + total + 0.4;
    }

    // ---------- Key signature ----------

    // LilyPond key-signature placement tables (indexed by (c0-position mod 7)).
    // LILYPOND-REF: scm/output-lib.scm key-signature-interface::alteration-position;
    // scm/define-grobs.scm sharp-positions / flat-positions.
    private static readonly int[] KeySigSharpPositions = [4, 5, 4, 2, 3, 2, 3];
    private static readonly int[] KeySigFlatPositions = [2, 3, 4, 2, 1, 2, 1];
    // Order of accidentals: sharps F C G D A E B; flats B E A D G C F.
    private static readonly int[] KeySigSharpSteps = [3, 0, 4, 1, 5, 2, 6];
    private static readonly int[] KeySigFlatSteps = [6, 2, 5, 1, 4, 0, 3];

    private static double DrawKeySignature(
        KeySignature key, ClefType clef, double x, double staffY, IDrawingContext gc)
    {
        // Non-traditional signature: draw the written (step, alter) pairs in
        // print order, each on the position the standard tables would give
        // that step for its sign. LILYPOND-REF: keyAlterations; MusicXML
        // non-traditional <key-step>/<key-alter> pairs.
        if (key.Custom is { } custom)
        {
            foreach (var (step, alter) in KeySignature.DecodeCustom(custom))
            {
                string kind = alter switch
                {
                    2 => "doubleSharp",
                    1 => "sharp",
                    -1 => "flat",
                    -2 => "doubleFlat",
                    _ => "natural",
                };
                int staffPosition = KeySigStaffPositionForStep(clef, alter >= 0, step);
                double y = (staffY - StaffHeight / 2) + staffPosition / 2.0;
                gc.DrawGlyph(EmmentalerGlyphs.AccidentalGlyph(kind), x, y, FontSize);
                x += GlyphMetrics.GetKeySignatureAccidentalWidth(alter >= 0);
            }
            return x + 0.4;
        }

        if (key.Sharps == 0) return x;

        bool isSharps = key.Sharps > 0;
        char glyph = isSharps ? EmmentalerGlyphs.AccidentalSharp : EmmentalerGlyphs.AccidentalFlat;
        int[] positions = isSharps ? KeySigSharpPositions : KeySigFlatPositions;
        int[] steps = isSharps ? KeySigSharpSteps : KeySigFlatSteps;

        int n = Math.Min(Math.Abs(key.Sharps), 7);

        double accidentalWidth = GlyphMetrics.GetKeySignatureAccidentalWidth(isSharps);
        for (int i = 0; i < n; i++)
        {
            int staffPosition = KeySigStaffPosition(clef, isSharps, i);
            double y = (staffY - StaffHeight / 2) + staffPosition / 2.0;
            gc.DrawGlyph(glyph, x, y, FontSize);
            x += accidentalWidth;
        }
        return x + 0.4;
    }

    /// <summary>
    /// Kerning between two adjacent cancellation naturals. The naturals are
    /// laid out left→right; the gap depends on whether the LEFT glyph's right
    /// edge and the RIGHT glyph's left edge overlap vertically: overlap → 0.3,
    /// corners just touch → 0.15, clear → 0.
    /// LILYPOND-REF: lily/key-signature-interface.cc — the left glyph contributes
    /// its ht_right [2p−6, 2p+3]; the right (already-placed) glyph contributes its
    /// last_ht_left = ht_right shifted +3 = [2p−3, 2p+6].
    /// </summary>
    private static double NaturalKernPadding(int prevPos, int curPos)
    {
        double lo1 = 2 * prevPos - 6, hi1 = 2 * prevPos + 3; // left glyph, right edge (ht_right)
        double lo2 = 2 * curPos - 3, hi2 = 2 * curPos + 6;   // right glyph, left edge (last_ht_left)
        double lo = Math.Max(lo1, lo2), hi = Math.Min(hi1, hi2);
        if (lo > hi) return 0;
        return hi > lo ? 0.3 : 0.15;
    }

    /// <summary>
    /// Staff position of the i-th key-signature accidental for a clef.
    /// LILYPOND-REF: scm/music-functions.scm key-signature-interface —
    /// staffPosition = hi − modulo(hi − (c0 + step), 7).
    /// </summary>
    /// <summary>Key-signature staff position for an ARBITRARY step (custom
    /// signatures) — same octave-choice tables, indexed by step instead of the
    /// standard order. LILYPOND-REF: key-signature-interface alteration-position.</summary>
    /// <summary>The (step, alter) pairs of a STANDARD key signature in print
    /// order (sharps F C G D A E B / flats B E A D G C F).</summary>
    private static List<(int Step, int Alter)> StandardKeySteps(int sharps)
    {
        var list = new List<(int, int)>();
        int n = Math.Min(Math.Abs(sharps), 7);
        int[] steps = sharps > 0 ? KeySigSharpSteps : KeySigFlatSteps;
        for (int i = 0; i < n; i++)
            list.Add((steps[i], Math.Sign(sharps)));
        return list;
    }
    private static int KeySigStaffPositionForStep(ClefType clef, bool sharpish, int step)
    {
        int c0Position = clef switch
        {
            ClefType.Bass or ClefType.Bass8Below => 6,
            ClefType.Alto or ClefType.Percussion => 0,
            ClefType.Tenor => 2,
            ClefType.Soprano => -4,
            ClefType.MezzoSoprano => -2,
            ClefType.Baritone => 4,
            _ => -6,
        };
        int cPos = ((c0Position % 7) + 7) % 7;
        int[] positions = sharpish ? KeySigSharpPositions : KeySigFlatPositions;
        int hi = positions[cPos];
        int diff = hi - (cPos + step);
        return hi - (((diff % 7) + 7) % 7);
    }

    private static int KeySigStaffPosition(ClefType clef, bool isSharps, int index)
    {
        int c0Position = clef switch
        {
            ClefType.Bass or ClefType.Bass8Below => 6,
            ClefType.Alto or ClefType.Percussion => 0,
            ClefType.Tenor => 2,
            ClefType.Soprano => -4,
            ClefType.MezzoSoprano => -2,
            ClefType.Baritone => 4,
            _ => -6, // treble (and treble_8)
        };
        int cPos = ((c0Position % 7) + 7) % 7;
        int[] positions = isSharps ? KeySigSharpPositions : KeySigFlatPositions;
        int[] steps = isSharps ? KeySigSharpSteps : KeySigFlatSteps;
        int hi = positions[cPos];
        int diff = hi - (cPos + steps[index]);
        int modDiff = ((diff % 7) + 7) % 7;
        return hi - modDiff;
    }
}
