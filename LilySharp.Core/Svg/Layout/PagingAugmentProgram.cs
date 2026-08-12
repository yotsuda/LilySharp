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
using System.Collections.Immutable;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// ONE system's share of <c>LayoutEngine.AugmentSkylinesForPaging</c>, reified: the exact
/// sequence of skyline merges that turns the system's base paging skyline into its augmented
/// one, with every argument RESOLVED at build time. The same object is both the merge
/// program (<see cref="Execute"/>) and the memo key (<see cref="Matches"/>): the program IS
/// the function's input, so "equal program + same base skyline instance ⇒ identical output"
/// needs no coverage argument over what the annotation layouts were computed FROM — staff
/// offsets, fonts, neighbours, all of it is already baked into the resolved arguments,
/// which is what discharges the extra key term a bow's baked-in staff offset would
/// otherwise demand (HANDOFF §1 第141 発見⑴).
/// </summary>
/// <remarks>
/// ⚠️ BYTE-IDENTITY IS BY CONSTRUCTION, and the construction is the point:
/// <see cref="Execute"/> replays the family-major loops' per-system call pattern verbatim —
/// one fresh wrapper skyline per step, merged from the running pair, then the step's own
/// ink, in the same family order (scripts → tuplet groups → slur group → tie group →
/// figured bass → volta → marks → custom texts → chord names → bar numbers) and the same
/// within-family array order the old loops produced. Nothing is batched: the batched
/// seeding candidate fell in session 141 because slope merges round differently under a
/// different association (4,878 ULP mismatches over the tree), so the association here is
/// exactly the old one.
/// <para>
/// ⚠️ THE KEY MUST NAME EVERY FIELD THE MERGE READS. Steps that execute from a stored
/// layout struct (scripts via <c>ArticulationEngraver.MergeScriptProfile</c>, tuplet groups
/// via <c>SkylineBuilder.AddTupletBracketsToSkyline</c>) put those fields — and ONLY
/// derived-from-those-fields values — into <see cref="_args"/>/<see cref="_texts"/>; the
/// stored structs ride outside the key. Script reads (verified 2026-08-12):
/// Glyph, FontSizeStep, SkylineHorizontalPadding, X, Ink (fallback box) + anchorY.
/// Tuplet reads: IsStemUp, ShowBracket, StartX/EndX, DrawnStartX/DrawnEndX,
/// StartYUp/EndYUp, NumberText, NumberX, NumberYUp. If a read is added to either callee,
/// its value must join the key here — both callees carry a pointer back to this remark.
/// Every other step is fully argument-driven (a resolved box or the bow's eight control
/// numbers), so for them the key and the execution literally share the same doubles.
/// </para>
/// </remarks>
internal sealed class PagingAugmentProgram
{
    /// <summary>Step opcodes. A script's direction is in its opcode so the key needs no
    /// separate bool, and the executor knows which single side to wrap (the family loops
    /// only ever re-wrapped the side they merged into).</summary>
    private enum Kind : byte
    {
        ScriptUp,
        ScriptDown,
        TupletGroup,
        BowGroup,
        FiguredBassBox,
        VoltaBox,
        MarkBox,
        BarNumberBox,
    }

    private readonly Kind[] _kinds;
    private readonly double[] _args;
    private readonly string?[] _texts;
    // Execution payloads — NOT part of the key; see the class remark for why that is sound.
    private readonly ArticulationLayout[] _scripts;
    private readonly ImmutableArray<TupletBracketLayout>[] _tupletGroups;

    private PagingAugmentProgram(
        Kind[] kinds, double[] args, string?[] texts,
        ArticulationLayout[] scripts, ImmutableArray<TupletBracketLayout>[] tupletGroups)
    {
        _kinds = kinds;
        _args = args;
        _texts = texts;
        _scripts = scripts;
        _tupletGroups = tupletGroups;
    }

    public bool IsEmpty => _kinds.Length == 0;

    /// <summary>Exact key equality: opcodes, resolved numbers (bit-for-bit — a NaN would
    /// compare unequal and merely cost a recompute) and texts. The payload structs are
    /// deliberately NOT compared — their merge-read fields are all in the arrays.</summary>
    public bool Matches(PagingAugmentProgram other)
    {
        if (_kinds.Length != other._kinds.Length
            || _args.Length != other._args.Length
            || _texts.Length != other._texts.Length)
            return false;
        for (int i = 0; i < _kinds.Length; i++)
            if (_kinds[i] != other._kinds[i])
                return false;
        for (int i = 0; i < _args.Length; i++)
            if (_args[i] != other._args[i])
                return false;
        for (int i = 0; i < _texts.Length; i++)
            if (!string.Equals(_texts[i], other._texts[i], StringComparison.Ordinal))
                return false;
        return true;
    }

    /// <summary>Replays the merges against <paramref name="baseline"/> and returns the
    /// augmented pair. The baseline instances are only read (merged FROM), never mutated —
    /// an empty program hands them back untouched, exactly as the family loops left an
    /// unannotated system's originals in place.</summary>
    public (VerticalSkyline up, VerticalSkyline down) Execute(
        (VerticalSkyline up, VerticalSkyline down) baseline)
    {
        var cur = baseline;
        int a = 0, t = 0, sc = 0, tg = 0;
        foreach (var kind in _kinds)
        {
            switch (kind)
            {
                case Kind.ScriptUp:
                {
                    double anchorY = _args[a];
                    a += ScriptArgs;
                    t++;
                    var up = new VerticalSkyline(VerticalDirection.Up);
                    up.Merge(cur.up);
                    ArticulationEngraver.MergeScriptProfile(up, _scripts[sc++], anchorY);
                    cur = (up, cur.down);
                    break;
                }
                case Kind.ScriptDown:
                {
                    double anchorY = _args[a];
                    a += ScriptArgs;
                    t++;
                    var down = new VerticalSkyline(VerticalDirection.Down);
                    down.Merge(cur.down);
                    ArticulationEngraver.MergeScriptProfile(down, _scripts[sc++], anchorY);
                    cur = (cur.up, down);
                    break;
                }
                case Kind.TupletGroup:
                {
                    var group = _tupletGroups[tg++];
                    a += 1 + group.Length * TupletArgsPerItem;
                    t += group.Length;
                    var up = new VerticalSkyline(VerticalDirection.Up);
                    up.Merge(cur.up);
                    var down = new VerticalSkyline(VerticalDirection.Down);
                    down.Merge(cur.down);
                    SkylineBuilder.AddTupletBracketsToSkyline(
                        group, staffTopUp: 0, StaffSize.FullSize, up, down);
                    cur = (up, down);
                    break;
                }
                case Kind.BowGroup:
                {
                    int count = (int)_args[a++];
                    var up = new VerticalSkyline(VerticalDirection.Up);
                    up.Merge(cur.up);
                    var down = new VerticalSkyline(VerticalDirection.Down);
                    down.Merge(cur.down);
                    for (int i = 0; i < count; i++, a += BowArgsPerItem)
                    {
                        SkylineBuilder.SeedBowInk(
                            _args[a], _args[a + 1],
                            (_args[a + 2], _args[a + 3]), (_args[a + 4], _args[a + 5]),
                            _args[a + 6], _args[a + 7],
                            staffTopUp: 0, StaffSize.FullSize, up, down);
                    }
                    cur = (up, down);
                    break;
                }
                case Kind.FiguredBassBox:
                {
                    var down = new VerticalSkyline(VerticalDirection.Down);
                    down.Merge(cur.down);
                    down.Merge(VerticalSkyline.FromBox(
                        _args[a], _args[a + 1], _args[a + 2], _args[a + 3],
                        VerticalDirection.Down));
                    a += BoxArgs;
                    cur = (cur.up, down);
                    break;
                }
                case Kind.VoltaBox:
                case Kind.BarNumberBox:
                {
                    var up = new VerticalSkyline(VerticalDirection.Up);
                    up.Merge(cur.up);
                    up.Merge(VerticalSkyline.FromBox(
                        _args[a], _args[a + 1], _args[a + 2], _args[a + 3],
                        VerticalDirection.Up));
                    a += BoxArgs;
                    cur = (up, cur.down);
                    break;
                }
                case Kind.MarkBox:
                {
                    var up = new VerticalSkyline(VerticalDirection.Up);
                    up.Merge(cur.up);
                    up.Merge(VerticalSkyline.FromBox(
                        _args[a], _args[a + 1], _args[a + 2], _args[a + 3],
                        VerticalDirection.Up));
                    var down = new VerticalSkyline(VerticalDirection.Down);
                    down.Merge(cur.down);
                    down.Merge(VerticalSkyline.FromBox(
                        _args[a], _args[a + 1], _args[a + 2], _args[a + 3],
                        VerticalDirection.Down));
                    a += BoxArgs;
                    cur = (up, down);
                    break;
                }
                default:
                    throw new InvalidOperationException($"unknown paging op {kind}");
            }
        }
        return cur;
    }

    private const int ScriptArgs = 8;        // anchorY, X, FontSizeStep, pad, Ink L/B/R/T
    private const int TupletArgsPerItem = 10;
    private const int BowArgsPerItem = 8;
    private const int BoxArgs = 4;

    /// <summary>Accumulates one system's steps in merge order.</summary>
    public sealed class Builder
    {
        private readonly List<Kind> _kinds = new();
        private readonly List<double> _args = new();
        private readonly List<string?> _texts = new();
        private readonly List<ArticulationLayout> _scripts = new();
        private readonly List<ImmutableArray<TupletBracketLayout>> _tupletGroups = new();

        public void AddScript(in ArticulationLayout script, double anchorY)
        {
            _kinds.Add(script.IsAbove ? Kind.ScriptUp : Kind.ScriptDown);
            _args.Add(anchorY);
            _args.Add(script.X);
            _args.Add(script.FontSizeStep);
            _args.Add(script.SkylineHorizontalPadding);
            _args.Add(script.Ink.Left);
            _args.Add(script.Ink.Bottom);
            _args.Add(script.Ink.Right);
            _args.Add(script.Ink.Top);
            _texts.Add(script.Glyph);
            _scripts.Add(script);
        }

        public void AddTupletGroup(ImmutableArray<TupletBracketLayout> group)
        {
            _kinds.Add(Kind.TupletGroup);
            _args.Add(group.Length);
            foreach (var b in group)
            {
                _args.Add(b.IsStemUp ? 1 : 0);
                _args.Add(b.ShowBracket ? 1 : 0);
                _args.Add(b.StartX);
                _args.Add(b.EndX);
                _args.Add(b.DrawnStartX);
                _args.Add(b.DrawnEndX);
                _args.Add(b.StartYUp);
                _args.Add(b.EndYUp);
                _args.Add(b.NumberX);
                _args.Add(b.NumberYUp);
                _texts.Add(b.NumberText);
            }
            _tupletGroups.Add(group);
        }

        /// <summary>One family's bows for this system, in array order — slurs and ties are
        /// separate calls, in that order, mirroring the family loops. The eight numbers per
        /// bow are exactly <c>SeedBowInk</c>'s inputs.</summary>
        public void AddBowGroup<T>(List<T> bows) where T : BowLayout
        {
            _kinds.Add(Kind.BowGroup);
            _args.Add(bows.Count);
            foreach (var b in bows)
            {
                _args.Add(b.StartX);
                _args.Add(b.StartYUp);
                _args.Add(b.Control1.X);
                _args.Add(b.Control1.Y);
                _args.Add(b.Control2.X);
                _args.Add(b.Control2.Y);
                _args.Add(b.EndX);
                _args.Add(b.EndYUp);
            }
        }

        public void AddFiguredBassBox(double xLeft, double xRight, double bottom, double top)
            => AddBox(Kind.FiguredBassBox, xLeft, xRight, bottom, top);

        public void AddVoltaBox(double xLeft, double xRight, double bottom, double top)
            => AddBox(Kind.VoltaBox, xLeft, xRight, bottom, top);

        public void AddMarkBox(double xLeft, double xRight, double bottom, double top)
            => AddBox(Kind.MarkBox, xLeft, xRight, bottom, top);

        public void AddBarNumberBox(double xLeft, double xRight, double bottom, double top)
            => AddBox(Kind.BarNumberBox, xLeft, xRight, bottom, top);

        private void AddBox(Kind kind, double xLeft, double xRight, double bottom, double top)
        {
            _kinds.Add(kind);
            _args.Add(xLeft);
            _args.Add(xRight);
            _args.Add(bottom);
            _args.Add(top);
        }

        public PagingAugmentProgram Build() => new(
            _kinds.ToArray(), _args.ToArray(), _texts.ToArray(),
            _scripts.ToArray(), _tupletGroups.ToArray());
    }
}
