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

/// <summary>Which way a <c>.lys</c> file groups its part × section music cells.</summary>
public enum Grouping
{
    /// <summary>Could not tell (no part blocks / inner sections found).</summary>
    Unknown,
    /// <summary>Grouped by section: <c>section A { melody { … } bass { … } }</c>.</summary>
    BySection,
    /// <summary>Grouped by part: <c>part melody { … section A { … } section B { … } }</c>.</summary>
    ByPart,
}

/// <summary>
/// Regroups a <c>.lys</c> document between the two equivalent groupings:
/// by section (a section holds per-part music blocks) and by part (a part
/// holds its own inner sections). The two are duals over the part × section music
/// cells; only the nesting is transposed. Music-cell text is preserved verbatim;
/// the part/section scaffolding is regenerated to a canonical shape, and every
/// other top-level item (title, structure, score, phrases, …) is kept verbatim.
/// </summary>
public static class PartSectionRegrouper
{
    /// <summary>Detects the current layout of <paramref name="source"/>.</summary>
    public static Grouping Detect(string source) => Detect(SyntaxTree.Parse(source).GetRoot());

    /// <summary>Detects the layout from a parsed root.</summary>
    public static Grouping Detect(CompilationUnitSyntax root)
    {
        var parts = TopLevel(root).OfType<PartDeclarationSyntax>().ToList();
        // by-part iff any part declaration carries inner sections.
        if (parts.Any(p => DirectChildrenOfType<SectionDeclarationSyntax>(p).Any()))
            return Grouping.ByPart;
        // by-section iff any top-level section carries part blocks.
        if (TopLevel(root).OfType<SectionDeclarationSyntax>()
                .Any(s => DirectChildrenOfType<PartBlockSyntax>(s).Any()))
            return Grouping.BySection;
        return Grouping.Unknown;
    }

    /// <summary>
    /// Converts <paramref name="source"/> to the OTHER layout. Returns null when the
    /// layout can't be determined (nothing to transpose).
    /// </summary>
    public static string? Convert(string source) => Convert(source, out _);

    /// <summary>
    /// <see cref="Convert(string)"/>, saying why when it refuses because two declarations
    /// fill the same cell: <paramref name="collision"/> names it (<c>part 'oboe' in section
    /// A</c>), null for every other refusal.
    /// </summary>
    /// <remarks>
    /// ⚠️ The transposed layout has room for ONE text per cell, so a cell written twice
    /// (LYS DuplicateCell / DuplicateTrackSection — errors, but not syntax errors) would come
    /// out with the later text only and the earlier one silently gone. Refused instead: the
    /// caller overwrites the whole document. A section written in several declarations whose
    /// cells do not collide (<c>section A { flute { … } } section A { oboe { … } }</c>) is
    /// legal and merges into one.
    /// </remarks>
    public static string? Convert(string source, out string? collision)
    {
        collision = null;
        var tree = SyntaxTree.Parse(source);
        // Never transpose a malformed file: the cell extraction relies on a clean,
        // balanced tree, and the caller overwrites the whole document with the
        // result. A file with syntax errors is left untouched.
        if (tree.HasErrors)
            return null;

        var root = tree.GetRoot();
        var form = Detect(root);
        if (form == Grouping.Unknown)
            return null;
        var target = form == Grouping.ByPart ? Grouping.BySection : Grouping.ByPart;

        // Chord (`chords name { }`) and lyric blocks are BY-SECTION ONLY — a
        // part's inner section holds music, not sub-blocks — so going to by-part
        // has nowhere to put them. Refuse rather than silently drop them (data loss);
        // the editor turns this into a clear "kept as by-section" message.
        if (target == Grouping.ByPart && HasUntransposableSectionContent(root))
            return null;

        var result = Emit(source, root, target, out collision);
        if (result == null)
            return null;
        // Safety net: only return a result that round-trips to a clean parse, so a
        // surprising cell (e.g. one with embedded braces) can never corrupt the
        // document in place. If it wouldn't parse, report "no change" instead.
        return SyntaxTree.Parse(result).HasErrors ? null : result;
    }

    /// <summary>
    /// True when a by-section section carries a block the converter cannot transpose.
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

    private static string? Emit(string source, CompilationUnitSyntax root, Grouping target, out string? collision)
    {
        // The first cell written twice, which the maps below would keep only the later of.
        string? firstCollision = null;
        void Put<TKey>(Dictionary<TKey, string> map, TKey key, string text, string what) where TKey : notnull
        {
            if (!map.TryAdd(key, text))
                firstCollision ??= what;
        }
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
        // section -> its own directive text (`key g major` …): a standalone by-part
        // header, or the directives folded into a by-section section.
        var sectionHeaders = new Dictionary<string, string>();
        // Two declarations restating the same directives lose nothing; differing ones would.
        void PutHeader(string section, string directives)
        {
            if (directives.Length == 0)
                return;
            if (sectionHeaders.TryGetValue(section, out var had) && had != directives)
                firstCollision ??= $"the directives of section {section}";
            sectionHeaders[section] = directives;
        }
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

        // THE COMMENTS (owner's report 2026-10-11: Regroup lost them). Every comment the
        // copied pieces below do not carry — a cell's and an attribute's text are taken
        // verbatim, so the comments INSIDE them travel already — is given to the nearest
        // thing that survives the regroup: a comment around a CELL goes inside it, at its top,
        // so it moves with its music; one on a part or a section stands above that block in
        // the output; anything left leads the regenerated region. The first structural
        // member's leading trivia is Reassemble's and is not counted twice.
        var comments = CommentSpans(root);
        var firstLead = FirstStructuralLead(root);
        var partNotes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var sectionNotes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var looseNotes = new List<string>();
        // The comments inside [start, end) that none of `carried` holds.
        List<string> CommentsOutside(int start, int end, IEnumerable<(int Start, int End)> carried)
        {
            var found = new List<string>();
            foreach (var (s, e) in comments)
                if (s >= start && e <= end && !(s >= firstLead.Start && e <= firstLead.End)
                    && !carried.Any(c => s >= c.Start && e <= c.End))
                    found.Add(source[s..e].TrimEnd());
            return found;
        }
        void Note(Dictionary<string, List<string>> map, string key, List<string> notes)
        {
            if (notes.Count == 0) return;
            if (!map.TryGetValue(key, out var list)) map[key] = list = new();
            list.AddRange(notes);
        }
        // A cell's music with the comments written around it (outside its braces) on top.
        string Cell(SyntaxNode node)
        {
            var (open, close) = Interior(source, node, comments);
            string music = source[open..close].Trim();
            var notes = CommentsOutside(node.Position, node.Position + node.FullWidth, [(open, close)]);
            // Indented as a cell's lines are (both emitters write a cell two spaces in).
            return notes.Count == 0 ? music : string.Join("\n    ", notes) + "\n    " + music;
        }
        (int Start, int End) Whole(SyntaxNode n) => (n.Position, n.Position + n.FullWidth);

        foreach (var member in TopLevel(root))
        {
            switch (member)
            {
                case PartDeclarationSyntax part:
                    var attrs = new List<string>();
                    var carried = new List<(int, int)>();
                    foreach (var child in DirectChildren(part))
                    {
                        carried.Add(Whole(child));
                        if (child is SectionDeclarationSyntax inner) // by-part cell
                        {
                            AddSection(inner.SectionName);
                            Put(cells, (part.Name.Text, inner.SectionName), Cell(inner),
                                $"part '{part.Name.Text}' in section {inner.SectionName}");
                        }
                        else
                        {
                            attrs.Add(Verbatim(source, child).Trim());
                        }
                    }
                    Note(partNotes, part.Name.Text, CommentsOutside(member.Position, member.Position + member.FullWidth, carried));
                    parts.Add((part.Name.Text, JoinPieces(attrs)));
                    break;

                // By-part chord track: chords name { section A { c1 } section B { c1 } }.
                case ChordPartBlockSyntax topChords when topChords.HasSections:
                    AddChordPart(topChords.PartName);
                    foreach (var cs in topChords.Sections)
                    {
                        AddSection(cs.SectionName);
                        Put(chordCells, (topChords.PartName, cs.SectionName), Cell(cs),
                            $"{ChordsLabel(topChords.PartName)} in section {cs.SectionName}");
                    }
                    looseNotes.AddRange(CommentsOutside(member.Position, member.Position + member.FullWidth,
                        topChords.Sections.Select(c => Whole(c))));
                    break;

                // By-part lyric track: lyrics [name] { section A { .. } section B { .. } }.
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
                        Put(lyricCells, (topLyrics.VoiceName, ord, ls.SectionName), Cell(ls),
                            $"{LyricsLabel(topLyrics.VoiceName)} in section {ls.SectionName}");
                    }
                    looseNotes.AddRange(CommentsOutside(member.Position, member.Position + member.FullWidth,
                        topLyrics.Sections.Select(l => Whole(l))));
                    break;
                }

                // Standalone by-part section header: `section A { key g major }` — the
                // section's directives stated once, parallel to the parts.
                case SectionDeclarationSyntax header when IsSectionHeader(header):
                    AddSection(header.SectionName);
                    PutHeader(header.SectionName, SectionDirectiveText(source, header));
                    Note(sectionNotes, header.SectionName, CommentsOutside(member.Position, member.Position + member.FullWidth,
                        DirectChildren(header).Where(IsSectionDirective).Select(Whole)));
                    break;

                case SectionDeclarationSyntax section
                        when DirectChildrenOfType<PartBlockSyntax>(section).Any(): // by-section
                    AddSection(section.SectionName);
                    // Section-level directives (`key g major`) fold out to a standalone
                    // header in by-part.
                    PutHeader(section.SectionName, SectionDirectiveText(source, section));
                    // Comments on the section itself (above it, after its `{`, between its
                    // blocks) — the cells and directives carry their own.
                    Note(sectionNotes, section.SectionName, CommentsOutside(member.Position, member.Position + member.FullWidth,
                        DirectChildren(section).Where(c => c is PartBlockSyntax or ChordPartBlockSyntax or LyricsBlockSyntax
                            || IsSectionDirective(c)).Select(Whole)));
                    foreach (var pb in DirectChildrenOfType<PartBlockSyntax>(section))
                        Put(cells, (pb.Name, section.SectionName), Cell(pb),
                            $"part '{pb.Name}' in section {section.SectionName}");
                    // In-section chord tracks become by-part chord tracks and back.
                    foreach (var cb in DirectChildrenOfType<ChordPartBlockSyntax>(section))
                    {
                        AddChordPart(cb.PartName);
                        Put(chordCells, (cb.PartName, section.SectionName), Cell(cb),
                            $"{ChordsLabel(cb.PartName)} in section {section.SectionName}");
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
                        Put(lyricCells, (lb.VoiceName, ord, section.SectionName), Cell(lb),
                            $"{LyricsLabel(lb.VoiceName)} in section {section.SectionName}");
                    }
                    break;
            }
        }

        collision = firstCollision;
        if (collision != null)
            return null;

        // Ensure every (part) appears even if it only shows up as a cell owner.
        foreach (var (p, _) in cells.Keys.ToList())
            if (!parts.Any(pt => pt.Name == p))
                parts.Add((p, ""));

        var tracks = new TrackData(chordParts, chordCells, lyricTracks, lyricCells, lyricSings);
        var notes = new Notes(partNotes, sectionNotes, looseNotes);
        var body = target == Grouping.ByPart
            ? EmitGroupedByPart(parts, sectionOrder, cells, tracks, sectionHeaders, notes)
            : EmitGroupedBySection(parts, sectionOrder, cells, tracks, sectionHeaders, notes);

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

    /// <summary>A chord track as a refusal names it: <c>chords 'harmony'</c>.</summary>
    private static string ChordsLabel(string? name) => name == null ? "chords" : $"chords '{name}'";

    /// <summary>A lyrics track as a refusal names it: <c>lyrics 'words'</c>.</summary>
    private static string LyricsLabel(string? name) => name == null ? "lyrics" : $"lyrics '{name}'";

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

    /// <summary>The comments a regroup moves rather than copies (Emit's remark): those on a
    /// part, those on a section, and the rest.</summary>
    private sealed record Notes(
        Dictionary<string, List<string>> Parts,
        Dictionary<string, List<string>> Sections,
        List<string> Loose);

    /// <summary>Writes each comment on a line of its own.</summary>
    private static void AppendNotes(StringBuilder sb, IEnumerable<string>? notes)
    {
        if (notes == null) return;
        foreach (var n in notes)
            sb.Append(n).Append('\n');
    }

    private static string EmitGroupedByPart(
        List<(string Name, string Attrs)> parts, List<string> sectionOrder,
        Dictionary<(string, string), string> cells, TrackData tracks,
        Dictionary<string, string> sectionHeaders, Notes notes)
    {
        var sb = new StringBuilder();
        AppendNotes(sb, notes.Loose);
        // A section's own directives (`key g major`) stand parallel to the parts as a
        // standalone header, stated once for every part playing the section. They lead
        // the layout — a section's meter/key/tempo reads best BEFORE the parts that use it.
        // A comment on a section has no section block of its own here: it stands where the
        // section's header does (or would).
        foreach (var section in sectionOrder)
        {
            AppendNotes(sb, notes.Sections.GetValueOrDefault(section));
            if (sectionHeaders.TryGetValue(section, out var dir) && dir.Length > 0)
                sb.Append("section ").Append(section).Append(' ').Append(Braced(dir).Replace("\n  }", "\n}")).Append('\n');
        }
        foreach (var (name, attrs) in parts)
        {
            AppendNotes(sb, notes.Parts.GetValueOrDefault(name));
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

    private static string EmitGroupedBySection(
        List<(string Name, string Attrs)> parts, List<string> sectionOrder,
        Dictionary<(string, string), string> cells, TrackData tracks,
        Dictionary<string, string> sectionHeaders, Notes notes)
    {
        var sb = new StringBuilder();
        AppendNotes(sb, notes.Loose);
        foreach (var (name, attrs) in parts)
        {
            AppendNotes(sb, notes.Parts.GetValueOrDefault(name));
            sb.Append("part ").Append(name);
            // Settings that hold a line comment take lines of their own, or the comment
            // would swallow the brace.
            sb.Append(attrs.Length == 0 ? " { }\n"
                : attrs.Contains('\n') || attrs.Contains("//") ? " {\n  " + attrs + "\n}\n"
                : " { " + attrs + " }\n");
        }
        sb.Append('\n');
        foreach (var section in sectionOrder)
        {
            AppendNotes(sb, notes.Sections.GetValueOrDefault(section));
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
            if (IsStructural(member))
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
        // What follows the last member — the comments and blank lines at the END of the
        // file — is the end-of-file token's leading trivia, and the walk above visits nodes
        // only. Until 2026-10-11 every comment after the last block (a lesson's TRY and NEXT
        // lines, say) was dropped by Regroup (owner's report).
        if (root.GetChild(root.SlotCount - 1) is SyntaxTokenNode { Kind: SyntaxKind.EndOfFile } eof)
            sb.Append(source, eof.FullSpan.Start, source.Length - eof.FullSpan.Start);
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

    /// <summary>A section-level directive child — delegated to THE one spelling
    /// (<see cref="SectionSymbols.IsSectionDirective"/>), which the LYS1036 validator
    /// reads too.</summary>
    private static bool IsSectionDirective(SyntaxNode n) => SectionSymbols.IsSectionDirective(n);

    /// <summary>A directives-only section header (<c>section A { key g major }</c>) —
    /// delegated to THE one spelling (<see cref="SectionSymbols.IsBareHeader"/>). In
    /// by-part it stands parallel to the parts; in by-section it folds into the
    /// section. This file's own copy was the second of three; the remark on the shared
    /// spelling records what the third one's disagreement cost.</summary>
    private static bool IsSectionHeader(SectionDeclarationSyntax s) => SectionSymbols.IsBareHeader(s);

    /// <summary>The section's directive children joined verbatim (the header text folded
    /// into / split out of a section), empty when it carries none.</summary>
    private static string SectionDirectiveText(string source, SectionDeclarationSyntax s)
        => JoinPieces(DirectChildren(s).Where(IsSectionDirective).Select(d => Verbatim(source, d).Trim()));

    /// <summary>
    /// Settings joined on one line — or, when one holds a comment, a line each: a line comment
    /// joined to the setting after it would swallow it (<c>clef treble // high</c> +
    /// <c>instrument flute</c> on one line drops the instrument, and still parses).
    /// </summary>
    private static string JoinPieces(IEnumerable<string> pieces)
    {
        var kept = pieces.Where(t => t.Length > 0).ToList();
        return kept.Any(t => t.Contains("//") || t.Contains('\n'))
            ? string.Join("\n  ", kept.Select(t => t.Replace("\n", "\n  ")))
            : string.Join(" ", kept);
    }

    /// <summary>Every comment in the tree, as source spans (line comments without their line
    /// break, block comments whole).</summary>
    private static List<(int Start, int End)> CommentSpans(CompilationUnitSyntax root)
    {
        var found = new List<(int, int)>();
        Walk(root.Green, 0);
        return found;

        void Walk(Syntax.InternalSyntax.GreenNode g, int pos)
        {
            if (g.Kind is SyntaxKind.LineCommentTrivia or SyntaxKind.BlockCommentTrivia)
            {
                found.Add((pos, pos + g.FullWidth));
                return;
            }
            if (g.IsTrivia && g.SlotCount == 0)
                return;
            if (g.IsToken)
            {
                if (g.LeadingTrivia is { } lead) Walk(lead, pos);
                if (g.TrailingTrivia is { } trail) Walk(trail, pos + g.FullWidth - trail.FullWidth);
                return;
            }
            for (int i = 0; i < g.SlotCount; i++)
                if (g.GetSlot(i) is { } child)
                {
                    Walk(child, pos);
                    pos += child.FullWidth;
                }
        }
    }

    /// <summary>The leading trivia of the first structural member's keyword — what
    /// Reassemble keeps above the regenerated region (empty when there is none).</summary>
    private static (int Start, int End) FirstStructuralLead(CompilationUnitSyntax root)
        => TopLevel(root).FirstOrDefault(IsStructural)?.GetChild(0) is SyntaxTokenNode kw
            ? (kw.FullSpan.Start, kw.Span.Start)
            : (0, 0);

    /// <summary>A top-level member the regroup rewrites (parts and part-bearing sections,
    /// by-part tracks, standalone section headers).</summary>
    private static bool IsStructural(SyntaxNode member)
        => member is PartDeclarationSyntax
            || (member is ChordPartBlockSyntax cb && cb.HasSections)   // by-part chord track
            || (member is LyricsBlockSyntax lb && lb.HasSections)      // by-part lyric track
            || (member is SectionDeclarationSyntax s
                    && (DirectChildrenOfType<PartBlockSyntax>(s).Any()  // by-section
                        || IsSectionHeader(s)));                        // standalone header

    /// <summary>The source range between a node's own <c>{</c> and its last <c>}</c>, neither
    /// taken from inside a comment — a brace in a comment above or after the block is not its own.</summary>
    private static (int Open, int Close) Interior(string source, SyntaxNode node, List<(int Start, int End)> comments)
    {
        int start = node.Position, end = node.Position + node.FullWidth;
        bool InComment(int i) => comments.Any(c => i >= c.Start && i < c.End);
        int open = -1, close = -1;
        for (int i = start; i < end && open < 0; i++)
            if (source[i] == '{' && !InComment(i)) open = i;
        for (int i = end - 1; i > open && close < 0; i--)
            if (source[i] == '}' && !InComment(i)) close = i;
        return open < 0 || close <= open ? (start, end) : (open + 1, close);
    }

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

    /// <summary>Wraps a music cell in braces. A cell that spans lines or ends in a
    /// <c>//</c> line comment puts the closing brace on its OWN line, so the comment
    /// can't swallow the brace (which would unbalance and corrupt the document). That
    /// line is indented like the cell's opening line — both emitters write every cell
    /// two spaces in — so the brace closes under the <c>section</c> / part name it
    /// belongs to rather than at column 0, where it read as closing the outer block.</summary>
    private static string Braced(string music)
        => music.Contains('\n') || music.Contains("//")
            ? "{ " + music + "\n  }"
            : "{ " + music + " }";
}
