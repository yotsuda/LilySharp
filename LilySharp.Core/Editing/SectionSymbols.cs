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

namespace LilySharp.Core.Editing;

/// <summary>
/// What declares a section name, and what refers to one — asked of a single node, so a
/// caller that is already walking the tree pays nothing extra to ask.
/// </summary>
/// <remarks>
/// <para>
/// Two consumers need the SAME answer and would otherwise spell it twice.
/// <c>SymbolReferenceValidator</c> asks in order to report <c>LYS1005 Undefined section</c>;
/// the language server's semantic tokens ask in order to decide whether a name in a
/// <c>form { }</c> gets a colour. ⚠️ Those two must agree exactly: a name the validator
/// squiggles as undefined must not also be painted as a resolved section, which is the
/// contradiction a second spelling produces the first time one side learns a new
/// spelling and the other does not.
/// </para>
/// <para>
/// ⚠️ A form has TWO spellings of a reference — <c>A</c> and <c>~A</c>, the latter hiding
/// the printed rehearsal label — and that is exactly the pair that has drifted before:
/// <c>form main { ~Nope }</c> passed <c>lysc check</c> clean until the silent spelling was
/// added to the validator. Both live in <see cref="ReferencedName"/> so the next reader
/// finds one place to add a third.
/// </para>
/// <para>
/// ⚠️ These are PREDICATES ON A NODE, deliberately, rather than tree walks returning
/// collections. Both callers run on the keystroke path (diagnostics and semantic tokens
/// are both re-requested on every edit), where an extra <c>DescendantNodes()</c> pass is
/// the cost RULES §7.9 is about. Asked this way, each caller folds the question into the
/// walk it was already making.
/// </para>
/// </remarks>
public static class SectionSymbols
{
    /// <summary>The name token this node DECLARES as a section, or null if it declares
    /// none. Part-major and section-major spell a declaration the same way — a
    /// <c>section NAME { … }</c> inside a <c>part { }</c> is the same node as one at the
    /// top level — so there is nothing to branch on here.</summary>
    public static SyntaxTokenNode? DeclaredName(SyntaxNode node) =>
        node is SectionDeclarationSyntax declaration ? declaration.Name : null;

    /// <summary>
    /// A DIRECTIVES-ONLY section header: `section A { key g major }` - every direct child
    /// is a section directive and there is at least one. It declares the name but gives it
    /// no music; in part-major it states a section's key/meter/tempo once, parallel to the
    /// `part` blocks (GRAMMAR.md's SectionSetting note).
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ONE SPELLING, and it earned that the hard way. Three readers ask this question and
    /// two of them used to answer it themselves. `LilyPondExporter.OrderedMusic` asked a
    /// DIFFERENT one - "does LooseSectionMusic yield anything", a list that counts a directive
    /// AS music - so a header registered as a playable declaration and, the dictionary being
    /// last-declaration-wins, a header written after the part overwrote the part's real cell:
    /// the twin wrote the directive twice and not one note while the page engraved the music
    /// (HANDOFF §2 F ⒯, closed 2026-08-31).
    /// </para>
    /// <para>
    /// ⚠️ An EMPTY section (`section A { }`) is NOT a header by this predicate - there is no
    /// directive to be only. Both callers want that: the converter has nothing to fold, and
    /// LYS1036 raises an ERROR, so the safe failure is silence on a shape nobody listed
    /// rather than refusal of a legal book.
    /// </para>
    /// </remarks>
    public static bool IsBareHeader(SectionDeclarationSyntax section)
    {
        bool sawDirective = false;
        for (int i = 0; i < section.SlotCount; i++)
        {
            if (section.GetChild(i) is not SyntaxNode c || c is SyntaxTokenNode)
                continue;
            if (IsSectionDirective(c)) { sawDirective = true; continue; }
            return false; // a part/chord/lyric block or inline music -> not a bare header
        }
        return sawDirective;
    }

    /// <summary>A section-level directive child - <c>key</c> / <c>time</c> / <c>tempo</c>
    /// / <c>partial</c> / <c>clef</c> / <c>octave</c> - that a section may carry beside
    /// its part blocks (section-major) or alone (a standalone part-major header).</summary>
    public static bool IsSectionDirective(SyntaxNode n)
        => n is KeySignatureSyntax or TimeSignatureSyntax or TempoDeclarationSyntax
            or PartialDeclarationSyntax or ClefDeclarationSyntax or OctaveDirectiveSyntax;

    /// <summary>The name token this node REFERS to, or null if it refers to no section.
    /// The plain spelling (<c>A</c>) and the silent one (<c>~A</c>) both answer here; the
    /// caller that wants to underline the whole reference uses the NODE's span, which
    /// includes the <c>~</c>, while the caller that wants to colour the name uses this
    /// token.</summary>
    public static SyntaxTokenNode? ReferencedName(SyntaxNode node) => node switch
    {
        SectionReferenceSyntax reference => reference.Identifier,
        { Kind: SyntaxKind.SilentSectionReference } => node.GetChild(1) as SyntaxTokenNode,
        _ => null,
    };
}
