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
/// Warns at a rehearsal mark (<c>@mark("A")</c>) that a section label at the same bar keeps
/// off the page (LYS4021). The page engraves one mark a moment and it is the label — the
/// rule LilyPond's <c>Mark_tracking_translator</c> applies to the twin, which writes the
/// label's <c>\mark</c> first — and LilyPond says "discarding event" at the one it drops;
/// this is that sentence.
/// </summary>
/// <remarks>
/// ⚠️ THE LIST IS THE COLLECT'S, NOT THIS VALIDATOR'S. Which marks the page leaves out is
/// decided by <c>Svg.Layout.MusicMarkEngraver.ShadowedBySectionLabel</c> on the finished
/// score (its marks, the primary staff's measures, the layout plan), and
/// <see cref="MeasureCollector.RecordShadowedRehearsalMarks"/> asks that same predicate of
/// the same score at the one exit every collect takes. A second spelling of the rule here
/// — "a mark whose measure has a label" read off the tree — would drift from the page the
/// first time the layout plan or the primary staff mattered. So this only reads the list.
/// <para>
/// ⚠️ It is NOT LYS4019's silence: that validator asks whether the collect PRODUCED the mark,
/// and it did — the item is in the score's marks. Only the layout leaves it out, which is
/// why the two cannot report the same mark.
/// </para>
/// </remarks>
internal sealed class ShadowedRehearsalMarkValidator : ISharedCollectValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree) =>
        ValidateWith(tree, new System.Lazy<MeasureCollector?>(
            () => SemanticValidation.TryCollect(tree)));

    public void ValidateWith(SyntaxTree tree, System.Lazy<MeasureCollector?> sharedCollect)
    {
        // A malformed score (null collector) surfaces its real error elsewhere.
        var collector = sharedCollect.Value;
        if (collector == null)
            return;

        // One warning a written mark: a repeated section plays the same mark at every
        // pass, and every pass opens with the same label.
        var seen = new HashSet<int>();
        foreach (var w in collector.ShadowedRehearsalMarks)
        {
            if (!seen.Add(w.SourcePosition))
                continue;
            // ASCII punctuation only: these strings reach legacy-codepage consoles via the CLI.
            _diagnostics.Warning(new TextSpan(w.SourcePosition, 1),
                DiagnosticCodes.RehearsalMarkShadowedBySectionLabel,
                $"this rehearsal mark \"{w.MarkText}\" is not printed: the section label \"{w.Label}\" "
                + "opens the same bar, and one mark is printed at a bar (LilyPond keeps the label "
                + "and discards the other) - remove the @mark, or hide the label with ~"
                + " before the section's name in the form");
        }
    }
}
