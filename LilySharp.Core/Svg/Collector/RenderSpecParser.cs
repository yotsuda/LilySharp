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
        string? outputFile = null;
        var items = new List<RenderItemSpec>();

        // Structure: scoreKeyword, [basename string], openBrace, items..., closeBrace
        // The only header token is the optional basename string; its extension (if
        // written) is dropped because the file format is a CLI choice.
        // The name (if any) is the single token right after the keyword — child 1
        // is either the name token or the opening brace.
        if (render.GetChild(1) is SyntaxTokenNode nameTok && nameTok.Kind != SyntaxKind.OpenBrace)
            outputFile = System.IO.Path.GetFileNameWithoutExtension(nameTok.Text.Trim('"'));

        // The basename is optional. When omitted, OutputFile is empty (the
        // consumer derives it from the input file). Name doubles as the
        // selector for `--score <name>`; default to "score" when unnamed.
        var name = string.IsNullOrEmpty(outputFile) ? "score" : outputFile;

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
                    items.Add(new ChordRowSpec(chordRow.PartName));
                    break;

                case LyricsRowRenderSyntax lyricsRow:
                    items.Add(new LyricsRowSpec(lyricsRow.PartName));
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
                if (items[ii] is SingleStaffSpec { Staff: { InstrumentName: null, NameSuppressed: false } st } sss)
                {
                    string defaultName = char.ToUpperInvariant(st.VoiceName[0]) + st.VoiceName[1..];
                    items[ii] = sss with { Staff = st with { InstrumentName = defaultName } };
                }
            }
        }

        var scoreTranspose = render.Transpose is { } t
            ? LilySharp.Core.Semantics.PartTranspose.ReadProperty(t)
            : null;

        // A score-local `structure { ... }` overrides the top-level structure for
        // this score only. The first one wins (a second is flagged by the validator).
        var localStructure = render.DescendantNodes()
            .OfType<StructureDeclarationSyntax>()
            .FirstOrDefault();

        return new RenderSpec(name, outputFile ?? "", [.. items], scoreTranspose, localStructure);
    }

    /// <summary>
    /// Finds the first render declaration in a syntax tree.
    /// </summary>
    public static RenderSpec? FindFirst(SyntaxTree tree)
    {
        foreach (var node in tree.GetRoot().DescendantNodes())
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
        foreach (var node in tree.GetRoot().DescendantNodes())
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
        foreach (var node in tree.GetRoot().DescendantNodes())
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
            return null; // Grand staff requires at least 2 staves

        return new GrandStaffSpec([.. staves]);
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

        string? withChords = null;
        int wi = toks.FindIndex(t => t.Kind == SyntaxKind.WithKeyword);
        if (wi >= 1 && wi + 2 < toks.Count + 1 && toks.Count >= wi + 3)
        {
            withChords = toks[wi + 2].Text; // [with][chords][NAME]
            toks = toks.GetRange(0, wi);    // what precedes = [clef?] part
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
        if (partIdx >= toks.Count) return null;
        var partToken = toks[partIdx];
        string voiceName = partToken.Text;
        if (nameOverride == null && partIdx + 1 < toks.Count)
            nameOverride = toks[partIdx + 1].Text;

        // No explicit clef in the render block → take it from the part definition.
        ClefType clef = explicitClef ?? GetPartClef(staff, voiceName) ?? ClefType.Treble;
        // Priority: per-score override ("…") > part `name` > part `instrument`.
        // The ensemble default (capitalized part name) is applied in Parse()
        // once the staff count is known; ~ suppresses the label entirely.
        string? instrumentName = nameSuppressed
            ? null
            : nameOverride
              ?? GetPartProperty(staff, voiceName, "name")
              ?? GetPartProperty(staff, voiceName, "instrument");

        // Hara-kiri, as a part property: `removeEmpty true` hides the staff in
        // systems where it only rests but keeps it in the FIRST system
        // (LP \RemoveEmptyStaves); `removeEmpty all` hides the first system too
        // (LP \RemoveAllEmptyStaves). Anything else (or absent) keeps the staff.
        // LILYPOND-REF: ly/context-mods-init.ly — RemoveEmptyStaves /
        // RemoveAllEmptyStaves set VerticalAxisGroup.remove-empty (+ remove-first).
        string? removeEmpty = GetPartProperty(staff, voiceName, "removeempty")?.ToLowerInvariant();
        int lines = int.TryParse(GetPartProperty(staff, voiceName, "lines"), out int ln)
            && ln is >= 1 and <= 5 ? ln : 5;
        return new StaffSpec(clef, voiceName, instrumentName,
            RemoveEmpty: removeEmpty is "true" or "all",
            RemoveFirst: removeEmpty is "all",
            Lines: lines,
            WithChords: withChords,
            NameSuppressed: nameSuppressed);
    }

    private static TabStaffSpec? ParseTab(TabRenderSyntax tab)
    {
        // [part] or [tuning, part]; braces (if any) are skipped.
        var toks = RenderTargetTokens(tab);
        if (toks.Count == 0) return null;
        var partToken = toks[^1];
        var tuningToken = toks.Count >= 2 ? toks[0] : null;
        string voiceName = partToken.Text;

        // Explicit tuning override → the part's `tuning` property → the tuning
        // implied by the part's `instrument` preset → else guitar.
        string? tuningName = tuningToken?.Text.ToLowerInvariant()
            ?? GetPartProperty(tab, voiceName, "tuning")?.ToLowerInvariant()
            ?? InstrumentDefaults.GetTuning(GetPartProperty(tab, voiceName, "instrument"));
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
        var staffSpec = new StaffSpec(sourceClef, voiceName);
        return new TabStaffSpec(staffSpec, tuning);
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

        // Search for part declaration with matching name
        foreach (var partDecl in root.DescendantNodes().OfType<PartDeclarationSyntax>())
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
                    var valueToken = prop.GetChild(2) as SyntaxTokenNode;
                    if (valueToken == null) continue;

                    var (clef, _) = InstrumentDefaults.GetDefaults(valueToken.Text);
                    return clef;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Looks up an arbitrary string property from a part definition.
    /// </summary>
    private static string? GetPartProperty(SyntaxNode node, string partName, string propertyName)
    {
        var root = node;
        while (root.Parent != null)
            root = root.Parent;

        foreach (var partDecl in root.DescendantNodes().OfType<PartDeclarationSyntax>())
        {
            if (partDecl.Name.Text != partName)
                continue;

            foreach (var prop in partDecl.Properties)
            {
                if (prop.NameToken.Text.ToLowerInvariant() == propertyName)
                {
                    // Join ALL value tokens — a hyphenated bare value
                    // ("bass-guitar") is word+minus+word in the green tree.
                    var sb = new System.Text.StringBuilder();
                    for (int vi = 2; vi < prop.SlotCount; vi++)
                        if (prop.GetChild(vi) is SyntaxTokenNode vt)
                            sb.Append(vt.Text);
                    if (sb.Length == 0) continue;

                    // Handle string literals (strip quotes) and identifiers
                    var text = sb.ToString();
                    if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
                        text = text[1..^1];
                    return text;
                }
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