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
/// Refuses a <c>@harmonic(12)</c> on a note that is not an open string of its part's tuning
/// (<see cref="DiagnosticCodes.HarmonicNotAnOpenString"/>, LYS4073; owner-approved 2026-11-13,
/// grammar evaluation 3). The writer writes the OPEN STRING the harmonic is played on —
/// <c>e,4@harmonic(12)</c> — and the page shows what it sounds; a note written as the sounding
/// pitch, or on a string the part does not have, is the mistake this names.
/// </summary>
/// <remarks>
/// Read off the collect (<see cref="NoteItem.HarmonicOffString"/>), because whether a note is
/// an open string is a question about its PITCH — its relative octave, its part's transposition,
/// its tuning (a tuning written out included) and the file's capo, under which the open strings
/// are the capo's — and the page is where all of those are resolved.
/// <para>
/// ⚠️ OF THE FIRST SCORE, as every shared-collect validator is: a part only a later score
/// draws is not judged.
/// </para>
/// </remarks>
internal sealed class HarmonicOpenStringValidator : ISharedCollectValidator
{
    private readonly List<Diagnostic> _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public void Validate(SyntaxTree tree) =>
        ValidateWith(tree, new System.Lazy<MeasureCollector?>(() => SemanticValidation.TryCollect(tree)));

    public void ValidateWith(SyntaxTree tree, System.Lazy<MeasureCollector?> sharedCollect)
    {
        // Cheap exit: no natural harmonic, nothing to ask — and no collect to pay for. The
        // marks are kept by their note's offset, which is the address the page's item carries.
        Dictionary<int, MusicMarkSyntax>? marks = null;
        foreach (var mark in tree.GetRoot().DescendantNodes<MusicMarkSyntax>())
            if (mark.Parent is NoteSyntax host && Music.Harmonics.FretOn(host) > 0 && AnnotationValues.HarmonicFret(mark) is not null)
                (marks ??= new())[host.SourceStart] = mark;
        if (marks is null || sharedCollect.Value?.Collected is not { } score)
            return;

        var said = new HashSet<int>();
        foreach (var group in score.StaffGroups)
            foreach (var staff in group.Staves)
                foreach (var voice in staff.Voices)
                    foreach (var measure in voice.Measures)
                        foreach (var item in measure.Items)
                        {
                            if (item is not NoteItem { HarmonicOffString: true } note
                                || !marks.TryGetValue(note.SourcePosition, out var mark) || !said.Add(note.SourcePosition))
                                continue;
                            // ASCII punctuation only: these strings reach legacy-codepage consoles via the CLI.
                            _diagnostics.Add(Diagnostic.Error(mark.Span, DiagnosticCodes.HarmonicNotAnOpenString,
                                $"'@harmonic({note.HarmonicFret})' is written on the OPEN STRING the harmonic is played on, and this "
                                + "note is not an open string of the part's tuning (with a capo, the strings as the capo "
                                + "leaves them). Write the string's own note - on a guitar e,4@harmonic(12) is the sixth "
                                + "string touched at fret 12, sounding an octave higher - and the staff shows what it "
                                + "sounds. The harmonic is drawn and played above the note written all the same."));
                        }
    }
}
