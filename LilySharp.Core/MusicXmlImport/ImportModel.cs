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

namespace LilySharp.Core.MusicXmlImport;

/// <summary>
/// The Import IR: a structure-PRESERVING model of a MusicXML score (repeats not
/// unrolled), sitting between the dirty XML reader and the <c>.lys</c> serializer.
/// It is deliberately NOT the render model (<c>MeasureCollector</c>'s Score is
/// structure-EXPANDED and downstream of parse, so it loses the repeat/section
/// shape we want to emit). Tier 1 is single-voice per measure.
/// </summary>
internal sealed class ImportDocument
{
    public string? Title { get; set; }
    public string? Composer { get; set; }
    /// <summary>Opening tempo (quarter BPM), or null when the source gives none.</summary>
    public int? Tempo { get; set; }
    public List<ImportPart> Parts { get; } = new();
}

/// <summary>One part (staff) of the score.</summary>
internal sealed class ImportPart
{
    /// <summary>MusicXML part id (e.g. "P1").</summary>
    public string Id { get; set; } = "";
    /// <summary>The printed part name, if any (e.g. "Piano").</summary>
    public string? Name { get; set; }
    /// <summary>A sanitized Lily# identifier the scaffolding refers to (e.g. "melody", "part1").</summary>
    public string SafeName { get; set; } = "";
    /// <summary>The part's opening clef as a Lily# clef name ("treble", "bass", ...).</summary>
    public string Clef { get; set; } = "treble";
    public List<ImportMeasure> Measures { get; } = new();
}

/// <summary>Closing barline style of a measure (the right-hand bar).</summary>
internal enum BarlineKind
{
    /// <summary>Ordinary single barline.</summary>
    Plain,
    /// <summary>Double thin barline (<c>||</c>).</summary>
    Double,
    /// <summary>Final barline (<c>|.</c>).</summary>
    Final,
    /// <summary>Repeat-end (<c>:|</c>).</summary>
    RepeatEnd,
}

/// <summary>A key signature as fifths (+sharps / -flats) plus mode name.</summary>
internal readonly record struct ImportKey(int Fifths, string Mode);

/// <summary>A time signature.</summary>
internal readonly record struct ImportTime(int Beats, int BeatType);

/// <summary>
/// One measure. Attribute fields (key/time/clef/tempo) are set only when they
/// CHANGE at this measure (the first measure carries the openers). Tier 1 keeps a
/// single flat item stream; multi-voice arrives with <c>&lt;backup&gt;</c> in Tier 2.
/// </summary>
internal sealed class ImportMeasure
{
    /// <summary>MusicXML <c>implicit="yes"</c> — an anacrusis/pickup measure.</summary>
    public bool Implicit { get; set; }
    public ImportKey? Key { get; set; }
    public ImportTime? Time { get; set; }
    public string? Clef { get; set; }
    public int? Tempo { get; set; }
    /// <summary>A repeat-start (<c>|:</c>) opens this measure.</summary>
    public bool RepeatForward { get; set; }
    /// <summary>The measure's closing bar style.</summary>
    public BarlineKind BarlineRight { get; set; } = BarlineKind.Plain;
    public List<ImportItem> Items { get; } = new();
}

/// <summary>Base for the ordered contents of a measure (notes, rests, harmonies).</summary>
internal abstract class ImportItem
{
}

/// <summary>
/// A note or rest. Rests set <see cref="IsRest"/> (pitch fields ignored). Chord
/// members after the first set <see cref="ChordWithPrev"/> (the MusicXML
/// <c>&lt;chord/&gt;</c> flag): they share the first member's duration.
/// </summary>
internal sealed class ImportNote : ImportItem
{
    public bool IsRest { get; set; }
    /// <summary>Diatonic step, 0=C .. 6=B.</summary>
    public int Step { get; set; }
    /// <summary>Chromatic alteration in semitones (-2..+2).</summary>
    public int Alter { get; set; }
    /// <summary>MusicXML octave number (middle C = octave 4).</summary>
    public int Octave { get; set; }
    /// <summary>The written note value: 1=whole, 2=half, 4=quarter, 8, 16, ...</summary>
    public int NoteValue { get; set; } = 4;
    public int Dots { get; set; }
    /// <summary>This note is a chord member sounding with the previous note.</summary>
    public bool ChordWithPrev { get; set; }
    /// <summary>A tie starts on this note (a <c>~</c> is emitted after it).</summary>
    public bool TieStart { get; set; }
    /// <summary>A tie ends on this note (continuation of the previous note).</summary>
    public bool TieStop { get; set; }
    /// <summary>A slur starts on this note (a <c>(</c> is emitted after it).</summary>
    public bool SlurStart { get; set; }
    /// <summary>A slur ends on this note (a <c>)</c> is emitted after it).</summary>
    public bool SlurStop { get; set; }
    /// <summary>Lily# articulation/ornament mark names (<c>staccato</c>, <c>accent</c>,
    /// <c>fermata</c>, <c>trill</c>, …), emitted as <c>@name</c> suffixes.</summary>
    public List<string> Articulations { get; } = new();
    /// <summary>When this note OPENS a tuplet: the ratio (actual in the time of normal,
    /// e.g. 3 in 2 for a triplet), emitted as <c>tuplet A/N { </c> before it.</summary>
    public (int Actual, int Normal)? TupletStart { get; set; }
    /// <summary>When this note CLOSES a tuplet (a <c>}</c> is emitted after it).</summary>
    public bool TupletStop { get; set; }
    /// <summary>Sung syllables, one per verse, in MusicXML note order.</summary>
    public List<ImportLyric> Lyrics { get; } = new();
}

/// <summary>
/// A chord symbol (MusicXML <c>&lt;harmony&gt;</c>). It attaches to the FOLLOWING
/// note as an inline <c>@chord(...)</c> in the serializer, matching where it sits
/// in the measure's element order.
/// </summary>
internal sealed class ImportHarmony : ImportItem
{
    public int RootStep { get; set; }
    public int RootAlter { get; set; }
    /// <summary>MusicXML harmony <c>&lt;kind&gt;</c> text (e.g. "minor-seventh").</summary>
    public string Kind { get; set; } = "major";
    /// <summary>The <c>text</c> attribute of an "other"/unmapped kind, for the
    /// quoted <c>@chord("...")</c> escape hatch; null otherwise.</summary>
    public string? KindText { get; set; }
    public int? BassStep { get; set; }
    public int? BassAlter { get; set; }
}

/// <summary>
/// A figured-bass group (MusicXML <c>&lt;figured-bass&gt;</c>). Like a harmony it
/// attaches to the FOLLOWING (bass) note, as an inline <c>@fig(...)</c>.
/// </summary>
internal sealed class ImportFiguredBass : ImportItem
{
    public List<ImportFigure> Figures { get; } = new();
}

/// <summary>One figure in a figured-bass group.</summary>
/// <param name="Number">The figure digit (0 = accidental-only / held placeholder).</param>
/// <param name="Alteration">0=none, 1=sharp, -1=flat, 2=natural.</param>
/// <param name="Held">A continuation (<c>_</c>) figure sustained from the previous bass note.</param>
internal readonly record struct ImportFigure(int Number, int Alteration, bool Held);

/// <summary>One sung syllable on a note.</summary>
/// <param name="Verse">1-based verse number.</param>
/// <param name="Text">The syllable text.</param>
/// <param name="Syllabic">"single" | "begin" | "middle" | "end".</param>
/// <param name="Extend">Whether the syllable is held (melisma extender).</param>
internal readonly record struct ImportLyric(int Verse, string Text, string Syllabic, bool Extend);
