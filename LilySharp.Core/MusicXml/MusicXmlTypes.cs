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
internal sealed class MusicXmlDocument
{
    public string? Title { get; set; }
    public string? Composer { get; set; }
    public string? Subtitle { get; set; }
    public string? Poet { get; set; }
    public List<MusicXmlPart> Parts { get; } = new();

    /// <summary>
    /// The score's staff groups over runs of consecutive parts (<c>&lt;part-group&gt;</c>), outer
    /// before inner: a <c>grandStaff</c> kept as several parts (its staves labelled apart) is a
    /// brace, a <c>staffGroup</c> a bracket with its bar lines through, a <c>choirStaff</c> a
    /// bracket with its bar lines per staff. Until 2026-09-30 only the first was written.
    /// </summary>
    public List<(MusicXmlPart First, MusicXmlPart Last, string Symbol, bool Barline)> PartGroups { get; } = new();

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

        // Identification: one <creator> per credited person, typed.
        if (!string.IsNullOrEmpty(Composer) || !string.IsNullOrEmpty(Poet))
        {
            var identification = new XElement("identification");
            if (!string.IsNullOrEmpty(Composer))
                identification.Add(new XElement("creator", new XAttribute("type", "composer"), Composer));
            if (!string.IsNullOrEmpty(Poet))
                identification.Add(new XElement("creator", new XAttribute("type", "poet"), Poet));
            scorePartwise.Add(identification);
        }

        // MusicXML has no subtitle field; a typed <credit> is the page text that carries one
        // (after <identification> and <defaults>, before <part-list> in the schema's order).
        if (!string.IsNullOrEmpty(Subtitle))
        {
            scorePartwise.Add(new XElement("credit",
                new XAttribute("page", 1),
                new XElement("credit-type", "subtitle"),
                new XElement("credit-words", Subtitle)));
        }

        // Part list
        var partList = new XElement("part-list");
        // A group's number is the smallest one no open group holds, so nested groups differ.
        var groupNumbers = new Dictionary<int, int>();
        for (int i = 0; i < Parts.Count; i++)
        {
            var part = Parts[i];
            string id = $"P{i + 1}";
            for (int g = 0; g < PartGroups.Count; g++)
            {
                if (!ReferenceEquals(PartGroups[g].First, part))
                    continue;
                int number = 1;
                while (groupNumbers.ContainsValue(number))
                    number++;
                groupNumbers[g] = number;
                partList.Add(new XElement("part-group",
                    new XAttribute("type", "start"), new XAttribute("number", number),
                    new XElement("group-symbol", PartGroups[g].Symbol),
                    new XElement("group-barline", PartGroups[g].Barline ? "yes" : "no")));
            }
            var scorePart = new XElement("score-part",
                new XAttribute("id", id),
                // A staff the page labels nothing still needs a name: the id, marked as
                // not printed (2026-09-30), so a reader prints what the page prints.
                new XElement("part-name",
                    part.DisplayName == null ? new XAttribute("print-object", "no") : null,
                    part.DisplayName ?? part.Name ?? $"Part {i + 1}"));
            if (part.MidiProgram is int program)
            {
                // The sound the .mid gives the part (HANDOFF §2 F-midi): <score-instrument>
                // first, then the <midi-instrument> that points at it (the order the schema
                // requires). MusicXML counts channels and programs from 1. Channels follow the
                // part order and step over 10, the GM drum channel, as the .mid's do.
                string instrumentId = id + "-I1";
                int channel = i < 9 ? i + 1 : Math.Min(16, i + 2);
                scorePart.Add(new XElement("score-instrument",
                    new XAttribute("id", instrumentId),
                    new XElement("instrument-name", Midi.GeneralMidi.InstrumentNames[program])));
                scorePart.Add(new XElement("midi-instrument",
                    new XAttribute("id", instrumentId),
                    new XElement("midi-channel", channel),
                    new XElement("midi-program", program + 1)));
            }
            partList.Add(scorePart);
            // Inner groups close first.
            for (int g = PartGroups.Count - 1; g >= 0; g--)
            {
                if (!ReferenceEquals(PartGroups[g].Last, part) || !groupNumbers.Remove(g, out int number))
                    continue;
                partList.Add(new XElement("part-group",
                    new XAttribute("type", "stop"), new XAttribute("number", number)));
            }
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
internal sealed class MusicXmlPart
{
    /// <summary>The part's id in the source (<c>vo</c>) — what the exporter finds it by.</summary>
    public string? Name { get; set; }

    /// <summary>The name the page labels the part's staff with (<c>part vo "Vocal"</c>,
    /// <c>staff rh "Right"</c>), written as <c>&lt;part-name&gt;</c>; null writes the id.</summary>
    public string? DisplayName { get; set; }

    /// <summary>The part's General MIDI program (0-based), written as its
    /// <c>&lt;midi-instrument&gt;</c>; null writes none.</summary>
    public int? MidiProgram { get; set; }
    public List<MusicXmlMeasure> Measures { get; } = new();

    /// <summary>The part's tablature staff, or null when the score shows it on none.</summary>
    public MusicXmlTab? Tab { get; set; }

    public XElement ToXml(string id)
    {
        var part = new XElement("part", new XAttribute("id", id));
        for (int i = 0; i < Measures.Count; i++)
        {
            part.Add(Measures[i].ToXml(Tab, first: i == 0));
        }
        return part;
    }
}

/// <summary>
/// A part's tablature staff: <c>tab gt</c> alone makes the part's one staff a TAB staff;
/// <c>staff gt  tab gt</c> makes it a two-staff part, notation on staff 1 and the same
/// notes again on staff 2 with their strings and frets — the way a guitar part with
/// linked tablature is written (MusicXML 4.0 <c>&lt;staff-details&gt;</c> /
/// <c>&lt;staff-tuning&gt;</c>, <c>&lt;clef&gt;&lt;sign&gt;TAB</c>).
/// </summary>
/// <param name="Tuning">The open strings' sounding MIDI pitches, lowest string first.</param>
/// <param name="WithNotation">True when the score also shows the part on a notation staff.</param>
internal sealed record MusicXmlTab(int[] Tuning, bool WithNotation)
{
    /// <summary>The staff the tablature is: 2 beside a notation staff, else the part's only one.</summary>
    public int? StaffNumber => WithNotation ? 2 : null;

    /// <summary>The TAB clef, numbered for its staff.</summary>
    public XElement Clef() => new("clef",
        StaffNumber is { } n ? new XAttribute("number", n) : null,
        new XElement("sign", "TAB"),
        new XElement("line", 5));

    /// <summary>The TAB staff's lines and open strings (line 1 = the lowest string), and the capo.</summary>
    public XElement Details(int? capo)
    {
        var details = new XElement("staff-details",
            StaffNumber is { } n ? new XAttribute("number", n) : null,
            new XElement("staff-lines", Tuning.Length));
        for (int i = 0; i < Tuning.Length; i++)
        {
            int midi = Tuning[i];
            var (step, alter) = SharpSpelling[((midi % 12) + 12) % 12];
            details.Add(new XElement("staff-tuning", new XAttribute("line", i + 1),
                new XElement("tuning-step", step),
                alter != 0 ? new XElement("tuning-alter", alter) : null,
                new XElement("tuning-octave", midi / 12 - 1)));
        }
        if (capo is { } c && c > 0)
            details.Add(new XElement("capo", c));
        return details;
    }

    private static readonly (char Step, int Alter)[] SharpSpelling =
    {
        ('C', 0), ('C', 1), ('D', 0), ('D', 1), ('E', 0), ('F', 0),
        ('F', 1), ('G', 0), ('G', 1), ('A', 0), ('A', 1), ('B', 0),
    };
}

/// <summary>
/// Represents a measure in MusicXML.
/// </summary>
internal sealed class MusicXmlMeasure
{
    public int Number { get; set; }

    /// <summary>
    /// True for an anacrusis (pickup) measure: MusicXML marks it
    /// <c>implicit="yes"</c> so consumers don't count it in the bar numbering.
    /// </summary>
    public bool Implicit { get; set; }

    public MusicXmlAttributes? Attributes { get; set; }
    public MusicXmlDirection? Direction { get; set; }

    /// <summary>A playback tempo (quarters a minute) that prints nothing — a bare
    /// <c>&lt;sound tempo&gt;</c> — or null.</summary>
    public int? SoundTempo { get; set; }
    public List<MusicXmlDirection> Directions { get; } = new();
    public List<MusicXmlNote> Notes { get; } = new();

    /// <summary>Repeat sign opening this measure (<c>|:</c>).</summary>
    public bool RepeatForward { get; set; }
    /// <summary>Repeat sign closing this measure (<c>:|</c>).</summary>
    public bool RepeatBackward { get; set; }
    /// <summary>Closing bar style ("light-light" for <c>||</c>, "light-heavy"
    /// for <c>|.</c>, "dashed" for <c>!</c>); null = ordinary barline.</summary>
    public string? BarStyle { get; set; }

    /// <summary>Volta ending that STARTS on this measure — the number(s) as a
    /// MusicXML ending list ("1", "1,2", "1,2,3"); null when none.</summary>
    public string? EndingStartNumbers { get; set; }
    /// <summary>Volta ending that ENDS on this measure — the number(s); null when none.</summary>
    public string? EndingStopNumbers { get; set; }
    /// <summary>The ending-stop kind: "stop" (closed hook, e.g. a 1st ending) or
    /// "discontinue" (open, e.g. the final ending). Defaults to "stop".</summary>
    public string? EndingStopType { get; set; }

    public XElement ToXml(MusicXmlTab? tab = null, bool first = false)
    {
        var measure = new XElement("measure", new XAttribute("number", Number));
        if (Implicit)
            measure.Add(new XAttribute("implicit", "yes"));

        if (Attributes != null)
            measure.Add(Attributes.ToXml(tab, first));
        else if (tab != null && first)
            measure.Add(new MusicXmlAttributes { Divisions = null }.ToXml(tab, first));

        if (RepeatForward || EndingStartNumbers != null)
        {
            // DTD order inside <barline>: bar-style, ending, repeat.
            var left = new XElement("barline", new XAttribute("location", "left"));
            if (RepeatForward)
                left.Add(new XElement("bar-style", "heavy-light"));
            if (EndingStartNumbers != null)
                left.Add(new XElement("ending",
                    new XAttribute("number", EndingStartNumbers),
                    new XAttribute("type", "start")));
            if (RepeatForward)
                left.Add(new XElement("repeat", new XAttribute("direction", "forward")));
            measure.Add(left);
        }

        // Legacy single direction (tempo)
        if (Direction != null)
            measure.Add(Direction.ToXml());
        if (SoundTempo is { } soundTempo)
            measure.Add(new XElement("sound", new XAttribute("tempo", soundTempo)));

        // Interleave directions with notes by emitting all directions first
        foreach (var dir in Directions)
            measure.Add(dir.ToXml());

        foreach (var note in Notes)
            measure.Add(note.ToXml(staff: tab?.WithNotation == true ? 1 : null, tabStaff: tab is { WithNotation: false }));

        // The tablature beside a notation staff: back to the bar's start and the same notes
        // again on staff 2, with their strings and frets (directions and harmonies stay on
        // staff 1, where they were written).
        if (tab is { WithNotation: true } && Notes.Any(n => n.RawElement == null && !n.IsBackup))
        {
            int at = 0;
            foreach (var n in Notes)
                if (n.RawElement == null && !n.IsChord && !n.IsGrace)
                    at += n.IsBackup ? -n.Duration : n.Duration;
            if (at > 0)
                measure.Add(new XElement("backup", new XElement("duration", at)));
            foreach (var note in Notes)
                if (note.RawElement == null)
                    measure.Add(note.ToXml(staff: 2, tabStaff: true, tabCopy: true));
        }

        if (RepeatBackward || BarStyle != null || EndingStopNumbers != null)
        {
            // DTD order inside <barline>: bar-style, ending, repeat.
            var barline = new XElement("barline", new XAttribute("location", "right"));
            if (RepeatBackward || BarStyle != null)
                barline.Add(new XElement("bar-style",
                    RepeatBackward ? "light-heavy" : BarStyle));
            if (EndingStopNumbers != null)
                barline.Add(new XElement("ending",
                    new XAttribute("number", EndingStopNumbers),
                    new XAttribute("type", EndingStopType ?? "stop")));
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
internal sealed class MusicXmlAttributes
{
    /// <summary>Ticks per quarter. Null on a mid-piece CHANGE block, which says only what
    /// changed — a reader that saw &lt;divisions&gt; once keeps it, and repeating it invites
    /// the two copies to disagree.</summary>
    public int? Divisions { get; set; } = 1;
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
    /// <summary>&lt;measure-style&gt;&lt;measure-repeat&gt; type
    /// ("start"/"stop") for percent-repeat signs.</summary>
    public string? MeasureRepeat { get; set; }

    /// <summary>How many measures one repetition is — 1 for <c>%</c>, 2 for <c>%%</c> — written
    /// as the content of a <c>start</c>.</summary>
    public int MeasureRepeatBars { get; set; } = 1;
    /// <summary>&lt;measure-style&gt;&lt;multiple-rest&gt;: the number of bars a multi-measure
    /// rest (<c>R1*N</c>) spans, written on the FIRST of them; each of the N measures then
    /// holds a whole-measure rest (<see cref="MusicXmlNote.IsMeasureRest"/>). Null when none
    /// starts here.</summary>
    public int? MultipleRest { get; set; }
    /// <summary>The part's staff count (<c>&lt;staves&gt;</c>) — 2 for a grand staff written as
    /// one part — or null for one staff.</summary>
    public int? Staves { get; set; }

    /// <summary>The staff <see cref="ClefSign"/> is on in a multi-staff part (the clef's
    /// <c>number</c>), or null on a one-staff part.</summary>
    public int? ClefNumber { get; set; }

    /// <summary>A grand staff's lower clef (<c>&lt;clef number="2"&gt;</c>), or null.</summary>
    public (string Sign, int? Line, int? OctaveChange)? Staff2Clef { get; set; }

    public string? ClefSign { get; set; }
    public int? ClefLine { get; set; }
    /// <summary>±1 for the _8 / ^8 octave clefs (&lt;clef-octave-change&gt;).</summary>
    public int? ClefOctaveChange { get; set; }

    /// <summary>
    /// The part's WHOLE written→sounding shift in semitones (&lt;transpose&gt;) — the
    /// instrument's own plus the octave its clef carries — or null when the part sounds as
    /// it prints.
    /// </summary>
    /// <remarks>
    /// ⚠️ This DOES include the octave <see cref="ClefOctaveChange"/> shows, and that is not
    /// a double count: the two elements answer different questions. An octave clef is
    /// notation — it says where the written pitch is DRAWN — while this says what that
    /// written pitch SOUNDS, and a reader has nothing else to read for the second question.
    /// It is how every octave-transposing instrument is published (a guitar carries
    /// <c>clef-octave-change</c> −1 AND <c>transpose</c> −12). ⚠️ An importer must therefore
    /// subtract the clef's share when the clef NAME it chooses already carries it, which is
    /// what <c>MusicXmlReader.ReadPart</c> does; honouring both as though they stacked drops
    /// a guitar two octaves.
    /// </remarks>
    public int? TransposeSemitones { get; set; }

    /// <summary>The capo's fret (<c>&lt;staff-details&gt;&lt;capo&gt;</c>, 2026-09-29): the
    /// score's <c>chordDiagrams … capo N</c>, written on a part whose harmonies carry a
    /// <c>&lt;frame&gt;</c> — the frames are the shapes PRESSED above the capo, and this is
    /// what tells a reader so. Null when there is none.</summary>
    public int? Capo { get; set; }

    public XElement ToXml(MusicXmlTab? tab = null, bool first = false)
    {
        var attrs = new XElement("attributes",
            Divisions is { } div ? new XElement("divisions", div) : null);

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
            // The page draws a written 4/4 as C and 2/2 as the cut C, always
            // (GlyphMetrics.GetTimeSigWidth's glyph test); until 2026-09-30 the file said
            // neither and a reader printed the digits (LilySharp-Omr
            // docs/repro/musicxml-exporter-bugs.md #8).
            string beats = TimeBeatsText ?? TimeBeats.Value.ToString();
            string? symbol = (beats, TimeBeatType.Value) switch
            {
                ("4", 4) => "common",
                ("2", 2) => "cut",
                _ => null,
            };
            attrs.Add(new XElement("time",
                symbol != null ? new XAttribute("symbol", symbol) : null,
                new XElement("beats", beats),
                new XElement("beat-type", TimeBeatType.Value)));
        }

        // Schema order: … time*, staves?, … clef*, staff-details*, transpose*, …
        if (tab is { WithNotation: true } && first)
            attrs.Add(new XElement("staves", 2));
        else if (Staves is { } staves)
            attrs.Add(new XElement("staves", staves));

        // A TAB-only part's staff is the TAB staff throughout: its clef is the TAB clef, and a
        // notation clef change is not drawn on it. Beside a notation staff the written clef is
        // staff 1's.
        if (ClefSign != null && tab is not { WithNotation: false })
        {
            attrs.Add(new XElement("clef",
                tab != null ? new XAttribute("number", 1)
                    : ClefNumber is { } clefNumber ? new XAttribute("number", clefNumber) : null,
                new XElement("sign", ClefSign),
                ClefLine.HasValue ? new XElement("line", ClefLine.Value) : null,
                ClefOctaveChange.HasValue
                    ? new XElement("clef-octave-change", ClefOctaveChange.Value)
                    : null));
        }
        if (Staff2Clef is { } lower)
            attrs.Add(new XElement("clef", new XAttribute("number", 2),
                new XElement("sign", lower.Sign),
                lower.Line is { } lowerLine ? new XElement("line", lowerLine) : null,
                lower.OctaveChange is { } lowerOctave ? new XElement("clef-octave-change", lowerOctave) : null));
        if (tab != null && first)
        {
            attrs.Add(tab.Clef());
            attrs.Add(tab.Details(Capo));
        }
        else if (Capo is { } capo && capo > 0 && tab == null)
            attrs.Add(new XElement("staff-details", new XElement("capo", capo)));

        if (TransposeSemitones is { } semis && semis != 0)
        {
            // Whole octaves get <octave-change> and a zero chromatic, which is how every
            // octave-transposing instrument is written; anything else is a plain chromatic
            // shift with the diatonic step it implies.
            int octaves = semis / 12, rest = semis % 12;
            var tr = new XElement("transpose",
                new XElement("diatonic", rest == 0 ? 0 : DiatonicStepsFor(rest)),
                new XElement("chromatic", rest));
            if (octaves != 0)
                tr.Add(new XElement("octave-change", octaves));
            attrs.Add(tr);
        }

        if (MeasureRepeat != null)
        {
            // The start names how many measures each repetition is — 1 for %, 2 for %% — as
            // the element's content (MusicXML 4.0 measure-repeat); the stop has none.
            var mr = new XElement("measure-repeat", new XAttribute("type", MeasureRepeat));
            if (MeasureRepeat == "start")
            {
                mr.Add(new XAttribute("slashes", 1));
                mr.Add(MeasureRepeatBars);
            }
            attrs.Add(new XElement("measure-style", mr));
        }

        // One <measure-style> holds ONE of multiple-rest / measure-repeat / beat-repeat / slash
        // (the schema's choice), so a multi-measure rest gets a measure-style of its own.
        if (MultipleRest is { } bars)
            attrs.Add(new XElement("measure-style", new XElement("multiple-rest", bars)));

        return attrs;
    }

    /// <summary>
    /// The diatonic step count that goes with a chromatic shift of <paramref name="semis"/>
    /// (|semis| &lt; 12) — how many letter names the spelling moves by.
    /// </summary>
    /// <remarks>
    /// MusicXML wants both numbers and they are not the same: a B♭ instrument transposes
    /// −2 chromatic and −1 diatonic. This is the usual reading of each interval within an
    /// octave; Lily# only produces octaves today, so it is here for the shape rather than
    /// for a case in the corpus.
    /// </remarks>
    private static int DiatonicStepsFor(int semis)
    {
        int[] steps = { 0, 0, 1, 2, 2, 3, 3, 4, 5, 5, 6, 6 };
        int mag = steps[System.Math.Abs(semis)];
        return semis < 0 ? -mag : mag;
    }
}

/// <summary>
/// Direction element for dynamics, tempo, and other performance indications.
/// </summary>
internal sealed class MusicXmlDirection
{
    public string? DynamicType { get; set; }
    /// <summary>The metronome's per-minute figure, in <see cref="TempoBeatUnit"/>s.</summary>
    public int? Tempo { get; set; }
    /// <summary>The metronome's beat unit as a note value (4 = quarter) and its dots. Until
    /// session 398 the unit was written as a quarter whatever the source said, so
    /// <c>tempo 2 = 60</c> came out ♩ = 60 — half the speed.</summary>
    public int TempoBeatUnit { get; set; } = 4;
    public int TempoBeatDots { get; set; }
    public string? Placement { get; set; }
    /// <summary>Hairpin: "crescendo" / "diminuendo" / "stop".</summary>
    public string? WedgeType { get; set; }
    /// <summary>Pedal mark: "start" / "stop" / "sostenuto".</summary>
    public string? PedalType { get; set; }
    /// <summary>Ottava line: "down" (8va) / "up" (8vb) / "stop".</summary>
    public string? OctaveShiftType { get; set; }
    /// <summary>The octave line's size: 8 for an ottava, 15 for a quindicesima (2026-09-29;
    /// every line was written as 8 before, a <c>@quindicesima</c> included).</summary>
    public int OctaveShiftSize { get; set; } = 8;
    /// <summary>Free words (<c>una corda</c>, <c>tre corde</c>): MusicXML has no pedal type
    /// for the una corda, so it is the words a score prints.</summary>
    public string? Words { get; set; }
    /// <summary>A rehearsal mark's label (<c>@mark("A")</c> on a note, 2026-09-29): MusicXML's
    /// <c>&lt;rehearsal&gt;</c> direction, which is a boxed label like the page's.</summary>
    public string? Rehearsal { get; set; }

    /// <summary>The rehearsal label's frame (<c>square</c>, <c>none</c>), or null for the
    /// reader's default.</summary>
    public string? RehearsalEnclosure { get; set; }
    /// <summary>
    /// Where the direction stands in its bar, in divisions from the bar's head (2026-09-29).
    /// The measure writes every direction at its head (<see cref="MusicXmlMeasure.ToXml"/>),
    /// so this is what places a wedge, a dynamic, a pedal or an octave line at ITS note:
    /// until now <c>c4 d@cresc</c> opened its wedge at the bar's first beat.
    /// </summary>
    public int Offset { get; set; }

    /// <summary>The staff of a multi-staff part the direction belongs to, or null.</summary>
    public int? Staff { get; set; }

    /// <summary>The wedge's and octave line's <c>number</c>: null (the reader's 1), or 2 on a
    /// grand staff's lower staff, whose hairpins and octave lines run beside the upper staff's
    /// in the one part (2026-09-30; both staves' were number 1 before, so a hairpin under each
    /// hand at once closed the other's).</summary>
    public int? Number { get; set; }

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
                new XElement("wedge", new XAttribute("type", WedgeType),
                    Number is { } wedgeNumber ? new XAttribute("number", wedgeNumber) : null)));

        if (PedalType != null)
            direction.Add(new XElement("direction-type",
                new XElement("pedal", new XAttribute("type", PedalType))));

        if (OctaveShiftType != null)
        {
            var shift = new XElement("octave-shift", new XAttribute("type", OctaveShiftType));
            if (Number is { } shiftNumber)
                shift.Add(new XAttribute("number", shiftNumber));
            shift.Add(new XAttribute("size", OctaveShiftSize));
            direction.Add(new XElement("direction-type", shift));
        }

        if (Words != null)
            direction.Add(new XElement("direction-type", new XElement("words", Words)));

        if (Rehearsal != null)
            direction.Add(new XElement("direction-type", new XElement("rehearsal",
                RehearsalEnclosure != null ? new XAttribute("enclosure", RehearsalEnclosure) : null,
                Rehearsal)));

        XElement? sound = null;
        if (Tempo.HasValue)
        {
            var metronome = new XElement("metronome", new XElement("beat-unit", BeatUnitName(TempoBeatUnit)));
            for (int i = 0; i < TempoBeatDots; i++)
                metronome.Add(new XElement("beat-unit-dot"));
            metronome.Add(new XElement("per-minute", Tempo.Value));
            direction.Add(new XElement("direction-type", metronome));
            // <sound tempo> is in QUARTER notes per minute whatever the metronome's unit.
            var value = new Syntax.TempoValue(null, TempoBeatUnit, TempoBeatDots, Tempo.Value, 0);
            sound = new XElement("sound", new XAttribute("tempo",
                System.Math.Round(value.QuarterBpm!.Value, 2).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        // Schema order: direction-type+, offset?, …, staff?, sound?.
        if (Offset > 0)
            direction.Add(new XElement("offset", Offset));
        if (Staff is { } staff)
            direction.Add(new XElement("staff", staff));
        if (sound != null)
            direction.Add(sound);

        return direction;
    }

    /// <summary>MusicXML's note-type name of a beat unit written as a note value.</summary>
    private static string BeatUnitName(int unit) => unit switch
    {
        1 => "whole",
        2 => "half",
        8 => "eighth",
        16 => "16th",
        32 => "32nd",
        64 => "64th",
        _ => "quarter",
    };
}

/// <summary>
/// Represents a note in MusicXML.
/// </summary>
internal sealed class MusicXmlNote
{
    public bool IsRest { get; set; }
    public bool IsChord { get; set; }
    /// <summary>Drum note: serialize &lt;unpitched&gt; with Step/Octave as the
    /// DISPLAY position instead of &lt;pitch&gt;.</summary>
    public bool IsUnpitched { get; set; }
    /// <summary>A rest whose vertical place comes from a written pitch (<c>a4@rest</c>):
    /// serialize Step/Octave as display-step/display-octave INSIDE the &lt;rest&gt;.</summary>
    /// <remarks>
    /// The same shape as <see cref="IsUnpitched"/> one element over, and MusicXML's own way
    /// of saying what LilyPond says with <c>a4\rest</c> — the pitch places the glyph and
    /// sounds nothing. Without it the exporter wrote a &lt;pitch&gt;: a note where the page
    /// prints a rest.
    /// </remarks>
    public bool RestHasDisplayPitch { get; set; }
    /// <summary>A whole-measure rest (<c>&lt;rest measure="yes"/&gt;</c>): what each bar of a
    /// multi-measure rest holds, so a reader draws the bar-centred rest rather than a rest at
    /// beat one.</summary>
    public bool IsMeasureRest { get; set; }
    /// <summary>False for a note that holds its time and prints nothing
    /// (<c>print-object="no"</c>) — Lily#'s spacer <c>s</c>. MusicXML has no spacer of its
    /// own; until 2026-09-29 a spacer was written as an ordinary <c>&lt;rest/&gt;</c>, so every
    /// other program drew a rest where the page draws nothing.</summary>
    public bool PrintObject { get; set; } = true;
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
    /// <summary>Direct &lt;notations&gt; children (arpeggiate, glissando, …).</summary>
    public List<XElement> ExtraNotations { get; } = new();
    public bool IsGrace { get; set; }
    public bool IsSlash { get; set; }
    public bool TieStart { get; set; }
    public bool TieStop { get; set; }
    public bool SlurStart { get; set; }
    public bool SlurStop { get; set; }

    /// <summary>The slur's <c>number</c>: 1, or 2 on a grand staff's lower staff, whose slurs
    /// run beside the upper staff's in the one part.</summary>
    public int SlurNumber { get; set; } = 1;

    // Legacy property for backward compatibility
    public string? Dynamic { get; set; }

    /// <summary>Sung syllables (verse, text, syllabic form, extender) — one per
    /// verse. Vocal editors (VOCALOID, Synthesizer V, CeVIO, NEUTRINO …)
    /// read these on import.</summary>
    public List<(int Verse, string Text, string Syllabic, bool Extend)> Lyrics { get; } = new();

    /// <summary>Where the note was written — the syntax node's <c>SourceStart</c>, the offset the
    /// page's items carry as <c>SourcePosition</c> — or −1 for a note no written item stands
    /// behind (a pad, a pseudo-entry). It ties an exported note to the page's own reading of the
    /// same note, which is where the beams come from.</summary>
    public int SourcePosition { get; set; } = -1;

    /// <summary>The staff of a multi-staff part this note is on (<c>&lt;staff&gt;</c>), or null
    /// on a one-staff part.</summary>
    public int? Staff { get; set; }

    /// <summary>The note's <c>&lt;beam&gt;</c> elements, level 1 first: <c>begin</c>,
    /// <c>continue</c>, <c>end</c>, <c>forward hook</c> or <c>backward hook</c>.</summary>
    public List<(int Number, string Value)> Beams { get; } = new();

    /// <summary>The string and fret the page's tab staff plays this note on, or null when the
    /// part has no tab staff (or the note is off the fretboard).</summary>
    public (int String, int Fret)? Tab { get; set; }

    /// <param name="staff">The staff to write, over <see cref="Staff"/>.</param>
    /// <param name="tabStaff">The note is on a TAB staff: it carries its
    /// <see cref="Tab"/> string and fret as <c>&lt;technical&gt;</c> (in place of a written
    /// string number's bare <c>&lt;string&gt;</c>).</param>
    /// <param name="tabCopy">The note is the TAB staff's copy of a notation-staff note: its
    /// voice moves to the second staff's range (5–8), and what the notation staff already
    /// carries — lyrics, slurs, articulations, ornaments, other notations — is not repeated.</param>
    public XElement ToXml(int? staff = null, bool tabStaff = false, bool tabCopy = false)
    {
        // Non-note pseudo-entries keep their slot in the note stream.
        if (RawElement != null)
            return RawElement;

        // Multi-voice measure-cursor rewind — not a <note> at all.
        if (IsBackup)
            return new XElement("backup", new XElement("duration", Duration));

        var note = new XElement("note");
        if (!PrintObject)
            note.Add(new XAttribute("print-object", "no"));

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
            note.Add(RestHasDisplayPitch
                ? new XElement("rest",
                    new XElement("display-step", Step),
                    new XElement("display-octave", Octave))
                : IsMeasureRest
                    ? new XElement("rest", new XAttribute("measure", "yes"))
                    : new XElement("rest"));
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

        if (tabCopy)
            note.Add(new XElement("voice", (Voice ?? 1) + 4));
        else if (Voice.HasValue)
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

        // MusicXML order: … notehead, staff, beam, notations, lyric.
        if ((staff ?? Staff) is { } staffNumber)
            note.Add(new XElement("staff", staffNumber));

        foreach (var (number, value) in Beams)
            note.Add(new XElement("beam", new XAttribute("number", number), value));

        var technicals = Technicals;
        if (tabStaff && Tab is { } tab)
        {
            technicals = Technicals.Where(t => t.Name.LocalName != "string").ToList();
            technicals.Add(new XElement("string", tab.String));
            technicals.Add(new XElement("fret", tab.Fret));
        }
        bool slurStart = SlurStart && !tabCopy, slurStop = SlurStop && !tabCopy;
        var articulations = tabCopy ? new List<string>() : Articulations;
        var ornaments = tabCopy ? new List<string>() : Ornaments;
        var extraNotations = tabCopy ? new List<XElement>() : ExtraNotations;

        // Notations (articulations, ornaments, ties, slurs)
        var hasNotations = articulations.Count > 0 || ornaments.Count > 0 ||
                          technicals.Count > 0 || extraNotations.Count > 0 ||
                          TieStart || TieStop || slurStart || slurStop;

        if (hasNotations)
        {
            var notations = new XElement("notations");

            // Tied notations
            if (TieStart)
                notations.Add(new XElement("tied", new XAttribute("type", "start")));
            if (TieStop)
                notations.Add(new XElement("tied", new XAttribute("type", "stop")));

            // Slur notations
            if (slurStart)
                notations.Add(new XElement("slur", new XAttribute("type", "start"), new XAttribute("number", SlurNumber)));
            if (slurStop)
                notations.Add(new XElement("slur", new XAttribute("type", "stop"), new XAttribute("number", SlurNumber)));

            // Articulations
            if (articulations.Count > 0)
            {
                var artics = new XElement("articulations");
                foreach (var a in articulations)
                    artics.Add(new XElement(a));
                notations.Add(artics);
            }

            // Ornaments
            if (ornaments.Count > 0)
            {
                var orns = new XElement("ornaments");
                foreach (var o in ornaments)
                    orns.Add(new XElement(o));
                notations.Add(orns);
            }

            // Technical (guitar/TAB techniques)
            if (technicals.Count > 0)
            {
                var tech = new XElement("technical");
                foreach (var t in technicals)
                    tech.Add(t);
                notations.Add(tech);
            }

            foreach (var extra in extraNotations)
                notations.Add(extra);

            note.Add(notations);
        }

        // <lyric> — after notations per the MusicXML order.
        foreach (var (verse, text, syllabic, extend) in tabCopy ? [] : Lyrics)
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
