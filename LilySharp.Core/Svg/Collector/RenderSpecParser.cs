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

                case LyricsRowRenderSyntax lyricsRow:
                    items.Add(new LyricsRowSpec(lyricsRow.PartName));
                    break;

                // `title` / `composer` inside the score block: this score's own
                // header, applied over the file's when it is collected.
                case MetadataDeclarationSyntax meta:
                    headerOverrides.Add(meta);
                    break;
            }
        }

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
            [.. headerOverrides]);
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
                if (spec == null) continue;

                // Match by Name (e.g., "score")
                if (spec.Name == name)
                    return spec;

                // Match by full output filename (e.g., "fur-elise.svg")
                if (spec.OutputFile == name)
                    return spec;

                // Match by filename without extension (e.g., "fur-elise")
                var filenameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(spec.OutputFile);
                if (filenameWithoutExt == name)
                    return spec;
            }
        }
        return null;
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

        foreach (var staff in grandStaff.Staves)
        {
            var staffSpec = ParseStaff(staff);
            if (staffSpec != null)
                staves.Add(staffSpec);
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

    private static StaffSpec? ParseStaff(StaffRenderSyntax staff)
    {
        // [~][clef?] part ["display"] [with chords chordPart]; braces skipped.
        var toks = RenderTargetTokens(staff);
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

        // Scan every `with X` clause (repeatable, any order): `with chords NAME
        // [as roman|both|names]` and `with lyrics NAME`. Everything before the first
        // `with` is the [clef?] part [display]. `with lyrics` stacks (verses).
        string? withChords = null;
        var chordDisplay = ChordDisplayMode.Names;
        var withLyrics = ImmutableArray.CreateBuilder<string>();
        int firstWith = toks.FindIndex(t => t.Kind == SyntaxKind.WithKeyword);
        if (firstWith >= 1)
        {
            int i = firstWith;
            while (i < toks.Count)
            {
                if (toks[i].Kind != SyntaxKind.WithKeyword || i + 2 >= toks.Count)
                {
                    i++;
                    continue;
                }
                var kind = toks[i + 1].Kind;   // chords | lyrics
                string name = toks[i + 2].Text;
                if (kind == SyntaxKind.ChordsKeyword)
                {
                    withChords = name;
                    if (i + 4 < toks.Count && toks[i + 3].Text == "as")
                    {
                        chordDisplay = ParseChordMode(toks[i + 4].Text);
                        i += 5;
                    }
                    else
                    {
                        i += 3;
                    }
                }
                else if (kind == SyntaxKind.LyricsKeyword)
                {
                    if (name.Length > 0)
                        withLyrics.Add(name);
                    i += 3;
                }
                else
                {
                    i++;
                }
            }
            toks = toks.GetRange(0, firstWith);
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
        // The value arrives typed, so the staff-line count is READ rather than
        // reparsed out of the joined token text (docs/VALUE_SITE_AUDIT.md §2).
        int lines = GetPartPropertyValue(staff, voiceName, "lines")?.AsInt is int ln
            && ln is >= 1 and <= 5 ? ln : 5;
        // Piano pedal style (part property `pedal bracket|text|mixed`; default Bracket).
        var pedalStyle = Staff.ParsePedalStyle(
            GetPartProperty(staff, voiceName, "pedal")?.ToLowerInvariant());
        return new StaffSpec(clef, voiceName, instrumentName,
            RemoveEmpty: removeEmpty is "true" or "all",
            RemoveFirst: removeEmpty is "all",
            Lines: lines,
            WithChords: withChords,
            NameSuppressed: nameSuppressed,
            ChordDisplay: chordDisplay,
            WithLyrics: withLyrics.ToImmutable(),
            PedalStyle: pedalStyle);
    }

    /// <summary>Maps the `as roman | both | names` selector text to its mode.</summary>
    private static ChordDisplayMode ParseChordMode(string? text) => text?.ToLowerInvariant() switch
    {
        "roman" => ChordDisplayMode.Roman,
        "both" => ChordDisplayMode.Both,
        _ => ChordDisplayMode.Names,
    };

    private static TabStaffSpec? ParseTab(TabRenderSyntax tab)
    {
        // [tuning?] part [as numbers|full] [with chords NAME [as roman|both|names]];
        // braces (if any) are skipped.
        var toks = RenderTargetTokens(tab);
        if (toks.Count == 0) return null;

        // `with chords NAME [as roman|both|names]` — a chord attachment, same as the
        // notation-staff form. Split it off FIRST so the tab-style `as` below can't
        // grab the chord-display `as` (both selectors can coexist:
        // `tab m as numbers with chords h as both`).
        string? withChords = null;
        var chordDisplay = ChordDisplayMode.Names;
        int wi = toks.FindIndex(t => t.Kind == SyntaxKind.WithKeyword);
        if (wi >= 0 && toks.Count >= wi + 3)
        {
            withChords = toks[wi + 2].Text; // [with][chords][NAME]
            if (toks.Count >= wi + 5 && toks[wi + 3].Text == "as")
                chordDisplay = ParseChordMode(toks[wi + 4].Text);
            toks = toks.GetRange(0, wi);
        }
        if (toks.Count == 0) return null;

        // Trailing `as numbers | full` — the tab STYLE selector (parallel to the
        // chord `as roman|both|names`). Strip it before reading the part/tuning so
        // the part stays the last token. `numbers` = fret digits only; `full` (or
        // absent) = this renderer's default rhythm-drawing tab.
        bool numbersOnly = false;
        int asIdx = toks.FindIndex(t => string.Equals(t.Text, "as", System.StringComparison.Ordinal));
        if (asIdx >= 0 && asIdx + 1 < toks.Count)
        {
            numbersOnly = string.Equals(toks[asIdx + 1].Text, "numbers", System.StringComparison.OrdinalIgnoreCase);
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
        return new TabStaffSpec(staffSpec, tuning, transposition, numbersOnly, withChords, chordDisplay);
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
        // ossia [clef] partName — slots: [kw, name] or [kw, clef, name]
        // (the LAST slot is always the part name; a clef word alone is a name).
        if (ossia.SlotCount < 2 || ossia.GetChild(ossia.SlotCount - 1) is not SyntaxTokenNode nameToken)
            return null;
        string voiceName = nameToken.Text;

        ClefType? explicitClef = null;
        if (ossia.SlotCount >= 3 && ossia.GetChild(1) is SyntaxTokenNode clefToken)
        {
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
        return new OssiaStaffSpec(new StaffSpec(clef, voiceName));
    }

    private static bool IsInsideGrandStaff(StaffRenderSyntax staff)
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