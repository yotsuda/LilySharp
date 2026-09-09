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
/// The bars a SECTION BOUNDARY splits: a section whose last bar is short, followed in the
/// form by a section whose first bar is the rest of that bar. Written that way when a
/// repeat sign or a volta bracket stands mid-bar — <c>|: A [1. B] :| [2. C]</c> where A ends
/// on the half bar and every ending opens with the other half (the reader's Disco Inferno,
/// 2026-09-09) — and the two halves are ONE bar of the music, so neither is a short bar to
/// warn about (LYS2001 on the last, LYS2006 on the first).
/// </summary>
/// <remarks>
/// <para>
/// The question is asked of the FORM, not of the text order: B's predecessors are the
/// sections played right before it in every play of every form, and B's first bar is exempt
/// only when EVERY one of them ends short by exactly the complement, in the same part; A's
/// last bar likewise only when every successor opens with its complement. One odd neighbour
/// and both warnings stand — this exempts the split bar, not short bars in general.
/// </para>
/// <para>
/// The play order comes from <see cref="FormWalk"/> (the one reader of a form's spellings);
/// a repeat block plays its body once per turn with the turn's ending after it (the count
/// is the written <c>:|*N</c>, else the number of endings, else 2 — the collector's rule).
/// The bar lengths come from <see cref="MeasureModel.Split"/> over the neighbour's cell for
/// the same part, under the meter of the bar being judged; a section that has no cell for
/// the part is padded by the collector and completes nothing.
/// </para>
/// <para>
/// LILYPOND-REF: lily/repeat-acknowledge-engraver.cc, lily/volta-engraver.cc — a repeat sign
/// and a volta bracket stand at whatever moment the music reaches; LilyPond has no notion of
/// a section, so a bar running from <c>\repeat volta</c>'s body into its <c>\alternative</c>
/// is simply one bar and its bar check passes. Lily#'s section is a bar-line boundary, so
/// the same bar arrives here as two short ones; this class is how the check learns that.
/// </para>
/// </remarks>
internal sealed class SectionBoundaryBars
{
    private readonly SyntaxNode _root;
    private readonly IReadOnlyDictionary<string, SyntaxNode> _phraseBodies;
    private Dictionary<string, HashSet<string>>? _before;
    private Dictionary<string, HashSet<string>>? _after;
    private readonly Dictionary<(string Section, string Part, Fraction Meter), List<MeasureModel.Bar>?> _bars = new();

    public SectionBoundaryBars(SyntaxNode root, IReadOnlyDictionary<string, SyntaxNode> phraseBodies)
    {
        _root = root;
        _phraseBodies = phraseBodies;
    }

    /// <summary>The (section, part) cell a music item belongs to — a section-major part block
    /// or a part-major section body — or null for music outside any section.</summary>
    public static (string Section, string Part)? CellOf(SyntaxNode? item)
    {
        for (var n = item; n != null; n = n.Parent)
        {
            if (n is PartBlockSyntax pb && pb.Parent is SectionDeclarationSyntax sec)
                return (sec.SectionName, pb.Name);
            if (n is SectionDeclarationSyntax s && s.Parent is PartDeclarationSyntax part)
                return (s.SectionName, part.Name.Text);
        }
        return null;
    }

    /// <summary>True when the section's FIRST bar, <paramref name="firstBar"/> long, is the
    /// rest of a bar every predecessor in the form leaves open by exactly that much.</summary>
    public bool FirstBarCompletesEveryPredecessor((string Section, string Part) cell, Fraction firstBar, Fraction meter)
        => Complements(cell, firstBar, meter, before: true);

    /// <summary>True when the section's LAST bar, <paramref name="lastBar"/> long, is finished
    /// by every successor in the form opening with exactly its complement.</summary>
    public bool LastBarCompletedByEverySuccessor((string Section, string Part) cell, Fraction lastBar, Fraction meter)
        => Complements(cell, lastBar, meter, before: false);

    private bool Complements((string Section, string Part) cell, Fraction bar, Fraction meter, bool before)
    {
        if (bar <= Fraction.Zero || bar >= meter)
            return false;
        var neighbours = Neighbours(cell.Section, before);
        if (neighbours.Count == 0)
            return false;
        foreach (var other in neighbours)
        {
            var bars = BarsOf(other, cell.Part, meter);
            if (bars == null || bars.Count == 0)
                return false;
            var edge = before ? bars[^1] : bars[0];
            if (edge.IsEmpty || edge.Duration <= Fraction.Zero || edge.Duration + bar != meter)
                return false;
        }
        return true;
    }

    private HashSet<string> Neighbours(string section, bool before)
    {
        if (_before == null || _after == null)
            BuildAdjacency();
        var map = before ? _before! : _after!;
        return map.TryGetValue(section, out var set) ? set : new HashSet<string>();
    }

    /// <summary>Every (played-before, played-after) pair over every play of every form.</summary>
    private void BuildAdjacency()
    {
        _before = new(StringComparer.Ordinal);
        _after = new(StringComparer.Ordinal);
        foreach (var form in _root.DescendantNodes().OfType<FormDeclarationSyntax>())
        {
            var plays = new List<string>();
            Expand(FormWalk.Read(form), plays);
            for (int i = 1; i < plays.Count; i++)
            {
                Add(_after, plays[i - 1], plays[i]);
                Add(_before, plays[i], plays[i - 1]);
            }
        }

        static void Add(Dictionary<string, HashSet<string>> map, string key, string value)
        {
            if (!map.TryGetValue(key, out var set))
                map[key] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(value);
        }
    }

    private static void Expand(IReadOnlyList<FormWalk.Item> items, List<string> plays)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case FormWalk.SectionRef r:
                    plays.Add(r.Name);
                    break;
                case FormWalk.Ending e:
                    // A lone ending plays once, as its plain section.
                    plays.Add(e.Node.SectionName.Text);
                    break;
                case FormWalk.Repeat rep:
                    ExpandRepeat(rep, plays);
                    break;
            }
        }
    }

    private static void ExpandRepeat(FormWalk.Repeat rep, List<string> plays)
    {
        var body = new List<FormWalk.Item>();
        var endings = new List<FormWalk.Ending>();
        foreach (var child in rep.Children)
        {
            if (child is FormWalk.Ending e)
                endings.Add(e);
            else if (child is FormWalk.SectionRef or FormWalk.Repeat)
                body.Add(child);
        }
        // The collector's count: the written `:|*N`, else the number of endings, else 2.
        int turns = rep.ExplicitPlayCount ?? (endings.Count > 0 ? endings.Count : rep.PlayCount);
        for (int t = 0; t < turns; t++)
        {
            Expand(body, plays);
            if (endings.Count > 0)
                plays.Add(endings[Math.Min(t, endings.Count - 1)].Node.SectionName.Text);
        }
    }

    /// <summary>The bars of section <paramref name="section"/>'s cell for <paramref name="part"/>
    /// under <paramref name="meter"/>, or null when the part has no cell in that section.</summary>
    private List<MeasureModel.Bar>? BarsOf(string section, string part, Fraction meter)
    {
        var key = (section, part, meter);
        if (_bars.TryGetValue(key, out var cached))
            return cached;
        List<MeasureModel.Bar>? bars = null;
        foreach (var sec in _root.DescendantNodes().OfType<SectionDeclarationSyntax>())
        {
            if (sec.SectionName != section)
                continue;
            if (sec.Parent is PartDeclarationSyntax owner)
            {
                if (owner.Name.Text == part)
                {
                    bars = MeasureModel.Split(sec, _phraseBodies, meter);
                    break;
                }
                continue;
            }
            for (int i = 0; i < sec.SlotCount; i++)
            {
                if (sec.GetChild(i) is PartBlockSyntax pb && pb.Name == part)
                {
                    bars = MeasureModel.Split(pb, _phraseBodies, meter);
                    break;
                }
            }
            if (bars != null)
                break;
        }
        _bars[key] = bars;
        return bars;
    }
}
