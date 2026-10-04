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
/// The play order comes from <see cref="FormWalk"/> (the one reader of a form's spellings)
/// through <c>Svg.Collector.PlayedOrder</c>, the one expansion every reader of a form's
/// played order shares (session 792): a repeat block plays its body once per turn with the
/// turn's ending after it — the ending whose numbers name the turn, and as many turns as the
/// written <c>:|*N</c>, else the highest number named (<see cref="RepeatPasses"/>, the MIDI's
/// rule) — and the jump texts are followed along <see cref="FormRoute"/>.
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
    private readonly SectionHeaders _headers;
    private Dictionary<string, HashSet<string>>? _before;
    private Dictionary<string, HashSet<string>>? _after;
    private readonly Dictionary<(string Section, string Part, Fraction Meter), List<MeasureModel.Bar>?> _bars = new();

    /// <param name="headers">The section headers, for their <c>partial</c>s: a predecessor
    /// whose only bar is its declared pickup leaves THAT much open, not a bar of the meter
    /// (<see cref="FirstBarCompletesEveryPredecessor"/>).</param>
    public SectionBoundaryBars(SyntaxNode root, IReadOnlyDictionary<string, SyntaxNode> phraseBodies,
        SectionHeaders? headers = null)
    {
        _root = root;
        _phraseBodies = phraseBodies;
        _headers = headers ?? SectionHeaders.Empty;
    }

    /// <summary>The (section, part) cell a music item belongs to — a by-section part block
    /// or a by-part section body — or null for music outside any section.</summary>
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
    /// rest of a bar every predecessor in the form leaves open by exactly that much. The bar a
    /// predecessor leaves open is a bar of <paramref name="meter"/> — or its declared PICKUP, when
    /// the predecessor's only bar is that pickup (<c>section Body_1 { partial 2 }</c> whose body is
    /// <c>r8</c> and no bar line, the owner's `Locked out of Heaven`: the next section's first
    /// three eighths finish the half-bar pickup, not a whole bar).</summary>
    public bool FirstBarCompletesEveryPredecessor((string Section, string Part) cell, Fraction firstBar, Fraction meter)
        => Complements(cell, firstBar, meter, target: null, before: true);

    /// <summary>True when the section's LAST bar, <paramref name="lastBar"/> long, is finished
    /// by every successor in the form opening with exactly its complement to
    /// <paramref name="target"/> — the meter, or the section's declared pickup when that last
    /// bar is the pickup bar. <paramref name="meter"/> is the meter the successors' bars are
    /// split under.</summary>
    public bool LastBarCompletedByEverySuccessor((string Section, string Part) cell, Fraction lastBar, Fraction meter, Fraction target)
        => Complements(cell, lastBar, meter, target, before: false);

    private bool Complements((string Section, string Part) cell, Fraction bar, Fraction meter, Fraction? target, bool before)
    {
        if (bar <= Fraction.Zero || bar >= (target ?? meter))
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
            // What the open bar is worth: the target the caller named (judging a last bar), or —
            // judging a first bar — the predecessor's declared pickup when its edge bar IS that
            // pickup (its only bar), else a bar of the meter.
            var whole = target
                ?? (bars.Count == 1 && _headers.Partials.TryGetValue(other, out var pickup) ? pickup.ToFraction() : meter);
            if (edge.IsEmpty || edge.Duration <= Fraction.Zero || edge.Duration + bar != whole)
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
        foreach (var form in TopLevelNodes.OfRoot<FormDeclarationSyntax>(_root))
        {
            // The played order off the form — repeats per their passes, the jump texts
            // followed — through the ONE expansion every reader of it shares
            // (Svg.Collector.PlayedOrder; until session 792 this class spelled its own, which
            // stopped at the jump texts).
            var names = new List<string>();
            var printed = Svg.Collector.PlayedOrder.PlaysOf(FormWalk.Read(form), names);
            var played = Svg.Collector.PlayedOrder.Expand(printed);
            for (int i = 1; i < played.Count; i++)
            {
                Add(_after, names[played[i - 1]], names[played[i]]);
                Add(_before, names[played[i]], names[played[i - 1]]);
            }
        }

        static void Add(Dictionary<string, HashSet<string>> map, string key, string value)
        {
            if (!map.TryGetValue(key, out var set))
                map[key] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(value);
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
        foreach (var sec in _root.DescendantNodes<SectionDeclarationSyntax>())
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
