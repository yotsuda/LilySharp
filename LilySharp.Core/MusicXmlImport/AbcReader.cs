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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LilySharp.Core.Semantics;

namespace LilySharp.Core.MusicXmlImport;

/// <summary>
/// ABC notation (<c>.abc</c>, ABC 2.1) read into the import model (owner's decision 2026-11-18,
/// <c>docs/abc-import-design.md</c>): the same <see cref="ImportDocument"/> the MusicXML and MIDI
/// readers fill, written by <see cref="LysWriter"/>. The first tune of a file is read; what the
/// reader leaves out goes to the report (design §3).
/// </summary>
internal static class AbcReader
{
    /// <summary>Whether the bytes are ABC text: not a zip, a MIDI file or XML, with a <c>K:</c> line
    /// and a field (<c>X:</c>, <c>T:</c>, …) as the first line that says anything.</summary>
    public static bool IsAbc(byte[] bytes)
    {
        if (bytes.Length < 3 || (bytes[0] == 'P' && bytes[1] == 'K') || MidiReader.IsMidi(bytes))
            return false;
        string text = Decode(bytes);
        string? first = null;
        bool key = false;
        foreach (var raw in SplitLines(text))
        {
            string line = raw.TrimEnd();
            if (line.Length == 0 || line.StartsWith('%'))
                continue;
            first ??= line;
            if (line.StartsWith("K:", StringComparison.Ordinal))
                key = true;
        }
        return key && first != null && first[0] != '<' && FieldLine.IsMatch(first);
    }

    /// <summary>The text of an ABC file: UTF-8 (ABC 2.1's own), its byte-order mark dropped.</summary>
    public static string Decode(byte[] bytes)
    {
        string text = Encoding.UTF8.GetString(bytes);
        return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
    }

    /// <summary>Reads one tune (the first, when the text holds several: <see cref="SplitTunes"/>).</summary>
    public static ImportDocument Read(string text, ImportReport report) => new Reading(report).Run(text);

    /// <summary>One tune of a file: its <c>X:</c> number and first <c>T:</c> as written (null when
    /// absent), and its text — the file header's fields first, so they hold for it as for every tune.</summary>
    public readonly record struct Tune(string? Number, string? Title, string Text);

    /// <summary>The tunes of an ABC file, each from its <c>X:</c> line to the next (ABC 2.1 §2.2: a
    /// tune book). A file with no <c>X:</c> is one tune. The file header — the field lines before the
    /// first tune — goes in front of each tune's own header, which comes after it and so wins.</summary>
    public static List<Tune> SplitTunes(string text)
    {
        var lines = SplitLines(text).ToList();
        var starts = lines.Select((l, i) => (l, i)).Where(x => x.l.StartsWith("X:", StringComparison.Ordinal)).Select(x => x.i).ToList();
        if (starts.Count == 0)
            return [new Tune(null, TitleOf(lines), text)];
        var header = lines.Take(starts[0]).Where(l => FieldLine.IsMatch(l) && !l.StartsWith("T:", StringComparison.Ordinal)).ToList();
        var tunes = new List<Tune>();
        for (int k = 0; k < starts.Count; k++)
        {
            int end = k + 1 < starts.Count ? starts[k + 1] : lines.Count;
            var own = lines.Skip(starts[k]).Take(end - starts[k]).ToList();
            string number = own[0][2..].Trim();
            var body = new List<string> { own[0] };
            body.AddRange(header);
            body.AddRange(own.Skip(1));
            tunes.Add(new Tune(number.Length > 0 ? number : null, TitleOf(own), string.Join("\n", body)));
        }
        return tunes;
    }

    private static string? TitleOf(IEnumerable<string> lines)
        => lines.FirstOrDefault(l => l.StartsWith("T:", StringComparison.Ordinal)) is { } t && t[2..].Trim() is { Length: > 0 } title ? title : null;

    /// <summary>A file name (no extension) for each tune of a book: the <c>X:</c> number, padded to
    /// the book's widest (three at least), and the title made safe for a file name —
    /// <c>001-speed-the-plough</c>. A tune with no usable number takes its place in the book, and a
    /// name already taken a further number.</summary>
    public static List<string> FileNames(IReadOnlyList<Tune> tunes)
    {
        var numbers = new List<int>();
        var seen = new HashSet<int>();
        for (int k = 0; k < tunes.Count; k++)
        {
            int n = int.TryParse(tunes[k].Number, NumberStyles.None, CultureInfo.InvariantCulture, out int x) && x > 0 && seen.Add(x) ? x : -1;
            numbers.Add(n);
        }
        for (int k = 0; k < numbers.Count; k++)
            if (numbers[k] < 0)
            {
                int n = k + 1;
                while (!seen.Add(n)) n++;
                numbers[k] = n;
            }
        int width = Math.Max(3, numbers.Max().ToString(CultureInfo.InvariantCulture).Length);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new List<string>();
        for (int k = 0; k < tunes.Count; k++)
        {
            string name = numbers[k].ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
            string slug = Slug(tunes[k].Title);
            if (slug.Length > 0)
                name += "-" + slug;
            string candidate = name;
            for (int extra = 2; !used.Add(candidate); extra++)
                candidate = name + "-" + extra;
            names.Add(candidate);
        }
        return names;
    }

    /// <summary>A title as part of a file name: letters and digits of any script, lower case, the
    /// rest one hyphen; sixty characters at most.</summary>
    private static string Slug(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "";
        var sb = new StringBuilder();
        foreach (char c in title.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }
        string slug = sb.ToString().Trim('-');
        return slug.Length > 60 ? slug[..60].TrimEnd('-') : slug;
    }

    private static readonly Regex FieldLine = new(@"^[A-Za-z+]:", RegexOptions.CultureInvariant);

    private static IEnumerable<string> SplitLines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    // ---- the reading -------------------------------------------------------

    /// <summary>One pitch of a note event: diatonic step (0 = C), MusicXML octave, sounding alteration.</summary>
    private readonly record struct Pitch(int Step, int Octave, int Alter);

    /// <summary>A note, chord or rest read and not yet written: the broken rhythm after it
    /// (<c>a&gt;b</c>) changes its length, and a tie or a slur's end after it lands on it.</summary>
    private sealed class Event
    {
        public List<Pitch> Pitches { get; } = new();
        public bool Rest, Spacer;
        public Fraction Length;
        public List<string> Marks { get; } = new();
        public List<ImportHarmony> Harmonies { get; } = new();
        public List<ImportGraceNote> Grace { get; } = new();
        public int? Fingering;
        public string? ClefBefore;
        public bool SlurStart, TieAfter;
        public int SlurStops;
        public (int Actual, int Normal)? TupletStart;
        public bool TupletStop;
    }

    private sealed class Voice
    {
        public required ImportPart Part;
        public ImportMeasure Measure = new();
        public Fraction Unit;
        public (int Fifths, string Mode)? Key;
        public string? Clef;
        public Dictionary<(int Step, int Octave), int> BarAccidentals { get; } = new();
        public Event? Held;
        public Fraction BrokenCarry = Fraction.Whole;    // the length factor a broken rhythm left for the next event
        public List<string> PendingMarks { get; } = new();
        public List<ImportHarmony> PendingHarmonies { get; } = new();
        public List<ImportGraceNote> PendingGrace { get; } = new();
        public int? PendingFingering;
        public string? PendingClef;
        public bool PendingSlurStart;
        public int SlurDepth, SlursDropped, SlurDropDepth;
        public (int Actual, int Normal, int Left)? Tuplet;
        public List<Pitch> TiedFrom { get; } = new();    // the pitches the last written event ties on
        public List<ImportNote>? LastHeads;               // the heads of the last written piece
        public ImportMeasure? LastClosed;
        public bool EndingOpen;
        public bool SkipToBar;
        public List<(ImportNote Head, int Bar)> Singable { get; } = new();
        public int LyricStart, Verse;
    }

    private sealed class Reading(ImportReport report)
    {
        private readonly ImportDocument _doc = new();
        private readonly Dictionary<string, Voice> _voices = new(StringComparer.Ordinal);
        private readonly List<Voice> _order = new();
        private readonly HashSet<string> _used = new(StringComparer.Ordinal);
        private readonly HashSet<string> _said = new(StringComparer.Ordinal);
        private Voice? _voice;
        private Fraction _unit = new(1, 8);
        private (int Beats, int BeatType)? _meter;
        private (int Fifths, string Mode)? _key;
        private string? _clef;
        private string? _tempoField;
        private bool _lineContinues;

        private void Once(string message)
        {
            if (_said.Add(message))
                report.Warn(message);
        }

        public ImportDocument Run(string text)
        {
            var lines = SplitLines(text).ToList();
            int tunes = lines.Count(l => l.StartsWith("X:", StringComparison.Ordinal));
            int start = lines.FindIndex(l => l.StartsWith("X:", StringComparison.Ordinal));
            if (start < 0)
                start = 0;

            // The header: up to and with the K: line.
            int at = start;
            bool sawUnit = false, sawMeter = false;
            for (; at < lines.Count; at++)
            {
                string line = StripComment(lines[at]).Trim();
                if (at > start && line.Length == 0 && tunes > 0)
                    break;
                if (!FieldLine.IsMatch(line))
                    continue;
                char field = line[0];
                string value = line[2..].Trim();
                if (at > start && field == 'X')
                    break;
                switch (field)
                {
                    case 'T':
                        if (_doc.Title == null) _doc.Title = value;
                        else _doc.Subtitle ??= value;
                        break;
                    case 'C':
                        _doc.Composer = _doc.Composer == null ? value : _doc.Composer + ", " + value;
                        break;
                    case 'M':
                        _meter = ReadMeter(value);
                        sawMeter = true;
                        break;
                    case 'L':
                        if (ReadUnit(value) is { } unit) { _unit = unit; sawUnit = true; }
                        break;
                    case 'Q':
                        _tempoField = value;
                        break;
                    case 'V':
                        DeclareVoice(value);
                        break;
                    case 'P':
                        Once("the order of the parts (P:) is not read: the music is written as it stands.");
                        break;
                    case 'W':
                        Once("words after the tune (W:) are not read.");
                        break;
                    case 'U':
                    case 'm':
                        Once("user-defined symbols and macros (U:, m:) are not read.");
                        break;
                }
                if (field == 'K')
                {
                    var (key, clef) = ReadKey(value);
                    _key = key;
                    _clef = clef ?? _clef;
                    at++;
                    break;
                }
            }
            if (!sawMeter)
                Once("the tune states no meter (M:): read as 4/4.");
            _meter ??= (4, 4);
            if (!sawUnit)
                _unit = sawMeter && (double)_meter.Value.Beats / _meter.Value.BeatType < 0.75 ? new Fraction(1, 16) : new Fraction(1, 8);
            if (_tempoField != null)
                ReadTempo(_tempoField, out int? bpm, out string? words, header: true);
            foreach (var v in _order)
            {
                v.Unit = _unit;
                v.Key ??= _key;
                v.Clef ??= _clef;
            }

            // The body.
            for (; at < lines.Count; at++)
            {
                string rawLine = lines[at];
                if (tunes > 0 && rawLine.StartsWith("X:", StringComparison.Ordinal))
                    break;
                if (rawLine.StartsWith("%%", StringComparison.Ordinal))
                    continue;
                string line = StripComment(rawLine).TrimEnd();
                if (line.Trim().Length == 0)
                {
                    if (tunes > 0 && rawLine.Trim().Length == 0)
                        break;      // a blank line ends a tune (ABC 2.1 §2.2)
                    continue;
                }
                if (FieldLine.IsMatch(line))
                {
                    BodyField(line[0], line[2..].Trim());
                    continue;
                }
                MusicLine(line);
            }

            foreach (var v in _order)
                Finish(v);
            var parts = _order.Where(v => v.Part.Measures.Count > 0).Select(v => v.Part).ToList();
            if (parts.Count == 0)
                throw new System.IO.InvalidDataException("This ABC tune holds no music.");
            int bars = parts.Max(p => p.Measures.Count);
            foreach (var part in parts.Where(p => p.Measures.Count < bars))
            {
                report.Warn($"the voice {part.Name ?? part.SafeName} has {part.Measures.Count} bars of the tune's {bars}: whole-bar rests fill the rest.");
                while (part.Measures.Count < bars)
                {
                    var rest = new ImportMeasure();
                    rest.Voice(1).Add(new ImportNote { IsRest = true, IsMeasureRest = true, NoteValue = 1 });
                    part.Measures.Add(rest);
                }
            }
            _doc.Parts.AddRange(parts);
            return _doc;
        }

        // ---- fields ---------------------------------------------------------

        private Voice Current()
        {
            if (_voice != null)
                return _voice;
            _voice = _order.FirstOrDefault() ?? NewVoice("1", null);
            return _voice;
        }

        private Voice NewVoice(string id, string? name)
        {
            bool alone = _order.Count == 0 && id == "1" && name == null;
            var part = new ImportPart
            {
                Id = "V" + (_order.Count + 1),
                Name = alone ? null : name ?? id,
                Label = name,
            };
            part.SafeName = MusicXmlReader.SafeIdentifier(alone ? "melody" : name ?? (char.IsLetter(id[0]) ? id : "voice" + id), _order.Count, _used);
            var v = new Voice { Part = part, Unit = _unit, Key = _key, Clef = _clef };
            _voices[id] = v;
            _order.Add(v);
            return v;
        }

        /// <summary>A <c>V:</c> field: its id, then <c>name="…"</c> / <c>nm="…"</c>, <c>clef=…</c>.</summary>
        private Voice DeclareVoice(string value)
        {
            var m = Regex.Match(value, @"^\s*(\S+)(.*)$");
            string id = m.Success ? m.Groups[1].Value : "1";
            string rest = m.Success ? m.Groups[2].Value : "";
            if (!_voices.TryGetValue(id, out var v))
            {
                var name = Regex.Match(rest, @"(?:name|nm)\s*=\s*""([^""]*)""");
                v = NewVoice(id, name.Success && name.Groups[1].Value.Length > 0 ? name.Groups[1].Value : null);
            }
            if (ClefIn(rest) is { } clef)
                SetClef(v, clef);
            return v;
        }

        private void BodyField(char field, string value)
        {
            switch (field)
            {
                case 'V':
                    if (_voice != null)
                        Flush(_voice);
                    _voice = DeclareVoice(value);
                    _lineContinues = false;
                    break;
                case 'w':
                    Lyrics(Current(), value);
                    break;
                case '+':
                    // A field's continuation: only the words' is read.
                    break;
                default:
                    InlineField(Current(), field, value);
                    break;
            }
        }

        private void InlineField(Voice v, char field, string value)
        {
            switch (field)
            {
                case 'K':
                {
                    Flush(v);
                    var (key, clef) = ReadKey(value);
                    if (key != null && key != v.Key)
                    {
                        v.Key = key;
                        if (v.Measure.HasAnyItems)
                            Once("a key that changes inside a bar is written at the next bar.");
                        PendingKey(v, key.Value);
                    }
                    if (clef != null)
                        SetClef(v, clef);
                    break;
                }
                case 'M':
                {
                    Flush(v);
                    var meter = ReadMeter(value);
                    if (meter != null)
                    {
                        if (v.Measure.HasAnyItems)
                            Once("a meter that changes inside a bar is written at the next bar.");
                        _meter = meter;
                        v.Measure.Time = new ImportTime(meter.Value.Beats, meter.Value.BeatType);
                        _pendingTime[v] = v.Measure.HasAnyItems;
                    }
                    break;
                }
                case 'L':
                    Flush(v);
                    if (ReadUnit(value) is { } unit)
                        v.Unit = unit;
                    break;
                case 'Q':
                    ReadTempo(value, out int? bpm, out string? words, header: false);
                    if (v == _order[0] && bpm != null)
                    {
                        v.Measure.Tempo = bpm;
                        v.Measure.TempoWords = words;
                    }
                    break;
                case 'V':
                    break;
                case 'T':
                    Once("a title inside the tune (T:) is not read.");
                    break;
                case 'P':
                    Once("the order of the parts (P:) is not read: the music is written as it stands.");
                    break;
                case 'W':
                    Once("words after the tune (W:) are not read.");
                    break;
                case 's':
                    Once("symbol lines (s:) are not read.");
                    break;
                case 'U':
                case 'm':
                    Once("user-defined symbols and macros (U:, m:) are not read.");
                    break;
            }
        }

        // A meter set in a bar that already holds notes moves to the next bar's head (Close).
        private readonly Dictionary<Voice, bool> _pendingTime = new();
        private readonly Dictionary<Voice, (int Fifths, string Mode)> _pendingKey = new();

        private void PendingKey(Voice v, (int Fifths, string Mode) key)
        {
            if (!v.Measure.HasAnyItems)
                v.Measure.Key = new ImportKey(key.Fifths, key.Mode);
            else
                _pendingKey[v] = key;
        }

        private void SetClef(Voice v, string clef)
        {
            if (v.Part.Measures.Count == 0 && !v.Measure.HasAnyItems)
                v.Clef = clef;
            else if (!v.Measure.HasAnyItems)
                v.Measure.Clef = clef;
            else
                v.PendingClef = clef;
        }

        private static string? ClefIn(string text)
        {
            var m = Regex.Match(text, @"(?:^|\s)(?:clef\s*=\s*)?(treble|bass|alto|tenor|perc|drum)(-8|\+8)?(?=\s|$)", RegexOptions.IgnoreCase);
            if (!m.Success)
                return null;
            string name = m.Groups[1].Value.ToLowerInvariant();
            if (name is "perc" or "drum")
                return "percussion";
            return name + (m.Groups[2].Value == "-8" ? "_8" : m.Groups[2].Value == "+8" ? "^8" : "");
        }

        private (int Beats, int BeatType)? ReadMeter(string value)
        {
            value = value.Trim();
            if (value == "C") return (4, 4);
            if (value == "C|") return (2, 2);
            if (value.Length == 0 || value.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                Once("a free meter (M:none) is read as 4/4.");
                return (4, 4);
            }
            var m = Regex.Match(value, @"^\(?([\d+]+)\)?\s*/\s*(\d+)");
            if (!m.Success)
            {
                Once($"the meter '{value}' is not read: 4/4 is used.");
                return (4, 4);
            }
            var counts = m.Groups[1].Value.Split('+', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
            if (counts.Count > 1)
                Once($"the meter {value} is written as {counts.Sum()}/{m.Groups[2].Value}.");
            return (counts.Sum(), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
        }

        private static Fraction? ReadUnit(string value)
        {
            var m = Regex.Match(value.Trim(), @"^(\d+)\s*/\s*(\d+)");
            if (m.Success && int.Parse(m.Groups[1].Value) > 0 && int.Parse(m.Groups[2].Value) > 0)
                return new Fraction(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
            return value.Trim() == "1" ? Fraction.Whole : null;
        }

        /// <summary>A <c>Q:</c> field: <c>1/4=120</c>, <c>3/8=60</c>, <c>120</c> (in L), with words in quotes.</summary>
        private void ReadTempo(string value, out int? bpm, out string? words, bool header)
        {
            bpm = null;
            var quoted = Regex.Matches(value, "\"([^\"]*)\"");
            words = quoted.Count > 0 ? string.Join(" ", quoted.Select(q => q.Groups[1].Value)).Trim() : null;
            if (words?.Length == 0) words = null;
            string rest = Regex.Replace(value, "\"[^\"]*\"", " ").Trim();
            var beat = Regex.Match(rest, @"((?:\d+/\d+\s*)+)=\s*(\d+)");
            Fraction? length = null;
            int count = 0;
            if (beat.Success)
            {
                length = Fraction.Zero;
                foreach (Match part in Regex.Matches(beat.Groups[1].Value, @"(\d+)/(\d+)"))
                    length += new Fraction(int.Parse(part.Groups[1].Value), int.Parse(part.Groups[2].Value));
                count = int.Parse(beat.Groups[2].Value);
            }
            else if (Regex.Match(rest, @"^(?:C\s*=\s*)?(\d+)$") is { Success: true } plain)
            {
                length = _voice?.Unit ?? _unit;
                count = int.Parse(plain.Groups[1].Value);
            }
            if (length is { } l && count > 0)
                bpm = (int)Math.Round(count * (double)l.Numerator / l.Denominator * 4);
            if (header)
            {
                _doc.Tempo = bpm;
                if (bpm != null) _doc.TempoWords = words;
                else _doc.TempoWordsAlone = words;
            }
        }

        private static readonly Dictionary<char, int> NaturalFifths = new() { ['C'] = 0, ['D'] = 2, ['E'] = 4, ['F'] = -1, ['G'] = 1, ['A'] = 3, ['B'] = 5 };

        /// <summary>A <c>K:</c> field: tonic and mode (or <c>none</c>), and a clef in it.</summary>
        private ((int Fifths, string Mode)? Key, string? Clef) ReadKey(string value)
        {
            value = value.Trim();
            string? clef = ClefIn(value);
            if (value.StartsWith("none", StringComparison.OrdinalIgnoreCase))
                return ((0, ImportKey.NoneMode), clef);
            if (value.StartsWith("HP", StringComparison.Ordinal) || value.StartsWith("Hp", StringComparison.Ordinal))
            {
                Once("a bagpipe key (K:HP) is read as no key.");
                return ((0, ImportKey.NoneMode), clef);
            }
            var m = Regex.Match(value, @"^([A-G])([#b]?)\s*([A-Za-z]*)");
            if (!m.Success)
                return (clef != null || value.Length == 0 ? null : (0, "major"), clef);
            int fifths = NaturalFifths[m.Groups[1].Value[0]] + (m.Groups[2].Value == "#" ? 7 : m.Groups[2].Value == "b" ? -7 : 0);
            string word = m.Groups[3].Value.ToLowerInvariant();
            if (word.StartsWith("clef") || word is "treble" or "bass" or "alto" or "tenor" or "perc" or "exp")
                word = "";
            string mode = word.Length == 0 || word.StartsWith("maj") || word.StartsWith("ion") ? "major"
                : word == "m" || word.StartsWith("min") ? "minor"
                : word.StartsWith("aeo") ? "aeolian"
                : word.StartsWith("dor") ? "dorian"
                : word.StartsWith("phr") ? "phrygian"
                : word.StartsWith("lyd") ? "lydian"
                : word.StartsWith("mix") ? "mixolydian"
                : word.StartsWith("loc") ? "locrian"
                : "major";
            int offset = mode switch { "lydian" => 1, "mixolydian" => -1, "dorian" => -2, "minor" or "aeolian" => -3, "phrygian" => -4, "locrian" => -5, _ => 0 };
            if (Regex.IsMatch(value[m.Length..], @"(?:^|\s)(?:\^|_|=)[A-Ga-g]"))
                Once($"the accidentals added to the key ({value}) are not read: write them on the notes.");
            return ((fifths + offset, mode), clef);
        }

        // ---- music ------------------------------------------------------------

        private void MusicLine(string line)
        {
            var v = Current();
            bool continues = line.EndsWith('\\');
            if (continues)
                line = line[..^1];
            if (!_lineContinues)
            {
                v.LyricStart = v.Singable.Count;
                v.Verse = 0;
            }
            _lineContinues = continues;

            int i = 0;
            while (i < line.Length)
            {
                v = Current();
                char c = line[i];
                if (v.SkipToBar && c != '|' && c != ':')
                {
                    i++;
                    continue;
                }
                if (char.IsWhiteSpace(c) || c is '`' or '$' or 'y')
                {
                    i++;
                    continue;
                }
                switch (c)
                {
                    case '"':
                    {
                        int end = line.IndexOf('"', i + 1);
                        if (end < 0) end = line.Length;
                        Quoted(v, line[(i + 1)..end]);
                        i = end + 1;
                        continue;
                    }
                    case '!':
                    case '+':
                    {
                        int end = line.IndexOf(c, i + 1);
                        if (end < 0)
                        {
                            i++;    // an old line break (!) or a lone sign
                            continue;
                        }
                        Decoration(v, line[(i + 1)..end]);
                        i = end + 1;
                        continue;
                    }
                    case '{':
                    {
                        int end = line.IndexOf('}', i + 1);
                        if (end < 0) end = line.Length;
                        Grace(v, line[(i + 1)..end]);
                        i = end + 1;
                        continue;
                    }
                    case '(':
                        if (i + 1 < line.Length && char.IsDigit(line[i + 1]))
                        {
                            i = TupletSpec(v, line, i + 1);
                            continue;
                        }
                        if (v.SlurDepth > 0)
                        {
                            v.SlursDropped++;
                            v.SlurDropDepth++;
                            Once("a slur inside a slur is left out: Lily# draws one at a time.");
                        }
                        else
                            v.PendingSlurStart = true;
                        v.SlurDepth++;
                        i++;
                        continue;
                    case ')':
                        if (v.SlurDepth > 0)
                        {
                            v.SlurDepth--;
                            if (v.SlurDropDepth > 0)
                                v.SlurDropDepth--;
                            else if (v.Held != null)
                                v.Held.SlurStops++;
                            else if (v.LastHeads is { Count: > 0 } heads)
                                heads[0].SlurStop = true;
                        }
                        i++;
                        continue;
                    case '-':
                        if (v.Held != null)
                            v.Held.TieAfter = true;
                        i++;
                        continue;
                    case '>':
                    case '<':
                    {
                        int n = 0;
                        while (i + n < line.Length && line[i + n] == c) n++;
                        Broken(v, c, Math.Min(n, 3));
                        i += n;
                        continue;
                    }
                    case '[':
                        if (i + 2 < line.Length && char.IsLetter(line[i + 1]) && line[i + 2] == ':')
                        {
                            int end = line.IndexOf(']', i);
                            if (end < 0) end = line.Length;
                            char field = line[i + 1];
                            string value = line[(i + 3)..end].Trim();
                            if (field == 'V')
                            {
                                Flush(v);
                                _voice = DeclareVoice(value);
                            }
                            else
                                InlineField(v, field, value);
                            i = end + 1;
                            continue;
                        }
                        if (i + 1 < line.Length && char.IsDigit(line[i + 1]))
                        {
                            i = Ending(v, line, i + 1);
                            continue;
                        }
                        if (i + 1 < line.Length && line[i + 1] == '|')
                        {
                            i = BarLine(v, line, i);
                            continue;
                        }
                        i = Chord(v, line, i + 1);
                        continue;
                    case '|':
                    case ':':
                        i = BarLine(v, line, i);
                        continue;
                    case '.':
                        if (i + 1 < line.Length && line[i + 1] == '|')
                        {
                            i = BarLine(v, line, i + 1);
                            continue;
                        }
                        v.PendingMarks.Add("staccato");
                        i++;
                        continue;
                    case '&':
                        Once("a voice overlay (&) is left out: write a second voice with V:.");
                        v.SkipToBar = true;
                        i++;
                        continue;
                    case '\\':
                        i++;
                        continue;
                }
                if (Shorthand(c) is { } mark)
                {
                    if (mark.Length > 0)
                        v.PendingMarks.Add(mark);
                    else
                        Once($"the decoration '{c}' is left out.");
                    i++;
                    continue;
                }
                if (c is '^' or '_' or '=' || (c >= 'A' && c <= 'G') || (c >= 'a' && c <= 'g'))
                {
                    i = Note(v, line, i);
                    continue;
                }
                if (c is 'z' or 'x')
                {
                    int j = i + 1;
                    var length = ReadLength(line, ref j);
                    var e = Begin(v);
                    e.Rest = true;
                    e.Spacer = c == 'x';
                    e.Length = v.Unit * length * Take(v);
                    Hold(v, e);
                    i = j;
                    continue;
                }
                if (c is 'Z' or 'X')
                {
                    int j = i + 1;
                    int count = 0;
                    while (j < line.Length && char.IsDigit(line[j])) count = count * 10 + (line[j++] - '0');
                    MeasureRests(v, Math.Max(1, count));
                    i = j;
                    continue;
                }
                Once($"the sign '{c}' is not read.");
                i++;
            }
            // The line's last note is written now, so the words under this line find it (a broken
            // rhythm does not reach over a line's end).
            Flush(Current());
        }

        private static string? Shorthand(char c) => c switch
        {
            '~' => "turn",
            'H' => "fermata",
            'L' => "accent",
            'M' => "mordent",
            'P' => "prall",
            'T' => "trill",
            'u' => "upBow",
            'v' => "downBow",
            'O' or 'S' => "",
            _ => null,
        };

        /// <summary>A <c>!name!</c> decoration.</summary>
        private void Decoration(Voice v, string name)
        {
            string? mark = name switch
            {
                "trill" => "trill",
                "lowermordent" or "mordent" => "mordent",
                "uppermordent" or "pralltriller" => "prall",
                "turn" or "roll" => "turn",
                "invertedturn" => "reverseTurn",
                "fermata" or "invertedfermata" => "fermata",
                "accent" or ">" or "emphasis" => "accent",
                "tenuto" => "tenuto",
                "staccato" => "staccato",
                "wedge" => "staccatissimo",
                "marcato" or "^" => "marcato",
                "upbow" => "upBow",
                "downbow" => "downBow",
                "breath" => "breath",
                "arpeggio" => "arpeggio",
                "crescendo(" or "<(" => "cresc",
                "crescendo)" or "<)" => "!cresc",
                "diminuendo(" or ">(" => "decresc",
                "diminuendo)" or ">)" => "!decresc",
                _ => null,
            };
            if (mark == null && LysWriter.IsDynamic(name) && name != "n")
                mark = name;
            if (mark != null)
            {
                v.PendingMarks.Add(mark);
                return;
            }
            if (name.Length == 1 && name[0] is >= '0' and <= '5')
            {
                v.PendingFingering = name[0] - '0';
                return;
            }
            Once($"the decoration !{name}! is left out.");
        }

        /// <summary>A quoted string: a chord name, or text placed by its first sign.</summary>
        private void Quoted(Voice v, string text)
        {
            if (text.Length == 0)
                return;
            if (text[0] is '^' or '_' or '<' or '>' or '@')
            {
                string words = text[1..].Trim();
                if (words.Length > 0)
                    v.PendingMarks.Add("text(\"" + LysWriter.EscapeString(words) + "\")" + (text[0] == '_' ? "" : ".up"));
                return;
            }
            if (ChordName(text) is { } harmony)
            {
                v.PendingHarmonies.Add(harmony);
                return;
            }
            Once($"the chord name \"{text}\" is not one Lily# reads: it is written as text.");
            v.PendingMarks.Add("text(\"" + LysWriter.EscapeString(text) + "\").up");
        }

        private static readonly Dictionary<string, string> Kinds = new(StringComparer.Ordinal)
        {
            [""] = "major", ["M"] = "major", ["maj"] = "major",
            ["m"] = "minor", ["min"] = "minor", ["mi"] = "minor", ["-"] = "minor",
            ["7"] = "dominant", ["dom7"] = "dominant",
            ["m7"] = "minor-seventh", ["min7"] = "minor-seventh", ["mi7"] = "minor-seventh", ["-7"] = "minor-seventh",
            ["maj7"] = "major-seventh", ["M7"] = "major-seventh", ["ma7"] = "major-seventh", ["Maj7"] = "major-seventh", ["Δ7"] = "major-seventh", ["Δ"] = "major-seventh", ["j7"] = "major-seventh",
            ["dim"] = "diminished", ["o"] = "diminished", ["°"] = "diminished",
            ["dim7"] = "diminished-seventh", ["o7"] = "diminished-seventh", ["°7"] = "diminished-seventh",
            ["aug"] = "augmented", ["+"] = "augmented", ["+5"] = "augmented",
            ["sus4"] = "suspended-fourth", ["sus"] = "suspended-fourth", ["sus2"] = "suspended-second",
            ["6"] = "major-sixth", ["m6"] = "minor-sixth", ["min6"] = "minor-sixth",
            ["9"] = "dominant-ninth", ["maj9"] = "major-ninth", ["M9"] = "major-ninth", ["m9"] = "minor-ninth", ["min9"] = "minor-ninth",
            ["m7b5"] = "half-diminished", ["m7-5"] = "half-diminished", ["ø"] = "half-diminished", ["ø7"] = "half-diminished",
            ["aug7"] = "augmented-seventh", ["+7"] = "augmented-seventh", ["7+"] = "augmented-seventh", ["7#5"] = "augmented-seventh",
            ["11"] = "dominant-11th", ["m11"] = "minor-11th", ["13"] = "dominant-13th", ["maj13"] = "major-13th", ["m13"] = "minor-13th",
            ["5"] = "power", ["mmaj7"] = "major-minor", ["mM7"] = "major-minor", ["m(maj7)"] = "major-minor",
        };

        private static readonly (string Text, ImportDegree Degree)[] Alterations =
        [
            ("b9", new(9, -1, "add")), ("#9", new(9, 1, "add")), ("b13", new(13, -1, "add")), ("#11", new(11, 1, "add")),
            ("add9", new(9, 0, "add")), ("b5", new(5, -1, "alter")), ("#5", new(5, 1, "alter")),
        ];

        /// <summary>A chord name (<c>Am7</c>, <c>G/B</c>, <c>F#m7b5</c>, <c>C7b9</c>) as a harmony, or null.</summary>
        private static ImportHarmony? ChordName(string text)
        {
            var m = Regex.Match(text.Trim(), @"^([A-G])([#b♯♭]?)([^/]*)(?:/([A-G])([#b♯♭]?))?$");
            if (!m.Success)
                return null;
            static int Alter(string s) => s is "#" or "♯" ? 1 : s is "b" or "♭" ? -1 : 0;
            var h = new ImportHarmony { RootStep = "CDEFGAB".IndexOf(m.Groups[1].Value[0]), RootAlter = Alter(m.Groups[2].Value) };
            string quality = m.Groups[3].Value.Trim().Replace("(", "").Replace(")", "");
            if (!Kinds.TryGetValue(quality, out var kind))
            {
                // A known kind with alterations after it: C7b9, Cm7#5, Cadd9.
                var degrees = new List<ImportDegree>();
                string q = quality;
                bool found = true;
                while (found && !Kinds.ContainsKey(q))
                {
                    found = false;
                    foreach (var (alt, degree) in Alterations)
                        if (q.EndsWith(alt, StringComparison.Ordinal))
                        {
                            degrees.Insert(0, degree);
                            q = q[..^alt.Length];
                            found = true;
                            break;
                        }
                }
                if (!Kinds.TryGetValue(q, out kind))
                {
                    h.Kind = "other";
                    h.KindText = quality;
                }
                else
                {
                    h.Kind = kind;
                    h.Degrees.AddRange(degrees);
                }
            }
            else
                h.Kind = kind;
            if (m.Groups[4].Success && m.Groups[4].Value.Length > 0)
            {
                h.BassStep = "CDEFGAB".IndexOf(m.Groups[4].Value[0]);
                h.BassAlter = Alter(m.Groups[5].Value);
            }
            return h;
        }

        private static readonly int[] SharpOrder = [3, 0, 4, 1, 5, 2, 6];    // F C G D A E B
        private static readonly int[] FlatOrder = [6, 2, 5, 1, 4, 0, 3];     // B E A D G C F

        private static int KeyAlter((int Fifths, string Mode)? key, int step)
        {
            if (key is not { } k || k.Mode == ImportKey.NoneMode)
                return 0;
            if (k.Fifths > 0)
                return Array.IndexOf(SharpOrder, step) < k.Fifths ? 1 : 0;
            if (k.Fifths < 0)
                return Array.IndexOf(FlatOrder, step) < -k.Fifths ? -1 : 0;
            return 0;
        }

        /// <summary>A pitch at <paramref name="i"/>: accidental, letter, octave marks. Its sounding
        /// alteration: the accidental (which then holds for the bar), else one already in the bar,
        /// else the tie it continues, else the key's.</summary>
        private Pitch ReadPitch(Voice v, string s, ref int i, bool remember = true)
        {
            int? accidental = null;
            if (s[i] == '^')
            {
                accidental = 1;
                if (++i < s.Length && s[i] == '^') { accidental = 2; i++; }
            }
            else if (s[i] == '_')
            {
                accidental = -1;
                if (++i < s.Length && s[i] == '_') { accidental = -2; i++; }
            }
            else if (s[i] == '=')
            {
                accidental = 0;
                i++;
            }
            if (i < s.Length && s[i] == '/')
            {
                Once("quarter-tone accidentals (^/ _/) are read as half-tones.");
                i++;
            }
            if (i >= s.Length || !"ABCDEFGabcdefg".Contains(s[i]))
                return new Pitch(0, 4, 0);
            char letter = s[i++];
            int step = "CDEFGAB".IndexOf(char.ToUpperInvariant(letter));
            int octave = char.IsUpper(letter) ? 4 : 5;
            while (i < s.Length && (s[i] == '\'' || s[i] == ','))
                octave += s[i++] == '\'' ? 1 : -1;
            int alter;
            if (accidental is { } a)
            {
                alter = a;
                if (remember)
                    v.BarAccidentals[(step, octave)] = a;
            }
            else if (v.BarAccidentals.TryGetValue((step, octave), out int held))
                alter = held;
            else if (v.TiedFrom.FindIndex(p => p.Step == step && p.Octave == octave) is int tied and >= 0)
                alter = v.TiedFrom[tied].Alter;
            else
                alter = KeyAlter(v.Key, step);
            return new Pitch(step, octave, alter);
        }

        /// <summary>A length multiplier: <c>2</c>, <c>3/2</c>, <c>/</c>, <c>//</c>, <c>/4</c>.</summary>
        private static Fraction ReadLength(string s, ref int i)
        {
            int num = 0;
            bool any = false;
            while (i < s.Length && char.IsDigit(s[i])) { num = num * 10 + (s[i++] - '0'); any = true; }
            if (!any) num = 1;
            int den = 1;
            while (i < s.Length && s[i] == '/')
            {
                i++;
                int d = 0;
                bool digits = false;
                while (i < s.Length && char.IsDigit(s[i])) { d = d * 10 + (s[i++] - '0'); digits = true; }
                den *= digits && d > 0 ? d : 2;
            }
            return new Fraction(Math.Max(num, 1), den);
        }

        private int Note(Voice v, string s, int i)
        {
            Flush(v);
            var pitch = ReadPitch(v, s, ref i);
            var length = ReadLength(s, ref i);
            var e = Begin(v);
            e.Pitches.Add(pitch);
            e.Length = v.Unit * length * Take(v);
            Hold(v, e);
            return i;
        }

        /// <summary><c>[CEG]2</c>: the first note's length times the length after the bracket.</summary>
        private int Chord(Voice v, string s, int i)
        {
            Flush(v);
            var e = Begin(v);
            Fraction? first = null;
            while (i < s.Length && s[i] != ']')
            {
                char c = s[i];
                if (c is '^' or '_' or '=' || (c >= 'A' && c <= 'G') || (c >= 'a' && c <= 'g'))
                {
                    var pitch = ReadPitch(v, s, ref i);
                    var length = ReadLength(s, ref i);
                    first ??= length;
                    if (!e.Pitches.Contains(pitch))
                        e.Pitches.Add(pitch);
                    continue;
                }
                if (c == '-')
                    e.TieAfter = true;
                else if (c == '"')
                {
                    int end = s.IndexOf('"', i + 1);
                    if (end < 0) end = s.Length - 1;
                    i = end;
                }
                else if (c == '!')
                {
                    int end = s.IndexOf('!', i + 1);
                    if (end > 0)
                    {
                        Decoration(v, s[(i + 1)..end]);
                        e.Marks.AddRange(v.PendingMarks);
                        v.PendingMarks.Clear();
                        i = end;
                    }
                }
                else if (Shorthand(c) is { Length: > 0 } mark)
                    e.Marks.Add(mark);
                else if (c == '.')
                    e.Marks.Add("staccato");
                i++;
            }
            if (i < s.Length) i++;      // the ]
            var outer = ReadLength(s, ref i);
            while (i < s.Length && s[i] == '-') { e.TieAfter = true; i++; }
            if (e.Pitches.Count == 0)
            {
                e.Rest = true;
                Once("an empty chord [] is read as a rest.");
            }
            e.Pitches.Sort((a, b) => (a.Octave * 7 + a.Step).CompareTo(b.Octave * 7 + b.Step));
            e.Length = v.Unit * (first ?? Fraction.Whole) * outer * Take(v);
            Hold(v, e);
            return i;
        }

        private void Grace(Voice v, string inside)
        {
            bool slash = inside.StartsWith('/');
            int i = slash ? 1 : 0;
            var notes = new List<(Pitch Pitch, Fraction Length)>();
            while (i < inside.Length)
            {
                char c = inside[i];
                if (c is '^' or '_' or '=' || (c >= 'A' && c <= 'G') || (c >= 'a' && c <= 'g'))
                {
                    var pitch = ReadPitch(v, inside, ref i, remember: false);
                    notes.Add((pitch, ReadLength(inside, ref i)));
                    continue;
                }
                i++;
            }
            foreach (var (pitch, length) in notes)
            {
                // A grace note is a value, not a time: L's units, an eighth when none fits.
                var values = Values(v.Unit * length);
                var (value, dots) = values.Count == 1 ? values[0] : (notes.Count > 1 ? 16 : 8, 0);
                v.PendingGrace.Add(new ImportGraceNote { Step = pitch.Step, Alter = pitch.Alter, Octave = pitch.Octave, NoteValue = value, Dots = dots, Slash = slash && notes.Count == 1 });
            }
            if (slash && notes.Count > 1)
                Once("a slashed grace group of several notes is written without the slash.");
        }

        /// <summary><c>(p:q:r</c>: p notes in the time of q, for the next r notes.</summary>
        private int TupletSpec(Voice v, string s, int i)
        {
            Flush(v);
            int Read(ref int j)
            {
                int n = 0;
                while (j < s.Length && char.IsDigit(s[j])) n = n * 10 + (s[j++] - '0');
                return n;
            }
            int p = Read(ref i), q = 0, r = 0;
            if (i < s.Length && s[i] == ':')
            {
                i++;
                q = Read(ref i);
                if (i < s.Length && s[i] == ':')
                {
                    i++;
                    r = Read(ref i);
                }
            }
            bool compound = _meter is { } m && m.Beats % 3 == 0 && m.Beats > 3;
            if (q == 0)
                q = p switch { 2 => 3, 3 => 2, 4 => 3, 6 => 2, 8 => 3, _ => compound ? 3 : 2 };
            if (r == 0)
                r = p;
            if (p > 1)
                v.Tuplet = (p, q, r);
            return i;
        }

        private void Broken(Voice v, char sign, int count)
        {
            if (v.Held == null)
                return;
            int den = 1 << count;                          // 2, 4, 8
            var shorter = new Fraction(1, den);            // 1/2, 1/4, 1/8
            var longer = new Fraction(2 * den - 1, den);   // 3/2, 7/4, 15/8
            v.Held.Length *= sign == '>' ? longer : shorter;
            v.BrokenCarry = sign == '>' ? shorter : longer;
        }

        /// <summary>The length factor a broken rhythm left for this event (and clears it).</summary>
        private static Fraction Take(Voice v)
        {
            var f = v.BrokenCarry;
            v.BrokenCarry = Fraction.Whole;
            return f;
        }

        /// <summary>A new event, with what was read before it (marks, chord names, grace notes, a slur, a tuplet).</summary>
        private Event Begin(Voice v)
        {
            Flush(v);
            var e = new Event { SlurStart = v.PendingSlurStart, Fingering = v.PendingFingering, ClefBefore = v.PendingClef };
            e.Marks.AddRange(v.PendingMarks);
            e.Harmonies.AddRange(v.PendingHarmonies);
            e.Grace.AddRange(v.PendingGrace);
            v.PendingMarks.Clear();
            v.PendingHarmonies.Clear();
            v.PendingGrace.Clear();
            v.PendingSlurStart = false;
            v.PendingFingering = null;
            v.PendingClef = null;
            if (v.Tuplet is { } t)
            {
                if (!_tupletOpen.Contains(v))
                {
                    e.TupletStart = (t.Actual, t.Normal);
                    _tupletOpen.Add(v);
                }
                int left = t.Left - 1;
                if (left <= 0)
                {
                    e.TupletStop = true;
                    v.Tuplet = null;
                    _tupletOpen.Remove(v);
                }
                else
                    v.Tuplet = (t.Actual, t.Normal, left);
            }
            return e;
        }

        private readonly HashSet<Voice> _tupletOpen = new();

        private static void Hold(Voice v, Event e) => v.Held = e;

        /// <summary>Writes the held event into the bar: its length as written values tied together.</summary>
        private void Flush(Voice v)
        {
            if (v.Held is not { } e)
                return;
            v.Held = null;
            var items = v.Measure.Voice(1);
            items.AddRange(e.Harmonies);
            var values = Values(e.Length);
            if (values.Count == 0)
            {
                Once("a note shorter than a 128th is left out.");
                values = [(128, 0)];
            }
            var previousTies = v.TiedFrom.ToList();
            List<ImportNote>? firstHeads = null;
            for (int k = 0; k < values.Count; k++)
            {
                var (value, dots) = values[k];
                var heads = new List<ImportNote>();
                if (e.Rest)
                    heads.Add(new ImportNote { IsRest = true, IsSpacer = e.Spacer, NoteValue = value, Dots = dots });
                else
                    foreach (var p in e.Pitches)
                        heads.Add(new ImportNote
                        {
                            Step = p.Step, Alter = p.Alter, Octave = p.Octave, NoteValue = value, Dots = dots,
                            ChordWithPrev = heads.Count > 0,
                            TieStart = k < values.Count - 1 || (e.TieAfter),
                            TieStop = k > 0 || previousTies.Any(t => t.Step == p.Step && t.Octave == p.Octave),
                        });
                if (k == 0)
                {
                    firstHeads = heads;
                    var head = heads[0];
                    head.Articulations.AddRange(e.Marks);
                    head.LeadingGrace.AddRange(e.Grace);
                    head.SlurStart = e.SlurStart;
                    head.Fingering = e.Fingering;
                    head.ClefBefore = e.ClefBefore;
                    head.TupletStart = e.TupletStart;
                }
                items.AddRange(heads);
                v.LastHeads = heads;
            }
            if (e.TupletStop)
                v.LastHeads![0].TupletStop = true;
            if (e.SlurStops > 0)
                v.LastHeads![0].SlurStop = true;
            v.TiedFrom.Clear();
            if (e.TieAfter && !e.Rest)
                v.TiedFrom.AddRange(e.Pitches);
            // Words fall on every note, a tied one too ("tied, slurred or beamed notes are treated as
            // separate notes", ABC 2.1 §5.1), and on no rest or grace note.
            if (!e.Rest)
                v.Singable.Add((firstHeads![0], v.Part.Measures.Count));
        }

        /// <summary>The written values that make up a length, longest first, to be tied.</summary>
        private static List<(int Value, int Dots)> Values(Fraction length)
        {
            var result = new List<(int, int)>();
            var left = length;
            for (int guard = 0; left > Fraction.Zero && guard < 32; guard++)
            {
                (int Value, int Dots, Fraction Length)? best = null;
                for (int value = 1; value <= 128; value *= 2)
                {
                    var plain = new Fraction(1, value);
                    var written = plain;
                    var dot = plain;
                    for (int dots = 0; dots <= 2; dots++)
                    {
                        if (written <= left && (best == null || written > best.Value.Length))
                            best = (value, dots, written);
                        dot = new Fraction(dot.Numerator, dot.Denominator * 2);
                        written += dot;
                    }
                }
                if (best == null)
                    break;
                result.Add((best.Value.Value, best.Value.Dots));
                left -= best.Value.Length;
            }
            return result;
        }

        private void MeasureRests(Voice v, int count)
        {
            Flush(v);
            v.PendingMarks.Clear();
            v.PendingHarmonies.Clear();
            for (int k = 0; k < count; k++)
            {
                if (k > 0)
                    Close(v, BarlineKind.Plain, repeatEnd: false);
                if (k == 0 && count > 1)
                    v.Measure.MultipleRest = count;
                v.Measure.Voice(1).Add(new ImportNote { IsRest = true, IsMeasureRest = true, NoteValue = 1 });
            }
        }

        // ---- bars -----------------------------------------------------------------

        private int BarLine(Voice v, string s, int i)
        {
            Flush(v);
            int start = i;
            while (i < s.Length && (s[i] is '|' or ':' or ']' || (s[i] == '[' && i + 1 < s.Length && s[i + 1] == '|')))
                i++;
            string bar = s[start..i];
            if (bar == ":" )
            {
                // A lone colon is no bar line.
                return i;
            }
            bool endRepeat = bar.StartsWith(':');
            bool startRepeat = bar.EndsWith(':') && bar.Length > 1 || bar == "::";
            if (bar.Contains("::") && !bar.Contains('|'))
                (endRepeat, startRepeat) = (true, true);
            bool final = bar.Contains("|]");
            bool thick = bar.Contains("[|");
            bool dbl = bar.Contains("||") || thick;
            var kind = endRepeat ? BarlineKind.RepeatEnd : final ? BarlineKind.Final : dbl ? BarlineKind.Double : BarlineKind.Plain;
            v.SkipToBar = false;

            bool closed = Close(v, kind, endRepeat, stopEnding: endRepeat || final || dbl || startRepeat);
            if (startRepeat)
                v.Measure.RepeatForward = true;
            // `|1` `:|2`: an ending opens the next bar.
            if (i < s.Length && char.IsDigit(s[i]))
                i = Ending(v, s, i);
            _ = closed;
            return i;
        }

        private int Ending(Voice v, string s, int i)
        {
            int number = 0;
            while (i < s.Length && char.IsDigit(s[i])) number = number * 10 + (s[i++] - '0');
            if (i < s.Length && (s[i] == ',' || s[i] == '-'))
            {
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == ',' || s[i] == '-')) i++;
                Once("an ending for several times ([1,3) is written for its first number.");
            }
            if (v.EndingOpen && v.LastClosed != null && !v.Measure.HasAnyItems)
                v.LastClosed.EndingStop = true;
            v.Measure.EndingStart = number;
            v.EndingOpen = true;
            return i;
        }

        /// <summary>Ends the bar being read (nothing when it holds nothing: a bar line that opens a
        /// line, or two in a row) and opens the next.</summary>
        private bool Close(Voice v, BarlineKind kind, bool repeatEnd, bool stopEnding = false)
        {
            if (!v.Measure.HasAnyItems)
            {
                // The sign still says something: a repeat's end, a double bar, on the bar before.
                if (v.LastClosed != null && kind != BarlineKind.Plain && v.LastClosed.BarlineRight == BarlineKind.Plain)
                    v.LastClosed.BarlineRight = kind;
                if (stopEnding && v.EndingOpen && v.LastClosed != null && v.Measure.EndingStart == null)
                {
                    v.LastClosed.EndingStop = true;
                    v.EndingOpen = false;
                }
                return false;
            }
            var m = v.Measure;
            m.BarlineRight = kind;
            if (v.EndingOpen && (stopEnding || repeatEnd))
            {
                m.EndingStop = true;
                v.EndingOpen = false;
            }
            if (v.Part.Measures.Count == 0)
            {
                m.Time ??= _meter is { } meter ? new ImportTime(meter.Beats, meter.BeatType) : null;
                if (v.Key is { } key && m.Key == null && v.Clef != "percussion")
                    m.Key = new ImportKey(key.Fifths, key.Mode);
            }
            v.Part.Measures.Add(m);
            v.LastClosed = m;
            v.Measure = new ImportMeasure();
            v.BarAccidentals.Clear();
            if (_pendingKey.Remove(v, out var pending))
                v.Measure.Key = new ImportKey(pending.Fifths, pending.Mode);
            if (_pendingTime.Remove(v, out bool moved) && moved && m.Time is { } time)
            {
                v.Measure.Time = time;
                m.Time = null;
            }
            return true;
        }

        private void Finish(Voice v)
        {
            Flush(v);
            v.Part.Clef = v.Clef ?? "treble";
            if (v.Measure.HasAnyItems)
                Close(v, BarlineKind.Plain, repeatEnd: false, stopEnding: true);
            else if (v.EndingOpen && v.LastClosed != null)
                v.LastClosed.EndingStop = true;
            if (v.SlurDepth > 0 && v.LastHeads is { Count: > 0 } heads && v.SlurDropDepth < v.SlurDepth)
                heads[0].SlurStop = true;
        }

        // ---- words ----------------------------------------------------------------

        /// <summary>A <c>w:</c> line: its syllables on the notes of the music line before it.</summary>
        private void Lyrics(Voice v, string text)
        {
            Flush(v);
            v.Verse++;
            var tokens = new List<(char Kind, string Text, bool Hyphen)>();    // 's' syllable, '_' hold, '*' skip, '|' bar
            var current = new StringBuilder();
            void Push(bool hyphen)
            {
                if (current.Length > 0)
                    tokens.Add(('s', current.ToString(), hyphen));
                else if (hyphen)
                    tokens.Add(('*', "", false));
                current.Clear();
            }
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                switch (c)
                {
                    case ' ' or '\t': Push(false); break;
                    case '-':
                        if (current.Length > 0) Push(true);
                        else if (tokens.Count > 0 && tokens[^1].Kind == 's' && !tokens[^1].Hyphen && i > 0 && text[i - 1] != ' ')
                            tokens[^1] = (tokens[^1].Kind, tokens[^1].Text, true);
                        else tokens.Add(('*', "", false));
                        break;
                    case '_': Push(false); tokens.Add(('_', "", false)); break;
                    case '*': Push(false); tokens.Add(('*', "", false)); break;
                    case '|': Push(false); tokens.Add(('|', "", false)); break;
                    case '~': current.Append(' '); break;
                    case '\\' when i + 1 < text.Length && text[i + 1] == '-': current.Append('-'); i++; break;
                    default: current.Append(c); break;
                }
            }
            Push(false);

            int at = v.LyricStart;
            bool hyphenBefore = false;
            int lost = 0;
            ImportNote? last = null;
            foreach (var (kind, syllable, hyphen) in tokens)
            {
                switch (kind)
                {
                    case 's':
                        if (at >= v.Singable.Count)
                        {
                            lost++;
                            break;
                        }
                        var head = v.Singable[at++].Head;
                        string syllabic = (hyphenBefore, hyphen) switch
                        {
                            (false, false) => "single",
                            (false, true) => "begin",
                            (true, true) => "middle",
                            _ => "end",
                        };
                        head.Lyrics.RemoveAll(l => l.Verse == v.Verse);
                        head.Lyrics.Add(new ImportLyric(v.Verse, syllable, syllabic, false));
                        last = head;
                        hyphenBefore = hyphen;
                        break;
                    case '_':
                        if (last != null && last.Lyrics.FindIndex(l => l.Verse == v.Verse) is int k and >= 0)
                            last.Lyrics[k] = last.Lyrics[k] with { Extend = true };
                        at++;
                        break;
                    case '*':
                        at++;
                        break;
                    case '|':
                        if (at > v.LyricStart && at < v.Singable.Count)
                        {
                            int bar = v.Singable[at - 1].Bar;
                            while (at < v.Singable.Count && v.Singable[at].Bar == bar) at++;
                        }
                        break;
                }
            }
            if (lost > 0)
                Once("some words (w:) have more syllables than their line has notes: the rest are left out.");
        }

        private static string StripComment(string line)
        {
            for (int i = 0; i < line.Length; i++)
                if (line[i] == '%' && (i == 0 || line[i - 1] != '\\'))
                    return line[..i];
            return line;
        }
    }
}
