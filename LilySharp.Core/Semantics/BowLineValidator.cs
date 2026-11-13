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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// <c>@slur(dashed)</c> and <c>@tie(dashed)</c> (and <c>dotted</c>; owner-approved 2026-11-13,
/// grammar evaluation 3): the annotation is written on the note where the slur or the tie
/// BEGINS — <c>c4@slur(dashed)( d e)</c>, <c>c2@tie(dashed)~ c2</c> — and says how that one
/// curve is drawn. What is refused: an argument that is neither word
/// (<see cref="DiagnosticCodes.BowLineMalformed"/>), and a note no slur / no tie begins on
/// (<see cref="DiagnosticCodes.BowLineWithoutBow"/>), where the annotation would do nothing
/// without a word. A PHRASING slur's line, <c>@phrasingSlur(dashed)</c> (owner-approved
/// 2026-11-13), is the curve's own start, so only its word is judged (the first code).
/// </summary>
/// <remarks>
/// The second is read off the collect (<see cref="MusicItem.SlurLine"/> against the item's own
/// slur-start flag), because whether a <c>(</c> opens a slur on a note is the collector's to
/// say — a marker run folds onto its note there, a chord's member may carry the mark, a tuplet
/// has its own arm.
/// <para>
/// ⚠️ OF THE FIRST SCORE, as every shared-collect validator is: a part only a later score
/// draws is not judged.
/// </para>
/// LILYSHARP-OWN: the spelling. LilyPond's is a switch before the music
/// (<c>\slurDashed … \slurSolid</c>), which the twin writes as <c>\once</c>.
/// </remarks>
internal sealed class BowLineValidator : ISharedCollectValidator
{
    private readonly List<Diagnostic> _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public void Validate(SyntaxTree tree) =>
        ValidateWith(tree, new System.Lazy<MeasureCollector?>(() => SemanticValidation.TryCollect(tree)));

    public void ValidateWith(SyntaxTree tree, System.Lazy<MeasureCollector?> sharedCollect)
    {
        // The well-written ones, by their host's offset — the address the page's item carries.
        List<(MusicMarkSyntax Mark, bool Slur, SyntaxNode? Host)>? written = null;
        foreach (var mark in tree.GetRoot().DescendantNodes<MusicMarkSyntax>())
        {
            // A phrasing slur's line (owner-approved 2026-11-13): the mark is the curve's own
            // start, so there is no "nothing begins here" to judge — only the word.
            if (AnnotationValues.OpensPhrasingSlurWithALine(mark))
            {
                if (AnnotationValues.BowLineOf(mark, mark.Name) is null)
                {
                    string word = string.Join(" ", mark.Arguments.Select(a => a.Text));
                    // ASCII punctuation only: these strings reach legacy-codepage consoles via the CLI.
                    _diagnostics.Add(Diagnostic.Error(mark.Span, DiagnosticCodes.BowLineMalformed,
                        (word.Length == 0 ? $"'@{mark.Name}()' names no line" : $"'{word}' is not a line a phrasing slur is drawn with")
                        + $" - write '@{mark.Name}(solid)', '@{mark.Name}(dashed)' or '@{mark.Name}(dotted)', or the bare '@{mark.Name}' for a solid one "
                        + "(c4@phrasingSlur(dashed) d e f@!phrasingSlur). The phrasing slur is drawn solid."));
                }
                continue;
            }
            if (mark.IsSpanEnd || mark.Name is not (AnnotationValues.SlurLineName or AnnotationValues.TieLineName))
                continue;
            bool slur = mark.Name == AnnotationValues.SlurLineName;
            if (AnnotationValues.BowLineOf(mark, mark.Name) is null)
            {
                string wrote = string.Join(" ", mark.Arguments.Select(a => a.Text));
                // ASCII punctuation only: these strings reach legacy-codepage consoles via the CLI.
                _diagnostics.Add(Diagnostic.Error(mark.Span, DiagnosticCodes.BowLineMalformed,
                    (wrote.Length == 0 ? $"'@{mark.Name}()' names no line" : $"'{wrote}' is not a line a {What(slur)} is drawn with")
                    + $" - write '@{mark.Name}(solid)', '@{mark.Name}(dashed)' or '@{mark.Name}(dotted)', on the note where the {What(slur)} begins "
                    + (slur ? "(c4@slur(dashed)( d e))" : "(c2@tie(dashed)~ c2)")
                    + $". The {What(slur)} is drawn solid."));
                continue;
            }
            // On a member of a chord the host is no item of its own.
            var host = mark.Parent is { Parent: ChordSyntax } ? null : mark.Parent;
            (written ??= new()).Add((mark, slur, host));
        }
        if (written is null)
            return;

        Dictionary<int, MusicItem>? items = null;
        if (written.Any(w => w.Host != null) && sharedCollect.Value?.Collected is { } score)
        {
            items = new();
            foreach (var group in score.StaffGroups)
                foreach (var staff in group.Staves)
                    foreach (var voice in staff.Voices)
                        foreach (var measure in voice.Measures)
                            foreach (var item in measure.Items)
                                if (item.SourcePosition >= 0 && (item.SlurLine != BowLine.Solid || item.TieLine != BowLine.Solid))
                                    items.TryAdd(item.SourcePosition, item);
        }

        foreach (var (mark, slur, host) in written)
        {
            if (host is null)
            {
                Without(mark, slur, "it is written on one note inside a chord; write it after the chord");
                continue;
            }
            if (host is RestSyntax)
            {
                Without(mark, slur, "it is written on a rest");
                continue;
            }
            // Not on the page of the first score (another score's part), or in grace time: not judged.
            if (items is null || !items.TryGetValue(host.SourceStart, out var item))
                continue;
            bool begins = slur
                ? item is NoteItem { HasSlurStart: true } or ChordItem { HasSlurStart: true }
                : item is NoteItem { HasTieStart: true } or ChordItem { HasTieStart: true };
            if (!begins)
                Without(mark, slur, slur ? "no '(' opens a slur on this note" : "no '~' ties this note on");
        }
    }

    private static string What(bool slur) => slur ? "slur" : "tie";

    private void Without(MusicMarkSyntax mark, bool slur, string why)
        => _diagnostics.Add(Diagnostic.Error(mark.Span, DiagnosticCodes.BowLineWithoutBow,
            $"this '@{mark.Name}(...)' draws nothing - {why}. It says how the {What(slur)} that BEGINS on its note is drawn, so "
            + "write it on the note with the "
            + (slur ? "'(': c4@slur(dashed)( d e)." : "'~': c2@tie(dashed)~ c2.")));
}
