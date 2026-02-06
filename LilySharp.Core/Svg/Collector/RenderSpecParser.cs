using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

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
        string? name = null;
        string? outputFile = null;
        var items = new List<RenderItemSpec>();

        // Extract name and output file
        // Structure: renderKeyword, [name], filename, openBrace, items..., closeBrace
        // name can be Identifier or keywords like 'score', 'audio'
        for (int i = 0; i < render.SlotCount; i++)
        {
            var child = render.GetChild(i);
            if (child is SyntaxTokenNode token)
            {
                // Skip render keyword and braces
                if (token.Kind == SyntaxKind.RenderKeyword ||
                    token.Kind == SyntaxKind.OpenBrace ||
                    token.Kind == SyntaxKind.CloseBrace)
                    continue;

                // Name can be Identifier or keywords like 'score'
                if (name == null && IsNameToken(token.Kind))
                {
                    name = token.Text;
                }
                else if (token.Kind == SyntaxKind.StringLiteral)
                {
                    outputFile = token.Text.Trim('"');
                }
            }
        }

        if (name == null || outputFile == null)
            return null;

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
            }
        }

        return new RenderSpec(name, outputFile, [.. items]);
    }

    private static bool IsNameToken(SyntaxKind kind)
    {
        return kind == SyntaxKind.Identifier ||
               kind == SyntaxKind.ScoreKeyword;
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

    private static StaffSpec? ParseStaff(StaffRenderSyntax staff)
    {
        ClefType? explicitClef = null;
        string? voiceName = null;
        bool foundOpenBrace = false;

        for (int i = 0; i < staff.SlotCount; i++)
        {
            var child = staff.GetChild(i);
            if (child is SyntaxTokenNode token)
            {
                // Track when we've passed the open brace
                if (token.Kind == SyntaxKind.OpenBrace)
                {
                    foundOpenBrace = true;
                    continue;
                }

                if (token.Kind == SyntaxKind.CloseBrace)
                    break;

                // Before open brace: clef keywords set the clef
                if (!foundOpenBrace)
                {
                    switch (token.Kind)
                    {
                        case SyntaxKind.TrebleKeyword:
                            explicitClef = ClefType.Treble;
                            break;
                        case SyntaxKind.BassKeyword:
                            explicitClef = ClefType.Bass;
                            break;
                        case SyntaxKind.AltoKeyword:
                            explicitClef = ClefType.Alto;
                            break;
                        case SyntaxKind.TenorKeyword:
                            explicitClef = ClefType.Tenor;
                            break;
                        case SyntaxKind.Treble8Keyword:
                            explicitClef = ClefType.Treble8Below;
                            break;
                    }
                }
                else
                {
                    // After open brace: this is the voice/part name
                    // It can be an Identifier or a keyword like 'bass' used as a part name
                    if (token.Kind != SyntaxKind.StaffKeyword)
                    {
                        voiceName = token.Text;
                    }
                }
            }
        }

        if (voiceName == null)
            return null;

        // If no explicit clef in render block, look up part definition
        ClefType clef = explicitClef ?? GetPartClef(staff, voiceName) ?? ClefType.Treble;

        return new StaffSpec(clef, voiceName);
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