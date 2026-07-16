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
/// Warns when a section's plain (unbracketed) lyric verse is fully shadowed by the
/// section's <c>[N. …]</c> verses. A plain line only fills an occurrence that NO bracket
/// covers; if every written-out occurrence already has a numbered verse, the plain words
/// never render, which is silent and easy to miss (a miscounted bracket, or a leftover
/// line). Naming an occurrence for the plain line, or removing it, clears the warning.
/// </summary>
/// <remarks>
/// Whether the plain line is reached depends on how many times the section is written out
/// in the form, which <see cref="MeasureCollector"/> only counts with a voice bound — the
/// shared no-voice collect skips part-major music and UNDER-counts occurrences, which
/// would turn a genuinely-used plain line into a false positive. So, like
/// <see cref="NavigationPlacementValidator"/>, this collects the primary part itself and
/// reads back the placements recorded as a side effect
/// (<see cref="MeasureCollector.LyricShadowedPlainWarnings"/>).
/// </remarks>
internal sealed class LyricPlainVerseShadowedValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        // The occurrence count is only accurate when the form is walked with a voice
        // bound. The first declared part names the primary voice.
        var root = tree.GetRoot();
        string? voice = root.DescendantNodes().OfType<PartDeclarationSyntax>().FirstOrDefault()?.Name.Text;

        // Lyrics attach EXPLICITLY (`staff X with lyrics NAME`) — there is no auto-attach,
        // so resolve the tracks the score binds to this voice and collect them; otherwise
        // no verse is aligned and a genuinely-shadowed plain line goes unreported.
        IReadOnlyList<string>? attachedLyrics = null;
        var spec = Svg.Collector.RenderSpecParser.FindFirst(tree);
        if (spec != null && voice != null)
        {
            var names = spec.GetVoiceBindings()
                .Where(b => b.VoiceName == voice)
                .SelectMany(b => b.WithLyrics)
                .Distinct()
                .ToList();
            if (names.Count > 0)
                attachedLyrics = names;
        }

        Svg.Collector.MeasureCollector collector;
        try
        {
            collector = new Svg.Collector.MeasureCollector();
            collector.Collect(tree, voice, attachedLyricParts: attachedLyrics);
        }
        catch
        {
            return; // a malformed score surfaces its real error elsewhere
        }

        foreach (var w in collector.LyricShadowedPlainWarnings)
            // ASCII punctuation only: this exact string reaches legacy-codepage
            // consoles through the CLI.
            _diagnostics.Warning(w.Span, DiagnosticCodes.LyricPlainVerseShadowed,
                $"the plain lyrics in section '{w.SectionName}' are shadowed by its " +
                "[N.] verses; every occurrence already has a numbered verse, so these " +
                "words will not be shown - remove them or give them an occurrence to cover");
    }
}
