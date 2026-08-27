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

using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Parses render declarations into RenderSpec.
/// </summary>
public static class RenderSpecParser
{
    /// <summary>
    /// Parses a RenderDeclarationSyntax into a RenderSpec.
    /// </summary>
    public static RenderSpec? Parse(RenderDeclarationSyntax render)
    {
        var items = new List<RenderItemSpec>();
        var headerOverrides = new List<MetadataDeclarationSyntax>();
        FontDeclarationSyntax? fontsRef = null;
        PaperDeclarationSyntax? paperRef = null;

        // Header: `score <FormName> ["basename"] [transpose …]`. The form name says
        // WHICH form to render; the basename names the OUTPUT file.
        string formName = render.FormNameText;
        string? basename = render.BasenameText;

        // Output basename rule: an explicit "basename" wins; else the reserved
        // form name `main` writes to the input .lys stem (empty OutputFile = "derive
        // from the input file"); any other form name becomes the file name.
        string outputFile = !string.IsNullOrEmpty(basename)
            ? System.IO.Path.GetFileNameWithoutExtension(basename)
            : formName == "main" ? "" : formName;

        // Name doubles as the `--score <name>` selector — the form name, or "score"
        // when the header is malformed (no form name).
        var name = string.IsNullOrEmpty(formName) ? "score" : formName;

        // Parse render items
        foreach (var child in render.DescendantNodes())
        {
            switch (child)
            {
                case GrandStaffRenderSyntax grandStaff:
                    var grandSpec = ParseGrandStaff(grandStaff);
                    if (grandSpec != null)
                        items.Add(new GrandStaffRenderSpec(grandSpec));
                    break;

                case CondensedStaffRenderSyntax condensed:
                    items.Add(ParseCondensedStaff(condensed));
                    break;

                case CombinedStaffRenderSyntax combined:
                    items.Add(ParseCombinedStaff(combined));
                    break;

                case StaffRenderSyntax staff when !IsInsideGrandStaff(staff):
                    var staffSpec = ParseStaff(staff);
                    if (staffSpec != null)
                        items.Add(new SingleStaffSpec(staffSpec));
                    break;

                case TabRenderSyntax tab:
                    var tabSpec = ParseTab(tab);
                    if (tabSpec != null)
                        items.Add(tabSpec);
                    break;

                case OssiaRenderSyntax ossia:
                    var ossiaSpec = ParseOssia(ossia);
                    if (ossiaSpec != null)
                        items.Add(ossiaSpec);
                    break;

                case ChordRowRenderSyntax chordRow:
                    items.Add(new ChordRowSpec(chordRow.PartName, ParseChordMode(chordRow.DisplayModeText)));
                    break;

                // A row inside a group body belongs to the group (ParseGrandStaff
                // folds it into the staff above), same guard as the staff case.
                case LyricsRowRenderSyntax lyricsRow when !IsInsideGrandStaff(lyricsRow):
                    items.Add(new LyricsRowSpec(lyricsRow.PartName));
                    break;

                // `title` / `composer` inside the score block: this score's own
                // header, applied over the file's when it is collected.
                case MetadataDeclarationSyntax meta:
                    headerOverrides.Add(meta);
                    break;

                // `fonts NAME [{ … }]` / `paper NAME [{ … }]`: this score's reference
                // to a named top-level block. The LAST wins, like every repeated
                // single-value setting (the validator names the earlier ones).
                case FontDeclarationSyntax fonts:
                    fontsRef = fonts;
                    break;

                case PaperDeclarationSyntax paper:
                    paperRef = paper;
                    break;
            }
        }

        // SCORE = A VERTICAL STACK OF BANDS (user decision, 2026-08-19, closed
        // before the first tag): a row standing NEXT TO the staff it belongs to IS
        // that staff's attachment. The association is written at the definition
        // (`sings`, or the name-is-binding voice rule); the score only orders the
        // bands; adjacency + affinity decide the gluing — a bound `lyrics` row
        // directly BELOW its staff folds under it (LilyPond's Lyrics,
        // staff-affinity UP), and several in a run stack as verses. A row whose
        // binding is NOT the adjacent staff's part keeps its place as an
        // independent band (a part sheet carrying another part's words), and an
        // unbound lyrics row stays the even-spread lead-sheet row. A CHORDS row
        // needs no folding: the row already is the LilyPond-ported adhesion
        // (see FoldAdjacentRows' remarks).
        // LILYPOND-REF: ly/engraver-init.ly:648-658 Lyrics nonstaff-relatedstaff-spacing,
        // staff-affinity UP — a LilyPond score is exactly this vertical list, and
        // the gluing is the affinity, not a clause.
        FoldAdjacentRows(render, items);

        // Ensemble default: with two or more plain staves, each unlabeled
        // staff shows its part name (capitalized) on the first line — writers
        // opt out per staff with `staff ~flute` or rename with
        // `staff flute "…"`. Solo scores, grand staves and tabs stay clean.
        int plainStaffCount = items.Count(it => it is SingleStaffSpec);
        if (plainStaffCount >= 2)
        {
            for (int ii = 0; ii < items.Count; ii++)
            {
                // VoiceName is empty only when the part name failed to parse (a
                // pitch-like token such as `staff a` / `staff b5` yields a zero-width
                // missing token, reported as a syntax error). Skip auto-labeling it
                // rather than indexing [0] into an empty string and crashing — the
                // diagnostics already surface the real problem.
                if (items[ii] is SingleStaffSpec { Staff: { InstrumentName: null, NameSuppressed: false } st } sss
                    && st.VoiceName.Length > 0)
                {
                    string defaultName = char.ToUpperInvariant(st.VoiceName[0]) + st.VoiceName[1..];
                    items[ii] = sss with { Staff = st with { InstrumentName = defaultName } };
                }
            }
        }

        var scoreTranspose = render.Transpose is { } t
            ? LilySharp.Core.Semantics.PartTranspose.ReadProperty(t)
            : null;

        // Bind the score to its form by name (case-sensitive). Null when the name
        // is missing or unresolved — the validator reports it; the score renders nothing.
        var form = ResolveForm(render, formName);

        return new RenderSpec(name, outputFile, [.. items], scoreTranspose, form,
            [.. headerOverrides], fontsRef, paperRef);
    }

    /// <summary>
    /// Folds bound lyrics rows into their staff's attachment, in place — the
    /// reading that makes a score a VERTICAL STACK OF BANDS (see the caller's
    /// remark): a <c>lyrics NAME</c> row immediately BELOW the staff whose part
    /// it sings becomes an attached verse (of a group, the LAST staff — the one
    /// the row stands directly below), and a RUN of such rows stacks as verses
    /// in written order. MEASURED (2026-08-19, scratch/p216/pins): the folded
    /// row renders BYTE-IDENTICAL (modulo data-pos) to the old attachment
    /// clause on all three priority-stack pins, so the fold is the whole port —
    /// the two spellings were already one mechanism.
    /// </summary>
    /// <remarks>
    /// A CHORDS row glues DOWN, and WHICH machinery carries it follows the regime,
    /// because each was ported and measured separately:
    /// <list type="bullet">
    /// <item>A LEADING chords row (no staff-like item above it) stays a ROW — the
    /// loose-chain port: <c>lyrics.chord-row.between-systems.*</c> measured
    /// LilyPond putting the system-opening row INTO the lyric chain
    /// (page-layout-problem.cc:948-990, ported 2026-07-27, residuals are font
    /// terms only), and folding it into the attached-chords engraver MOVES that
    /// geometry away from LilyPond (measured: residual −0.002157 → +0.030400).</item>
    /// <item>An INTERIOR chords row (a staff-like item above it, a staff or tab
    /// below) folds into the staff below as its attached symbols — the engraver
    /// port: the between-staves band placement reads no up-skyline, so an
    /// unfolded interior row sat ON the rest another voice pushed out of the
    /// staff below (ChordRowOnALowerStaff_ClearsARest…, measured 2026-08-19),
    /// while the attached engraver's reservation clears it.</item>
    /// </list>
    /// Deliberately NOT folded, so each keeps its current reading:
    /// <list type="bullet">
    /// <item>A lyrics row after a staff it does not sing — an independent band at
    /// its written place (the part sheet carrying another part's words).</item>
    /// <item>An unbound lyrics row — the even-spread lead-sheet row.</item>
    /// <item>A lyrics row after a tab staff: the attachment plumbing has never
    /// engraved syllables under a tab (the old clause grammar could not spell it),
    /// so the row stays a band rather than exercising an untrodden path.</item>
    /// <item>An interior chords row whose next item already carries chords, or is
    /// a group/condensed/combined item — it keeps its band.</item>
    /// <item>An intervening non-row item (an ossia, a condensed/combined staff)
    /// closes the lyrics fold window — the band between breaks adjacency.</item>
    /// </list>
    /// The binding walks are CWT-cached per tree (<see cref="Music.LyricBindings"/>),
    /// and a score with no rows never reaches them — the per-keystroke spec parse
    /// (IncrementalCompiler) pays two O(items) scans and nothing else.
    /// </remarks>
    private static void FoldAdjacentRows(RenderDeclarationSyntax render, List<RenderItemSpec> items)
    {
        SyntaxNode root = render;
        while (root.Parent != null)
            root = root.Parent;

        // Interior chords rows glue DOWN into the staff below (see remarks).
        bool seenStaffLike = false;
        for (int i = 0; i < items.Count; i++)
        {
            bool staffLike = items[i] is SingleStaffSpec or TabStaffSpec
                or GrandStaffRenderSpec or CondensedStaffSpec or CombinedStaffSpec;
            if (items[i] is ChordRowSpec row && seenStaffLike && i + 1 < items.Count)
            {
                switch (items[i + 1])
                {
                    case SingleStaffSpec { Staff.WithChords: null } s:
                        items[i + 1] = new SingleStaffSpec(s.Staff with
                        { WithChords = row.PartName, ChordDisplay = row.DisplayMode });
                        items.RemoveAt(i);
                        i--;
                        continue;
                    case TabStaffSpec { WithChords: null } tab:
                        items[i + 1] = tab with
                        { WithChords = row.PartName, ChordDisplay = row.DisplayMode };
                        items.RemoveAt(i);
                        i--;
                        continue;
                }
            }
            seenStaffLike |= staffLike;
        }

        // Lyrics glue UP. `open` is the staff-like item still accepting verses;
        // a fold keeps it open (the next row is the next verse), anything that
        // is not a folding row moves or closes it.
        int open = -1;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is LyricsRowSpec row && open >= 0
                && PartOfFoldTarget(items[open]) is { Length: > 0 } part
                && RowBindsToPart(root, row.PartName, part))
            {
                items[open] = AddFoldedVerse(items[open], row.PartName);
                items.RemoveAt(i);
                i--;
                continue;
            }
            open = items[i] is SingleStaffSpec or GrandStaffRenderSpec ? i : -1;
        }
    }

    /// <summary>The part a lyrics row below <paramref name="item"/> would sing:
    /// a single staff's part, or a group's LAST staff's (the one the row stands
    /// directly below). Null = not a fold target.</summary>
    private static string? PartOfFoldTarget(RenderItemSpec item) => item switch
    {
        SingleStaffSpec s => s.Staff.VoiceName,
        GrandStaffRenderSpec { GrandStaff.Staves: { Length: > 0 } staves } => staves[^1].VoiceName,
        _ => null,
    };

    /// <summary>
    /// Whether the named lyrics track belongs to the named part — the SAME rule
    /// the attachment validator applies (LyricSingsValidator): a declared
    /// <c>sings</c> target naming the part or one of its voices binds; with no
    /// <c>sings</c> anywhere, the track's NAME being the part or one of its
    /// voices is the binding (<c>voice sop { } + lyrics sop { }</c>).
    /// </summary>
    internal static bool RowBindsToPart(SyntaxNode root, string track, string part)
    {
        var voices = Music.LyricBindings.VoicesOfPart(root, part);
        return Music.LyricBindings.TargetOf(root, track) is { } sings
            ? string.Equals(sings, part, StringComparison.Ordinal) || voices.Contains(sings)
            : string.Equals(track, part, StringComparison.Ordinal) || voices.Contains(track);
    }

    private static RenderItemSpec AddFoldedVerse(RenderItemSpec item, string track)
    {
        static ImmutableArray<string> Append(ImmutableArray<string> a, string s)
            => (a.IsDefault ? ImmutableArray<string>.Empty : a).Add(s);
        return item switch
        {
            SingleStaffSpec s => new SingleStaffSpec(
                s.Staff with { WithLyrics = Append(s.Staff.WithLyrics, track) }),
            GrandStaffRenderSpec g => new GrandStaffRenderSpec(g.GrandStaff with
            {
                Staves = g.GrandStaff.Staves.SetItem(g.GrandStaff.Staves.Length - 1,
                    g.GrandStaff.Staves[^1] with
                    { WithLyrics = Append(g.GrandStaff.Staves[^1].WithLyrics, track) }),
            }),
            _ => item,
        };
    }

    /// <summary>
    /// Resolves a score's <c>form &lt;Name&gt;</c> reference to the matching top-level
    /// form declaration (case-sensitive). Null when the name is empty or unknown.
    /// </summary>
    private static FormDeclarationSyntax? ResolveForm(RenderDeclarationSyntax render, string formName)
    {
        if (string.IsNullOrEmpty(formName))
            return null;
        SyntaxNode root = render;
        while (root.Parent != null)
            root = root.Parent;
        // Form declarations are top-level only (Parser.ParseTopLevelItem), so the
        // root's direct children are the whole search space — a descendant walk
        // here re-enumerated every music body per lookup (see SyntaxNode.ChildNodes).
        return root.ChildNodes()
            .OfType<FormDeclarationSyntax>()
            .FirstOrDefault(f => string.Equals(f.NameText, formName, System.StringComparison.Ordinal));
    }

    /// <summary>
    /// Finds the first render declaration in a syntax tree.
    /// </summary>
    public static RenderSpec? FindFirst(SyntaxTree tree)
    {
        foreach (var node in tree.GetRoot().ChildNodes())
        {
            if (node is RenderDeclarationSyntax render)
            {
                var spec = Parse(render);
                if (spec != null)
                    return spec;
            }
        }
        return null;
    }

    /// <summary>
    /// Finds all score-type render declarations in a syntax tree.
    /// </summary>
    public static IReadOnlyList<RenderSpec> FindAll(SyntaxTree tree)
    {
        var specs = new List<RenderSpec>();
        foreach (var node in tree.GetRoot().ChildNodes())
        {
            if (node is RenderDeclarationSyntax render)
            {
                var spec = Parse(render);
                if (spec != null)
                    specs.Add(spec);
            }
        }
        return specs;
    }

    /// <summary>The single part the score being played engraves, or null when it names
    /// none or several (two parts means the page draws the same music in two registers,
    /// and a single stream cannot be both).</summary>
    /// <remarks>
    /// The spec is <paramref name="score"/> when the caller named one; otherwise the one
    /// whose form is <paramref name="form"/> — the form being played, so
    /// <c>lysc midi --score movement2</c> attributes by movement 2's staves — else the
    /// first. ⚠️ Not <c>?? default</c> on the parts: a DEFAULT ImmutableArray throws on
    /// Length, and a file with no <c>score</c> block at all is the ordinary case that
    /// reaches here (4 books of the 566 crashed on it before the null return below).
    /// ONE HOME on purpose (2026-08-26): this lived as two private near-copies in
    /// MidiExporter and MusicXmlExporter, already one comment-drift apart.
    /// </remarks>
    public static string? SingleEngravedPart(
        SyntaxTree tree, RenderSpec? score, FormDeclarationSyntax? form)
    {
        var spec = score;
        if (spec == null)
        {
            var played = form ?? Semantics.ScoreForms.Primary(tree.GetRoot());
            RenderSpec? first = null;
            foreach (var s in FindAll(tree))
            {
                first ??= s;
                if (played != null && ReferenceEquals(s.Form, played))
                {
                    spec = s;
                    break;
                }
            }
            spec ??= first;
        }
        if (spec == null) return null;
        var parts = spec.EngravedPartNames;
        return parts.Length == 1 ? parts[0] : null;
    }

    /// <summary>
    /// Finds a render declaration by name, output filename, or filename without extension.
    /// </summary>
    public static RenderSpec? FindByName(SyntaxTree tree, string name)
    {
        foreach (var node in tree.GetRoot().ChildNodes())
        {
            if (node is RenderDeclarationSyntax render)
            {
                var spec = Parse(render);
                if (spec != null && MatchesName(spec, name))
                    return spec;
            }
        }
        return null;
    }

    /// <summary>Whether <paramref name="name"/> selects <paramref name="spec"/> — by its
    /// Name (e.g. "sub"), its full output filename (e.g. "fur-elise.svg"), or that
    /// filename without its extension. The name-match policy has ONE home: both
    /// <see cref="FindByName"/> and <see cref="Choose"/> read it, so the CLI's
    /// <c>--score</c> and the preview's render session cannot drift apart.</summary>
    private static bool MatchesName(RenderSpec spec, string name)
        => spec.Name == name
            || spec.OutputFile == name
            || System.IO.Path.GetFileNameWithoutExtension(spec.OutputFile) == name;

    /// <summary>
    /// The render-selection policy of <see cref="SvgGenerator.Generate(SyntaxTree, Renderer.SvgRenderOptions, string)"/>,
    /// over an already-parsed spec list: no name (null/empty) takes the first spec;
    /// a name takes the first spec it matches (<see cref="MatchesName"/>), falling
    /// back to the first spec when nothing matches — a stale preview selection still
    /// shows the default score rather than nothing. Shared by that full path and by
    /// <see cref="IncrementalCompiler"/> so a named session resolves the SAME spec
    /// the full compile it must byte-match would.
    /// </summary>
    internal static RenderSpec? Choose(IReadOnlyList<RenderSpec> specs, string? renderName)
    {
        if (!string.IsNullOrEmpty(renderName))
        {
            foreach (var spec in specs)
                if (MatchesName(spec, renderName!))
                    return spec;
        }
        return specs.Count > 0 ? specs[0] : null;
    }

    /// <summary>
    /// <c>condensedStaff { partA partB … }</c> → one staff carrying every named part's
    /// voices. The clef is the FIRST part's, since a condensed staff has only one and the
    /// leading part is the one whose register the writer put on top.
    /// </summary>
    /// <remarks>
    /// ⚠️ Unlike <see cref="ParseGrandStaff"/> this NEVER returns null. A group of fewer
    /// than two staves is dropped there, and the score then reports "its body declares no
    /// staff" (LYS6002) about a body that plainly declares one — a message that cannot lead
    /// anyone to the real mistake. The arity here is a diagnostic of its own
    /// (CondensedStaffNeedsTwoParts), and the item survives so the rest of the score still
    /// renders.
    /// </remarks>
    private static CondensedStaffSpec ParseCondensedStaff(CondensedStaffRenderSyntax condensed)
    {
        var names = condensed.PartNames.Where(n => n.Length > 0).ToImmutableArray();
        var clef = (names.Length > 0 ? GetPartClef(condensed, names[0]) : null) ?? ClefType.Treble;
        return new CondensedStaffSpec(clef, names);
    }

    /// <summary>
    /// <c>combinedStaff { partA partB }</c> → one staff whose two parts are merged where
    /// they agree. The clef is the FIRST part's, as for a condensed staff.
    /// </summary>
    /// <remarks>
    /// Never returns null, and for the same reason <see cref="ParseCondensedStaff"/> does
    /// not: an item that disappears makes the score report a missing staff about a body
    /// that declares one. The arity is its own diagnostic (CombinedStaffNeedsTwoParts).
    /// </remarks>
    private static CombinedStaffSpec ParseCombinedStaff(CombinedStaffRenderSyntax combined)
    {
        var names = combined.PartNames.Where(n => n.Length > 0).ToImmutableArray();
        var clef = (names.Length > 0 ? GetPartClef(combined, names[0]) : null) ?? ClefType.Treble;
        return new CombinedStaffSpec(clef, names);
    }

    private static GrandStaffSpec? ParseGrandStaff(GrandStaffRenderSyntax grandStaff)
    {
        var staves = new List<StaffSpec>();

        // Members in written order: `staff` items open staves, and a bound
        // `lyrics NAME` row directly below the staff it sings folds into that
        // staff's verses — the same fold FoldAdjacentRows applies outside the
        // braces, which is how a chorale writes words between the staves.
        // A row that sings no adjacent staff is dropped here and reported by the
        // validator (LYS6012): a group has no independent band to give it.
        foreach (var member in grandStaff.ChildNodes())
        {
            switch (member)
            {
                case StaffRenderSyntax staff:
                    var staffSpec = ParseStaff(staff);
                    if (staffSpec != null)
                        staves.Add(staffSpec);
                    break;
                case LyricsRowRenderSyntax row when staves.Count > 0
                    && RowBindsToPart(grandStaff, row.PartName, staves[^1].VoiceName):
                    staves[^1] = staves[^1] with
                    {
                        WithLyrics = (staves[^1].WithLyrics.IsDefault
                            ? ImmutableArray<string>.Empty
                            : staves[^1].WithLyrics).Add(row.PartName),
                    };
                    break;
            }
        }

        if (staves.Count < 2)
            return null; // a staff group requires at least 2 staves

        var type = grandStaff.GrandStaffKeyword.Kind switch
        {
            SyntaxKind.StaffGroupKeyword => StaffGroupType.StaffGroup,
            SyntaxKind.ChoirStaffKeyword => StaffGroupType.ChoirStaff,
            _ => StaffGroupType.GrandStaff,
        };
        return new GrandStaffSpec([.. staves], type);
    }

    /// <summary>
    /// Cuts a trailing <c>as lines N</c> selector off a render item's target
    /// tokens and returns the written staff-line count, or null when there is
    /// no selector or its value is unreadable (the parser already reported
    /// that; the render falls back to the five-line default). ONE HOME for the
    /// cut: PartReferenceFinder and the LilyPond twin call this same method so
    /// the part token stays the last remaining slot everywhere. Matched by
    /// TEXT — <c>as</c> lexes as the Dutch A-flat pitch, <c>lines</c> as an
    /// ordinary word.
    /// </summary>
    internal static int? CutLinesSelector(List<SyntaxTokenNode> toks)
    {
        for (int i = 0; i + 1 < toks.Count; i++)
        {
            if (!string.Equals(toks[i].Text, "as", System.StringComparison.Ordinal)
                || !string.Equals(toks[i + 1].Text, "lines", System.StringComparison.Ordinal))
                continue;
            int? lines = i + 2 < toks.Count
                && int.TryParse(toks[i + 2].Text, out int n)
                && n >= StaffSpec.MinLines && n <= StaffSpec.MaxLines
                ? n : null;
            toks.RemoveRange(i, toks.Count - i);
            return lines;
        }
        return null;
    }

    /// <summary>The non-keyword, non-brace tokens of a render item, in order:
    /// either [part] or [modifier, part] (modifier = clef or tuning).</summary>
    private static List<SyntaxTokenNode> RenderTargetTokens(SyntaxNode node)
    {
        var toks = new List<SyntaxTokenNode>();
        for (int i = 1; i < node.SlotCount; i++) // skip the leading keyword
            if (node.GetChild(i) is SyntaxTokenNode t
                && t.Kind is not (SyntaxKind.OpenBrace or SyntaxKind.CloseBrace))
                toks.Add(t);
        return toks;
    }

    /// <summary>One home for the staff-item token scan: the sings validator reads
    /// the same (part, with-lyrics) answer the renderer does, never a re-spelling.</summary>
    internal static StaffSpec? ParseStaffSpec(StaffRenderSyntax staff) => ParseStaff(staff);

    private static StaffSpec? ParseStaff(StaffRenderSyntax staff)
    {
        // [~][clef?] part ["display"] [with chords chordPart]; braces skipped.
        var toks = RenderTargetTokens(staff);
        // `as lines N` — the staff-line count is a property of THIS rendering
        // (the part header no longer carries one); cut it before the display
        // name scan below so `as` cannot be read as a bare display name.
        int? selectorLines = CutLinesSelector(toks);
        if (toks.Count == 0) return null;

        // `staff ~flute` = no instrument-name label for this staff.
        bool nameSuppressed = toks.RemoveAll(t => t.Kind == SyntaxKind.Tilde) > 0;

        // `staff flute "津田さん"` = per-score display-name override.
        string? nameOverride = null;
        int si = toks.FindIndex(t => t.Kind == SyntaxKind.StringLiteral);
        if (si >= 0)
        {
            nameOverride = toks[si].Text.Trim('"');
            toks.RemoveAt(si);
        }
        if (toks.Count == 0) return null;

        // [clef?] part [bare display name] — the clef is a distinct keyword
        // kind, so the part is the first non-clef token; anything after it is
        // an unquoted display name (`staff flute 津田さん`).
        ClefType? explicitClef = toks[0].Kind switch
        {
            SyntaxKind.TrebleKeyword => ClefType.Treble,
            SyntaxKind.BassKeyword => ClefType.Bass,
            SyntaxKind.AltoKeyword => ClefType.Alto,
            SyntaxKind.TenorKeyword => ClefType.Tenor,
            SyntaxKind.Treble8Keyword => ClefType.Treble8Below,
            SyntaxKind.Treble8UpKeyword => ClefType.Treble8Above,
            SyntaxKind.SopranoKeyword => ClefType.Soprano,
            SyntaxKind.MezzoSopranoKeyword => ClefType.MezzoSoprano,
            SyntaxKind.BaritoneKeyword => ClefType.Baritone,
            SyntaxKind.Bass8Keyword => ClefType.Bass8Below,
            SyntaxKind.PercussionKeyword => ClefType.Percussion,
            _ => null,
        };
        int partIdx = explicitClef != null ? 1 : 0;
        if (explicitClef != null && partIdx >= toks.Count)
        {
            // The only token is a clef-name word, so it IS the part name, not a clef
            // modifier — `staff bass` references a part literally named "bass" (its clef
            // then comes from the part definition), not a bass-clef staff with no part.
            explicitClef = null;
            partIdx = 0;
        }
        if (partIdx >= toks.Count) return null;
        var partToken = toks[partIdx];
        string voiceName = partToken.Text;
        if (nameOverride == null && partIdx + 1 < toks.Count)
            nameOverride = toks[partIdx + 1].Text;

        // No explicit clef in the render block → take it from the part definition.
        ClefType clef = explicitClef ?? GetPartClef(staff, voiceName) ?? ClefType.Treble;
        // Priority: per-score override ("…") > part inline display name > instrument.
        // The ensemble default (capitalized part name) is applied in Parse()
        // once the staff count is known; ~ suppresses the label entirely.
        string? instrumentName = nameSuppressed
            ? null
            : nameOverride
              ?? GetPartDisplayName(staff, voiceName)
              ?? GetInstrument(staff, voiceName)?.DisplayName;

        // Hara-kiri, as a part property: `removeEmpty true` hides the staff in
        // systems where it only rests but keeps it in the FIRST system
        // (LP \RemoveEmptyStaves); `removeEmpty all` hides the first system too
        // (LP \RemoveAllEmptyStaves). Anything else (or absent) keeps the staff.
        // LILYPOND-REF: ly/context-mods-init.ly — RemoveEmptyStaves /
        // RemoveAllEmptyStaves set VerticalAxisGroup.remove-empty (+ remove-first).
        string? removeEmpty = GetPartProperty(staff, voiceName, "removeempty")?.ToLowerInvariant();
        int lines = selectorLines ?? StaffSpec.MaxLines;
        // Piano pedal style (part property `pedal bracket|text|mixed`; default Bracket).
        var pedalStyle = Staff.ParsePedalStyle(
            GetPartProperty(staff, voiceName, "pedal")?.ToLowerInvariant());
        return new StaffSpec(clef, voiceName, instrumentName,
            RemoveEmpty: removeEmpty is "true" or "all",
            RemoveFirst: removeEmpty is "all",
            Lines: lines,
            NameSuppressed: nameSuppressed,
            // Empty, not default: readers Assert/iterate without an IsDefault
            // guard. The row fold (FoldAdjacentRows / ParseGrandStaff) is the
            // only writer.
            WithLyrics: ImmutableArray<string>.Empty,
            PedalStyle: pedalStyle);
    }

    /// <summary>Maps the `as roman | names` selector text to its mode.</summary>
    /// <remarks>
    /// The <c>_</c> arm is names, and it is NOT the place an unknown word is caught: an
    /// unrecognised selector is an error (LYS2012, <c>ChordDisplayModeValidator</c>), so by
    /// the time a book renders, the only words that reach here are the two above and null.
    /// This arm exists so a book with that error still previews something rather than
    /// throwing — the same "a typo still draws" rule the unresolved-form fallback follows.
    /// ⚠️ It was the only reader of the retired <c>both</c>, and while the validator did not
    /// exist it was also what made the retirement unsafe: `as both` would have kept parsing
    /// and silently become `as names`.
    /// </remarks>
    private static ChordDisplayMode ParseChordMode(string? text) => text?.ToLowerInvariant() switch
    {
        "roman" => ChordDisplayMode.Roman,
        _ => ChordDisplayMode.Names,
    };

    private static TabStaffSpec? ParseTab(TabRenderSyntax tab)
    {
        // [tuning?] part [as numbers|full]; braces (if any) are skipped.
        var toks = RenderTargetTokens(tab);
        if (toks.Count == 0) return null;

        // Trailing `as numbers | full` — the tab STYLE selector (parallel to the
        // chord `as roman|names`). Strip it before reading the part/tuning so
        // the part stays the last token. `numbers` = fret digits only; `full` (or
        // absent) = this renderer's default rhythm-drawing tab.
        // ⚠️ ORDINAL. It was OrdinalIgnoreCase, so `as NUMBERS` engraved a numbers-only tab
        // while every other symbol in the language is case-sensitive — the split `removeEmpty`
        // had until 2026-08-19. TabRenderVocabularyValidator refuses the wrong case now, and
        // a reader that still lowercased it would accept what the compiler had just rejected.
        bool numbersOnly = false;
        int asIdx = toks.FindIndex(t => string.Equals(t.Text, "as", System.StringComparison.Ordinal));
        if (asIdx >= 0 && asIdx + 1 < toks.Count)
        {
            numbersOnly = string.Equals(toks[asIdx + 1].Text, "numbers", System.StringComparison.Ordinal);
            toks = toks.GetRange(0, asIdx);
        }
        if (toks.Count == 0) return null;

        var partToken = toks[^1];
        var tuningToken = toks.Count >= 2 ? toks[0] : null;
        string voiceName = partToken.Text;

        // Explicit tuning override → the part's `tuning` property → the tuning
        // implied by the part's `instrument` preset → else guitar.
        string? tuningName = tuningToken?.Text.ToLowerInvariant()
            ?? GetPartProperty(tab, voiceName, "tuning")?.ToLowerInvariant()
            ?? InstrumentDefaults.GetTuning(GetInstrument(tab, voiceName)?.Preset);
        TuningType tuning = tuningName switch
        {
            "bass" => TuningType.Bass,
            "bass5" => TuningType.Bass5,
            "bass6" => TuningType.Bass6,
            "ukulele" or "uke" => TuningType.Ukulele,
            _ => TuningType.Guitar, // "standard"/"guitar"/unknown/none
        };

        // Carry the part's NOTATION clef: treble_8 marks written-8va
        // (guitar) parts, which the fret calculation shifts down an octave.
        var sourceClef = GetPartClef(tab, voiceName) ?? ClefType.Treble;
        var transposition = ResolvePartTransposition(tab, voiceName, tuning);
        var staffSpec = new StaffSpec(sourceClef, voiceName);
        return new TabStaffSpec(staffSpec, tuning, transposition, numbersOnly);
    }

    /// <summary>
    /// Resolves a part's SOUNDING transposition (semitones, excluding the clef octave):
    /// an explicit <c>transposition</c> property (<c>8vb</c> etc.) &gt; the instrument
    /// preset's default (bass = −12, piccolo = +12) &gt; the tuning's default (bass
    /// tunings = −12). This is the single value the tab fret shift and MIDI both read.
    /// </summary>
    private static int ResolvePartTransposition(SyntaxNode node, string partName, TuningType tuning)
    {
        var text = GetPartProperty(node, partName, "transposition");
        if (text != null && InstrumentDefaults.ParseTranspositionSemitones(text) is int ex)
            return ex;
        var instrument = GetInstrument(node, partName)?.Preset;
        return instrument != null
            ? InstrumentDefaults.GetTransposition(instrument)
            : Tablature.Tunings.TuningTransposition(tuning);
    }

    /// <summary>
    /// Looks up the clef from a part definition by name.
    /// </summary>
    private static ClefType? GetPartClef(SyntaxNode node, string partName)
    {
        // Navigate to root
        var root = node;
        while (root.Parent != null)
            root = root.Parent;

        // Search for part declaration with matching name. Part declarations are
        // top-level only (Parser.ParseTopLevelItem), as for every lookup below.
        foreach (var partDecl in root.ChildNodes().OfType<PartDeclarationSyntax>())
        {
            if (partDecl.Name.Text != partName)
                continue;

            // Check properties for clef
            foreach (var prop in partDecl.Properties)
            {
                if (prop.NameToken.Text.ToLowerInvariant() == "clef")
                {
                    // Value is at index 2 (after name and colon)
                    var valueToken = prop.GetChild(2) as SyntaxTokenNode;
                    if (valueToken == null) continue;

                    var value = valueToken.Text.ToLowerInvariant();
                    return value switch
                    {
                        "bass" => ClefType.Bass,
                        "alto" => ClefType.Alto,
                        "tenor" => ClefType.Tenor,
                        "treble_8" => ClefType.Treble8Below,
                        "treble^8" => ClefType.Treble8Above,
                        "soprano" => ClefType.Soprano,
                        "mezzosoprano" => ClefType.MezzoSoprano,
                        "baritone" => ClefType.Baritone,
                        "bass_8" => ClefType.Bass8Below,
                        "percussion" => ClefType.Percussion,
                        _ => ClefType.Treble
                    };
                }
            }

            // No explicit clef - check for instrument property to infer clef
            foreach (var prop in partDecl.Properties)
            {
                if (prop.NameToken.Text.ToLowerInvariant() == "instrument")
                {
                    // Join ALL value tokens — a hyphenated preset ("electric-bass")
                    // is word+minus+word in the green tree, so child(2) alone is just
                    // "electric" and would fall through to the default treble clef.
                    var texts = new List<string>();
                    for (int vi = 2; vi < prop.SlotCount; vi++)
                        if (prop.GetChild(vi) is SyntaxTokenNode vt)
                            texts.Add(vt.Text);
                    if (texts.Count == 0) continue;

                    var (clef, _) = InstrumentDefaults.GetDefaults(
                        InstrumentDefaults.SplitInstrument(texts).Preset);
                    return clef;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The inline display name of the part named <paramref name="partName"/>
    /// (<c>part melody "Violin I"</c>), or null. This is the part's default printed
    /// label — a score's <c>staff X "…"</c> overrides it per-score.
    /// </summary>
    private static string? GetPartDisplayName(SyntaxNode node, string partName)
    {
        var root = node;
        while (root.Parent != null)
            root = root.Parent;

        foreach (var partDecl in root.ChildNodes().OfType<PartDeclarationSyntax>())
            if (partDecl.Name.Text == partName && partDecl.DisplayName is { } dn)
                return dn;
        return null;
    }

    /// <summary>
    /// Looks up a part property as the word it was written as.
    /// </summary>
    private static string? GetPartProperty(SyntaxNode node, string partName, string propertyName)
        => GetPartPropertyValue(node, partName, propertyName)?.AsText;

    /// <summary>
    /// Looks up a part property as a VALUE. The one lookup; the string form above is a
    /// reading of it, so a numeric property is never recovered by reparsing text
    /// (docs/VALUE_SITE_AUDIT.md §2).
    /// </summary>
    private static LysValue? GetPartPropertyValue(SyntaxNode node, string partName, string propertyName)
    {
        var root = node;
        while (root.Parent != null)
            root = root.Parent;

        foreach (var partDecl in root.ChildNodes().OfType<PartDeclarationSyntax>())
        {
            if (partDecl.Name.Text != partName)
                continue;

            foreach (var prop in partDecl.Properties)
            {
                if (prop.NameToken.Text.ToLowerInvariant() == propertyName)
                {
                    // The join (a hyphenated bare value like "bass-guitar" is
                    // word+minus+word in the green tree) and the quote stripping are
                    // written once, on the node — this method used to hold the only
                    // live copy while the node's own accessor held a different,
                    // unused one. docs/VALUE_SITE_AUDIT.md §7 ①.
                    if (prop.Value is { } value)
                        return value;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Reads a part's <c>instrument</c> property split into its preset (drives
    /// clef/octave/tuning) and display name (a trailing <c>"…"</c> label, else the
    /// preset). Null when the part has no <c>instrument</c> property.
    /// </summary>
    private static (string Preset, string DisplayName)? GetInstrument(SyntaxNode node, string partName)
    {
        var root = node;
        while (root.Parent != null)
            root = root.Parent;

        foreach (var partDecl in root.ChildNodes().OfType<PartDeclarationSyntax>())
        {
            if (partDecl.Name.Text != partName)
                continue;
            foreach (var prop in partDecl.Properties)
            {
                if (prop.NameToken.Text.ToLowerInvariant() != "instrument")
                    continue;
                var texts = new List<string>();
                for (int vi = 2; vi < prop.SlotCount; vi++)
                    if (prop.GetChild(vi) is SyntaxTokenNode vt)
                        texts.Add(vt.Text);
                return texts.Count == 0 ? null : InstrumentDefaults.SplitInstrument(texts);
            }
        }
        return null;
    }

    /// <summary>
    /// Parses an ossia render item: ossia [clef] { partName }
    /// LILYPOND-REF: ly/engraver-init.ly — ossia staves use reduced fontSize
    /// </summary>
    private static OssiaStaffSpec? ParseOssia(OssiaRenderSyntax ossia)
    {
        // ossia [clef] partName [as lines N] — after the selector cut, the LAST
        // token is always the part name; a clef word alone is a name.
        var toks = RenderTargetTokens(ossia);
        int? ossiaLines = CutLinesSelector(toks);
        if (toks.Count == 0)
            return null;
        var nameToken = toks[^1];
        string voiceName = nameToken.Text;

        ClefType? explicitClef = null;
        if (toks.Count >= 2)
        {
            var clefToken = toks[0];
            explicitClef = clefToken.Kind switch
            {
                SyntaxKind.TrebleKeyword => ClefType.Treble,
                SyntaxKind.BassKeyword => ClefType.Bass,
                SyntaxKind.AltoKeyword => ClefType.Alto,
                SyntaxKind.TenorKeyword => ClefType.Tenor,
                SyntaxKind.Treble8Keyword => ClefType.Treble8Below,
                SyntaxKind.Treble8UpKeyword => ClefType.Treble8Above,
                SyntaxKind.SopranoKeyword => ClefType.Soprano,
                SyntaxKind.MezzoSopranoKeyword => ClefType.MezzoSoprano,
                SyntaxKind.BaritoneKeyword => ClefType.Baritone,
                SyntaxKind.Bass8Keyword => ClefType.Bass8Below,
                SyntaxKind.PercussionKeyword => ClefType.Percussion,
                _ => null,
            };
        }

        ClefType clef = explicitClef ?? GetPartClef(ossia, voiceName) ?? ClefType.Treble;
        return new OssiaStaffSpec(new StaffSpec(clef, voiceName,
            Lines: ossiaLines ?? StaffSpec.MaxLines));
    }

    private static bool IsInsideGrandStaff(SyntaxNode staff)
    {
        var parent = staff.Parent;
        while (parent != null)
        {
            if (parent is GrandStaffRenderSyntax)
                return true;
            parent = parent.Parent;
        }
        return false;
    }
}