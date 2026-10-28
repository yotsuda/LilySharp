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
using System.Linq;
using LilySharp.Core.Midi;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// The <c>check</c> line: a check the file turns on, by name, with what it checks — a pragma, as
/// <c>#pragma warning</c> is in C# (owner's decision 2026-10-28: "pragma であることがわかりやすい方が
/// 良いし、一般化しておけば後でほかの拡張もしやすくなる"). It changes nothing that is drawn, played
/// or exported; it only adds what the check finds. docs/voice-leading-design.md §7.
/// LILYSHARP-OWN: LilyPond has no checks to turn on; the pragma is Lily#'s own.
/// </summary>
public static class CheckPragma
{
    /// <summary>The word that opens the line — at the top level only, and not a keyword: a part
    /// may still be named <c>check</c>.</summary>
    public const string Keyword = "check";

    /// <summary><c>check voiceLeading s a t b</c>.</summary>
    public const string VoiceLeading = "voiceLeading";

    /// <summary>Every check, with what it does — the completion's and the hover's text.</summary>
    public static IReadOnlyDictionary<string, string> Rules { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [VoiceLeading] = "Four-part (SATB) writing: parallel fifths and octaves, crossed voices, and voices too far apart, "
            + "between the parts named after it, highest first — 'check voiceLeading s a t b'.",
    };

    /// <summary>The checks, for a sentence.</summary>
    public static string RuleList => string.Join(", ", Rules.Keys);

    /// <summary>The file's <c>check</c> lines.</summary>
    public static IEnumerable<PropertyAssignmentSyntax> Lines(SyntaxTree tree)
        => tree.GetRoot().ChildNodes().OfType<PropertyAssignmentSyntax>().Where(p => p.NameToken.Text == Keyword);

    /// <summary>A line's check name and what follows it.</summary>
    public static (SyntaxTokenNode? Rule, IReadOnlyList<SyntaxTokenNode> Arguments) Read(PropertyAssignmentSyntax line)
    {
        var words = line.Values.OfType<SyntaxTokenNode>().ToList();
        return words.Count == 0 ? (null, []) : (words[0], words.Skip(1).ToList());
    }

    /// <summary>The parts of each well-formed <c>check voiceLeading</c> line, highest first.</summary>
    internal static IEnumerable<IReadOnlyList<string>> VoiceLeadingParts(SyntaxTree tree)
    {
        var declared = DeclaredParts(tree);
        foreach (var line in Lines(tree))
        {
            var (rule, args) = Read(line);
            var names = args.Select(a => a.Text).ToList();
            if (rule?.Text == VoiceLeading && names.Count >= 2 && names.Distinct(StringComparer.Ordinal).Count() == names.Count
                && names.All(declared.Contains))
                yield return names;
        }
    }

    internal static HashSet<string> DeclaredParts(SyntaxTree tree)
        => tree.GetRoot().DescendantNodes<PartDeclarationSyntax>().Select(p => p.Name.Text).ToHashSet(StringComparer.Ordinal);
}

/// <summary>LYS3001: a <c>check</c> line Lily# cannot carry out.</summary>
internal sealed class CheckPragmaValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        HashSet<string>? declared = null;
        foreach (var line in CheckPragma.Lines(tree))
        {
            var (rule, args) = CheckPragma.Read(line);
            if (rule == null)
                continue;   // the parser said what the line lacks
            if (!CheckPragma.Rules.ContainsKey(rule.Text))
            {
                _diagnostics.Error(rule.Span, DiagnosticCodes.CheckPragma,
                    $"'{rule.Text}' is no check Lily# has. 'check' turns on: {CheckPragma.RuleList}.");
                continue;
            }
            declared ??= CheckPragma.DeclaredParts(tree);
            if (args.Count < 2)
                _diagnostics.Error(rule.Span, DiagnosticCodes.CheckPragma,
                    "'check voiceLeading' names the voices to compare, at least two parts, highest first — "
                    + "e.g. 'check voiceLeading s a t b'.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var arg in args)
            {
                if (!declared.Contains(arg.Text))
                    _diagnostics.Error(arg.Span, DiagnosticCodes.CheckPragma,
                        $"No part is named '{arg.Text}': 'check voiceLeading' names the file's parts, highest voice first.");
                else if (!seen.Add(arg.Text))
                    _diagnostics.Error(arg.Span, DiagnosticCodes.CheckPragma,
                        $"'{arg.Text}' is named twice: each voice is one part, in its place from the top.");
            }
        }
    }
}

/// <summary>
/// LYS3002–3004, under <c>check voiceLeading</c>: parallel fifths and octaves, crossed voices, and
/// voices too far apart (docs/voice-leading-design.md §3–§4). Read off the notes the MIDI export
/// plays — SOUNDING pitches, every transposition and `octave N` applied, a tenor's treble_8 as it
/// sounds — in the order they play.
/// </summary>
/// <remarks>
/// <para>A SONORITY is the voices' notes sounding when any voice starts one. Two neighbouring
/// sonorities are compared; a voice resting in either breaks the line there.</para>
/// <para>Stage 1 (§6): one voice is one part. A part sounding two notes at once (a chord, or
/// <c>voice { } { }</c>) is read by its highest note, the lowest voice by its lowest; the two
/// voices of one staff are stage 2.</para>
/// <para>LILYSHARP-OWN: LilyPond checks no voice leading. The rules are the common-practice
/// textbook's — parallel perfect intervals, crossing, an octave between upper voices and a twelfth
/// over the bass.</para>
/// <para>Information, not a warning (owner's decision 2026-10-28): the rules are a style's.
/// ASCII only: the message reaches legacy-codepage consoles through the CLI.</para>
/// </remarks>
internal sealed class VoiceLeadingValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    /// <summary>Semitones past which two neighbouring upper voices stand too far apart (an
    /// octave), and the lowest two (a twelfth).</summary>
    private const int UpperSpacing = 12, BassSpacing = 19;

    private readonly record struct Note(int Start, int End, int Pitch, int Pos);

    public void Validate(SyntaxTree tree)
    {
        var lines = CheckPragma.VoiceLeadingParts(tree).ToList();
        if (lines.Count == 0)
            return;
        MidiFile midi;
        try
        {
            midi = new MidiExporter().Export(tree);
        }
        catch
        {
            return; // a book that cannot play surfaces its real error elsewhere
        }
        string text = tree.Text;
        var reported = new HashSet<(string Code, int Pos)>();
        foreach (var parts in lines)
            Check(parts, midi, text, reported);
    }

    private void Check(IReadOnlyList<string> parts, MidiFile midi, string text, HashSet<(string, int)> reported)
    {
        int n = parts.Count;
        var voices = new List<Note>[n];
        for (int v = 0; v < n; v++)
        {
            bool lowest = v == n - 1;
            voices[v] = midi.Tracks.SelectMany(t => t.Notes)
                .Where(m => m.Part == parts[v] && m.Channel != 9 && !m.IsGrace)
                .GroupBy(m => m.StartTick)
                .Select(g => lowest ? g.MinBy(m => m.Pitch) : g.MaxBy(m => m.Pitch))
                .OrderBy(m => m.StartTick)
                .Select(m => new Note(m.StartTick, m.StartTick + m.DurationTicks, m.Pitch, m.SourcePos))
                .ToList();
        }
        var times = voices.SelectMany(v => v.Select(m => m.Start)).Distinct().Order().ToList();
        Note?[]? before = null;
        foreach (int t in times)
        {
            var now = new Note?[n];
            for (int v = 0; v < n; v++)
                now[v] = SoundingAt(voices[v], t);
            Vertical(parts, now, t, text, reported);
            if (before != null)
                Parallels(parts, before, now, text, reported);
            before = now;
        }
    }

    private static Note? SoundingAt(List<Note> notes, int t)
    {
        Note? found = null;
        foreach (var m in notes)
        {
            if (m.Start > t)
                break;
            if (m.End > t)
                found = m;
        }
        return found;
    }

    /// <summary>LYS3003 and LYS3004 at the sonority's new notes.</summary>
    private void Vertical(IReadOnlyList<string> parts, Note?[] now, int t, string text, HashSet<(string, int)> reported)
    {
        int n = parts.Count;
        for (int v = 0; v + 1 < n; v++)
        {
            if (now[v] is not { } upper || now[v + 1] is not { } lower)
                continue;
            if (upper.Start != t && lower.Start != t)
                continue;   // said at the onset that made it, not again while it is held
            var at = lower.Start == t ? lower : upper;
            if (lower.Pitch > upper.Pitch)
                Report(DiagnosticCodes.VoiceCrossing, at.Pos, text, reported,
                    $"voice crossing: {parts[v + 1]} ({Spell(lower.Pitch)}) is above {parts[v]} ({Spell(upper.Pitch)})");
            else if (n >= 3)
            {
                bool bass = v + 1 == n - 1;
                int limit = bass ? BassSpacing : UpperSpacing;
                if (upper.Pitch - lower.Pitch > limit)
                    Report(DiagnosticCodes.VoiceSpacing, at.Pos, text, reported,
                        $"{parts[v]} ({Spell(upper.Pitch)}) and {parts[v + 1]} ({Spell(lower.Pitch)}) are more than "
                        + (bass ? "a twelfth" : "an octave") + " apart");
            }
        }
    }

    /// <summary>LYS3002: the same two voices from a perfect fifth or octave to the same again,
    /// both moving.</summary>
    private void Parallels(IReadOnlyList<string> parts, Note?[] before, Note?[] now, string text, HashSet<(string, int)> reported)
    {
        int n = parts.Count;
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                if (before[i] is not { } a0 || before[j] is not { } b0 || now[i] is not { } a1 || now[j] is not { } b1)
                    continue;
                if (a0.Pitch == a1.Pitch || b0.Pitch == b1.Pitch)
                    continue;   // one voice holds or repeats: not parallel motion
                int from = Math.Abs(a0.Pitch - b0.Pitch), to = Math.Abs(a1.Pitch - b1.Pitch);
                int kind = from % 12;
                if (kind != to % 12 || (kind != 0 && kind != 7))
                    continue;
                string name = kind == 7 ? "fifths" : from == 0 && to == 0 ? "unisons" : "octaves";
                string Interval(int d) => kind == 7 ? (d > 12 ? "a twelfth" : "a fifth") : d == 0 ? "a unison" : d > 12 ? "two octaves" : "an octave";
                // Both moved, so both start here: the upper voice's note carries it.
                Report(DiagnosticCodes.ParallelPerfectIntervals, a1.Pos, text, reported,
                    $"parallel {name}: {parts[i]} and {parts[j]} move from {Interval(from)} ({Spell(a0.Pitch)} over {Spell(b0.Pitch)}) "
                    + $"to {Interval(to)} ({Spell(a1.Pitch)} over {Spell(b1.Pitch)})");
            }
    }

    private void Report(string code, int pos, string text, HashSet<(string, int)> reported, string message)
    {
        if (pos < 0 || pos >= text.Length || !reported.Add((code, pos)))
            return;
        int end = pos;
        while (end < text.Length && (char.IsLetter(text[end]) || text[end] is '\'' or ','))
            end++;
        _diagnostics.Info(new TextSpan(pos, Math.Max(1, end - pos)), code, message);
    }

    private static string Spell(int midi)
        => new[] { "C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B" }[midi % 12] + (midi / 12 - 1);
}
