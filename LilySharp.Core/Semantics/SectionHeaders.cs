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
/// The section HEADER registry, keyed by section NAME: the directives a section states for
/// EVERY part that plays it — its own starting <c>key</c>, <c>time</c>, <c>tempo</c> and
/// <c>partial</c> — read off every declaration of the name that holds no inline music: the
/// first direct child of each kind, and the first declaration of the name wins per directive.
/// </summary>
/// <remarks>
/// ⚠️ THE RULE IS THE COLLECTOR'S AND THIS IS ITS ONE SPELLING.
/// <c>MeasureCollector.ProcessSectionPrologue</c> applies the four tables at every section
/// boundary. Until 2026-10-02 the page (MeasureCollector.Definitions), the MIDI
/// (MidiExporter.Export), the MusicXML (MusicXmlExporter.BuildSectionHeaderRegistry) and the
/// LilyPond twin (LilyPondExporter.BuildSectionHeaderRegistry) each built them from the same
/// declarations in a loop of their own — four copies of one rule, one of them (the MIDI's)
/// without the tempo table (REFACTOR_PLAN stage C1).
/// <para>
/// Keyed by NAME and not read off the declaration being played, because a section reaches
/// the form in SPLIT declarations too — <c>section A { partial 8 }</c> beside
/// <c>part melody { section A { … } }</c> — and the played declaration is not the one holding
/// the header. Read off the chosen declaration alone, the twin of blogger2.lys carried no
/// <c>\partial</c> (第99 handoff ③) and the MusicXML exported a <c>key d major time 3/4</c>
/// header as fifths 0 in 4/4 (Lab sessions/p398/probes/xml-partial).
/// </para>
/// <para>
/// A declaration WITH inline music registers nothing — its directives are walked as music,
/// from their own position (<see cref="Svg.Collector.MeasureCollector.SectionHasInlineMusic"/>
/// is the one spelling of "inline music"). WHICH declarations are offered is the caller's:
/// the page leaves out the cells of a <c>chords</c> / <c>lyrics</c> track
/// (<c>MeasureCollector.IsInsideGroupedByPartTrack</c>: a track cell is not a structure
/// section); the exporters offer every declaration of the file, in document order.
/// ⚠️ <c>MeasureValidator.CollectSectionTimes</c> / <c>CollectSectionPartials</c> still read
/// the same directives by a rule of their own (the LAST declaration wins; a cell directly
/// under a <c>part</c> is no header) — stage C5's to reconcile, with its own nets.
/// </para>
/// </remarks>
public sealed class SectionHeaders
{
    /// <summary>No headers at all — the state before a read.</summary>
    public static readonly SectionHeaders Empty = new(
        new Dictionary<string, KeySignatureSyntax>(StringComparer.Ordinal),
        new Dictionary<string, TimeSignatureSyntax>(StringComparer.Ordinal),
        new Dictionary<string, TempoDeclarationSyntax>(StringComparer.Ordinal),
        new Dictionary<string, PartialDeclarationSyntax>(StringComparer.Ordinal));

    /// <summary>Section name → its header <c>key</c>.</summary>
    public IReadOnlyDictionary<string, KeySignatureSyntax> Keys { get; }
    /// <summary>Section name → its header <c>time</c>.</summary>
    public IReadOnlyDictionary<string, TimeSignatureSyntax> Times { get; }
    /// <summary>Section name → its header <c>tempo</c>.</summary>
    public IReadOnlyDictionary<string, TempoDeclarationSyntax> Tempos { get; }
    /// <summary>Section name → its header <c>partial</c>.</summary>
    public IReadOnlyDictionary<string, PartialDeclarationSyntax> Partials { get; }

    private SectionHeaders(
        Dictionary<string, KeySignatureSyntax> keys,
        Dictionary<string, TimeSignatureSyntax> times,
        Dictionary<string, TempoDeclarationSyntax> tempos,
        Dictionary<string, PartialDeclarationSyntax> partials)
    {
        Keys = keys;
        Times = times;
        Tempos = tempos;
        Partials = partials;
    }

    /// <summary>The registry of every section declaration under <paramref name="root"/>, in
    /// document order.</summary>
    public static SectionHeaders Read(SyntaxNode root)
        => Read(root.DescendantNodes().OfType<SectionDeclarationSyntax>());

    /// <summary>The registry of the given declarations, in the order given — the first
    /// declaration of a name wins per directive.</summary>
    public static SectionHeaders Read(IEnumerable<SectionDeclarationSyntax> declarations)
    {
        var keys = new Dictionary<string, KeySignatureSyntax>(StringComparer.Ordinal);
        var times = new Dictionary<string, TimeSignatureSyntax>(StringComparer.Ordinal);
        var tempos = new Dictionary<string, TempoDeclarationSyntax>(StringComparer.Ordinal);
        var partials = new Dictionary<string, PartialDeclarationSyntax>(StringComparer.Ordinal);
        foreach (var section in declarations)
        {
            if (Svg.Collector.MeasureCollector.SectionHasInlineMusic(section))
                continue;
            string name = section.SectionName;
            if (FirstDirect<KeySignatureSyntax>(section) is { } hk) keys.TryAdd(name, hk);
            if (FirstDirect<TimeSignatureSyntax>(section) is { } ht) times.TryAdd(name, ht);
            if (FirstDirect<TempoDeclarationSyntax>(section) is { } htp) tempos.TryAdd(name, htp);
            if (FirstDirect<PartialDeclarationSyntax>(section) is { } hp) partials.TryAdd(name, hp);
        }
        return new SectionHeaders(keys, times, tempos, partials);
    }

    /// <summary>The directives of a name in the order the page applies them — time, tempo,
    /// key, partial — or null when it has none (the twin writes them as the stream items of
    /// a play, in this order).</summary>
    public IReadOnlyList<SyntaxNode>? DirectivesOf(string name)
    {
        List<SyntaxNode>? list = null;
        if (Times.TryGetValue(name, out var time)) (list ??= new()).Add(time);
        if (Tempos.TryGetValue(name, out var tempo)) (list ??= new()).Add(tempo);
        if (Keys.TryGetValue(name, out var key)) (list ??= new()).Add(key);
        if (Partials.TryGetValue(name, out var partial)) (list ??= new()).Add(partial);
        return list;
    }

    /// <summary>The first direct child of <paramref name="section"/> of type
    /// <typeparamref name="T"/> — the section's own starting directive — or null.</summary>
    public static T? FirstDirect<T>(SectionDeclarationSyntax section) where T : SyntaxNode
    {
        for (int i = 0; i < section.SlotCount; i++)
            if (section.GetChild(i) is T t)
                return t;
        return null;
    }
}
