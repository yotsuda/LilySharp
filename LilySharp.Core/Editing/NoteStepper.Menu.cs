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

namespace LilySharp.Core.Editing;

/// <summary>
/// What the preview's right-click menu does to a note (owner's choice 2026-11-03: with the drags, a
/// score made can be put right with the mouse). Each answers the whole text as it should be, or
/// null when there is nothing it can do there. Every other pitch keeps sounding where it was, as
/// for a drag: a rest hangs on nothing and a chord's first member is what the rest hang on, so
/// taking one away moves what came after it unless its marks are put right.
/// </summary>
public static partial class NoteStepper
{
    /// <summary>The note, or the whole chord <paramref name="at"/> is a member of, as a rest of its
    /// length. What was written on it goes with it: a staccato on a rest means nothing.</summary>
    public static string? NoteToRest(string text, SyntaxTree tree, int at, Func<string, SyntaxTree>? expand = null)
    {
        var carrier = NoteOrChordAt(tree, at);
        if (carrier == null)
            return null;
        SyntaxNode? duration = carrier is NoteSyntax note ? note.Duration : ((ChordSyntax)carrier).Duration;
        string rest = "r" + (duration != null ? text[duration.Span.Start..duration.Span.End] : "");
        int end = carrier.Span.End;
        // A tie goes with the note it was to carry on from.
        if (end < text.Length && text[end] == '~')
            end++;
        string after = text[..carrier.Span.Start] + rest + text[end..];
        var gone = carrier.DescendantNodes<PitchSyntax>().Select(p => p.SourceStart).ToHashSet();
        return HoldAfter(text, tree, after, gone, -1, 0, expand);
    }

    /// <summary>A member taken out of its chord; one left is a note again.</summary>
    public static string? RemoveChordMember(string text, SyntaxTree tree, int at, Func<string, SyntaxTree>? expand = null)
    {
        var pitch = PitchesInOrder(tree).FirstOrDefault(p => p.SourceStart == at);
        if (pitch?.Parent is not ChordSyntax chord || chord.IsShapeChord)
            return null;
        var members = chord.DescendantNodes<PitchSyntax>().Where(p => p.Parent == chord).ToList();
        if (members.Count < 2)
            return null;
        // With the space before it, or after it when it is first.
        int start = pitch.SourceStart, end = pitch.Span.End;
        while (end < text.Length && text[end] is '\'' or ',')
            end++;
        if (members[0] == pitch)
            while (end < text.Length && text[end] == ' ')
                end++;
        else
            while (start > 0 && text[start - 1] == ' ')
                start--;
        string after = text[..start] + text[end..];
        var gone = new HashSet<int> { pitch.SourceStart };
        // One left, and nothing on the bracket: a note, as it would have been written.
        if (members.Count == 2 && SyntaxTree.Parse(after).GetNodes<ChordSyntax>().FirstOrDefault(c => c.SourceStart == chord.SourceStart) is { } left
            && CloseBracket(left) is { } close && MarksEnd(after, close.Span.End) == close.Span.End
            && left.DescendantNodes<PitchSyntax>().SingleOrDefault() is { } only
            && after[(left.SourceStart + 1)..close.Span.Start].Trim() == after[only.SourceStart..MarksEnd(after, NameEnd(only))])
        {
            string note = after[only.SourceStart..MarksEnd(after, NameEnd(only))];
            after = after[..left.SourceStart] + note + after[close.Span.End..];
            // The note stands where the bracket stood: same order, same place in the walk.
        }
        return HoldAfter(text, tree, after, gone, -1, 0, expand);
    }

    /// <summary>A rest as a note of its length, at the pitch of the note before it, there being
    /// nothing better to guess: from there a drag takes it where it should be.</summary>
    public static string? RestToNote(string text, SyntaxTree tree, int at, Func<string, SyntaxTree>? expand = null)
    {
        var rest = tree.GetNodes<RestSyntax>().FirstOrDefault(r => r.SourceStart == at);
        if (rest == null || at >= text.Length || text[at] != 'r')
            return null;
        var before = PitchesInOrder(tree).LastOrDefault(p => p.SourceStart < at && p.Parent is NoteSyntax or ChordSyntax);
        int want = -1;
        string name = "c'";
        if (before != null)
        {
            if (WrittenMidi(text, tree, expand).TryGetValue(before.SourceStart, out int heard))
                want = heard;
            name = text[before.SourceStart..NameEnd(before)];
        }
        string after = text[..at] + name + text[(at + 1)..];
        return HoldAfter(text, tree, after, [], at, want, expand);
    }

    /// <summary>A mark put on the note or the chord, or taken off if it has it. A dynamic stands
    /// alone on a note (LYS: a second is not printed), so another is replaced.</summary>
    public static string? ToggleNoteMark(string text, SyntaxTree tree, int at, string word, bool isDynamic)
    {
        var carrier = NoteOrChordAt(tree, at);
        if (carrier == null)
            return null;
        var marks = Enumerable.Range(0, carrier.SlotCount).Select(carrier.GetChild).Where(c => c != null).ToList();
        if (isDynamic)
        {
            foreach (var dynamic in marks.OfType<DynamicSyntax>())
            {
                string has = dynamic.DynamicToken.Text;
                if (!DynamicSteps(has))
                    continue;
                if (has == word)
                    return text[..dynamic.Span.Start] + text[dynamic.Span.End..];
                return text[..dynamic.DynamicToken.Span.Start] + word + text[dynamic.DynamicToken.Span.End..];
            }
        }
        else
        {
            foreach (var mark in marks.OfType<ArticulationSyntax>())
            {
                if (SpellingOf(text, mark) is not { } spelling)
                    continue;
                if (spelling.Spelling == word)
                    return text[..mark.Span.Start] + text[mark.Span.End..];
            }
        }
        int end = carrier.Span.End;
        string result = text[..end] + "@" + word + text[end..];
        return SyntaxTree.Parse(result).HasErrors && !tree.HasErrors ? null : result;
    }

    /// <summary>The note a pitch at <paramref name="at"/> is, or the chord it is a member of.</summary>
    private static SyntaxNode? NoteOrChordAt(SyntaxTree tree, int at)
    {
        var pitch = PitchesInOrder(tree).FirstOrDefault(p => p.SourceStart == at);
        return pitch?.Parent switch
        {
            NoteSyntax note => note,
            ChordSyntax { IsShapeChord: false } chord => chord,
            _ => null,
        };
    }

    /// <summary>
    /// <paramref name="after"/> with its marks put right, every pitch of <paramref name="text"/>
    /// not in <paramref name="gone"/> where it was, and a new pitch at <paramref name="added"/>
    /// at <paramref name="addedWant"/> if that is known. Null when the edit broke the text.
    /// </summary>
    private static string? HoldAfter(string text, SyntaxTree tree, string after, HashSet<int> gone, int added, int addedWant, Func<string, SyntaxTree>? expand)
    {
        if (!tree.HasErrors && SyntaxTree.Parse(after).HasErrors)
            return null;
        var was = WrittenMidi(text, tree, expand);
        var old = PitchesInOrder(tree).Where(p => !gone.Contains(p.SourceStart)).ToList();
        string result = after;
        for (int round = 0; round < MaxOctaveRounds; round++)
        {
            var all = PitchesInOrder(SyntaxTree.Parse(result));
            var now = all.Where(p => p.SourceStart != added).ToList();
            if (now.Count != old.Count)
                break;
            var midi = WrittenMidi(result, null, expand);
            // In written order: what the first one off hangs on is never later than it.
            var pairs = all.Select(p => p.SourceStart == added
                ? (Now: p, Target: addedWant >= 0 ? addedWant : (int?)null)
                : (Now: p, Target: was.TryGetValue(old[now.IndexOf(p)].SourceStart, out int w) ? w : (int?)null));
            bool corrected = false;
            foreach (var (p, target) in pairs)
            {
                if (target is not { } t || !midi.TryGetValue(p.SourceStart, out int heard))
                    continue;
                int off = heard - t;
                if (off == 0 || off % 12 != 0)
                    continue;
                int end = NameEnd(p);
                result = result[..end] + Marks(p.OctaveOffset - off / 12) + result[MarksEnd(result, end)..];
                corrected = true;
                break;
            }
            if (!corrected)
                break;
        }
        return result;
    }
}
