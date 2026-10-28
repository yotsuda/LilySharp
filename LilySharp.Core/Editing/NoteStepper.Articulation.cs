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

using System;
using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// The step keys on an articulation (owner's request 2026-10-16: "@upBow and @downBow should
/// switch on Ctrl+Shift+Up / Down; @hammerOn and @pullOff too; and any other meaningful group
/// should rotate"). The semitone pair steps the mark along its group (the octave pair places it,
/// <see cref="PlacementEdit"/>)
/// (<see cref="ArticulationGroups"/>): Up to the next name, Down to the one before. A group
/// of up to four STOPS at its ends (owner's decision, same day: "ふたつかみっつ程度なら
/// ローテートしない方が使いやすい" — so Up always means the same thing: up-bow, hammer-on,
/// longer, stronger, above, away upward); only a group of <see cref="RotatesFrom"/> or more
/// members would wrap round, and none does today. Only the name changes: a <c>.up</c> /
/// <c>.down</c> placement stays, and nothing sounds.
/// </summary>
/// <remarks>
/// The groups are Lily#'s reading of "meaningful": the two the owner named, and the marks that
/// are the same kind of sign in another degree or direction, each written in Up order — bowing
/// (downBow, upBow), legato (pullOff, hammerOn — a hammer-on usually climbs), length
/// (staccatissimo, staccato, portato, tenuto), weight (accent, marcato), fermatas (short, plain,
/// long), the mordent pair (mordent below, prall above), the turn pair (reverseTurn starts
/// below, turn above), the trill pair (trill, pralltriller), the organ pedal pair (heel, toe),
/// the jazz approaches (scoop from below, plop from above) and departures (fall down, doit up),
/// the breathing pair (breath, caesura), the stem's direction (stemDown, stemUp — owner's
/// request 2026-10-17), the ottava's printed spellings (15mb, 8vb, 8va, 15ma) and the tempo
/// words (rall, rit, accel — owner's decisions 2026-10-28), whose <c>@!</c> end follows them.
/// Since the same day a group may also be marks whose order means little, so that a related one
/// is a key away: the ottava's words with their argument form, the pedals, courtesy / editorial,
/// the half ties, harmonic / flageolet, the arpeggio and its bracket — a group lists SPELLINGS,
/// an argument included (<see cref="SpellingOf"/>). A mark whose argument is one of a list —
/// <c>@notehead(x)</c>, <c>@finger(3)</c> — steps that argument (<see cref="ArgumentSteps"/>).
/// <c>@tap</c>, <c>@snapPizz</c>, <c>@stopped</c> and <c>@thumb</c> stand alone, and the status
/// bar says so. LILYSHARP-OWN: an editor convenience with no LilyPond counterpart.
/// </remarks>
public static partial class NoteStepper
{
    /// <summary>The groups the step walks, each in Up order; every name is one Lily# consumes
    /// (<see cref="IsSteppable"/>), so a retired spelling drops out of its group instead of
    /// being written.</summary>
    private static readonly string[][] ArticulationGroups =
        [.. new[]
        {
            new[] { "downBow", "upBow" },
            new[] { "pullOff", "hammerOn" },
            new[] { "staccatissimo", "staccato", "portato", "tenuto" },
            new[] { "accent", "marcato" },
            new[] { "shortFermata", "fermata", "longFermata" },
            new[] { "mordent", "prall" },
            new[] { "reverseTurn", "turn" },
            new[] { "trill", "pralltriller" },
            new[] { "heel", "toe" },
            new[] { "scoop", "plop" },
            new[] { "fall", "doit" },
            new[] { "breath", "caesura" },
            new[] { "stemDown", "stemUp" },
            // The ottava's printed spellings, lowest first (owner's request 2026-10-28); the end
            // that closes the stepped one is renamed with it (Step, SpanNames.EndOf).
            new[] { "15mb", "8vb", "8va", "15ma" },
            // The tempo words, slowest first — Up is faster, as a tempo's number steps (owner's
            // decision 2026-10-28: rall in the group too, so rall and rit can be traded); the
            // @! that closes the stepped one is renamed with it.
            new[] { "rall", "rit", "accel" },
            // Groups whose order means little, so that a related mark is a key away (owner's
            // request 2026-10-28: "並びの順に意味がなくても、ユーザーがなるべく関連する記号を簡単に
            // 見つけられる方が良い"). The ottava's words, lowest first, the argument form among them;
            // the pedals as they stand under the piano, left to right; the two ways of bracketing
            // an accidental; the half ties, the one that looks back below the one that rings on;
            // the two harmonic circles; the arpeggio and its bracket. A pedal's or an ottava's
            // @! end is renamed with it.
            new[] { "quindicesima(bassa)", "ottava(bassa)", "ottava", "quindicesima" },
            new[] { "unaCorda", "sostenuto", "sustain" },
            new[] { "editorial", "courtesy" },
            new[] { "repeatTie", "laissezVibrer" },
            new[] { "flageolet", "harmonic" },
            new[] { "arpeggio", "arpeggio(bracket)" },
        }.Select(g => g.Where(s => IsSteppable(HeadOf(s))).ToArray()).Where(g => g.Length > 1)];

    /// <summary>
    /// The values an argument steps through, in Up order (owner's request 2026-10-28: "notehead
    /// のかっこの中なども up/down で操作できる方が便利"): the completion's lists
    /// (<c>GetAnnotationArgumentCompletions</c>; a test holds the two together). A finger and a
    /// bend go Up to the larger number, a feather Up to the faster beam (as the tempo words do);
    /// the notehead shapes and the plucking fingers have no order, so Down walks the completion's
    /// list, as a keyword's value word steps. Each stops at its ends.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string[]> ArgumentSteps = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["notehead"] = ["xcircle", "slash", "triangle", "diamond", "cross", "x"],
        ["finger"] = ["0", "1", "2", "3", "4", "5"],
        ["pluck"] = ["a", "m", "i", "p"],
        // The words, then the counts AnnotationValues.Bend also takes (1..12 semitones).
        ["bend"] = ["half", "full", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12"],
        ["feather"] = ["left", "right"],
    };

    /// <summary>A name the step may write: one the registry draws, or a plain annotation the
    /// collector consumes by name — <c>@stemUp</c> / <c>@stemDown</c> are a stem's direction, not
    /// an articulation (MeasureCollector reads them; AnnotationNameValidator names them).</summary>
    private static bool IsSteppable(string name)
        => ArticulationRegistry.IsKnown(name) || AnnotationNameValidator.PlainFeatureNames.Contains(name)
           || Svg.Model.MusicMarkItem.ParseMarkName(name) != null;

    /// <summary>The group a spelling is in, or null: a mark in no group has nothing of its own
    /// to step, so the keys step the note it is written on (<see cref="CarrierOf"/>).</summary>
    private static string[]? GroupOf(string spelling)
        => Array.Find(ArticulationGroups, g => Array.IndexOf(g, spelling) >= 0);

    /// <summary>A spelling's name, before its argument: <c>ottava(bassa)</c> → <c>ottava</c>.</summary>
    private static string HeadOf(string spelling)
        => spelling.IndexOf('(') is var open and >= 0 ? spelling[..open] : spelling;

    /// <summary>
    /// A mark as a group lists it, with the range that spells it: an articulation's name, or a
    /// mark's name with its argument as written, blanks dropped (<c>ottava(bassa)</c>,
    /// <c>arpeggio(bracket)</c>) — from the name to the <c>)</c>, so a <c>.up</c> after it stays.
    /// Null for an end (<c>@!8va</c>: it follows its start) or anything else.
    /// </summary>
    private static (string Spelling, int Start, int End)? SpellingOf(string text, SyntaxNode node)
    {
        if (node is ArticulationSyntax a)
            return (a.Name, a.NameToken.Span.Start, a.NameToken.Span.End);
        if (node is not MusicMarkSyntax { IsSpanEnd: false } mark)
            return null;
        SyntaxTokenNode? name = null, close = null;
        for (int i = 0; i < mark.SlotCount; i++)
            if (mark.GetChild(i) is SyntaxTokenNode t)
            {
                if (name == null && t.Kind is not SyntaxKind.At)
                    name = t;
                else if (t.Kind == SyntaxKind.CloseParen)
                    close = t;
            }
        if (name == null)
            return null;
        int end = mark.HasArgumentList && close != null ? close.Span.End : name.Span.End;
        string spelling = string.Concat(text[name.Span.Start..end].Where(c => !char.IsWhiteSpace(c)));
        return (spelling, name.Span.Start, end);
    }

    /// <summary>The argument of a mark whose argument steps (<see cref="ArgumentSteps"/>): its
    /// values and the written value's range, or null.</summary>
    private static (string[] Values, int Start, int End)? SteppedArgumentOf(string text, SyntaxNode node)
    {
        if (node is not MusicMarkSyntax { IsSpanEnd: false, HasArgumentList: true } mark
            || !ArgumentSteps.TryGetValue(mark.Name, out var values))
            return null;
        int open = -1, close = -1;
        for (int i = 0; i < mark.SlotCount; i++)
            if (mark.GetChild(i) is SyntaxTokenNode t)
            {
                if (t.Kind == SyntaxKind.OpenParen && open < 0) open = t.Span.End;
                else if (t.Kind == SyntaxKind.CloseParen) close = t.Span.Start;
            }
        if (open < 0 || close < open)
            return null;
        int start = open, end = close;
        while (start < end && char.IsWhiteSpace(text[start])) start++;
        while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
        return (values, start, end);
    }

    /// <summary>Whether the keys have a step of the mark's own: its spelling is in a group, or its
    /// argument steps.</summary>
    private static bool MarkSteps(string text, SyntaxNode node)
        => SpellingOf(text, node) is { } s && GroupOf(s.Spelling) != null || SteppedArgumentOf(text, node) != null;

    /// <summary>A mark with an argument the caret is on — strictly after its <c>@</c> — whose
    /// spelling or argument steps, or null.</summary>
    private static MusicMarkSyntax? SteppingMarkAt(string text, SyntaxTree tree, int offset)
    {
        foreach (var m in tree.GetNodes<MusicMarkSyntax>())
            if (m.Span.Start < offset && offset <= m.Span.End && MarkSteps(text, m))
                return m;
        return null;
    }

    /// <summary>The size from which a group wraps round instead of stopping at its ends (owner's
    /// decision 2026-10-16: two or three — and four, Lily#'s reading — are easier to use when
    /// Up and Down keep their meaning). No group is that large today.</summary>
    private const int RotatesFrom = 5;

    /// <summary>An articulation the caret at <paramref name="offset"/> is on — strictly after
    /// its <c>@</c>, which also touches the note before it — or null.</summary>
    private static ArticulationSyntax? ArticulationAt(SyntaxTree tree, int offset)
    {
        foreach (var a in tree.GetNodes<ArticulationSyntax>())
            if (a.SourceStart < offset && offset <= a.Span.End)
                return a;
        return null;
    }

    /// <summary>
    /// A mark the caret is on that takes a <c>.up</c> / <c>.down</c> placement — an articulation
    /// (<c>@staccato</c>), a dynamic level (<c>@f</c>, not a hairpin), or <c>@text(…)</c> /
    /// <c>@diagram(…)</c> / <c>@todo(…)</c> — or null. The same marks the editor offers the
    /// placement after (the completion's <c>TakesPlacement</c> and
    /// <see cref="SyntaxFacts.AnnotationTakesPlacementAfterArgument"/>).
    /// </summary>
    private static SyntaxNode? PlacementAt(SyntaxTree tree, int offset)
    {
        if (ArticulationAt(tree, offset) is { } articulation && ArticulationRegistry.IsKnown(articulation.Name))
            return articulation;
        if (DynamicAt(tree, offset) is { } dynamic && LanguageVocabulary.IsDynamicLevel(dynamic.DynamicToken.Text))
            return dynamic;
        foreach (var mark in tree.GetNodes<MusicMarkSyntax>())
            if (SyntaxFacts.AnnotationTakesPlacementAfterArgument(mark.Name) && mark.Span.Start < offset && offset <= mark.Span.End)
                return mark;
        return null;
    }

    /// <summary>
    /// One step of a mark's placement on the octave pair (owner's request 2026-10-28: "直後に
    /// .up/.down をつけることができる装飾の上で ctrl+shift+pgup を押したら、.up/.down を切り替える"):
    /// PageUp raises it — <c>.down</c> → none (the mark's own side) → <c>.up</c> — PageDown lowers
    /// it, each stopping at its end, as the other steps of up to four stops do. The name, a
    /// dynamic's <c>.subito</c> and an argument stay; nothing sounds.
    /// </summary>
    private static (Edit? Edit, string Message) PlacementEdit(string text, SyntaxNode mark, int dir)
    {
        string name = mark switch
        {
            ArticulationSyntax a => a.Name,
            DynamicSyntax d => d.DynamicToken.Text,
            MusicMarkSyntax m => m.Name,
            _ => "",
        };
        string listed = $"@{name}: .up / its own side / .down";
        int end = mark.Span.End;
        string written = text[mark.Span.Start..end];
        int now = written.EndsWith(".up", StringComparison.Ordinal) ? 1
            : written.EndsWith(".down", StringComparison.Ordinal) ? -1 : 0;
        int to = now + dir;
        if (to is > 1 or < -1)
            return (null, listed);
        int from = end - (now == 1 ? ".up".Length : now == -1 ? ".down".Length : 0);
        return (new Edit(from, end, to == 1 ? ".up" : to == -1 ? ".down" : ""), listed);
    }

    /// <summary>One step of an articulation: the edit (null when it has no group, or stands at
    /// an end) and the message — the group's names, nothing more (owner's decision 2026-10-16:
    /// repeating the current name is redundant, and "already the last" was noise), listed from
    /// the top down (owner's request 2026-10-17: "upBow / downBow" reads as Up above Down, the
    /// way the keys go), or why nothing moved.</summary>
    private static (Edit? Edit, string Message) ArticulationEdit(string text, SyntaxNode mark, int dir)
    {
        if (SpellingOf(text, mark) is not { } spelling)
            return (null, "not a mark to step");
        var group = GroupOf(spelling.Spelling);
        if (group == null)
        {
            // A mark whose argument steps: along its values (ArgumentSteps), stopping at the ends.
            if (SteppedArgumentOf(text, mark) is { } argument)
            {
                string head = HeadOf(spelling.Spelling);
                string values = $"@{head}(…): {string.Join(" / ", argument.Values.Reverse())}";
                int now = Array.IndexOf(argument.Values, text[argument.Start..argument.End]);
                int next = now + dir;
                if (now < 0 || next < 0 || next >= argument.Values.Length)
                    return (null, values);
                return (new Edit(argument.Start, argument.End, argument.Values[next]), values);
            }
            string name = HeadOf(spelling.Spelling);
            return (null, IsSteppable(name)
                ? $"@{name}: in no group of marks to rotate through"
                : $"@{name}: not an articulation Lily# knows");
        }
        string listed = string.Join(" / ", group.Reverse());
        int at = Array.IndexOf(group, spelling.Spelling);
        int to = at + dir;
        if (to < 0 || to >= group.Length)
        {
            if (group.Length < RotatesFrom)
                return (null, listed);
            to = (to + group.Length) % group.Length;
        }
        return (new Edit(spelling.Start, spelling.End, group[to]), listed);
    }

    /// <summary>The name a stepped mark's <c>@!</c> end is to be renamed to — the head of its new
    /// spelling, when its end carries its old head (<c>@ottava … @!ottava</c> stepped to
    /// <c>@quindicesima(bassa)</c> ends at <c>@!quindicesima</c>) — with that end, or null.</summary>
    private static (SyntaxTokenNode End, string Name)? RenamedEnd(string text, SyntaxNode mark, Edit edit)
    {
        if (SpellingOf(text, mark) is not { } spelling || SpanNames.EndOf(mark) is not { } end
            || end.Name != HeadOf(spelling.Spelling) || edit.Start < spelling.Start || edit.End > spelling.End)
            return null;
        string after = text[spelling.Start..edit.Start] + edit.NewText + text[edit.End..spelling.End];
        string head = HeadOf(after);
        return head == end.Name ? null : (end.NameToken, head);
    }
}
