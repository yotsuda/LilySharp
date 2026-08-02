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

using System.Linq;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Validates measures against time signatures (per-block fullness). Cross-part
/// alignment lives in <see cref="CrossPartMeasureValidator"/>; the two share the
/// warned-span set and the <see cref="MeasureDurations"/> beat-counting logic.
/// </summary>
internal sealed class MeasureValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();
    // Spans already flagged by the per-block fullness pass. The cross-part pass
    // reads this to avoid double-reporting the same bar (one root cause, one
    // diagnostic); the set is shared with CrossPartMeasureValidator.
    private readonly HashSet<(int Start, int Length)> _warnedSpans = new();
    private Fraction _timeSignature = new(4, 4); // Default 4/4
    private bool _senzaMisura; // time none: no bar-length validation
    // The meter AS WRITTEN ("4/4", "6/8") for diagnostics — the Fraction
    // normalizes (4/4 → "1"), which made the overfull warning read
    // "exceeds time signature 1".
    private string _meterText = "4/4";
    // Set by a top-level `partial N` — the declared pickup length for every
    // voice's first measure (mirrors MeasureCollector._filePartial).
    private Fraction? _filePartial;
    // True once the file has any part/section/form: a `partial` then belongs to a section
    // directive; a bare note stream takes a leading `partial` instead. Drives the pickup hint.
    private bool _structured;

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    /// <summary>
    /// Sets the current time signature.
    /// </summary>
    public void SetTimeSignature(int beats, int beatUnit)
    {
        _timeSignature = DurationCalculator.ParseTimeSignature(beats, beatUnit);
        _meterText = $"{beats}/{beatUnit}";
    }

    /// <summary>
    /// Validates all measures in a compilation unit.
    /// </summary>
    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        _structured = root.DescendantNodes().Any(n =>
            n is PartDeclarationSyntax or SectionDeclarationSyntax or FormDeclarationSyntax);
        _phraseBodies = CollectPhraseBodies(root);
        ValidateNode(root);
        // Empty `| |` placeholders are flagged over EVERY defined scope, independent of
        // whether a form references it — the same reach the underfull check above has, so
        // the two are consistent (a section validates the moment it is written, before it
        // is wired into a form).
        ValidateEmptyPlaceholders(root);
        // Cross-part alignment runs AFTER per-block fullness and shares its
        // warned spans, so a fullness warning suppresses a mismatch report.
        new CrossPartMeasureValidator(_diagnostics, _warnedSpans).Validate(root);
    }

    private Dictionary<string, SyntaxNode> _phraseBodies = new();

    private static Dictionary<string, SyntaxNode> CollectPhraseBodies(SyntaxNode root)
    {
        var bodies = new Dictionary<string, SyntaxNode>();
        foreach (var n in root.DescendantNodes())
        {
            if (n is PhraseDeclarationSyntax ph)
                bodies[ph.Name.Text] = ph.Body;
            else if (n is VariableDeclarationSyntax vd)
                bodies[vd.Name.Text] = vd.Expression;
        }
        return bodies;
    }

    /// <summary>
    /// Warns for every empty <c>| |</c> placeholder measure in the score, reading the
    /// shared <see cref="MeasureModel"/> so the rule matches the render collector and the
    /// cross-part pass exactly. The scopes enumerated here — each part block, each
    /// part-major section's inline music, and a bare top-level stream — mirror what the
    /// collector would render, but WITHOUT following the form, so an unreferenced section
    /// is validated too. (Before this the warning came only from the form-driven collector,
    /// so a defined-but-unused section's empty bars went unflagged.)
    /// </summary>
    private void ValidateEmptyPlaceholders(SyntaxNode root)
    {
        var scopes = new List<SyntaxNode>();
        // Section-major: each part block's own music.
        scopes.AddRange(root.DescendantNodes().OfType<PartBlockSyntax>());
        // Part-major: a section whose body is inline music (no part-block children).
        foreach (var sec in root.DescendantNodes().OfType<SectionDeclarationSyntax>())
            if (!sec.DescendantNodes().OfType<PartBlockSyntax>().Any())
                scopes.Add(sec);
        // A bare top-level note stream (no parts/sections at all).
        if (!root.DescendantNodes().Any(n =>
                n is PartBlockSyntax or SectionDeclarationSyntax or PartDeclarationSyntax))
            scopes.Add(root);

        foreach (var scope in scopes)
            foreach (var bar in MeasureModel.Split(scope, _phraseBodies))
            {
                if (!bar.IsEmpty)
                    continue;
                if (!_warnedSpans.Add((bar.Span.Start, bar.Span.Length)))
                    continue; // already flagged (defensive; scopes don't overlap)
                // The zero case of the underfull warning (LYS2001) — same kind as the
                // user fills the bar (`| |` -> `| c4 |` -> full). ASCII punctuation only:
                // this string reaches legacy-codepage consoles via the CLI.
                _diagnostics.Warning(bar.Span, DiagnosticCodes.MeasureIncomplete,
                    "Measure duration 0 is less than the meter - an empty measure; " +
                    "fill it (or keep it as a slot that aligns the parts)");
            }
    }

    private void ValidateNode(SyntaxNode node)
    {
        // A tuplet/grace body is a nested MusicBlock, but its notes belong to the
        // enclosing measure (and are counted there with the correct tuplet scale).
        // Don't recurse into it, or it would be validated as a short standalone bar.
        // …and a cue region for the same reason: its body is metric material of the
        // ENCLOSING bar (folded in by MeasureDurations.ItemDuration), never a bar of its own.
        if (node is TupletExpressionSyntax or GraceExpressionSyntax or CueExpressionSyntax)
            return;

        // A tremolo repeat's body ("{ c32 }") is metric material of the
        // ENCLOSING measure (counted above via ItemDuration), never a
        // standalone bar — recursing flagged it as a 1/32 "first measure".
        if (node is RepeatExpressionSyntax trem && trem.RepeatType.Text == "tremolo")
            return;

        // LYS0010 recovery: a nested voice's block INLINES into the enclosing
        // voice (SplitIntoMeasures expands it there) — validating it as a
        // standalone block would re-add the phantom short-bar warnings.
        if (node is NestedVoiceRecoverySyntax)
            return;

        // A voice span's blocks are not standalone bar streams: voice 1 is INLINED into
        // the enclosing stream by SplitIntoMeasures (the collector walks it inline too),
        // and voices 2..N are validated from there with the bar's lead-in. Recursing here
        // would validate each voice as its own stream starting on a barline — which is
        // what reported three short "first measures" for `c'2 voice { d'2 } voice { e'2 }`,
        // a bar the renderer fills exactly.
        if (node is ParallelExpressionSyntax)
            return;

        switch (node)
        {
            case MusicBlockSyntax block:
                ValidateMusicBlock(block);
                break;

            case SectionDeclarationSyntax section:
                ValidateSectionInlineMusic(section);
                break;

            case TimeSignatureSyntax timeSig when !IsInsideMusicBlock(timeSig) && timeSig.IsSenzaMisura:
                _senzaMisura = true;
                break;

            case TimeSignatureSyntax timeSig when !IsInsideMusicBlock(timeSig):
                // Only a TOP-LEVEL / header `time` re-arms the document meter.
                // An in-block change is applied inside ValidateMusicBlock and
                // scoped there; re-applying it here leaked the previous part's
                // mid-music 3/4 onto the NEXT part's perfectly good 4/4 bars.
                SetTimeSignature(timeSig.Beats, timeSig.BeatType);
                break;

            case PartialDeclarationSyntax filePartial when !IsInsideMusicBlock(filePartial):
                // A top-level `partial N` (a GlobalSetting) arms EVERY voice's
                // first measure; the first bar of each block is then strictly
                // checked against the declared pickup length.
                _filePartial = filePartial.ToFraction();
                break;

            case MetadataDeclarationSyntax:
                // MetadataDeclaration now only handles title/composer
                // Time signatures use TimeSignatureSyntax
                break;

            case PropertyAssignmentSyntax:
                // Property assignments are handled within blocks
                break;
        }

        // Recurse into children
        for (int i = 0; i < node.SlotCount; i++)
        {
            var child = node.GetChild(i);
            if (child != null && child is not SyntaxTokenNode)
            {
                ValidateNode(child);
            }
        }
    }

    private void ValidateMusicBlock(MusicBlockSyntax block)
        => ValidateItemsScoped(block.Items, block.Position);

    // Part-major sections hold their music INLINE (a SectionDeclarationSyntax with
    // note/bar children and no MusicBlock wrapper), so the bar-check never saw them
    // — only section-major sections, whose part blocks each wrap a MusicBlock, were
    // checked. Validate the inline music the same way. A section-major section holds
    // part blocks (not inline music) and is left to the per-part-block pass.
    private void ValidateSectionInlineMusic(SectionDeclarationSyntax section)
    {
        var items = new List<SyntaxNode>();
        for (int i = 0; i < section.SlotCount; i++)
        {
            var child = section.GetChild(i);
            if (child is null or SyntaxTokenNode) continue;
            if (child is PartBlockSyntax) return; // section-major: not inline music
            items.Add(child);
        }
        if (items.Count > 0)
            ValidateItemsScoped(items, section.Position);
    }

    /// <param name="leadIn">Beats already elapsed in the bar this stream starts inside —
    /// non-zero only for voices 2..N of a span opened mid-bar, which sound from there.</param>
    /// <param name="initialDefault">The running default note value at that point (a bare
    /// note inherits the previous note's duration), or null for a fresh quarter-note frame.</param>
    private void ValidateItemsScoped(IEnumerable<SyntaxNode> items, int startPos,
        Fraction? leadIn = null, Fraction? initialDefault = null)
    {
        // A mid-music `time` re-arms the meter for the rest of THIS block/section
        // only — the state must not leak into the next part's block (each part
        // restates its own changes), or every 4/4 bar of the following part gets
        // flagged against the previous part's 3/4.
        // LILYPOND-REF: Timing is Score-level in LP, but Lily# parts restate
        // meter changes per part; validation follows the per-block timeline.
        var savedTime = _timeSignature;
        var savedMeterText = _meterText;
        var savedSenza = _senzaMisura;
        try
        {
            ValidateMeasures(items, startPos, leadIn, initialDefault);
        }
        finally
        {
            _timeSignature = savedTime;
            _meterText = savedMeterText;
            _senzaMisura = savedSenza;
        }
    }

    private void ValidateMeasures(IEnumerable<SyntaxNode> items, int startPos,
        Fraction? leadIn = null, Fraction? initialDefault = null)
    {
        var measures = SplitIntoMeasures(items, startPos, out var voiceSpans);

        // Measure durations in one pass, threading the running default note
        // value (a bare note inherits the previous note's duration across bar
        // lines). Precomputed so the FINAL bar can be compared against the
        // OPENING pickup (anacrusis complement) below.
        var durations = new Fraction[measures.Count];
        var defaultDuration = initialDefault ?? Fraction.Quarter;
        // Where each voice span opened: the beats already elapsed in its bar, and the
        // running default note value there. Voices 2..N sound from that instant, so this
        // is the lead-in their own first bar is validated with.
        var spanEntry = new List<(ParallelExpressionSyntax Span, Fraction LeadIn, Fraction Default)>();
        for (int di = 0; di < measures.Count; di++)
        {
            var barItems = measures[di].Items;
            var total = Fraction.Zero;
            int from = 0;
            foreach (var vs in voiceSpans)
            {
                if (vs.MeasureIndex != di)
                    continue;
                // Count up to the span's item index, snapshot, then carry on — one
                // spelling of the beat count (MeasureDurations), just read twice.
                total += MeasureDurations.CalculateMeasureDuration(
                    barItems.GetRange(from, vs.ItemIndex - from), ref defaultDuration);
                spanEntry.Add((vs.Span, total, defaultDuration));
                from = vs.ItemIndex;
            }
            total += MeasureDurations.CalculateMeasureDuration(
                barItems.GetRange(from, barItems.Count - from), ref defaultDuration);
            durations[di] = total;
        }

        // The bar this stream starts inside may already be part-elapsed (a voice span
        // opened mid-bar); those beats belong to this voice's first bar too.
        if (measures.Count > 0 && leadIn is { } lead && lead != Fraction.Zero)
            durations[0] += lead;

        // A span whose bar never materialized (voice 1 empty at the very end of a stream)
        // still has voices to check; they simply start on the boundary.
        foreach (var vs in voiceSpans)
            if (vs.MeasureIndex >= measures.Count)
                spanEntry.Add((vs.Span, Fraction.Zero, defaultDuration));

        // The opening pickup: the first sounding bar, when it is shorter than a
        // full bar. A legitimately shortened FINAL bar must complete it
        // (pickup + final == one bar); otherwise a short final bar warns.
        int openingPickupIndex = -1;
        var openingPickupDuration = Fraction.Zero;
        bool seenSounding = false;

        for (int i = 0; i < measures.Count; i++)
        {
            var measure = measures[i];

            // A mid-piece \time takes effect at the bar it appears in
            // (LilyPond applies the new meter from that timestep), so adopt
            // the new reference meter before checking this bar's fill — else a
            // valid 3/4 bar after a 4/4 opening is wrongly flagged underfull.
            // A 'partial N' in the bar declares it a pickup of length 1/N, which
            // is then the expected fill for THIS measure only.
            Fraction? partialLength = null;
            foreach (var item in measure.Items)
            {
                if (item is TimeSignatureSyntax ts)
                {
                    if (ts.IsSenzaMisura) _senzaMisura = true;
                    else { _senzaMisura = false; SetTimeSignature(ts.Beats, ts.BeatType); }
                }
                else if (item is PartialDeclarationSyntax pd)
                    partialLength = pd.ToFraction();
            }

            var duration = durations[i];

            // A file-level `partial` describes the PIECE-opening pickup. Blocks
            // are validated per section, so apply it to a block's first bar
            // only when that bar does not already fill the meter (a later
            // section's full first bar is not the pickup). An inline `partial`
            // in the bar always wins.
            if (partialLength == null && i == 0 && _filePartial is { } fp
                && duration != _timeSignature)
            {
                partialLength = fp;
            }

            // The pickup's declared length overrides the meter as the fill target
            // for the measure that carries the \partial.
            var expected = partialLength ?? _timeSignature;

            // Remember the opening pickup: the first sounding bar, when shorter
            // than a full bar (a bare anacrusis or a declared \partial). Its
            // length is what a shortened final bar must complete.
            if (duration != Fraction.Zero && !seenSounding)
            {
                seenSounding = true;
                if (duration < _timeSignature)
                {
                    openingPickupIndex = i;
                    openingPickupDuration = duration;
                }
            }

            if (!_senzaMisura && duration != expected && duration != Fraction.Zero)
            {
                if (duration < expected)
                {
                    bool isFirst = i == 0;
                    bool isLast = i == measures.Count - 1;

                    // A short FINAL bar is exempt ONLY when it completes the
                    // opening pickup (anacrusis: pickup + final == one bar).
                    // Without such a pickup it is a genuine short bar and warns.
                    bool completesOpeningPickup = isLast
                        && openingPickupIndex >= 0 && openingPickupIndex < i
                        && openingPickupDuration + duration == expected;

                    // An underfull FIRST bar written bare is a benefit-of-the-
                    // doubt anacrusis: no strict length check, just a nudge to
                    // declare it. A measure carrying an explicit 'partial N' is
                    // always checked strictly — that is the whole point of \partial.
                    // A voice of a span (leadIn is set) has no opening of its own to
                    // be an anacrusis of: it starts wherever the span opened, and a
                    // pickup is section-wide anyway, so its short bar is a plain
                    // short bar and gets the plain message.
                    bool isBarePickup = isFirst && partialLength == null && leadIn is null;

                    EmitUnderfull(measure, duration, expected, partialLength, completesOpeningPickup, isBarePickup);
                }
                else if (duration > expected)
                {
                    EmitOverfull(measure, duration, expected, partialLength);
                }
            }
        }

        // Voices 2..N of each span, once this stream's own bars are counted: they are
        // simultaneous with the music just validated, so each is its own bar stream that
        // begins with the span's lead-in already elapsed (and inherits the running note
        // value at that instant, as a bare note does anywhere else).
        foreach (var (span, spanLeadIn, spanDefault) in spanEntry)
            foreach (var voice in span.Voices.Skip(1))
                ValidateItemsScoped(ItemsOf(voice), voice.Position, spanLeadIn, spanDefault);
    }

    /// <summary>The music items of one voice block of a span.</summary>
    private static IEnumerable<SyntaxNode> ItemsOf(SyntaxNode voice)
        => voice is MusicBlockSyntax block ? block.Items : [];

    /// <summary>Emits the diagnostic (if any) for a bar shorter than its expected
    /// fill: a hard incomplete-measure warning, a soft pickup-without-partial nudge
    /// for a bare first bar, or nothing when the bar completes the opening pickup.</summary>
    private void EmitUnderfull(MeasureContent measure, Fraction duration, Fraction expected,
        Fraction? partialLength, bool completesOpeningPickup, bool isBarePickup)
    {
        if (partialLength != null || (!completesOpeningPickup && !isBarePickup))
        {
            var span = MeasureDurations.GetSpan(measure.Items);
            _warnedSpans.Add((span.Start, span.Length));
            _diagnostics.Warning(span, DiagnosticCodes.MeasureIncomplete,
                partialLength != null
                    ? $"Pickup measure duration {duration} is less than the declared partial {expected}"
                    : $"Measure duration {duration} is less than time signature {_meterText}");
        }
        else if (isBarePickup)
        {
            // An underfull FIRST bar is conventionally an anacrusis, but written
            // bare it is indistinguishable from a miscount, gets no strict length
            // check, and bar numbering counts it as bar 1. Nudge toward declaring
            // it (a declared pickup is checked exactly and numbered as bar 0).
            var span = MeasureDurations.GetSpan(measure.Items);
            // A pickup is declared as a section directive in a structured file
            // (section/part/form), or as a leading `partial` in a bare note stream. It is
            // NOT allowed at the top level or inside a voice (see PartialScopeValidator).
            string where = _structured
                ? $"as a section directive (e.g. section A {{ {SuggestPartial(duration)}  … }})"
                : $"with a leading '{SuggestPartial(duration)}'";
            _diagnostics.Warning(span, DiagnosticCodes.PickupWithoutPartial,
                $"first measure is shorter than the meter ({duration} of {_meterText}); " +
                $"if this is a pickup, declare it {where} so its length is checked and " +
                "bar numbering starts after it");
        }
        // else: completesOpeningPickup — exempt, no diagnostic.
    }

    /// <summary>Emits the overfull-measure warning (a bar longer than its expected fill).</summary>
    private void EmitOverfull(MeasureContent measure, Fraction duration, Fraction expected, Fraction? partialLength)
    {
        var span = MeasureDurations.GetSpan(measure.Items);
        _warnedSpans.Add((span.Start, span.Length));
        _diagnostics.Warning(span, DiagnosticCodes.MeasureOverflow,
            partialLength != null
                ? $"Pickup measure duration {duration} exceeds the declared partial {expected}"
                : $"Measure duration {duration} exceeds time signature {_meterText}");
    }

    /// <summary>True when the node sits inside a music block (an in-music
    /// `partial`/`time` belongs to one voice/section; only a top-level one is
    /// file-wide). A PART-MAJOR section holds its music inline with no MusicBlock
    /// wrapper, so a `partial`/`time` written among that inline music counts as
    /// in-music too — otherwise it leaks as a file-wide pickup onto every section.
    /// A SECTION-MAJOR section holds part blocks; its section-level `time`/`partial`
    /// arms the meter for the whole section and is left to the top-level path, so
    /// that is NOT treated as "inside a block" here.</summary>
    private static bool IsInsideMusicBlock(SyntaxNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
        {
            if (p is MusicBlockSyntax)
                return true;
            if (p is SectionDeclarationSyntax s && SectionHasInlineMusic(s))
                return true;
        }
        return false;
    }

    /// <summary>True for a PART-MAJOR section with actual inline music (note/bar children),
    /// false for a SECTION-MAJOR section (part blocks), a directives-only header
    /// (<c>section A { partial 2 }</c>), or an empty one. Mirrors the collector's
    /// <c>SectionHasInlineMusic</c> — the two must not drift, or a directives-only header's
    /// <c>partial</c>/<c>time</c> is classed as in-music here and dropped as a section-wide
    /// pickup (a real bug: the pickup rendered fine but the bar was flagged short).</summary>
    private static bool SectionHasInlineMusic(SectionDeclarationSyntax section)
    {
        for (int i = 0; i < section.SlotCount; i++)
        {
            var child = section.GetChild(i);
            if (child is null or SyntaxTokenNode) continue;
            // Part / chord / lyric blocks are section-major or track cells, not inline music.
            if (child is PartBlockSyntax or ChordPartBlockSyntax or LyricsBlockSyntax) continue;
            // A section-level directive (`partial`/`time`/`key`/`clef`/octave/grob) arms the
            // whole section; it does NOT make the section inline music.
            if (child is KeySignatureSyntax or TimeSignatureSyntax or TempoDeclarationSyntax
                or PartialDeclarationSyntax or ClefDeclarationSyntax or OctaveDirectiveSyntax
                or OverrideDeclarationSyntax or RevertDeclarationSyntax or OnceModifierSyntax)
                continue;
            return true; // a music node
        }
        return false;
    }

    /// <summary>The `partial` clause matching a pickup of <paramref name="length"/>:
    /// exact for plain (1/N) and dotted (3/2N) lengths, a generic hint otherwise.</summary>
    private static string SuggestPartial(Fraction length)
    {
        if (length.Numerator == 1)
            return $"partial {length.Denominator}";
        if (length.Numerator == 3 && length.Denominator % 2 == 0)
            return $"partial {length.Denominator / 2}.";
        return "partial <duration>";
    }

    private record MeasureContent(List<SyntaxNode> Items, int StartPosition);

    /// <summary>A voice span met while splitting: the measure it opened in, the item index
    /// within that measure's (already inlined) items where it opened, and the span node.</summary>
    private readonly record struct VoiceSpan(int MeasureIndex, int ItemIndex, ParallelExpressionSyntax Span);

    private List<MeasureContent> SplitIntoMeasures(IEnumerable<SyntaxNode> blockItems, int blockStartPos,
        out List<VoiceSpan> voiceSpans)
    {
        var measures = new List<MeasureContent>();
        var currentItems = new List<SyntaxNode>();
        var spans = new List<VoiceSpan>();
        int startPos = blockStartPos;

        void AddItems(IEnumerable<SyntaxNode> items)
        {
            foreach (var item in items)
            {
                if (item is BarlineSyntax)
                {
                    if (currentItems.Count > 0)
                    {
                        measures.Add(new MeasureContent(currentItems, startPos));
                        currentItems = [];
                    }
                    startPos = item.Position + item.FullWidth;
                }
                else if (item is NestedVoiceRecoverySyntax)
                {
                    // LYS0010 recovery: the nested voice's braces are
                    // transparent — its content counts in THIS voice's bars.
                    for (int ci = 0; ci < item.SlotCount; ci++)
                        if (item.GetChild(ci) is MusicBlockSyntax inner)
                            AddItems(inner.Items);
                }
                else if (item is ParallelExpressionSyntax par)
                {
                    // A voice span is SIMULTANEOUS music, not a sequence. The collector
                    // walks voice 1 INLINE in this stream — barlines and all — and
                    // reconstructs voices 2..N as their own tracks over the same bars
                    // (MeasureCollector.ProcessMusicNode, the ParallelExpressionSyntax
                    // case). Count it the same way. Before this the span was one item of
                    // zero duration, so the bar it sat in was never checked at all:
                    // `voice { c d e f } e f g a` drew eight quarters in one 4/4 bar and
                    // said nothing, while the bare spelling warns LYS2002.
                    spans.Add(new VoiceSpan(measures.Count, currentItems.Count, par));
                    if (par.Voices.FirstOrDefault() is MusicBlockSyntax lead)
                        AddItems(lead.Items);
                }
                else
                {
                    currentItems.Add(item);
                }
            }
        }
        AddItems(blockItems);

        // Add final measure if not empty
        if (currentItems.Count > 0)
        {
            measures.Add(new MeasureContent(currentItems, startPos));
        }

        voiceSpans = spans;
        return measures;
    }
}
