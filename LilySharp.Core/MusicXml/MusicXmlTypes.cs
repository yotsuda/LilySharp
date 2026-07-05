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

using System.Xml.Linq;

namespace LilySharp.Core.MusicXml;

/// <summary>
/// Represents a MusicXML document.
/// </summary>
public sealed class MusicXmlDocument
{
    public string? Title { get; set; }
    public string? Composer { get; set; }
    public List<MusicXmlPart> Parts { get; } = new();

    /// <summary>
    /// Converts to XML document.
    /// </summary>
    public XDocument ToXml()
    {
        var scorePartwise = new XElement("score-partwise",
            new XAttribute("version", "4.0"));

        // Work info
        if (!string.IsNullOrEmpty(Title))
        {
            scorePartwise.Add(new XElement("work",
                new XElement("work-title", Title)));
        }

        // Identification
        if (!string.IsNullOrEmpty(Composer))
        {
            scorePartwise.Add(new XElement("identification",
                new XElement("creator", new XAttribute("type", "composer"), Composer)));
        }

        // Part list
        var partList = new XElement("part-list");
        for (int i = 0; i < Parts.Count; i++)
        {
            var part = Parts[i];
            partList.Add(new XElement("score-part",
                new XAttribute("id", $"P{i + 1}"),
                new XElement("part-name", part.Name ?? $"Part {i + 1}")));
        }
        scorePartwise.Add(partList);

        // Parts
        for (int i = 0; i < Parts.Count; i++)
        {
            scorePartwise.Add(Parts[i].ToXml($"P{i + 1}"));
        }

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XDocumentType("score-partwise", "-//Recordare//DTD MusicXML 4.0 Partwise//EN",
                "http://www.musicxml.org/dtds/partwise.dtd", null),
            scorePartwise);
    }

    /// <summary>
    /// Saves to file.
    /// </summary>
    public void Save(string path)
    {
        ToXml().Save(path);
    }
}

/// <summary>
/// Represents a part in MusicXML.
/// </summary>
public sealed class MusicXmlPart
{
    public string? Name { get; set; }
    public List<MusicXmlMeasure> Measures { get; } = new();

    public XElement ToXml(string id)
    {
        var part = new XElement("part", new XAttribute("id", id));
        foreach (var measure in Measures)
        {
            part.Add(measure.ToXml());
        }
        return part;
    }
}

/// <summary>
/// Represents a measure in MusicXML.
/// </summary>
public sealed class MusicXmlMeasure
{
    public int Number { get; set; }

    /// <summary>
    /// True for an anacrusis (pickup) measure: MusicXML marks it
    /// <c>implicit="yes"</c> so consumers don't count it in the bar numbering.
    /// </summary>
    public bool Implicit { get; set; }

    public MusicXmlAttributes? Attributes { get; set; }
    public MusicXmlDirection? Direction { get; set; }
    public List<MusicXmlDirection> Directions { get; } = new();
    public List<MusicXmlNote> Notes { get; } = new();

    /// <summary>Repeat sign opening this measure (<c>|:</c>).</summary>
    public bool RepeatForward { get; set; }
    /// <summary>Repeat sign closing this measure (<c>:|</c>).</summary>
    public bool RepeatBackward { get; set; }
    /// <summary>Closing bar style ("light-light" for <c>||</c>, "light-heavy"
    /// for <c>|.</c>, "dashed" for <c>!</c>); null = ordinary barline.</summary>
    public string? BarStyle { get; set; }

    public XElement ToXml()
    {
        var measure = new XElement("measure", new XAttribute("number", Number));
        if (Implicit)
            measure.Add(new XAttribute("implicit", "yes"));

        if (Attributes != null)
            measure.Add(Attributes.ToXml());

        if (RepeatForward)
            measure.Add(new XElement("barline", new XAttribute("location", "left"),
                new XElement("bar-style", "heavy-light"),
                new XElement("repeat", new XAttribute("direction", "forward"))));

        // Legacy single direction (tempo)
        if (Direction != null)
            measure.Add(Direction.ToXml());

        // Interleave directions with notes by emitting all directions first
        foreach (var dir in Directions)
            measure.Add(dir.ToXml());

        foreach (var note in Notes)
            measure.Add(note.ToXml());

        if (RepeatBackward || BarStyle != null)
        {
            var barline = new XElement("barline", new XAttribute("location", "right"));
            barline.Add(new XElement("bar-style",
                RepeatBackward ? "light-heavy" : BarStyle));
            if (RepeatBackward)
                barline.Add(new XElement("repeat", new XAttribute("direction", "backward")));
            measure.Add(barline);
        }

        return measure;
    }
}

/// <summary>
/// Measure attributes (time signature, key, clef, divisions).
/// </summary>
public sealed class MusicXmlAttributes
{
    public int Divisions { get; set; } = 1;
    public int? KeyFifths { get; set; }
    public string? KeyMode { get; set; }
    /// <summary>Non-traditional key: encoded (step, alter) pairs
    /// (KeySignature.EncodeCustom); wins over fifths.</summary>
    public string? KeyCustom { get; set; }
    public int? TimeBeats { get; set; }
    /// <summary>Numerator as written for additive meters ("3+2"); wins over
    /// <see cref="TimeBeats"/> in the serialized &lt;beats&gt;.</summary>
    public string? TimeBeatsText { get; set; }
    public int? TimeBeatType { get; set; }
    /// <summary>Unmeasured (&lt;senza-misura/&gt;); wins over beats.</summary>
    public bool TimeSenzaMisura { get; set; }
    public string? ClefSign { get; set; }
    public int? ClefLine { get; set; }
    /// <summary>±1 for the _8 / ^8 octave clefs (&lt;clef-octave-change&gt;).</summary>
    public int? ClefOctaveChange { get; set; }

    public XElement ToXml()
    {
        var attrs = new XElement("attributes",
            new XElement("divisions", Divisions));

        if (KeyCustom != null)
        {
            var keyEl = new XElement("key");
            foreach (var (step, alter) in LilySharp.Core.Svg.Model.KeySignature.DecodeCustom(KeyCustom))
            {
                keyEl.Add(new XElement("key-step", "CDEFGAB"[step]));
                keyEl.Add(new XElement("key-alter", alter));
            }
            attrs.Add(keyEl);
        }
        else if (KeyFifths.HasValue)
        {
            attrs.Add(new XElement("key",
                new XElement("fifths", KeyFifths.Value),
                KeyMode != null ? new XElement("mode", KeyMode) : null));
        }

        if (TimeSenzaMisura)
        {
            attrs.Add(new XElement("time", new XElement("senza-misura")));
        }
        else if (TimeBeats.HasValue && TimeBeatType.HasValue)
        {
            attrs.Add(new XElement("time",
                new XElement("beats", TimeBeatsText ?? TimeBeats.Value.ToString()),
                new XElement("beat-type", TimeBeatType.Value)));
        }

        if (ClefSign != null)
        {
            attrs.Add(new XElement("clef",
                new XElement("sign", ClefSign),
                ClefLine.HasValue ? new XElement("line", ClefLine.Value) : null,
                ClefOctaveChange.HasValue
                    ? new XElement("clef-octave-change", ClefOctaveChange.Value)
                    : null));
        }

        return attrs;
    }
}

/// <summary>
/// Direction element for dynamics, tempo, and other performance indications.
/// </summary>
public sealed class MusicXmlDirection
{
    public string? DynamicType { get; set; }
    public int? Tempo { get; set; }
    public string? Placement { get; set; }
    /// <summary>Hairpin: "crescendo" / "diminuendo" / "stop".</summary>
    public string? WedgeType { get; set; }
    /// <summary>Pedal mark: "start" / "stop" / "sostenuto".</summary>
    public string? PedalType { get; set; }
    /// <summary>Ottava line: "down" (8va) / "up" (8vb) / "stop".</summary>
    public string? OctaveShiftType { get; set; }

    public XElement ToXml()
    {
        var placement = Placement ?? "above";
        var direction = new XElement("direction", new XAttribute("placement", placement));

        if (DynamicType != null)
        {
            direction.Add(new XElement("direction-type",
                new XElement("dynamics",
                    new XElement(DynamicType))));
        }

        if (WedgeType != null)
            direction.Add(new XElement("direction-type",
                new XElement("wedge", new XAttribute("type", WedgeType))));

        if (PedalType != null)
            direction.Add(new XElement("direction-type",
                new XElement("pedal", new XAttribute("type", PedalType))));

        if (OctaveShiftType != null)
        {
            var shift = new XElement("octave-shift", new XAttribute("type", OctaveShiftType));
            if (OctaveShiftType != "stop")
                shift.Add(new XAttribute("size", 8));
            direction.Add(new XElement("direction-type", shift));
        }

        if (Tempo.HasValue)
        {
            direction.Add(new XElement("direction-type",
                new XElement("metronome",
                    new XElement("beat-unit", "quarter"),
                    new XElement("per-minute", Tempo.Value))));
            direction.Add(new XElement("sound", new XAttribute("tempo", Tempo.Value)));
        }

        return direction;
    }
}

/// <summary>
/// Represents a note in MusicXML.
/// </summary>
public sealed class MusicXmlNote
{
    public bool IsRest { get; set; }
    public bool IsChord { get; set; }
    /// <summary>Drum note: serialize &lt;unpitched&gt; with Step/Octave as the
    /// DISPLAY position instead of &lt;pitch&gt;.</summary>
    public bool IsUnpitched { get; set; }
    /// <summary>A &lt;backup&gt; pseudo-entry (multi-voice): rewinds the measure
    /// cursor by <see cref="Duration"/> before the next voice's notes.</summary>
    public bool IsBackup { get; set; }
    /// <summary>Escape hatch for non-note entries that must keep their place
    /// in the measure's note stream (e.g. &lt;harmony&gt; before its note):
    /// when set, ToXml returns this element verbatim.</summary>
    public XElement? RawElement { get; set; }
    /// <summary>MusicXML voice number (1-based) in multi-voice measures.</summary>
    public int? Voice { get; set; }
    /// <summary>Notehead style name ("x", "diamond", …) or null for default.</summary>
    public string? Notehead { get; set; }
    public string? Step { get; set; }
    public double? Alter { get; set; }
    public int? Octave { get; set; }
    /// <summary>Explicit &lt;accidental&gt; name (quarter-sharp etc.); null =
    /// let the importer infer from alter.</summary>
    public string? AccidentalName { get; set; }
    public int Duration { get; set; }
    public string? Type { get; set; }
    public int Dots { get; set; }
    /// <summary>Tuplet ratio for <c>&lt;time-modification&gt;</c>: this note plays
    /// <see cref="NormalNotes"/> in the time of <see cref="ActualNotes"/> (e.g. a
    /// triplet is 3 actual in 2 normal). Null outside a tuplet.</summary>
    public int? ActualNotes { get; set; }
    public int? NormalNotes { get; set; }
    public List<string> Articulations { get; } = new();
    public List<string> Ornaments { get; } = new();
    /// <summary>&lt;technical&gt; child elements (tap, snap-pizzicato, …).</summary>
    public List<XElement> Technicals { get; } = new();
    public bool IsGrace { get; set; }
    public bool IsSlash { get; set; }
    public bool TieStart { get; set; }
    public bool TieStop { get; set; }
    public bool SlurStart { get; set; }
    public bool SlurStop { get; set; }

    // Legacy property for backward compatibility
    public string? Dynamic { get; set; }

    /// <summary>Sung syllables (verse, text, syllabic form, extender) — one per
    /// verse. Vocal editors (VOCALOID, Synthesizer V, CeVIO, NEUTRINO …)
    /// read these on import.</summary>
    public List<(int Verse, string Text, string Syllabic, bool Extend)> Lyrics { get; } = new();

    public XElement ToXml()
    {
        // Non-note pseudo-entries keep their slot in the note stream.
        if (RawElement != null)
            return RawElement;

        // Multi-voice measure-cursor rewind — not a <note> at all.
        if (IsBackup)
            return new XElement("backup", new XElement("duration", Duration));

        var note = new XElement("note");

        if (IsGrace)
        {
            var graceEl = new XElement("grace");
            if (IsSlash)
                graceEl.Add(new XAttribute("slash", "yes"));
            note.Add(graceEl);
        }

        if (IsChord)
            note.Add(new XElement("chord"));

        if (IsRest)
        {
            note.Add(new XElement("rest"));
        }
        else if (IsUnpitched)
        {
            note.Add(new XElement("unpitched",
                new XElement("display-step", Step),
                new XElement("display-octave", Octave)));
        }
        else
        {
            var pitch = new XElement("pitch",
                new XElement("step", Step),
                Alter.HasValue && Alter.Value != 0 ? new XElement("alter", Alter.Value) : null,
                new XElement("octave", Octave));
            note.Add(pitch);
        }

        if (!IsGrace)
            note.Add(new XElement("duration", Duration));

        // Ties (before type)
        if (TieStart)
            note.Add(new XElement("tie", new XAttribute("type", "start")));
        if (TieStop)
            note.Add(new XElement("tie", new XAttribute("type", "stop")));

        if (Voice.HasValue)
            note.Add(new XElement("voice", Voice.Value));

        if (Type != null)
            note.Add(new XElement("type", Type));

        for (int i = 0; i < Dots; i++)
            note.Add(new XElement("dot"));

        if (AccidentalName != null)
            note.Add(new XElement("accidental", AccidentalName));

        // Tuplet timing: <actual-notes> play in the time of <normal-notes>.
        if (ActualNotes.HasValue && NormalNotes.HasValue)
            note.Add(new XElement("time-modification",
                new XElement("actual-notes", ActualNotes.Value),
                new XElement("normal-notes", NormalNotes.Value)));

        if (Notehead != null)
            note.Add(new XElement("notehead", Notehead));

        // Notations (articulations, ornaments, ties, slurs)
        var hasNotations = Articulations.Count > 0 || Ornaments.Count > 0 ||
                          Technicals.Count > 0 ||
                          TieStart || TieStop || SlurStart || SlurStop;

        if (hasNotations)
        {
            var notations = new XElement("notations");

            // Tied notations
            if (TieStart)
                notations.Add(new XElement("tied", new XAttribute("type", "start")));
            if (TieStop)
                notations.Add(new XElement("tied", new XAttribute("type", "stop")));

            // Slur notations
            if (SlurStart)
                notations.Add(new XElement("slur", new XAttribute("type", "start"), new XAttribute("number", "1")));
            if (SlurStop)
                notations.Add(new XElement("slur", new XAttribute("type", "stop"), new XAttribute("number", "1")));

            // Articulations
            if (Articulations.Count > 0)
            {
                var artics = new XElement("articulations");
                foreach (var a in Articulations)
                    artics.Add(new XElement(a));
                notations.Add(artics);
            }

            // Ornaments
            if (Ornaments.Count > 0)
            {
                var orns = new XElement("ornaments");
                foreach (var o in Ornaments)
                    orns.Add(new XElement(o));
                notations.Add(orns);
            }

            // Technical (guitar/TAB techniques)
            if (Technicals.Count > 0)
            {
                var tech = new XElement("technical");
                foreach (var t in Technicals)
                    tech.Add(t);
                notations.Add(tech);
            }

            note.Add(notations);
        }

        // <lyric> — after notations per the MusicXML order.
        foreach (var (verse, text, syllabic, extend) in Lyrics)
        {
            var lyric = new XElement("lyric", new XAttribute("number", verse));
            // A '~' inside the syllable is an ELISION: two texts joined by
            // <elision>‿</elision> inside ONE lyric (MusicXML lyric sequence).
            var parts = text.Split('~', '‿');
            lyric.Add(new XElement("syllabic", syllabic));
            lyric.Add(new XElement("text", parts[0]));
            for (int pi = 1; pi < parts.Length; pi++)
            {
                lyric.Add(new XElement("elision", "‿"));
                lyric.Add(new XElement("text", parts[pi]));
            }
            if (extend)
                lyric.Add(new XElement("extend"));
            note.Add(lyric);
        }

        return note;
    }
}
