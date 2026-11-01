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
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using LilySharp.Core.Midi;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// The composing checks' second entrance (owner's proposal 2026-10-30, docs/review-tool-design.md):
/// a report on a score for an AI to explain to the person who wrote it, or to read without one.
/// What the checks count is not a judgement; the report gives the AI what it needs to judge: the
/// piece's facts, what it does well, and for each finding the convention behind it and when it
/// does not apply.
/// </summary>
public static class ScoreReview
{
    private const int MostWarnings = 20;

    private static readonly JsonSerializerOptions Written = new()
    {
        WriteIndented = true,
        // A report is read, by an AI or a person: ♩, ♭ and the piece's own words as they are.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The report on <paramref name="source"/> as one JSON document. <paramref name="focus"/>
    /// (<c>key</c>, <c>rhythm</c>, <c>form</c>, <c>parts</c>, <c>notation</c>) keeps the findings of that
    /// theme alone; the facts and the strengths stay.</summary>
    public static string Json(string source, string audience = "beginner", string? focus = null)
        => Report(source, audience, focus).ToJsonString(Written);

    private static JsonObject Report(string source, string audience, string? focus)
    {
        var report = new JsonObject { ["audience"] = audience };
        var tree = SyntaxTree.Parse(source);
        var said = tree.Diagnostics.ToList();
        if (!tree.HasErrors)
            try
            {
                said.AddRange(Semantics.SemanticValidation.Run(tree));
            }
            catch
            {
                // A tree the validation trips on still has its notes to read.
            }
        var errors = said.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        var warnings = said.Where(d => d.Severity == DiagnosticSeverity.Warning).ToList();
        report["compiles"] = errors.Count == 0;
        if (errors.Count > 0)
            report["errors"] = Said(errors, source);
        if (warnings.Count > 0)
        {
            report["warnings"] = Said(warnings.Take(MostWarnings), source);
            if (warnings.Count > MostWarnings)
                report["warnings_not_listed"] = warnings.Count - MostWarnings;
        }
        // Not read as music, the piece has no facts to report: the errors are the report.
        if (tree.HasErrors)
            return report;

        var findings = CompositionChecks.Findings(source);
        MidiFile? midi = null;
        try
        {
            midi = new MidiExporter().Export(tree);
        }
        catch
        {
            // What the notation alone says is still said.
        }
        report["facts"] = Facts(source, tree.GetRoot(), midi);
        report["strengths"] = Strings(Strengths(tree.GetRoot(), midi, findings));
        report["findings"] = new JsonArray([.. findings
            .Where(f => focus == null || InFocus(f, focus))
            .OrderBy(f => ReviewNotes.All[f.Id].Kind)
            .Select(Finding)]);
        return report;
    }

    private static JsonArray Said(IEnumerable<Diagnostic> diagnostics, string source)
        => new([.. diagnostics.Select(d => (JsonNode?)new JsonObject
        {
            ["line"] = 1 + source.AsSpan(0, System.Math.Min(d.Span.Start, source.Length)).Count('\n'),
            ["code"] = d.Code,
            ["message"] = d.Message,
        })]);

    private static JsonArray Strings(IEnumerable<string> values) => new([.. values.Select(v => (JsonNode?)v)]);

    private static JsonNode Finding(CompositionFinding finding)
    {
        var note = ReviewNotes.All[finding.Id];
        var found = new JsonObject
        {
            ["id"] = finding.Id,
            ["kind"] = note.Kind.ToString().ToLowerInvariant(),
            ["confidence"] = note.Confidence.ToString().ToLowerInvariant(),
            ["where"] = new JsonObject
            {
                ["parts"] = Strings(finding.Parts),
                ["bars"] = new JsonArray([.. finding.Bars.Select(b => (JsonNode?)b)]),
            },
            ["what"] = finding.Message,
            ["why"] = note.Why,
        };
        if (note.FineWhen != null)
            found["fine_when"] = note.FineWhen;
        return found;
    }

    private static bool InFocus(CompositionFinding finding, string focus)
    {
        string theme = finding.Id[..finding.Id.IndexOf('.')];
        return focus switch
        {
            "key" => theme is "harmony" or "spelling",
            "rhythm" => theme is "rhythm" or "meter" or "phrase" or "drums" or "texture",
            "form" => theme is "form" or "chorus",
            "parts" => theme is "part" or "band" or "bass" or "piano" or "singers" or "voicing" or "dynamics" or "tune" or "staff" or "melody" or "octave",
            "notation" => ReviewNotes.All[finding.Id].Kind == ReviewKind.Notation,
            _ => true,
        };
    }

    private static JsonObject Facts(string source, SyntaxNode root, MidiFile? midi)
    {
        var facts = new JsonObject();
        if (CompositionChecks.TitleOf(source) is { } title)
            facts["title"] = title;
        if (HomeKey(root) is { } key)
            facts["key"] = key;
        var form = CompositionChecks.PlayedSections(root);
        if (form.Count > 0)
            facts["form"] = Strings(form);
        var parts = root.DescendantNodes<PartDeclarationSyntax>().Where(p => !p.IsStrokes).GroupBy(p => p.Name.Text).Select(g => g.First()).ToList();
        if (midi == null)
        {
            facts["parts"] = new JsonArray([.. parts.Select(p => (JsonNode?)Part(p, null, 0))]);
            return facts;
        }
        var meters = midi.Tracks.SelectMany(t => t.TimeSignatures).OrderBy(t => t.Tick).Select(t => $"{t.Numerator}/{t.Denominator}").Distinct().ToList();
        if (meters.Count > 0)
            facts["meter"] = string.Join(", ", meters);
        var tempos = midi.Tracks.SelectMany(t => t.TempoChanges).OrderBy(t => t.Tick)
            .Select(t => $"♩ = {System.Math.Round(60_000_000.0 / t.MicrosecondsPerBeat)}").Distinct().ToList();
        if (tempos.Count > 0)
            facts["tempo"] = string.Join(", ", tempos);
        var (bars, seconds) = CompositionChecks.LengthOf(midi);
        facts["bars"] = bars;
        facts["seconds"] = seconds;
        int fifths = PartRanges.KeyFifths(root);
        var ranges = PartRanges.Of(midi, source, fifths);
        facts["parts"] = new JsonArray([.. parts.Select(p => (JsonNode?)Part(p, ranges.FirstOrDefault(r => r.Name == p.Name.Text), fifths))]);
        return facts;
    }

    private static JsonObject Part(PartDeclarationSyntax part, PartRange? range, int fifths)
    {
        var said = new JsonObject { ["name"] = part.Name.Text };
        if (CompositionChecks.InstrumentOf(part) is { Length: > 0 } instrument)
            said["instrument"] = instrument;
        if (range != null)
        {
            said["range"] = $"{range.Low.Name}–{range.High.Name}";
            said["mostly"] = $"{PartRanges.SpelledFromPitch(range.TessituraLow, fifths)}–{PartRanges.SpelledFromPitch(range.TessituraHigh, fifths)}";
        }
        return said;
    }

    private static string? HomeKey(SyntaxNode root)
        => CompositionChecks.PlayedKeys(root) is { } keys ? $"{CompositionChecks.TonicName(keys.Home.Tonic)} {keys.Home.Mode}" : null;

    /// <summary>What the piece does well (a beginner is told what to keep, not only what to change):
    /// each only where its check had music to read and said nothing.</summary>
    private static IEnumerable<string> Strengths(SyntaxNode root, MidiFile? midi, IReadOnlyList<CompositionFinding> findings)
    {
        bool Said(params string[] ids) => findings.Any(f => ids.Contains(f.Id));
        int pitched = midi == null ? 0 : midi.Tracks.Sum(t => t.Notes.Count(n => n.Channel != 9));
        if (pitched == 0)
            yield break;

        if (HomeKey(root) is { } key && !Said("form.ends-in-another-key", "form.final-bass"))
            yield return $"It ends where it began: in {key}, over its tonic.";
        var form = CompositionChecks.PlayedSections(root);
        if (form.Distinct().Count() >= 2 && form.Distinct().Count() < form.Count)
            yield return $"The form has a return: {string.Join(" ", form)}.";
        if (midi != null && !Said("form.length"))
        {
            var (bars, seconds) = CompositionChecks.LengthOf(midi);
            yield return $"It has room to say something: {bars} bars, about {seconds} seconds.";
        }
        if (pitched >= 32 && !Said("rhythm.one-value", "rhythm.same-bars", "rhythm.same-bars-in-section", "phrase.formula", "phrase.positions", "texture.one-rhythm"))
            yield return "The rhythm varies: no part sits on one note value, and the parts do not all move together.";
        if (pitched >= 32 && !Said("melody.leaps", "melody.sequence", "octave.slip", "tune.under-chords"))
            yield return "The lines hold together: no slipped octave, and no tune that only breaks its chords.";
        var levelled = CompositionChecks.DynamicLevelsByPart(root);
        var players = root.DescendantNodes<PartDeclarationSyntax>().Where(p => !p.IsStrokes && !CompositionChecks.IsDrums(p)).Select(p => p.Name.Text).Distinct().ToList();
        if (players.Count > 0 && players.All(levelled.Contains) && !Said("dynamics.part-silent", "dynamics.late"))
            yield return "Every part has its own dynamics, from its first note.";
        if (PartRanges.KeyFifths(root) != 0 && pitched >= 16 && !Said("spelling.key", "spelling.natural"))
            yield return "The notes of the key are written by the key's own names.";
    }
}
