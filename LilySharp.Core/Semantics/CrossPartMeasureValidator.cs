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
    /// Flags a section whose bar count differs between the voices that define it
    /// part-major: the same `section S` written inside more than one `part`, or inside a
    /// `part` and a named `chords` track (<c>chords prog { section S { … } }</c>). The
    /// collector pads the shorter parts with spacer rests up to the section's canonical
    /// bar count (the greatest over parts AND chord tracks — MeasureCollector's
    /// GetCanonicalSectionBars) to keep staves aligned, so this renders — but a differing
    /// count is usually a miscount worth surfacing. A chord track's section is counted by
    /// the row's own rule (every written barline closes a bar); a lyrics track's is not
    /// counted at all — a lyrics section longer than its music is a stacked verse by design.
    /// </summary>
    /// <remarks>
    /// ★ Chord tracks joined this pass on 2026-09-10 (scratch/ベースタブLy/tooLongChords.lys):
    /// <c>chords prog { section A { Dm7 | G7 } }</c> over a ONE-bar melody A said nothing —
    /// only `part`-nested sections were gathered — and the page put G7 on B's first bar
    /// beside B's own Cmaj7. Now melody's A is the short one (warned here, anchored on
    /// its section name) and the collector pads it to two bars.
    /// </remarks>
    private void ValidatePartMajorSections(SyntaxNode root)
    {
        // The voices and their bar counts come from THE ONE HOUSE (SectionBarCounts —
        // the semantic counter, MeasureModel.Split with the score-level meter in force),
        // the same index the LilyPond / MIDI / MusicXML exporters pad by, so what this
        // warning says is short is what those pad. Section name -> its part-major voices.
        var byName = new Dictionary<string, List<SectionVoice>>();
        foreach (var voice in Svg.Collector.SectionBarCounts.SemanticVoices(root))
        {
            if (!voice.PartMajor)
                continue; // section-major voices: ValidateSectionCrossPart, which also compares beats
            if (!byName.TryGetValue(voice.SectionName, out var list))
                byName[voice.SectionName] = list = new();
            list.Add(new SectionVoice(voice.Label, voice.IsChords, voice.Bars, voice.Anchor));
        }

        foreach (var (name, list) in byName)
            ReportBarCountMismatch(name, list);
    }

    /// <summary>One voice of a section as the bar-count pass sees it: a part, or a named
    /// chord track. <see cref="Label"/> is how the message names it (the spelling
    /// SectionBarCounts.SemanticVoice.Label uses: <c>part 'x'</c> / <c>chords 'x'</c>).</summary>
    private readonly record struct SectionVoice(string Label, bool IsChords, int Bars, TextSpan Span)
    {
        public static SectionVoice Part(string name, int bars, TextSpan span) => new($"part '{name}'", false, bars, span);
        public static SectionVoice Chords(string name, int bars, TextSpan span) => new($"chords '{name}'", true, bars, span);
    }

    /// <summary>
    /// The bar-count comparison shared by both layouts: every voice that writes fewer bars
    /// than the section's longest voice is reported on its own span. The tail of the message
    /// says what the page does with the shortfall — a part is padded with spacer rests, a
    /// chord row simply has no chord over the bars it does not write.
    /// </summary>
    private void ReportBarCountMismatch(string sectionName, List<SectionVoice> voices)
    {
        if (voices.Count < 2)
            return;
        int maxBars = voices.Max(v => v.Bars);
        if (voices.All(v => v.Bars == maxBars))
            return; // all voices agree
        var reference = voices.First(v => v.Bars == maxBars);
        foreach (var voice in voices)
        {
            if (voice.Bars == maxBars)
                continue;
            string tail = voice.IsChords
                ? "the row writes no chord over the remaining bar(s)"
                : "the shorter part is padded with rests to align";
            _diagnostics.Warning(voice.Span, DiagnosticCodes.SectionBarCountMismatch,
                $"Section '{sectionName}' spans {voice.Bars} bar(s) in {voice.Label} but {maxBars} in "
                + $"{reference.Label} — {tail}");
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
        // Named chord blocks of the section: voices of the bar-count check only (a chord
        // row has bars but no beats to compare per measure).
        var chordVoices = new List<SectionVoice>();
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
                case ChordPartBlockSyntax { PartName: { } track, NameToken: { } nameToken } cb:
                    chordVoices.Add(SectionVoice.Chords(track, Svg.Collector.ChordNameCollector.CountBars(cb), nameToken.Span));
                    break;
            }
        }

        if (parts.Count + chordVoices.Count < 2)
            return time;
        if (parts.Count < 2)
        {
            // One part beside chord rows: nothing to compare per measure, only the count.
            ReportSectionMajorBarCount(section, parts, chordVoices);
            return time;
        }

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
        ReportSectionMajorBarCount(section, parts, chordVoices);

        return time;
    }

    /// <summary>The bar-count check of a section-major section over its part blocks AND
    /// its named chord blocks (a part-block voice is anchored on its part name).</summary>
    private void ReportSectionMajorBarCount(
        SectionDeclarationSyntax section,
        List<(string Name, Fraction Time, TextSpan TimeSpan, List<MeasureModel.Bar> Measures)> parts,
        List<SectionVoice> chordVoices)
    {
        var voices = parts.Select(p => SectionVoice.Part(p.Name, p.Measures.Count, p.TimeSpan)).ToList();
        voices.AddRange(chordVoices);
        ReportBarCountMismatch(section.SectionName, voices);
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

}
