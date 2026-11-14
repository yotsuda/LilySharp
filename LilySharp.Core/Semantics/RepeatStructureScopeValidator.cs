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

using System.Linq;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Keeps the constructs that change a book's PLAYING ORDER out of its PHRASES: a repeat
/// barline (<c>|:</c> <c>:|</c> <c>:|:</c>), a volta ending (<c>[1. … ]</c>) and a navigation
/// mark (<c>segno</c>, <c>coda</c>, <c>fine</c>, <c>to coda</c>, <c>dc</c>, <c>ds</c> and the
/// <c>al</c> forms) written in a <c>phrase</c> are refused
/// (<see cref="DiagnosticCodes.RepeatStructureInPhrase"/>).
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ THE RULE WAS WIDER UNTIL 2026-11-15 (owner's decision 2026-11-14, session 1243;
/// docs/inline-repeat-design.md): from 2026-08-31 the signs — and from 2026-10-04 the marks —
/// were legal in a <c>form</c> ALONE, because the readers of a sign written in the music
/// disagreed (the page drew a score's repeat, the MIDI unfolded one part). Since the readers
/// share one column of signs (<see cref="RepeatStream"/>), a section's music and a
/// <c>chords</c> row write them too. A phrase stays out: it is a part played wherever it is
/// named, and a sign in it would stand at a different bar line of a different repeat each time.
/// </para>
/// <para>
/// ⚠️ ONE PREDICATE, NOT N PARSER ARMS, and that is the load-bearing choice. The four shapes
/// the tree's books spread this across — inside a phrase, inside a <c>chords</c> row, inside a
/// by-part section, inside a by-section part block — all reach the same two node types,
/// so asking the TREE catches them together. Editing the parser instead means enumerating the
/// arms, and the session that measured this rule's reach miscounted that very quantity three
/// times in one hour (HANDOFF §2 F ⒫).
/// </para>
/// <para>
/// ⚠️ THE ISLAND, COUNTED BY SINK rather than by source text (RULES §5.0). Every
/// <see cref="BarlineSyntax"/> that can stand in a parsed tree comes from four sites —
/// <c>Parser.Music.cs</c>'s music item, <c>Parser.Sections.cs</c>'s chord-row item, and the two
/// form arms (a stray <c>:|</c>, and <c>ParseFormBarline</c>'s "every other barline") — and
/// every <see cref="InlineVoltaSyntax"/> from one, <c>ParseInlineVolta</c>. The form arms are
/// why the ancestor test is needed at all: a form's own <c>|: … :|</c> block keeps its bars as
/// RAW TOKENS inside <c>FormRepeatBlockGreen</c> and makes no barline node, but a <c>:|</c> or
/// <c>:|:</c> standing loose in a form body does make one.
/// </para>
/// <para>
/// ⚠️ TWO SPELLINGS THIS MUST NOT CATCH, and neither needs an exclusion: a LYRIC verse
/// <c>verse 1 { … }</c> is a <c>LyricVerse</c> node (the words for the Nth play — not a repeat), and
/// a lyric row's barline stays a raw token inside <c>LyricMeasureGreen</c>, so it is not a
/// <see cref="BarlineSyntax"/> at all. Both are invisible to a rule written on node types;
/// they were NOT invisible to the text-matching counters that preceded it, which is how two of
/// those three miscounts happened. <c>repeat percent</c> / <c>repeat unfold</c> /
/// <c>tremolo</c> are likewise untouched — they are their own node, and they abbreviate notes
/// rather than reorder them.
/// </para>
/// <para>
/// ⚠️ Only nodes reached from <c>tree.GetRoot()</c> are asked. Two places build these node
/// types by hand with a NULL parent — <c>LilyPondExporter.CreateEnding</c> / <c>CreateBarline</c>
/// (rebuilding a form ending as an inline one so <c>EmitInlineRepeat</c> stays the single place
/// that writes <c>\alternative</c>) and <c>MeasureCollector.Form.CreateBarlineSyntax</c> (the
/// bars a form's repeat composes into the music stream). Those are how a LEGAL form repeat is
/// rendered, so a predicate applied to them would accuse every book that has one; walking from
/// the root is what keeps this rule about what the author WROTE.
/// </para>
/// </remarks>
internal sealed class RepeatStructureScopeValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        foreach (var node in tree.GetRoot().DescendantNodesOfKinds(RepeatStructureKinds))
        {
            switch (node)
            {
                case BarlineSyntax bar
                    when SyntaxFacts.IsRepeatBarlineKind(bar.BarToken.Kind)
                         && bar.IsInside<PhraseDeclarationSyntax>():
                    // At the bar token, not the node: `:|*3` carries its count on the same
                    // node, and the count is not what is wrong with it.
                    _diagnostics.Error(bar.BarToken.Span,
                        DiagnosticCodes.RepeatStructureInPhrase,
                        $"'{bar.BarToken.Text}' is a repeat sign, and a phrase holds none: a "
                        + "phrase is played wherever it is named, and the sign would stand in "
                        + "a different repeat each time. Write a plain '|' here and the sign "
                        + "where the phrase is played, in the section's music - "
                        + "'|: NAME :|' - or in the form.");
                    break;

                case InlineVoltaSyntax volta when volta.IsInside<PhraseDeclarationSyntax>():
                    // At the '[' alone. An ending's body can run for bars, and squiggling all
                    // of it buries the one token to delete (the rule CueRegionValidator
                    // reports a nested cue by).
                    _diagnostics.Error(volta.OpenBracket.Span,
                        DiagnosticCodes.RepeatStructureInPhrase,
                        $"a volta ending ('[{volta.VoltaText} … ]') cannot be written in a "
                        + "phrase: a phrase is played wherever it is named, and the ending "
                        + "would belong to a different repeat each time. Write the ending "
                        + "where the phrase is played, in the section's music - "
                        + $"'[{volta.VoltaText} NAME ]' - or in the form.");
                    break;

                case NavigationMarkSyntax nav when nav.IsInside<PhraseDeclarationSyntax>():
                    _diagnostics.Error(nav.Span,
                        DiagnosticCodes.RepeatStructureInPhrase,
                        $"'{Written(nav)}' is a navigation mark, and a phrase holds none: a "
                        + "phrase is played wherever it is named, and the mark would stand at "
                        + "every one of those places. Write it where the phrase is played, in "
                        + "the section's music at a bar line, or in the form.");
                    break;
            }
        }
    }

    /// <summary>The mark as the writer spelled it (<c>ds al coda</c>), one space between
    /// its words.</summary>
    private static string Written(NavigationMarkSyntax mark)
    {
        var words = new List<string>();
        for (int i = 0; i < mark.SlotCount; i++)
            if (mark.GetChild(i) is SyntaxTokenNode token)
                words.Add(token.Text);
        return string.Join(' ', words);
    }

    /// <summary>The kinds the switch above has a case for, so the walk asks the tree's
    /// descendant index for those nodes instead of offering it every node of the book
    /// (this pass runs after every settled keystroke).</summary>
    /// <remarks>
    /// ⚠️ A SECOND SPELLING OF THE SWITCH, kept beside it on purpose (the shape
    /// <see cref="Editing.PartReferenceFinder.ReferenceKinds"/> has): a third repeat
    /// spelling must be added to BOTH, or it is accepted in a phrase in silence. The
    /// cases carry typed bindings and long messages, so there is no predicate to ask
    /// instead — <c>TailValidatorKindsTests</c> spells the two TYPES itself and compares
    /// the plain walk's answer with the index's, over every net book.
    /// </remarks>
    internal static readonly SyntaxKind[] RepeatStructureKinds =
        [SyntaxKind.Barline, SyntaxKind.InlineVolta, SyntaxKind.NavigationMark];
}
