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
using System.Text;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>Which way a <c>.lys</c> file nests its part × section music cells.</summary>
public enum LayoutForm
{
    /// <summary>Could not tell (no part blocks / inner sections found).</summary>
    Unknown,
    /// <summary>section-major: <c>section A { melody { … } bass { … } }</c>.</summary>
    SectionMajor,
    /// <summary>part-major: <c>part melody { … section A { … } section B { … } }</c>.</summary>
    PartMajor,
}

/// <summary>
/// Converts a <c>.lys</c> document between the two equivalent authoring layouts:
/// section-major (a section holds per-part music blocks) and part-major (a part
/// holds its own inner sections). The two are duals over the part × section music
/// cells; only the nesting is transposed. Music-cell text is preserved verbatim;
/// the part/section scaffolding is regenerated to a canonical shape, and every
/// other top-level item (title, structure, score, phrases, …) is kept verbatim.
/// </summary>
public static class PartSectionLayoutConverter
{
    /// <summary>Detects the current layout of <paramref name="source"/>.</summary>
    public static LayoutForm Detect(string source) => Detect(SyntaxTree.Parse(source).GetRoot());

    /// <summary>Detects the layout from a parsed root.</summary>
    public static LayoutForm Detect(CompilationUnitSyntax root)
    {
        var parts = TopLevel(root).OfType<PartDeclarationSyntax>().ToList();
        // part-major iff any part declaration carries inner sections.
        if (parts.Any(p => DirectChildrenOfType<SectionDeclarationSyntax>(p).Any()))
            return LayoutForm.PartMajor;
        // section-major iff any top-level section carries part blocks.
        if (TopLevel(root).OfType<SectionDeclarationSyntax>()
                .Any(s => DirectChildrenOfType<PartBlockSyntax>(s).Any()))
            return LayoutForm.SectionMajor;
        return LayoutForm.Unknown;
    }

    /// <summary>
    /// Converts <paramref name="source"/> to the OTHER layout. Returns null when the
    /// layout can't be determined (nothing to transpose).
    /// </summary>
    public static string? Convert(string source)
    {
        var tree = SyntaxTree.Parse(source);
        // Never transpose a malformed file: the cell extraction relies on a clean,
        // balanced tree, and the caller overwrites the whole document with the
        // result. A file with syntax errors is left untouched.
        if (tree.HasErrors)
            return null;

        var root = tree.GetRoot();
        var form = Detect(root);
        if (form == LayoutForm.Unknown)
            return null;
        var target = form == LayoutForm.PartMajor ? LayoutForm.SectionMajor : LayoutForm.PartMajor;

        // Chord (`chords name { }`) and lyric blocks are SECTION-MAJOR ONLY — a
        // part's inner section holds music, not sub-blocks — so going to part-major
        // has nowhere to put them. Refuse rather than silently drop them (data loss);
        // the editor turns this into a clear "kept as section-major" message.
        if (target == LayoutForm.PartMajor && HasUntransposableSectionContent(root))
            return null;

        var result = Emit(source, root, target);
        // Safety net: only return a result that round-trips to a clean parse, so a
        // surprising cell (e.g. one with embedded braces) can never corrupt the
        // document in place. If it wouldn't parse, report "no change" instead.
        return SyntaxTree.Parse(result).HasErrors ? null : result;
    }

    /// <summary>
    /// True when a section-major section carries a block the converter cannot transpose.
    /// Part blocks, <c>chords name { }</c> chord parts and <c>lyrics { }</c> blocks are
    /// all transposed, so nothing common trips this today — it stays as a guard against
    /// any future section-only block type. The editor uses it to explain a refusal.
    /// </summary>
    public static bool HasUntransposableSectionContent(string source)
        => HasUntransposableSectionContent(SyntaxTree.Parse(source).GetRoot());

    private static bool HasUntransposableSectionContent(CompilationUnitSyntax root)
        => TopLevel(root).OfType<SectionDeclarationSyntax>()
            .Where(s => DirectChildrenOfType<PartBlockSyntax>(s).Any())
            .Any(s => DirectChildren(s).Any(
                c => c is not PartBlockSyntax and not ChordPartBlockSyntax and not LyricsBlockSyntax
                  && !IsSectionDirective(c)));

    // --- model extraction -----------------------------------------------------

    private static string Emit(string source, CompilationUnitSyntax root, LayoutForm target)
    {
        // Part order + attributes (the part body items that are NOT inner sections).
        var parts = new List<(string Name, string Attrs)>();
        // (part, section) -> music cell text (verbatim, between the braces).
        var cells = new Dictionary<(string Part, string Section), string>();
        // Chord tracks: name (null = the nameless form) in first-seen order, and
        // (chordName, section) -> the chord entries text.
        var chordParts = new List<string?>();
        var chordCells = new Dictionary<(string? Name, string Section), string>();
        // Lyric tracks: (name, ordinal) — the ordinal separates several same-named
        // (usually nameless) verse blocks in one section — and (track, section) text.
        var lyricTracks = new List<(string? Name, int Ordinal)>();
        // The `sings` binding is a property of the track NAME - carried whole
        // through the conversion, or the round trip would silently unbind it.
        var lyricSings = new Dictionary<string, string>(StringComparer.Ordinal);
        var lyricCells = new Dictionary<(string? Name, int Ordinal, string Section), string>();
        // section -> its own directive text (`key g major` …): a standalone part-major
        // header, or the directives folded into a section-major section.
        var sectionHeaders = new Dictionary<string, string>();
        var sectionOrder = new List<string>();
        void AddSection(string name)
        {
            if (!sectionOrder.Contains(name)) sectionOrder.Add(name);
        }
        void AddChordPart(string? name)
        {
            if (!chordParts.Contains(name)) chordParts.Add(name);
        }
        void AddLyricTrack((string?, int) track)
        {
            if (!lyricTracks.Contains(track)) lyricTracks.Add(track);
        }

        foreach (var member in TopLevel(root))
        {
            switch (member)
            {
                case PartDeclarationSyntax part:
                    var attrs = new List<string>();
                    foreach (var child in DirectChildren(part))
                    {
                        if (child is SectionDeclarationSyntax inner) // part-major cell
                        {
                            AddSection(inner.SectionName);
                            cells[(part.Name.Text, inner.SectionName)] = BetweenBraces(Verbatim(source, inner));
                        }
                        else
                        {
                            attrs.Add(Verbatim(source, child).Trim());
                        }
                    }
                    parts.Add((part.Name.Text, string.Join(" ", attrs.Where(a => a.Length > 0))));
                    break;

                // Part-major chord track: chords name { section A { c1 } section B { c1 } }.
                case ChordPartBlockSyntax topChords when topChords.HasSections:
                    AddChordPart(topChords.PartName);
                    foreach (var cs in topChords.Sections)
                    {
                        AddSection(cs.SectionName);
                        chordCells[(topChords.PartName, cs.SectionName)] = BetweenBraces(Verbatim(source, cs));
                    }
                    break;

                // Part-major lyric track: lyrics [name] { section A { .. } section B { .. } }.
                case LyricsBlockSyntax topLyrics when topLyrics.HasSections:
                {
                    int ord = lyricTracks.Count(t => t.Name == topLyrics.VoiceName);
                    AddLyricTrack((topLyrics.VoiceName, ord));
                    if (topLyrics is { VoiceName: { } tn, SingsTarget: { } tt }
                        && !lyricSings.ContainsKey(tn))
                        lyricSings[tn] = tt;
                    foreach (var ls in topLyrics.Sections)
                    {
                        AddSection(ls.SectionName);
                        lyricCells[(topLyrics.VoiceName, ord, ls.SectionName)] = BetweenBraces(Verbatim(source, ls));
                    }
                    break;
                }

                // Standalone part-major section header: `section A { key g major }` — the
                // section's directives stated once, parallel to the parts.
                case SectionDeclarationSyntax header when IsSectionHeader(header):
                    AddSection(header.SectionName);
                    sectionHeaders[header.SectionName] = SectionDirectiveText(source, header);
                    break;

                case SectionDeclarationSyntax section
                        when DirectChildrenOfType<PartBlockSyntax>(section).Any(): // section-major
                    AddSection(section.SectionName);
                    // Section-level directives (`key g major`) fold out to a standalone
                    // header in part-major.
                    var directives = SectionDirectiveText(source, section);
                    if (directives.Length > 0)
                        sectionHeaders[section.SectionName] = directives;
                    foreach (var pb in DirectChildrenOfType<PartBlockSyntax>(section))
                        cells[(pb.Name, section.SectionName)] = BetweenBraces(Verbatim(source, pb));
                    // In-section chord tracks become part-major chord tracks and back.
                    foreach (var cb in DirectChildrenOfType<ChordPartBlockSyntax>(section))
                    {
                        AddChordPart(cb.PartName);
                        chordCells[(cb.PartName, section.SectionName)] = BetweenBraces(Verbatim(source, cb));
                    }
                    // In-section lyric verses: the Nth same-named block per section is
                    // the Nth verse of that track (so stacked verses stay distinct).
                    // Count per name; "" stands in for the nameless form (a
                    // Dictionary rejects a null key).
                    var lyricOrd = new Dictionary<string, int>();
                    foreach (var lb in DirectChildrenOfType<LyricsBlockSyntax>(section))
                    {
                        string key = lb.VoiceName ?? "";
                        int ord = lyricOrd.GetValueOrDefault(key);
                        lyricOrd[key] = ord + 1;
                        AddLyricTrack((lb.VoiceName, ord));
                        if (lb is { VoiceName: { } ln, SingsTarget: { } lt }
                            && !lyricSings.ContainsKey(ln))
                            lyricSings[ln] = lt;
                        lyricCells[(lb.VoiceName, ord, section.SectionName)] = BetweenBraces(Verbatim(source, lb));
                    }
                    break;
            }
        }

        // Ensure every (part) appears even if it only shows up as a cell owner.
        foreach (var (p, _) in cells.Keys.ToList())
            if (!parts.Any(pt => pt.Name == p))
                parts.Add((p, ""));

        var tracks = new TrackData(chordParts, chordCells, lyricTracks, lyricCells, lyricSings);
        var body = target == LayoutForm.PartMajor
            ? EmitPartMajor(parts, sectionOrder, cells, tracks, sectionHeaders)
            : EmitSectionMajor(parts, sectionOrder, cells, tracks, sectionHeaders);

        return Reassemble(source, root, body);
    }

    /// <summary>The non-part section tracks (chords + lyrics) carried across a
    /// transpose, so the emitters take one bundle rather than four parameters.</summary>
    private sealed record TrackData(
        List<string?> ChordParts,
        Dictionary<(string? Name, string Section), string> ChordCells,
        List<(string? Name, int Ordinal)> LyricTracks,
        Dictionary<(string? Name, int Ordinal, string Section), string> LyricCells,
        Dictionary<string, string> LyricSings);

    /// <summary>Writes the <c>chords</c> keyword with its optional name.</summary>
    private static void AppendChordsKeyword(StringBuilder sb, string? name)
    {
        sb.Append("chords");
        if (name != null)
            sb.Append(' ').Append(name);
    }

    /// <summary>Writes the <c>lyrics</c> keyword with its optional voice name and
    /// the track's <c>sings</c> binding (repeating it on every emitted block is the
    /// legal spelling - later blocks may restate the binding identically).</summary>
    private static void AppendLyricsKeyword(StringBuilder sb, string? name, TrackData tracks)
    {
        sb.Append("lyrics");
        if (name != null)
        {
            sb.Append(' ').Append(name);
            if (tracks.LyricSings.TryGetValue(name, out var sings))
                sb.Append(" sings ").Append(sings);
        }
    }

    private static string EmitPartMajor(
        List<(string Name, string Attrs)> parts, List<string> sectionOrder,
        Dictionary<(string, string), string> cells, TrackData tracks,
        Dictionary<string, string> sectionHeaders)
    {
        var sb = new StringBuilder();
        // A section's own directives (`key g major`) stand parallel to the parts as a
        // standalone header, stated once for every part playing the section. They lead
        // the layout — a section's meter/key/tempo reads best BEFORE the parts that use it.
        foreach (var section in sectionOrder)
            if (sectionHeaders.TryGetValue(section, out var dir) && dir.Length > 0)
                sb.Append("section ").Append(section).Append(" { ").Append(dir).Append(" }\n");
        foreach (var (name, attrs) in parts)
        {
            sb.Append("part ").Append(name).Append(" {\n");
            if (attrs.Length > 0)
                sb.Append("  ").Append(attrs).Append('\n');
            foreach (var section in sectionOrder)
                if (cells.TryGetValue((name, section), out var music))
                    sb.Append("  section ").Append(section).Append(' ').Append(Braced(music)).Append('\n');
            sb.Append("}\n");
        }
        // Each chord track becomes its own top-level block with inner sections.
        foreach (var chordName in tracks.ChordParts)
        {
            AppendChordsKeyword(sb, chordName);
            sb.Append(" {\n");
            foreach (var section in sectionOrder)
                if (tracks.ChordCells.TryGetValue((chordName, section), out var entries))
                    sb.Append("  section ").Append(section).Append(' ').Append(Braced(entries)).Append('\n');
            sb.Append("}\n");
        }
        // ... and each lyric verse-track likewise.
        foreach (var (lyricName, ordinal) in tracks.LyricTracks)
        {
            AppendLyricsKeyword(sb, lyricName, tracks);
            sb.Append(" {\n");
            foreach (var section in sectionOrder)
                if (tracks.LyricCells.TryGetValue((lyricName, ordinal, section), out var text))
                    sb.Append("  section ").Append(section).Append(' ').Append(Braced(text)).Append('\n');
            sb.Append("}\n");
        }
        return sb.ToString();
    }

    private static string EmitSectionMajor(
        List<(string Name, string Attrs)> parts, List<string> sectionOrder,
        Dictionary<(string, string), string> cells, TrackData tracks,
        Dictionary<string, string> sectionHeaders)
    {
        var sb = new StringBuilder();
        foreach (var (name, attrs) in parts)
        {
            sb.Append("part ").Append(name);
            sb.Append(attrs.Length > 0 ? " { " + attrs + " }\n" : " { }\n");
        }
        sb.Append('\n');
        foreach (var section in sectionOrder)
        {
            sb.Append("section ").Append(section).Append(" {\n");
            // A standalone header's directives fold back in at the section's top.
            if (sectionHeaders.TryGetValue(section, out var dir) && dir.Length > 0)
                sb.Append("  ").Append(dir).Append('\n');
            foreach (var (name, _) in parts)
                if (cells.TryGetValue((name, section), out var music))
                    sb.Append("  ").Append(name).Append(' ').Append(Braced(music)).Append('\n');
            // Chord tracks fold back into per-section chords blocks.
            foreach (var chordName in tracks.ChordParts)
                if (tracks.ChordCells.TryGetValue((chordName, section), out var entries))
                {
                    sb.Append("  ");
                    AppendChordsKeyword(sb, chordName);
                    sb.Append(' ').Append(Braced(entries)).Append('\n');
                }
            // Lyric verse-tracks fold back into per-section lyrics blocks.
            foreach (var (lyricName, ordinal) in tracks.LyricTracks)
                if (tracks.LyricCells.TryGetValue((lyricName, ordinal, section), out var text))
                {
                    sb.Append("  ");
                    AppendLyricsKeyword(sb, lyricName, tracks);
                    sb.Append(' ').Append(Braced(text)).Append('\n');
                }
            sb.Append("}\n");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Rebuilds the document: every NON-structural top-level item (title, structure,
    /// score, phrases, global key/time/tempo, …) is kept verbatim in source order,
    /// and the regenerated part/section block replaces the structural region (parts
    /// + part-bearing sections) at the position of its first member.
    /// </summary>
    private static string Reassemble(string source, CompilationUnitSyntax root, string structuralBody)
    {
        var sb = new StringBuilder();
        bool emitted = false;
        foreach (var member in TopLevel(root))
        {
            bool structural = member is PartDeclarationSyntax
                || (member is ChordPartBlockSyntax cb && cb.HasSections)   // part-major chord track
                || (member is LyricsBlockSyntax lb && lb.HasSections)      // part-major lyric track
                || (member is SectionDeclarationSyntax s
                        && (DirectChildrenOfType<PartBlockSyntax>(s).Any()  // section-major
                            || IsSectionHeader(s)));                        // standalone header
            if (structural)
            {
                if (!emitted)
                {
                    // Keep the blank line / comment the user placed above the first
                    // part-or-section block — it is the leading trivia of that block's
                    // keyword token (the node-level trivia width is 0 for composites,
                    // so read it from the keyword token's span).
                    if (member.GetChild(0) is SyntaxTokenNode kw && kw.Span.Start > kw.FullSpan.Start)
                        sb.Append(source[kw.FullSpan.Start..kw.Span.Start]);
                    sb.Append(structuralBody);
                    emitted = true;
                }
                continue; // absorbed into the regenerated block
            }
            sb.Append(Verbatim(source, member));
        }
        if (!emitted) sb.Append(structuralBody);
        return sb.ToString();
    }

    // --- helpers --------------------------------------------------------------

    /// <summary>The top-level member nodes (skips the trailing EOF token).</summary>
    private static IEnumerable<SyntaxNode> TopLevel(CompilationUnitSyntax root) => DirectChildren(root);

    /// <summary>A node's direct child NODES (skips tokens such as keywords/braces).</summary>
    private static IEnumerable<SyntaxNode> DirectChildren(SyntaxNode node)
    {
        for (int i = 0; i < node.SlotCount; i++)
            if (node.GetChild(i) is SyntaxNode n and not SyntaxTokenNode)
                yield return n;
    }

    private static IEnumerable<T> DirectChildrenOfType<T>(SyntaxNode node) where T : SyntaxNode
        => DirectChildren(node).OfType<T>();

    /// <summary>A section-level directive child — <c>key</c> / <c>time</c> / <c>tempo</c>
    /// / <c>partial</c> / <c>clef</c> / <c>octave</c> — that a section may carry beside
    /// its part blocks (section-major) or alone (a standalone part-major header).</summary>
    private static bool IsSectionDirective(SyntaxNode n)
        => n is KeySignatureSyntax or TimeSignatureSyntax or TempoDeclarationSyntax
            or PartialDeclarationSyntax or ClefDeclarationSyntax or OctaveDirectiveSyntax;

    /// <summary>A directives-only section header (<c>section A { key g major }</c>): no
    /// part/chord/lyric blocks and no inline music, at least one directive. In part-major
    /// it stands parallel to the parts; in section-major it folds into the section.</summary>
    private static bool IsSectionHeader(SectionDeclarationSyntax s)
    {
        bool sawDirective = false;
        foreach (var c in DirectChildren(s))
        {
            if (IsSectionDirective(c)) { sawDirective = true; continue; }
            return false; // a part/chord/lyric block or inline music → not a bare header
        }
        return sawDirective;
    }

    /// <summary>The section's directive children joined verbatim (the header text folded
    /// into / split out of a section), empty when it carries none.</summary>
    private static string SectionDirectiveText(string source, SectionDeclarationSyntax s)
        => string.Join(" ", DirectChildren(s).Where(IsSectionDirective)
            .Select(d => Verbatim(source, d).Trim()).Where(t => t.Length > 0));

    /// <summary>
    /// A node's own characters, taken from the SOURCE rather than re-spelled out of
    /// the tree.
    /// </summary>
    /// <remarks>
    /// ⚠️ Not a micro-optimisation — the difference between handing the user their
    /// file back and handing them a rewrite of it. <c>ToFullString()</c> concatenates
    /// a node's green children IN TREE ORDER, and the tree does not always hold them
    /// in the order they were typed: a post-event written after a slur/tie/beam mark
    /// is hoisted onto the note and the mark is replayed behind it, so
    /// <c>c,,1~@mark("A") c,,</c> comes back out as <c>c,,1@mark("A") ~c,,</c> — the
    /// tie now reads as belonging to the NEXT note. This converter overwrites the
    /// document in place, so it re-spelled that tie in every book written that way:
    /// measured 2026-08-16, ~40 real files in the author's own library.
    /// The reorder loses no width (verified over 1025 books: root.FullWidth equals
    /// the source length and every node's span is in range), so the SLICE at
    /// (Position, FullWidth) is exactly what was typed even where ToFullString() is
    /// not. §2F ⑺ tracks making the tree itself faithful; until then, a writer that
    /// means "verbatim" has to say which of the two it means.
    /// </remarks>
    private static string Verbatim(string source, SyntaxNode node)
        => source.Substring(node.Position, node.FullWidth);

    /// <summary>The inner content between a node's first <c>{</c> and last <c>}</c>,
    /// trimmed — preserves the music verbatim (including any inner comments).</summary>
    private static string BetweenBraces(string text)
    {
        int open = text.IndexOf('{');
        int close = text.LastIndexOf('}');
        if (open < 0 || close < 0 || close <= open)
            return text.Trim();
        return text.Substring(open + 1, close - open - 1).Trim();
    }

    /// <summary>Wraps a music cell in braces. A cell that spans lines or ends in a
    /// <c>//</c> line comment puts the closing brace on its OWN line, so the comment
    /// can't swallow the brace (which would unbalance and corrupt the document).</summary>
    private static string Braced(string music)
        => music.Contains('\n') || music.Contains("//")
            ? "{ " + music + "\n}"
            : "{ " + music + " }";
}
