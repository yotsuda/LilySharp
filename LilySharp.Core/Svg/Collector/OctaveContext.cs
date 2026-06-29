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

    // Octave resolution mode. Default (false) = LilyPond-style relative: each
    // pitch takes the octave nearest the previous one, then '/, adjust. When
    // true (set by `octave absolute`), '/, are absolute offsets from a fixed C4
    // anchor (bare c = C4, c' = C5, c, = C3) and notes do not carry octave.
    public bool OctaveAbsolute;
    // File-level default mode, restored per voice and per section.
    public bool InitialOctaveAbsolute;

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
        CurrentOctave = InitialOctave;
        LastPitchName = 'c';
    }

    /// <summary>
    /// Resets the relative frame at a section boundary, additionally reverting
    /// the octave mode to the file-level default.
    /// </summary>
    public void ResetForSection()
    {
        CurrentOctave = InitialOctave;
        LastPitchName = 'c';
        OctaveAbsolute = InitialOctaveAbsolute;
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
        OctaveAbsolute = false;
        InitialOctaveAbsolute = false;
        LastPitchName = 'c';
    }
}

/// <summary>Running octave state captured for a nested frame.</summary>
internal readonly record struct OctaveSnapshot(int Octave, char PitchName);
