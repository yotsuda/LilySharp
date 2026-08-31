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

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// The running state of the relative-octave chain plus the part transpose that
/// composes on top of it. Extracted from <see cref="MeasureCollector"/> as the
/// last (and hardest) god-class seam: unlike the accumulator collaborators
/// (<c>TabResolver</c> / <c>ChordNameCollector</c> / <c>LyricsCollector</c>),
/// which own an output list and run as post-passes, this is CORE state that the
/// main walk reads and writes on every note, chord, grace and tuplet. It is a
/// MUTABLE class the walk drives in place (an immutable record + <c>with</c>
/// would be far clumsier for the frequent mutate / save / restore pattern).
///
/// Only the per-walker running state lives here; the octave ALGORITHM stays
/// shared in <see cref="RelativeOctave"/>. Making the resolution a pure function
/// and sharing this context across the MIDI / MusicXML walkers (whose anchor
/// conventions differ — see the <c>RelativeOctave</c> remarks) is a deliberate
/// future step, out of scope for this extraction.
/// </summary>
internal sealed class OctaveContext
{
    // Running state, advanced as each pitch resolves.
    public int CurrentOctave = 4;
    public char LastPitchName = 'c';

    // Reset target for section boundaries (the octave armed for this voice).
    public int InitialOctave = 4;

    // Absolute-mode anchor: bare c = C(OctaveBase). Defaults to 4 (LilyPond's
    // fixed c=C4) and is overridden ONLY by an explicit `part X { octave N }`, so
    // a bass part can be written `octave 2` to avoid piling up `,` commas. The
    // clef default is deliberately NOT used here (absolute stays c=C4 by default).
    public int OctaveBase = 4;

    // Reset target for section boundaries, the absolute-mode twin of InitialOctave: the
    // base this voice was ARMED with, before anything moved it. Every mutation of
    // OctaveBase is balanced today (a phrase reference pushes and pops it, a chord anchor
    // saves and restores it), so restoring it at a boundary is an IDENTITY — until a
    // section reference carries octave marks, which is when it stops being one: without
    // it `~B'` would leave the base a step high for every section played after B.
    public int InitialOctaveBase = 4;

    // The shift the section REFERENCE that opened the current play wrote (`~B'` = +1), kept
    // for the WHOLE play rather than applied once. A phrase body and a parallel span open a
    // FRESH frame at this voice's anchor (ResetToInitial), and that anchor is the SECTION's,
    // not the part's: `section B { P }` played as `~B'` has to move too, or the notation
    // would mean "an octave up, unless the section happens to be written as a reference".
    // ⚠️ MEASURED 2026-08-31, and the two modes had already parted: absolute moved it (a
    // phrase reference pushes OctaveBase on top of the already-shifted base) and relative did
    // NOT (ResetToInitial went back to InitialOctave and dropped the play's shift). One book,
    // two answers, decided by `octave absolute`.
    public int SectionOctaveOffset;

    // Octave resolution mode. Default (false) = LilyPond-style relative: each
    // pitch takes the octave nearest the previous one, then '/, adjust. When
    // true (set by `octave absolute`), '/, are absolute offsets from a fixed C4
    // anchor (bare c = C4, c' = C5, c, = C3) and notes do not carry octave.
    public bool OctaveAbsolute;
    // File-level default mode, restored per voice and per section.
    public bool InitialOctaveAbsolute;

    // (A phrase-scoped DIATONIC shift lived here until 2026-08-28: ± scale steps armed by
    // a reference's glued interval argument — Melody'(3) = +2 — applied after relative
    // resolution and before the chromatic transpose below. The spelling was removed as
    // unreadable (user decision); nothing else ever set the field, so it went with the
    // save/restore stack, the reset marker's carrier and the DiatonicShift table.)

    // Part-option transpose: when set, every pitch is shifted by the interval
    // from c to (TransposeStep, TransposeAlt) AFTER relative-octave resolution.
    // LILYPOND-REF: scm/music-functions.scm \transpose (with from = c).
    public bool HasTranspose;
    public int TransposeStep;
    public int TransposeAlt;
    public int TransposeOctave;

    /// <summary>
    /// Resolves the absolute octave for a written pitch and advances the running
    /// last-pitch state. The CALLER updates <see cref="CurrentOctave"/> from the
    /// resolved octave (chords defer this until the first pitch), so this method
    /// intentionally does NOT touch <see cref="CurrentOctave"/>.
    /// </summary>
    public int Resolve(int step, int octaveOffset, char pitchName)
    {
        int actualOctave = OctaveAbsolute
            ? OctaveBase + octaveOffset
            : RelativeOctave.Resolve(
                RelativeOctave.StepIndex(LastPitchName), CurrentOctave,
                step, octaveOffset);
        LastPitchName = pitchName;
        return actualOctave;
    }

    /// <summary>
    /// Captures the running state for a nested frame (grace body, parallel
    /// sub-voice) that must be restored once the frame is collected.
    /// </summary>
    public OctaveSnapshot Snapshot() => new(CurrentOctave, LastPitchName);

    /// <summary>Restores running state captured by <see cref="Snapshot"/>.</summary>
    public void Restore(OctaveSnapshot snap)
    {
        CurrentOctave = snap.Octave;
        LastPitchName = snap.PitchName;
    }

    /// <summary>
    /// Resets the relative frame to this voice's initial octave at a phrase
    /// boundary (a <c>\relative</c> reset marker, or a parallel span that
    /// evaluates in a fresh frame). The octave MODE is left unchanged.
    /// </summary>
    public void ResetToInitial()
    {
        // …at the anchor of the section being PLAYED, which is the part's plus whatever the
        // reference that opened this play asked for (see SectionOctaveOffset). Zero for every
        // play written without marks, so this is the same line it has always been.
        CurrentOctave = InitialOctave + SectionOctaveOffset;
        LastPitchName = 'c';
    }

    /// <summary>
    /// Resets the relative frame at a section boundary, additionally reverting
    /// the octave mode to the file-level default.
    /// </summary>
    /// <param name="octaveOffset">
    /// The net shift the REFERENCE that opened this play wrote (<c>~B'</c> = +1). It moves
    /// BOTH anchors because the two modes read different ones: relative resolves against
    /// <see cref="CurrentOctave"/>, absolute against <see cref="OctaveBase"/>. Moving only
    /// one would make the marks work in one mode and vanish in the other — and the books
    /// this notation exists for are mostly the absolute ones (283 of the author's 326,
    /// measured 2026-08-31). It is the same pair the phrase reference already moves
    /// (MeasureCollector.EnterDefaultFrame / EnterPhraseTranspose).
    /// </param>
    public void ResetForSection(int octaveOffset = 0)
    {
        SectionOctaveOffset = octaveOffset;
        CurrentOctave = InitialOctave + octaveOffset;
        LastPitchName = 'c';
        OctaveAbsolute = InitialOctaveAbsolute;
        OctaveBase = InitialOctaveBase + octaveOffset;
    }

    /// <summary>
    /// Resets the octave fields to file-level defaults (from
    /// <c>MeasureCollector.Reset</c> between renders). Transpose is armed
    /// separately by <c>ApplyTranspose</c> and is NOT cleared here, matching the
    /// original inline reset.
    /// </summary>
    public void ResetAll()
    {
        CurrentOctave = 4;
        InitialOctave = 4;
        OctaveBase = 4;
        InitialOctaveBase = 4;
        SectionOctaveOffset = 0;
        OctaveAbsolute = false;
        InitialOctaveAbsolute = false;
        LastPitchName = 'c';
    }

    // --- Part transpose: composes on top of relative-octave resolution ---
    // LILYPOND-REF: scm/music-functions.scm \transpose (with from = c). The
    // relative chain runs on the ORIGINAL pitches; transpose is applied AFTER
    // Resolve, so a transposed part still resolves octaves from what the user
    // wrote. State (HasTranspose / Transpose*) and the application logic live
    // together here; the collector only composes the target (part + score) and
    // hands it to SetTranspose.

    /// <summary>
    /// Arms (or clears) the part transpose from an already-composed target (the
    /// part's own transpose combined with any score-level transpose). Null clears.
    /// </summary>
    public void SetTranspose((int step, int alt, int oct)? transpose)
    {
        if (transpose is { } t)
        {
            HasTranspose = true;
            TransposeStep = t.step;
            TransposeAlt = t.alt;
            TransposeOctave = t.oct;
        }
        else
        {
            HasTranspose = false;
        }
    }

    /// <summary>
    /// The currently armed transpose target (c→target interval), or null when the
    /// voice is untransposed. Lets a caller save/restore the transpose around a
    /// scoped shift (a phrase reference's auto-transpose).
    /// </summary>
    public (int step, int alt, int oct)? GetTranspose()
        => HasTranspose ? (TransposeStep, TransposeAlt, TransposeOctave) : null;

    /// <summary>
    /// Applies the part transpose to a resolved display pitch (no-op when the part
    /// is untransposed).
    /// </summary>
    public (int step, int alt, int octave) TransposePitch(int step, int alt, int octave) =>
        HasTranspose
            ? PitchTransposer.Transpose(step, alt, octave, TransposeStep, TransposeAlt, TransposeOctave)
            : (step, alt, octave);

    /// <summary>
    /// Shifts a written key signature's sharp count by the part transpose (no-op
    /// when untransposed). C major (0) transposed by d becomes D major (+2).
    /// LILYPOND-REF: \transpose also moves \key.
    /// </summary>
    public int TransposeKeySharps(int sharps) =>
        HasTranspose
            ? sharps + PitchTransposer.KeySignatureFifthsShift(TransposeStep, TransposeAlt)
            : sharps;
}

/// <summary>Running octave state captured for a nested frame.</summary>
internal readonly record struct OctaveSnapshot(int Octave, char PitchName);
