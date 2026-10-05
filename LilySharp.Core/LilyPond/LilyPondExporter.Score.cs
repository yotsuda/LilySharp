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

using System.Text;
using LilySharp.Core.Music;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.LilyPond;

// The \score block: staves, staff groups, ossia, shared staves, instrument names, tab staves
// and their tuning. Split out of LilyPondExporter.cs as a partial class (2026-10-02); same
// instance state, no behavior change.
public sealed partial class LilyPondExporter
{
    private void EmitScore(RenderDeclarationSyntax? render, List<PartDeclarationSyntax> parts,
        Dictionary<string, string> partVars)
    {
        _sb.Append("\\score {\n");

        // The rows of the system, in source order. An ossia is a row like any other and
        // is MOVED into place by alignAboveContext, exactly as RenderSpec.OrderedItems
        // moves it — see EmitOssia.
        var rows = new List<string>();
        string? lastMainStaffPart = null;   // what an ossia written next would sit above
        var alignedAbove = new HashSet<string>(StringComparer.Ordinal);
        if (render != null)
        {
            foreach (var item in RenderRows(render))
            {
                switch (item)
                {
                    case GrandStaffRenderSyntax group:
                        rows.Add(EmitStaffGroup(group, parts, partVars));
                        // LilyPond aligns above a STAFF, so a group is named by its first
                        // staff — the row Lily# would insert the ossia in front of.
                        lastMainStaffPart = RowPartNames(group).FirstOrDefault(n => n != null)
                            ?? lastMainStaffPart;
                        break;
                    case StaffRenderSyntax st:
                        AddInlineChordRow(rows, RenderPartName(st), "    ");
                        rows.Add(EmitStaff(RenderPartName(st), parts, partVars, tab: false, "    ", writtenClef: StaffClefWord(st)));
                        AddFiguredBassRow(rows, RenderPartName(st), "    ");
                        AddLyricRows(rows, RenderPartName(st), "    ", asRow: false);
                        lastMainStaffPart = RenderPartName(st) ?? lastMainStaffPart;
                        break;
                    case CondensedStaffRenderSyntax or CombinedStaffRenderSyntax:
                        if (EmitSharedStaff(item, rows, parts, partVars, "    ") is { } firstPart)
                            lastMainStaffPart = firstPart;
                        break;
                    case TabRenderSyntax tb:
                        {
                            bool numbersOnly = TabIsNumbersOnly(tb, render);
                            // A numbers-only tab prints no attached chord on the page
                            // (TabStaffStencils.BlanksNoteAttachedChord), so none stands over
                            // its twin either.
                            if (!numbersOnly)
                                AddInlineChordRow(rows, RenderPartName(tb), "    ");
                            rows.Add(EmitStaff(RenderPartName(tb), parts, partVars, tab: true, "    ",
                                tabNumbersOnly: numbersOnly));
                            AddLyricRows(rows, RenderPartName(tb), "    ", asRow: false);
                            lastMainStaffPart = RenderPartName(tb) ?? lastMainStaffPart;
                        }
                        break;
                    case OssiaRenderSyntax os:
                        rows.Add(EmitOssia(os, parts, partVars, lastMainStaffPart));
                        if (lastMainStaffPart != null)
                            alignedAbove.Add(lastMainStaffPart);
                        break;
                    // A chord row is the ChordNames context of its part's \chordmode variable
                    // (EmitChordTracks), standing where the row stands — above the next staff,
                    // which is where Lily# folds an interior row too (RenderSpecParser
                    // .FoldAdjacentRows). A row whose part has no chords block is REPORTED
                    // rather than dropped: a twin silently missing a row is the shape that has
                    // cost this exporter five holes already.
                    case ChordRowRenderSyntax chords:
                        if (_chordVars.TryGetValue(chords.PartName, out var chordVar))
                        {
                            rows.Add("    \\new ChordNames " + ChordNamesWith() + "\\" + chordVar + "\n");
                            // …and its diagrams under the names, when some entry writes a shape.
                            if (_fretVars.TryGetValue(chords.PartName, out var fretVar))
                                rows.Add("    \\new FretBoards " + FretBoardsWith(chords.PartName) + "\\" + fretVar + "\n");
                            // The page can show the row as degrees of the key; LilyPond
                            // prints the names it realizes, so say so once.
                            if (string.Equals(chords.DisplayModeText, "roman", StringComparison.OrdinalIgnoreCase))
                                _warnings.Add($"chord row '{chords.PartName}' is shown as roman degrees on "
                                    + "the page; the twin prints LilyPond's chord names");
                        }
                        else
                        {
                            _warnings.Add($"chord row '{chords.PartName}' is not exported — no chords block of that name");
                        }
                        break;
                    // A lyrics row is its part's Lyrics context(s), standing where the row
                    // stands (EmitLyricTracks); a row the page placed nothing for is reported.
                    case LyricsRowRenderSyntax lyrics:
                        if (_lyricRowVars.ContainsKey(lyrics.PartName))
                            AddLyricRows(rows, lyrics.PartName, "    ", asRow: true);
                        else if (PageKeepsLyricsRow(lyrics.PartName))
                            _warnings.Add($"lyrics row '{lyrics.PartName}' is not exported — the page placed no syllable of it");
                        break;
                }
            }
        }
        // A lead sheet's clock stands first: the silent timing track (EmitLeadSheetTiming).
        if (_leadSheetTimingVar != null)
            rows.Insert(0, "    \\new Devnull \\" + _leadSheetTimingVar + "\n");
        if (rows.Count == 0 && partVars.Count > 0)
        {
            // Fall back to a plain staff for the first part.
            var first = partVars.First();
            rows.Add(EmitStaff(first.Key, parts, partVars, tab: false, "    "));
        }

        // An ossia's alignAboveContext names a context, so the staff it decorates has to
        // carry that id. Only the staves an ossia actually names get one.
        foreach (string partName in alignedAbove)
            for (int i = 0; i < rows.Count; i++)
                rows[i] = NameStaffContext(rows[i], partName, partVars);

        if (rows.Count == 1)
        {
            _sb.Append(rows[0]);
        }
        else
        {
            // ⚠️ Plain simultaneity, NOT \new StaffGroup. Loose `staff a staff b` rows are
            // separate single-staff groups in Lily# (RenderSpec.ToStaffGroups →
            // StaffGroup.CreateSingle each), so a StaffGroup context would add a bracket and
            // span bars the .lys never asked for. A DECLARED group emits its own context.
            _sb.Append("  <<\n");
            foreach (var s in rows) _sb.Append(s);
            _sb.Append("  >>\n");
        }
        // THE INDENT IS WRITTEN, ALWAYS, as LilyPond's own default. Until 2026-09-25 a nameless
        // book's twin wrote 0\mm, because Lily# indented only a score that named an
        // instrument; the page now indents as LilyPond does (owner's decision, session 586 —
        // LayoutEngine.EffectiveIndent), so the twin says LilyPond's default in its own words.
        // A paper block's own indent is not carried — no paper{} reaches the twin (the
        // warning at EmitHeader).
        // ⚠️ `\mm`, NOT A BARE NUMBER. A bare number in \layout is read in MILLIMETRES, so
        // writing the staff-space figure silently produced a DIFFERENT page: measured on the
        // four-name twin, `indent = #8.535826771653543` engraved an effective indent of
        // 4.857400 (= 8.535827 mm ÷ 1.757355 mm per staff space) and the names moved with it,
        // while LilyPond compiled it without a murmur. Writing LilyPond's own spelling of its
        // own default removes the conversion instead of getting it right.
        // LILYPOND-REF: ly/paper-defaults-init.ly — indent = 15\mm.
        // ⚠️ printInitialRepeatBar IS WRITTEN, ALWAYS. Lily# prints a `|:` that opens the piece
        // (owner decision, session 328: the writer spelled it, so it is printed — the
        // lead-sheet convention the corpus follows), where LilyPond's default drops the
        // automatic opener at moment 0 (lily/bar-engraver.cc:432-449
        // Bar_engraver::pre_process_music, "At the start of a piece, we don't print any repeat
        // bars"). The twin says so in LilyPond's own words so the two pages agree; on a piece
        // that does not open with a repeat the setting changes nothing.
        // LILYPOND-REF: Documentation/en/notation/repeats.itely:160-172 printInitialRepeatBar.
        // ⚠️ …EXCEPT when the piece opens with a REWIND (`form { A :| }`): the repeat body
        // EmitRewindRepeat wraps the opening stretch in has no written `|:`, the page draws
        // none, and `##t` would make LilyPond draw one — so that book writes `##f`
        // (LilyPond's own default). Decided per book, from the streams already emitted.
        // …and, after it, the plan's size and style attributes as grob overrides in the
        // same \Score context (FontOverrideLines) — the one reason a fonts directive
        // reaches the twin at all: a `step` IS LilyPond's font-size, so writing it keeps
        // the twin a control for a score that uses one, where the faces (unwritten, see
        // EmitHeader) would only add a difference that exists in the comparison.
        // …and the `layout { barNumbers … }` policy in LilyPond's own words
        // (BarNumberContextLines): `none` removes the engraver, `every N` sets the
        // visibility function — the same context, so the twin's numbers stand where the
        // page's do. `lines` is LilyPond's default and writes nothing.
        // …and the layout's engraving style (`Stem.thickness 1.5`, …) as the same \Score's
        // overrides, `lineThickness` as the \layout variable it is in LilyPond
        // (EngravingStyleContextLines / LineThicknessVariable) — nothing at the defaults.
        string overrides = BarNumberContextLines() + FontOverrideLines() + EngravingStyleContextLines();
        string initialRepeatBar = _rewindOpensThePiece ? "##f" : "##t";
        _sb.Append("  \\layout { indent = 15\\mm").Append(LineThicknessVariable());
        if (overrides.Length == 0)
            _sb.Append(" \\context { \\Score printInitialRepeatBar = ").Append(initialRepeatBar)
               .Append(" } }\n}\n");
        else
            _sb.Append("\n    \\context {\n      \\Score\n      printInitialRepeatBar = ")
               .Append(initialRepeatBar).Append('\n')
               .Append(overrides)
               .Append("    }\n  }\n}\n");
    }

    // ---- Chord tracks (\chordmode) ------------------------------------------

    /// <summary>
    /// A declared staff group — <c>grandStaff</c> / <c>staffGroup</c> / <c>choirStaff</c> —
    /// as the LilyPond context of the same name.
    /// </summary>
    /// <remarks>
    /// The three map one-to-one, and LilyPond derives them from one another the same way
    /// Lily# does: <c>GrandStaff</c> is <c>StaffGroup</c> with a brace instead of a bracket,
    /// <c>ChoirStaff</c> is <c>StaffGroup</c> minus the span bars
    /// (LILYPOND-REF: ly/engraver-init.ly:468-557 Span_bar_engraver — the StaffGroup
    /// context, then GrandStaff and ChoirStaff derived from it).
    /// <para>
    /// ⚠️ <c>GrandStaff</c>, not <c>PianoStaff</c>: PianoStaff adds
    /// <c>Keep_alive_together_engraver</c>, so its staves are "only removed together, never
    /// separately" (ly/engraver-init.ly:535-544 PianoStaff / Keep_alive_together_engraver)
    /// — and Lily#'s grandStaff removes them
    /// separately, so a PianoStaff twin would not be a pair for any book with
    /// <c>removeEmpty</c>.
    /// </para>
    /// </remarks>
    private string EmitStaffGroup(GrandStaffRenderSyntax group,
        List<PartDeclarationSyntax> parts, Dictionary<string, string> partVars,
        string indent = "    ")
    {
        string memberIndent = indent + "  ";
        string context = group.GrandStaffKeyword.Kind switch
        {
            SyntaxKind.StaffGroupKeyword => "StaffGroup",
            SyntaxKind.ChoirStaffKeyword => "ChoirStaff",
            _ => "GrandStaff",
        };
        var sb = new StringBuilder();
        sb.Append(indent).Append("\\new ").Append(context).Append(" <<\n");
        foreach (var member in group.Members)
        {
            // A nested grandStaff is its own context inside this one — LilyPond's own spelling
            // of the piano inside the orchestra's bracket.
            if (member is GrandStaffRenderSyntax inner)
            {
                sb.Append(EmitStaffGroup(inner, parts, partVars, memberIndent));
                continue;
            }
            if (member is CondensedStaffRenderSyntax or CombinedStaffRenderSyntax)
            {
                var sharedRows = new List<string>(1);
                EmitSharedStaff(member, sharedRows, parts, partVars, memberIndent);
                foreach (var r in sharedRows) sb.Append(r);
                continue;
            }
            if (member is not StaffRenderSyntax staff)
            {
                Skip(member);
                continue;
            }
            var groupRows = new List<string>(1);
            AddInlineChordRow(groupRows, RenderPartName(staff), memberIndent);
            foreach (var r in groupRows) sb.Append(r);
            sb.Append(EmitStaff(RenderPartName(staff), parts, partVars, tab: false, memberIndent, writtenClef: StaffClefWord(staff)));
            groupRows.Clear();
            AddFiguredBassRow(groupRows, RenderPartName(staff), memberIndent);
            AddLyricRows(groupRows, RenderPartName(staff), memberIndent, asRow: false);
            foreach (var r in groupRows) sb.Append(r);
        }
        sb.Append(indent).Append(">>\n");
        return sb.ToString();
    }

    /// <summary>
    /// An <c>ossia</c> row: a small staff with no meter and no opening clef, pulled above
    /// the staff it decorates.
    /// </summary>
    /// <remarks>
    /// Every tweak here is one Lily# already spells in its own renderer, so the twin says the
    /// same thing twice rather than inventing a convention:
    /// <list type="bullet">
    /// <item><c>alignAboveContext</c> — RenderSpec.OrderedItems moves an ossia directly above
    ///   the nearest PRECEDING main row, which is the property LilyPond's own ossia recipe
    ///   uses for it (Documentation/en/notation/staff.itely, NR "Ossia staves").</item>
    /// <item><c>\remove Time_signature_engraver</c> — SharedRenderer prints no meter on an
    ///   ossia at all. LILYPOND-REF: ly/engraver-init.ly Time_signature_engraver, the Staff
    ///   context's engraver that the same recipe removes.</item>
    /// <item><c>firstClef = ##f</c> — SharedRenderer's <c>drawClef</c> is false on the ossia's
    ///   FIRST appearance. LILYPOND-REF: lily/clef-engraver.cc Clef_engraver, which creates
    ///   the opening clef only when a previous clef exists or firstClef is true.</item>
    /// <item><c>fontSize = #-3</c> with <c>StaffSymbol.staff-space</c>/<c>thickness</c> at
    ///   <c>magstep -3</c> — EngravingDefaults.OssiaScale IS magstep(-3) = 0.7071 and cites
    ///   this spelling. LILYPOND-REF: scm/lily-library.scm magstep, 2^(s/6).
    ///   ⚠️ NOT <c>\magnifyStaff #2/3</c>, which the NR example uses: 2/3 is a different
    ///   number (0.667) and the twin would be a size apart.</item>
    /// </list>
    /// ⚠️ These are LP-DERIVED even though none is a literal transcription — §7.6 ⒝, so they
    /// carry LILYPOND-REF and not LILYSHARP-OWN. What could not be copied literally is the
    /// SHAPE: Lily# spells the ossia convention as renderer behaviour and LilyPond as context
    /// properties, so the twin has to restate it in the other vocabulary.
    /// </remarks>
    private string EmitOssia(OssiaRenderSyntax ossia, List<PartDeclarationSyntax> parts,
        Dictionary<string, string> partVars, string? alignAbovePart)
    {
        string? partName = OssiaPartName(ossia);
        string varName = partName != null && partVars.TryGetValue(partName, out var v)
            ? v : partVars.Values.FirstOrDefault() ?? "music";

        var sb = new StringBuilder();
        sb.Append("    \\new Staff \\with {\n");
        sb.Append("      \\remove Time_signature_engraver\n");
        if (alignAbovePart != null && partVars.TryGetValue(alignAbovePart, out var above))
            sb.Append("      alignAboveContext = \"").Append(above).Append("\"\n");
        sb.Append("      fontSize = #-3\n");
        sb.Append("      \\override StaffSymbol.staff-space = #(magstep -3)\n");
        sb.Append("      \\override StaffSymbol.thickness = #(magstep -3)\n");
        sb.Append("      firstClef = ##f\n");
        sb.Append("    } { ");
        // The ossia's own clef word (`ossia bass melody`) when it has one, else the part's —
        // which is its `clef` property or the one its `instrument` implies, the same two the
        // page reads (RenderSpecParser.ParseOssia → GetPartClef).
        // ⚠️ An explicit clef is written even though firstClef suppresses the OPENING one:
        // the glyph stays hidden, but the notes still have to be READ in that clef.
        string? clef = OssiaClef(ossia)
                       ?? PartClefWord(parts.FirstOrDefault(p => p.Name.Text == partName));
        if (clef != null) sb.Append("\\clef ").Append(LyClefName(clef)).Append(' ');
        sb.Append('\\').Append(varName).Append(" }\n");
        return sb.ToString();
    }

    /// <summary>The part an ossia row names — the LAST token after the
    /// <c>as lines N</c> cut, the same read as
    /// <see cref="Svg.Collector.RenderSpecParser"/>'s ParseOssia (one home for
    /// the cut; the twin does not carry a line count, so only the slots move).</summary>
    private static string? OssiaPartName(OssiaRenderSyntax ossia)
    {
        var toks = OssiaTargetTokens(ossia);
        return toks.Count > 0 ? toks[^1].Text : null;
    }

    /// <summary>The clef word of <c>staff [clef] part</c>, or null when the row names only the
    /// part. Read through the renderer's own scan (RenderSpecParser.ParseStaffSpec), so the twin
    /// and the page cannot disagree on whether a lone clef word is a clef or the part.</summary>
    private static string? StaffClefWord(StaffRenderSyntax staff) =>
        RenderSpecParser.ParseStaffSpec(staff)?.WrittenClef is { } c ? InstrumentDefaults.ClefWord(c) : null;

    /// <summary>The clef word of <c>ossia [clef] part</c>, or null when the row is just
    /// <c>ossia part</c> (a lone word is the PART, never a clef).</summary>
    private static string? OssiaClef(OssiaRenderSyntax ossia)
    {
        var toks = OssiaTargetTokens(ossia);
        return toks.Count >= 2 ? toks[0].Text : null;
    }

    /// <summary>The ossia row's tokens after the keyword with the trailing
    /// <c>as lines N</c> selector cut off — the shared cut, so the part stays
    /// the last slot here exactly as it does for the renderer.</summary>
    private static List<SyntaxTokenNode> OssiaTargetTokens(OssiaRenderSyntax ossia)
    {
        var toks = new List<SyntaxTokenNode>();
        for (int i = 1; i < ossia.SlotCount; i++)
            if (ossia.GetChild(i) is SyntaxTokenNode t)
                toks.Add(t);
        Svg.Collector.RenderSpecParser.CutStaffSelectors(toks);
        return toks;
    }

    /// <summary>
    /// Gives an already-emitted <c>\new Staff</c> row the context id an ossia aligns above.
    /// </summary>
    private static string NameStaffContext(string row, string partName,
        Dictionary<string, string> partVars)
    {
        if (!partVars.TryGetValue(partName, out var varName))
            return row;
        // The row's own variable reference is what identifies it; `\with` rows (the ossias
        // themselves) never match, because the marker is immediately followed by `{`.
        string marker = "\\new Staff { ";
        int at = row.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0 || !row.Contains("\\" + varName + " }", StringComparison.Ordinal))
            return row;
        return row.Insert(at + "\\new Staff".Length, " = \"" + varName + "\"");
    }

    /// <summary>
    /// True for <c>tab part as numbers</c> — fret digits only.
    /// </summary>
    /// <remarks>
    /// ⚠️ IT NO LONGER MIRRORS <c>RenderSpecParser</c>, IT SHARES WITH IT. This walked the
    /// tokens for the <c>as</c> itself and then compared <c>OrdinalIgnoreCase</c> — the same
    /// two lines, with the same case defect, as the page's copy; its own doc said "the two
    /// must agree, or the twin is drawn in the other mode from the page", which is the
    /// argument for one reading rather than two careful ones (HANDOFF §5.2.1②).
    /// </remarks>
    /// <summary>The page's answer to a tab's style — explicit clause, else numbers beside
    /// a notation staff of the same part and full alone — asked of the one home that
    /// answers it for the page (session 335: the twin used to read the explicit word only,
    /// and exported every paired tab with <c>\tabFullNotation</c>).</summary>
    private static bool TabIsNumbersOnly(TabRenderSyntax tab, RenderDeclarationSyntax render) =>
        Svg.Collector.RenderSpecParser.TabIsNumbersOnly(tab, render);

    /// <summary>Fills <see cref="_instrumentNames"/> from the page's own reading of the
    /// render block.</summary>
    /// <remarks>
    /// ⚠️ A LONE TAB STAFF IS SKIPPED, because the page skips it: DrawInstrumentNames drops
    /// the label for a tab staff in its no-staff-groups branch (a single-staff score) and
    /// keeps it in the per-group branch. Mirroring that here is what keeps the twin the same
    /// picture; emitting it unconditionally would invent a divergence in the tab books, which
    /// are the ones least able to afford one.
    /// </remarks>
    private void CollectInstrumentNames(SyntaxTree tree)
    {
        _instrumentNames.Clear();
        var spec = Score is { } score ? RenderSpecParser.Parse(score) : RenderSpecParser.FindFirst(tree);
        if (spec is null) return;

        int staffItems = spec.Items.Count(
            i => i is SingleStaffSpec or GrandStaffRenderSpec or TabStaffSpec);

        void Take(StaffSpec st)
        {
            if (!string.IsNullOrEmpty(st.InstrumentName))
                _instrumentNames[st.VoiceName] = st.InstrumentName!;
        }

        void TakeGroup(GrandStaffSpec group)
        {
            foreach (var m in group.Members)
            {
                if (m is SingleStaffSpec st) Take(st.Staff);
                else if (m is GrandStaffRenderSpec inner) TakeGroup(inner.GrandStaff);
            }
        }

        foreach (var item in spec.Items)
            switch (item)
            {
                case SingleStaffSpec s: Take(s.Staff); break;
                case GrandStaffRenderSpec g:
                    TakeGroup(g.GrandStaff);
                    break;
                case OssiaStaffSpec o: Take(o.Staff); break;
                case TabStaffSpec t when staffItems > 1: Take(t.Staff); break;
            }
    }

    /// <summary>
    /// A <c>condensedStaff</c> / <c>combinedStaff</c> row: several parts on ONE staff, written
    /// the way the probes that measured the page's port wrote it by hand
    /// (<c>audit/lpreg/pcombine-ctl.ly</c> and <c>pcombine-lp.ly</c>). Returns the first
    /// part's name — the staff an ossia written next would sit above — or null when nothing
    /// was written.
    /// </summary>
    /// <remarks>
    /// Condensed is <c>&lt;&lt; \a \\ \b &gt;&gt;</c>: each part its own voice in written
    /// order, which is Lily#'s own reading (the first part gets voice 1, stems up — see
    /// <c>CondensedStaffRenderSyntax.PartNameTokens</c>). Combined is
    /// <c>\partCombine \a \b</c>, LilyPond's own combiner, whose a2 / Solo / Solo II texts
    /// the page prints too (CombinedStaffTests).
    /// The clef is the FIRST part's, as <c>RenderSpecParser.ParseCombinedStaff</c> reads it;
    /// it stands before the voices in the staff's own braces (LilyPond 2.26.0 draws one staff
    /// with that clef for both spellings — scratch/p382/shared/clef-pc.ly).
    /// No instrument name: the page names single staves only (RenderSpecParser's default
    /// name fills <c>SingleStaffSpec</c> alone).
    /// A combined staff whose arity the validator already refused (CombinedStaffNeedsTwoParts)
    /// is reported rather than written: <c>\partCombine</c> takes exactly two.
    /// ⚠️ A tab or drum part named here is written as ordinary staff music: the page reads
    /// every member as a plain staff (<c>CondensedStaffSpec</c> / <c>CombinedStaffSpec</c>
    /// carry one clef and no tuning).
    /// </remarks>
    private string? EmitSharedStaff(SyntaxNode item, List<string> rows,
        List<PartDeclarationSyntax> parts, Dictionary<string, string> partVars, string indent)
    {
        bool combined = item is CombinedStaffRenderSyntax;
        var names = (item switch
        {
            CombinedStaffRenderSyntax c => SharedStaffPartNames(c.PartNames),
            CondensedStaffRenderSyntax c => SharedStaffPartNames(c.PartNames),
            _ => Enumerable.Empty<string?>(),
        }).OfType<string>().ToList();
        var vars = names.Select(n => partVars.TryGetValue(n, out var v) ? v : null).ToList();
        if (names.Count < 2 || (combined && names.Count != 2) || vars.Any(v => v == null))
        {
            Skip(item);
            return null;
        }

        foreach (var name in names)
            AddInlineChordRow(rows, name, indent);

        var sb = new StringBuilder();
        sb.Append(indent).Append("\\new Staff { ");
        var part = parts.FirstOrDefault(p => p.Name.Text == names[0]);
        if (PartClefWord(part) is { } clef)
            sb.Append("\\clef ").Append(LyClefName(clef)).Append(' ');
        sb.Append(PedalStyleSet(names[0]));
        sb.Append(OttavaStyleSet(names[0]));
        sb.Append(StrokeFingerSet(names[0]));
        if (combined)
            sb.Append("\\partCombine \\").Append(vars[0]).Append(" \\").Append(vars[1]);
        else
            sb.Append("<< ").Append(string.Join(" \\\\ ", vars.Select(v => "\\" + v))).Append(" >>");
        sb.Append(" }\n");
        rows.Add(sb.ToString());

        foreach (var name in names)
            AddLyricRows(rows, name, indent, asRow: false);
        return names[0];
    }

    /// <summary>The <c>\with { instrumentName = … }</c> clause a staff carries, or null.</summary>
    private string? InstrumentNameClause(string? partName) =>
        partName != null && _instrumentNames.TryGetValue(partName, out var n)
            ? "instrumentName = " + QuoteLilyPondString(n)
            : null;

    /// <summary>A Lily# label as a LilyPond string literal.</summary>
    /// <remarks>Only the two characters LilyPond's lexer treats specially inside <c>"…"</c>
    /// need escaping. Non-ASCII goes through as UTF-8, which is what LilyPond reads.</remarks>
    private static string QuoteLilyPondString(string s)
        => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>The <c>\with { midiInstrument = … }</c> clause a staff carries, or null when the
    /// part plays program 1 — LilyPond's own default ("acoustic grand"), so writing it would
    /// change nothing the twin sounds and every twin that names no instrument stays as it was.</summary>
    /// <remarks>
    /// The part's program is <see cref="PartHeaderDefaults.MidiProgram"/> — its
    /// <c>midiInstrument "…"</c>, else its preset's — read through the same table the
    /// <c>.mid</c> reads, and written back as LilyPond's own name for it
    /// (<see cref="Midi.GeneralMidi.InstrumentNames"/>). LILYPOND-REF: scm/midi.scm:21-180
    /// instrument-names-alist — the names <c>midiInstrument</c> accepts.
    /// </remarks>
    private static string? MidiInstrumentClause(PartDeclarationSyntax? part)
    {
        int program = PartHeaderDefaults.Read(part).MidiProgram;
        return program == 0
            ? null
            : "midiInstrument = " + QuoteLilyPondString(Midi.GeneralMidi.InstrumentNames[program]);
    }

    private string EmitStaff(string? partName, List<PartDeclarationSyntax> parts,
        Dictionary<string, string> partVars, bool tab, string indent,
        bool tabNumbersOnly = false, string? writtenClef = null)
    {
        string varName = partName != null && partVars.TryGetValue(partName, out var v)
            ? v : partVars.Values.FirstOrDefault() ?? "music";
        var part = parts.FirstOrDefault(p => p.Name.Text == partName)
                   ?? (parts.Count == 1 ? parts[0] : null);
        string? clef = writtenClef ?? PartClefWord(part);

        var sb = new StringBuilder();
        if (tab)
        {
            string tuning = TabTuning(part);
            sb.Append(indent).Append("\\new TabStaff");
            var tabWith = new List<string>(3);
            if (tuning.Length > 0) tabWith.Add("stringTunings = #" + tuning);
            if (InstrumentNameClause(partName) is { } tabName) tabWith.Add(tabName);
            if (MidiInstrumentClause(part) is { } tabMidi) tabWith.Add(tabMidi);
            if (tabWith.Count > 0)
                sb.Append(" \\with { ").Append(string.Join(" ", tabWith)).Append(" }");
            sb.Append(" { ");
            // ⚠️ A bare LilyPond TabStaff prints fret digits ALONE — it omits Stem, Beam,
            // Flag, Dots, Rest and TupletBracket (ly/engraver-init.ly TabStaff /
            // `\tabFullNotation` in ly/property-init.ly) — and that is Lily#'s `tab part as
            // numbers`, which is also the page's DEFAULT for a tab beside a notation staff
            // of the same part (user decision, 2026-08-29). A lone `tab part` draws the
            // rhythm, so its twin has to ask for it back; tabNumbersOnly is the page's own
            // answer (RenderSpecParser.TabIsNumbersOnly), not a reading of the clause alone.
            // Measured: without this the twin of `tab-beam-script` held TWO Beam grobs (the
            // notation staff's) against the page's four, so every tab book was uncomparable
            // on beams and was written off as a frame problem in the sweep.
            if (!tabNumbersOnly)
                sb.Append("\\tabFullNotation ");
            // ⚠️ LilyPond pitches are SOUNDING; Lily# writes DISPLAY pitches and recovers the
            // sounding octave when it frets (Tunings.SoundingShift, read by
            // TabResolver.ResolveTabStrings). Written verbatim, the twin frets the DISPLAY
            // pitch and lands somewhere else entirely: `tab-percent-repeat` fingered
            // 17 0 17 5 5 17 0 17 against the page's 5 3 5 3 3 5 3 5, because A2 written is
            // A1 sounding. The shift is asked of the same table the page uses, so the two
            // cannot drift.
            // No StrokeFingerSet here: LilyPond's TabVoice removes New_fingering_engraver
            // (ly/engraver-init.ly, TabVoice), so a tab staff prints no right-hand finger
            // whatever the orientation says; the page letters its tab (self-acknowledged).
            AppendTabTranspose(sb, part, partName);
            sb.Append('\\').Append(varName).Append(" }\n");
        }
        else if (partName != null && _drumParts.Contains(partName))
        {
            // A DrumStaff is what reads \drummode: it carries the percussion clef, the
            // drum-kit notehead table and the position table, which is where the part's
            // `clef percussion` and Lily#'s DrumNameRegistry placements both come from
            // (LILYPOND-REF: ly/engraver-init.ly DrumStaff, ly/drumpitch-init.ly drums-style).
            // No \clef is written: the context's own is that clef, and a second one would be
            // this exporter inventing a convention.
            sb.Append(indent).Append("\\new DrumStaff");
            if (InstrumentNameClause(partName) is { } drumName)
                sb.Append(" \\with { ").Append(drumName).Append(" }");
            sb.Append(" { \\").Append(varName).Append(" }\n");
        }
        else
        {
            sb.Append(indent).Append("\\new Staff");
            var staffWith = new List<string>(3);
            if (InstrumentNameClause(partName) is { } staffName) staffWith.Add(staffName);
            if (MidiInstrumentClause(part) is { } staffMidi) staffWith.Add(staffMidi);
            // LilyPond would print a circled digit for every `\N` on this staff; Lily#'s
            // notation staff never draws one (see _stringNumberParts).
            if (partName != null && _stringNumberParts.Contains(partName))
                staffWith.Add("\\omit StringNumber");
            if (staffWith.Count > 0)
                sb.Append(" \\with { ").Append(string.Join(" ", staffWith)).Append(" }");
            sb.Append(" { ");
            if (clef != null) sb.Append("\\clef ").Append(LyClefName(clef)).Append(' ');
            sb.Append(PedalStyleSet(partName));
            sb.Append(OttavaStyleSet(partName));
            sb.Append(StrokeFingerSet(partName));
            // An octave clef does not move a Lily# pitch: `g` under `treble_8` is drawn where
            // `g` stands under treble and SOUNDS an octave down (the 0.8.0 rule; TabResolver
            // frets it there). LilyPond's pitches are sounding and its `treble_8` draws them an
            // octave UP, so the written pitch handed over verbatim stood an octave above the
            // page's on every octave-clef staff (Lab sessions/p646 o8: g d' g' b' at 1.0 … −3.5
            // on the page, −6.0 … on the twin). The staff gets the sounding pitch the tab
            // already did (AppendTabTranspose, the clef half of it).
            if (clef != null
                && Tablature.Tunings.ClefOctaveShift(ClefFromName(clef)) is int clefShift and not 0
                && clefShift % 12 == 0)
                sb.Append("\\transpose c ").Append('c')
                  .Append(new string(clefShift < 0 ? ',' : '\'', Math.Abs(clefShift) / 12)).Append(' ');
            sb.Append('\\').Append(varName).Append(" }\n");
        }
        return sb.ToString();
    }
}
