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

using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Cross-part measure-alignment validation. Extracted from <see cref="MeasureValidator"/>
/// (which owns per-block fullness): the two share only the running warned-span set
/// (a fullness warning suppresses the mismatch report for the same span) and the
/// <see cref="MeasureDurations"/> beat-counting logic.
/// </summary>
/// <remarks>
/// Time signatures are SCORE-level (like LilyPond's Timing context): a
/// "time" declared at top level or section level governs every part, so
/// a part that writes the right number of beats without restating the
/// time signature is correct and must NOT warn. What the time signature
/// cannot explain — two parts disagreeing about a measure's length at
/// the same index — breaks vertical alignment, span bars and playback,
/// and is reported here. Fullness warnings already emitted by the
/// per-block pass suppress the mismatch report for the same source span
/// (one root cause, one diagnostic).
/// </remarks>
internal sealed class CrossPartMeasureValidator
{
    private readonly DiagnosticBag _diagnostics;
    private readonly HashSet<(int Start, int Length)> _warnedSpans;
    private Dictionary<string, SyntaxNode>? _phraseBodies;

    /// <summary>
    /// Shares the caller's diagnostic bag and warned-span set so the cross-part
    /// pass runs AFTER (and defers to) the per-block fullness pass.
    /// </summary>
    public CrossPartMeasureValidator(DiagnosticBag diagnostics, HashSet<(int Start, int Length)> warnedSpans)
    {
        _diagnostics = diagnostics;
        _warnedSpans = warnedSpans;
    }

    public void Validate(SyntaxNode root)
    {
        _phraseBodies = new Dictionary<string, SyntaxNode>();
        foreach (var n in root.DescendantNodes())
        {
            if (n is PhraseDeclarationSyntax ph)
                _phraseBodies[ph.Name.Text] = ph.Body;
            else if (n is VariableDeclarationSyntax vd)
                _phraseBodies[vd.Name.Text] = vd.Expression;
        }

        // Document-order walk: top-level time declarations update the score
        // time; each section validates with the time in force at its site.
        var time = new Fraction(4, 4);
        WalkForSections(root, ref time);

        // Part-major (`part X { section S { … } }`) sections are not visited above
        // (WalkForSections only sees section-major blocks that hold part sub-blocks),
        // so cross-part alignment there is checked separately by section name.
        ValidatePartMajorSections(root);
    }

    /// <summary>
    /// Flags a section whose bar count differs between the parts that define it
    /// part-major (the same `section S` written inside more than one `part`). The
    /// collector pads the shorter parts with spacer rests to keep staves aligned,
    /// so this renders — but a differing count is usually a miscount worth surfacing.
    /// </summary>
    private void ValidatePartMajorSections(SyntaxNode root)
    {
        // section name -> each defining part's (name, bar count, section-name span)
        var byName = new Dictionary<string, List<(string Part, int Bars, TextSpan Span)>>();
        // The running SCORE-level meter in document order (a `time` outside any
        // part/section body arms everything after it) — it steers the repeat-flow
        // auto-complete inside MeasureModel.Split, so a part-major section's bar
        // count agrees with the collector under a non-4/4 score meter too.
        var time = DurationCalculator.ParseTimeSignature(4, 4);
        foreach (var section in root.DescendantNodes())
        {
            if (section is TimeSignatureSyntax ts && !ts.IsSenzaMisura && IsScoreLevel(ts))
                time = DurationCalculator.ParseTimeSignature(ts.Beats, ts.BeatType);
            if (section is not SectionDeclarationSyntax sec || sec.Parent is not PartDeclarationSyntax part)
                continue; // only sections nested directly in a `part` (part-major)
            int bars = BuildPartMeasures(sec, time).Count;
            if (!byName.TryGetValue(sec.SectionName, out var list))
                byName[sec.SectionName] = list = new();
            list.Add((part.Name.Text, bars, sec.Name.Span));
        }

        foreach (var (name, list) in byName)
        {
            if (list.Count < 2)
                continue;
            int maxBars = list.Max(x => x.Bars);
            if (list.All(x => x.Bars == maxBars))
                continue; // all parts agree
            var reference = list.First(x => x.Bars == maxBars);
            foreach (var (part, bars, span) in list)
            {
                if (bars == maxBars)
                    continue;
                _diagnostics.Warning(span, DiagnosticCodes.SectionBarCountMismatch,
                    $"Section '{name}' spans {bars} bar(s) in part '{part}' but {maxBars} in part "
                    + $"'{reference.Part}' — the shorter part is padded with rests to align");
            }
        }
    }

    private void WalkForSections(SyntaxNode node, ref Fraction time)
    {
        for (int i = 0; i < node.SlotCount; i++)
        {
            var child = node.GetChild(i);
            if (child == null || child is SyntaxTokenNode)
                continue;
            switch (child)
            {
                case TimeSignatureSyntax ts:
                    time = DurationCalculator.ParseTimeSignature(ts.Beats, ts.BeatType);
                    break;
                case SectionDeclarationSyntax section:
                    time = ValidateSectionCrossPart(section, time);
                    break;
                case PhraseDeclarationSyntax:
                case VariableDeclarationSyntax:
                    break; // bodies are validated where referenced
                default:
                    WalkForSections(child, ref time);
                    break;
            }
        }
    }

    private Fraction ValidateSectionCrossPart(SectionDeclarationSyntax section, Fraction time)
    {
        // Section items in document order: a section-level time declaration
        // applies to the part blocks that follow it. Each part records the
        // time in force at its own position.
        var parts = new List<(string Name, Fraction Time, TextSpan TimeSpan, List<MeasureModel.Bar> Measures)>();
        for (int i = 0; i < section.SlotCount; i++)
        {
            var child = section.GetChild(i);
            switch (child)
            {
                case TimeSignatureSyntax ts:
                    time = DurationCalculator.ParseTimeSignature(ts.Beats, ts.BeatType);
                    break;
                case PartBlockSyntax pb:
                    parts.Add((pb.Name, time, pb.PartName.Span, BuildPartMeasures(pb, time)));
                    break;
            }
        }

        if (parts.Count < 2)
            return time;

        // A time declared BETWEEN part blocks would put the parts of one
        // section in different meters — flag it; alignment is undefined.
        for (int p = 1; p < parts.Count; p++)
        {
            if (parts[p].Time != parts[0].Time)
            {
                _diagnostics.Warning(parts[p].TimeSpan, DiagnosticCodes.ConflictingTimeSignatures,
                    $"Part '{parts[p].Name}' is in {parts[p].Time} but part '{parts[0].Name}' is in {parts[0].Time} within the same section");
            }
        }

        int maxLen = parts.Max(p => p.Measures.Count);
        for (int i = 0; i < maxLen; i++)
        {
            // An explicit empty placeholder (`| |`) is the author padding a tacet bar
            // themselves. It is worth a full measure since 2026-08-28 (MeasureModel
            // hands it the meter, mirroring the spacer MeasureBuilder fills it with), so
            // it would conform here anyway; the skip stays because a GAP is not a claim
            // about how long the other parts' bars are, and reading it as one would put
            // this pass's message on the wrong bar under `time none`, where it is still
            // worth zero.
            var present = parts.Where(p => i < p.Measures.Count && !p.Measures[i].IsEmpty).ToList();
            if (present.Count < 2)
                continue;

            var durations = present.Select(p => p.Measures[i].Duration).Distinct().ToList();
            if (durations.Count <= 1)
                continue;

            // Blame the parts whose duration deviates from their meter; if
            // none matches the meter, blame everyone after the first.
            var conformers = present.Where(p => p.Measures[i].Duration == p.Time).ToList();
            var reference = conformers.Count > 0 ? conformers[0] : present[0];
            foreach (var part in present)
            {
                if (part.Measures[i].Duration == reference.Measures[i].Duration)
                    continue;
                var span = part.Measures[i].Span;
                if (_warnedSpans.Contains((span.Start, span.Length)))
                    continue; // already explained by a fullness warning
                _warnedSpans.Add((span.Start, span.Length));
                _diagnostics.Warning(span, DiagnosticCodes.MeasureDurationMismatch,
                    $"Measure {i + 1} of part '{part.Name}' lasts {part.Measures[i].Duration} but part '{reference.Name}' has {reference.Measures[i].Duration} — parts will not align");
            }
        }

        // Bar-count mismatch: a part with fewer bars than its section-mates is padded
        // to align (the per-measure loop above only compares indices both parts reach).
        int maxCount = parts.Max(p => p.Measures.Count);
        if (parts.Any(p => p.Measures.Count != maxCount))
        {
            var longest = parts.First(p => p.Measures.Count == maxCount);
            foreach (var part in parts)
            {
                if (part.Measures.Count == maxCount)
                    continue;
                _diagnostics.Warning(part.TimeSpan, DiagnosticCodes.SectionBarCountMismatch,
                    $"Section '{section.SectionName}' spans {part.Measures.Count} bar(s) in part "
                    + $"'{part.Name}' but {maxCount} in part '{longest.Name}' — the shorter part is "
                    + "padded with rests to align");
            }
        }

        return time;
    }

    /// <summary>
    /// Splits a music scope (a section-major part block, or a part-major section body)
    /// into measures via the shared <see cref="MeasureModel"/> — the one place that
    /// applies the bare-barline rule and expands phrase references. The empty-placeholder
    /// warning is emitted from <see cref="MeasureValidator"/> over the same model, so the
    /// two passes agree on which bars exist (IsEmpty never depends on the meter, so that
    /// pass omitting it is harmless). The meter in force at the scope steers the
    /// repeat-flow auto-complete.
    /// </summary>
    private List<MeasureModel.Bar> BuildPartMeasures(SyntaxNode scope, Fraction time)
        => MeasureModel.Split(scope, _phraseBodies!, time);

    /// <summary>True for a node outside every part/section/music body — the score level,
    /// where a <c>time</c> declaration arms the whole document after it (LP's Timing is
    /// Score-level; Lily# part-local changes are restated per part and stay local).</summary>
    private static bool IsScoreLevel(SyntaxNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
            if (p is PartDeclarationSyntax or PartBlockSyntax or SectionDeclarationSyntax or MusicBlockSyntax)
                return false;
        return true;
    }
}
