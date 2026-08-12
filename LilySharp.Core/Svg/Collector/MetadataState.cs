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

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Piece-level metadata state owned by <see cref="MeasureCollector"/>: title/
/// composer, tempo, time signature, key, clef, and the source positions of the
/// header declarations. Grouped out of MeasureCollector's ~55-field bag so this
/// state has one owner and one <see cref="Reset"/> (mirrors OctaveContext).
/// </summary>
internal sealed class MetadataState
{
    public string? Title;
    public string? Composer;

    // Text font-family for all non-music text (title/composer/lyrics/dynamics/etc.),
    // set by the top-level `font "NAME" [embedded]` header directive. Null = renderer
    // default (generic serif/sans). EmbedFont is collected but unused this phase
    // (font embedding is a later phase); the name is applied as a plain reference.
    public string? TextFont;
    public bool EmbedFont;

    // Source offsets of the header grobs (0 = none), emitted as data-pos so the
    // preview can click-to-jump to the title/composer/time/key/clef declarations.
    public int TitlePosition;
    public int ComposerPosition;
    public int TimePosition;
    public int KeyPosition;
    public int ClefPosition;
    public int TempoPosition;

    public int? Tempo;
    public string? TempoText;
    public int TempoBeatUnit = 4;
    public int TempoDots;
    public int SwingSubdivision;

    public int TimeBeats = 4;
    public string? TimeBeatsText; // additive numerator as written ("3+2")
    public bool TimeSenzaMisura;  // time none — unmeasured
    public int TimeBeatType = 4;

    public int KeySharps;
    public string? KeyCustom;     // encoded custom key (KeySignature.EncodeCustom)
    public string? InitialKeyCustom;
    public int InitialKeySharps;  // preserved for Score.KeySignature (not mutated by mid-measure key changes)
    // KeyTonicStep/KeyTonicAlter describe the SCORE main key's tonic (the phrase
    // auto-transpose home). Like the other Key* header fields they are set by the
    // top-level `key` and, matching KeyTonicStep's original behavior, left out of
    // Reset() (a fresh collector zeroes them = C natural, the default tonic).
    public int KeyTonicStep;      // tonic's diatonic step (0=C..6=B); also Roman-numeral chord degrees
    public int KeyTonicAlter;     // tonic's accidental in semitones (Ees=-1, Fis=+1)

    public string Clef = "treble";
    public string InitialClef = "treble"; // preserved for Score.Clef (not mutated by mid-measure clef changes)

    /// <summary>A field-for-field copy, for the collect walk checkpoint
    /// (CollectWalkProbe). All fields are scalars/strings, so a shallow
    /// memberwise clone IS a full snapshot.</summary>
    public MetadataState Clone() => (MetadataState)MemberwiseClone();

    /// <summary>Restores every field from a <see cref="Clone"/> snapshot.</summary>
    public void CopyFrom(MetadataState other)
    {
        Title = other.Title;
        Composer = other.Composer;
        TextFont = other.TextFont;
        EmbedFont = other.EmbedFont;
        TitlePosition = other.TitlePosition;
        ComposerPosition = other.ComposerPosition;
        TimePosition = other.TimePosition;
        KeyPosition = other.KeyPosition;
        ClefPosition = other.ClefPosition;
        TempoPosition = other.TempoPosition;
        Tempo = other.Tempo;
        TempoText = other.TempoText;
        TempoBeatUnit = other.TempoBeatUnit;
        TempoDots = other.TempoDots;
        SwingSubdivision = other.SwingSubdivision;
        TimeBeats = other.TimeBeats;
        TimeBeatsText = other.TimeBeatsText;
        TimeSenzaMisura = other.TimeSenzaMisura;
        TimeBeatType = other.TimeBeatType;
        KeySharps = other.KeySharps;
        KeyCustom = other.KeyCustom;
        InitialKeyCustom = other.InitialKeyCustom;
        InitialKeySharps = other.InitialKeySharps;
        KeyTonicStep = other.KeyTonicStep;
        KeyTonicAlter = other.KeyTonicAlter;
        Clef = other.Clef;
        InitialClef = other.InitialClef;
    }

    /// <summary>
    /// Resets to first-bar defaults for a fresh collection pass. NOTE:
    /// <see cref="KeyTonicStep"/> is intentionally NOT reset here — the original
    /// MeasureCollector.Reset() did not reset it, and this preserves that exact
    /// behavior (all current callers use a fresh collector, so it is 0 anyway).
    /// </summary>
    public void Reset()
    {
        Title = null;
        Composer = null;
        TitlePosition = 0;
        ComposerPosition = 0;
        TimePosition = 0;
        KeyPosition = 0;
        ClefPosition = 0;
        TempoPosition = 0;
        Tempo = null;
        TempoText = null;
        TempoBeatUnit = 4;
        TempoDots = 0;
        SwingSubdivision = 0;
        TimeBeats = 4;
        TimeBeatsText = null;
        TimeSenzaMisura = false;
        TimeBeatType = 4;
        KeySharps = 0;
        KeyCustom = null;
        InitialKeyCustom = null;
        InitialKeySharps = 0;
        Clef = "treble";
        InitialClef = "treble";
    }
}
