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
/// Warns when a rehearsal mark is WRITTEN and this score does not print it.
/// </summary>
/// <remarks>
/// ⚠️ WHY A FAMILY THAT WORKS NEEDS A WARNING AT ALL. Until 2026-08-30 a <c>@mark("A")</c>
/// written inside a container that owns its own walk — an inline ending, a tuplet, a repeat,
/// a cue — was dropped by the collector and NOTHING said so: no box, no letter, no
/// diagnostic. It cost 45 of one reader's books 120 letters, and the only reason it was
/// found is that the reader looked at a chart. The drop is fixed (the mark is now built at
/// its host note, beside every other note-attached mark); this validator is the reader's
/// decision that the family should answer "it is not drawn" the way the SPAN family already
/// does — <b>say where</b> — so the next way to lose one cannot be silent either.
/// <para>
/// ⚠️ IT NEEDS THE TREE, unlike its neighbours, and that is the point rather than an
/// inconvenience: the marks it must report are the ones the collect never SAW. A mark in a
/// <c>section</c> no form plays is not a mark the collector dropped — the walk never went
/// there — so nothing in <see cref="MeasureCollector"/> can name it. The written side has to
/// come from the source. <see cref="ISharedCollectValidator.ValidateWith"/> carries both.
/// </para>
/// <para>
/// What this catches, MEASURED the day it was written (2026-08-30, the 263 books on disk
/// that write <c>@mark</c>, 2244 marks): a mark in an unplayed <c>section</c>, a mark in a
/// <c>part</c> no score renders, and a mark on a GRACE note. That last one is not a mark
/// defect and must not be fixed here: <c>MeasureCollector.CollectGraceNotes</c> reads pitch
/// and duration only, so a grace note carries NO annotation of any kind — <c>@staccato</c>
/// and <c>@text</c> are dropped there too (docs/HANDOFF.md §2). This warning is what makes
/// that hole audible; closing it is its own trip.
/// </para>
/// <para>
/// ⚠️ NO DIAGNOSTIC IN THE OTHER DIRECTION. A mark that prints TWICE is impossible to write:
/// the collector keeps one item per source position, which is the rule a part drawn on both
/// a staff and a tab depends on.
/// </para>
/// </remarks>
internal sealed class RehearsalMarkEngravedValidator : ISharedCollectValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree) =>
        ValidateWith(tree, new System.Lazy<MeasureCollector?>(
            () => SemanticValidation.TryCollect(tree)));

    public void ValidateWith(SyntaxTree tree, System.Lazy<MeasureCollector?> sharedCollect)
    {
        // A malformed score (null collector) surfaces its real error elsewhere, and it would
        // otherwise report EVERY mark in the file — a book whose '@mark' does not parse at
        // all already prints three errors per mark without help from here.
        var collector = sharedCollect.Value;
        if (collector == null)
            return;

        // ONE walk. An early "does this book write any mark at all" test would be a SECOND
        // walk of the same tree for every book that does, which is the population that
        // matters; the books that write none pay one pass over a tree the parser just built.
        var engraved = collector.EngravedRehearsalMarkPositions;
        foreach (var node in tree.GetRoot().DescendantNodes().OfType<MusicMarkSyntax>())
        {
            if (AnnotationValues.Rehearsal(node, out _) is null)
                continue;
            if (engraved.Contains(node.SourceStart))
                continue;
            // ASCII punctuation only: these strings reach legacy-codepage consoles via the CLI.
            _diagnostics.Warning(new TextSpan(node.SourceStart, 1),
                DiagnosticCodes.UnengravedRehearsalMark,
                "this rehearsal mark is not printed by this score - the music it is written "
                + "in is either not played by the form, not rendered by any staff here, or a "
                + "grace note (a grace note carries no annotations at all)");
        }
    }
}
