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

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// The flat list of music sites one <c>ProcessNodes</c> invocation of the collector's
/// top-level walk consumes — the gathered sites of a container (a part block, a part-major
/// cell), phrase references expanded in place — read by index, as the checkpoint/resume
/// substrate addresses it (<see cref="WalkCheckpoint.NodeIndex"/>). Three shapes behind
/// one reader:
/// <list type="bullet">
/// <item><see cref="Preset"/>: sites already in hand (a form's fabricated bar line, a
/// section's inline direct children, a nested walk's body) — no gather root, no paths.</item>
/// <item><see cref="Eager"/>: the container's whole gather, made before the walk starts —
/// the RECORDING collect's shape, which consumes every site anyway and whose expansion
/// budget is charged in gather order (see <c>MeasureCollector.ProcessMusicContainer</c>).</item>
/// <item><see cref="Lazy"/>: the RESUMED collect's shape — sites are pulled from the green
/// walk only as the reader reaches them, and <see cref="TrySeek"/> enters the walk at a
/// recorded checkpoint's site by its slot path without gathering anything before it, so a
/// walk that adopts a prefix and splices a tail never gathers the adopted bars at all.</item>
/// </list>
/// </summary>
/// <remarks>
/// A site before the seek point is never asked for: the resumed walk starts AT the
/// checkpoint's index and only ever looks forward (the marker peek reads later sites), so
/// the list holds nothing below it — asking is a bug, not a miss, and throws.
/// </remarks>
internal sealed class MusicSiteList
{
    private readonly List<GreenSite> _sites;
    private readonly SyntaxNode? _container;
    private readonly GreenSiteRule? _rule;
    private readonly Action<GreenSite, List<GreenSite>>? _emit;
    private readonly CollectWalkProbe? _probe;
    private IEnumerator<GreenSite>? _source;
    private int _offset;

    private MusicSiteList(List<GreenSite> sites, SyntaxNode? container, GreenSiteRule? rule,
        Action<GreenSite, List<GreenSite>>? emit, IEnumerator<GreenSite>? source, CollectWalkProbe? probe)
    {
        _sites = sites;
        _container = container;
        _rule = rule;
        _emit = emit;
        _source = source;
        _probe = probe;
    }

    /// <summary>Sites already in hand, under no gather root.</summary>
    public static MusicSiteList Preset(List<GreenSite> sites) => new(sites, null, null, null, null, null);

    /// <summary>The whole gather of <paramref name="container"/>, already made; the
    /// sites' paths (<see cref="PathOf"/>) are read against it.</summary>
    public static MusicSiteList Eager(List<GreenSite> sites, SyntaxNode container)
        => new(sites, container, null, null, null, null);

    /// <summary>
    /// The gather of <paramref name="container"/> pulled on demand: <paramref name="source"/>
    /// is its green walk (<c>MeasureCollector.MusicSitesLazy</c>), <paramref name="rule"/>
    /// the same walk's rule (what <see cref="TrySeek"/> re-enters it with), and
    /// <paramref name="emit"/> turns one walked site into the list's entries (a reference
    /// into its expansion, a collectable site into itself, anything else into nothing).
    /// <paramref name="probe"/> counts what gets materialized (a resume-mode diagnostic).
    /// <paramref name="sites"/> is the buffer the pulled sites land in — LENT by the caller
    /// (<c>MeasureCollector.ProcessMusicContainer</c>, which takes it back once the walk it
    /// hands this list to has returned), the same buffer its eager arm gathers into.
    /// </summary>
    public static MusicSiteList Lazy(SyntaxNode container, GreenSiteRule rule, IEnumerable<GreenSite> source,
        Action<GreenSite, List<GreenSite>> emit, CollectWalkProbe? probe, List<GreenSite> sites)
        => new(sites, container, rule, emit, source.GetEnumerator(), probe);

    /// <summary>The gather root the sites' paths are relative to; null for a preset list.</summary>
    public SyntaxNode? Container => _container;

    /// <summary>How many sites this list has actually produced (a lazy list: pulled or
    /// seeked; an eager or preset one: all of them).</summary>
    public int Materialized => _sites.Count;

    /// <summary>The site at <paramref name="index"/>, pulling the walk forward as far as
    /// needed; false past the end.</summary>
    public bool TryGet(int index, out GreenSite site)
    {
        int local = index - _offset;
        if (local < 0)
            throw new InvalidOperationException(
                $"music site {index} lies before the list's seek point ({_offset}) and was never gathered");
        while (local >= _sites.Count && Pull())
        {
        }
        if (local < _sites.Count)
        {
            site = _sites[local];
            return true;
        }
        site = default;
        return false;
    }

    private bool Pull()
    {
        if (_source == null)
            return false;
        if (!_source.MoveNext())
        {
            _source.Dispose();
            _source = null;
            return false;
        }
        int before = _sites.Count;
        _emit!(_source.Current, _sites);
        if (_probe != null)
            _probe.GatherSitesMaterialized += _sites.Count - before;
        return true;
    }

    /// <summary>
    /// Enters a lazy list's walk at the site <paramref name="path"/> names
    /// (<see cref="SyntaxNode.TryGreenSiteAt"/>), making it the element at
    /// <paramref name="index"/> — the recorded checkpoint's address — with nothing gathered
    /// before it. Only before anything has been pulled, and only for a path that leads to a
    /// collectable site (a reference expands into preset markers and never carries a path;
    /// one met here is drift). False leaves the list untouched, and the reader gathers from
    /// the start as before; the checkpoint's own address check (position and kind at the
    /// index) judges what the seek found exactly as it judges a gathered site.
    /// </summary>
    public bool TrySeek(int index, int[] path)
    {
        if (_source == null || _sites.Count > 0 || _offset != 0 || _container == null)
            return false;
        if (!_container.TryGreenSiteAt(_rule!, path, out var site, out var frames))
            return false;
        if (site.Kind == SyntaxKind.VariableReference)
            return false;
        _source.Dispose();
        _source = SyntaxNode.GreenSitesLazyFrom(_rule!, frames).GetEnumerator();
        _offset = index;
        _emit!(site, _sites);
        if (_probe != null)
        {
            _probe.GatherSeeks++;
            _probe.GatherSitesMaterialized += _sites.Count;
        }
        return true;
    }

    /// <summary>The slot path of the site at <paramref name="index"/> from the gather root
    /// (<see cref="GreenSite.PathFrom"/>), or null when there is no root or the site was
    /// not gathered under it — what a checkpoint records so a later resume can
    /// <see cref="TrySeek"/> it.</summary>
    public int[]? PathOf(int index)
        => _container != null && TryGet(index, out var site) ? site.PathFrom(_container) : null;
}
