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

using LilySharp.Core.Editing;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Validates that all symbol references (variables, phrases, sections, and the score's
/// staff/ossia/tab part targets) are defined.
/// </summary>
internal sealed class SymbolReferenceValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();
    private readonly HashSet<string> _definedVariables = new();
    private readonly HashSet<string> _definedPhrases = new();
    private readonly HashSet<string> _definedSections = new();
    private readonly HashSet<string> _definedParts = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    /// <summary>
    /// Validates all symbol references in a syntax tree.
    /// </summary>
    public void Validate(SyntaxTree tree)
    {
        _definedVariables.Clear();
        _definedPhrases.Clear();
        _definedSections.Clear();
        _definedParts.Clear();

        var root = tree.GetRoot();
        var nodes = new List<SyntaxNode> { root };
        nodes.AddRange(root.DescendantNodes());

        // First pass: collect all definitions
        foreach (var node in nodes)
            CollectDefinitions(node);

        // Second pass: validate references
        foreach (var node in nodes)
            ValidateReferences(node);

        // A score's staff/ossia/tab render targets must name a defined part — a `part
        // NAME { … }` header OR a section-body part block `NAME { … }`. `score { staff
        // melody2 }` with no such part is an error (it otherwise rendered an empty staff).
        foreach (var reference in PartReferenceFinder.ReferenceTokens(root))
            if (!_definedParts.Contains(reference.Text))
                _diagnostics.Error(reference.Span, DiagnosticCodes.UndefinedPart,
                    $"Undefined part: '{reference.Text}'. Define it with a section body "
                    + $"('{reference.Text} {{ … }}' in a section) or a header "
                    + $"('part {reference.Text} {{ … }}').");

        // The score's TRACK references name their own tracks, not the staff parts above: a
        // `chords NAME` row — or a `staff X with chords NAME` attachment — is declared by a
        // named `chords NAME { … }` block, and the two lyric spellings by a named
        // `lyrics NAME { … }` block. Checked against those sets rather than folded into
        // _definedParts, because folding them would make `staff prog` legal on a chord track
        // — the empty staff this diagnostic exists to catch. Until this was written the
        // track references were checked by NOTHING: a typo in `chords progg`, or in
        // `with chords progg`, passed `lysc check` clean and silently drew no row (measured:
        // the typo's SVG is byte-identical to the same score with the clause deleted).
        var tracks = PartReferenceFinder.Tracks(root);
        foreach (var (token, isChord) in tracks.References)
        {
            string keyword = isChord ? "chords" : "lyrics";
            if ((isChord ? tracks.ChordTracks : tracks.LyricTracks).Contains(token.Text))
                continue;
            _diagnostics.Error(token.Span, DiagnosticCodes.UndefinedPart,
                $"Undefined {keyword} part: '{token.Text}'. Define it with a named block "
                + $"('{keyword} {token.Text} {{ … }}').");
        }
    }

    private void CollectDefinitions(SyntaxNode node)
    {
        switch (node)
        {
            case VariableDeclarationSyntax varDecl:
                _definedVariables.Add(varDecl.Name.Text);
                break;
                
            case PhraseDeclarationSyntax phraseDecl:
                _definedPhrases.Add(phraseDecl.Name.Text);
                // Phrases can also be referenced as variables
                _definedVariables.Add(phraseDecl.Name.Text);
                break;
                
            case SectionDeclarationSyntax sectionDecl:
                _definedSections.Add(sectionDecl.SectionName);
                break;
                
            case PartDeclarationSyntax partDecl:
                _definedParts.Add(partDecl.Name.Text);
                break;

            // A section-body part block `melody { … }` DEFINES the part `melody` (its
            // music), so a staff may render it even with no `part melody { … }` header.
            case PartBlockSyntax partBlock:
                _definedParts.Add(partBlock.PartName.Text);
                break;
        }
    }

    private void ValidateReferences(SyntaxNode node)
    {
        switch (node)
        {
            case VariableReferenceSyntax varRef:
                var varName = varRef.Name.Text;
                if (!_definedVariables.Contains(varName) && !_definedPhrases.Contains(varName))
                {
                    _diagnostics.Error(
                        varRef.Name.Span,
                        DiagnosticCodes.UndefinedVariable,
                        $"Undefined variable or phrase: '{varName}'");
                }
                break;
                
            case SectionReferenceSyntax sectionRef:
                ValidateSectionName(sectionRef.SectionName, sectionRef.Span);
                break;

            // `~Name` is the same reference with its rehearsal label hidden, so the NAME is
            // checked the same way. It was not, and the failure was silent in the worst
            // sense: `form main { ~Nope }` passed `lysc check` clean and simply never
            // played, where `form main { Nope }` reported the typo. A '~' hides a label,
            // not a mistake.
            case { Kind: SyntaxKind.SilentSectionReference } silentRef
                when silentRef.GetChild(1) is SyntaxTokenNode silentName:
                ValidateSectionName(silentName.Text, silentRef.Span);
                break;
        }
    }

    /// <summary>
    /// One house for the check, because a form has TWO spellings of a section reference and
    /// they must answer alike.
    /// </summary>
    /// <param name="span">
    /// The whole reference, matching what the plain spelling underlines — so `~Nope` is
    /// squiggled with its '~', which is the thing the author has to look at.
    /// </param>
    private void ValidateSectionName(string name, TextSpan span)
    {
        if (!_definedSections.Contains(name))
            _diagnostics.Error(span, DiagnosticCodes.UndefinedSection,
                $"Undefined section: '{name}'");
    }
}
