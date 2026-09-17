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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Warns when a section's plain (unbracketed) lyric verse is fully shadowed by the
/// section's <c>[N. …]</c> verses. A plain line only fills an occurrence that NO bracket
/// covers; if every written-out occurrence already has a numbered verse, the plain words
/// never render, which is silent and easy to miss (a miscounted bracket, or a leftover
/// line). Naming an occurrence for the plain line, or removing it, clears the warning.
/// </summary>
/// <remarks>
/// Whether the plain line is reached depends on how many times the section is written out
/// in the form, which <see cref="MeasureCollector"/> counts while placing the verse under
/// its staff (<see cref="MeasureCollector.LyricShadowedPlainWarnings"/>). This reads that
/// off the shared collect — the render path's own, every staff of the first score with its
/// bound tracks — so a shadowed line under ANY drawn staff is reported.
/// <para>
/// ⚠️ IT USED TO COLLECT THE BOOK ITSELF, a third full collect per settled keystroke
/// (46 of the pass's 110 ms on perf-plain1k, session 399 — on a book with no lyrics at
/// all), under a remark that the shared collect was "no-voice" and under-counted
/// occurrences. That was true of the bare <c>new MeasureCollector().Collect(tree)</c> the
/// validators once shared; since 2026-08-16 the shared collect is the render path's
/// (<see cref="SemanticValidation.TryCollect(SyntaxTree)"/>), which walks every drawn
/// part with its voice bound. Its own collect also looked at the FIRST declared part
/// only; the shared one sees every staff the score draws, which is the whole of what the
/// warning is about (MEASURED over 759 books — fixtures and the Lab corpora — the two
/// answers were identical, both empty; the second-staff case is held by its tests).
/// </para>
/// </remarks>
internal sealed class LyricPlainVerseShadowedValidator : ISharedCollectValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree) =>
        ValidateWith(tree, new System.Lazy<MeasureCollector?>(
            () => SemanticValidation.TryCollect(tree)));

    public void ValidateWith(SyntaxTree tree, System.Lazy<MeasureCollector?> sharedCollect)
    {
        // A malformed score (null collector) surfaces its real error elsewhere.
        var warnings = sharedCollect.Value?.LyricShadowedPlainWarnings;
        if (warnings == null)
            return;

        // A track placed under two staves places its verse twice; one spelling is one
        // fault, so report each source position once.
        foreach (var w in warnings.DistinctBy(w => w.Span.Start))
            // ASCII punctuation only: this exact string reaches legacy-codepage
            // consoles through the CLI.
            _diagnostics.Warning(w.Span, DiagnosticCodes.LyricPlainVerseShadowed,
                $"the plain lyrics in section '{w.SectionName}' are shadowed by its " +
                "[N.] verses; every occurrence already has a numbered verse, so these " +
                "words will not be shown - remove them or give them an occurrence to cover");
    }
}
