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

using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// THE ONE HOUSE for "how many bars does a section span" and "how many does one voice of it
/// write". A section spans as many bars as its LONGEST voice; every reader that lays a
/// section's voices side by side pads the shorter ones up to that count with silent bars —
/// the page (<see cref="MeasureCollector"/>'s section padding), the LilyPond twin, the
/// MIDI walk and the MusicXML export — and the cross-part validator (LYS2007) tells the
/// author which voice is short.
/// </summary>
/// <remarks>
/// The voices of a section named <c>A</c>: every part-major cell <c>part p { section A { … } }</c>,
/// every chord-track cell <c>chords t { section A { … } }</c>, the part blocks and the NAMED
/// chord blocks inside a section-major <c>section A { p { … } chords t { … } }</c>, and a
/// top-level declaration's own inline music (the single-part shorthand, counted only when
/// the declaration holds no part or chord block). A lyrics track's cell is NOT a voice: a
/// lyrics section longer than its music is a stacked verse by design (LyricsCollector's
/// auto-wrap). A header-only declaration (<c>section A { key g major }</c>) writes 0 bars.
/// <para>
/// ⚠️ TWO COUNTERS, named here on purpose, that must AGREE. <b>Semantic</b>
/// (<see cref="SemanticVoices"/>): a music voice's bars are <see cref="MeasureModel.Split"/>'s
/// — <c>R1*4</c> is four bars, a <c>repeat</c> its body COUNT times, a phrase reference its
/// body — the count the validator has always compared (LYS2007) and the one the exporters
/// pad by. <b>Syntactic</b> (<see cref="CanonicalByNameSyntactic"/>): the page's count,
/// <see cref="MeasureCollector.CountBarsInScope(SyntaxNode, out bool, MeasureCollector.PhraseBarTable)"/>
/// on greens with the book's phrase table — kept because the page pays it per keystroke and
/// the green walk exists to avoid a whole-book red first-touch (session 155). It reads the
/// same three expansions off the greens (2026-09-10; before that one token was one bar and a
/// phrase reference nothing, so a section written as three phrases counted ZERO bars and a
/// short part beside it was never padded — 97 of the fixture net's sections disagreed). The
/// net that keeps the two together is SectionVoicePaddingExportTests'
/// SyntacticCanonical_MatchesSemantic_OnEveryNetBook. MEASURED what the syntactic count did
/// to the exporters before it learned the expansions: 48 spurious <c>s1 |</c> after
/// canon-in-d's <c>repeat unfold 13</c> and two after every <c>R1*4</c>
/// (scratch/p363/compare-leg3-ly.txt, first cut).
/// </para>
/// <para>
/// ★ Before 2026-09-10 the page read this off its own cell registry (parts only), the
/// validator gathered `part`-nested sections only, and the three exporters did not pad at
/// all — five readers, three answers: the chord row's G7 stood on B's first bar of the page
/// while the twin drew B's music under it and the MIDI played B a bar early
/// (scratch/ベースタブLy/tooLongChords.lys; scratch/p363/pm-two-parts.lys for the
/// part-against-part twin and MusicXML).
/// </para>
/// </remarks>
internal static class SectionBarCounts
{
    /// <summary>One voice of one section, as the semantic count sees it.</summary>
    /// <param name="SectionName">The section it writes.</param>
    /// <param name="Label">How a message names it: <c>part 'x'</c> / <c>chords 'x'</c> /
    /// <c>section 'x'</c> (the single-part shorthand).</param>
    /// <param name="IsChords">A chord row (bars, no beats).</param>
    /// <param name="Container">The node whose items are the voice's bars — the part-major
    /// cell, the section-major part block / chord block, or the top-level section itself.</param>
    /// <param name="Anchor">The name token the validator anchors its warning on.</param>
    /// <param name="Bars">Bars written (semantic).</param>
    /// <param name="TrailingOpen">The last bar is still open — items after the last bar
    /// line — so a reader padding the voice closes it with its first added bar line.</param>
    /// <param name="PartMajor">Written inside a <c>part</c> or a <c>chords</c> track (the
    /// cross-part validator's part-major pass), as opposed to inside a section-major
    /// declaration.</param>
    public sealed record SemanticVoice(
        string SectionName, string Label, bool IsChords, SyntaxNode Container, TextSpan Anchor,
        int Bars, bool TrailingOpen, bool PartMajor);

    /// <summary>The semantic voices of every section under <paramref name="root"/>, in
    /// document order, with the meter rule the validator applies: a score-level <c>time</c>
    /// (outside every part / section / music body) arms everything after it, a section's
    /// own direct-child <c>time</c> arms the part blocks after it in that section. (The meter
    /// only steers <see cref="MeasureModel.Split"/>'s repeat-flow auto-complete.)</summary>
    public static List<SemanticVoice> SemanticVoices(SyntaxNode root)
    {
        var phrases = new Dictionary<string, SyntaxNode>(StringComparer.Ordinal);
        foreach (var n in root.DescendantNodes())
        {
            if (n is PhraseDeclarationSyntax ph)
                phrases[ph.Name.Text] = ph.Body;
            else if (n is VariableDeclarationSyntax vd)
                phrases[vd.Name.Text] = vd.Expression;
        }

        var voices = new List<SemanticVoice>();
        var time = DurationCalculator.ParseTimeSignature(4, 4);
        foreach (var n in root.DescendantNodes())
        {
            if (n is TimeSignatureSyntax ts && !ts.IsSenzaMisura && IsScoreLevel(ts))
                time = DurationCalculator.ParseTimeSignature(ts.Beats, ts.BeatType);
            if (n is not SectionDeclarationSyntax sec)
                continue;
            string name = sec.SectionName;
            switch (sec.Parent)
            {
                case PartDeclarationSyntax part:
                    voices.Add(Music(name, $"part '{part.Name.Text}'", sec, sec.Name.Span, time, phrases, partMajor: true));
                    continue;
                case ChordPartBlockSyntax { PartName: { } track }:
                    voices.Add(Chords(name, $"chords '{track}'", sec, sec.Name.Span, partMajor: true));
                    continue;
                case ChordPartBlockSyntax or LyricsBlockSyntax:
                    continue; // a nameless chord block's cell, or a lyrics cell: no voice
            }
            // Section-major (or a standalone / header declaration): its blocks are the voices.
            var local = time;
            bool anyBlock = false;
            foreach (var child in sec.ChildNodes())
            {
                switch (child)
                {
                    case TimeSignatureSyntax t when !t.IsSenzaMisura:
                        local = DurationCalculator.ParseTimeSignature(t.Beats, t.BeatType);
                        break;
                    case PartBlockSyntax pb:
                        voices.Add(Music(name, $"part '{pb.Name}'", pb, pb.PartName.Span, local, phrases, partMajor: false));
                        anyBlock = true;
                        break;
                    case ChordPartBlockSyntax { PartName: { } track, NameToken: { } tok } cb:
                        voices.Add(Chords(name, $"chords '{track}'", cb, tok.Span, partMajor: false));
                        anyBlock = true;
                        break;
                }
            }
            // The single-part shorthand: the lone part's music written straight into a
            // top-level section (MeasureCollector.Form.cs "Single-part shorthand").
            if (!anyBlock && sec.Parent is CompilationUnitSyntax && MeasureCollector.SectionHasInlineMusic(sec))
                voices.Add(Music(name, $"section '{name}'", sec, sec.Name.Span, local, phrases, partMajor: false));
        }
        return voices;
    }

    private static SemanticVoice Music(string section, string label, SyntaxNode container, TextSpan anchor,
        Fraction time, Dictionary<string, SyntaxNode> phrases, bool partMajor)
    {
        int bars = MeasureModel.Split(container, phrases, time).Count;
        MeasureCollector.CountBarsInScope(container, out bool open);
        return new SemanticVoice(section, label, false, container, anchor, bars, open, partMajor);
    }

    private static SemanticVoice Chords(string section, string label, SyntaxNode container, TextSpan anchor, bool partMajor)
    {
        int bars = container is ChordPartBlockSyntax block
            ? ChordNameCollector.CountBars(block, out bool open)
            : ChordNameCollector.CountSectionBars((SectionDeclarationSyntax)container, out open);
        return new SemanticVoice(section, label, true, container, anchor, bars, open, partMajor);
    }

    /// <summary>The semantic voices folded for the exporters: the canonical bar count per
    /// section name (the greatest voice) and each voice by its container node, so a reader
    /// appending a voice's play looks up what it wrote and pads the difference.</summary>
    public sealed class SemanticIndex
    {
        public Dictionary<string, int> Canonical { get; } = new(StringComparer.Ordinal);
        public Dictionary<SyntaxNode, SemanticVoice> ByContainer { get; } = new(ReferenceEqualityComparer.Instance);

        /// <summary>Bars this voice's play is short of its section: 0 when it is the longest,
        /// unknown, or a container the index does not hold. <paramref name="trailingOpen"/>
        /// says whether its last bar is still open (see <see cref="SemanticVoice.TrailingOpen"/>).
        /// A section-major part block may be handed as its BODY (the LilyPond twin's container
        /// is the block's music node); the lookup walks up to the block.</summary>
        public int Missing(SyntaxNode container, out bool trailingOpen)
        {
            trailingOpen = false;
            if (container is MusicBlockSyntax { Parent: PartBlockSyntax block })
                container = block;
            if (!ByContainer.TryGetValue(container, out var voice)
                || !Canonical.TryGetValue(voice.SectionName, out int canonical))
                return 0;
            trailingOpen = voice.TrailingOpen;
            return Math.Max(0, canonical - voice.Bars);
        }
    }

    public static SemanticIndex BuildSemanticIndex(SyntaxNode root)
    {
        var index = new SemanticIndex();
        foreach (var voice in SemanticVoices(root))
        {
            index.ByContainer[voice.Container] = voice;
            index.Canonical[voice.SectionName] = Math.Max(
                index.Canonical.TryGetValue(voice.SectionName, out int prior) ? prior : 0, voice.Bars);
        }
        return index;
    }

    /// <summary>The PAGE's canonical bar count of every section name: the same voices, counted
    /// on greens (<see cref="MeasureCollector.CountBarsInScope(SyntaxNode)"/> for music,
    /// <see cref="ChordNameCollector.CountBars(ChordPartBlockSyntax)"/> for chord rows) — see
    /// the class remarks for why the page keeps this counter and what it undercounts.</summary>
    public static Dictionary<string, int> CanonicalByNameSyntactic(SyntaxNode root)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        var phrases = MeasureCollector.PhraseGreens(root);
        foreach (var section in root.KindSites(SyntaxKind.SectionDeclaration).OfType<SectionDeclarationSyntax>())
        {
            int bars = DeclarationBarsSyntactic(section, phrases);
            if (bars < 0)
                continue; // a lyrics cell: not a voice
            result[section.SectionName] = Math.Max(
                result.TryGetValue(section.SectionName, out int prior) ? prior : 0, bars);
        }
        return result;
    }

    /// <summary>The greatest bar count among the voices ONE declaration of a section
    /// holds: its own bars for a part-major or chord-track cell, the longest of its part
    /// blocks / named chord blocks for a section-major declaration, its inline music for
    /// the single-part shorthand. −1 for a lyrics track's cell (no voice at all).</summary>
    private static int DeclarationBarsSyntactic(SectionDeclarationSyntax section, MeasureCollector.PhraseBarTable phrases)
    {
        switch (section.Parent)
        {
            case PartDeclarationSyntax:
                return MeasureCollector.CountBarsInScope(section, out _, phrases);
            case ChordPartBlockSyntax block:
                return block.PartName == null ? -1 : ChordNameCollector.CountSectionBars(section);
            case LyricsBlockSyntax:
                return -1;
        }
        int max = 0;
        foreach (var child in section.ChildNodes())
        {
            if (child is PartBlockSyntax part)
                max = Math.Max(max, MeasureCollector.CountBarsInScope(part, out _, phrases));
            else if (child is ChordPartBlockSyntax { PartName: not null } chords)
                max = Math.Max(max, ChordNameCollector.CountBars(chords));
        }
        // The single-part shorthand (and a header-only declaration, which counts 0).
        if (max == 0)
            max = MeasureCollector.CountBarsInScope(section, out _, phrases);
        return max;
    }

    /// <summary>True for a node outside every part / section / music body — the score level,
    /// where a <c>time</c> declaration arms the whole document after it (LP's Timing is
    /// Score-level; Lily# part-local changes are restated per part and stay local).</summary>
    public static bool IsScoreLevel(SyntaxNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
            if (p is PartDeclarationSyntax or PartBlockSyntax or SectionDeclarationSyntax or MusicBlockSyntax)
                return false;
        return true;
    }

    /// <summary>The root of the tree a node lives in.</summary>
    public static SyntaxNode RootOf(SyntaxNode node)
    {
        while (node.Parent != null)
            node = node.Parent;
        return node;
    }
}
